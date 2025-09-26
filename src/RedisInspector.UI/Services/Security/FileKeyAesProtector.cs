using System.IO;
using System;
using System.Security.Cryptography;
using System.Text;

namespace RedisInspector.UI.Services.Security;

/// <summary>
/// Cross-platform secret protector: generates a per-user AES-GCM key under the app config dir.
/// The key file is created with user-only permissions when possible.
/// </summary>
public sealed class FileKeyAesProtector : ISecretProtector
{
    private readonly string _keyPath;
    private readonly byte[] _key; // 32 bytes

    public FileKeyAesProtector(string appConfigDir)
    {
        Directory.CreateDirectory(appConfigDir);
        _keyPath = Path.Combine(appConfigDir, "secrets.key");

        if (File.Exists(_keyPath))
        {
            _key = File.ReadAllBytes(_keyPath);
        }
        else
        {
            _key = RandomNumberGenerator.GetBytes(32);
            using (var fs = new FileStream(_keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(_key, 0, _key.Length);
                fs.Flush(true);
            }
            TryRestrictPermissions(_keyPath);
        }
    }

    public string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return null;

        var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintextBytes.Length];

        using var aes = new AesGcm(_key);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // payload = nonce || tag || ciphertext  (all base64)
        var payload = new byte[12 + 16 + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, 12);
        Buffer.BlockCopy(tag, 0, payload, 12, 16);
        Buffer.BlockCopy(ciphertext, 0, payload, 28, ciphertext.Length);
        return Convert.ToBase64String(payload);
    }

    public string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return null;
        try
        {
            var payload = Convert.FromBase64String(protectedText);
            if (payload.Length < 28) return null;

            var nonce = new byte[12];
            var tag = new byte[16];
            var ciphertext = new byte[payload.Length - 28];
            Buffer.BlockCopy(payload, 0, nonce, 0, 12);
            Buffer.BlockCopy(payload, 12, tag, 0, 16);
            Buffer.BlockCopy(payload, 28, ciphertext, 0, ciphertext.Length);

            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(_key);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch { return null; }
    }

    private static void TryRestrictPermissions(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows: the file inherits the user's profile ACLs in AppData—usually OK.
                return;
            }
            // On Unix, chmod 600
            try { System.Diagnostics.Process.Start("chmod", $"600 \"{path}\"")?.Dispose(); } catch { }
        }
        catch { /* best effort */ }
    }
}
