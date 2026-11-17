using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace DcpTool.Core.Tests;

/// <summary>
/// The UI texts of src/DcpTool.App/Localisation: every key in all three languages with the same placeholders,
/// every key the app uses defined, and no key left unused. Reads the files of the source tree, so it needs no
/// reference to the app.
/// </summary>
public class ResourceTests
{
    private static readonly string AppFolder = Path.Combine(RepositoryRoot(), "src", "DcpTool.App");
    private static readonly string ResourceFolder = Path.Combine(AppFolder, "Localisation");

    // Starts from this source file too: with --artifacts-path the binaries live outside the repository
    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(here) ?? "" })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "DcpTool.sln")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new InvalidOperationException("DcpTool.sln not found above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, string> Read(string file)
    {
        var doc = XDocument.Load(Path.Combine(ResourceFolder, file));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = (string)data.Attribute("name")!;
            Assert.True(result.TryAdd(name, (string?)data.Element("value") ?? ""), $"{file}: key {name} is defined twice");
        }

        return result;
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)(?:[,:][^}]*)?\}").Select(m => m.Groups[1].Value).Distinct().Order();

    // Keys appear in the app as "Key" in C# and as {l:Tr Key} in XAML
    private static string AppSources() => string.Join("\n",
        Directory.EnumerateFiles(AppFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));

    [Fact]
    public void EveryLanguageHasTheSameKeysAndPlaceholders()
    {
        var english = Read("Strings.resx");
        Assert.NotEmpty(english);
        foreach (var file in new[] { "Strings.de.resx", "Strings.it.resx" })
        {
            var other = Read(file);
            Assert.Empty(english.Keys.Except(other.Keys).Select(k => $"{file} lacks {k}"));
            Assert.Empty(other.Keys.Except(english.Keys).Select(k => $"{file} has {k}, the English file has not"));
            foreach (var (key, text) in english)
            {
                Assert.True(other[key].Trim().Length > 0, $"{file}: {key} is empty");
                Assert.Equal(Placeholders(text), Placeholders(other[key]));
            }
        }
    }

    [Fact]
    public void EveryKeyIsUsedAndEveryUsedKeyExists()
    {
        var keys = Read("Strings.resx").Keys.ToHashSet(StringComparer.Ordinal);
        var sources = AppSources();

        var unused = keys.Where(k => !sources.Contains($"\"{k}\"", StringComparison.Ordinal)
                                     && !sources.Contains($"Tr {k}}}", StringComparison.Ordinal));
        Assert.Empty(unused);

        // Any string literal that looks like a key ("Status_Saved": one of the key prefixes, an underscore, a name)
        var prefixes = string.Join("|", keys.Select(k => k[..k.IndexOf('_')]).Distinct().Select(Regex.Escape));
        var used = Regex.Matches(sources, @"\{l:Tr (\w+)\}").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(sources, $@"""((?:{prefixes})_\w+)""").Select(m => m.Groups[1].Value));
        Assert.Empty(used.Where(k => !keys.Contains(k)).Distinct());
    }
}
