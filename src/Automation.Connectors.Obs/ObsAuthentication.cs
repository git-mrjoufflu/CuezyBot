using System.Security.Cryptography;
using System.Text;

namespace Automation.Connectors.Obs;

public static class ObsAuthentication
{
    public static string ComputeResponse(string password, string salt, string challenge)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(challenge);

        var secretBytes = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        var secret = Convert.ToBase64String(secretBytes);
        var responseBytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge));
        return Convert.ToBase64String(responseBytes);
    }
}
