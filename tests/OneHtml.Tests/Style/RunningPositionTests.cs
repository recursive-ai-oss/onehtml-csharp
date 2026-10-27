using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// <c>position: running(&lt;custom-ident&gt;)</c> (CSS GCPM 3 section 1). Chrome for Testing 156.0.8078.47 does not support it and drops every form (its
/// <c>getComputedStyle</c> still says the earlier <c>position</c>), so Chrome is the reference only for the forms that are invalid in the specification
/// too: there the earlier declaration must stand. A valid form is kept as a declaration, typed as <see cref="PositionValue.Running"/>, and wins the cascade.
/// </summary>
public class RunningPositionTests
{
    // The declaration after "position: relative;", and whether the specification accepts it.
    [Theory]
    [InlineData("position: running()", false)]
    [InlineData("position: running(a b)", false)]
    [InlineData("position: running(initial)", false)]
    [InlineData("position: running(inherit)", false)]
    [InlineData("position: running(default)", false)]
    [InlineData("position: running(\"x\")", false)]
    [InlineData("position: running(1x)", false)]
    [InlineData("position: running(x, y)", false)]
    [InlineData("position: running", false)]
    [InlineData("position: running(x) y", false)]
    [InlineData("position: running(header)", true)]
    [InlineData("position: running( header )", true)]
    [InlineData("position: running(Header)", true)]
    [InlineData("position: RUNNING(header)", true)]
    [InlineData("position: running(--x)", true)]
    [InlineData("position: running(none)", true)]
    [InlineData("position: running(header) !important", true)]
    public void Valid_forms_are_kept_and_invalid_ones_leave_the_earlier_position(string declaration, bool valid)
    {
        Document doc = Document.Parse("<p style='position: relative; " + declaration + "'>x</p>");
        ComputedStyle s = DocumentStyles.Create(doc).GetStyle(doc.Root.QuerySelector("p"));
        if (valid)
        {
            Assert.Equal(PositionValue.Running, s.Position);
            Assert.NotNull(s.RunningName);
        }
        else
        {
            Assert.Equal(PositionValue.Relative, s.Position);
            Assert.Null(s.RunningName);
            Assert.Equal("relative", s.GetPropertyValue("position"));
        }
    }

    [Fact]
    public void The_name_is_case_sensitive_and_the_text_is_the_declared_one()
    {
        Document doc = Document.Parse("<style>.a{position:running(Header)} .b{position:RUNNING(header)}</style><p class=a>x</p><p class=b>y</p>");
        DocumentStyles ds = DocumentStyles.Create(doc);
        ComputedStyle a = ds.GetStyle(doc.Root.QuerySelector(".a"));
        ComputedStyle b = ds.GetStyle(doc.Root.QuerySelector(".b"));
        Assert.Equal("Header", a.RunningName);
        Assert.Equal("header", b.RunningName);
        Assert.Equal("running(Header)", a.GetPropertyValue("position"));
        Assert.Equal("running(header)", b.GetComputedValue("position"));
        Assert.Equal("running(Header)", a.GetComputedValue("position"));
    }

    [Fact]
    public void The_keywords_still_work_and_the_cascade_orders_running_like_any_position()
    {
        Document doc = Document.Parse("<style>p{position:running(h)} p.f{position:fixed} #s{position:sticky} #x{position:static}</style><p class=f>a</p><p id=s>b</p><p id=x>c</p><p>d</p>");
        DocumentStyles ds = DocumentStyles.Create(doc);
        Assert.Equal(PositionValue.Fixed, ds.GetStyle(doc.Root.QuerySelector(".f")!).Position);
        Assert.Equal(PositionValue.Sticky, ds.GetStyle(doc.Root.QuerySelector("#s")!).Position);
        Assert.Equal(PositionValue.Static, ds.GetStyle(doc.Root.QuerySelector("#x")!).Position);
        Assert.Equal(PositionValue.Running, ds.GetStyle(doc.Root.QuerySelectorAll("p")[^1]).Position);
    }

    [Fact]
    public void A_running_element_is_not_blockified_and_is_not_an_absolute_box()
    {
        Document doc = Document.Parse("<style>span{position:running(h)}</style><span>x</span>");
        ComputedStyle s = DocumentStyles.Create(doc).GetStyle(doc.Root.QuerySelector("span"));
        Assert.Equal("inline", s.Display.ToCss());
    }

    [Theory]
    [InlineData("running(x)", true)]
    [InlineData("running()", false)]
    [InlineData("sticky", true)]
    [InlineData("bogus", false)]
    public void Typed_parsing_accepts_the_same_forms(string value, bool valid)
    {
        Assert.Equal(valid, TypedProperties.IsValid("position", CssTree.ParseComponentValueList(value).Values));
        Assert.Equal(valid, TypedProperties.TrySerialize("position", CssTree.ParseComponentValueList(value).Values, out string css) && css.Length > 0);
    }
}
