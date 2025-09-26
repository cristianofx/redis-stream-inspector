using System.Globalization;
using RedisInspector.CLI.src.RedisInspector.Core.Services;
using RedisInspector.Core.Models.Helpers;
using StackExchange.Redis;

public class RedisStreamInspector
{
    private readonly CliOptions _opts;
    private readonly SshTunnel? _tunnel;
    private volatile bool _stop;

    public RedisStreamInspector(CliOptions opts, SshTunnel? tunnel) { _opts = opts; _tunnel = tunnel; }
    public void RequestStop() => _stop = true;

    public async System.Threading.Tasks.Task RunAsync()
    {
        var (config, connString) = BuildConfiguration(_opts, _tunnel);
        using var muxer = await ConnectionMultiplexer.ConnectAsync(config);
        var db = muxer.GetDatabase();

        Console.WriteLine($"Connected to {connString}. Press Ctrl+C to stop.");

        var streamKeys = await ResolveStreamsAsync(muxer, db, _opts.Streams);
        if (streamKeys.Count == 0)
        {
            Console.WriteLine("No streams matched. Exiting.");
            return;
        }

        while (!_stop)
        {
            Console.Clear();
            Console.WriteLine($"{DateTime.UtcNow:u}  |  Redis Streams Overview (interval: {_opts.IntervalSec}s)");
            Console.WriteLine("STREAM KEY                                           LEN        PENDING   OLDEST_PENDING   LAG    GROUPS  IDLE_CONS");
            Console.WriteLine(new string('-', 104));

            foreach (var key in streamKeys)
            {
                try
                {
                    var snap = await SnapshotStreamAsync(db, key);
                    Console.WriteLine($"{Trunc(key,45),-45}  {snap.Length,8}  {snap.GroupPending,10}  {FormatAge(snap.OldestPendingAgeMs),14}  {snap.GroupLag,6}  {snap.GroupCount,6}  {snap.IdleConsumers,10}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{Trunc(key,45),-45}  ERROR: {ex.Message}");
                    Console.ResetColor();
                }
            }

            await TaskDelaySafe(TimeSpan.FromSeconds(_opts.IntervalSec));
        }
    }

    private static (ConfigurationOptions cfg, string connString) BuildConfiguration(CliOptions o, SshTunnel? tunnel)
    {
        // Parse Redis URL for credentials and TLS; endpoints may be overridden by SSH local forward.
        var cfg = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            SyncTimeout = 5000,
            KeepAlive = 10,
            ReconnectRetryPolicy = new ExponentialRetry(5000)
        };

