# RAI upload and Word export

## The RAI DCP web application

The DCP application runs on RAI's intranet (`dcp.servizi.rai.it`, an ASP.NET WebForms site). External producers reach it through RAI's Pulse Secure / Juniper SSL VPN portal at `https://www.intranetssl.rai.it`. The portal rewrites intranet URLs into the form

```
https://www.intranetssl.rai.it/,DanaInfo=.<token>+<Page>.aspx
```

`DcpInterface` talks to it with plain `HttpClient` requests (screen scraping). There is no API and no documentation from RAI. Everything below was reverse-engineered from the pages.

| Page | Use |
|---|---|
| `/dana-na/auth/url_56/login.cgi` | VPN login |
| `/dana-na/auth/logout.cgi?delivery=psal` | logout (implemented, never called) |
| `…+DettaglioDCP.aspx` | new / edit DCP form. A GET opens an empty form |
| `…+RicercaDCP.aspx` | DCP search, opened in the embedded browser by "View → Open Browser" |

The `DanaInfo` token (`addrqiyFpv21lzr7O7r0S2C`) is a hard-coded constant. It isn't known whether it depends on the account or changes over time. It changed at least once in the past (commit "fixed DanaInfo").

### Login

`POST login.cgi` with `username`, `password`, `realm=ADonly`, `btnSubmit=Entra`, `tz_offset=60`.

- The response containing "Invalid username or password" means wrong credentials.
- "You have reached the maximum number of open user sessions": the page lists the open sessions. The tool posts back `postfixSID` (from `postfixSID_1`), `FormDataStr` (from `DSIDFormDataStr`) and `btnContinue=Close Selected Sessions and Log in`, which closes the old session.
- Session cookies live in the `HttpClient`'s `CookieContainer`. After login they're copied into WinINet (`InternetSetCookie`), so the IE-based `BrowserWindow` is logged in too.
- The responses are written to `loginRes.html` / `loginRes1.html` in the current working directory for debugging.

`DcpInterface` remembers that it's logged in (`_loggedIn`) for the lifetime of the app. It never notices an expired session.

### Uploading a DCP

1. `GET DettaglioDCP.aspx` returns an empty form with four empty line rows.
2. For every block of four lines: `POST` the form back to the URL of the last response, with
   - the hidden ASP.NET state `__VIEWSTATE`, `__VIEWSTATEGENERATOR` and `__EVENTVALIDATION`, copied from the previous response,
   - the header fields (`GetBasicFormData`),
   - the fields of **all lines so far**, with the last block padded with empty lines to four (`GetLineFormData`),
   - the "save" image button coordinates `CommandBarControl1:ImageButton_Salva.x/.y`.

   The control indices keep counting up (`_ctl2`..`_ctl5` for the first block, `_ctl6`..`_ctl9` for the second). Presumably each save adds four empty rows to the form, which the next POST fills.
3. Each response is checked for an error in `MessageBarControl1_Table1`. The error text is shown, and the response is saved as `uploadRes.html` and opened.
4. The final URL opens in `BrowserWindow`, where the user checks the DCP and finishes it on RAI's side.

Before the first POST, `GetLineFormData` refuses the DCP if a line has no Durata or no Autori.

The body is URL-encoded by hand, because `FormUrlEncodedContent` fails on the very long `__VIEWSTATE`.

**Partial uploads.** Each POST saves. If the second block fails, the first four lines are already saved on RAI's side, and a retry creates a second DCP.

### Header fields

| Form field | Value |
|---|---|
| `TextBox_TitoloIta`, `TextBox_TitoloOri`, `TextBox_SottotitoloPuntata` | `TitoloItaliano`, `TitoloOriginale`, `Sottotitolo` |
| `DateControl_DataRegistrazione:TextBox_Data` | `DataRegistrazione`, `dd/MM/yyyy` |
| `DateControl_DataTrasmissione:TextBox_Data`, `DateControl_DataRepl:TextBox_Data` | `DataTrasmissione` |
| `TimeControl_DurataTx:hh/mm/ss` | `Durata` |
| `TimeControl_OraInizio:*`, `TimeControl_OraFine:*` | `OraInizio`, `OraFine` |
| `APControl_UMSP:UorgTxt / MatricolaTxt / SerieTxt / PuntataTxt` | `Uorg`, `Matricola`, `Serie` (3 digits), `Puntata`. Zero is sent as empty |
| `Textbox_Committente` | `"BZ/" + (int)Sede`, e.g. `BZ/16` |
| `DropDownList_SediPool` | `(int)Sede` |
| `CheckControl_Struttura:TextBox1` | `ReteTrasmissione` (`TV1`..`TV4`) |
| `…:Hidden_*`, `…:TextBox_NomeTabella`, `…:TextBox_Dimensioni`, `flag*`, … | constants copied from the real form |

