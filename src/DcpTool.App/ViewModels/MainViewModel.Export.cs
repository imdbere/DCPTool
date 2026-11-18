using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DcpTool.App.Localisation;
using DcpTool.App.Services;
using DcpTool.Core.Export;
using DcpTool.Core.Model;
using DcpTool.Core.Persistence;
using DcpTool.Core.Rai;
using DcpTool.Core.Validation;
using DcpTool.Core.Workflow;

namespace DcpTool.App.ViewModels;

public enum ConsoleLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>A line of the console on the export page: infos, warnings and upload errors in one list.</summary>
/// <param name="IsCheck">A result of the check (Prüfen); the next check replaces it.</param>
public sealed record ConsoleLine(string Time, ConsoleLevel Level, string Message, bool IsCheck)
{
    public string LevelText => Level switch
    {
        ConsoleLevel.Error => Loc.Get("Console_LevelError"),
        ConsoleLevel.Warning => Loc.Get("Console_LevelWarning"),
        _ => Loc.Get("Console_LevelInfo"),
    };

    public bool IsError => Level == ConsoleLevel.Error;
    public bool IsWarning => Level == ConsoleLevel.Warning;

    public override string ToString() => $"{Time}  {LevelText,-8} {Message}";
}

/// <summary>Why the last upload failed, in plain words, for the export page.</summary>
public sealed record UploadFailure(string Text, string Hint, bool CanForget);

