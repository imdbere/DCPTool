using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private IReadOnlyList<LibraryEntry> _entries = [];
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

    private Action<string>? _promptAccept;

    public ObservableCollection<LibraryRow> Presets { get; } = [];
    public ObservableCollection<LibraryRow> LibraryDcps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLibraryPanel), nameof(ShowLibraryStrip))]
    private bool _isLibraryPanelOpen = true;

    [ObservableProperty] private string _librarySearch = "";
    [ObservableProperty] private string _libraryNotice = "";
    [ObservableProperty] private string _presetsEmptyText = "";
    [ObservableProperty] private string _dcpsEmptyText = "";
    [ObservableProperty] private string _presetsHeader = "Presets";
    [ObservableProperty] private string _dcpsHeader = "DCPs";

    [ObservableProperty] private bool _isPromptOpen;
    [ObservableProperty] private string _promptLabel = "";
    [ObservableProperty] private string _promptValue = "";
    [ObservableProperty] private string _promptAction = "OK";

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

    partial void OnLibrarySearchChanged(string value) => ApplyLibraryFilter();

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
            ProgressText = $"The library folder \"{folder}\" cannot be used: {ex.Message}";
            return null;
        }
    }

    private void ApplyLibrarySetting()
    {
        _library = CreateLibrary(_settings.LibraryFolder);
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
        var folder = await _dialogs.PickFolder("Choose the library folder for your DCPs");
        if (folder is null)
        {
            return;
        }

        _settings.LibraryFolder = folder;
        SaveSettings();
        ApplyLibrarySetting();
        if (_library is not null)
        {
            ProgressText = $"Library folder: {_library.Folder}";
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
                    _entries = [];
                    LibraryNotice = "";
                }
                else
                {
                    var (entries, exists) = await Task.Run(() => (library.Scan(), library.Exists));
                    if (!ReferenceEquals(library, _library))
                    {
                        _scanAgain = true; // the folder was changed meanwhile
                        continue;
                    }

                    _entries = entries;
                    LibraryNotice = exists ? "" : "The library folder does not exist (any more). Choose another one in Settings, or it is created when a DCP is saved.";
                }

                ApplyLibraryFilter();
            }
            while (_scanAgain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DcpFileException)
        {
            ProgressText = "Could not read the library folder: " + ex.Message;
        }
        finally
        {
            _scanning = false;
        }
    }

    private void ApplyLibraryFilter()
    {
        var matching = _entries.Where(e => e.Matches(LibrarySearch)).ToList();
        var presets = matching.Where(e => e.IsPreset).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var dcps = matching.Where(e => !e.IsPreset).ToList();
        Sync(Presets, presets);
        Sync(LibraryDcps, dcps);
        UpdateOpenRows();

        var searching = !string.IsNullOrWhiteSpace(LibrarySearch);
        var allPresets = _entries.Count(e => e.IsPreset);
        var allDcps = _entries.Count - allPresets;
        PresetsHeader = searching ? $"Presets ({presets.Count} of {allPresets})" : $"Presets ({allPresets})";
        DcpsHeader = searching ? $"DCPs ({dcps.Count} of {allDcps})" : $"DCPs ({allDcps})";
        PresetsEmptyText = presets.Count > 0 || _library is null ? ""
            : allPresets == 0 ? "No presets yet. \"Save as preset\" keeps the programme information of the open DCP for its next episodes."
            : "No preset matches the search.";
        DcpsEmptyText = dcps.Count > 0 || _library is null ? ""
            : allDcps == 0 ? "No DCPs in the library yet. Start one with \"New\" or by clicking a preset."
            : "No DCP matches the search.";
    }

    /// <summary>Updates the list in place, so the selection and the scroll position survive a refresh.</summary>
    private static void Sync(ObservableCollection<LibraryRow> rows, List<LibraryEntry> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (i >= rows.Count)
            {
                rows.Add(new LibraryRow(entries[i]));
            }
            else if (rows[i].Entry != entries[i])
            {
                rows[i] = new LibraryRow(entries[i]);
            }
        }

        while (rows.Count > entries.Count)
        {
            rows.RemoveAt(rows.Count - 1);
        }
    }

    private void UpdateOpenRows()
    {
        foreach (var row in Presets.Concat(LibraryDcps))
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

    private void StartNewDocument(Dcp dcp, string? folder, bool dirty, string status)
    {
        LoadDcp(dcp, null, dirty);
        _newFileFolder = folder;
        if (status.Length > 0)
        {
            ProgressText = status;
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
        FollowHeaderName(library);
        return WriteLibraryFile(FilePath, isNew: false);
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
                ProgressText = $"Saved the new DCP in the library as {RelativeName(path)}.";
            }

            _ = RefreshLibraryAsync();
            return true;
        }
        catch (DcpFileException ex)
        {
            ProgressText = ex.Message;
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
            ProgressText = $"Renamed the file to {Path.GetFileName(moved)} to match the DCP.";
        }
        catch (DcpFileException ex)
        {
            _nameFollowsHeader = false;
            ProgressText = ex.Message;
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

        try
        {
            DcpFile.Save(path, session.Dcp);
            ProgressText = $"Saved the DCP from the last session as {RelativeName(path)}.";
            return true;
        }
        catch (DcpFileException ex)
        {
            ProgressText = ex.Message;
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
                    ProgressText = "The upload could not be recorded in the file: " + ex.Message;
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
                    ProgressText = $"{name} was moved or deleted outside DCP Tool. The DCP stays open"
                                   + (IsLibraryDocument ? " and is written there again when you change it." : "; Save writes it again.");
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
                ProgressText = $"Reloaded {name}: it was changed outside DCP Tool.";
                _ = RefreshLibraryAsync();
            }
            catch (DcpFileException ex)
            {
                if (_externalNotice != modified)
                {
                    _externalNotice = modified;
                    ProgressText = $"{name} was changed outside DCP Tool and cannot be read now: {ex.Message}";
                }
            }

            return;
        }

        if (_externalNotice == modified)
        {
            return;
        }

        _externalNotice = modified;
        ProgressText = IsLibraryDocument
            ? $"{name} was changed outside DCP Tool. Your changes are kept and replace that version."
            : $"{name} was changed outside DCP Tool. Your unsaved changes are kept; Save writes them over that version.";
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
            ProgressText = $"{row.Name} cannot be opened: {error}";
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
            ProgressText = ex.Message;
            return;
        }

        StartNewDocument(dcp, null, dirty: false, $"New DCP from the preset \"{row.Name}\".");
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
            ProgressText = ex.Message;
            return;
        }

        StartNewDocument(next, Path.GetDirectoryName(row.Path), dirty: true, $"Next episode of {row.Name}.");
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
                StartNewDocument(clone, null, dirty: true, "Copy of the DCP.");
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
                ProgressText = $"Duplicated {Path.GetFileName(source)} as {RelativeName(copy)}.";
            }
        }
        catch (DcpFileException ex)
        {
            ProgressText = ex.Message;
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
        OpenPrompt($"New name for \"{name}\"", name, "Rename", newName => RenameFile(path, newName));
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

            ProgressText = $"Renamed {Path.GetFileName(path)} to {Path.GetFileName(target)}.";
        }
        catch (DcpFileException ex)
        {
            ProgressText = ex.Message;
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
        OpenPrompt("Preset name. The preset keeps the programme information of the open DCP, without lines, episode and dates.",
            name, "Save preset", presetName => SavePreset(dcp, presetName));
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

        var existed = File.Exists(Path.Combine(library.PresetsFolder, DcpLibrary.SafeFileName(name) + DcpFile.Extension));
        try
        {
            var path = library.SavePreset(dcp, name);
            ProgressText = $"{(existed ? "Replaced" : "Saved")} the preset \"{Path.GetFileNameWithoutExtension(path)}\".";
        }
        catch (DcpFileException ex)
        {
            ProgressText = ex.Message;
        }

        _ = RefreshLibraryAsync();
    }

    [RelayCommand]
    private void ShowInFileManager(LibraryRow? row)
    {
        var path = row?.Path ?? FilePath;
        if (path is null)
        {
            ProgressText = "This DCP has no file yet.";
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
        var what = row.IsPreset ? "preset" : "DCP";
        var isOpen = SamePath(row.Path, FilePath);
        var openNote = isOpen ? " It is the DCP that is open now and will be closed." : "";
        var recycle = FileManager.DeletesToRecycleBin;
        var text = recycle
            ? $"Move the {what} \"{name}\" to the Recycle Bin?{openNote} You can restore it from there."
            : $"Delete the {what} \"{name}\" permanently?{openNote} This cannot be undone.";
        if (!await _dialogs.Confirm($"Delete {what}", text, recycle ? "Move to Recycle Bin" : "Delete permanently", "Cancel"))
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
            ProgressText = $"{name} was not deleted: {ex.Message}";
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

        ProgressText = recycle ? $"Moved {name} to the Recycle Bin." : $"Deleted {name}.";
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
