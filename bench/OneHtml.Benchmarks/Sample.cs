using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RecursiveAI.OneHtml.Differential;

namespace RecursiveAI.OneHtml.Benchmarks;

/// <summary>
/// The fixed, deterministic page sample the parse benchmarks run over. Pages are taken in SHA-256 order from the
/// corpus (or loose files) named by <c>ONEHTML_BENCH_CORPUS</c>, so the same corpus always gives the same sample.
/// <c>ONEHTML_BENCH_SAMPLE_SIZE</c> (default 100) is the page count; <c>ONEHTML_BENCH_BAND_CAP</c> (default none)
/// limits how many pages one size band may contribute, so the sample mixes small and large pages. The bands are
/// under 16 KiB, 16 to 128 KiB, 128 to 512 KiB and 512 KiB and up. Nothing from the corpus is stored in the repo.
/// </summary>
internal static class Sample
{
    public const string CorpusVariable = "ONEHTML_BENCH_CORPUS";

    private static readonly Lazy<List<CorpusItem>> s_items = new(Select);

    public static IReadOnlyList<CorpusItem> Items => s_items.Value;

    public static long TotalBytes { get; private set; }

    private static int Band(long bytes) => bytes < 16 * 1024 ? 0 : bytes < 128 * 1024 ? 1 : bytes < 512 * 1024 ? 2 : 3;

    private static List<CorpusItem> Select()
    {
        string? dir = Environment.GetEnvironmentVariable(CorpusVariable);
        if (string.IsNullOrEmpty(dir))
        {
            throw new InvalidOperationException($"set {CorpusVariable} to a corpus version directory (or a directory of HTML files)");
        }

        int size = Env("ONEHTML_BENCH_SAMPLE_SIZE", 100);
        int cap = Env("ONEHTML_BENCH_BAND_CAP", int.MaxValue);
        var picked = new List<CorpusItem>();
        var perBand = new int[4];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (CorpusItem item in Corpus.Load(dir, pages: true, stylesheets: false))
        {
            if (picked.Count >= size)
            {
                break;
            }

            long bytes = item.Bytes >= 0 ? item.Bytes : new FileInfo(item.Path).Length;
            int band = Band(bytes);
            if (perBand[band] >= cap || !seen.Add(item.Sha256))
            {
                continue;
            }

            perBand[band]++;
            picked.Add(item);
            total += bytes;
        }

        if (picked.Count == 0)
        {
            throw new InvalidOperationException("the corpus has no pages to sample");
        }

        TotalBytes = total;
        return picked;
    }

    /// <summary>Reads the sampled bodies. Done once per process, outside the measured code.</summary>
    public static byte[][] LoadBodies()
    {
        var bodies = new byte[Items.Count][];
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i] = File.ReadAllBytes(Items[i].Path);
        }

        return bodies;
    }

    public static string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Items.Count} pages, {TotalBytes} bytes, first sha256 {Items[0].Sha256[..12]}, last {Items[^1].Sha256[..12]}");

    private static int Env(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : fallback;
}
