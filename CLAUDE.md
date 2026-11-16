# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

DCP Tool is a Windows-only WPF desktop app (.NET 6, `net6.0-windows`) for creating RAI "DCP" music cue sheets (Distinta Consuntiva Programmi): lists of the music used in a broadcast. It builds a DCP from audio files, then either uploads it to the RAI intranet DCP web app or exports it as a filled-in Word document. Domain names in the models are Italian (`Titolo`, `Durata`, `Autori`, `Sede`, ...). Keep them that way, because they map to fields in the RAI forms.

## Commands

```bash
dotnet build DCP-Tool.sln
```

```bash
dotnet run --project DCP-Tool
```

```bash
dotnet publish DCP-Tool -p:PublishProfile=FolderProfile
```

CI (`.github/workflows/build.yml`) publishes a framework-dependent `win-x64` folder (users install the .NET 6 Desktop Runtime) on every push to `master` or `feature/**` and every PR to `master`, and uploads it as a workflow artifact. Pushing a `v*` tag also attaches a zip of it to a GitHub Release. Each push to a `feature/**` branch replaces that branch's pre-release (tag `build-<branch>`, `/` → `-`) with the new zip. Deleting the branch deletes its pre-release, but only once the workflow is on `master`, because `delete` events run the default branch's workflow. The app version comes from `<Version>` in the csproj, which CI overrides from the tag (`-p:Version`), so don't put version attributes back into `AssemblyInfo.cs`.

There are no tests and no lint setup. The app accepts an optional `.dcp` file path as its first command-line argument and opens it on startup (`App.xaml.cs`).

On macOS/Linux the project compiles (but can't run) with `dotnet build DCP-Tool/DCP-Tool.csproj -p:EnableWindowsTargeting=true`. `Models/`, `Helpers/DcpLineImporter.cs`, `Helpers/ArtlistCueSheetReader.cs`, `DCPInterfaceExtensions.cs` and `DocumentWriter.cs` have no WPF dependency, so they can be exercised from a console project that links them.

Detailed documentation for the planned rewrite lives in `doc/` (architecture, music sources, the RAI upload protocol, known bugs).

## Architecture

**Data flow:** audio files (mp3/wav) and Artlist cue sheets (`.xlsx`), added by drag-drop or the "+" button → `Helpers/DcpLineImporter` reads ID3v2 tags with TagLib (and cue sheets with `ArtlistCueSheetReader`) and creates `DcpLine`s → the user edits them in two auto-generated `DataGrid`s (a DCP list plus the lines of the selected DCP) → one of three outputs:
- **Save/Open `.dcp`**: JSON via `DataContractJsonSerializer` of a single `Dcp`.
- **Export `.docx`**: `Helpers/DocumentWriter` copies the `dcp.docx` template (copied to the output dir) and writes into fixed cell coordinates of `body.ChildElements[3]` (header rows 3–13, line rows start at row 29, max 10 lines). Changing the template breaks these indices.
- **Upload**: `Helpers/DcpInterface` (partial class, split across `DCPInterface.cs` and `DcpInterface.Helpers.cs`).

**Music sources (`DcpLineImporter`):**
- **Sonoton** (musicmedia.it), the default: composers/performers are `|`-separated. The title is cut at `---`. `SiglaNum` is the album name before the first `-`. `Marca` is hard-coded to "Sonoton". The line used to be enriched through the Sonofind API (`Helpers/SonofindInterface`, now unused) and all data comes from the tags.
- **Artlist**, recognised by a matching cue sheet or "Artlist" in the copyright tag: the tags have no composer, and the artist is *not* the composer, so Autori, Marca (Label), SiglaNum (album UPC) and `Edizione` (publisher) come from the cue sheet `cue_sheet_<title>_<id>.xlsx`. Cue sheets are taken from the same drop or from the audio file's folder, matched by title + artist (fallback: file name, tie-break: duration). Cue sheets dropped later fill in existing lines that have no Autori. `ArtlistCueSheetReader` opens the xlsx as a zip, because its `styles.xml` breaks Excel libraries.
- Upload refuses lines without Autori or Durata (`GetLineFormData`).

**RAI upload (`DcpInterface`)** is HTTP screen-scraping of an ASP.NET WebForms site behind a Pulse Secure/Juniper SSL VPN (`intranetssl.rai.it`):
- Login posts to `dana-na/auth/.../login.cgi` and handles the "maximum number of open user sessions" page by closing sessions. Pages are addressed through the VPN's `,DanaInfo=.<token>+Page.aspx` URL rewriting. `DanaInfo` is a hard-coded constant, and the code isn't sure whether it depends on the account.
- Every POST copies `__VIEWSTATE`, `__VIEWSTATEGENERATOR` and `__EVENTVALIDATION` from the previous response HTML (HtmlAgilityPack).
- Lines are uploaded **4 per POST** (the form's grid size). Short batches are padded with empty lines. Form field names (`DataGrid1:_ctl{n+2}:...`) and all the hidden-field values live in `Helpers/DCPInterfaceExtensions.cs`.
- Upload errors are found by reading `MessageBarControl1_Table1` in the response. Responses are written to `loginRes.html` / `uploadRes.html` in the working directory for debugging.
- After login, session cookies are pushed into WinINet (`Tools.SetInternetExplorerCookies`), so `BrowserWindow` (a WPF `WebBrowser`, i.e. the IE engine, with script errors silenced through COM) opens the result page already logged in.

`dcp.html` (with `dcp.png`) in the repo root is an HTML mock-up of the old paper DCP form with sample data. It is not the RAI web form and has none of its field names. Those exist only in `DCPInterfaceExtensions.cs`.

Credentials are stored in user-scoped `Properties/Settings` (`Username`/`Password`) and set through `LoginWindow`.

## v2 rewrite (branch `rewrite/v2`, in progress)

The rewrite lives next to v1: `DcpTool.sln`, `src/` and `tests/`. v1 (`DCP-Tool/`, `DCP-Tool.sln`) stays until v2 has replaced it, and `tests/DcpTool.Legacy` compiles the v1 sources so v2 is tested against v1's behaviour.

```bash
dotnet test --solution DcpTool.sln
```

```bash
dotnet run --project src/DcpTool.App
```

Targets `net10.0`. On a machine with only an older SDK set the environment variable `DCPTOOL_TFM=net9.0`.

- `DcpTool.Core`: model (`Dcp`, `DcpLine`, `Autore`, code tables as strings), `DcpValidator` (the single gate for upload and export), `.dcp` v2 format plus reader for v1 files, `RecoveryStore` (session restore), settings, and the interfaces the other projects implement.
- `DcpTool.Sources`: `IMusicSource` implementations (`MusicmediaSource`, `ArtlistSource`), tag reading with ATL, `ImportService`. A new library = a new `IMusicSource`.
- `DcpTool.Rai`: `RaiClient` (login, upload, redacted diagnostics zip), `DcpFormMapper` (must stay byte-identical to v1's form data, enforced by parity tests), `DanaInfoEncoder`. Tests use a fake HTTP handler. Never run tests against the real portal: every upload creates a real DCP at RAI.
- `DcpTool.Export`: `DocxExporter`, fills the embedded template by label text.
- `DcpTool.App`: Avalonia 12 UI (MVVM), Velopack updates. Not yet exercised by hand.

Releases: pushing a tag `v2.x.y` runs `.github/workflows/v2.yml`, which packs with Velopack and publishes to the public releases-only repository that installed apps update from. Setup and procedure: `doc/releasing.md`. v1 releases use `v0.*` / `v1.*` tags.
