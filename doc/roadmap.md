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
| G2 | A library of DCPs: browse, find and duplicate old ones. Plain `.dcp` files in a folder the user chooses, like an Obsidian vault | M2 |
| G3 | Presets: the programme (Sendung) information is entered once and reused | M2 |
| G4 | UI overhaul | M3 |
| G5 | The UI follows the workflow. No trips to the top menu | M3 |
| G6 | Clear errors, communication and verification, above all for the RAI upload | M1, M5 |
| G7 | Fixes reach users within minutes | M1 |

## Workflow the app is built around

1. **Start**: pick a programme preset or duplicate an earlier DCP. The header is filled.
2. **Music**: drop the master timeline (M4), or audio files and cue sheets (today).
3. **Review**: the list shows what is missing or doubtful, line by line.
4. **Publish**: upload to RAI and/or export the Word form.
5. **Verify**: the app shows what RAI saved and keeps the DCP in the library with its RAI code.

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

Status 2026-11-17: built: library folder, side panel with search, presets, next episode, duplicate,
rename, delete, automatic saving of library files. Covered by tests in Core; the panel itself has not
been clicked through yet. Localisation (German, Italian, English; 291 texts) is also built, ahead of M3;
the translations await a native check.

## M3: UI overhaul around the workflow

Goal: G4 and G5. Start with a short design concept (layout, colours, the five workflow steps on screen),
agree on it, then build.

- **Layout.** Library on the left, the DCP in the middle (header as a compact card, lines below),
  a status and issue area that is always visible. The primary action of the current step is a button
  on the page ("Add music", "Upload to RAI", "Export"), not a menu entry. The menu remains as a fallback
  and for the macOS menu bar.
- **Lines.** A proper authors editor (Nome / Cognome rows), inline validation marks with the reason,
  source and cue-sheet state per line, keyboard-friendly editing, undo.
- **Upload panel.** Login state, progress per block, result per line, link to the draft at RAI, the
  "already uploaded" guard. No modal dialogs for results.
- **Empty and first-run states.** First start asks for the library folder, the company profile and the
  RAI login in one guided screen.
- **Grid control.** Avalonia's free DataGrid only gets bug fixes. Decide during the concept whether
  the lines become a custom list (cards or rows built from standard controls) instead of a grid.
- **Language (decided 2026-11-16).** The UI is available in German and Italian, English optional.
  Code, comments, logs, file formats and diagnostics stay English. RAI field names (Titolo, Autori ...)
  are not translated in any language, they are the form's own terms. See "Localisation" below.

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

Done when: a new user creates, fills, uploads and exports a DCP without opening the menu, and the
design has been reviewed on a real screen.

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
