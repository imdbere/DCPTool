# DCP Tool v2 roadmap

Status on 2026-11-16: v2 is a working prototype on branch `rewrite/v2`. It imports musicmedia and Artlist
files, saves and restores DCPs, exports the Word form and has uploaded to RAI once in a real test.
It is not released, the UI is a first draft, and v1 (0.10.0) is what users run.

This plan is ordered so that each milestone ends in something that can be released and used.
Work one milestone at a time; each has a "done when" list that can be checked.

## Product goals

| # | Goal | Milestone |
|---|---|---|
| G1 | Only the master timeline is imported: the app finds the music, the files, the metadata and the time each piece was on air | M4 |
| G2 | DCPs are plain `.dcp` files in a library folder the user chooses, filed by Sendung and year when first saved. Finding old ones is Explorer's job; the app offers presets, recent DCPs and a button that opens the folder | M2, M3 |
| G3 | Presets: the programme (Sendung) information is entered once and reused | M2 |
| G4 | UI overhaul | M3 |
| G5 | The UI follows the workflow. No trips to the top menu | M3 |
| G6 | Clear errors, communication and verification, above all for the RAI upload | M1, M5 |
| G7 | Fixes reach users within minutes | M1 |

## Workflow the app is built around

Decided 2026-11-17 (design brief: https://claude.ai/artifact/ERPGZZFu1X9yVLFqvCZmdz):

1. **Open**: start from a preset of the programme or from a recent DCP, in the left drawer. Older DCPs
   are found with Explorer, through "Open library folder".
2. **Add**: drop songs or a timeline onto the Add area, pick files with Add, or learn how in the guide (How?).
3. **Adjust**: edit the table of songs, like today.
4. **Export or upload**: the export page with RAI (verify, upload, log) and documents (export, edit template).
   Speichern writes the DCP; leaving it with unsaved changes asks first (no automatic saving since 2026-11-18,
   except right after an upload, since 2026-11-18).

## M1: Release v2 as a safe replacement for v1

Goal: users can install v2 instead of 0.10.0 and lose nothing. G6 (first part), G7.

- **Musicmedia labels.** musicmedia distributes about 42 labels, the app still falls back to "Sonoton".
  Test the import against `TestingEquipment/Musicmedia Examples` (MSC, NLM, ADZ, CLC ...) and build the
  prefix → Marca table. The label is not in the tags; the Ablauf builder notes already hold such a table.
  **Needs from the team: the Marca RAI accepts for each label.**
- **Sample-based tests.** Point the skipped Artlist tests at the samples and add musicmedia ones.
  Decide whether small sample files may live in the repository.
- **Upload safety.**
  - Resume: a saved draft has a stable address (`DettaglioDCP.aspx?codice_dcp=...`). After a partial
    upload the app continues or corrects that draft and never creates a second one.
  - Store RAI's DCP code (`1788111-00`) and state (`Bozza`) in the `.dcp` and show them.
  - Pre-flight for values RAI is known to reject (unknown Edizione, see the 2026-11-16 rejection).
  - Verification shown per line: saved / not saved / rejected with RAI's text.
- **Diagnostics.** Remove people's names from the diagnostics zip (today only passwords, cookies and
  tokens are removed). Add a log pane or log file of what the app did.
- **First release.** Set up the releases repository (`doc/releasing.md`), publish `v2.0.0-beta.1`,
  install it on one machine, publish a second build and watch the update arrive.
- **Moving over from v1.** Open v1 `.dcp` files (done), take over the RAI user name, and say in the
  release notes what changed (one DCP per window, session restore).

Status 2026-11-16: built and covered by tests: musicmedia rules and RAI's label list, sample tests,
resume on the draft, RAI code and state, warnings for unknown Marca and filled Edizione, per-line
rejections in the issue list, names removed from diagnostics, activity log, v1 login take-over.
Packaging was dry-run locally (63 MB installer). Open: the real checks below and the first release,
which need the releases repository, its token and a RAI account.

