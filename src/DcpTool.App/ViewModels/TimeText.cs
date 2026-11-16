using System.Globalization;

namespace DcpTool.App.ViewModels;

/// <summary>hh:mm:ss text for durations and times, with tolerant parsing.</summary>
public static class TimeText
{
    public static string Format(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

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
