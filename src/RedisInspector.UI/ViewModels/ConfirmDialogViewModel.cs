using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace RedisInspector.UI.ViewModels;

public sealed class ConfirmDialogViewModel : ObservableObject
{
    public string Title { get; }
    public string Message { get; }
    public IRelayCommand YesCommand { get; }
    public IRelayCommand NoCommand { get; }
    public event EventHandler<bool>? CloseRequested;

    public ConfirmDialogViewModel(string title, string message)
    {
        Title = title; Message = message;
        YesCommand = new RelayCommand(() => CloseRequested?.Invoke(this, true));
        NoCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
    }
}
