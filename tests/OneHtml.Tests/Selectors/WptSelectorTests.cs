using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Selectors;
using Xunit;
using Xunit.Sdk;

namespace RecursiveAI.OneHtml.Tests.Selectors;

public enum WptOutcome
{
    Pass,
    Fail,
    NotApplicable,
}

public sealed record WptResult(string Group, string Name, WptOutcome Outcome, string Detail);

/// <summary>
/// Runs the data-driven parts of the vendored web-platform selector tests against our implementation: the test data is
/// read from the files; assertions that need script, layout or a mutable DOM are reported as not applicable with the reason.
/// </summary>
public class WptSelectorTests
{
    private static readonly string Root = Path.Combine(TestPaths.TestData, "wpt", "selectors");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    // ---- dom/nodes: selectors.js through querySelector(All), matches and closest ----

    [Fact]
    public void Dom_selectors_api()
    {
        Report("dom/nodes selectors.js", RunDomSelectors());
    }

    internal static List<WptResult> RunDomSelectors()
    {
        var results = new List<WptResult>();
        string js = Read("dom", "nodes", "selectors.js");
        List<object?> valid = JsReader.ReadVarArray(js, "validSelectors")!;
        List<object?> invalid = JsReader.ReadVarArray(js, "invalidSelectors")!;
        List<object?> scoped = JsReader.ReadVarArray(js, "scopedSelectors")!;
        Assert.True(valid.Count > 150 && invalid.Count > 20 && scoped.Count > 20);

        Document doc = Document.Parse(File.ReadAllBytes(Path.Combine(Root, "dom", "nodes", "ParentNode-querySelector-All-content.html")));
        Node root = doc.GetElementById("root");
        string rootHtml = doc.SerializeNode(root);
        Document fragDoc = Document.ParseFragment(rootHtml, new FragmentContext(ElementNamespace.Html, "body"));
        Node frag = fragDoc.Root;
        Node detached = frag.FirstElementChild;

        var contexts = new (string Name, string NodeType, Node Node)[]
        {
            ("Document", "document", doc.Root),
            ("In-document Element", "element", root),
            ("Detached Element", "detached", detached),
            ("Fragment", "fragment", frag),
        };

        foreach ((string ctxName, _, Node node) in contexts)
        {
            foreach (Dictionary<string, object?> s in invalid.Cast<Dictionary<string, object?>>())
            {
                string q = (string)s["selector"]!;
                results.Add(Check("dom-qsa", $"{ctxName}.querySelector invalid: {q}", () =>
                    Assert.Throws<SelectorSyntaxException>(() => node.QuerySelector(q))));
                if (node.IsElement)
                {
                    results.Add(Check("dom-matches", $"{ctxName}.matches invalid: {q}", () =>
                        Assert.Throws<SelectorSyntaxException>(() => node.Matches(q))));
                }
            }
        }

        foreach ((string ctxName, string nodeType, Node node) in contexts)
        {
            Document d = node.Document!;
            // Only the document's own target element can be :target; a detached tree or fragment has none.
            var state = new InteractionState { Target = node.IsConnected ? d.GetElementById("target") : default };
            var options = new SelectorMatchOptions { State = state };
            foreach (Dictionary<string, object?> s in valid.Concat(scoped).Cast<Dictionary<string, object?>>())
            {
                string q = (string)s["selector"]!;
                string name = (string)s["name"]!;
                var expect = ((List<object?>)s["expect"]!).Cast<string>().ToList();
                var exclude = s.TryGetValue("exclude", out object? ex) && ex is List<object?> el ? el.Cast<string>().ToList() : [];
                int testType = EvalFlags(((JsExpr)s["testType"]!).Text);
                if (exclude.Contains(nodeType) || exclude.Contains("html"))
                {
                    continue;
                }

                // attr-presence-i1 gets a namespaced title from script (setAttributeNS), which no parsed document can have.
                bool needsMissing = expect.Any(id => d.GetElementById(id).IsNull) || (q.Contains('|', StringComparison.Ordinal) && expect.Contains("attr-presence-i1"));
                if ((testType & 1) != 0 && !s.ContainsKey("ctx"))
                {
                    string label = $"{ctxName}.querySelectorAll: {name}: {q}";
                    if (needsMissing)
                    {
                        results.Add(new WptResult("dom-qsa", label, WptOutcome.NotApplicable, "expected elements are created by script (createElementNS)"));
                    }
                    else
                    {
                        results.Add(Check("dom-qsa", label, () =>
                        {
                            List<Node> found = node.QuerySelectorAll(SelectorList.Parse(q), options);
                            Assert.Equal(string.Join(",", expect), string.Join(",", found.Select(n => n.GetAttribute("id"))));
                            Node first = node.QuerySelector(SelectorList.Parse(q), options);
                            Assert.Equal(expect.Count > 0 ? expect[0] : null, first.IsNull ? null : first.GetAttribute("id"));
                        }));
                    }
                }

                if ((testType & 0x10) != 0)
                {
                    string label = $"{ctxName}.matches: {name}: {q}";
                    if (needsMissing)
                    {
                        results.Add(new WptResult("dom-matches", label, WptOutcome.NotApplicable, "expected elements are created by script (createElementNS)"));
                    }
                    else
                    {
                        // The old two-argument form's second argument is ignored by browsers, so :scope is the element itself.
                        results.Add(Check("dom-matches", label, () =>
                        {
                            foreach (string id in expect)
                            {
                                Node e = d.GetElementById(id);
                                Assert.True(e.Matches(SelectorList.Parse(q), options), "#" + id + " should match");
                            }

                            if (s.TryGetValue("unexpected", out object? un) && un is List<object?> ul)
                            {
                                foreach (string id in ul.Cast<string>())
                                {
                                    Assert.False(d.GetElementById(id).Matches(SelectorList.Parse(q), options), "#" + id + " should not match");
                                }
                            }
                        }));
                    }
                }
            }
        }

        return results;
    }

