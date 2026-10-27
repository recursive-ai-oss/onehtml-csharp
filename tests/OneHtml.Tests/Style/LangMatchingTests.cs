using System;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

public class LangMatchingTests
{
    // RFC 4647 section 3.3.2 extended filtering as the first implementation wrote it, with string splitting: the reference the
    // allocation-free matcher must agree with.
    private static bool Reference(string range, string tag)
    {
        if (range.Length == 0)
        {
            return tag.Length == 0;
        }

        if (tag.Length == 0)
        {
            return false;
        }

        string[] r = range.Split('-');
        string[] t = tag.Split('-');
        if (r[0] != "*" && !string.Equals(r[0], t[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int ri = 1;
        int ti = 1;
        while (ri < r.Length)
        {
            if (r[ri] == "*")
            {
                ri++;
                continue;
            }

            if (ti >= t.Length)
            {
                return false;
            }

            if (string.Equals(r[ri], t[ti], StringComparison.OrdinalIgnoreCase))
            {
                ri++;
                ti++;
            }
            else if (t[ti].Length == 1)
            {
                return false;
            }
            else
            {
                ti++;
            }
        }

        return true;
    }

    [Fact]
    public void Lang_matching_agrees_with_the_string_splitting_reference()
    {
        var r = new Random(47);
        string[] pieces = ["en", "EN", "de", "x", "a", "us", "US", "1996", "*", "Latn", "ch", "", "zh"];
        string Make(bool range)
        {
            var sb = new StringBuilder();
            int n = 1 + r.Next(4);
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    sb.Append('-');
                }

                string p = pieces[r.Next(pieces.Length)];
                sb.Append(!range && p == "*" ? "en" : p);
            }

            return sb.ToString();
        }

        int checkedCount = 0;
        for (int i = 0; i < 4000; i++)
        {
            string tag = Make(false);
            string range = Make(true);
            if (range.Length == 0 || range.Contains('"'))
            {
                continue;
            }

            Document doc = Document.Parse("<p lang=\"" + tag + "\">x</p>");
            Node p = doc.Root.QuerySelector("p");
            // The selector parser may refuse some ranges (an empty subtag); those have nothing to compare.
            bool matched;
            try
            {
                matched = p.Matches(":lang(\"" + range + "\")");
            }
            catch (RecursiveAI.OneHtml.Selectors.SelectorSyntaxException)
            {
                continue;
            }

            Assert.True(Reference(range, tag) == matched, "range '" + range + "' tag '" + tag + "'");
            checkedCount++;
        }

        Assert.True(checkedCount > 1000);
    }
}
