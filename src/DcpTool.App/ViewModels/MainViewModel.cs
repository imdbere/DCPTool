using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DcpTool.App.Localisation;
using DcpTool.App.Services;
using DcpTool.App.Views;
using DcpTool.Core.Export;
using DcpTool.Core.Import;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;
using DcpTool.Core.Rai;
using DcpTool.Core.Settings;
using DcpTool.Core.Validation;

namespace DcpTool.App.ViewModels;

/// <summary>Everything the main window shows: one DCP, its lines, issues and the actions on them.</summary>
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
    private readonly List<ImportMessage> _importMessages = [];

    // What the last upload reported. Shown in the issues list until the next upload.
    private readonly List<IssueItem> _uploadMessages = [];
    private bool _loading;
    private CancellationTokenSource? _operation;
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
        _recoveryTimer.Tick += (_, _) =>
        {
            _recoveryTimer.Stop();
            // A library document goes to its file first, so the snapshot then records it as saved
            SaveLibraryDocument(leaving: false);
            WriteRecovery();
        };
    }

    /// <summary>
    /// Set by the view: pushes an edit still pending in the grid or in a text box into the model. Called
    /// before saving, checking, exporting and uploading, so a shortcut pressed while typing loses nothing.
    /// </summary>
    public Action? CommitPendingEdits { get; set; }

    public ObservableCollection<LineRow> Lines { get; } = [];
    public ObservableCollection<IssueItem> Issues { get; } = [];

    [ObservableProperty] private DcpHeader _header;
    [ObservableProperty] private LineRow? _selectedLine;
    [ObservableProperty] private IssueItem? _selectedIssue;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _updateReady;
    [ObservableProperty] private string _loginText = Loc.Get("Login_NotLoggedIn");

    // The English twin of the status text being set, for the activity log file (see SetStatus)
    private string? _statusEnglish;

    public string[] RecentFiles => [.. _settings.RecentFiles];

    private string DocumentName => FilePath is null ? Loc.Get("Doc_Untitled") : Path.GetFileName(FilePath);

    public string FileText
    {
        get
        {
            if (IsLibraryDocument)
            {
                return Loc.Format("Doc_InLibrary", DocumentName);
            }

            if (IsPendingLibraryDocument)
            {
                return Loc.Format("Doc_PendingLibrary", DocumentName);
            }

            return IsDirty ? Loc.Format("Doc_Modified", DocumentName) : DocumentName;
        }
    }

    public string WindowTitle => $"{DocumentName}{(IsDirty && !IsAutoSaved ? "*" : "")} – DCP Tool";

    /// <summary>Header of the issues tab: the counts, or that there is nothing.</summary>
    public string IssuesHeader
    {
        get
        {
            var errors = Issues.Count(i => i.Level == IssueLevel.Error);
            var warnings = Issues.Count(i => i.Level == IssueLevel.Warning);
            return Loc.Format("Issues_Tab", Issues.Count == 0
                ? Loc.Get("Issues_None")
                : Loc.Format("Issues_Counts", errors, warnings, Issues.Count - errors - warnings));
        }
    }

    /// <summary>A validation issue in the UI language.</summary>
    private string IssueText(ValidationIssue issue) =>
        MessageText.Issue(issue, id => _dcp.Lines.FirstOrDefault(l => l.Id == id)?.Titolo);

    /// <summary>Shows a status text in the UI language; the activity log file gets it in English.</summary>
    private void SetStatus(string key, params object?[] args) => SetStatusText(Loc.Format(key, args), Loc.English(key, args));

    /// <param name="english">The text for the log file; null when <paramref name="text"/> is the same in every language.</param>
    private void SetStatusText(string text, string? english = null)
    {
        _statusEnglish = english;
        ProgressText = text;
        _statusEnglish = null;
    }

    public IReadOnlyList<string> ImportPatterns => _importService.FilePatterns;
    public bool CanImportPath(string path) => _importService.CanImport(path);

    /// <summary>What the app did, newest last. Also written to a daily file in the log folder.</summary>
    public ObservableCollection<string> Activity { get; } = [];

    /// <summary>Folder of the activity log files. Set once at start.</summary>
    public string? ActivityLogFolder { get; set; }

    // Every status text is an event worth keeping: import results, login, each upload step, failures.
    // The Log tab shows it in the UI language, the file keeps English.
    partial void OnProgressTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var time = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        Activity.Add($"{time}  {value}");
        var entry = $"{time}  {_statusEnglish ?? value}";
        while (Activity.Count > 500)
        {
            Activity.RemoveAt(0);
        }

        if (ActivityLogFolder is { } folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"activity-{DateTime.Now:yyyyMMdd}.log"), entry + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The log is a convenience and must never get in the way of the work
            }
        }
    }

    partial void OnFilePathChanged(string? value)
    {
        OnFileStateChanged();
        UpdateOpenRows();
    }

    partial void OnIsDirtyChanged(bool value) => OnFileStateChanged();

    private void OnFileStateChanged()
    {
        OnPropertyChanged(nameof(FileText));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnSelectedIssueChanged(IssueItem? value)
    {
        if (value?.LineId is { } id)
        {
            SelectedLine = Lines.FirstOrDefault(l => l.Line.Id == id);
        }
    }

    // ---------------------------------------------------------------- document state

    private void LoadDcp(Dcp dcp, string? path, bool dirty)
    {
        _loading = true;
        _newFileFolder = null;
        _nameFollowsHeader = false;
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
            _importMessages.Clear();
            _uploadMessages.Clear();
            SelectedLine = null;
            FilePath = path;
            IsDirty = dirty;
        }
        finally
        {
            _loading = false;
        }

        Revalidate();
        OnPropertyChanged(nameof(RecentFiles));
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
        Header.LinesChanged();
        Revalidate();
        ScheduleRecovery();
    }

    private void ScheduleRecovery()
    {
        _recoveryTimer.Stop();
        _recoveryTimer.Start();
    }

    /// <summary>Writes the recovery snapshot now. Never throws.</summary>
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
            SetStatus("Status_RecoveryFailed", ex.Message);
        }
    }

    private void Revalidate()
    {
        var found = DcpValidator.Validate(_dcp, ValidationPurpose.Upload);
        var byLine = found.Where(i => i.LineId is not null).ToLookup(i => i.LineId!.Value);
        foreach (var row in Lines)
        {
            row.SetIssues(byLine[row.Line.Id].ToList(), IssueText);
        }

        var selected = SelectedLine;
        Issues.Clear();
        foreach (var i in found)
        {
            Issues.Add(new IssueItem(i.IsError ? IssueLevel.Error : IssueLevel.Warning, i.IsError ? Brushes.Crimson : Brushes.DarkOrange, IssueText(i), i.LineId));
        }

        foreach (var item in _uploadMessages)
        {
            Issues.Add(item);
        }

        foreach (var m in _importMessages)
        {
            var (level, brush) = m.Severity switch
            {
                ImportSeverity.Error => (IssueLevel.Error, Brushes.Crimson),
                ImportSeverity.Warning => (IssueLevel.Warning, Brushes.DarkOrange),
                _ => (IssueLevel.Info, Brushes.SteelBlue),
            };
            Issues.Add(new IssueItem(level, brush, MessageText.Import(m), null));
        }

        SelectedLine = selected;
        OnPropertyChanged(nameof(IssuesHeader));
        OnPropertyChanged(nameof(HasRaiDcp));
        OnPropertyChanged(nameof(RaiDcpText));
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
            _ = RefreshLibraryAsync();
        }
    }

    private async Task InitializeDocumentAsync(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.EndsWith(DcpFile.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        var session = _recovery.TryRead();
        if (arg is not null)
        {
            // Opening the file would overwrite the snapshot, the only copy of unsaved work from last time.
            // A DCP of the library is simply written to its file first.
            var keep = session is { HasUnsavedChanges: true } && !SaveSessionToLibrary(session) && !await _dialogs.Confirm(
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

        LoadDcp(session.Dcp, file, session.HasUnsavedChanges);
        if (fileExists)
        {
            // A file newer than the snapshot was changed by something else: reported, and our version is kept
            _lastWriteUtc = session.SavedAt.UtcDateTime;
            UpdateNameFollowsHeader();
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

    public void Shutdown()
    {
        try
        {
            CommitPendingEdits?.Invoke(); // a cell still being edited belongs into the snapshot
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "shutdown");
        }

        SaveLibraryDocument(leaving: false);
        WriteRecovery();
        (_rai as IDisposable)?.Dispose();
    }

    // ---------------------------------------------------------------- file commands

    [RelayCommand]
    private async Task NewDcp()
    {
        if (IsBusy)
        {
            return;
        }

        if (!await LeaveDocumentAsync())
        {
            return;
        }

        StartNewDocument(_settings.Company.CreateDcp(), NewDocumentFolder(), dirty: false, _library is null ? null : "Status_NewDcp");
    }

    [RelayCommand]
    private async Task NewNextEpisode()
    {
        if (IsBusy)
        {
            return;
        }

        var folder = IsLibraryDocument ? Path.GetDirectoryName(FilePath) : null;
        if (!await LeaveDocumentAsync())
        {
            return;
        }

        SyncLines();
        StartNewDocument(_dcp.CreateNextEpisode(), folder, dirty: true, _library is null ? null : "Status_NextEpisodeCreated");
    }

    [RelayCommand]
    private async Task Open()
    {
        if (IsBusy)
        {
            return;
        }

        var path = await _dialogs.PickOpenDcp(_settings.LastDocumentFolder ?? _library?.Folder);
        if (path is not null && !IsOpenLibraryFile(path) && await LeaveDocumentAsync())
        {
            await TryOpenAsync(path);
        }
    }

    // Opening the open library file again does nothing: there is nothing unsaved to revert
    private bool IsOpenLibraryFile(string path) => IsLibraryDocument && SamePath(path, FilePath);

    [RelayCommand]
    private async Task OpenRecent(string? path)
    {
        if (IsBusy)
        {
            return;
        }

        if (path is not null && !IsOpenLibraryFile(path) && await LeaveDocumentAsync())
        {
            await TryOpenAsync(path);
        }
    }

    /// <param name="quiet">Problems go to the status text instead of a message box (library panel, session restore).</param>
    private async Task<bool> TryOpenAsync(string path, bool quiet = false)
    {
        try
        {
            var content = DcpFile.Load(path);
            var inLibrary = _library?.Contains(path) == true;

            // A library file is only rewritten when it is edited, never just because it was opened
            LoadDcp(content.Dcp, path, content.IsLegacy && !inLibrary && !quiet);
            _lastWriteUtc = LastWriteUtc(path);
            UpdateNameFollowsHeader();
            if (!quiet)
            {
                RememberFile(path);
            }

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
                SetStatus("Status_CannotOpen", ex.Message);
            }
            else
            {
                await _dialogs.Message(Loc.Get("Common_Open"), Loc.Format("Status_CannotOpen", ex.Message));
            }

            return false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        CommitPendingEdits?.Invoke();
        if (FilePath is null && IsPendingLibraryDocument && !string.IsNullOrWhiteSpace(_dcp.TitoloItaliano))
        {
            SaveLibraryDocument(leaving: false);
        }
        else if (FilePath is null)
        {
            await SaveAs();
        }
        else
        {
            SaveTo(FilePath);
        }
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        var name = string.IsNullOrWhiteSpace(_dcp.TitoloItaliano) ? "DCP" : _dcp.TitoloItaliano.Trim();
        if (_dcp.Puntata is { } p)
        {
            name += $" {p}";
        }

        var path = await _dialogs.PickSave(Loc.Get("Picker_SaveDcp"), Sanitize(name), DcpFile.Extension, Loc.Get("Picker_DcpFile"), _settings.LastDocumentFolder ?? _library?.Folder);
        if (path is not null)
        {
            SaveTo(path);
        }
    }

    private void SaveTo(string path)
    {
        CommitPendingEdits?.Invoke();
        if (!KeepLegacyOriginal(path, out var original))
        {
            return;
        }

        try
        {
            SyncLines();
            DcpFile.Save(path, _dcp);
            ReportLegacyConverted(true, path, original);
            _lastWriteUtc = LastWriteUtc(path);
            _newFileFolder = null;
            FilePath = path;
            IsDirty = false;
            UpdateNameFollowsHeader();
            RememberFile(path);
            WriteRecovery();
            if (original is null)
            {
                SetStatus("Status_Saved", Path.GetFileName(path));
            }
            _ = RefreshLibraryAsync();
        }
        catch (DcpFileException ex)
        {
            _ = _dialogs.Message(Loc.Get("Common_Save"), Loc.Format("Msg_SaveFailed", ex.Message));
        }
    }

    private void RememberFile(string path)
    {
        _settings.AddRecentFile(path);
        _settings.LastDocumentFolder = Path.GetDirectoryName(Path.GetFullPath(path));
        SaveSettings();
        OnPropertyChanged(nameof(RecentFiles));
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Status_SettingsNotSaved", ex.Message);
        }
    }

    private async Task<bool> ConfirmDiscardAsync() =>
        !IsDirty || await _dialogs.Confirm(Loc.Get("Dlg_UnsavedChanges"), Loc.Get("Msg_DiscardChanges"), Loc.Get("Dlg_Replace"), Loc.Get("Common_Cancel"));

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [RelayCommand]
    private async Task ShowSettings()
    {
        var libraryBefore = _settings.LibraryFolder;
        if (await _dialogs.ShowSettings(_settings))
        {
            SaveSettings();
            if (!string.Equals(libraryBefore, _settings.LibraryFolder, StringComparison.Ordinal))
            {
                ApplyLibrarySetting();
            }
        }
    }

    // ---------------------------------------------------------------- lines

    [RelayCommand]
    private async Task AddMusicFiles()
    {
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
        SetStatus("Status_Importing");
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
            var progress = new Progress<ImportProgress>(p => SetStatus("Status_ImportProgress", p.Done, p.Total, p.CurrentFile ?? ""));
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

            _importMessages.Clear();
            _importMessages.AddRange(result.Messages);
            if (result.NewLines.Count > 0 || result.CompletedLines.Count > 0)
            {
                OnChanged();
            }
            else
            {
                Revalidate();
            }

            if (result.CompletedLines.Count > 0)
            {
                SetStatus("Status_ImportedCompleted", result.NewLines.Count, result.CompletedLines.Count);
            }
            else
            {
                SetStatus("Status_Imported", result.NewLines.Count);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "import");
            await _dialogs.Message(Loc.Get("Dlg_Import"), Loc.Format("Msg_ImportFailed", ex.Message));
            ProgressText = "";
        }
        finally
        {
            IsBusy = false;
        }
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
        if (SelectedLine is not { } s)
        {
            return;
        }

        var from = Lines.IndexOf(s);
        var to = from + delta;
        if (to < 0 || to >= Lines.Count)
        {
            return;
        }

        Lines.Move(from, to);
        Renumber();
        OnChanged();
        SelectedLine = s;
    }

    // ---------------------------------------------------------------- publish

    private IReadOnlyList<ValidationIssue> Validate(ValidationPurpose purpose)
    {
        CommitPendingEdits?.Invoke();
        SyncLines();
        return DcpValidator.Validate(_dcp, purpose);
    }

    [RelayCommand]
    private async Task Check()
    {
        var upload = Validate(ValidationPurpose.Upload);
        var document = Validate(ValidationPurpose.Document);
        var text = Loc.Get("Check_ForUpload") + "\n" + Describe(upload) + "\n\n" + Loc.Get("Check_ForDocument") + "\n" + Describe(document);
        await _dialogs.ShowText(Loc.Get("Check_Title"), text);
    }

    private string Describe(IReadOnlyList<ValidationIssue> issues) =>
        issues.Count == 0
            ? "  " + Loc.Get("Issues_None")
            : string.Join("\n", issues.Select(i => $"  [{Loc.Get(i.IsError ? "Kind_Error" : "Kind_Warning")}] {IssueText(i)}"));

    /// <summary>Drops what is known about an earlier upload, so the next upload creates a new DCP at RAI.</summary>
    [RelayCommand]
    private async Task ForgetUpload()
    {
        if (_dcp.Upload is null)
        {
            SetStatus("Status_NoEarlierUpload");
            return;
        }

        var text = _dcp.Upload.Codice is { } codice ? Loc.Format("Msg_ForgetUploadCodice", codice) : Loc.Get("Msg_ForgetUpload");
        if (!await _dialogs.Confirm(Loc.MenuName("Menu_ForgetUpload"), text, Loc.Get("Dlg_Forget"), Loc.Get("Common_Cancel")))
        {
            return;
        }

        _dcp.Upload = null;
        OnChanged();
        Revalidate();
        SetStatus("Status_UploadForgotten");
    }

    [RelayCommand]
    private async Task ShowUploadPreview()
    {
        CommitPendingEdits?.Invoke();
        SyncLines();
        try
        {
            // The preview itself is a technical listing of the form fields and stays English
            await _dialogs.ShowText(Loc.Get("Dlg_UploadPreview"), _rai.DescribeUpload(_dcp));
        }
        catch (Exception ex)
        {
            await _dialogs.Message(Loc.Get("Dlg_UploadPreview"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task Upload()
    {
        if (IsBusy)
        {
            return;
        }

        var issues = Validate(ValidationPurpose.Upload);
        var errors = issues.Where(i => i.IsError).ToList();
        _uploadMessages.Clear();
        if (errors.Count > 0)
        {
            // No dialog: the problems are in the list below the grid, each one selects its line
            Revalidate();
            SetStatus("Status_NotUploadedProblems", errors.Count);
            SelectedIssue = Issues.FirstOrDefault(i => i.Level == IssueLevel.Error);
            return;
        }

        // A complete upload would be duplicated, so ask. An incomplete one is continued on its draft at RAI.
        if (_dcp.Upload is { Complete: true } &&
            issues.FirstOrDefault(i => i.Code == "already-uploaded") is { } again &&
            !await _dialogs.Confirm(Loc.Get("Dlg_AlreadyUploaded"), IssueText(again) + "\n\n" + Loc.Get("Msg_UploadAnyway"),
                Loc.Get("Dlg_UploadAgain"), Loc.Get("Common_Cancel")))
        {
            return;
        }

        // What goes to RAI is in the library file first
        SaveLibraryDocument(leaving: false);
        IsBusy = true;
        var dcp = _dcp;
        try
        {
            if (!await EnsureLoginAsync())
            {
                return;
            }

            _operation = new CancellationTokenSource();
            // The client's step text (English) goes only into the log file
            var progress = new Progress<UploadProgress>(p => SetStatusText(
                Loc.Format("Status_UploadProgress", p.LinesSaved, p.LinesTotal),
                Loc.English("Status_UploadProgress", p.LinesSaved, p.LinesTotal) + " " + p.Message));
            SetStatus("Status_Uploading");
            var result = await _rai.UploadAsync(dcp, progress, _operation.Token);
            dcp.Upload = result.Record;
            OnChanged();
            SaveUploadRecord(dcp);
            var record = result.Record;
            var (key, args) = record.Codice is { } c
                ? ("Status_UploadedAs", new object?[] { record.LinesSaved, record.Stato is { } st ? $"{c} ({st})" : c })
                : ("Status_Uploaded", new object?[] { record.LinesSaved });
            SetStatusText(
                Loc.Format(key, args) + (result.Verified ? " " + Loc.Get("Status_UploadVerified") : ""),
                Loc.English(key, args) + (result.Verified ? " " + Loc.English("Status_UploadVerified") : ""));
            _uploadMessages.Add(new IssueItem(IssueLevel.Info, Brushes.SeaGreen, ProgressText, null));
            foreach (var w in result.Warnings)
            {
                // The client's checks of RAI's answer, in English
                _uploadMessages.Add(new IssueItem(IssueLevel.Warning, Brushes.DarkOrange, Loc.Format("Issue_Upload", w), null));
            }

            Revalidate();

            // Straight to RAI's page of the DCP, where the user checks it and sends it on
            await OpenBrowserAsync(result.Url);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Status_UploadCancelled");
            _uploadMessages.Add(new IssueItem(IssueLevel.Warning, Brushes.DarkOrange, ProgressText, null));
            Revalidate();
        }
        catch (RaiException ex)
        {
            await HandleRaiFailureAsync(ex, dcp);
        }
        finally
        {
            _operation = null;
            IsBusy = false;
            RefreshLogin();
        }
    }

    private async Task HandleRaiFailureAsync(RaiException ex, Dcp dcp)
    {
        // No dialog: everything goes into the issues list, RAI's messages attached to the lines they name
        if (ex.PartialUpload is { } partial)
        {
            dcp.Upload = partial;
            OnChanged();
            SaveUploadRecord(dcp);
            _uploadMessages.Add(new IssueItem(IssueLevel.Error, Brushes.Crimson,
                Loc.Format("Issue_PartialUpload", partial.LinesSaved, dcp.Lines.Count), null));
        }

        if (ex.PortalMessages.Count == 0)
        {
            _uploadMessages.Add(new IssueItem(IssueLevel.Error, Brushes.Crimson, Loc.Format("Issue_Upload", MessageText.Rai(ex)), null));
        }

        foreach (var message in ex.PortalMessages)
        {
            // RAI numbers the lines ("opera 6") like the grid does
            var match = System.Text.RegularExpressions.Regex.Match(message, @"opera\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var line = match.Success && int.TryParse(match.Groups[1].Value, out var n) && n >= 1 && n <= dcp.Lines.Count ? dcp.Lines[n - 1] : null;
            var title = line is null || string.IsNullOrWhiteSpace(line.Titolo) ? "" : $" ({line.Titolo.Trim()})";
            // RAI's own message, as RAI wrote it
            _uploadMessages.Add(new IssueItem(IssueLevel.Error, Brushes.Crimson, $"RAI: {message}{title}", line?.Id));
        }

        Revalidate();
        var failed = _uploadMessages.Count(i => i.LineId is not null);
        // The log file gets the client's English message, which has the details
        if (failed > 0)
        {
            SetStatusText(Loc.Format("Status_UploadRejectedLines", failed), Loc.English("Status_UploadRejectedLines", failed) + " " + ex.Message);
        }
        else
        {
            SetStatusText(
                Loc.Format("Status_UploadFailed", Loc.MenuName("Menu_SaveDiagnostics")),
                Loc.English("Status_UploadFailed", Loc.MenuName("Menu_SaveDiagnostics", System.Globalization.CultureInfo.InvariantCulture)) + " " + ex.Message);
        }
        SelectedIssue = Issues.FirstOrDefault(i => i.Level == IssueLevel.Error && i.LineId is not null) ?? SelectedIssue;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Export()
    {
        var issues = Validate(ValidationPurpose.Document);
        SaveLibraryDocument(leaving: false);
        var errors = issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            await _dialogs.ShowText(Loc.Get("Dlg_CannotExport"), Loc.Get("Msg_FixFirst") + "\n\n" + string.Join("\n", errors.Select(e => "- " + IssueText(e))));
            return;
        }

        var name = string.IsNullOrWhiteSpace(_dcp.TitoloItaliano) ? "DCP" : _dcp.TitoloItaliano.Trim();
        var path = await _dialogs.PickSave(Loc.MenuName("Menu_Export"), Sanitize(name), _exporter.FileExtension, Loc.Get("Picker_WordDocument"), _settings.LastDocumentFolder);
        if (path is null)
        {
            return;
        }

        try
        {
            // Into memory first: a failing export must not leave a broken (or truncated older) document behind
            using (var buffer = new MemoryStream())
            {
                _exporter.Export(_dcp, _settings.Company, buffer);
                File.WriteAllBytes(path, buffer.ToArray());
            }

            SetStatus("Status_Exported", Path.GetFileName(path));
            OpenWithShell(path);
            var warnings = issues.Where(i => !i.IsError).ToList();
            if (warnings.Count > 0)
            {
                await _dialogs.ShowText(Loc.Get("Dlg_ExportedWithWarnings"), Loc.Get("Msg_ExportWarnings") + "\n\n" + string.Join("\n", warnings.Select(w => "- " + IssueText(w))));
            }
        }
        catch (Exception ex) when (ex is ExportException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.Message(Loc.Get("Dlg_Export"), Loc.Format("Msg_ExportFailed", ex.Message));
        }
    }

    // ---------------------------------------------------------------- RAI

    private void RefreshLogin() =>
        LoginText = _rai.IsLoggedIn ? Loc.Format("Login_LoggedInAs", _rai.Username) : Loc.Get("Login_NotLoggedIn");

    private async Task<bool> EnsureLoginAsync(bool forcePrompt = false)
    {
        if (_rai.IsLoggedIn && !forcePrompt)
        {
            return true;
        }

        var username = _settings.RaiUsername;
        var saved = string.IsNullOrWhiteSpace(username) ? null : _credentials.LoadPassword(username);
        var attempt = forcePrompt || saved is null ? null : new RaiCredentials(username, saved);
        string? error = null;
        var rememberDefault = saved is not null || _settings.RaiUsername.Length == 0;

        while (true)
        {
            var remember = rememberDefault;
            if (attempt is null)
            {
                var input = await _dialogs.ShowLogin(_settings.RaiUsername, saved ?? "", remember, _credentials.IsPersistent, error);
                if (input is null)
                {
                    return false;
                }

                attempt = new RaiCredentials(input.Username.Trim(), input.Password);
                remember = input.Remember;
                saved = null;
            }

            _operation = new CancellationTokenSource();
            try
            {
                SetStatus("Status_LoggingIn");
                await _rai.LoginAsync(attempt, _operation.Token);
                SetStatus("Status_LoggedIn");
                if (_settings.RaiUsername != attempt.Username)
                {
                    _settings.RaiUsername = attempt.Username;
                    SaveSettings();
                }

                if (_credentials.IsPersistent || remember)
                {
                    if (remember)
                    {
                        _credentials.SavePassword(attempt.Username, attempt.Password);
                    }
                    else
                    {
                        _credentials.DeletePassword(attempt.Username);
                    }
                }

                RefreshLogin();
                return true;
            }
            catch (RaiException ex) when (ex.Kind == RaiErrorKind.InvalidCredentials)
            {
                error = MessageText.Rai(ex);
                attempt = null;
                saved = null;
            }
            catch (RaiException ex)
            {
                await _dialogs.Message(Loc.Get("Dlg_RaiLogin"), MessageText.Rai(ex));
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _operation = null;
            }
        }
    }

    [RelayCommand]
    private async Task LogIn()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (await EnsureLoginAsync(forcePrompt: true))
            {
                await _dialogs.Message("RAI", Loc.Get("Msg_LoggedIn"));
            }
        }
        finally
        {
            IsBusy = false;
            RefreshLogin();
        }
    }

    /// <summary>True when this DCP was saved at RAI by an upload, completely or in part.</summary>
    public bool HasRaiDcp => !string.IsNullOrEmpty(_dcp.Upload?.Url);

    /// <summary>RAI's code and state of this DCP for the status bar, e.g. "RAI 1788316-00 (Bozza)".</summary>
    public string RaiDcpText
    {
        get
        {
            if (_dcp.Upload is not { } record)
            {
                return "";
            }

            var text = "RAI " + (record.Codice ?? "DCP") + (record.Stato is null ? "" : $" ({record.Stato})");
            return record.Complete ? text : Loc.Format("Status_RaiDcpPartly", text, record.LinesSaved, _dcp.Lines.Count);
        }
    }

    /// <summary>Opens the page of this DCP at RAI, which is much easier than finding it in RAI's search.</summary>
    [RelayCommand]
    private async Task OpenRaiDcp()
    {
        if (_dcp.Upload is not { Url.Length: > 0 } record || !Uri.TryCreate(record.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, _rai.PortalUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Status_NotUploadedYet");
            return;
        }

        if (!_rai.IsLoggedIn && !await EnsureLoginAsync())
        {
            return;
        }

        await OpenBrowserAsync(uri);
    }

    [RelayCommand]
    private async Task OpenDcpSearch()
    {
        if (!_rai.IsLoggedIn && !await EnsureLoginAsync())
        {
            return;
        }

        await OpenBrowserAsync(_rai.GetPageUri(RaiPage.RicercaDcp));
    }

    private async Task OpenBrowserAsync(Uri uri)
    {
        if (!await _dialogs.ShowBrowser(uri, _rai))
        {
            OpenWithShell(uri.ToString());
            await _dialogs.Message(
                Loc.Get("Dlg_SystemBrowser"),
                Loc.Get("Msg_SystemBrowser"));
        }
    }

    [RelayCommand]
    private async Task SaveDiagnostics()
    {
        var folder = await _dialogs.PickFolder(Loc.Get("Picker_SaveDiagnostics"));
        if (folder is null)
        {
            return;
        }

        try
        {
            var file = await _rai.SaveDiagnosticsAsync(folder);
            await _dialogs.Message(Loc.Get("Dlg_DiagnosticsSaved"), Loc.Format("Msg_DiagnosticsSaved", file) + "\n\n" + Loc.Get("Msg_DiagnosticsSend"));
        }
        catch (Exception ex) when (ex is RaiException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.Message(Loc.MenuName("Menu_SaveDiagnostics"), ex is RaiException rai ? MessageText.Rai(rai) : ex.Message);
        }
    }

    [RelayCommand]
    private void CancelOperation() => _operation?.Cancel();

    // ---------------------------------------------------------------- help

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        SetStatus("Status_CheckingForUpdates");
        var outcome = await Task.Run(_updates.CheckAndDownloadAsync);
        ProgressText = "";
        switch (outcome)
        {
            case UpdateCheckOutcome.Downloaded:
                UpdateReady = true;
                await _dialogs.Message(Loc.Get("Dlg_Update"), Loc.Format("Msg_UpdateDownloaded", _updates.ReadyVersion, Loc.Get("Status_UpdateReady")));
                break;
            case UpdateCheckOutcome.UpToDate:
                await _dialogs.Message(Loc.Get("Dlg_Update"), Loc.Get("Msg_UpToDate"));
                break;
            case UpdateCheckOutcome.NotInstalled:
                await _dialogs.Message(Loc.Get("Dlg_Update"), Loc.Get("Msg_NotInstalled"));
                break;
            default:
                await _dialogs.Message(Loc.Get("Dlg_Update"), Loc.Format("Msg_UpdateFailed", _updates.LastError));
                break;
        }
    }

    [RelayCommand]
    private void RestartForUpdate() => _updates.ApplyAndRestart();

    [RelayCommand]
    private async Task About()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Loc.Get("About_UnknownVersion");
        await _dialogs.Message(Loc.MenuName("Menu_About"), "DCP Tool\n" + Loc.Format("About_Version", version.Split('+')[0]) + "\n\n" + Loc.Get("About_Text"));
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