Done when: a real DCP with musicmedia and Artlist music goes to RAI complete and correct from an
installed v2, a deliberately broken one is reported line by line without a duplicate draft, and an
update has been delivered through the releases repository.

## M2: Library and presets

Goal: G2 and G3. The app knows where the DCPs live and what a programme looks like.

- **Library folder ("vault").** The user picks a folder once. Every DCP is a `.dcp` file in it, sub-folders
  allowed. The app only reads and writes these files, no database. An index (title, episode, date,
  upload state, RAI code) is rebuilt from the files and cached; deleting the cache loses nothing.
  Files changed outside the app (copied, synced, renamed) are picked up.
- **Browser.** A side panel with search, sort by date/programme, upload state at a glance. Open,
  rename, duplicate, move to a "done" folder, reveal in Explorer/Finder.
- **Naming.** New DCPs are saved straight into the library with a generated name
  (`<programme> <episode> <date>.dcp`), so "Save as" is rarely needed. Session restore stays.
- **Presets.** A preset is a `.dcp` without lines, kept in a `Presets` folder of the library: programme
  titles, Uorg, Matricola, Serie, Sede, Rete, duration, start time, contract, Responsabile RAI.
  "New from preset" and "Duplicate as next episode" (episode + 1, lines and upload state cleared).
  Because presets are ordinary files they travel with the folder.
- **Company profile** stays in the settings (supplier, compiler, legal representative).

Done when: a user finds last month's DCP by typing part of its title, duplicates it for the next
episode in two clicks, and the whole library can be copied to another PC as a folder.

Open questions: one shared library on a network drive for several users (locking, conflicts)?
Should uploaded DCPs become read-only?

Superseded in part on 2026-11-17: the app gets no library browser (see M3). The library folder,
automatic saving, file naming, presets, next episode and the v1 backups stay; the browser panel
(folder tree, search, sorting, move, rename, delete) is replaced by the drawer of the new design.

Status 2026-11-17: built: library folder, side panel with search, presets, next episode, duplicate,
rename, delete, automatic saving of library files. Covered by tests in Core; the panel itself has not
been clicked through yet. Localisation (German, Italian, English; 291 texts) is also built, ahead of M3;
the translations await a native check.

## M3: UI overhaul around the workflow

Goal: G4 and G5. Design decided on 2026-11-17 from our own sketches, after a first proposal came back far
too busy. The design brief describes it in full and marks what is still open:
https://claude.ai/artifact/ERPGZZFu1X9yVLFqvCZmdz

- **Window.** A collapsible left drawer, the main panel with a programme strip on top and the table of
  songs, and two large areas at the bottom: **Add** (step 2) and **Export or upload** (step 4). The areas
  never move; the current step's area has a highlighted background, and clicking an area switches step.
- **Drawer (step 1).** Collapsible lists *Presets* and *Recent*; at the bottom *Open library folder*
  (opens it in Explorer/Finder) and *Settings*. No library browser in the app.
- **Add area (step 2).** Drop songs or a timeline; *Add*, or a click anywhere on the area (Enter / Space
  when it has the focus), opens the file dialog; *How?* (the prominent button) opens the guide.
- **Guide.** Takes over the main panel; the drawer lists the ways to add songs (DaVinci timeline,
  musicmedia.it, Artlist with cue sheets, other providers), each with steps, conditions and
  troubleshooting. We write the texts.
- **Table (step 3).** Works like today; a question mark next to some headers opens the field help.
  No validation marks in the table.
- **Export page (step 4).** Sections *RAI* (Verify, Upload, show log; after an upload the DCP number and
  "Open at RAI") and *Documents* (one export per document, today the music list on Allegato B, each with
  "Edit template"). No save button on the page: Speichern is in the programme strip (superseded below: no
  automatic saving since 2026-11-18).
- **Look.** As plain as today's app: neutral greys, one accent colour for the active step and main
  buttons. No hint panels, warning lists, badges or colour-coded states.
