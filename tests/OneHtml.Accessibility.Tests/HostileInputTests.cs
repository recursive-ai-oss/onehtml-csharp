using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Aria;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

public sealed class HostileInputTests
{
    private static OneHtml.Document Parse(string html) => OneHtml.Document.Parse(html, new OneHtml.ParseOptions { Limits = new OneHtml.ParseLimits { MaxOpenElements = 300_000, MaxNodes = 10_000_000 } });

    private static void FastEnough(Action action, int ms = 5000)
    {
        var sw = Stopwatch.StartNew();
        action();
        Assert.True(sw.ElapsedMilliseconds < ms, "took " + sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void Huge_role_token_list_is_bounded()
    {
        string tokens = string.Concat(Enumerable.Repeat("bogus ", 300_000));
        OneHtml.Document doc = OneHtml.Document.Parse("<div id=t role='" + tokens + "button'>");
        FastEnough(() =>
        {
            NodeRole r = RoleComputer.Compute(doc)[doc.Root.QuerySelector("#t")];
            // Only the first MaxRoleTokens tokens are examined, so the late valid token is not used.
            Assert.Equal("generic", r.ComputedRole);
            Assert.True(r.Diagnostics.Length <= 64);
            Assert.Contains(r.Diagnostics.ToArray(), d => d.Kind == RoleDiagnosticKind.LimitExceeded);
        });
    }

    [Fact]
    public void Thousands_of_id_references_are_checked_in_linear_time()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 50_000; i++)
        {
            sb.Append('x').Append(i).Append(' ');
        }

        OneHtml.Document doc = OneHtml.Document.Parse("<div id=t aria-describedby='" + sb + "'>");
        FastEnough(() =>
        {
            NodeRole r = RoleComputer.Compute(doc)[doc.Root.QuerySelector("#t")];
            Assert.True(r.Diagnostics.Length <= 64);
            Assert.Contains(r.Diagnostics.ToArray(), d => d.Kind == RoleDiagnosticKind.BrokenIdReference);
        });
    }

    [Fact]
    public void Deep_trees_do_not_recurse()
    {
        const int depth = 100_000;
        var sb = new StringBuilder();
        for (int i = 0; i < depth; i++)
        {
            sb.Append("<div>");
        }

        sb.Append("<button id=t>");
        OneHtml.Document doc = Parse(sb.ToString());
        FastEnough(() =>
        {
            RoleMap map = RoleComputer.Compute(doc);
            Assert.Equal("button", map[doc.Root.QuerySelector("#t")].ComputedRole);
        });
    }

    [Fact]
    public void Deep_lists_of_presentational_tables_and_wide_rows()
    {
        var sb = new StringBuilder("<table role=none>");
        for (int i = 0; i < 2000; i++)
        {
            sb.Append("<tr>");
            for (int j = 0; j < 100; j++)
            {
                sb.Append("<th>x");
            }
        }

        OneHtml.Document doc = Parse(sb.ToString());
        FastEnough(() => RoleComputer.Compute(doc));
        // One very wide row of headers must not cost quadratic time.
        var wide = new StringBuilder("<table><tr>");
        for (int j = 0; j < 200_000; j++)
        {
            wide.Append("<th>x");
        }

        OneHtml.Document doc2 = Parse(wide.ToString());
        FastEnough(() => RoleComputer.Compute(doc2));
    }

    [Fact]
    public void Many_attributes_and_many_elements()
    {
        var sb = new StringBuilder("<div id=t");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append(" aria-x").Append(i).Append("=1");
        }

        sb.Append('>');
        for (int i = 0; i < 100_000; i++)
        {
            sb.Append("<li aria-label=x>");
        }

        OneHtml.Document doc = Parse(sb.ToString());
        FastEnough(() =>
        {
            RoleMap map = RoleComputer.Compute(doc);
            Assert.True(map[doc.Root.QuerySelector("#t")].Diagnostics.Length <= 64);
        });
    }

    [Fact]
    public void Cancellation_is_observed()
    {
        OneHtml.Document doc = Parse(string.Concat(Enumerable.Repeat("<p>x", 20_000)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => RoleComputer.Compute(doc, null, cts.Token));
    }
}
