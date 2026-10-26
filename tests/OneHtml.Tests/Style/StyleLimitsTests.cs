using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Fuzz;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>Hostile inputs for the cascade: they finish quickly and fail cleanly (a diagnostic, never a crash or a hang).</summary>
public class StyleLimitsTests
{
    private static void Fast(Action a, int seconds = 20)
    {
        var sw = Stopwatch.StartNew();
        a();
        Assert.True(sw.Elapsed.TotalSeconds < seconds, "took " + sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
    }

    [Fact]
    public void Deeply_nested_elements_do_not_recurse()
    {
        const int depth = 4000;
        var sb = new StringBuilder("<style>div{color:red} div div{margin-left:1px} div:has(> span){color:blue}</style>");
        for (int i = 0; i < depth; i++)
        {
            sb.Append("<div style=\"--a:").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\">");
        }

        sb.Append("<span id=leaf>x</span>");
        Fast(() =>
        {
            Document doc = Document.Parse(sb.ToString());
            DocumentStyles s = DocumentStyles.Create(doc);
            Assert.True(s.ComputeAll() > depth);
            Node leaf = doc.GetElementById("leaf");
            Assert.Equal("blue", s.GetStyle(leaf).GetPropertyValue("color"));
            DocumentStyles o = s.WithContext(StyleContext.Default.WithHover(leaf));
            Assert.NotNull(o.GetStyle(leaf));
        });
    }

    [Fact]
    public void Many_rules_and_the_rule_limit()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100_000; i++)
        {
            sb.Append(".c").Append(i.ToString(CultureInfo.InvariantCulture)).Append("{color:red}");
        }

        Fast(() =>
        {
            Document doc = Document.Parse("<style>" + sb + "</style><p class=c77>x");
            DocumentStyles s = DocumentStyles.Create(doc);
            Assert.Equal("red", s.GetStyle(doc.Root.QuerySelector("p")).GetPropertyValue("color"));
            DocumentStyles capped = DocumentStyles.Create(doc, null, new StyleSourceOptions { MaxRules = 1000 });
            Assert.Contains(capped.Diagnostics, d => d.Kind == StyleDiagnosticKind.LimitExceeded);
            Assert.True(capped.RuleCount <= 1000 + 64);
        });
    }

    [Fact]
    public void Many_layers_and_the_layer_limit()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 70_000; i++)
        {
            sb.Append("@layer l").Append(i.ToString(CultureInfo.InvariantCulture)).Append("{p{color:red}}");
        }

        Fast(() =>
        {
            Document doc = Document.Parse("<style>" + sb + "</style><p>x");
            DocumentStyles s = DocumentStyles.Create(doc);
            _ = s.GetStyle(doc.Root.QuerySelector("p")).GetPropertyValue("color");
            Assert.Contains(s.Diagnostics, d => d.Kind == StyleDiagnosticKind.LimitExceeded);
        });
    }

    [Fact]
    public void Import_cycles_and_deep_chains_stop()
    {
        Fast(() =>
        {
            var opts = new StyleSourceOptions
            {
                BaseUrl = "https://x.test/p.html",
                Resolver = url =>
                {
                    string name = url[(url.LastIndexOf('/') + 1)..];
                    if (name == "self.css")
                    {
                        return new StylesheetResource(Encoding.UTF8.GetBytes("@import 'self.css'; p{color:red}"));
                    }

                    if (name.StartsWith('n') && int.TryParse(name.AsSpan(1, name.Length - 5), CultureInfo.InvariantCulture, out int k))
                    {
                        return new StylesheetResource(Encoding.UTF8.GetBytes("@import 'n" + (k + 1).ToString(CultureInfo.InvariantCulture) + ".css'; p{margin-top:" + k.ToString(CultureInfo.InvariantCulture) + "px}"));
                    }

                    return null;
                },
            };
            Document cyc = Document.Parse("<link rel=stylesheet href=self.css><p>x");
            DocumentStyles a = DocumentStyles.Create(cyc, null, opts);
            Assert.Contains(a.Diagnostics, d => d.Kind == StyleDiagnosticKind.ImportCycle);
            Assert.Equal("red", a.GetStyle(cyc.Root.QuerySelector("p")).GetPropertyValue("color"));
            Document chain = Document.Parse("<link rel=stylesheet href=n0.css><p>x");
            DocumentStyles b = DocumentStyles.Create(chain, null, opts);
            Assert.Contains(b.Diagnostics, d => d.Kind == StyleDiagnosticKind.ImportDepthExceeded);
            _ = b.GetStyle(chain.Root.QuerySelector("p"));
        });
    }

    [Fact]
    public void Variable_chains_across_elements_and_within_one_element()
    {
        const int n = 3000;
        var sb = new StringBuilder("<div style=\"--v0:1px");
        for (int i = 1; i < n; i++)
        {
            sb.Append(";--v").Append(i.ToString(CultureInfo.InvariantCulture)).Append(":var(--v").Append((i - 1).ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        sb.Append(";margin-left:var(--v").Append(n - 1).Append(")\"><p id=p>x</p></div>");
        var chain = new StringBuilder();
        string open = "<div style=\"--a:var(--a, 1px)\">";
        for (int i = 0; i < n; i++)
        {
            chain.Append(open);
        }

        chain.Append("<i id=q></i>");
        Fast(() =>
        {
            Document d1 = Document.Parse(sb.ToString());
            DocumentStyles s1 = DocumentStyles.Create(d1);
            _ = s1.GetStyle(d1.GetElementById("p")).GetPropertyValue("margin-left");
            Document d2 = Document.Parse(chain.ToString());
            DocumentStyles s2 = DocumentStyles.Create(d2);
            _ = s2.GetStyle(d2.GetElementById("q")).GetPropertyValue("--a");
        });
    }

    [Fact]
    public void Nesting_that_would_double_the_selector_each_level_is_capped()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 40; i++)
        {
            sb.Append("&, &.x { ");
        }

        sb.Append("color: red");
        for (int i = 0; i < 40; i++)
        {
            sb.Append(" }");
        }

        Fast(() =>
        {
            Document doc = Document.Parse("<style>.a{" + sb + "}</style><p class=a>x");
            DocumentStyles s = DocumentStyles.Create(doc);
            _ = s.GetStyle(doc.Root.QuerySelector("p")).GetPropertyValue("color");
        });
    }

    [Fact]
    public void Style_target_survives_hostile_pages()
    {
        foreach (string css in new[] { "*{all:unset} @layer a{@layer b{@layer c{*{color:red}}}}", "@media (width>0) {@supports (display:grid) {*{--x:var(--x)}}}", "*{width:calc(var(--a) + var(--b))}" })
        {
            StyleTarget.Run(Encoding.UTF8.GetBytes("\u0001<div><p>a<a href=x>b</a></p></div>\0" + css));
        }
    }
}
