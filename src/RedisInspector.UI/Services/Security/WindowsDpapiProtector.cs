using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RedisInspector.UI.Services.Security
{
    public sealed class WindowsDpapiProtector : ISecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RedisInspector.v1");

        public string? Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return null;
            var data = Encoding.UTF8.GetBytes(plainText);
            var prot = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(prot);
        }

        public string? Unprotect(string? protectedText)
        {
            if (string.IsNullOrEmpty(protectedText)) return null;
            try
            {
                var bytes = Convert.FromBase64String(protectedText);
                var raw = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch { return null; }
        }
    }
}
