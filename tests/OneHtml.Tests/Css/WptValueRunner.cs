using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>How one call in a web-platform-tests file came out.</summary>
internal enum WptOutcome
{
    Pass,
    Fail,
    NotApplicable,
}

/// <summary>The result of one call (one test) of a web-platform-tests file.</summary>
internal sealed record WptResult(string Directory, string File, string Call, string Input, WptOutcome Outcome, string Detail);

/// <summary>
/// Runs the parsing, serialization and evaluation calls of web-platform-tests files without a browser. Each call
/// of the harness functions (<c>test_valid_value</c>, <c>test_invalid_value</c>, <c>test_computed_value</c>,
/// <c>test_math_used</c>, the media-query helpers and the colour helpers) is replayed against this library; what
/// needs script, a document or layout is counted as not applicable.
/// </summary>
internal sealed class WptValueRunner
{
    private static readonly string[] Primitives =
    [
        "test_valid_value", "test_invalid_value", "test_computed_value",
        "fuzzy_test_valid_color", "fuzzy_test_valid_color_property", "fuzzy_test_computed_color", "fuzzy_test_computed_color_property",
        "test_math_used", "test_math_computed", "test_math_specified",
        "test_parsing",
        "query_should_be_known", "query_should_be_unknown", "query_should_be_css_parseable", "query_should_not_be_css_parseable",
        "query_should_be_js_parseable", "query_should_not_be_js_parseable",
        "query_should_be_parseable", "query_should_not_be_parseable",
        "expression_should_be_known", "expression_should_be_unknown",
        "should_apply", "should_not_apply",
        "test_plus_infinity", "test_minus_infinity", "test_plus_zero", "test_minus_zero", "test_nan",
    ];

    private static readonly HashSet<string> PrimitiveSet = new(Primitives, StringComparer.Ordinal);

    private static readonly Dictionary<string, QuantityKinds> QuantityProperties = BuildQuantityProperties();

    private readonly List<WptResult> _results = [];
    private readonly string _directory;
    private readonly string _file;
    private LengthContext _length = LengthContext.Default;
    private double _containerWidth = 100;
    private string?[]? _row;

    private WptValueRunner(string directory, string file)
    {
        _directory = directory;
        _file = file;
    }

    /// <summary>Runs one test file.</summary>
    /// <param name="directory">The directory it belongs to, for the report.</param>
    /// <param name="path">The HTML file.</param>
    public static List<WptResult> RunFile(string directory, string path)
    {
        var runner = new WptValueRunner(directory, Path.GetFileName(path));
        string html = File.ReadAllText(path);
        runner.ReadContainer(html);
        runner.Run(JsScript.ExtractScript(html));
        return runner._results;
    }

    private static Dictionary<string, QuantityKinds> BuildQuantityProperties()
    {
        var d = new Dictionary<string, QuantityKinds>(StringComparer.Ordinal);
        const QuantityKinds lp = QuantityKinds.LengthPercentage | QuantityKinds.UnitlessZeroLength;
        foreach (string p in new[]
        {
            "width", "height", "min-width", "min-height", "max-width", "max-height", "top", "left", "right", "bottom", "text-indent",
            "margin", "margin-top", "margin-right", "margin-bottom", "margin-left", "margin-inline-start", "margin-block-start",
            "padding-top", "padding-right", "padding-bottom", "padding-left", "flex-basis", "inline-size", "block-size",
            "border-top-left-radius", "text-underline-offset", "vertical-align", "word-spacing", "background-position-x",
        })
        {
            d[p] = lp;
        }

        foreach (string p in new[] { "letter-spacing", "transform-origin-z", "outline-offset", "border-top-width", "border-width", "column-gap" })
        {
            d[p] = QuantityKinds.Length | QuantityKinds.UnitlessZeroLength;
        }

        d["rotate"] = QuantityKinds.Angle;
        d["scale"] = QuantityKinds.Number | QuantityKinds.Percentage;
        d["opacity"] = QuantityKinds.Number | QuantityKinds.Percentage;
        d["z-index"] = QuantityKinds.Number | QuantityKinds.IntegerOnly;
        d["order"] = QuantityKinds.Number | QuantityKinds.IntegerOnly;
        d["transition-delay"] = QuantityKinds.Time;
        d["transition-duration"] = QuantityKinds.Time;
        d["animation-delay"] = QuantityKinds.Time;
        d["image-resolution"] = QuantityKinds.Resolution;
        d["flex-grow"] = QuantityKinds.Number;
        d["line-height"] = QuantityKinds.Number | lp;
        return d;
    }

