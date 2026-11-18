using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DcpTool.App.Localisation;
using DcpTool.App.Services;
using DcpTool.App.Views;
using DcpTool.Core.Library;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;

namespace DcpTool.App.ViewModels;

/// <summary>
/// The library: all DCPs as .dcp files in one folder the user chooses. The app has no browser for it (Explorer does
/// that, through "Bibliotheksordner öffnen"); the drawer shows the presets ("Sendungen") and the DCPs edited last.
/// Nothing is saved automatically: a .dcp file is only written by Speichern, Speichern unter… and the Speichern of the
/// prompt shown before leaving a DCP with unsaved changes. The one exception is the save right after an upload that
/// RAI saved, so the upload record cannot get lost (<see cref="SaveAfterUploadAsync"/>). A new DCP is saved into the library as
/// <c>&lt;Sendung&gt;/&lt;year&gt;/&lt;Sendung&gt; &lt;Puntata&gt; &lt;date&gt;.dcp</c> (<see cref="DcpLibrary.SuggestPath"/>).
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

    // Modified time of the open file when this app last read or wrote it. A newer one means someone else changed it.
    private DateTime _lastWriteUtc;

    // The external change already reported, so it is said once and not on every activation
    private DateTime? _externalNotice;

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
        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(HasNoLibrary));
        OnPropertyChanged(nameof(HasNoPresets));
        OnPropertyChanged(nameof(LibraryFolderText));
        OnFileStateChanged();
        Settings.Refresh();
        _ = RefreshLibraryAsync();
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
        if (!await LeaveDocumentAsync())
        {
            return;
        }

        Dcp next;
        try
        {
            // The open DCP as it is on screen, saved or not; another one from its file
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

        StartNewDocument(next, dirty: false, "Status_NextEpisodeOf", item.Label);
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
        StartNewDocument(dcp, dirty: false, "Status_NewFromPreset", preset.Name);
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

    /// <summary>"Als Sendung speichern" (▾ of Speichern): the same as the + next to "Sendungen", with the drawer shown.</summary>
    [RelayCommand]
    private void SaveAsPreset()
    {
        if (_library is null || IsBusy)
        {
            return;
        }

        IsDrawerOpen = true;
        if (IsGuidePage)
        {
            Page = AppPage.Songs;
        }

        if (!IsPresetEditorOpen || _presetRenaming is not null)
        {
            BeginNewPreset();
        }
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

            // A new DCP without a Sendung belongs to the one made from it: that is where it will be saved.
            // Not an edit of a saved DCP, so nothing becomes unsaved by this.
            if (!replacing && FilePath is null && string.IsNullOrWhiteSpace(_dcp.Sendung))
            {
                _dcp.Sendung = Path.GetFileNameWithoutExtension(path);
            }
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

    // ---------------------------------------------------------------- leaving a DCP

    /// <summary>
    /// Before another DCP replaces the open one (Recent, a Sendung, a new DCP, a dropped .dcp, closing the window): when
    /// it has unsaved changes the user chooses Speichern, Nicht speichern or Abbrechen. False: stay with the open DCP.
    /// </summary>
    internal async Task<bool> LeaveDocumentAsync()
    {
        CommitPendingEdits?.Invoke();
        if (!IsDirty)
        {
            return true;
        }

        SyncLines();
        var plan = SavePlan.For(FilePath, _library, _dcp);
        var text = plan.Kind == SaveKind.NewInLibrary
            ? Loc.Format("Msg_SaveNewDcp", RelativeName(plan.Path!))
            : Loc.Format("Msg_SaveChanges", DocumentName);
        switch (await _dialogs.AskToSave(Loc.Get("Dlg_UnsavedChanges"), text, Loc.Get("Save_Button"), Loc.Get("Dlg_DontSave"), Loc.Get("Common_Cancel")))
        {
            case SaveChoice.Save:
                return await SaveDocumentAsync();
            case SaveChoice.DontSave:
                // Discarded on purpose: the snapshot must not bring the changes back on the next start
                DiscardRecovery();
                return true;
            default:
                return false;
        }
    }

    /// <param name="dirty">True when the new DCP holds something that is in no file (a copy of songs); false for one made only of defaults.</param>
    /// <param name="statusKey">Status text to show (with <paramref name="statusArgs"/>), or null for none.</param>
    private void StartNewDocument(Dcp dcp, bool dirty, string? statusKey, params object?[] statusArgs)
    {
        LoadDcp(dcp, null, dirty);
        if (statusKey is not null)
        {
            SetStatus(statusKey, statusArgs);
        }
    }

    /// <summary>"Kopie als neuer DCP" (▾ of Speichern): a new, unsaved DCP with the same header and songs, never uploaded.</summary>
    [RelayCommand]
    private async Task CopyAsNew()
    {
        if (IsBusy)
        {
            return;
        }

        CommitPendingEdits?.Invoke();
        SyncLines();
        var copy = DcpLibrary.CreateCopy(_dcp);
        var from = FilePath is null ? DocumentName : Path.GetFileNameWithoutExtension(FilePath);
        if (!await LeaveDocumentAsync())
        {
            return;
        }

        StartNewDocument(copy, dirty: true, "Status_CopiedAsNew", from);
        Page = AppPage.Songs;
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
                    Report(ConsoleLevel.Warning, "Status_FileGone", name, Loc.Get("Save_Button"));
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
        Report(ConsoleLevel.Warning, "Status_ChangedKept", name, Loc.Get("Save_Button"));
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

    private string RelativeName(string path) =>
        _library is { } library && library.Contains(path) ? Path.GetRelativePath(library.Folder, path) : Path.GetFileName(path);
}