    private static int EvalFlags(string text)
    {
        int v = 0;
        foreach (string part in text.Split('|', StringSplitOptions.TrimEntries))
        {
            v |= part switch { "TEST_QSA" => 1, "TEST_FIND" => 4, "TEST_MATCH" => 0x10, _ => 0 };
        }

        return v;
    }

    // ---- dom/nodes/Element-closest.html ----

    [Fact]
    public void Dom_closest()
    {
        string html = Read("dom", "nodes", "Element-closest.html");
        Document doc = Document.Parse(html);
        var results = new List<WptResult>();
        foreach (string script in JsReader.InlineScripts(html))
        {
            foreach (string st in JsReader.Statements(script))
            {
                var call = JsReader.ReadCall(st);
                if (call is not { Name: "do_test" } c)
                {
                    continue;
                }

                string sel = (string)c.Args[0]!;
                string from = (string)c.Args[1]!;
                string want = (string)c.Args[2]!;
                results.Add(Check("dom-closest", $"closest({sel}) from #{from}", () =>
                {
                    Node target = doc.GetElementById(from).Closest(SelectorList.Parse(sel));
                    Assert.Equal(want, target.IsNull ? string.Empty : target.Id);
                }));
            }
        }

        Assert.True(results.Count >= 25);
        Report("dom/nodes Element-closest.html", results);
    }

    // ---- css/selectors/parsing ----

    [Fact]
    public void Css_selectors_parsing()
    {
        var results = new List<WptResult>();
        foreach (string file in Directory.GetFiles(Path.Combine(Root, "css", "selectors", "parsing"), "*.html").OrderBy(f => f, StringComparer.Ordinal))
        {
            string html = File.ReadAllText(file);
            string fileName = Path.GetFileName(file);
            int before = results.Count;
            foreach (string script in JsReader.InlineScripts(html))
            {
                foreach (string st in JsReader.Statements(script))
                {
                    RunParsingStatement(fileName, st, results);
                }
            }

            if (results.Count == before)
            {
                results.Add(new WptResult("css-parsing", fileName, WptOutcome.NotApplicable, "no data-only assertions"));
            }
        }

        Report("css/selectors/parsing", results);
    }

