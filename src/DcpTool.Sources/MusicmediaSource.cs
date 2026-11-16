using System.Text.RegularExpressions;
using DcpTool.Core.Import;
using DcpTool.Core.Model;

namespace DcpTool.Sources;

/// <summary>
/// musicmedia.it (Sonoton family and partner libraries): everything is in the ID3 tags. It is the
/// fallback source, so it recognises every file with a low score and Artlist wins where it applies.
/// </summary>
public sealed class MusicmediaSource : MusicSourceBase
{
    public const string SourceId = "musicmedia";

    // Catalogue prefix (the leading letters of the CD code, e.g. "SCDV 579") -> label.
    // Every Sonoton prefix says "Sonoton", as v1 did for every file.
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SCD"] = "Sonoton", ["SON"] = "Sonoton", ["SCDV"] = "Sonoton", ["SCDA"] = "Sonoton",
        ["SCDC"] = "Sonoton", ["SAS"] = "Sonoton", ["STT"] = "Sonoton", ["STTV"] = "Sonoton",
        ["SOVS"] = "Sonoton", ["SONV"] = "Sonoton", ["CNS"] = "Sonoton", ["CLC"] = "Sonoton",
        ["MMIT"] = "Musica.it",
        ["CM"] = "Cielo Music",
        ["SCORE"] = "Score",
        ["ISCD"] = "Intersound",
        ["ROCK"] = "Rockshop",
        ["SURE"] = "Surefire",
    };

    private const string DefaultLabel = "Sonoton";

    public override string Id => SourceId;
    public override string DisplayName => "musicmedia.it";

    public override int Recognise(AudioFile file, IReadOnlyList<ISidecar> sidecars) => 10;

    public override SourceLine CreateLine(AudioFile file, IReadOnlyList<ISidecar> sidecars)
    {
        var tags = file.Tags;

        // Missing tags stay blank so they can be filled in by hand.
        var autori = SplitValues(tags.Composers)
            .SelectMany(ParseComposers)
            .Where(a => !a.IsEmpty)
            .DistinctBy(a => a.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var esecutori = SplitValues(tags.Performers).ToList();
        if (esecutori.Count == 0)
        {
            esecutori = autori.Select(a => a.FullName).ToList();
        }

        var title = tags.Title ?? "";
        var siglaNum = CatalogueCode(tags.Album);

        var line = new DcpLine
        {
            Titolo = title.Contains("---") ? title.Split("---")[0] : title,
            Autori = autori,
            Esecutori = string.Join(", ", esecutori),
            Durata = file.Duration,
            Marca = MarcaFor(siglaNum, tags.Publisher),
            SiglaNum = siglaNum,
            Origin = new LineOrigin
            {
                SourceId = Id,
                FilePath = file.Path,
                FileDuration = file.Duration,
                Isrc = tags.Isrc,
            },
        };
        return new SourceLine(line);
    }

    /// <summary>
    /// The album tag starts with the catalogue code: "SCDV 579 - Dark Worlds" (old Sonoton),
    /// "ADZ0149 - HOOKY BEATS", or without a dash "MSC192 Organic &amp; Accentuated".
    /// </summary>
    internal static string CatalogueCode(string? album)
    {
        album = (album ?? "").Trim();

        // A first word like "MSC268" is the code, whatever follows ("MSC268 Constructions - Facts & Figures")
        var first = album.Split(' ', 2)[0];
        if (Regex.IsMatch(first, @"^[A-Za-z]{1,8}\d+$"))
        {
            return first;
        }

        var dash = album.IndexOf('-');
        return dash > 0 ? album[..dash].Trim() : album;
    }

    /// <summary>
    /// One composer value. Sonoton writes one "Cognome, Nome" per value. The partner libraries write a
    /// comma-separated list of full names, often with the collecting society: "Citokid (GEMA), Tai Jason (GEMA)".
    /// </summary>
    internal static IEnumerable<Autore> ParseComposers(string value)
    {
        var withoutSociety = Regex.Replace(value, @"\s*\([A-Za-z]{2,10}\)", "");
        var hadSociety = withoutSociety.Length != value.Length;
        var parts = withoutSociety.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
        {
            return [Autore.FromFullName(withoutSociety)];
        }

        // "Lekaba, Themba": a one-word surname, then the first name
        if (parts.Length == 2 && !hadSociety && !parts[0].Contains(' '))
        {
            return [new Autore(parts[1], parts[0])];
        }

        return parts.Select(Autore.FromFullName);
    }

    /// <summary>
    /// RAI only accepts labels from its own list, and most of musicmedia's partner labels are not in it.
    /// So: the catalogue prefix's label or one of the publishers if RAI knows it, otherwise "Sonoton",
    /// which v1 sent for every musicmedia file and which RAI accepts.
    /// </summary>
    internal static string MarcaFor(string? siglaNum, string? publishers)
    {
        var prefix = new string((siglaNum ?? "").TrimStart().TakeWhile(char.IsLetter).ToArray());
        var candidates = new List<string>();
        if (Labels.TryGetValue(prefix, out var label))
        {
            candidates.Add(label);
        }

        candidates.AddRange((publishers ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => CompanySuffix.Replace(p, "").Trim()));

        return candidates.FirstOrDefault(RaiMarche.Contains) ?? DefaultLabel;
    }

    private static readonly Regex CompanySuffix = new(
        @"\s+(Music\s+)?(GmbH(\s*&\s*Co\.?\s*KG)?|Ltd\.?|S\.?r\.?l\.?|Inc\.?)$",
        RegexOptions.IgnoreCase);

    internal static string LabelFor(string? siglaNum) => MarcaFor(siglaNum, null);

    /// <summary>Values the file stores, each split at <c>|</c> (Sonoton packs several names into one value).</summary>
    internal static IEnumerable<string> SplitValues(IEnumerable<string> values) =>
        values.Where(v => v is not null)
            .SelectMany(v => v.Split('|'))
            .Select(v => v.Trim())
            .Where(v => v.Length > 0);
}
