# Notes for the rewrite

What to fix, generalise or decide before DCP Tool can be sold to other production companies. Items marked **(verified)** were reproduced while writing these notes, by running the app's model and helper code in a console harness. The rest come from reading the code.

## Known bugs

| # | Bug | Effect |
|---|---|---|
| 1 | **(verified)** `DataContratto` defaults to `DateTime.MinValue`. `DataContractJsonSerializer` converts dates to UTC, which is below the minimum in any UTC+ timezone | **Save As throws** (and the exception is unhandled) unless a contract date is entered |
| 2 | **(verified)** `Sede` defaults to `0`, which isn't an enum member. `Dcp.GetPaperDcpValue` indexes an empty array | **Export throws** unless a Sede is picked. Upload sends `BZ/0` |
| 3 | Word export stops after 10 lines without saying so | Lines are silently missing from the document |
| 4 | Programme `Durata` is exported as `mm:ss` | An hour-long programme shows as `05:00` |
| 5 | `_loggedIn` is never reset, and `SendPost` dereferences `__VIEWSTATE` without a null check | After the VPN session expires, uploads fail with a `NullReferenceException` until the app is restarted |
| 6 | An upload error after the first block of four lines | The first lines are saved on RAI's side, and a retry creates a second DCP |
| 7 | `ShowLogin` calls `Login(user, pass)`, which returns at once if already logged in. `DcpInterface` keeps the credentials it was created with | Changing the account in Settings doesn't take effect until restart, and new credentials aren't actually checked |
| 8 | Debug responses (`loginRes.html`, `uploadRes.html`) are written to the working directory | They contain session data and the DCP, and the write fails (so login fails) when the working directory isn't writable, e.g. under Program Files |
| 9 | `AutoriString` is split at commas | Typing `"Smith, John"` into the grid creates two authors |
| 10 | `tz_offset=60` is hard-coded in the login | Probably harmless, wrong in summer time |
| 11 | `MainWindow` calls `ShowLogin()` from its constructor without awaiting it (CS4014) | The login dialog opens modally before the main window exists, so the main window only appears once it is closed |
| 12 | `LoadDcpFile` never disposes the stream | The `.dcp` file stays locked until GC |

Fixed together with the Artlist import:

- Names with three or more words lost words: `"John Michael Smith"` became Nome `John`, Cognome `Michael`, both on import and on every `.dcp` save/load (because `AutoriString` is re-parsed on load). Now the last word is the Cognome and nothing is dropped.
- Dropping files with no DCP selected silently did nothing. It now says "Please select a DCP first".
- Tags were read on the UI thread, which froze the progress bar.

## Hard-coded for one customer

| What | Where |
|---|---|
| Supplier name "Penn Pro" | `DocumentWriter.FillHeader` |
| "Georg Penn" (legal representative), "Brigitte Margesin" (RAI programme manager) | inside `dcp.docx` |
| Only the three Bolzano sedi (`BZ/14`, `/15`, `/16`), `Textbox_Committente = "BZ/" + sede` | `Sede` enum, `GetBasicFormData` |
| `Marca = "Sonoton"`, Sonoton title/album conventions | `DcpLineImporter.CreateSonotonLine` |
| Genere SIAE `ML`, Ruolo `SF`, Tipo generazione `9` for every line | `DcpLineImporter` |
| VPN realm `ADonly`, login URL `url_56`, `DanaInfo` token | `DcpInterface`, `DcpInterfaceExtensions.GetLoginForm` |

A product needs a **company profile** (supplier name, representative, default sede/struttura, default RAI contact) and per-line defaults, all editable in the UI.

## Open questions for RAI's DCP office

1. Is **`ARTLIST ORIGINAL`** accepted as Marca, or does the form check Marca against a list? Same for **Edizione** (`ARTLIST ORIGINAL MUSIC`), whose control has an `Edizione_esiste` ("publisher exists") field. Sonoton lines never sent an Edizione, so this path is untested. If the upload rejects them, clear the Edizione / Marca cells in the line grid and upload again.
2. Is the **album UPC** acceptable as *Sigla / numero* for Artlist, or should it be the Artlist song ID, or empty?
3. Which **support code** fits a downloaded file on the paper / Word form? (`C` = CD doesn't really fit.)
4. Which value should **Proprietà** have for library music? Its allowed values aren't known.
5. Is there an official import format (CSV/XML) or an API, so the product doesn't depend on screen scraping?
6. Does `DanaInfo` depend on the account?
7. Does RAI allow automated access to the intranet form by third-party software? **Ask before selling.**

## Recommendations

### Platform

- .NET 6 has been out of support since November 2024. Move to the current LTS.
- Stay a **desktop app**. The upload needs the user's RAI VPN account, and a hosted service would have to store other companies' RAI passwords.
- The IE-based `WebBrowser` and the WinINet cookie trick depend on the retired Internet Explorer engine. Use WebView2, and pass the session cookies through its cookie manager.
- Store credentials in the Windows Credential Manager (or DPAPI), not in plain text in `user.config`.

### Structure

Split into projects that can be tested without a UI:

```
DcpTool.Core      model, validation, music sources, .dcp format
DcpTool.Rai       RAI client (login, session handling, form mapping)
DcpTool.Export    Word / PDF
DcpTool.App       WPF (or Avalonia) shell, MVVM
DcpTool.Tests     fixtures: example audio tags, cue sheets, saved RAI HTML pages
```

- **Music sources as plug-ins**: `IMusicSource { bool Recognises(AudioFile); DcpLine CreateLine(AudioFile, SidecarFiles); }`. The current two (Sonoton tags, Artlist cue sheets) become the first implementations. Other libraries each need their own mapping, and a manual CSV import is the fallback.
- **One validation step** used by upload and export, with per-line messages shown in the grid (missing Autori/Durata/Sede, more than 10 lines for Word, unknown Marca, …), instead of exceptions thrown in the middle of the upload.
- **RAI client**: detect an expired session and log in again; fail with a clear message when the form changes; no debug files in the working directory (use a log folder, opt-in); a dry-run mode that writes the POST bodies to a file; tests against saved HTML responses.
- **`.dcp` format**: versioned JSON (`System.Text.Json`) with ISO dates and enum names; a loader for the current files. Store authors once (not `Autori` *and* `AutoriString`).
- **Word export**: find the table and rows by their header text, add rows instead of stopping at 10, and fill the customer fields from the company profile. A PDF of the paper form could replace Word.

### UI

- Explicit columns with Italian labels and tooltips instead of auto-generated ones. Hide `TitoloProgramma`/`TitoloSerie`/`Societa`/`Df`/`Regista` or actually use them.
- An authors editor (list of Nome/Cognome rows) instead of the comma-separated string.
- Show each line's source (Sonoton / Artlist / manual) and whether a cue sheet was found.
- Remember the last folder. Optionally look for Artlist cue sheets in the user's Downloads folder.
