using System;

namespace RedisInspector.UI.Models
{
    public sealed class ConnectionProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string RedisUrl { get; set; } = "redis://localhost:6389";
        public string? SshHost { get; set; }
        public int SshPort { get; set; } = 22;
        public string? SshUser { get; set; }
        public string SshPass { get; set; } = "";
        public string? EncryptedPassword { get; set; }
        public bool HasSecret { get; set; }

        public override string ToString() => Name;
    }
}