### Line fields

Each line `n` (0-based, counted over the whole DCP) uses the prefix `DataGrid1:_ctl{n+2}:`.

| Field | Value |
|---|---|
| `CheckControl_GenereSIAE:TextBox1` | `ML` (lookup table `TabGenereSIAE`) |
| `CheckControl_Ruolo:TextBox1` | `SF` (`TabRuolo`) |
| `TextBox_TitoloOpera` | `Titolo` |
| `AuthorsControl_AutoriOpera:HiddenText` | authors, see below |
| `TimeControl_Durata:hh/mm/ss` | `Durata` (fractions of a second are dropped) |
| `CheckControl_TipoGenerazione:TextBox1` | `9` (`TabTipoGenerazione`) |
| `CheckControlMarca_MarcaOpera:TextBox1` | `Marca`, upper case. RAI checks it against its own list: `SONOTON` and `ARTLIST` are accepted, `ARTLIST ORIGINAL` was rejected ("La marca dell'opera 6 non è valida") |
| `CheckControlEdizione_EdizioneOpera:TextBox1` | always empty. RAI checks it against its own list of publishers and rejected Artlist's ("L'edizione dell'opera 6 non è valida", November 2026) |
| `CheckControlEdizione_EdizioneOpera:Edizione_esiste` | always empty. Presumably set by the form's publisher lookup |
| `TextBox_SiglaNum` | `SiglaNum` |
| `TextBox_EsecutoriOpera` | `Esecutori` |
| `TextBox_NoteOpera`, `CheckControl_Proprieta:TextBox1`, `TextBox_DataProduz` | always empty |

Padding lines have empty Genere SIAE, Tipo generazione, Marca and Edizione. Their Ruolo is the enum default `PP`.

**Author encoding** (`HiddenText`): `"<count>#" + for each author "<Cognome>%<Nome>#"`, with spaces inside a name replaced by `+`. Example: `2#Montgomery%Jeff#Noble%Matthew#`. A line without authors would be `1#%#`, but upload now refuses it.

`GetSearchFormData` holds the field set of the search page (`RicercaDCP.aspx`). It isn't used.

## Word export

`DocumentWriter` copies `dcp.docx` (the paper DCP as a Word table) and writes into fixed cells of the **fourth body element** (`body.ChildElements[3]`, the big table). `ChildElements` of a table row includes the `trPr` element, so cell index 1 is the first visible cell.

| Row | Cells |
|---|---|
| 3 | 1: supplier (hard-coded **"Penn Pro"**), 2: Sede text from `[PaperDcpValue]` (`PROG. L. TED.   BZ/16`), 3: broadcast date |
| 5 | 2: Titolo italiano, 4: Puntata, 6: Durata `mm:ss` |
| 7 | 2: Titolo originale |
| 9 | 2: Sottotitolo, 6: Durata |
| 11 | 2: contract number, 4: contract date |
| 13 | 2: Uorg, 4: Matricola, 6: Puntata |
| 29–38 | one music line each: 2 Genere SIAE, 3 Ruolo, 4 Titolo, 5 Autori, 6 Esecutori, 7 Durata `mm:ss`, 8 `Marca + " " + SiglaNum`, 9 Edizione (falls back to the Marca when empty, which is the case for Sonoton) |

Limits:

- **Only 10 lines.** Line 11 and later are silently left out.
- The template itself contains customer data: the supplier's representative ("Georg Penn") and the RAI programme manager ("Philipp Nössing"), plus sample values in the cells the writer overwrites.
- Any edit to the template that adds or removes a row or cell shifts the indices and breaks the export.
- Dates use `ToShortDateString()`, i.e. the Windows user's date format.
