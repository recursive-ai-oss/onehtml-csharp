using System;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// Differential harness: compares our output with AngleSharp in-process, and with Chrome and axe-core
/// driven as black boxes over the DevTools protocol. Comparisons are added as commands.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("OneHtml.Differential: no commands yet" + (args.Length > 0 ? " (got: " + args[0] + ")" : ""));
        return 0;
    }
}
