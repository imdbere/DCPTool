using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DcpTool.Core.Persistence;
using DcpTool.Core.Settings;

namespace DcpTool.App.Services;

/// <summary>Windows: one DPAPI-protected (current user) file holding the passwords by user name.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore(AppPaths paths) : ICredentialStore
{
    public bool IsPersistent => true;

    public string? LoadPassword(string username)
    {

        return ReadAll().GetValueOrDefault(username);
    }

    public void SavePassword(string username, string password)
    {

        var all = ReadAll();
        all[username] = password;
        WriteAll(all);
    }

    public void DeletePassword(string username)
    {

        var all = ReadAll();
        if (all.Remove(username))
        {
            WriteAll(all);
        }
    }

    private Dictionary<string, string> ReadAll()
    {
        try
        {
            if (File.Exists(paths.CredentialsFile))
            {
                var plain = ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(paths.CredentialsFile).Trim()), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? [];
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable (other user, damaged): treated as "no saved password"
        }

        return [];
    }

    private void WriteAll(Dictionary<string, string> all)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(all);
        var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(paths.Root);
        AtomicFile.WriteAllText(paths.CredentialsFile, Convert.ToBase64String(protectedBytes));
    }
}

/// <summary>Other platforms (until the macOS keychain store exists): lives as long as the app runs.</summary>
public sealed class MemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _passwords = [];
    public bool IsPersistent => false;
    public string? LoadPassword(string username) => _passwords.GetValueOrDefault(username);
    public void SavePassword(string username, string password) => _passwords[username] = password;
    public void DeletePassword(string username) => _passwords.Remove(username);
}
