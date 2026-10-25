using System;
using System.IO;
using SharpFuzz;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// Fuzz harness. Usage: <c>OneHtml.Fuzz &lt;target&gt; [file]</c>. With a file argument the target runs once on
/// that input (to replay a crash); without one it runs under SharpFuzz reading inputs from stdin.
/// Add a target by adding a line to <see cref="Targets.All"/>.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1 || !Targets.All.TryGetValue(args[0], out Action<ReadOnlySpan<byte>>? target))
        {
            Console.Error.WriteLine("usage: OneHtml.Fuzz <target> [file]");
            Console.Error.WriteLine("targets: " + string.Join(", ", Targets.All.Keys));
            return 2;
        }

        if (args.Length > 1)
        {
            target(File.ReadAllBytes(args[1]));
            return 0;
        }

        Fuzzer.OutOfProcess.Run(stream =>
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            target(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        });
        return 0;
    }
}
