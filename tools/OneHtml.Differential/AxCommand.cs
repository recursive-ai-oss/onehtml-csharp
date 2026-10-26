using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Tree;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// The Chrome accessibility-tree differential: for each captured page and viewport, builds our tree from Chrome's serialized DOM and
/// compares it with Chrome's <c>Accessibility.getFullAXTree</c> node by node. See <c>docs/chrome-ax-differential.md</c>.
/// </summary>
internal static class AxCommand
{
    /// <summary>Version of the output files.</summary>
    public const string Schema = "onehtml-ax-differential/1";

    internal sealed record CaptureRef(string Sha, string Viewport, string Dir);

    internal sealed class CaptureResult
    {
        public required CaptureRef Ref { get; init; }

        public string Url { get; set; } = string.Empty;

        /// <summary>ok, refused, skipped (capture incomplete) or error.</summary>
        public string Status { get; set; } = "ok";

        public string Detail { get; set; } = string.Empty;

        /// <summary>The comparison the report is about: with facts for modes <c>on</c> and <c>both</c>, without for <c>off</c>.</summary>
        public AxPageDiff? Diff { get; set; }

        /// <summary>The same page's comparison without facts, in mode <c>both</c> only.</summary>
        public AxPageDiff? Baseline { get; set; }
    }

