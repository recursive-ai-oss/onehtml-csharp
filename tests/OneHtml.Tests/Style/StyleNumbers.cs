using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Cascade timings on a page of about 35,000 nodes with a few thousand rules. Prints when ONEHTML_STYLE_NUMBERS is set (use a
/// Release build); otherwise only checks that the large page computes and the style table shares.
/// </summary>
public class StyleNumbers
{
    private static string Page(Random r, out int focusables)
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>");
        string[] tags = ["div", "span", "li", "a", "p", "section", "b"];
        for (int i = 0; i < 3000; i++)
        {
            string c = ".c" + r.Next(400).ToString(CultureInfo.InvariantCulture);
            string sel = r.Next(10) switch
            {
                0 => c + " " + tags[r.Next(tags.Length)],
                1 => c + " > " + tags[r.Next(tags.Length)] + ".c" + r.Next(400).ToString(CultureInfo.InvariantCulture),
                2 => tags[r.Next(tags.Length)] + c,
                3 => c + ":hover",
                4 => c + ":not(.c" + r.Next(400).ToString(CultureInfo.InvariantCulture) + ")",
                5 => "li:nth-child(" + r.Next(5).ToString(CultureInfo.InvariantCulture) + "n+1)" + c,
                6 => c + " + " + c,
                7 => "a" + c + ":focus-visible",
                _ => c,
            };
            string[] decls = ["color:#" + r.Next(0x1000000).ToString("x6", CultureInfo.InvariantCulture), "display:" + new[] { "block", "inline", "none", "flex" }[r.Next(4)], "font-size:" + (10 + r.Next(10)).ToString(CultureInfo.InvariantCulture) + "px", "margin:" + r.Next(9).ToString(CultureInfo.InvariantCulture) + "px", "background-color:rgb(" + r.Next(256).ToString(CultureInfo.InvariantCulture) + ",0,0)", "--v:" + r.Next(9).ToString(CultureInfo.InvariantCulture)];
            sb.Append(sel).Append('{').Append(decls[r.Next(decls.Length)]).Append(';').Append(decls[r.Next(decls.Length)]).Append("}\n");
        }

        sb.Append("</style></head><body>");
        focusables = 0;
        for (int s = 0; s < 240; s++)
        {
            sb.Append("<section class=c").Append(r.Next(10)).Append('>');
            for (int g = 0; g < 4; g++)
            {
                sb.Append("<div class=\"c").Append(10 + r.Next(5)).Append(" c").Append(15 + r.Next(5)).Append("\"><ul>");
                for (int i = 0; i < 6; i++)
                {
                    sb.Append("<li class=c").Append(20 + r.Next(3)).Append("><a href=/x class=c").Append(23 + r.Next(2)).Append(">link <span class=c").Append(25 + r.Next(2)).Append(">text</span></a></li>");
                    focusables++;
                }

                sb.Append("</ul><p class=c").Append(27 + r.Next(2)).Append(">para <b>bold</b></p></div>");
            }

            sb.Append("</section>");
        }

        return sb.Append("</body></html>").ToString();
    }

    [Fact]
    public void Cascade_numbers_on_a_large_page()
    {
        var r = new Random(7);
        string html = Page(r, out int focusables);
        Document doc = Document.Parse(html);
        bool print = Environment.GetEnvironmentVariable("ONEHTML_STYLE_NUMBERS") is not null;
        int elements = doc.Root.DescendantElements.Count();

        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        DocumentStyles s = DocumentStyles.Create(doc);
        double gather = sw.Elapsed.TotalMilliseconds;
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        s.ComputeAll();
        double compute = sw.Elapsed.TotalMilliseconds;
        long a2 = GC.GetAllocatedBytesForCurrentThread();
        int distinct = s.DistinctStyleCount;

        var focus = doc.Root.DescendantElements.Where(e => e.LocalName == "a").ToList();
        sw.Restart();
        int sample = Math.Min(200, focus.Count);
        int touched = 0;
        for (int i = 0; i < sample; i++)
        {
            DocumentStyles o = s.WithContext(StyleContext.Default.WithFocus(focus[i * focus.Count / sample]));
            touched += o.ChangedElements.Count;
        }

        double overlay = sw.Elapsed.TotalMilliseconds / sample;

        sw.Restart();
        Node li = doc.Root.QuerySelector("li");
        string html2 = html.Replace("<li class=", "<li data-x=1 class=", StringComparison.Ordinal);
        _ = li;
        Document doc2 = Document.Parse(html2.Replace("<section class=c", "<section id=zz class=c", StringComparison.Ordinal));
        double parse2 = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        RestyleResult rr = s.Restyle(doc2, new StyleChange { Elements = doc2.Root.DescendantElements.Where(e => e.LocalName == "section").Select(e => new ElementChange(e.Index, ["id"])).Take(1).ToList() });
        double restyle = sw.Elapsed.TotalMilliseconds;

        double ratio = (double)distinct / elements;
        Assert.True(ratio < 0.9, "style sharing ratio " + ratio.ToString("F3", CultureInfo.InvariantCulture));
        if (print)
        {
            Console.WriteLine($"NUMBERS nodes={doc.NodeCount} elements={elements} rules={s.RuleCount} focusables={focusables}");
            Console.WriteLine($"NUMBERS gather+index {gather:F1} ms, {(a1 - a0) / 1024} KiB");
            Console.WriteLine($"NUMBERS compute all {compute:F1} ms, {(a2 - a1) / 1024} KiB, distinct styles {distinct} ({ratio:P1} of elements)");
            Console.WriteLine($"NUMBERS focus overlay {overlay:F3} ms per focusable (avg {touched / (double)sample:F1} elements restyled), sample {sample}");
            Console.WriteLine($"NUMBERS restyle of one attribute change {restyle:F2} ms ({rr.RestyledElements.Count} restyled, full={rr.FullRecompute}); parse of changed page {parse2:F1} ms");
        }
    }
}
