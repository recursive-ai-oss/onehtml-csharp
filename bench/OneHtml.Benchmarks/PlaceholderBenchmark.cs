using BenchmarkDotNet.Attributes;

namespace RecursiveAI.OneHtml.Benchmarks;

/// <summary>Placeholder that keeps the benchmark project wired up until real benchmarks land.</summary>
[MemoryDiagnoser]
public class PlaceholderBenchmark
{
    private readonly string _name = PackageInfo.Name;

    /// <summary>Reads the package name.</summary>
    [Benchmark]
    public int NameLength() => _name.Length;
}
