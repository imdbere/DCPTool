namespace DcpTool.Core.Settings;

/// <summary>
/// Where the app keeps its own files. Everything lives under one per-user folder:
/// <c>%AppData%\DcpTool</c> on Windows, <c>~/Library/Application Support/DcpTool</c> on macOS.
/// Never the installation folder (it is replaced on every update) and never the working directory.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "DcpTool";

    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
            AppFolderName);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>The DCP that was open when the app crashed or was killed, see <c>RecoveryStore</c>.</summary>
    public string RecoveryFolder => Path.Combine(Root, "recovery");

    /// <summary>
    /// Originals of library files written by DCP Tool 0.x, copied here (under their path below the library) before
    /// they are first saved in the new format, which DCP Tool 0.x cannot read.
    /// </summary>
    public string LegacyBackupFolder => Path.Combine(Root, "legacy-backups");

    public string LogFolder => Path.Combine(Root, "logs");

    /// <summary>The user's replacement for the built-in Word template of Allegato B; absent when the built-in one is used.</summary>
    public string DocumentTemplateFile => Path.Combine(Root, "templates", "allegato-b.docx");

    /// <summary>Protected password file of the Windows credential store.</summary>
    public string CredentialsFile => Path.Combine(Root, "credentials.bin");
}
