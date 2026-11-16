using DcpTool.Core.Model;
using DcpTool.Core.Persistence;

namespace DcpTool.Core.Library;

public enum LibraryUploadState
{
    NotUploaded,

    /// <summary>An upload stopped part-way; a draft exists at RAI.</summary>
    Partial,

    Uploaded,
}

/// <summary>One .dcp file of the library, with what the browser shows about it.</summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="RelativePath">Path below the library folder, with <c>/</c> separators.</param>
/// <param name="IsPreset">True for files in the <see cref="DcpLibrary.PresetsFolderName"/> folder.</param>
/// <param name="Error">Why the file could not be read. The other values are empty then.</param>
public sealed record LibraryEntry(
    string Path,
    string RelativePath,
    bool IsPreset,
    string Title,
    string Sottotitolo,
    int? Puntata,
    DateOnly? DataTrasmissione,
    int LineCount,
    LibraryUploadState UploadState,
    string? RaiCodice,
    DateTime Modified,
    string? Error = null)
{
    /// <summary>File name without extension.</summary>
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>Sub-folder below the library, empty for files directly in it.</summary>
    public string Folder => System.IO.Path.GetDirectoryName(RelativePath)?.Replace('\\', '/') ?? "";

    /// <summary>True when every word of <paramref name="search"/> occurs in the name, titles, episode, date or RAI code.</summary>
    public bool Matches(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var haystack = string.Join(' ', RelativePath, Title, Sottotitolo, Puntata?.ToString(), RaiCodice,
            DataTrasmissione?.ToString("dd.MM.yyyy"), DataTrasmissione?.ToString("yyyy-MM-dd"));
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }
}

/// <summary>
/// The user's DCPs as plain <c>.dcp</c> files in one folder they choose (sub-folders allowed), like a
/// notes vault: no database, the folder can be copied, synced or edited outside the app at any time.
/// Presets are ordinary .dcp files without music lines in the <c>Presets</c> sub-folder.
/// Everything here reads the folder afresh; nothing is cached, so there is nothing to get out of step.
/// </summary>
public sealed class DcpLibrary(string folder)
{
    public const string PresetsFolderName = "Presets";

    public string Folder { get; } = Path.GetFullPath(folder);

    public string PresetsFolder => Path.Combine(Folder, PresetsFolderName);

    public bool Exists => Directory.Exists(Folder);

