using System.Text.Json.Serialization;

namespace DcpTool.Core.Model;

/// <summary>One piece of music (opera) in a DCP.</summary>
public sealed class DcpLine
{
    /// <summary>Stable identity, so validation messages and the UI can point at a line.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Titolo { get; set; } = "";

    /// <summary>Composers and lyricists. Never the performers: RAI passes these names to SIAE.</summary>
    public List<Autore> Autori { get; set; } = [];

    /// <summary>Performers, free text.</summary>
    public string Esecutori { get; set; } = "";

    /// <summary>Duration of the music as used in the programme.</summary>
    public TimeSpan Durata { get; set; }

    /// <summary>See <see cref="RaiCodeTables.GeneriSiae"/>.</summary>
    public string GenereSiae { get; set; } = LineDefaults.Standard.GenereSiae;

    /// <summary>See <see cref="RaiCodeTables.Ruoli"/>.</summary>
    public string Ruolo { get; set; } = LineDefaults.Standard.Ruolo;

    /// <summary>See <see cref="RaiCodeTables.TipiGenerazione"/>.</summary>
    public string TipoGenerazione { get; set; } = LineDefaults.Standard.TipoGenerazione;

    /// <summary>Label / library. Sent to RAI in upper case.</summary>
    public string Marca { get; set; } = "";

    /// <summary>Music publisher. Sent to RAI in upper case.</summary>
    public string Edizione { get; set; } = "";

    /// <summary>Catalogue code of the recording ("Sigla / numero").</summary>
    public string SiglaNum { get; set; } = "";

    /// <summary>Where the line came from. Null for lines typed in by hand.</summary>
    public LineOrigin? Origin { get; set; }

    public bool HasAutori => Autori.Any(a => !a.IsEmpty);

    /// <summary>The authors as one editable text, e.g. <c>Jeff Montgomery; Matthew Noble</c>.</summary>
    [JsonIgnore]
    public string AutoriText
    {
        get => Autore.FormatList(Autori);
        set => Autori = Autore.ParseList(value);
    }

    public DcpLine Clone() => new()
    {
        Id = Guid.NewGuid(),
        Titolo = Titolo,
        Autori = [.. Autori],
        Esecutori = Esecutori,
        Durata = Durata,
        GenereSiae = GenereSiae,
        Ruolo = Ruolo,
        TipoGenerazione = TipoGenerazione,
        Marca = Marca,
        Edizione = Edizione,
        SiglaNum = SiglaNum,
        Origin = Origin is null ? null : Origin with { },
    };
}

/// <summary>Provenance of an imported line. Informational, never sent to RAI.</summary>
public sealed record LineOrigin
{
    /// <summary><c>IMusicSource.Id</c> of the source that created the line, e.g. <c>musicmedia</c> or <c>artlist</c>.</summary>
    public string SourceId { get; init; } = "";

    /// <summary>Full path of the audio file at import time. May no longer exist.</summary>
    public string? FilePath { get; init; }

    /// <summary>Full length of the audio file. <see cref="DcpLine.Durata"/> starts as this value and may be shortened to the length used.</summary>
    public TimeSpan? FileDuration { get; init; }

    /// <summary>File name of the cue sheet or other sidecar file the data came from.</summary>
    public string? SidecarFile { get; init; }

    public string? Isrc { get; init; }
    public string? Iswc { get; init; }

    /// <summary>True while the source could not provide everything it normally does, e.g. an Artlist song without its cue sheet.</summary>
    public bool Incomplete { get; init; }

    public string? FileName => FilePath is null ? null : Path.GetFileName(FilePath);
}

/// <summary>The codes a new line starts with.</summary>
public sealed record LineDefaults
{
    public string GenereSiae { get; init; } = "ML";
    public string Ruolo { get; init; } = "SF";
    public string TipoGenerazione { get; init; } = "9";

    /// <summary>What v1 used for every line: musica leggera, sottofondo, opera su disco.</summary>
    public static LineDefaults Standard { get; } = new();

    public void ApplyTo(DcpLine line)
    {
        line.GenereSiae = GenereSiae;
        line.Ruolo = Ruolo;
        line.TipoGenerazione = TipoGenerazione;
    }
}
