using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedisInspector.UI.Models;
using System;

namespace RedisInspector.UI.ViewModels
{
    public sealed class EditConnectionViewModel : ObservableObject
    {
        private readonly Guid _id;
        // Bound fields
        private string _name = "";
        public string Name { get => _name; set { SetProperty(ref _name, value); UpdateCanAccept(); } }

        private string _redisUrl = "redis://localhost:6389";
        public string RedisUrl { get => _redisUrl; set { SetProperty(ref _redisUrl, value); ValidateRedisUrl(); UpdateCanAccept(); } }

        private string? _sshHost;
        public string? SshHost { get => _sshHost; set => SetProperty(ref _sshHost, value); }

        private int _sshPort = 22;
        public int SshPort { get => _sshPort; set => SetProperty(ref _sshPort, value); }

        private string? _sshUser;
        public string? SshUser { get => _sshUser; set => SetProperty(ref _sshUser, value); }

        private string _sshPass = "";
        public string SshPass { get => _sshPass; set => SetProperty(ref _sshPass, value); }

        // Validation state
        private bool _isRedisUrlValid = true;
        public bool IsRedisUrlValid { get => _isRedisUrlValid; private set { SetProperty(ref _isRedisUrlValid, value); OnPropertyChanged(nameof(HasRedisUrlError)); } }
        public bool HasRedisUrlError => !IsRedisUrlValid;

        private string? _redisUrlError;
        public string? RedisUrlError { get => _redisUrlError; private set => SetProperty(ref _redisUrlError, value); }

        private readonly bool _isEdit;
        private readonly string _originalPassword = "";

        // Accept/cancel
        private bool _canAccept;
        public bool CanAccept { get => _canAccept; private set => SetProperty(ref _canAccept, value); }

        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public event EventHandler<ConnectionProfile?>? CloseRequested;
        private bool _initializing;


        public EditConnectionViewModel(ConnectionProfile? existing)
        {
            _isEdit = existing is not null;
            _id = existing?.Id ?? Guid.NewGuid();
            _initializing = true;
            OkCommand = new RelayCommand(() => CloseRequested?.Invoke(this, BuildProfile()), () => CanAccept);
            CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, null));


            if (existing != null)
            {
                Name = existing.Name;
                RedisUrl = existing.RedisUrl;
                SshHost = existing.SshHost;
                SshPort = existing.SshPort;
                SshUser = existing.SshUser;
                _originalPassword = existing.SshPass ?? "";
                // Optional UX: leave SshPass empty so user can type a new one;
                // if left empty, we’ll keep _originalPassword on OK.
                _sshPass = "";
            }

            // Let UI see initial values
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(RedisUrl));
            OnPropertyChanged(nameof(SshHost));
            OnPropertyChanged(nameof(SshPort));
            OnPropertyChanged(nameof(SshUser));
            OnPropertyChanged(nameof(SshPass));


            ValidateRedisUrl();
            UpdateCanAccept();

            _initializing = false;
        }

        public void SetDecryptedPassword(string? pwd) => SshPass = pwd ?? "";

        private void UpdateCanAccept()
        {
            CanAccept = !string.IsNullOrWhiteSpace(Name) && IsRedisUrlValid;
            ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
        }

        private void ValidateRedisUrl()
        {
            var redisUrlInput = RedisUrl?.Trim();
            if (string.IsNullOrEmpty(redisUrlInput))
            {
                IsRedisUrlValid = false;
                RedisUrlError = "Redis URL is required.";
                ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
                return;
            }

            if (redisUrlInput.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
                redisUrlInput.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // simple host[:port]
                    var parts = redisUrlInput.Replace("redis://", "").Replace("rediss://", "").Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var host = parts.Length > 0 ? parts[0] : null;
                    var port = parts.Length > 1 ? parts[1] : null;
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        IsRedisUrlValid = false;
                        RedisUrlError = "Host is required. eg. redis://<HOST>:6389";
                        ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
                        return;
                    }

                    if (parts.Length > 1 && !int.TryParse(parts[1], out var portOut))
                    {
                        IsRedisUrlValid = false;
                        RedisUrlError = "Port must be a number.  eg. redis://localhost:6389";
                        ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
                        return;
                    }
                    IsRedisUrlValid = true;
                    RedisUrlError = null;


                    var u = new Uri(redisUrlInput);
                    if (string.IsNullOrWhiteSpace(u.Host))
                    {
                        IsRedisUrlValid = false;
                        RedisUrlError = "URI must include host.";
                        ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
                        return;
                    }
                    if (u.Port < 1 || u.Port > 65535)
                    {
                        IsRedisUrlValid = false;
                        RedisUrlError = "Port out of range.";
                        ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
                        return;
                    }
                    IsRedisUrlValid = true;
                    RedisUrlError = null;
                    ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
                    return;
                }
                catch (Exception ex)
                {
                    IsRedisUrlValid = false;
                    RedisUrlError = "Invalid URI. " + ex.Message;
                    ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
                    return;
                }
            }
            else
            {
                IsRedisUrlValid = false;
                RedisUrlError = "Invalid URI. must start with  redis:// or rediss://";
                ((RelayCommand)OkCommand).NotifyCanExecuteChanged();
                return;
            }
        }

        private ConnectionProfile BuildProfile() => new()
        {
            Id = _id,
            Name = Name.Trim(),
            RedisUrl = RedisUrl.Trim(),
            SshHost = string.IsNullOrWhiteSpace(SshHost) ? null : SshHost.Trim(),
            SshPort = SshPort,
            SshUser = string.IsNullOrWhiteSpace(SshUser) ? null : SshUser.Trim(),
            // if editing and user didn’t type a new password, keep the old one
            SshPass = (!string.IsNullOrEmpty(SshPass) ? SshPass : (_isEdit ? _originalPassword : ""))
        };
    }
}
