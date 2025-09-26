using RedisInspector.UI.Models;
using System.Threading.Tasks;

namespace RedisInspector.UI.Services
{
    public interface IWindowDialogService
    {
        Task<ConnectionProfile?> ShowEditConnectionAsync(ConnectionProfile? existing);
        Task<bool> ShowConfirmAsync(string title, string message);
    }
}
