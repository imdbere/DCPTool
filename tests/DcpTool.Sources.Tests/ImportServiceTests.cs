using DcpTool.Core.Import;
using DcpTool.Core.Model;
using Xunit;
using static DcpTool.Sources.Tests.TestSupport;

namespace DcpTool.Sources.Tests;

public class ImportServiceTests
{
    private static readonly LineDefaults Defaults = new() { Ruolo = "PP", GenereSiae = "MC", TipoGenerazione = "1" };

    private static (ImportService Service, FakeReader Reader, string Dir) Setup()
    {
        var reader = new FakeReader();
        return (new ImportService(reader, [new ArtlistSource(), new MusicmediaSource()]), reader, TempDir());
    }

    private static string Touch(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void ImportsFolderSortedAndAppliesDefaults()
    {
        var (service, reader, dir) = Setup();
        foreach (var n in new[] { "b.mp3", "a.mp3" })
        {
            var p = Touch(dir, n);
            reader.Files[p] = Audio(p, title: n, composers: ["Jane Doe"], album: "SCD 1 - X");
        }
        Touch(dir, "readme.txt");

        var result = service.Import(new ImportRequest([dir], [], Defaults));

        Assert.Equal(["a.mp3", "b.mp3"], result.NewLines.Select(l => l.Titolo));
        Assert.All(result.NewLines, l => { Assert.Equal("PP", l.Ruolo); Assert.Equal("MC", l.GenereSiae); Assert.Equal("1", l.TipoGenerazione); });
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void BadFileDoesNotStopOthers()
    {
        var (service, reader, dir) = Setup();
        var good = Touch(dir, "good.mp3");
        var bad = Touch(dir, "bad.mp3");
        reader.Files[good] = Audio(good, title: "Good");

        var result = service.Import(new ImportRequest([bad, good], [], Defaults));

        Assert.Single(result.NewLines);
        var msg = Assert.Single(result.Messages);
        Assert.Equal(ImportSeverity.Error, msg.Severity);
        Assert.Equal("bad.mp3", msg.File);
    }

    [Fact]
    public void CueSheetNextToAudioIsUsedWithoutBeingDropped()
    {
        var (service, reader, dir) = Setup();
        var mp3 = Touch(dir, "Damon Power - Up Until Sunrise.mp3");
        reader.Files[mp3] = Audio(mp3, title: "Up Until Sunrise", performers: ["Damon Power"]);
        WriteCueSheet(dir, "cue_sheet_Up_Until_Sunrise_6001971.xlsx", "Up Until Sunrise", "Damon Power", "Youval Ziso");

        var result = service.Import(new ImportRequest([mp3], [], Defaults));

        var line = Assert.Single(result.NewLines);
        Assert.Equal("Youval", line.Autori.Single().Nome);
        Assert.Equal("artlist", line.Origin!.SourceId);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void ArtlistFileWithoutCueSheetWarns()
    {
        var (service, reader, dir) = Setup();
        var mp3 = Touch(dir, "song.mp3");
        reader.Files[mp3] = Audio(mp3, title: "Song", performers: ["Band"], copyright: "Licensed for video by Artlist.io");

        var result = service.Import(new ImportRequest([mp3], [], Defaults));

        Assert.True(result.NewLines.Single().Origin!.Incomplete);
        var msg = Assert.Single(result.Messages);
        Assert.Equal(ImportSeverity.Warning, msg.Severity);
        Assert.Equal("song.mp3", msg.File);
    }

    [Fact]
    public void CueSheetDroppedLaterCompletesTheLine()
    {
        var (service, reader, dir) = Setup();
        var mp3 = Touch(dir, "song.mp3");
        reader.Files[mp3] = Audio(mp3, title: "Song", performers: ["Band"], copyright: "Artlist");
        var existing = service.Import(new ImportRequest([mp3], [], Defaults)).NewLines.Single();

        var cue = WriteCueSheet(TempDir(), "cue_sheet_Song_1.xlsx", "Song", "Band", "Jane Doe");
        var second = service.Import(new ImportRequest([cue], [existing], Defaults));

        Assert.Empty(second.NewLines);
        var completed = Assert.Single(second.CompletedLines);
        Assert.Equal(existing.Id, completed.Id);
        Assert.NotSame(existing, completed);
        Assert.Equal("Jane", completed.Autori.Single().Nome);
        Assert.False(completed.Origin!.Incomplete);
        Assert.Empty(existing.Autori);                         // the request's lines are untouched
        Assert.True(existing.Origin!.Incomplete);
        Assert.All(second.Messages, m => Assert.NotEqual(ImportSeverity.Warning, m.Severity));
    }

    [Fact]
    public void UnmatchedCueSheetWarns()
    {
        var (service, _, _) = Setup();
        var cue = WriteCueSheet(TempDir(), "cue_sheet_Other_2.xlsx", "Other", "Someone");

        var result = service.Import(new ImportRequest([cue], [new DcpLine { Titolo = "SONG" }], Defaults));

        Assert.Empty(result.CompletedLines);
        Assert.Equal(ImportSeverity.Warning, Assert.Single(result.Messages).Severity);
    }

    [Fact]
    public void DroppedXlsxThatIsNoCueSheetIsAnError()
    {
        var (service, _, dir) = Setup();
        var bad = Touch(dir, "table.xlsx");

        var result = service.Import(new ImportRequest([bad], [], Defaults));

        Assert.True(result.HasErrors);
        var message = Assert.Single(result.Messages);
        Assert.StartsWith("Not an Artlist cue sheet", message.Text);
        Assert.Equal("not-a-cue-sheet", message.Code);
    }

    [Fact]
    public void ReportsProgressAndHonoursCancellation()
    {
        var (service, reader, dir) = Setup();
        var a = Touch(dir, "a.mp3");
        reader.Files[a] = Audio(a, title: "A");
        var reports = new List<ImportProgress>();

        service.Import(new ImportRequest([a], [], Defaults), new SyncProgress(reports.Add));
        Assert.Equal(new ImportProgress(1, 1, null), reports[^1]);
        Assert.Equal(new ImportProgress(0, 1, "a.mp3"), reports[0]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => service.Import(new ImportRequest([a], [], Defaults), null, cts.Token));
    }

    [Fact]
    public void CanImportAndPatterns()
    {
        var (service, _, dir) = Setup();
        Assert.True(service.CanImport(dir));
        Assert.True(service.CanImport("x.MP3"));
        Assert.True(service.CanImport("cue.xlsx"));
        Assert.False(service.CanImport("x.pdf"));
        Assert.Contains("*.xlsx", service.FilePatterns);
        Assert.Contains("*.mp3", service.FilePatterns);
        Assert.Equal(2, SourcesRegistration.CreateDefault().Sources.Count);
    }

    // Progress<T> posts to the thread pool; this one is synchronous.
    private sealed class SyncProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }
}
