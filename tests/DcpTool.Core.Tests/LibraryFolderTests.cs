using DcpTool.Core.Library;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;
using Xunit;

namespace DcpTool.Core.Tests;

/// <summary>Sub-folders, natural order, the scan cache and the safety copy of files written by DCP Tool 0.x.</summary>
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
    public void ScanFindsEveryDcpBelowTheFolderAndNothingElse()
    {
        Write("DCP 2024/zipp 1_24.dcp", "Zipp", 1, new DateOnly(2024, 1, 1));
        Write("DCP 2024/Deboriada 2024/Deboriada 1_24.dcp", "Deboriada", 1, new DateOnly(2024, 2, 1));
        Write("top.dcp", "Top", 1, new DateOnly(2026, 2, 1));
        Write("Presets/Preset.dcp", "Preset", null, new DateOnly(2026, 2, 1));
        Write(".hidden/secret.dcp", "Hidden", 1, new DateOnly(2026, 2, 1));
        Directory.CreateDirectory(Path.Combine(_dir, "DCP 9", "Empty"));
        File.WriteAllText(Path.Combine(_dir, "DCP 2024", "form.pdf"), "not a dcp");

        var entries = _library.ScanAll().Entries;

        Assert.Equal(["DCP 2024/Deboriada 2024/Deboriada 1_24.dcp", "DCP 2024/zipp 1_24.dcp", "Presets/Preset.dcp", "top.dcp"],
            entries.Select(e => e.RelativePath).Order(StringComparer.Ordinal));
        Assert.Equal(["Preset"], entries.Where(e => e.IsPreset).Select(e => e.Name));
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
        var moved = Path.Combine(_dir, "sub", "a.dcp");
        File.Move(a, moved);
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
        DcpLibrary.CreateFromPreset(legacy);

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
    public void EveryFileLoads()
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

        Assert.Contains(snapshot.Entries, e => e.RelativePath == "DCP Tool 2024/Deboriada 2024/Deboriada 1_24.dcp");
        Assert.Contains(snapshot.Entries, e => e.RelativePath == "SpoZi/SpoZi 01_26.dcp");
        Assert.Contains(snapshot.Entries, e => e is { RelativePath: "Presets/ZIPP TV.dcp", IsPreset: true });

        // A second scan comes from memory
        watch.Restart();
        library.ScanAll(TestContext.Current.CancellationToken);
        var secondScan = watch.Elapsed;
        Assert.Equal(0, library.LastScanReads);
        TestContext.Current.SendDiagnosticMessage($"Archive: {files.Length} files, first scan {firstScan.TotalMilliseconds:0} ms, re-scan {secondScan.TotalMilliseconds:0} ms");
        Assert.True(firstScan < TimeSpan.FromSeconds(20), $"first scan took {firstScan}");
    }
}