- **Open, to be designed:** the programme strip and how it is edited; Recent (continue vs. new episode);
  creating and editing presets; where validation lives (one contained place); what Verify checks;
  editing document templates (Word today, PDF considered); opening a DCP found in Explorer.
- **Language (decided 2026-11-16).** The UI is available in German and Italian, English optional.
  Code, comments, logs, file formats and diagnostics stay English. RAI field names (Titolo, Autori ...)
  are not translated in any language, they are the form's own terms. See "Localisation" below.
- **Code already prepared:** `DcpWorkflow` (current step and totals), field help popups, the library core
  (`DcpLibrary`: scan, presets, next episode, naming). The current browser panel, issue list and menu
  layout will be replaced.

### Localisation (first step of M3, before the redesign multiplies the strings)

- One resource file per language (`Strings.resx`, `Strings.de.resx`, `Strings.it.resx`) with English as
  the base and the fallback. Views bind to keys, never to literal text.
- Messages that come from below the UI (validation, import, RAI client, export, file errors) are
  identified by a stable code plus arguments, and the app turns them into text. The validator already
  has codes; import messages and `RaiException` get them. English text stays in the log and diagnostics.
- Language setting: "System" (default), Deutsch, Italiano, English. Dates, times and numbers follow it.
- RAI's own messages ("L'edizione dell'opera 6 non è valida") are shown as RAI wrote them.
- The Word form is an Italian RAI document and stays Italian.
- Translations need a native check for both languages before release (owner: team).

Done when: a new user starts a DCP from a preset or a recent one, adds songs, uploads and exports
without opening the menu, and the design has been reviewed on a real screen.

