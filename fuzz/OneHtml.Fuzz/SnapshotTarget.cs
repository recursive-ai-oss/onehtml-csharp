using System;
using System.IO;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Tests;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// Fuzz target for the DOMSnapshot reader, the adapter and the background-sample reader. The first byte picks the path: bit 0 reads the
/// rest as background-sample JSON; otherwise it is a snapshot, which is mapped onto a fixed document (and, with bit 1, has samples
/// applied). Only the typed exceptions may escape the library; a mapping that succeeds must account for every node.
/// </summary>
internal static class SnapshotTarget
{
    private const string Html = "<!DOCTYPE html><div id=a><p>hello</p><input value=x><iframe id=f></iframe></div><template><b>t</b></template><ul><li>one<li>two</ul>";

    private static readonly DomSnapshotLimits Limits = new() { MaxInputBytes = 1 << 20, MaxNodes = 1 << 16, MaxStrings = 1 << 16, MaxLayoutNodes = 1 << 16, MaxTextBoxes = 1 << 16, MaxDocuments = 16, MaxNodeDepth = 512, MaxArrayElements = 1 << 18 };

    private static readonly Lazy<RecursiveAI.OneHtml.Document> Doc = new(() => RecursiveAI.OneHtml.Document.Parse(Html));

    public static void Run(ReadOnlySpan<byte> input)
    {
        if (input.Length < 2)
        {
            return;
        }

        byte flags = input[0];
        ReadOnlySpan<byte> body = input[1..];
        if ((flags & 1) != 0)
        {
            try
            {
                BackgroundSamples samples = BackgroundSamples.Parse(body);
                _ = samples.Entries.Count;
            }
            catch (BackgroundSamplesFormatException)
            {
            }

            return;
        }

        DomSnapshot snapshot;
        try
        {
            snapshot = DomSnapshot.Parse(body, FakeSnapshot.StyleNames, Limits);
        }
        catch (DomSnapshotFormatException)
        {
            return;
        }

        RecursiveAI.OneHtml.Document doc = Doc.Value;
        for (int i = 0; i < snapshot.Documents.Count && i < 3; i++)
        {
            DomSnapshotMapResult r = DomSnapshotAdapter.Map(snapshot, doc, new DomSnapshotMapOptions { DocumentIndex = i, Attributes = (flags & 4) != 0 ? AttributeCheck.None : AttributeCheck.Values });
            if (!r.Succeeded)
            {
                if (r.Refusal is null)
                {
                    throw new InvalidOperationException("refused without a reason");
                }

                continue;
            }

            if (!r.Accounting!.IsComplete)
            {
                throw new InvalidOperationException("accounting incomplete: " + r.Accounting);
            }

            RenderedFacts facts = r.Facts!;
            for (int n = 0; n < doc.NodeCount; n++)
            {
                NodeFacts nf = facts[doc.GetNode(n)];
                _ = nf.GeneratedBoxes.Count + nf.StackingContextNodeIndex + nf.BackendNodeId + (nf.InputValue?.Length ?? 0);
                if (nf.HasLayout)
                {
                    BoxFacts b = nf.Layout;
                    _ = b.BorderBox.Right + (b.PaintOrder ?? 0) + b.TextBoxes.Length + (b.LayoutText?.Length ?? 0) + (b.GetStyle("display")?.Length ?? 0);
                }
            }

            if ((flags & 2) != 0)
            {
                var one = new BackgroundSamples([new BackgroundSampleEntry(1, null, new BackgroundSample(new Rgba(1, 2, 3, 1), new Rgba(0, 0, 0, 1), new Rgba(9, 9, 9, 1), 4))]);
                _ = facts.WithBackgroundSamples(one).Applied;
            }
        }
    }

    /// <summary>Writes seed inputs: fake snapshots of the fixed document with and without generated content and shadow trees.</summary>
    public static void WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        RecursiveAI.OneHtml.Document doc = Doc.Value;
        for (int variant = 0; variant < 4; variant++)
        {
            FakeNode root = FakeSnapshot.FromDocument(doc, n => n.Kind == RecursiveAI.OneHtml.NodeKind.Element ? new FakeLayout { X = 1, Y = 2, W = 30, H = 10, Stacking = n.LocalName == "div", Styles = ["block", "rgb(0, 0, 0)", "visible"] } : null);
            if (variant >= 1)
            {
                FakeSnapshot.Find(root, "P").Kids.Insert(0, new FakeNode { Type = 1, Name = "::before", Pseudo = "before", Layout = new FakeLayout { Text = "x" } });
            }

            if (variant >= 2)
            {
                var shadow = new FakeNode { Type = 11, Name = "#document-fragment", ShadowRoot = "user-agent" };
                shadow.Kids.Add(new FakeNode { Type = 1, Name = "DIV", Layout = new FakeLayout() });
                FakeNode input = FakeSnapshot.Find(root, "INPUT");
                input.Kids.Add(shadow);
                input.InputValue = "x";
                input.InputChecked = true;
            }

            if (variant >= 3)
            {
                FakeSnapshot.Find(root, "IFRAME").ContentDocument = 1;
            }

            byte[] json = variant >= 3 ? FakeSnapshot.ToJson(root, FakeSnapshot.FromDocument(RecursiveAI.OneHtml.Document.Parse("<p>f</p>"))) : FakeSnapshot.ToJson(root);
            File.WriteAllBytes(Path.Combine(dir, $"snapshot-{variant}.bin"), [(byte)variant, .. json]);
        }

        File.WriteAllBytes(Path.Combine(dir, "samples.bin"), [1, .. "{\"format\":\"onehtml-background-samples\",\"version\":1,\"viewport\":{\"width\":1280,\"height\":800},\"samples\":[{\"backendNodeId\":3,\"median\":\"#003366\",\"p5\":\"rgb(0, 30, 80)\",\"p95\":\"#114477\",\"pixels\":10}]}"u8]);
    }
}
