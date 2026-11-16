using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        _recoveryTimer.Tick += (_, _) =>
        {
            _recoveryTimer.Stop();
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
    [ObservableProperty] private string _loginText = "RAI: not logged in";

    public string[] RecentFiles => [.. _settings.RecentFiles];

    public string FileText =>
        (FilePath is null ? "Untitled" : Path.GetFileName(FilePath)) + (IsDirty ? "  • modified" : "");

    public string WindowTitle =>
        $"{(FilePath is null ? "Untitled" : Path.GetFileName(FilePath))}{(IsDirty ? "*" : "")} – DCP Tool";

    public string IssueSummary
    {
        get
        {
            var errors = Issues.Count(i => i.Kind == "Error");
            var warnings = Issues.Count(i => i.Kind == "Warning");
            return Issues.Count == 0 ? "No problems found." : $"{errors} error(s), {warnings} warning(s), {Issues.Count - errors - warnings} note(s)";
        }
    }

    public IReadOnlyList<string> ImportPatterns => _importService.FilePatterns;
    public bool CanImportPath(string path) => _importService.CanImport(path);

    /// <summary>What the app did, newest last. Also written to a daily file in the log folder.</summary>
    public ObservableCollection<string> Activity { get; } = [];

    /// <summary>Folder of the activity log files. Set once at start.</summary>
    public string? ActivityLogFolder { get; set; }

    // Every status text is an event worth keeping: import results, login, each upload step, failures
    partial void OnProgressTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var entry = $"{DateTime.Now:HH:mm:ss}  {value}";
        Activity.Add(entry);
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

    partial void OnFilePathChanged(string? value) => OnFileStateChanged();
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
            ProgressText = "Could not write the recovery snapshot: " + ex.Message;
        }
    }

    private void Revalidate()
    {
        var found = DcpValidator.Validate(_dcp, ValidationPurpose.Upload);
        var byLine = found.Where(i => i.LineId is not null).ToLookup(i => i.LineId!.Value);
        foreach (var row in Lines)
        {
            row.SetIssues(byLine[row.Line.Id].ToList());
        }

        var selected = SelectedLine;
        Issues.Clear();
        foreach (var i in found)
        {
            Issues.Add(new IssueItem(i.IsError ? "Error" : "Warning", i.IsError ? Brushes.Crimson : Brushes.DarkOrange, i.Message, i.LineId));
        }

        foreach (var item in _uploadMessages)
        {
            Issues.Add(item);
        }

        foreach (var m in _importMessages)
        {
            var (kind, brush) = m.Severity switch
            {
                ImportSeverity.Error => ("Error", Brushes.Crimson),
                ImportSeverity.Warning => ("Warning", Brushes.DarkOrange),
                _ => ("Info", Brushes.SteelBlue),
            };
            Issues.Add(new IssueItem(kind, brush, (m.File is null ? "" : m.File + ": ") + m.Text, null));
        }

        SelectedLine = selected;
        OnPropertyChanged(nameof(IssueSummary));
        OnPropertyChanged(nameof(HasRaiDcp));
        OnPropertyChanged(nameof(RaiDcpText));
    }

    // ---------------------------------------------------------------- startup / shutdown

    public async Task InitializeAsync(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.EndsWith(DcpFile.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        var session = _recovery.TryRead();
        if (arg is not null)
        {
            // Opening the file would overwrite the snapshot, the only copy of unsaved work from last time
            var keep = session is { HasUnsavedChanges: true } && !await _dialogs.Confirm(
                "Unsaved changes",
                $"The DCP from the last session has changes that were never saved to a file. Open \"{Path.GetFileName(arg)}\" and discard those changes?",
                "Open file",
                "Keep unsaved DCP");
            if (!keep && await TryOpenAsync(arg))
            {
                return;
            }
        }

        if (session is not null)
        {
            LoadDcp(session.Dcp, session.FilePath, session.HasUnsavedChanges);
            return;
        }

        LoadDcp(_settings.Company.CreateDcp(), null, false);
        IsDirty = false;
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

        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        LoadDcp(_settings.Company.CreateDcp(), null, false);
    }

    [RelayCommand]
    private async Task NewNextEpisode()
    {
        if (IsBusy)
        {
            return;
        }

        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        SyncLines();
        LoadDcp(_dcp.CreateNextEpisode(), null, true);
    }

    [RelayCommand]
    private async Task Open()
    {
        if (IsBusy)
        {
            return;
        }

        var path = await _dialogs.PickOpenDcp(_settings.LastDocumentFolder);
        if (path is not null && await ConfirmDiscardAsync())
        {
            await TryOpenAsync(path);
        }
    }

    [RelayCommand]
    private async Task OpenRecent(string? path)
    {
        if (IsBusy)
        {
            return;
        }

        if (path is not null && await ConfirmDiscardAsync())
        {
            await TryOpenAsync(path);
        }
    }

    private async Task<bool> TryOpenAsync(string path)
    {
        try
        {
            var content = DcpFile.Load(path);
            LoadDcp(content.Dcp, path, content.IsLegacy);
            RememberFile(path);
            if (content.IsLegacy)
            {
                await _dialogs.Message("Older file format", "This file was written by an older version of DCP Tool. It will be saved in the new format the next time you save it.");
            }

            return true;
        }
        catch (DcpFileException ex)
        {
            await _dialogs.Message("Open", ex.Message);
            return false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        CommitPendingEdits?.Invoke();
        if (FilePath is null)
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

        var path = await _dialogs.PickSave("Save DCP", Sanitize(name), DcpFile.Extension, "DCP file", _settings.LastDocumentFolder);
        if (path is not null)
        {
            SaveTo(path);
        }
    }

    private void SaveTo(string path)
    {
        CommitPendingEdits?.Invoke();
        try
        {
            SyncLines();
            DcpFile.Save(path, _dcp);
            FilePath = path;
            IsDirty = false;
            RememberFile(path);
            WriteRecovery();
            ProgressText = "Saved " + Path.GetFileName(path);
        }
        catch (DcpFileException ex)
        {
            _ = _dialogs.Message("Save", ex.Message);
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
            ProgressText = "Could not save the settings: " + ex.Message;
        }
    }

    private async Task<bool> ConfirmDiscardAsync() =>
        !IsDirty || await _dialogs.Confirm("Unsaved changes", "The current DCP has changes that are not saved to a file. Replace it anyway?", "Replace", "Cancel");

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [RelayCommand]
    private async Task ShowSettings()
    {
        if (await _dialogs.ShowSettings(_settings))
        {
            SaveSettings();
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
        ProgressText = "Importing...";
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
            var progress = new Progress<ImportProgress>(p => ProgressText = $"Importing {p.Done}/{p.Total}: {p.CurrentFile}");
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

            ProgressText = $"Imported {result.NewLines.Count} line(s)" + (result.CompletedLines.Count > 0 ? $", completed {result.CompletedLines.Count}" : "") + ".";
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "import");
            await _dialogs.Message("Import", "The import failed: " + ex.Message);
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
        var text = "For upload to RAI:\n" + Describe(upload) + "\n\nFor the Word document:\n" + Describe(document);
        await _dialogs.ShowText("Check", text);
    }

    private static string Describe(IReadOnlyList<ValidationIssue> issues) =>
        issues.Count == 0
            ? "  No problems found."
            : string.Join("\n", issues.Select(i => $"  [{(i.IsError ? "Error" : "Warning")}] {i.Message}"));

    /// <summary>Drops what is known about an earlier upload, so the next upload creates a new DCP at RAI.</summary>
    [RelayCommand]
    private async Task ForgetUpload()
    {
        if (_dcp.Upload is null)
        {
            ProgressText = "This DCP has no earlier upload.";
            return;
        }

        var what = _dcp.Upload.Codice is { } codice ? $"DCP {codice}" : "the DCP";
        if (!await _dialogs.Confirm("Forget the earlier upload",
                $"The next upload will create a new DCP at RAI. {what} that was saved there earlier is not deleted; delete it in RAI's DCP search if it is not needed.",
                "Forget", "Cancel"))
        {
            return;
        }

        _dcp.Upload = null;
        OnChanged();
        Revalidate();
        ProgressText = "The earlier upload was forgotten.";
    }

    [RelayCommand]
    private async Task ShowUploadPreview()
    {
        CommitPendingEdits?.Invoke();
        SyncLines();
        try
        {
            await _dialogs.ShowText("What would be uploaded", _rai.DescribeUpload(_dcp));
        }
        catch (Exception ex)
        {
            await _dialogs.Message("Show what would be uploaded", ex.Message);
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
            ProgressText = $"Not uploaded: {errors.Count} problem(s) to fix first, see the list below.";
            SelectedIssue = Issues.FirstOrDefault(i => i.Kind == "Error");
            return;
        }

        // A complete upload would be duplicated, so ask. An incomplete one is continued on its draft at RAI.
        if (_dcp.Upload is { Complete: true } &&
            issues.FirstOrDefault(i => i.Code == "already-uploaded") is { } again &&
            !await _dialogs.Confirm("Already uploaded", again.Message + "\n\nUpload anyway?", "Upload again", "Cancel"))
        {
            return;
        }

        IsBusy = true;
        var dcp = _dcp;
        try
        {
            if (!await EnsureLoginAsync())
            {
                return;
            }

            _operation = new CancellationTokenSource();
            var progress = new Progress<UploadProgress>(p => ProgressText = $"Uploading: {p.LinesSaved}/{p.LinesTotal} lines. {p.Message}");
            ProgressText = "Uploading...";
            var result = await _rai.UploadAsync(dcp, progress, _operation.Token);
            dcp.Upload = result.Record;
            OnChanged();
            WriteRecovery();
            ProgressText = $"Uploaded {result.Record.LinesSaved} line(s) to RAI{(result.Record.Codice is { } c ? $" as DCP {c}" + (result.Record.Stato is { } st ? $" ({st})" : "") : "")}." +
                           (result.Verified ? " RAI's page lists every title." : "");
            _uploadMessages.Add(new IssueItem("Info", Brushes.SeaGreen, ProgressText, null));
            foreach (var w in result.Warnings)
            {
                _uploadMessages.Add(new IssueItem("Warning", Brushes.DarkOrange, "Upload: " + w, null));
            }

            Revalidate();

            // Straight to RAI's page of the DCP, where the user checks it and sends it on
            await OpenBrowserAsync(result.Url);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Upload cancelled. RAI may already have saved part of the DCP.";
            _uploadMessages.Add(new IssueItem("Warning", Brushes.DarkOrange, ProgressText, null));
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
            WriteRecovery();
            _uploadMessages.Add(new IssueItem("Error", Brushes.Crimson,
                $"The upload is incomplete: RAI saved {partial.LinesSaved} of {dcp.Lines.Count} lines, and that partial DCP exists at RAI. " +
                "Complete or delete it in RAI's DCP search before uploading again, or a second DCP is created.", null));
        }

        if (ex.PortalMessages.Count == 0)
        {
            _uploadMessages.Add(new IssueItem("Error", Brushes.Crimson, "Upload: " + ex.Message, null));
        }

        foreach (var message in ex.PortalMessages)
        {
            // RAI numbers the lines ("opera 6") like the grid does
            var match = System.Text.RegularExpressions.Regex.Match(message, @"opera\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var line = match.Success && int.TryParse(match.Groups[1].Value, out var n) && n >= 1 && n <= dcp.Lines.Count ? dcp.Lines[n - 1] : null;
            var title = line is null || string.IsNullOrWhiteSpace(line.Titolo) ? "" : $" ({line.Titolo.Trim()})";
            _uploadMessages.Add(new IssueItem("Error", Brushes.Crimson, $"RAI: {message}{title}", line?.Id));
        }

        Revalidate();
        var failed = _uploadMessages.Count(i => i.LineId is not null);
        ProgressText = failed > 0
            ? $"Upload failed: RAI rejected {failed} line(s), see the list below."
            : "Upload failed, see the list below. RAI > Save diagnostics collects the details for the developers.";
        SelectedIssue = Issues.FirstOrDefault(i => i.Kind == "Error" && i.LineId is not null) ?? SelectedIssue;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Export()
    {
        var issues = Validate(ValidationPurpose.Document);
        var errors = issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            await _dialogs.ShowText("Cannot export yet", "Fix these problems first:\n\n" + string.Join("\n", errors.Select(e => "- " + e.Message)));
            return;
        }

        var name = string.IsNullOrWhiteSpace(_dcp.TitoloItaliano) ? "DCP" : _dcp.TitoloItaliano.Trim();
        var path = await _dialogs.PickSave("Export Word document", Sanitize(name), _exporter.FileExtension, _exporter.DisplayName, _settings.LastDocumentFolder);
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

            ProgressText = "Exported " + Path.GetFileName(path);
            OpenWithShell(path);
            var warnings = issues.Where(i => !i.IsError).ToList();
            if (warnings.Count > 0)
            {
                await _dialogs.ShowText("Exported with warnings", "The document was written, but please check:\n\n" + string.Join("\n", warnings.Select(w => "- " + w.Message)));
            }
        }
        catch (Exception ex) when (ex is ExportException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.Message("Export", ex.Message);
        }
    }

    // ---------------------------------------------------------------- RAI

    private void RefreshLogin() =>
        LoginText = _rai.IsLoggedIn ? $"RAI: logged in as {_rai.Username}" : "RAI: not logged in";

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
                ProgressText = "Logging in to RAI...";
                await _rai.LoginAsync(attempt, _operation.Token);
                ProgressText = "Logged in.";
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
                error = ex.Message;
                attempt = null;
                saved = null;
            }
            catch (RaiException ex)
            {
                await _dialogs.Message("RAI login", ex.Message);
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
                await _dialogs.Message("RAI", "Logged in to the RAI portal.");
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
            return record.Complete ? text : $"{text}, {record.LinesSaved} of {_dcp.Lines.Count} lines saved";
        }
    }

    /// <summary>Opens the page of this DCP at RAI, which is much easier than finding it in RAI's search.</summary>
    [RelayCommand]
    private async Task OpenRaiDcp()
    {
        if (_dcp.Upload is not { Url.Length: > 0 } record || !Uri.TryCreate(record.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, _rai.PortalUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            ProgressText = "This DCP has not been uploaded to RAI yet.";
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
                "Opened in the system browser",
                "The built-in browser is not available here, so the page was opened in your system browser. You have to sign in to the RAI portal there yourself.");
        }
    }

    [RelayCommand]
    private async Task SaveDiagnostics()
    {
        var folder = await _dialogs.PickFolder("Save diagnostics to");
        if (folder is null)
        {
            return;
        }

        try
        {
            var file = await _rai.SaveDiagnosticsAsync(folder);
            await _dialogs.Message("Diagnostics saved", $"Saved {file}\n\nSend this file to the developers. Passwords, cookies and session tokens are removed from it.");
        }
        catch (Exception ex) when (ex is RaiException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.Message("Diagnostics", ex.Message);
        }
    }

    [RelayCommand]
    private void CancelOperation() => _operation?.Cancel();

    // ---------------------------------------------------------------- help

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        ProgressText = "Checking for updates...";
        var outcome = await Task.Run(_updates.CheckAndDownloadAsync);
        ProgressText = "";
        switch (outcome)
        {
            case UpdateCheckOutcome.Downloaded:
                UpdateReady = true;
                await _dialogs.Message("Update", $"Version {_updates.ReadyVersion} was downloaded. Use \"Update ready - restart\" in the status bar to install it.");
                break;
            case UpdateCheckOutcome.UpToDate:
                await _dialogs.Message("Update", "You have the latest version.");
                break;
            case UpdateCheckOutcome.NotInstalled:
                await _dialogs.Message("Update", "This copy was not installed with the installer, so it cannot update itself.");
                break;
            default:
                await _dialogs.Message("Update", "The update check failed: " + _updates.LastError);
                break;
        }
    }

    [RelayCommand]
    private void RestartForUpdate() => _updates.ApplyAndRestart();

    [RelayCommand]
    private async Task About()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        await _dialogs.Message("About DCP Tool", $"DCP Tool\nVersion {version.Split('+')[0]}\n\nCreates RAI music cue sheets (DCP).");
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
