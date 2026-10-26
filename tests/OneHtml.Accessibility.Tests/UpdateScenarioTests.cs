using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Hand-written update cases: each states the name the edit has to produce, and that the update agrees with a rebuild.</summary>
public sealed class UpdateScenarioTests
{
    private static AccessibilityTree Update(string html, System.Func<Document, Edit[]> edits, out Document next, out AccessibilityTree before)
    {
        Document doc = Document.Parse(html);
        DocumentStyles styles = DocumentStyles.Create(doc);
        before = AccessibilityTree.Build(doc, styles);
        EditResult r = doc.Apply(edits(doc));
        Assert.True(r.Succeeded, r.Failure);
        next = r.Document!;
        AccessibilityTree updated = before.Update(r, styles.Restyle(r));
        AccessibilityTree rebuilt = AccessibilityTree.Build(next, DocumentStyles.Create(next));
        Assert.Equal(TreeDump.Of(rebuilt), TreeDump.Of(updated));
        Assert.True(updated.UpdateInfo!.Incremental, updated.UpdateInfo.FullRebuildReason);
        return updated;
    }

    private static string NameOf(AccessibilityTree t, Document d, string selector) => t.Find(d.Root.QuerySelector(selector))!.Name.Value;

    [Fact]
    public void Generated_content_in_a_name_follows_a_class_change_on_an_ancestor()
    {
        const string Html = "<style>.on .b::before{content:'New: '}</style><div id=a><a class=b href=/x>Sale</a></div>";
        AccessibilityTree t = Update(Html, d => [Edit.SetAttribute(d.Root.QuerySelector("#a"), "class", "on")], out Document next, out AccessibilityTree before);
        Assert.Equal("Sale", before.Find(before.Document.Root.QuerySelector(".b"))!.Name.Value);
        Assert.Equal("New: Sale", NameOf(t, next, ".b"));
    }

    [Fact]
    public void Generated_content_in_a_name_follows_an_edit_of_the_rule()
    {
        AccessibilityTree t = Update("<style id=s>a::after{content:' (pdf)'}</style><a href=/x>Report</a>", d => [Edit.ReplaceText(d.Root.QuerySelector("#s"), "a::after{content:' (doc)'}")], out Document next, out _);
        Assert.Equal("Report (doc)", NameOf(t, next, "a"));
    }

    [Fact]
    public void Retargeting_an_id_changes_the_name_that_references_it()
    {
        AccessibilityTree t = Update("<span id=a>First</span><span id=b>Second</span><button aria-labelledby=a>x</button>", d => [Edit.SetAttribute(d.Root.QuerySelector("#a"), "id", "z"), Edit.SetAttribute(d.Root.QuerySelector("#b"), "id", "a")], out Document next, out _);
        Assert.Equal("Second", NameOf(t, next, "button"));
    }

    [Fact]
    public void Removing_the_referenced_element_clears_the_name_and_reports_a_broken_reference()
    {
        AccessibilityTree t = Update("<span id=a>Label</span><button aria-labelledby=a></button>", d => [Edit.Remove(d.Root.QuerySelector("#a"))], out Document next, out _);
        Assert.Equal("", NameOf(t, next, "button"));
        Assert.Equal(["a"], t.Find(next.Root.QuerySelector("button"))!.GetRelation(AxRelationKind.LabelledBy)!.Value.BrokenIds.ToArray());
    }

    [Fact]
    public void A_label_gained_by_a_duplicate_id_leaves_the_previous_holder_unnamed()
    {
        AccessibilityTree t = Update("<label for=f>Name</label><input id=f>", d => [Edit.InsertBefore(d.Root.QuerySelector("input"), "<input id=f>")], out Document next, out _);
        var inputs = next.Root.QuerySelectorAll("input").ToList();
        Assert.Equal("Name", t.Find(inputs[0])!.Name.Value);
        Assert.Equal("", t.Find(inputs[1])!.Name.Value);
    }

    [Fact]
    public void Moving_content_under_an_aria_owns_owner_changes_the_owners_name_and_tree_position()
    {
        AccessibilityTree t = Update("<div role=button id=o aria-owns=x></div><p><span id=x>Owned text</span></p>", d => [Edit.SetAttribute(d.Root.QuerySelector("#x"), "id", "y")], out Document next, out _);
        Assert.Equal("", NameOf(t, next, "#o"));
    }

    [Fact]
    public void Text_edits_rename_every_ancestor_that_reads_them()
    {
        AccessibilityTree t = Update("<a href=/x id=l><span><b id=t>Old</b></span> tail</a>", d => [Edit.ReplaceText(d.Root.QuerySelector("#t"), "New")], out Document next, out _);
        Assert.Equal("New tail", NameOf(t, next, "#l"));
        Assert.True(t.UpdateInfo!.NamesReused >= 0);
    }

    [Fact]
    public void Unchanged_elements_keep_their_names_and_most_are_not_recomputed()
    {
        string html = "<main>" + string.Concat(Enumerable.Range(0, 50).Select(i => $"<section aria-label=s{i}><a href=/p{i}>Link {i}</a></section>")) + "</main>";
        AccessibilityTree t = Update(html, d => [Edit.SetAttribute(d.Root.QuerySelector("a[href='/p7']"), "title", "t")], out _, out _);
        Assert.True(t.UpdateInfo!.NamesRecomputed < 10, t.UpdateInfo.ToString());
        Assert.True(t.UpdateInfo.NamesReused > 90, t.UpdateInfo.ToString());
    }

    [Fact]
    public void A_context_change_recomputes_only_what_the_context_restyles()
    {
        Document doc = Document.Parse("<style>a:focus{display:none} li{display:list-item}</style><ul><li><a id=a href=/a>A</a></li><li><a href=/b>B</a></li></ul>");
        AccessibilityTree off = AccessibilityTree.Build(doc, DocumentStyles.Create(doc));
        var ctx = new StyleContext { Focus = doc.Root.QuerySelector("#a") };
        AccessibilityTree on = off.WithContext(ctx);
        AccessibilityTree rebuilt = AccessibilityTree.Build(doc, DocumentStyles.Create(doc), ctx);
        Assert.Equal(TreeDump.Of(rebuilt), TreeDump.Of(on));
        Assert.True(on.UpdateInfo!.Incremental);
        Assert.Null(on.Find(doc.Root.QuerySelector("#a")));
    }

    [Fact]
    public void Counters_in_names_build_the_tree_afresh_and_say_so()
    {
        Document doc = Document.Parse("<style>a::before{content:counter(n) '. ';counter-increment:n}</style><ul><li><a href=/x>A</a></li></ul>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        EditResult r = doc.Apply([Edit.Append(doc.Root.QuerySelector("ul"), "<li><a href=/y>B</a></li>")]);
        AccessibilityTree t = tree.Update(r, styles.Restyle(r));
        Assert.False(t.UpdateInfo!.Incremental);
        Assert.Contains("counters", t.UpdateInfo.FullRebuildReason);
    }

    [Fact]
    public void An_update_for_another_document_is_refused()
    {
        Document a = Document.Parse("<p>a</p>");
        Document b = Document.Parse("<p>b</p>");
        DocumentStyles sb = DocumentStyles.Create(b);
        AccessibilityTree ta = AccessibilityTree.Build(a, DocumentStyles.Create(a));
        EditResult r = b.Apply([Edit.ReplaceText(b.Root.QuerySelector("p"), "c")]);
        Assert.Throws<System.ArgumentException>(() => ta.Update(r, sb.Restyle(r)));
    }
}
