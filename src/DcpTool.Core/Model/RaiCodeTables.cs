namespace DcpTool.Core.Model;

/// <summary>A code of one of RAI's lookup tables.</summary>
/// <param name="Code">The value the RAI web form expects.</param>
/// <param name="Description">What it means, shown in the UI.</param>
/// <param name="PaperCode">The code printed on the paper form (Allegato B) when it differs from <paramref name="Code"/>.</param>
public sealed record CodeEntry(string Code, string Description, string? PaperCode = null)
{
    public override string ToString() => $"{Code} – {Description}";
}

/// <summary>A RAI office (sede) that commissions programmes.</summary>
/// <param name="Code">Committente code, e.g. <c>BZ/16</c>. This is what a <see cref="Dcp"/> stores.</param>
/// <param name="PoolValue">Value of the form's <c>DropDownList_SediPool</c>.</param>
/// <param name="Name">Name shown in the UI.</param>
/// <param name="PaperText">"Direzione Rai Competente" on the paper form.</param>
public sealed record Sede(string Code, string PoolValue, string Name, string PaperText)
{
    public override string ToString() => $"{Code} – {Name}";
}

/// <summary>
/// The values of RAI's lookup tables that are known to work. Model properties hold the plain code as a
/// string, so a code that is missing here can still be typed in and uploaded (validation only warns).
/// Sources: the v1 tool (values proven in production) and the legend on the paper form "Allegato B, MOD. 2017".
/// </summary>
public static class RaiCodeTables
{
    /// <summary>TabGenereSIAE. Only <c>ML</c> has ever been uploaded by v1; the others come from the paper form's legend.</summary>
    public static IReadOnlyList<CodeEntry> GeneriSiae { get; } =
    [
        new("ML", "Musica leggera"),
        new("MC", "Musica classica"),
        new("DO", "DOR (opere teatrali ecc.)"),
        new("LI", "Lirica"),
        new("OL", "OLAF (opere letterarie e arti figurative)"),
        new("CI", "Cinema"),
    ];

    /// <summary>TabRuolo: how the music is used.</summary>
    public static IReadOnlyList<CodeEntry> Ruoli { get; } =
    [
        new("SF", "Sottofondo"),
        new("PP", "Primo piano"),
        new("SI", "Sigla"),
    ];

    /// <summary>TabTipoGenerazione. The paper form has a support code instead (c = compact disc).</summary>
    public static IReadOnlyList<CodeEntry> TipiGenerazione { get; } =
    [
        new("9", "Opera su disco", PaperCode: "C"),
        new("1", "Dal vivo", PaperCode: ""),
    ];

    /// <summary>TabStrutture: the channels v1 offered.</summary>
    public static IReadOnlyList<CodeEntry> Reti { get; } =
    [
        new("TV1", "TV1"),
        new("TV2", "TV2"),
        new("TV3", "TV3"),
        new("TV4", "TV4"),
    ];

    /// <summary>The offices v1 supported: the three programme departments of RAI Bolzano.</summary>
    public static IReadOnlyList<Sede> Sedi { get; } =
    [
        new("BZ/16", "16", "Bolzano – programmi in lingua tedesca", "PROG. L. TED.   BZ/16"),
        new("BZ/14", "14", "Bolzano – programmi in lingua italiana", "PROG. L. IT.   BZ/14"),
        new("BZ/15", "15", "Bolzano – programmi in lingua ladina", "PROG. L. LAD.   BZ/15"),
    ];

    public static Sede? FindSede(string? code) =>
        Sedi.FirstOrDefault(s => string.Equals(s.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsKnown(IReadOnlyList<CodeEntry> table, string? code) =>
        table.Any(e => string.Equals(e.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static CodeEntry? Find(IReadOnlyList<CodeEntry> table, string? code) =>
        table.FirstOrDefault(e => string.Equals(e.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
}
