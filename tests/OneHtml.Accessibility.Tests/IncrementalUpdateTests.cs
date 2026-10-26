using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>An updated tree must equal a tree built from scratch on the new document, in every field the tree carries.</summary>
public sealed class IncrementalUpdateTests
{
    private sealed class Tally
    {
        public int Steps;
        public int Incremental;
        public int Full;
        public long Elements;
        public long Recomputed;
        public readonly Dictionary<string, int> Reasons = [];
        public readonly List<string> Failures = [];
    }

    internal static IEnumerable<(string Name, string Html)> Corpus()
    {
        foreach (string f in Directory.EnumerateFiles(Path.Combine(TestPaths.TestData, "wpt", "accname"), "*.html", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            yield return (Path.GetRelativePath(TestPaths.TestData, f), File.ReadAllText(f));
        }

        int n = 0;
        foreach (string f in Directory.EnumerateFiles(Path.Combine(TestPaths.TestData, "wpt", "html-syntax-parsing"), "*.dat").Order(StringComparer.Ordinal))
        {
            foreach (string input in DatInputs(File.ReadAllText(f)))
            {
                if (++n % 4 == 0)
                {
                    yield return (Path.GetFileName(f) + "#" + n, input);
                }
            }
        }

        foreach ((string name, string html) in RealisticPages.All)
        {
            yield return ("realistic:" + name, html);
        }
    }

    private static IEnumerable<string> DatInputs(string dat)
    {
        string[] lines = dat.Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            if (lines[i] != "#data")
            {
                i++;
                continue;
            }

            var sb = new StringBuilder();
            i++;
            while (i < lines.Length && !lines[i].StartsWith('#'))
            {
                sb.Append(lines[i]).Append('\n');
                i++;
            }

            // Skip fragment cases: they need a context element.
            bool fragment = false;
            for (int j = i; j < lines.Length && lines[j] != "#data"; j++)
            {
                if (lines[j].StartsWith("#document-fragment", StringComparison.Ordinal))
                {
                    fragment = true;
                    break;
                }
            }

            if (!fragment && sb.Length < 4000)
            {
                yield return sb.ToString();
            }
        }
    }

