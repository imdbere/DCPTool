using Xunit;
using DcpTool.Core.Export;
using DcpTool.Core.Model;
using DcpTool.Core.Settings;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DcpTool.Export.Tests;

public class DocxExporterTests
{
    private static readonly CompanyProfile Company = new()
    {
        Fornitore = "Müller Produktionen GmbH",
        Compilatore = "Anna Öttl",
        LegaleRappresentante = "Karl Größer",
    };

    private static Dcp MakeDcp(int lines)
    {
        var dcp = new Dcp
        {
            TitoloItaliano = "Pensieri sul silenzio",
            TitoloOriginale = "Gedanken über Stille",
            Sottotitolo = "Süße Träume",
            Puntata = 7,
            Uorg = 9016,
            Matricola = 996851,
            DataTrasmissione = new DateOnly(2026, 1, 5),
            DataContratto = new DateOnly(2025, 12, 3),
            NumeroContratto = "12/00",
            Durata = new TimeSpan(1, 2, 3),
            Sede = "BZ/16",
            ResponsabileRai = "Brigitte Rossi",
        };
        for (var i = 1; i <= lines; i++)
        {
            dcp.Lines.Add(new DcpLine
            {
                Titolo = $"Täg Ölé {i:000}",
                Autori = [new Autore("Jörg", "Weiß"), new Autore("Ann", "Lee")],
                Esecutori = "Band Ä",
                Durata = TimeSpan.FromSeconds(60 + i),
                Marca = "Sonoton",
                SiglaNum = $"SCD {i}",
            });
        }

        return dcp;
    }

    private static byte[] Export(Dcp dcp)
    {
        using var ms = new MemoryStream();
        new DocxExporter().Export(dcp, Company, ms);
        return ms.ToArray();
    }

    private static string Norm(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    private static string CellText(TableCell c) => string.Concat(c.Descendants<Text>().Select(t => t.Text));

    private static List<List<string>> RowsOf(Table t) =>
        t.Elements<TableRow>().Select(r => r.Descendants<TableCell>().Select(CellText).ToList()).ToList();

    private static List<Table> Tables(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return doc.MainDocumentPart!.Document!.Body!.Elements<Table>().Select(t => (Table)t.CloneNode(true)).ToList();
    }

    /// <summary>The 10 music rows of one page (cells 0..8).</summary>
    private static List<List<string>> MusicRows(Table t)
    {
        var rows = RowsOf(t);
        var header = rows.FindIndex(r => r[0].StartsWith("N. progr."));
        return rows.Skip(header + 1).Take(10).ToList();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(25, 3)]
    public void Pages_numbering_and_every_title_exactly_once(int lines, int pages)
    {
        var dcp = MakeDcp(lines);
        var tables = Tables(Export(dcp));

        Assert.Equal(pages, tables.Count);

        var titles = new List<string>();
        for (var p = 0; p < tables.Count; p++)
        {
            var rows = RowsOf(tables[p]);
            Assert.Equal((p + 1).ToString(), rows[1][3]); // Num. Pag.
            var music = MusicRows(tables[p]);
            Assert.Equal(10, music.Count);
            for (var i = 0; i < 10; i++)
            {
                Assert.Equal((p * 10 + i + 1).ToString(), music[i][0]);
                if (music[i][3].Length > 0)
                {
                    titles.Add(music[i][3]);
                }
            }
        }

        Assert.Equal(dcp.Lines.Select(l => l.Titolo), titles);
    }

    [Fact]
    public void Further_pages_start_on_a_new_page_and_ids_stay_unique()
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(Export(MakeDcp(25))), false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        var breaks = body.Elements<Paragraph>().Count(p => p.ParagraphProperties?.PageBreakBefore is not null);
        Assert.Equal(2, breaks);

        var sdtIds = body.Descendants<SdtId>().Select(i => i.Val!.Value).ToList();
        Assert.Equal(sdtIds.Count, sdtIds.Distinct().Count());
        var paraIds = body.Descendants<Paragraph>().Select(p => p.ParagraphId?.Value).Where(v => v != null).ToList();
        Assert.Equal(paraIds.Count, paraIds.Distinct().Count());
    }

