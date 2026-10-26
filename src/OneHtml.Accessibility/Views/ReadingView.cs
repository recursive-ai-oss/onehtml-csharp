using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Tree;
using RecursiveAI.OneHtml.Accessibility.Views;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility;

/// <summary>What a <see cref="ReadingItem"/> is.</summary>
public enum ReadingItemKind : byte
{
    /// <summary>A run of text: adjacent text of the page merged the way a browse-mode reader reads a line.</summary>
    Text,

    /// <summary>One announced element: a link, button, heading, form field, list item, table cell and so on.</summary>
    Element,

    /// <summary>Entering a container: a list, table, landmark, region, dialog, form or group.</summary>
    Start,

    /// <summary>Leaving a container that a <see cref="Start"/> item opened.</summary>
    End,
}

/// <summary>One step of a <see cref="ReadingView"/>.</summary>
public sealed class ReadingItem
{
    internal ReadingItem(ReadingItemKind kind, string text, AxNode node, IReadOnlyList<AxNode> nodes, string? lang, int depth)
    {
        Kind = kind;
        Text = text;
        Node = node;
        Nodes = nodes;
        Lang = lang;
        Depth = depth;
    }

    /// <summary>What kind of step this is.</summary>
    public ReadingItemKind Kind { get; }

    /// <summary>The wording of the step, in English; see <c>docs/reading-view.md</c>. Never empty.</summary>
    public string Text { get; }

    /// <summary>The tree node the step comes from: the announced element, the container, or the first text node of a run.</summary>
    public AxNode Node { get; }

    /// <summary>The DOM node of <see cref="Node"/>.</summary>
    public Node DomNode => Node.DomNode;

    /// <summary>Every tree node the step covers: the text nodes of a run, otherwise just <see cref="Node"/>.</summary>
    public IReadOnlyList<AxNode> Nodes { get; }

    /// <summary>The language in effect for the text (the nearest <c>lang</c> attribute), or <see langword="null"/> when none is declared or it is empty (unknown).</summary>
    public string? Lang { get; }

    /// <summary>How many containers are open around the step; a <see cref="ReadingItemKind.Start"/> and its <see cref="ReadingItemKind.End"/> have the same depth, the items between them one more.</summary>
    public int Depth { get; }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// The accessibility tree as one linear sequence of steps, in tree order (document order with <c>aria-owns</c> children placed
/// where the tree places them): what a user reading the page top to bottom in browse mode would be told. An approximation with
/// generic English wording, not what any particular screen reader says; see <c>docs/reading-view.md</c> for the wording rules.
/// </summary>
public sealed class ReadingView
{
    private ReadingView(AccessibilityTree tree, IReadOnlyList<ReadingItem> items)
    {
        Tree = tree;
        Items = items;
    }

    /// <summary>The tree the view was made from.</summary>
    public AccessibilityTree Tree { get; }

    /// <summary>The steps, in reading order.</summary>
    public IReadOnlyList<ReadingItem> Items { get; }

    /// <summary>Makes the reading view of a tree.</summary>
    /// <param name="tree">The tree.</param>
    /// <param name="cancellationToken">Observed periodically.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static ReadingView Create(AccessibilityTree tree, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return new ReadingView(tree, new Builder(tree, cancellationToken).Run());
    }

