namespace DcpTool.Core.Import;

/// <summary>
/// The metadata of an audio file, independent of the tag library and of the container (ID3 in mp3,
/// ID3 / LIST-INFO / bext / iXML chunks in wav). Music sources only see this, so they can be tested
/// without audio files.
/// </summary>
public sealed record AudioTags
{
    public string? Title { get; init; }

    /// <summary>The artist tag, one entry per value the file stores. Sources decide how to split further (Sonoton uses <c>|</c>).</summary>
    public IReadOnlyList<string> Performers { get; init; } = [];

    /// <summary>The composer tag, one entry per value the file stores.</summary>
    public IReadOnlyList<string> Composers { get; init; } = [];

    public string? Album { get; init; }
    public string? Copyright { get; init; }
    public string? Publisher { get; init; }
    public string? Isrc { get; init; }
    public string? Comment { get; init; }
    public string? Genre { get; init; }

    /// <summary>
    /// Everything else the file carries, by the tag library's field name (custom ID3 TXXX frames,
    /// <c>bext.*</c>, <c>info.*</c>, <c>ixml.*</c> ...). Keys are compared case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static AudioTags Empty { get; } = new();
}

/// <param name="Path">Full path of the file.</param>
/// <param name="Tags">Its metadata. <see cref="AudioTags.Empty"/> when the file has none.</param>
/// <param name="Duration">Full length of the audio.</param>
public sealed record AudioFile(string Path, AudioTags Tags, TimeSpan Duration)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public interface IAudioFileReader
{
    /// <summary>True for file types the reader supports. Decided by the extension, the file is not opened.</summary>
    bool IsAudioFile(string path);

    /// <summary>Reads tags and duration. Throws <see cref="ImportException"/> when the file can't be read as audio.</summary>
    AudioFile Read(string path);
}

/// <summary>A file could not be imported. The message is meant for the user and doesn't repeat the file name.</summary>
public sealed class ImportException(string message, Exception? inner = null) : Exception(message, inner);
