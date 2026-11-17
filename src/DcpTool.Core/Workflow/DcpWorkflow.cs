using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DcpTool.Core.Model;
using DcpTool.Core.Validation;

namespace DcpTool.Core.Workflow;

/// <summary>The five steps every DCP goes through (doc/roadmap.md, "Workflow the app is built around").</summary>
public enum WorkflowStep
{
    /// <summary>The programme header is not filled in yet.</summary>
    Start,

    /// <summary>The header is there, the music is not.</summary>
    AddMusic,

    /// <summary>There is music, and something still blocks the upload.</summary>
    Review,

    /// <summary>Ready to upload, or an upload stopped part-way.</summary>
    Publish,

    /// <summary>Uploaded completely.</summary>
    Verify,
}

/// <summary>The one action that moves the DCP forward from its current step.</summary>
public enum PrimaryAction
{
    CompleteHeader,
    AddMusic,
    FixProblems,
    Upload,
    ContinueUpload,
    OpenAtRai,
}

/// <param name="Errors">Problems that block the upload.</param>
/// <param name="Warnings">Problems worth a look that don't block it.</param>
/// <param name="MusicTotal">Sum of the line durations.</param>
/// <param name="ProgrammeDuration">The programme's Durata; zero when not entered.</param>
/// <param name="ChangedSinceUpload">
/// The DCP was uploaded completely and an uploaded value has changed since. RAI still has the old values:
/// they have to be corrected there, because a second upload would create a second DCP.
/// </param>
public sealed record WorkflowState(
    WorkflowStep Step,
    PrimaryAction Action,
    int Errors,
    int Warnings,
    TimeSpan MusicTotal,
    TimeSpan ProgrammeDuration,
    bool ChangedSinceUpload)
{
    /// <summary>Music time as a share of the programme, 0..1 and more. Null without a programme duration.</summary>
    public double? MusicShare => ProgrammeDuration > TimeSpan.Zero ? MusicTotal / ProgrammeDuration : null;
}

/// <summary>
/// Where a DCP stands in the workflow. Pure logic, so any layout can show it the same way:
/// a step indicator, the primary button, the totals.
/// </summary>
public static class DcpWorkflow
{
    /// <param name="uploadIssues">The result of <see cref="DcpValidator.Validate"/> for <see cref="ValidationPurpose.Upload"/>.</param>
    public static WorkflowState Evaluate(Dcp dcp, IReadOnlyList<ValidationIssue> uploadIssues)
    {
        var errors = uploadIssues.Count(i => i.IsError);
        var warnings = uploadIssues.Count(i => !i.IsError);
        var changed = DcpFingerprint.ChangedSinceUpload(dcp);

        // Header problems that stop a DCP before any music matters
        var headerBlocked = uploadIssues.Any(i => i.IsError && i.Code is "titolo-missing" or "sede-missing");

        var (step, action) = dcp switch
        {
            { Lines.Count: 0 } when headerBlocked => (WorkflowStep.Start, PrimaryAction.CompleteHeader),
            { Lines.Count: 0 } => (WorkflowStep.AddMusic, PrimaryAction.AddMusic),
            { Upload: { Complete: true } } => (WorkflowStep.Verify, PrimaryAction.OpenAtRai),
            _ when errors > 0 => (WorkflowStep.Review, PrimaryAction.FixProblems),
            { Upload: { Complete: false, LinesSaved: > 0 } } => (WorkflowStep.Publish, PrimaryAction.ContinueUpload),
            _ => (WorkflowStep.Publish, PrimaryAction.Upload),
        };

        return new WorkflowState(step, action, errors, warnings, dcp.DurataMusica, dcp.Durata, changed);
    }
}

/// <summary>
/// A fingerprint of everything an upload sends to RAI, so the app can tell when a DCP was changed after it
/// was uploaded. Fields that are not uploaded (contract, Responsabile Rai, where a line came from) don't count.
/// </summary>
public static class DcpFingerprint
{
    public static string Compute(Dcp dcp)
    {
        var text = new StringBuilder();
        void Add(object? value) => text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim()).Append('\u001f');

        Add(dcp.TitoloItaliano);
        Add(dcp.TitoloOriginale);
        Add(dcp.Sottotitolo);
        Add(dcp.Puntata);
        Add(dcp.Serie);
        Add(dcp.Uorg);
        Add(dcp.Matricola);
        Add(dcp.DataTrasmissione.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(dcp.EffectiveDataRegistrazione.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(WholeSeconds(dcp.Durata));
        Add(WholeSeconds(dcp.OraInizio));
        Add(dcp.Sede);
        Add(dcp.Rete);
        foreach (var line in dcp.Lines)
        {
            text.Append('\u001e');
            Add(line.Titolo);
            Add(Autore.FormatList(line.Autori));
            Add(line.Esecutori);
            // The form only takes whole seconds
            Add(WholeSeconds(line.Durata));
            Add(line.GenereSiae);
            Add(line.Ruolo);
            Add(line.TipoGenerazione);
            Add(line.Marca.ToUpperInvariant());
            Add(line.Edizione.ToUpperInvariant());
            Add(line.SiglaNum);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>True when a complete upload is recorded with a fingerprint that no longer matches the DCP.</summary>
    public static bool ChangedSinceUpload(Dcp dcp) =>
        dcp.Upload is { Complete: true, ContentHash: { Length: > 0 } hash } && hash != Compute(dcp);

    private static long WholeSeconds(TimeSpan value) => (long)Math.Floor(value.TotalSeconds);
}