    /// <summary>True when <paramref name="path"/> lies in the library folder or below it.</summary>
    public bool Contains(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var relative = Path.GetRelativePath(Folder, Path.GetFullPath(path));
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>
    /// All .dcp files, newest broadcast first, presets included (see <see cref="LibraryEntry.IsPreset"/>).
    /// Never throws: an unreadable file becomes an entry with <see cref="LibraryEntry.Error"/>, a missing
    /// folder gives an empty list.
    /// </summary>
    public IReadOnlyList<LibraryEntry> Scan(CancellationToken cancellationToken = default)
    {
        if (!Exists)
        {
            return [];
        }

        var entries = new List<LibraryEntry>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(Folder, "*" + DcpFile.Extension, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                // Skips the temp files of AtomicFile and other dot files
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(file).StartsWith('.'))
            {
                continue;
            }

            entries.Add(ReadEntry(file));
        }

        return entries
            .OrderByDescending(e => e.DataTrasmissione ?? DateOnly.MinValue)
            .ThenByDescending(e => e.Puntata ?? 0)
            .ThenBy(e => e.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public LibraryEntry ReadEntry(string file)
    {
        var relative = Path.GetRelativePath(Folder, file).Replace('\\', '/');
        var isPreset = relative.StartsWith(PresetsFolderName + "/", StringComparison.OrdinalIgnoreCase);
        DateTime modified;
        try
        {
            modified = File.GetLastWriteTime(file);
            var dcp = DcpFile.Load(file).Dcp;
            return new LibraryEntry(
                file, relative, isPreset,
                dcp.TitoloItaliano, dcp.Sottotitolo, dcp.Puntata, isPreset ? null : dcp.DataTrasmissione, dcp.Lines.Count,
                dcp.Upload switch
                {
                    null => LibraryUploadState.NotUploaded,
                    { Complete: true } => LibraryUploadState.Uploaded,
                    _ => LibraryUploadState.Partial,
                },
                dcp.Upload?.Codice, modified);
        }
        catch (Exception ex) when (ex is DcpFileException or IOException or UnauthorizedAccessException)
        {
            return new LibraryEntry(file, relative, isPreset, "", "", null, null, 0, LibraryUploadState.NotUploaded, null, default, ex.Message);
        }
    }

    /// <summary>
    /// A free path in <paramref name="subFolder"/> of the library for this DCP:
    /// <c>&lt;title&gt; &lt;episode&gt; &lt;broadcast date&gt;.dcp</c>, with a counter if that name is taken.
    /// </summary>
    public string SuggestPath(Dcp dcp, string subFolder = "")
    {
        var parts = new List<string>();
        var title = string.IsNullOrWhiteSpace(dcp.TitoloItaliano) ? "DCP" : dcp.TitoloItaliano.Trim();
        parts.Add(title);
        if (dcp.Puntata is { } puntata)
        {
            parts.Add(puntata.ToString("00"));
        }

        parts.Add(dcp.DataTrasmissione.ToString("yyyy-MM-dd"));
        return FreePath(Path.Combine(Folder, subFolder), string.Join(' ', parts));
    }

    /// <summary>
    /// Saves the header of <paramref name="dcp"/> as a preset: no lines, no upload, no episode, no dates.
    /// Returns the preset's path. An existing preset of the same name is replaced.
    /// </summary>
    public string SavePreset(Dcp dcp, string name)
    {
        var preset = DcpFile.Clone(dcp);
        preset.Lines.Clear();
        preset.Upload = null;
        preset.Puntata = null;
        preset.Sottotitolo = "";
        preset.DataRegistrazione = null;
        var path = Path.Combine(PresetsFolder, SafeFileName(name) + DcpFile.Extension);
        DcpFile.Save(path, preset);
        return path;
    }

    /// <summary>A new DCP with the preset's programme information and today's broadcast date. Not saved yet.</summary>
    public static Dcp CreateFromPreset(string presetPath)
    {
        var dcp = DcpFile.Load(presetPath).Dcp;
        dcp.Lines.Clear();
        dcp.Upload = null;
        dcp.DataTrasmissione = DateOnly.FromDateTime(DateTime.Today);
        dcp.DataRegistrazione = null;
        return dcp;
    }

    /// <summary>The next episode of the DCP in <paramref name="path"/>: same header, episode + 1, no lines. Not saved yet.</summary>
    public static Dcp CreateNextEpisode(string path) => DcpFile.Load(path).Dcp.CreateNextEpisode();

    /// <summary>A copy of the file next to it ("name (2).dcp"), as it is, except that it counts as never uploaded.</summary>
    public string Duplicate(string path)
    {
        var dcp = DcpFile.Load(path).Dcp;
        dcp.Upload = null;
        var copy = FreePath(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path), startCounterAt: 2);
        DcpFile.Save(copy, dcp);
        return copy;
    }

    /// <summary>Renames a file inside its folder. Returns the new path. Throws <see cref="DcpFileException"/> when the name is taken or invalid.</summary>
    public static string Rename(string path, string newName)
    {
        var target = Path.Combine(Path.GetDirectoryName(path)!, SafeFileName(newName) + DcpFile.Extension);
        if (string.Equals(target, path, StringComparison.Ordinal))
        {
            return path;
        }

        // A change of upper/lower case only is the same file on Windows and macOS
        if (File.Exists(target) && !string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
        {
            throw new DcpFileException($"A DCP named \"{Path.GetFileNameWithoutExtension(target)}\" already exists in this folder.");
        }

        try
        {
            File.Move(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DcpFileException($"Could not rename the file: {ex.Message}", ex);
        }

        return target;
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string((name ?? "").Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (cleaned.Length > 120)
        {
            cleaned = cleaned[..120].Trim();
        }

        return cleaned.Length == 0 ? "DCP" : cleaned;
    }

    private static string FreePath(string directory, string name, int startCounterAt = 0)
    {
        var safe = SafeFileName(name);
        var path = Path.Combine(directory, safe + DcpFile.Extension);
        if (startCounterAt == 0 && !File.Exists(path))
        {
            return path;
        }

        for (var counter = Math.Max(startCounterAt, 2); ; counter++)
        {
            path = Path.Combine(directory, $"{safe} ({counter}){DcpFile.Extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }
}
