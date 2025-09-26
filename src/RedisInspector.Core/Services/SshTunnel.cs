using System.Net;
using System.Net.Sockets;
using RedisInspector.Core.Models.Helpers;
using Renci.SshNet;


namespace RedisInspector.CLI.src.RedisInspector.Core.Services
{

    public sealed class SshTunnel : IDisposable
    {
        private readonly SshClient _client;
        private readonly ForwardedPortLocal _port;
        public string LocalHost { get; }
        public int LocalPort { get; }

        private SshTunnel(SshClient client, ForwardedPortLocal port, string localHost, int localPort)
        {
            _client = client; _port = port; LocalHost = localHost; LocalPort = localPort;
        }

        public static SshTunnel Open(CliOptions o)
        {
            if (string.IsNullOrWhiteSpace(o.SshHost) || string.IsNullOrWhiteSpace(o.SshUser))
                throw new InvalidOperationException("SSH host and user are required for tunneling.");

            var methods = new List<AuthenticationMethod>();
            if (!string.IsNullOrEmpty(o.SshPassword))
                methods.Add(new PasswordAuthenticationMethod(o.SshUser, o.SshPassword));
            if (!string.IsNullOrEmpty(o.SshKeyPath))
            {
                using var keyStream = File.OpenRead(Environment.ExpandEnvironmentVariables(o.SshKeyPath));
                var pkf = string.IsNullOrEmpty(o.SshKeyPassphrase) ? new PrivateKeyFile(keyStream) : new PrivateKeyFile(keyStream, o.SshKeyPassphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(o.SshUser, pkf));
            }
            if (methods.Count == 0)
                throw new InvalidOperationException("Provide --ssh-pass or --ssh-key for authentication.");

            var ci = new ConnectionInfo(o.SshHost!, o.SshPort, o.SshUser!, methods.ToArray())
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            var client = new SshClient(ci);
            client.Connect();

            var localHost = string.IsNullOrWhiteSpace(o.LocalBindHost) ? "127.0.0.1" : o.LocalBindHost;
            var localPort = o.LocalBindPort > 0 ? o.LocalBindPort : GetFreeTcpPort(localHost);

            var fwd = new ForwardedPortLocal(localHost, (uint)localPort, o.SshRemoteHost, (uint)o.SshRemoteRedisPort);
            client.AddForwardedPort(fwd);
            fwd.Start();

            return new SshTunnel(client, fwd, localHost, localPort);
        }

        public static SshTunnel Open(
            string sshHost, int sshPort, string sshUser,
            string? sshPassword,
            string? sshKeyPath, string? sshKeyPassphrase,
            string sshRemoteHost, int sshRemoteRedisPort,
            string? localBindHost = "127.0.0.1", int? localBindPort = null)
        {
            if (string.IsNullOrWhiteSpace(sshHost) || string.IsNullOrWhiteSpace(sshUser))
                throw new InvalidOperationException("SSH host and user are required for tunneling.");

            var methods = new List<AuthenticationMethod>();
            if (!string.IsNullOrEmpty(sshPassword))
                methods.Add(new PasswordAuthenticationMethod(sshUser, sshPassword));

            if (!string.IsNullOrEmpty(sshKeyPath))
            {
                using var keyStream = File.OpenRead(Environment.ExpandEnvironmentVariables(sshKeyPath));
                var pkf = string.IsNullOrEmpty(sshKeyPassphrase)
                    ? new PrivateKeyFile(keyStream)
                    : new PrivateKeyFile(keyStream, sshKeyPassphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(sshUser, pkf));
            }

            if (methods.Count == 0)
                throw new InvalidOperationException("Provide SSH password or key for authentication.");

            var ci = new ConnectionInfo(sshHost, sshPort, sshUser, methods.ToArray())
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            var client = new SshClient(ci);
            client.Connect();

            var localHost = string.IsNullOrWhiteSpace(localBindHost) ? "127.0.0.1" : localBindHost!;
            var localPort = localBindPort is > 0 ? localBindPort.Value : GetFreeTcpPort(localHost);

            var fwd = new ForwardedPortLocal(localHost, (uint)localPort, sshRemoteHost, (uint)sshRemoteRedisPort);
            client.AddForwardedPort(fwd);
            fwd.Start();

            return new SshTunnel(client, fwd, localHost, localPort);
        }

        private static int GetFreeTcpPort(string host)
        {
            var ip = IPAddress.Parse(host);
            var l = new TcpListener(ip, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            try { _port.Stop(); } catch { }
            try { if (_client.IsConnected) _client.Disconnect(); } catch { }
            _client.Dispose();
        }
    }
}