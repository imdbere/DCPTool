using DcpTool.Core.Import;
using DcpTool.Core.Rai;
using DcpTool.Core.Validation;

namespace DcpTool.App.Localisation;

/// <summary>
/// Words the messages of the lower layers (validation, import, RAI client) in the UI language, by their stable
/// code and arguments. A code without a text here falls back to the layer's English message.
/// </summary>
public static class MessageText
{
    /// <param name="lineTitle">Titolo of the line with that id, for the "Line 3 (TITLE):" prefix.</param>
    public static string Issue(ValidationIssue issue, Func<Guid, string?> lineTitle)
    {
        var a = issue.Args;
        var key = issue.Code switch
        {
            "titolo-missing" => "Issue_TitoloMissing",
            "sede-missing" => "Issue_SedeMissing",
            "sede-unknown" when a.Count == 1 => "Issue_SedeUnknown",
            "rete-missing" => "Issue_ReteMissing",
            "rete-unknown" when a.Count == 1 => "Issue_ReteUnknown",
            "no-lines" => "Issue_NoLines",
            "uorg-missing" => "Issue_UorgMissing",
            "matricola-missing" => "Issue_MatricolaMissing",
            "already-uploaded" when a.Count == 5 && a[1] is true => "Issue_AlreadyUploaded",
            "already-uploaded" when a.Count == 5 => "Issue_AlreadyUploadedPartly",
            "changed-since-upload" => "Issue_ChangedSinceUpload",
            "durata-missing" => "Issue_DurataMissing",
            "durata-too-long" => "Issue_DurataTooLong",
            "registrazione-after-trasmissione" => "Issue_RegistrazioneAfterTrasmissione",
            "line-titolo-missing" => "Issue_LineTitoloMissing",
            "line-autori-missing" when a.Count == 1 && a[0] is true => "Issue_LineAutoriMissingCueSheet",
            "line-autori-missing" => "Issue_LineAutoriMissing",
            "line-autore-cognome-missing" => "Issue_LineAutoreCognomeMissing",
            "line-durata-missing" => "Issue_LineDurataMissing",
            "line-durata-too-long" => "Issue_LineDurataTooLong",
            "line-durata-exceeds-programme" => "Issue_LineDurataExceedsProgramme",
            "line-genere-missing" => "Issue_LineGenereMissing",
            "line-genere-unknown" when a.Count == 1 => "Issue_LineGenereUnknown",
            "line-ruolo-missing" => "Issue_LineRuoloMissing",
            "line-ruolo-unknown" when a.Count == 1 => "Issue_LineRuoloUnknown",
            "line-tipo-generazione-missing" => "Issue_LineTipoGenerazioneMissing",
            "line-tipo-generazione-unknown" when a.Count == 1 => "Issue_LineTipoGenerazioneUnknown",
            "line-marca-missing" => "Issue_LineMarcaMissing",
            "line-marca-unknown" when a.Count == 1 => "Issue_LineMarcaUnknown",
            "line-edizione-set" => "Issue_LineEdizioneSet",
            _ => null,
        };
        if (key is null)
        {
            return issue.Message; // already has the line prefix
        }

        // The DCP code of an incomplete upload is shown in brackets after "DCP", when there is one
        object[] args = key == "Issue_AlreadyUploadedPartly"
            ? [a[0], a[2], a[3], a[4] is string { Length: > 0 } codice ? $" ({codice})" : ""]
            : [.. a];
        var text = Loc.Format(key, args);
        if (issue.LineId is not { } id || issue.LineNumber is not { } number)
        {
            return text;
        }

        var title = lineTitle(id)?.Trim();
        var prefix = string.IsNullOrEmpty(title) ? Loc.Format("Issue_Line", number) : Loc.Format("Issue_LineWithTitle", number, title);
        return $"{prefix}: {text}";
    }

    public static string Import(ImportMessage message)
    {
        var a = message.Args;
        var key = message.Code switch
        {
            "file-not-found" => "Import_FileNotFound",
            "unsupported-file-type" => "Import_UnsupportedFileType",
            "no-source" => "Import_NoSource",
            "artlist-cue-sheet-missing" => "Import_ArtlistCueSheetMissing",
            "not-a-cue-sheet" => "Import_NotACueSheet",
            "audio-unreadable" => "Import_AudioUnreadable",
            "read-failed" when a.Count == 1 => "Import_ReadFailed",
            "line-completed" when a.Count == 1 => "Import_LineCompleted",
            "sidecar-unmatched" => "Import_SidecarUnmatched",
            _ => null,
        };
        var text = key is null ? message.Text : Loc.Format(key, [.. a]);
        return message.File is null ? text : $"{message.File}: {text}";
    }

    /// <summary>What went wrong at RAI, without RAI's own messages (those are shown as RAI wrote them).</summary>
    public static string Rai(RaiException ex) => ex.Code switch
    {
        RaiException.TooManySessions => Loc.Get("Rai_TooManySessions"),
        RaiException.DraftNotFound => Loc.Format("Rai_DraftNotFound", Loc.MenuName("Menu_ForgetUpload")),
        _ => ex.Kind switch
        {
            RaiErrorKind.InvalidCredentials => Loc.Get("Rai_InvalidCredentials"),
            RaiErrorKind.InteractiveLoginRequired => Loc.Get("Rai_InteractiveLoginRequired"),
            RaiErrorKind.SessionExpired => Loc.Get("Rai_SessionExpired"),
            RaiErrorKind.UnexpectedPage => Loc.Format("Rai_UnexpectedPage", Loc.MenuName("Menu_SaveDiagnostics")),
            RaiErrorKind.FormRejected => Loc.Get("Rai_FormRejected"),
            RaiErrorKind.Network => Loc.Get("Rai_Network"),
            _ => ex.Message,
        },
    };
}
