using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// The axe-core 4.14.0 outcomes recorded in <c>testdata/axe/expectations.tsv</c> (captured in Chrome for Testing on the pages beside it),
/// checked against our verdicts on the same pages. A fifth column holds our own verdict where it deliberately differs from axe's; the
/// reason for each such row is in <c>docs/axe-differential.md</c>. Rows are keyed by element id, so they survive reformatting of the pages.
/// </summary>
public sealed class AxeExpectationsTests
{
    private static string Root { get; } = Path.Combine(AriaActTests.Repo, "testdata", "axe");

    private static string Group(Outcome o) => o switch
    {
        Outcome.Failed => "violation",
        Outcome.Passed => "pass",
        _ => "incomplete",
    };

    private static bool IsInside(Node n, Node ancestor)
    {
        for (Node p = n.Parent; !p.IsNull; p = p.Parent)
        {
            if (p.Equals(ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static (Document Doc, RenderedFacts? Facts) Load(string page)
    {
        string dir = Path.Combine(Root, "captures", Path.GetFileNameWithoutExtension(page));
        if (!Directory.Exists(dir))
        {
            return (Document.Parse(File.ReadAllBytes(Path.Combine(Root, page))), null);
        }

        static byte[] Read(string d, string name)
        {
            using FileStream f = File.OpenRead(Path.Combine(d, name + ".json.gz"));
            using var z = new GZipStream(f, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            z.CopyTo(ms);
            return ms.ToArray();
        }

        using JsonDocument dom = JsonDocument.Parse(Read(dir, "dom"));
        string doctype = dom.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        Document doc = Document.Parse(doctype + dom.RootElement.GetProperty("outerHTML").GetString());
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(DomSnapshot.Parse(Read(dir, "snapshot")), doc);
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        return (doc, map.Facts);
    }

    private static int Severity(string g) => g switch { "violation" => 3, "incomplete" => 2, "pass" => 1, _ => 0 };

    [Fact]
    public void Our_verdicts_match_the_recorded_axe_outcomes_or_the_documented_difference()
    {
        var failures = new List<string>();
        int rows = 0;
        var checker = new Checker();
        foreach (IGrouping<(string Page, string Rule), string[]> g in File.ReadAllLines(Path.Combine(Root, "expectations.tsv"))
                     .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).GroupBy(c => (c[0], c[1])))
        {
            // A page with a Chrome capture beside it (captures/<name>/) is checked with its rendered facts, as the rules that need layout require.
            (Document doc, RenderedFacts? facts) = Load(g.Key.Page);
            CheckReport report = checker.Check(AccessibilityTree.Build(doc, DocumentStyles.Create(doc)), new CheckOptions { RuleIds = [g.Key.Rule], Facts = facts });
            foreach (string[] row in g)
            {
                rows++;
                Node el = doc.GetElementById(row[2]);
                string actual = "none";
                // Table rules report on cells where axe reports the table: take the cells' verdicts.
                bool container = g.Key.Rule is "td-has-header" or "table-fake-caption" or "td-headers-attr" or "th-has-data-cells";
                foreach (RuleResult r in report.Results.Where(r => !r.Target.IsNull && (r.Target.Equals(el) || (container && IsInside(r.Target, el)))))
                {
                    string grp = Group(r.Outcome);
                    if (Severity(grp) > Severity(actual))
                    {
                        actual = grp;
                    }
                }

                string expected = row.Length > 4 && row[4].Length > 0 ? row[4] : row[3];
                // A pass axe reports for a candidate we do not report on is coverage, not disagreement (docs/axe-differential.md).
                if (actual != expected && !(expected == "pass" && actual == "none"))
                {
                    failures.Add($"{row[0]} {row[1]} #{row[2]}: expected {expected}, ours {actual} (axe {row[3]})");
                }
            }
        }

        Assert.True(rows > 50, "the expectations file is missing or empty");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
