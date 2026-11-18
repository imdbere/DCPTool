using System.Globalization;
using System.Text.RegularExpressions;
using DcpTool.Core.Export;
using DcpTool.Core.Model;
using DcpTool.Core.Settings;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2010.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DcpTool.Export;

/// <summary>
/// Fills the paper form "Allegato B, MOD. 2017" (an embedded Word template). Cells are found by the text of
/// their label, so a reworked template keeps working as long as the labels stay. The form has room for
/// ten music lines: more lines repeat the whole form on further pages.
/// </summary>
public sealed class DocxExporter : IDocumentExporter
{
    private const int LinesPerPage = 10;
    private const string TemplateResource = "DcpTool.Export.dcp-template.docx";

    public string Id => "docx";
    public string DisplayName => "Word document";
    public string FileExtension => ".docx";

    public void Export(Dcp dcp, CompanyProfile company, Stream output, Stream? template = null)
    {
        ArgumentNullException.ThrowIfNull(dcp);
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(output);

        using var buffer = new MemoryStream();
        if (template is not null)
        {
            template.CopyTo(buffer);
            buffer.Position = 0;
        }
        else
        {
            using var builtIn = typeof(DocxExporter).Assembly.GetManifestResourceStream(TemplateResource)
                                ?? throw new ExportException("The Word template is missing from the application.");
            builtIn.CopyTo(buffer);
        }

        WordprocessingDocument doc;
        try
        {
            doc = WordprocessingDocument.Open(buffer, true);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or FileFormatException)
        {
            throw new ExportException("The template is not a Word document (.docx).", ex);
        }

        using (doc)
        {
            var body = doc.MainDocumentPart?.Document?.Body
                       ?? throw new ExportException("The Word template has no document body.");
            Fill(body, dcp, company);
            doc.Save();
        }

        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    private static void Fill(Body body, Dcp dcp, CompanyProfile company)
    {
        var table = body.Elements<Table>().FirstOrDefault()
                    ?? throw new ExportException("The Word template contains no form table.");
        var intro = body.ChildElements.TakeWhile(e => e != table).OfType<Paragraph>().ToList();
        if (intro.Count == 0)
        {
            throw new ExportException("The Word template has no title paragraphs.");
        }

        var pageCount = Math.Max(1, (dcp.Lines.Count + LinesPerPage - 1) / LinesPerPage);
        var usedSdtIds = body.Descendants<SdtId>().Where(i => i.Val is not null).Select(i => i.Val!.Value).ToHashSet();
        var pages = new List<Table> { table };
        OpenXmlElement last = table;
        for (var page = 1; page < pageCount; page++)
        {
            // Each page is a full copy of the form. The page break goes on its first paragraph, so the
            // table of the previous page needs no extra paragraph after it.
            var pageIntro = intro.Select(p => (Paragraph)p.CloneNode(true)).ToList();
            var pageTable = (Table)table.CloneNode(true);
            foreach (var p in pageIntro)
            {
                last = body.InsertAfter(p, last)!;
            }

            last = body.InsertAfter(pageTable, last)!;
            pageIntro[0].ParagraphProperties ??= new ParagraphProperties();
            pageIntro[0].ParagraphProperties!.PageBreakBefore = new PageBreakBefore();
            MakeIdsUnique(pageIntro.Cast<OpenXmlElement>().Append(pageTable), usedSdtIds);
            pages.Add(pageTable);
        }

        for (var i = 0; i < pages.Count; i++)
        {
            FillPage(pages[i], dcp, company, i + 1, dcp.Lines.Skip(i * LinesPerPage).Take(LinesPerPage).ToList());
        }
    }

    // Word wants unique paragraph ids (w14:paraId) and content control ids in a document.
    private static void MakeIdsUnique(IEnumerable<OpenXmlElement> roots, HashSet<int> usedSdtIds)
    {
        foreach (var root in roots)
        {
            var paragraphs = root.Descendants<Paragraph>().ToList();
            if (root is Paragraph self)
            {
                paragraphs.Add(self);
            }

            foreach (var p in paragraphs)
            {
                p.ParagraphId = null;
                p.TextId = null;
            }

            foreach (var id in root.Descendants<SdtId>())
            {
                int next;
                do
                {
                    next = Random.Shared.Next(1, int.MaxValue);
                }
                while (!usedSdtIds.Add(next));

                id.Val = next;
            }
        }
    }

    private static void FillPage(Table table, Dcp dcp, CompanyProfile company, int pageNumber, List<DcpLine> lines)
    {
        var form = new FormTable(table);
        var inv = CultureInfo.InvariantCulture;
        var puntata = dcp.Puntata?.ToString(inv) ?? "";
        var durata = FormatDuration(dcp.Durata);

        // First row pair: the values sit in the row below their labels.
        form.SetBelow("Fornitore del Programma", company.Fornitore);
        form.SetBelow("Direzione Rai Competente", RaiCodeTables.FindSede(dcp.Sede)?.PaperText ?? dcp.Sede);
        form.SetBelow("Data trasmissione Programma", FormatDate(dcp.DataTrasmissione));
        form.SetBelow("Num. Pag.", pageNumber.ToString(inv));

        form.SetAfter("Titolo italiano del", dcp.TitoloItaliano, LabelMatch.StartsWith);
        form.SetAfter("in n.", puntata, LabelMatch.Exact);
        form.SetAfter("puntate della durata di", durata, LabelMatch.StartsWith);
        form.SetAfter("Titolo originale", dcp.TitoloOriginale, LabelMatch.StartsWith);
        form.SetAfter("Sottotitolo puntata", dcp.Sottotitolo, LabelMatch.StartsWith);
        form.SetAfter("Durata puntata", durata, LabelMatch.Exact);
        form.SetAfter("Contratto n.", dcp.NumeroContratto, LabelMatch.Exact);
        form.SetAfter("del", FormatDate(dcp.DataContratto), LabelMatch.Exact);
        form.SetAfter("Uorg", dcp.Uorg?.ToString(inv) ?? "", LabelMatch.Exact);
        form.SetAfter("Matricola", dcp.Matricola?.ToString(inv) ?? "", LabelMatch.Exact);
        form.SetAfter("Puntata n.", puntata, LabelMatch.Exact);
        form.SetAfter("Nominativo Fornitore incaricato", company.Compilatore, LabelMatch.StartsWith);
        form.SetAfter("Responsabile Rai del Programma", dcp.ResponsabileRai, LabelMatch.StartsWith);
        form.SetAfter("legale rappresentante", company.LegaleRappresentante, LabelMatch.Contains);

        // A DCP without music declares that no work was used.
        form.SetCheckbox("Opere musicali", dcp.Lines.Count > 0);
        form.SetCheckbox("Nessuna opera dell", dcp.Lines.Count == 0);

        var firstRow = form.FindRow("N. progr.", LabelMatch.StartsWith) + 1;
        var first = (pageNumber - 1) * LinesPerPage;
        for (var i = 0; i < LinesPerPage; i++)
        {
            var row = form.Rows.ElementAtOrDefault(firstRow + i)
                      ?? throw new ExportException("The Word template has fewer than 10 music rows.");
            var cells = FormTable.CellsOf(row);
            if (cells.Count < 9)
            {
                throw new ExportException("A music row of the Word template has fewer than 9 cells.");
            }

            FormTable.Write(cells[0], (first + i + 1).ToString(inv));
            var line = i < lines.Count ? lines[i] : null;
            string[] values = line is null
                ? new string[8].Select(_ => "").ToArray()
                :
                [
                    line.GenereSiae,
                    line.Ruolo,
                    line.Titolo,
                    string.Join(", ", line.Autori.Where(a => !a.IsEmpty).Select(a => a.FullName)),
                    line.Esecutori,
                    FormatDuration(line.Durata),
                    line.Marca + " " + line.SiglaNum,
                    // Libraries such as Sonoton have no publisher, their label stands in for it
                    string.IsNullOrEmpty(line.Edizione) ? line.Marca : line.Edizione,
                ];
            for (var c = 0; c < values.Length; c++)
            {
                FormTable.Write(cells[c + 1], values[c]);
            }
        }
    }

    internal static string FormatDuration(TimeSpan d) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)d.TotalHours:00}:{d.Minutes:00}:{d.Seconds:00}");

    private static string FormatDate(DateOnly? d) =>
        d?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";
}

