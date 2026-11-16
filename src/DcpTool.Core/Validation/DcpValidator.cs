using DcpTool.Core.Model;

namespace DcpTool.Core.Validation;

public enum IssueSeverity
{
    /// <summary>Worth a look, the action still runs.</summary>
    Warning,

    /// <summary>The action is refused until this is fixed.</summary>
    Error,
}

/// <summary>What the DCP is about to be used for. Some rules only matter for one of them.</summary>
public enum ValidationPurpose
{
    /// <summary>Upload to the RAI web form.</summary>
    Upload,

    /// <summary>Export of the paper form as a document.</summary>
    Document,
}

/// <param name="Code">Stable identifier of the rule, for tests and for filtering.</param>
/// <param name="Field">Name of the <see cref="Dcp"/> or <see cref="DcpLine"/> property the issue is about, if any.</param>
/// <param name="LineId"><see cref="DcpLine.Id"/> when the issue belongs to a line.</param>
/// <param name="LineNumber">1-based position of that line.</param>
public sealed record ValidationIssue(
    IssueSeverity Severity,
    string Code,
    string Message,
    string? Field = null,
    Guid? LineId = null,
    int? LineNumber = null)
{
    public bool IsError => Severity == IssueSeverity.Error;
}

/// <summary>
/// The one place that decides whether a DCP can be uploaded or exported. Both actions call it first,
/// so problems show up as a list next to the grid instead of as an exception half-way through an upload.
/// </summary>
public static class DcpValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(Dcp dcp, ValidationPurpose purpose)
    {
        var issues = new List<ValidationIssue>();
        var upload = purpose == ValidationPurpose.Upload;

        void Error(string code, string message, string? field = null) =>
            issues.Add(new ValidationIssue(IssueSeverity.Error, code, message, field));
        void Warning(string code, string message, string? field = null) =>
            issues.Add(new ValidationIssue(IssueSeverity.Warning, code, message, field));

        if (string.IsNullOrWhiteSpace(dcp.TitoloItaliano))
        {
            Error("titolo-missing", "Titolo italiano is empty.", nameof(Dcp.TitoloItaliano));
        }

        if (string.IsNullOrWhiteSpace(dcp.Sede))
        {
            Error("sede-missing", "No Sede selected.", nameof(Dcp.Sede));
        }
        else if (RaiCodeTables.FindSede(dcp.Sede) is null)
        {
            // The upload needs the office's number in RAI's list, which is only known for the built-in offices
            issues.Add(new ValidationIssue(
                upload ? IssueSeverity.Error : IssueSeverity.Warning,
                "sede-unknown",
                $"Sede \"{dcp.Sede}\" is not one of the known RAI offices.",
                nameof(Dcp.Sede)));
        }

        if (upload)
        {
            if (string.IsNullOrWhiteSpace(dcp.Rete))
            {
                Error("rete-missing", "No Rete (Struttura) selected.", nameof(Dcp.Rete));
            }
            else if (!RaiCodeTables.IsKnown(RaiCodeTables.Reti, dcp.Rete))
            {
                Warning("rete-unknown", $"Rete \"{dcp.Rete}\" is not one of the known codes. RAI may reject it.", nameof(Dcp.Rete));
            }

            if (dcp.Lines.Count == 0)
            {
                Error("no-lines", "The DCP has no music lines, there is nothing to upload.", nameof(Dcp.Lines));
            }

            if (dcp.Uorg is null or 0)
            {
                Warning("uorg-missing", "Uorg is empty.", nameof(Dcp.Uorg));
            }

            if (dcp.Matricola is null or 0)
            {
                Warning("matricola-missing", "Matricola is empty.", nameof(Dcp.Matricola));
            }

            if (dcp.Upload is { } record)
            {
                Warning(
                    "already-uploaded",
                    record.Complete
                        ? $"This DCP was already uploaded on {record.UploadedAt.LocalDateTime:g}. Uploading again creates a second DCP at RAI."
                        : $"An upload on {record.UploadedAt.LocalDateTime:g} stopped after {record.LinesSaved} of {dcp.Lines.Count} lines. " +
                          "That partial DCP still exists at RAI. Uploading again creates a second one.",
                    nameof(Dcp.Upload));
            }
        }

        if (dcp.Durata <= TimeSpan.Zero)
        {
            Warning("durata-missing", "The programme's Durata is empty.", nameof(Dcp.Durata));
        }

        if (dcp.Durata >= TimeSpan.FromHours(24))
        {
            Error("durata-too-long", "The programme's Durata must be shorter than 24 hours.", nameof(Dcp.Durata));
        }

        if (dcp.DataRegistrazione is { } registrazione && registrazione > dcp.DataTrasmissione)
        {
            Warning("registrazione-after-trasmissione", "Data registrazione is after Data trasmissione.", nameof(Dcp.DataRegistrazione));
        }

        for (var i = 0; i < dcp.Lines.Count; i++)
        {
            ValidateLine(dcp, dcp.Lines[i], i + 1, upload, issues);
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<ValidationIssue> issues) => issues.Any(i => i.IsError);

    private static void ValidateLine(Dcp dcp, DcpLine line, int number, bool upload, List<ValidationIssue> issues)
    {
        var name = string.IsNullOrWhiteSpace(line.Titolo) ? $"Line {number}" : $"Line {number} ({line.Titolo.Trim()})";

        void Add(IssueSeverity severity, string code, string message, string field) =>
            issues.Add(new ValidationIssue(severity, code, $"{name}: {message}", field, line.Id, number));

        // What the upload refuses is only a warning on the document, which can be completed by hand
        var blocking = upload ? IssueSeverity.Error : IssueSeverity.Warning;

        if (string.IsNullOrWhiteSpace(line.Titolo))
        {
            Add(blocking, "line-titolo-missing", "Titolo is empty.", nameof(DcpLine.Titolo));
        }

        if (!line.HasAutori)
        {
            Add(
                blocking,
                "line-autori-missing",
                line.Origin is { Incomplete: true }
                    ? "Autori are missing because the source's cue sheet was not found. Add the cue sheet or enter the authors."
                    : "Autori are missing.",
                nameof(DcpLine.Autori));
        }
        else if (line.Autori.Any(a => !a.IsEmpty && string.IsNullOrWhiteSpace(a.Cognome)))
        {
            Add(IssueSeverity.Warning, "line-autore-cognome-missing", "an author has no Cognome (surname).", nameof(DcpLine.Autori));
        }

        if (line.Durata <= TimeSpan.Zero)
        {
            Add(blocking, "line-durata-missing", "Durata is empty.", nameof(DcpLine.Durata));
        }
        else if (line.Durata >= TimeSpan.FromHours(24))
        {
            Add(IssueSeverity.Error, "line-durata-too-long", "Durata must be shorter than 24 hours.", nameof(DcpLine.Durata));
        }
        else if (dcp.Durata > TimeSpan.Zero && line.Durata > dcp.Durata)
        {
            Add(IssueSeverity.Warning, "line-durata-exceeds-programme", "Durata is longer than the programme. Enter the length that was actually used.", nameof(DcpLine.Durata));
        }

        CheckCode(line.GenereSiae, RaiCodeTables.GeneriSiae, "Genere SIAE", "line-genere", nameof(DcpLine.GenereSiae));
        CheckCode(line.Ruolo, RaiCodeTables.Ruoli, "Ruolo", "line-ruolo", nameof(DcpLine.Ruolo));
        CheckCode(line.TipoGenerazione, RaiCodeTables.TipiGenerazione, "Tipo generazione", "line-tipo-generazione", nameof(DcpLine.TipoGenerazione));

        if (string.IsNullOrWhiteSpace(line.Marca))
        {
            Add(IssueSeverity.Warning, "line-marca-missing", "Marca is empty.", nameof(DcpLine.Marca));
        }
        else if (upload && !RaiMarche.Contains(line.Marca))
        {
            Add(IssueSeverity.Warning, "line-marca-unknown", $"Marca \"{line.Marca.Trim()}\" is not in RAI's list of labels. RAI may reject it.", nameof(DcpLine.Marca));
        }

        if (upload && !string.IsNullOrWhiteSpace(line.Edizione))
        {
            // RAI checks Edizione against a publisher list we don't have. It rejected Artlist's publisher on 2026-11-16.
            Add(IssueSeverity.Warning, "line-edizione-set", "Edizione is filled in. RAI only accepts publishers it knows, leave it empty if in doubt.", nameof(DcpLine.Edizione));
        }

        void CheckCode(string value, IReadOnlyList<CodeEntry> table, string label, string code, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Add(blocking, code + "-missing", $"{label} is empty.", field);
            }
            else if (!RaiCodeTables.IsKnown(table, value))
            {
                Add(IssueSeverity.Warning, code + "-unknown", $"{label} \"{value}\" is not one of the known codes. RAI may reject it.", field);
            }
        }
    }
}
