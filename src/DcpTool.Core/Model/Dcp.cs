namespace DcpTool.Core.Model;

/// <summary>
/// One DCP (Distinta Consuntiva Programmi): a programme episode and the music used in it.
/// Property names are the Italian names of the RAI form fields. Keep them, they map 1:1 to the form.
/// </summary>
public sealed class Dcp
{
    public string TitoloItaliano { get; set; } = "";
    public string TitoloOriginale { get; set; } = "";
    public string Sottotitolo { get; set; } = "";

    /// <summary>Episode number. Null = not set (sent to RAI as an empty field).</summary>
    public int? Puntata { get; set; }

    /// <summary>Series number. RAI gets it as three digits; null is sent as <c>000</c>, like v1 did.</summary>
    public int? Serie { get; set; }

    public int? Uorg { get; set; }
    public int? Matricola { get; set; }

    public DateOnly DataTrasmissione { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Null = not entered. See <see cref="EffectiveDataRegistrazione"/>.</summary>
    public DateOnly? DataRegistrazione { get; set; }

    /// <summary>Length of the programme.</summary>
    public TimeSpan Durata { get; set; }

    /// <summary>Broadcast start as time of day.</summary>
    public TimeSpan OraInizio { get; set; }

    /// <summary>Committente code of the RAI office, e.g. <c>BZ/16</c>. See <see cref="RaiCodeTables.Sedi"/>. Empty = not chosen yet.</summary>
    public string Sede { get; set; } = "";

    /// <summary>Struttura / channel code, e.g. <c>TV1</c>. See <see cref="RaiCodeTables.Reti"/>.</summary>
    public string Rete { get; set; } = "TV1";

    /// <summary>Only on the paper form.</summary>
    public string NumeroContratto { get; set; } = "";

    /// <summary>Only on the paper form.</summary>
    public DateOnly? DataContratto { get; set; }

    /// <summary>"Responsabile Rai del Programma" on the paper form.</summary>
    public string ResponsabileRai { get; set; } = "";

    public List<DcpLine> Lines { get; set; } = [];

    /// <summary>What is known about the last upload of this DCP to RAI. Null = never uploaded from this file.</summary>
    public UploadRecord? Upload { get; set; }

    /// <summary>The recording date RAI gets: the entered one, or five days before the broadcast (the v1 default).</summary>
    public DateOnly EffectiveDataRegistrazione => DataRegistrazione ?? DataTrasmissione.AddDays(-5);

    /// <summary>Broadcast end as time of day. Wraps at midnight.</summary>
    public TimeSpan OraFine
    {
        get
        {
            var end = OraInizio + Durata;
            return new TimeSpan(end.Hours, end.Minutes, end.Seconds);
        }
    }

    /// <summary>Sum of the line durations.</summary>
    public TimeSpan DurataMusica => Lines.Aggregate(TimeSpan.Zero, (sum, l) => sum + l.Durata);

    /// <summary>
    /// A new DCP for the next episode of the same programme: same header, episode + 1, no lines, never uploaded.
    /// </summary>
    public Dcp CreateNextEpisode() => new()
    {
        TitoloItaliano = TitoloItaliano,
        TitoloOriginale = TitoloOriginale,
        Puntata = Puntata + 1,
        Serie = Serie,
        Uorg = Uorg,
        Matricola = Matricola,
        Durata = Durata,
        OraInizio = OraInizio,
        Sede = Sede,
        Rete = Rete,
        NumeroContratto = NumeroContratto,
        DataContratto = DataContratto,
        ResponsabileRai = ResponsabileRai,
    };
}

/// <summary>Result of an upload, stored in the .dcp file so a second upload of the same DCP can be warned about.</summary>
public sealed class UploadRecord
{
    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>Address of the saved DCP inside the RAI portal.</summary>
    public string Url { get; set; } = "";

    /// <summary>How many lines RAI had saved when the upload ended. Less than the DCP's line count after a failed upload.</summary>
    public int LinesSaved { get; set; }

    public bool Complete { get; set; }

    /// <summary>RAI's code of the DCP as its page shows it, e.g. <c>1788111-00</c>. Null when the page didn't show one.</summary>
    public string? Codice { get; set; }

    /// <summary>RAI's state of the DCP at upload time, e.g. <c>Bozza</c>.</summary>
    public string? Stato { get; set; }

    /// <summary>
    /// <c>DcpFingerprint</c> of the DCP as it was uploaded, to notice later changes. Null for records written
    /// before it existed, which then never count as changed.
    /// </summary>
    public string? ContentHash { get; set; }
}
