using System;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Benchmarks;

/// <summary>
/// Cost of one edit on a generated page of roughly 35,000 nodes (no corpus needed): a single attribute change, a single
/// insertion, a 100-edit transaction, and a plain parse of the same bytes for scale. An edit re-parses the output to
/// check it, so the single-edit rows are expected to sit close to one parse.
/// </summary>
[MemoryDiagnoser]
public class EditBenchmarks
{
    private byte[] _bytes = [];
    private Document _doc = null!;
    private Node _target;
    private Node _list;
    private Edit[] _many = [];
    private Document _edited = null!;
    private Document _fresh = null!;

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder("<!DOCTYPE html>\n<html><head><title>Bench</title></head><body>\n<ul id=\"list\">\n");
        for (int i = 0; i < 5000; i++)
        {
            sb.Append("  <li class=\"item\"><a href=\"/p/").Append(i).Append("\">Entry ").Append(i).Append("</a> <span>&amp; more</span></li>\n");
        }

        sb.Append("</ul>\n</body></html>\n");
        _bytes = Encoding.UTF8.GetBytes(sb.ToString());
        _doc = Document.Parse(_bytes);
        _list = _doc.GetNode(0);
        _target = _doc.GetNode(0);
        for (int i = 0; i < _doc.NodeCount; i++)
        {
            Node n = _doc.GetNode(i);
            if (n.IsElement && n.LocalName == "ul")
            {
                _list = n;
            }
        }

        int seen = 0;
        var many = new List<Edit>();
        for (Node li = _list.FirstChild; !li.IsNull; li = li.NextSibling)
        {
            if (li.IsElement && ++seen == 2500)
            {
                _target = li;
            }

            if (li.IsElement && seen % 50 == 0 && many.Count < 100)
            {
                many.Add(Edit.SetAttribute(li, "class", "changed"));
            }
        }

        _many = [.. many];
        _fresh = Document.Parse(_bytes);
        _edited = _doc.Apply([Edit.SetAttribute(_target, "class", "other")]).Document!;
        Console.WriteLine($"edit benchmark page: {_bytes.Length} bytes, {_doc.NodeCount} nodes, {_many.Length} edits in the large transaction");
    }

    /// <summary>The nodes in the generated page.</summary>
    [Benchmark(Baseline = true)]
    public Document Parse() => Document.Parse(_bytes);

    /// <summary>One attribute value replaced.</summary>
    [Benchmark]
    public EditResult SetAttribute() => _doc.Apply([Edit.SetAttribute(_target, "class", "other")]);

    /// <summary>One element inserted, indented like its neighbours.</summary>
    [Benchmark]
    public EditResult InsertAfter() => _doc.Apply([Edit.InsertAfter(_target, "<li>new</li>")]);

    /// <summary>One hundred attribute edits in one transaction.</summary>
    [Benchmark]
    public EditResult HundredEdits() => _doc.Apply(_many);

    /// <summary>Reads every node name and attribute of a fresh parse (the read-path reference).</summary>
    [Benchmark]
    public long ReadFresh() => ReadAll(_fresh);

    /// <summary>The same read over the old snapshot after it was edited: should equal ReadFresh.</summary>
    [Benchmark]
    public long ReadOldAfterEdit() => ReadAll(_doc);

    /// <summary>The same read over the snapshot an edit returned.</summary>
    [Benchmark]
    public long ReadEdited() => ReadAll(_edited);

    private static long ReadAll(Document doc)
    {
        long sum = 0;
        for (int i = 0; i < doc.NodeCount; i++)
        {
            Node n = doc.GetNode(i);
            if (n.IsElement)
            {
                sum += n.LocalName.Length + (n.GetAttribute("class")?.Length ?? 0) + (n.FirstChild.IsNull ? 0 : 1);
            }
        }

        return sum;
    }
}
