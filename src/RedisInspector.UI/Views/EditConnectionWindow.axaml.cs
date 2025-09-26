using Avalonia.Controls;
using RedisInspector.UI.ViewModels;
using System.Threading.Tasks;
using RedisInspector.UI.Models;
using Avalonia.Markup.Xaml;

namespace RedisInspector.UI.Views;

public partial class EditConnectionWindow : Window
{
    public EditConnectionWindow(ConnectionProfile? existing)
    {
        InitializeComponent();
        var vm = new EditConnectionViewModel(existing);
        DataContext = vm;  // make sure nothing else overwrites this later

        vm.CloseRequested += (_, result) => Close(result);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public async Task<ConnectionProfile?> ShowAsync(ConnectionProfile? existing, string? decryptedPassword)
    {
        var vm = new EditConnectionViewModel(existing);
        if (!string.IsNullOrEmpty(decryptedPassword))
            vm.SetDecryptedPassword(decryptedPassword);
        vm.CloseRequested += (_, result) => Close(result);
        DataContext = vm;
        return await ShowDialog<ConnectionProfile?>(this);
    }
}
