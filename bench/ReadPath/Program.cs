// Times the calls a visible-text extractor makes on one page, one step at a time, on a synthetic contact page (about 8 KB, 236
// elements) built here: parse, create the styles, build the accessibility tree and its navigation lists (or the landmarks-and-headings
// shortcut), and read the computed style of every element the way an extractor deciding what is visible does.
// Usage: dotnet run -c Release -- [repeats]   (add -p:WithLandmarksApi=true to time the shortcut)
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Css;

int repeats = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 400;

foreach (bool withStyles in new[] { true, false })
{
    string text = Page(withStyles);
    Document doc = Document.Parse(text);
    int elements = 0;
    foreach (Node unused in doc.Root.DescendantElements)
    {
        elements++;
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"page with{(withStyles ? string.Empty : "out")} author styles: {text.Length} chars, {elements} elements"));
    // The consumer's order on one page, timed as growing prefixes so each step is measured in context: the walk after the tree
    // finds the styles the tree already computed. Each row is the cost of the whole prefix; the step is the difference.
    double parse = Time("parse", repeats, () => Document.Parse(text).NodeCount);
    double create = Time("parse + styles", repeats, () => DocumentStyles.Create(Document.Parse(text)).GetHashCode());
    double fullTree = Time("parse + styles + full tree + navigation lists", repeats, () =>
    {
        Document d = Document.Parse(text);
        NavigationLists lists = AccessibilityTree.Build(d, DocumentStyles.Create(d)).CreateNavigationLists();
        return lists.Landmarks.Count + lists.Headings.Count;
    });
    double fullAll = Time("parse + styles + full tree + lists + style walk", repeats, () =>
    {
        Document d = Document.Parse(text);
        DocumentStyles s = DocumentStyles.Create(d);
        NavigationLists lists = AccessibilityTree.Build(d, s).CreateNavigationLists();
        return lists.Landmarks.Count + lists.Headings.Count + ReadWalk(d, s);
    });
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  steps in context (full path): parse {parse:F1}, styles {create - parse:F1}, tree+lists {fullTree - create:F1}, walk {fullAll - fullTree:F1}, total {fullAll:F1} us"));
#if WITH_LANDMARKS_API
    double shortTree = Time("parse + styles + page outline", repeats, () =>
    {
        Document d = Document.Parse(text);
        PageOutline l = PageOutline.Create(d, DocumentStyles.Create(d), includeHeadingNames: false);
        return l.Landmarks.Count + l.Headings.Count;
    });
    double shortAll = Time("parse + styles + page outline + style walk", repeats, () =>
    {
        Document d = Document.Parse(text);
        DocumentStyles s = DocumentStyles.Create(d);
        PageOutline l = PageOutline.Create(d, s, includeHeadingNames: false);
        return l.Landmarks.Count + l.Headings.Count + ReadWalk(d, s);
    });
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  steps in context (shortcut): parse {parse:F1}, styles {create - parse:F1}, landmarks {shortTree - create:F1}, walk {shortAll - shortTree:F1}, total {shortAll:F1} us"));
#endif
    Console.WriteLine();
}

static double Time(string label, int repeats, Func<int> work)
{
    for (int i = 0; i < 30; i++)
    {
        work();
    }

    double best = double.MaxValue;
    long bytes = 0;
    for (int round = 0; round < 7; round++)
    {
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < repeats; i++)
        {
            work();
        }

        sw.Stop();
        bytes = (GC.GetAllocatedBytesForCurrentThread() - a0) / repeats;
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000 / repeats);
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {label,-52} {best,9:F1} us {bytes / 1024.0,8:F1} KB"));
    return best;
}

// The properties a visible-text reader looks at for each element: what removes it, what hides its text, how it breaks lines.
static int ReadWalk(Document doc, DocumentStyles styles)
{
    int k = 0;
    foreach (Node el in doc.Root.DescendantElements)
    {
        ComputedStyle s = styles.GetStyle(el);
        string display = s.GetPropertyValue("display");
        k += display.Length + s.GetPropertyValue("opacity").Length;
        string position = s.GetPropertyValue("position");
        if (position is "absolute" or "fixed")
        {
            k += s.GetPropertyValue("left").Length + s.GetPropertyValue("top").Length + s.GetPropertyValue("right").Length;
        }

        string overflowX = s.GetPropertyValue("overflow-x");
        string overflowY = s.GetPropertyValue("overflow-y");
        if (overflowX is "hidden" or "clip" || overflowY is "hidden" or "clip")
        {
            k += s.GetPropertyValue("height").Length + s.GetPropertyValue("width").Length + s.GetPropertyValue("max-height").Length + s.GetPropertyValue("max-width").Length;
        }

        k += s.GetPropertyValue("clip").Length + s.GetPropertyValue("clip-path").Length;
        k += s.GetPropertyValue("visibility").Length + s.GetPropertyValue("text-indent").Length + s.GetPropertyValue("white-space").Length;
        k += s.GetPropertyValue("unicode-bidi").Length + s.GetPropertyValue("direction").Length;
        k += (int)s.FontSizePx;
    }

    return k;
}

static string Page(bool withStyles)
{
    string[] given = ["Jane", "John", "Maria", "Robert", "Linda", "James", "Patricia", "Michael"];
    string[] family = ["Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis"];
    string[] street = ["Main St", "Oak Avenue", "Maple Drive", "Cedar Ln"];
    const string Filler = "We have served the community for more than twenty years and we are proud of the work we do.";
    var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><title>Contact us</title><meta charset=\"utf-8\">");
    if (withStyles)
    {
        html.Append("<style>.hide{display:none}.person{margin:4px}</style>");
    }

    html.Append("</head><body><nav><a href=\"/\">Home</a> <a href=\"/about\">About</a> <a href=\"/contact\">Contact</a></nav><main>");
    for (int i = 0; i < 24; i++)
    {
        string g = given[i % given.Length];
        string f = family[i * 3 % family.Length];
        html.Append("<section class=\"person\"><h3>").Append(g).Append(' ').Append(f).Append("</h3>");
        html.Append("<p>Acme Holdings LLC</p>");
        html.Append("<p>").Append(10 + (i * 37)).Append(' ').Append(street[i % street.Length]).Append("</p>");
        html.Append("<p>Springfield, IL 62704</p>");
        html.Append("<p>Tel: (").Append(200 + i).Append(") 555-").Append(1000 + i).Append("</p>");
        html.Append("<p>Email: ").Append(g.ToUpperInvariant()).Append('.').Append(f.ToUpperInvariant()).Append("@acme").Append(i).Append(".com</p>");
        html.Append("<p>www.acme").Append(i).Append(".com</p><p>").Append(Filler).Append("</p>");
        if (withStyles && i % 6 == 0)
        {
            html.Append("<p class=\"hide\">hidden@example.com</p><p style=\"display:none\">555-0000</p>");
        }

        html.Append("</section>");
    }

    return html.Append("</main><footer>Copyright</footer></body></html>").ToString();
}
