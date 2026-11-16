using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DcpTool.App.Services;
using DcpTool.App.ViewModels;
using DcpTool.App.Views;
using DcpTool.Core.Persistence;
using DcpTool.Core.Settings;
using DcpTool.Export;
using DcpTool.Rai;
using DcpTool.Sources;

namespace DcpTool.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;
    private Dialogs? _dialogs;
    private bool _showingCrash;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Plain wiring: no container
            var paths = new AppPaths();
            var settingsStore = new SettingsStore(paths);
            var settings = settingsStore.Load();
            ICredentialStore credentials = OperatingSystem.IsWindows()
                ? new DpapiCredentialStore(paths)
                : new MemoryCredentialStore();

            // First start after DCP Tool 0.x: take over its RAI login. The password moves from v1's
            // plain-text settings into the protected store; v1's own file is left as it is.
            if (settings.RaiUsername.Length == 0 && LegacySettings.TryReadLogin() is { } old)
            {
                settings.RaiUsername = old.Username;
                if (old.Password.Length > 0 && credentials.IsPersistent)
                {
                    credentials.SavePassword(old.Username, old.Password);
                }

                settingsStore.Save(settings);
            }
            _dialogs = new Dialogs();
            _viewModel = new MainViewModel(
                settings,
                settingsStore,
                new RecoveryStore(paths),
                SourcesRegistration.CreateDefault(),
                new RaiClient(RaiPortalOptions.FromSettings(settings.RaiPortal)),
                new DocxExporter(),
                credentials,
                new UpdateService(settings.IncludePrereleases),
                _dialogs)
            {
                ActivityLogFolder = paths.LogFolder,
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) => OnUnhandled(e.ExceptionObject as Exception, "domain");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                e.SetObserved();
                OnUnhandled(e.Exception, "task");
            };
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                OnUnhandled(e.Exception, "ui");
            };

            var window = new MainWindow { DataContext = _viewModel };
            _dialogs.Owner = window;
            desktop.MainWindow = window;
            var args = desktop.Args ?? [];
            window.Opened += async (_, _) =>
            {
                try
                {
                    await _viewModel.InitializeAsync(args);
                    await _viewModel.StartUpdateCheckAsync();
                }
                catch (Exception ex)
                {
                    OnUnhandled(ex, "startup");
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnUnhandled(Exception? ex, string where)
    {
        if (ex is null)
        {
            return;
        }

        var file = CrashLog.Write(ex, where);
        try
        {
            // The recovery snapshot is the safety net: refresh it, but never let that fail loudly
            _viewModel?.WriteRecovery();
        }
        catch
        {
        }

        if (_showingCrash || _dialogs is null)
        {
            return;
        }

        _showingCrash = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await _dialogs.Message(
                    "Unexpected error",
                    $"Something went wrong: {ex.Message}\n\nYour work is kept and restored on the next start." + (file is null ? "" : $"\nDetails: {file}"));
            }
            catch
            {
            }
            finally
            {
                _showingCrash = false;
            }
        });
    }
}
