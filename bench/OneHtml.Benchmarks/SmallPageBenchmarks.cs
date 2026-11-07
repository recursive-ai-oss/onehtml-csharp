using System.Text;
using BenchmarkDotNet.Attributes;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Benchmarks;

/// <summary>
/// The fixed cost per document on a small page (about 8 KB, 230 elements, no author styles): creating the style database, reading
/// the style of every element, and building the accessibility tree. The user-agent rules are shared by all documents, so the
/// style rows should cost about what the page's own elements need.
/// </summary>
[MemoryDiagnoser]
public class SmallPageBenchmarks
{
    private Document _doc = null!;
    private DocumentStyles _styles = null!;

    [GlobalSetup]
    public void Setup()
    {
        var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><title>Contact us</title></head><body><nav><a href=\"/\">Home</a> <a href=\"/about\">About</a></nav><main>");
        for (int i = 0; i < 24; i++)
        {
            html.Append("<section><h3>Person ").Append(i).Append("</h3><p>Acme Holdings LLC</p><p>10 Main St</p><p>Springfield, IL 62704</p>")
                .Append("<p>Tel: (200) 555-1000</p><p>Email: person").Append(i).Append("@example.com</p><p>www.example.com</p><p>We have served the community for years.</p></section>");
        }

        html.Append("</main><footer>Copyright</footer></body></html>");
        _doc = Document.Parse(html.ToString());
        _styles = DocumentStyles.Create(_doc);
    }

    /// <summary>Gathers the style sources of the page and prepares the cascade.</summary>
    [Benchmark]
    public DocumentStyles CreateStyles() => DocumentStyles.Create(_doc);

    /// <summary>Create plus the computed <c>display</c> of every element.</summary>
    [Benchmark]
    public int CreateAndReadEveryElement()
    {
        DocumentStyles s = DocumentStyles.Create(_doc);
        int total = 0;
        foreach (Node e in _doc.Root.DescendantElements)
        {
            total += s.GetStyle(e).GetPropertyValue("display").Length;
        }

        return total;
    }

    /// <summary>Builds the accessibility tree from existing styles.</summary>
    [Benchmark]
    public AccessibilityTree BuildTree() => AccessibilityTree.Build(_doc, _styles);
}
