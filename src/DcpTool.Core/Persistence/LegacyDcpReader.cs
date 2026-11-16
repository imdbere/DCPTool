using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using DcpTool.Core.Model;

namespace DcpTool.Core.Persistence;

/// <summary>
/// Reads .dcp files written by DCP Tool 0.x. Those are the <c>DataContractJsonSerializer</c> output of
/// the old <c>Dcp</c> class: PascalCase names, dates as <c>"/Date(1640473200000+0100)/"</c>, durations
/// as ISO 8601 (<c>"PT3M41S"</c>), enums as numbers, each line with both <c>Autori</c> and <c>AutoriString</c>.
/// </summary>
internal static partial class LegacyDcpReader
{
    public static bool LooksLikeLegacy(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        (root.TryGetProperty("Lines", out _) || root.TryGetProperty("DataTrasmissione", out _));

    public static Dcp Read(JsonElement root)
    {
        var dataTrasmissione = Date(root, "DataTrasmissione") ?? DateOnly.FromDateTime(DateTime.Today);
        var dataRegistrazione = Date(root, "DataRegistrazione");

        var titolo = Text(root, "TitoloItaliano");
        // The old getter returned TitoloProgramma + " - " + TitoloSerie when no title was entered
        if (titolo.Trim() == "-")
        {
            titolo = "";
        }

        var dcp = new Dcp
        {
            TitoloItaliano = titolo,
            TitoloOriginale = Text(root, "TitoloOriginale"),
            Sottotitolo = Text(root, "Sottotitolo"),
            Puntata = NonZero(root, "Puntata"),
            Serie = NonZero(root, "Serie"),
            Uorg = NonZero(root, "Uorg"),
            Matricola = NonZero(root, "Matricola"),
            DataTrasmissione = dataTrasmissione,
            // The old model always stored a recording date, by default five days before the broadcast.
            // Keep "not entered" as such, the effective value is the same.
            DataRegistrazione = dataRegistrazione == dataTrasmissione.AddDays(-5) ? null : dataRegistrazione,
            Durata = Duration(root, "Durata"),
            OraInizio = Duration(root, "OraInizio"),
            Sede = Int(root, "Sede") switch
            {
                16 => "BZ/16",
                14 => "BZ/14",
                15 => "BZ/15",
                null or 0 => "",
                var other => "BZ/" + other.Value.ToString(CultureInfo.InvariantCulture),
            },
            Rete = Int(root, "ReteTrasmissione") switch
            {
                1 => "TV2",
                2 => "TV3",
                3 => "TV4",
                _ => "TV1",
            },
            NumeroContratto = Text(root, "NumeroContratto"),
            DataContratto = Date(root, "DataContratto"),
        };

        if (root.TryGetProperty("Lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in lines.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.Object))
            {
                dcp.Lines.Add(ReadLine(line));
            }
        }

        return dcp;
    }

    private static DcpLine ReadLine(JsonElement element) => new()
    {
        Titolo = Text(element, "Titolo"),
        Autori = ReadAutori(element),
        Esecutori = Text(element, "Esecutori"),
        Durata = Duration(element, "Durata"),
        // GenereSiae had the single member ML
        GenereSiae = "ML",
        Ruolo = Int(element, "Ruolo") switch
        {
            1 => "SF",
            2 => "SI",
            _ => "PP",
        },
        TipoGenerazione = Int(element, "TipoGenerazione") switch
        {
            1 => "1",
            null or 0 or 9 => "9",
            var other => other.Value.ToString(CultureInfo.InvariantCulture),
        },
        Marca = Text(element, "Marca"),
        Edizione = Text(element, "Edizione"),
        SiglaNum = Text(element, "SiglaNum"),
    };

    private static List<Autore> ReadAutori(JsonElement line)
    {
        // The array holds Nome and Cognome exactly as they were. AutoriString is the same data joined to
        // "Nome Cognome, ..." and would have to be split again, which loses multi-word surnames.
        if (line.TryGetProperty("Autori", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            return array.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.Object)
                .Select(a => new Autore(Text(a, "Nome").Trim(), Text(a, "Cognome").Trim()))
                .Where(a => !a.IsEmpty)
                .ToList();
        }

        return Text(line, "AutoriString")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Autore.FromFullName)
            .Where(a => !a.IsEmpty)
            .ToList();
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static int? NonZero(JsonElement element, string name) =>
        Int(element, name) is { } number && number != 0 ? number : null;

    private static TimeSpan Duration(JsonElement element, string name)
    {
        var text = Text(element, name);
        if (text.Length == 0)
        {
            return TimeSpan.Zero;
        }

        try
        {
            return XmlConvert.ToTimeSpan(text);
        }
        catch (FormatException)
        {
            return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// <c>/Date(ms+hhmm)/</c>: milliseconds since 1970 in UTC, plus the offset of the PC that saved the file.
    /// The date the user saw is the UTC instant shifted by that offset.
    /// </summary>
    private static DateOnly? Date(JsonElement element, string name)
    {
        var match = MicrosoftDate().Match(Text(element, name));
        if (!match.Success || !long.TryParse(match.Groups["ms"].Value, CultureInfo.InvariantCulture, out var ms))
        {
            return null;
        }

        var offset = TimeSpan.Zero;
        if (match.Groups["sign"].Success)
        {
            var hours = int.Parse(match.Groups["hh"].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(match.Groups["mm"].Value, CultureInfo.InvariantCulture);
            offset = new TimeSpan(hours, minutes, 0);
            if (match.Groups["sign"].Value == "-")
            {
                offset = -offset;
            }
        }

        try
        {
            var local = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(offset);
            // DateTime.MinValue stood for "no date" (DataContratto)
            return local.Year < 1900 ? null : DateOnly.FromDateTime(local.DateTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^/Date\((?<ms>-?\d+)(?:(?<sign>[+-])(?<hh>\d{2})(?<mm>\d{2}))?\)/$")]
    private static partial Regex MicrosoftDate();
}
