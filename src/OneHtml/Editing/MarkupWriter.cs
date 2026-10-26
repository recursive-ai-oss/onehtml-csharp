using System;
using System.Text;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>Facts about HTML element names the writer and the model need.</summary>
internal static class HtmlNames
{
    public static bool IsVoid(int name) =>
        name is N.Area or N.Base or N.Basefont or N.Bgsound or N.Br or N.Col or N.Embed or N.Frame or N.Hr
            or N.Img or N.Input or N.Keygen or N.Link or N.Meta or N.Param or N.Source or N.Track or N.Wbr;

    /// <summary>HTML 13.3: the elements whose text children are serialized without escaping.</summary>
    public static bool IsRawText(int name, bool scripting) =>
        name is N.Style or N.Script or N.Xmp or N.Iframe or N.Noembed or N.Noframes or N.Plaintext
            || (name == N.Noscript && scripting);

    public static string UpperAscii(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] is not (>= 'a' and <= 'z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(s.Length, s, static (dest, src) =>
        {
            for (int k = 0; k < src.Length; k++)
            {
                char c = src[k];
                dest[k] = c is >= 'a' and <= 'z' ? (char)(c - 0x20) : c;
            }
        });
    }
}

/// <summary>
/// How a document writes markup, read from the markup it already has: the case of tag and attribute names, the
/// attribute quote, how void elements end, and the newline. New markup copies it so an inserted element does not
/// look foreign.
/// </summary>
internal sealed class LocalStyle
{
    public bool UpperTags { get; private init; }

    public bool UpperAttributes { get; private init; }

    public char Quote { get; private init; } = '"';

    /// <summary>0 for <c>&lt;br&gt;</c>, 1 for <c>&lt;br/&gt;</c>, 2 for <c>&lt;br /&gt;</c>.</summary>
    public int VoidStyle { get; private init; }

    public bool CrLf { get; private init; }

    public static LocalStyle Default { get; } = new();

    /// <summary>Reads the style from a bounded sample of the document: the first few thousand elements and attributes.</summary>
    public static LocalStyle Infer(Document doc)
    {
        Arena a = doc.Arena;
        string text = doc.Source.Text;
        int lowerTags = 0, upperTags = 0, lowerAttrs = 0, upperAttrs = 0, dq = 0, sq = 0;
        int plain = 0, slash = 0, spaceSlash = 0;
        int limit = Math.Min(a.Count, 5000);
        for (int i = 0; i < limit; i++)
        {
            if (a.Kind[i] != (byte)NodeKind.Element || (a.Flags[i] & (F.ImpliedStart | F.Cloned | F.Synthesized)) != 0 || a.SrcStart[i] < 0)
            {
                continue;
            }

            int s = a.SrcStart[i];
            if (s + 1 < text.Length)
            {
                char c = text[s + 1];
                if (c is >= 'A' and <= 'Z')
                {
                    upperTags++;
                }
                else if (c is >= 'a' and <= 'z')
                {
                    lowerTags++;
                }
            }

            for (int k = 0; k < a.AttrCount[i]; k++)
            {
                int row = a.AttrStart[i] + k;
                int ns = a.ANameStart[row];
                if (ns < 0 || ns >= text.Length)
                {
                    continue;
                }

                char c = text[ns];
                if (c is >= 'A' and <= 'Z')
                {
                    upperAttrs++;
                }
                else if (c is >= 'a' and <= 'z')
                {
                    lowerAttrs++;
                }

                switch ((AttributeQuote)(a.AFlags[row] & AF.QuoteMask))
                {
                    case AttributeQuote.DoubleQuoted:
                        dq++;
                        break;
                    case AttributeQuote.SingleQuoted:
                        sq++;
                        break;
                    default:
                        break;
                }
            }

            if (a.Ns[i] == (byte)ElementNamespace.Html && HtmlNames.IsVoid(a.Name[i]))
            {
                int e = a.SrcEnd[i];
                if (e >= 2 && e <= text.Length && text[e - 1] == '>')
                {
                    if (e >= 3 && text[e - 2] == '/' && text[e - 3] == ' ')
                    {
                        spaceSlash++;
                    }
                    else if (text[e - 2] == '/')
                    {
                        slash++;
                    }
                    else
                    {
                        plain++;
                    }
                }
            }
        }

        // A "\r\n" in the bytes is one line feed in the text, so a line feed that took twice the bytes of a letter was a pair.
        bool crlf = false;
        SourceText src = doc.Source;
        if (src.Bytes.Length > 0)
        {
            int nl = text.IndexOf('\n', StringComparison.Ordinal);
            int letter = -1;
            for (int i = 0; i < text.Length && i < 4096; i++)
            {
                if (text[i] is >= 'a' and <= 'z')
                {
                    letter = i;
                    break;
                }
            }

            if (nl >= 0 && letter >= 0)
            {
                int wn = src.ToByteOffset(nl + 1) - src.ToByteOffset(nl);
                int wl = src.ToByteOffset(letter + 1) - src.ToByteOffset(letter);
                crlf = wl > 0 && wn == 2 * wl;
            }
        }

        return new LocalStyle
        {
            UpperTags = upperTags > lowerTags,
            UpperAttributes = upperAttrs > lowerAttrs,
            Quote = sq > dq ? '\'' : '"',
            VoidStyle = spaceSlash > plain && spaceSlash >= slash ? 2 : slash > plain ? 1 : 0,
            CrLf = crlf,
        };
    }
}

