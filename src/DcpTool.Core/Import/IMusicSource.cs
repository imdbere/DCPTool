using DcpTool.Core.Model;

namespace DcpTool.Core.Import;

/// <summary>
/// A file that accompanies audio files and carries the data their tags lack, e.g. an Artlist cue sheet.
/// Each source defines its own implementation.
/// </summary>
public interface ISidecar
{
    /// <summary>Full path of the file it was read from.</summary>
    string Path { get; }
}

/// <param name="Line">The new line. The source fills the music data and <see cref="DcpLine.Origin"/>; the import sets the default codes.</param>
/// <param name="UsedSidecar">The sidecar the data came from, if any.</param>
/// <param name="Warning">Something the user has to do for this line, e.g. "no cue sheet found". Null when the line is complete.</param>
public sealed record SourceLine(DcpLine Line, ISidecar? UsedSidecar = null, string? Warning = null);

/// <summary>
/// One music library or shop (musicmedia.it, Artlist, ...): knows how to recognise its files and how to
/// turn them into DCP lines.
/// <para>
/// To add a source, implement this interface (usually by deriving from <see cref="MusicSourceBase"/>)
/// and register it with the import service. Rules every source must follow:
/// never put the performer into Autori unless the library guarantees the performer is the composer,
/// and leave Autori empty (with <see cref="LineOrigin.Incomplete"/> set) rather than guess.
/// </para>
/// </summary>
public interface IMusicSource
{
    /// <summary>Stable identifier, stored in <see cref="LineOrigin.SourceId"/>. Lower case, no spaces.</summary>
    string Id { get; }

    /// <summary>Name shown to the user.</summary>
    string DisplayName { get; }

    /// <summary>One sentence for the user on what this source needs, e.g. where to download its cue sheets. Null when tags are enough.</summary>
    string? WorkflowHint { get; }

    /// <summary>True when <paramref name="path"/> looks like one of this source's sidecar files. Cheap: by name, the file is not opened.</summary>
    bool IsSidecarFile(string path);

    /// <summary>The sidecar files of this source in a folder that contains audio files.</summary>
    IEnumerable<string> FindSidecarFiles(string directory);

    /// <summary>Reads a sidecar file. Throws <see cref="ImportException"/> when it isn't one after all.</summary>
    ISidecar ReadSidecar(string path);

    /// <summary>
    /// How sure the source is that the file is one of its own: 0 = not mine, 100 = certain.
    /// The source with the highest value creates the line. <paramref name="sidecars"/> are this source's own.
    /// </summary>
    int Recognise(AudioFile file, IReadOnlyList<ISidecar> sidecars);

    SourceLine CreateLine(AudioFile file, IReadOnlyList<ISidecar> sidecars);

    /// <summary>
    /// Completes lines that were imported before their sidecar was available. The source may change the
    /// given lines (the import service passes copies) and returns one entry per line it changed.
    /// </summary>
    IReadOnlyList<SidecarMatch> CompleteLines(IReadOnlyList<DcpLine> lines, IReadOnlyList<ISidecar> sidecars);
}

/// <param name="Line">The line that was completed.</param>
/// <param name="Sidecar">The sidecar that completed it.</param>
public sealed record SidecarMatch(DcpLine Line, ISidecar Sidecar);

/// <summary>A source whose files carry everything in their tags only has to override two members.</summary>
public abstract class MusicSourceBase : IMusicSource
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public virtual string? WorkflowHint => null;

    public virtual bool IsSidecarFile(string path) => false;
    public virtual IEnumerable<string> FindSidecarFiles(string directory) => [];
    public virtual ISidecar ReadSidecar(string path) => throw new ImportException($"{DisplayName} has no sidecar files.");

    public abstract int Recognise(AudioFile file, IReadOnlyList<ISidecar> sidecars);
    public abstract SourceLine CreateLine(AudioFile file, IReadOnlyList<ISidecar> sidecars);

    public virtual IReadOnlyList<SidecarMatch> CompleteLines(IReadOnlyList<DcpLine> lines, IReadOnlyList<ISidecar> sidecars) => [];
}