    private static void RunParsingStatement(string file, string st, List<WptResult> results)
    {
        if (st.StartsWith("function run_tests_on_anplusb_selector", StringComparison.Ordinal))
        {
            // The body is a list of assert_* calls on `source + '(…)'`; run it for each of the four pseudo-class names.
            foreach (string source in new[] { ":nth-child", ":nth-last-child", ":nth-of-type", ":nth-last-of-type" })
            {
                int open = st.IndexOf('{', StringComparison.Ordinal);
                foreach (string inner in JsReader.Statements(st[(open + 1)..^1]))
                {
                    var c = JsReader.ReadCall(inner.Replace("source +", "'" + source + "' +", StringComparison.Ordinal));
                    if (c is null)
                    {
                        continue;
                    }

                    if (c.Value.Name == "assert_selector_serializes_to")
                    {
                        AddValid(file, results, (string)c.Value.Args[0]!, (string)c.Value.Args[1]!, false);
                    }
                    else if (c.Value.Name == "assert_invalid_selector")
                    {
                        AddInvalid(file, results, (string)c.Value.Args[0]!);
                    }
                }
            }

            return;
        }

        var call = JsReader.ReadCall(st);
        if (call is null)
        {
            return;
        }

        (string name, List<object?> args) = call.Value;
        switch (name)
        {
            case "test_valid_selector":
            case "test_valid_forgiving_selector":
            {
                if (args[0] is not string sel)
                {
                    results.Add(new WptResult("css-parsing", file + ": " + st, WptOutcome.NotApplicable, "selector is a script expression"));
                    return;
                }

                object? ser = args.Count > 1 && args[1] is not JsUndefined and not null ? args[1] : sel;
                AddValid(file, results, sel, ser, name == "test_valid_forgiving_selector");
                break;
            }

            case "test_invalid_selector":
                if (args[0] is string bad)
                {
                    AddInvalid(file, results, bad);
                }

                break;
        }
    }

    private static void AddValid(string file, List<WptResult> results, string sel, object? serialized, bool forgivingOnly)
    {
        results.Add(Check("css-parsing", $"{file}: valid {sel}", () =>
        {
            SelectorList list = SelectorList.Parse(sel);
            string text = list.ToString();
            if (serialized is List<object?> alts)
            {
                Assert.Contains(text, alts.Cast<string>());
            }
            else
            {
                Assert.Equal((string)serialized!, text);
            }

            Assert.Equal(text, SelectorList.Parse(text).ToString());
            if (!sel.Contains(',', StringComparison.Ordinal) || forgivingOnly)
            {
                Assert.Equal(!forgivingOnly, SelectorList.IsValidComplexSelector(sel));
            }
        }));
    }

    private static void AddInvalid(string file, List<WptResult> results, string sel)
    {
        results.Add(Check("css-parsing", $"{file}: invalid {sel}", () =>
        {
            Assert.False(SelectorList.TryParse(sel, out _), "should not parse");
        }));
    }

    // ---- html/semantics/selectors/pseudo-classes ----

