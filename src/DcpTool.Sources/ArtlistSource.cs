using System.Globalization;
using DcpTool.Core.Import;
using DcpTool.Core.Model;

namespace DcpTool.Sources;

/// <summary>
/// Artlist: the tags have no composer and the artist is not the composer, so Autori, label, UPC and
/// publisher come from the cue sheet (<c>cue_sheet_*.xlsx</c>). Without it the line is incomplete.
/// </summary>
public sealed class ArtlistSource : MusicSourceBase
{
    public const string SourceId = "artlist";
    private const string CueSheetPattern = "cue_sheet_*.xlsx";

    public override string Id => SourceId;
    public override string DisplayName => "Artlist";
    public override string? WorkflowHint =>
        "Artlist files don't name the composers. Download the cue sheet (Artlist > Downloads > Song License > Cue sheet) and drop it together with the audio file.";

    // Any dropped .xlsx is a candidate; whether it is a cue sheet is found out when it is read.
    public override bool IsSidecarFile(string path) =>
        string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

    public override IEnumerable<string> FindSidecarFiles(string directory) =>
        Directory.EnumerateFiles(directory, CueSheetPattern);

    public override ISidecar ReadSidecar(string path) => ArtlistCueSheetReader.Read(path);

    public override int Recognise(AudioFile file, IReadOnlyList<ISidecar> sidecars)
    {
        if (FindCueSheet(file, sidecars) is not null)
        {
            return 100;
        }
        // Artlist writes "Licensed for video by Artlist.io" into the copyright tag
        return file.Tags.Copyright?.Contains("artlist", StringComparison.OrdinalIgnoreCase) == true ? 90 : 0;
    }

    public override SourceLine CreateLine(AudioFile file, IReadOnlyList<ISidecar> sidecars)
    {
        var tags = file.Tags;
        var cue = FindCueSheet(file, sidecars);

        var line = new DcpLine
        {
            Titolo = (tags.Title ?? "").ToUpper(CultureInfo.InvariantCulture),
            Esecutori = string.Join(", ", MusicmediaSource.SplitValues(tags.Performers)),
            Marca = "Artlist",
            Durata = file.Duration, // always from the file, the cue sheet rounds to the second
            Origin = new LineOrigin
            {
                SourceId = Id,
                FilePath = file.Path,
                FileDuration = file.Duration,
                Isrc = tags.Isrc,
                Incomplete = cue is null,
            },
        };

        if (cue is null)
        {
            return new SourceLine(line, null,
                "No Artlist cue sheet found, so the authors are missing. Download it (Artlist > Downloads > Song License > Cue sheet) and drop it onto the list.");
        }

        Apply(line, cue);
        return new SourceLine(line, cue);
    }

    public override IReadOnlyList<SidecarMatch> CompleteLines(IReadOnlyList<DcpLine> lines, IReadOnlyList<ISidecar> sidecars)
    {
        var cues = sidecars.OfType<ArtlistCueSheet>().ToList();
        var matches = new List<SidecarMatch>();
        foreach (var line in lines.Where(l => !l.HasAutori))
        {
            var performers = string.IsNullOrWhiteSpace(line.Esecutori) ? null : line.Esecutori;
            var candidates = cues.Where(c => c.Matches(line.Titolo, performers)).ToList();
            var cue = Closest(candidates, line.Origin?.FileDuration ?? line.Durata);
            if (cue is null)
            {
                continue;
            }
            Apply(line, cue);
            matches.Add(new SidecarMatch(line, cue));
        }
        return matches;
    }

    private void Apply(DcpLine line, ArtlistCueSheet cue)
    {
        line.Autori = cue.Writers.Select(w => Autore.FromFullName(w.FullName)).ToList();
        line.Titolo = cue.SongTitle.ToUpper(CultureInfo.InvariantCulture);
        if (cue.Performers.Length > 0)
        {
            line.Esecutori = cue.Performers;
        }
        // RAI knows the label as ARTLIST, not by the cue sheet's "Artlist Original"
        line.Marca = "Artlist";
        line.SiglaNum = cue.AlbumUpc ?? cue.SongId ?? "";
        // RAI checks Edizione against its own publisher list and rejects Artlist's publishers
        // ("L'edizione dell'opera n non è valida", seen on 2026-11-16), so it stays empty.
        line.Edizione = "";

        var origin = line.Origin ?? new LineOrigin { SourceId = Id };
        line.Origin = origin with
        {
            SidecarFile = cue.FileName,
            Isrc = cue.Isrc ?? origin.Isrc,
            Iswc = cue.Iswc ?? origin.Iswc,
            Incomplete = false,
        };
    }

    private static ArtlistCueSheet? FindCueSheet(AudioFile file, IReadOnlyList<ISidecar> sidecars)
    {
        var cues = sidecars.OfType<ArtlistCueSheet>().ToList();
        var performers = string.Join(", ", MusicmediaSource.SplitValues(file.Tags.Performers));

        var matches = cues.Where(c => c.Matches(file.Tags.Title, performers.Length > 0 ? performers : null)).ToList();
        if (matches.Count == 0)
        {
            matches = cues.Where(c => c.MatchesFileName(file.Path)).ToList();
        }
        return Closest(matches, file.Duration);
    }

    // Versions of a song (e.g. "No Backing Vocals") share title and artist: prefer the closest duration.
    private static ArtlistCueSheet? Closest(List<ArtlistCueSheet> matches, TimeSpan duration) =>
        matches.OrderBy(c => c.Duration.HasValue ? Math.Abs((c.Duration.Value - duration).TotalSeconds) : double.MaxValue)
            .FirstOrDefault();
}