    [Fact]
    public void Header_values_and_line_cells()
    {
        var t = Tables(Export(MakeDcp(3)))[0];
        var rows = RowsOf(t);

        Assert.Equal("Müller Produktionen GmbH", rows[1][0]);
        Assert.Contains("BZ/16", rows[1][1]);
        Assert.Contains("PROG. L. TED.", rows[1][1]);
        Assert.Equal("05.01.2026", rows[1][2]);

        string After(string label)
        {
            var row = rows.First(r => r.Any(c => Norm(c) == Norm(label)));
            return row[row.FindIndex(c => Norm(c) == Norm(label)) + 1];
        }

        Assert.Equal("Pensieri sul silenzio", After(rows.SelectMany(r => r).First(x => x.StartsWith("Titolo italiano"))));
        Assert.Equal("7", After("in n."));
        Assert.Equal("01:02:03", After("puntate della durata di"));
        Assert.Equal("Gedanken über Stille", After(rows.SelectMany(r => r).First(x => x.StartsWith("Titolo originale"))));
        Assert.Equal("Süße Träume", After(rows.SelectMany(r => r).First(x => x.StartsWith("Sottotitolo"))));
        Assert.Equal("01:02:03", After("Durata puntata"));
        Assert.Equal("12/00", After("Contratto n."));
        Assert.Equal("03.12.2025", After("del"));
        Assert.Equal("9016", After("Uorg"));
        Assert.Equal("996851", After("Matricola"));
        Assert.Equal("7", After("Puntata n."));
        Assert.Equal("Anna Öttl", After(rows.SelectMany(r => r).First(x => x.StartsWith("Nominativo Fornitore"))));
        Assert.Equal("Brigitte Rossi", After(rows.SelectMany(r => r).First(x => x.StartsWith("Responsabile Rai"))));
        Assert.Equal("Karl Größer", After(rows.SelectMany(r => r).First(x => x.Contains("legale rappresentante"))));

        var line = MusicRows(t)[1];
        Assert.Equal(["2", "ML", "SF", "Täg Ölé 002", "Jörg Weiß, Ann Lee", "Band Ä", "00:01:02", "Sonoton SCD 2", "Sonoton"], line);
    }

