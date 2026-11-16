using DCP_Tool.Helpers;
using DcpTool.Core.Model;
using DcpTool.Core.Rai;
using Xunit;
using Old = DCP_Tool.Models;

namespace DcpTool.Rai.Tests;

/// <summary>The v2 mapper must send exactly what v1 sent: same keys, values and order.</summary>
public class DcpFormMapperParityTests
{
    private static readonly string[] AspNet = ["__VIEWSTATE", "__VIEWSTATEGENERATOR", "__EVENTVALIDATION"];

    public static TheoryData<int, bool, bool, bool> Shapes => new()
    {
        { 1, false, false, false },
        { 4, false, false, true },
        { 5, true, false, false },
        { 9, true, false, true },
        { 5, true, true, false },
        { 1, true, true, true },
        { 8, false, true, false },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void PostsMatchV1(int lines, bool umlauts, bool emptyOptionals, bool explicitRegistration)
    {
        var (old, dcp) = Build(lines, umlauts, emptyOptionals, explicitRegistration);

        // v1 UploadDcp, with the page values replaced by predictable ones
        var content = old.GetBasicFormData();
        var expected = new List<List<string>>();
        var n = (int)Math.Ceiling(old.Lines.Count / 4f);
        for (var i = 0; i < n; i++)
        {
            content = content.Concat(old.GetLineFormData(i * 4, 4)).ToDictionary(e => e.Key, e => e.Value);
            foreach (var name in AspNet)
            {
                content[name] = $"{name}-{i}";
            }

            expected.Add(content.Select(kv => $"{kv.Key}={kv.Value}").ToList());
        }

        var body = new FormBody();
        body.SetRange(DcpFormMapper.Header(dcp));
        for (var i = 0; i < n; i++)
        {
            body.SetRange(DcpFormMapper.Lines(dcp, i * 4, 4));
            foreach (var name in AspNet)
            {
                body.Set(name, $"{name}-{i}");
            }

            Assert.Equal(expected[i], body.Items.Select(kv => $"{kv.Key}={kv.Value}").ToList());
        }
    }

    [Fact]
    public void LoginFormMatchesV1()
    {
        var expected = DcpInterfaceExtensions.GetLoginForm("mario", "p&ss w+rd");
        var actual = DcpFormMapper.LoginForm(new RaiPortalOptions(), new RaiCredentials("mario", "p&ss w+rd"));
        Assert.Equal(expected.Select(kv => $"{kv.Key}={kv.Value}"), actual.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    [Fact]
    public void UnknownSedeIsRefused()
    {
        var (_, dcp) = Build(1, false, false, false);
        dcp.Sede = "XX/99";
        Assert.Throws<InvalidOperationException>(() => DcpFormMapper.Header(dcp));
    }

    private static (Old.Dcp Old, Dcp New) Build(int count, bool umlauts, bool emptyOptionals, bool explicitRegistration)
    {
        var tx = new DateTime(2026, 3, 14);
        var (sedeOld, sedeCode) = (count % 3) switch
        {
            0 => (Old.Sede.Bolzano_IT, "BZ/14"),
            1 => (Old.Sede.Bolzano_DE, "BZ/16"),
            _ => (Old.Sede.Bolzano_LAD, "BZ/15"),
        };
        var title = umlauts ? "Südtirol Größe – Närrisch" : "Plain Title";

        var old = new Old.Dcp
        {
            DataTrasmissione = tx,
            Puntata = emptyOptionals ? 0 : 12,
            Durata = new TimeSpan(0, 52, 30),
            Sede = sedeOld,
            TitoloItaliano = title,
            TitoloOriginale = umlauts ? "Original Ünal" : "Original",
            Sottotitolo = emptyOptionals ? "" : "Sub & title",
            ReteTrasmissione = Old.ReteTrasmissione.TV3,
            Uorg = emptyOptionals ? 0 : 5128,
            Matricola = emptyOptionals ? 0 : 77,
            Serie = emptyOptionals ? 0 : 7,
            OraInizio = new TimeSpan(20, 15, 0),
        };
        var dcp = new Dcp
        {
            DataTrasmissione = DateOnly.FromDateTime(tx),
            Puntata = emptyOptionals ? null : 12,
            Durata = old.Durata,
            Sede = sedeCode,
            TitoloItaliano = title,
            TitoloOriginale = old.TitoloOriginale,
            Sottotitolo = old.Sottotitolo,
            Rete = "TV3",
            Uorg = emptyOptionals ? null : 5128,
            Matricola = emptyOptionals ? null : 77,
            Serie = emptyOptionals ? null : 7,
            OraInizio = old.OraInizio,
        };
        if (explicitRegistration)
        {
            old.DataRegistrazione = new DateTime(2026, 3, 1);
            dcp.DataRegistrazione = new DateOnly(2026, 3, 1);
        }

        for (var i = 0; i < count; i++)
        {
            var oldAuthors = new List<Old.Autore> { new("Anne Marie", umlauts ? "De Müller" : "De Luca") };
            var newAuthors = new List<Autore> { new("Anne Marie", umlauts ? "De Müller" : "De Luca") };
            if (i % 2 == 1)
            {
                oldAuthors.Add(new("Jörg", "Ärztin"));
                newAuthors.Add(new("Jörg", "Ärztin"));
            }

            var durata = i == 2 ? new TimeSpan(1, 2, 3) : new TimeSpan(0, 3, 5 + i);
            var ruolo = (i % 3) switch { 0 => Old.Ruolo.PP, 1 => Old.Ruolo.SF, _ => Old.Ruolo.SI };
            var tipo = i % 2 == 0 ? Old.TipoGenerazione.OperaSuDisco : Old.TipoGenerazione.DalVivo;

            old.Lines.Add(new Old.DcpLine
            {
                Titolo = $"Track {i} " + (umlauts ? "Größe & Co" : "plain"),
                Autori = oldAuthors.ToArray(),
                Durata = durata,
                Gensiae = Old.GenereSiae.ML,
                Ruolo = ruolo,
                TipoGenerazione = tipo,
                Marca = emptyOptionals ? "" : " sonoton ",
                Edizione = emptyOptionals ? "" : " Ed. Süd ",
                SiglaNum = emptyOptionals ? "" : "ABC-12",
                Esecutori = emptyOptionals ? "" : "Björk|Ünal",
            });
            dcp.Lines.Add(new DcpLine
            {
                Titolo = old.Lines[i].Titolo,
                Autori = newAuthors,
                Durata = durata,
                GenereSiae = "ML",
                Ruolo = ruolo.ToString(),
                TipoGenerazione = ((int)tipo).ToString(),
                Marca = old.Lines[i].Marca,
                Edizione = old.Lines[i].Edizione,
                SiglaNum = old.Lines[i].SiglaNum,
                Esecutori = old.Lines[i].Esecutori,
            });
        }

        return (old, dcp);
    }
}
