using Xunit;

namespace RecursiveAI.OneHtml.Tests;

public class PackageTests
{
    [Fact]
    public void Package_name_is_set()
    {
        Assert.Equal("RecursiveAI.OneHtml", PackageInfo.Name);
    }
}
