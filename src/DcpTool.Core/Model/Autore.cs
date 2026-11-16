namespace DcpTool.Core.Model;

/// <summary>An author (composer or lyricist). RAI wants surname and first name separately.</summary>
public sealed record Autore(string Nome, string Cognome)
{
    private static readonly char[] ListSeparators = [';', '|', '\n', '\r'];

    public bool IsEmpty => string.IsNullOrWhiteSpace(Nome) && string.IsNullOrWhiteSpace(Cognome);

    /// <summary><c>Nome Cognome</c>.</summary>
    public string FullName => $"{Nome} {Cognome}".Trim();

    public override string ToString() => FullName;

    /// <summary>
    /// "First [Middle] Last": the last word is the Cognome, everything before it the Nome.
    /// A single word becomes the Nome (as in v1, so uploads stay identical).
    /// </summary>
    public static Autore FromFullName(string fullName)
    {
        var words = (fullName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return new Autore("", "");
        }

        return words.Length == 1
            ? new Autore(words[0], "")
            : new Autore(string.Join(' ', words[..^1]), words[^1]);
    }

    /// <summary>One author, written either as <c>Cognome, Nome</c> or as <c>Nome Cognome</c>.</summary>
    public static Autore Parse(string text)
    {
        text ??= "";
        var comma = text.IndexOf(',');
        if (comma < 0)
        {
            return FromFullName(text);
        }

        var cognome = text[..comma].Trim();
        var rest = text[(comma + 1)..];
        var nextComma = rest.IndexOf(',');
        var nome = (nextComma < 0 ? rest : rest[..nextComma]).Trim();
        return new Autore(nome, cognome);
    }

    /// <summary>
    /// Several authors in one text. Authors are separated by <c>;</c> (or <c>|</c> or a line break), and each
    /// may be <c>Nome Cognome</c> or <c>Cognome, Nome</c>.
    /// Text without any of these separators is read the v1 way, as a comma-separated list of
    /// <c>Nome Cognome</c>, except for the one case that is clearly a single <c>Cognome, Nome</c>:
    /// two parts with a one-word surname ("Smith, John").
    /// </summary>
    public static List<Autore> ParseList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (text.IndexOfAny(ListSeparators) >= 0)
        {
            return text.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Parse)
                .Where(a => !a.IsEmpty)
                .ToList();
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && !parts[0].Contains(' '))
        {
            return [new Autore(parts[1], parts[0])];
        }

        return parts.Select(FromFullName).Where(a => !a.IsEmpty).ToList();
    }

    /// <summary>The form <see cref="ParseList"/> reads back without loss: <c>Nome Cognome; Nome Cognome</c>.</summary>
    public static string FormatList(IEnumerable<Autore> autori, string separator = "; ") =>
        string.Join(separator, autori.Where(a => !a.IsEmpty).Select(a => a.FullName));
}
