using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Locators;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Editing;

/// <summary>
/// Replay outcomes at the edges of the fingerprint (edit-format.md, "fingerprint"): which attribute changes the
/// fingerprint sees, and which a saved edit is allowed to ignore.
/// </summary>
public class ReplayFingerprintTests
{
    private static ReplayOutcome ReplayOn(string original, string mutated, string tag)
    {
        Document doc = Document.Parse(original);
        EditEnvelope env = EditJson.Parse(EditJson.Create(doc, [Edit.SetAttribute(EditTestHelpers.Find(doc, tag), "data-x", "1")]).ToJson());
        return env.Replay(Document.Parse(mutated)).Outcomes[0];
    }

    [Fact]
    public void A_changed_plain_class_makes_the_edit_stale()
    {
        Assert.Equal(EditStatus.Stale, ReplayOn("<body><p class=\"intro\">a</p>", "<body><p class=\"zz-intro\">a</p>", "p").Status);
    }

    [Fact]
    public void A_changed_hash_like_class_is_not_part_of_the_fingerprint()
    {
        // Generated class names change with every build, so the fingerprint leaves them out (edit-format.md).
        Assert.True(GeneratedNames.IsHashLike("N8MGzv"));
        Assert.Equal(EditStatus.Applied, ReplayOn("<body><p class=\"N8MGzv intro\">a</p>", "<body><p class=\"zz-N8MGzv intro\">a</p>", "p").Status);
    }

    [Fact]
    public void A_changed_plain_id_makes_the_edit_stale_and_a_changed_hash_like_id_does_not()
    {
        Assert.Equal(EditStatus.Stale, ReplayOn("<body><p id=\"intro\">a</p>", "<body><p id=\"introx\">a</p>", "p").Status);
        Assert.True(GeneratedNames.IsHashLike("jt9h7ab"));
        Assert.Equal(EditStatus.Applied, ReplayOn("<body><p id=\"jt9h7ab\">a</p>", "<body><p id=\"jt9h7abx\">a</p>", "p").Status);
    }

    [Fact]
    public void Swapping_attributes_that_have_white_space_between_them_still_applies()
    {
        Assert.Equal(EditStatus.Applied, ReplayOn("<body><a href=\"/x\" title=\"t\">a</a>", "<body><a title=\"t\" href=\"/x\">a</a>", "a").Status);
    }

    [Fact]
    public void Swapping_attributes_with_no_white_space_between_them_changes_the_page_not_the_order()
    {
        // `href="/x""b"` has the attribute named `"b"`; swapping the two texts yields a different attribute, so the edit is stale.
        Assert.Equal(EditStatus.Stale, ReplayOn("<body><a href=\"/x\"\"b\">a</a>", "<body><a \"b\"href=\"/x\">a</a>", "a").Status);
    }

    [Fact]
    public void After_a_rule_is_removed_a_same_selector_same_declarations_twin_is_the_target()
    {
        const string before = "<style>.a{color:red}\n.a { color: red }\n.b{top:0}</style>";
        Document doc = Document.Parse(before);
        InlineStylesheet inl = InlineStylesheets.Of(doc).Single();
        EditEnvelope env = EditJson.Parse(EditJson.Create(doc, null, [SheetEdit.SetDeclaration(inl.Sheet, SheetEditTests.Rule(inl.Sheet, 0), "margin", "0")]).ToJson());
        ReplayOutcome o = env.Replay(Document.Parse("<style>.a { color: red }\n.b{top:0}</style>")).Outcomes[0];
        Assert.Equal(EditStatus.Applied, o.Status);
        Assert.Contains("color: red", o.Rule!.Value.Raw.ToString(), System.StringComparison.Ordinal);

        // A rule with other declarations is never taken for it.
        ReplayOutcome other = env.Replay(Document.Parse("<style>.a { color: blue }\n.b{top:0}</style>")).Outcomes[0];
        Assert.Equal(EditStatus.Stale, other.Status);
    }
}