    [Fact]
    public void Html_pseudo_classes()
    {
        var results = new List<WptResult>();
        string dir = Path.Combine(Root, "html", "semantics", "selectors", "pseudo-classes");
        foreach (string file in Directory.GetFiles(dir, "*.html").OrderBy(f => f, StringComparer.Ordinal))
        {
            string html = File.ReadAllText(file);
            string fileName = Path.GetFileName(file);
            Document doc = Document.Parse(Encoding.UTF8.GetBytes(html));
            bool mutated = false;
            int before = results.Count;
            foreach (string script in JsReader.InlineScripts(html))
            {
                foreach (string st in JsReader.Statements(script))
                {
                    var call = JsReader.ReadCall(st);
                    if (call is { Name: "testSelectorIdsMatch" } c && c.Args.Count >= 3 && c.Args[0] is string sel && c.Args[1] is List<object?> ids)
                    {
                        string label = $"{fileName}: {sel} ({c.Args[2]})";
                        if (mutated)
                        {
                            results.Add(new WptResult("html-pseudo", label, WptOutcome.NotApplicable, "runs after a script mutation of the DOM or element state"));
                            continue;
                        }

                        results.Add(Check("html-pseudo", label, () =>
                        {
                            List<Node> found = doc.Root.QuerySelectorAll(SelectorList.Parse(sel));
                            Assert.Equal(string.Join(",", ids.Cast<string>()), string.Join(",", found.Select(n => n.GetAttribute("id"))));
                        }));
                    }
                    else if (call is null || call.Value.Name != "testSelectorIdsMatch")
                    {
                        if (st.StartsWith("function ", StringComparison.Ordinal) || st.StartsWith("var ", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        mutated = true;
                    }
                }
            }

            if (results.Count == before)
            {
                results.Add(new WptResult("html-pseudo", fileName, WptOutcome.NotApplicable, "needs script, user interaction or a reference rendering"));
            }
        }

        Report("html/semantics/selectors/pseudo-classes", results);
    }

    // ---- reporting ----

    private static WptResult Check(string group, string name, Action body)
    {
        try
        {
            body();
            return new WptResult(group, name, WptOutcome.Pass, string.Empty);
        }
        catch (XunitException e)
        {
            return new WptResult(group, name, WptOutcome.Fail, e.Message.Split('\n')[0]);
        }
        catch (Exception e) when (e is SelectorSyntaxException or ArgumentException or InvalidOperationException)
        {
            return new WptResult(group, name, WptOutcome.Fail, e.GetType().Name + ": " + e.Message);
        }
    }

    private static void Report(string suite, List<WptResult> results)
    {
        int pass = results.Count(r => r.Outcome == WptOutcome.Pass);
        int na = results.Count(r => r.Outcome == WptOutcome.NotApplicable);
        var failures = results.Where(r => r.Outcome == WptOutcome.Fail).ToList();
        var unexpected = failures.Where(f => !WptSelectorExpectations.IsKnown(f.Name)).ToList();
        var stale = WptSelectorExpectations.Known.Keys
            .Where(k => !k.EndsWith('*') && results.Any(r => WptSelectorExpectations.Matches(k, r.Name) && r.Outcome == WptOutcome.Pass)).ToList();

        string summary = $"{suite}: pass {pass}, known failures {failures.Count - unexpected.Count}, n/a {na}, unexpected failures {unexpected.Count}";
        if (Environment.GetEnvironmentVariable("WPT_SELECTOR_DUMP") is { Length: > 0 } dump)
        {
            File.AppendAllLines(dump, [summary, .. failures.Select(f => "FAIL\t" + f.Name + "\t" + f.Detail), .. results.Where(r => r.Outcome == WptOutcome.NotApplicable).GroupBy(r => r.Detail).Select(g => "NA\t" + g.Count() + "\t" + g.Key)]);
        }

        Assert.True(unexpected.Count == 0 && stale.Count == 0,
            summary + "\n" + string.Join("\n", unexpected.Take(40).Select(f => "  FAIL " + f.Name + " -> " + f.Detail))
            + (stale.Count > 0 ? "\nStale known failures:\n" + string.Join("\n", stale.Take(20)) : string.Empty));
    }
}

/// <summary>
/// Cases of the web-platform selector tests that fail on purpose, with the reason. Keyed by the name the runner gives the
/// case. A case that starts passing must be removed.
/// </summary>
internal static class WptSelectorExpectations
{
    /// <summary>Exact names, or a prefix ending in <c>*</c> for a whole file.</summary>
    public static Dictionary<string, string> Known { get; } = new(StringComparer.Ordinal)
    {
        ["parse-class-prefix.tentative.html:*"] = "Tentative proposal (class prefix selectors) that is not in Selectors 4; not implemented.",
        ["parse-has-class-prefix.tentative.html:*"] = "Tentative proposal (class prefix selectors) that is not in Selectors 4; not implemented.",
        ["parse-has-slotted.tentative.html:*"] = "Tentative proposal (:has-slotted) that is not in Selectors 4; not implemented.",
    };

    public static bool Matches(string key, string name) =>
        key.EndsWith('*') ? name.StartsWith(key[..^1], StringComparison.Ordinal) : key == name;

    public static bool IsKnown(string name) => Known.Keys.Any(k => Matches(k, name));
}