/// <summary>
/// Writes nodes of the working tree as markup in a document's local style (HTML 13.3, with the document's quote,
/// case and void-element conventions). A character the document's encoding cannot hold becomes a numeric character
/// reference where the syntax has references, and an error where it does not (raw text, comments, names).
/// </summary>
internal sealed class MarkupWriter
{
    private readonly Document _doc;
    private readonly Arena _a;
    private readonly LocalStyle _style;
    private readonly DocumentEncoder? _encoder;
    private readonly bool _scripting;

    public MarkupWriter(Document doc, LocalStyle style, DocumentEncoder? encoder, bool scripting)
    {
        _doc = doc;
        _a = doc.Arena;
        _style = style;
        _encoder = encoder;
        _scripting = scripting;
    }

    /// <summary>Set when something could not be written; the output is then unusable.</summary>
    public string? Error { get; private set; }

    public void WriteNode(int node, StringBuilder sb)
    {
        if (_a.Kind[node] == (byte)NodeKind.Element)
        {
            WriteOpenTag(node, sb);
            if (!IsVoid(node))
            {
                Walk(Holder(node), sb);
                WriteCloseTag(node, sb);
            }
        }
        else if (_a.Kind[node] is (byte)NodeKind.Document or (byte)NodeKind.DocumentFragment)
        {
            Walk(node, sb);
        }
        else
        {
            WriteLeaf(node, sb);
        }
    }

    public void WriteOpenTag(int e, StringBuilder sb)
    {
        bool html = _a.Ns[e] == (byte)ElementNamespace.Html;
        string name = _doc.Names.NameOf(_a.Name[e]);
        sb.Append('<');
        Name(html && _style.UpperTags ? HtmlNames.UpperAscii(name) : name, sb);
        int start = _a.AttrStart[e];
        for (int i = 0; i < _a.AttrCount[e]; i++)
        {
            int row = start + i;
            sb.Append(' ');
            string local = _doc.Names.NameOf(_a.AName[row]);
            string q = (AttributeNamespace)((_a.AFlags[row] & AF.NsMask) >> AF.NsShift) switch
            {
                AttributeNamespace.XLink => "xlink:" + local,
                AttributeNamespace.Xml => "xml:" + local,
                AttributeNamespace.Xmlns => local == "xmlns" ? "xmlns" : "xmlns:" + local,
                _ => local,
            };
            Name(html && _style.UpperAttributes ? HtmlNames.UpperAscii(q) : q, sb);
            sb.Append('=').Append(_style.Quote);
            AttributeValue(new Attr(_doc, row).Value, sb);
            sb.Append(_style.Quote);
        }

        if (html && HtmlNames.IsVoid(_a.Name[e]))
        {
            sb.Append(_style.VoidStyle switch { 1 => "/>", 2 => " />", _ => ">" });
        }
        else
        {
            sb.Append('>');
        }
    }

    /// <summary>Appends the attribute text <c> name="value"</c> for a name and value, the way the open tag writes it.</summary>
    public void WriteAttribute(string qualifiedName, bool htmlElement, ReadOnlySpan<char> value, StringBuilder sb)
    {
        sb.Append(' ');
        Name(htmlElement && _style.UpperAttributes ? HtmlNames.UpperAscii(qualifiedName) : qualifiedName, sb);
        sb.Append('=').Append(_style.Quote);
        AttributeValue(value, sb);
        sb.Append(_style.Quote);
    }

    public void WriteCloseTag(int e, StringBuilder sb)
    {
        if (IsVoid(e))
        {
            return;
        }

        string name = _doc.Names.NameOf(_a.Name[e]);
        sb.Append("</");
        Name(_a.Ns[e] == (byte)ElementNamespace.Html && _style.UpperTags ? HtmlNames.UpperAscii(name) : name, sb);
        sb.Append('>');
    }

    /// <summary>Appends text exactly as given, for a text node of a raw text element; fails when the encoding cannot hold it.</summary>
    public void WriteRawText(string text, StringBuilder sb) => Raw(text, sb);

    public void WriteLeaf(int n, StringBuilder sb)
    {
        ReadOnlySpan<char> data = new Node(_doc, n).Data;
        switch ((NodeKind)_a.Kind[n])
        {
            case NodeKind.Text:
                Text(n, data, sb);
                break;
            case NodeKind.Comment:
                sb.Append("<!--");
                Raw(data, sb);
                sb.Append("-->");
                break;
            case NodeKind.ProcessingInstruction:
                sb.Append("<?").Append(_doc.Names.NameOf(_a.Name[n])).Append(' ');
                Raw(data, sb);
                sb.Append("?>");
                break;
            case NodeKind.DocumentType:
                sb.Append("<!DOCTYPE ").Append(_doc.Names.NameOf(_a.Name[n])).Append('>');
                break;
            default:
                break;
        }
    }

