// Redis Stream Inspector – Desktop/CLI (v0.1)
// Adds SSH local port forwarding support to reach internal-only Redis.
// Single-file .NET 8 console app. Focus: read-only observability for Redis Streams.
//
// Features:
//  - Connect to Redis directly OR via SSH tunnel (local port forward)
//  - Discover streams (explicit keys or glob patterns)
//  - Per-stream metrics: length, pending count, oldest pending age, lag (Redis 7), idle consumers
//  - Live refresh at interval
//  - Basic subscription/licensing (offline RSA signature verification)
//
// Build instructions (shell):
//   dotnet new console -n RedisInspector.CLI
//   cd RedisInspector.CLI
//   dotnet add package StackExchange.Redis --version 2.7.33
//   dotnet add package SSH.NET --version 2023.0.0  // Renci.SshNet
//   // Replace Program.cs with this file content.
//   dotnet run -- --redis "redis://:pass@localhost:6379" --streams "orders*,payments" --interval 5
//
// Optional publish (single-file example for Win-x64):
//   dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true /p:PublishTrimmed=true --self-contained true
//   // binaries in bin/Release/net8.0/win-x64/publish
//
// License file: JSON {"name","email","tier","exp_utc","sig"}
//   - sig = Base64 RSA-SHA256 signature over canonical string: "name|email|tier|exp_utc"
//   - Public key PEM is embedded below (placeholder). Keep your private key server-side only.
//   - Default locations:
//       Windows: %APPDATA%/RedisInspector/license.json
//       Linux/macOS: ~/.config/redis-inspector/license.json
//   - Or pass --license <path>

