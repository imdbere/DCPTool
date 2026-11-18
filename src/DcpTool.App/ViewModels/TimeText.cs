using System.Globalization;

namespace DcpTool.App.ViewModels;

/// <summary>Text for durations and times of day, with tolerant parsing.</summary>
public static class TimeText
{
    /// <summary>hh:mm:ss, for times of day.</summary>
    public static string Format(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    /// <summary>mm:ss for durations under an hour, h:mm:ss above; empty for zero.</summary>
    public static string FormatDuration(TimeSpan value) =>
        value <= TimeSpan.Zero ? ""
        : value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes:00}:{value.Seconds:00}";

    /// <summary>"hh:mm:ss", "mm:ss" or plain seconds. Separators may be ':' or '.', whitespace is ignored.</summary>
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Trim().Replace('.', ':').Replace(';', ':').Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length > 3)
        {
            return false;
        }

        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 6 || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        // long: six digits of hours times 3600 overflow an int
        long seconds = numbers.Length switch
        {
            1 => numbers[0],
            2 => numbers[0] * 60L + numbers[1],
            _ => numbers[0] * 3600L + numbers[1] * 60L + numbers[2],
        };
        if (numbers.Length >= 2 && numbers[^1] >= 60 || numbers.Length == 3 && numbers[1] >= 60)
        {
            return false;
        }

        value = TimeSpan.FromSeconds(seconds);
        return true;
    }
}

/// <summary>Dates as dd.MM.yyyy text (the RAI form's format in every language), with tolerant parsing.</summary>
public static class DateText
{
    public static string Format(DateOnly? value) => value?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";

    /// <summary>
    /// Day, month and year separated by '.', '/' or '-' ("14.03.2026", "14/3/26", "14.03." for this year), or ISO
    /// "2026-03-14". Empty text is a valid null.
    /// </summary>
    public static bool TryParse(string? text, out DateOnly? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Trim().Split(['.', '/', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 3 || parts.Any(p => !p.All(char.IsAsciiDigit)))
        {
            return false;
        }

        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        int day, month, year;
        if (parts[0].Length == 4 && parts.Length == 3)
        {
            (year, month, day) = (numbers[0], numbers[1], numbers[2]);
        }
        else
        {
            (day, month) = (numbers[0], numbers[1]);
            year = parts.Length == 3 ? numbers[2] : DateTime.Today.Year;
            if (parts.Length == 3 && parts[2].Length <= 2)
            {
                year += 2000;
            }
        }

        if (year is < 1900 or > 2200 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        value = new DateOnly(year, month, day);
        return true;
    }
}
