namespace RedisInspector.Core.Models.Helpers
{
    public class CliOptions
    {
        public bool MessageOnly { get; set; } = false;  // print only the JSON in --json-field
                                                        // Search paging/range
        public int FindLast { get; set; } = 0; // if > 0, scan only the last N entries per stream (overrides from/to)
                                               // Search
        public string? FindField { get; set; }
        public string? FindEq { get; set; }
        public string FindFromId { get; set; } = "-";
        public string FindToId { get; set; } = "+";
        public int FindMax { get; set; } = 50;
        public int FindPage { get; set; } = 1000;
        public bool OutputJson { get; set; } = false;
        public bool FindCaseInsensitive { get; set; } = false;
        // NEW: JSON-in-a-field support
        public string JsonField { get; set; } = "message"; // the stream field that contains JSON text
        public string? JsonPath { get; set; } = null;      // optional dotted path inside that JSON
                                                           // Redis
        public string RedisUrl { get; set; } = string.Empty; // e.g., redis://:pass@host:6379 or rediss://...
        public List<string> Streams { get; set; } = new();   // keys or glob patterns
        public int IntervalSec { get; set; } = 5;
        public string? SslHost { get; set; } // for TLS SNI/cert verification when tunneling

        // License
        public string LicensePath { get; set; } = string.Empty;

        // SSH tunnel
        public bool UseSsh => !string.IsNullOrEmpty(SshHost) && !string.IsNullOrEmpty(SshUser);
        public string? SshHost { get; set; }
        public int SshPort { get; set; } = 22;
        public string? SshUser { get; set; }
        public string? SshPassword { get; set; }
        public string? SshKeyPath { get; set; }
        public string? SshKeyPassphrase { get; set; }
        public string SshRemoteHost { get; set; } = "127.0.0.1"; // host where Redis is reachable from SSH server
        public int SshRemoteRedisPort { get; set; } = 6379;
        public string LocalBindHost { get; set; } = "127.0.0.1";
        public int LocalBindPort { get; set; } = 0; // 0 = auto

        public bool ShowHelp { get; set; }

        public static void PrintHelp()
        {
            Console.WriteLine("""
        Redis Stream Inspector CLI (v0.3)
        Summary:
        Observability + search over Redis Streams. Supports SSH tunneling and JSON-in-string payloads.

        Usage:
        redis-inspector --redis <redis-url|host[:port]> --streams <keyOrPattern[,more]> [--interval 5]
                        [--ssl-host <hostname>] [--license <path>]

        # SSH tunnel options
                        [--ssh-host <bastion>] [--ssh-port 22] --ssh-user <user>
                        [--ssh-pass <pwd>] [--ssh-key <path>] [--ssh-key-pass <passphrase>]
                        [--ssh-redis-host <host>] [--ssh-remote-port <port>]
                        [--ssh-local-host 127.0.0.1] [--ssh-local-port 0]

        # Search options (one-shot; ignores --interval)
                        [--find-field <name>] [--find-eq <value>]
                        [--find-from-id <id|->] [--find-to-id <id|+>]
                        [--find-last <N>] [--find-max <N>] [--find-page <N>]
                        [--json] [--find-ci] [--json-field <name>] [--json-path <a.b[0].c>]
                        [--message-only]  # print only the JSON payload from --json-field (default: message)

        Notes:
        • When tunneling, the remote host/port default from the Redis URL if not specified.
            Precedence: --ssh-redis-host/--ssh-remote-port > URL host/port > 127.0.0.1:6379.
        • For TLS (rediss://) over SSH, use --ssl-host to set the certificate/SNI name if it differs
            from the URL host.
        • Search mode is one-shot and exits after printing results; --interval is ignored in this mode.
        • Payloads like { "message": "<json>" } are supported: use --json-field (default: message)
            and optionally --json-path if the key is nested (e.g., meta.asset_id).
        • Environment fallback: SSH_HOST and SSH_PASSWORD are read from the process environment
            when the corresponding flags are omitted.
        • Alias: --ssh-redis-port is accepted as a deprecated alias for --ssh-remote-port.

        Examples:
        # Direct (no SSH)
        redis-inspector --redis "redis://:pass@10.0.0.10:6379" --streams "orders*,payments"

        # SSH, password auth; remote Redis bound to 127.0.0.1:6389 on the server
        redis-inspector --redis "redis://:REDIS_PASS@ignored:6389" --streams "orders*" \
            --ssh-host bastion.example --ssh-user ops --ssh-pass "$SSH_PASSWORD" \
            --ssh-redis-host 127.0.0.1 --ssh-remote-port 6389

        # SSH, key auth, TLS Redis with cert for redis.internal
        redis-inspector --redis "rediss://:REDIS_PASS@ignored:6380" \
            --ssl-host redis.internal --streams "events" \
            --ssh-host bastion --ssh-user ops --ssh-key ~/.ssh/id_ed25519 --ssh-remote-port 6380

        # Search by field inside JSON stored in "message": exact match, print JSON lines
        redis-inspector --redis "redis://:pass@10.0.0.10:6379" --streams "mystream" \
            --find-field asset_id --find-eq "4BD039D7B89AD252331E3DF31EC73CA0" \
            --json-field message --json

        # Search nested value (meta.asset_id) only in the last 500 messages per stream
        redis-inspector --redis "redis://:pass@10.0.0.10:6379" \
            --streams "AssetOutputStream,AssetSeenStream" \
            --find-field asset_id --json-field message --json-path meta.asset_id \
            --find-last 500 --find-max 100 --json
        """);
        }