    [Fact]
    public void Empty_numbers_and_dates_stay_empty_and_unknown_sede_prints_its_code()
    {
        var dcp = MakeDcp(1);
        dcp.Puntata = null;
        dcp.Uorg = null;
        dcp.Matricola = null;
        dcp.DataContratto = null;
        dcp.Sede = "XX/99";
        dcp.Lines[0].Edizione = "Own Publisher";
        var rows = RowsOf(Tables(Export(dcp))[0]);

        Assert.Equal("XX/99", rows[1][1]);
        var uorg = rows.First(r => r.Any(c => c.Trim() == "Uorg"));
        Assert.Equal("", uorg[uorg.FindIndex(c => c.Trim() == "Uorg") + 1]);
        var del = rows.First(r => r.Any(c => c.Trim() == "del"));
        Assert.Equal("", del[del.FindIndex(c => c.Trim() == "del") + 1]);
        Assert.Equal("Own Publisher", MusicRows(Tables(Export(dcp))[0])[0][8]);
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(3, true, false)]
    [InlineData(11, true, false)]
    public void Checkboxes(int lines, bool musical, bool none)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(Export(MakeDcp(lines))), false);
        foreach (var table in doc.MainDocumentPart!.Document!.Body!.Elements<Table>())
        {
            bool State(string label)
            {
                var row = table.Elements<TableRow>().First(r => r.Descendants<TableCell>().Any(c => CellText(c).StartsWith(label)));
                var sdt = row.Elements<SdtCell>().First(s => CellText(s.Descendants<TableCell>().First()) is "☒" or "☐"
                    && s.PreviousSibling<OpenXmlElement>() is TableCell prev && CellText(prev).StartsWith(label));
                var on = sdt.SdtProperties!.Descendants<DocumentFormat.OpenXml.Office2010.Word.Checked>().Single().Val!.Value == DocumentFormat.OpenXml.Office2010.Word.OnOffValues.One;
                Assert.Equal(on ? "☒" : "☐", CellText(sdt.Descendants<TableCell>().Single()));
                return on;
            }

            Assert.Equal(musical, State("Opere musicali"));
            Assert.Equal(none, State("Nessuna opera dell"));
            Assert.False(State("Opere filmate"));
            Assert.False(State("Altre opere"));
        }
    }

    [Fact]
    public void No_sample_data_of_the_template_is_left()
    {
        var bytes = Export(new Dcp());
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var xml = doc.MainDocumentPart!.Document!.OuterXml;
        foreach (var w in new[] { "Penn", "Margesin", "Gedanken zur", "BARREN", "Montgomery", "9016", "996851" })
        {
            Assert.DoesNotContain(w, xml);
        }

        Assert.Empty(doc.MainDocumentPart.ImageParts);
        Assert.DoesNotContain("Penn", doc.PackageProperties.LastModifiedBy ?? "");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(25)]
    public void Document_has_no_validation_errors_beyond_the_template(int lines)
    {
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        using var template = typeof(DocxExporter).Assembly.GetManifestResourceStream("DcpTool.Export.dcp-template.docx")!;
        using var templateDoc = WordprocessingDocument.Open(template, false);
        var known = validator.Validate(templateDoc).Select(e => e.Description).ToHashSet();

        using var doc = WordprocessingDocument.Open(new MemoryStream(Export(MakeDcp(lines))), false);
        var fresh = validator.Validate(doc).Select(e => e.Description).Where(d => !known.Contains(d)).ToList();
        Assert.Empty(fresh);
    }

    [Fact]
    public void Durations_keep_their_hours()
    {
        Assert.Equal("01:02:03", DocxExporter.FormatDuration(new TimeSpan(1, 2, 3)));
        Assert.Equal("26:00:00", DocxExporter.FormatDuration(TimeSpan.FromHours(26)));
        Assert.Equal("00:03:41", DocxExporter.FormatDuration(TimeSpan.FromSeconds(221)));
    }

    [Fact]
    public void Values_that_look_like_labels_are_not_taken_for_labels()
    {
        var dcp = MakeDcp(1);
        dcp.TitoloItaliano = "del";
        dcp.TitoloOriginale = "Uorg";
        dcp.Sottotitolo = "Matricola";
        dcp.NumeroContratto = "Puntata n.";
        var rows = RowsOf(Tables(Export(dcp))[0]);

        string AfterLabel(string label, string rowWith)
        {
            var row = rows.Select(r => r.Select(c => c.Trim()).ToList()).First(r => r.Contains(rowWith) && r.Contains(label));
            return row[row.LastIndexOf(label) + 1];
        }

        Assert.Equal("03.12.2025", AfterLabel("del", "Contratto n."));
        Assert.Equal("9016", AfterLabel("Uorg", "Matricola"));
        Assert.Equal("996851", AfterLabel("Matricola", "Uorg"));
        Assert.Equal("7", AfterLabel("Puntata n.", "Uorg"));
    }

    [Fact]
    public void Missing_label_names_the_label()
    {
        var table = new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("Something else"))))));
        var ex = Assert.Throws<ExportException>(() => new FormTable(table).SetAfter("Uorg", "1", LabelMatch.Exact));
        Assert.Contains("Uorg", ex.Message);
    }

    [Fact]
    public void Exporter_metadata()
    {
        var e = new DocxExporter();
        Assert.Equal("docx", e.Id);
        Assert.Equal(".docx", e.FileExtension);
    }

    [Fact]
    public void AReplacementTemplateIsFilledAndAFileThatIsNoWordDocumentIsRefused()
    {
        var dcp = MakeDcp(3);
        using (var template = typeof(DocxExporter).Assembly.GetManifestResourceStream("DcpTool.Export.dcp-template.docx")!)
        using (var output = new MemoryStream())
        {
            new DocxExporter().Export(dcp, Company, output, template);
            var replaced = Tables(output.ToArray());
            var builtIn = Tables(Export(dcp));
            Assert.Equal(builtIn.Count, replaced.Count);
            Assert.Equal(MusicRows(builtIn[0]), MusicRows(replaced[0]));
        }

        using var notWord = new MemoryStream("this is not a Word document"u8.ToArray());
        Assert.Throws<ExportException>(() => new DocxExporter().Export(dcp, Company, new MemoryStream(), notWord));
    }
}
