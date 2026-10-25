using System.Collections.Generic;
using System.Linq;

namespace RecursiveAI.OneHtml.Tests.Html5lib;

/// <summary>
/// The html5lib-tests cases that are not run as written, with the reason. Nothing else is skipped.
/// </summary>
internal static class KnownDifferences
{
    /// <summary>
    /// Files that are not run at all.
    /// <c>xmlViolation.test</c> expects the output of the "coercing an HTML DOM into an infoset" tweaks, which the
    /// HTML standard no longer contains, so no HTML tokenizer produces it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>
    {
        ["xmlViolation.test"] = "expects XML infoset coercion, which the HTML standard no longer defines",
    };

    /// <summary>
    /// Tokenizer cases whose tokens are checked but whose error list is not, keyed by "file|description".
    /// These three expect <c>unexpected-question-mark-instead-of-tag-name</c> for "&lt;?" followed by a newline. The
    /// current HTML standard (13.2.5.6, 13.2.5.72) turns "&lt;?" into a processing instruction start and reports
    /// <c>invalid-first-character-of-processing-instruction-target</c> at the next character instead; the comment
    /// the tests expect is the same. The tests have not been updated for the new tokenizer states.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ErrorListsNotChecked = new Dictionary<string, string>
    {
        ["domjs.test|CR in bogus comment state"] = "old error code for <? ; the standard now has processing instructions",
        ["domjs.test|CRLF in bogus comment state"] = "old error code for <? ; the standard now has processing instructions",
        ["domjs.test|CRLFLF in bogus comment state"] = "old error code for <? ; the standard now has processing instructions",
    };

    /// <summary>
    /// Compares error lists as "code@line:column". Two differences from the suite are tolerated, both in how the
    /// suite reports rather than in what is reported:
    /// the suite puts <c>eof-in-processing-instruction</c> at the end of the target (column 4 in <c>&lt;?a a</c>), not at the
    /// end of the input where the standard's EOF is, so for that code only the code is compared; and the order of
    /// two errors at one position (an input-stream error and a tokenizer error about the same character) is not
    /// defined by the standard, so errors at one position are compared as a set.
    /// </summary>
    public static bool ErrorsMatch(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        return Normalize(expected).SequenceEqual(Normalize(actual));
    }

    private static List<string> Normalize(IReadOnlyList<string> errors)
    {
        const string PiEof = "eof-in-processing-instruction@";
        var list = new List<(string Position, string Code)>();
        foreach (string e in errors)
        {
            int at = e.IndexOf('@', System.StringComparison.Ordinal);
            bool piEof = e.StartsWith(PiEof, System.StringComparison.Ordinal);
            list.Add((piEof ? string.Empty : e[(at + 1)..], e[..at]));
        }

        // Stable grouping by position; within a group the codes are ordered.
        var result = new List<string>();
        for (int i = 0; i < list.Count;)
        {
            int j = i + 1;
            while (j < list.Count && list[j].Position == list[i].Position && list[i].Position.Length > 0)
            {
                j++;
            }

            result.AddRange(list.GetRange(i, j - i).OrderBy(x => x.Code, System.StringComparer.Ordinal).Select(x => x.Code + "@" + x.Position));
            i = j;
        }

        return result;
    }
}
