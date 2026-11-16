# DCP Tool documentation

DCP Tool fills in the RAI "DCP" (*Distinta Consuntiva Programmi*), the list of every piece of music used in a programme delivered to RAI. RAI passes the DCP to SIAE, which uses it to pay the composers. Entering a DCP by hand in RAI's intranet form is slow. The tool builds it from the audio files used in the edit, then uploads it to the RAI form or exports it as a Word document.

These notes describe how the tool works today (October 2026, version 0.8.x). They are meant as the basis for a rewrite into a product other production companies can buy.

| Document | Contents |
|---|---|
| [architecture.md](architecture.md) | Components, data flow, data model, `.dcp` file format, UI, build and CI |
| [music-sources.md](music-sources.md) | How audio files become DCP lines: musicmedia.it / Sonoton tags, Artlist cue sheets, matching rules, field mapping |
| [rai-dcp-upload.md](rai-dcp-upload.md) | How the upload to RAI's intranet works (VPN login, ASP.NET form, field encoding) and the Word export |
| [rewrite-notes.md](rewrite-notes.md) | Known bugs, customer-specific hard-coding, open questions for RAI, and recommendations for the rewrite |

## Glossary

The model uses the Italian names of the RAI form fields. Keep them, because they map 1:1 to the form.

| Term | Meaning |
|---|---|
| DCP | *Distinta Consuntiva Programmi*: one programme (episode) with its list of music |
| Titolo italiano / originale | Programme title in Italian / original language |
| Puntata | Episode number |
| Serie | Series number (sent as three digits, `001`) |
| Uorg, Matricola | RAI internal production codes of the programme |
| Sede | RAI regional office. RAI Südtirol is Bolzano: 16 German, 14 Italian, 15 Ladin programmes |
| Rete / Struttura | Channel (`TV1`..`TV4`) |
| Data trasmissione / registrazione | Broadcast date / recording date |
| Ora inizio / fine | Broadcast start / end time |
| Opera | One piece of music, i.e. one DCP line |
| Titolo | Title of the piece |
| Autori | Authors (composers, lyricists). Sent as Cognome (surname) + Nome (first name) |
| Esecutori | Performers |
| Durata | Duration of the piece as used |
| Genere SIAE | SIAE genre. Always `ML` (presumably *musica leggera*) in this tool |
| Ruolo | How the music is used. The code knows `PP`, `SF`, `SI` (presumably *primo piano*, *sottofondo* = background, *sigla* = theme) and always uses `SF` |
| Tipo generazione | `9` = recorded work (*opera su disco*), `1` = live |
| Marca | Label / library. Must be on RAI's list (`SONOTON`, `ARTLIST`) |
| Sigla / numero | Catalogue code of the recording (Sonoton CD code `SCDV 579`, Artlist album UPC) |
| Edizione | Music publisher |
| Proprietà | Ownership code. Never filled by the tool |
