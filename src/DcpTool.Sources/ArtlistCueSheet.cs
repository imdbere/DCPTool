// Reads Artlist "cue_sheet_<title>_<songId>.xlsx" files.
//
// No Excel library on purpose: Artlist's styles.xml contains colours like "#A0A0A0", which are not
// valid aRGB, so strict readers refuse the file. We only need cell text, so the .xlsx is opened as the
// zip it is and sharedStrings + the first sheet are read.
//
// The sheet is a vertical list of sections. Column A names the section ("Album Info", "Song Details",
// "Publisher Info", "Writers", "Public Domain Disclaimer"); the same row holds the column headers in
// B..; the rows below hold one record each, until the next section label. Headers are looked up by
// name, so a reordered column or an extra writer row doesn't break anything.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DcpTool.Core.Import;

namespace DcpTool.Sources;

public sealed record ArtlistWriter(
    string FullName,      // "Oliver Kobák" (first name first)
    string RoleCode,      // CWR-style: C, A, CA, AR, AD ...
    string? Pro,
    string? Ipi,
    string? Split)
{
    /// <summary>Role code for the paper DCP form (A, E, arr, Y).</summary>
    public string DcpRole => RoleCode.ToUpperInvariant() switch
    {
        "C" or "A" or "CA" or "AC" => "A",
        "AR" => "arr",
        "AD" or "TR" or "SR" => "E",
        _ => "Y",
    };
}

