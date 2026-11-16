using DcpTool.Core.Model;

namespace DcpTool.Core.Settings;

/// <summary>The production company that uses the tool. Printed on the paper form and used as defaults for new DCPs.</summary>
public sealed class CompanyProfile
{
    /// <summary>"Fornitore del Programma" on the paper form.</summary>
    public string Fornitore { get; set; } = "";

    /// <summary>"Nominativo Fornitore incaricato della compilazione".</summary>
    public string Compilatore { get; set; } = "";

    /// <summary>The supplier's legal representative.</summary>
    public string LegaleRappresentante { get; set; } = "";

    /// <summary>Committente code new DCPs start with, e.g. <c>BZ/16</c>. Empty = none.</summary>
    public string DefaultSede { get; set; } = "";

    public string DefaultRete { get; set; } = "TV1";

    /// <summary>"Responsabile Rai del Programma" new DCPs start with.</summary>
    public string DefaultResponsabileRai { get; set; } = "";

    /// <summary>A new DCP with this profile's defaults.</summary>
    public Dcp CreateDcp() => new()
    {
        Sede = DefaultSede,
        Rete = string.IsNullOrWhiteSpace(DefaultRete) ? "TV1" : DefaultRete,
        ResponsabileRai = DefaultResponsabileRai,
    };
}

/// <summary>
/// Overrides for the built-in addresses of the RAI portal. Null = use the built-in value. They exist so
/// that a change on RAI's side can be worked around from the settings file before a fixed version is out.
/// </summary>
public sealed class RaiPortalSettings
{
    /// <summary>E.g. <c>https://www.intranetssl.rai.it</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Path of the sign-in policy's login script, e.g. <c>/dana-na/auth/url_56/login.cgi</c>.</summary>
    public string? LoginPath { get; set; }

    public string? Realm { get; set; }

    /// <summary>The token in the portal's rewritten addresses: <c>/,DanaInfo=.&lt;token&gt;+Page.aspx</c>.</summary>
    public string? DanaInfo { get; set; }
}

public sealed class AppSettings
{
    public CompanyProfile Company { get; set; } = new();
    public LineDefaults LineDefaults { get; set; } = new();

    /// <summary>RAI portal user name. The password is not stored here, see <see cref="ICredentialStore"/>.</summary>
    public string RaiUsername { get; set; } = "";

    public RaiPortalSettings RaiPortal { get; set; } = new();

    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Also offer pre-release versions.</summary>
    public bool IncludePrereleases { get; set; }

    /// <summary>Folder the "add files" dialog opened last.</summary>
    public string? LastImportFolder { get; set; }

    /// <summary>Folder a DCP or document was last saved to or opened from.</summary>
    public string? LastDocumentFolder { get; set; }

    /// <summary>Most recent first.</summary>
    public List<string> RecentFiles { get; set; } = [];

    public const int MaxRecentFiles = 10;

    public void AddRecentFile(string path)
    {
        var full = Path.GetFullPath(path);
        RecentFiles.RemoveAll(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, full);
        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }
}

/// <summary>
/// Keeps the RAI password in the operating system's protected storage (Windows: DPAPI, macOS: Keychain)
/// instead of a settings file.
/// </summary>
public interface ICredentialStore
{
    /// <summary>False when no protected storage is available, so the password only lives as long as the app runs.</summary>
    bool IsPersistent { get; }

    string? LoadPassword(string username);
    void SavePassword(string username, string password);
    void DeletePassword(string username);
}
