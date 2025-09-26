using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisInspector.CLI.src.RedisInspector.Core.Services;
using RedisInspector.Core.Models.Helpers;
using RedisInspector.UI.Models;
using RedisInspector.UI.Services;
using StackExchange.Redis;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media;

namespace RedisInspector.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public IWindowDialogService? Dialogs { get; set; }

    public IRelayCommand AddConnectionCommand { get; }
    public IRelayCommand UpdateConnectionCommand { get; }
    public IRelayCommand DeleteConnectionCommand { get; }


    // --- Bindable inputs ---
    public SearchOptionsViewDto SearchOptions { get; } = new();

    // --- State / output ---
    public ObservableCollection<SearchHit> Results { get; } = new();
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string status = "Idle";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private string footer = "";
    [ObservableProperty] private string? selectedRawMessage;

    // Text Finder
    [ObservableProperty] private bool isFindVisible;
    [ObservableProperty] private string? searchQuery;
    [ObservableProperty] private bool matchCase;
    [ObservableProperty] private bool wholeWord;
    [ObservableProperty] private string searchStatus = "";

    public event Action? FocusFindRequested;
    public event Action<SearchTextDirection>? SearchTextRequested;
    public event Action? SearchClearRequested;

    [RelayCommand]
    private void ShowFind()
    {
        IsFindVisible = true;
        FocusFindRequested?.Invoke();
    }
    [RelayCommand]
    private void HideFind()
    {
        IsFindVisible = false;
    }

    [RelayCommand] private void FindNextText() => SearchTextRequested?.Invoke(SearchTextDirection.Next);
    [RelayCommand] private void FindPrevText() => SearchTextRequested?.Invoke(SearchTextDirection.Prev);
    [RelayCommand] private void ClearSearch() { SearchQuery = ""; SearchStatus = ""; SearchClearRequested?.Invoke(); }

    public enum SearchTextDirection { Next, Prev }

    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand NewProfileFromFormCommand { get; }

    // Validation state
    private bool _isRedisUrlValid = true;
    public bool IsRedisUrlValid
    {
        get => _isRedisUrlValid;
        private set
        {
            if (SetProperty(ref _isRedisUrlValid, value))
            {
                OnPropertyChanged(nameof(HasRedisUrlError));
                OnPropertyChanged(nameof(CanStart)); // if you use this to gate Run
            }
        }
    }


    [ObservableProperty]
    private IBrush statusBrush = Brushes.Gray;


    //Status spinner
    private CancellationTokenSource? _statusSpinnerCts;
    private Task? _statusSpinnerTask;

    public bool HasRedisUrlError => !IsRedisUrlValid;

    private string? _redisUrlError;
    public string? RedisUrlError
    {
        get => _redisUrlError;
        private set => SetProperty(ref _redisUrlError, value);
    }

    private SearchHit? _selectedItem;
    public SearchHit? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
                SelectedRawMessage = FormatIfJson(value?.RawMessage);
        }
    }

    private CancellationTokenSource? _cts;
    private IConnectionMultiplexer? _mux;
    private SshTunnel? _tunnel;

    // Collapsible pane state

    private bool _isOptionsOpen = true;
    public bool IsOptionsOpen
    {
        get => _isOptionsOpen;
        set => SetProperty(ref _isOptionsOpen, value);
    }

    //Profiles
    private readonly ConnectionProfileStore _connectionProfileStore = new();

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = new();

    private ConnectionProfile? _selectedConnectionProfile;
    public ConnectionProfile? SelectedConnectionProfile
    {
        get => _selectedConnectionProfile;
        set
        {
            if (SetProperty(ref _selectedConnectionProfile, value))
            {
                if (value != null)
                {
                    ValidateRedisUrl();
                }
                ((AsyncRelayCommand)UpdateConnectionCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DeleteConnectionCommand).NotifyCanExecuteChanged();
            }
        }
    }

    private string _newConnectionProfileName = "";
    public string NewConnectionProfileName { get => _newConnectionProfileName; set => SetProperty(ref _newConnectionProfileName, value); }

    public bool CanStart => !IsRunning && IsRedisUrlValid;

    // On Events
    partial void OnStatusChanged(string value)
    {
        StatusBrush = value switch
        {
            var s when s.Contains("Searching", StringComparison.OrdinalIgnoreCase) => Brushes.LightBlue,
            var s when s.Contains("Connecting", StringComparison.OrdinalIgnoreCase) => Brushes.Yellow,
            "Done" => Brushes.LightGreen,
            "No matches" => Brushes.Orange,
            var s when s.Contains("Error", StringComparison.OrdinalIgnoreCase) => Brushes.Red,
            var s when s.Contains("Canceled", StringComparison.OrdinalIgnoreCase) => Brushes.DarkOrange,
            var s when s.Contains("Opening", StringComparison.OrdinalIgnoreCase) => Brushes.LightSalmon,
            _ => Brushes.Gray
        };
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanStart));

    private bool CanSaveConnectionProfile()
    {
        // Require a name & a valid URL
        return !string.IsNullOrWhiteSpace(NewConnectionProfileName) && IsRedisUrlValid;
    }

    public MainWindowViewModel()
    {
        Footer = "Ready";
        ValidateRedisUrl();

        SaveProfileCommand = new RelayCommand(SaveOrUpdateConnectionProfile, CanSaveConnectionProfile);
        DeleteProfileCommand = new RelayCommand(DeleteSelectedConnectionProfile, () => SelectedConnectionProfile != null);
        NewProfileFromFormCommand = new RelayCommand(CreateNewProfileFromForm);

        AddConnectionCommand = new AsyncRelayCommand(AddConnectionAsync);
        UpdateConnectionCommand = new AsyncRelayCommand(UpdateConnectionAsync, () => SelectedConnectionProfile != null);
        DeleteConnectionCommand = new AsyncRelayCommand(DeleteConnectionAsync, () => SelectedConnectionProfile != null);

        // Load persisted profiles once
        foreach (var p in _connectionProfileStore.LoadAll()) ConnectionProfiles.Add(p);
    }

    [RelayCommand]
    private async Task StartSearch()
    {
        try
        {
            if (IsRunning) return;

            // Validate quickly
            if (string.IsNullOrWhiteSpace(SelectedConnectionProfile?.RedisUrl) || string.IsNullOrWhiteSpace(SearchOptions.StreamsCsv))
            {
                Status = "Please fill Streams.";
                return;
            }

            // Open SSH tunnel if requested
            if (SelectedConnectionProfile != null && !string.IsNullOrWhiteSpace(SelectedConnectionProfile.SshHost))
            {
                string? redisHost = string.Empty;
                string? redisPort = "0";
                if (!string.IsNullOrEmpty(SelectedConnectionProfile?.RedisUrl))
                {
                    redisHost = SelectedConnectionProfile.RedisUrl.Replace("redis://", "").Replace("rediss://", "").Split(":")[0];
                    redisPort = SelectedConnectionProfile.RedisUrl.Replace("redis://", "").Replace("rediss://", "").Split(":")[1];
                }
                Status = "Opening SSH tunnel...";
                await StartStatusSpinner("Opening SSH tunnel");
                _tunnel = await Task.Run(() => SshTunnel.Open(
                    sshHost: SelectedConnectionProfile?.SshHost!,
                    sshPort: SelectedConnectionProfile != null ? SelectedConnectionProfile.SshPort : 0,
                    sshUser: SelectedConnectionProfile != null && !string.IsNullOrEmpty(SelectedConnectionProfile.SshUser) ? SelectedConnectionProfile.SshUser : string.Empty,
                    sshPassword: SelectedConnectionProfile != null && string.IsNullOrEmpty(SelectedConnectionProfile.EncryptedPassword) ? null : _connectionProfileStore.Unprotect(SelectedConnectionProfile.EncryptedPassword),
                    sshKeyPath: null,
                    sshKeyPassphrase: null,
                    sshRemoteHost: redisHost,
                    sshRemoteRedisPort: Int32.Parse(redisPort),
                    localBindHost: "127.0.0.1",
                    localBindPort: null
                ));
                await StopStatusSpinner();
            }

            IsRunning = true;
            _cts = new CancellationTokenSource();
            Results.Clear();
            SelectedRawMessage = null;
            Status = "Connecting to Redis...";
            await StartStatusSpinner("Connecting to Redis");
            Footer = "";


            // Build configuration (minimal, UI-only)
            var cfg = BuildConfigurationWithOptionalTunnel(SelectedConnectionProfile?.RedisUrl!, _tunnel);
            _mux = await ConnectionMultiplexer.ConnectAsync(cfg);
            var db = _mux.GetDatabase();

            var options = new SearchOptions
            {
                Streams = SearchOptions.StreamsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                FindField = SearchOptions.FindField,
                FindEq = string.IsNullOrWhiteSpace(SearchOptions.FindEq) ? null : SearchOptions.FindEq,
                FindFromId = "-",
                FindToId = "+",
                FindLast = Math.Max(0, SearchOptions.FindLast),
                FindMax = SearchOptions.FindMax <= 0 ? int.MaxValue : SearchOptions.FindMax,
                FindPage = SearchOptions.FindPage <= 0 ? 100 : SearchOptions.FindPage,
                FindCaseInsensitive = SearchOptions.FindCaseInsensitive,
                JsonField = string.IsNullOrWhiteSpace(SearchOptions.JsonField) ? "message" : SearchOptions.JsonField!,
                JsonPath = string.IsNullOrWhiteSpace(SearchOptions.JsonPath) ? null : SearchOptions.JsonPath,
                MessageOnly = SearchOptions.MessageOnly
            };

            var runner = new SearchRunner(_mux, db, options);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int emitted = 0;
            await StopStatusSpinner();

            Status = "Searching...";
            await StartStatusSpinner("Searching");

            try
            {
                await foreach (var hit in runner.RunAsync(_cts.Token))
                {
                    Results.Add(hit);
                    emitted++;
                    if (emitted == 1)
                        SelectedRawMessage = FormatIfJson(hit.RawMessage);
                }
                sw.Stop();
                Summary = $"Matches: {emitted}";
                Footer = $"Elapsed: {sw.ElapsedMilliseconds} ms";
                Status = emitted == 0 ? "No matches" : "Done";
            }
            catch (NoStreamsFoundException ex)
            {
                sw.Stop();
                await StopStatusSpinner();
                Summary = string.Empty;
                Footer = string.Empty;
                Footer = $"Elapsed: {sw.ElapsedMilliseconds} ms";
                Status = $"Error: {ex.Message}";        // e.g., "Stream 'foo' was not found."
            }
            finally
            {
                await StopStatusSpinner();              // stop the animation
            }

            
        }
        catch (OperationCanceledException)
        {
            await StopStatusSpinner();
            Status = "Canceled";
        }
        catch (Exception ex)
        {
            await StopStatusSpinner();
            Status = $"Error: {ex.Message}";
            Footer = ex.Message;
        }
        finally
        {
            await StopStatusSpinner();
            IsRunning = false;
            _cts?.Dispose(); _cts = null;
            try { _mux?.Dispose(); } catch { }
            _mux = null;
            try { _tunnel?.Dispose(); } catch { }
            _tunnel = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private static ConfigurationOptions BuildConfigurationWithOptionalTunnel(string redisUrl, SshTunnel? tunnel)
    {
        var cfg = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 10000,
            SyncTimeout = 10000,
            KeepAlive = 10,
            ReconnectRetryPolicy = new ExponentialRetry(5000)
        };

        // Detect TLS & auth from URL, but endpoint from tunnel if present.
        bool uriLike = redisUrl.StartsWith("redis://", StringComparison.OrdinalIgnoreCase)
                    || redisUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);

        bool useTls = false;
        string? redisPassword = null;
        string? redisUser = null;
        string? urlHost = null;
        int urlPort = -1;

        if (uriLike)
        {
            var u = new Uri(redisUrl);
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
            var parts = redisUrl.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            urlHost = parts.Length > 0 ? parts[0] : "127.0.0.1";
            urlPort = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 6379;
        }

        if (tunnel is not null)
            cfg.EndPoints.Add(tunnel.LocalHost, tunnel.LocalPort);
        else
            cfg.EndPoints.Add(string.IsNullOrEmpty(urlHost) ? "127.0.0.1" : urlHost, urlPort > 0 ? urlPort : 6379);

        cfg.Ssl = useTls;
        if (!string.IsNullOrEmpty(redisPassword)) cfg.Password = redisPassword;
        if (!string.IsNullOrEmpty(redisUser)) cfg.User = redisUser;

        return cfg;
    }

    /// <summary>
    /// Pretty-prints JSON if the text is (or contains) JSON; otherwise returns the original text.
    /// Handles direct JSON ("{...}" or "[...]") and JSON string that contains JSON ("\"{...}\"").
    /// </summary>
    public string? FormattedRawMessage => FormatIfJson(SelectedRawMessage);

    private static string? FormatIfJson(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        var t = s.Trim();

        // direct JSON object/array
        if (t.StartsWith("{") || t.StartsWith("["))
        {
            if (TryPrettyPrint(t, out var pretty))
                return pretty;
            return s;
        }

        // JSON string literal possibly containing JSON
        if (t.StartsWith("\""))
        {
            try
            {
                using var outer = JsonDocument.Parse(t);
                if (outer.RootElement.ValueKind == JsonValueKind.String)
                {
                    var inner = outer.RootElement.GetString();
                    if (!string.IsNullOrWhiteSpace(inner))
                    {
                        var ti = inner.Trim();
                        if ((ti.StartsWith("{") || ti.StartsWith("[")) && TryPrettyPrint(ti, out var prettyInner))
                            return prettyInner;
                        return inner; // plain string contents
                    }
                }
            }
            catch { /* ignore and fall through */ }
        }

        // not JSON
        return s;

        static bool TryPrettyPrint(string json, out string pretty)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                // Serialize the JsonElement with indentation
                pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                return true;
            }
            catch
            {
                pretty = json;
                return false;
            }
        }
    }

    // Core validation logic.
    // Accepts either:
    //  - URI: redis://host[:port] or rediss://host[:port] (user[:pass]@ OK)
    //  - Simple: host[:port] (your rule: split by ':', port must be int if present)
    private void ValidateRedisUrl()
    {
        var s = SelectedConnectionProfile?.RedisUrl?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            IsRedisUrlValid = false;
            RedisUrlError = "Redis URL is required.";
            return;
        }

        if (s.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var u = new Uri(s);
                if (string.IsNullOrWhiteSpace(u.Host))
                {
                    IsRedisUrlValid = false;
                    RedisUrlError = "URI must include host (e.g., redis://localhost:6379).";
                    return;
                }
                if (u.Port < -1 || u.Port > 65535)
                {
                    IsRedisUrlValid = false;
                    RedisUrlError = "Port out of range.";
                    return;
                }
                // OK
                IsRedisUrlValid = true;
                RedisUrlError = null;
                return;
            }
            catch (Exception ex)
            {
                IsRedisUrlValid = false;
                RedisUrlError = "Invalid redis URI. " + ex.Message;
                return;
            }
        }

        // Simple form: host[:port]
        var parts = s.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var urlHost = parts.Length > 0 ? parts[0] : null;
        int urlPort = -1;

        if (string.IsNullOrWhiteSpace(urlHost))
        {
            IsRedisUrlValid = false;
            RedisUrlError = "Host is required (e.g., localhost or localhost:6379).";
            return;
        }

        if (parts.Length > 1 && !int.TryParse(parts[1], out urlPort))
        {
            IsRedisUrlValid = false;
            RedisUrlError = "Port must be a number.";
            return;
        }

        if (urlPort is < -1 or > 65535)
        {
            IsRedisUrlValid = false;
            RedisUrlError = "Port out of range.";
            return;
        }

        // OK – host only (defaults later) or host:port
        IsRedisUrlValid = true;
        RedisUrlError = null;
    }

    private void SaveOrUpdateConnectionProfile()
    {
        if (!CanSaveConnectionProfile()) return;

        var encPwd = _connectionProfileStore.Protect(SelectedConnectionProfile?.SshPass);

        var existing = ConnectionProfiles.FirstOrDefault(p =>
            string.Equals(p.Name, NewConnectionProfileName, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            existing = new ConnectionProfile { Name = NewConnectionProfileName };
            ConnectionProfiles.Add(existing);
        }

        existing.RedisUrl = SelectedConnectionProfile?.RedisUrl?.Trim()!;
        existing.SshHost = string.IsNullOrWhiteSpace(SelectedConnectionProfile?.SshHost) ? null : SelectedConnectionProfile?.SshHost.Trim();
        existing.SshPort = SelectedConnectionProfile.SshPort;
        existing.SshUser = string.IsNullOrWhiteSpace(SelectedConnectionProfile?.SshUser) ? null : SelectedConnectionProfile?.SshUser.Trim();
        existing.EncryptedPassword = encPwd;

        _connectionProfileStore.SaveAll(ConnectionProfiles);
        SelectedConnectionProfile = existing;
        Status = $"Saved profile '{existing.Name}'.";
    }

    private void DeleteSelectedConnectionProfile()
    {
        if (SelectedConnectionProfile == null) return;
        var name = SelectedConnectionProfile.Name;
        ConnectionProfiles.Remove(SelectedConnectionProfile);
        _connectionProfileStore.SaveAll(ConnectionProfiles);
        SelectedConnectionProfile = null;
        Status = $"Deleted profile '{name}'.";
    }

    private void CreateNewProfileFromForm()
    {
        // Seed the name quickly from host
        if (string.IsNullOrWhiteSpace(NewConnectionProfileName))
        {
            // derive host from url for a default name
            var host = "profile";
            try
            {
                if (SelectedConnectionProfile!.RedisUrl.StartsWith("redis", StringComparison.OrdinalIgnoreCase))
                    host = new Uri(SelectedConnectionProfile?.RedisUrl!).Host;
                else host = SelectedConnectionProfile?.RedisUrl.Split(':', 2)[0];
            }
            catch { }
            NewConnectionProfileName = $"{host}-{DateTime.Now:HHmmss}";
        }
        SelectedConnectionProfile = null;
        Status = "Ready to save as new profile.";
    }

    private async Task AddConnectionAsync()
    {
        if (Dialogs is null) return;
        var result = await Dialogs.ShowEditConnectionAsync(null);
        if (result == null) return;

        if (!string.IsNullOrEmpty(result.SshPass))
        {
            // Encrypt password and persist
            result.EncryptedPassword = _connectionProfileStore.Protect(result.SshPass);
            result.SshPass = "";
        }

        ConnectionProfiles.Add(result);
        _connectionProfileStore.SaveAll(ConnectionProfiles);
        SelectedConnectionProfile = result;
        Status = $"Created profile '{result.Name}'.";
    }

    private async Task UpdateConnectionAsync()
    {
        if (Dialogs is null || SelectedConnectionProfile is null) return;

        // Decrypt existing password
        var pwd = _connectionProfileStore.Unprotect(SelectedConnectionProfile.EncryptedPassword);

        // Show prefilled dialog
        var edited = await Dialogs.ShowEditConnectionAsync(SelectedConnectionProfile);
        if (edited == null) return; // cancelled

        // If user provided a new password in this edit, persist it
        if (!string.IsNullOrEmpty(edited.SshPass))
        {
            edited.EncryptedPassword = _connectionProfileStore.Protect(edited.SshPass);
            SelectedConnectionProfile.EncryptedPassword = edited.EncryptedPassword;
            SelectedConnectionProfile.HasSecret = true;
            SelectedConnectionProfile.SshPass = "";
        }
        else
        {
            // no new password typed: keep whatever was stored
            edited.EncryptedPassword = SelectedConnectionProfile.EncryptedPassword;
        }

        // Update existing object in-place (keeps selection binding happy)
        SelectedConnectionProfile.Name = edited.Name;
        SelectedConnectionProfile.RedisUrl = edited.RedisUrl;
        SelectedConnectionProfile.SshHost = edited.SshHost;
        SelectedConnectionProfile.SshPort = edited.SshPort;
        SelectedConnectionProfile.SshUser = edited.SshUser;


        _connectionProfileStore.SaveAll(ConnectionProfiles);

        // raise changes for UI if needed
        OnPropertyChanged(nameof(ConnectionProfiles));
        var idx = ConnectionProfiles.IndexOf(SelectedConnectionProfile);
        if (idx >= 0)
        {
            ConnectionProfiles[idx] = edited;            // ObservableCollection raises Replace
            edited.SshPass = "";
            SelectedConnectionProfile = edited;          // keep selection coherent
        }
        Status = $"Updated profile '{SelectedConnectionProfile.Name}'.";
    }

    private async Task DeleteConnectionAsync()
    {
        if (Dialogs is null || SelectedConnectionProfile is null) return;
        var ok = await Dialogs.ShowConfirmAsync("Delete connection",
            $"Are you sure you want to delete '{SelectedConnectionProfile.Name}'?");
        if (!ok) return;

        var name = SelectedConnectionProfile.Name;
        ConnectionProfiles.Remove(SelectedConnectionProfile);
        _connectionProfileStore.SaveAll(ConnectionProfiles);
        SelectedConnectionProfile = null;
        Status = $"Deleted profile '{name}'.";
    }

    // Helper for MainWindow to fetch a decrypted password (optional pattern)
    public string? GetDecryptedPasswordFor(ConnectionProfile? p)
        => p == null ? null : _connectionProfileStore.Unprotect(p.EncryptedPassword);

    // start/stop helpers
    private async Task StartStatusSpinner(string baseText)
    {
        await StopStatusSpinner(); // in case one is already running
        _statusSpinnerCts = new CancellationTokenSource();
        _statusSpinnerTask = AnimateStatusAsync(baseText, _statusSpinnerCts.Token);
    }

    private async Task StopStatusSpinner()
    {
        if (_statusSpinnerCts is null) return;
        _statusSpinnerCts.Cancel();
        try { if (_statusSpinnerTask is not null) await _statusSpinnerTask; } catch { /* ignored */ }
        _statusSpinnerCts.Dispose();
        _statusSpinnerCts = null;
        _statusSpinnerTask = null;
    }

    // animation loop
    private async Task AnimateStatusAsync(string baseText, CancellationToken token)
    {
        var frames = new[] { "...", "", ".", ".." }; // 0..3 then repeat
        var i = 0;

        // set initial frame immediately
        await Dispatcher.UIThread.InvokeAsync(() => Status = $"{baseText}{frames[i]}");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                i = (i + 1) % frames.Length;
                var next = $"{baseText}{frames[i]}";
                // update on UI thread
                await Dispatcher.UIThread.InvokeAsync(() => Status = next);
            }
        }
        catch (OperationCanceledException) { /* normal stop */ }
    }

}
