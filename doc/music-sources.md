# Music sources

`Helpers/DcpLineImporter.cs` turns audio files into `DcpLine`s. It supports two sources:

| Source | Recognised by | Where the DCP data comes from |
|---|---|---|
| **musicmedia.it** (Sonoton catalogue) | everything that isn't Artlist | the ID3 tags of the file |
| **Artlist** (artlist.io) | a matching cue sheet, or `Artlist` in the copyright tag (`TCOP` = "Licensed for video by Artlist.io") | title, artist and duration from the file; everything else from the Artlist cue sheet `.xlsx` |

Both use the same fixed values: Genere SIAE `ML`, Ruolo `SF`, Tipo generazione `9` (opera su disco). The duration always comes from the audio file (TagLib), never from a cue sheet.

## What the importer accepts

- Audio files (`.mp3`, `.wav`; anything TagLib can read works when dropped).
- Artlist cue sheets (any `.xlsx`). A file that isn't a cue sheet ends up in the error list.
- Folders: they stand for the `.mp3` / `.wav` files directly inside them.

All of these can be mixed in one drop or one selection in the "+" dialog.

## musicmedia.it / Sonoton

Unchanged from before the Artlist work:

| DCP field | From | Rule |
|---|---|---|
| Titolo | title tag | cut at the first `---` (Sonoton appends a description) |
| Autori | composer tag | split at `\|`. `"Cognome, Nome"` if there is a comma, otherwise the last word is the Cognome |
| Esecutori | artist tag | split at `\|`, joined with `, `. Falls back to the Autori |
| Marca | — | fixed `Sonoton` |
| Sigla / numero | album tag | the part before the first `-` (`"SCDV 579 - Dark Worlds"` → `"SCDV 579 "`) |
| Edizione | — | empty. The Word export prints the Marca in the Edizione column instead |

The importer used to enrich lines through the Sonofind API (`SonofindInterface`, looked up by the CD code at the start of the file name). That call has been disabled since the tags carry all the data.

## Artlist

### What Artlist provides

**MP3 tags** contain only title, artist, album (= song title for singles), year, `TCOP` "Licensed for video by Artlist.io", genre "Unknown" and some Pro Tools leftovers. **No composer, publisher, ISRC or catalogue number.** The artist is a project name and usually *not* the composer ("BalloonPlanet" was written by Oliver Kobák).

**The cue sheet** (Artlist → Downloads → Song License → Cue sheet, file name `cue_sheet_<title>_<songId>.xlsx`) is one sheet of sections, with the section name in column A and headers in the same row:

| Section | Columns |
|---|---|
| Album Info | Label · Album Title · Album UPC |
| Song Details | Song Title · Primary Artist/s (Remixer) · Featured Artist/s · ISRC · ISWC · Track Duration |
| Publisher Info | Publisher Name · Publisher Pro Affiliation · Publisher IPI · Publisher Split |
| Writers (one row per writer) | Full Legal Name · Role (C/A/CA/AR/…) · PRO Affiliation · IPI Number · Split |
| Public Domain Disclaimer | Yes / No |

Example values from the three songs in `Artlist Example/`:

| Song | Artist | Writer (role, society) | Publisher | Label | UPC |
|---|---|---|---|---|---|
| lose my mind | Flint | Ofri Flint (CA, BMI) | Artlist Original Music (BMI) | Artlist Original | 194492129483 |
| Steady Hearts, Stronger Roads | BalloonPlanet | Oliver Kobák (C, BMI) | Artlist Original Music (BMI) | Artlist Original | 194492066535 |
| Up Until Sunrise | Damon Power | Youval Ziso (C, ASCAP) | Artlist Records Original Tracks (ASCAP) | Artlist Original | 194492126796 |

Quirks the reader handles:

- **Strict Excel libraries can't open the file.** `styles.xml` has colours like `#A0A0A0`, which aren't valid aRGB (openpyxl refuses it; ClosedXML/EPPlus probably do too). `ArtlistCueSheetReader` opens the `.xlsx` as a zip and reads only `sharedStrings.xml` and the first sheet, with no NuGet dependency.
- Headers are looked up **by name** (case-insensitive substring), so reordered columns or extra writer rows don't matter.
- `N/A` and empty cells become `null`.
- The track duration is rounded to the second (`5:17` for a 5:16.9 file). It is only used to tell versions apart (see below).
- Data glitches exist (one sheet lists the publisher split as `200%`). They don't affect the DCP.
- **The file name doesn't contain the song ID**, and the cue sheet doesn't name the version. `cue_sheet_Steady_Hearts,_Stronger_Roads_-_No_Backing_Vocals__135440.xlsx` contains the title "Steady Hearts, Stronger Roads", the same as the MP3's title tag.

