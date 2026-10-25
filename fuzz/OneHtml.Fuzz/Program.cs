using System;
using System.IO;
using SharpFuzz;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// Fuzz harness. Usage: <c>OneHtml.Fuzz &lt;target&gt; [file]</c>. With a file argument the target runs once on
/// that input (to replay a crash); without one it runs under SharpFuzz reading inputs from stdin.
/// <c>OneHtml.Fuzz &lt;target&gt; --mutate &lt;seconds&gt; &lt;seed-dir&gt;</c> is a coverage-blind mutation loop for
/// machines without a libFuzzer driver: it mutates the seed files at random and stops at the first exception,
/// saving the input as <c>crash-*.bin</c> in the working directory.
/// Add a target by adding a line to <see cref="Targets.All"/>.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Under a coverage-guided driver the instrumented library must not run before SharpFuzz has attached its
        // trace buffer, so nothing may touch Targets (whose initializers call into the library) until the first input.
        if (args.Length == 1 && args[0] != "--list")
        {
            string name = args[0];
            Fuzzer.LibFuzzer.Run(span => Targets.All[name](span));
            return 0;
        }

        if (args.Length < 2 || !Targets.All.TryGetValue(args[0], out Action<ReadOnlySpan<byte>>? target))
        {
            Console.Error.WriteLine("usage: OneHtml.Fuzz <target> <file> | <target> --mutate <seconds> <seed-dir> | <target> (libFuzzer driver mode)");
            Console.Error.WriteLine("targets: " + string.Join(", ", Targets.All.Keys));
            return 2;
        }

        if (args.Length == 4 && args[1] == "--mutate")
        {
            return Mutate(target, int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3]);
        }

        target(File.ReadAllBytes(args[1]));
        return 0;
    }

    private static int Mutate(Action<ReadOnlySpan<byte>> target, int seconds, string seedDir)
    {
        var seeds = new System.Collections.Generic.List<byte[]>();
        foreach (string f in Directory.EnumerateFiles(seedDir, "*", SearchOption.AllDirectories))
        {
            byte[] b = File.ReadAllBytes(f);
            if (b.Length <= 100_000)
            {
                seeds.Add(b);
            }
        }

        if (seeds.Count == 0)
        {
            Console.Error.WriteLine("no seeds");
            return 2;
        }

        var rng = new Random(12345);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long runs = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            byte[] input = (byte[])seeds[rng.Next(seeds.Count)].Clone();
            int edits = 1 + rng.Next(8);
            for (int e = 0; e < edits; e++)
            {
                switch (rng.Next(4))
                {
                    case 0 when input.Length > 0:
                        input[rng.Next(input.Length)] = (byte)rng.Next(256);
                        break;
                    case 1 when input.Length > 0:
                        input[rng.Next(input.Length)] ^= (byte)(1 << rng.Next(8));
                        break;
                    case 2:
                        {
                            byte[] ins = new byte[1 + rng.Next(6)];
                            string pool = "<>&;/!-?=\"' \n\r\0[]xA#";
                            for (int i = 0; i < ins.Length; i++)
                            {
                                ins[i] = rng.Next(3) == 0 ? (byte)rng.Next(256) : (byte)pool[rng.Next(pool.Length)];
                            }

                            int at = rng.Next(input.Length + 1);
                            input = [.. input[..at], .. ins, .. input[at..]];
                            break;
                        }

                    default:
                        if (input.Length > 2)
                        {
                            int at = rng.Next(input.Length - 1);
                            input = [.. input[..at], .. input[(at + 1 + rng.Next(Math.Min(8, input.Length - at - 1)))..]];
                        }

                        break;
                }
            }

            try
            {
                target(input);
            }
            catch (Exception ex)
            {
                string name = $"crash-{runs}.bin";
                File.WriteAllBytes(name, input);
                Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}\nsaved {name}");
                return 1;
            }

            runs++;
        }

        Console.WriteLine($"{runs} executions in {seconds}s, no crash");
        return 0;
    }
}
