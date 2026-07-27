using System.IO;
using System.Security.Cryptography;
using System.Text;
using SteamAchievements.Core.Abstractions;

namespace SteamAchievements.Windows;

/// <summary>
/// The API key, encrypted with DPAPI in the <c>CurrentUser</c> scope. That scope
/// is what makes two Windows users on one machine independent without any code
/// saying so.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;

    public DpapiSecretStore(string path) => _path = path;

    public string? Read()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), optionalEntropy: null, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // A CurrentUser blob is unreadable from another Windows profile and
            // after a reinstall. That is the same state as "no key was ever
            // stored", and onboarding already handles it — there is no second
            // branch worth writing.
            return null;
        }
    }

    public void Write(string secret)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        File.WriteAllBytes(_path, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret), optionalEntropy: null, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
