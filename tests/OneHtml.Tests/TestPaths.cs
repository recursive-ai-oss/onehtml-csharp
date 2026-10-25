using System.IO;
using System.Runtime.CompilerServices;

namespace RecursiveAI.OneHtml.Tests;

/// <summary>Finds the repository's <c>testdata</c> folder from the location of this source file.</summary>
internal static class TestPaths
{
    public static string TestData { get; } = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "..", "testdata"));

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
