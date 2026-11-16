# Architecture

DCP Tool is a single-project WPF desktop app for Windows (`net6.0-windows`), about 1,800 lines of C# (1,300 before the Artlist import). There is no MVVM layer, no dependency injection and no test project. Most logic sits in `MainWindow.xaml.cs` and a few static helpers.

## Data flow

```
 audio files (.mp3/.wav)            Artlist cue sheets (cue_sheet_*.xlsx)
 drag & drop / "+" button           dropped too, or found next to the audio
          │                                       │
          └──────────────► DcpLineImporter ◄──────┘
                           (TagLib + ArtlistCueSheetReader)
                                     │  DcpLine[]
                                     ▼
          MainWindow: DCP grid (one row per Dcp) + line grid (lines of the selected Dcp)
          the user completes header fields and corrects lines by hand
                                     │
       ┌─────────────────────────────┼──────────────────────────────┐
       ▼                             ▼                              ▼
 File → Save As / Open        Publish → Upload               Publish → Export
 .dcp (JSON of one Dcp)       DcpInterface → RAI intranet    DocumentWriter → .docx
                              form, then BrowserWindow       from the dcp.docx template
                              shows the saved DCP
```

## Components

| File | Role |
|---|---|
| `App.xaml.cs` | Startup. Sets the UI culture, opens `MainWindow`, passes an optional `.dcp` path from the command line |
| `MainWindow.xaml(.cs)` | The whole UI: menu, two auto-generated `DataGrid`s, progress bar, "+" button. Handles open/save, drag-drop, upload, export, login |
| `Models/DCP.cs` | `Dcp` (programme header + `Lines`), `DcpLine`, `Autore`, `PaperDcpValueAttribute` |
| `Models/DCPEnums.cs` | `GenereSiae`, `Ruolo`, `TipoGenerazione`, `ReteTrasmissione`, `Sede`, with numeric values from the RAI form and `[PaperDcpValue]` texts for the Word form |
| `Helpers/DcpLineImporter.cs` | Audio files (+ Artlist cue sheets) → `DcpLine`s. See [music-sources.md](music-sources.md) |
| `Helpers/ArtlistCueSheetReader.cs` | Reads Artlist cue sheet `.xlsx` files without an Excel library |
| `Helpers/DCPInterface.cs`, `DcpInterface.Helpers.cs` | HTTP client for the RAI intranet: VPN login, posting the DCP form. See [rai-dcp-upload.md](rai-dcp-upload.md) |
| `Helpers/DCPInterfaceExtensions.cs` | Builds the form-field dictionaries (`Dcp` / `DcpLine` → RAI field names), validates lines before upload |
| `Helpers/DocumentWriter.cs` | Fills the Word template `dcp.docx` |
| `Helpers/Tools.cs` | Copies the session cookies into WinINet for the embedded browser, opens files with the shell |
| `Helpers/SonofindInterface.cs` | Old client for the Sonofind search API. **Unused** since the data comes from the tags |
| `BrowserWindow.xaml(.cs)` | WPF `WebBrowser` (Internet Explorer engine) that shows the RAI page after an upload, with script errors suppressed through COM |
| `LoginWindow.xaml(.cs)` | Asks for the RAI username and password |
| `Properties/Settings.settings` | User-scoped `Username` / `Password`, stored in plain text in `user.config` |
| `dcp.docx` | Word template of the paper DCP, copied to the output folder |
| `../dcp.html`, `../dcp.png` | An HTML mock-up of the *old* paper form ("DCP-Viewer") with sample data. Not used by the app, and not a copy of the RAI web form |

## Data model

### `Dcp` (one programme / episode)

| Property | Notes | Used by upload | Used by Word |
|---|---|---|---|
| `DataTrasmissione` | default: today | ✓ (also as replica date) | ✓ |
| `Puntata` | episode number | ✓ | ✓ (twice) |
| `DataRegistrazione` | default: `DataTrasmissione` − 5 days | ✓ | |
| `Durata` | programme length, entered by hand | ✓ | ✓ (`mm:ss`, hours are lost) |
| `Sede` | enum, default `0` = **undefined**, must be picked | ✓ (`BZ/16`) | ✓ |
| `TitoloProgramma`, `TitoloSerie` | only used to build the default `TitoloItaliano` | | |
| `TitoloItaliano` | default: `"<Programma> - <Serie>"` | ✓ | ✓ |
| `TitoloOriginale`, `Sottotitolo` | | ✓ | ✓ |
| `ReteTrasmissione` | `TV1`..`TV4` | ✓ | |
| `Uorg`, `Matricola`, `Serie` | | ✓ | Uorg, Matricola |
| `NumeroContratto`, `DataContratto` | | | ✓ |
| `OraInizio`, `OraFine` | `OraFine` = `OraInizio` + `Durata`, read-only | ✓ | |
| `Societa`, `Df`, `Regista` | shown in the grid but **not used anywhere** | | |
| `Lines` | public field (so not a grid column) | ✓ | ✓ (max 10) |

