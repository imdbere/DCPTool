using DcpTool.Core.Import;
using DcpTool.Core.Model;

namespace DcpTool.Sources;

/// <summary>Porting of v1 <c>DcpLineImporter.Import</c> onto <see cref="IMusicSource"/>s.</summary>
public sealed class ImportService : IImportService
{
    private readonly IAudioFileReader _reader;
    private readonly IReadOnlyList<IMusicSource> _sources;

    public ImportService(IAudioFileReader reader, IEnumerable<IMusicSource> sources)
    {
        _reader = reader;
        _sources = sources.ToList();
    }

    public IReadOnlyList<IMusicSource> Sources => _sources;

    public IReadOnlyList<string> FilePatterns { get; } =
        [.. AtlAudioFileReader.SupportedExtensions.Select(e => "*" + e), "*.xlsx"];

    public bool CanImport(string path) =>
        Directory.Exists(path) || _reader.IsAudioFile(path) || _sources.Any(s => s.IsSidecarFile(path));

    public ImportResult Import(ImportRequest request, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();

        // Classify what was dropped. A folder stands for the audio files directly inside it.
        var audioPaths = new List<string>();
        var droppedSidecarPaths = new List<string>();
        foreach (var path in request.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(path))
            {
                audioPaths.AddRange(ListFiles(path, result).Where(_reader.IsAudioFile)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            }
            else if (!File.Exists(path))
            {
                Add(result, ImportSeverity.Error, path, "File not found.");
            }
            else if (_reader.IsAudioFile(path))
            {
                audioPaths.Add(path);
            }
            else if (_sources.Any(s => s.IsSidecarFile(path)))
            {
                droppedSidecarPaths.Add(path);
            }
            else
            {
                Add(result, ImportSeverity.Warning, path, "Unsupported file type.");
            }
        }
        audioPaths = audioPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Sidecars: the dropped ones, then those next to the audio files.
        var sidecars = new List<(IMusicSource Source, ISidecar Sidecar, bool Dropped)>();
        foreach (var path in droppedSidecarPaths)
        {
            var read = ReadSidecar(path, ImportSeverity.Error, result);
            if (read is { } r)
            {
                sidecars.Add((r.Source, r.Sidecar, true));
            }
        }

        var known = new HashSet<string>(droppedSidecarPaths, StringComparer.OrdinalIgnoreCase);
        var directories = audioPaths.Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in directories)
        {
            foreach (var source in _sources)
            {
                foreach (var path in FindSidecarFiles(source, dir!, result))
                {
                    if (!known.Add(path))
                    {
                        continue;
                    }
                    var read = ReadSidecar(path, ImportSeverity.Warning, result, source);
                    if (read is { } r)
                    {
                        sidecars.Add((r.Source, r.Sidecar, false));
                    }
                }
            }
        }

        // Audio files
        var used = new HashSet<ISidecar>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < audioPaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = audioPaths[i];
            var name = Path.GetFileName(file);
            progress?.Report(new ImportProgress(i, audioPaths.Count, name));

            try
            {
                var audio = _reader.Read(file);
                var (source, own) = ChooseSource(audio, sidecars);
                if (source is null)
                {
                    Add(result, ImportSeverity.Error, file, "No music source recognised this file.");
                    continue;
                }

                var sourceLine = source.CreateLine(audio, own);
                request.Defaults.ApplyTo(sourceLine.Line);
                result.NewLines.Add(sourceLine.Line);
                if (sourceLine.UsedSidecar is not null)
                {
                    used.Add(sourceLine.UsedSidecar);
                }
                if (sourceLine.Warning is not null)
                {
                    Add(result, ImportSeverity.Warning, file, sourceLine.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Add(result, ImportSeverity.Error, file, ex.Message);
            }
        }
        progress?.Report(new ImportProgress(audioPaths.Count, audioPaths.Count, null));

        CompleteExistingLines(request, sidecars.Where(s => s.Dropped && !used.Contains(s.Sidecar)).ToList(), result);
        return result;
    }

    // The source with the highest score wins, the first registered on a tie.
    private (IMusicSource? Source, IReadOnlyList<ISidecar> Sidecars) ChooseSource(
        AudioFile audio, List<(IMusicSource Source, ISidecar Sidecar, bool Dropped)> sidecars)
    {
        IMusicSource? best = null;
        IReadOnlyList<ISidecar> bestOwn = [];
        var bestScore = 0;
        foreach (var source in _sources)
        {
            IReadOnlyList<ISidecar> own = sidecars.Where(s => s.Source == source).Select(s => s.Sidecar).ToList();
            var score = source.Recognise(audio, own);
            if (score > bestScore)
            {
                (best, bestOwn, bestScore) = (source, own, score);
            }
        }
        return (best, bestOwn);
    }

    // Dropped sidecars that no new audio file used are tried on copies of the existing lines.
    private void CompleteExistingLines(
        ImportRequest request, List<(IMusicSource Source, ISidecar Sidecar, bool Dropped)> unused, ImportResult result)
    {
        if (unused.Count == 0)
        {
            return;
        }

        // Copies keep the Id, so the caller can swap them in. Later sidecars see earlier completions.
        var copies = request.ExistingLines.Select(l =>
        {
            var copy = l.Clone();
            copy.Id = l.Id;
            return copy;
        }).ToList();

        var matched = new HashSet<ISidecar>(ReferenceEqualityComparer.Instance);
        var completed = new List<DcpLine>();
        foreach (var group in unused.GroupBy(u => u.Source))
        {
            var sheets = group.Select(g => g.Sidecar).ToList();
            foreach (var match in group.Key.CompleteLines(copies, sheets))
            {
                matched.Add(match.Sidecar);
                if (!completed.Contains(match.Line))
                {
                    completed.Add(match.Line);
                }
                Add(result, ImportSeverity.Info, match.Sidecar.Path,
                    $"Completed \"{match.Line.Titolo}\" with this file.");
            }
        }
        result.CompletedLines.AddRange(completed);

        foreach (var u in unused.Where(u => !matched.Contains(u.Sidecar)))
        {
            Add(result, ImportSeverity.Warning, u.Sidecar.Path, "Matches none of the new files or of the lines in the list.");
        }
    }

    private (IMusicSource Source, ISidecar Sidecar)? ReadSidecar(
        string path, ImportSeverity severityOnError, ImportResult result, IMusicSource? only = null)
    {
        foreach (var source in only is null ? _sources.Where(s => s.IsSidecarFile(path)) : [only])
        {
            try
            {
                return (source, source.ReadSidecar(path));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Add(result, severityOnError, path, ex.Message);
            }
        }
        return null;
    }

    private static IEnumerable<string> FindSidecarFiles(IMusicSource source, string dir, ImportResult result)
    {
        try
        {
            return source.FindSidecarFiles(dir).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Add(result, ImportSeverity.Warning, dir, ex.Message);
            return [];
        }
    }

    private static string[] ListFiles(string dir, ImportResult result)
    {
        try
        {
            return Directory.GetFiles(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Add(result, ImportSeverity.Error, dir, ex.Message);
            return [];
        }
    }

    private static void Add(ImportResult result, ImportSeverity severity, string path, string text) =>
        result.Messages.Add(new ImportMessage(severity, Path.GetFileName(path), text));
}
