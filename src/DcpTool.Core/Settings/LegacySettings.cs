using System.Xml.Linq;

namespace DcpTool.Core.Settings;

/// <summary>
/// Reads the RAI login that DCP Tool 0.x stored (in plain text) in
/// <c>%LocalAppData%\DCP-Tool\DCP-Tool_Url_*\&lt;version&gt;\user.config</c>, so a user who moves to v2
/// doesn't have to type it again. v2 keeps the password in the operating system's protected storage.
/// </summary>
public static class LegacySettings
{
    /// <summary>The login of the most recently used v1 installation, or null. Never throws.</summary>
    public static (string Username, string Password)? TryReadLogin(string? localAppData = null)
    {
        try
        {
            var root = Path.Combine(
                localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DCP-Tool");
            if (!Directory.Exists(root))
            {
                return null;
            }

            foreach (var file in Directory.EnumerateFiles(root, "user.config", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var settings = XDocument.Load(file).Descendants("setting")
                    .ToDictionary(s => (string?)s.Attribute("name") ?? "", s => (string?)s.Element("value") ?? "");
                if (settings.TryGetValue("Username", out var username) && !string.IsNullOrWhiteSpace(username))
                {
                    return (username.Trim(), settings.GetValueOrDefault("Password", ""));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
            // Convenience only: without it the user is simply asked for the login
        }

        return null;
    }
}
