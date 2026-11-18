using DcpTool.Core.Library;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;
using Xunit;

namespace DcpTool.Core.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dcptool-lib-" + Guid.NewGuid().ToString("N"));
    private readonly DcpLibrary _library;

    public LibraryTests()
    {
        Directory.CreateDirectory(_dir);
        _library = new DcpLibrary(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static Dcp Make(string title, int? puntata, DateOnly date, int lines = 2) => new()
    {
        TitoloItaliano = title,
        Puntata = puntata,
        DataTrasmissione = date,
        Sede = "BZ/16",
        Uorg = 9016,
        Lines = Enumerable.Range(1, lines).Select(i => new DcpLine { Titolo = "Song " + i }).ToList(),
    };

    [Fact]
    public void ScanListsFilesAndSurvivesBrokenOnes()
    {
        DcpFile.Save(_library.SuggestPath(Make("Kompass", 3, new DateOnly(2026, 10, 1))), Make("Kompass", 3, new DateOnly(2026, 10, 1)));
        var newer = Make("Kompass", 4, new DateOnly(2026, 11, 5));
        newer.Upload = new UploadRecord { Complete = true, Codice = "1788111-00", LinesSaved = 2 };
        DcpFile.Save(_library.SuggestPath(newer, "2026"), newer);
        File.WriteAllText(Path.Combine(_dir, "broken.dcp"), "{ not json");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "ignored");

        var entries = _library.ScanAll().Entries;

        Assert.Equal(3, entries.Count);
        var uploaded = entries.Single(e => e.Puntata == 4);
        Assert.Equal("2026/Kompass 04 2026-11-05.dcp", uploaded.RelativePath);
        Assert.Equal(LibraryUploadState.Uploaded, uploaded.UploadState);
        Assert.Equal("1788111-00", uploaded.RaiCodice);
        Assert.Equal(9016, uploaded.Uorg);
        Assert.Equal(2, entries.Single(e => e.Puntata == 3).LineCount);
        Assert.NotNull(entries.Single(e => e.Name == "broken").Error);
    }

    [Fact]
    public void SuggestedNamesAreSafeAndUnique()
    {
        var dcp = Make("Was? Wie: \"Wo\"/", 1, new DateOnly(2026, 1, 2));
        var first = Save(dcp);
        var second = _library.SuggestPath(dcp);
        Assert.Equal("Was Wie Wo 01 2026-01-02.dcp", Path.GetFileName(first));
        Assert.Equal("Was Wie Wo 01 2026-01-02 (2).dcp", Path.GetFileName(second));
    }

    [Fact]
    public void PresetKeepsTheProgrammeAndDropsTheEpisode()
    {
        var dcp = Make("Kompass", 7, new DateOnly(2026, 5, 5));
        dcp.Sottotitolo = "Folge 7";
        dcp.Upload = new UploadRecord { Complete = true };

        var path = _library.SavePreset(dcp, "Kompass");
        var entry = _library.ScanAll().Entries.Single();
        var fresh = DcpLibrary.CreateFromPreset(path);

        Assert.True(entry.IsPreset);
        Assert.Equal("Presets/Kompass.dcp", entry.RelativePath);
        Assert.Equal("Kompass", fresh.TitoloItaliano);
        Assert.Equal(9016, fresh.Uorg);
        Assert.Equal("BZ/16", fresh.Sede);
        Assert.Null(fresh.Puntata);
        Assert.Equal("", fresh.Sottotitolo);
        Assert.Empty(fresh.Lines);
        Assert.Null(fresh.Upload);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), fresh.DataTrasmissione);
    }

    [Fact]
    public void NextEpisode()
    {
        var dcp = Make("Kompass", 7, new DateOnly(2026, 5, 5));
        dcp.Upload = new UploadRecord { Complete = true };
        var path = Save(dcp);

        var next = DcpLibrary.CreateNextEpisode(path);
        Assert.Equal(8, next.Puntata);
        Assert.Empty(next.Lines);
        Assert.Null(next.Upload);
    }

    [Fact]
    public void RenameRefusesATakenName()
    {
        var a = Save(Make("A", 1, new DateOnly(2026, 1, 1)));
        var b = Save(Make("B", 1, new DateOnly(2026, 1, 1)));
        var renamed = DcpLibrary.Rename(a, "Neu: Name");
        Assert.Equal("Neu Name.dcp", Path.GetFileName(renamed));
        Assert.True(File.Exists(renamed));
        Assert.Throws<DcpFileException>(() => DcpLibrary.Rename(b, "Neu Name"));
    }

    [Fact]
    public void AFileOutsideTheLibraryIsReadLikeOneInIt()
    {
        var outside = Path.Combine(Path.GetTempPath(), "dcptool-outside-" + Guid.NewGuid().ToString("N") + ".dcp");
        try
        {
            DcpFile.Save(outside, Make("Kompass", 9, new DateOnly(2026, 3, 3)));
            var entry = DcpLibrary.ReadFile(outside);
            Assert.Null(entry.Error);
            Assert.False(entry.IsPreset);
            Assert.Equal((9, 9016), (entry.Puntata, entry.Uorg));
            Assert.Equal(Path.GetFileName(outside), entry.RelativePath);

            File.WriteAllText(outside, "{ broken");
            Assert.NotNull(DcpLibrary.ReadFile(outside).Error);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void ContainsOnlyPathsBelowTheFolder()
    {
        Assert.True(_library.Contains(Path.Combine(_dir, "sub", "x.dcp")));
        Assert.False(_library.Contains(Path.Combine(_dir, "..", "x.dcp")));
        Assert.False(_library.Contains(null));
    }

    private string Save(Dcp dcp)
    {
        var path = _library.SuggestPath(dcp);
        DcpFile.Save(path, dcp);
        return path;
    }
}
