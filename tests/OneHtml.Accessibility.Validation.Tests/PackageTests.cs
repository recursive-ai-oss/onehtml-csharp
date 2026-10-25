using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

public class PackageTests
{
    [Fact]
    public void Package_name_is_set()
    {
        Assert.Equal("RecursiveAI.OneHtml.Accessibility.Validation", PackageInfo.Name);
    }
}