    private static void Run(string name, string html, int seed, int steps, Tally t)
    {
        var rng = new Random(seed);
        Document doc = Document.Parse(html);
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        for (int step = 0; step < steps; step++)
        {
            AccessibilityTree next;
            AccessibilityTree full;
            string what;
            if (rng.Next(6) == 0)
            {
                // A context change: focus or hover on a random element.
                List<Node> els = doc.Root.DescendantElements.ToList();
                if (els.Count == 0)
                {
                    break;
                }

                var ctx = new StyleContext { Focus = rng.Next(2) == 0 ? els[rng.Next(els.Count)] : default, Hover = rng.Next(2) == 0 ? els[rng.Next(els.Count)] : default };
                what = "context";
                next = tree.WithContext(ctx);
                full = AccessibilityTree.Build(doc, tree.Styles, ctx);
                styles = next.Styles;
                if (Environment.GetEnvironmentVariable("ONEHTML_UPDATE_DEBUG") == "1")
                {
                    DocumentStyles fresh2 = DocumentStyles.Create(doc, ctx);
                    foreach (Node e in doc.Root.DescendantElements)
                    {
                        if (fresh2.GetStyle(e).FirstDifference(next.Styles.GetStyle(e)) is { } d2)
                        {
                            t.Failures.Add($"{name} seed={seed} step={step}: OVERLAY style differs from fresh at <{e.LocalName}> #{e.Index}: {d2}");
                            break;
                        }
                    }

                    AccessibilityTree freshTree = AccessibilityTree.Build(doc, fresh2);
                    foreach (Node e in doc.Root.Descendants)
                    {
                        if (freshTree.GetHidden(e).Reasons != full.GetHidden(e).Reasons)
                        {
                            t.Failures.Add($"HIDDEN differs at #{e.Index} {e.Kind} {e.LocalName}: fresh {freshTree.GetHidden(e).Reasons} overlay {full.GetHidden(e).Reasons}");
                            break;
                        }
                    }

                    foreach (Node e in doc.Root.DescendantElements)
                    {
                        string x = freshTree.ComputeName(e).Value + "|" + freshTree.ComputeDescription(e).Value;
                        string y = full.ComputeName(e).Value + "|" + full.ComputeDescription(e).Value;
                        if (x != y)
                        {
                            t.Failures.Add($"NAME differs at #{e.Index} {e.LocalName}: fresh '{x}' overlay '{y}'; styles: fresh {fresh2.GetStyle(e).Display.ToCss()} vs {full.Styles.GetStyle(e).Display.ToCss()}");
                            break;
                        }
                    }

                    string fa = TreeDump.Of(freshTree);
                    if (fa != TreeDump.Of(full))
                    {
                        t.Failures.Add($"{name} seed={seed} step={step}: OVERLAY TREE differs from fresh: {TreeDump.FirstDifference(TreeDump.Of(full), fa)}");
                    }
                }
            }
            else
            {
                List<Edit> edits = RandomEdits.Next(doc, rng, 1 + rng.Next(3));
                EditResult result = doc.Apply(edits);
                if (!result.Succeeded)
                {
                    continue;
                }

                what = string.Join(",", edits.Select(e => e.Operation.ToString())) + " " + string.Join(" | ", result.Changes!.Ranges.Select(r => r.NewText.Replace("\n", "\\n")));
                RestyleResult restyle = styles.Restyle(result);
                next = tree.Update(result, restyle);
                // The reference: the same context, styles computed from scratch.
                DocumentStyles fresh = DocumentStyles.Create(result.Document!, restyle.Styles.Context);
                foreach (Node e in result.Document!.Root.DescendantElements)
                {
                    if (fresh.GetStyle(e).FirstDifference(restyle.Styles.GetStyle(e)) is { } diff)
                    {
                        t.Failures.Add($"{name} seed={seed} step={step}: RESTYLE differs from fresh at <{e.LocalName}> #{e.Index}: {diff}");
                        break;
                    }
                }

                full = AccessibilityTree.Build(result.Document!, fresh);
                doc = result.Document!;
                styles = restyle.Styles;
            }

            t.Steps++;
            TreeUpdateInfo info = next.UpdateInfo!;
            if (info.Incremental)
            {
                t.Incremental++;
                t.Elements += info.ElementNodes;
                t.Recomputed += info.NamesRecomputed;
            }
            else
            {
                t.Full++;
                t.Reasons[info.FullRebuildReason!] = t.Reasons.GetValueOrDefault(info.FullRebuildReason!) + 1;
            }

            string a = TreeDump.Of(next);
            string b = TreeDump.Of(full);
            if (a != b && t.Failures.Count < 8)
            {
                string c = TreeDump.Of(AccessibilityTree.Build(next.Document, DocumentStyles.Create(next.Document, next.Styles.Context)));
                if (Environment.GetEnvironmentVariable("ONEHTML_UPDATE_DEBUG") == "1")
                {
                    t.Failures.Add("  OLD TREE:\n" + string.Join("\n", TreeDump.Of(tree).Split('\n').Where(l => l.Contains("heading") || l.Contains("mark") || l.Contains("owns")).Select(l => l.Length > 260 ? l[..260] : l)));
                    t.Failures.Add("  NEW (full):\n" + string.Join("\n", b.Split('\n').Where(l => l.Contains("heading") || l.Contains("owns")).Select(l => l.Length > 260 ? l[..260] : l)));
                }

                t.Failures.Add("  (a rebuild again equals the first rebuild: " + (c == b) + "; incremental equals rebuild again: " + (a == c) + ")");
                t.Failures.Add($"{name} seed={seed} step={step} {what}: {TreeDump.FirstDifference(a, b)}\n{doc.SerializeChildren(doc.Root).Replace("\n", "\\n")}");
            }

            tree = next;
        }
    }

    [Fact]
    public void Updated_trees_equal_trees_built_from_scratch_across_the_corpus()
    {
        var t = new Tally();
        int page = 0;
        int seedBase = int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_UPDATE_SEED"), out int sb) ? sb : 1000;
        int steps = int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_UPDATE_STEPS"), out int st) ? st : 6;
        string? only = Environment.GetEnvironmentVariable("ONEHTML_UPDATE_ONLY");
        foreach ((string name, string html) in Corpus())
        {
            if (only is not null && !name.Contains(only, StringComparison.Ordinal))
            {
                page++;
                continue;
            }

            Run(name, html, seedBase + page++, steps, t);
        }

        string? report = Environment.GetEnvironmentVariable("ONEHTML_UPDATE_REPORT");
        string summary = $"pages={page} steps={t.Steps} incremental={t.Incremental} full={t.Full} recomputedShare={(t.Elements == 0 ? 0 : (double)t.Recomputed / t.Elements):P1} reasons=[{string.Join("; ", t.Reasons.Select(p => p.Key + ": " + p.Value))}]\n";
        if (report is not null)
        {
            File.AppendAllText(report, summary);
        }

        Assert.True(t.Failures.Count == 0, summary + string.Join("\n", t.Failures));
    }
}
