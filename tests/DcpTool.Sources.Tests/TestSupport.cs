using System.IO.Compression;
using System.Text;
using DcpTool.Core.Import;
using Xunit;

namespace DcpTool.Sources.Tests;

internal static class TestSupport
{
    // Starts from this source file too: with --artifacts-path the binaries live outside the repo.
    public static string SampleDir([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Samples("Artlist Example", here);

    /// <summary>
    /// Licensed music can't live in the repository. The samples are looked for in "local/" inside the
    /// repository and in "TestingEquipment/" next to it; tests that need them are skipped without them.
    /// </summary>
    public static string Samples(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var starts = new[] { AppContext.BaseDirectory, Path.GetDirectoryName(here) ?? "" };
        foreach (var dir in starts.Select(s => new DirectoryInfo(s)).SelectMany(Ancestors))
        {
            if (File.Exists(Path.Combine(dir.FullName, "DcpTool.sln")))
            {
                var candidates = new[]
                {
                    Path.Combine(dir.FullName, "local", name),
                    Path.Combine(dir.Parent?.FullName ?? dir.FullName, "TestingEquipment", name),
                };
                if (candidates.FirstOrDefault(Directory.Exists) is { } samples)
                {
                    return samples;
                }
            }
        }
        Assert.Skip($"The sample folder \"{name}\" is not available");
        return "";
    }

    private static IEnumerable<DirectoryInfo> Ancestors(DirectoryInfo start)
    {
        for (DirectoryInfo? d = start; d is not null; d = d.Parent)
        {
            yield return d;
        }
    }

    public static string[] SampleMp3s() =>
        Directory.GetFiles(SampleDir(), "*.mp3").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dcp-sources-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static AudioFile Audio(string path, string? title = null, string[]? performers = null, string[]? composers = null,
        string? album = null, string? copyright = null, double seconds = 180) =>
        new(path, new AudioTags
        {
            Title = title,
            Performers = performers ?? [],
            Composers = composers ?? [],
            Album = album,
            Copyright = copyright,
        }, TimeSpan.FromSeconds(seconds));

    /// <summary>Writes a minimal cue sheet in Artlist's layout (inline strings, no styles).</summary>
    public static string WriteCueSheet(string dir, string fileName, string title, string artist, string writer = "Jane Doe",
        string duration = "3:00", string label = "Artlist Original", string upc = "123456", string publisher = "Pub Inc")
    {
        var rows = new List<string[]>
        {
            new[] { "Album Info", "Label", "Album Title", "Album UPC" },
            new[] { "", label, title, upc },
            new[] { "Song Details", "Song Title", "Primary Artist/s (Remixer)", "Featured Artist/s", "ISRC", "ISWC", "Track Duration" },
            new[] { "", title, artist, "N/A", "ISRC1", "N/A", duration },
            new[] { "Publisher Info", "Publisher Name", "Publisher Pro Affiliation" },
            new[] { "", publisher, "BMI" },
            new[] { "Writers", "Full Legal Name", "Role", "PRO Affiliation" },
            new[] { "", writer, "C", "BMI" },
        };

        var sheet = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c].Length > 0)
                {
                    var text = System.Security.SecurityElement.Escape(rows[r][c]);
                    sheet.Append($"<c r=\"{(char)('A' + c)}{r + 1}\" t=\"inlineStr\"><is><t>{text}</t></is></c>");
                }
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        var path = Path.Combine(dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        Add("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheets><sheet name=\"s\" sheetId=\"1\"/></sheets></workbook>");
        Add("xl/worksheets/sheet1.xml", sheet.ToString());
        return path;
    }

    /// <summary>Audio reader for tests: returns what was registered for a path, no real files needed.</summary>
    public sealed class FakeReader : IAudioFileReader
    {
        public Dictionary<string, AudioFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsAudioFile(string path) => path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
        public AudioFile Read(string path) =>
            Files.TryGetValue(path, out var f) ? f : throw new ImportException("not audio");
    }
}
