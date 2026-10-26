using System;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// Differential and corpus harness. <c>roundtrip</c> checks lossless output and robustness over a corpus;
/// <c>angle</c> compares our trees with AngleSharp's (used in-process, as a black box). Chrome and axe-core
/// comparisons are added as further commands.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Options? o = args.Length >= 2 ? Options.Parse(args) : null;
        if (o is null || args[0] is not ("roundtrip" or "angle" or "replay"))
        {
            Console.Error.WriteLine("usage: OneHtml.Differential <roundtrip|angle|replay> <corpus-dir> [options]");
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        int code = args[0] switch { "roundtrip" => RoundTrip.Run(o), "angle" => AngleDiff.Run(o), _ => ReplayCommand.Run(o) };
        // Abandoned hung parses may still hold threads; leave regardless.
        Environment.Exit(code);
        return code;
    }
}
