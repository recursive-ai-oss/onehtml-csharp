using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Tests.Html5lib;

/// <summary>
/// Tree construction cases that are not run as written, keyed by "file|index" with the reason. Nothing else is skipped.
/// </summary>
internal static class TreeConstructionKnownDifferences
{
    private const string NeedsScript =
        "the expected tree is the result of running the script in the test; the library never runs script, "
        + "so the tree the parsing algorithm gives is the one without the script's effect";

    public static readonly IReadOnlyDictionary<string, string> Cases = new Dictionary<string, string>
    {
        ["scripted_adoption01.dat|0"] = NeedsScript,
        ["scripted_ark.dat|0"] = NeedsScript,
        ["scripted_foster01.dat|0"] = NeedsScript,
        ["scripted_foster01.dat|1"] = NeedsScript,
        ["scripted_webkit01.dat|0"] = NeedsScript,
        ["scripted_webkit01.dat|1"] = NeedsScript,
    };
}