    public static int Run(Options o)
    {
        string root = o.Corpus;
        List<CaptureRef> captures = FindCaptures(root);
        if (o.Pages is { } pages)
        {
            captures.RemoveAll(c => !pages.Any(p => c.Sha.StartsWith(p, StringComparison.Ordinal)));
        }

        if (captures.Count > o.Limit)
        {
            captures.RemoveRange(o.Limit, captures.Count - o.Limit);
        }

        StylesheetStore? styles = o.StyleCorpus is { Length: > 0 } sc ? StylesheetStore.Load(sc) : null;
        string facts = o.Facts;
        Console.WriteLine($"ax: {captures.Count} captures, facts {facts}, jobs {o.Jobs}, timeout {o.TimeoutSeconds}s, {styles?.Count ?? 0} stylesheets");
        var results = new List<CaptureResult>(captures.Count);
        var clock = Stopwatch.StartNew();
        int done = 0;
        Parallel.ForEach(captures, new ParallelOptions { MaxDegreeOfParallelism = o.Jobs }, cap =>
        {
            CaptureResult r = RunCapture(cap, styles, o.TimeoutSeconds, o.Dump, facts);
            lock (results)
            {
                results.Add(r);
            }

            int n = Interlocked.Increment(ref done);
            if (n % 100 == 0)
            {
                Console.WriteLine($"  {n}/{captures.Count} after {clock.Elapsed.TotalSeconds:0}s");
            }
        });

        results.Sort(static (a, b) => string.CompareOrdinal(a.Ref.Sha + "/" + a.Ref.Viewport, b.Ref.Sha + "/" + b.Ref.Viewport));
        Directory.CreateDirectory(o.Out);
        if (o.Dump)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(o.Out, "dump"));
            foreach (CaptureResult r in results.Where(r => r.Diff is not null))
            {
                File.WriteAllLines(System.IO.Path.Combine(o.Out, "dump", r.Ref.Sha[..Math.Min(12, r.Ref.Sha.Length)] + "-" + r.Ref.Viewport + ".txt"), r.Diff!.Dump, new UTF8Encoding(false, false));
            }
        }

        string env = ReadEnvironment(root);
        AxReport.Write(o, results, env, facts, clock.Elapsed.TotalSeconds);
        int ok = results.Count(r => r.Status == "ok");
        Console.WriteLine($"ax: {ok} compared, {results.Count(r => r.Status == "refused")} refused, {results.Count(r => r.Status is "skipped" or "error")} skipped or failed; summary in {Path.GetFullPath(o.Out)}");
        return 0;
    }

    /// <summary>Finds capture directories (<c>…/&lt;sha&gt;/&lt;viewport&gt;/</c> with a <c>meta.json</c> or <c>errors.json</c>) below a capture root.</summary>
    internal static List<CaptureRef> FindCaptures(string root)
    {
        var list = new List<CaptureRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return list;
        }

        foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string name = System.IO.Path.GetFileName(f);
            if (name is not ("meta.json" or "errors.json"))
            {
                continue;
            }

            string dir = System.IO.Path.GetDirectoryName(f)!;
            string viewport = System.IO.Path.GetFileName(dir);
            string sha = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(dir)!);
            if (seen.Add(dir))
            {
                list.Add(new CaptureRef(sha, viewport, dir));
            }
        }

        list.Sort(static (a, b) => string.CompareOrdinal(a.Sha + "/" + a.Viewport, b.Sha + "/" + b.Viewport));
        return list;
    }

    internal static string ReadEnvironment(string root)
    {
        string? dir = root;
        for (int i = 0; i < 3 && dir is not null; i++, dir = System.IO.Path.GetDirectoryName(dir))
        {
            string p = System.IO.Path.Combine(dir, "environment.json");
            if (!File.Exists(p))
            {
                continue;
            }

            try
            {
                using JsonDocument d = JsonDocument.Parse(File.ReadAllBytes(p));
                if (d.RootElement.TryGetProperty("chrome", out JsonElement c))
                {
                    string product = c.TryGetProperty("product", out JsonElement pr) ? pr.GetString() ?? string.Empty : string.Empty;
                    string rev = c.TryGetProperty("revision", out JsonElement rv) ? rv.GetString() ?? string.Empty : string.Empty;
                    string proto = c.TryGetProperty("protocolVersion", out JsonElement pv) ? pv.GetString() ?? string.Empty : string.Empty;
                    return $"{product} (revision {rev}, protocol {proto})";
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    internal static byte[]? ReadFile(string dir, string name)
    {
        string gz = System.IO.Path.Combine(dir, name + ".json.gz");
        if (File.Exists(gz))
        {
            using FileStream f = File.OpenRead(gz);
            using var z = new GZipStream(f, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            z.CopyTo(ms);
            return ms.ToArray();
        }

        string plain = System.IO.Path.Combine(dir, name + ".json");
        return File.Exists(plain) ? File.ReadAllBytes(plain) : null;
    }

    private static CaptureResult RunCapture(CaptureRef cap, StylesheetStore? sheets, int timeoutSeconds, bool dump, string factsMode)
    {
        var r = new CaptureResult { Ref = cap };
        (CaptureResult? res, string? failure) = Guard.Run(ct => Compare(cap, sheets, r, dump, factsMode, ct), timeoutSeconds);
        if (failure is not null)
        {
            r.Status = "error";
            r.Detail = failure;
            return r;
        }

        return res!;
    }

    private static CaptureResult Compare(CaptureRef cap, StylesheetStore? sheets, CaptureResult r, bool dump, string factsMode, CancellationToken ct)
    {
        byte[]? meta = ReadFile(cap.Dir, "meta");
        if (meta is null)
        {
            r.Status = "skipped";
            r.Detail = "capture failed (no meta.json)";
            return r;
        }

        using JsonDocument metaDoc = JsonDocument.Parse(meta);
        JsonElement m = metaDoc.RootElement;
        r.Url = m.TryGetProperty("url", out JsonElement u) ? u.GetString() ?? string.Empty : string.Empty;
        if (m.TryGetProperty("ok", out JsonElement okv) && okv.ValueKind == JsonValueKind.False)
        {
            r.Status = "skipped";
            r.Detail = "capture not ok";
            return r;
        }

        byte[]? axJson = ReadFile(cap.Dir, "ax");
        byte[]? snapJson = ReadFile(cap.Dir, "snapshot");
        byte[]? domJson = ReadFile(cap.Dir, "dom");
        if (axJson is null || snapJson is null || domJson is null)
        {
            r.Status = "skipped";
            r.Detail = "capture incomplete (" + string.Join(',', new[] { axJson is null ? "ax" : null, snapJson is null ? "snapshot" : null, domJson is null ? "dom" : null }.Where(s => s is not null)) + " missing)";
            return r;
        }

        double width = 1280, height = 800, dpr = 1;
        if (m.TryGetProperty("viewport", out JsonElement vp))
        {
            width = Num(vp, "width", width);
            height = Num(vp, "height", height);
            dpr = Num(vp, "deviceScaleFactor", dpr);
        }

        string baseUrl = m.TryGetProperty("final_url", out JsonElement fu) && fu.ValueKind == JsonValueKind.String ? fu.GetString()! : r.Url;

        using JsonDocument domDoc = JsonDocument.Parse(domJson);
        string outer = domDoc.RootElement.GetProperty("outerHTML").GetString() ?? string.Empty;
        string doctype = domDoc.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        Document doc = Document.Parse(doctype + outer, cancellationToken: ct);

        var context = StyleContext.Default.WithMedia(MediaFeatures.Default with { Width = width, Height = height, DevicePixelRatio = dpr });
        var options = new StyleSourceOptions { BaseUrl = baseUrl, Resolver = sheets is null ? null : sheets.Resolve };
        DocumentStyles styles = DocumentStyles.Create(doc, context, options, ct);

        DomSnapshot snapshot = DomSnapshot.Parse(snapJson);
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(snapshot, doc, new DomSnapshotMapOptions { ViewportWidth = width, ViewportHeight = height, Context = context }, ct);
        if (!map.Succeeded)
        {
            r.Status = "refused";
            r.Detail = map.Refusal is { } rf ? rf.Reason + ": " + rf.Message : "refused";
            return r;
        }

        RenderedFacts rendered = map.Facts!;
        var backendToDom = new Dictionary<long, int>();
        for (int i = 0; i < doc.NodeCount; i++)
        {
            if (rendered.TryGet(doc.GetNode(i), out NodeFacts nf) && nf.BackendNodeId >= 0)
            {
                backendToDom[nf.BackendNodeId] = i;
            }
        }

        int Map(long backend) => backendToDom.TryGetValue(backend, out int d) ? d : -1;

        using JsonDocument axDoc = JsonDocument.Parse(axJson);
        JsonElement frames = axDoc.RootElement.GetProperty("frames");
        if (frames.GetArrayLength() == 0)
        {
            r.Status = "skipped";
            r.Detail = "no frames in ax.json";
            return r;
        }

        JsonElement main = frames[0];
        if (main.TryGetProperty("error", out JsonElement fe) && fe.ValueKind == JsonValueKind.String)
        {
            r.Status = "skipped";
            r.Detail = "ax frame error: " + fe.GetString();
            return r;
        }

        AxSide chrome = AxReader.Read(main.GetProperty("nodes"), Map, null);
        foreach (AxEntry c in chrome.Entries)
        {
            c.Path = "chrome#" + c.Backend.ToString(CultureInfo.InvariantCulture) + " " + c.Role;
            if (dump && c.Dom >= 0 && rendered is not null)
            {
                try
                {
                    Node dn = doc.GetNode(c.Dom);
                    c.Extra = " dom=" + DomPath(dn) + (dn.Kind == NodeKind.Text ? TextNote(rendered, dn) : FactsNote(rendered, dn));
                }
                catch (InvalidOperationException)
                {
                    c.Extra = " dom=?";
                }
            }
        }

        AxPageDiff Run(RenderedFacts? withFacts, bool wantDump)
        {
            AccessibilityTree tree = AccessibilityTree.Build(doc, styles, null, new AccessibilityOptions { BaseUrl = baseUrl, Facts = withFacts }, ct);
            using JsonDocument oursDoc = JsonDocument.Parse(tree.ToChromeJson());
            AxSide ours = AxReader.Read(oursDoc.RootElement.GetProperty("nodes"), static b => b > int.MaxValue ? -1 : (int)b, tree);
            foreach (AxEntry o in ours.Entries)
            {
                o.Path = o.Dom >= 0 ? DomPath(doc.GetNode(o.Dom)) : "#document";
                if (dump && withFacts is not null && o.Dom >= 0)
                {
                    try
                    {
                        Node on = doc.GetNode(o.Dom);
                        o.Extra = on.Kind == NodeKind.Text ? TextNote(withFacts, on) : FactsNote(withFacts, on);
                    }
                    catch (InvalidOperationException ex)
                    {
                        o.Extra = " [note failed: " + ex.Message + "]";
                    }
                }
            }

            return AxComparer.Compare(chrome, ours, wantDump);
        }

        if (factsMode == "off")
        {
            r.Diff = Run(null, dump);
        }
        else
        {
            r.Diff = Run(rendered, dump);
            if (factsMode == "both")
            {
                r.Baseline = Run(null, false);
            }
        }

        return r;
    }

    /// <summary>The layout facts that decide pruning questions, for the dump: display, position, float, generated text and child kinds.</summary>
    private static string FactsNote(RenderedFacts facts, Node node)
    {
        if (!node.IsElement || !facts.TryGet(node, out NodeFacts f) || !f.HasLayout)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(" [");
        sb.Append("d=").Append(f.GetStyle("display")).Append(" p=").Append(f.GetStyle("position")).Append(" f=").Append(f.GetStyle("float"));
        foreach (GeneratedBox g in f.GeneratedBoxes)
        {
            sb.Append(' ').Append(g.PseudoType).Append(g.HasLayout ? "=" : "~").Append('\'').Append(g.Text.Length > 12 ? g.Text[..12] : g.Text).Append('\'');
            if (g.HasLayout)
            {
                sb.Append('(').Append(g.Layout.GetStyle("display")).Append('/').Append(g.Layout.GetStyle("position")).Append('/').Append(g.Layout.GetStyle("float")).Append(')');
            }
        }

        int el = 0;
        int tx = 0;
        int ws = 0;
        for (Node c = node.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.IsElement)
            {
                el++;
            }
            else if (c.Kind == NodeKind.Text)
            {
                if (c.Data.Trim().Length > 0)
                {
                    tx++;
                }
                else
                {
                    ws++;
                }
            }
        }

        sb.Append(" kids=e").Append(el).Append("/t").Append(tx).Append("/w").Append(ws);
        if (node.ParentElement is { IsNull: false } pe && facts.TryGet(pe, out NodeFacts pf))
        {
            sb.Append(" pd=").Append(pf.GetStyle("display"));
        }

        return sb.Append(']').ToString();
    }

    private static string TextNote(RenderedFacts facts, Node text)
    {
        string Sib(Node n)
        {
            if (n.IsNull)
            {
                return "-";
            }

            if (n.Kind == NodeKind.Text)
            {
                return "text";
            }

            string d = facts.TryGet(n, out NodeFacts nf) && nf.HasLayout ? nf.GetStyle("display") ?? "?" : "nobox";
            return n.LocalName + ":" + d;
        }

        bool box = facts.TryGet(text, out NodeFacts f) && f.HasLayout;
        string pd = text.ParentElement is { IsNull: false } pe && facts.TryGet(pe, out NodeFacts pf) ? pf.GetStyle("display") ?? "?" : "-";
        return " [text ws=" + (text.Data.Trim().Length == 0 ? "1" : "0") + " box=" + (box ? f.Layout.TextBoxes.Length : -1) + " prev=" + Sib(text.PreviousSibling) + " next=" + Sib(text.NextSibling) + " pd=" + pd + "]";
    }

    private static double Num(JsonElement o, string name, double fallback) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    /// <summary>A readable path of a node: element names with their position among the element siblings, text as <c>#text[k]</c>.</summary>
    internal static string DomPath(Node node)
    {
        var parts = new List<string>();
        int guard = 0;
        for (Node n = node; !n.IsNull && n.Parent.Document is not null && guard++ < 256; n = n.Parent)
        {
            if (n.IsElement)
            {
                int k = 1;
                for (Node s = n.PreviousElementSibling; !s.IsNull; s = s.PreviousElementSibling)
                {
                    k++;
                }

                parts.Add(n.LocalName + "[" + k.ToString(CultureInfo.InvariantCulture) + "]");
            }
            else
            {
                int k = 1;
                foreach (Node s in n.Parent.ChildNodes)
                {
                    if (s.Equals(n))
                    {
                        break;
                    }

                    k++;
                }

                parts.Add("#text[" + k.ToString(CultureInfo.InvariantCulture) + "]");
            }
        }

        parts.Reverse();
        return parts.Count == 0 ? "#document" : string.Join(" > ", parts);
    }
}

/// <summary>Stylesheets supplied to the cascade by URL: the corpus manifest's stylesheet bodies, or loose <c>.css</c> files of a directory.</summary>
internal sealed class StylesheetStore
{
    private readonly Dictionary<string, CorpusItem> _byUrl = new(StringComparer.Ordinal);
    private readonly List<CorpusItem> _loose = [];

    public int Count => _byUrl.Count + _loose.Count;

    public static StylesheetStore Load(string dir)
    {
        var s = new StylesheetStore();
        bool manifest = File.Exists(System.IO.Path.Combine(dir, "manifest.jsonl"));
        foreach (CorpusItem item in Corpus.Load(dir, pages: false, stylesheets: true))
        {
            if (manifest)
            {
                s._byUrl[Strip(item.Url)] = item;
            }
            else
            {
                s._loose.Add(item);
            }
        }

        return s;
    }

    private static string Strip(string url)
    {
        int h = url.IndexOf('#', StringComparison.Ordinal);
        return h < 0 ? url : url[..h];
    }

    /// <summary>The sheet Chrome was served for a URL: the same stored bytes.</summary>
    public StylesheetResource? Resolve(string absoluteUrl)
    {
        string url = Strip(absoluteUrl);
        CorpusItem? item = _byUrl.GetValueOrDefault(url);
        if (item is null && _loose.Count > 0)
        {
            // A directory of loose files has no URLs: a request matches the file whose relative path ends the URL's path.
            string path = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.AbsolutePath.TrimStart('/') : url;
            item = _loose.FirstOrDefault(i => path.EndsWith(i.Url.Replace('\\', '/'), StringComparison.Ordinal));
        }

        if (item is null || !File.Exists(item.Path) || !IsCss(item.ContentType))
        {
            return null;
        }

        return new StylesheetResource(File.ReadAllBytes(item.Path), Charset(item.ContentType));
    }

    /// <summary>
    /// A browser in standards mode applies a linked sheet only when the response says <c>text/css</c> (a crawl that stored an HTML error page
    /// under a stylesheet's URL gave Chrome nothing, so it must give the cascade nothing). A missing header is accepted.
    /// </summary>
    internal static bool IsCss(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        int semi = contentType.IndexOf(';', StringComparison.Ordinal);
        string essence = (semi >= 0 ? contentType[..semi] : contentType).Trim();
        return essence.Equals("text/css", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Charset(string? contentType)
    {
        if (contentType is null)
        {
            return null;
        }

        int i = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return null;
        }

        string v = contentType[(i + 8)..].Trim().Trim('"', '\'');
        int semi = v.IndexOf(';', StringComparison.Ordinal);
        return semi >= 0 ? v[..semi].Trim() : v;
    }
}
