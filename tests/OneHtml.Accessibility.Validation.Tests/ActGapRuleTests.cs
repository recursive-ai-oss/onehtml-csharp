using System.Linq;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

public sealed class ActGapRuleTests
{
    private static string[] Outcomes(string html, string rule) =>
        new Checker().Check(Document.Parse(html), new CheckOptions { RuleIds = [rule] }).Results.Select(r => r.Outcome.ToString()).ToArray();

    [Fact]
    public void Focus_indicator_removed_by_the_page_fails_every_element_without_a_replacement()
    {
        const string css = "<style>a:focus-visible,button:focus-visible{outline:none}</style>";
        Assert.Equal(["Failed", "Failed"], Outcomes(css + "<a href=x>a</a><button>b</button>", "focus-visible-indicator"));
    }

    [Fact]
    public void Focus_indicator_given_by_another_style_passes()
    {
        const string css = "<style>a:focus-visible,button:focus-visible{outline:none}a:focus-visible{background:#ff0}button:focus-visible{border:2px solid red}</style>";
        Assert.Equal(["Passed", "Passed"], Outcomes(css + "<a href=x>a</a><button>b</button>", "focus-visible-indicator"));
    }

    [Fact]
    public void Focus_indicator_shown_on_a_sibling_counts()
    {
        const string css = "<style>a:focus-visible{outline:none}a:focus-visible + span{background:#000;color:#fff}</style>";
        Assert.Equal("Passed", Outcomes(css + "<a href=x>a</a><span>mark</span><button>b</button>", "focus-visible-indicator")[0]);
    }

    [Fact]
    public void A_single_focusable_element_is_not_applicable()
    {
        Assert.Equal(["Inapplicable"], Outcomes("<style>a:focus-visible{outline:none}</style><a href=x>a</a>", "focus-visible-indicator"));
    }

    [Fact]
    public void A_focus_handler_makes_a_missing_indicator_a_review_item()
    {
        const string css = "<style>a:focus-visible,button:focus-visible{outline:none}</style>";
        Assert.Equal(["NeedsReview", "Failed"], Outcomes(css + "<a href=x onfocus='show()'>a</a><button>b</button>", "focus-visible-indicator"));
    }

    [Fact]
    public void Links_with_one_name_in_different_blocks_are_not_compared()
    {
        Assert.Equal(["Inapplicable"], Outcomes("<p><a href=a>More</a></p><p><a href=b>More</a></p>", "identical-links-same-context"));
        Assert.Equal(["NeedsReview", "NeedsReview"], Outcomes("<p><a href=a>More</a> <a href=b>More</a></p>", "identical-links-same-context"));
        Assert.Equal(["Passed", "Passed"], Outcomes("<p><a href=a>More</a> <a href=a>More</a></p>", "identical-links-same-context"));
    }

    [Fact]
    public void Pictures_inside_an_author_named_control_are_not_decorative_candidates()
    {
        Assert.Equal(["Inapplicable"], Outcomes("<a href=x aria-label='Home'><img src=i.png alt=''></a>", "image-decorative"));
        Assert.Equal(["NeedsReview"], Outcomes("<img src=i.png alt=''>", "image-decorative"));
    }
}
