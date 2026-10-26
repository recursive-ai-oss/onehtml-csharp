using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Editing;

/// <summary>
/// Random stylesheet edits over the vendored suites, our own sheets and generated sheets. Every successful transaction
/// must leave the bytes outside its reported ranges alone, produce the declared change, and (on regular generated
/// sheets without fallback) be undone exactly by the inverse edit.
/// </summary>
public class SheetEditPropertyTests
{
    private static readonly string[] s_props = ["color", "margin", "--x", "background", "display", "Width", "font-family", "--Y"];
    private static readonly string[] s_values = ["red", "0", "1px solid #fff", "calc(1px + 2px)", "var(--x)", "url(a.png)", "\"s\"", "a b c", "rgb(1 2 3 / 50%)", "{a}", "1px/2px", "\\31 23"];
    private static readonly string[] s_rules = ["x{y:z}", "@media print{a{b:c}}", "@import \"a.css\";", "p, q > r {\n  s: t;\n}", "a{}", "@font-face{src:url(x)}", "u { v: w; x: y !important }", "  b  { c : d }  "];

    private sealed class Counts
    {
        public Dictionary<string, int[]> PerOp = [];

        public void Add(string op, bool ok, bool fallback, bool invalid)
        {
            if (!PerOp.TryGetValue(op, out int[]? c))
            {
                c = new int[4];
                PerOp[op] = c;
            }

            c[0]++;
            if (ok)
            {
                c[1]++;
            }

            if (fallback)
            {
                c[2]++;
            }

            if (invalid)
            {
                c[3]++;
            }
        }

        public override string ToString() =>
            string.Join("; ", PerOp.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}: {k.Value[0]} tried, {k.Value[1]} applied, {k.Value[2]} fallback, {k.Value[3]} invalid/failed"));
    }

