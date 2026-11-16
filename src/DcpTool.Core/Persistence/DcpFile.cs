using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DcpTool.Core.Model;

namespace DcpTool.Core.Persistence;

/// <summary>A .dcp file could not be read or written. The message is meant for the user.</summary>
public sealed class DcpFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <param name="Dcp">The loaded DCP.</param>
/// <param name="Version">Format version of the file: 1 for files written by DCP Tool 0.x, otherwise the envelope's version.</param>
public sealed record DcpFileContent(Dcp Dcp, int Version)
{
    public bool IsLegacy => Version < DcpFile.CurrentVersion;
}

/// <summary>
/// Reads and writes .dcp files.
/// <para>
/// Version 2 is JSON with an envelope: <c>{ "format": "dcp-tool", "version": 2, "dcp": { ... } }</c>,
/// camelCase names, ISO dates (<c>2026-11-16</c>), durations as <c>hh:mm:ss</c>, codes as strings.
/// Files written by DCP Tool 0.x (DataContractJsonSerializer output, no envelope) are still read,
/// see <see cref="LegacyDcpReader"/>. Saving always writes version 2.
/// </para>
/// </summary>
public static class DcpFile
{
    public const string FormatName = "dcp-tool";
    public const int CurrentVersion = 2;
    public const string Extension = ".dcp";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Names like "Kobák" stay readable. The file is never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static DcpFileContent Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DcpFileException($"Could not read \"{path}\": {ex.Message}", ex);
        }

        try
        {
            return Parse(json);
        }
        catch (DcpFileException ex)
        {
            throw new DcpFileException($"\"{Path.GetFileName(path)}\": {ex.Message}", ex);
        }
    }

    public static DcpFileContent Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new DcpFileException("This is not a DCP file (it is not valid JSON).", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new DcpFileException("This is not a DCP file.");
            }

            if (root.TryGetProperty("format", out var format))
            {
                return ParseCurrent(root, format);
            }

            if (LegacyDcpReader.LooksLikeLegacy(root))
            {
                return new DcpFileContent(Normalize(LegacyDcpReader.Read(root)), 1);
            }

            throw new DcpFileException("This is not a DCP file.");
        }
    }

    /// <summary>
    /// Writes the file atomically: the content goes to a temporary file next to the target, which then
    /// replaces it. A crash or a full disk during saving never leaves a half-written .dcp behind.
    /// </summary>
    public static void Save(string path, Dcp dcp)
    {
        try
        {
            AtomicFile.WriteAllText(path, Serialize(dcp));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DcpFileException($"Could not save \"{path}\": {ex.Message}", ex);
        }
    }

    public static string Serialize(Dcp dcp)
    {
        var envelope = new Envelope
        {
            Format = FormatName,
            Version = CurrentVersion,
            SavedWith = typeof(DcpFile).Assembly.GetName().Version?.ToString(3),
            Dcp = dcp,
        };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    /// <summary>A deep copy, made through the file format so it can't drift from what a save/load cycle does.</summary>
    public static Dcp Clone(Dcp dcp) => Parse(Serialize(dcp)).Dcp;

    private static DcpFileContent ParseCurrent(JsonElement root, JsonElement format)
    {
        if (format.ValueKind != JsonValueKind.String || format.GetString() != FormatName)
        {
            throw new DcpFileException("This is not a DCP file.");
        }

        var version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out var number) ? number : 0;
        if (version > CurrentVersion)
        {
            throw new DcpFileException(
                $"The file was saved by a newer version of DCP Tool (file format {version}). Update DCP Tool to open it.");
        }

        if (version < 2 || !root.TryGetProperty("dcp", out var dcpElement) || dcpElement.ValueKind != JsonValueKind.Object)
        {
            throw new DcpFileException("The DCP file is damaged (it has no DCP in it).");
        }

        try
        {
            var dcp = dcpElement.Deserialize<Dcp>(JsonOptions) ?? throw new DcpFileException("The DCP file is damaged.");
            return new DcpFileContent(Normalize(dcp), version);
        }
        catch (JsonException ex)
        {
            throw new DcpFileException($"The DCP file is damaged: {ex.Message}", ex);
        }
    }

    /// <summary>Repairs what a hand-edited or damaged file can contain: nulls where the model expects values, duplicate line ids.</summary>
    internal static Dcp Normalize(Dcp dcp)
    {
        dcp.TitoloItaliano ??= "";
        dcp.TitoloOriginale ??= "";
        dcp.Sottotitolo ??= "";
        dcp.Sede ??= "";
        dcp.Rete ??= "";
        dcp.NumeroContratto ??= "";
        dcp.ResponsabileRai ??= "";
        dcp.Lines = (dcp.Lines ?? []).Where(l => l is not null).ToList();

        var ids = new HashSet<Guid>();
        foreach (var line in dcp.Lines)
        {
            line.Titolo ??= "";
            line.Esecutori ??= "";
            line.GenereSiae ??= "";
            line.Ruolo ??= "";
            line.TipoGenerazione ??= "";
            line.Marca ??= "";
            line.Edizione ??= "";
            line.SiglaNum ??= "";
            line.Autori = (line.Autori ?? [])
                .Where(a => a is not null)
                .Select(a => new Autore(a.Nome ?? "", a.Cognome ?? ""))
                .Where(a => !a.IsEmpty)
                .ToList();

            if (line.Id == Guid.Empty || !ids.Add(line.Id))
            {
                line.Id = Guid.NewGuid();
                ids.Add(line.Id);
            }
        }

        return dcp;
    }

    private sealed class Envelope
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; }
        public string? SavedWith { get; set; }
        public Dcp? Dcp { get; set; }
    }
}
