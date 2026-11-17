namespace DcpTool.Core.Library;

/// <summary>How the library panel orders its DCPs and folders.</summary>
public enum LibrarySort
{
    /// <summary>File modified time, newest first. Folders: the one with the most recent change first.</summary>
    RecentlyChanged,

    /// <summary>Data trasmissione, newest first.</summary>
    BroadcastDate,

    /// <summary>Titolo italiano (the file name when there is none), A to Z.</summary>
    Title,

    /// <summary>File name, A to Z in natural order.</summary>
    Name,
}

public static class LibrarySorting
{
    /// <summary>The stable name of a sort mode in the settings file.</summary>
    public static string ToSetting(LibrarySort sort) => sort switch
    {
        LibrarySort.BroadcastDate => "broadcast",
        LibrarySort.Title => "title",
        LibrarySort.Name => "name",
        _ => "recent",
    };

    /// <summary>The sort mode of a settings value; anything unknown is the default, <see cref="LibrarySort.RecentlyChanged"/>.</summary>
    public static LibrarySort FromSetting(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "broadcast" => LibrarySort.BroadcastDate,
        "title" => LibrarySort.Title,
        "name" => LibrarySort.Name,
        _ => LibrarySort.RecentlyChanged,
    };

    public static IOrderedEnumerable<LibraryEntry> Sort(IEnumerable<LibraryEntry> entries, LibrarySort sort)
    {
        var natural = NaturalComparer.Instance;
        return sort switch
        {
            LibrarySort.BroadcastDate => entries
                .OrderByDescending(e => e.DataTrasmissione ?? DateOnly.MinValue)
                .ThenByDescending(e => e.Puntata ?? 0)
                .ThenBy(e => e.Name, natural)
                .ThenBy(e => e.RelativePath, natural),
            LibrarySort.Title => entries
                .OrderBy(TitleOf, natural)
                .ThenBy(e => e.Puntata ?? 0)
                .ThenBy(e => e.Name, natural)
                .ThenBy(e => e.RelativePath, natural),
            LibrarySort.Name => entries
                .OrderBy(e => e.Name, natural)
                .ThenBy(e => e.RelativePath, natural),
            _ => entries
                .OrderByDescending(e => e.Modified)
                .ThenBy(e => e.Name, natural)
                .ThenBy(e => e.RelativePath, natural),
        };
    }

    private static string TitleOf(LibraryEntry e) => string.IsNullOrWhiteSpace(e.Title) ? e.Name : e.Title.Trim();
}

/// <summary>A folder of the library with its sub-folders and DCPs, sorted. The root has an empty <see cref="RelativePath"/>.</summary>
public sealed class LibraryFolderNode
{
    public LibraryFolderNode(string relativePath, DateTime modified)
    {
        RelativePath = relativePath;
        Modified = modified;
    }

    /// <summary>Path below the library folder, with <c>/</c> separators. Empty for the library itself.</summary>
    public string RelativePath { get; }

    public string Name => RelativePath.Length == 0 ? "" : RelativePath[(RelativePath.LastIndexOf('/') + 1)..];

    /// <summary>Last write time of the directory itself.</summary>
    public DateTime Modified { get; }

    public List<LibraryFolderNode> Folders { get; } = [];

    public List<LibraryEntry> Entries { get; } = [];

    /// <summary>
    /// The most recent change in this folder: the newest DCP anywhere below it, or, for a folder without any
    /// DCP, the folder's own time.
    /// </summary>
    public DateTime LatestChange { get; internal set; }

    /// <summary>Every DCP in this folder and below it.</summary>
    public IEnumerable<LibraryEntry> AllEntries => Entries.Concat(Folders.SelectMany(f => f.AllEntries));

    public IEnumerable<LibraryFolderNode> AllFolders => Folders.SelectMany(f => f.AllFolders.Prepend(f));
}

public static class LibraryTree
{
    /// <summary>
    /// The folder tree of a scan: every folder (empty ones too) with its DCPs, presets left out.
    /// Folders come in natural name order; with <see cref="LibrarySort.RecentlyChanged"/> the folder with the most
    /// recent change comes first. DCPs are sorted by <paramref name="sort"/>.
    /// </summary>
    public static LibraryFolderNode Build(LibrarySnapshot snapshot, LibrarySort sort)
    {
        var root = new LibraryFolderNode("", DateTime.MinValue);
        var nodes = new Dictionary<string, LibraryFolderNode>(StringComparer.OrdinalIgnoreCase) { [""] = root };

        LibraryFolderNode NodeFor(string relative)
        {
            if (nodes.TryGetValue(relative, out var existing))
            {
                return existing;
            }

            var cut = relative.LastIndexOf('/');
            var parent = NodeFor(cut < 0 ? "" : relative[..cut]);
            var node = new LibraryFolderNode(relative, snapshot.FolderTimes.GetValueOrDefault(relative));
            parent.Folders.Add(node);
            nodes[relative] = node;
            return node;
        }

        foreach (var folder in snapshot.Folders)
        {
            if (!IsPresetFolder(folder))
            {
                NodeFor(folder);
            }
        }

        foreach (var entry in snapshot.Entries.Where(e => !e.IsPreset))
        {
            NodeFor(entry.Folder).Entries.Add(entry);
        }

        Arrange(root, sort);
        return root;
    }

    private static bool IsPresetFolder(string relative) =>
        relative.Equals(DcpLibrary.PresetsFolderName, StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(DcpLibrary.PresetsFolderName + "/", StringComparison.OrdinalIgnoreCase);

    private static void Arrange(LibraryFolderNode node, LibrarySort sort)
    {
        foreach (var folder in node.Folders)
        {
            Arrange(folder, sort);
        }

        var sorted = LibrarySorting.Sort(node.Entries, sort).ToList();
        node.Entries.Clear();
        node.Entries.AddRange(sorted);

        var latest = node.Entries.Select(e => e.Modified).Concat(node.Folders.Where(f => f.AllEntries.Any()).Select(f => f.LatestChange))
            .DefaultIfEmpty(DateTime.MinValue).Max();
        node.LatestChange = latest == DateTime.MinValue ? node.Modified : latest;

        var folders = sort == LibrarySort.RecentlyChanged
            ? node.Folders.OrderByDescending(f => f.LatestChange).ThenBy(f => f.Name, NaturalComparer.Instance).ToList()
            : node.Folders.OrderBy(f => f.Name, NaturalComparer.Instance).ToList();
        node.Folders.Clear();
        node.Folders.AddRange(folders);
    }
}
