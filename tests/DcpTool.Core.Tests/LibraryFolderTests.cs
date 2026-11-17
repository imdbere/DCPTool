using DcpTool.Core.Library;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;
using Xunit;

namespace DcpTool.Core.Tests;

/// <summary>Sub-folders, sorting, moving, the scan cache and the safety copy of files written by DCP Tool 0.x.</summary>
public sealed class LibraryFolderTests : IDisposable
{
    // A file as DCP Tool 0.x wrote it (DataContractJsonSerializer, no envelope)
    private const string LegacyJson =
        """{"DataTrasmissione":"\/Date(1574687754000+0100)\/","Durata":"PT25M48S","Lines":[{"AutoriString":"Otto Sieben","Durata":"PT2M10S","Esecutori":"Otto Sieben","Gensiae":0,"Marca":"Sonoton","Ruolo":1,"SiglaNum":"SCDV 52","TipoGenerazione":9,"Titolo":"ARCADES"}],"Matricola":486840,"OraInizio":"PT18H","Puntata":34,"ReteTrasmissione":3,"Sede":14,"Serie":0,"Sottotitolo":"","Struttura":"BZ\/16","TitoloItaliano":"ZIPP TV 2019","TitoloOriginale":"ZIPP TV 2019","Uorg":9016}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dcptool-libf-" + Guid.NewGuid().ToString("N"));
    private readonly DcpLibrary _library;

    public LibraryFolderTests()
    {
        Directory.CreateDirectory(_dir);
        _library = new DcpLibrary(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string relative, string title, int? puntata, DateOnly date, DateTime? modified = null)
    {
        var path = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));
        DcpFile.Save(path, new Dcp { TitoloItaliano = title, Puntata = puntata, DataTrasmissione = date });
        if (modified is { } time)
        {
            File.SetLastWriteTime(path, time);
        }

        return path;
    }

    [Fact]
    public void NaturalOrderReadsNumbersAsNumbers()
    {
        string[] names = ["zipp 10_20", "zipp 2_20", "Zipp 1_20", "zipp 2_19", "SpoZi 01_26", "SpoZi 1_26", "a", "zipp", "Ändern 3"];
        var sorted = names.Order(NaturalComparer.Instance).ToList();

        Assert.True(sorted.IndexOf("zipp 2_20") < sorted.IndexOf("zipp 10_20"));
        Assert.True(sorted.IndexOf("Zipp 1_20") < sorted.IndexOf("zipp 2_19"));
        Assert.True(sorted.IndexOf("zipp 2_19") < sorted.IndexOf("zipp 2_20"));
        Assert.True(sorted.IndexOf("SpoZi 1_26") < sorted.IndexOf("SpoZi 01_26"));
        Assert.True(sorted.IndexOf("zipp") < sorted.IndexOf("Zipp 1_20"));
        Assert.Equal("a", sorted[0]);
        Assert.Equal(0, NaturalComparer.Instance.Compare("x 12345678901234567890", "x 12345678901234567890"));
        Assert.True(NaturalComparer.Instance.Compare("x 99999999999999999999", "x 100000000000000000000") < 0);
    }

    [Fact]
    public void SortModes()
    {
        var now = DateTime.Now;
        Write("zipp 10_20.dcp", "Zipp", 10, new DateOnly(2020, 3, 10), now.AddDays(-3));
        Write("zipp 2_20.dcp", "Zipp", 2, new DateOnly(2020, 1, 14), now.AddDays(-1));
        Write("Kompass.dcp", "Kompass", 1, new DateOnly(2021, 5, 5), now.AddDays(-2));
        var entries = _library.ScanAll().Entries;

        string[] Names(LibrarySort sort) => LibrarySorting.Sort(entries, sort).Select(e => e.Name).ToArray();

        Assert.Equal(["zipp 2_20", "Kompass", "zipp 10_20"], Names(LibrarySort.RecentlyChanged));
        Assert.Equal(["Kompass", "zipp 10_20", "zipp 2_20"], Names(LibrarySort.BroadcastDate));
        Assert.Equal(["Kompass", "zipp 2_20", "zipp 10_20"], Names(LibrarySort.Title));
        Assert.Equal(["Kompass", "zipp 2_20", "zipp 10_20"], Names(LibrarySort.Name));

        Assert.Equal(LibrarySort.RecentlyChanged, LibrarySorting.FromSetting(null));
        Assert.Equal(LibrarySort.RecentlyChanged, LibrarySorting.FromSetting("nonsense"));
        foreach (var sort in Enum.GetValues<LibrarySort>())
        {
            Assert.Equal(sort, LibrarySorting.FromSetting(LibrarySorting.ToSetting(sort)));
        }
    }

    [Fact]
    public void TreeHasEveryFolderAndSortsThem()
    {
        var now = DateTime.Now;
        Write("DCP 2024/zipp 1_24.dcp", "Zipp", 1, new DateOnly(2024, 1, 1), now.AddDays(-10));
        Write("DCP 2024/Deboriada 2024/Deboriada 1_24.dcp", "Deboriada", 1, new DateOnly(2024, 2, 1), now.AddDays(-1));
        Write("DCP 10/old.dcp", "Old", 1, new DateOnly(2010, 2, 1), now.AddDays(-100));
        Write("top.dcp", "Top", 1, new DateOnly(2026, 2, 1), now);
        Write("Presets/Preset.dcp", "Preset", null, new DateOnly(2026, 2, 1));
        Directory.CreateDirectory(Path.Combine(_dir, "DCP 9", "Empty"));
        Directory.CreateDirectory(Path.Combine(_dir, ".hidden"));
        File.WriteAllText(Path.Combine(_dir, "DCP 2024", "form.pdf"), "not a dcp");

        var snapshot = _library.ScanAll();
        var byName = LibraryTree.Build(snapshot, LibrarySort.Name);
        var byRecent = LibraryTree.Build(snapshot, LibrarySort.RecentlyChanged);

        Assert.Equal(["DCP 9", "DCP 10", "DCP 2024"], byName.Folders.Select(f => f.Name));
        // A folder without DCPs counts with its own time: "DCP 9" was just made
        Assert.Equal(["DCP 9", "DCP 2024", "DCP 10"], byRecent.Folders.Select(f => f.Name));
        Directory.SetLastWriteTime(Path.Combine(_dir, "DCP 9"), now.AddDays(-50));
        Assert.Equal(["DCP 2024", "DCP 9", "DCP 10"], LibraryTree.Build(_library.ScanAll(), LibrarySort.RecentlyChanged).Folders.Select(f => f.Name));
        Assert.Equal(["top"], byName.Entries.Select(e => e.Name));
        Assert.Equal(["DCP 9/Empty"], byName.Folders[0].Folders.Select(f => f.RelativePath));
        Assert.Equal("Deboriada 2024", byName.Folders[2].Folders.Single().Name);
        Assert.DoesNotContain(byName.AllFolders, f => f.Name is "Presets" or ".hidden");
        Assert.Equal(4, byName.AllEntries.Count());
        Assert.Contains(snapshot.Entries, e => e.IsPreset);
    }

    [Fact]
    public void CreateFolderAndMove()
    {
        var path = Write("a.dcp", "A", 1, new DateOnly(2026, 1, 1));
        var modified = File.GetLastWriteTimeUtc(path);

        var folder = _library.CreateFolder("", "SpoZi: 2026");
        Assert.Equal("SpoZi 2026", folder);
        var sub = _library.CreateFolder(folder, "Teil 2");
        Assert.Equal("SpoZi 2026/Teil 2", sub);
        Assert.Throws<DcpFileException>(() => _library.CreateFolder("", "SpoZi 2026"));
        Assert.Throws<DcpFileException>(() => _library.CreateFolder("../..", "outside"));

        var moved = _library.Move(path, sub);
        Assert.Equal(Path.Combine(_dir, "SpoZi 2026", "Teil 2", "a.dcp"), moved);
        Assert.False(File.Exists(path));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(moved));
        Assert.Equal(moved, _library.Move(moved, sub));

        Write("a.dcp", "Another A", 1, new DateOnly(2026, 1, 1));
        Assert.Throws<DcpFileException>(() => _library.Move(moved, ""));
        Assert.True(File.Exists(moved));

        var entry = _library.ScanAll().Entries.Single(e => e.Path == moved);
        Assert.Equal("SpoZi 2026/Teil 2", entry.Folder);
    }

    [Fact]
    public void RescanReadsOnlyNewAndChangedFiles()
    {
        var a = Write("a.dcp", "A", 1, new DateOnly(2026, 1, 1));
        var b = Write("sub/b.dcp", "B", 1, new DateOnly(2026, 1, 1));

        Assert.Equal(2, _library.ScanAll().Entries.Count);
        Assert.Equal(2, _library.LastScanReads);

        var again = _library.ScanAll();
        Assert.Equal(0, _library.LastScanReads);
        Assert.Equal("A", again.Entries.Single(e => e.Path == a).Title);

        // Changed outside the app, even with the old modified time: the size gives it away
        var time = File.GetLastWriteTimeUtc(a);
        DcpFile.Save(a, new Dcp { TitoloItaliano = "A changed outside", Puntata = 1, DataTrasmissione = new DateOnly(2026, 1, 1) });
        File.SetLastWriteTimeUtc(a, time);
        var changed = _library.ScanAll();
        Assert.Equal(1, _library.LastScanReads);
        Assert.Equal("A changed outside", changed.Entries.Single(e => e.Path == a).Title);

        // Same size, new time
        DcpFile.Save(a, new Dcp { TitoloItaliano = "B changed outside", Puntata = 1, DataTrasmissione = new DateOnly(2026, 1, 1) });
        File.SetLastWriteTimeUtc(a, time.AddSeconds(5));
        Assert.Equal("B changed outside", _library.ScanAll().Entries.Single(e => e.Path == a).Title);
        Assert.Equal(1, _library.LastScanReads);

        // Deleted and moved files are gone, a moved one is read at its new place
        File.Delete(b);
        var moved = _library.Move(a, "sub");
        var after = _library.ScanAll();
        Assert.Equal([moved], after.Entries.Select(e => e.Path));
        Assert.Equal(1, _library.LastScanReads);

        // A file that becomes broken shows its error instead of the old content
        File.WriteAllText(moved, "{ broken");
        Assert.NotNull(_library.ScanAll().Entries.Single().Error);
    }

    [Fact]
    public void LegacyFilesAreCopiedOnceBeforeTheyAreOverwritten()
    {
        var backups = Path.Combine(_dir, ".backups");
        var legacy = Path.Combine(_dir, "DCP Tool 2019", "zipp 34_19.dcp");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, LegacyJson);
        var current = Write("new.dcp", "New", 1, new DateOnly(2026, 1, 1));

        Assert.True(DcpFile.Load(legacy).IsLegacy);
        Assert.Null(_library.BackupIfLegacy(current, backups));
        Assert.Null(_library.BackupIfLegacy(Path.Combine(_dir, "missing.dcp"), backups));

        var copy = _library.BackupIfLegacy(legacy, backups);
        Assert.Equal(Path.Combine(backups, "DCP Tool 2019", "zipp 34_19.dcp"), copy);
        Assert.Equal(LegacyJson, File.ReadAllText(copy!));

        // Asked again before the file was saved: the same copy, no second one
        Assert.Equal(copy, _library.BackupIfLegacy(legacy, backups));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(copy)!));

        // Saved in the new format: nothing more to keep
        DcpFile.Save(legacy, DcpFile.Load(legacy).Dcp);
        Assert.Null(_library.BackupIfLegacy(legacy, backups));
        Assert.Equal(LegacyJson, File.ReadAllText(copy!));

        // Written by DCP Tool 0.x again with other content: a second copy next to the first
        File.WriteAllText(legacy, LegacyJson.Replace("ARCADES", "OTHER"));
        var second = _library.BackupIfLegacy(legacy, backups);
        Assert.EndsWith("zipp 34_19 (2).dcp", second);
        Assert.Equal(LegacyJson, File.ReadAllText(copy!));
    }

    [Fact]
    public void OpeningALegacyFileDoesNotChangeIt()
    {
        var legacy = Path.Combine(_dir, "old.dcp");
        File.WriteAllText(legacy, LegacyJson);
        var before = File.GetLastWriteTimeUtc(legacy);

        _library.ScanAll();
        DcpFile.Load(legacy);
        DcpLibrary.CreateNextEpisode(legacy);
        _library.Duplicate(legacy);

        Assert.Equal(LegacyJson, File.ReadAllText(legacy));
        Assert.Equal(before, File.GetLastWriteTimeUtc(legacy));
    }
}

/// <summary>
/// The real archive of a user: 399 files written by DCP Tool 0.x in year and programme folders, with PDF and Word
/// files next to them. Not in the repository; skipped when "TestingEquipment/DCP Library Folder" is missing.
/// The test works on a copy, the archive itself is never touched.
/// </summary>
public sealed class RealArchiveTests : IDisposable
{
    private const string ArchiveName = "DCP Library Folder";
    private readonly string _copy = Path.Combine(Path.GetTempPath(), "dcptool-archive-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_copy))
        {
            Directory.Delete(_copy, true);
        }
    }

    private static string? FindArchive([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(here) ?? "" })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "DcpTool.sln")))
                {
                    var candidate = Path.Combine(dir.Parent?.FullName ?? dir.FullName, "TestingEquipment", ArchiveName);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            File.Copy(file, target);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
        }
    }

    [Fact]
    public void EveryFileLoadsAndTheTreeIsComplete()
    {
        var archive = FindArchive();
        if (archive is null)
        {
            Assert.Skip($"The archive \"{ArchiveName}\" is not available");
            return;
        }

        CopyFolder(archive, _copy);
        var library = new DcpLibrary(_copy);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = library.ScanAll(TestContext.Current.CancellationToken);
        var firstScan = watch.Elapsed;

        var files = Directory.GetFiles(_copy, "*.dcp", SearchOption.AllDirectories);
        Assert.Equal(files.Length, snapshot.Entries.Count);
        Assert.True(files.Length >= 399, $"{files.Length} files");
        Assert.Empty(snapshot.Entries.Where(e => e.Error is not null).Select(e => $"{e.RelativePath}: {e.Error}"));

        // Every folder in the tree (presets apart), every DCP in exactly one place, in its own folder
        foreach (var sort in Enum.GetValues<LibrarySort>())
        {
            var tree = LibraryTree.Build(snapshot, sort);
            var folders = Directory.GetDirectories(_copy, "*", SearchOption.AllDirectories)
                .Select(d => Path.GetRelativePath(_copy, d).Replace('\\', '/'))
                .Where(d => !d.StartsWith(DcpLibrary.PresetsFolderName, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal);
            Assert.Equal(folders, tree.AllFolders.Select(f => f.RelativePath).Order(StringComparer.Ordinal));
            var inTree = tree.AllEntries.ToList();
            Assert.Equal(snapshot.Entries.Count(e => !e.IsPreset), inTree.Count);
            Assert.Equal(inTree.Count, inTree.Select(e => e.Path).Distinct().Count());
            Assert.All(tree.AllFolders, f => Assert.All(f.Entries, e => Assert.Equal(f.RelativePath, e.Folder)));
        }

        Assert.Contains(snapshot.Entries, e => e.RelativePath == "DCP Tool 2024/Deboriada 2024/Deboriada 1_24.dcp");
        Assert.Contains(snapshot.Entries, e => e.RelativePath == "SpoZi/SpoZi 01_26.dcp");
        Assert.Contains(snapshot.Entries, e => e is { RelativePath: "Presets/ZIPP TV.dcp", IsPreset: true });

        // Natural order in a programme folder
        var spozi = LibraryTree.Build(snapshot, LibrarySort.Name).Folders.Single(f => f.Name == "SpoZi");
        Assert.Equal("SpoZi 01_26", spozi.Entries[0].Name);
        Assert.True(spozi.Entries.FindIndex(e => e.Name == "SpoZi 02_26") < spozi.Entries.FindIndex(e => e.Name == "SpoZi 10_26"));

        // A second scan comes from memory
        watch.Restart();
        library.ScanAll(TestContext.Current.CancellationToken);
        var secondScan = watch.Elapsed;
        Assert.Equal(0, library.LastScanReads);
        TestContext.Current.SendDiagnosticMessage($"Archive: {files.Length} files, first scan {firstScan.TotalMilliseconds:0} ms, re-scan {secondScan.TotalMilliseconds:0} ms");
        Assert.True(firstScan < TimeSpan.FromSeconds(20), $"first scan took {firstScan}");
    }
}
