using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Tests;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Locators;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>An incremental re-check must equal a fresh check of the edited document in every result, field and position.</summary>
public sealed class RecheckEquivalenceTests
{
    private static readonly string[] s_extraNames =
    [
        "lang", "href", "type", "aria-checked", "aria-foo", "accesskey", "ismap", "src", "controls", "autoplay", "name", "content", "http-equiv",
        "aria-required", "aria-level", "aria-roledescription", "aria-braillelabel", "aria-busy", "style", "checked", "scope", "headers",
    ];

    private static readonly string[] s_extraValues =
    [
        "en", "xx", "#ins1", "#d0", "#main", "checkbox", "image", "true", "false", "mixed", "refresh", "viewport", "user-scalable=no", "3;url=/x", "row", "1", "a.png", "", "color:red", "visibility:hidden", "position:absolute;left:-9999px",
    ];

    private static IEnumerable<(string Name, string Html)> Pages()
    {
        string golden = Path.Combine(AriaActTests.Repo, "tests", "OneHtml.Accessibility.Validation.Tests", "Golden");
        foreach (string f in Directory.EnumerateFiles(golden, "*.html").Order(StringComparer.Ordinal))
        {
            yield return (Path.GetFileName(f), File.ReadAllText(f));
        }

        foreach ((string name, string html) in RealisticPages.All)
        {
            yield return ("realistic:" + name, html);
        }
    }

    internal static List<Edit> Edits(Document doc, Random rng)
    {
        List<Edit> edits = RandomEdits.Next(doc, rng, 1 + rng.Next(3));
        if (rng.Next(3) == 0)
        {
            var elements = doc.Root.DescendantElements.ToList();
            if (elements.Count > 0)
            {
                Node e = elements[rng.Next(elements.Count)];
                edits.Add(rng.Next(4) == 0 && e.Attributes.Count > 0
                    ? Edit.RemoveAttribute(e, e.Attributes[rng.Next(e.Attributes.Count)].LocalName)
                    : Edit.SetAttribute(e, s_extraNames[rng.Next(s_extraNames.Length)], s_extraValues[rng.Next(s_extraValues.Length)]));
            }
        }

        return edits;
    }

