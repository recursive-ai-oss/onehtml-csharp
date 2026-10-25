using System;
using System.Buffers;

namespace RecursiveAI.OneHtml.Parsing;

public sealed partial class HtmlTokenizer
{
    private static readonly SearchValues<char> s_tagNameEnd = SearchValues.Create("\t\n\f />");
    private static readonly SearchValues<char> s_attributeNameEnd = SearchValues.Create("\t\n\f />=");
    private static readonly SearchValues<char> s_doubleQuoteStops = SearchValues.Create("\"&\0");
    private static readonly SearchValues<char> s_singleQuoteStops = SearchValues.Create("'&\0");
    private static readonly SearchValues<char> s_unquotedStops = SearchValues.Create("\t\n\f &>\0");
    private static readonly SearchValues<char> s_unquotedStopsErrors = SearchValues.Create("\t\n\f &>\0\"'<=`");

    private AttrEntry[] _attrs = new AttrEntry[8];
    private int _attrCount;
    private AttrEntry[] _dupes = new AttrEntry[2];
    private int _dupCount;
    private int[]? _attrIndex;
    private bool _indexActive;

    internal struct AttrEntry
    {
        public TextRef Name;
        public TextRef Value;
        public int NameStart;
        public int NameEnd;
        public int ValueStart;
        public int ValueEnd;
        public AttributeQuote Quote;
        public int Hash;
    }

    internal ReadOnlySpan<char> ResolveText(TextRef r) => Resolve(r);

    internal int AttributeTotal(bool duplicates) => duplicates ? _dupCount : _attrCount;

    internal ref readonly AttrEntry AttributeAt(int index, bool duplicates) =>
        ref (duplicates ? ref _dupes[index] : ref _attrs[index]);

    // A "<" at p followed by "!", "/" and a letter or other than "/>" and EOF, or "?" (markup that is not a start tag
    // or a plain end tag). The text before p has been flushed.
    private bool MarkupAt(int p)
    {
        string t = _text;
        int c1 = t[p + 1];
        if (c1 == '!')
        {
            return MarkupDeclaration(p);
        }

        if (c1 == '?')
        {
            return ProcessingInstruction(p);
        }

        if (c1 == '/')
        {
            // HTML 13.2.5.7 "end tag open state"; the caller made sure a character follows.
            int c2 = t[p + 2];
            if (IsAsciiAlpha(c2))
            {
                return ParseTag(p, isEnd: true, p + 2);
            }

            if (c2 == '>')
            {
                ReportError(HtmlParseError.MissingEndTagName, p + 2);
                _pos = p + 3;
                return false;
            }

            ReportError(HtmlParseError.InvalidFirstCharacterOfTagName, p + 2);
            return BogusComment(p, p + 2);
        }

        return ParseTag(p, isEnd: false, p + 1);
    }

