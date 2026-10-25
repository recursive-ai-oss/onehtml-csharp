using System.Collections.Generic;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Html5lib;

/// <summary>Runs the tree construction suite (HTML parsing tests from web-platform-tests) against <see cref="Document"/>.</summary>
public class TreeConstructionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string file in TreeConstructionSuite.Files())
        {
            int count = TreeConstructionSuite.Load(file).Count;
            for (int i = 0; i < count; i++)
            {
                yield return [file, i];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Builds_the_expected_tree(string file, int index)
    {
        TreeCase c = TreeConstructionSuite.Load(file)[index];
        if (TreeConstructionKnownDifferences.Cases.TryGetValue($"{file}|{index}", out _))
        {
            return;
        }

        bool[] modes = c.Scripting is { } s ? [s] : [false, true];
        foreach (bool scripting in modes)
        {
            var options = new ParseOptions { ScriptingEnabled = scripting };
            Document doc;
            if (c.FragmentContext is { } ctx)
            {
                doc = Document.ParseFragment(c.Data, ParseContext(ctx), options);
            }
            else
            {
                doc = Document.Parse(c.Data, options);
            }

            string actual = TreeConstructionSuite.Dump(doc.Root);
            Assert.True(c.Expected == actual, $"{file} #{index} (scripting {scripting}), data: {c.Data}\n--- expected\n{c.Expected}\n--- actual\n{actual}");
        }
    }

    private static FragmentContext ParseContext(string spec)
    {
        if (spec.StartsWith("svg ", System.StringComparison.Ordinal))
        {
            return new FragmentContext(ElementNamespace.Svg, spec[4..]);
        }

        if (spec.StartsWith("math ", System.StringComparison.Ordinal))
        {
            return new FragmentContext(ElementNamespace.MathMl, spec[5..]);
        }

        return new FragmentContext(ElementNamespace.Html, spec);
    }
}
