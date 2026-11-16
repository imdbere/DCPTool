using ATL;
using DcpTool.Core.Import;

namespace DcpTool.Sources;

/// <summary>Reads audio tags with ATL (z440.atl.core).</summary>
public sealed class AtlAudioFileReader : IAudioFileReader
{
    public static IReadOnlyList<string> SupportedExtensions { get; } = [".mp3", ".wav", ".aif", ".aiff", ".flac", ".m4a"];

    private static readonly HashSet<string> Extensions = new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    // ATL joins multi-value fields with a display separator. A control character can't occur in real
    // tags, so we can split the values apart again without mangling names that contain '/' or ';'.
    private const char ValueSeparator = '\u001F';

    static AtlAudioFileReader()
    {
        Settings.DisplayValueSeparator = ValueSeparator;
    }

    public bool IsAudioFile(string path) => Extensions.Contains(Path.GetExtension(path));

    public AudioFile Read(string path)
    {
        Track track;
        try
        {
            track = new Track(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ImportException($"Could not read the audio file: {ex.Message}", ex);
        }

        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in track.AdditionalFields)
        {
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                extra[key] = value;
            }
        }

        var tags = new AudioTags
        {
            Title = Clean(track.Title),
            Performers = Values(track.Artist),
            Composers = Values(track.Composer),
            Album = Clean(track.Album),
            Copyright = Clean(track.Copyright),
            Publisher = Clean(track.Publisher),
            Isrc = Clean(track.ISRC),
            Comment = Clean(track.Comment),
            Genre = Clean(track.Genre),
            Extra = extra,
        };
        return new AudioFile(path, tags, TimeSpan.FromMilliseconds(track.DurationMs));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static List<string> Values(string? s) =>
        (s ?? "").Split(ValueSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