    // HTML 13.2.5.8 to 13.2.5.40: a start or end tag whose name begins at nameStart. Returns true when the tag was
    // emitted; false when the input ended inside it (the tag is abandoned and the end of file follows).
    private bool ParseTag(int tagStart, bool isEnd, int nameStart)
    {
        string t = _text;
        int len = t.Length;
        _attrCount = 0;
        _dupCount = 0;
        _selfClosing = false;
        _bufLen = 0;
        ResetAttributeIndex();

        // Tag name state.
        int nameEnd = len;
        int found = t.AsSpan(nameStart).IndexOfAny(s_tagNameEnd);
        if (found >= 0)
        {
            nameEnd = nameStart + found;
        }

        _name = MakeName(nameStart, nameEnd);
        if (found < 0)
        {
            return Abandon();
        }

        int p = nameEnd;
        switch (t[p])
        {
            case '>':
                return EmitTag(tagStart, isEnd, p + 1);
            case '/':
                p++;
                goto SelfClosing;
            default:
                p++;
                break;
        }

    BeforeAttributeName:
        // HTML 13.2.5.32
        while (p < len && IsTagWhitespace(t[p]))
        {
            p++;
        }

        if (p >= len)
        {
            return Abandon();
        }

        char c = t[p];
        if (c == '>')
        {
            return EmitTag(tagStart, isEnd, p + 1);
        }

        if (c == '/')
        {
            p++;
            goto SelfClosing;
        }

        int attrStart = p;
        int scanFrom = p;
        if (c == '=')
        {
            ReportError(HtmlParseError.UnexpectedEqualsSignBeforeAttributeName, p);
            scanFrom = p + 1;
        }

    AttributeName:
        // HTML 13.2.5.33
        if (_attrCount + _dupCount >= _limits.MaxAttributesPerTag)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxAttributesPerTag), _limits.MaxAttributesPerTag, (long)_attrCount + _dupCount + 1);
        }

        int attrNameEnd = len;
        int stop = t.AsSpan(scanFrom).IndexOfAny(s_attributeNameEnd);
        if (stop >= 0)
        {
            attrNameEnd = scanFrom + stop;
        }

        TextRef attrName = MakeAttributeName(attrStart, attrNameEnd);
        if (stop < 0)
        {
            return Abandon();
        }

        // Leaving the attribute name state: a repeated name is an error and the attribute is dropped.
        AddAttribute(attrName, attrStart, attrNameEnd, attrNameEnd);
        p = attrNameEnd;
        c = t[p];
        if (c == '=')
        {
            p++;
            goto BeforeAttributeValue;
        }

        // HTML 13.2.5.34 "after attribute name state"
        while (p < len && IsTagWhitespace(t[p]))
        {
            p++;
        }

        if (p >= len)
        {
            return Abandon();
        }

        c = t[p];
        if (c == '/')
        {
            p++;
            goto SelfClosing;
        }

        if (c == '=')
        {
            p++;
            goto BeforeAttributeValue;
        }

        if (c == '>')
        {
            return EmitTag(tagStart, isEnd, p + 1);
        }

        attrStart = p;
        scanFrom = p;
        goto AttributeName;

    BeforeAttributeValue:
        // HTML 13.2.5.35
        while (p < len && IsTagWhitespace(t[p]))
        {
            p++;
        }

        if (p >= len)
        {
            return Abandon();
        }

        c = t[p];
        if (c == '>')
        {
            ReportError(HtmlParseError.MissingAttributeValue, p);
            ref AttrEntry missing = ref CurrentAttribute();
            missing.Quote = AttributeQuote.Unquoted;
            missing.ValueStart = missing.ValueEnd = p;
            missing.Value = new TextRef { Start = p, Length = 0, SourceStart = p, SourceEnd = p };
            return EmitTag(tagStart, isEnd, p + 1);
        }

        if (c is '"' or '\'')
        {
            // HTML 13.2.5.36 and 13.2.5.37
            p++;
            if (!ParseQuotedValue(ref p, c))
            {
                return Abandon();
            }

            // HTML 13.2.5.39 "after attribute value (quoted) state"
            if (p >= len)
            {
                return Abandon();
            }

            c = t[p];
            if (IsTagWhitespace(c))
            {
                p++;
                goto BeforeAttributeName;
            }

            if (c == '/')
            {
                p++;
                goto SelfClosing;
            }

            if (c == '>')
            {
                return EmitTag(tagStart, isEnd, p + 1);
            }

            ReportError(HtmlParseError.MissingWhitespaceBetweenAttributes, p);
            goto BeforeAttributeName;
        }

        // HTML 13.2.5.38 "attribute value (unquoted) state"
        if (!ParseUnquotedValue(ref p))
        {
            return Abandon();
        }

        c = t[p];
        if (c == '>')
        {
            return EmitTag(tagStart, isEnd, p + 1);
        }

        p++;
        goto BeforeAttributeName;

    SelfClosing:
        // HTML 13.2.5.40
        if (p >= len)
        {
            return Abandon();
        }

        if (t[p] == '>')
        {
            _selfClosing = true;
            return EmitTag(tagStart, isEnd, p + 1);
        }

        ReportError(HtmlParseError.UnexpectedSolidusInTag, p);
        goto BeforeAttributeName;
    }

    // The input ended inside a tag (eof-in-tag): nothing is emitted for the tag, only the end of file.
    private bool Abandon()
    {
        ReportError(HtmlParseError.EofInTag, _text.Length);
        _state = State.Eof;
        _pos = _text.Length;
        _attrCount = 0;
        _dupCount = 0;
        return false;
    }

    private bool EmitTag(int tagStart, bool isEnd, int end)
    {
        _kind = isEnd ? HtmlTokenKind.EndTag : HtmlTokenKind.StartTag;
        _start = tagStart;
        _end = end;
        _pos = end;
        _state = State.Data;
        if (isEnd)
        {
            if (_attrCount > 0)
            {
                ReportError(HtmlParseError.EndTagWithAttributes, end - 1);
            }

            if (_selfClosing)
            {
                ReportError(HtmlParseError.EndTagWithTrailingSolidus, end - 1);
            }
        }
        else
        {
            // Remember the name for "appropriate end tag" matching.
            ReadOnlySpan<char> name = Resolve(_name);
            _lastStartLength = 0;
            _lastStartIsLetters = false;
            if (name.Length <= MaxTagNameLetters)
            {
                name.CopyTo(_lastStart);
                _lastStartLength = name.Length;
                _lastStartIsLetters = true;
                foreach (char ch in name)
                {
                    if (ch is not (>= 'a' and <= 'z'))
                    {
                        _lastStartIsLetters = false;
                        break;
                    }
                }
            }
        }

        return true;
    }

    // The tag name is the source text lowercased; it is only copied when it has an uppercase letter or a NUL.
    private TextRef MakeName(int start, int end)
    {
        var r = new TextRef { Start = start, Length = end - start, SourceStart = start, SourceEnd = end };
        ReadOnlySpan<char> span = _text.AsSpan(start, end - start);
        if (span.IndexOfAnyInRange('A', 'Z') < 0 && span.IndexOf('\0') < 0)
        {
            return r;
        }

        return CopyLowercased(start, end);
    }

    private TextRef MakeAttributeName(int start, int end)
    {
        ReadOnlySpan<char> span = _text.AsSpan(start, end - start);
        if (_errors is not null)
        {
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] is '"' or '\'' or '<')
                {
                    ReportError(HtmlParseError.UnexpectedCharacterInAttributeName, start + i);
                }
            }
        }

        if (span.IndexOfAnyInRange('A', 'Z') < 0 && span.IndexOf('\0') < 0)
        {
            return new TextRef { Start = start, Length = end - start, SourceStart = start, SourceEnd = end };
        }

        return CopyLowercased(start, end);
    }

    // Lowercases ASCII letters and replaces NUL with U+FFFD (an error each), into the scratch buffer.
    private TextRef CopyLowercased(int start, int end)
    {
        int bufStart = _bufLen;
        EnsureBuffer(end - start);
        for (int i = start; i < end; i++)
        {
            char ch = _text[i];
            if (ch is >= 'A' and <= 'Z')
            {
                ch = (char)(ch + 0x20);
            }
            else if (ch == '\0')
            {
                ReportError(HtmlParseError.UnexpectedNullCharacter, i);
                ch = '\uFFFD';
            }

            _buf[_bufLen++] = ch;
        }

        return new TextRef { Start = bufStart, Length = end - start, InBuffer = true, SourceStart = start, SourceEnd = end };
    }

    private ref AttrEntry CurrentAttribute()
    {
        // The attribute being built is the last one added to either list; see AddAttribute.
        return ref _currentIsDuplicate ? ref _dupes[_dupCount - 1] : ref _attrs[_attrCount - 1];
    }

    private bool _currentIsDuplicate;

    // Adds the attribute whose name was just read. Duplicates (HTML 13.2.5.33) go to their own list.
    private void AddAttribute(TextRef name, int start, int end, int valueAt)
    {
        ReadOnlySpan<char> nameSpan = Resolve(name);
        int hash = HashName(nameSpan);
        bool duplicate = FindAttribute(nameSpan, hash) >= 0;
        if (duplicate)
        {
            ReportError(HtmlParseError.DuplicateAttribute, end);
            if (_dupCount == _dupes.Length)
            {
                Array.Resize(ref _dupes, _dupes.Length * 2);
            }

            _currentIsDuplicate = true;
            _dupes[_dupCount++] = NewEntry(name, start, end, valueAt, hash);
            return;
        }

        if (_attrCount == _attrs.Length)
        {
            Array.Resize(ref _attrs, _attrs.Length * 2);
        }

        _currentIsDuplicate = false;
        _attrs[_attrCount] = NewEntry(name, start, end, valueAt, hash);
        if (_indexActive || _attrCount + 1 >= IndexThreshold)
        {
            IndexAttribute(_attrCount, hash);
        }

        _attrCount++;
    }

    private static AttrEntry NewEntry(TextRef name, int start, int end, int valueAt, int hash) => new()
    {
        Name = name,
        NameStart = start,
        NameEnd = end,
        Quote = AttributeQuote.NoValue,
        ValueStart = valueAt,
        ValueEnd = valueAt,
        Value = new TextRef { Start = valueAt, Length = 0, SourceStart = valueAt, SourceEnd = valueAt },
        Hash = hash,
    };

    // Tags with many attributes use a small hash index so that finding a duplicate stays cheap.
    private const int IndexThreshold = 16;

    private int FindAttribute(ReadOnlySpan<char> name, int hash)
    {
        if (!_indexActive)
        {
            for (int i = 0; i < _attrCount; i++)
            {
                if (_attrs[i].Hash == hash && Resolve(_attrs[i].Name).SequenceEqual(name))
                {
                    return i;
                }
            }

            return -1;
        }

        int mask = _attrIndex!.Length - 1;
        for (int slot = hash & mask; ; slot = (slot + 1) & mask)
        {
            int entry = _attrIndex[slot];
            if (entry == 0)
            {
                return -1;
            }

            ref AttrEntry candidate = ref _attrs[entry - 1];
            if (candidate.Hash == hash && Resolve(candidate.Name).SequenceEqual(name))
            {
                return entry - 1;
            }
        }
    }

    private void IndexAttribute(int index, int hash)
    {
        if (!_indexActive)
        {
            // Sized for the attribute limit so that it never needs to grow: a power of two at least twice the limit.
            if (_attrIndex is null)
            {
                int size = 64;
                while (size < _limits.MaxAttributesPerTag * 2L && size < (1 << 24))
                {
                    size <<= 1;
                }

                _attrIndex = new int[size];
            }

            _indexActive = true;
            for (int i = 0; i < index; i++)
            {
                Insert(i, _attrs[i].Hash);
            }
        }

        Insert(index, hash);
    }

    private void Insert(int index, int hash)
    {
        int mask = _attrIndex!.Length - 1;
        int slot = hash & mask;
        while (_attrIndex[slot] != 0)
        {
            slot = (slot + 1) & mask;
        }

        _attrIndex[slot] = index + 1;
    }

    private static int HashName(ReadOnlySpan<char> name)
    {
        uint h = 2166136261;
        foreach (char c in name)
        {
            h = (h ^ c) * 16777619;
        }

        return (int)h;
    }

    // Clears the hash index when a new tag starts, if the previous tag used it.
    private void ResetAttributeIndex()
    {
        if (_indexActive)
        {
            Array.Clear(_attrIndex!);
            _indexActive = false;
        }
    }

    // Reads a quoted value starting at p (just after the opening quote). On success p is after the closing quote.
    private bool ParseQuotedValue(ref int p, char quote)
    {
        string t = _text;
        int len = t.Length;
        CurrentAttribute().Quote = quote == '"' ? AttributeQuote.DoubleQuoted : AttributeQuote.SingleQuoted;
        int valueStart = p;
        int bufStart = -1;
        int segment = p;
        SearchValues<char> stops = quote == '"' ? s_doubleQuoteStops : s_singleQuoteStops;
        while (true)
        {
            int i = t.AsSpan(p).IndexOfAny(stops);
            if (i < 0)
            {
                p = len;
                return false;
            }

            p += i;
            char c = t[p];
            if (c == quote)
            {
                int valueEnd = p;
                ref AttrEntry attr = ref CurrentAttribute();
                attr.ValueStart = valueStart;
                attr.ValueEnd = valueEnd;
                if (bufStart < 0)
                {
                    attr.Value = new TextRef { Start = valueStart, Length = valueEnd - valueStart, SourceStart = valueStart, SourceEnd = valueEnd };
                }
                else
                {
                    AppendBuffer(t.AsSpan(segment, valueEnd - segment));
                    attr.Value = new TextRef { Start = bufStart, Length = _bufLen - bufStart, InBuffer = true, SourceStart = valueStart, SourceEnd = valueEnd };
                }

                p++;
                return true;
            }

            // Start copying into the buffer at the first character that is not taken as written.
            if (bufStart < 0)
            {
                bufStart = _bufLen;
            }

            AppendBuffer(t.AsSpan(segment, p - segment));
            if (c == '\0')
            {
                ReportError(HtmlParseError.UnexpectedNullCharacter, p);
                AppendBuffer('\uFFFD');
                p++;
            }
            else
            {
                int n = ParseCharacterReference(p, inAttribute: true, out int end);
                if (n == 0)
                {
                    AppendBuffer('&');
                }
                else
                {
                    AppendBuffer(_reference.AsSpan(0, n));
                }

                p = end;
            }

            segment = p;
        }
    }

    // Reads an unquoted value starting at p. On success p is at the character that ended it (whitespace or ">").
    private bool ParseUnquotedValue(ref int p)
    {
        string t = _text;
        int len = t.Length;
        CurrentAttribute().Quote = AttributeQuote.Unquoted;
        int valueStart = p;
        int bufStart = -1;
        int segment = p;
        SearchValues<char> stops = _errors is null ? s_unquotedStops : s_unquotedStopsErrors;
        while (true)
        {
            int i = t.AsSpan(p).IndexOfAny(stops);
            if (i < 0)
            {
                p = len;
                return false;
            }

            p += i;
            char c = t[p];
            if (IsTagWhitespace(c) || c == '>')
            {
                int valueEnd = p;
                ref AttrEntry attr = ref CurrentAttribute();
                attr.ValueStart = valueStart;
                attr.ValueEnd = valueEnd;
                if (bufStart < 0)
                {
                    attr.Value = new TextRef { Start = valueStart, Length = valueEnd - valueStart, SourceStart = valueStart, SourceEnd = valueEnd };
                }
                else
                {
                    AppendBuffer(t.AsSpan(segment, valueEnd - segment));
                    attr.Value = new TextRef { Start = bufStart, Length = _bufLen - bufStart, InBuffer = true, SourceStart = valueStart, SourceEnd = valueEnd };
                }

                return true;
            }

            if (c == '&' || c == '\0')
            {
                if (bufStart < 0)
                {
                    bufStart = _bufLen;
                }

                AppendBuffer(t.AsSpan(segment, p - segment));
                if (c == '\0')
                {
                    ReportError(HtmlParseError.UnexpectedNullCharacter, p);
                    AppendBuffer('\uFFFD');
                    p++;
                }
                else
                {
                    int n = ParseCharacterReference(p, inAttribute: true, out int end);
                    if (n == 0)
                    {
                        AppendBuffer('&');
                    }
                    else
                    {
                        AppendBuffer(_reference.AsSpan(0, n));
                    }

                    p = end;
                }

                segment = p;
                continue;
            }

            // One of " ' < = ` : an error, and part of the value (only found when errors are recorded).
            ReportError(HtmlParseError.UnexpectedCharacterInUnquotedAttributeValue, p);
            p++;
        }
    }
}

