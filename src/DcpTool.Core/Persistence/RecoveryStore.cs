using System.Text.Json;
using DcpTool.Core.Model;
using DcpTool.Core.Settings;

namespace DcpTool.Core.Persistence;

/// <param name="Dcp">The DCP as it was when the app last wrote it.</param>
/// <param name="FilePath">The .dcp file it belongs to, or null if it was never saved to a file.</param>
/// <param name="HasUnsavedChanges">True when it differs from <paramref name="FilePath"/> (always true without a file).</param>
/// <param name="SavedAt">When the snapshot was written.</param>
public sealed record RecoveredSession(Dcp Dcp, string? FilePath, bool HasUnsavedChanges, DateTimeOffset SavedAt);

/// <summary>
/// Keeps a snapshot of the open DCP in the app's data folder, so work survives a crash or a killed process: the next
/// start restores it. The app writes the snapshot shortly after every change and clears it when the window closes
/// normally or the user discards the changes ("Nicht speichern"), so only an abnormal end leaves one behind. The
/// user's own .dcp file is only written by Speichern.
/// </summary>
public sealed class RecoveryStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions MetaOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private string DcpFilePath => Path.Combine(paths.RecoveryFolder, "session" + DcpFile.Extension);
    private string MetaFilePath => Path.Combine(paths.RecoveryFolder, "session.json");

    /// <summary>Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the snapshot can't be written.</summary>
    public void Write(Dcp dcp, string? filePath, bool hasUnsavedChanges)
    {
        // The DCP first: a meta file without its DCP is ignored by TryRead, the other way round would restore stale data
        AtomicFile.WriteAllText(DcpFilePath, DcpFile.Serialize(dcp));
        var meta = new Meta
        {
            FilePath = filePath,
            HasUnsavedChanges = hasUnsavedChanges || filePath is null,
            SavedAt = DateTimeOffset.Now,
        };
        AtomicFile.WriteAllText(MetaFilePath, JsonSerializer.Serialize(meta, MetaOptions));
    }

    /// <summary>
    /// The last snapshot, or null when there is none or it can't be read. Never throws: a damaged
    /// snapshot must not keep the app from starting.
    /// </summary>
    public RecoveredSession? TryRead()
    {
        try
        {
            if (!File.Exists(DcpFilePath) || !File.Exists(MetaFilePath))
            {
                return null;
            }

            var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(MetaFilePath), MetaOptions);
            if (meta is null)
            {
                return null;
            }

            var dcp = DcpFile.Load(DcpFilePath).Dcp;
            return new RecoveredSession(dcp, meta.FilePath, meta.HasUnsavedChanges || meta.FilePath is null, meta.SavedAt);
        }
        catch (Exception ex) when (ex is DcpFileException or JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes the snapshot. Never throws.</summary>
    public void Clear()
    {
        foreach (var file in new[] { MetaFilePath, DcpFilePath })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover snapshot is only restored once more
            }
        }
    }

    private sealed class Meta
    {
        public string? FilePath { get; set; }
        public bool HasUnsavedChanges { get; set; }
        public DateTimeOffset SavedAt { get; set; }
    }
}