internal enum LabelMatch { Exact, StartsWith, Contains }

/// <summary>The form's table, addressed by label text instead of row/cell numbers.</summary>
internal sealed class FormTable(Table table)
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public List<TableRow> Rows { get; } = table.Elements<TableRow>().ToList();

    // Value cells already filled. A label lookup must never land on one: a user value such as a title
    // "del" or "Matricola" would otherwise be taken for the label and the real value written after it.
    private readonly HashSet<TableCell> _filled = new(ReferenceEqualityComparer.Instance);

    /// <summary>Cells of a row in reading order, including those wrapped in a content control (the checkboxes).</summary>
    public static List<TableCell> CellsOf(TableRow row) => row.Descendants<TableCell>().ToList();

    private static string TextOf(TableCell cell) => Spaces.Replace(cell.InnerText, " ").Trim();

    private static bool Matches(TableCell cell, string label, LabelMatch mode)
    {
        var text = TextOf(cell);
        return mode switch
        {
            LabelMatch.Exact => text.Equals(label, StringComparison.OrdinalIgnoreCase),
            LabelMatch.StartsWith => text.StartsWith(label, StringComparison.OrdinalIgnoreCase),
            _ => text.Contains(label, StringComparison.OrdinalIgnoreCase),
        };
    }

    private (int Row, int Cell) Find(string label, LabelMatch mode)
    {
        for (var r = 0; r < Rows.Count; r++)
        {
            var cells = CellsOf(Rows[r]);
            for (var c = 0; c < cells.Count; c++)
            {
                if (!_filled.Contains(cells[c]) && Matches(cells[c], label, mode))
                {
                    return (r, c);
                }
            }
        }

        throw new ExportException($"The Word template has no cell labelled \"{label}\".");
    }

    public int FindRow(string label, LabelMatch mode) => Find(label, mode).Row;

    public void SetAfter(string label, string value, LabelMatch mode)
    {
        var (r, c) = Find(label, mode);
        var cells = CellsOf(Rows[r]);
        if (c + 1 >= cells.Count)
        {
            throw new ExportException($"The Word template has no value cell after \"{label}\".");
        }

        Write(cells[c + 1], value);
        _filled.Add(cells[c + 1]);
    }

    public void SetBelow(string label, string value)
    {
        var (r, c) = Find(label, LabelMatch.StartsWith);
        var column = GridStart(CellsOf(Rows[r]), c);
        var below = r + 1 < Rows.Count ? CellsOf(Rows[r + 1]) : [];
        for (var i = 0; i < below.Count; i++)
        {
            if (GridStart(below, i) == column)
            {
                Write(below[i], value);
                _filled.Add(below[i]);
                return;
            }
        }

        throw new ExportException($"The Word template has no value cell below \"{label}\".");
    }

    private static int GridStart(List<TableCell> cells, int index) =>
        cells.Take(index).Sum(c => (int?)c.TableCellProperties?.GridSpan?.Val?.Value ?? 1);

    /// <summary>Ticks the content-control checkbox that follows the label cell.</summary>
    public void SetCheckbox(string label, bool on)
    {
        var (r, c) = Find(label, LabelMatch.StartsWith);
        var cells = CellsOf(Rows[r]);
        var sdt = c + 1 < cells.Count ? cells[c + 1].Ancestors<SdtCell>().FirstOrDefault() : null;
        var box = sdt?.SdtProperties?.Descendants<DocumentFormat.OpenXml.Office2010.Word.Checked>().FirstOrDefault();
        if (box is null)
        {
            throw new ExportException($"The Word template has no checkbox next to \"{label}\".");
        }

        box.Val = on ? OnOffValues.One : OnOffValues.Zero;
        var text = cells[c + 1].Descendants<Text>().FirstOrDefault();
        if (text is not null)
        {
            text.Text = on ? "☒" : "☐";
        }
    }

    /// <summary>Replaces the cell content by one centered 8 pt paragraph (the look of the v1 export).</summary>
    public static void Write(TableCell cell, string text)
    {
        foreach (var p in cell.Elements<Paragraph>().ToList())
        {
            p.Remove();
        }

        var clean = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        cell.Append(new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(
                new RunProperties(new FontSize { Val = "16" }),
                new Text(clean) { Space = SpaceProcessingModeValues.Preserve })));
    }
}
