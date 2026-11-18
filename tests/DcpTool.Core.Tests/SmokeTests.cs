using System.Runtime.Serialization.Json;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;
using DcpTool.Core.Validation;
using Xunit;

namespace DcpTool.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void SaveLoadRoundTrip()
    {
        var dcp = new Dcp { TitoloItaliano = "Gedanken zur Stille", Sede = "BZ/16", Puntata = 3, Durata = TimeSpan.FromSeconds(221) };
        dcp.Lines.Add(new DcpLine { Titolo = "A BARREN WORLD A", Autori = [new("Jeff", "Montgomery"), new("Oliver", "Kobák")], Durata = TimeSpan.FromSeconds(214), Marca = "Sonoton" });
        var back = DcpFile.Parse(DcpFile.Serialize(dcp));
        Assert.Equal(2, back.Version);
        Assert.Equal("Gedanken zur Stille", back.Dcp.TitoloItaliano);
        Assert.Equal(dcp.Lines[0].Autori, back.Dcp.Lines[0].Autori);
        Assert.Equal(dcp.Lines[0].Id, back.Dcp.Lines[0].Id);
        Assert.Equal(dcp.Lines[0].Durata, back.Dcp.Lines[0].Durata);
    }

    [Fact]
    public void ReadsFileWrittenByV1()
    {
        var old = new DCP_Tool.Models.Dcp
        {
            DataTrasmissione = new DateTime(2021, 12, 26, 10, 0, 0, DateTimeKind.Local),
            DataContratto = new DateTime(2021, 12, 13, 0, 0, 0, DateTimeKind.Local),
            TitoloItaliano = "Gedanken zur Stille", Puntata = 1, Uorg = 9016, Matricola = 996851,
            Sede = DCP_Tool.Models.Sede.Bolzano_DE, ReteTrasmissione = DCP_Tool.Models.ReteTrasmissione.TV3,
            Durata = TimeSpan.FromSeconds(221), OraInizio = new TimeSpan(20, 15, 0),
        };
        old.Lines.Add(new DCP_Tool.Models.DcpLine
        {
            Titolo = "A BARREN WORLD A", Durata = TimeSpan.FromSeconds(214), Marca = "Sonoton", SiglaNum = "SCDV 579 ",
            Ruolo = DCP_Tool.Models.Ruolo.SF, TipoGenerazione = DCP_Tool.Models.TipoGenerazione.OperaSuDisco,
            Autori = [new DCP_Tool.Models.Autore("Jeff", "Montgomery"), new DCP_Tool.Models.Autore("Jean", "de la Tour")],
        });
        using var ms = new MemoryStream();
        new DataContractJsonSerializer(typeof(DCP_Tool.Models.Dcp)).WriteObject(ms, old);
        var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        var loaded = DcpFile.Parse(json);
        Assert.True(loaded.IsLegacy);
        var dcp = loaded.Dcp;
        Assert.Equal(new DateOnly(2021, 12, 26), dcp.DataTrasmissione);
        Assert.Null(dcp.DataRegistrazione);
        Assert.Equal(new DateOnly(2021, 12, 13), dcp.DataContratto);
        Assert.Equal("BZ/16", dcp.Sede);
        Assert.Equal("TV3", dcp.Rete);
        Assert.Equal(9016, dcp.Uorg);
        Assert.Null(dcp.Serie);
        Assert.Equal(TimeSpan.FromSeconds(221), dcp.Durata);
        Assert.Equal(new TimeSpan(20, 15, 0), dcp.OraInizio);
        var line = Assert.Single(dcp.Lines);
        Assert.Equal("SF", line.Ruolo);
        Assert.Equal("9", line.TipoGenerazione);
        Assert.Equal([new Autore("Jeff", "Montgomery"), new Autore("Jean", "de la Tour")], line.Autori);
    }

    [Theory]
    [InlineData("Jeff Montgomery; Matthew Noble", "Jeff|Montgomery;Matthew|Noble")]
    [InlineData("Jeff Montgomery, Matthew Noble", "Jeff|Montgomery;Matthew|Noble")]
    [InlineData("Smith, John", "John|Smith")]
    [InlineData("John Michael Smith", "John Michael|Smith")]
    [InlineData("Madonna", "Madonna|")]
    [InlineData("", "")]
    public void ParsesAuthors(string text, string expected)
    {
        var actual = string.Join(";", Autore.ParseList(text).Select(a => $"{a.Nome}|{a.Cognome}"));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ValidationBlocksUploadWithoutAutori()
    {
        var dcp = new Dcp { TitoloItaliano = "x", Sede = "BZ/16", Durata = TimeSpan.FromMinutes(5), Uorg = 1, Matricola = 1 };
        dcp.Lines.Add(new DcpLine { Titolo = "t", Durata = TimeSpan.FromSeconds(10), Marca = "m" });
        Assert.Contains(DcpValidator.Validate(dcp, ValidationPurpose.Upload), i => i.Code == "line-autori-missing" && i.IsError);
        Assert.DoesNotContain(DcpValidator.Validate(dcp, ValidationPurpose.Document), i => i.IsError);
    }

    [Fact]
    public void RecoveryStoreRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dcptool-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RecoveryStore(new DcpTool.Core.Settings.AppPaths(dir));
            Assert.Null(store.TryRead());
            store.Write(new Dcp { TitoloItaliano = "abc" }, null, false);
            var session = store.TryRead();
            Assert.NotNull(session);
            Assert.Equal("abc", session.Dcp.TitoloItaliano);
            Assert.True(session.HasUnsavedChanges);
            store.Clear();
            Assert.Null(store.TryRead());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The snapshot is the crash safety net: what was written last comes back with its file and its unsaved state; after
    /// a normal close or "Nicht speichern" (both clear it) nothing comes back.
    /// </summary>
    [Fact]
    public void RecoverySnapshotOnlySurvivesAnAbnormalEnd()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dcptool-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new DcpTool.Core.Settings.AppPaths(dir);
            var file = Path.Combine(dir, "ZIPP TV 33 2026-11-18.dcp");
            new RecoveryStore(paths).Write(new Dcp { Sendung = "ZIPP TV", TitoloItaliano = "edited" }, file, hasUnsavedChanges: true);

            // The process was killed: the next start finds the edit, its file and that it was not saved
            var session = new RecoveryStore(paths).TryRead();
            Assert.NotNull(session);
            Assert.Equal(("edited", "ZIPP TV", file, true), (session.Dcp.TitoloItaliano, session.Dcp.Sendung, session.FilePath, session.HasUnsavedChanges));

            // Saved since: restored as saved
            new RecoveryStore(paths).Write(new Dcp { TitoloItaliano = "edited" }, file, hasUnsavedChanges: false);
            Assert.False(new RecoveryStore(paths).TryRead()!.HasUnsavedChanges);

            // Closed normally, or "Nicht speichern": the next start is a fresh one
            new RecoveryStore(paths).Clear();
            Assert.Null(new RecoveryStore(paths).TryRead());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