using RedisInspector.CLI.src.RedisInspector.Core.Models.Enums;
using RedisInspector.CLI.src.RedisInspector.Core.Services;
using RedisInspector.Core.Models.Helpers;
using StackExchange.Redis;
using System.Text.Json;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            CliOptions.PrintHelp();
            return 0;
        }

        var license = LicenseVerifier.LoadAndVerify(options.LicensePath, out var licenseMsg);
        Console.WriteLine(licenseMsg);

        if (license.TierEnum == LicenseTier.Free && options.Streams.Count > 2)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Free tier] Limited to 2 streams. Truncating the list.");
            Console.ResetColor();
            options.Streams = options.Streams.GetRange(0, 2);
        }

        // A search is requested if any of these are set (one-shot mode)
        bool isSearchMode =
            !string.IsNullOrWhiteSpace(options.FindField) ||
            options.FindLast > 0;

        SshTunnel? tunnel = null;
        IConnectionMultiplexer? mux = null;
        try
        {
            if (options.UseSsh)
            {
                tunnel = SshTunnel.Open(options);
                Console.WriteLine($"SSH tunnel established: {tunnel.LocalHost}:{tunnel.LocalPort} → {options.SshRemoteHost}:{options.SshRemoteRedisPort} via {options.SshHost}:{options.SshPort}");

                // Build Redis configuration (honors URI, TLS, SNI, SSH tunnel)
                var cfg = BuildConfiguration(options, tunnel);
                mux = await ConnectionMultiplexer.ConnectAsync(cfg);
                var db = mux.GetDatabase();

                if (isSearchMode)
                {
                    Console.WriteLine($"Trying to fetch messages from Streams");
                    Console.WriteLine($"");
                    // Map CLI flags -> Core SearchOptions
                    var so = ToSearchOptions(options);

                    var runner = new SearchRunner(mux, db, so);

                    int emitted = 0;
                    await foreach (var hit in runner.RunAsync())
                    {
                        if (so.MessageOnly)
                        {
                            if (hit.RawMessage is not null)
                                Console.WriteLine(hit.RawMessage);
                        }
                        else if (options.OutputJson)
                        {
                            // NDJSON of hits
                            Console.WriteLine(JsonSerializer.Serialize(hit));
                        }
                        else
                        {
                            Console.WriteLine($"[{hit.Stream}] {hit.Id}");
                            foreach (var kv in hit.Fields)
                                Console.WriteLine($"  {kv.Key} = {kv.Value}");
                            if (!string.IsNullOrWhiteSpace(hit.RawMessage))
                                Console.WriteLine($"  (message) {hit.RawMessage}");
                            Console.WriteLine();
                        }

                        emitted++;
                        if (emitted >= so.FindMax) break;
                    }

                    if (!so.MessageOnly && emitted == 0 && !options.OutputJson)
                        Console.WriteLine("No matches found.");

                    return 0;
                }
                else
                {
                    // Interval polling monitor (your existing implementation in Core)
                    var inspector = new RedisStreamInspector(options, tunnel);
                    Console.CancelKeyPress += (_, e) => { e.Cancel = true; inspector.RequestStop(); };
                    await inspector.RunAsync();
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fatal: {ex.Message}{ex}");
            Console.ResetColor();
            return 2;
        }
        finally
        {
            tunnel?.Dispose();
        }
        return 0;
    }

        // --- helpers ---

    private static ConfigurationOptions BuildConfiguration(CliOptions o, SshTunnel? tunnel)
    {
        var cfg = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            SyncTimeout = 5000,
            KeepAlive = 10,
            ReconnectRetryPolicy = new ExponentialRetry(5000)
        };

        bool uriLike = o.RedisUrl.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
                       o.RedisUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);

        bool useTls = false;
        string? redisPassword = null;
        string? redisUser = null;
        string? urlHost = null;
        int urlPort = -1;

        if (uriLike)
        {
            var u = new Uri(o.RedisUrl);
            urlHost = string.IsNullOrEmpty(u.Host) ? null : u.Host;
            urlPort = u.Port > 0 ? u.Port : -1;
            useTls = string.Equals(u.Scheme, "rediss", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(u.UserInfo))
            {
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
        else
        {
            var parts = o.RedisUrl.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            urlHost = parts.Length > 0 ? parts[0] : null;
            urlPort = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : -1;
        }

        if (tunnel is not null)
            cfg.EndPoints.Add(tunnel.LocalHost, tunnel.LocalPort);
        else
            cfg.EndPoints.Add(string.IsNullOrEmpty(urlHost) ? "127.0.0.1" : urlHost, urlPort > 0 ? urlPort : 6379);

        cfg.Ssl = useTls;
        if (useTls)
        {
            if (!string.IsNullOrEmpty(o.SslHost)) cfg.SslHost = o.SslHost;
            else if (!string.IsNullOrEmpty(urlHost) && !IsLocal(urlHost)) cfg.SslHost = urlHost;
        }
        if (!string.IsNullOrEmpty(redisPassword)) cfg.Password = redisPassword;
        if (!string.IsNullOrEmpty(redisUser)) cfg.User = redisUser;

        return cfg;
    }

    private static bool IsLocal(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host == "127.0.0.1" || host == "::1";

    private static SearchOptions ToSearchOptions(CliOptions o)
    {
        return new SearchOptions
        {
            Streams = new List<string>(o.Streams),
            FindField = o.FindField,
            FindEq = o.FindEq,
            FindFromId = string.IsNullOrWhiteSpace(o.FindFromId) ? "-" : o.FindFromId!,
            FindToId = string.IsNullOrWhiteSpace(o.FindToId) ? "+" : o.FindToId!,
            FindLast = o.FindLast,
            FindMax = o.FindMax <= 0 ? int.MaxValue : o.FindMax,
            FindPage = o.FindPage <= 0 ? 100 : o.FindPage,
            FindCaseInsensitive = o.FindCaseInsensitive,
            JsonField = string.IsNullOrWhiteSpace(o.JsonField) ? "message" : o.JsonField!,
            JsonPath = o.JsonPath,
            MessageOnly = o.MessageOnly
        };
    }

}
