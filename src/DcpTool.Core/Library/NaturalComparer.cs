using System.Globalization;

namespace DcpTool.Core.Library;

/// <summary>
/// Compares names the way people read them: runs of digits as numbers, so "zipp 2_20" comes before
/// "zipp 10_20". Text is compared case-insensitively in the current culture. Equal numbers with a different
/// number of leading zeros ("01" and "1") and names equal apart from case still get a stable order.
/// </summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var compareInfo = CultureInfo.CurrentCulture.CompareInfo;
        int i = 0, j = 0;
        var tieBreak = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                var result = CompareNumbers(x.AsSpan(startX, i - startX), y.AsSpan(startY, j - startY));
                if (result != 0)
                {
                    return result;
                }

                if (tieBreak == 0)
                {
                    // "1" before "01"
                    tieBreak = (i - startX).CompareTo(j - startY);
                }

                continue;
            }

            var endX = i;
            while (endX < x.Length && !char.IsAsciiDigit(x[endX]))
            {
                endX++;
            }

            var endY = j;
            while (endY < y.Length && !char.IsAsciiDigit(y[endY]))
            {
                endY++;
            }

            var text = compareInfo.Compare(x, i, endX - i, y, j, endY - j, CompareOptions.IgnoreCase);
            if (text != 0)
            {
                return text;
            }

            if (tieBreak == 0)
            {
                tieBreak = compareInfo.Compare(x, i, endX - i, y, j, endY - j, CompareOptions.None);
            }

            i = endX;
            j = endY;
        }

        var length = (x.Length - i).CompareTo(y.Length - j);
        return length != 0 ? length : tieBreak != 0 ? tieBreak : string.CompareOrdinal(x, y);
    }

    private static int CompareNumbers(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        if (a.Length != b.Length)
        {
            return a.Length.CompareTo(b.Length);
        }

        // Same number of significant digits: digit by digit. Works for numbers of any size.
        return a.SequenceCompareTo(b);
    }
}
