using System.Collections.ObjectModel;
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
/// The library ("vault"): all DCPs as .dcp files in one folder, browsed in the side panel.
/// A document whose file lies in the library is written to it automatically shortly after every change,
/// before switching documents, before closing and before upload/export, so it never has an "unsaved" state.
/// A new DCP gets its file in the library as soon as it has a Titolo italiano. Files outside the library
/// keep the explicit Save / Save as.
/// </summary>
public sealed partial class MainViewModel
{
    private DcpLibrary? _library;
    private LibrarySnapshot _snapshot = LibrarySnapshot.Empty;
    private bool _initialized;

    // Tree items by folder path and rows by file path: reused across scans, so expansion, selection and scroll position stay
    private readonly Dictionary<string, LibraryFolderItem> _folderItems = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, LibraryRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    // Folders the user opened in the tree, for this session
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);

    // The open file whose folders were last opened in the tree, so that is done once per file
    private string? _revealedPath;
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

    private Action<string>? _promptAccept;

    public ObservableCollection<LibraryRow> Presets { get; } = [];

    /// <summary>The search results: matching DCPs of all folders, flat.</summary>
    public ObservableCollection<LibraryRow> LibraryDcps { get; } = [];

    /// <summary>The library's folders and DCPs (presets apart), shown when not searching.</summary>
    public ObservableCollection<LibraryTreeItem> LibraryTree { get; } = [];

    public IReadOnlyList<LibrarySortOption> LibrarySortOptions => LibrarySortOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryTargetText))]
    private LibraryTreeItem? _selectedLibraryItem;

    /// <summary>Folder (below the library, "" = the library itself) new DCPs and new folders go into: the selected folder, or the selected DCP's folder.</summary>
    public string LibraryTargetFolder => SelectedLibraryItem switch
    {
        LibraryFolderItem folder => folder.RelativePath,
        LibraryRow { IsPreset: false } row => row.Entry.Folder,
        _ => "",
    };

    public string LibraryTargetText => Loc.Format("Library_TargetFolder", FolderDisplayName(LibraryTargetFolder));

    public bool IsLibrarySearching => !string.IsNullOrWhiteSpace(LibrarySearch);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLibraryPanel), nameof(ShowLibraryStrip))]
    private bool _isLibraryPanelOpen = true;

    [ObservableProperty] private string _librarySearch = "";
    [ObservableProperty] private string _libraryNotice = "";
    [ObservableProperty] private string _presetsEmptyText = "";
    [ObservableProperty] private string _dcpsEmptyText = "";
    [ObservableProperty] private string _presetsHeader = Loc.Format("Library_PresetsHeader", 0);
    [ObservableProperty] private string _dcpsHeader = Loc.Format("Library_DcpsHeader", 0);

    [ObservableProperty] private bool _isPromptOpen;
    [ObservableProperty] private string _promptLabel = "";
    [ObservableProperty] private string _promptValue = "";
    [ObservableProperty] private string _promptAction = "";

    public bool HasLibrary => _library is not null;
    public bool ShowLibraryBanner => _library is null;
    public bool ShowLibraryPanel => _library is not null && IsLibraryPanelOpen;
    public bool ShowLibraryStrip => _library is not null && !IsLibraryPanelOpen;
    public string LibraryFolderText => _library?.Folder ?? "";

    /// <summary>The open document is a file in the library: it is saved automatically.</summary>
    public bool IsLibraryDocument => _library is not null && FilePath is not null && _library.Contains(FilePath);

    /// <summary>The open document has no file yet and gets one in the library once it has a Titolo italiano.</summary>
    public bool IsPendingLibraryDocument => _library is not null && FilePath is null;

    public bool IsAutoSaved => IsLibraryDocument || IsPendingLibraryDocument;

    partial void OnLibrarySearchChanged(string value)
    {
        OnPropertyChanged(nameof(IsLibrarySearching));
        ApplyLibraryFilter();
    }

    public LibrarySortOption SelectedLibrarySort
    {
        get => LibrarySortOption.All.FirstOrDefault(o => o.Sort == LibrarySorting.FromSetting(_settings.LibrarySort)) ?? LibrarySortOption.All[0];
        set
        {
            if (value is null || value.Sort == SelectedLibrarySort.Sort)
            {
                return;
            }

            _settings.LibrarySort = LibrarySorting.ToSetting(value.Sort);
            SaveSettings();
            OnPropertyChanged();
            ApplyLibraryFilter();
        }
    }

    private string FolderDisplayName(string relative) => relative.Length == 0 ? Loc.Get("Library_TopLevel") : relative;

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
            SetStatus("Status_LibraryUnusable", folder, ex.Message);
            return null;
        }
    }

    private void ApplyLibrarySetting()
    {
        _library = CreateLibrary(_settings.LibraryFolder);
        _folderItems.Clear();
        _expandedFolders.Clear();
        _revealedPath = null;
        SelectedLibraryItem = null;
        UpdateNameFollowsHeader();
        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(ShowLibraryBanner));
        OnPropertyChanged(nameof(ShowLibraryPanel));
        OnPropertyChanged(nameof(ShowLibraryStrip));
        OnPropertyChanged(nameof(LibraryFolderText));
        OnFileStateChanged();
        _ = RefreshLibraryAsync();

        // An open DCP that has a title but no file now gets its file in the library
        ScheduleRecovery();
    }

    [RelayCommand]
    private async Task ChooseLibraryFolder()
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

    [RelayCommand]
    private void ToggleLibraryPanel() => IsLibraryPanelOpen = !IsLibraryPanelOpen;

    // ---------------------------------------------------------------- scanning

    [RelayCommand]
    private async Task RefreshLibrary()
    {
        CheckExternalChange();
        await RefreshLibraryAsync();
    }

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
                if (library is null)
                {
                    _snapshot = LibrarySnapshot.Empty;
                    LibraryNotice = "";
                }
                else
                {
                    // Off the UI thread; only new and changed files are read (DcpLibrary keeps the rest in memory)
                    var (snapshot, exists) = await Task.Run(() => (library.ScanAll(), library.Exists));
                    if (!ReferenceEquals(library, _library))
                    {
                        _scanAgain = true; // the folder was changed meanwhile
                        continue;
                    }

                    _snapshot = snapshot;
                    LibraryNotice = exists ? "" : Loc.Get("Library_FolderMissing");
                }

                ApplyLibraryFilter();
            }
            while (_scanAgain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DcpFileException)
        {
            SetStatus("Status_LibraryReadFailed", ex.Message);
        }
        finally
        {
            _scanning = false;
        }
    }

    private void ApplyLibraryFilter()
    {
        var entries = _snapshot.Entries;
        var sort = SelectedLibrarySort.Sort;
        var searching = IsLibrarySearching;
        var rows = new Dictionary<string, LibraryRow>(StringComparer.OrdinalIgnoreCase);
        LibraryRow RowFor(LibraryEntry entry)
        {
            if (!rows.TryGetValue(entry.Path, out var row))
            {
                if (_rows.TryGetValue(entry.Path, out var old))
                {
                    row = old;
                    row.Update(entry);
                }
                else
                {
                    row = new LibraryRow(entry);
                }

                rows[entry.Path] = row;
            }

            return row;
        }

        var matching = entries.Where(e => e.Matches(LibrarySearch)).ToList();
        var presets = matching.Where(e => e.IsPreset).OrderBy(e => e.Name, NaturalComparer.Instance).ToList();
        var dcps = LibrarySorting.Sort(matching.Where(e => !e.IsPreset), sort).ToList();
        SyncItems(Presets, presets.Select(RowFor).ToList());
        SyncItems(LibraryDcps, searching ? dcps.Select(RowFor).ToList() : []);

        // The tree is kept up to date while searching too, so it is as it was when the search is cleared
        RevealOpenFile();
        var tree = Core.Library.LibraryTree.Build(_snapshot, sort);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SyncFolder(LibraryTree, tree, RowFor, folders);
        foreach (var gone in _folderItems.Keys.Where(k => !folders.Contains(k)).ToList())
        {
            _folderItems.Remove(gone);
        }

        _rows = rows;
        if (SelectedLibraryItem is LibraryFolderItem selectedFolder && !folders.Contains(selectedFolder.RelativePath)
            || SelectedLibraryItem is LibraryRow selectedRow && !rows.ContainsKey(selectedRow.Path))
        {
            SelectedLibraryItem = null;
        }

        OnPropertyChanged(nameof(LibraryTargetText));
        UpdateOpenRows();

        var allPresets = entries.Count(e => e.IsPreset);
        var allDcps = entries.Count - allPresets;
        PresetsHeader = searching ? Loc.Format("Library_PresetsHeaderFiltered", presets.Count, allPresets) : Loc.Format("Library_PresetsHeader", allPresets);
        DcpsHeader = searching ? Loc.Format("Library_DcpsHeaderFiltered", dcps.Count, allDcps) : Loc.Format("Library_DcpsHeader", allDcps);
        PresetsEmptyText = presets.Count > 0 || _library is null ? ""
            : allPresets == 0 ? Loc.Format("Library_NoPresets", Loc.Get("Library_SaveAsPreset"))
            : Loc.Get("Library_NoPresetMatch");
        DcpsEmptyText = dcps.Count > 0 || _library is null ? ""
            : allDcps == 0 ? Loc.Format("Library_NoDcps", Loc.Get("Library_New"))
            : Loc.Get("Library_NoDcpMatch");
    }

    private void SyncFolder(ObservableCollection<LibraryTreeItem> target, LibraryFolderNode node, Func<LibraryEntry, LibraryRow> rowFor,
        HashSet<string> folders)
    {
        var items = new List<LibraryTreeItem>(node.Folders.Count + node.Entries.Count);
        foreach (var folder in node.Folders)
        {
            if (!_folderItems.TryGetValue(folder.RelativePath, out var item))
            {
                item = new LibraryFolderItem(folder.RelativePath, OnFolderExpansionChanged)
                {
                    IsExpanded = _expandedFolders.Contains(folder.RelativePath),
                };
                _folderItems[folder.RelativePath] = item;
            }

            folders.Add(folder.RelativePath);
            item.Update(folder);
            SyncFolder(item.Children, folder, rowFor, folders);
            items.Add(item);
        }

        items.AddRange(node.Entries.Select(rowFor));
        SyncItems(target, items);
    }

    private void OnFolderExpansionChanged(LibraryFolderItem folder)
    {
        if (folder.IsExpanded)
        {
            _expandedFolders.Add(folder.RelativePath);
        }
        else
        {
            _expandedFolders.Remove(folder.RelativePath);
        }
    }

    /// <summary>Opens the folders down to <paramref name="relativeFolder"/> in the tree.</summary>
    private void ExpandFolder(string relativeFolder)
    {
        var parts = relativeFolder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i <= parts.Length; i++)
        {
            var path = string.Join('/', parts.Take(i));
            _expandedFolders.Add(path);
            if (_folderItems.TryGetValue(path, out var item))
            {
                item.IsExpanded = true;
            }
        }
    }

    /// <summary>Once per opened library file: its folders are opened in the tree, so it can be seen there.</summary>
    private void RevealOpenFile()
    {
        if (_library is not { } library || FilePath is not { } path || !library.Contains(path) || SamePath(path, _revealedPath))
        {
            return;
        }

        _revealedPath = path;
        var folder = Path.GetDirectoryName(Path.GetRelativePath(library.Folder, path))?.Replace('\\', '/') ?? "";
        ExpandFolder(folder);
    }

    /// <summary>
    /// Makes <paramref name="target"/> hold <paramref name="items"/> in that order with as few changes as possible, never
    /// holding an item twice, so the selection and the scroll position survive a refresh.
    /// </summary>
    private static void SyncItems<T>(ObservableCollection<T> target, IReadOnlyList<T> items) where T : class
    {
        var wanted = new HashSet<T>(items, ReferenceEqualityComparer.Instance);
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], items[i]))
            {
                continue;
            }

            var at = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], items[i]))
                {
                    at = j;
                    break;
                }
            }

            if (at >= 0)
            {
                target.Move(at, i);
            }
            else
            {
                target.Insert(i, items[i]);
            }
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private void UpdateOpenRows()
    {
        foreach (var row in _rows.Values)
        {
            row.IsOpen = SamePath(row.Path, FilePath);
        }
    }

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
            SetStatus("Status_LegacyBackupFailed", Path.GetFileName(path), ex.Message);
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
            SetStatus("Status_FileProblem", ex.Message);
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
            SetStatus("Status_FileProblem", ex.Message);
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
            SetStatus("Status_FileProblem", ex.Message);
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
                    SetStatus("Status_UploadNotRecorded", ex.Message);
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
                        SetStatus("Status_FileGoneLibrary", name);
                    }
                    else
                    {
                        SetStatus("Status_FileGone", name, Loc.MenuName("Menu_Save"));
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
                    SetStatus("Status_ChangedUnreadable", name, ex.Message);
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
            SetStatus("Status_ChangedKeptLibrary", name);
        }
        else
        {
            SetStatus("Status_ChangedKept", name, Loc.MenuName("Menu_Save"));
        }
    }

    // ---------------------------------------------------------------- panel actions

    /// <summary>Click or Enter on a row: opens the DCP (or the preset itself, for editing it).</summary>
    [RelayCommand]
    private async Task OpenLibraryEntry(LibraryRow? row)
    {
        if (row is null || IsBusy || SamePath(row.Path, FilePath))
        {
            return;
        }

        if (row.Entry.Error is { } error)
        {
            SetStatus("Status_CannotOpenEntry", row.Name, error);
            return;
        }

        if (await LeaveDocumentAsync() && await TryOpenAsync(row.Path, quiet: true))
        {
            RememberFile(row.Path);
            _ = RefreshLibraryAsync();
        }
    }

    [RelayCommand]
    private async Task NewFromPreset(LibraryRow? row)
    {
        if (row is null || IsBusy || _library is null)
        {
            return;
        }

        if (!await LeaveDocumentAsync())
        {
            return;
        }

        Dcp dcp;
        try
        {
            // Read after leaving: when the preset itself was open, its last changes are in the file now
            dcp = DcpLibrary.CreateFromPreset(row.Path);
        }
        catch (DcpFileException ex)
        {
            SetStatus("Status_FileProblem", ex.Message);
            return;
        }

        StartNewDocument(dcp, NewDocumentFolder(), dirty: false, "Status_NewFromPreset", row.Name);
    }

    /// <summary>Where a new DCP gets its file: the folder selected in the library panel.</summary>
    private string? NewDocumentFolder() => _library?.FolderPath(LibraryTargetFolder);

    // ---------------------------------------------------------------- folders

    [RelayCommand]
    private void NewFolder(LibraryFolderItem? parent)
    {
        if (_library is null || IsBusy)
        {
            return;
        }

        var parentPath = parent?.RelativePath ?? LibraryTargetFolder;
        OpenPrompt(Loc.Format("Prompt_NewFolderLabel", FolderDisplayName(parentPath)), "", Loc.Get("Prompt_Create"),
            name => _ = CreateFolderAsync(parentPath, name));
    }

    private async Task CreateFolderAsync(string parentPath, string name)
    {
        if (_library is not { } library)
        {
            return;
        }

        string created;
        try
        {
            created = library.CreateFolder(parentPath, name);
        }
        catch (DcpFileException ex)
        {
            SetStatus("Status_FileProblem", ex.Message);
            return;
        }

        ExpandFolder(parentPath);
        SetStatus("Status_FolderCreated", created);
        await RefreshLibraryAsync();
        if (_folderItems.TryGetValue(created, out var item))
        {
            SelectedLibraryItem = item;
        }
    }

    /// <summary>The folders a DCP can be moved to: the library itself and every folder in the tree, as (path, display name).</summary>
    public IReadOnlyList<(string Path, string Display)> MoveTargets() =>
        [("", FolderDisplayName("")),
         .. _folderItems.Keys.Order(NaturalComparer.Instance).Select(f => (f, f))];

    public async Task MoveEntryAsync(LibraryRow row, string targetFolder)
    {
        if (IsBusy || _library is not { } library || row.IsPreset)
        {
            return;
        }

        var path = row.Path;
        var isOpen = SamePath(path, FilePath);
        if (isOpen)
        {
            // What is on screen goes into the file first, at its old place
            CommitPendingEdits?.Invoke();
            if (!SaveLibraryDocument(leaving: false))
            {
                return;
            }

            path = FilePath!;
        }

        try
        {
            var target = library.Move(path, targetFolder);
            ReplaceRecent(path, target);
            if (isOpen)
            {
                FilePath = target;
                _revealedPath = target;
                UpdateNameFollowsHeader();
                ScheduleRecovery();
            }

            ExpandFolder(targetFolder);
            SetStatus("Status_Moved", Path.GetFileName(target), FolderDisplayName(targetFolder));
        }
        catch (DcpFileException ex)
        {
            SetStatus("Status_FileProblem", ex.Message);
        }

        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private void OpenFolderInFileManager(LibraryFolderItem? folder)
    {
        if (_library is { } library)
        {
            OpenWithShell(library.FolderPath(folder?.RelativePath ?? ""));
        }
    }

    /// <summary>A new DCP for the next episode of <paramref name="row"/>, or of the open DCP when null.</summary>
    [RelayCommand]
    private async Task DuplicateNextEpisode(LibraryRow? row)
    {
        if (IsBusy)
        {
            return;
        }

        if (row is null || SamePath(row.Path, FilePath))
        {
            await NewNextEpisode();
            return;
        }

        if (!await LeaveDocumentAsync())
        {
            return;
        }

        Dcp next;
        try
        {
            next = DcpLibrary.CreateNextEpisode(row.Path);
        }
        catch (DcpFileException ex)
        {
            SetStatus("Status_FileProblem", ex.Message);
            return;
        }

        StartNewDocument(next, Path.GetDirectoryName(row.Path), dirty: true, "Status_NextEpisodeOf", row.Name);
    }

    /// <summary>An exact copy (never uploaded) of <paramref name="row"/>, or of the open DCP when null, which is then opened.</summary>
    [RelayCommand]
    private async Task DuplicateDcp(LibraryRow? row)
    {
        if (IsBusy || _library is not { } library)
        {
            return;
        }

        var isCurrent = row is null || SamePath(row.Path, FilePath);
        if (isCurrent && !IsLibraryDocument)
        {
            // Not a library file: the copy is made from what is on screen
            CommitPendingEdits?.Invoke();
            SyncLines();
            var clone = DcpFile.Clone(_dcp);
            clone.Upload = null;
            if (await LeaveDocumentAsync())
            {
                StartNewDocument(clone, null, dirty: true, "Status_CopyOfDcp");
            }

            return;
        }

        // Writes the open DCP first, so a copy of it has everything
        if (!await LeaveDocumentAsync())
        {
            return;
        }

        var source = isCurrent ? FilePath! : row!.Path;
        try
        {
            var copy = library.Duplicate(source);
            if (await TryOpenAsync(copy, quiet: true))
            {
                RememberFile(copy);
                SetStatus("Status_Duplicated", Path.GetFileName(source), RelativeName(copy));
            }
        }
        catch (DcpFileException ex)
        {
            SetStatus("Status_FileProblem", ex.Message);
        }

        _ = RefreshLibraryAsync();
    }

    [RelayCommand]
    private void RenameEntry(LibraryRow? row)
    {
        var path = row?.Path ?? (IsLibraryDocument ? FilePath : null);
        if (path is null || IsBusy)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        OpenPrompt(Loc.Format("Prompt_RenameLabel", name), name, Loc.Get("Prompt_Rename"), newName => RenameFile(path, newName));
    }

    private void RenameFile(string path, string newName)
    {
        var isOpen = SamePath(path, FilePath);
        if (isOpen)
        {
            CommitPendingEdits?.Invoke();
            if (!SaveLibraryDocument(leaving: false))
            {
                return;
            }

            path = FilePath!; // saving may have given it the name that fits the header
        }

        try
        {
            var target = DcpLibrary.Rename(path, newName);
            ReplaceRecent(path, target);
            if (isOpen)
            {
                FilePath = target;
                UpdateNameFollowsHeader();
            }

            SetStatus("Status_Renamed", Path.GetFileName(path), Path.GetFileName(target));
        }
        catch (DcpFileException ex)
        {
            SetStatus("Status_FileProblem", ex.Message);
        }

        _ = RefreshLibraryAsync();
    }

    [RelayCommand]
    private void SaveAsPreset()
    {
        if (_library is null || IsBusy)
        {
            return;
        }

        CommitPendingEdits?.Invoke();
        SyncLines();
        var dcp = _dcp;
        var name = dcp.TitoloItaliano.Trim();
        OpenPrompt(Loc.Get("Prompt_PresetLabel"),
            name, Loc.Get("Prompt_SavePreset"), presetName => SavePreset(dcp, presetName));
    }

    private void SavePreset(Dcp dcp, string name)
    {
        if (_library is not { } library)
        {
            return;
        }

        if (ReferenceEquals(dcp, _dcp))
        {
            SyncLines();
        }

        var presetPath = library.PresetPath(name);
        var existed = File.Exists(presetPath);
        if (!KeepLegacyOriginal(presetPath, out var original))
        {
            return;
        }

        try
        {
            var path = library.SavePreset(dcp, name);
            SetStatus(existed ? "Status_PresetReplaced" : "Status_PresetSaved", Path.GetFileNameWithoutExtension(path));
            ReportLegacyConverted(true, path, original);
        }
        catch (DcpFileException ex)
        {
            SetStatus("Status_FileProblem", ex.Message);
        }

        _ = RefreshLibraryAsync();
    }

    [RelayCommand]
    private void ShowInFileManager(LibraryRow? row)
    {
        var path = row?.Path ?? FilePath;
        if (path is null)
        {
            SetStatus("Status_NoFileYet");
            return;
        }

        FileManager.Reveal(path);
    }

    [RelayCommand]
    private async Task DeleteEntry(LibraryRow? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        var name = Path.GetFileName(row.Path);
        var isOpen = SamePath(row.Path, FilePath);
        var recycle = FileManager.DeletesToRecycleBin;
        var question = (row.IsPreset, recycle) switch
        {
            (true, true) => Loc.Format("Msg_RecyclePreset", name),
            (true, false) => Loc.Format("Msg_DeletePreset", name),
            (false, true) => Loc.Format("Msg_RecycleDcp", name),
            (false, false) => Loc.Format("Msg_DeleteDcp", name),
        };
        var text = string.Join(" ", new[]
        {
            question,
            isOpen ? Loc.Get("Msg_DeleteOpenNote") : "",
            Loc.Get(recycle ? "Msg_RecycleNote" : "Msg_DeleteNote"),
        }.Where(s => s.Length > 0));
        if (!await _dialogs.Confirm(Loc.Get(row.IsPreset ? "Dlg_DeletePreset" : "Dlg_DeleteDcp"), text,
                Loc.Get(recycle ? "Dlg_MoveToRecycleBin" : "Dlg_DeletePermanently"), Loc.Get("Common_Cancel")))
        {
            return;
        }

        // Nothing may write the file again once it is gone
        _recoveryTimer.Stop();
        try
        {
            FileManager.Delete(row.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Status_NotDeleted", name, ex.Message);
            ScheduleRecovery();
            return;
        }

        if (_settings.RecentFiles.RemoveAll(f => SamePath(f, row.Path)) > 0)
        {
            SaveSettings();
            OnPropertyChanged(nameof(RecentFiles));
        }

        if (isOpen)
        {
            LoadDcp(_settings.Company.CreateDcp(), null, false);
        }

        SetStatus(recycle ? "Status_MovedToRecycleBin" : "Status_Deleted", name);
        _ = RefreshLibraryAsync();
    }

    // ---------------------------------------------------------------- name prompt (inline in the panel, not a dialog)

    private void OpenPrompt(string label, string value, string action, Action<string> accept)
    {
        _promptAccept = accept;
        PromptLabel = label;
        PromptValue = value;
        PromptAction = action;
        IsPromptOpen = true;
    }

    [RelayCommand]
    private void AcceptPrompt()
    {
        var value = PromptValue.Trim();
        if (value.Length == 0)
        {
            return;
        }

        var accept = _promptAccept;
        CancelPrompt();
        accept?.Invoke(value);
    }

    [RelayCommand]
    private void CancelPrompt()
    {
        _promptAccept = null;
        IsPromptOpen = false;
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
        OnPropertyChanged(nameof(RecentFiles));
    }
}
