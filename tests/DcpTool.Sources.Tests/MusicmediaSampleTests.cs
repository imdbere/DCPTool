using DcpTool.Core.Import;
using DcpTool.Core.Model;
using Xunit;

namespace DcpTool.Sources.Tests;

/// <summary>Rules found in real musicmedia downloads (Harvest Media tags), November 2026.</summary>
public class MusicmediaSampleTests
{
    [Theory]
    [InlineData("SCDV 579 - Dark Worlds", "SCDV 579")]
    [InlineData("ADZ0149 - HOOKY BEATS", "ADZ0149")]
    [InlineData("MSC192 Organic & Accentuated", "MSC192")]
    [InlineData("Some Album", "Some Album")]
    [InlineData(null, "")]
    public void CatalogueCodeComesFromTheAlbum(string? album, string expected) =>
        Assert.Equal(expected, MusicmediaSource.CatalogueCode(album));

    [Theory]
    [InlineData("Lekaba, Themba", "Themba|Lekaba")]
    [InlineData("Christian Tschuggnall (AKM)", "Christian|Tschuggnall")]
    [InlineData("Citokid (GEMA), Tai Jason (GEMA)", "Citokid|;Tai|Jason")]
    [InlineData("Troy Hewson", "Troy|Hewson")]
    [InlineData("Troy Hewson, Jane Doe", "Troy|Hewson;Jane|Doe")]
    public void ComposersAreReadInBothStyles(string value, string expected) =>
        Assert.Equal(expected, string.Join(";", MusicmediaSource.ParseComposers(value).Select(a => $"{a.Nome}|{a.Cognome}")));

    [Theory]
    [InlineData("ADZ0149", "Afro Dizzy|Sonoton Music GmbH & Co. KG", "Sonoton")]
    [InlineData("CLC0124", "Sonoton Music GmbH & Co. KG|Unbound Music GmbH", "Sonoton")]
    // Not in RAI's label list, so the distributor's label is used, as v1 did
    [InlineData("MSC192", "Music Sculptor GmbH", "Sonoton")]
    [InlineData("NLM249", "Online Production Music", "Sonoton")]
    [InlineData("ISCD123", null, "Intersound")]
    public void MarcaIsOneRaiKnows(string code, string? publishers, string expected)
    {
        var marca = MusicmediaSource.MarcaFor(code, publishers);
        Assert.Equal(expected, marca);
        Assert.True(RaiMarche.Contains(marca));
    }

    [Fact]
    public void RealFilesGiveCompleteLines()
    {
        var dir = TestSupport.Samples("Musicmedia Examples");
        var service = SourcesRegistration.CreateDefault();
        var result = service.Import(new ImportRequest(Directory.GetFiles(dir, "*.wav"), [], LineDefaults.Standard));

        Assert.DoesNotContain(result.Messages, m => m.Severity == ImportSeverity.Error);
        Assert.Equal(5, result.NewLines.Count);
        Assert.All(result.NewLines, line =>
        {
            Assert.Equal("musicmedia", line.Origin?.SourceId);
            Assert.True(line.HasAutori);
            Assert.True(line.Durata > TimeSpan.FromSeconds(5));
            Assert.True(RaiMarche.Contains(line.Marca));
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(line.SiglaNum, @"^[A-Z]+\d+$"), $"SiglaNum '{line.SiglaNum}' of '{line.Titolo}'");
            Assert.DoesNotContain("(", line.AutoriText);
        });

        var purpose = result.NewLines.Single(l => l.Titolo.StartsWith("Built with Purpose"));
        Assert.Equal([new Autore("Citokid", ""), new Autore("Tai", "Jason")], purpose.Autori);
        var sayMyName = result.NewLines.Single(l => l.Titolo == "SAY MY NAME");
        Assert.Equal([new Autore("Themba", "Lekaba")], sayMyName.Autori);
    }
}
