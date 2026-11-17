using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DcpTool.App.Localisation;
using DcpTool.Core.Library;

namespace DcpTool.App.ViewModels;

/// <summary>An item of the library tree: a folder or a DCP (<see cref="LibraryRow"/>).</summary>
public abstract partial class LibraryTreeItem : ObservableObject
{
    /// <summary>Only folders expand; bound two-way to the tree item.</summary>
    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => ExpansionChanged();

    protected virtual void ExpansionChanged()
    {
    }
}

/// <summary>
/// A folder in the library tree. The same object stays for the same folder across scans, so its expansion
/// and the tree's selection survive a refresh.
/// </summary>
public sealed partial class LibraryFolderItem(string relativePath, Action<LibraryFolderItem> expansionChanged) : LibraryTreeItem
{
    public string RelativePath { get; } = relativePath;

    public string Name => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];

    public ObservableCollection<LibraryTreeItem> Children { get; } = [];

    /// <summary>Number of DCPs in this folder and below it.</summary>
    [ObservableProperty] private string _countText = "";

    [ObservableProperty] private bool _isEmpty;

    public string Tip => RelativePath;

    public void Update(LibraryFolderNode node)
    {
        var count = node.AllEntries.Count();
        CountText = count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        IsEmpty = count == 0;
    }

    protected override void ExpansionChanged() => expansionChanged(this);
}

/// <summary>One choice of the sort box in the library panel.</summary>
public sealed record LibrarySortOption(LibrarySort Sort, string Label)
{
    public override string ToString() => Label;

    public static IReadOnlyList<LibrarySortOption> All { get; } =
    [
        new(LibrarySort.RecentlyChanged, Loc.Get("Library_SortRecent")),
        new(LibrarySort.BroadcastDate, Loc.Get("Library_SortBroadcast")),
        new(LibrarySort.Title, Loc.Get("Library_SortTitle")),
        new(LibrarySort.Name, Loc.Get("Library_SortName")),
    ];
}
