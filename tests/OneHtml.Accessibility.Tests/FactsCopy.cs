using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// Loads the committed Chrome captures through the DOMSnapshot adapter, rebuilds the same facts through <see cref="RenderedFactsBuilder"/>
/// from the adapter's own values (read back through the public members), and dumps every public fact for comparison.
/// </summary>
internal static class FactsCopy
{
    public static string CapturesDir { get; } = Path.GetFullPath(Path.Combine(TestPaths.TestData, "rendered", "captures"));

    public static IEnumerable<string> Pages() => Directory.GetDirectories(CapturesDir).Where(d => File.Exists(Path.Combine(d, "snapshot.json.gz"))).Select(Path.GetFileName)!;

    private static byte[] ReadGz(string dir, string name)
    {
        using FileStream f = File.OpenRead(Path.Combine(dir, name + ".json.gz"));
        using var z = new GZipStream(f, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    public static (Document Doc, RenderedFacts Facts) Load(string page) => LoadDir(Path.Combine(CapturesDir, page), requireSuccess: true)!.Value;

    /// <summary>
    /// The capture directories of a larger store (<c>ONEHTML_CAPTURE_DIR</c>, laid out as <c>captures/aa/sha/1280x800/</c>), every
    /// <c>ONEHTML_CAPTURE_EVERY</c>th one in name order; empty when the variable is not set.
    /// </summary>
    public static List<string> StoreDirs()
    {
        string? root = Environment.GetEnvironmentVariable("ONEHTML_CAPTURE_DIR");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return [];
        }

        int every = int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_CAPTURE_EVERY"), out int e) && e > 0 ? e : 20;
        var all = new List<string>();
        foreach (string d in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
        {
            foreach (string p in Directory.GetDirectories(d).Order(StringComparer.Ordinal))
            {
                string v = Path.Combine(p, "1280x800");
                if (File.Exists(Path.Combine(v, "snapshot.json.gz")) && File.Exists(Path.Combine(v, "dom.json.gz")))
                {
                    all.Add(v);
                }
            }
        }

        return [.. all.Where((_, i) => i % every == 0)];
    }

    /// <summary>Loads one capture directory; with <paramref name="requireSuccess"/> false, null when the adapter refuses the snapshot for this DOM.</summary>
    public static (Document Doc, RenderedFacts Facts)? LoadDir(string dir, bool requireSuccess)
    {
        using JsonDocument dom = JsonDocument.Parse(ReadGz(dir, "dom"));
        string doctype = dom.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        Document doc = Document.Parse(doctype + dom.RootElement.GetProperty("outerHTML").GetString());
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(DomSnapshot.Parse(ReadGz(dir, "snapshot")), doc, new DomSnapshotMapOptions { ViewportWidth = 1280, ViewportHeight = 800 });
        if (!map.Succeeded && !requireSuccess)
        {
            return null;
        }

        Assert.True(map.Succeeded, map.Refusal?.ToString());
        return (doc, map.Facts!);
    }

    /// <summary>Appends a line to the file named by <c>ONEHTML_CAPTURE_REPORT</c>, if any.</summary>
    public static void Report(string line)
    {
        if (Environment.GetEnvironmentVariable("ONEHTML_CAPTURE_REPORT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }

    private static BoxInput ToInput(BoxFacts b, IReadOnlyList<string> names) => new()
    {
        BorderBox = b.BorderBox,
        OffsetRect = b.OffsetRect,
        ClientRect = b.ClientRect,
        ScrollRect = b.ScrollRect,
        PaintOrder = b.PaintOrder,
        IsStackingContext = b.IsStackingContext,
        LayoutText = b.LayoutText,
        TextBoxes = b.TextBoxes.ToArray(),
        BlendedBackgroundColor = b.BlendedBackgroundColor,
        TextColorOpacity = b.TextColorOpacity,
        Styles = names.Select(n => b.GetStyle(n)).ToArray(),
    };

    public static RenderedFacts Rebuild(Document doc, RenderedFacts f)
    {
        var b = new RenderedFactsBuilder(doc);
        b.SetViewport(f.ViewportWidth, f.ViewportHeight);
        b.SetContext(f.Context);
        b.SetPage(f.Page);
        b.SetSource(f.Product, f.ProtocolVersion);
        b.SetComputedStyleNames(f.ComputedStyleNames);
        for (int i = 0; i < f.NodeCount; i++)
        {
            Node node = doc.GetNode(i);
            NodeFacts nf = f[node];
            if (nf.HasLayout)
            {
                b.AddBox(node, ToInput(nf.Layout, f.ComputedStyleNames));
            }

            b.SetBackendNodeId(node, nf.BackendNodeId);
            b.SetSnapshotNodeIndex(node, nf.SnapshotNodeIndex);
            if (nf.InputValue is { } iv)
            {
                b.SetInputValue(node, iv);
            }

            if (nf.TextValue is { } tv)
            {
                b.SetTextValue(node, tv);
            }

            b.SetChecked(node, nf.InputChecked);
            b.SetOptionSelected(node, nf.OptionSelected);
            b.SetClickable(node, nf.IsClickable);
            if (nf.Background is { } bg)
            {
                b.SetBackgroundSample(node, bg);
            }

            foreach (GeneratedBox g in nf.GeneratedBoxes)
            {
                BoxInput[] boxes = [.. g.Boxes.Select(x => ToInput(x, f.ComputedStyleNames))];
                string own = g.HasLayout ? Dump(g.Layout, f.ComputedStyleNames) : string.Empty;
                int layoutIndex = g.HasLayout ? Array.FindIndex(g.Boxes.ToArray(), x => Dump(x, f.ComputedStyleNames) == own) : -1;
                b.AddGeneratedContent(node, new GeneratedBoxInput(g.PseudoType)
                {
                    Identifier = g.Identifier.Length == 0 ? null : g.Identifier,
                    Text = g.Text.Length == 0 ? null : g.Text,
                    NodeCount = g.SnapshotNodeCount,
                    Boxes = boxes,
                    LayoutIndex = layoutIndex,
                });
            }
        }

        foreach (FrameLink link in f.Frames)
        {
            b.SetFrame(doc.GetNode(link.NodeIndex), link.SnapshotDocumentIndex);
        }

        return b.Build();
    }

    private static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static string Rect(LayoutRect? r) => r is { } v ? $"[{R(v.X)},{R(v.Y)},{R(v.Width)},{R(v.Height)}]" : "-";

    private static string Dump(BoxFacts b, IReadOnlyList<string> names)
    {
        var sb = new StringBuilder();
        sb.Append(Rect(b.BorderBox)).Append(' ').Append(Rect(b.OffsetRect)).Append(' ').Append(Rect(b.ClientRect)).Append(' ').Append(Rect(b.ScrollRect));
        sb.Append(" paint=").Append(b.PaintOrder?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(" stack=").Append(b.IsStackingContext);
        sb.Append(" text=").Append(b.LayoutText is null ? "-" : JsonSerializer.Serialize(b.LayoutText)).Append(" bg=").Append(b.BlendedBackgroundColor ?? "-");
        sb.Append(" op=").Append(b.TextColorOpacity is { } o ? R(o) : "-").Append(" tb=");
        foreach (TextBox t in b.TextBoxes)
        {
            sb.Append(t.Start).Append('+').Append(t.Length).Append(Rect(t.Bounds)).Append(';');
        }

        sb.Append(" st=");
        foreach (string n in names)
        {
            sb.Append(b.GetStyle(n) ?? "-").Append('|');
        }

        return sb.ToString();
    }

    /// <summary>Every public fact of every node, one line each, plus the document-level facts.</summary>
    public static string Dump(Document doc, RenderedFacts f)
    {
        var sb = new StringBuilder();
        sb.Append($"doc={f.DocumentId == doc.Id} nodes={f.NodeCount} fp={f.StructureFingerprint} product={f.Product} proto={f.ProtocolVersion} vp={R(f.ViewportWidth)}x{R(f.ViewportHeight)} ctx={f.Context is null}\n");
        sb.Append(f.Page).Append(" boxes=").Append(f.BoxCount).Append(" gen=").Append(f.GeneratedBoxCount).Append(" samples=").Append(f.BackgroundSampleCount).Append(" names=").Append(string.Join(',', f.ComputedStyleNames)).Append('\n');
        foreach (FrameLink l in f.Frames)
        {
            sb.Append("frame ").Append(l.NodeIndex).Append("->").Append(l.SnapshotDocumentIndex).Append('\n');
        }

        for (int i = 0; i < f.NodeCount; i++)
        {
            NodeFacts n = f[doc.GetNode(i)];
            sb.Append(i).Append(" backend=").Append(n.BackendNodeId).Append(" snap=").Append(n.SnapshotNodeIndex);
            // A node the capture did not contain has no stacking context in the adapter's facts; the builder derives one from the tree.
            sb.Append(" stackroot=").Append(n.SnapshotNodeIndex >= 0 ? n.StackingContextNodeIndex : -2);
            sb.Append(" in=").Append(n.InputValue ?? "-").Append(" tv=").Append(n.TextValue ?? "-");
            sb.Append(" chk=").Append(n.InputChecked).Append(n.OptionSelected).Append(n.IsClickable);
            if (n.Background is { } bg)
            {
                sb.Append(" bgs=").Append(bg);
            }

            sb.Append(n.HasLayout ? " box: " + Dump(n.Layout, f.ComputedStyleNames) : " nobox");
            foreach (GeneratedBox g in n.GeneratedBoxes)
            {
                sb.Append("\n   ::").Append(g.PseudoType).Append('(').Append(g.Identifier).Append(") nodes=").Append(g.SnapshotNodeCount).Append(" text=").Append(JsonSerializer.Serialize(g.Text));
                sb.Append(" own=").Append(g.HasLayout ? Dump(g.Layout, f.ComputedStyleNames) : "-");
                foreach (BoxFacts x in g.Boxes)
                {
                    sb.Append("\n      ").Append(Dump(x, f.ComputedStyleNames));
                }
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
