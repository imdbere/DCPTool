using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace DcpTool.Rai;

internal sealed record Exchange(
    DateTimeOffset At,
    string Method,
    string Url,
    IReadOnlyList<string> RequestHeaders,
    IReadOnlyList<string>? RequestForm,
    int Status,
    IReadOnlyList<string> ResponseHeaders,
    string ResponseBody);

/// <summary>Removes everything from logged traffic that must not leave the user's machine.</summary>
internal sealed partial class Redactor
{
    private const string Mask = "[REDACTED]";
    private readonly List<string> _secrets = [];

    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password#2", "username", "FormDataStr", "DSIDFormDataStr", "postfixSID", "xsauth", "DSID",
    };

    private static readonly HashSet<string> LongFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "__VIEWSTATE", "__VIEWSTATEGENERATOR", "__EVENTVALIDATION",
    };

    /// <summary>A value (password, user name, cookie value) to remove wherever it shows up.</summary>
    public void AddSecret(string? value)
    {
        // A one-letter secret would shred every text; the field-based rules still cover those.
        if (value is { Length: >= 3 } && !_secrets.Contains(value, StringComparer.Ordinal))
        {
            _secrets.Add(value);
        }
    }

    public string Text(string text)
    {
        foreach (var s in _secrets)
        {
            text = text.Replace(s, Mask, StringComparison.Ordinal);
        }

        return TokenParam().Replace(text, "$1=" + Mask);
    }

    public string Field(string key, string value)
    {
        if (IsSensitive(key))
        {
            return Mask;
        }

        return LongFields.Contains(key) ? Truncate(value) : Text(value);
    }

    public string Header(string name, string value)
    {
        if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
        {
            return Mask;
        }

        if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
        {
            var eq = value.IndexOf('=');
            return (eq > 0 ? value[..eq] : "cookie") + "=" + Mask;
        }

        return Text(value);
    }

    public string Html(string html)
    {
        html = InputTag().Replace(html, m =>
        {
            var tag = m.Value;
            var names = AttrName().Matches(tag).Select(x => x.Groups[1].Value).ToList();
            var sensitive = names.Any(IsSensitive);
            var long_ = names.Any(LongFields.Contains);
            if (!sensitive && !long_)
            {
                return tag;
            }

            return ValueAttr().Replace(tag, v => v.Groups[1].Value + v.Groups[2].Value
                + (sensitive ? Mask : Truncate(v.Groups[3].Value)) + v.Groups[2].Value);
        });
        return Text(People(html));
    }

    /// <summary>
    /// RAI's pages name people: the logged-in user ("Utente: ..."), the compiler, and a drop-down of
    /// RAI staff with their personnel numbers. None of it is needed to debug the form.
    /// </summary>
    internal static string People(string html)
    {
        html = System.Text.RegularExpressions.Regex.Replace(
            html, @"(<option[^>]*value="")P\d+(""[^>]*>)[^<]*(</option>)", "$1P000000$2[person]$3");
        html = System.Text.RegularExpressions.Regex.Replace(
            html, @"(<span[^>]*id=""[^""]*(?:Wellcome|Welcome|Compilatore|Utente)[^""]*""[^>]*>)[^<]*(</span>)", "$1[person]$2",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return html;
    }

    private static bool IsSensitive(string name) =>
        SensitiveFields.Contains(name) || name.StartsWith("postfixSID", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("password", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string v) => v.Length <= 40 ? v : v[..40] + $"...[{v.Length - 40} more characters]";

    [GeneratedRegex(@"\b(DSID|xsauth|FormDataStr|DSIDFormDataStr|postfixSID)=[^&\s""'<>;]*", RegexOptions.IgnoreCase)]
    private static partial Regex TokenParam();

    [GeneratedRegex(@"<input\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InputTag();

    [GeneratedRegex(@"\b(?:name|id)\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex AttrName();

    [GeneratedRegex(@"(\bvalue\s*=\s*)([""'])(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ValueAttr();
}

/// <summary>The exchanges of one session, bounded so a long-running app doesn't grow without limit.</summary>
internal sealed class DiagnosticsLog
{
    private const int MaxExchanges = 40;
    private const int MaxBodyChars = 2_000_000;
    private readonly Queue<Exchange> _items = new();
    private readonly object _lock = new();

    public void Add(Exchange e)
    {
        lock (_lock)
        {
            _items.Enqueue(e);
            while (_items.Count > MaxExchanges)
            {
                _items.Dequeue();
            }
        }
    }

    public static string Clip(string body) => body.Length <= MaxBodyChars ? body : body[..MaxBodyChars] + "\n[cut]";

    public byte[] BuildZip(RaiPortalOptions options)
    {
        Exchange[] items;
        lock (_lock)
        {
            items = [.. _items];
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var summary = new StringBuilder();
            summary.AppendLine("DCP Tool, RAI portal diagnostics");
            summary.AppendLine($"Created: {DateTimeOffset.Now:O}");
            summary.AppendLine($"Portal: {options.BaseUrl}, login {options.LoginPath}, realm {options.Realm}, batch size {options.BatchSize}");
            summary.AppendLine("Passwords, user names, cookies and session tokens were removed; __VIEWSTATE values are shortened.");
            summary.AppendLine();

            for (var i = 0; i < items.Length; i++)
            {
                var e = items[i];
                var n = $"{i + 1:00}";
                summary.AppendLine($"{n}  {e.At:HH:mm:ss}  {e.Method} {e.Url} -> {e.Status}");

                var sb = new StringBuilder();
                sb.AppendLine($"{e.Method} {e.Url}");
                foreach (var h in e.RequestHeaders)
                {
                    sb.AppendLine(h);
                }

                if (e.RequestForm is not null)
                {
                    sb.AppendLine().AppendLine("Form fields:");
                    foreach (var f in e.RequestForm)
                    {
                        sb.AppendLine("  " + f);
                    }
                }

                sb.AppendLine().AppendLine($"Response: {e.Status}");
                foreach (var h in e.ResponseHeaders)
                {
                    sb.AppendLine(h);
                }

                Write(zip, $"{n}-{e.Method}-exchange.txt", sb.ToString());
                Write(zip, $"{n}-{e.Method}-response.html", e.ResponseBody);
            }

            Write(zip, "summary.txt", summary.ToString());
        }

        return ms.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }
}