    // ---- reading the page's fixed inputs ----

    // The pages fix their inputs in inline styles and one style sheet: the element's and the root's font size, and the
    // width that percentages resolve against. Only these simple forms are read; anything else keeps the defaults.
    private void ReadContainer(string html)
    {
        double? root = Px(html, @":root\s*\{[^}]*?font-size:\s*(?<n>[\d.]+)px");
        double? target = Px(html, @"#target\s*\{[^}]*?font-size:\s*(?<n>[\d.]+)px") ?? Px(html, @"id=""container""[^>]*style=""[^""]*?font-size:\s*(?<n>[\d.]+)px");
        if (root is { } r)
        {
            _length = _length with { RootFontSize = r, FontSize = r };
        }

        if (target is { } t)
        {
            _length = _length with { FontSize = t };
        }

        double? width = Px(html, @"#parent\s*\{[^}]*?(?<![-\w])width:\s*(?<n>[\d.]+)px");
        if (width is null)
        {
            foreach (Match m in Regex.Matches(html, @"style=""[^""]*?(?<![-\w])width:\s*(?<n>[\d.]+)px", RegexOptions.CultureInvariant))
            {
                width = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            }
        }

        if (width is { } w)
        {
            _containerWidth = w;
        }
    }

    private static double? Px(string html, string pattern)
    {
        Match m = Regex.Match(html, pattern, RegexOptions.CultureInvariant);
        return m.Success ? double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : null;
    }

    // ---- driving the script ----

    private void Run(string script)
    {
        _currentScript = script;
        List<JsFunction> functions = JsScript.FindFunctions(script);
        var wrappers = new Dictionary<string, JsFunction>(StringComparer.Ordinal);
        foreach (JsFunction f in functions)
        {
            if (!PrimitiveSet.Contains(f.Name))
            {
                wrappers[f.Name] = f;
            }
        }

        bool Known(string n) => PrimitiveSet.Contains(n) || wrappers.ContainsKey(n);

        // Bodies of functions with parameters run when called; so do the bodies of the no-parameter helpers the file calls.
        List<JsCall> all = JsScript.FindCalls(script, 0, script.Length, Known);
        var called = new HashSet<string>(StringComparer.Ordinal);
        var skip = new List<(int Start, int End)>();
        foreach (JsFunction f in functions)
        {
            if (f.Parameters.Count > 0)
            {
                skip.Add((f.BodyStart, f.BodyEnd));
            }
        }

        foreach (JsCall c in all)
        {
            if (wrappers.ContainsKey(c.Name) && !skip.Any(s => c.Position >= s.Start && c.Position < s.End))
            {
                called.Add(c.Name);
            }
        }

        foreach (JsFunction f in functions)
        {
            if (f.Parameters.Count == 0 && called.Contains(f.Name))
            {
                skip.Add((f.BodyStart, f.BodyEnd));
            }
        }

        var rowEvents = new List<(int Position, List<string?[]> Rows)>();
        foreach (Match m in Regex.Matches(script, @"\btests\s*=\s*\[", RegexOptions.CultureInvariant))
        {
            int open = m.Index + m.Length - 1;
            int close = JsScript.MatchClose(script, open);
            if (close > 0)
            {
                rowEvents.Add((m.Index, ParseRows(script, open, close)));
            }
        }

        var styleEvents = new List<(int Position, string Property, double Value)>();
        foreach (Match m in Regex.Matches(script, @"getElementById\('container'\)\.style\.(?<p>fontSize|width)\s*=\s*'(?<v>[\d.]+)px'", RegexOptions.CultureInvariant))
        {
            styleEvents.Add((m.Index, m.Groups["p"].Value, double.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture)));
        }