    private bool IsVoid(int e) => _a.Ns[e] == (byte)ElementNamespace.Html && HtmlNames.IsVoid(_a.Name[e]);

    private int Holder(int e) =>
        _a.Ns[e] == (byte)ElementNamespace.Html && _a.Name[e] == N.Template && _a.Aux[e] >= 0 ? _a.Aux[e] : e;

    private void Walk(int holder, StringBuilder sb)
    {
        var stack = new System.Collections.Generic.Stack<int>();
        int cur = _a.First[holder];
        while (true)
        {
            if (cur >= 0)
            {
                if (_a.Kind[cur] == (byte)NodeKind.Element)
                {
                    WriteOpenTag(cur, sb);
                    if (!IsVoid(cur))
                    {
                        stack.Push(cur);
                        cur = _a.First[Holder(cur)];
                        continue;
                    }
                }
                else
                {
                    WriteLeaf(cur, sb);
                }

                cur = _a.Next[cur];
            }
            else
            {
                if (stack.Count == 0)
                {
                    return;
                }

                int parent = stack.Pop();
                WriteCloseTag(parent, sb);
                cur = _a.Next[parent];
            }
        }
    }

    private void Name(string name, StringBuilder sb)
    {
        foreach (char c in name)
        {
            if (c > 0x7F && !CanEncode(c))
            {
                Error ??= "a name has a character the document's encoding cannot hold: " + name;
            }
        }

        sb.Append(name);
    }

    private bool CanEncode(char c) => _encoder is null || (!char.IsSurrogate(c) && _encoder.CanEncode(c));

    private void Raw(ReadOnlySpan<char> data, StringBuilder sb)
    {
        if (_encoder is not null)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] > 0x7F)
                {
                    int cp = char.IsHighSurrogate(data[i]) && i + 1 < data.Length ? char.ConvertToUtf32(data[i], data[i + 1]) : data[i];
                    if (!_encoder.CanEncode(cp))
                    {
                        Error ??= "text that cannot use character references has a character the document's encoding cannot hold";
                    }

                    if (cp > 0xFFFF)
                    {
                        i++;
                    }
                }
            }
        }

        sb.Append(data);
    }

    private void Text(int n, ReadOnlySpan<char> data, StringBuilder sb)
    {
        int p = _a.Parent[n];
        bool htmlParent = p >= 0 && _a.Kind[p] == (byte)NodeKind.Element && _a.Ns[p] == (byte)ElementNamespace.Html;
        if (htmlParent && HtmlNames.IsRawText(_a.Name[p], _scripting))
        {
            Raw(data, sb);
            return;
        }

        // HTML 13.2.6.4.7: a line feed right after the start tag of pre, listing and textarea is dropped by the
        // parser, so one that is data needs another in front of it.
        if (htmlParent && _a.Prev[n] < 0 && data.Length > 0 && data[0] == '\n' && _a.Name[p] is N.Pre or N.Listing or N.Textarea)
        {
            sb.Append('\n');
        }

        Escaped(data, attribute: false, sb, '"');
    }

    private void AttributeValue(ReadOnlySpan<char> value, StringBuilder sb) => Escaped(value, attribute: true, sb, _style.Quote);

    /// <summary>The text to put between quotes of the given kind for an attribute value.</summary>
    public string EscapeAttributeValue(ReadOnlySpan<char> value, char quote)
    {
        var sb = new StringBuilder(value.Length + 8);
        Escaped(value, attribute: true, sb, quote);
        return sb.ToString();
    }

    /// <summary>The text to put in place of a text node's characters.</summary>
    public string EscapeText(ReadOnlySpan<char> value)
    {
        var sb = new StringBuilder(value.Length + 8);
        Escaped(value, attribute: false, sb, '"');
        return sb.ToString();
    }

    // HTML 13.3 "escaping a string", plus the references this writer needs: the quote in use, a carriage return (a
    // literal one would be read as a line feed) and characters the encoding has no bytes for.
    private void Escaped(ReadOnlySpan<char> s, bool attribute, StringBuilder sb, char quote)
    {
        int from = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            string? rep = c switch
            {
                '&' => "&amp;",
                '\u00A0' => "&nbsp;",
                '<' when !attribute => "&lt;",
                '>' when !attribute => "&gt;",
                '"' when attribute && quote == '"' => "&quot;",
                '\'' when attribute && quote == '\'' => "&#39;",
                '\r' => "&#13;",
                _ => null,
            };
            int length = 1;
            if (rep is null && c > 0x7F && _encoder is not null)
            {
                int cp = c;
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, s[i + 1]);
                    length = 2;
                }

                if (!_encoder.CanEncode(cp))
                {
                    rep = "&#x" + cp.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + ";";
                }
            }

            if (rep is not null)
            {
                sb.Append(s[from..i]).Append(rep);
                from = i + length;
            }

            i += length - 1;
        }

        sb.Append(s[from..]);
    }
}