        var uriLike = o.RedisUrl.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
                      o.RedisUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);

        bool useTls = false;
        string? redisPassword = null;
        string? redisUser = null;

        if (uriLike)
        {
            var u = new Uri(o.RedisUrl);
            useTls = string.Equals(u.Scheme, "rediss", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                // formats: ":pass" or "user:pass"
                var ui = u.UserInfo;
                if (ui.StartsWith(':')) redisPassword = Uri.UnescapeDataString(ui.Substring(1));
                else
                {
                    var parts = ui.Split(':', 2);
                    if (parts.Length == 2) { redisUser = parts[0]; redisPassword = Uri.UnescapeDataString(parts[1]); }
                    else redisUser = ui;
                }
            }
        }

        if (tunnel != null)
        {
            cfg.EndPoints.Add(tunnel.LocalHost, tunnel.LocalPort);
        }
        else if (uriLike)
        {
            var url = new Uri(o.RedisUrl);
            cfg.EndPoints.Add(string.IsNullOrEmpty(url.Host) ? "127.0.0.1" : url.Host, url.Port > 0 ? url.Port : 6379);
        }
        else
        {
            // host:port or just host
            var parts = o.RedisUrl.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 6379;
            cfg.EndPoints.Add(host, port);
        }

        cfg.Ssl = useTls;
        if (useTls && !string.IsNullOrEmpty(o.SslHost)) cfg.SslHost = o.SslHost; // preserve certificate name across SSH tunnels
        if (!string.IsNullOrEmpty(redisPassword)) cfg.Password = redisPassword;
        if (!string.IsNullOrEmpty(redisUser)) cfg.User = redisUser;

        var ep = string.Join(',', cfg.EndPoints);
        return (cfg, ep + (tunnel != null ? " (via SSH)" : string.Empty));
    }

    private static async System.Threading.Tasks.Task<List<RedisKey>> ResolveStreamsAsync(ConnectionMultiplexer mux, IDatabase db, List<string> inputs)
    {
        var server = GetFirstServer(mux);
        var keys = new HashSet<RedisKey>();
        foreach (var item in inputs)
        {
            if (item.Contains('*') || item.Contains('?') || item.Contains('['))
            {
                await foreach (var k in server.KeysAsync(pattern: item, pageSize: 1000))
                {
                    var type = await db.ExecuteAsync("TYPE", k);
                    if (type.ToString() == "stream") keys.Add(k);
                }
            }
            else
            {
                var type = await db.ExecuteAsync("TYPE", item);
                if (type.ToString() == "stream") keys.Add(item);
            }
        }
        return new List<RedisKey>(keys);
    }

    private static IServer GetFirstServer(ConnectionMultiplexer mux)
    {
        foreach (var ep in mux.GetEndPoints())
        {
            var s = mux.GetServer(ep);
            if (s.IsConnected) return s;
        }
        throw new InvalidOperationException("No connected Redis server endpoint.");
    }

    private static async System.Threading.Tasks.Task<StreamSnapshot> SnapshotStreamAsync(IDatabase db, RedisKey key)
    {
        var xinfo = await db.ExecuteAsync("XINFO", "STREAM", key);
        var streamInfo = ParseMap(xinfo);
        long length = streamInfo.TryGetValue("length", out var len) ? (long)len : 0L;
        string lastId = streamInfo.TryGetValue("last-generated-id", out var lid) ? lid.ToString()! : "-";

        var xgroups = await db.ExecuteAsync("XINFO", "GROUPS", key);
        var groups = ParseArrayOfMaps(xgroups);
        long totalPending = 0;
        long totalLag = 0;
        long groupCount = groups.Count;
        long idleConsumersTotal = 0;
        long oldestPendingAgeMs = 0;

        foreach (var g in groups)
        {
            string gname = g.TryGetValue("name", out var n) ? n.ToString()! : "";
            long pend = g.TryGetValue("pending", out var p) ? Convert.ToInt64(p) : 0;
            totalPending += pend;
            long lag = g.TryGetValue("lag", out var l) ? Convert.ToInt64(l) : 0; // Redis 7 only
            totalLag += lag;

            var xp = await db.ExecuteAsync("XPENDING", key, gname);
            var ps = ParseXPendingSummary(xp);
            if (ps.OldestIdTs.HasValue)
            {
                var ageMs = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ps.OldestIdTs.Value);
                if (ageMs > oldestPendingAgeMs) oldestPendingAgeMs = ageMs;
            }

            try
            {
                var xcons = await db.ExecuteAsync("XINFO", "CONSUMERS", key, gname);
                var consumers = ParseArrayOfMaps(xcons);
                foreach (var c in consumers)
                {
                    long idle = c.TryGetValue("idle", out var id) ? Convert.ToInt64(id) : 0;
                    if (idle >= 5 * 60 * 1000) idleConsumersTotal++;
                }
            }
            catch { }
        }

        return new StreamSnapshot
        {
            Key = key,
            Length = length,
            LastGeneratedId = lastId,
            GroupPending = totalPending,
            GroupLag = totalLag,
            GroupCount = groupCount,
            IdleConsumers = idleConsumersTotal,
            OldestPendingAgeMs = oldestPendingAgeMs
        };
    }

    private static Dictionary<string, object> ParseMap(RedisResult rr)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (rr.Type != ResultType.MultiBulk) return dict;
        var arr = (RedisResult[])rr;
        for (int i = 0; i + 1 < arr.Length; i += 2)
        {
            var key = arr[i].ToString();
            var val = arr[i + 1];
            if (val.Type == ResultType.Integer) dict[key!] = (long)val;
            else dict[key!] = val.ToString()!;
        }
        return dict;
    }

    private static List<Dictionary<string, object>> ParseArrayOfMaps(RedisResult rr)
    {
        var list = new List<Dictionary<string, object>>();
        if (rr.Type != ResultType.MultiBulk) return list;
        foreach (var item in (RedisResult[])rr)
            list.Add(ParseMap(item));
        return list;
    }

    private static XPendingSummary ParseXPendingSummary(RedisResult rr)
    {
        try
        {
            var arr = (RedisResult[])rr;
            long count = (long)arr[0];
            string oldestId = arr[1].ToString()!;
            long? ts = TryParseIdTimestamp(oldestId);
            return new XPendingSummary { Count = count, OldestId = oldestId, OldestIdTs = ts };
        }
        catch { return new XPendingSummary(); }
    }

    private static long? TryParseIdTimestamp(string id)
    {
        var i = id.IndexOf('-');
        if (i <= 0) return null;
        if (long.TryParse(id.AsSpan(0, i), NumberStyles.None, CultureInfo.InvariantCulture, out var ms)) return ms;
        return null;
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    private static string FormatAge(long ms)
    {
        if (ms <= 0) return "-";
        var ts = TimeSpan.FromMilliseconds(ms);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h{ts.Minutes:D2}";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m{ts.Seconds:D2}";
        return $"{ts.Seconds}s";
    }

    private static async System.Threading.Tasks.Task TaskDelaySafe(TimeSpan t)
    {
        try { await System.Threading.Tasks.Task.Delay(t); } catch { }
    }
}

sealed record StreamSnapshot
{
    public string Key { get; init; } = string.Empty;
    public long Length { get; init; }
    public string LastGeneratedId { get; init; } = string.Empty;
    public long GroupPending { get; init; }
    public long GroupLag { get; init; }
    public long GroupCount { get; init; }
    public long IdleConsumers { get; init; }
    public long OldestPendingAgeMs { get; init; }
}

sealed record XPendingSummary
{
    public long Count { get; init; }
    public string OldestId { get; init; } = string.Empty;
    public long? OldestIdTs { get; init; }
}