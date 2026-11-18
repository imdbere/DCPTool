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
        DcpFile.Save(_library.SuggestPath(newer), newer);
        File.WriteAllText(Path.Combine(_dir, "broken.dcp"), "{ not json");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "ignored");

        var entries = _library.ScanAll().Entries;

        Assert.Equal(3, entries.Count);
        var uploaded = entries.Single(e => e.Puntata == 4);
        Assert.Equal("Kompass/2026/Kompass 04 2026-11-05.dcp", uploaded.RelativePath);
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
        Assert.Equal(Path.Combine(_dir, "Was Wie Wo", "2026"), Path.GetDirectoryName(second));
    }

    [Fact]
    public void NewDcpsAreFiledBySendungAndYear()
    {
        // The Sendung names folder and file, not the longer Titolo italiano
        var dcp = Make("ZIPP TV 2026 - DAS KINDERFERNSEHEN", 33, new DateOnly(2026, 11, 18));
        dcp.Sendung = "ZIPP TV";
        Assert.Equal(Path.Combine(_dir, "ZIPP TV", "2026", "ZIPP TV 33 2026-11-18.dcp"), _library.SuggestPath(dcp));

        // The year is the broadcast's, not today's
        dcp.DataTrasmissione = new DateOnly(2027, 1, 5);
        dcp.Puntata = 1;
        Assert.Equal(Path.Combine(_dir, "ZIPP TV", "2027", "ZIPP TV 01 2027-01-05.dcp"), _library.SuggestPath(dcp));

        // Without a Sendung: the Titolo italiano; without a Puntata it is left out; without anything "DCP"
        dcp.Sendung = "";
        dcp.Puntata = null;
        Assert.Equal(Path.Combine(_dir, "ZIPP TV 2026 - DAS KINDERFERNSEHEN", "2027", "ZIPP TV 2026 - DAS KINDERFERNSEHEN 2027-01-05.dcp"),
            _library.SuggestPath(dcp));
        Assert.Equal(Path.Combine(_dir, "DCP", "2026", "DCP 2026-02-03.dcp"), _library.SuggestPath(new Dcp { DataTrasmissione = new DateOnly(2026, 2, 3) }));

        // Characters a file name cannot have are dropped from folder and file alike
        dcp.Sendung = "Sport: Zeit?";
        Assert.Equal(Path.Combine(_dir, "Sport Zeit", "2027", "Sport Zeit 2027-01-05.dcp"), _library.SuggestPath(dcp));

        // A Sendung named like the presets folder must not put DCPs among the presets
        dcp.Sendung = "presets";
        Assert.False(Path.GetRelativePath(_dir, _library.SuggestPath(dcp)).StartsWith("presets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestingAPathCreatesNothing()
    {
        _library.SuggestPath(Make("Kompass", 1, new DateOnly(2026, 1, 1)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
    }

    [Fact]
    public void ADcpStartedFromAPresetKeepsItsSendung()
    {
        var source = Make("ZIPP TV 2026 - DAS KINDERFERNSEHEN", 30, new DateOnly(2026, 11, 4));
        source.Sendung = "Something else";
        var preset = _library.SavePreset(source, "ZIPP TV");

        // The preset is named by its file, not by what the DCP it was made from had
        Assert.Equal("", DcpFile.Load(preset).Dcp.Sendung);

        var fresh = DcpLibrary.CreateFromPreset(preset);
        Assert.Equal("ZIPP TV", fresh.Sendung);
        fresh.Puntata = 31;
        var path = Save(fresh);
        var date = fresh.DataTrasmissione;
        Assert.Equal(Path.Combine("ZIPP TV", date.Year.ToString(), $"ZIPP TV 31 {date:yyyy-MM-dd}.dcp"), Path.GetRelativePath(_dir, path));

        // Saved and read again, and passed on to the next episode
        Assert.Equal("ZIPP TV", DcpFile.Load(path).Dcp.Sendung);
        Assert.Equal("ZIPP TV", DcpLibrary.CreateNextEpisode(path).Sendung);
    }

    [Fact]
    public void FilesWithoutSendungStillOpen()
    {
        var path = Path.Combine(_dir, "old.dcp");
        File.WriteAllText(path, """{ "format": "dcp-tool", "version": 2, "dcp": { "titoloItaliano": "Kompass", "dataTrasmissione": "2026-01-01" } }""");
        var dcp = DcpFile.Load(path).Dcp;
        Assert.Equal("", dcp.Sendung);
        Assert.Equal("Kompass", DcpLibrary.SendungName(dcp));
    }

    [Fact]
    public void ACopyHasTheSameHeaderAndSongsButNoUpload()
    {
        var dcp = Make("Kompass", 7, new DateOnly(2026, 5, 5), lines: 3);
        dcp.Sendung = "Kompass";
        dcp.Sottotitolo = "Folge 7";
        dcp.Upload = new UploadRecord { Complete = true, Codice = "1788111-00" };

        var copy = DcpLibrary.CreateCopy(dcp);

        Assert.Null(copy.Upload);
        Assert.NotNull(dcp.Upload);
        Assert.Equal(("Kompass", "Kompass", 7, "Folge 7", 9016, "BZ/16"),
            (copy.Sendung, copy.TitoloItaliano, copy.Puntata, copy.Sottotitolo, copy.Uorg, copy.Sede));
        Assert.Equal(dcp.Lines.Select(l => l.Titolo), copy.Lines.Select(l => l.Titolo));

        // Its own songs: editing the copy leaves the original alone
        copy.Lines[0].Titolo = "changed";
        Assert.Equal("Song 1", dcp.Lines[0].Titolo);
        Assert.Empty(copy.Lines.Select(l => l.Id).Intersect(dcp.Lines.Select(l => l.Id)));
    }

    [Fact]
    public void SaveGoesToTheFileOrIntoTheLibraryOrAsks()
    {
        var dcp = Make("ZIPP TV 2026", 33, new DateOnly(2026, 11, 18));
        dcp.Sendung = "ZIPP TV";

        // A DCP with a file: that file, wherever it is (also outside the schema: existing files stay where they are)
        var elsewhere = Path.Combine(_dir, "old place", "zipp 33.dcp");
        Assert.Equal(new SavePlan(SaveKind.ExistingFile, elsewhere), SavePlan.For(elsewhere, _library, dcp));

        // Never saved: straight into the library, no dialog
        Assert.Equal(new SavePlan(SaveKind.NewInLibrary, Path.Combine(_dir, "ZIPP TV", "2026", "ZIPP TV 33 2026-11-18.dcp")),
            SavePlan.For(null, _library, dcp));

        // Never saved and no library: the user picks the file
        Assert.Equal(new SavePlan(SaveKind.AskForPath, null), SavePlan.For(null, null, dcp));
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
        // Episodes 1 and 2 of one programme: the same folder
        var a = Save(Make("A", 1, new DateOnly(2026, 1, 1)));
        var b = Save(Make("A", 2, new DateOnly(2026, 1, 1)));
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
