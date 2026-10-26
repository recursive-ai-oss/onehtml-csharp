using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Tests.Css;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>One test of a web-platform-tests file run through the cascade.</summary>
internal sealed record StyleWptResult(string Directory, string File, string Test, WptOutcome Outcome, string Detail);

/// <summary>
/// Runs the static parts of web-platform-tests cascade files without a browser or a script engine. Three kinds of tests
/// are understood; everything else is counted as not applicable with a reason.
/// <list type="bullet">
/// <item><description>Reference tests (<c>rel=match</c>): the page is styled and checked for the "no red" outcome the
/// tests state: no visible element paints red (text, background, border or outline). This approximates the comparison with the
/// reference, which would need rendering.</description></item>
/// <item><description><c>test()</c> blocks whose body only reads <c>getComputedStyle</c> values and compares them with literals.</description></item>
/// <item><description><c>test_computed_value(property, specified, expected)</c> calls, run on the page's <c>#target</c> with the specified value as its style.</description></item>
/// <item><description>The <c>testCases</c> table loop of the layer tests: each case's style text is appended to the page and the
/// <c>getComputedStyle</c> assertion is evaluated on the elements the loop selects.</description></item>
/// </list>
/// </summary>
internal sealed class StyleWptRunner
{
    private static readonly Regex RefLink = new(@"<link[^>]+rel=[""']?(?<rel>match|mismatch)[""']?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Decl = new(@"(?:const|let|var)\s+(?<n>[A-Za-z_$][\w$]*)\s*=\s*(?<v>[^;]+);", RegexOptions.CultureInvariant);
    private static readonly Regex ComputedRead = new(@"^(?:window\.)?getComputedStyle\((?<el>.+?)\)(?:\.(?<prop>[A-Za-z]\w*)|\.getPropertyValue\((?<q>['""])(?<name>[^'""]+)\k<q>\))$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex ById = new(@"^document\.getElementById\((['""])(?<id>.*?)\1\)$", RegexOptions.CultureInvariant);
    private static readonly Regex Query = new(@"^document\.querySelector\((['""])(?<s>.*?)\1\)$", RegexOptions.CultureInvariant);

    private readonly List<StyleWptResult> _results = [];
    private readonly string _directory;
    private readonly string _file;
    private readonly string _root;
    private readonly string _relative;
    private readonly string _html;
    private DocumentStyles? _base;

    private StyleWptRunner(string directory, string root, string path)
    {
        _directory = directory;
        _root = root;
        _file = Path.GetFileName(path);
        _relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        _html = File.ReadAllText(path);
    }

    public static List<StyleWptResult> RunFile(string directory, string root, string path)
    {
        var r = new StyleWptRunner(directory, root, path);
        r.Run();
        return r._results;
    }

    private void Add(string test, WptOutcome o, string detail) => _results.Add(new StyleWptResult(_directory, _file, test, o, detail));

    private void NotApplicable(string test, string reason) => Add(test, WptOutcome.NotApplicable, reason);

    // ---- sources ----

    private StylesheetResource? Resolve(string url)
    {
        const string dataPrefix = "data:text/css,";
        if (url.StartsWith(dataPrefix, StringComparison.Ordinal))
        {
            // The caller supplies the bytes; a data: URL carries them inline.
            return new StylesheetResource(System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(url[dataPrefix.Length..])));
        }

        const string prefix = "https://wpt.test/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string rel = url[prefix.Length..];
        string path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? new StylesheetResource(File.ReadAllBytes(path)) : null;
    }

    private StyleSourceOptions Options() => new() { BaseUrl = "https://wpt.test/" + _relative, Resolver = Resolve };

    private DocumentStyles Styles(string html) => DocumentStyles.Create(Document.Parse(html), null, Options());

    private DocumentStyles BaseStyles() => _base ??= Styles(_html);

    // ---- dispatch ----

    private void Run()
    {
        Match rl = RefLink.Match(_html);
        if (rl.Success)
        {
            RunReftest(rl.Groups["rel"].Value.Equals("mismatch", StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (!_html.Contains("testharness.js", StringComparison.Ordinal))
        {
            NotApplicable("(file)", "not a test (reference or support file)");
            return;
        }

        string script = JsScript.ExtractScript(_html);
        if (_html.Contains("computed-testcommon.js", StringComparison.Ordinal) && script.Contains("test_computed_value", StringComparison.Ordinal))
        {
            RunComputedValues(script);
        }
        else if (Regex.IsMatch(script, @"\btestCases\b", RegexOptions.CultureInvariant) && script.Contains("createElement('style')", StringComparison.Ordinal) || script.Contains("createElement(\"style\")", StringComparison.Ordinal) && script.Contains("testCases", StringComparison.Ordinal))
        {
            RunTestCases(script);
        }
        else
        {
            RunTestBlocks(script);
        }
    }

    // ---- reference tests ----

    private void RunReftest(bool mismatch)
    {
        string name = "(reftest)";
        if (mismatch)
        {
            NotApplicable(name, "mismatch reference test: needs rendering");
            return;
        }

        string script = JsScript.ExtractScript(_html);
        if (script.Trim().Length > 0 && !_html.Contains("reftest-wait", StringComparison.Ordinal))
        {
            NotApplicable(name, "reference test needs script");
            return;
        }

        if (script.Trim().Length > 0)
        {
            NotApplicable(name, "reference test waits for script");
            return;
        }

        DocumentStyles s = BaseStyles();
        string? red = FindRed(s);

        // The reference decides whether red is expected: most tests say "no red", a few deliberately show red rectangles.
        Match href = Regex.Match(_html, @"<link[^>]+rel=[""']?match[""']?[^>]*href=[""']?(?<h>[^""'\s>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!href.Success)
        {
            href = Regex.Match(_html, @"<link[^>]+href=[""']?(?<h>[^""'\s>]+)[""']?[^>]*rel=[""']?match", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        string? refPath = href.Success ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.Combine(_root, _relative))!, href.Groups["h"].Value)) : null;
        if (refPath is null || !File.Exists(refPath))
        {
            NotApplicable(name, "the reference page is not in the vendored files");
            return;
        }

        string? refRed = FindRed(Styles(File.ReadAllText(refPath)));
        if ((red is null) == (refRed is null))
        {
            Add(name, WptOutcome.Pass, red is null ? "no red painted, as in the reference" : "red painted, as in the reference");
        }
        else
        {
            Add(name, WptOutcome.Fail, red is not null ? "paints red (the reference does not): " + red : "does not paint the red the reference paints");
        }
    }

    private static bool IsRed(CssColor c)
    {
        (double r, double g, double b, double a) = c.ToSrgb();
        return a > 0 && Math.Abs(r - 1) < 0.01 && g < 0.01 && b < 0.01;
    }

    // The runner paints nothing. A red background counts as hidden when an opaque non-red box could sit on top of it: a
    // descendant, or an absolutely positioned box anywhere (the reference tests stack a green square over the red one).
    private static bool IsCovered(DocumentStyles s, Node e)
    {
        static bool OpaqueNonRed(ComputedStyle st)
        {
            (double r, double g, double b, double a) = st.BackgroundColor.ToSrgb();
            return a >= 1 && !(r > 0.99 && g < 0.01 && b < 0.01);
        }

        foreach (Node d in s.Document.Root.DescendantElements)
        {
            ComputedStyle ds = s.GetStyle(d);
            if (OpaqueNonRed(ds) && ds.Display.Kind != DisplayKind.None && (ds.Position == PositionValue.Absolute || IsDescendant(d, e)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDescendant(Node d, Node e)
    {
        for (Node p = d.ParentElement; !p.IsNull; p = p.ParentElement)
        {
            if (p == e)
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindRed(DocumentStyles s)
    {
        foreach (Node e in s.Document.Root.DescendantElements)
        {
            // Skip anything inside a display:none subtree.
            bool hidden = false;
            for (Node p = e; !p.IsNull; p = p.ParentElement)
            {
                ComputedStyle ps = s.GetStyle(p);
                if (ps.Display.Kind == DisplayKind.None || ps.Opacity == 0)
                {
                    hidden = true;
                    break;
                }
            }

            if (hidden)
            {
                continue;
            }

            ComputedStyle st = s.GetStyle(e);
            if (st.Visibility != VisibilityValue.Visible)
            {
                continue;
            }

            string who = "<" + e.LocalName + (e.Id.Length > 0 ? " id=" + e.Id : string.Empty) + ">";
            if (IsRed(st.BackgroundColor) && !IsCovered(s, e))
            {
                return who + " background-color";
            }

            if (IsRed(st.Color) && e.ChildNodes.Any(c => c.Kind == NodeKind.Text && !string.IsNullOrWhiteSpace(c.Data.ToString())))
            {
                return who + " color";
            }

            foreach (string side in new[] { "top", "right", "bottom", "left" })
            {
                string style = st.GetPropertyValue("border-" + side + "-style");
                if (style is not ("none" or "hidden" or "") && CssColor.TryParse(st.GetComputedValue("border-" + side + "-color").Replace("currentcolor", st.Color.ToCss(), StringComparison.Ordinal), out CssColor bc) && IsRed(bc)
                    && st.GetPropertyValue("border-" + side + "-width") is not ("0" or "0px"))
                {
                    return who + " border-" + side + "-color";
                }
            }

            if (st.OutlineWidthPx > 0 && IsRed(st.OutlineColor))
            {
                return who + " outline-color";
            }
        }

        return null;
    }

    // ---- test_computed_value ----

    private void RunComputedValues(string script)
    {
        foreach (JsCall call in JsScript.FindCalls(script, 0, script.Length, n => n == "test_computed_value"))
        {
            if (call.Args.Count < 2 || call.Args[0].Parts.Count != 1 || call.Args[1].Parts.Count != 1 || call.Args[0].Parts[0] is not string prop || call.Args[1].Parts[0] is not string specified)
            {
                NotApplicable("test_computed_value", "arguments are not literals");
                continue;
            }

            string expected = specified;
            if (call.Args.Count >= 3)
            {
                if (call.Args[2].Parts.Count != 1 || call.Args[2].Parts[0] is not string exp)
                {
                    NotApplicable(prop + ": " + specified, "expected value is not a literal");
                    continue;
                }

                expected = exp;
            }

            string name = prop + ": " + specified;
            string? html = WithTargetStyle(prop, specified);
            if (html is null)
            {
                NotApplicable(name, "page has no #target");
                continue;
            }

            DocumentStyles s = Styles(html);
            Node target = s.Document.GetElementById("target");
            string actual = s.GetStyle(target).GetComputedValue(prop);
            if (ValuesEqual(expected, actual))
            {
                Add(name, WptOutcome.Pass, string.Empty);
            }
            else
            {
                Add(name, WptOutcome.Fail, $"expected '{expected}', got '{actual}'");
            }
        }
    }

    private string? WithTargetStyle(string prop, string value)
    {
        Match m = Regex.Match(_html, @"<(?<tag>\w+)(?<attrs>[^>]*\bid=[""']?target[""']?[^>]*)>", RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return null;
        }

        string declaration = prop + ": " + value;
        string escaped = declaration.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
        string tag = "<" + m.Groups["tag"].Value + m.Groups["attrs"].Value + " style=\"" + escaped + "\">";
        return string.Concat(_html.AsSpan(0, m.Index), tag, _html.AsSpan(m.Index + m.Length));
    }

    // ---- the testCases loop of the layer tests ----

    private void RunTestCases(string script)
    {
        Match arr = Regex.Match(script, @"(?:var|let|const)\s+testCases\s*=\s*\[", RegexOptions.CultureInvariant);
        if (!arr.Success)
        {
            NotApplicable("(testCases)", "no literal testCases table");
            return;
        }

        int open = arr.Index + arr.Length - 1;
        int close = JsScript.MatchClose(script, open);
        Match assertion = Regex.Match(script[close..], @"assert_equals\(\s*(?:window\.)?getComputedStyle\(target\)\.(?<p>\w+),\s*(?<q>['""])(?<v>[^'""]+)\k<q>", RegexOptions.CultureInvariant);
        Match selector = Regex.Match(script[close..], @"querySelectorAll\((['""])(?<s>[^'""]+)\1\)", RegexOptions.CultureInvariant);
        if (close < 0 || !assertion.Success || !selector.Success || script.Contains("inlineStyle", StringComparison.Ordinal))
        {
            NotApplicable("(testCases)", "the table's test body is not a plain computed-style comparison");
            return;
        }

        string prop = Kebab(assertion.Groups["p"].Value);
        string expected = assertion.Groups["v"].Value;
        foreach (string item in JsScript.SplitArgs(script, open + 1, close))
        {
            JsExpr obj = JsScript.ParseExpr(item);
            if (obj.Object is null || !obj.Object.TryGetValue("style", out JsExpr? st) || !obj.Object.TryGetValue("title", out JsExpr? ti) || st.Parts.Count != 1 || st.Parts[0] is not string css || ti.Parts.Count != 1 || ti.Parts[0] is not string title)
            {
                NotApplicable("(testCases entry)", "table entry is not a literal object");
                continue;
            }

            // The layer-import tests keep the imported sheets in a literal table and turn them into data: URLs.
            foreach (Match im in Regex.Matches(script, "[\"'](?<k>[\\w.-]+\\.css)[\"']\\s*:\\s*`(?<v>[^`]*)`", RegexOptions.CultureInvariant))
            {
                css = css.Replace("url(" + im.Groups["k"].Value + ")", "url(data:text/css," + Uri.EscapeDataString(im.Groups["v"].Value) + ")", StringComparison.Ordinal);
            }

            int head = _html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            string page = head >= 0 ? _html.Insert(head, "<style>" + css + "</style>") : "<style>" + css + "</style>" + _html;
            DocumentStyles s = Styles(page);
            List<Node> targets = s.Document.Root.QuerySelectorAll(selector.Groups["s"].Value);
            bool ok = targets.Count > 0;
            string detail = string.Empty;
            foreach (Node t in targets)
            {
                string actual = s.GetStyle(t).GetComputedValue(prop);
                if (!ValuesEqual(expected, actual))
                {
                    ok = false;
                    detail = $"<{t.LocalName} class={t.ClassName}> {prop} expected '{expected}', got '{actual}'";
                    break;
                }
            }

            Add(title, ok ? WptOutcome.Pass : WptOutcome.Fail, detail);
        }
    }

    // ---- test() blocks ----

    private void RunTestBlocks(string script)
    {
        var calls = JsScript.FindCalls(script, 0, script.Length, n => n is "test" or "async_test" or "promise_test" or "setup" or "add_completion_callback");
        // Top-level statements other than the tests: constants and element lookups are fine, anything else may change the page.
        var rest = new StringBuilder(script);
        foreach (JsCall c in calls.OrderByDescending(c => c.Position))
        {
            int open = script.IndexOf('(', c.Position);
            int close = JsScript.MatchClose(script, open);
            if (close > 0)
            {
                rest.Remove(c.Position, Math.Min(close + 1, script.Length) - c.Position);
            }
        }

        string leftover = Decl.Replace(rest.ToString(), string.Empty);
        leftover = Regex.Replace(leftover, @"['""]use strict['""];?|;", string.Empty, RegexOptions.CultureInvariant).Trim();
        bool pageScript = leftover.Length > 0;

        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Decl.Matches(script))
        {
            constants[m.Groups["n"].Value] = m.Groups["v"].Value.Trim();
        }

        var tests = calls.Where(c => c.Name is "test" or "async_test" or "promise_test").ToList();
        if (tests.Count == 0)
        {
            NotApplicable("(file)", pageScript ? "page script without a plain test()" : "no test() blocks (helper functions)");
            return;
        }

        foreach (JsCall c in tests)
        {
            string title = c.Args.Count > 1 && c.Args[1].Parts.Count == 1 && c.Args[1].Parts[0] is string t ? t : "(untitled)";
            if (c.Name != "test")
            {
                NotApplicable(title, c.Name + " needs the event loop");
                continue;
            }

            if (pageScript)
            {
                NotApplicable(title, "the page script changes the document or calls helpers");
                continue;
            }

            EvalBody(title, c.Args[0].Raw, constants);
        }
    }

    private void EvalBody(string title, string fn, Dictionary<string, string> constants)
    {
        int bodyStart = fn.IndexOf('{', StringComparison.Ordinal);
        string body;
        if (bodyStart >= 0 && (fn.StartsWith("function", StringComparison.Ordinal) || fn.Contains("=>", StringComparison.Ordinal) && fn.IndexOf("=>", StringComparison.Ordinal) < bodyStart))
        {
            int bodyEnd = JsScript.MatchClose(fn, bodyStart);
            if (bodyEnd < 0)
            {
                NotApplicable(title, "unreadable test body");
                return;
            }

            body = fn.Substring(bodyStart + 1, bodyEnd - bodyStart - 1);
        }
        else
        {
            NotApplicable(title, "unreadable test body");
            return;
        }

        var locals = new Dictionary<string, string>(constants, StringComparer.Ordinal);
        DocumentStyles s = BaseStyles();
        int asserts = 0;
        foreach (string raw in SplitStatements(body))
        {
            string st = raw.Trim();
            if (st.Length == 0)
            {
                continue;
            }

            Match d = Regex.Match(st, @"^(?:const|let|var)\s+(?<n>[A-Za-z_$][\w$]*)\s*=\s*(?<v>.+)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
            if (d.Success)
            {
                locals[d.Groups["n"].Value] = d.Groups["v"].Value.Trim();
                continue;
            }

            Match a = Regex.Match(st, @"^assert_equals\((?<args>.*)\)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
            if (!a.Success)
            {
                NotApplicable(title, "needs script: " + Snippet(st));
                return;
            }

            List<string> args = JsScript.SplitArgs(a.Groups["args"].Value, 0, a.Groups["args"].Value.Length);
            if (args.Count < 2)
            {
                NotApplicable(title, "needs script: " + Snippet(st));
                return;
            }

            Match read = ComputedRead.Match(args[0].Trim());
            if (!read.Success)
            {
                NotApplicable(title, "assertion reads something other than getComputedStyle: " + Snippet(args[0]));
                return;
            }

            string? expected = Literal(args[1].Trim(), locals);
            if (expected is null)
            {
                NotApplicable(title, "expected value is not a literal: " + Snippet(args[1]));
                return;
            }

            Node? el = Element(s, read.Groups["el"].Value.Trim(), locals);
            if (el is null)
            {
                NotApplicable(title, "element expression is not understood: " + Snippet(read.Groups["el"].Value));
                return;
            }

            string prop = read.Groups["name"].Success ? read.Groups["name"].Value : Kebab(read.Groups["prop"].Value);
            if (el.Value.IsNull)
            {
                Add(title, WptOutcome.Fail, "no element for " + read.Groups["el"].Value);
                return;
            }

            string actual = s.GetStyle(el.Value).GetComputedValue(prop);
            asserts++;
            if (!ValuesEqual(expected, actual))
            {
                Add(title, WptOutcome.Fail, $"{prop}: expected '{expected}', got '{actual}'");
                return;
            }
        }

        if (asserts == 0)
        {
            NotApplicable(title, "no computed-style assertion");
            return;
        }

        Add(title, WptOutcome.Pass, string.Empty);
    }

    private static string Snippet(string s)
    {
        s = Regex.Replace(s, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return s.Length <= 60 ? s : s[..60];
    }

    private static IEnumerable<string> SplitStatements(string body)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c is '\'' or '"' or '`')
            {
                i = JsScript.SkipString(body, i) - 1;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ';' && depth == 0)
            {
                yield return body[start..i];
                start = i + 1;
            }
        }

        yield return body[start..];
    }

    private static string? Literal(string expr, Dictionary<string, string> locals)
    {
        for (int guard = 0; guard < 4; guard++)
        {
            JsExpr e = JsScript.ParseExpr(expr);
            if (!e.Unknown && !e.Undefined && e.Object is null && e.Parts.Count == 1 && e.Parts[0] is string str)
            {
                return str;
            }

            if (e.Parts.Count == 1 && e.Parts[0] is JsVarRef v && locals.TryGetValue(v.Name, out string? next))
            {
                expr = next;
                continue;
            }

            return null;
        }

        return null;
    }

    private static Node? Element(DocumentStyles s, string expr, Dictionary<string, string> locals)
    {
        for (int guard = 0; guard < 4; guard++)
        {
            Match id = ById.Match(expr);
            if (id.Success)
            {
                return s.Document.GetElementById(id.Groups["id"].Value);
            }

            Match q = Query.Match(expr);
            if (q.Success)
            {
                return s.Document.Root.QuerySelector(q.Groups["s"].Value);
            }

            if (Regex.IsMatch(expr, @"^[A-Za-z_$][\w$]*$", RegexOptions.CultureInvariant))
            {
                if (locals.TryGetValue(expr, out string? next))
                {
                    expr = next.Trim();
                    continue;
                }

                // An element id is a global name in a document.
                Node byId = s.Document.GetElementById(expr);
                return byId.IsNull ? null : byId;
            }

            return null;
        }

        return null;
    }

    private static string Kebab(string camel)
    {
        if (camel == "cssFloat")
        {
            return "float";
        }

        var sb = new StringBuilder();
        foreach (char c in camel)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                sb.Append('-').Append((char)(c + 32));
            }
            else
            {
                sb.Append(c);
            }
        }

        string r = sb.ToString();
        return r.StartsWith("webkit-", StringComparison.Ordinal) ? "-" + r : r;
    }

    private static bool ValuesEqual(string expected, string actual)
    {
        string e = Collapse(expected);
        string a = Collapse(actual);
        if (e == a)
        {
            return true;
        }

        if (CssColor.TryParse(e, out CssColor ce) && CssColor.TryParse(a, out CssColor ca))
        {
            (double r1, double g1, double b1, double a1) = ce.ToSrgb();
            (double r2, double g2, double b2, double a2) = ca.ToSrgb();
            return Math.Abs(r1 - r2) < 0.002 && Math.Abs(g1 - g2) < 0.002 && Math.Abs(b1 - b2) < 0.002 && Math.Abs(a1 - a2) < 0.004;
        }

        return double.TryParse(e.Replace("px", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            && double.TryParse(a.Replace("px", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out double y) && Math.Abs(x - y) < 1e-6;
    }

    private static string Collapse(string s) => Regex.Replace(s.Trim(), @"\s+", " ", RegexOptions.CultureInvariant).ToLowerInvariant();
}