/// <summary>Where an attribute sits in the source and what it says; a view of the tokenizer's current tag.</summary>
/// <remarks>Valid until the next call to <see cref="HtmlTokenizer.Next"/>.</remarks>
public readonly struct AttributeView
{
    private readonly HtmlTokenizer _tokenizer;
    private readonly int _index;
    private readonly bool _duplicate;

    internal AttributeView(HtmlTokenizer tokenizer, int index, bool duplicate)
    {
        _tokenizer = tokenizer;
        _index = index;
        _duplicate = duplicate;
    }

    private ref readonly HtmlTokenizer.AttrEntry Entry => ref _tokenizer.AttributeAt(_index, _duplicate);

    /// <summary>The name, lowercased (U+0000 becomes U+FFFD).</summary>
    public ReadOnlySpan<char> Name => _tokenizer.ResolveText(Entry.Name);

    /// <summary>The value with character references decoded; empty when there is none.</summary>
    public ReadOnlySpan<char> Value => _tokenizer.ResolveText(Entry.Value);

    /// <summary>How the value was written.</summary>
    public AttributeQuote Quote => Entry.Quote;

    /// <summary>True when the attribute has a value (even an empty one): <c>a=""</c> and <c>a=</c> have one, <c>a</c> has none.</summary>
    public bool HasValue => Entry.Quote != AttributeQuote.NoValue;

    /// <summary>The offset of the first character of the name as written.</summary>
    public int NameStart => Entry.NameStart;

    /// <summary>The offset after the last character of the name as written.</summary>
    public int NameEnd => Entry.NameEnd;

    /// <summary>The offset of the first character of the value as written, inside any quotes. Equals <see cref="NameEnd"/> with no value.</summary>
    public int ValueStart => Entry.ValueStart;

    /// <summary>The offset after the last character of the value as written, before any closing quote.</summary>
    public int ValueEnd => Entry.ValueEnd;

    /// <summary>The offset where the attribute starts: its name.</summary>
    public int Start => Entry.NameStart;

    /// <summary>The offset after the attribute: after the closing quote, the end of an unquoted value, or the end of the name.</summary>
    public int End => Entry.Quote switch
    {
        AttributeQuote.NoValue => Entry.NameEnd,
        AttributeQuote.Unquoted => Entry.ValueEnd,
        _ => Entry.ValueEnd + 1,
    };
}

/// <summary>The attributes of the tokenizer's current tag.</summary>
public readonly struct AttributeViewList
{
    private readonly HtmlTokenizer _tokenizer;
    private readonly bool _duplicates;

    internal AttributeViewList(HtmlTokenizer tokenizer, bool duplicates)
    {
        _tokenizer = tokenizer;
        _duplicates = duplicates;
    }

    /// <summary>The number of attributes.</summary>
    public int Count => _tokenizer.AttributeTotal(_duplicates);

    /// <summary>Gets an attribute by position in source order.</summary>
    /// <param name="index">From 0 to <see cref="Count"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public AttributeView this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new AttributeView(_tokenizer, index, _duplicates);
        }
    }

    /// <summary>Finds an attribute by exact (lowercase) name; returns false when there is none.</summary>
    /// <param name="name">The name to find.</param>
    /// <param name="attribute">The attribute found.</param>
    public bool TryGet(ReadOnlySpan<char> name, out AttributeView attribute)
    {
        for (int i = 0; i < Count; i++)
        {
            var candidate = new AttributeView(_tokenizer, i, _duplicates);
            if (candidate.Name.SequenceEqual(name))
            {
                attribute = candidate;
                return true;
            }
        }

        attribute = default;
        return false;
    }
}