### `DcpLine` (one piece of music)

| Member | Notes |
|---|---|
| `Titolo` | |
| `Autori` (field) / `AutoriString` (property) | `Autori` is an array of `Autore { Nome, Cognome }`. The grid shows `AutoriString` (`"Nome Cognome, Nome Cognome"`). Editing it re-parses the string: split at `,`, then the last word is the Cognome and everything before it the Nome |
| `Durata` | from the audio file |
| `Gensiae`, `Ruolo`, `TipoGenerazione` | always `ML`, `SF`, `9` from the importer |
| `Marca` | sent upper case |
| `Edizione` | publisher. Empty for Sonoton, the cue sheet's publisher for Artlist |
| `SiglaNum` | catalogue code |
| `Esecutori` | free text |
| `HasAutori()` | a method rather than a property, so the grid doesn't show it |

### `.dcp` file format

`File → Save As` writes the **selected** `Dcp` with `DataContractJsonSerializer` (no `[DataContract]` attributes, so every public read/write property and public field is written). Dates are `"\/Date(ms+offset)\/"`, `TimeSpan`s are ISO 8601 (`"PT3M56S"`), enums are numbers.

`DcpLine` stores both `Autori` and `AutoriString`. Members are read in alphabetical order, so `AutoriString` is set last and its parsed value wins. Files from older versions that have no `Edizione` load fine, because the serializer runs the parameterless constructor and the property initializer gives `""`.

`File → Open` and the command-line argument **add** the loaded DCP to the list. "Save" (without "As") is disabled.

## UI

- Both grids generate their columns from the public properties (`AutoGenerateColumns`). Adding a property to `Dcp` or `DcpLine` adds a column. Enum properties become combo boxes.
- New DCPs are created by typing into the empty "new row" of the DCP grid.
- Files can be added only while a DCP is selected (the "+" button is disabled otherwise, and dropping shows a message).
- The importer runs on a background thread. Its errors and warnings are collected into one message box.
- "View → Open Browser" logs in and opens RAI's DCP search page (`RicercaDCP.aspx`) in `BrowserWindow`.

## Build and CI

- `dotnet build DCP-Tool.sln` on Windows. On macOS/Linux the project still compiles with `-p:EnableWindowsTargeting=true`, but it can't run there.
- To make a Windows build on a Mac: `dotnet publish DCP-Tool/DCP-Tool.csproj -c Release -r win-x64 --self-contained false -p:EnableWindowsTargeting=true -o publish/DCP-Tool`, then copy the folder to the Windows PC.
- CI (`.github/workflows/build.yml`) publishes a framework-dependent `win-x64` build on every push to `master` or `feature/**` and every PR to `master`, kept as a workflow artifact. A `v*` tag creates a GitHub release with a zip, and the tag becomes the version.
- Every push to a `feature/**` branch replaces the branch's **pre-release** on the Releases page (tag `build-feature-<name>`) with the new zip, so the latest test build is always at the same place. Deleting the branch deletes the pre-release (once the workflow is on `master`).
- NuGet packages: `TagLib.Portable` (tags), `HtmlAgilityPack` (RAI pages), `DocumentFormat.OpenXml` (Word), `Newtonsoft.Json` (only for the unused Sonofind client), `Microsoft.Windows.Compatibility` (7.0.6, the patched 7.0 version), plus `Microsoft.DotNet.UpgradeAssistant.Extensions.Default.Analyzers` and `System.Data.DataSetExtensions`, left over from the .NET Framework migration. `System.Formats.Asn1`, `System.Net.Http` and `System.Text.RegularExpressions` are referenced only to pin patched versions of vulnerable indirect dependencies. Check with `dotnet list DCP-Tool/DCP-Tool.csproj package --vulnerable --include-transitive`.
