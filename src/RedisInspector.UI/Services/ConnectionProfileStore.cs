using System.Collections.Generic;
using System.IO;
using System;
using System.Text.Json;
using RedisInspector.UI.Models;
using RedisInspector.UI.Services.Security;

namespace RedisInspector.UI.Services;

public sealed class ConnectionProfileStore
{
    private readonly string _dir;
    private readonly string _filePath;
    private readonly ISecretProtector _protector;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ConnectionProfileStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RedisInspector");
        Directory.CreateDirectory(_dir);
        _filePath = Path.Combine(_dir, "connections.json");

        _protector = OperatingSystem.IsWindows()
            ? new WindowsDpapiProtector()
            : new FileKeyAesProtector(_dir);
    }

    public List<ConnectionProfile> LoadAll()
    {
        if (!File.Exists(_filePath)) return new();
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(json) ?? new();
        }
        catch { return new(); }
    }

    public void SaveAll(IEnumerable<ConnectionProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, _json);
        File.WriteAllText(_filePath, json);
    }

    // helpers your VM can call:
    public string? Protect(string? plain) => _protector.Protect(plain);
    public string? Unprotect(string? blob) => _protector.Unprotect(blob);
}
