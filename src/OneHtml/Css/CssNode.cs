using System;
using System.Collections;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// A node of the parse: a rule, a declaration, or a discarded stretch of source. A view over the tree's node table;
/// members that do not apply to the node's <see cref="Kind"/> return empty or default values.
/// </summary>
public readonly struct CssNode : IEquatable<CssNode>
{
    private readonly CssTree _tree;
    private readonly int _index;

    internal CssNode(CssTree tree, int index)
    {
        _tree = tree;
        _index = index;
    }

    private ref readonly Item I => ref _tree.Items[_index];

    /// <summary>The tree this node belongs to.</summary>
    public CssTree Tree => _tree;

    /// <summary>What kind of node this is.</summary>
    public CssNodeKind Kind => I.Kind;

    /// <summary>The node's index in the tree's node table; the root is 0.</summary>
    public int Index => _index;

    /// <summary>The parent node; for a top-level node the root. The root has no parent: this returns the root itself.</summary>
    public CssNode Parent => new(_tree, I.Parent < 0 ? 0 : I.Parent);

    /// <summary>The next node with the same parent, or <c>null</c>.</summary>
    public CssNode? NextSibling => I.NextSibling < 0 ? null : new CssNode(_tree, I.NextSibling);

    /// <summary>
    /// Source range of the whole node: for a rule from its first token to its closing <c>}</c> or terminating
    /// <c>;</c>; for a declaration from its name to its <c>;</c> (or to its last token when there is none). A rule whose
    /// block is not closed ends at the end of the text.
    /// </summary>
    public CssRange Range => new(I.Start, I.End);

    /// <summary>The child nodes (rules and declarations in a block) in source order, whitespace and comments between them excluded.</summary>
    public CssNodeList Children => new(_tree, I.FirstChild);

    /// <summary>Only the declarations among <see cref="Children"/>.</summary>
    public DeclarationEnumerable Declarations => new(_tree, I.FirstChild);

    /// <summary>Only the rules (qualified and at-rules) among <see cref="Children"/>.</summary>
    public RuleEnumerable Rules => new(_tree, I.FirstChild);

    // ---- rules ----

    /// <summary>For an at-rule, its name without the <c>@</c>, with escapes decoded; for a declaration, its name.</summary>
    public string Name => I.Name < 0 ? string.Empty : NameToken.Value;

    /// <summary>For an at-rule or declaration, the name token.</summary>
    public CssToken NameToken => I.Name < 0 ? throw new InvalidOperationException("The node has no name.") : new CssToken(_tree, I.Name);

    /// <summary>For an at-rule or declaration, the range of the name token (the <c>@</c> is included for an at-rule); empty otherwise.</summary>
    public CssRange NameRange => I.Name < 0 ? default : new CssRange(_tree.Tokens[I.Name].Start, _tree.Tokens[I.Name].End);

    /// <summary>The prelude of a rule as component values, whitespace included (CSS Syntax §5.2).</summary>
    public ComponentValueList Prelude => I.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule
        ? new ComponentValueList(_tree, I.V0, I.V1)
        : default;

    /// <summary>
    /// The prelude's source range without leading and trailing whitespace; for a style rule this is the selector
    /// text, spliced by the editor.
    /// </summary>
    public CssRange PreludeRange => Prelude.Trimmed().Range is { IsEmpty: false } r ? r : PreludeAnchor;

    // An empty prelude has no tokens; point where it would be.
    private CssRange PreludeAnchor
    {
        get
        {
            if (I.Kind == CssNodeKind.AtRule)
            {
                int p = _tree.Tokens[I.Name].End;
                return new CssRange(p, p);
            }

            if (I.Kind == CssNodeKind.QualifiedRule)
            {
                return new CssRange(I.Start, I.Start);
            }

            return default;
        }
    }

    /// <summary>The prelude's source text, trimmed, verbatim (comments inside are kept).</summary>
    public ReadOnlySpan<char> PreludeText
    {
        get
        {
            CssRange r = PreludeRange;
            return _tree.Text.AsSpan(r.Start, r.Length);
        }
    }

    /// <summary>True when the rule has a <c>{}</c> block.</summary>
    public bool HasBlock => (I.Flags & Item.HasBlock) != 0;

    /// <summary>True when the rule's block has its closing <c>}</c> (false when it runs to the end of input).</summary>
    public bool IsBlockClosed => (I.Flags & Item.BlockClosed) != 0;

    /// <summary>Range from the opening <c>{</c> to the closing <c>}</c> inclusive; empty when there is no block.</summary>
    public CssRange BlockRange => !HasBlock ? default : new CssRange(_tree.Tokens[I.Open].Start, I.End);

    /// <summary>
    /// Range of what is between the braces, whitespace and comments included; where an unclosed block ends at the end of
    /// the text. Empty when there is no block.
    /// </summary>
    public CssRange ContentRange => !HasBlock
        ? default
        : new CssRange(_tree.Tokens[I.Open].End, I.Close >= 0 ? _tree.Tokens[I.Close].Start : _tree.Text.Length);

    /// <summary>The contents of the block as unparsed component values (the children are the parsed form).</summary>
    public ComponentValueList BlockValues => !HasBlock
        ? default
        : new ComponentValueList(_tree, I.Open + 1, I.Close >= 0 ? I.Close : _tree.MainCount);

    /// <summary>The range of the statement at-rule's terminating <c>;</c>, or empty.</summary>
    public CssRange SemicolonRange => I.Semi < 0 ? new CssRange(I.End, I.End) : new CssRange(_tree.Tokens[I.Semi].Start, _tree.Tokens[I.Semi].End);

    // ---- declarations ----

    /// <summary>For a declaration, its value as component values, without surrounding whitespace and <c>!important</c>.</summary>
    public ComponentValueList Value => I.Kind == CssNodeKind.Declaration ? new ComponentValueList(_tree, I.V0, I.V1) : default;

    /// <summary>
    /// For a declaration, the source text of its value, verbatim: from the first to the last value token, comments
    /// between them kept. Custom properties use this as their value (CSS Syntax §5.5.6 "original text").
    /// </summary>
    public ReadOnlySpan<char> ValueText => Value.Raw;

    /// <summary>For a declaration, the range of <see cref="ValueText"/>; empty (at the colon) when the value is empty.</summary>
    public CssRange ValueRange => I.Kind != CssNodeKind.Declaration
        ? default
        : I.V1 > I.V0 ? Value.Range : new CssRange(_tree.Tokens[I.Colon].End, _tree.Tokens[I.Colon].End);

    /// <summary>For a declaration, the range of the colon.</summary>
    public CssRange ColonRange => I.Colon < 0 ? default : new CssRange(_tree.Tokens[I.Colon].Start, _tree.Tokens[I.Colon].End);

    /// <summary>For a declaration, true when it ends in <c>!important</c>.</summary>
    public bool IsImportant => (I.Flags & Item.Important) != 0;

    /// <summary>For a declaration, the range from the <c>!</c> to the end of <c>important</c>; empty when not important.</summary>
    public CssRange ImportantRange => !IsImportant ? new CssRange(I.End, I.End) : new CssRange(_tree.Tokens[I.Open].Start, _tree.Tokens[I.Close].End);

    /// <summary>For a declaration, true when the name starts with <c>--</c> (a custom property; the name is case-sensitive).</summary>
    public bool IsCustomProperty => (I.Flags & Item.CustomProperty) != 0;

    /// <summary>True when the declaration or at-rule name equals <paramref name="name"/> ignoring ASCII case.</summary>
    /// <param name="name">The ASCII name.</param>
    public bool NameEqualsIgnoreCase(string name) => I.Name >= 0 && NameToken.Value.Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The source text of the whole node, verbatim.</summary>
    public ReadOnlySpan<char> Raw => _tree.Text.AsSpan(I.Start, I.End - I.Start);

    // ---- at-rule views ----

    /// <summary>For an at-rule, which well-known at-rule it is (by ASCII case-insensitive name).</summary>
    public AtRuleKind AtRuleKind => I.Kind == CssNodeKind.AtRule ? CssAtRules.Classify(Name) : AtRuleKind.None;

    /// <summary>A view of an <c>@media</c> rule, or <c>null</c> if this is another node.</summary>
    public MediaRule? AsMedia() => AtRuleKind == AtRuleKind.Media ? new MediaRule(this) : null;

    /// <summary>A view of an <c>@supports</c> rule, or <c>null</c>.</summary>
    public SupportsRule? AsSupports() => AtRuleKind == AtRuleKind.Supports ? new SupportsRule(this) : null;

    /// <summary>A view of an <c>@layer</c> rule (statement or block), or <c>null</c>.</summary>
    public LayerRule? AsLayer() => AtRuleKind == AtRuleKind.Layer ? new LayerRule(this) : null;

    /// <summary>A view of an <c>@import</c> rule, or <c>null</c>.</summary>
    public ImportRule? AsImport() => AtRuleKind == AtRuleKind.Import ? new ImportRule(this) : null;

    /// <summary>A view of an <c>@container</c> rule, or <c>null</c>.</summary>
    public ContainerRule? AsContainer() => AtRuleKind == AtRuleKind.Container ? new ContainerRule(this) : null;

    /// <summary>A view of an <c>@namespace</c> rule, or <c>null</c>.</summary>
    public NamespaceRule? AsNamespace() => AtRuleKind == AtRuleKind.Namespace ? new NamespaceRule(this) : null;

    /// <summary>A view of a <c>@charset</c> rule, or <c>null</c>. Note that <c>@charset</c> is not a real rule (CSS Syntax §8.3).</summary>
    public CharsetRule? AsCharset() => AtRuleKind == AtRuleKind.Charset ? new CharsetRule(this) : null;

    /// <inheritdoc/>
    public bool Equals(CssNode other) => ReferenceEquals(_tree, other._tree) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CssNode o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => _index;

    /// <summary>Equality of handles.</summary>
    /// <param name="left">A handle.</param>
    /// <param name="right">Another handle.</param>
    public static bool operator ==(CssNode left, CssNode right) => left.Equals(right);

    /// <summary>Inequality of handles.</summary>
    /// <param name="left">A handle.</param>
    /// <param name="right">Another handle.</param>
    public static bool operator !=(CssNode left, CssNode right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => Raw.ToString();
}

/// <summary>A sibling-linked list of nodes.</summary>
public readonly struct CssNodeList : IEnumerable<CssNode>
{
    private readonly CssTree _tree;
    private readonly int _first;

    internal CssNodeList(CssTree tree, int first)
    {
        _tree = tree;
        _first = first;
    }

    /// <summary>True when the list has no nodes.</summary>
    public bool IsEmpty => _first < 0;

    /// <summary>The number of nodes (counted by walking the list).</summary>
    public int Count
    {
        get
        {
            int n = 0;
            for (int i = _first; i >= 0; i = _tree.Items[i].NextSibling)
            {
                n++;
            }

            return n;
        }
    }

    /// <summary>Gets an enumerator over the nodes.</summary>
    public Enumerator GetEnumerator() => new(_tree, _first);

    IEnumerator<CssNode> IEnumerable<CssNode>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerator over a <see cref="CssNodeList"/>.</summary>
    public struct Enumerator : IEnumerator<CssNode>
    {
        private readonly CssTree _tree;
        private int _next;
        private int _current;

        internal Enumerator(CssTree tree, int first)
        {
            _tree = tree;
            _next = first;
            _current = -1;
        }

        /// <inheritdoc/>
        public readonly CssNode Current => new(_tree, _current);

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_next < 0)
            {
                return false;
            }

            _current = _next;
            _next = _tree.Items[_current].NextSibling;
            return true;
        }

        /// <inheritdoc/>
        public readonly void Reset() => throw new NotSupportedException();

        /// <inheritdoc/>
        public readonly void Dispose()
        {
        }
    }
}

/// <summary>The declarations among a node's children.</summary>
public readonly struct DeclarationEnumerable : IEnumerable<CssNode>
{
    private readonly CssTree _tree;
    private readonly int _first;

    internal DeclarationEnumerable(CssTree tree, int first)
    {
        _tree = tree;
        _first = first;
    }

    /// <summary>Gets an enumerator over the declarations.</summary>
    public IEnumerator<CssNode> GetEnumerator()
    {
        for (int i = _first; i >= 0; i = _tree.Items[i].NextSibling)
        {
            if (_tree.Items[i].Kind == CssNodeKind.Declaration)
            {
                yield return new CssNode(_tree, i);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>The rules (qualified and at-rules) among a node's children.</summary>
public readonly struct RuleEnumerable : IEnumerable<CssNode>
{
    private readonly CssTree _tree;
    private readonly int _first;

    internal RuleEnumerable(CssTree tree, int first)
    {
        _tree = tree;
        _first = first;
    }

    /// <summary>Gets an enumerator over the rules.</summary>
    public IEnumerator<CssNode> GetEnumerator()
    {
        for (int i = _first; i >= 0; i = _tree.Items[i].NextSibling)
        {
            if (_tree.Items[i].Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule)
            {
                yield return new CssNode(_tree, i);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
