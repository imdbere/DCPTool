using DcpTool.Core.Model;

namespace DcpTool.Core.Import;

/// <param name="Paths">What the user dropped or selected: audio files, sidecar files and folders, in any mix.</param>
/// <param name="ExistingLines">The lines already in the DCP. Sidecars that belong to none of the new audio files are tried on these.</param>
/// <param name="Defaults">The codes new lines get.</param>
public sealed record ImportRequest(
    IReadOnlyList<string> Paths,
    IReadOnlyList<DcpLine> ExistingLines,
    LineDefaults Defaults);

public enum ImportSeverity
{
    Info,
    Warning,
    Error,
}

/// <param name="File">File name (not the full path) the message is about, if any.</param>
/// <param name="Text">The message in English: for logs and diagnostics, and the fallback text.</param>
public sealed record ImportMessage(ImportSeverity Severity, string? File, string Text)
{
    /// <summary>Stable identifier of the message, so the UI can word it in its language. Null: only <see cref="Text"/> exists.</summary>
    public string? Code { get; init; }

    /// <summary>The values <see cref="Text"/> is built from, in order. Empty when there are none.</summary>
    public IReadOnlyList<object> Args { get; init; } = [];
}

/// <param name="Done">Files finished so far.</param>
/// <param name="Total">Files to process.</param>
/// <param name="CurrentFile">Name of the file being read.</param>
public sealed record ImportProgress(int Done, int Total, string? CurrentFile);

public sealed class ImportResult
{
    /// <summary>Lines for the new audio files, in the order of the request.</summary>
    public List<DcpLine> NewLines { get; } = [];

    /// <summary>
    /// Completed copies of existing lines (same <see cref="DcpLine.Id"/>), e.g. Artlist lines whose cue
    /// sheet was added later. The caller replaces the originals with them. The import never changes
    /// <see cref="ImportRequest.ExistingLines"/> itself, so it is safe to run on a background thread.
    /// </summary>
    public List<DcpLine> CompletedLines { get; } = [];

    public List<ImportMessage> Messages { get; } = [];

    public bool HasErrors => Messages.Any(m => m.Severity == ImportSeverity.Error);
}

/// <summary>Turns dropped files into DCP lines, using the registered <see cref="IMusicSource"/>s.</summary>
public interface IImportService
{
    IReadOnlyList<IMusicSource> Sources { get; }

    /// <summary>True when the path is a folder, an audio file or a sidecar file of one of the sources. For drag-over feedback and file dialogs.</summary>
    bool CanImport(string path);

    /// <summary>File name patterns for the "add files" dialog, e.g. <c>*.mp3</c>, <c>*.wav</c>, <c>*.xlsx</c>.</summary>
    IReadOnlyList<string> FilePatterns { get; }

    /// <summary>
    /// Never throws for a bad file: problems are reported in <see cref="ImportResult.Messages"/> and the
    /// other files are still imported. Only cancellation throws.
    /// </summary>
    ImportResult Import(ImportRequest request, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);
}
