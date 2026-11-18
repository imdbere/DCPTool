using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DcpTool.App.Localisation;
using DcpTool.App.Services;
using DcpTool.App.Views;
using DcpTool.Core.Export;
using DcpTool.Core.Import;
using DcpTool.Core.Library;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;
using DcpTool.Core.Rai;
using DcpTool.Core.Settings;

namespace DcpTool.App.ViewModels;

/// <summary>
/// Everything the main window shows: one DCP and its songs, the drawer (presets and recent DCPs), the guide, the
/// export page and the settings page. Split over several files by area: Library (drawer, leaving a DCP),
/// Pages (what the middle shows, the two areas at the bottom), Export (RAI, documents, the console), Settings, Guide.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly RecoveryStore _recovery;
    private readonly IImportService _importService;
    private readonly IRaiClient _rai;
    private readonly IDocumentExporter _exporter;
    private readonly ICredentialStore _credentials;
    private readonly UpdateService _updates;
    private readonly Dialogs _dialogs;
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer _statusTimer = new();
    private bool _loading;
    private Dcp _dcp = new();

    public MainViewModel(
        AppSettings settings,
        SettingsStore settingsStore,
        RecoveryStore recovery,
        IImportService importService,
        IRaiClient rai,
        IDocumentExporter exporter,
        ICredentialStore credentials,
        UpdateService updates,
        Dialogs dialogs)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _recovery = recovery;
        _importService = importService;
        _rai = rai;
        _exporter = exporter;
        _credentials = credentials;
        _updates = updates;
        _dialogs = dialogs;
        _header = new DcpHeader(_dcp, OnChanged);
        _library = CreateLibrary(settings.LibraryFolder);
        Settings = new SettingsPageViewModel(this, settings, credentials);
        Guide = new GuideViewModel();
        _recoveryTimer.Tick += (_, _) => WriteRecovery();
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText = "";
        };
    }

    /// <summary>
    /// Set by the view: pushes an edit still pending in the table or in a text box into the model. Called
    /// before saving, checking, exporting and uploading, so a shortcut pressed while typing loses nothing.
    /// </summary>
    public Action? CommitPendingEdits { get; set; }

    /// <summary>Set by the view: puts the cursor into the broadcast date, the one field a new episode needs first.</summary>
    public Action? FocusBroadcastDate { get; set; }

    public ObservableCollection<LineRow> Lines { get; } = [];

    public SettingsPageViewModel Settings { get; }
    public GuideViewModel Guide { get; }

    [ObservableProperty] private DcpHeader _header;
    [ObservableProperty] private LineRow? _selectedLine;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _updateReady;

    /// <summary>The last thing that happened, shown for a few seconds below the table. Everything is also in the console.</summary>
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusIsProblem;

    private string DocumentName => FilePath is null ? Loc.Get("Doc_Untitled") : Path.GetFileName(FilePath);

    public string WindowTitle => $"{DocumentName}{(IsDirty ? "*" : "")} – DCP Tool";

    public bool HasLines => Lines.Count > 0;

    /// <summary>"8 Titel · Musik gesamt 09:36" below the table.</summary>
    public string SongsSummary => Loc.Format(Lines.Count == 1 ? "Songs_SummaryOne" : "Songs_Summary",
        Lines.Count, TimeText.FormatDuration(_dcp.DurataMusica) is { Length: > 0 } total ? total : "00:00");

    public bool CanImportPath(string path) => _importService.CanImport(path);

    /// <summary>Folder of the activity log files. Set once at start.</summary>
    public string? ActivityLogFolder { get; set; }

    partial void OnFilePathChanged(string? value)
    {
        OnFileStateChanged();
        RebuildRecent();
    }

    partial void OnIsDirtyChanged(bool value) => OnFileStateChanged();

    private void OnFileStateChanged() => OnPropertyChanged(nameof(WindowTitle));

    // ---------------------------------------------------------------- status and the activity log

    /// <summary>A status text in the UI language; the activity log file gets it in English.</summary>
    private void SetStatus(string key, params object?[] args) => Report(ConsoleLevel.Info, key, args);

    private void Report(ConsoleLevel level, string key, params object?[] args) =>
        ReportText(level, Loc.Format(key, args), Loc.English(key, args));

    /// <summary>
    /// Adds a line to the console of the export page and to the daily activity log file, and shows it below the
    /// table for a while. <paramref name="english"/>: the text for the log file, null when it is the same in every language.
    /// <paramref name="isCheck"/>: a result of the check, replaced by the next check and not shown below the table.
    /// <paramref name="quiet"/>: only for the console, not shown below the table.
    /// </summary>
    private void ReportText(ConsoleLevel level, string text, string? english = null, bool isCheck = false, bool quiet = false)
    {
        var now = DateTime.Now;
        ConsoleLines.Add(new ConsoleLine(now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), level, text, isCheck));
        while (ConsoleLines.Count > 500)
        {
            ConsoleLines.RemoveAt(0);
        }

        OnConsoleChanged();
        WriteActivityLog(now, level, english ?? text);
        if (!isCheck && !quiet)
        {
            StatusText = text;
            StatusIsProblem = level != ConsoleLevel.Info;
            _statusTimer.Stop();
            _statusTimer.Interval = TimeSpan.FromSeconds(level == ConsoleLevel.Info ? 8 : 20);
            _statusTimer.Start();
        }
    }

    private void WriteActivityLog(DateTime time, ConsoleLevel level, string text)
    {
        if (ActivityLogFolder is not { } folder)
        {
            return;
        }

        try
        {
            var prefix = level switch { ConsoleLevel.Error => "ERROR ", ConsoleLevel.Warning => "WARN  ", _ => "" };
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, $"activity-{time:yyyyMMdd}.log"),
                $"{time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}  {prefix}{text}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is a convenience and must never get in the way of the work
        }
    }

    // ---------------------------------------------------------------- document state

    private void LoadDcp(Dcp dcp, string? path, bool dirty)
    {
        _loading = true;
        try
        {
            _dcp = dcp;
            Header = new DcpHeader(dcp, OnChanged);
            Lines.Clear();
            foreach (var line in dcp.Lines)
            {
                Lines.Add(new LineRow(line, OnChanged));
            }

            Renumber();
            SelectedLine = null;
            FilePath = path;
            IsDirty = dirty;
        }
        finally
        {
            _loading = false;
        }

        // What the console and the Add area said was about the previous DCP
        ConsoleLines.Clear();
        _uploadFailure = null;
        _lastExport = null;
        _lastCheck = null;
        ClearAddNotice();
        OnConsoleChanged();
        OnLinesChanged();
        OnUploadStateChanged();
        OnDocumentExportChanged();
        RebuildRecent();
        ScheduleRecovery();
    }

    private void Renumber()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].Number = i + 1;
        }
    }

    private void SyncLines()
    {
        _dcp.Lines = Lines.Select(l => l.Line).ToList();
    }

    /// <summary>Called for every edit: header, line cell, or list structure.</summary>
    private void OnChanged()
    {
        if (_loading)
        {
            return;
        }

        SyncLines();
        IsDirty = true;
        ClearAddNotice();
        _lastCheck = null;
        OnLinesChanged();
        OnUploadStateChanged();
        UpdateCurrentRecent();
        ScheduleRecovery();
    }

    private void OnLinesChanged()
    {
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(SongsSummary));
    }

    private void ScheduleRecovery()
    {
        _recoveryTimer.Stop();
        _recoveryTimer.Start();
    }

    /// <summary>Writes the recovery snapshot now: what a crash or a killed process would otherwise lose. Never throws.</summary>
    public void WriteRecovery()
    {
        try
        {
            _recoveryTimer.Stop();
            SyncLines();
            _recovery.Write(_dcp, FilePath, IsDirty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(ConsoleLevel.Warning, "Status_RecoveryFailed", ex.Message);
        }
    }

    /// <summary>
    /// Removes the recovery snapshot: the window closed normally or the user discarded the changes, so the next start
    /// must not bring them back.
    /// </summary>
    private void DiscardRecovery()
    {
        _recoveryTimer.Stop();
        _recovery.Clear();
    }

    // ---------------------------------------------------------------- startup / shutdown

    public async Task InitializeAsync(string[] args)
    {
        try
        {
            await InitializeDocumentAsync(args);
        }
        finally
        {
            _initialized = true;
            ShowWelcomeOnFirstStart();
            _ = RefreshLibraryAsync();
        }
    }

    /// <summary>The very first start opens with the guide on its welcome; later starts open as usual.</summary>
    private void ShowWelcomeOnFirstStart()
    {
        if (_settings.WelcomeShown)
        {
            return;
        }

        Guide.ShowWelcome();
        Page = AppPage.Guide;
        _settings.WelcomeShown = true;
        SaveSettings();
    }

    private async Task InitializeDocumentAsync(string[] args)
    {
        // A .dcp passed on the command line: double-clicked in Explorer
        var arg = args.FirstOrDefault(a => a.EndsWith(DcpFile.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(a));

        // A snapshot is only left after a crash or a killed process: a normal close removes it
        var session = _recovery.TryRead();
        if (arg is not null)
        {
            // Opening the file would overwrite the snapshot, the only copy of unsaved work from last time
            var keep = session is { HasUnsavedChanges: true } && !await _dialogs.Confirm(
                Loc.Get("Dlg_UnsavedChanges"),
                Loc.Format("Msg_UnsavedSession", Path.GetFileName(arg)),
                Loc.Get("Dlg_OpenFile"),
                Loc.Get("Dlg_KeepUnsaved"));
            if (!keep && await TryOpenAsync(arg))
            {
                return;
            }
        }

        if (session is not null)
        {
            await RestoreSessionAsync(session);
            return;
        }

        LoadDcp(_settings.Company.CreateDcp(), null, false);
        IsDirty = false;
    }

    private async Task RestoreSessionAsync(RecoveredSession session)
    {
        var file = session.FilePath;
        var fileExists = file is not null && File.Exists(file);

        // Nothing unsaved: the file is the truth, it may have been changed outside the app since
        if (!session.HasUnsavedChanges && fileExists && await TryOpenAsync(file!, quiet: true))
        {
            return;
        }

        // An untouched new DCP is nothing to ask about later
        var unsaved = session.HasUnsavedChanges && (file is not null || HasContent(session.Dcp));
        LoadDcp(session.Dcp, file, unsaved);
        if (unsaved)
        {
            Report(ConsoleLevel.Warning, "Status_Recovered");
        }

        if (fileExists)
        {
            // A file newer than the snapshot was changed by something else: reported, and our version is kept
            _lastWriteUtc = session.SavedAt.UtcDateTime;
            CheckExternalChange();
        }
    }

    /// <summary>Check for updates in the background, if enabled. Never blocks, never throws.</summary>
    public async Task StartUpdateCheckAsync()
    {
        if (!_settings.CheckForUpdates)
        {
            return;
        }

        var outcome = await Task.Run(_updates.CheckAndDownloadAsync);
        if (outcome == UpdateCheckOutcome.Downloaded)
        {
            UpdateReady = true;
        }
    }

    /// <summary>Restarts into the downloaded update, after the open DCP's unsaved changes were saved or discarded.</summary>
    [RelayCommand]
    private async Task RestartForUpdate()
    {
        if (IsBusy || !await LeaveDocumentAsync())
        {
            return;
        }

        DiscardRecovery();
        _updates.ApplyAndRestart();
    }

    /// <summary>"Jetzt suchen" in the settings. Returns the result in words.</summary>
    internal async Task<string> CheckForUpdatesAsync()
    {
        var outcome = await Task.Run(_updates.CheckAndDownloadAsync);
        switch (outcome)
        {
            case UpdateCheckOutcome.Downloaded:
                UpdateReady = true;
                return Loc.Format("Msg_UpdateDownloaded", _updates.ReadyVersion, Loc.Get("Update_Restart"));
            case UpdateCheckOutcome.UpToDate:
                return Loc.Get("Msg_UpToDate");
            case UpdateCheckOutcome.NotInstalled:
                return Loc.Get("Msg_NotInstalled");
            default:
                return Loc.Format("Msg_UpdateFailed", _updates.LastError);
        }
    }

    /// <summary>
    /// The window may close: false when the open DCP has unsaved changes and the user cancelled the prompt
    /// (Speichern / Nicht speichern / Abbrechen).
    /// </summary>
    public Task<bool> ConfirmCloseAsync() => LeaveDocumentAsync();

    /// <param name="normal">
    /// True for a normal close (after <see cref="ConfirmCloseAsync"/>): the recovery snapshot is removed, so the next start
    /// is a fresh one. False when Windows shuts down and nobody could be asked: the snapshot keeps the work.
    /// </param>
    public void Shutdown(bool normal)
    {
        try
        {
            CommitPendingEdits?.Invoke(); // a cell still being edited belongs into the snapshot
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "shutdown");
        }

        if (normal)
        {
            DiscardRecovery();
        }
        else
        {
            WriteRecovery();
        }

        (_rai as IDisposable)?.Dispose();
    }

    // ---------------------------------------------------------------- file commands

    /// <summary>A new DCP without a programme (Ctrl+N, or "Ohne Sendung beginnen" in the drawer).</summary>
    [RelayCommand]
    private async Task NewDcp()
    {
        if (IsBusy || !await LeaveDocumentAsync())
        {
            return;
        }

        StartNewDocument(_settings.Company.CreateDcp(), dirty: false, "Status_NewDcp");
        Page = AppPage.Songs;
        IsStripExpanded = true;
    }

    /// <summary>Ctrl+O. Older DCPs are usually opened from Explorer: double-clicked or dropped onto the window.</summary>
    [RelayCommand]
    private async Task Open()
    {
        if (IsBusy)
        {
            return;
        }

        var path = await _dialogs.PickOpenDcp(_settings.LastDocumentFolder ?? _library?.Folder);
        if (path is not null)
        {
            await OpenFileAsync(path);
        }
    }

    /// <summary>Opens a .dcp dropped onto the window or picked; unsaved changes of the open DCP are asked about first.</summary>
    public async Task OpenFileAsync(string path)
    {
        if (IsBusy)
        {
            return;
        }

        // The open file again, with nothing unsaved: nothing to do
        if (!IsDirty && SamePath(path, FilePath))
        {
            Page = AppPage.Songs;
            return;
        }

        if (await LeaveDocumentAsync() && await TryOpenAsync(path))
        {
            Page = AppPage.Songs;
            _ = RefreshLibraryAsync();
        }
    }

    /// <param name="quiet">Problems go to the status text instead of a message box (drawer, session restore).</param>
    private async Task<bool> TryOpenAsync(string path, bool quiet = false)
    {
        try
        {
            var content = DcpFile.Load(path);
            var inLibrary = _library?.Contains(path) == true;

            // An older file is written in the new format only when the user saves it
            LoadDcp(content.Dcp, path, false);
            _lastWriteUtc = LastWriteUtc(path);
            RememberFile(path);

            if (content.IsLegacy)
            {
                if (quiet || inLibrary)
                {
                    SetStatus("Status_OlderFormat", Path.GetFileName(path));
                }
                else
                {
                    await _dialogs.Message(Loc.Get("Dlg_OlderFormat"), Loc.Get("Msg_OlderFormat"));
                }
            }

            return true;
        }
        catch (DcpFileException ex)
        {
            // The file layer's messages are English; the sentence around them is not
            if (quiet)
            {
                Report(ConsoleLevel.Error, "Status_CannotOpen", ex.Message);
            }
            else
            {
                await _dialogs.Message(Loc.Get("Common_Open"), Loc.Format("Status_CannotOpen", ex.Message));
            }

            return false;
        }
    }

    /// <summary>
    /// Speichern (Ctrl+S): to the DCP's file; a DCP that was never saved goes into the library as
    /// <c>&lt;Sendung&gt;/&lt;year&gt;/&lt;Sendung&gt; &lt;Puntata&gt; &lt;date&gt;.dcp</c> without a dialog, or, without a
    /// library, to the file the user picks.
    /// </summary>
    [RelayCommand]
    private async Task Save() => await SaveDocumentAsync();

    /// <summary>Speichern unter… (Ctrl+Shift+S): always asks for the file.</summary>
    [RelayCommand]
    private async Task SaveAs() => await SaveAsAsync();

    /// <summary>True when the DCP is in its file now.</summary>
    private async Task<bool> SaveDocumentAsync()
    {
        CommitPendingEdits?.Invoke();
        SyncLines();
        var plan = SavePlan.For(FilePath, _library, _dcp);
        return plan.Kind == SaveKind.AskForPath ? await SaveAsAsync() : SaveTo(plan.Path!);
    }

    /// <param name="afterUpload">See <see cref="SaveTo"/>.</param>
    private async Task<bool> SaveAsAsync(bool afterUpload = false)
    {
        CommitPendingEdits?.Invoke();
        SyncLines();
        string name;
        string? folder;
        if (FilePath is { } current)
        {
            name = Path.GetFileNameWithoutExtension(current);
            folder = Path.GetDirectoryName(current);
        }
        else
        {
            name = DcpLibrary.SuggestName(_dcp);
            folder = _library is { } library ? ExistingFolder(library.SuggestFolder(_dcp), library.Folder) : _settings.LastDocumentFolder;
        }

        var path = await _dialogs.PickSave(Loc.Get("Picker_SaveDcp"), name, DcpFile.Extension, Loc.Get("Picker_DcpFile"), folder);
        return path is not null && SaveTo(path, afterUpload);
    }

    /// <summary>The suggested folder when it exists already, else the nearest folder above it, up to <paramref name="root"/>.</summary>
    private static string ExistingFolder(string folder, string root)
    {
        for (var dir = folder; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            if (Directory.Exists(dir) || SamePath(dir, root))
            {
                return dir;
            }
        }

        return root;
    }

    /// <param name="afterUpload">
    /// The save right after an upload: the console says that the upload record is in the file, and a failure goes to
    /// the console instead of a message box, because the caller asks for another file next.
    /// </param>
    private bool SaveTo(string path, bool afterUpload = false)
    {
        CommitPendingEdits?.Invoke();
        if (!KeepLegacyOriginal(path, out var original))
        {
            return false;
        }

        try
        {
            SyncLines();
            DcpFile.Save(path, _dcp);
            ReportLegacyConverted(true, path, original);
            _lastWriteUtc = LastWriteUtc(path);
            FilePath = path;
            IsDirty = false;
            RememberFile(path);
            WriteRecovery();
            if (afterUpload)
            {
                SetStatus("Status_UploadSaved", RelativeName(path));
            }
            else if (original is null)
            {
                SetStatus("Status_Saved", RelativeName(path));
            }

            _ = RefreshLibraryAsync();
            return true;
        }
        catch (DcpFileException ex)
        {
            if (afterUpload)
            {
                Report(ConsoleLevel.Error, "Msg_SaveFailed", ex.Message);
            }
            else
            {
                _ = _dialogs.Message(Loc.Get("Common_Save"), Loc.Format("Msg_SaveFailed", ex.Message));
            }

            return false;
        }
    }

    private void RememberFile(string path)
    {
        _settings.AddRecentFile(path);
        _settings.LastDocumentFolder = Path.GetDirectoryName(Path.GetFullPath(path));
        SaveSettings();
    }

    internal void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(ConsoleLevel.Warning, "Status_SettingsNotSaved", ex.Message);
        }
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // ---------------------------------------------------------------- songs

    /// <summary>"Hinzufügen…", or a click on the Add area: the file dialog.</summary>
    [RelayCommand]
    private async Task AddMusicFiles()
    {
        if (IsBusy)
        {
            return;
        }

        var files = await _dialogs.PickMusicFiles(_settings.LastImportFolder, _importService.FilePatterns);
        if (files.Count > 0)
        {
            _settings.LastImportFolder = Path.GetDirectoryName(files[0]);
            SaveSettings();
            await ImportAsync(files);
        }
    }

    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy || paths.Count == 0)
        {
            return;
        }

        IsBusy = true;
        Page = AppPage.Songs;
        SetAddState(AddState.Importing, Loc.Get("Add_Importing"));
        var before = Lines.ToList();
        try
        {
            // Copies with the same Ids: the import runs on another thread and must not read the live model
            var existing = Lines.Select(l =>
            {
                var copy = l.Line.Clone();
                copy.Id = l.Line.Id;
                return copy;
            }).ToList();
            var request = new ImportRequest(paths, existing, _settings.LineDefaults);
            var progress = new Progress<ImportProgress>(p => AddNotice = Loc.Format("Add_ImportProgress", p.Done, p.Total));
            var result = await Task.Run(() => _importService.Import(request, progress));

            _loading = true;
            try
            {
                foreach (var done in result.CompletedLines)
                {
                    var index = IndexOfLine(done.Id);
                    if (index >= 0)
                    {
                        var wasSelected = SelectedLine == Lines[index];
                        Lines[index] = new LineRow(done, OnChanged);
                        if (wasSelected)
                        {
                            SelectedLine = Lines[index];
                        }
                    }
                }

                foreach (var line in result.NewLines)
                {
                    Lines.Add(new LineRow(line, OnChanged));
                }

                Renumber();
            }
            finally
            {
                _loading = false;
            }

            if (result.NewLines.Count > 0 || result.CompletedLines.Count > 0)
            {
                OnChanged();
            }

            ReportImport(result, paths, before);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "import");
            SetAddState(AddState.Idle, "");
            await _dialogs.Message(Loc.Get("Dlg_Import"), Loc.Format("Msg_ImportFailed", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>What the import did goes into the Add area (with undo, or "Warum?" when something was left out) and the console.</summary>
    private void ReportImport(ImportResult result, IReadOnlyList<string> paths, List<LineRow> before)
    {
        var added = result.NewLines.Count;
        var completed = result.CompletedLines.Count;
        foreach (var m in result.Messages)
        {
            var level = m.Severity switch
            {
                ImportSeverity.Error => ConsoleLevel.Error,
                ImportSeverity.Warning => ConsoleLevel.Warning,
                _ => ConsoleLevel.Info,
            };
            ReportText(level, MessageText.Import(m), m.File is null ? m.Text : $"{m.File}: {m.Text}", quiet: true);
        }

        // Files that gave no line at all, and files whose line lacks something
        static bool LeftOut(ImportMessage m) =>
            m.Severity == ImportSeverity.Error || m.Code is "file-not-found" or "unsupported-file-type" or "no-source" or "audio-unreadable" or "read-failed";
        var failed = result.Messages.Where(LeftOut).Select(m => m.File ?? m.Text).Distinct().Count();
        var incomplete = result.Messages.Where(m => m.Severity == ImportSeverity.Warning && !LeftOut(m)).Select(m => m.File ?? m.Text).Distinct().Count();
        Guide.SetImportProblems(result.Messages.Where(m => m.Severity != ImportSeverity.Info).Select(MessageText.Import).ToList(),
            result.Messages.Any(m => m.Code == "artlist-cue-sheet-missing") ? GuideWay.ArtlistId : GuideWay.OtherId);

        if (failed > 0)
        {
            SetAddState(AddState.Partial, Loc.Format("Add_Partial", added, added + failed, failed));
            Report(ConsoleLevel.Warning, "Status_ImportedPartly", added, added + failed);
        }
        else if (incomplete > 0)
        {
            SetAddState(AddState.Partial, Loc.Format("Add_Incomplete", added + completed, incomplete));
            Report(ConsoleLevel.Warning, "Status_ImportedIncomplete", added + completed, incomplete);
        }
        else if (added + completed > 0)
        {
            _undoLines = before;
            var single = paths.Count == 1 ? Path.GetFileName(paths[0]) : null;
            var shortName = single is null ? null : ShortFileName(single);
            var text = (completed, shortName) switch
            {
                ( > 0, _) => Loc.Format("Add_AddedCompleted", added, completed),
                (_, not null) => Loc.Format(added == 1 ? "Add_AddedOneFrom" : "Add_AddedFrom", added, shortName),
                _ => Loc.Format(added == 1 ? "Add_AddedOne" : "Add_Added", added),
            };

            // The full name as a tip, when the notice shows it shortened
            var tip = completed == 0 && single is not null && shortName != single
                ? Loc.Format(added == 1 ? "Add_AddedOneFrom" : "Add_AddedFrom", added, single)
                : null;
            SetAddState(AddState.Added, text, tip);
            SetStatus(completed > 0 ? "Status_ImportedCompleted" : "Status_Imported", added, completed);
        }
        else
        {
            ClearAddNotice();
            Report(ConsoleLevel.Warning, "Status_ImportedNothing");
        }
    }

    /// <summary>"Rückgängig" in the Add area: the songs as they were before the last import.</summary>
    [RelayCommand]
    private void UndoImport()
    {
        if (_undoLines is not { } before || IsBusy)
        {
            return;
        }

        CommitPendingEdits?.Invoke();
        _loading = true;
        try
        {
            Lines.Clear();
            foreach (var row in before)
            {
                Lines.Add(row);
            }

            Renumber();
        }
        finally
        {
            _loading = false;
        }

        OnChanged();
        SetStatus("Status_ImportUndone");
    }

    private int IndexOfLine(Guid id)
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Line.Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void Insert(int index, DcpLine line)
    {
        var row = new LineRow(line, OnChanged);
        Lines.Insert(Math.Clamp(index, 0, Lines.Count), row);
        Renumber();
        OnChanged();
        SelectedLine = row;
    }

    /// <summary>An empty song, for music that has no file.</summary>
    [RelayCommand]
    private void AddLine()
    {
        var line = new DcpLine();
        _settings.LineDefaults.ApplyTo(line);
        Insert(SelectedLine is { } s ? Lines.IndexOf(s) + 1 : Lines.Count, line);
    }

    [RelayCommand]
    private void DuplicateLine()
    {
        if (SelectedLine is { } s)
        {
            Insert(Lines.IndexOf(s) + 1, s.Line.Clone());
        }
    }

    [RelayCommand]
    private void DeleteLine()
    {
        if (SelectedLine is not { } s)
        {
            return;
        }

        var index = Lines.IndexOf(s);
        Lines.RemoveAt(index);
        Renumber();
        OnChanged();
        SelectedLine = Lines.Count == 0 ? null : Lines[Math.Min(index, Lines.Count - 1)];
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedLine is { } s)
        {
            var from = Lines.IndexOf(s);
            MoveLine(from, from + delta);
        }
    }

    /// <summary>Moves a song to another position (row menu, Alt+arrow, or dragged by its number).</summary>
    public void MoveLine(int from, int to)
    {
        if (from < 0 || from >= Lines.Count || to < 0 || to >= Lines.Count || from == to)
        {
            return;
        }

        // Remove and insert: the table does not redraw for a Move notification
        var row = Lines[from];
        _loading = true;
        try
        {
            Lines.RemoveAt(from);
            Lines.Insert(to, row);
        }
        finally
        {
            _loading = false;
        }

        Renumber();
        OnChanged();
        SelectedLine = row;
    }

    public static void OpenWithShell(string pathOrUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing registered for the file type: the file is still there
        }
    }
}