Status 2026-11-18: built after the design canvas (https://claude.ai/code/artifact/b9d48760-2c3f-4156-a28f-3e07bc134044):
drawer with presets ("Vorlagen", renamed from "Sendungen" on 2026-11-18) and Zuletzt bearbeitet (+ starts the next episode, Puntata counted per Uorg/Matricola), programme
strip, songs table with field help, row menu and drag by the number, Add area with undo and "Warum?", guide (placeholder
texts), export page with one console, document export with a replaceable template, settings page, drop anywhere, .dcp
from the command line. The library browser, issue list and menu bar are gone. Prüfen checks locally only; it does not
sign in at RAI. Not checked yet: upload against RAI, dropping files from Explorer, a real screen review.

Changed on 2026-11-18 after the first review on a real screen: **no automatic saving.** A .dcp is written only by
Speichern, Speichern unter… and the Speichern of the prompt (Speichern / Nicht speichern / Abbrechen) before leaving a DCP
with unsaved changes. New DCPs are saved as `<library>/<Sendung>/<year>/<Sendung> <Puntata> <date>.dcp`, where Sendung is
the preset the DCP was started from (stored in the .dcp) or its Titolo italiano; existing files stay where they are and the
file name no longer follows the header. The recovery snapshot only restores after a crash or a killed process. The ▾ of
Speichern also has "Kopie als neuer DCP" and "Als Sendung speichern"; after an upload the RAI page opens by itself.
This supersedes "automatic saving" and "Session restore stays" in M2.

Second review round, 2026-11-18: **the one automatic save: right after an upload** that RAI saved, completely or in part,
the DCP with its new upload record is written to its file (a never-saved DCP to its library path, as Speichern would;
without a library the app asks with Speichern unter… and says so in the console), so a second upload continues the draft
or warns, and never creates a duplicate at RAI by accident (`SavePlan.AfterUpload`). The very first start (no settings
file) opens the guide on a new first chapter, *Welcome*, above the ways to add songs (`AppSettings.WelcomeShown`). The
whole Add area opens the file dialog. The table has faint column lines (grey 200, grey 300 in the header and below it),
and the ? of a header stays on the height of its first line when the name wraps.

## M4: Timeline import

Goal: G1. Drop the master timeline, get the finished music list. The approach is proven by the Ablauf
builder prototype (`TestingEquipment/AblaufBuilderSkill.md`, `TestingEquipment/TimelineAnalyzer`); this
milestone ports it into the app.

- **Input.** DaVinci Resolve `.drt` first, because it contains the nested timelines and the file paths.
  It is a zip of XML, undocumented and version-dependent, so the reader is tolerant and tested against
  several real exports. FCP 7 XML as the documented second format.
- **Time on air.** Per music file: sum of the seconds actually used by the master, recursing into nested
  timelines, overlaps counted once, fades included. Same results as the prototype, which was checked
  against Resolve. Decide the rounding rule for the DCP (the Ablauf rounds up to 5 s; RAI's rule is open).
- **Which clips are music.** Library music is kept, show jingles, original sound and voice-overs are not.
  Rules from the prototype: source recognised by a music source plug-in, file-name pattern, composer and
  publisher present. Everything left out is listed so nothing disappears silently.
- **Finding the files.** Paths in the timeline come from the editing machine. The app maps them to a
  project root the user picks (remembered per project) and searches below it by file name.
  A file that is not found still gives a line from its file name, marked incomplete.
- **Into the list.** Each piece becomes a line through the existing music sources (tags, cue sheets),
  with Durata = time on air and the file's full length kept for reference. Re-importing a newer
  timeline updates durations and shows what changed instead of duplicating lines.
- **Header.** The programme duration comes from the master timeline.

Done when: for two real episodes the imported list matches the hand-made DCP in pieces and seconds,
and missing files or unknown clips are reported clearly.

Later, not in this milestone: LLM help for clips that the rules cannot classify or match.

## M5: Staying alive when RAI changes

Goal: the second half of G6. These are the breakages that are likely, in order of probability.

- **Interactive login.** RAI's default sign-in page already asks for a second factor; the tool uses a
  password-only page. If that page changes, sign in inside the embedded browser and continue with its
  session. Chrome works with the form, which makes this feasible.
- **Form drift check.** Before an upload, compare the fields the app sends with the fields of the live
  form and stop with a clear message when they differ, instead of failing half-way.
- **Portal upgrade.** RAI's VPN appliance is due for an upgrade around January 2027 (unconfirmed);
  the login page markup may change. Keep the captured pages as tests and add new captures after it.
- **Emergency levers.** Portal address, sign-in path and realm can be overridden in the settings
  without a new build. Document this for support.
- **Questions for RAI** (owner: team): future of the password-only login, whether automated access is
  permitted, the official code tables.

Done when: a simulated login change and a simulated renamed form field both end in a clear message and a
working fallback, not in a stuck upload.

## M6: Distribution and platforms

- Code signing for Windows (needs a registered company for Azure signing, or a classic certificate).
- macOS build: Apple developer account, signing and notarisation, Keychain for the password, and a
  test of RAI's form in the Safari engine.
- Remove v1 (`DCP-Tool/`, `tests/DcpTool.Legacy`) once v2 has been in use without a fallback.

## Backlog (unscheduled)

- More music sources as plug-ins (Epidemic Sound, Audio Network, Universal ...), each with its own workflow.
- Optional enrichment for Sonoton-family tracks (composer IPI, ISRC) from the public Sonofind data.
- ODF or PDF export of the paper form.
- Author roles on the paper form (A / E / arr).
- Edit an already uploaded DCP at RAI from the app.

## Risks to keep in view

| Risk | Effect | Answer |
|---|---|---|
| RAI adds a second factor to the password-only login | Upload stops for everyone | M5 interactive login |
| RAI's form or portal changes | Upload fails | Captured-page tests, drift check, fast release path |
| No test system at RAI | Every test creates a real DCP | Fake-portal tests, dry-run preview, few and deliberate real tests |
| Automated access is not formally allowed | Product risk | Ask RAI before selling to others |
| `.drt` format changes with Resolve versions | Timeline import breaks | Tolerant reader, FCP 7 XML as fallback |
| Unsigned Windows builds | Install warning, blocked on managed PCs | M6 code signing |
| Free grid control is deprecated | UI dead end | Decide in M3 |