/// <summary>
/// The export page (step 4): RAI (Prüfen, Hochladen, the console) and the documents (Allegato B, its template).
/// Nothing here saves the DCP: after an upload, what RAI saved is in the DCP and goes into its file with Speichern.
/// </summary>
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _operation;
    private UploadFailure? _uploadFailure;

    // The result of the last check: errors and warnings, for the Export area's second line. Null after an edit.
    private (int Errors, int Warnings)? _lastCheck;

    // The document exported last, while this DCP is open
    private (string Path, DateTime At)? _lastExport;

    // Something was sent to the portal in this session, so there are diagnostics worth saving
    private bool _raiContacted;

    public ObservableCollection<ConsoleLine> ConsoleLines { get; } = [];

    /// <summary>The replacement of the built-in Word template, when the user chose one. Set once at start.</summary>
    public string? TemplateFile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpload))]
    private bool _isUploading;

    // ---------------------------------------------------------------- the Export area and the RAI section

    /// <summary>Second line of the Export area: where the DCP stands at RAI.</summary>
    public string ExportAreaSubtitle
    {
        get
        {
            if (_uploadFailure is not null)
            {
                return Loc.Get("Area_ExportFailed");
            }

            var state = DcpWorkflow.Evaluate(_dcp, DcpValidator.Validate(_dcp, ValidationPurpose.Upload));
            if (state.Step == WorkflowStep.Verify)
            {
                return state.ChangedSinceUpload ? Loc.Get("Area_ExportChanged")
                    : _dcp.Upload?.Codice is { } codice ? Loc.Format("Area_ExportUploadedAs", codice) : Loc.Get("Area_ExportUploaded");
            }

            if (state.Action == PrimaryAction.ContinueUpload)
            {
                return Loc.Get("Area_ExportPartly");
            }

            return _lastCheck switch
            {
                { Errors: > 0 } c => Loc.Format(c.Errors == 1 ? "Area_ExportCheckedError" : "Area_ExportCheckedErrors", c.Errors),
                { Warnings: > 0 } c => Loc.Format(c.Warnings == 1 ? "Area_ExportCheckedWarning" : "Area_ExportCheckedWarnings", c.Warnings),
                not null => Loc.Get("Area_ExportCheckedOk"),
                _ => Loc.Get("Area_ExportSub"),
            };
        }
    }

    public bool IsUploadFailed => _uploadFailure is not null;
    public bool IsUploaded => _uploadFailure is null && _dcp.Upload is { Complete: true };
    public bool IsUploadReady => !IsUploadFailed && !IsUploaded;
    public bool CanUpload => HasLines && !IsUploading;

    public string UploadFailureText => _uploadFailure?.Text ?? "";
    public string UploadFailureHint => _uploadFailure?.Hint ?? "";
    public bool CanForgetUpload => _uploadFailure?.CanForget == true;

    public string UploadedAtText => _dcp.Upload is { } record
        ? Loc.Format("Export_UploadedAt", record.UploadedAt.LocalDateTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            record.UploadedAt.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture))
        : "";

    public string RaiNumberLabel => _dcp.Upload?.Stato is { Length: > 0 } stato
        ? Loc.Get("Export_DcpNumber") + " · " + stato
        : Loc.Get("Export_DcpNumber");

    public string RaiNumber => _dcp.Upload?.Codice ?? "–";
    public bool HasRaiNumber => _dcp.Upload?.Codice is { Length: > 0 };

    /// <summary>An earlier upload stopped part-way: the next one continues that DCP at RAI.</summary>
    public string PartialUploadNote => _uploadFailure is null && _dcp.Upload is { Complete: false } record
        ? Loc.Format("Export_PartialNote", record.LinesSaved, _dcp.Lines.Count, record.Codice is { } c ? $" {c}" : "")
        : "";

    public bool HasPartialUploadNote => PartialUploadNote.Length > 0;

    private void OnUploadStateChanged()
    {
        OnPropertyChanged(nameof(ExportAreaSubtitle));
        OnPropertyChanged(nameof(IsUploadFailed));
        OnPropertyChanged(nameof(IsUploaded));
        OnPropertyChanged(nameof(IsUploadReady));
        OnPropertyChanged(nameof(CanUpload));
        OnPropertyChanged(nameof(UploadFailureText));
        OnPropertyChanged(nameof(UploadFailureHint));
        OnPropertyChanged(nameof(CanForgetUpload));
        OnPropertyChanged(nameof(UploadedAtText));
        OnPropertyChanged(nameof(RaiNumberLabel));
        OnPropertyChanged(nameof(RaiNumber));
        OnPropertyChanged(nameof(HasRaiNumber));
        OnPropertyChanged(nameof(PartialUploadNote));
        OnPropertyChanged(nameof(HasPartialUploadNote));
    }

    // ---------------------------------------------------------------- the console

    /// <summary>"Protokoll · 1 Fehler · 2 Warnungen".</summary>
    public string ConsoleHeader
    {
        get
        {
            var errors = ConsoleLines.Count(l => l.IsError);
            var warnings = ConsoleLines.Count(l => l.IsWarning);
            var text = Loc.Get("Console_Title");
            if (errors > 0)
            {
                text += " · " + Loc.Format(errors == 1 ? "Console_OneError" : "Console_Errors", errors);
            }

            if (warnings > 0)
            {
                text += " · " + Loc.Format(warnings == 1 ? "Console_OneWarning" : "Console_Warnings", warnings);
            }

            return text;
        }
    }

    public bool IsConsoleEmpty => ConsoleLines.Count == 0;

    private void OnConsoleChanged()
    {
        OnPropertyChanged(nameof(ConsoleHeader));
        OnPropertyChanged(nameof(IsConsoleEmpty));
    }

    [RelayCommand]
    private async Task CopyConsole()
    {
        await _dialogs.CopyText(string.Join(Environment.NewLine, ConsoleLines));
        SetStatus("Status_ConsoleCopied");
    }

    /// <summary>
    /// "Speichern…": the console as a text file, and next to it what was exchanged with the portal (passwords, cookies
    /// and names removed), which is what the developers need when an upload breaks.
    /// </summary>
    [RelayCommand]
    private async Task SaveConsole()
    {
        var name = $"DCP Tool {DateTime.Now:yyyy-MM-dd HH-mm}";
        var path = await _dialogs.PickSave(Loc.Get("Picker_SaveConsole"), name, ".txt", Loc.Get("Picker_TextFile"), _settings.LastDocumentFolder);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, ConsoleLines) + Environment.NewLine, Encoding.UTF8);
            if (_raiContacted)
            {
                var zip = await _rai.SaveDiagnosticsAsync(Path.GetDirectoryName(path)!);
                SetStatus("Status_ConsoleSavedWithDiagnostics", Path.GetFileName(path), Path.GetFileName(zip));
            }
            else
            {
                SetStatus("Status_ConsoleSaved", Path.GetFileName(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RaiException)
        {
            Report(ConsoleLevel.Error, "Status_FileProblem", ex.Message);
        }
    }

    // ---------------------------------------------------------------- Prüfen

    /// <summary>"Prüfen": checks the DCP for the upload and for the document, without contacting RAI.</summary>
    [RelayCommand]
    private void Check()
    {
        CommitPendingEdits?.Invoke();
        RunCheck(manual: true);
    }

    /// <summary>
    /// The check's lines replace those of the last check. Runs when the export page opens and on "Prüfen". It checks
    /// with the rules that guard the upload and the document (<see cref="DcpValidator"/>) and never contacts RAI.
    /// </summary>
    private void RunCheck(bool manual)
    {
        SyncLines();
        for (var i = ConsoleLines.Count - 1; i >= 0; i--)
        {
            if (ConsoleLines[i].IsCheck)
            {
                ConsoleLines.RemoveAt(i);
            }
        }

        var issues = DcpValidator.Validate(_dcp, ValidationPurpose.Upload)
            .Concat(DcpValidator.Validate(_dcp, ValidationPurpose.Document))
            .GroupBy(i => (i.Code, i.LineId, i.Field))
            .Select(g => g.OrderByDescending(i => i.IsError).First())
            .OrderBy(i => i.LineNumber ?? 0)
            .ToList();

        var total = TimeText.FormatDuration(_dcp.DurataMusica);
        ReportText(ConsoleLevel.Info, Loc.Format(Lines.Count == 1 ? "Check_SongsOne" : "Check_Songs", Lines.Count, total.Length > 0 ? total : "00:00"),
            Loc.English("Check_Songs", Lines.Count, total), isCheck: true);
        foreach (var issue in issues)
        {
            ReportText(issue.IsError ? ConsoleLevel.Error : ConsoleLevel.Warning, IssueText(issue), issue.Message, isCheck: true);
        }

        var errors = issues.Count(i => i.IsError);
        var warnings = issues.Count(i => !i.IsError);
        if (manual)
        {
            _lastCheck = (errors, warnings);
            var (key, args) = (errors, warnings) switch
            {
                (0, 0) => ("Check_DoneOk", Array.Empty<object>()),
                (0, _) => ("Check_DoneWarnings", new object[] { warnings }),
                _ => ("Check_DoneErrors", new object[] { errors, warnings }),
            };
            ReportText(errors > 0 ? ConsoleLevel.Error : ConsoleLevel.Info, Loc.Format(key, args), Loc.English(key, args), isCheck: true);
        }

        OnUploadStateChanged();
    }

    /// <summary>A validation issue in the UI language.</summary>
    private string IssueText(ValidationIssue issue) =>
        MessageText.Issue(issue, id => _dcp.Lines.FirstOrDefault(l => l.Id == id)?.Titolo);

    // ---------------------------------------------------------------- Hochladen

    [RelayCommand]
    private async Task Upload()
    {
        if (IsBusy)
        {
            return;
        }

        CommitPendingEdits?.Invoke();
        SyncLines();
        _uploadFailure = null;
        var issues = DcpValidator.Validate(_dcp, ValidationPurpose.Upload);
        var errors = issues.Count(i => i.IsError);
        if (errors > 0)
        {
            // No dialog: the problems are in the console, the check lists them
            RunCheck(manual: false);
            Report(ConsoleLevel.Error, "Status_NotUploadedProblems", errors);
            OnUploadStateChanged();
            return;
        }

        // A complete upload would be duplicated, so ask. An incomplete one is continued on its draft at RAI.
        if (_dcp.Upload is { Complete: true } &&
            issues.FirstOrDefault(i => i.Code == "already-uploaded") is { } again &&
            !await _dialogs.Confirm(Loc.Get("Dlg_AlreadyUploaded"), IssueText(again) + "\n\n" + Loc.Get("Msg_UploadAnyway"),
                Loc.Get("Dlg_UploadAgain"), Loc.Get("Common_Cancel")))
        {
            return;
        }

        IsBusy = true;
        IsUploading = true;
        var dcp = _dcp;
        var uploaded = false;
        _operation = new CancellationTokenSource();
        try
        {
            if (!await EnsureLoginAsync(_operation.Token))
            {
                return;
            }

            // The client's step text (English) goes only into the log file
            var progress = new Progress<UploadProgress>(p => ReportText(ConsoleLevel.Info,
                Loc.Format("Status_UploadProgress", p.LinesSaved, p.LinesTotal),
                Loc.English("Status_UploadProgress", p.LinesSaved, p.LinesTotal) + " " + p.Message));
            SetStatus("Status_Uploading");
            _raiContacted = true;
            var result = await _rai.UploadAsync(dcp, progress, _operation.Token);
            dcp.Upload = result.Record;
            OnChanged();
            RecordUpload(dcp);
            uploaded = true;
            var record = result.Record;
            var (key, args) = record.Codice is { } c
                ? ("Status_UploadedAs", new object?[] { record.LinesSaved, record.Stato is { } st ? $"{c} ({st})" : c })
                : ("Status_Uploaded", new object?[] { record.LinesSaved });
            ReportText(ConsoleLevel.Info,
                Loc.Format(key, args) + (result.Verified ? " " + Loc.Get("Status_UploadVerified") : ""),
                Loc.English(key, args) + (result.Verified ? " " + Loc.English("Status_UploadVerified") : ""));
            foreach (var w in result.Warnings)
            {
                // The client's checks of RAI's answer, in English
                ReportText(ConsoleLevel.Warning, Loc.Format("Issue_Upload", w), "Upload: " + w, quiet: true);
            }

            Report(ConsoleLevel.Info, "Status_SaveUploadRecord", Loc.Get("Save_Button"));
        }
        catch (OperationCanceledException)
        {
            Report(ConsoleLevel.Warning, "Status_UploadCancelled");
        }
        catch (RaiException ex)
        {
            HandleRaiFailure(ex, dcp);
        }
        finally
        {
            _operation = null;
            IsBusy = false;
            IsUploading = false;
            OnUploadStateChanged();
        }

        // The DCP at RAI, in the app's browser window, as "Bei RAI öffnen" does
        if (uploaded && ReferenceEquals(dcp, _dcp) && dcp.Upload is { Url.Length: > 0 })
        {
            await OpenRaiPageAsync();
        }
    }

    /// <summary>
    /// Right after an upload: what RAI saved is in the DCP, which is unsaved now. The recovery snapshot gets it at
    /// once, so a crash does not lose it; the file gets it with Speichern.
    /// </summary>
    private void RecordUpload(Dcp dcp)
    {
        if (ReferenceEquals(dcp, _dcp))
        {
            WriteRecovery();
        }
    }

    /// <summary>
    /// Signs in with the user name and password from the settings. Without them the upload stops with a failure that
    /// leads to the settings. Throws <see cref="RaiException"/> when the portal refuses.
    /// </summary>
    private async Task<bool> EnsureLoginAsync(CancellationToken cancellationToken)
    {
        if (_rai.IsLoggedIn)
        {
            return true;
        }

        var username = _settings.RaiUsername.Trim();
        var password = username.Length == 0 ? null : _credentials.LoadPassword(username);
        if (password is null)
        {
            _uploadFailure = new UploadFailure(Loc.Get("Export_NoLogin"), Loc.Get("Export_HintCredentials"), CanForget: false);
            Report(ConsoleLevel.Error, "Status_NoLogin");
            return false;
        }

        _raiContacted = true;
        SetStatus("Status_LoggingIn", username);
        await _rai.LoginAsync(new RaiCredentials(username, password), cancellationToken);
        SetStatus("Status_LoggedIn", username);
        return true;
    }

    private void HandleRaiFailure(RaiException ex, Dcp dcp)
    {
        if (ex.PartialUpload is { } partial)
        {
            dcp.Upload = partial;
            OnChanged();
            RecordUpload(dcp);
            Report(ConsoleLevel.Error, "Issue_PartialUpload", partial.LinesSaved, dcp.Lines.Count);
        }

        // The log file gets the client's English message, which has the details
        if (ex.PortalMessages.Count == 0)
        {
            ReportText(ConsoleLevel.Error, Loc.Format("Issue_Upload", MessageText.Rai(ex)), "Upload: " + ex.Message, quiet: true);
        }

        foreach (var message in ex.PortalMessages)
        {
            // RAI numbers the lines ("opera 6") like the table does; RAI's own message, as RAI wrote it
            var match = Regex.Match(message, @"opera\s+(\d+)", RegexOptions.IgnoreCase);
            var line = match.Success && int.TryParse(match.Groups[1].Value, out var n) && n >= 1 && n <= dcp.Lines.Count ? dcp.Lines[n - 1] : null;
            var title = line is null || string.IsNullOrWhiteSpace(line.Titolo) ? "" : $" ({line.Titolo.Trim()})";
            ReportText(ConsoleLevel.Error, $"RAI: {message}{title}", quiet: true);
        }

        var sent = ex.PartialUpload is { } saved
            ? Loc.Format("Export_PartlySent", saved.LinesSaved, dcp.Lines.Count)
            : Loc.Get("Export_NothingSent");
        var hint = (ex.Kind, ex.Code) switch
        {
            (_, RaiException.DraftNotFound) => Loc.Get("Export_HintDraftNotFound"),
            (RaiErrorKind.InvalidCredentials or RaiErrorKind.InteractiveLoginRequired, _) => Loc.Get("Export_HintCredentials"),
            (RaiErrorKind.Network, _) => Loc.Get("Export_HintNetwork"),
            (RaiErrorKind.FormRejected, _) => Loc.Get("Export_HintRejected"),
            (_, RaiException.TooManySessions) or (RaiErrorKind.SessionExpired, _) => Loc.Get("Export_HintRetry"),
            _ => Loc.Get("Export_HintDiagnostics"),
        };
        var reason = ex.Kind == RaiErrorKind.FormRejected && ex.PortalMessages.Count > 0 ? Loc.Get("Rai_FormRejected") : MessageText.Rai(ex);
        _uploadFailure = new UploadFailure($"{reason} {sent}", hint, ex.Code == RaiException.DraftNotFound);
        Report(ConsoleLevel.Error, "Status_UploadFailed");
    }

    [RelayCommand]
    private void CancelOperation() => _operation?.Cancel();

    /// <summary>Drops what is known about an earlier upload, so the next upload creates a new DCP at RAI.</summary>
    [RelayCommand]
    private async Task ForgetUpload()
    {
        if (_dcp.Upload is null)
        {
            return;
        }

        var text = _dcp.Upload.Codice is { } codice ? Loc.Format("Msg_ForgetUploadCodice", codice) : Loc.Get("Msg_ForgetUpload");
        if (!await _dialogs.Confirm(Loc.Get("Export_Forget"), text, Loc.Get("Dlg_Forget"), Loc.Get("Common_Cancel")))
        {
            return;
        }

        _dcp.Upload = null;
        _uploadFailure = null;
        OnChanged();
        SetStatus("Status_UploadForgotten");
    }

    /// <summary>"Bei RAI öffnen": the page of this DCP at RAI, much easier than finding it in RAI's search.</summary>
    [RelayCommand]
    private async Task OpenRaiDcp() => await OpenRaiPageAsync();

    /// <summary>The DCP's page at RAI in the app's browser window (signed in), or the system browser when that fails.</summary>
    private async Task OpenRaiPageAsync()
    {
        if (_dcp.Upload is not { Url.Length: > 0 } record || !Uri.TryCreate(record.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, _rai.PortalUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            Report(ConsoleLevel.Warning, "Status_NotUploadedYet");
            return;
        }

        try
        {
            if (!await EnsureLoginAsync(CancellationToken.None))
            {
                OnUploadStateChanged();
                return;
            }
        }
        catch (RaiException ex)
        {
            Report(ConsoleLevel.Error, "Issue_Upload", MessageText.Rai(ex));
            return;
        }

        if (!await _dialogs.ShowBrowser(uri, _rai))
        {
            OpenWithShell(uri.ToString());
            await _dialogs.Message(Loc.Get("Dlg_SystemBrowser"), Loc.Get("Msg_SystemBrowser"));
        }
    }

    [RelayCommand]
    private async Task CopyRaiNumber()
    {
        if (_dcp.Upload?.Codice is { } codice)
        {
            await _dialogs.CopyText(codice);
            SetStatus("Status_NumberCopied", codice);
        }
    }

    /// <summary>"Verbindung testen" in the settings: signs in with what is entered there. Returns the result in words.</summary>
    internal async Task<string> TestRaiLoginAsync(string username, string password)
    {
        if (IsBusy)
        {
            return "";
        }

        IsBusy = true;
        _operation = new CancellationTokenSource();
        try
        {
            _raiContacted = true;
            SetStatus("Status_LoggingIn", username);
            await _rai.LoginAsync(new RaiCredentials(username, password), _operation.Token);
            SetStatus("Status_LoggedIn", username);
            return Loc.Format("Settings_TestOk", DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
        }
        catch (RaiException ex)
        {
            Report(ConsoleLevel.Warning, "Issue_Upload", MessageText.Rai(ex));
            return MessageText.Rai(ex);
        }
        catch (OperationCanceledException)
        {
            return "";
        }
        finally
        {
            _operation = null;
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- documents

    public bool HasCustomTemplate => TemplateFile is { } file && File.Exists(file);

    /// <summary>Second line of the document row: what it is, or when it was exported.</summary>
    public string DocumentMeta => (_lastExport, HasCustomTemplate) switch
    {
        ({ } done, _) => Loc.Format("Docs_ExportedAt", WhenText(done.At), done.At.ToString("HH:mm", CultureInfo.InvariantCulture)),
        (null, true) => Loc.Get("Docs_MetaCustom"),
        _ => Loc.Get("Docs_Meta"),
    };

    public bool IsDocumentExported => _lastExport is not null;

    private void OnDocumentExportChanged()
    {
        OnPropertyChanged(nameof(DocumentMeta));
        OnPropertyChanged(nameof(IsDocumentExported));
        OnPropertyChanged(nameof(HasCustomTemplate));
    }

    [RelayCommand]
    private async Task ExportDocument()
    {
        CommitPendingEdits?.Invoke();
        SyncLines();
        var issues = DcpValidator.Validate(_dcp, ValidationPurpose.Document);
        var errors = issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            RunCheck(manual: false);
            Report(ConsoleLevel.Error, "Status_NotExportedProblems", errors.Count);
            return;
        }

        var name = string.IsNullOrWhiteSpace(_dcp.TitoloItaliano) ? "DCP" : _dcp.TitoloItaliano.Trim();
        if (_dcp.Puntata is { } p)
        {
            name += $" {p:00}";
        }

        var folder = FilePath is { } current ? Path.GetDirectoryName(current) : _settings.LastDocumentFolder;
        var path = await _dialogs.PickSave(Loc.Get("Docs_Export"), Sanitize(name + " Allegato B"), _exporter.FileExtension, Loc.Get("Picker_WordDocument"), folder);
        if (path is null)
        {
            return;
        }

        try
        {
            // Into memory first: a failing export must not leave a broken (or truncated older) document behind
            using (var buffer = new MemoryStream())
            {
                using var template = HasCustomTemplate ? File.OpenRead(TemplateFile!) : null;
                _exporter.Export(_dcp, _settings.Company, buffer, template);
                await File.WriteAllBytesAsync(path, buffer.ToArray());
            }

            _lastExport = (path, DateTime.Now);
            OnDocumentExportChanged();
            SetStatus("Status_Exported", Path.GetFileName(path));
            // Warnings the console lists already (from the check) are not repeated
            foreach (var text in issues.Where(i => !i.IsError).Select(IssueText).Where(t => ConsoleLines.All(l => l.Message != t)))
            {
                ReportText(ConsoleLevel.Warning, text, quiet: true);
            }
        }
        catch (Exception ex) when (ex is ExportException or IOException or UnauthorizedAccessException)
        {
            Report(ConsoleLevel.Error, "Msg_ExportFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenExportedDocument()
    {
        if (_lastExport is { } done && File.Exists(done.Path))
        {
            OpenWithShell(done.Path);
        }
    }

    [RelayCommand]
    private void ShowExportedDocument()
    {
        if (_lastExport is { } done)
        {
            FileManager.Reveal(done.Path);
        }
    }

    /// <summary>
    /// "Vorlage ersetzen…": another Word file is used for Allegato B from now on. It is tried with this DCP first, so a
    /// file the export cannot fill is refused right away.
    /// </summary>
    [RelayCommand]
    private async Task ReplaceTemplate()
    {
        if (TemplateFile is not { } target)
        {
            return;
        }

        var file = await _dialogs.PickFile(Loc.Get("Picker_Template"), Loc.Get("Picker_WordDocument"), "*" + _exporter.FileExtension, _settings.LastDocumentFolder);
        if (file is null)
        {
            return;
        }

        try
        {
            SyncLines();
            using (var template = File.OpenRead(file))
            using (var trial = new MemoryStream())
            {
                _exporter.Export(_dcp, _settings.Company, trial, template);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            SetStatus("Status_TemplateReplaced", Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is ExportException or IOException or UnauthorizedAccessException)
        {
            Report(ConsoleLevel.Error, "Status_TemplateRefused", Path.GetFileName(file), ex.Message);
        }

        OnDocumentExportChanged();
    }

    [RelayCommand]
    private void UseOriginalTemplate()
    {
        if (!HasCustomTemplate)
        {
            return;
        }

        try
        {
            File.Delete(TemplateFile!);
            SetStatus("Status_TemplateOriginal");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(ConsoleLevel.Error, "Status_FileProblem", ex.Message);
        }

        OnDocumentExportChanged();
    }
}
