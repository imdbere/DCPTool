using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DCP_Tool.Models;

namespace DCP_Tool.Helpers
{
    public class ImportResult
    {
        public List<DcpLine> Lines { get; } = new List<DcpLine>();

        // Cue sheets that were added explicitly but belong to none of the added audio files
        public List<ArtlistCueSheet> UnusedCueSheets { get; } = new List<ArtlistCueSheet>();

        // Artlist audio files for which no cue sheet was found, so their lines have no Autori
        public List<string> MissingCueSheets { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Turns audio files into DCP lines.
    /// musicmedia.it (Sonoton) files carry everything in their ID3 tags. Artlist files don't name the
    /// composers, so their data comes from the Artlist cue sheet (cue_sheet_*.xlsx), which is either
    /// added together with the audio files or lies in the same folder.
    /// </summary>
    public static class DcpLineImporter
    {
        private const string CueSheetPattern = "cue_sheet_*.xlsx";
        private static readonly string[] AudioExtensions = { ".mp3", ".wav" };

        public static ImportResult Import(IEnumerable<string> paths)
        {
            var result = new ImportResult();

            // A dropped folder stands for the audio files in it
            var files = paths
                .SelectMany<string, string>(p => Directory.Exists(p)
                    ? ListFiles(p, "*", result.Errors).Where(IsAudioFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    : new[] { p })
                .ToList();
            var audioFiles = files.Where(f => !IsCueSheetFile(f)).ToList();

            var addedCueSheets = LoadCueSheets(files.Where(IsCueSheetFile), result.Errors);
            var cueSheetsNextToAudio = audioFiles
                .Select(Path.GetDirectoryName)
                .Where(dir => !string.IsNullOrEmpty(dir))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(dir => ListFiles(dir, CueSheetPattern, result.Errors))
                .Except(files.Where(IsCueSheetFile), StringComparer.OrdinalIgnoreCase);
            var cueSheets = addedCueSheets
                .Concat(LoadCueSheets(cueSheetsNextToAudio, result.Errors))
                .ToList();

            var usedCueSheets = new HashSet<ArtlistCueSheet>(ReferenceEqualityComparer.Instance);
            foreach (var file in audioFiles)
            {
                try
                {
                    result.Lines.Add(ReadDcpLineFromFile(file, cueSheets, out var isArtlist, out var cueSheet));

                    if (cueSheet != null)
                    {
                        usedCueSheets.Add(cueSheet);
                    }
                    else if (isArtlist)
                    {
                        result.MissingCueSheets.Add(Path.GetFileName(file));
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }

            result.UnusedCueSheets.AddRange(addedCueSheets.Where(c => !usedCueSheets.Contains(c)));
            return result;
        }

        /// <summary>
        /// Completes lines that were added before their cue sheet: every line without Autori whose title
        /// and performers match a cue sheet gets that cue sheet's data. Returns the cue sheets that matched no line.
        /// </summary>
        public static List<ArtlistCueSheet> ApplyCueSheets(IEnumerable<DcpLine> lines, IEnumerable<ArtlistCueSheet> cueSheets)
        {
            var unused = new List<ArtlistCueSheet>();
            foreach (var cueSheet in cueSheets)
            {
                var matching = lines
                    .Where(l => !l.HasAutori())
                    .Where(l => cueSheet.Matches(l.Titolo, string.IsNullOrWhiteSpace(l.Esecutori) ? null : l.Esecutori))
                    .ToList();
                foreach (var line in matching)
                {
                    ApplyCueSheet(line, cueSheet);
                }

                if (matching.Count == 0)
                {
                    unused.Add(cueSheet);
                }
            }

            return unused;
        }

        private static DcpLine ReadDcpLineFromFile(string filename, List<ArtlistCueSheet> cueSheets, out bool isArtlist, out ArtlistCueSheet cueSheet)
        {
            using var fileStream = File.OpenRead(filename);

            var tagFile = TagLib.File.Create(new TagLib.StreamFileAbstraction(filename, fileStream, fileStream));
            // Files without an ID3v2 tag (e.g. wav) fall back to whatever tags TagLib found
            var tags = tagFile.GetTag(TagLib.TagTypes.Id3v2) ?? tagFile.Tag;
            var durata = tagFile.Properties?.Duration ?? TimeSpan.Zero;

            var performers = string.Join(", ", SplitTagValues(tags?.Performers));
            cueSheet = FindCueSheet(cueSheets, tags?.Title, performers.Length > 0 ? performers : null, filename, durata);
            // Artlist writes "Licensed for video by Artlist.io" into the copyright tag
            isArtlist = cueSheet != null || (tags?.Copyright?.Contains("artlist", StringComparison.OrdinalIgnoreCase) ?? false);

            return isArtlist
                ? CreateArtlistLine(tags, durata, cueSheet)
                : CreateSonotonLine(tags, durata);
        }

        // Sonoton (musicmedia.it) files: composers/performers are "|"-separated,
        // the title is cut at "---" and the album starts with the CD code ("SCDV 579 - ...")
        // Data used to be enriched through SonofindInterface, now it all comes from the tags.
        private static DcpLine CreateSonotonLine(TagLib.Tag tags, TimeSpan durata)
        {
            // Missing tags are left blank so they can be filled in manually
            var autori = SplitTagValues(tags?.Composers).Select(ParseAutore).ToArray();
            var esecutori = SplitTagValues(tags?.Performers).ToArray();
            if (esecutori.Length == 0)
            {
                esecutori = autori.Select(a => a.ToString().Trim()).ToArray();
            }

            var title = tags?.Title ?? "";

            return new DcpLine()
            {
                // null instead of an empty array, AutoriString can't handle empty arrays
                Autori = autori.Length > 0 ? autori : null,
                Marca = "Sonoton",
                Esecutori = string.Join(", ", esecutori),
                Titolo = title.Contains("---") ? title.Split("---")[0] : title,
                Durata = durata,
                SiglaNum = (tags?.Album ?? "").Split('-')[0],
                TipoGenerazione = TipoGenerazione.OperaSuDisco,
                Ruolo = Ruolo.SF,
                Gensiae = GenereSiae.ML
            };
        }

        // Artlist files only have title, artist and album in their tags. The album is not a catalogue
        // code and the artist is not the composer, so the rest comes from the cue sheet.
        private static DcpLine CreateArtlistLine(TagLib.Tag tags, TimeSpan durata, ArtlistCueSheet cueSheet)
        {
            var line = new DcpLine()
            {
                Titolo = (tags?.Title ?? "").ToUpper(CultureInfo.InvariantCulture),
                Esecutori = string.Join(", ", SplitTagValues(tags?.Performers)),
                Marca = "Artlist",
                // Always from the file, the cue sheet rounds it to the second
                Durata = durata,
                TipoGenerazione = TipoGenerazione.OperaSuDisco,
                Ruolo = Ruolo.SF,
                Gensiae = GenereSiae.ML
            };

            if (cueSheet != null)
            {
                ApplyCueSheet(line, cueSheet);
            }

            return line;
        }

        private static void ApplyCueSheet(DcpLine line, ArtlistCueSheet cueSheet)
        {
            var autori = cueSheet.Writers.Select(w => new Autore(w.FullName)).ToArray();

            // null instead of an empty array, AutoriString can't handle empty arrays
            line.Autori = autori.Length > 0 ? autori : null;
            line.Titolo = cueSheet.SongTitle.ToUpper(CultureInfo.InvariantCulture);
            if (cueSheet.Performers.Length > 0)
            {
                line.Esecutori = cueSheet.Performers;
            }
            // RAI only accepts Marca values from its own list: "ARTLIST" is on it, the cue sheet's label ("Artlist Original") is not
            line.Marca = "Artlist";
            line.SiglaNum = cueSheet.AlbumUpc ?? cueSheet.SongId ?? "";
            line.Edizione = cueSheet.PublisherName ?? "";
        }

        private static ArtlistCueSheet FindCueSheet(List<ArtlistCueSheet> cueSheets, string title, string performers, string filename, TimeSpan durata)
        {
            var matches = cueSheets.Where(c => c.Matches(title, performers)).ToList();
            if (matches.Count == 0)
            {
                matches = cueSheets.Where(c => c.MatchesFileName(filename)).ToList();
            }

            // Versions of a song (e.g. "No Backing Vocals") share title and artist. Prefer the closest duration.
            return matches
                .OrderBy(c => c.Duration.HasValue ? Math.Abs((c.Duration.Value - durata).TotalSeconds) : double.MaxValue)
                .FirstOrDefault();
        }

        private static List<ArtlistCueSheet> LoadCueSheets(IEnumerable<string> files, List<string> errors)
        {
            var cueSheets = new List<ArtlistCueSheet>();
            foreach (var file in files)
            {
                try
                {
                    cueSheets.Add(ArtlistCueSheetReader.Read(file));
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }

            return cueSheets;
        }

        private static string[] ListFiles(string dir, string pattern, List<string> errors)
        {
            try
            {
                return Directory.GetFiles(dir, pattern);
            }
            catch (Exception ex)
            {
                errors.Add($"{dir}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static bool IsCueSheetFile(string file)
        {
            return string.Equals(Path.GetExtension(file), ".xlsx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAudioFile(string file)
        {
            return AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SplitTagValues(string[] values)
        {
            return (values ?? Array.Empty<string>())
                .Where(v => v != null)
                .SelectMany(v => v.Split("|"))
                .Select(v => v.Trim())
                .Where(v => v.Length > 0);
        }

        private static Autore ParseAutore(string c)
        {
            if (c.Contains(","))
            {
                // "Cognome, Nome"
                var split = c.Split(",");
                return new Autore(split[1].Trim(), split[0].Trim());
            }

            return new Autore(c);
        }
    }
}
