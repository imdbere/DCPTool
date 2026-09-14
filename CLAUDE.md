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

CI (`.github/workflows/build.yml`) publishes a framework-dependent `win-x64` folder (users install the .NET 6 Desktop Runtime) on every push/PR to `master` and uploads it as a workflow artifact. Pushing a `v*` tag also attaches a zip of it to a GitHub Release. The app version comes from `<Version>` in the csproj, which CI overrides from the tag (`-p:Version`), so don't put version attributes back into `AssemblyInfo.cs`.

There are no tests and no lint setup. The app accepts an optional `.dcp` file path as its first command-line argument and opens it on startup (`App.xaml.cs`).

## Architecture

**Data flow:** audio files (mp3/wav), added by drag-drop or the "+" button → `MainWindow.ReadDcpLineFromFile` reads ID3v2 tags with TagLib and creates `DcpLine`s → the user edits them in two auto-generated `DataGrid`s (a DCP list plus the lines of the selected DCP) → one of three outputs:
- **Save/Open `.dcp`**: JSON via `DataContractJsonSerializer` of a single `Dcp`.
- **Export `.docx`**: `Helpers/DocumentWriter` copies the `dcp.docx` template (copied to the output dir) and writes into fixed cell coordinates of `body.ChildElements[3]` (header rows 3–13, line rows start at row 29, max 10 lines). Changing the template breaks these indices.
- **Upload**: `Helpers/DcpInterface` (partial class, split across `DCPInterface.cs` and `DcpInterface.Helpers.cs`).

**Tag parsing conventions (Sonoton library files):** composers/performers are `|`-separated. The title is cut at `---`. `SiglaNum` is the album name before the first `-`. `Marca` is hard-coded to "Sonoton". The line was previously enriched through the Sonofind API (`Helpers/SonofindInterface`). That call is now commented out and all data comes from the tags.

**RAI upload (`DcpInterface`)** is HTTP screen-scraping of an ASP.NET WebForms site behind a Pulse Secure/Juniper SSL VPN (`intranetssl.rai.it`):
- Login posts to `dana-na/auth/.../login.cgi` and handles the "maximum number of open user sessions" page by closing sessions. Pages are addressed through the VPN's `,DanaInfo=.<token>+Page.aspx` URL rewriting. `DanaInfo` is a hard-coded constant, and the code isn't sure whether it depends on the account.
- Every POST copies `__VIEWSTATE`, `__VIEWSTATEGENERATOR` and `__EVENTVALIDATION` from the previous response HTML (HtmlAgilityPack).
- Lines are uploaded **4 per POST** (the form's grid size). Short batches are padded with empty lines. Form field names (`DataGrid1:_ctl{n+2}:...`) and all the hidden-field values live in `Helpers/DCPInterfaceExtensions.cs`.
- Upload errors are found by reading `MessageBarControl1_Table1` in the response. Responses are written to `loginRes.html` / `uploadRes.html` in the working directory for debugging.
- After login, session cookies are pushed into WinINet (`Tools.SetInternetExplorerCookies`), so `BrowserWindow` (a WPF `WebBrowser`, i.e. the IE engine, with script errors silenced through COM) opens the result page already logged in.

`dcp.html` in the repo root is a saved copy of the RAI DCP form page. Use it as a reference for field names.

Credentials are stored in user-scoped `Properties/Settings` (`Username`/`Password`) and set through `LoginWindow`.