public sealed record ArtlistCueSheet(
    string Path,
    string? Label,
    string? AlbumTitle,
    string? AlbumUpc,
    string SongTitle,
    string? PrimaryArtists,
    string? FeaturedArtists,
    string? Isrc,
    string? Iswc,
    TimeSpan? Duration,
    string? PublisherName,
    string? PublisherPro,
    string? PublisherIpi,
    IReadOnlyList<ArtlistWriter> Writers,
    bool? PublicDomain,
    string? SongId) : ISidecar
{
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Primary + featured artists, comma-separated, for Esecutori.</summary>
    public string Performers => string.Join(", ",
        new[] { PrimaryArtists, FeaturedArtists }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>True if this cue sheet belongs to a file with these Title / Artist tags. A null artist matches any.</summary>
    public bool Matches(string? titleTag, string? artistTag) =>
        Norm(titleTag) == Norm(SongTitle) &&
        (artistTag is null || Norm(artistTag) == Norm(PrimaryArtists));

    /// <summary>
    /// Fallback for files without usable tags: Artlist names its downloads
    /// "Artist - Title[ - Version].mp3", so both have to appear in the file name.
    /// </summary>
    public bool MatchesFileName(string fileName)
    {
        var name = Norm(System.IO.Path.GetFileNameWithoutExtension(fileName));
        return PrimaryArtists is not null &&
            name.Contains(Norm(SongTitle)) && name.Contains(Norm(PrimaryArtists));
    }

    internal static string Norm(string? s) =>
        Regex.Replace((s ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");
}

public static class ArtlistCueSheetReader
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Throws <see cref="ImportException"/> when the file isn't an Artlist cue sheet.</summary>
    public static ArtlistCueSheet Read(string path)
    {
        try
        {
            return ReadCore(path);
        }
        catch (ImportException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Any .xlsx is offered to this reader, so whatever a foreign workbook makes it throw
            // (zip, XML, number parsing, unsupported compression...) means "not a cue sheet"
            throw new ImportException($"Not an Artlist cue sheet: {ex.Message}", ex);
        }
    }

    private static ArtlistCueSheet ReadCore(string path)
    {
        var grid = ReadFirstSheet(path);
        var sections = SplitSections(grid);

        Dictionary<string, string?> One(string section) =>
            sections.TryGetValue(section, out var rows) && rows.Count > 0 ? rows[0] : new();

        var album = One("album info");
        var song = One("song details");
        var pub = One("publisher info");

        var writers = sections.TryGetValue("writers", out var wRows)
            ? wRows.Where(r => !string.IsNullOrWhiteSpace(Get(r, "full legal name")))
                .Select(r => new ArtlistWriter(
                    Get(r, "full legal name")!,
                    Get(r, "role") ?? "",
                    Get(r, "pro affiliation"),
                    Get(r, "ipi"),
                    Get(r, "split")))
                .ToList()
            : [];

        bool? publicDomain = null;
        if (sections.TryGetValue("public domain disclaimer", out var pd) && pd.Count > 0)
        {
            var v = pd[0].Values.FirstOrDefault(x => x is not null);
            if (v is not null)
            {
                publicDomain = v.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
        }

        var title = Get(song, "song title")
            ?? throw new ImportException("Not an Artlist cue sheet: no Song Title found.");

        var idMatch = Regex.Match(System.IO.Path.GetFileNameWithoutExtension(path), @"_(\d+)$");

        return new ArtlistCueSheet(
            Path: path,
            Label: Get(album, "label"),
            AlbumTitle: Get(album, "album title"),
            AlbumUpc: Get(album, "upc"),
            SongTitle: title,
            PrimaryArtists: Get(song, "primary artist"),
            FeaturedArtists: Get(song, "featured artist"),
            Isrc: Get(song, "isrc"),
            Iswc: Get(song, "iswc"),
            Duration: ParseDuration(Get(song, "duration")),
            PublisherName: Get(pub, "publisher name"),
            PublisherPro: Get(pub, "pro affiliation"),
            PublisherIpi: Get(pub, "ipi"),
            Writers: writers,
            PublicDomain: publicDomain,
            SongId: idMatch.Success ? idMatch.Groups[1].Value : null);
    }

    /// <summary>Value of the first column whose header contains <paramref name="key"/>. "N/A" and empty count as missing.</summary>
    private static string? Get(Dictionary<string, string?> row, string key)
    {
        foreach (var (header, value) in row)
        {
            if (header.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(value) || value.Trim().Equals("N/A", StringComparison.OrdinalIgnoreCase)
                    ? null : value.Trim();
            }
        }
        return null;
    }

    private static TimeSpan? ParseDuration(string? s)
    {
        if (s is null)
        {
            return null;
        }
        string[] formats = [@"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss"];
        return TimeSpan.TryParseExact(s, formats, CultureInfo.InvariantCulture, out var t) ? t : null;
    }

    // section name (lower case) -> records (header -> value)
    private static Dictionary<string, List<Dictionary<string, string?>>> SplitSections(SortedDictionary<int, SortedDictionary<int, string>> grid)
    {
        var result = new Dictionary<string, List<Dictionary<string, string?>>>();
        List<Dictionary<string, string?>>? current = null;
        SortedDictionary<int, string>? headers = null;

        foreach (var (_, cells) in grid)
        {
            if (cells.TryGetValue(1, out var label) && !string.IsNullOrWhiteSpace(label))
            {
                current = [];
                result[label.Trim().ToLowerInvariant()] = current;
                headers = new(cells.Where(c => c.Key > 1).ToDictionary(c => c.Key, c => c.Value.Trim()));
                continue;
            }
            if (current is null || headers is null)
            {
                continue;
            }

            var rec = new Dictionary<string, string?>();
            if (headers.Count == 0)                       // section without headers (Public Domain)
            {
                foreach (var (col, v) in cells) rec[$"col{col}"] = v;
            }
            else
            {
                foreach (var (col, h) in headers) rec[h] = cells.GetValueOrDefault(col);
            }
            current.Add(rec);
        }
        return result;
    }

    // row -> col -> text (1-based)
    private static SortedDictionary<int, SortedDictionary<int, string>> ReadFirstSheet(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XDocument Load(string name) =>
            XDocument.Load((zip.GetEntry(name) ?? throw new InvalidDataException($"missing {name}")).Open());

        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is not null)
        {
            shared = Load("xl/sharedStrings.xml").Root!.Elements(M + "si")
                .Select(si => string.Concat(si.Descendants(M + "t")
                    .Where(t => t.Parent?.Name != M + "rPh")   // skip phonetic hints
                    .Select(t => t.Value)))
                .ToList();
        }

        var sheetPath = "xl/worksheets/sheet1.xml";
        var firstSheet = Load("xl/workbook.xml").Root!.Element(M + "sheets")?.Elements(M + "sheet").FirstOrDefault();
        var rid = firstSheet?.Attribute(R + "id")?.Value;
        if (rid is not null && zip.GetEntry("xl/_rels/workbook.xml.rels") is not null)
        {
            var target = Load("xl/_rels/workbook.xml.rels").Root!.Elements(PR + "Relationship")
                .FirstOrDefault(e => e.Attribute("Id")?.Value == rid)?.Attribute("Target")?.Value;
            if (target is not null)
            {
                sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            }
        }

        var grid = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var c in Load(sheetPath).Descendants(M + "c"))
        {
            var reference = c.Attribute("r")?.Value;
            if (reference is null)
            {
                continue;
            }
            var (row, col) = ParseRef(reference);

            var type = c.Attribute("t")?.Value;
            string? text = type switch
            {
                "s" => int.TryParse(c.Element(M + "v")?.Value, out var i) && i < shared.Count ? shared[i] : null,
                "inlineStr" => string.Concat(c.Descendants(M + "t").Select(t => t.Value)),
                _ => c.Element(M + "v")?.Value,
            };
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (!grid.TryGetValue(row, out var r))
            {
                grid[row] = r = [];
            }
            r[col] = text;
        }
        return grid;
    }

    private static (int row, int col) ParseRef(string reference)
    {
        int col = 0, i = 0;
        for (; i < reference.Length && char.IsLetter(reference[i]); i++)
        {
            col = col * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
        }
        return (int.Parse(reference[i..], CultureInfo.InvariantCulture), col);
    }
}
