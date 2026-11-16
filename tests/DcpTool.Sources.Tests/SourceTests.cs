using DcpTool.Core.Model;
using Xunit;
using static DcpTool.Sources.Tests.TestSupport;

namespace DcpTool.Sources.Tests;

public class MusicmediaSourceTests
{
    private readonly MusicmediaSource _source = new();

    [Fact]
    public void MapsSonotonTags()
    {
        var file = Audio("a.mp3", title: "A BARREN WORLD A --- dark piano", performers: ["Jeff Montgomery | Oliver Kobák"],
            composers: ["Montgomery, Jeff | Oliver Kobák", "Oliver Kobák"], album: "SCDV 579 - Dark Worlds");

        var line = _source.CreateLine(file, []).Line;

        Assert.Equal("A BARREN WORLD A ", line.Titolo);
        Assert.Equal([new Autore("Jeff", "Montgomery"), new Autore("Oliver", "Kobák")], line.Autori);
        Assert.Equal("Jeff Montgomery, Oliver Kobák", line.Esecutori);
        Assert.Equal("SCDV 579", line.SiglaNum);
        Assert.Equal("Sonoton", line.Marca);
        Assert.Equal(TimeSpan.FromSeconds(180), line.Durata);
        Assert.Equal("musicmedia", line.Origin!.SourceId);
        Assert.Equal("a.mp3", line.Origin.FilePath);
    }

    [Fact]
    public void PerformersFallBackToAuthors()
    {
        var line = _source.CreateLine(Audio("a.mp3", composers: ["Doe, Jane"]), []).Line;
        Assert.Equal("Jane Doe", line.Esecutori);
    }

    [Fact]
    public void MissingTagsGiveBlankLine()
    {
        var line = _source.CreateLine(Audio("a.mp3"), []).Line;
        Assert.Equal("", line.Titolo);
        Assert.Empty(line.Autori);
        Assert.Equal("Sonoton", line.Marca);
    }

    [Theory]
    [InlineData("SCDV 579 ", "Sonoton")]
    [InlineData("SCD 12", "Sonoton")]
    [InlineData("CNS 5", "Sonoton")]
    [InlineData("MMIT 1", "Musica.it")]
    [InlineData("cm 7", "Sonoton")]
    [InlineData("SCORE 3", "Score")]
    [InlineData("ISCD 9", "Intersound")]
    [InlineData("ROCK 2", "Sonoton")]
    [InlineData("SURE 1", "Sonoton")]
    [InlineData("XYZ 1", "Sonoton")]
    [InlineData("", "Sonoton")]
    public void LabelFromCataloguePrefix(string siglaNum, string label) =>
        Assert.Equal(label, MusicmediaSource.LabelFor(siglaNum));

    [Fact]
    public void IsTheLowPriorityFallback() =>
        Assert.Equal(10, _source.Recognise(Audio("a.mp3"), []));
}

public class ArtlistSourceTests
{
    private readonly ArtlistSource _source = new();

    private static ArtlistCueSheet Cue(string title = "Up Until Sunrise", string artist = "Damon Power", TimeSpan? duration = null, string path = "cue.xlsx") =>
        new(path, "Artlist Original", "Album", "194492126796", title, artist, null, "ISRC", null, duration,
            "Artlist Records", "ASCAP", null, [new ArtlistWriter("Youval Ziso", "C", "ASCAP", null, null)], false, "6001971");

    [Fact]
    public void RecognisesByCueSheetCopyrightOrNot()
    {
        var file = Audio("x.mp3", title: "Up Until Sunrise", performers: ["Damon Power"]);
        Assert.Equal(100, _source.Recognise(file, [Cue()]));
        Assert.Equal(90, _source.Recognise(Audio("x.mp3", copyright: "Licensed for video by Artlist.io"), []));
        Assert.Equal(0, _source.Recognise(file, []));
    }

    [Fact]
    public void FileNameFallbackMatches()
    {
        var file = Audio(@"C:\m\Damon Power - Up Until Sunrise.mp3");
        Assert.Equal(100, _source.Recognise(file, [Cue()]));
    }

    [Fact]
    public void PicksClosestDurationAmongVersions()
    {
        var full = Cue(duration: TimeSpan.FromSeconds(180), path: "full.xlsx");
        var shortVersion = Cue(duration: TimeSpan.FromSeconds(60), path: "short.xlsx");
        var file = Audio("x.mp3", title: "Up Until Sunrise", performers: ["Damon Power"], seconds: 61);

        var created = _source.CreateLine(file, [full, shortVersion]);

        Assert.Same(shortVersion, created.UsedSidecar);
    }

    [Fact]
    public void LineWithCueSheet()
    {
        var created = _source.CreateLine(Audio("x.mp3", title: "up until sunrise", performers: ["Damon Power"], seconds: 200), [Cue()]);
        var line = created.Line;

        Assert.Null(created.Warning);
        Assert.Equal("UP UNTIL SUNRISE", line.Titolo);
        Assert.Equal([new Autore("Youval", "Ziso")], line.Autori);
        Assert.Equal("Artlist", line.Marca);
        Assert.Equal("194492126796", line.SiglaNum);
        Assert.Equal("", line.Edizione);
        Assert.Equal(TimeSpan.FromSeconds(200), line.Durata);
        Assert.False(line.Origin!.Incomplete);
        Assert.Equal("cue.xlsx", line.Origin.SidecarFile);
    }

    [Fact]
    public void LineWithoutCueSheetIsIncompleteWithWarning()
    {
        var created = _source.CreateLine(Audio("x.mp3", title: "Song", performers: ["Band"], copyright: "Artlist"), []);

        Assert.Empty(created.Line.Autori);
        Assert.Equal("Artlist", created.Line.Marca);
        Assert.True(created.Line.Origin!.Incomplete);
        Assert.Contains("cue sheet", created.Warning);
        Assert.Contains("Downloads", created.Warning);
    }

    [Fact]
    public void CompleteLinesFillsOnlyLinesWithoutAutori()
    {
        var empty = new DcpLine { Titolo = "UP UNTIL SUNRISE", Esecutori = "Damon Power", Origin = new LineOrigin { SourceId = "artlist", Incomplete = true } };
        var done = new DcpLine { Titolo = "UP UNTIL SUNRISE", Esecutori = "Damon Power", Autori = [new("A", "B")] };
        var other = new DcpLine { Titolo = "OTHER", Esecutori = "Damon Power" };

        var matches = _source.CompleteLines([empty, done, other], [Cue()]);

        var match = Assert.Single(matches);
        Assert.Same(empty, match.Line);
        Assert.Equal([new Autore("Youval", "Ziso")], empty.Autori);
        Assert.False(empty.Origin!.Incomplete);
        Assert.Equal("", empty.Edizione);
        Assert.Equal([new Autore("A", "B")], done.Autori);
        Assert.Empty(other.Autori);
    }
}
