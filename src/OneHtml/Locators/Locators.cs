using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Locators;

/// <summary>
/// Names that look machine-generated. Build tools mint class names and ids such as <c>Button_primary__3xK9z</c>,
/// <c>css-1x2y3z</c> or <c>sc-bdfBwQ</c> that change with every build, so neither locators nor fingerprints may depend on them.
/// This is the one definition; <see cref="ElementFingerprint"/> and <see cref="LocatorGenerator"/> both use it.
/// </summary>
public static class GeneratedNames
{
    /// <summary>
    /// True when the name looks generated. The name is cut into segments at every character that is not an ASCII letter or
    /// digit, and it is hash-like when some segment of five or more characters is one of:
    /// <list type="bullet">
    /// <item>all digits (<c>12345</c>);</item>
    /// <item>letters and digits with at least two changes between a letter and a digit (<c>3xK9z</c>, <c>550e8400</c>; <c>year2024</c> has one change and is not hash-like);</item>
    /// <item>letters only, with both cases, that is not a run of camel-case words — an optional capital, lower-case letters, then any number of words that are a capital followed by one or more lower-case letters (<c>bdfBwQ</c> is hash-like; <c>navLink</c>, <c>Button</c> and <c>iPhone</c> are not).</item>
    /// </list>
    /// </summary>
    /// <param name="name">A class name or id.</param>
    public static bool IsHashLike(ReadOnlySpan<char> name)
    {
        int i = 0;
        while (i < name.Length)
        {
            if (!char.IsAsciiLetterOrDigit(name[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < name.Length && char.IsAsciiLetterOrDigit(name[i]))
            {
                i++;
            }

            if (i - start >= 5 && SegmentIsHashLike(name[start..i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentIsHashLike(ReadOnlySpan<char> s)
    {
        bool digit = false;
        bool lower = false;
        bool upper = false;
        int transitions = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            digit |= char.IsAsciiDigit(c);
            lower |= char.IsAsciiLetterLower(c);
            upper |= char.IsAsciiLetterUpper(c);
            if (i > 0 && char.IsAsciiDigit(c) != char.IsAsciiDigit(s[i - 1]))
            {
                transitions++;
            }
        }

        if (digit && !lower && !upper)
        {
            return true;
        }

        if (digit)
        {
            return transitions >= 2;
        }

        return lower && upper && !IsCamelCaseWords(s);
    }

    private static bool IsCamelCaseWords(ReadOnlySpan<char> s)
    {
        int i = 0;
        if (char.IsAsciiLetterUpper(s[0]))
        {
            i = 1;
        }

        int lowers = 0;
        while (i < s.Length && char.IsAsciiLetterLower(s[i]))
        {
            i++;
            lowers++;
        }

        if (lowers == 0)
        {
            return false;
        }

        while (i < s.Length)
        {
            if (!char.IsAsciiLetterUpper(s[i]))
            {
                return false;
            }

            i++;
            lowers = 0;
            while (i < s.Length && char.IsAsciiLetterLower(s[i]))
            {
                i++;
                lowers++;
            }

            if (lowers == 0)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Generates locators: selectors that are unique in the document they were generated from and name one element. Used to
/// find an element again later, so they prefer attributes that authors keep stable. See <c>docs/locators.md</c>.
/// </summary>
/// <remarks>
/// A generator keeps a table of how often each candidate selector occurs, built on first use, so generating a locator for
/// every element of a document is linear in the document size, and a generator can be shared between threads.
/// </remarks>
public sealed class LocatorGenerator
{
    private static readonly string[] s_hookAttributes = ["data-testid", "data-test", "data-test-id", "data-cy", "data-qa", "data-automation-id", "data-id"];

    private const int MaxSteps = 120;

    private readonly Document _document;
    private readonly bool _foldIds;
    private Dictionary<string, int>? _counts;

    /// <summary>Creates a generator for one document.</summary>
    /// <param name="document">The snapshot whose elements get locators.</param>
    public LocatorGenerator(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _foldIds = document.Mode == QuirksMode.Quirks;
    }

    /// <summary>
    /// Generates the locator of an element: the selector for the first of these that is unique in the document —
    /// <c>#id</c>; a <c>data-testid</c>-style hook; <c>tag[name="…"]</c>; <c>[role="…"]</c> and <c>[aria-label="…"]</c>; the plain tag
    /// name — taken from the element or, failing that, from its nearest ancestor, followed by a chain of
    /// <c>&gt;</c>-joined <c>tag:nth-of-type(k)</c> steps down to the element.
    /// </summary>
    /// <param name="element">An element of the generator's document that is part of the document tree.</param>
    /// <returns>The locator.</returns>
    /// <exception cref="ArgumentException">The node is not an element of this document's tree (a template's content, for example, has no locator), or lies more than 120 steps below its nearest anchor.</exception>
    public string Generate(Node element)
    {
        return TryGenerate(element, out string? locator)
            ? locator!
            : throw new ArgumentException("The node is not an element in the document tree.", nameof(element));
    }

    /// <summary>Like <see cref="Generate"/>, returning false instead of throwing for a node that cannot have a locator.</summary>
    /// <param name="element">A node.</param>
    /// <param name="locator">The locator, or null.</param>
    public bool TryGenerate(Node element, out string? locator)
    {
        locator = null;
        if (!element.IsElement || !ReferenceEquals(element.Document, _document) || !element.IsConnected)
        {
            return false;
        }

        Dictionary<string, int> counts = Counts;
        Arena a = _document.Arena;
        var steps = new List<string>();
        string? anchor = null;
        for (int x = element.Index; x >= 0 && a.Kind[x] == (byte)NodeKind.Element; x = a.Parent[x])
        {
            anchor = AnchorFor(new Node(_document, x), counts);
            if (anchor is not null)
            {
                break;
            }

            // The parser bounds a selector at 128 compounds; an element that far below any anchor has no locator.
            if (steps.Count >= MaxSteps)
            {
                return false;
            }

            steps.Add(Step(x));
        }

        if (anchor is null)
        {
            return false;
        }

        var sb = new StringBuilder(anchor);
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            sb.Append(" > ").Append(steps[i]);
        }

        string text = sb.ToString();

        // The chain is exact by construction (a unique anchor, then one child at a fixed position per step); this check
        // is that the text parses and names the element.
        if (!SelectorList.TryParse(text, out SelectorList? list) || !element.Matches(list!))
        {
            return false;
        }

        locator = text;
        return true;
    }

    /// <summary>
    /// The exhaustive check: the locator parses, and querying the whole document with it finds exactly the element. Linear
    /// in the document size per call, which makes it suitable for tests and one-off checks rather than for every element.
    /// </summary>
    /// <param name="element">The element the locator should name.</param>
    /// <param name="locator">The locator text.</param>
    public bool Verify(Node element, string locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        if (!element.IsElement || !ReferenceEquals(element.Document, _document) || !SelectorList.TryParse(locator, out SelectorList? list))
        {
            return false;
        }

        List<Node> found = _document.Root.QuerySelectorAll(list!);
        return found.Count == 1 && found[0] == element;
    }

    // ---- candidates ----

    private string Step(int x)
    {
        Arena a = _document.Arena;
        string tag = CssText.Ident(_document.Names.NameOf(a.Name[x]));
        int parent = a.Parent[x];
        bool plain = a.Ns[x] == (byte)ElementNamespace.Html && (parent < 0 || a.Kind[parent] == (byte)NodeKind.Document || a.Ns[parent] == (byte)ElementNamespace.Html);
        DocumentIndex.SiblingTable types = _document.Index.Types;
        if (plain)
        {
            return types.Total[x] <= 1
                ? tag
                : tag + ":nth-of-type(" + types.Position[x].ToString(CultureInfo.InvariantCulture) + ")";
        }

        return tag + ":nth-child(" + _document.Index.Siblings.Position[x].ToString(CultureInfo.InvariantCulture) + ")";
    }

    private string? AnchorFor(Node el, Dictionary<string, int> counts)
    {
        string? found = null;
        VisitCandidates(el, (key, text) =>
        {
            if (counts.TryGetValue(key, out int n) && n == 1)
            {
                found = text;
                return true;
            }

            return false;
        });
        return found;
    }

    /// <summary>Calls <paramref name="visit"/> with (count key, selector text) for each candidate of the element, best first, until it returns true.</summary>
    private void VisitCandidates(Node el, Func<string, string, bool> visit)
    {
        string local = el.LocalName;
        string tagText = CssText.Ident(local);
        string tagKey = SelectorParser.AsciiLower(local);

        // 1. id
        int idAt = el.FindAttr(N.Id);
        if (idAt >= 0)
        {
            string id = el.AttrValue(idAt).ToString();
            if (id.Length > 0 && !GeneratedNames.IsHashLike(id))
            {
                string key = "#" + (_foldIds ? SelectorParser.AsciiLower(id) : id);
                if (visit(key, "#" + CssText.Ident(id)))
                {
                    return;
                }
            }
        }

        // 2. test hooks
        AttributeList attrs = el.Attributes;
        foreach (string hook in s_hookAttributes)
        {
            for (int i = 0; i < attrs.Count; i++)
            {
                Attr at = attrs[i];
                if (at.Namespace == AttributeNamespace.None && at.LocalName == hook && at.Value.Length > 0)
                {
                    string v = at.Value.ToString();
                    string text = "[" + hook + "=" + CssSerializer.SerializeString(v) + "]";
                    if (visit("a" + text, text))
                    {
                        return;
                    }
                }
            }
        }

        // 3. name on form controls and similar
        string? name = el.GetAttribute(AttributeNamespace.None, "name");
        if (!string.IsNullOrEmpty(name))
        {
            if (visit("n" + tagKey + "[name=" + name + "]", tagText + "[name=" + CssSerializer.SerializeString(name) + "]"))
            {
                return;
            }
        }

        // 4. landmark and ARIA hooks
        string? role = el.GetAttribute(AttributeNamespace.None, "role");
        string? label = el.GetAttribute(AttributeNamespace.None, "aria-label");
        bool hasRole = !string.IsNullOrEmpty(role);
        bool hasLabel = !string.IsNullOrEmpty(label);
        if (hasRole && hasLabel)
        {
            if (visit("r" + role + "\0" + label, "[role=" + CssSerializer.SerializeString(role!) + "][aria-label=" + CssSerializer.SerializeString(label!) + "]"))
            {
                return;
            }
        }

        if (hasLabel)
        {
            if (visit("l" + label, "[aria-label=" + CssSerializer.SerializeString(label!) + "]"))
            {
                return;
            }
        }

        if (hasRole)
        {
            if (visit("o" + role, "[role=" + CssSerializer.SerializeString(role!) + "]"))
            {
                return;
            }
        }

        // 5. the tag name, when only one element has it (html, head, body, title, main, …)
        visit("t" + tagKey, tagText);
    }

    private Dictionary<string, int> Counts => System.Threading.Volatile.Read(ref _counts) ?? BuildCounts();

    private Dictionary<string, int> BuildCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        Arena a = _document.Arena;
        for (int i = Node.NextPreorder(a, 0, 0); i >= 0; i = Node.NextPreorder(a, i, 0))
        {
            if (a.Kind[i] != (byte)NodeKind.Element)
            {
                continue;
            }

            // Count every candidate the element offers, whether or not it is the best one.
            VisitCandidates(new Node(_document, i), (key, _) =>
            {
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                return false;
            });
        }

        return System.Threading.Interlocked.CompareExchange(ref _counts, counts, null) ?? counts;
    }
}

/// <summary>The identity of an element for durable targets: its tag, key attributes, text and position, as hashes and values (see <c>docs/locators.md</c>).</summary>
public sealed class ElementFingerprint : IEquatable<ElementFingerprint>
{
    private static readonly string[] s_keyAttributes = ["class", "href", "id", "name", "role", "src", "type"];

    private ElementFingerprint(string tag, IReadOnlyList<KeyValuePair<string, string>> attributes, string textHash, string pathHash)
    {
        Tag = tag;
        Attributes = attributes;
        TextHash = textHash;
        PathHash = pathHash;
    }

    /// <summary>The element's local name.</summary>
    public string Tag { get; }

    /// <summary>
    /// The key attributes that are present, sorted by name: <c>class</c> (hash-like classes removed, the rest joined by single
    /// spaces; left out when none remain), <c>href</c>, <c>id</c> (left out when hash-like), <c>name</c>, <c>role</c>, <c>src</c>, <c>type</c>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; }

    /// <summary><c>sha256:</c> and the hex SHA-256 of the whitespace-normalized text (see <see cref="NormalizeText"/>).</summary>
    public string TextHash { get; }

    /// <summary><c>sha256:</c> and the hex SHA-256 of the position path: <c>tag[k]</c> steps from the document element to this element, joined by <c>/</c>, with k the 1-based position among element siblings.</summary>
    public string PathHash { get; }

    /// <summary>Computes the fingerprint of an element.</summary>
    /// <param name="element">An element.</param>
    /// <exception cref="ArgumentException">The node is not an element.</exception>
    public static ElementFingerprint Create(Node element)
    {
        if (!element.IsElement)
        {
            throw new ArgumentException("The node is not an element.", nameof(element));
        }

        var attrs = new List<KeyValuePair<string, string>>();
        foreach (string key in s_keyAttributes)
        {
            string? v = element.GetAttribute(AttributeNamespace.None, key);
            if (v is null)
            {
                continue;
            }

            if (key == "class")
            {
                var kept = new List<string>();
                foreach (ReadOnlySpan<char> token in element.ClassList)
                {
                    if (!GeneratedNames.IsHashLike(token))
                    {
                        kept.Add(token.ToString());
                    }
                }

                if (kept.Count == 0)
                {
                    continue;
                }

                v = string.Join(' ', kept);
            }
            else if (key == "id" && GeneratedNames.IsHashLike(v))
            {
                continue;
            }

            attrs.Add(new KeyValuePair<string, string>(key, v));
        }

        string text = NormalizeText(element.TextContent ?? string.Empty);
        return new ElementFingerprint(element.LocalName, attrs, "sha256:" + Sha256.HashHex(Encoding.UTF8.GetBytes(text)), "sha256:" + Sha256.HashHex(Encoding.UTF8.GetBytes(PathText(element))));
    }

    /// <summary>
    /// The text the text hash covers: runs of ASCII whitespace (space, tab, line feed, form feed, carriage return) become one
    /// space, and leading and trailing whitespace is removed. Other characters, no-break spaces included, are untouched.
    /// </summary>
    /// <param name="text">The element's text content.</param>
    public static string NormalizeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        bool pending = false;
        foreach (char c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\f' or '\r')
            {
                pending = sb.Length > 0;
                continue;
            }

            if (pending)
            {
                sb.Append(' ');
                pending = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>The position path text that <see cref="PathHash"/> hashes, for example <c>html[1]/body[2]/div[3]</c>.</summary>
    /// <param name="element">An element.</param>
    public static string PathText(Node element)
    {
        var parts = new List<string>();
        for (Node n = element; n.IsElement; n = n.Parent)
        {
            int pos = Math.Max(1, element.Document!.Index.Siblings.Position[n.Index]);
            parts.Add(n.LocalName + "[" + pos.ToString(CultureInfo.InvariantCulture) + "]");
        }

        parts.Reverse();
        return string.Join('/', parts);
    }

    /// <summary>What replay compares: tag and key attributes exactly, the text hash, and (advisory) the path hash.</summary>
    /// <param name="other">The fingerprint to compare with.</param>
    public FingerprintComparison Compare(ElementFingerprint other)
    {
        ArgumentNullException.ThrowIfNull(other);
        bool sameAttrs = Tag == other.Tag && Attributes.Count == other.Attributes.Count;
        for (int i = 0; sameAttrs && i < Attributes.Count; i++)
        {
            sameAttrs = Attributes[i].Key == other.Attributes[i].Key && Attributes[i].Value == other.Attributes[i].Value;
        }

        return new FingerprintComparison(sameAttrs, TextHash == other.TextHash, PathHash == other.PathHash);
    }

    /// <inheritdoc />
    public bool Equals(ElementFingerprint? other)
    {
        if (other is null)
        {
            return false;
        }

        FingerprintComparison c = Compare(other);
        return c.TagAndAttributes && c.Text && c.Path;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ElementFingerprint);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Tag, TextHash, PathHash);
}

/// <summary>The three parts of a fingerprint comparison.</summary>
/// <param name="TagAndAttributes">The tag and every key attribute are equal.</param>
/// <param name="Text">The normalized text hashes are equal.</param>
/// <param name="Path">The position path hashes are equal (advisory: a mismatch does not make a target stale).</param>
public readonly record struct FingerprintComparison(bool TagAndAttributes, bool Text, bool Path);
