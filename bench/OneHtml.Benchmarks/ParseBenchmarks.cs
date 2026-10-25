using System;
using System.Globalization;
using System.IO;
using AngleSharp.Html.Parser;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using HtmlAgilityPack;

namespace RecursiveAI.OneHtml.Benchmarks;

/// <summary>Adds MB/s and allocated bytes per document, both computed from the sweep over the whole sample.</summary>
internal sealed class ParseConfig : ManualConfig
{
    public ParseConfig()
    {
        AddColumn(new SampleColumn(throughput: true), new SampleColumn(throughput: false));
    }

    private sealed class SampleColumn : IColumn
    {
        private readonly bool _throughput;

        public SampleColumn(bool throughput) => _throughput = throughput;

        public string Id => _throughput ? "SampleMBps" : "SampleAllocPerDoc";

        public string ColumnName => _throughput ? "MB/s" : "Alloc/doc";

        public bool AlwaysShow => true;

        public ColumnCategory Category => ColumnCategory.Custom;

        public int PriorityInCategory => _throughput ? 0 : 1;

        public bool IsNumeric => true;

        public UnitType UnitType => UnitType.Dimensionless;

        public string Legend => _throughput ? "Sample bytes divided by mean time of one sweep" : "Allocated bytes per sweep divided by pages in the sample";

        public bool IsAvailable(Summary summary) => true;

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            BenchmarkReport? report = summary[benchmarkCase];
            if (report?.ResultStatistics is null)
            {
                return "n/a";
            }

            if (_throughput)
            {
                double seconds = report.ResultStatistics.Mean / 1e9;
                return (Sample.TotalBytes / 1e6 / seconds).ToString("0.0", CultureInfo.InvariantCulture);
            }

            long? perSweep = report.GcStats.GetBytesAllocatedPerOperation(benchmarkCase);
            return perSweep is null ? "n/a" : (perSweep.Value / (double)Sample.Items.Count).ToString("0", CultureInfo.InvariantCulture);
        }

        public override string ToString() => ColumnName;
    }
}

/// <summary>
/// Parse from bytes, end to end, over the fixed sample. Each benchmark invocation parses the whole sample once, so
/// per-page figures are the sweep figures divided by the page count (see the extra columns).
/// </summary>
[MemoryDiagnoser]
[Config(typeof(ParseConfig))]
public class ParseBenchmarks
{
    private byte[][] _bodies = [];

    /// <summary>Reads the sample from the corpus named by the environment.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _bodies = Sample.LoadBodies();
        Console.WriteLine("sample: " + Sample.Describe());
    }

    /// <summary>OneHtml: <see cref="Document.Parse(ReadOnlyMemory{byte}, ParseOptions?, System.Threading.CancellationToken)"/> on the bytes.</summary>
    [Benchmark(Baseline = true)]
    public long OneHtml()
    {
        long n = 0;
        foreach (byte[] body in _bodies)
        {
            n += Document.Parse(body).NodeCount;
        }

        return n;
    }

    /// <summary>OneHtml parse plus a walk reading every node's name and attributes: the read-path check.</summary>
    [Benchmark]
    public long OneHtmlParseAndWalk()
    {
        long n = 0;
        foreach (byte[] body in _bodies)
        {
            n += Walk(Document.Parse(body));
        }

        return n;
    }

    /// <summary>AngleSharp's <c>HtmlParser</c> on a stream over the bytes.</summary>
    [Benchmark]
    public long AngleSharp()
    {
        var parser = new HtmlParser();
        long n = 0;
        foreach (byte[] body in _bodies)
        {
            using var stream = new MemoryStream(body, writable: false);
            n += parser.ParseDocument(stream).All.Length;
        }

        return n;
    }

    /// <summary>HtmlAgilityPack's <c>HtmlDocument.Load</c> on a stream over the bytes.</summary>
    [Benchmark]
    public long HtmlAgilityPack()
    {
        long n = 0;
        foreach (byte[] body in _bodies)
        {
            using var stream = new MemoryStream(body, writable: false);
            var doc = new HtmlDocument();
            doc.Load(stream);
            n += doc.DocumentNode.ChildNodes.Count;
        }

        return n;
    }

    /// <summary>Visits every node in document order without recursion, summing name, attribute and text lengths.</summary>
    internal static long Walk(Document doc)
    {
        long sum = 0;
        Node n = doc.Root;
        while (!n.IsNull)
        {
            switch (n.Kind)
            {
                case NodeKind.Element:
                    sum += n.LocalName.Length;
                    foreach (Attr a in n.Attributes)
                    {
                        sum += a.LocalName.Length + a.Value.Length;
                    }

                    break;
                case NodeKind.Text or NodeKind.Comment:
                    sum += n.Data.Length;
                    break;
            }

            if (n.HasChildNodes)
            {
                n = n.FirstChild;
                continue;
            }

            while (!n.IsNull && n.NextSibling.IsNull)
            {
                n = n.Parent;
            }

            n = n.IsNull ? n : n.NextSibling;
        }

        return sum;
    }
}

/// <summary>Runs the benchmarks; pass BenchmarkDotNet arguments such as <c>--filter</c> and <c>--job dry</c>.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Sample.CorpusVariable)))
        {
            Console.Error.WriteLine($"set {Sample.CorpusVariable} to a corpus version directory (or a directory of HTML files)");
            return 2;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