        public static CliOptions Parse(string[] args)
        {
            var opts = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a is "-h" or "--help") { opts.ShowHelp = true; break; }
                if (a == "--redis" && i + 1 < args.Length) { opts.RedisUrl = args[++i]; continue; }
                if (a == "--streams" && i + 1 < args.Length)
                {
                    var raw = args[++i];
                    opts.Streams = new List<string>(raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    continue;
                }
                if (a == "--interval" && i + 1 < args.Length) { if (int.TryParse(args[++i], out var iv) && iv > 0) opts.IntervalSec = iv; continue; }
                if (a == "--ssl-host" && i + 1 < args.Length) { opts.SslHost = args[++i]; continue; }
                if (a == "--license" && i + 1 < args.Length) { opts.LicensePath = args[++i]; continue; }

                // SSH options
                if (a == "--ssh-host" && i + 1 < args.Length) { opts.SshHost = args[++i]; continue; }
                if (a == "--ssh-port" && i + 1 < args.Length) { if (int.TryParse(args[++i], out var p)) opts.SshPort = p; continue; }
                if (a == "--ssh-user" && i + 1 < args.Length) { opts.SshUser = args[++i]; continue; }
                if (a == "--ssh-pass" && i + 1 < args.Length) { opts.SshPassword = args[++i]; continue; }
                if (a == "--ssh-key" && i + 1 < args.Length) { opts.SshKeyPath = args[++i]; continue; }
                if (a == "--ssh-key-pass" && i + 1 < args.Length) { opts.SshKeyPassphrase = args[++i]; continue; }
                if (a == "--ssh-redis-host" && i + 1 < args.Length) { opts.SshRemoteHost = args[++i]; continue; }
                if (a == "--ssh-redis-port" && i + 1 < args.Length) { if (int.TryParse(args[++i], out var rp)) opts.SshRemoteRedisPort = rp; continue; }
                if (a == "--ssh-local-host" && i + 1 < args.Length) { opts.LocalBindHost = args[++i]; continue; }
                if (a == "--ssh-local-port" && i + 1 < args.Length) { if (int.TryParse(args[++i], out var lp)) opts.LocalBindPort = lp; continue; }
                // Search
                if (a == "--find-field" && i + 1 < args.Length) { opts.FindField = args[++i]; continue; }
                if (a == "--find-eq" && i + 1 < args.Length) { opts.FindEq = args[++i]; continue; }
                if (a == "--find-from-id" && i + 1 < args.Length) { opts.FindFromId = args[++i]; continue; }
                if (a == "--find-to-id" && i + 1 < args.Length) { opts.FindToId = args[++i]; continue; }
                if (a == "--find-max" && i + 1 < args.Length) { if (int.TryParse(args[++i], out var fm)) opts.FindMax = Math.Max(1, fm); continue; }
                if (a == "--find-page" && i + 1 < args.Length) { if (int.TryParse(args[++i], out var fp)) opts.FindPage = Math.Max(10, fp); continue; }
                if (a == "--json") { opts.OutputJson = true; continue; }
                if (a == "--find-ci") { opts.FindCaseInsensitive = true; continue; }
                if (a == "--json-field" && i + 1 < args.Length) { opts.JsonField = args[++i]; continue; }
                if (a == "--json-path" && i + 1 < args.Length) { opts.JsonPath = args[++i]; continue; }
                if (a == "--find-last" && i + 1 < args.Length) { if (int.TryParse(args[++i], out var fl)) opts.FindLast = Math.Max(1, fl); continue; }
                if (a == "--message-only") { opts.MessageOnly = true; continue; }
            }

            // after CliOptions.Parse(...)
            opts.SshHost ??= Environment.GetEnvironmentVariable("SSH_HOST");
            opts.SshPassword ??= Environment.GetEnvironmentVariable("SSH_PASSWORD");

            if (string.IsNullOrWhiteSpace(opts.RedisUrl) || opts.Streams.Count == 0)
            {
                opts.ShowHelp = true;
            }
            return opts;
        }
    }
}
