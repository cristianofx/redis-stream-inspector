using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedisInspector.UI.Services.Security
{
    public interface ISecretProtector
    {
        string? Protect(string? plainText);    // returns base64 or null
        string? Unprotect(string? protectedText);
    }
}
