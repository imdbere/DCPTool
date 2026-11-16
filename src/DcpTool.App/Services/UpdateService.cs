using Velopack;
using Velopack.Sources;

namespace DcpTool.App.Services;

public enum UpdateCheckOutcome
{
    NotInstalled,
    UpToDate,
    Downloaded,
    Failed,
}

/// <summary>Velopack updates from the GitHub releases. Never throws: every failure is an outcome.</summary>
public sealed class UpdateService(bool includePrereleases)
{
    // A public repository that holds only the installers and update packages. The source repository is
    // private, and an app can't read a private repository's releases without shipping a token.
    // Keep in sync with RELEASES_REPO in .github/workflows/v2.yml. See doc/releasing.md.
    private const string RepoUrl = "https://github.com/TheDiffi/DCPTool-releases";
    private UpdateManager? _manager;
    private UpdateInfo? _ready;

    public bool IsInstalled
    {
        get
        {
            try
            {
                return Manager.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool UpdateReady => _ready is not null;
    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();
    public string? LastError { get; private set; }

    private UpdateManager Manager => _manager ??= new UpdateManager(new GithubSource(RepoUrl, null, includePrereleases));

    /// <summary>Checks and, if there is a newer version, downloads it in the background.</summary>
    public async Task<UpdateCheckOutcome> CheckAndDownloadAsync()
    {
        try
        {
            if (!Manager.IsInstalled)
            {
                return UpdateCheckOutcome.NotInstalled;
            }

            var info = await Manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                return UpdateCheckOutcome.UpToDate;
            }

            await Manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _ready = info;
            return UpdateCheckOutcome.Downloaded;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return UpdateCheckOutcome.Failed;
        }
    }

    /// <summary>Applies the downloaded update and restarts the app. Does not return when it works.</summary>
    public void ApplyAndRestart()
    {
        if (_ready is not null)
        {
            Manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
        }
    }
}