    /// <summary>The steps as lines of text, indented two spaces per <see cref="ReadingItem.Depth"/>, one per step.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (ReadingItem i in Items)
        {
            sb.Append(' ', i.Depth * 2).Append(i.Text).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>The differences between this view and another: see <see cref="ReadingViewDiff"/>.</summary>
    /// <param name="other">A view of the same document under another context, or of another snapshot when <paramref name="changes"/> is given.</param>
    /// <param name="changes">The edit's change set when <paramref name="other"/> is of the next snapshot; <see langword="null"/> for the same document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentException">The views are of different documents and no change set relates them.</exception>
    public ReadingViewDiff Diff(ReadingView other, Editing.ChangeSet? changes = null)
    {
        ArgumentNullException.ThrowIfNull(other);
        return ReadingViewDiff.Compute(this, other, changes);
    }

    // ---- wording ----

    private enum Class : byte
    {
        Silent,
        Announce,
        Boundary,
    }

    private sealed record RoleWording(Class Kind, string Phrase);

    private static readonly Dictionary<string, RoleWording> Words = BuildWords();

    private static Dictionary<string, RoleWording> BuildWords()
    {
        var d = new Dictionary<string, RoleWording>(StringComparer.Ordinal);
        void A(string role, string phrase) => d[role] = new RoleWording(Class.Announce, phrase);
        void B(string role, string phrase) => d[role] = new RoleWording(Class.Boundary, phrase);
        A("link", "link");
        A("button", "button");
        A("heading", "heading");
        A("checkbox", "checkbox");
        A("radio", "radio button");
        A("switch", "switch");
        A("textbox", "text field");
        A("searchbox", "search field");
        A("combobox", "combo box");
        A("option", "option");
        A("slider", "slider");
        A("spinbutton", "spin button");
        A("progressbar", "progress bar");
        A("meter", "meter");
        A("scrollbar", "scroll bar");
        A("image", "image");
        A("img", "image");
        A("menuitem", "menu item");
        A("menuitemcheckbox", "checkbox menu item");
        A("menuitemradio", "radio menu item");
        A("tab", "tab");
        A("treeitem", "tree item");
        A("separator", "separator");
        A("math", "math");
        A("tooltip", "tooltip");
        A("alert", "alert");
        A("status", "status");
        A("log", "log");
        A("marquee", "marquee");
        A("timer", "timer");
        A("note", "note");
        A("blockquote", "block quote");
        A("term", "term");
        A("definition", "definition");
        A("code", "code");
        A("columnheader", "column header");
        A("rowheader", "row header");
        A("cell", "cell");
        A("gridcell", "cell");
        A("listitem", "list item");
        A("tabpanel", "tab panel");
        A("application", "application");
        A("document", "document");
        A("insertion", "inserted text");
        A("deletion", "deleted text");
        B("list", "list");
        B("table", "table");
        B("grid", "grid");
        B("treegrid", "tree grid");
        B("banner", "banner landmark");
        B("complementary", "complementary landmark");
        B("contentinfo", "content info landmark");
        B("main", "main landmark");
        B("navigation", "navigation landmark");
        B("search", "search landmark");
        B("region", "region");
        B("form", "form");
        B("dialog", "dialog");
        B("alertdialog", "alert dialog");
        B("group", "group");
        B("radiogroup", "radio group");
        B("toolbar", "toolbar");
        B("menu", "menu");
        B("menubar", "menu bar");
        B("listbox", "list box");
        B("tablist", "tab list");
        B("tree", "tree");
        B("figure", "figure");
        B("article", "article");
        return d;
    }

    /// <summary>Roles whose contents are part of the control and are not read again (WAI-ARIA 1.2 "Children Presentational: True").</summary>
    private static bool ChildrenPresentational(string role) => role is "button" or "checkbox" or "image" or "img" or "menuitemcheckbox" or "menuitemradio"
        or "option" or "radio" or "scrollbar" or "separator" or "slider" or "switch" or "tab" or "math" or "progressbar" or "meter" or "combobox";

    // ---- the walk ----

    private sealed class Frame
    {
        public required AxNode Node;
        public int Next;
        public bool Entered;
        public bool SkipChildren;
        public bool Boundary;
        public string? EndText;
        public bool FlushOnExit;
        public string? Lang;
    }

    private sealed class Builder
    {
        private readonly AccessibilityTree _tree;
        private readonly CancellationToken _ct;
        private readonly List<ReadingItem> _items = [];
        private readonly List<AxNode> _run = [];
        private readonly StringBuilder _runText = new();
        private string? _runLang;
        private int _depth;
        private readonly Dictionary<AxNode, TableModel> _tables = [];
        private readonly Dictionary<AxNode, int> _listSizes = [];
        private readonly HashSet<int> _nameLabels = [];
        private readonly Lang.Cache _langs = new();

        public Builder(AccessibilityTree tree, CancellationToken ct)
        {
            _tree = tree;
            _ct = ct;
        }

        public List<ReadingItem> Run()
        {
            CountListItems();
            foreach (AxNode n in _tree.Nodes)
            {
                // A control or group whose name is its label's text says that text itself; the label is not read again.
                if (n.Kind == AxNodeKind.Element && !n.IsIgnored && n.Name.Source == AxTextSource.NativeLabelElement && Words.ContainsKey(n.Role))
                {
                    foreach (Node label in n.Name.RelatedElements)
                    {
                        _nameLabels.Add(label.Index);
                    }
                }
            }

            var stack = new Stack<Frame>();
            stack.Push(new Frame { Node = _tree.Root });
            int steps = 0;
            while (stack.Count > 0)
            {
                if ((++steps & 255) == 0)
                {
                    _ct.ThrowIfCancellationRequested();
                }

                Frame f = stack.Peek();
                if (!f.Entered)
                {
                    f.Entered = true;
                    Enter(f);
                }

                IReadOnlyList<AxNode> kids = f.Node.Children;
                if (!f.SkipChildren && f.Next < kids.Count)
                {
                    stack.Push(new Frame { Node = kids[f.Next++] });
                    continue;
                }

                Exit(f);
                stack.Pop();
            }

            Flush();
            return _items;
        }

        // A list says how many items it has: the listitem nodes whose nearest list ancestor it is.
        private void CountListItems()
        {
            var stack = new Stack<(AxNode Node, AxNode? List)>();
            stack.Push((_tree.Root, null));
            while (stack.Count > 0)
            {
                (AxNode n, AxNode? list) = stack.Pop();
                AxNode? next = list;
                if (n.Kind == AxNodeKind.Element && !n.IsIgnored)
                {
                    if (n.Role == "list")
                    {
                        next = n;
                        _listSizes[n] = 0;
                    }
                    else if (n.Role == "listitem" && list is not null)
                    {
                        _listSizes[list]++;
                    }
                }

                foreach (AxNode c in n.Children)
                {
                    stack.Push((c, next));
                }
            }
        }

        private void Enter(Frame f)
        {
            AxNode n = f.Node;
            if (n.Kind == AxNodeKind.Document)
            {
                return;
            }

            if (n.Kind == AxNodeKind.Text)
            {
                f.SkipChildren = true;
                if (!n.IsIgnored)
                {
                    if (!InsideNameLabel(n.DomNode))
                    {
                        AddText(n);
                    }
                }
                else if (_run.Count > 0 && n.IgnoredReasons.Count == 1 && n.IgnoredReasons[0] == "uninteresting")
                {
                    // White space between inline neighbours is a space in the line being read.
                    _runText.Append(' ');
                }

                return;
            }

            bool block = IsBlock(n);
            if (block)
            {
                Flush();
            }

            if (n.IsIgnored || !Words.TryGetValue(n.Role, out RoleWording? w))
            {
                f.FlushOnExit = block;
                return;
            }

            Flush();
            string? lang = _langs.Of(n.DomNode);
            f.Lang = lang;
            string name = n.Name.Value;
            if (w.Kind == Class.Boundary)
            {
                f.Boundary = true;
                f.EndText = "end of " + w.Phrase;
                Add(ReadingItemKind.Start, Container(n, w), n, lang);
                _depth++;
                return;
            }

            f.FlushOnExit = true;
            if (n.Role is "cell" or "gridcell" or "columnheader" or "rowheader")
            {
                Add(ReadingItemKind.Element, CellText(n, w, out bool inline), n, lang);
                f.SkipChildren = inline;
                return;
            }

            // A control whose contents are only its own name is read in one step. Otherwise (a heading holding a link, a
            // list item) the step announces the role and the contents follow as steps of their own, so the name that came
            // from those contents is not said twice.
            bool leaf = ChildrenPresentational(n.Role) || (name.Length > 0 && IsTextOnly(n));
            var sb = new StringBuilder(Phrase(n, w));
            AppendStates(sb, n);
            if (name.Length > 0 && (leaf || n.Name.Source != AxTextSource.Contents))
            {
                sb.Append(": ").Append(name);
            }

            AppendValue(sb, n);

            Add(ReadingItemKind.Element, sb.ToString(), n, lang);
            f.SkipChildren = leaf;
        }

        private bool InsideNameLabel(Node text)
        {
            if (_nameLabels.Count == 0)
            {
                return false;
            }

            for (Node cur = text.Parent; !cur.IsNull && cur.IsElement; cur = cur.Parent)
            {
                if (_nameLabels.Contains(cur.Index))
                {
                    return true;
                }
            }

            return false;
        }

        private void Exit(Frame f)
        {
            if (f.Boundary)
            {
                Flush();
                _depth--;
                Add(ReadingItemKind.End, f.EndText!, f.Node, f.Lang);
            }
            else if (f.FlushOnExit)
            {
                Flush();
            }
        }

        // ---- text runs ----

        private void AddText(AxNode n)
        {
            string? lang = _langs.Of(n.DomNode);
            if (_run.Count > 0 && !string.Equals(lang, _runLang, StringComparison.Ordinal))
            {
                Flush();
            }

            if (_run.Count == 0)
            {
                _runLang = lang;
            }

            _run.Add(n);
            _runText.Append(n.Name.Value);
        }

        private void Flush()
        {
            if (_run.Count == 0)
            {
                return;
            }

            string text = TreeText.Collapse(_runText.ToString());
            if (text.Length > 0)
            {
                _items.Add(new ReadingItem(ReadingItemKind.Text, text, _run[0], _run.ToArray(), _runLang, _depth));
            }

            _run.Clear();
            _runText.Clear();
        }

        private void Add(ReadingItemKind kind, string text, AxNode node, string? lang)
        {
            _items.Add(new ReadingItem(kind, text, node, [node], lang, _depth));
        }

        // ---- wording of one element ----

        private static string Phrase(AxNode n, RoleWording w)
        {
            if (n.Role == "heading")
            {
                return n.States.Level is { } l ? "heading level " + l.ToString(CultureInfo.InvariantCulture) : "heading";
            }

            if (n.Role == "textbox" && n.States.Multiline)
            {
                return "text area";
            }

            if (n.Role == "listitem" && n.States.PosInSet is { } p && n.States.SetSize is { } s)
            {
                return "item " + p.ToString(CultureInfo.InvariantCulture) + " of " + s.ToString(CultureInfo.InvariantCulture);
            }

            if (n.States.RoleDescription is { Length: > 0 } rd)
            {
                return rd;
            }

            return w.Phrase;
        }

        private string Container(AxNode n, RoleWording w)
        {
            var sb = new StringBuilder(w.Phrase);
            if (n.Role == "list")
            {
                int count = _listSizes.GetValueOrDefault(n);
                if (count > 0)
                {
                    sb.Append(" with ").Append(count.ToString(CultureInfo.InvariantCulture)).Append(count == 1 ? " item" : " items");
                }
            }
            else if (n.Role is "table" or "grid" or "treegrid")
            {
                TableModel m = ModelOf(n);
                sb.Append(" with ").Append(m.Rows.ToString(CultureInfo.InvariantCulture)).Append(m.Rows == 1 ? " row" : " rows")
                    .Append(" and ").Append(m.Columns.ToString(CultureInfo.InvariantCulture)).Append(m.Columns == 1 ? " column" : " columns");
            }
            else if (n.Role == "dialog" || n.Role == "alertdialog")
            {
                if (n.States.Modal)
                {
                    sb.Append(", modal");
                }
            }

            AppendStates(sb, n);
            if (n.Name.Value.Length > 0)
            {
                sb.Append(": ").Append(n.Name.Value);
            }

            return sb.ToString();
        }

        private TableModel ModelOf(AxNode table)
        {
            if (!_tables.TryGetValue(table, out TableModel? m))
            {
                _tables[table] = m = TableModel.Build(table, 100_000);
            }

            return m;
        }

        private string CellText(AxNode n, RoleWording w, out bool inline)
        {
            var sb = new StringBuilder(w.Phrase);
            AxNode? table = n.Parent;
            while (table is not null && !(table.Kind == AxNodeKind.Element && table.Role is "table" or "grid" or "treegrid"))
            {
                table = table.Parent;
            }

            inline = false;
            if (n.Role is "columnheader" or "rowheader")
            {
                inline = n.Name.Value.Length > 0 && IsTextOnly(n);
                AppendStates(sb, n);
                if (inline)
                {
                    sb.Append(": ").Append(n.Name.Value);
                }

                return sb.ToString();
            }

            if (table is not null && ModelOf(table).Find(n) is { } cell)
            {
                (List<AxNode> cols, List<AxNode> rows) = ModelOf(table).HeadersOf(cell);
                var parts = new List<string>();
                if (cols.Count > 0)
                {
                    parts.Add("column header " + Join(cols));
                }

                if (rows.Count > 0)
                {
                    parts.Add("row header " + Join(rows));
                }

                if (parts.Count > 0)
                {
                    sb.Append(": ").AppendJoin(", ", parts);
                }
            }

            AppendStates(sb, n);
            return sb.ToString();
        }

        private static string Join(List<AxNode> headers)
        {
            var names = new List<string>();
            foreach (AxNode h in headers)
            {
                if (h.Name.Value.Length > 0)
                {
                    names.Add(h.Name.Value);
                }
            }

            return string.Join(" / ", names);
        }

        private static void AppendStates(StringBuilder sb, AxNode n)
        {
            AxStates s = n.States;
            if (n.Role is "checkbox" or "radio" or "switch" or "menuitemcheckbox" or "menuitemradio" && s.Checked is { } c)
            {
                sb.Append(c switch { AxTristate.True => ", checked", AxTristate.Mixed => ", partially checked", _ => ", not checked" });
            }

            if (s.Pressed is { } p)
            {
                sb.Append(p switch { AxTristate.True => ", pressed", AxTristate.Mixed => ", partially pressed", _ => ", not pressed" });
            }

            if (s.Expanded is { } e)
            {
                sb.Append(e ? ", expanded" : ", collapsed");
            }

            if (s.Selected == true)
            {
                sb.Append(", selected");
            }

            if (s.Current is { Length: > 0 } cur)
            {
                sb.Append(cur switch { "page" or "true" => ", current page", "step" => ", current step", "location" => ", current location", "date" => ", current date", "time" => ", current time", _ => ", current" });
            }

            if (s.Required)
            {
                sb.Append(", required");
            }

            if (s.Invalid is { Length: > 0 } inv && inv != "false")
            {
                sb.Append(", invalid entry");
            }

            if (s.Disabled)
            {
                sb.Append(", unavailable");
            }

            if (s.ReadOnly)
            {
                sb.Append(", read only");
            }

            if (s.HasPopup is { Length: > 0 })
            {
                sb.Append(", has popup");
            }

            if (n.Role != "listitem" && n.Role is "tab" or "option" or "radio" or "menuitem" or "treeitem" && s.PosInSet is { } pos && s.SetSize is { } size)
            {
                sb.Append(", ").Append(pos.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(size.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AppendValue(StringBuilder sb, AxNode n)
        {
            if (n.Role is not ("textbox" or "searchbox" or "combobox" or "slider" or "spinbutton" or "progressbar" or "meter"))
            {
                return;
            }

            string? v = n.States.ValueText ?? n.States.Value ?? (n.States.ValueNow is { } d ? d.ToString("R", CultureInfo.InvariantCulture) : null);
            if (!string.IsNullOrEmpty(v))
            {
                sb.Append(", value ").Append(v);
            }
        }

        // ---- facts about the page ----

        private bool IsBlock(AxNode n)
        {
            DisplayValue d = _tree.Styles.GetStyle(n.DomNode).Display;
            return !(d.Kind == DisplayKind.Contents || (d.Kind == DisplayKind.Box && d.Outside == DisplayOutside.Inline && d.Inside == DisplayInside.Flow && !d.IsListItem));
        }

        /// <summary>True when everything under the node that is read at all is plain text: its name already says it.</summary>
        private static bool IsTextOnly(AxNode node)
        {
            var stack = new Stack<AxNode>();
            foreach (AxNode c in node.Children)
            {
                stack.Push(c);
            }

            while (stack.Count > 0)
            {
                AxNode n = stack.Pop();
                if (n.Kind == AxNodeKind.Element && !n.IsIgnored && Words.TryGetValue(n.Role, out RoleWording? word) && word.Kind != Class.Silent)
                {
                    return false;
                }

                foreach (AxNode c in n.Children)
                {
                    stack.Push(c);
                }
            }

            return true;
        }
    }
}
