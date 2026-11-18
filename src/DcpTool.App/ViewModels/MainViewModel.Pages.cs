using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DcpTool.App.Views.Controls;

namespace DcpTool.App.ViewModels;

/// <summary>What the middle of the window shows. The drawer, the programme strip and the two areas stay.</summary>
public enum AppPage
{
    /// <summary>The songs table (steps 2 and 3).</summary>
    Songs,

    /// <summary>How to add songs ("Wie?"), step 2.</summary>
    Guide,

    /// <summary>RAI and documents, step 4.</summary>
    Export,

    Settings,
}

/// <summary>What the Add area says.</summary>
public enum AddState
{
    Idle,
    Importing,

    /// <summary>Songs were added, with undo.</summary>
    Added,

    /// <summary>Some files were left out or incomplete, with "Warum?".</summary>
    Partial,
}

/// <summary>What is being dragged over the window.</summary>
public enum DragKind
{
    None,

    /// <summary>Songs, cue sheets or folders: the Add area becomes the drop target.</summary>
    Songs,

    /// <summary>A .dcp: the whole window becomes the drop target, the DCP is opened.</summary>
    Dcp,
}

/// <summary>
/// The window is the workflow (board "Plan"): the drawer is step 1, the Add area step 2, the table step 3 and the
/// Export area step 4. The highlight shows the current step; the areas never move. The guide, the export page and
/// the settings replace only the middle.
/// </summary>
public sealed partial class MainViewModel
{
    // Lines before the last import, for "Rückgängig"
    private List<LineRow>? _undoLines;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSongsPage), nameof(IsGuidePage), nameof(IsExportPage), nameof(IsSettingsPage),
        nameof(OpenStep), nameof(IsAddStep), nameof(IsExportStep), nameof(ShowDrawer), nameof(ShowDrawerLists))]
    private AppPage _page = AppPage.Songs;

    public bool IsSongsPage => Page == AppPage.Songs;
    public bool IsGuidePage => Page == AppPage.Guide;
    public bool IsExportPage => Page == AppPage.Export;
    public bool IsSettingsPage => Page == AppPage.Settings;

    /// <summary>The highlighted area: Add for the songs and the guide, Export for the export page, none for the settings.</summary>
    public OpenStep OpenStep => Page switch
    {
        AppPage.Songs or AppPage.Guide => OpenStep.Add,
        AppPage.Export => OpenStep.Export,
        _ => OpenStep.None,
    };

    public bool IsAddStep => OpenStep == OpenStep.Add;
    public bool IsExportStep => OpenStep == OpenStep.Export;

    partial void OnPageChanged(AppPage value)
    {
        if (value == AppPage.Export)
        {
            CommitPendingEdits?.Invoke();
            RunCheck(manual: false);
        }
    }

    // ---------------------------------------------------------------- drawer and programme strip

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDrawer))]
    private bool _isDrawerOpen = true;

    /// <summary>The guide lists its ways in the drawer, so the drawer is there while the guide is open.</summary>
    public bool ShowDrawer => IsDrawerOpen || IsGuidePage;

    public bool ShowDrawerLists => !IsGuidePage;

    [RelayCommand]
    private void ToggleDrawer() => IsDrawerOpen = !ShowDrawer;

    /// <summary>All fields of the programme, folded out below the strip.</summary>
    [ObservableProperty] private bool _isStripExpanded;

    [RelayCommand]
    private void ToggleStrip() => IsStripExpanded = !IsStripExpanded;

    // ---------------------------------------------------------------- navigation

    /// <summary>A click on the Add area: back to the songs.</summary>
    [RelayCommand]
    private void GoSongs() => Page = AppPage.Songs;

    [RelayCommand]
    private void GoExport() => Page = AppPage.Export;

    /// <summary>"Wie?" opens the guide; pressed again it closes it.</summary>
    [RelayCommand]
    private void ToggleGuide() => Page = IsGuidePage ? AppPage.Songs : AppPage.Guide;

    [RelayCommand]
    private void OpenSettings()
    {
        Settings.Refresh();
        Page = AppPage.Settings;
    }

    /// <summary>"Warum?" after a partly failed import: the troubleshooting of the way that fits the problem.</summary>
    [RelayCommand]
    private void OpenImportHelp()
    {
        Guide.ShowTroubleshooting();
        Page = AppPage.Guide;
    }

    // ---------------------------------------------------------------- the Add area

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddIdle), nameof(IsAddImporting), nameof(IsAddAdded), nameof(IsAddPartial), nameof(ShowAddButtons))]
    private AddState _addState;

    /// <summary>The line in the Add area after an import: "8 Titel hinzugefügt aus „Kompass_12.drt“".</summary>
    [ObservableProperty] private string _addNotice = "";

    /// <summary>The notice with the full file name, when <see cref="AddNotice"/> shows it shortened; else null (no tip).</summary>
    [ObservableProperty] private string? _addNoticeTip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraggingSongs), nameof(IsDraggingDcp), nameof(IsAddIdle), nameof(IsAddAdded),
        nameof(IsAddPartial), nameof(IsAddImporting), nameof(ShowAddButtons))]
    private DragKind _drag;

    /// <summary>File name of the .dcp being dragged over the window.</summary>
    [ObservableProperty] private string _draggedDcpName = "";

    public bool IsDraggingSongs => Drag == DragKind.Songs;
    public bool IsDraggingDcp => Drag == DragKind.Dcp;
    public bool IsAddIdle => !IsDraggingSongs && AddState == AddState.Idle;
    public bool IsAddImporting => !IsDraggingSongs && AddState == AddState.Importing;
    public bool IsAddAdded => !IsDraggingSongs && AddState == AddState.Added;
    public bool IsAddPartial => !IsDraggingSongs && AddState == AddState.Partial;
    public bool ShowAddButtons => !IsDraggingSongs && AddState != AddState.Importing;

    private void SetAddState(AddState state, string notice, string? tip = null)
    {
        AddState = state;
        AddNotice = notice;
        AddNoticeTip = tip;
    }

    /// <summary>A long file name for the Add area: its start and its end (with the extension), "…" in the middle.</summary>
    internal static string ShortFileName(string name, int max = 26)
    {
        if (name.Length <= max)
        {
            return name;
        }

        var tail = Math.Min(10, max / 2);
        return name[..(max - tail - 1)].TrimEnd() + "…" + name[^tail..];
    }

    private void ClearAddNotice()
    {
        if (AddState == AddState.Importing)
        {
            return;
        }

        _undoLines = null;
        AddState = AddState.Idle;
        AddNotice = "";
        AddNoticeTip = null;
    }
}