### Finding the cue sheet for an audio file

For each audio file, the importer considers:

1. the cue sheets that were dropped or selected together with it, and
2. every `cue_sheet_*.xlsx` in the audio file's folder.

It then matches:

1. **By tags**: song title equal to the title tag, and primary artist equal to the artist tag (an empty artist tag matches any). Both are compared after Unicode normalisation, lower-casing and removing everything except letters and digits.
2. **By file name** when the tags don't match (e.g. a WAV without tags): both the song title and the primary artist must appear in the file name. Artlist names downloads `<Artist> - <Title>[ - <Version>].mp3`.
3. If several cue sheets match (versions of the same song share title and artist), the one whose **duration is closest** to the file's wins.

**Cue sheets added later.** If cue sheets are dropped without audio files, the importer applies each one to the lines already in the selected DCP that have no Autori and whose Titolo and Esecutori match. So the usual fix for a missing cue sheet is to download it and drop it onto the list.

### Field mapping

| DCP field | With cue sheet | Without cue sheet |
|---|---|---|
| Titolo | cue sheet *Song Title*, upper case | title tag, upper case |
| Autori | every *Writers* row, *Full Legal Name* (last word = Cognome) | **empty**, a warning is shown and the upload is refused |
| Esecutori | *Primary Artist/s* + *Featured Artist/s* | artist tag |
| Durata | audio file | audio file |
| Marca | *Label* (`Artlist Original`, uploaded as `ARTLIST ORIGINAL`) | `Artlist` |
| Sigla / numero | *Album UPC*, else the song ID from the file name | empty |
| Edizione | *Publisher Name* (grid and Word export only, never uploaded: RAI rejected it) | empty |

Not used (no field for them in the RAI form): ISRC, ISWC (always empty so far), IPI numbers, PRO affiliations, splits, the writer role. `ArtlistWriter.DcpRole` already maps the role to the paper form's codes (C/A/CA → `A`, AR → `arr`, AD/TR/SR → `E`, other → `Y`) in case the rewrite needs it.

Titles are upper-cased to match the Sonoton data, which arrives in upper case.

### Validation

Upload refuses any DCP where a line has no Autori ("Autori missing on: …"). This applies to every source. Before this check, a line without authors was uploaded as one empty author (`1#%#`).

### Not tested yet

- Cue sheets with **several writers** or with **featured artists**. None of the examples has them. The reader handles extra writer rows by design.
- Which Marca RAI accepts for Artlist. The first real upload (November 2026) rejected both `ARTLIST ORIGINAL` as Marca and `ARTLIST ORIGINAL MUSIC` as Edizione, so Edizione is no longer uploaded. See [rewrite-notes.md](rewrite-notes.md#open-questions-for-rais-dcp-office).
- Artlist **WAV** downloads. Their metadata is unknown. Without tags, matching falls back to the file name.

### Licensing notes (from the Artlist analysis, October 2026)

- The Artlist Max Pro license covers TV broadcast of client work for companies with up to 50 employees. Broadcasters themselves need Max Business or Enterprise.
- The license doesn't pay collecting societies. In Italy RAI pays SIAE, and SIAE splits the money using the DCP, so correct writer names matter. Artlist's writers are BMI/ASCAP members, paid through SIAE's agreements with those societies.
- **Cue sheets can only be downloaded while the subscription is active.** Download and archive the cue sheet for every song used, together with the project.
- Get the license points confirmed by Artlist in writing before selling a product built on them.

## Adding another source

1. Decide how the source is recognised (a tag value, a sidecar file, the file name).
2. Write a `Create<Source>Line` method in `DcpLineImporter` and add it to the decision in `ReadDcpLineFromFile`.
3. If the source has sidecar files like Artlist's cue sheets, load them in `Import` and apply them in `ApplyCueSheets` style, so they can also be added after the audio.
4. Never put the artist into Autori unless the source guarantees the artist is the composer.

For the rewrite this should become an interface (`IMusicSource`) with one implementation per library. See [rewrite-notes.md](rewrite-notes.md).
