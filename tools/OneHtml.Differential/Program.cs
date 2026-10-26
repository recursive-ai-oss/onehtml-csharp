using System;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// Differential and corpus harness. <c>roundtrip</c> checks lossless output and robustness over a corpus;
/// <c>angle</c> compares our trees with AngleSharp's (used in-process, as a black box); <c>ax</c> compares our accessibility
/// trees with Chrome's from captured output.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Options? o = args.Length >= 2 ? Options.Parse(args) : null;
        if (o is null || args[0] is not ("roundtrip" or "angle" or "replay" or "ax" or "show" or "locators" or "styles" or "editfuzz" or "axe"))
        {
            Console.Error.WriteLine("usage: OneHtml.Differential <roundtrip|angle|replay|locators|styles|editfuzz|ax|axe|show> <corpus-or-capture-dir-or-file> [options]");
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        int code = args[0] switch { "roundtrip" => RoundTrip.Run(o), "angle" => AngleDiff.Run(o), "ax" => AxCommand.Run(o), "show" => Show.Run(o), "locators" => LocatorsCommand.Run(o), "styles" => StylesCommand.Run(o), "editfuzz" => EditFuzzCommand.Run(o), "axe" => AxeCommand.Run(o), _ => ReplayCommand.Run(o) };
        // Abandoned hung parses may still hold threads; leave regardless.
        Environment.Exit(code);
        return code;
    }
}
