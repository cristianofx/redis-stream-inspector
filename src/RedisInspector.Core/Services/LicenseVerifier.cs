using RedisInspector.CLI.src.RedisInspector.Core.Models.DTOs;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace RedisInspector.CLI.src.RedisInspector.Core.Services
{
    public static class LicenseVerifier
    {
        // Replace with your real RSA public key (PEM, PKCS#8). Example key here is a placeholder.
        private const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
    MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAnm3f2M1k5cVxkZLQx1sV
    5n4qK5n9+2m7qK0a2d3V5sE5W2r8y0Xo4WvN2d8e0kq8o9lX6q1G1fZ1sZk2Z0Qp
    bUQ4V6qf3T0S3S4C8bX4i2y3q4y5a6b7c8d9e0f1g2h3i4j5k6l7m8n9o0p1q2r3
    4s5t6u7v8w9x0y1z2A3B4C5D6E7F8G9H0I1J2K3L4M5N6O7P8Q9R0S1T2U3V4W5
    X6Y7Z8AAABBBCCCDDDEEEFFF==
    -----END PUBLIC KEY-----";

        public static LicenseInfo LoadAndVerify(string? path, out string message)
        {
            try
            {
                var (autoPath, found) = ResolveLicensePath(path);
                if (!found)
                {
                    message = "License: Free tier (no license file found).";
                    return new LicenseInfo();
                }

                var json = File.ReadAllText(autoPath);
                var lic = JsonSerializer.Deserialize<LicenseInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (lic == null) { message = "License: invalid file."; return new LicenseInfo(); }

                if (lic.ExpUtc < DateTime.UtcNow)
                {
                    message = $"License: expired on {lic.ExpUtc:u}. Reverting to Free.";
                    return new LicenseInfo();
                }

                var canonical = $"{lic.Name}|{lic.Email}|{lic.Tier}|{lic.ExpUtc:u}";
                if (!VerifySignature(PublicKeyPem, canonical, lic.Sig))
                {
                    message = "License: signature invalid. Using Free tier.";
                    return new LicenseInfo();
                }

                message = $"License: {lic.Tier} valid until {lic.ExpUtc:u} for {lic.Name}.";
                return lic;
            }
            catch (Exception ex)
            {
                message = $"License: error ({ex.Message}). Using Free tier.";
                return new LicenseInfo();
            }
        }

        private static (string path, bool found) ResolveLicensePath(string? overridePath)
        {
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
                return (overridePath!, true);

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultPath = Path.Combine(appData, "RedisInspector", "license.json");
            if (!File.Exists(defaultPath))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var alt = Path.Combine(home, ".config", "redis-inspector", "license.json");
                if (File.Exists(alt)) return (alt, true);
                return (defaultPath, false);
            }
            return (defaultPath, true);
        }

        private static bool VerifySignature(string publicKeyPem, string data, string base64Sig)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                var sig = Convert.FromBase64String(base64Sig);
                var bytes = Encoding.UTF8.GetBytes(data);
                return rsa.VerifyData(bytes, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch { return false; }
        }
    }
}