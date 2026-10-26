using System;
using System.Diagnostics;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// <c>all: revert-layer</c> in many layers makes every property roll back at every layer. Each rollback used to re-read
/// every declaration of every rule (properties times declarations); it now reads only the declarations of its own property.
/// </summary>
public class RollbackCostTests
{
    [Fact]
    public void Many_layers_of_all_revert_layer_are_cheap_and_roll_back_to_the_first_layer()
    {
        var sb = new StringBuilder("<style>@layer a0 { div { color: green; margin-top: 7px } }");
        for (int i = 1; i <= 16; i++)
        {
            sb.Append("@layer a").Append(i).Append(" { div { all: revert-layer } }");
        }

        sb.Append("</style>");
        for (int i = 0; i < 60; i++)
        {
            sb.Append("<div>x</div>");
        }

        var sw = Stopwatch.StartNew();
        Document doc = Document.Parse(sb.ToString());
        DocumentStyles styles = DocumentStyles.Create(doc);
        styles.ComputeAll();
        // Well under a second on a development machine, mostly first-call JIT. The bound allows a slow shared runner and only
        // catches a hang; the checks below pin the behaviour.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), "took " + sw.Elapsed.TotalSeconds + " s");
        foreach (Node div in doc.Root.QuerySelectorAll("div"))
        {
            Assert.Equal("green", styles.GetStyle(div).GetPropertyValue("color"));
            Assert.Equal("7px", styles.GetStyle(div).GetPropertyValue("margin-top"));
        }
    }
}
