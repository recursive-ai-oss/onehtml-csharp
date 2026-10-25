using System;

namespace RecursiveAI.OneHtml.Parsing;

/// <summary>
/// Lookup in the named character reference table (HTML 13.5). The data is generated from the standard's
/// <c>entities.json</c>; this part finds the longest name that is a prefix of the input.
/// </summary>
internal static partial class NamedCharacterReferences
{
    // Start of each name in NameData (plus one past the end), and of each value in ValueData. Built on first use.
    private static readonly int[] s_nameStarts = BuildNameStarts();
    private static readonly int[] s_valueStarts = BuildValueStarts();

    /// <summary>
    /// Finds the longest table name that begins the input (the standard's "consume the maximum number of characters
    /// possible"). Names that end in a semicolon and legacy names without one are both in the table.
    /// </summary>
    /// <param name="input">The text after the ampersand.</param>
    /// <param name="length">The number of input characters the name uses, semicolon included.</param>
    /// <param name="value">The replacement text: one or two code points.</param>
    /// <returns>True when a name matched.</returns>
    public static bool TryMatch(ReadOnlySpan<char> input, out int length, out ReadOnlySpan<char> value)
    {
        ReadOnlySpan<char> names = NameData;
        int lo = 0;
        int hi = Count;
        int best = -1;
        int bestLength = 0;
        int k = 0;
        while (true)
        {
            // Every name in [lo, hi) starts with input[..k]. The one that is exactly that prefix sorts first.
            int first = lo;
            if (k > 0 && lo < hi && NameLength(lo) == k)
            {
                best = lo;
                bestLength = k;
                first = lo + 1;
            }

            if (k >= input.Length || k >= MaxNameLength || first >= hi)
            {
                break;
            }

            char c = input[k];

            // Narrow to the names whose k-th character is c.
            int l = first;
            int h = hi;
            while (l < h)
            {
                int mid = (l + h) >>> 1;
                if (names[s_nameStarts[mid] + k] < c)
                {
                    l = mid + 1;
                }
                else
                {
                    h = mid;
                }
            }

            int upper = l;
            int top = hi;
            while (upper < top)
            {
                int mid = (upper + top) >>> 1;
                if (names[s_nameStarts[mid] + k] <= c)
                {
                    upper = mid + 1;
                }
                else
                {
                    top = mid;
                }
            }

            if (l >= upper)
            {
                break;
            }

            lo = l;
            hi = upper;
            k++;
        }

        if (best < 0)
        {
            length = 0;
            value = default;
            return false;
        }

        length = bestLength;
        value = ((ReadOnlySpan<char>)ValueData).Slice(s_valueStarts[best], ValueLengths[best]);
        return true;
    }

    private static int NameLength(int index) => s_nameStarts[index + 1] - s_nameStarts[index] - 1;

    private static int[] BuildNameStarts()
    {
        var starts = new int[Count + 1];
        ReadOnlySpan<char> data = NameData;
        int n = 0;
        starts[0] = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == '\n')
            {
                starts[++n] = i + 1;
            }
        }

        // The last name has no trailing separator; give it a virtual one so that NameLength works.
        starts[Count] = data.Length + 1;
        return starts;
    }

    private static int[] BuildValueStarts()
    {
        var starts = new int[Count];
        int offset = 0;
        for (int i = 0; i < Count; i++)
        {
            starts[i] = offset;
            offset += ValueLengths[i];
        }

        return starts;
    }
}
