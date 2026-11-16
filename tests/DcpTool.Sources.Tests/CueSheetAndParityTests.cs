using DcpTool.Core.Import;
using Xunit;
using V1 = DCP_Tool.Helpers;

namespace DcpTool.Sources.Tests;

public class CueSheetReaderTests
{
    [Fact]
    public void ReadsRealCueSheetsLikeV1()
    {
        var dir = TestSupport.SampleDir();
        foreach (var path in Directory.GetFiles(dir, "cue_sheet_*.xlsx"))
        {
            var cue = ArtlistCueSheetReader.Read(path);
            var old = V1.ArtlistCueSheetReader.Read(path);

            Assert.Equal(old.SongTitle, cue.SongTitle);
            Assert.Equal(old.Label, cue.Label);
            Assert.Equal(old.AlbumUpc, cue.AlbumUpc);
            Assert.Equal(old.PrimaryArtists, cue.PrimaryArtists);
            Assert.Equal(old.FeaturedArtists, cue.FeaturedArtists);
            Assert.Equal(old.Duration, cue.Duration);
            Assert.Equal(old.PublisherName, cue.PublisherName);
            Assert.Equal(old.SongId, cue.SongId);
            Assert.Equal(old.Performers, cue.Performers);
            Assert.Equal(old.Writers.Select(w => w.FullName), cue.Writers.Select(w => w.FullName));
            Assert.NotEmpty(cue.Writers);
        }
    }

    [Fact]
    public void SpecificValues()
    {
        var path = Directory.GetFiles(TestSupport.SampleDir(), "cue_sheet_Up_Until*.xlsx").Single();
        var cue = ArtlistCueSheetReader.Read(path);
        Assert.Equal("Up Until Sunrise", cue.SongTitle);
        Assert.Equal("Damon Power", cue.PrimaryArtists);
        Assert.Equal("Youval Ziso", cue.Writers.Single().FullName);
        Assert.Equal("6001971", cue.SongId);
    }

    [Fact]
    public void NonCueSheetThrowsImportException()
    {
        var path = Path.Combine(TestSupport.TempDir(), "x.xlsx");
        File.WriteAllText(path, "not a zip");
        Assert.Throws<ImportException>(() => ArtlistCueSheetReader.Read(path));
    }
}

public class LegacyParityTests
{
    // TagLib reports these CBR mp3s ~0.5 s longer than their audio data (file size / bitrate) is; ATL
    // matches the data. So v1 parity is only checked to within a second here.
    private const double DurationToleranceMs = 600;

    [Fact]
    public void AtlReadsSameTagsAndDurationAsTagLib()
    {
        var reader = new AtlAudioFileReader();
        foreach (var mp3 in TestSupport.SampleMp3s())
        {
            var audio = reader.Read(mp3);
            using var fs = File.OpenRead(mp3);
            var tf = TagLib.File.Create(new TagLib.StreamFileAbstraction(mp3, fs, fs));
            var tag = tf.GetTag(TagLib.TagTypes.Id3v2) ?? tf.Tag;

            Assert.Equal(tag.Title, audio.Tags.Title);
            Assert.Equal(tag.Performers.Where(p => p != null), audio.Tags.Performers);
            Assert.Equal(tf.Properties.Duration.TotalMilliseconds, audio.Duration.TotalMilliseconds, tolerance: DurationToleranceMs);
        }
    }

    [Fact]
    public void NewLinesEqualV1ForRealSamples()
    {
        var mp3s = TestSupport.SampleMp3s();
        Assert.Equal(3, mp3s.Length);

        var oldResult = V1.DcpLineImporter.Import(TestSupport.SampleMp3s().Concat(Directory.GetFiles(TestSupport.SampleDir(), "*.xlsx")));
        Assert.Empty(oldResult.Errors);

        var result = SourcesRegistration.CreateDefault()
            .Import(new ImportRequest(mp3s, [], new DcpTool.Core.Model.LineDefaults()));

        Assert.Empty(result.Messages);
        Assert.Equal(oldResult.Lines.Count, result.NewLines.Count);
        foreach (var old in oldResult.Lines)
        {
            var line = Assert.Single(result.NewLines, l => l.Titolo == old.Titolo);
            Assert.Equal(old.Autori.Select(a => a.ToString()), line.Autori.Select(a => a.ToString()));
            Assert.Equal(old.Esecutori, line.Esecutori);
            Assert.Equal("Artlist", line.Marca); // RAI wants ARTLIST, v1 sent the cue sheet label
            Assert.Equal(old.SiglaNum, line.SiglaNum);
            Assert.Equal("", line.Edizione); // v1 sent the publisher, which RAI rejects
            Assert.Equal(old.Durata.TotalMilliseconds, line.Durata.TotalMilliseconds, tolerance: DurationToleranceMs);
            Assert.Equal("artlist", line.Origin!.SourceId);
        }
    }

    [Fact]
    public void WithoutCueSheetsV1AndV2AgreeOnIncompleteLines()
    {
        var dir = TestSupport.TempDir();
        var mp3s = TestSupport.SampleMp3s().Select(f => { var d = Path.Combine(dir, Path.GetFileName(f)); File.Copy(f, d); return d; }).ToArray();

        var oldResult = V1.DcpLineImporter.Import(mp3s);
        var result = SourcesRegistration.CreateDefault()
            .Import(new ImportRequest(mp3s, [], new DcpTool.Core.Model.LineDefaults()));

        Assert.Equal(oldResult.MissingCueSheets.Count, result.Messages.Count(m => m.Severity == ImportSeverity.Warning));
        for (var i = 0; i < mp3s.Length; i++)
        {
            Assert.Equal(oldResult.Lines[i].Titolo, result.NewLines[i].Titolo);
            Assert.Equal(oldResult.Lines[i].Esecutori, result.NewLines[i].Esecutori);
            Assert.Equal("Artlist", result.NewLines[i].Marca);
            Assert.Empty(result.NewLines[i].Autori);
            Assert.True(result.NewLines[i].Origin!.Incomplete);
        }
    }
}
