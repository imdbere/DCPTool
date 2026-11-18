using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DcpTool.App.Localisation;
using DcpTool.App.Services;
using DcpTool.Core.Library;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;

namespace DcpTool.App.ViewModels;

/// <summary>
/// The library: all DCPs as .dcp files in one folder the user chooses. The app has no browser for it (Explorer does
/// that, through "Bibliotheksordner öffnen"); the drawer shows the presets ("Sendungen") and the DCPs edited last.
/// A document whose file lies in the library is written to it automatically shortly after every change, before
/// switching documents, before closing and before upload/export, so it never has an "unsaved" state. A new DCP gets
/// its file in the library as soon as it has a Titolo italiano. Files outside the library keep the explicit Speichern.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxRecent = 10;

    private DcpLibrary? _library;
    private LibrarySnapshot _snapshot = LibrarySnapshot.Empty;

    // Recently opened files outside the library, read in the background with each scan
    private IReadOnlyList<LibraryEntry> _outsideRecent = [];
    private bool _initialized;
    private bool _scanning;
    private bool _scanAgain;

    // Folder a new, still untitled document gets its file in (the original's folder for a next episode). Null = library root.
    private string? _newFileFolder;

    // Modified time of the open file when this app last read or wrote it. A newer one means someone else changed it.
    private DateTime _lastWriteUtc;

    // The external change already reported, so it is said once and not on every activation
    private DateTime? _externalNotice;

    // The file name is the one SuggestPath gives for the header, so it follows title, episode and date when they change
    private bool _nameFollowsHeader;

    // The preset being renamed in the drawer's name box; null while a new one is named
    private PresetItem? _presetRenaming;

    public ObservableCollection<PresetItem> Presets { get; } = [];
    public ObservableCollection<RecentItem> Recent { get; } = [];

    [ObservableProperty] private bool _arePresetsExpanded = true;
    [ObservableProperty] private bool _isRecentExpanded = true;

    /// <summary>The name box below the presets: a new preset (+) or a new name for one (⋯ › Umbenennen).</summary>
    [ObservableProperty] private bool _isPresetEditorOpen;
    [ObservableProperty] private string _presetEditorText = "";
    [ObservableProperty] private string _presetEditorHint = "";

    /// <summary>A problem with the library folder, shown in the drawer.</summary>
    [ObservableProperty] private string _libraryNotice = "";

    public bool HasLibrary => _library is not null;
    public bool HasNoLibrary => _library is null;
    public bool HasNoPresets => _library is not null && Presets.Count == 0;
    public string LibraryFolderText => _library?.Folder ?? "";

    /// <summary>The open document is a file in the library: it is saved automatically.</summary>
    public bool IsLibraryDocument => _library is not null && FilePath is not null && _library.Contains(FilePath);

    /// <summary>The open document has no file yet and gets one in the library once it has a Titolo italiano.</summary>
    public bool IsPendingLibraryDocument => _library is not null && FilePath is null;

    public bool IsAutoSaved => IsLibraryDocument || IsPendingLibraryDocument;

    [RelayCommand]
    private void TogglePresets() => ArePresetsExpanded = !ArePresetsExpanded;

    [RelayCommand]
    private void ToggleRecent() => IsRecentExpanded = !IsRecentExpanded;

    // ---------------------------------------------------------------- setting up

    private DcpLibrary? CreateLibrary(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        try
        {
            return new DcpLibrary(folder.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or System.Security.SecurityException)
        {
            Report(ConsoleLevel.Warning, "Status_LibraryUnusable", folder, ex.Message);
            return null;
        }
    }

    /// <summary>The library folder setting changed.</summary>
    internal void ApplyLibrarySetting()
    {
        _library = CreateLibrary(_settings.LibraryFolder);
        UpdateNameFollowsHeader();
        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(HasNoLibrary));
        OnPropertyChanged(nameof(HasNoPresets));
        OnPropertyChanged(nameof(LibraryFolderText));
        OnFileStateChanged();
        Settings.Refresh();
        _ = RefreshLibraryAsync();

        // An open DCP that has a title but no file now gets its file in the library
        ScheduleRecovery();
    }

    [RelayCommand]
    internal async Task ChooseLibraryFolder()
    {
        var folder = await _dialogs.PickFolder(Loc.Get("Picker_ChooseLibrary"));
        if (folder is null)
        {
            return;
        }

        _settings.LibraryFolder = folder;
        SaveSettings();
        ApplyLibrarySetting();
        if (_library is not null)
        {
            SetStatus("Status_LibraryFolder", _library.Folder);
        }
    }

    /// <summary>Older DCPs are found in Explorer: the library folder opens there.</summary>
    [RelayCommand]
    private void OpenLibraryFolder()
    {
        if (_library is not { } library)
        {
            OpenSettings();
            return;
        }

        try
        {
            Directory.CreateDirectory(library.Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(ConsoleLevel.Warning, "Status_FileProblem", ex.Message);
            return;
        }

        OpenWithShell(library.Folder);
    }

    // ---------------------------------------------------------------- scanning

    /// <summary>Reads the library folder again, off the UI thread. Calls during a scan are folded into one more scan.</summary>
    public async Task RefreshLibraryAsync()
    {
        if (_scanning)
        {
            _scanAgain = true;
            return;
        }

        _scanning = true;
        try
        {
            do
            {
                _scanAgain = false;
                var library = _library;
                var recentFiles = _settings.RecentFiles.ToList();
                var (snapshot, exists, outside) = await Task.Run(() =>
                {
                    // Off the UI thread; only new and changed files are read (DcpLibrary keeps the rest in memory)
                    var found = library?.ScanAll() ?? LibrarySnapshot.Empty;
                    var others = recentFiles
                        .Where(f => library?.Contains(f) != true && File.Exists(f))
                        .Select(DcpLibrary.ReadFile)
                        .ToList();
                    return (found, library?.Exists ?? true, others);
                });
                if (!ReferenceEquals(library, _library))
                {
                    _scanAgain = true; // the folder was changed meanwhile
                    continue;
                }

                _snapshot = snapshot;
                _outsideRecent = outside;
                LibraryNotice = exists ? "" : Loc.Get("Library_FolderMissing");
                RebuildPresets();
                RebuildRecent();
            }
            while (_scanAgain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DcpFileException)
        {
            Report(ConsoleLevel.Warning, "Status_LibraryReadFailed", ex.Message);
        }
        finally
        {
            _scanning = false;
        }
    }

    private void RebuildPresets()
    {
        var presets = _snapshot.Entries.Where(e => e.IsPreset && e.Error is null).OrderBy(e => e.Name, NaturalComparer.Instance).ToList();
        if (!presets.Select(p => (p.Path, p.Title)).SequenceEqual(Presets.Select(p => (p.Path, p.Entry.Title))))
        {
            Presets.Clear();
            foreach (var preset in presets)
            {
                Presets.Add(new PresetItem(preset));
            }
        }

        OnPropertyChanged(nameof(HasNoPresets));
    }

    /// <summary>
    /// "Zuletzt bearbeitet": the DCPs of the library and the files opened outside it, last changed first, with the open
    /// DCP marked (and added on top when it is not among them).
    /// </summary>
    private void RebuildRecent()
    {
        var entries = _snapshot.Entries.Where(e => !e.IsPreset)
            .Concat(_outsideRecent)
            .GroupBy(e => Path.GetFullPath(e.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(e => e.Modified)
            .Take(MaxRecent)
            .ToList();

        var items = new List<RecentItem>();
        var currentFound = false;
        foreach (var entry in entries)
        {
            var isCurrent = SamePath(entry.Path, FilePath);
            currentFound |= isCurrent;
            var item = new RecentItem(entry.Path) { IsCurrent = isCurrent, CanStartNext = entry.Error is null };
            if (isCurrent)
            {
                FillFromOpenDcp(item);
            }
            else
            {
                SetLabel(item, entry.Title, entry.Puntata, entry.Name);
                item.Meta = Loc.Format("Recent_Meta", WhenText(entry.Modified), entry.Error is not null ? Loc.Get("Recent_Unreadable") : StateText(entry.LineCount, entry.UploadState));
                item.NextEpisodeTip = NextEpisodeTip(Programme.Of(entry), entry.Puntata);
            }

            items.Add(item);
        }

        if (!currentFound && _initialized)
        {
            var current = new RecentItem(FilePath) { IsCurrent = true };
            FillFromOpenDcp(current);
            items.Insert(0, current);
        }

        if (!items.Select(i => i.Signature).SequenceEqual(Recent.Select(i => i.Signature)))
        {
            Recent.Clear();
            foreach (var item in items)
            {
                Recent.Add(item);
            }
        }
    }

    /// <summary>The open DCP's entry follows its header while it is edited.</summary>
    private void UpdateCurrentRecent()
    {
        if (Recent.FirstOrDefault(r => r.IsCurrent) is { } item)
        {
            FillFromOpenDcp(item);
        }
    }

    private void FillFromOpenDcp(RecentItem item)
    {
        var upload = _dcp.Upload switch
        {
            null => LibraryUploadState.NotUploaded,
            { Complete: true } => LibraryUploadState.Uploaded,
            _ => LibraryUploadState.Partial,
        };
        SetLabel(item, _dcp.TitoloItaliano, _dcp.Puntata, FilePath is null ? Loc.Get("Doc_Untitled") : Path.GetFileNameWithoutExtension(FilePath));
        var modified = FilePath is not null && !IsDirty ? LastWriteUtc(FilePath).ToLocalTime() : DateTime.Now;
        item.Meta = Loc.Format("Recent_Meta", WhenText(modified), StateText(_dcp.Lines.Count, upload));
        item.NextEpisodeTip = NextEpisodeTip(Programme.Of(_dcp), _dcp.Puntata);
        item.CanStartNext = true;
    }

    // Puntata is the RAI form's term, the same in every language
    private static void SetLabel(RecentItem item, string title, int? puntata, string fallback)
    {
        item.TitleText = string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
        item.EpisodeText = puntata is { } p ? $" · Puntata {p:00}" : "";
        item.Label = item.TitleText + item.EpisodeText;
    }

    private static string WhenText(DateTime modified)
    {
        var day = modified.Date;
        if (day == DateTime.Today)
        {
            return Loc.Get("Recent_Today");
        }

        if (day == DateTime.Today.AddDays(-1))
        {
            return Loc.Get("Recent_Yesterday");
        }

        return day.Year == DateTime.Today.Year
            ? day.ToString("dd.MM.", CultureInfo.InvariantCulture)
            : day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    private static string StateText(int lineCount, LibraryUploadState upload) => upload switch
    {
        LibraryUploadState.Uploaded => Loc.Get("Recent_Uploaded"),
        LibraryUploadState.Partial => Loc.Get("Recent_PartlyUploaded"),
        _ when lineCount == 0 => Loc.Get("Recent_New"),
        _ => Loc.Get("Recent_Working"),
    };

    private string NextEpisodeTip(Programme programme, int? puntata) =>
        NextPuntata(programme, puntata) is { } next
            ? Loc.Format("Recent_NextEpisodeNumber", next.ToString("00", CultureInfo.InvariantCulture))
            : Loc.Get("Recent_NextEpisode");

    /// <summary>
    /// Which programme a DCP belongs to: RAI's Uorg and Matricola when it has them (several programmes may share a
    /// title), its Titolo italiano otherwise.
    /// </summary>
    private readonly record struct Programme(string Title, int? Uorg, int? Matricola)
    {
        public static Programme Of(Dcp dcp) => new(dcp.TitoloItaliano.Trim(), dcp.Uorg, dcp.Matricola);
        public static Programme Of(LibraryEntry e) => new(e.Title.Trim(), e.Uorg, e.Matricola);

        public bool Includes(Programme other) => Matricola is not null && other.Matricola is not null
            ? Matricola == other.Matricola && Uorg == other.Uorg
            : Title.Length > 0 && string.Equals(Title, other.Title, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// The episode after the last one of this programme: the highest Puntata of its DCPs (and of
    /// <paramref name="atLeastAfter"/>) plus one. Null when the programme has no numbered episode yet.
    /// </summary>
    private int? NextPuntata(Programme programme, int? atLeastAfter = null)
    {
        var known = _snapshot.Entries
            .Where(e => !e.IsPreset && e.Puntata is not null && programme.Includes(Programme.Of(e)))
            .Select(e => e.Puntata!.Value)
            .ToList();
        if (atLeastAfter is { } after)
        {
            known.Add(after);
        }

        if (_dcp.Puntata is { } open && programme.Includes(Programme.Of(_dcp)))
        {
            known.Add(open);
        }

        return known.Count == 0 ? null : known.Max() + 1;
    }

    /// <summary>Where a new DCP of this programme gets its file: next to its last episode, else the library itself.</summary>
    private string? FolderForProgramme(Programme programme) =>
        _snapshot.Entries
            .Where(e => !e.IsPreset && e.Error is null && programme.Includes(Programme.Of(e)))
            .OrderByDescending(e => e.Modified)
            .Select(e => Path.GetDirectoryName(e.Path))
            .FirstOrDefault();

    /// <summary>The window became active: the user may have changed files outside the app meanwhile.</summary>
    public void OnWindowActivated()
    {
        if (!_initialized)
        {
            return;
        }

        CheckExternalChange();
        _ = RefreshLibraryAsync();
    }

    // ---------------------------------------------------------------- drawer actions

    /// <summary>A click on a recent DCP: continue it.</summary>
    [RelayCommand]
    private async Task OpenRecent(RecentItem? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        if (item.IsCurrent || item.Path is null)
        {
            Page = AppPage.Songs;
            return;
        }

        if (await LeaveDocumentAsync() && await TryOpenAsync(item.Path, quiet: true))
        {
            Page = AppPage.Songs;
            _ = RefreshLibraryAsync();
        }
    }

    /// <summary>The + on a recent DCP: its next episode, Puntata + 1, same programme information, no songs.</summary>
    [RelayCommand]
    private async Task StartNextEpisode(RecentItem? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        var fromOpen = item.IsCurrent || item.Path is null;
        if (fromOpen)
        {
            CommitPendingEdits?.Invoke();
            SyncLines();
        }

        var source = fromOpen ? _dcp : null;
        var folder = item.Path is null ? _newFileFolder : Path.GetDirectoryName(item.Path);
        if (!await LeaveDocumentAsync())
        {
            return;
        }

        Dcp next;
        try
        {
            // Read after leaving: when it was the open DCP, its last changes are in the file now
            next = source?.CreateNextEpisode() ?? DcpLibrary.CreateNextEpisode(item.Path!);
        }
        catch (DcpFileException ex)
        {
            Report(ConsoleLevel.Warning, "Status_FileProblem", ex.Message);
            return;
        }

        if (NextPuntata(Programme.Of(next), next.Puntata - 1) is { } puntata)
        {
            next.Puntata = puntata;
        }

        StartNewDocument(next, folder, dirty: true, "Status_NextEpisodeOf", item.Label);
        Page = AppPage.Songs;
        FocusBroadcastDate?.Invoke();
    }

    /// <summary>A click on a preset: a new DCP with its programme information, as the next episode of the programme.</summary>
    [RelayCommand]
    private async Task StartFromPreset(PresetItem? preset)
    {
        if (preset is null || IsBusy || !await LeaveDocumentAsync())
        {
            return;
        }

        Dcp dcp;
        try
        {
            dcp = DcpLibrary.CreateFromPreset(preset.Path);
        }
        catch (DcpFileException ex)
        {
            Report(ConsoleLevel.Warning, "Status_FileProblem", ex.Message);
            return;
        }

        dcp.Puntata ??= NextPuntata(Programme.Of(dcp));
        StartNewDocument(dcp, FolderForProgramme(Programme.Of(dcp)), dirty: false, "Status_NewFromPreset", preset.Name);
        Page = AppPage.Songs;
        FocusBroadcastDate?.Invoke();
    }

    /// <summary>The + next to "Sendungen": names a new preset made of the open DCP's programme information.</summary>
    [RelayCommand]
    private void BeginNewPreset()
    {
        if (_library is null || IsBusy)
        {
            return;
        }

        if (IsPresetEditorOpen && _presetRenaming is null)
        {
            CancelPresetEditor();
            return;
        }

        CommitPendingEdits?.Invoke();
        _presetRenaming = null;
        PresetEditorText = _dcp.TitoloItaliano.Trim();
        PresetEditorHint = Loc.Get("Presets_EditorHint");
        ArePresetsExpanded = true;
        IsPresetEditorOpen = true;
    }

    [RelayCommand]
    private void BeginRenamePreset(PresetItem? preset)
    {
        if (preset is null || IsBusy)
        {
            return;
        }

        _presetRenaming = preset;
        PresetEditorText = preset.Name;
        PresetEditorHint = Loc.Get("Presets_EditorHint");
        ArePresetsExpanded = true;
        IsPresetEditorOpen = true;
    }

    [RelayCommand]
    private void CommitPresetEditor()
    {
        var name = PresetEditorText.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var renaming = _presetRenaming;
        CancelPresetEditor();
        if (renaming is null)
        {
            SavePreset(name, replacing: false);
        }
        else
        {
            RenamePreset(renaming, name);
        }
    }

    [RelayCommand]
    private void CancelPresetEditor()
    {
        _presetRenaming = null;
        IsPresetEditorOpen = false;
    }

    /// <summary>⋯ › "Mit aktuellen Angaben aktualisieren": the preset takes the open DCP's programme information.</summary>
    [RelayCommand]
    private void UpdatePreset(PresetItem? preset)
    {
        if (preset is not null && !IsBusy)
        {
            CommitPendingEdits?.Invoke();
            SavePreset(preset.Name, replacing: true);
        }
    }

    private void SavePreset(string name, bool replacing)
    {
        if (_library is not { } library)
        {
            return;
        }

        SyncLines();
        var presetPath = library.PresetPath(name);
        var existed = File.Exists(presetPath);
        if (!KeepLegacyOriginal(presetPath, out var original))
        {
            return;
        }

        try
        {
            var path = library.SavePreset(_dcp, name);
            SetStatus(replacing ? "Status_PresetUpdated" : existed ? "Status_PresetReplaced" : "Status_PresetSaved", Path.GetFileNameWithoutExtension(path));
            ReportLegacyConverted(true, path, original);
        }
        catch (DcpFileException ex)
        {
            Report(ConsoleLevel.Warning, "Status_FileProblem", ex.Message);
        }

        _ = RefreshLibraryAsync();
    }

    private void RenamePreset(PresetItem preset, string newName)
    {
        try
        {
            var target = DcpLibrary.Rename(preset.Path, newName);
            SetStatus("Status_Renamed", preset.Name, Path.GetFileNameWithoutExtension(target));
        }
        catch (DcpFileException ex)
        {
            Report(ConsoleLevel.Warning, "Status_FileProblem", ex.Message);
        }

        _ = RefreshLibraryAsync();
    }

    [RelayCommand]
    private async Task DeletePreset(PresetItem? preset)
    {
        if (preset is null || IsBusy)
        {
            return;
        }

        var recycle = FileManager.DeletesToRecycleBin;
        var text = Loc.Format(recycle ? "Msg_RecyclePreset" : "Msg_DeletePreset", preset.Name) + " " + Loc.Get(recycle ? "Msg_RecycleNote" : "Msg_DeleteNote");
        if (!await _dialogs.Confirm(Loc.Get("Dlg_DeletePreset"), text,
                Loc.Get(recycle ? "Dlg_MoveToRecycleBin" : "Dlg_DeletePermanently"), Loc.Get("Common_Cancel")))
        {
            return;
        }

        try
        {
            FileManager.Delete(preset.Path);
            SetStatus(recycle ? "Status_MovedToRecycleBin" : "Status_Deleted", Path.GetFileName(preset.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(ConsoleLevel.Warning, "Status_NotDeleted", Path.GetFileName(preset.Path), ex.Message);
        }

        _ = RefreshLibraryAsync();
    }

    // ---------------------------------------------------------------- saving library documents

    /// <summary>
    /// Before another document replaces the open one. A library document is written to its file (an untitled one
    /// with content gets a file too, so nothing is lost and nothing is asked). Other documents ask as before.
    /// </summary>
    private async Task<bool> LeaveDocumentAsync()
    {
        CommitPendingEdits?.Invoke();
        if (IsAutoSaved)
        {
            return SaveLibraryDocument(leaving: true);
        }

        return await ConfirmDiscardAsync();
    }

    /// <param name="statusKey">Status text to show (with <paramref name="statusArgs"/>), or null for none.</param>
    private void StartNewDocument(Dcp dcp, string? folder, bool dirty, string? statusKey, params object?[] statusArgs)
    {
        LoadDcp(dcp, null, dirty);
        _newFileFolder = folder;
        if (statusKey is not null)
        {
            SetStatus(statusKey, statusArgs);
        }

        // Has it a title already (preset, next episode)? Then it gets its file right away
        SaveLibraryDocument(leaving: false);
    }

    /// <summary>
    /// Writes a changed library document to its file, and gives a new document its file once it has a title.
    /// <paramref name="leaving"/>: the document is about to be replaced, so an untitled one with content is filed too.
    /// Returns false when writing failed (the status text says why).
    /// </summary>
    private bool SaveLibraryDocument(bool leaving)
    {
        if (_library is not { } library)
        {
            return true;
        }

        if (FilePath is null)
        {
            var titled = !string.IsNullOrWhiteSpace(_dcp.TitoloItaliano);
            if (!titled && !(leaving && IsDirty && HasContent(_dcp)))
            {
                return true;
            }

            SyncLines();
            var folder = _newFileFolder is { } f && library.Contains(f) ? f : library.Folder;
            return WriteLibraryFile(library.SuggestPath(_dcp, RelativeFolder(library, folder)), isNew: true);
        }

        if (!IsDirty || !library.Contains(FilePath))
        {
            return true;
        }

        SyncLines();
        if (!KeepLegacyOriginal(FilePath, out var original))
        {
            return false;
        }

        FollowHeaderName(library);
        var saved = WriteLibraryFile(FilePath, isNew: false);
        ReportLegacyConverted(saved, FilePath, original);
        return saved;
    }

    /// <summary>Folder for the originals of files written by DCP Tool 0.x (see <see cref="KeepLegacyOriginal"/>). Set once at start.</summary>
    public string? LegacyBackupFolder { get; set; }

    /// <summary>
    /// The library may still be used by DCP Tool 0.x, which cannot read the new format. Before a file of the old format
    /// in the library is overwritten for the first time, the original is copied to <see cref="LegacyBackupFolder"/>.
    /// False when that failed: the file must not be overwritten then (the status text says why).
    /// </summary>
    private bool KeepLegacyOriginal(string path, out string? original)
    {
        original = null;
        if (_library is not { } library || !library.Contains(path) || LegacyBackupFolder is not { } backups)
        {
            return true;
        }

        try
        {
            original = library.BackupIfLegacy(path, backups);
            return true;
        }
        catch (DcpFileException ex)
        {
            Report(ConsoleLevel.Error, "Status_LegacyBackupFailed", Path.GetFileName(path), ex.Message);
            return false;
        }
    }

    private void ReportLegacyConverted(bool saved, string path, string? original)
    {
        if (saved && original is not null)
        {
            SetStatus("Status_LegacyConverted", Path.GetFileName(path), original);
        }
    }

    private bool WriteLibraryFile(string path, bool isNew)
    {
        try
        {
            DcpFile.Save(path, _dcp);
            _lastWriteUtc = LastWriteUtc(path);
            FilePath = path;
            IsDirty = false;
            UpdateNameFollowsHeader();
            if (isNew)
            {
                _newFileFolder = null;
                RememberFile(path);
                SetStatus("Status_SavedNewInLibrary", RelativeName(path));
            }

            _ = RefreshLibraryAsync();
            return true;
        }
        catch (DcpFileException ex)
        {
            Report(ConsoleLevel.Warning, "Status_FileProblem", ex.Message);
            return false;
        }
    }

    /// <summary>Renames the open file when its name was the suggested one and the header no longer fits it.</summary>
    private void FollowHeaderName(DcpLibrary library)
    {
        if (!_nameFollowsHeader || FilePath is not { } current
            || NameMatches(Path.GetFileNameWithoutExtension(current), ConventionalName(_dcp)))
        {
            return;
        }

        var folder = Path.GetDirectoryName(current)!;
        var target = library.SuggestPath(_dcp, RelativeFolder(library, folder));
        try
        {
            var moved = DcpLibrary.Rename(current, Path.GetFileNameWithoutExtension(target));
            ReplaceRecent(current, moved);
            FilePath = moved;
            SetStatus("Status_RenamedToMatch", Path.GetFileName(moved));
        }
        catch (DcpFileException ex)
        {
            _nameFollowsHeader = false;
            Report(ConsoleLevel.Warning, "Status_FileProblem", ex.Message);
        }
    }

    private void UpdateNameFollowsHeader() =>
        _nameFollowsHeader = _library is { } library && FilePath is { } path && library.Contains(path)
                             && !IsInPresets(library, path)
                             && NameMatches(Path.GetFileNameWithoutExtension(path), ConventionalName(_dcp));

    /// <summary>The name <see cref="DcpLibrary.SuggestPath"/> gives this DCP, without folder, counter and extension.</summary>
    private static string ConventionalName(Dcp dcp)
    {
        var parts = new List<string> { string.IsNullOrWhiteSpace(dcp.TitoloItaliano) ? "DCP" : dcp.TitoloItaliano.Trim() };
        if (dcp.Puntata is { } puntata)
        {
            parts.Add(puntata.ToString("00"));
        }

        parts.Add(dcp.DataTrasmissione.ToString("yyyy-MM-dd"));
        return DcpLibrary.SafeFileName(string.Join(' ', parts));
    }

    private static bool NameMatches(string name, string conventional) =>
        string.Equals(name, conventional, StringComparison.CurrentCultureIgnoreCase)
        || Regex.IsMatch(name, "^" + Regex.Escape(conventional) + @" \(\d+\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Writes the unsaved DCP of the last session to its library file. False when it is not a library DCP or writing failed.</summary>
    private bool SaveSessionToLibrary(RecoveredSession session)
    {
        if (_library is not { } library)
        {
            return false;
        }

        string path;
        if (session.FilePath is { } file && library.Contains(file))
        {
            path = file;
        }
        else if (session.FilePath is null)
        {
            if (!HasContent(session.Dcp))
            {
                return true; // an empty new DCP: nothing to keep
            }

            path = library.SuggestPath(session.Dcp);
        }
        else
        {
            return false;
        }

        if (!KeepLegacyOriginal(path, out var original))
        {
            return false;
        }

        try
        {
            DcpFile.Save(path, session.Dcp);
            SetStatus("Status_SessionSaved", RelativeName(path));
            ReportLegacyConverted(true, path, original);
            return true;
        }
        catch (DcpFileException ex)
        {
            Report(ConsoleLevel.Warning, "Status_FileProblem", ex.Message);
            return false;
        }
    }

    /// <summary>Right after an upload: what RAI saved goes into the file, so a second upload is warned about.</summary>
    private void SaveUploadRecord(Dcp dcp)
    {
        if (ReferenceEquals(dcp, _dcp))
        {
            if (IsAutoSaved)
            {
                SaveLibraryDocument(leaving: true);
            }
            else if (FilePath is { } path)
            {
                try
                {
                    SyncLines();
                    DcpFile.Save(path, _dcp);
                    _lastWriteUtc = LastWriteUtc(path);
                    IsDirty = false;
                }
                catch (DcpFileException ex)
                {
                    Report(ConsoleLevel.Error, "Status_UploadNotRecorded", ex.Message);
                }
            }
        }

        WriteRecovery();
        _ = RefreshLibraryAsync();
    }

    /// <summary>
    /// The open file was changed by something else: reloaded when we have no unsaved changes, otherwise ours are
    /// kept and the status text says so.
    /// </summary>
    private void CheckExternalChange()
    {
        if (FilePath is not { } path || IsBusy)
        {
            return;
        }

        var name = Path.GetFileName(path);
        DateTime modified;
        try
        {
            if (!File.Exists(path))
            {
                if (_externalNotice != DateTime.MaxValue)
                {
                    _externalNotice = DateTime.MaxValue;
                    if (IsLibraryDocument)
                    {
                        Report(ConsoleLevel.Warning, "Status_FileGoneLibrary", name);
                    }
                    else
                    {
                        Report(ConsoleLevel.Warning, "Status_FileGone", name, Loc.Get("Save_Button"));
                    }
                }

                return;
            }

            modified = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (modified <= _lastWriteUtc)
        {
            return;
        }

        if (!IsDirty)
        {
            try
            {
                var content = DcpFile.Load(path);
                LoadDcp(content.Dcp, path, false);
                _lastWriteUtc = modified;
                UpdateNameFollowsHeader();
                SetStatus("Status_Reloaded", name);
                _ = RefreshLibraryAsync();
            }
            catch (DcpFileException ex)
            {
                if (_externalNotice != modified)
                {
                    _externalNotice = modified;
                    Report(ConsoleLevel.Warning, "Status_ChangedUnreadable", name, ex.Message);
                }
            }

            return;
        }

        if (_externalNotice == modified)
        {
            return;
        }

        _externalNotice = modified;
        if (IsLibraryDocument)
        {
            Report(ConsoleLevel.Warning, "Status_ChangedKeptLibrary", name);
        }
        else
        {
            Report(ConsoleLevel.Warning, "Status_ChangedKept", name, Loc.Get("Save_Button"));
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Anything worth keeping beyond the defaults a new DCP starts with.</summary>
    private static bool HasContent(Dcp dcp) =>
        !string.IsNullOrWhiteSpace(dcp.TitoloItaliano) || !string.IsNullOrWhiteSpace(dcp.TitoloOriginale)
        || !string.IsNullOrWhiteSpace(dcp.Sottotitolo) || dcp.Puntata is not null || dcp.Lines.Count > 0;

    private static bool SamePath(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static DateTime LastWriteUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static string RelativeFolder(DcpLibrary library, string folder)
    {
        var relative = Path.GetRelativePath(library.Folder, folder);
        return relative == "." ? "" : relative;
    }

    private static bool IsInPresets(DcpLibrary library, string path) =>
        Path.GetRelativePath(library.Folder, path).Replace('\\', '/')
            .StartsWith(DcpLibrary.PresetsFolderName + "/", StringComparison.OrdinalIgnoreCase);

    private string RelativeName(string path) =>
        _library is { } library && library.Contains(path) ? Path.GetRelativePath(library.Folder, path) : Path.GetFileName(path);

    private void ReplaceRecent(string oldPath, string newPath)
    {
        var index = _settings.RecentFiles.FindIndex(f => SamePath(f, oldPath));
        if (index < 0)
        {
            return;
        }

        _settings.RecentFiles[index] = Path.GetFullPath(newPath);
        SaveSettings();
    }
}
