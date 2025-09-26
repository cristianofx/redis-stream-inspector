using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RedisInspector.UI.ViewModels;

namespace RedisInspector.UI.Views;

public partial class ConfirmDialogWindow : Window
{
    public ConfirmDialogWindow(string title, string message)
    {
        InitializeComponent();

        var vm = new ConfirmDialogViewModel(title, message);
        vm.CloseRequested += (_, result) => Close(result);   // <-- critical
        DataContext = vm;

#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
