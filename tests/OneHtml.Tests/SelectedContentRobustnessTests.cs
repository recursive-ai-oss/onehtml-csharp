using System;
using System.Text;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

/// <summary>
/// The customizable-select steps run while the tree is built, so they see every shape the tree builder can produce.
/// None of them may throw anything but the typed limit exception.
/// </summary>
public class SelectedContentRobustnessTests
{
    private static Document Parse(string html, ParseLimits? limits = null) =>
        Document.Parse(Encoding.UTF8.GetBytes(html), new ParseOptions { Limits = limits });

    // Cloning a template into selectedcontent adds a content fragment node. Adding a node can reallocate the arena
    // columns, which once lost the link from the cloned template to its fragment when the arrays were full exactly
    // there. Sweeping the amount of preceding content moves the growth point across every clone step.
    [Fact]
    public void Cloning_templates_survives_arena_growth_at_any_point()
    {
        for (int pad = 0; pad < 80; pad++)
        {
            string html = "<select><button><selectedcontent></button><option>" + new string('x', 0)
                + string.Concat(System.Linq.Enumerable.Repeat("<i>", pad))
                + "<template><template><b>t</template>h</template><template>z</template></option></select>";
            Document doc = Parse(html);
            Assert.Null(DocumentInvariants.Check(doc));
            Node content = doc.DocumentElement.LastChild.FirstChild.FirstChild.FirstChild;
            Assert.Equal("selectedcontent", content.LocalName);
            Assert.NotNull(FindTemplate(content));
        }
    }

    private static Node? FindTemplate(Node n)
    {
        for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.LocalName == "template")
            {
                return c;
            }

            Node? inner = FindTemplate(c);
            if (inner is not null)
            {
                return inner;
            }
        }

        return null;
    }

    [Fact]
    public void Cloning_nested_templates_in_a_template_context_does_not_throw()
    {
        string html = "<template><select><option selected>"
            + string.Concat(System.Linq.Enumerable.Repeat("<template><template></template></template>", 50))
            + "<select><button><selectedcontent></button><option>b</template>";
        Assert.Null(DocumentInvariants.Check(Parse(html)));
    }

    [Fact]
    public void A_node_limit_reached_while_cloning_fails_with_the_typed_exception()
    {
        string html = "<select><button><selectedcontent></button><option>"
            + string.Concat(System.Linq.Enumerable.Repeat("<template>a</template>", 200)) + "</option></select>";
        var limits = new ParseLimits { MaxNodes = 500 };
        Assert.Throws<HtmlLimitExceededException>(() => Parse(html, limits));
    }
}
