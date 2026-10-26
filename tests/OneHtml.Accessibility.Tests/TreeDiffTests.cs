using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

public sealed class TreeDiffTests
{
    // The export of every node without its tree position, keyed by tree id: what the diff has to account for.
    private static Dictionary<int, string> Exported(AccessibilityTree tree)
    {
        var d = new Dictionary<int, string>();
        using JsonDocument j = JsonDocument.Parse(tree.ToChromeJson());
        foreach (JsonElement n in j.RootElement.GetProperty("nodes").EnumerateArray())
        {
            var o = new Dictionary<string, string>();
            foreach (JsonProperty p in n.EnumerateObject())
            {
                if (p.Name is not ("nodeId" or "parentId" or "childIds" or "backendDOMNodeId"))
                {
                    o[p.Name] = System.Text.RegularExpressions.Regex.Replace(p.Value.GetRawText(), "\"backendDOMNodeId\":\\d+,?", string.Empty);
                }
            }

            d[int.Parse(n.GetProperty("nodeId").GetString()!, System.Globalization.CultureInfo.InvariantCulture)] = string.Join("|", o.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value));
        }

        return d;
    }

    private static void CheckDiff(AccessibilityTree before, AccessibilityTree after, ChangeSet? changes, string where)
    {
        TreeDiff diff = before.Diff(after, changes);
        Dictionary<int, string> eb = Exported(before);
        Dictionary<int, string> ea = Exported(after);
        var changedAfter = diff.Changed.Select(c => c.After).ToHashSet();
        var addedSet = diff.Added.ToHashSet();
        var removedSet = diff.Removed.ToHashSet();

        // Every node is matched, added or removed, once.
        foreach (AxNode a in after.Nodes)
        {
            AxNode? pair = null;
            foreach (AxNode b in before.Nodes)
            {
                if (diff.Counterpart(b) == a)
                {
                    pair = b;
                    break;
                }
            }

            Assert.True((pair is null) == addedSet.Contains(a), where + ": added mismatch at " + a);
            if (pair is not null && eb[pair.Id] != ea[a.Id])
            {
                Assert.True(changedAfter.Contains(a), where + ": an exported difference is not reported at " + a + " dom " + a.DomNode.Index + " pairdom " + pair.DomNode.Index + "\n  " + eb[pair.Id] + "\n  " + ea[a.Id] + "\n relB=" + string.Join(",", pair.Relations.Select(r => r.Kind + ":" + r.Elements.Count + "/" + r.BrokenIds.Count)) + " relA=" + string.Join(",", a.Relations.Select(r => r.Kind + ":" + r.Elements.Count + "/" + r.BrokenIds.Count)));
            }
        }

        foreach (AxNode b in before.Nodes)
        {
            Assert.Equal(diff.Counterpart(b) is null, removedSet.Contains(b));
        }

        // The reported changes, applied to the earlier node, give the later one.
        foreach (AxNodeChange c in diff.Changed)
        {
            Assert.NotEmpty(c.Properties);
            foreach (AxPropertyChange p in c.Properties)
            {
                Assert.NotEqual(p.OldValue, p.NewValue);
                switch (p.Property)
                {
                    case "role":
                        Assert.Equal(c.Before.InternalRole ?? c.Before.Role, p.OldValue);
                        Assert.Equal(c.After.InternalRole ?? c.After.Role, p.NewValue);
                        break;
                    case "name":
                        Assert.Equal(c.Before.Name.Value, p.OldValue);
                        Assert.Equal(c.After.Name.Value, p.NewValue);
                        break;
                    case "description":
                        Assert.Equal(c.Before.Description.Value, p.OldValue);
                        Assert.Equal(c.After.Description.Value, p.NewValue);
                        break;
                    default:
                        break;
                }
            }
        }
    }

    [Fact]
    public void A_tree_diffed_with_itself_is_empty_across_the_corpus()
    {
        foreach ((string name, string html) in IncrementalUpdateTests.Corpus())
        {
            Document doc = Document.Parse(html);
            AccessibilityTree tree = AccessibilityTree.Build(doc, DocumentStyles.Create(doc));
            Assert.True(tree.Diff(tree).IsEmpty, name);
            Assert.True(tree.CreateReadingView().Diff(tree.CreateReadingView()).IsEmpty, name);
        }
    }

    [Fact]
    public void Reported_changes_account_for_every_difference_after_random_edits()
    {
        int page = 0;
        int diffs = 0;
        foreach ((string name, string html) in IncrementalUpdateTests.Corpus())
        {
            var rng = new Random(4242 + page++);
            Document doc = Document.Parse(html);
            DocumentStyles styles = DocumentStyles.Create(doc);
            AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
            for (int step = 0; step < 5; step++)
            {
                if (rng.Next(5) == 0)
                {
                    List<Node> els = doc.Root.DescendantElements.ToList();
                    if (els.Count > 0)
                    {
                        AccessibilityTree ctx = tree.WithContext(new StyleContext { Focus = els[rng.Next(els.Count)], Hover = els[rng.Next(els.Count)] });
                        CheckDiff(tree, ctx, null, name + " context");
                        CheckReadingDiff(tree, ctx, null);
                        diffs++;
                    }

                    continue;
                }

                EditResult result = doc.Apply(RandomEdits.Next(doc, rng, 1 + rng.Next(3)));
                if (!result.Succeeded)
                {
                    continue;
                }

                RestyleResult restyle = styles.Restyle(result);
                AccessibilityTree next = tree.Update(result, restyle);
                CheckDiff(tree, next, result.Changes, name + " step " + step);
                CheckReadingDiff(tree, next, result.Changes);
                diffs++;
                doc = result.Document!;
                styles = restyle.Styles;
                tree = next;
            }
        }

        Assert.True(diffs > 1000);
    }

    private static void CheckReadingDiff(AccessibilityTree before, AccessibilityTree after, ChangeSet? changes)
    {
        ReadingView vb = before.CreateReadingView();
        ReadingView va = after.CreateReadingView();
        ReadingViewDiff d = vb.Diff(va, changes);
        var removed = d.Removed.ToHashSet();
        var changedBefore = d.Changed.Select(c => c.Before).ToHashSet();
        var added = d.Added.ToHashSet();
        var changedAfter = d.Changed.Select(c => c.After).ToHashSet();
        List<string> kept = vb.Items.Where(i => !removed.Contains(i) && !changedBefore.Contains(i)).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        List<string> now = va.Items.Where(i => !added.Contains(i) && !changedAfter.Contains(i)).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(kept, now);
        Assert.Equal(vb.Items.Count - d.Removed.Count - d.Changed.Count, kept.Count);
        Assert.Equal(va.Items.Count - d.Added.Count - d.Changed.Count, now.Count);
    }

    private static string Key(ReadingItem i) => i.Kind + "|" + i.Lang + "|" + i.Text;

    // ---- hand-checked reading view differences ----

    private static (ReadingViewDiff Diff, ReadingView Before, ReadingView After) After(string html, Func<Document, Edit[]> edits)
    {
        Document doc = Document.Parse(html);
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        EditResult r = doc.Apply(edits(doc));
        Assert.True(r.Succeeded, r.Failure);
        AccessibilityTree next = tree.Update(r, styles.Restyle(r));
        ReadingView vb = tree.CreateReadingView();
        ReadingView va = next.CreateReadingView();
        return (vb.Diff(va, r.Changes), vb, va);
    }

    [Fact]
    public void Renaming_a_heading_changes_one_step()
    {
        var (d, _, _) = After("<h2 id=h>Old title</h2><p>Body</p>", doc => [Edit.ReplaceText(doc.Root.QuerySelector("#h"), "New title")]);
        Assert.Empty(d.Added);
        Assert.Empty(d.Removed);
        ReadingItemChange c = Assert.Single(d.Changed);
        Assert.Equal("heading level 2: Old title", c.Before.Text);
        Assert.Equal("heading level 2: New title", c.After.Text);
    }

    [Fact]
    public void Removing_a_list_item_removes_its_steps_and_renumbers_the_others()
    {
        var (d, _, _) = After("<ul><li id=a>One</li><li id=b>Two</li><li id=c>Three</li></ul>", doc => [Edit.Remove(doc.Root.QuerySelector("#b"))]);
        Assert.Equal(["item 2 of 3", "Two"], d.Removed.Select(i => i.Text).ToArray());
        Assert.Empty(d.Added);
        Assert.Equal(
            ["list with 3 items -> list with 2 items", "item 1 of 3 -> item 1 of 2", "item 3 of 3 -> item 2 of 2"],
            d.Changed.Select(c => c.Before.Text + " -> " + c.After.Text).ToArray());
    }

    [Fact]
    public void Adding_a_link_adds_one_step_and_splits_the_text_run()
    {
        var (d, _, va) = After("<p id=p>See the schedule today.</p>", doc => [Edit.ReplaceText(doc.Root.QuerySelector("#p"), "See the "), Edit.Append(doc.Root.QuerySelector("#p"), "<a href=/s>schedule</a> today.")]);
        Assert.Equal(["See the", "link: schedule", "today."], va.Items.Select(i => i.Text).ToArray());
        Assert.Contains(d.Added, i => i.Text == "link: schedule");
        Assert.Contains(d.Added, i => i.Text == "today.");
        ReadingItemChange c = Assert.Single(d.Changed);
        Assert.Equal("See the schedule today.", c.Before.Text);
        Assert.Equal("See the", c.After.Text);
    }

    [Fact]
    public void Hiding_content_under_a_context_removes_its_steps()
    {
        Document doc = Document.Parse("<style>.menu{display:none} a:focus + .menu{display:block}</style><a id=a href=#>Open</a><div class=menu><a href=/x>Item</a></div>");
        AccessibilityTree off = AccessibilityTree.Build(doc, DocumentStyles.Create(doc));
        AccessibilityTree on = off.WithContext(new StyleContext { Focus = doc.Root.QuerySelector("#a") });
        ReadingViewDiff d = off.CreateReadingView().Diff(on.CreateReadingView());
        Assert.Equal(["link: Item"], d.Added.Select(i => i.Text).ToArray());
        Assert.Empty(d.Removed);
        TreeDiff t = off.Diff(on);
        Assert.NotEmpty(t.Added);
        Assert.Empty(t.Removed);
    }
}
