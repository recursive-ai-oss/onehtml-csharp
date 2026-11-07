using System.Collections.Generic;
using System.Threading.Tasks;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>The user-agent rules are built once and shared by every document and thread; the result for a document must not depend on that.</summary>
public class SharedUserAgentRulesTests
{
    private const string Page = "<!doctype html><title>t</title><style>.x{color:red} #y:hover{color:blue}</style><h1 class=x>a</h1><ul><li>one<li id=y>two</ul><input placeholder=p><table><tr><td>c</table><span hidden>h</span>";
    private const string QuirksPage = "<h1 class=x>a</h1><table><tr><td><p>c</p></table><center>q</center>";

    private static string Dump(string html)
    {
        Document doc = Document.Parse(html);
        DocumentStyles s = DocumentStyles.Create(doc);
        var sb = new System.Text.StringBuilder();
        foreach (Node n in doc.Root.DescendantElements)
        {
            ComputedStyle cs = s.GetStyle(n);
            sb.Append(n.LocalName).Append(' ');
            foreach (string p in new[] { "display", "color", "margin-top", "font-size", "font-weight", "text-align", "padding-left" })
            {
                sb.Append(p).Append('=').Append(cs.GetPropertyValue(p)).Append(' ');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    [Fact]
    public void Documents_built_on_many_threads_get_the_same_styles_as_one_built_alone()
    {
        string alone = Dump(Page);
        string quirksAlone = Dump(QuirksPage);
        var results = new string[64];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => results[i] = i % 2 == 0 ? Dump(Page) : Dump(QuirksPage));
        for (int i = 0; i < results.Length; i++)
        {
            Assert.Equal(i % 2 == 0 ? alone : quirksAlone, results[i]);
        }

        Assert.Contains("display=none", alone);
        Assert.Contains("h1 display=block", alone);
    }

    [Fact]
    public void An_author_rule_after_the_shared_rules_still_wins_and_a_document_does_not_leak_into_the_next()
    {
        DocumentStyles a = CascadeTests.Styles("<style>div{display:inline} #q{color:red}</style><div id=q>x</div>");
        DocumentStyles b = CascadeTests.Styles("<div id=q>x</div>");
        Assert.Equal("inline", a.GetStyle(a.Document.GetElementById("q")).GetPropertyValue("display"));
        Assert.Equal("block", b.GetStyle(b.Document.GetElementById("q")).GetPropertyValue("display"));
        Assert.True(a.RuleCount == b.RuleCount + 2);
    }

    [Fact]
    public void Rule_limits_smaller_than_the_user_agent_sheet_still_apply()
    {
        Document doc = Document.Parse("<!doctype html><div id=q>x</div>");
        DocumentStyles full = DocumentStyles.Create(doc);
        DocumentStyles capped = DocumentStyles.Create(doc, null, new StyleSourceOptions { MaxRules = 3 });
        Assert.Equal(3, capped.RuleCount);
        Assert.True(full.RuleCount > 3);
    }

    [Fact]
    public void Excluding_the_user_agent_sheet_leaves_only_author_rules()
    {
        Document doc = Document.Parse("<!doctype html><style>p{color:red}</style><div id=q>x</div>");
        DocumentStyles s = DocumentStyles.Create(doc, null, new StyleSourceOptions { IncludeUserAgentSheet = false });
        Assert.Equal(1, s.RuleCount);
        Assert.Equal("inline", s.GetStyle(doc.GetElementById("q")).GetPropertyValue("display"));
    }
}
