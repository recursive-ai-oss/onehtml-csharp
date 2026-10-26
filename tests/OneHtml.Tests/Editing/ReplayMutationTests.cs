using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneHtml.Differential;
using RecursiveAI.OneHtml.Tests.Html5lib;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Editing;

/// <summary>
/// Saves an edit on sampled elements, mutates the page lightly and requires the replay outcome to match what the
/// mutation implies. The one hard requirement: a replay never lands on a different element.
/// </summary>
public class ReplayMutationTests
{
    private static int Seeds => int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_REPLAY_PER_PAGE"), out int n) && n > 0 ? n : 4;

    private static void Report(ReplayMutations m, string title, Xunit.ITestOutputHelper output)
    {
        output.WriteLine($"{title}: pages {m.Pages}, targets {m.Targets}");
        output.WriteLine(m.Table());
        if (Environment.GetEnvironmentVariable("ONEHTML_REPLAY_FINDINGS") is { Length: > 0 } path)
        {
            File.WriteAllLines(path + "." + title, m.Findings);
        }

        foreach (string f in m.Findings.GetRange(0, Math.Min(15, m.Findings.Count)))
        {
            output.WriteLine(f);
        }
    }

    [Fact]
    public void Realistic_and_edit_pages_never_replay_onto_the_wrong_element()
    {
        var m = new ReplayMutations();
        var rng = new Random(12);
        foreach (string dir in new[] { "replay", "edit" })
        {
            foreach (string f in System.IO.Directory.GetFiles(Path.Combine(TestPaths.TestData, dir), "*.html"))
            {
                m.CheckPage(Path.GetFileName(f), File.ReadAllText(f, new UTF8Encoding(false)), 40, rng);
            }
        }

        Report(m, "pages", TestContext.Current.TestOutputHelper!);
        Assert.Equal(0, m.WrongTotal);
        Assert.Empty(m.Findings.FindAll(x => x.StartsWith("WRONG", StringComparison.Ordinal)));
    }

    [Fact]
    public void Tree_construction_inputs_never_replay_onto_the_wrong_element()
    {
        var m = new ReplayMutations();
        var rng = new Random(34);
        int perPage = Seeds;
        foreach (string file in TreeConstructionSuite.Files())
        {
            foreach (TreeCase c in TreeConstructionSuite.Load(file))
            {
                if (c.FragmentContext is not null || c.Data.Length > 20000)
                {
                    continue;
                }

                m.CheckPage(file + "#" + c.Index, c.Data, perPage, rng);
            }
        }

        Report(m, "tree-construction", TestContext.Current.TestOutputHelper!);
        Assert.Equal(0, m.WrongTotal);
    }
}
