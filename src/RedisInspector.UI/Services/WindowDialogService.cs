using Avalonia.Controls;
using System.Threading.Tasks;
using RedisInspector.UI.Models;
using RedisInspector.UI.Views;

namespace RedisInspector.UI.Services;

public sealed class WindowDialogService : IWindowDialogService
{
    private readonly Window _owner;
    public WindowDialogService(Window owner) => _owner = owner;

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var dlg = new ConfirmDialogWindow(title, message);
        return await dlg.ShowDialog<bool>(_owner);
    }

    public async Task<ConnectionProfile?> ShowEditConnectionAsync(ConnectionProfile? existing)
    {
        var dlg = new EditConnectionWindow(existing);
        return await dlg.ShowDialog<ConnectionProfile?>(_owner);
    }
}
