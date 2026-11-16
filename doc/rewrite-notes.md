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

1. ~~Which Marca should Artlist lines have?~~ **Answered (November 2026):** RAI checks Marca and Edizione against its own lists. It rejected `ARTLIST ORIGINAL` as Marca and `ARTLIST ORIGINAL MUSIC` as Edizione, and accepts `ARTLIST` as Marca. Artlist lines now use Marca `Artlist`, and Edizione is always sent empty, as for Sonoton.
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

## Checklist for a cross-platform rewrite

What must not be overlooked when the app moves to a framework that runs on Windows, macOS and Linux. 🔴 = critical (getting it wrong breaks DCPs or loses data), 🟠 = difficult or a lot of work.

### The RAI upload (the riskiest part)

- 🔴 **RAI has no test environment.** Every test upload creates a real DCP. Before rewriting, find out how to delete or cancel a test DCP, or ask RAI's DCP office for a test account.
- 🔴 **Record what the current tool sends before changing anything.** Log the exact form data of a few real uploads (1, 4, 5 and 9 lines; accented names like "Kobák"; empty fields). The new version must send the same, field for field.
- 🔴 **The form logic is implicit and fragile.** See [rai-dcp-upload.md](rai-dcp-upload.md):
  - ASP.NET page state is copied from each response into the next request.
  - Lines go up 4 at a time, and each request resends all earlier lines.
  - Short batches are padded with empty lines, whose Ruolo is `PP`.
  - The line numbering in the field names continues across batches (`_ctl2`, `_ctl3`, …).
  - Authors are encoded as `N#Cognome%Nome#`, with `+` replacing spaces.
  - About 60 hidden constants must be sent unchanged.

  Port `DCPInterfaceExtensions.cs` field for field rather than rewriting it.
- 🔴 **Character encoding of accented names.** Check how RAI stores "Kobák" or "Nössing" today; the server may expect Windows-1252 rather than UTF-8. Test before trusting a different HTTP library.
- 🟠 **The VPN portal (Pulse Secure / Ivanti) is not under our control.** The login form, the realm `ADonly`, `url_56`, the "maximum sessions" page and the `DanaInfo` URL token can change with an Ivanti update. Check that login also works from macOS and Linux.
- 🟠 **Session handling is missing.** It needs expiry detection with automatic re-login, clear errors when the page layout changes, and a way to deal with partial uploads (each batch saves on RAI's side).
- 🔴 **Find out what users do in the browser after the upload.** The tool opens the saved DCP and the user finishes something there by hand. That step isn't documented; watch a real user before redesigning the flow.
- 🟠 **The embedded browser is Windows-only** (IE engine, cookies copied in through WinINet). Either use a cross-platform web view that can receive the session cookies (check what the chosen framework offers), or use the system browser, which means the user logs in a second time.

### The parts that only work on Windows

- `WebBrowser` (IE engine), WinINet cookie copying and the COM call that silences script errors.
- Credentials in plain text in `user.config`. Use the macOS Keychain, Windows Credential Manager and libsecret on Linux, through one cross-platform library.
- The Windows proxy login (`DefaultNetworkCredentials`) behaves differently elsewhere. Test from a company network.
- Opening files with `Process.Start` + `UseShellExecute` (`open` on macOS, `xdg-open` on Linux).
- Debug HTML written to the working directory. Use a per-OS log folder.
- Already portable: TagLib, OpenXml, HtmlAgilityPack and `ArtlistCueSheetReader`. Move them into a core library first and reuse them.

### Data and compatibility

- 🔴 **Existing `.dcp` files must still open.** They use the old Microsoft JSON date format (`\/Date(…)\/`) with timezone offsets, ISO durations (`PT3M56S`) and enums stored as numbers. Authors are stored twice, and on load `AutoriString` wins. Write an importer, test it against real old files, and version the new format.
- 🔴 **Domain rules hidden as small defaults.** Carry them over deliberately:
  - Recording date = broadcast date − 5 days.
  - `TitoloItaliano` = "Programma - Serie".
  - Serie is sent as 3 digits, and a zero is sent as empty.
  - `OraFine` = start time + duration.
  - `Committente` = `BZ/<sede>`.
  - Marca is sent upper case and must be on RAI's list. Edizione is never sent.
  - The Sonoton title is cut at `---`, and Sigla is the album text before the first `-`.
  - In names, the last word is the Cognome, and a comma means "Cognome, Nome".
- 🔴 **Dates and number formats.** RAI expects `dd/MM/yyyy` regardless of the user's settings. The Word export currently uses the system date format. Never depend on the machine's locale for anything sent to RAI.
- 🟠 **The Word template uses fixed cell positions**, is limited to 10 lines and contains hard-coded names. Find the table by its header text and add rows as needed.
- Old user settings only hold the username. Users can log in again.

### Audio files and matching

- 🔴 **Durata is the full file length today, but the DCP should list the time actually used in the programme.** Users presumably correct it by hand. Decide whether the rewrite reads the edit timeline (DaVinci Resolve, EDL/AAF/FCPXML). That would be the biggest gain in accuracy and a strong selling point.
- 🔴 **macOS stores accented file names in decomposed Unicode form (NFD).** Normalise before comparing (the Artlist matcher already does).
- 🟠 **Linux file systems are case-sensitive.** Compare extensions and the `cue_sheet_*` pattern case-insensitively.
- 🟠 **Ignore macOS helper files** (`._*.mp3` from network drives or USB sticks, `.DS_Store`), or dropping a folder produces errors.
- 🟠 **WAV metadata** (RIFF INFO, ID3 chunk, BWF `bext`/iXML): musicmedia and Artlist both offer WAV downloads. Check every field against real WAV files, not only MP3s.
- Upgrade the tag library: `TagLib.Portable` 1.3.1 is old; TagLibSharp is the maintained successor.
- 🟠 **Drag and drop differs between operating systems** (Finder hands over file URLs; folders and network drives behave differently). Test on each platform.

### Making it a product

- 🔴 **Ask RAI whether automated use of the intranet form is allowed** before selling, and whether an official import format exists.
- 🔴 **Make the repo private.** It's currently public, including the builds and these docs.
- 🟠 **Code signing:**
  - macOS needs an Apple Developer ID plus notarisation, or Gatekeeper blocks the app.
  - Windows needs a code-signing certificate, or SmartScreen warns users.
  - Add both to CI.
- 🟠 **Updates and licensing:** an auto-update mechanism, licence keys or activation, and per-company settings (supplier, legal representative, RAI contact, default sede/struttura, and all RAI sedi, whose codes are unknown so far).
- 🟠 **Privacy (GDPR):** the tool stores writer names, credentials and possibly logs. Decide what is kept and where.
- Music libraries as plug-ins (Sonoton tags and Artlist cue sheets first), plus a manual CSV import as the fallback.

### How to run the rewrite

- **Framework:** Avalonia is closest to WPF and keeps all the C# (importer, cue sheet reader, OpenXml, HTML parsing); target .NET 10 LTS. Electron or Tauri would also mean replacing TagLib, OpenXml and the reader. MAUI doesn't support Linux.
- **Core library first, with tests:** fixtures from the example cue sheets, tags taken from real files, and saved RAI pages.
- **Run old and new side by side:** compare the form data and the Word output for the same input files before switching users over.
- **Fix the [known bugs](#known-bugs) explicitly** rather than porting them.