        int nextStyle = 0;
        List<string?[]>? rows = null;
        int nextRows = 0;
        foreach (JsCall c in all)
        {
            if (skip.Any(s => c.Position >= s.Start && c.Position < s.End))
            {
                continue;
            }

            while (nextStyle < styleEvents.Count && styleEvents[nextStyle].Position < c.Position)
            {
                (_, string property, double px) = styleEvents[nextStyle++];
                if (property == "fontSize")
                {
                    _length = _length with { FontSize = px };
                }
                else
                {
                    _containerWidth = px;
                }
            }

            while (nextRows < rowEvents.Count && rowEvents[nextRows].Position < c.Position)
            {
                rows = rowEvents[nextRows++].Rows;
            }

            bool usesRow = c.Args.Any(a => a.Parts.Any(p => p is JsRowRef)) || c.Args.Any(a => a.Raw.Contains("test[", StringComparison.Ordinal));
            if (usesRow)
            {
                if (rows is null)
                {
                    Record(c.Name, c.Args.Count > 0 ? c.Args[0].Raw : string.Empty, WptOutcome.NotApplicable, "no table");
                    continue;
                }

                foreach (string?[] row in rows)
                {
                    _row = row;
                    Execute(c, new Dictionary<string, object?>(), wrappers, 0);
                }

                _row = null;
                continue;
            }

            Execute(c, new Dictionary<string, object?>(), wrappers, 0);
        }
    }

    private static List<string?[]> ParseRows(string script, int open, int close)
    {
        var rows = new List<string?[]>();
        foreach (string rowText in JsScript.SplitArgs(script, open + 1, close))
        {
            string r = rowText.Trim();
            if (r.Length < 2 || r[0] != '[')
            {
                continue;
            }

            var cells = new List<string?>();
            foreach (string cell in JsScript.SplitArgs(r, 1, r.Length - 1))
            {
                JsExpr e = JsScript.ParseExpr(cell);
                cells.Add(e.Unknown || e.Undefined || e.Parts.Any(p => p is not string) ? null : string.Concat(e.Parts.Cast<string>()));
            }

            rows.Add([.. cells]);
        }

        return rows;
    }

    private void Execute(JsCall call, Dictionary<string, object?> env, Dictionary<string, JsFunction> wrappers, int depth)
    {
        if (wrappers.TryGetValue(call.Name, out JsFunction? fn))
        {
            if (depth > 6)
            {
                Record(call.Name, call.Args.FirstOrDefault()?.Raw ?? string.Empty, WptOutcome.NotApplicable, "nesting");
                return;
            }

            var inner = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < fn.Parameters.Count; i++)
            {
                string p = fn.Parameters[i];
                JsExpr? arg = i < call.Args.Count ? call.Args[i] : null;
                object? value = arg is null ? null : TryEval(arg, env);
                if (p.StartsWith('{'))
                {
                    foreach (string n in p.Trim('{', '}').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        inner[n] = arg?.Object is { } o && o.TryGetValue(n, out JsExpr? v) ? TryEval(v, env) : null;
                    }

                    continue;
                }

                inner[p.Split('=')[0].Trim()] = value;
            }

            string script = _currentScript!;
            foreach (JsCall c in JsScript.FindCalls(script, fn.BodyStart, fn.BodyEnd, n => PrimitiveSet.Contains(n) || wrappers.ContainsKey(n)))
            {
                Execute(c, inner, wrappers, depth + 1);
            }

            return;
        }

        Primitive(call, env);
    }

    private string? _currentScript;

    // Evaluates an expression; a failure is stored as a marker so that only the arguments a check reads can make it not applicable.
    private sealed record Unevaluable(string Reason);

    private object? TryEval(JsExpr e, Dictionary<string, object?> env)
    {
        if (e.Undefined)
        {
            return null;
        }

        if (e.Object is not null)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach ((string k, JsExpr v) in e.Object)
            {
                d[k] = TryEval(v, env);
            }

            return d;
        }

        if (e.Unknown)
        {
            return new Unevaluable("expression: " + Shorten(e.Raw));
        }

        var sb = new System.Text.StringBuilder();
        foreach (object part in e.Parts)
        {
            switch (part)
            {
                case string s:
                    sb.Append(s);
                    break;
                case JsRowRef r:
                    if (_row is null || r.Index >= _row.Length || _row[r.Index] is null)
                    {
                        return new Unevaluable("table cell");
                    }

                    sb.Append(_row[r.Index]);
                    break;
                case JsVarRef v:
                    if (!env.TryGetValue(v.Name, out object? val) || val is Unevaluable)
                    {
                        return new Unevaluable("variable " + v.Name);
                    }

                    if (val is not string vs)
                    {
                        return val is null && e.Parts.Count == 1 ? null : new Unevaluable("variable " + v.Name);
                    }

                    sb.Append(vs);
                    break;
                default:
                    return new Unevaluable("expression");
            }
        }

        return sb.ToString();
    }

    private static string Fmt(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..60];

    // ---- argument access ----

    private sealed class Args(List<object?> values)
    {
        public int Count => values.Count;

        public string Str(int i)
        {
            if (i >= values.Count || values[i] is null)
            {
                throw new WptNotApplicableException("missing argument " + i.ToString(CultureInfo.InvariantCulture));
            }

            return values[i] switch
            {
                string s => s,
                Unevaluable u => throw new WptNotApplicableException(u.Reason),
                _ => throw new WptNotApplicableException("argument type"),
            };
        }

        public string? Opt(int i)
        {
            if (i >= values.Count || values[i] is null)
            {
                return null;
            }

            return Str(i);
        }

        public Dictionary<string, object?>? Options(int i) => i < values.Count ? values[i] as Dictionary<string, object?> : null;
    }

    private void Primitive(JsCall call, Dictionary<string, object?> env)
    {
        var values = new List<object?>();
        foreach (JsExpr a in call.Args)
        {
            values.Add(TryEval(a, env));
        }

        var args = new Args(values);
        string input = values.Count > 0 && values[0] is string s0 ? s0 : call.Args.FirstOrDefault()?.Raw ?? string.Empty;
        if (values.Count > 1 && values[0] is string p0 && values[1] is string p1 && call.Name is "test_valid_value" or "test_invalid_value" or "test_computed_value" or "fuzzy_test_valid_color_property" or "fuzzy_test_computed_color_property")
        {
            input = p0 + ": " + p1;
        }

        try
        {
            string? failure = Check(call.Name, args);
            Record(call.Name, input, failure is null ? WptOutcome.Pass : WptOutcome.Fail, failure ?? string.Empty);
        }
        catch (WptNotApplicableException ex)
        {
            Record(call.Name, input, WptOutcome.NotApplicable, ex.Message);
        }
    }

    private void Record(string call, string input, WptOutcome outcome, string detail) =>
        _results.Add(new WptResult(_directory, _file, call, input, outcome, detail));

    // ---- the checks; null means pass ----

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private string? Check(string name, Args a)
    {
        switch (name)
        {
            case "test_valid_value":
                return Valid(a.Str(0), a.Str(1), a.Count > 2 ? a.Opt(2) : a.Str(1), fuzzy: false, eps: 0);
            case "test_invalid_value":
                return Invalid(a.Str(0), a.Str(1));
            case "test_computed_value":
                return Computed(a.Str(0), a.Str(1), a.Opt(2) ?? a.Str(1), fuzzy: false, eps: 0);
            case "fuzzy_test_valid_color":
                return Valid("color", a.Str(0), a.Opt(1) ?? a.Str(0), fuzzy: true, Eps(a, 2));
            case "fuzzy_test_valid_color_property":
                return Valid(a.Str(0), a.Str(1), a.Opt(2) ?? a.Str(1), fuzzy: true, Eps(a, 3));
            case "fuzzy_test_computed_color":
                return Computed("color", a.Str(0), a.Opt(1) ?? a.Str(0), fuzzy: a.Opt(1) is not null, Eps(a, 2));
            case "fuzzy_test_computed_color_property":
                return Computed(a.Str(0), a.Str(1), a.Opt(2) ?? a.Str(1), fuzzy: a.Opt(2) is not null, Eps(a, 3));
            case "test_math_used" or "test_math_computed":
                return MathCheck(a);
            case "test_parsing":
                return Equal(MediaQueryList.Parse(a.Str(0)).ToCss(), a.Opt(1) ?? a.Str(0));
            case "query_should_be_known":
                return JsParseable(a.Str(0)) ?? CssParseable(a.Str(0), true) ?? (Known(a.Str(0)) ? null : "not known");
            case "query_should_be_unknown":
                return JsParseable(a.Str(0)) ?? CssParseable(a.Str(0), true) ?? (Known(a.Str(0)) ? "known" : null);
            case "query_should_be_js_parseable":
                return JsParseable(a.Str(0));
            case "query_should_not_be_js_parseable":
                return JsParseable(a.Str(0)) is null ? "parseable" : null;
            case "query_should_be_css_parseable":
                return CssParseable(a.Str(0), true);
            case "query_should_not_be_css_parseable":
                return CssParseable(a.Str(0), false);
            case "query_should_be_parseable":
                return ParseableAfterScreen(a.Str(0)) ? null : "not parseable";
            case "query_should_not_be_parseable":
                return ParseableAfterScreen(a.Str(0)) ? "parseable" : null;
            case "expression_should_be_known":
                return Known("(" + a.Str(0) + ")") ? null : "not known";
            case "expression_should_be_unknown":
                return !ParseableAfterScreen("(" + a.Str(0) + ")") ? "not parseable" : Known("(" + a.Str(0) + ")") ? "known" : null;
            case "should_apply" or "should_not_apply":
                throw new WptNotApplicableException("depends on the test frame's geometry");
            default:
                throw new WptNotApplicableException("not run: " + name);
        }
    }

    private static double Eps(Args a, int i) => a.Opt(i) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0.01;

    private static string? Equal(string actual, string expected) => actual == expected ? null : $"got '{actual}', expected '{expected}'";

    // ---- media queries (Media Queries 5 §2, §3: "unknown" and the test-frame helpers of the suite) ----

    private static bool ParseableAfterScreen(string q) => MediaQueryList.Parse("screen, " + q).ToCss() != "screen, not all";

    private static bool Known(string q) => MediaQueryList.Parse($"{q}, not all and {q}").Matches(MediaFeatures.Default);

    private static string? JsParseable(string q) => Equal(MediaQueryList.Parse(q).ToCss(), q);

    private static string? CssParseable(string q, bool expected) =>
        (MediaQueryList.Parse(q).ToCss() != "not all") == expected ? null : expected ? "not parseable" : "parseable";

    // ---- property values ----

    private static bool IsColorProperty(string prop) => prop == "color" || prop.EndsWith("-color", StringComparison.Ordinal);

    private static ComponentValueList Parse(string value) => CssTree.ParseComponentValueList(value, cancellationToken: Ct).Values;

    private static string? Valid(string prop, string value, string? expected, bool fuzzy, double eps)
    {
        expected ??= value;
        ComponentValueList list = Parse(value);
        if (IsColorProperty(prop))
        {
            if (VarSubstitution.ContainsVar(list))
            {
                return VarSubstitution.IsValidSyntax(list) ? null : "invalid var()";
            }

            if (!CssColor.TryParse(list, out CssColor c))
            {
                return "does not parse";
            }

            return Compare(c.ToCss(), expected, fuzzy, eps);
        }

        if (VarSubstitution.ContainsVar(list))
        {
            return VarSubstitution.IsValidSyntax(list) ? null : "invalid var()";
        }

        if (prop == "opacity")
        {
            // The specified value keeps the number and turns a percentage literal into its number (Compositing 1, opacity).
            if (!CssQuantity.TryParse(list, QuantityKinds.Number | QuantityKinds.Percentage, out CssQuantity oq))
            {
                return "does not parse";
            }

            string text = !oq.FromMath && oq.Unit == CssUnit.Percent ? Fmt(oq.Value / 100) : oq.ToCss();
            return Compare(text, expected, false, 0);
        }

        if (QuantityProperties.TryGetValue(prop, out QuantityKinds kinds))
        {
            if (NeedsEnvironment(value))
            {
                throw new WptNotApplicableException("needs viewport, font metrics or layout");
            }

            if (!CssQuantity.TryParse(list, kinds, out CssQuantity q))
            {
                return "does not parse";
            }

            return Compare(q.ToCss(), expected, false, 0);
        }

        if (TypedProperties.IsTyped(prop))
        {
            if (!TypedProperties.IsValid(prop, list))
            {
                return "does not parse";
            }

            if (TypedProperties.TrySerialize(prop, list, out string css) && css != expected)
            {
                return $"got '{css}', expected '{expected}'";
            }

            return null;
        }

        throw new WptNotApplicableException("property not typed: " + prop);
    }

    private static string? Invalid(string prop, string value)
    {
        ComponentValueList list = Parse(value);
        if (VarSubstitution.ContainsVar(list) && !VarSubstitution.IsValidSyntax(list))
        {
            return null;
        }

        if (IsColorProperty(prop))
        {
            return CssColor.TryParse(list, out _) ? "parses" : null;
        }

        if (QuantityProperties.TryGetValue(prop, out QuantityKinds kinds))
        {
            return CssQuantity.TryParse(list, kinds, out _) ? "parses" : null;
        }

        if (TypedProperties.IsTyped(prop))
        {
            return TypedProperties.IsValid(prop, list) ? "parses" : null;
        }

        throw new WptNotApplicableException("property not typed: " + prop);
    }

    private string? Computed(string prop, string value, string expected, bool fuzzy, double eps)
    {
        ComponentValueList list = Parse(value);
        if (IsColorProperty(prop))
        {
            if (VarSubstitution.ContainsVar(list))
            {
                throw new WptNotApplicableException("var() needs the cascade");
            }

            if (!CssColor.TryParse(list, out CssColor c))
            {
                return "does not parse";
            }

            return Compare(c.Resolve(CssColor.FromRgb(0, 0, 0)).ToCss(), expected, fuzzy, eps);
        }

        if (prop == "opacity")
        {
            if (!TypedProperties.TryParseOpacity(list, out double opacity))
            {
                return "does not parse";
            }

            return Compare(Fmt(opacity), expected, false, 0);
        }

        if (QuantityProperties.TryGetValue(prop, out QuantityKinds kinds))
        {
            if (NeedsEnvironment(value) || NeedsEnvironment(expected))
            {
                throw new WptNotApplicableException("needs viewport, font metrics or layout");
            }

            if (!CssQuantity.TryParse(list, kinds, out CssQuantity q))
            {
                return "does not parse";
            }

            if (!CssQuantity.TryParse(Parse(expected), kinds, out CssQuantity e))
            {
                throw new WptNotApplicableException("expected value is not a quantity");
            }

            if ((q.IsPercentage || e.IsPercentage) && !double.IsNaN(q.Resolve(_length, 0)))
            {
                return Compare(q.ToCss(), expected, false, 0);
            }

            // Computed values replace NaN by 0 (Values 4 §10.9.3, "NaN" in calculations).
            double qv = q.Resolve(_length);
            return Numbers(double.IsNaN(qv) ? 0 : qv, e.Resolve(_length), kinds, 0.001);
        }

        throw new WptNotApplicableException("computed value of an untyped property: " + prop);
    }

    private static bool NeedsEnvironment(string text) =>
        Regex.IsMatch(text, @"[\d.](v|sv|lv|dv|cq)(w|h|i|b|min|max)\b|[\d.](lh|rlh|ch|rch|ex|rex|cap|rcap|ic|ric)\b|random\(|attr\(|(^|[^\w-])(random|sibling-index|sibling-count)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? Compare(string actual, string expected, bool fuzzy, double eps)
    {
        if (!fuzzy)
        {
            return actual == expected ? null : $"got '{actual}', expected '{expected}'";
        }

        // color-testcommon.js: the numbers agree within eps and the text with the numbers removed is identical.
        List<double> an = NumbersIn(actual);
        List<double> en = NumbersIn(expected);
        string at = Regex.Replace(actual, @"[0-9.]", string.Empty, RegexOptions.CultureInvariant);
        string et = Regex.Replace(expected, @"[0-9.]", string.Empty, RegexOptions.CultureInvariant);
        bool ok = an.Count == en.Count && at == et;
        for (int i = 0; ok && i < an.Count; i++)
        {
            ok = Math.Abs(an[i] - en[i]) <= eps || (double.IsNaN(an[i]) && double.IsNaN(en[i]));
        }

        return ok ? null : $"got '{actual}', expected '{expected}'";
    }

    private static List<double> NumbersIn(string s)
    {
        var list = new List<double>();
        foreach (string part in s.Split([' ', '(', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            Match m = Regex.Match(part, @"^[+-]?(\d+\.?\d*|\.\d+)(e[+-]?\d+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
            {
                list.Add(double.Parse(m.Value, CultureInfo.InvariantCulture));
            }
        }

        return list;
    }

    // ---- math functions (numeric-testcommon.js: the used or computed value of a calculation compared with the expected) ----

    private string? MathCheck(Args a)
    {
        string test = a.Str(0);
        string expected = a.Str(1);
        Dictionary<string, object?>? o = a.Options(2);
        string type = o is not null && o.TryGetValue("type", out object? t) && t is string ts ? ts : "length";
        if (o is not null && o.TryGetValue("prop", out object? p) && p is not null)
        {
            throw new WptNotApplicableException("explicit property");
        }

        double approx = o is not null && o.TryGetValue("approx", out object? ap) && ap is string aps && double.TryParse(aps, NumberStyles.Float, CultureInfo.InvariantCulture, out double ad) ? ad : 0.001;
        QuantityKinds kinds = type switch
        {
            "number" => QuantityKinds.Number,
            "integer" => QuantityKinds.Number | QuantityKinds.IntegerOnly,
            "length" => QuantityKinds.LengthPercentage,
            "angle" => QuantityKinds.Angle,
            "time" => QuantityKinds.Time,
            "resolution" => QuantityKinds.Resolution,
            _ => throw new WptNotApplicableException("type " + type),
        };
        if (NeedsEnvironment(test) || NeedsEnvironment(expected))
        {
            throw new WptNotApplicableException("needs viewport, font metrics or layout");
        }

        if (!CssQuantity.TryParse(Parse(test), kinds, out CssQuantity q))
        {
            return "does not parse";
        }

        if (!CssQuantity.TryParse(Parse(expected), kinds, out CssQuantity e))
        {
            return "expected does not parse";
        }

        double basis = kinds.HasFlag(QuantityKinds.Percentage) ? _containerWidth : 1;
        return Numbers(q.Resolve(_length, basis), e.Resolve(_length, basis), kinds, approx);
    }

    private static string? Numbers(double actual, double expected, QuantityKinds kinds, double approx)
    {
        if (kinds.HasFlag(QuantityKinds.IntegerOnly) && !double.IsNaN(actual) && !double.IsInfinity(actual))
        {
            actual = Math.Floor(actual + 0.5);
            expected = Math.Floor(expected + 0.5);
        }

        if (double.IsNaN(actual) && double.IsNaN(expected))
        {
            return null;
        }

        if (actual.Equals(expected))
        {
            return null;
        }

        double tolerance = Math.Max(approx, Math.Abs(expected) * 1e-6);
        return Math.Abs(actual - expected) <= tolerance ? null : $"got {actual.ToString("R", CultureInfo.InvariantCulture)}, expected {expected.ToString("R", CultureInfo.InvariantCulture)}";
    }
}
