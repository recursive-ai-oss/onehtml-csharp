using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneHtml.Fuzz;
using RecursiveAI.OneHtml.Tests.Html5lib;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Editing;

/// <summary>
/// Random edit sequences over every test document: the checks live in <see cref="EditTarget"/>, which the fuzz
/// target shares. Set ONEHTML_EDIT_SEEDS to run more sequences per document, ONEHTML_EDIT_STATS to a file name to
/// get the per-operation counts.
/// </summary>
public class EditPropertyTests
{
    private static int Seeds(int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_EDIT_SEEDS"), out int n) && n > 0 ? n : fallback;

    private static string EditData { get; } = Path.Combine(TestPaths.TestData, "edit");

    [Fact]
    public void Synthetic_pages_in_several_encodings()
    {
        var failures = new List<string>();
        var stats = new EditStats();
        int seeds = Seeds(150);
        foreach ((string name, Document doc) in SyntheticDocuments())
        {
            RunSeeds(name, doc, seeds, stats, failures);
        }

        Report("synthetic", stats);
        Assert.True(failures.Count == 0, string.Join("\n---\n", failures));
        Assert.True(stats.Applied > stats.Transactions / 3, "too few transactions applied: " + stats.Applied + "/" + stats.Transactions);
    }

    [Fact]
    public void Tree_construction_inputs()
    {
        var failures = new List<string>();
        var stats = new EditStats();
        int seeds = Seeds(3);
        foreach (string file in TreeConstructionSuite.Files())
        {
            foreach (TreeCase c in TreeConstructionSuite.Load(file))
            {
                if (c.FragmentContext is not null || c.Data.Length > 4000)
                {
                    continue;
                }

                Document doc = Document.Parse(c.Data, new ParseOptions { ScriptingEnabled = c.Scripting ?? false });
                RunSeeds(file + "#" + c.Index, doc, seeds, stats, failures, c.Scripting ?? false);
                if (failures.Count > 8)
                {
                    break;
                }
            }
        }

        Report("tree-construction", stats);
        Assert.True(failures.Count == 0, string.Join("\n---\n", failures));
    }

    [Fact]
    public void Tokenizer_inputs()
    {
        var failures = new List<string>();
        var stats = new EditStats();
        int seeds = Seeds(2);
        foreach (string file in TokenizerSuite.Files())
        {
            foreach (TokenizerCase c in TokenizerSuite.Load(file))
            {
                if (c.Input.Length > 2000)
                {
                    continue;
                }

                Document doc = Document.Parse(c.Input);
                RunSeeds(file + "#" + c.Index, doc, seeds, stats, failures);
                if (failures.Count > 8)
                {
                    break;
                }
            }
        }

        Report("tokenizer", stats);
        Assert.True(failures.Count == 0, string.Join("\n---\n", failures));
    }

    private static void RunSeeds(string name, Document doc, int seeds, EditStats stats, List<string> failures, bool scripting = false)
    {
        // Each document gets its own run of seeds, or every document would draw the same edits.
        uint h = 2166136261;
        foreach (char c in name)
        {
            h = (h ^ c) * 16777619;
        }

        for (int i = 0; i < seeds; i++)
        {
            int seed = unchecked((int)(h & 0x3FFFFFFF)) + i;
            try
            {
                EditTarget.CheckDocument(doc, seed, stats, scripting);
            }
            catch (EditCheckException ex)
            {
                string src = doc.Source.Text.Length > 600 ? doc.Source.Text[..600] + "…" : doc.Source.Text;
                failures.Add(name + " " + ex.Message + "\nsource: " + src.Replace("\n", "\\n", StringComparison.Ordinal));
                return;
            }
        }
    }

    private static void Report(string label, EditStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{label}] transactions={stats.Transactions} applied={stats.Applied} withFallback={stats.WithFallback} oracleSkipped={stats.OracleSkipped} reverts={stats.Reverts} reparses={stats.Verifications}");
        foreach (var kv in stats.PerOperation)
        {
            long[] r = kv.Value;
            sb.AppendLine($"  {kv.Key,-15} attempted={r[0]} applied={r[1]} invalid={r[2]} failed={r[3]} fallback={r[4]}");
        }

        string? path = Environment.GetEnvironmentVariable("ONEHTML_EDIT_STATS");
        if (!string.IsNullOrEmpty(path))
        {
            File.AppendAllText(path, sb.ToString());
        }
    }

    private static byte[] Concat(byte[] a, byte[] b) => [.. a, .. b];

    private static IEnumerable<(string, Document)> SyntheticDocuments()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (string file in Directory.GetFiles(EditData, "*.html"))
        {
            string name = Path.GetFileName(file);
            byte[] bytes = File.ReadAllBytes(file);
            yield return (name, Document.Parse(bytes));
            if (name == "legacy-upper.html")
            {
                continue;
            }

            string text = Encoding.UTF8.GetString(bytes);
            yield return (name + " string", Document.Parse(text));
            yield return (name + " utf8-bom", Document.Parse(Concat(Encoding.UTF8.GetPreamble(), bytes)));
            yield return (name + " utf16le", Document.Parse(Concat(Encoding.Unicode.GetPreamble(), Encoding.Unicode.GetBytes(text))));
            yield return (name + " utf16be", Document.Parse(Concat(Encoding.BigEndianUnicode.GetPreamble(), Encoding.BigEndianUnicode.GetBytes(text))));
            string crlf = text.Replace("\n", "\r\n", StringComparison.Ordinal);
            yield return (name + " crlf", Document.Parse(Encoding.UTF8.GetBytes(crlf)));
            string japanese = text.Replace("<head>", "<head><meta charset=\"shift_jis\">", StringComparison.Ordinal).Replace("<title>", "<title>\u65E5\u672C\u8A9E ", StringComparison.Ordinal)
                .Replace("<p>", "<p>\u3053\u3093\u306B\u3061\u306F ", StringComparison.Ordinal);
            yield return (name + " shift_jis", Document.Parse(Encoding.GetEncoding("shift_jis").GetBytes(japanese)));
            string latin = text.Replace("<head>", "<head><meta charset=\"iso-8859-1\">", StringComparison.Ordinal).Replace("<title>", "<title>caf\u00E9 ", StringComparison.Ordinal);
            yield return (name + " latin1", Document.Parse(Encoding.Latin1.GetBytes(latin)));
            string gb = text.Replace("<head>", "<head><meta charset=\"gbk\">", StringComparison.Ordinal).Replace("<p>", "<p>\u4F60\u597D ", StringComparison.Ordinal);
            yield return (name + " gbk", Document.Parse(Encoding.GetEncoding("gbk").GetBytes(gb)));
        }
    }
}