    internal static string Describe(CheckReport r)
    {
        var locators = new LocatorGenerator(r.Document);
        var sb = new StringBuilder();
        sb.Append("rules=").Append(string.Join(',', r.RulesRun.Select(x => x.Descriptor.Id))).Append(" limit=").Append(r.LimitReached).Append('\n');
        foreach (RuleResult x in r.Results)
        {
            sb.Append(x.RuleId).Append('|').Append(x.Outcome).Append('|').Append(x.Target.IsNull ? -1 : x.Target.Index).Append('|').Append(x.Locator).Append('|')
                .Append(x.SourceBytes.Start).Append('-').Append(x.SourceBytes.End).Append('|').Append(x.Message).Append('|').Append(x.TaskImpact).Append('|').Append(x.TaskImpactReason)
                .Append('|').Append(x.CssOnly).Append('|').Append(string.Join(';', x.FactsUsed)).Append('|').Append(string.Join(';', x.FactsMissing));
            foreach (Finding f in x.Findings)
            {
                sb.Append(" F[").Append(f.Attribute).Append('=').Append(f.Value).Append(':').Append(f.Message).Append(']');
            }

            foreach (SuggestedEdit s in x.SuggestedEdits)
            {
                locators.TryGenerate(s.Edit.Target, out string? loc);
                sb.Append(" S[").Append(s.Edit.Operation).Append(':').Append(s.Edit.Target.Index).Append(':').Append(loc).Append(':').Append(s.Edit.Name).Append('=').Append(s.Edit.Value)
                    .Append(':').Append(s.Description).Append(':').Append(System.Text.Json.JsonSerializer.Serialize(s.Edit.Meta)).Append(']');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string FirstDifference(string a, string b)
    {
        string[] x = a.Split('\n'), y = b.Split('\n');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            string l = i < x.Length ? x[i] : "<none>", r = i < y.Length ? y[i] : "<none>";
            if (l != r)
            {
                return $"line {i}:\n  incremental: {l}\n  fresh:       {r}";
            }
        }

        return "(equal)";
    }

    [Fact]
    public void Rechecks_equal_fresh_checks_over_random_edit_sequences()
    {
        int seeds = int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_RECHECK_SEEDS"), out int s) ? s : 3;
        int steps = int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_RECHECK_STEPS"), out int t) ? t : 25;
        int seedBase = int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_RECHECK_SEED_BASE"), out int sb) ? sb : 0;
        var checker = new Checker();
        var failures = new List<string>();
        int total = 0, incremental = 0, partly = 0, carried = 0, fullRules = 0;
        var fullRuleCounts = new Dictionary<string, int>();
        foreach ((string name, string html) in Pages())
        {
            for (int seed = seedBase + 1; seed <= seedBase + seeds; seed++)
            {
                var rng = new Random(seed * 7919 + name.Length);
                Document doc = Document.Parse(html);
                DocumentStyles styles = DocumentStyles.Create(doc);
                AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
                CheckReport report = checker.Check(tree);
                for (int step = 0; step < steps; step++)
                {
                    List<Edit> edits = Edits(doc, rng);
                    EditResult result = doc.Apply(edits);
                    if (!result.Succeeded)
                    {
                        continue;
                    }

                    RestyleResult restyle = styles.Restyle(result);
                    AccessibilityTree updated = tree.Update(result, restyle);
                    CheckReport re = checker.Recheck(report, result, restyle, updated);
                    CheckReport fresh = checker.Check(updated);
                    total++;
                    if (re.Recheck!.Incremental)
                    {
                        incremental++;
                        partly += re.Recheck.RulesRerunPartly;
                        carried += re.Recheck.RulesCarried;
                        fullRules += re.Recheck.RulesRerunInFull;
                    }

                    string a = Describe(re), b = Describe(fresh);
                    if (a != b && failures.Count < 6)
                    {
                        failures.Add($"{name} seed={seed} step={step} edits={string.Join(',', edits.Select(e => e.Operation + ":" + e.Target.Index + ":" + e.Name + "=" + e.Value))} changed=[{string.Join(",", result.Changes!.Changed.Select(c => c.Node.Index + "/" + c.Changes))}] restyled=[{string.Join(",", restyle.RestyledElements)}] info=({re.Recheck!.RulesRerunInFull}/{re.Recheck.RulesRerunPartly}/{re.Recheck.RulesCarried})\n{FirstDifference(a, b)}");
                    }

                    doc = result.Document!;
                    styles = restyle.Styles;
                    tree = updated;
                    report = re;
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
        // The incremental path must be the one that ran, with rules actually carried and rerun in part.
        Assert.True(incremental * 10 >= total * 8, $"only {incremental} of {total} re-checks were incremental");
        Assert.True(carried > 0 && partly > 0, $"carried={carried} partly={partly}");
        string stats = $"rechecks={total} incremental={incremental} rulesPartly={partly} rulesCarried={carried} rulesFull={fullRules}";
        if (Environment.GetEnvironmentVariable("ONEHTML_RECHECK_STATS") is { Length: > 0 } statsPath)
        {
            File.WriteAllText(statsPath, stats);
        }
    }

    [Fact]
    public void Rechecks_equal_fresh_checks_with_other_options()
    {
        var checker = new Checker();
        int total = 0;
        foreach (CheckOptions options in new[]
        {
            new CheckOptions { IncludePasses = false, Profile = Catalogue.ValidationProfile.Wcag21AA },
            new CheckOptions { Criteria = ["4.1.2", "1.1.1"], Tags = ["cat.aria", "cat.text-alternatives", "cat.name-role-value"] },
        })
        foreach ((string name, string html) in Pages())
        {
            var rng = new Random(name.Length * 31 + 5);
            Document doc = Document.Parse(html);
            DocumentStyles styles = DocumentStyles.Create(doc);
            AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
            CheckReport report = checker.Check(tree, options);
            for (int step = 0; step < 20; step++)
            {
                EditResult result = doc.Apply(Edits(doc, rng));
                if (!result.Succeeded)
                {
                    continue;
                }

                RestyleResult restyle = styles.Restyle(result);
                AccessibilityTree updated = tree.Update(result, restyle);
                CheckReport re = checker.Recheck(report, result, restyle, updated);
                Assert.Equal(Describe(checker.Check(updated, options)), Describe(re));
                total++;
                doc = result.Document!;
                styles = restyle.Styles;
                tree = updated;
                report = re;
            }
        }

        Assert.True(total > 200);
    }

    [Fact]
    public void A_rule_that_declares_no_scope_runs_in_full_and_a_private_rule_keeps_working()
    {
        var checker = new Checker([new CountingRule()]);
        Document doc = Document.Parse("<!doctype html><title>t</title><p id=a>x</p><p id=b>y</p>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        CheckReport report = checker.Check(tree);
        EditResult result = doc.Apply([Edit.SetAttribute(doc.GetElementById("a"), "title", "t")]);
        RestyleResult restyle = styles.Restyle(result);
        AccessibilityTree updated = tree.Update(result, restyle);
        CheckReport re = checker.Recheck(report, result, restyle, updated);
        Assert.Equal(Describe(checker.Check(updated)), Describe(re));
        Assert.True(re.Recheck!.Incremental);
        Assert.Equal(2, re.Results.Count(r => r.RuleId == "private-count"));
    }

    private sealed class CountingRule : IAccessibilityRule
    {
        public Catalogue.RuleDescriptor Descriptor { get; } = new("private-count", [Catalogue.RuleSource.Own("private-count")], [], true, Catalogue.RuleNeeds.Static, null, "Private.");

        public string Description => "Reports every paragraph.";

        public string Help => "None.";

        public Impact Impact => Impact.Minor;

        public void Evaluate(CheckContext context, RuleReporter reporter)
        {
            foreach (Node e in context.Elements())
            {
                if (e.LocalName == "p")
                {
                    reporter.Passed(e);
                }
            }
        }
    }

    [Fact]
    public void Recheck_refuses_a_tree_or_edit_that_does_not_belong_to_the_report()
    {
        var checker = new Checker();
        Document doc = Document.Parse("<p id=a>x</p>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        CheckReport report = checker.Check(tree);
        EditResult result = doc.Apply([Edit.SetAttribute(doc.GetElementById("a"), "title", "t")]);
        RestyleResult restyle = styles.Restyle(result);
        AccessibilityTree updated = tree.Update(result, restyle);
        Assert.Throws<ArgumentException>(() => checker.Recheck(report, result, restyle, tree));
        CheckReport second = checker.Recheck(report, result, restyle, updated);
        Assert.Throws<ArgumentException>(() => checker.Recheck(second, result, restyle, updated));
        Assert.Throws<ArgumentNullException>(() => checker.Recheck(null!, result, restyle, updated));
    }

    [Fact]
    public void A_style_sheet_batch_rechecks_equal_a_fresh_check()
    {
        var checker = new Checker();
        const string Html = "<!doctype html><html lang=en><title>t</title><style>p{color:#111}.a{color:#ddd}</style><body style=background:#fff><p id=a>text</p><p id=b class=a>pale</p><button>go</button>";
        Document doc = Document.Parse(Html);
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        CheckReport report = checker.Check(tree);
        InlineStylesheet inline = InlineStylesheets.Of(doc)[0];
        CssNode rule = default;
        foreach (CssNode r in inline.Sheet.Rules)
        {
            rule = r;
            break;
        }

        BatchResult batch = new EditBatch(doc, [Edit.SetAttribute(doc.GetElementById("a"), "title", "x")], [SheetEdit.SetDeclaration(inline.Sheet, rule, "color", "#eee")]).Apply();
        Assert.True(batch.Succeeded, batch.Failure);
        RestyleResult restyle = styles.Restyle(batch);
        AccessibilityTree updated = tree.Update(batch, restyle);
        CheckReport re = checker.Recheck(report, batch, restyle, updated);
        Assert.Equal(Describe(checker.Check(updated)), Describe(re));
        Assert.Contains(re.Results, r => r.RuleId == "color-contrast" && r.Outcome == Outcome.Failed);
    }

    [Fact]
    public void Rechecks_equal_fresh_checks_on_a_page_of_many_repeated_cards()
    {
        var sb = new StringBuilder("<!doctype html><html lang=en><title>Cards</title><style>.card{margin:1em} .hide{display:none}</style><body><main>");
        for (int i = 0; i < 25; i++)
        {
            sb.Append("<section class=card aria-label='Card ").Append(i).Append("'><h2>Heading ").Append(i).Append("</h2><p>Some <b>bold</b> text and a <a href='/p/").Append(i)
                .Append("'>link ").Append(i).Append("</a>.</p><ul role=list><li role=listitem>One</li><li>Two <span class=hide>x</span></li></ul><button type=button aria-describedby=d").Append(i).Append(">Go</button><span id=d").Append(i)
                .Append(" role=nope aria-foo=1>help</span><div role=tab aria-selected=maybe></div><div aria-hidden=true><a href=#>x</a></div><img src=i").Append(i).Append(".png alt='Picture ").Append(i)
                .Append("'><label for=f").Append(i).Append(">Field</label><input id=f").Append(i).Append("><a href='/dup'>More</a><iframe title=t").Append(i % 7).Append(" src=/f").Append(i % 3).Append("></iframe></section>");
        }

        sb.Append("</main></body>");
        var checker = new Checker();
        var rng = new Random(11);
        Document doc = Document.Parse(sb.ToString());
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        CheckReport report = checker.Check(tree);
        int partly = 0;
        for (int step = 0; step < 12; step++)
        {
            EditResult result = doc.Apply(Edits(doc, rng));
            if (!result.Succeeded)
            {
                continue;
            }

            RestyleResult restyle = styles.Restyle(result);
            AccessibilityTree updated = tree.Update(result, restyle);
            CheckReport re = checker.Recheck(report, result, restyle, updated);
            partly += re.Recheck!.RulesRerunPartly;
            string a = Describe(re), b = Describe(checker.Check(updated));
            Assert.True(a == b, "step " + step + "\n" + FirstDifference(a, b));
            doc = result.Document!;
            styles = restyle.Styles;
            tree = updated;
            report = re;
        }

        Assert.True(partly > 0);
    }
}
