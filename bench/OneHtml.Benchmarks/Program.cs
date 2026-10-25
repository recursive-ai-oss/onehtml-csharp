using BenchmarkDotNet.Running;

namespace RecursiveAI.OneHtml.Benchmarks;

/// <summary>Runs every benchmark class in this assembly; pass BenchmarkDotNet arguments such as <c>--filter</c>.</summary>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
