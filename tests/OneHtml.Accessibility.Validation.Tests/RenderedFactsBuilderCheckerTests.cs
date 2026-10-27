using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Tests;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>Facts built directly from the DOMSnapshot adapter's own values give the checker the same findings, on every committed Chrome capture.</summary>
public sealed class RenderedFactsBuilderCheckerTests
{
    public static IEnumerable<TheoryDataRow<string>> Pages() => FactsCopy.Pages().Select(p => new TheoryDataRow<string>(p));

    [Theory]
    [MemberData(nameof(Pages))]
    public void Checker_results_with_built_facts_equal_those_with_adapter_facts(string page)
    {
        (Document doc, RenderedFacts adapter) = FactsCopy.Load(page);
        RenderedFacts built = FactsCopy.Rebuild(doc, adapter);
        DocumentStyles styles = DocumentStyles.Create(doc);
        string Run(RenderedFacts facts)
        {
            AccessibilityTree tree = AccessibilityTree.Build(doc, styles, null, new AccessibilityOptions { Facts = facts });
            CheckReport report = new Checker().Check(tree, new CheckOptions { Facts = facts });
            return report.ToNativeJson() + report.ToAxeJson();
        }

        string expected = Run(adapter);
        Assert.Equal(expected, Run(built));
        Assert.True(expected.Length > 1000);
    }

    /// <summary>The same comparison over a store of corpus captures, when <c>ONEHTML_CAPTURE_DIR</c> names one (skipped otherwise).</summary>
    [Fact]
    public void Checker_results_equal_on_stored_corpus_captures()
    {
        List<string> dirs = FactsCopy.StoreDirs();
        Assert.SkipWhen(dirs.Count == 0, "ONEHTML_CAPTURE_DIR is not set.");
        int compared = 0, refused = 0, findings = 0;
        foreach (string dir in dirs)
        {
            if (FactsCopy.LoadDir(dir, requireSuccess: false) is not { } cap)
            {
                refused++;
                continue;
            }

            RenderedFacts built = FactsCopy.Rebuild(cap.Doc, cap.Facts);
            DocumentStyles styles = DocumentStyles.Create(cap.Doc);
            string Run(RenderedFacts facts)
            {
                AccessibilityTree tree = AccessibilityTree.Build(cap.Doc, styles, null, new AccessibilityOptions { Facts = facts });
                CheckReport report = new Checker().Check(tree, new CheckOptions { Facts = facts });
                return report.ToNativeJson() + report.ToAxeJson();
            }

            string expected = Run(cap.Facts);
            Assert.True(expected == Run(built), dir);
            compared++;
            findings += expected.Length;
        }

        FactsCopy.Report($"checker captures={dirs.Count} compared={compared} refused={refused} jsonChars={findings}");
        Assert.True(compared > 0);
    }
}
