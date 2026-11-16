using System.Text.Json;

namespace DcpTool.Core.Model;

/// <summary>
/// The labels (Marca) RAI's DCP form knows: about 36,000 names collected from the form's own label
/// search in November 2026 (<c>marche.json</c>, embedded). RAI's list changes over time, so a name
/// missing here is a reason to warn, never to refuse.
/// </summary>
public static class RaiMarche
{
    private static readonly Lazy<HashSet<string>> Names = new(Load);

    public static int Count => Names.Value.Count;

    /// <summary>Compared like the upload sends it: trimmed, ignoring case.</summary>
    public static bool Contains(string? marca) =>
        !string.IsNullOrWhiteSpace(marca) && Names.Value.Contains(marca.Trim());

    /// <summary>Names starting with the text, for suggestions while typing.</summary>
    public static IEnumerable<string> StartingWith(string text, int max = 20)
    {
        var prefix = text.Trim();
        return prefix.Length == 0
            ? []
            : Names.Value
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(max);
    }

    private static HashSet<string> Load()
    {
        using var stream = typeof(RaiMarche).Assembly.GetManifestResourceStream("DcpTool.Core.marche.json")
            ?? throw new InvalidOperationException("marche.json is not embedded.");
        var names = JsonSerializer.Deserialize<string[]>(stream) ?? [];
        return new HashSet<string>(names.Select(n => n.Trim()).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
    }
}