    internal static IEnumerable<string> Inputs()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "testdata", "css");
        foreach (string file in Directory.GetFiles(Path.Combine(root, "css-parsing-tests"), "*.json"))
        {
            JsonArray cases = JsonNode.Parse(File.ReadAllText(file))!.AsArray();
            for (int i = 0; i + 1 < cases.Count; i += 2)
            {
                if (cases[i] is JsonValue v && v.TryGetValue(out string? s))
                {
                    yield return s;
                }
            }
        }

        foreach (string file in Directory.GetFiles(Path.Combine(root, "own"), "*.css"))
        {
            yield return File.ReadAllText(file);
        }

        foreach (string file in Directory.GetFiles(Path.Combine(root, "wpt", "css"), "*.html", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, "<style[^>]*>(.*?)</style>", RegexOptions.Singleline))
            {
                yield return m.Groups[1].Value;
            }
        }
    }

    /// <summary>A regular sheet: consistent layout, so that inverse edits can restore it exactly.</summary>
    internal static string Generate(Random rng)
    {
        string nl = rng.Next(4) == 0 ? "\r\n" : "\n";
        string unit = rng.Next(3) switch { 0 => "\t", 1 => "    ", _ => "  " };
        string colon = rng.Next(4) == 0 ? ":" : ": ";
        bool term = rng.Next(5) != 0;
        string gap = rng.Next(2) == 0 ? nl + nl : nl;
        var sb = new StringBuilder();
        int rules = 1 + rng.Next(6);
        for (int r = 0; r < rules; r++)
        {
            if (r > 0)
            {
                sb.Append(gap);
            }

            if (rng.Next(5) == 0)
            {
                sb.Append("@media (min-width: 600px) {").Append(nl);
                int inner = 1 + rng.Next(3);
                for (int k = 0; k < inner; k++)
                {
                    if (k > 0)
                    {
                        sb.Append(nl);
                    }

                    Block(sb, "." + "abc"[k] + "x", unit, 1, nl, colon, term, rng);
                }

                sb.Append(nl).Append('}');
            }
            else
            {
                Block(sb, "div.c" + r, unit, 0, nl, colon, term, rng);
            }
        }

        sb.Append(nl);
        return sb.ToString();
    }

    private static readonly string[] s_ws = ["", " ", "\n", "\n\n", "\r\n", "\t", "  \n  ", " /* c */ ", "\n/* note */\n"];

    /// <summary>An irregular but valid sheet: random spacing, comments, nested blocks, at-rules, unterminated tails.</summary>
    internal static string GenerateMessy(Random rng)
    {
        var sb = new StringBuilder();
        int top = 1 + rng.Next(6);
        for (int i = 0; i < top; i++)
        {
            sb.Append(s_ws[rng.Next(s_ws.Length)]);
            MessyRule(sb, rng, 0, i == top - 1);
        }

        sb.Append(s_ws[rng.Next(s_ws.Length)]);
        return sb.ToString();
    }

    private static void MessyRule(StringBuilder sb, Random rng, int depth, bool last)
    {
        switch (rng.Next(depth > 1 ? 3 : 8))
        {
            case 0:
                sb.Append("@import url(a.css)").Append(last && rng.Next(3) == 0 ? string.Empty : ";");
                return;
            case 1:
                sb.Append("@layer base").Append(rng.Next(2) == 0 ? ", utilities;" : " {" + s_ws[rng.Next(s_ws.Length)] + "p{q:r}" + s_ws[rng.Next(s_ws.Length)] + "}");
                return;
            case 2:
                sb.Append("@font-face").Append(s_ws[rng.Next(s_ws.Length)]).Append('{').Append(s_ws[rng.Next(s_ws.Length)]).Append("font-family").Append(rng.Next(2) == 0 ? ":" : " : ").Append("x;src:url(f.woff)").Append(s_ws[rng.Next(s_ws.Length)]).Append('}');
                return;
            case 3 or 4:
                sb.Append(rng.Next(2) == 0 ? "@media (min-width:1px)" : "@supports (display:grid)").Append(s_ws[rng.Next(s_ws.Length)]).Append('{');
                int n = rng.Next(4);
                for (int i = 0; i < n; i++)
                {
                    sb.Append(s_ws[rng.Next(s_ws.Length)]);
                    MessyRule(sb, rng, depth + 1, false);
                }

                sb.Append(s_ws[rng.Next(s_ws.Length)]).Append('}');
                return;
            default:
                break;
        }

        sb.Append(rng.Next(3) switch { 0 => "a", 1 => ".b > c, d:hover", _ => "#e" }).Append(s_ws[rng.Next(s_ws.Length)]).Append('{');
        int decls = rng.Next(5);
        for (int d = 0; d < decls; d++)
        {
            sb.Append(s_ws[rng.Next(s_ws.Length)]).Append(s_props[rng.Next(s_props.Length)]).Append(s_ws[rng.Next(3)]).Append(':').Append(s_ws[rng.Next(3)]).Append(s_values[rng.Next(5)]);
            if (rng.Next(6) == 0)
            {
                sb.Append(" !important");
            }

            if (d < decls - 1 || rng.Next(2) == 0)
            {
                sb.Append(';');
            }
        }

        if (depth < 2 && rng.Next(4) == 0)
        {
            sb.Append(s_ws[rng.Next(s_ws.Length)]).Append("&:hover").Append(s_ws[rng.Next(s_ws.Length)]).Append("{x:y}");
        }

        sb.Append(s_ws[rng.Next(s_ws.Length)]).Append('}');
    }

    private static void Block(StringBuilder sb, string selector, string unit, int depth, string nl, string colon, bool term, Random rng)
    {
        string ind = string.Concat(Enumerable.Repeat(unit, depth));
        sb.Append(ind).Append(selector).Append(" {").Append(nl);
        int n = rng.Next(4);
        for (int d = 0; d < n; d++)
        {
            sb.Append(ind).Append(unit).Append(s_props[d]).Append(colon).Append(s_values[rng.Next(4)]);
            if (term || d < n - 1)
            {
                sb.Append(';');
            }

            sb.Append(nl);
        }

        sb.Append(ind).Append('}');
    }

    internal static List<CssNode> Items(Stylesheet s)
    {
        var list = new List<CssNode>();
        var stack = new Stack<CssNode>();
        var top = s.Rules.ToList();
        for (int i = top.Count - 1; i >= 0; i--)
        {
            stack.Push(top[i]);
        }

        while (stack.Count > 0)
        {
            CssNode n = stack.Pop();
            list.Add(n);
            var kids = n.Children.ToList();
            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        return list;
    }

    private static List<int> PathOf(CssNode n)
    {
        var path = new List<int>();
        for (CssNode c = n; c.Kind != CssNodeKind.Root; c = c.Parent)
        {
            int k = 0;
            foreach (CssNode sib in c.Parent.Children)
            {
                if (sib == c)
                {
                    break;
                }

                k++;
            }

            path.Add(k);
        }

        path.Reverse();
        return path;
    }

    private static CssNode? Navigate(Stylesheet s, List<int> path)
    {
        CssNode cur = s.Tree.Root;
        foreach (int k in path)
        {
            CssNode? next = null;
            int i = 0;
            foreach (CssNode c in cur.Children)
            {
                if (i++ == k)
                {
                    next = c;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            cur = next.Value;
        }

        return cur;
    }

    private static SheetEdit? RandomEdit(Stylesheet s, List<CssNode> items, Random rng, out string label)
    {
        label = string.Empty;
        List<CssNode> rules = items.Where(n => n.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule).ToList();
        List<CssNode> blocks = rules.Where(n => n.HasBlock).ToList();
        switch (rng.Next(7))
        {
            case 0 or 1 when blocks.Count > 0:
                {
                    CssNode r = blocks[rng.Next(blocks.Count)];
                    var decls = r.Declarations.ToList();
                    string prop = decls.Count > 0 && rng.Next(2) == 0 ? decls[rng.Next(decls.Count)].Name : s_props[rng.Next(s_props.Length)];
                    label = "setDeclaration";
                    return SheetEdit.SetDeclaration(s, r, prop, s_values[rng.Next(s_values.Length)], rng.Next(4) == 0);
                }

            case 2 when blocks.Count > 0:
                {
                    CssNode r = blocks[rng.Next(blocks.Count)];
                    var decls = r.Declarations.ToList();
                    string prop = decls.Count > 0 && rng.Next(3) != 0 ? decls[rng.Next(decls.Count)].Name : s_props[rng.Next(s_props.Length)];
                    label = "removeDeclaration";
                    return SheetEdit.RemoveDeclaration(s, r, prop);
                }

            case 3 when rules.Count > 0:
                {
                    CssNode r = rules[rng.Next(rules.Count)];
                    label = "insertRule";
                    string css = s_rules[rng.Next(s_rules.Length)];
                    return rng.Next(2) == 0 ? SheetEdit.InsertRuleBefore(s, r, css) : SheetEdit.InsertRuleAfter(s, r, css);
                }

            case 4:
                {
                    label = "insertRule";
                    CssNode? c = blocks.Count > 0 && rng.Next(3) == 0 ? blocks[rng.Next(blocks.Count)] : null;
                    return SheetEdit.AppendRule(s, s_rules[rng.Next(s_rules.Length)], c);
                }

            case 5 when rules.Count > 0:
                label = "removeRule";
                return SheetEdit.RemoveRule(s, rules[rng.Next(rules.Count)]);
            case 6 when rules.Count > 0:
                label = "replaceRule";
                return SheetEdit.ReplaceRule(s, rules[rng.Next(rules.Count)], s_rules[rng.Next(s_rules.Length)]);
            default:
                return null;
        }
    }

    /// <summary>The bytes outside the reported ranges are the old bytes, and the ranges' new text rebuilds the new sheet.</summary>
    private static void CheckRanges(string oldText, StylesheetChangeSet c)
    {
        string newText = c.NewSheet.Text;
        var sb = new StringBuilder();
        int pos = 0;
        int newPos = 0;
        foreach (StylesheetEditedRange r in c.Ranges)
        {
            Assert.True(r.OldRange.Start >= pos && r.OldRange.End >= r.OldRange.Start, "ranges ordered");
            string between = oldText[pos..r.OldRange.Start];
            Assert.Equal(between, newText.Substring(newPos, between.Length));
            sb.Append(between);
            sb.Append(newText, r.NewRange.Start, r.NewRange.End - r.NewRange.Start);
            newPos = r.NewRange.End;
            pos = r.OldRange.End;
        }

        sb.Append(oldText, pos, oldText.Length - pos);
        Assert.Equal(newText, sb.ToString());
    }

    [Fact]
    public void Random_transactions_on_suite_and_own_sheets()
    {
        var rng = new Random(20261025);
        var counts = new Counts();
        var reasons = new Dictionary<string, int>();
        int sheets = 0;
        int transactions = 0;
        foreach (string css in Inputs())
        {
            if (css.Length > 6000 || css.Length == 0)
            {
                continue;
            }

            sheets++;
            Stylesheet s;
            try
            {
                s = Stylesheet.Parse(css);
            }
            catch (CssLimitExceededException)
            {
                continue;
            }

            for (int round = 0; round < 3; round++)
            {
                List<CssNode> items = Items(s);
                var edits = new List<SheetEdit>();
                string label = string.Empty;
                int n = 1 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    SheetEdit? e = RandomEdit(s, items, rng, out string l);
                    if (e is not null)
                    {
                        edits.Add(e);
                        label = l;
                    }
                }

                if (edits.Count == 0)
                {
                    continue;
                }

                transactions++;
                BatchResult r = s.Apply(edits);
                string op = edits.Count == 1 ? label : "mixed";
                if (!r.Succeeded)
                {
                    // Only the library's refusals are allowed, and they must say why.
                    Assert.Contains(r.Outcomes, o => o.Status is EditStatus.Invalid or EditStatus.Failed);
                    counts.Add(op, false, false, true);
                    string why = r.Outcomes.FirstOrDefault(o => o.Reason is not null)?.Reason ?? r.Failure ?? "?";
                    why = Regex.Replace(why, "[0-9]+", "N");
                    if (why.Length > 70)
                    {
                        why = why[..70];
                    }

                    reasons[op + ": " + why] = reasons.GetValueOrDefault(op + ": " + why) + 1;
                    continue;
                }

                StylesheetChangeSet c = r.Sheets[0].Changes;
                CheckRanges(s.Text, c);
                counts.Add(op, true, c.Ranges.Any(x => x.IsFallback), false);
                // The next round edits the new snapshot.
                s = r.Sheets[0].NewSheet;
                Assert.Equal(CssSourceWriter.Write(s.Tree), s.Text);
            }
        }

        Assert.True(sheets > 100);
        Assert.True(transactions > 300);
        TestContext.Current.SendDiagnosticMessage($"suite/own sheets: {sheets} sheets, {transactions} transactions. {counts}");
        TestContext.Current.SendDiagnosticMessage("refusals: " + string.Join(" | ", reasons.OrderByDescending(k => k.Value).Take(12).Select(k => k.Value + "x " + k.Key)));
        foreach (KeyValuePair<string, int[]> kv in counts.PerOp)
        {
            // Failures to apply are fine on hostile inputs, silent damage is not; fallbacks stay a small minority.
            Assert.True(kv.Value[1] > 0 || kv.Value[0] < 5, kv.Key);
        }
    }

    [Fact]
    public void Random_transaction_chains_on_messy_sheets()
    {
        var rng = new Random(424242);
        var counts = new Counts();
        var reasons = new Dictionary<string, int>();
        int transactions = 0;
        int samples = 0;
        for (int iter = 0; iter < 1500; iter++)
        {
            Stylesheet s = Stylesheet.Parse(GenerateMessy(rng));
            for (int round = 0; round < 6; round++)
            {
                List<CssNode> items = Items(s);
                var edits = new List<SheetEdit>();
                var labels = new List<string>();
                int n = rng.Next(3) == 0 ? 1 + rng.Next(4) : 1;
                for (int k = 0; k < n; k++)
                {
                    SheetEdit? e = RandomEdit(s, items, rng, out string l);
                    if (e is not null)
                    {
                        edits.Add(e);
                        labels.Add(l);
                    }
                }

                if (edits.Count == 0)
                {
                    continue;
                }

                transactions++;
                BatchResult r = s.Apply(edits);
                string op = edits.Count == 1 ? labels[0] : "mixed";
                if (!r.Succeeded)
                {
                    Assert.Contains(r.Outcomes, o => o.Status is EditStatus.Invalid or EditStatus.Failed);
                    counts.Add(op, false, false, true);
                    if (r.Failure!.Contains("re-parses", StringComparison.Ordinal) && samples++ < 6)
                    {
                        TestContext.Current.SendDiagnosticMessage("SAMPLE " + r.Failure + " SHEET<<" + s.Text + ">> EDITS " + string.Join("; ", edits.Select(e => e.Operation + " " + e.Property + "=" + e.Value + e.Css + " @" + (e.HasTarget ? e.Target.Raw.ToString() : "end"))));
                    }

                    string why = Regex.Replace(r.Failure ?? "?", "[0-9]+", "N");
                    reasons[op + ": " + (why.Length > 110 ? why[..110] : why)] = reasons.GetValueOrDefault(op + ": " + why) + 1;
                    continue;
                }

                StylesheetChangeSet c = r.Sheets[0].Changes;
                CheckRanges(s.Text, c);
                counts.Add(op, true, c.Ranges.Any(x => x.IsFallback), false);
                if (c.Ranges.Any(x => x.IsFallback) && samples++ < 6)
                {
                    TestContext.Current.SendDiagnosticMessage("FALLBACK SHEET<<" + s.Text + ">> EDITS " + string.Join("; ", edits.Select(e => e.Operation + " " + e.Property + "=" + e.Value + e.Css + " @" + (e.HasTarget ? e.Target.Raw.ToString() : "end"))) + " RESULT<<" + c.NewSheet.Text + ">>");
                }

                if (edits.Count == 1)
                {
                    SemanticCheck(s, c.NewSheet, edits[0]);
                }

                s = c.NewSheet;
            }
        }

        TestContext.Current.SendDiagnosticMessage($"messy sheets: {transactions} transactions. {counts}");
        TestContext.Current.SendDiagnosticMessage("messy refusals: " + string.Join(" | ", reasons.OrderByDescending(k => k.Value).Take(10).Select(k => k.Value + "x " + k.Key)));
        int total = counts.PerOp.Values.Sum(v => v[0]);
        int conflicts = reasons.Where(k => k.Key.Contains("change the same part", StringComparison.Ordinal)).Sum(k => k.Value);
        int refused = counts.PerOp.Values.Sum(v => v[3]) - conflicts;
        Assert.True(refused == 0, $"refused {refused} of {total}");
    }

    /// <summary>
    /// The narrow verification (only the stretch of rules around the edits is re-parsed) must give exactly what parsing
    /// the whole sheet gives: same success, same text, same ranges and levels, on messy sheets, with several edits per
    /// transaction so the stretches are wide.
    /// </summary>
    [Fact]
    public void Narrow_verification_equals_whole_sheet_verification()
    {
        var rng = new Random(777);
        var whole = new EditOptions { VerifyWholeStylesheet = true };
        int transactions = 0, ok = 0;
        for (int iter = 0; iter < 1500; iter++)
        {
            Stylesheet s = Stylesheet.Parse(iter % 3 == 0 ? Generate(rng) : GenerateMessy(rng));
            for (int round = 0; round < 5; round++)
            {
                List<CssNode> items = Items(s);
                var edits = new List<SheetEdit>();
                int n = 1 + (rng.Next(3) == 0 ? rng.Next(6) : 0);
                for (int k = 0; k < n; k++)
                {
                    SheetEdit? e = RandomEdit(s, items, rng, out _);
                    if (e is not null)
                    {
                        edits.Add(e);
                    }
                }

                if (edits.Count == 0)
                {
                    continue;
                }

                transactions++;
                BatchResult narrow = s.Apply(edits);
                BatchResult full = s.Apply(edits, whole);
                Assert.Equal(full.Succeeded, narrow.Succeeded);
                if (!narrow.Succeeded)
                {
                    Assert.Equal(full.Outcomes.Select(o => o.Status), narrow.Outcomes.Select(o => o.Status));
                    continue;
                }

                ok++;
                StylesheetChangeSet a = narrow.Sheets[0].Changes;
                StylesheetChangeSet b = full.Sheets[0].Changes;
                Assert.Equal(b.NewSheet.Text, a.NewSheet.Text);
                Assert.Equal(b.Ranges.Select(r => (r.OldRange, r.NewRange, r.FallbackLevel)), a.Ranges.Select(r => (r.OldRange, r.NewRange, r.FallbackLevel)));
                s = a.NewSheet;
            }
        }

        TestContext.Current.SendDiagnosticMessage($"narrow vs whole-sheet verification: {transactions} transactions, {ok} applied, identical");
        Assert.True(ok > transactions / 2);
    }

    [Fact]
    public void Generated_sheets_single_edits_have_exact_inverses()
    {
        var rng = new Random(77);
        var counts = new Counts();
        int checkedInverse = 0;
        for (int iter = 0; iter < 4000; iter++)
        {
            string css = Generate(rng);
            Stylesheet s = Stylesheet.Parse(css);
            List<CssNode> items = Items(s);
            SheetEdit? e = RandomEdit(s, items, rng, out string label);
            if (e is null)
            {
                continue;
            }

            BatchResult r = s.Apply([e]);
            if (!r.Succeeded)
            {
                counts.Add(label, false, false, true);
                continue;
            }

            StylesheetChangeSet c = r.Sheets[0].Changes;
            CheckRanges(s.Text, c);
            bool fallback = c.Ranges.Any(x => x.IsFallback);
            counts.Add(label, true, fallback, false);
            Stylesheet n = r.Sheets[0].NewSheet;
            SemanticCheck(s, n, e);
            if (fallback)
            {
                continue;
            }

            SheetEdit? inverse = Inverse(s, n, e, c);
            if (inverse is null)
            {
                continue;
            }

            BatchResult back = n.Apply([inverse]);
            Assert.True(back.Succeeded, back.Failure + "\n" + s.Text + "\n---\n" + n.Text);
            Assert.True(string.Equals(s.Text, back.Sheets[0].NewSheet.Text, StringComparison.Ordinal), $"inverse of {e} did not restore:\n{Show(s.Text)}\n--- after\n{Show(n.Text)}\n--- back\n{Show(back.Sheets[0].NewSheet.Text)}");
            checkedInverse++;
        }

        Assert.True(checkedInverse > 800, checkedInverse.ToString());
        TestContext.Current.SendDiagnosticMessage($"generated sheets: {checkedInverse} inverses restored exactly. {counts}");
        int total = counts.PerOp.Values.Sum(v => v[0]);
        int fallbacks = counts.PerOp.Values.Sum(v => v[2]);
        Assert.True(fallbacks * 100 < total, $"fallbacks {fallbacks} of {total}");
    }

    private static string Show(string s) => s.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);

    private static void SemanticCheck(Stylesheet before, Stylesheet after, SheetEdit e)
    {
        switch (e.Operation)
        {
            case SheetEditOperation.SetDeclaration:
                {
                    CssNode r = Navigate(after, PathOf(e.Target))!.Value;
                    CssNode? last = null;
                    foreach (CssNode d in r.Declarations)
                    {
                        if (string.Equals(d.Name, e.Property, e.Property!.StartsWith("--", StringComparison.Ordinal) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                        {
                            last = d;
                        }
                    }

                    Assert.NotNull(last);
                    Assert.Equal(e.Important, last!.Value.IsImportant);
                    Assert.Equal(SheetLayout_Norm(e.Value!), SheetLayout_Norm(last.Value.ValueText.ToString()));
                    break;
                }

            case SheetEditOperation.RemoveDeclaration:
                {
                    CssNode r = Navigate(after, PathOf(e.Target))!.Value;
                    Assert.DoesNotContain(r.Declarations, d => string.Equals(d.Name, e.Property, e.Property!.StartsWith("--", StringComparison.Ordinal) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                    break;
                }

            case SheetEditOperation.InsertRule:
                Assert.Equal(CountRules(before) + CountRules(Stylesheet.Parse(e.Css!.Trim())), CountRules(after));
                break;
            case SheetEditOperation.RemoveRule:
                Assert.True(CountRules(after) < CountRules(before));
                break;
            default:
                break;
        }
    }

    private static string SheetLayout_Norm(string s) => string.Join(' ', s.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries));

    private static int CountRules(Stylesheet s) => Items(s).Count(n => n.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule);

    /// <summary>The edit that undoes <paramref name="e"/> on the edited sheet, when the format promises one.</summary>
    private static SheetEdit? Inverse(Stylesheet before, Stylesheet after, SheetEdit e, StylesheetChangeSet c)
    {
        switch (e.Operation)
        {
            case SheetEditOperation.SetDeclaration:
                {
                    CssNode oldRule = e.Target;
                    CssNode? old = null;
                    foreach (CssNode d in oldRule.Declarations)
                    {
                        if (string.Equals(d.Name, e.Property, e.Property!.StartsWith("--", StringComparison.Ordinal) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                        {
                            old = d;
                        }
                    }

                    CssNode newRule = Navigate(after, PathOf(oldRule))!.Value;
                    if (old is { } o)
                    {
                        return SheetEdit.SetDeclaration(after, newRule, e.Property!, o.ValueText.ToString(), o.IsImportant);
                    }

                    // An appended declaration is removed again; a block that ended without a semicolon gains one the removal keeps.
                    var decls = oldRule.Declarations.ToList();
                    bool lastTerminated = decls.Count == 0 || decls[^1].SemicolonRange.Length > 0;
                    return decls.Count > 0 && !lastTerminated ? null : SheetEdit.RemoveDeclaration(after, newRule, e.Property!);
                }

            case SheetEditOperation.InsertRule:
                {
                    // Remove what the insertion added: the added rule that is not nested in another added rule.
                    // A semicolon or brace added to close the neighbour stays after the removal.
                    if (c.Changed.Any(ch => ch.Old.Kind == CssNodeKind.Declaration || (ch.Old.Kind == CssNodeKind.AtRule && !ch.Old.HasBlock)))
                    {
                        return null;
                    }

                    CssNode? added = c.Added.FirstOrDefault(n => n.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule);
                    return added is { } a ? SheetEdit.RemoveRule(after, a) : null;
                }

            default:
                return null;
        }
    }
}
