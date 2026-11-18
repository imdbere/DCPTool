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

/// <summary>One .dcp file of the library, with what the drawer shows about it.</summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="RelativePath">Path below the library folder, with <c>/</c> separators.</param>
/// <param name="IsPreset">True for files in the <see cref="DcpLibrary.PresetsFolderName"/> folder.</param>
/// <param name="Modified">Last write time of the file (local time).</param>
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
    /// <summary>RAI's codes of the programme (organisational unit and programme number), when the DCP has them.</summary>
    public int? Uorg { get; init; }

    /// <inheritdoc cref="Uorg"/>
    public int? Matricola { get; init; }

    /// <summary>File name without extension.</summary>
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>What a scan found.</summary>
/// <param name="Entries">All .dcp files, presets included, in no particular order.</param>
public sealed record LibrarySnapshot(IReadOnlyList<LibraryEntry> Entries)
{
    public static readonly LibrarySnapshot Empty = new([]);
}

/// <summary>
/// The user's DCPs as plain <c>.dcp</c> files in one folder they choose (sub-folders allowed), like a
/// notes vault: no database, the folder can be copied, synced or edited outside the app at any time. The app has no
/// browser for it: Explorer is that, the app lists presets and the DCPs changed last.
/// Presets are ordinary .dcp files without music lines in the <c>Presets</c> sub-folder.
/// Every scan lists the folder afresh. What was read from a file is kept in memory under its path, last write
/// time and size, so a re-scan only reads files that are new or changed, also when they were changed outside the app.
/// </summary>
public sealed class DcpLibrary(string folder)
{
    public const string PresetsFolderName = "Presets";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // Skips the temp files of AtomicFile and other hidden files
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    private readonly Dictionary<string, CachedEntry> _cache = new(PathComparer);
    private readonly Lock _cacheLock = new();

    public string Folder { get; } = Path.GetFullPath(folder);

    public string PresetsFolder => Path.Combine(Folder, PresetsFolderName);

    public bool Exists => Directory.Exists(Folder);

    /// <summary>How many files the last <see cref="ScanAll"/> had to read; the others came from memory.</summary>
    public int LastScanReads { get; private set; }

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
    /// All .dcp files, presets included (see <see cref="LibraryEntry.IsPreset"/>). Never throws for unreadable files or
    /// folders: an unreadable file becomes an entry with <see cref="LibraryEntry.Error"/>, a missing library folder gives
    /// an empty snapshot. Only new and changed files are read, see the class summary. Safe to call from any thread.
    /// </summary>
    public LibrarySnapshot ScanAll(CancellationToken cancellationToken = default)
    {
        if (!Exists)
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }

            LastScanReads = 0;
            return LibrarySnapshot.Empty;
        }

        var entries = new List<LibraryEntry>();
        var seen = new HashSet<string>(PathComparer);
        var reads = 0;
        try
        {
            foreach (var file in new DirectoryInfo(Folder).EnumerateFiles("*", Enumeration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Relative(file.FullName).Split('/').Any(part => part.StartsWith('.')))
                {
                    continue; // dot files (temp files of AtomicFile) and anything in dot folders
                }

                if (file.Extension.Equals(DcpFile.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    seen.Add(file.FullName);
                    entries.Add(GetEntry(file, ref reads));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder went away or became unreadable during the scan: what was found so far
        }

        lock (_cacheLock)
        {
            foreach (var gone in _cache.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _cache.Remove(gone);
            }
        }

        LastScanReads = reads;
        return new LibrarySnapshot(entries);
    }

    private LibraryEntry GetEntry(FileInfo file, ref int reads)
    {
        DateTime writeUtc;
        long size;
        try
        {
            writeUtc = file.LastWriteTimeUtc;
            size = file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reads++;
            return ReadEntry(file.FullName);
        }

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(file.FullName, out var cached) && cached.WriteUtc == writeUtc && cached.Size == size)
            {
                return cached.Entry;
            }
        }

        reads++;
        var entry = ReadEntry(file.FullName);
        lock (_cacheLock)
        {
            _cache[file.FullName] = new CachedEntry(writeUtc, size, entry);
        }

        return entry;
    }

    private static DateTime SafeTime(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTime;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private string Relative(string fullPath) => Path.GetRelativePath(Folder, fullPath).Replace('\\', '/');

    /// <summary>Reads one file, always from disk. Never throws: an unreadable file gives an entry with <see cref="LibraryEntry.Error"/>.</summary>
    public LibraryEntry ReadEntry(string file)
    {
        var relative = Relative(file);
        return Read(file, relative, relative.StartsWith(PresetsFolderName + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads a .dcp file that is not in a library (a file opened from elsewhere). Never throws, like <see cref="ReadEntry"/>.</summary>
    public static LibraryEntry ReadFile(string file) => Read(Path.GetFullPath(file), Path.GetFileName(file), isPreset: false);

    private static LibraryEntry Read(string file, string relative, bool isPreset)
    {
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
                dcp.Upload?.Codice, modified)
            {
                Uorg = dcp.Uorg,
                Matricola = dcp.Matricola,
            };
        }
        catch (Exception ex) when (ex is DcpFileException or IOException or UnauthorizedAccessException)
        {
            return new LibraryEntry(file, relative, isPreset, "", "", null, null, 0, LibraryUploadState.NotUploaded, null,
                SafeTime(new FileInfo(file)), ex.Message);
        }
    }

    // ---------------------------------------------------------------- files written by DCP Tool 0.x

    /// <summary>
    /// When <paramref name="path"/> is a file in the format of DCP Tool 0.x (which cannot read the new format), copies
    /// it into <paramref name="backupRoot"/> under its path below the library, before it is overwritten in the new
    /// format. Returns the copy's path, or null when the file is not in the old format or does not exist.
    /// Throws <see cref="DcpFileException"/> when the copy cannot be made: the file must not be overwritten then.
    /// </summary>
    public string? BackupIfLegacy(string path, string backupRoot)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            if (!DcpFile.Load(path).IsLegacy)
            {
                return null;
            }
        }
        catch (DcpFileException)
        {
            return null; // not a DCP file at all, so not one of DCP Tool 0.x either
        }

        var relative = Contains(path) ? Path.GetRelativePath(Folder, path) : Path.GetFileName(path);
        var target = Path.Combine(backupRoot, relative);
        try
        {
            var directory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(target))
            {
                if (File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(path)))
                {
                    return target; // this very original is kept already
                }

                target = FreePath(directory, Path.GetFileNameWithoutExtension(target), startCounterAt: 2);
            }

            File.Copy(path, target);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DcpFileException($"Could not keep a copy of the original file: {ex.Message}", ex);
        }
    }

    // ---------------------------------------------------------------- creating files

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
        var path = PresetPath(name);
        DcpFile.Save(path, preset);
        return path;
    }

    /// <summary>The path of the preset named <paramref name="name"/>, whether it exists or not.</summary>
    public string PresetPath(string name) => Path.Combine(PresetsFolder, SafeFileName(name) + DcpFile.Extension);

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

    private sealed record CachedEntry(DateTime WriteUtc, long Size, LibraryEntry Entry);
}
