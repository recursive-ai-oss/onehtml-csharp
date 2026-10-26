using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// Maps Chrome captures of the pages in <c>testdata/rendered/pages</c> (kept under <c>testdata/rendered/captures</c>) and checks key
/// facts. A capture directory holds <c>snapshot.json[.gz]</c> and <c>dom.json[.gz]</c>, and is found either by the page's name or by the
/// SHA-256 of the page's bytes appearing in its path. Without captures the tests skip.
/// </summary>
public sealed class CaptureTests
{
    private static readonly string Pages = Path.Combine(TestPaths.TestData, "rendered", "pages");
    private static readonly string Captures = Path.Combine(TestPaths.TestData, "rendered", "captures");

    private sealed record Capture(string Dir, DomSnapshot Snapshot, Document Document, DomSnapshotMapResult Result)
    {
        public RenderedFacts Facts => Result.Facts!;

        public Node Q(string selector) => Document.Root.QuerySelector(selector);
    }

    private static byte[]? ReadMaybeGz(string dir, string name)
    {
        string plain = Path.Combine(dir, name + ".json");
        string gz = plain + ".gz";
        if (File.Exists(plain))
        {
            return File.ReadAllBytes(plain);
        }

        if (File.Exists(gz))
        {
            using var f = File.OpenRead(gz);
            using var z = new GZipStream(f, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            z.CopyTo(ms);
            return ms.ToArray();
        }

        return null;
    }

    private static IEnumerable<string> CaptureDirs(string root) =>
        !Directory.Exists(root)
            ? []
            : Directory.EnumerateFiles(root, "snapshot.json*", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName).Where(d => d is not null).Select(d => d!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static Capture Load(string dir)
    {
        byte[] snapJson = ReadMaybeGz(dir, "snapshot") ?? throw new InvalidOperationException("no snapshot in " + dir);
        byte[] domJson = ReadMaybeGz(dir, "dom") ?? throw new InvalidOperationException("no dom in " + dir);
        using JsonDocument dom = JsonDocument.Parse(domJson);
        string outer = dom.RootElement.GetProperty("outerHTML").GetString()!;
        string doctype = dom.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        DomSnapshot snapshot = DomSnapshot.Parse(snapJson);
        Document doc = Document.Parse(doctype + outer);
        return new Capture(dir, snapshot, doc, DomSnapshotAdapter.Map(snapshot, doc));
    }

    private static Capture? ForPage(string page)
    {
        string file = Path.Combine(Pages, page + ".html");
        string sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        foreach (string dir in CaptureDirs(Captures))
        {
            string rel = Path.GetRelativePath(Captures, dir).Replace('\\', '/');
            if (rel.Contains(sha, StringComparison.Ordinal) || rel.Split('/').Contains(page, StringComparer.Ordinal))
            {
                return Load(dir);
            }
        }

        return null;
    }

    private static Capture Need(string page)
    {
        Capture? c = ForPage(page);
        if (c is null)
        {
            Assert.Skip($"no capture of {page}.html under testdata/rendered/captures");
        }

        Assert.True(c!.Result.Succeeded, c.Result.Refusal?.ToString());
        Assert.True(c.Result.Accounting!.IsComplete, c.Result.Accounting.ToString());
        return c;
    }

    [Fact]
    public void Every_fixture_capture_maps_with_every_node_accounted_for()
    {
        List<string> dirs = [.. CaptureDirs(Captures)];
        if (dirs.Count == 0)
        {
            Assert.Skip("no captures under testdata/rendered/captures");
        }

        foreach (string dir in dirs)
        {
            Capture c = Load(dir);
            Assert.True(c.Result.Succeeded, dir + ": " + c.Result.Refusal);
            Assert.True(c.Result.Accounting!.IsComplete, dir + ": " + c.Result.Accounting);
            Assert.True(c.Facts.BoxCount > 0, dir);
        }
    }

    [Fact]
    public void Local_samples_map_when_a_directory_is_given()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEHTML_RENDERED_SAMPLES");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Assert.Skip("ONEHTML_RENDERED_SAMPLES is not set");
        }

        var failures = new List<string>();
        int count = 0;
        foreach (string d in CaptureDirs(dir))
        {
            count++;
            Capture c = Load(d);
            if (!c.Result.Succeeded)
            {
                failures.Add(Path.GetRelativePath(dir, d) + ": " + c.Result.Refusal);
            }
            else if (!c.Result.Accounting!.IsComplete)
            {
                failures.Add(Path.GetRelativePath(dir, d) + ": accounting " + c.Result.Accounting);
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {count} refused:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Hidden_content_has_no_layout()
    {
        Capture c = Need("hidden");
        Assert.True(c.Facts[c.Q("#visible")].HasLayout);
        Assert.False(c.Facts[c.Q("#none")].HasLayout);
        Assert.False(c.Facts[c.Q("#none-child")].HasLayout);
        Assert.False(c.Facts[c.Q("#none-parent")].HasLayout);
        Assert.False(c.Facts[c.Q("#hidden-attr")].HasLayout);
        Assert.False(c.Facts[c.Q("#contents")].HasLayout);
        Assert.True(c.Facts[c.Q("#transparent")].HasLayout);
        Assert.True(c.Facts[c.Q("#vis-hidden")].HasLayout);
        if (c.Facts.ComputedStyleNames.Contains("visibility"))
        {
            Assert.Equal("hidden", c.Facts[c.Q("#vis-hidden")].GetStyle("visibility"));
            Assert.Equal("visible", c.Facts[c.Q("#vis-child")].GetStyle("visibility"));
        }
    }

    [Fact]
    public void Positioned_boxes_and_paint_order()
    {
        Capture c = Need("positioned");
        NodeFacts a = c.Facts[c.Q("#a")], b = c.Facts[c.Q("#b")];
        Assert.Equal(120, a.Layout.BorderBox.Width);
        Assert.Equal(80, a.Layout.BorderBox.Height);
        Assert.Equal(40, b.Layout.BorderBox.X - a.Layout.BorderBox.X);
        Assert.True(a.IsStackingContext && b.IsStackingContext);
        Assert.True(b.Layout.PaintOrder > a.Layout.PaintOrder);
        Assert.True(c.Facts[c.Q("#ctx")].IsStackingContext);
        Assert.True(c.Facts[c.Q("#ctx-child")].IsStackingContext);
        Assert.Equal("ctx-child", c.Facts.GetStackingContext(c.Q("#ctx-child")).Id);

        // Offset rectangles are relative to the offset parent; border boxes are in document coordinates.
        Assert.Equal(20, a.Layout.OffsetRect!.Value.X);
        Assert.Equal(20, a.Layout.OffsetRect.Value.Y);
        Assert.Equal(a.Layout.BorderBox.Width, a.Layout.ClientRect!.Value.Width);
        Assert.Equal(0.9, c.Facts[c.Q("#ctx-child")].Layout.TextColorOpacity!.Value, 3);
        Assert.Equal("rgb(204, 0, 0)", a.Layout.BlendedBackgroundColor);
        Assert.Equal("absolute", a.Layout.GetStyle("position"));
        Assert.Equal("fixed", c.Facts[c.Q("#fixed")].GetStyle("position"));
    }

    [Fact]
    public void A_wrapped_paragraph_has_several_text_boxes()
    {
        Capture c = Need("wrapped");
        NodeFacts text = c.Facts[c.Q("#narrow").FirstChild];
        Assert.True(text.HasLayout);
        Assert.True(text.Layout.TextBoxes.Length >= 3, "boxes: " + text.Layout.TextBoxes.Length);
        double y = double.MinValue;
        foreach (TextBox box in text.Layout.TextBoxes)
        {
            Assert.True(box.Bounds.Y >= y);
            y = box.Bounds.Y;
        }
    }

    [Fact]
    public void Generated_content_is_attached_to_its_host()
    {
        Capture c = Need("pseudo");
        IReadOnlyList<GeneratedBox> quote = c.Facts[c.Q("#quote")].GeneratedBoxes;
        Assert.Contains(quote, g => g.PseudoType == "before");
        Assert.Contains(quote, g => g.PseudoType == "after");
        GeneratedBox before = quote.First(g => g.PseudoType == "before");
        Assert.Equal("\u201C", before.Text);
        Assert.True(before.HasLayout);
        Assert.Equal(1, before.Boxes.Sum(b => b.TextBoxes.Length));
        Assert.Equal("New: ", c.Facts[c.Q("#badge")].GeneratedBoxes.Single(g => g.PseudoType == "before").Text);
        Assert.Equal("Label:", c.Facts[c.Q("#named")].GeneratedBoxes.Single().Text);
        Assert.Empty(c.Facts[c.Q("#none")].GeneratedBoxes);
        Assert.Contains(c.Facts[c.Q("#li1")].GeneratedBoxes, g => g.PseudoType == "marker");
    }

    [Fact]
    public void A_pseudo_element_behind_its_host_paints_first()
    {
        Capture c = Need("background");
        NodeFacts host = c.Facts[c.Q("#behind")];
        GeneratedBox before = Assert.Single(host.GeneratedBoxes);
        Assert.True(before.HasLayout);
        Assert.True(before.Layout.PaintOrder < host.Layout.PaintOrder);
        Assert.Equal("rgb(0, 51, 102)", c.Facts[c.Q("#solid")].GetStyle("background-color"));
    }

    [Fact]
    public void Form_controls_with_shadow_trees_map_and_keep_their_state()
    {
        Capture c = Need("forms");
        Assert.True(c.Facts[c.Q("#agree")].InputChecked);
        Assert.False(c.Facts[c.Q("#r1")].InputChecked);
        Assert.True(c.Facts[c.Q("#r2")].InputChecked);
        Assert.Equal("Ada", c.Facts[c.Q("#name")].InputValue);
        Assert.True(c.Facts[c.Q("#name")].HasLayout);
        Assert.True(c.Facts[c.Q("option[selected]")].OptionSelected);
    }

    [Fact]
    public void Frames_are_other_documents()
    {
        Capture c = Need("iframe");
        Assert.True(c.Facts.Frames.Count >= 2);
        Assert.True(c.Snapshot.Documents.Count >= 2);
        Assert.True(c.Result.Accounting!.OtherDocuments > 0);
        Assert.Contains(c.Facts.Frames, f => f.NodeIndex == c.Q("#f1").Index);
    }

    [Fact]
    public void Each_srcdoc_frame_maps_against_a_document_parsed_from_its_srcdoc()
    {
        Capture c = Need("iframe");
        foreach (FrameLink link in c.Facts.Frames)
        {
            Node owner = c.Document.GetNode(link.NodeIndex);
            Document frame = Document.Parse(owner.GetAttribute("srcdoc") ?? string.Empty);
            DomSnapshotMapResult r = DomSnapshotAdapter.Map(c.Snapshot, frame, new DomSnapshotMapOptions { DocumentIndex = link.SnapshotDocumentIndex });
            Assert.True(r.Succeeded, owner.Id + ": " + r.Refusal);
            Assert.True(r.Accounting!.IsComplete, owner.Id);
        }

        // A frame that was not displayed has no layout boxes of its own.
        Assert.False(c.Facts[c.Q("#f3")].HasLayout);
    }

    [Fact]
    public void Template_content_maps_and_has_no_layout()
    {
        Capture c = Need("template");
        Node inner = c.Q("#tpl").TemplateContent.FirstChild;
        Assert.Equal("in-tpl", inner.Id);
        Assert.False(c.Facts[inner].HasLayout);
    }

    [Fact]
    public void Whitespace_markup_maps()
    {
        Capture c = Need("whitespace");
        Assert.True(c.Facts[c.Q("#s1")].HasLayout);
        Assert.False(c.Facts[c.Q("#a").FirstChild].HasLayout);
    }

    [Fact]
    public void A_page_whose_script_changes_the_dom_maps_when_parsed_from_the_serialized_dom()
    {
        Capture c = Need("script");
        Assert.False(c.Q("#target").IsNull);
        Assert.True(c.Facts[c.Q("#target")].SnapshotNodeIndex >= 0);
    }

    [Fact]
    public void Edited_dom_is_refused_for_a_capture()
    {
        List<string> dirs = [.. CaptureDirs(Captures)];
        if (dirs.Count == 0)
        {
            Assert.Skip("no captures under testdata/rendered/captures");
        }

        Capture c = Load(dirs[0]);
        string html = c.Document.SerializeChildren(c.Document.Root);
        Document extra = Document.Parse(html.Replace("</body>", "<p>added</p></body>", StringComparison.Ordinal));
        DomSnapshotMapResult r = DomSnapshotAdapter.Map(c.Snapshot, extra);
        Assert.False(r.Succeeded);
        Assert.Equal(RefusalReason.NodeCountMismatch, r.Refusal!.Reason);
    }
}
