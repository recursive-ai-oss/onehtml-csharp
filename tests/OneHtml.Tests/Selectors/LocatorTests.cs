using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneHtml.Locators;
using RecursiveAI.OneHtml.Tests.Html5lib;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Selectors;

public class LocatorTests
{
    [Fact]
    public void Text_hash_is_sha256_of_the_normalized_text()
    {
        // Lengths around the 55/56 and 64 byte boundaries where the padding changes shape, with multi-byte characters mixed in.
        for (int n = 0; n <= 140; n++)
        {
            string text = string.Concat(Enumerable.Range(0, n).Select(i => i % 7 == 3 ? "é" : "a"));
            Document d = Document.Parse("<!DOCTYPE html><p>" + text + "</p>");
            ElementFingerprint f = ElementFingerprint.Create(d.Root.QuerySelector("p"));
            Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), f.TextHash);
        }
    }

    [Theory]
    [InlineData("nav-link", false)]
    [InlineData("navLink", false)]
    [InlineData("Button", false)]
    [InlineData("col-12", false)]
    [InlineData("year2024", false)]
    [InlineData("fooBarBaz", false)]
    [InlineData("btn-primary", false)]
    [InlineData("Button_primary__3xK9z", true)]
    [InlineData("css-1x2y3z", true)]
    [InlineData("sc-bdfBwQ", true)]
    [InlineData("jsx-1234567890", true)]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", true)]
    [InlineData("a1b2c3d4e5", true)]
    public void Hash_like_names(string name, bool expected)
    {
        Assert.Equal(expected, GeneratedNames.IsHashLike(name));
    }

    [Fact]
    public void Prefers_id_then_hooks_then_name_then_aria_then_tag_then_paths()
    {
        Document d = Document.Parse("<!DOCTYPE html><title>t</title><nav aria-label='Main'><ul><li><a href=x>1</a><li><a href=y data-testid=second>2</a><li><a href=z>3</a></ul></nav>"
            + "<form><input name=q><input name=q><input name=r id=rr></form><div role=search><p>a<p>b</div><div><p>c</div>");
        var g = new LocatorGenerator(d);
        Assert.Equal("#rr", g.Generate(d.Root.QuerySelector("[name=r]")));
        Assert.Equal("[data-testid=\"second\"]", g.Generate(d.Root.QuerySelector("[href=y]")));
        Assert.Equal("[aria-label=\"Main\"]", g.Generate(d.Root.QuerySelector("nav")));
        Assert.Equal("[role=\"search\"]", g.Generate(d.Root.QuerySelector("[role=search]")));
        Assert.Equal("title", g.Generate(d.Root.QuerySelector("title")));
        Assert.Equal("ul > li:nth-of-type(1) > a", g.Generate(d.Root.QuerySelector("[href=x]")));
        Assert.Equal("[role=\"search\"] > p:nth-of-type(2)", g.Generate(d.Root.QuerySelectorAll("p")[1]));
        // Duplicate name: not an anchor, so the path from the form is used.
        Assert.Equal("form > input:nth-of-type(1)", g.Generate(d.Root.QuerySelectorAll("input")[0]));
    }

    [Fact]
    public void Hash_like_ids_are_not_used()
    {
        Document d = Document.Parse("<!DOCTYPE html><div id=css-1x2y3z><p>x</div><div><p>y</div>");
        var g = new LocatorGenerator(d);
        Assert.Equal("body > div:nth-of-type(1) > p", g.Generate(d.Root.QuerySelector("p")));
    }

    [Fact]
    public void Every_element_of_every_tree_construction_document_has_a_locator_that_finds_exactly_it()
    {
        int docs = 0;
        int elements = 0;
        foreach (string file in TreeConstructionSuite.Files())
        {
            foreach (TreeCase c in TreeConstructionSuite.Load(file))
            {
                if (c.FragmentContext is not null)
                {
                    continue;
                }

                Document d = Document.Parse(c.Data);
                var g = new LocatorGenerator(d);
                docs++;
                foreach (Node e in d.Root.DescendantElements)
                {
                    elements++;
                    if (!g.TryGenerate(e, out string? loc))
                    {
                        // Only elements inside a template's content tree are not part of the document tree.
                        Assert.False(e.IsConnected, $"{file} #{c.Index}: no locator for {e}");
                        continue;
                    }

                    Assert.True(g.Verify(e, loc!), $"{file} #{c.Index}: {loc} does not name exactly {e}");
                }
            }
        }

        Assert.True(docs > 1000 && elements > 3000);
    }

    [Fact]
    public void Hostile_documents()
    {
        var cases = new List<string>
        {
            "<div id=a>1</div><div id=a>2</div><p id=a>3</p>",
            "<p id='a b'></p><p id='1x'></p><p id='\"q\"'></p><p id='a:b.c#d'></p><p id='\\'></p><p id='\u00e9\u4e2d'></p><p id='-'></p><p id='--'></p>",
            "<p id=A></p><p id=a></p>",
            "<input name='a\"b'><input name=\"a'b\"><input name='x\\y'>",
            "<div data-testid='a\"]'></div><div data-testid='\\'></div>",
            "<svg><a id=x></a><a></a><foreignObject><a></a></foreignObject></svg><a></a>",
            "<table><tr><td>1<td>2</table><table><tr><td>3</table>",
            "<ul>" + string.Concat(Enumerable.Repeat("<li>x", 20000)) + "</ul>",
            string.Concat(Enumerable.Repeat("<div>", 1500)) + "deep",
            string.Concat(Enumerable.Repeat("<div><span>", 300)) + "deep",
            "<template><p id=t>in template</p></template><p id=t></p>",
        };
        foreach (string html in cases)
        {
            foreach (string prefix in new[] { "<!DOCTYPE html>", string.Empty })
            {
                Document d = Document.Parse(prefix + html);
                var g = new LocatorGenerator(d);
                int made = 0;
                int checkedCount = 0;
                foreach (Node e in d.Root.DescendantElements)
                {
                    if (!g.TryGenerate(e, out string? loc))
                    {
                        continue;
                    }

                    made++;
                    // The exhaustive check is linear per element, so check a spread of them on the big documents.
                    if (d.NodeCount < 2000 || made % 997 == 0)
                    {
                        checkedCount++;
                        Assert.True(g.Verify(e, loc!), $"{loc} for {e} in {html[..Math.Min(60, html.Length)]}");
                    }
                }

                Assert.True(made > 0 && checkedCount > 0);
            }
        }
    }

    [Fact]
    public void Elements_far_below_an_anchor_have_no_locator_instead_of_a_huge_selector()
    {
        Document d = Document.Parse("<!DOCTYPE html>" + string.Concat(Enumerable.Repeat("<div>", 600)));
        var g = new LocatorGenerator(d);
        Node deepest = d.Root.DescendantElements.Last();
        Assert.False(g.TryGenerate(deepest, out _));
        Assert.Throws<ArgumentException>(() => g.Generate(deepest));
    }

    [Fact]
    public void Fingerprint_canonical_form()
    {
        Document d = Document.Parse("<!DOCTYPE html><html><body><div><p>skip</p><a id=\"go\" class=\"nav-link css-1x2y3z  big\" href=\"/calendar\" data-x=1>  Hello \n  <b>world</b>\t!  </a></div></body></html>");
        Node a = d.Root.QuerySelector("a");
        ElementFingerprint f = ElementFingerprint.Create(a);
        Assert.Equal("a", f.Tag);
        Assert.Equal(
            ["class=nav-link big", "href=/calendar", "id=go"],
            f.Attributes.Select(kv => kv.Key + "=" + kv.Value).ToArray());
        Assert.Equal("Hello world !", ElementFingerprint.NormalizeText(a.TextContent!));
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("Hello world !"))), f.TextHash);
        Assert.Equal("html[1]/body[2]/div[1]/a[2]".Replace("body[2]", "body[2]"), ElementFingerprint.PathText(a));
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ElementFingerprint.PathText(a)))), f.PathHash);

        // The same element in a re-parsed document compares equal; a changed text does not.
        Document d2 = Document.Parse(d.SerializeNode(d.DocumentElement));
        Assert.Equal(f, ElementFingerprint.Create(d2.Root.QuerySelector("a")));
        Document d3 = Document.Parse(d.SerializeNode(d.DocumentElement).Replace("world", "there", StringComparison.Ordinal));
        FingerprintComparison cmp = f.Compare(ElementFingerprint.Create(d3.Root.QuerySelector("a")));
        Assert.True(cmp.TagAndAttributes && !cmp.Text && cmp.Path);
    }
}
