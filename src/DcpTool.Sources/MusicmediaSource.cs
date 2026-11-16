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
            .Select(Autore.Parse)
            .Where(a => !a.IsEmpty)
            .DistinctBy(a => a.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var esecutori = SplitValues(tags.Performers).ToList();
        if (esecutori.Count == 0)
        {
            esecutori = autori.Select(a => a.FullName).ToList();
        }

        var title = tags.Title ?? "";
        var siglaNum = (tags.Album ?? "").Split('-')[0];

        var line = new DcpLine
        {
            Titolo = title.Contains("---") ? title.Split("---")[0] : title,
            Autori = autori,
            Esecutori = string.Join(", ", esecutori),
            Durata = file.Duration,
            Marca = LabelFor(siglaNum),
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

    internal static string LabelFor(string? siglaNum)
    {
        var prefix = new string((siglaNum ?? "").TrimStart().TakeWhile(char.IsLetter).ToArray());
        return Labels.TryGetValue(prefix, out var label) ? label : DefaultLabel;
    }

    /// <summary>Values the file stores, each split at <c>|</c> (Sonoton packs several names into one value).</summary>
    internal static IEnumerable<string> SplitValues(IEnumerable<string> values) =>
        values.Where(v => v is not null)
            .SelectMany(v => v.Split('|'))
            .Select(v => v.Trim())
            .Where(v => v.Length > 0);
}
