using System;
using System.Globalization;
using System.IO;
using RecursiveAI.OneHtml.Encodings;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// <c>replay</c>: for every page, saves edits on sampled elements, mutates the page in lightly different ways and
/// checks that each replay outcome (applied, stale, ambiguous) is what the mutation implies. Any replay that
/// edits the wrong element makes the exit status 1.
/// </summary>
internal static class ReplayCommand
{
    public static int Run(Options o)
    {
        var items = Corpus.Load(o.Corpus, pages: true, stylesheets: false);
        if (o.Limit < items.Count)
        {
            items.RemoveRange(o.Limit, items.Count - o.Limit);
        }

        var mutations = new ReplayMutations();
        int failures = 0;
        foreach (CorpusItem item in items)
        {
            var rng = new Random(o.Seed ^ Convert.ToInt32(item.Sha256[..7], 16));
            var (_, failure) = Guard.Run<int>(ct =>
            {
                byte[] bytes = File.ReadAllBytes(item.Path);
                string text = SourceText.Decode(bytes, WhatwgEncoding.ForLabel("utf-8")!, EncodingConfidence.Certain, null, ct).Text;
                mutations.CheckPage(item.Sha256, text, o.PerPage, rng);
                return 0;
            }, o.TimeoutSeconds);
            if (failure is not null)
            {
                failures++;
                Console.Error.WriteLine(item.Sha256 + ": " + failure);
            }
        }

        Directory.CreateDirectory(o.Out);
        string table = mutations.Table();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"pages {mutations.Pages}, targets {mutations.Targets}, failures {failures}"));
        Console.WriteLine(table);
        File.WriteAllText(Path.Combine(o.Out, "replay.md"), table);
        File.WriteAllLines(Path.Combine(o.Out, "replay-findings.txt"), mutations.Findings);
        foreach (string f in mutations.Findings.GetRange(0, Math.Min(20, mutations.Findings.Count)))
        {
            Console.WriteLine(f);
        }

        return mutations.WrongTotal > 0 || failures > 0 ? 1 : 0;
    }
}
