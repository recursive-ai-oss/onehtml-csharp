using System;
using System.Collections.Generic;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// Builds a <see cref="CssTree"/> following CSS Syntax §5. Block extents come from a bracket-matching pass over the
/// tokens, so no algorithm here recurses: a rule's block is parsed as a frame on an explicit stack, and skipping a
/// nested block or function is a jump to its matching closer.
/// </summary>
internal sealed class CssTreeBuilder
{
    private const byte ModeStylesheet = 0;
    private const byte ModeBlock = 1;
    private const byte ModeDeclarationList = 2;

    private readonly string _text;
    private readonly CssParseOptions _options;

    /// <summary>Set when bracket matching stopped at <see cref="CssParseOptions.MaxNestingDepth"/>.</summary>
    internal bool NestingLimitReached;

    private int _overflowAt = -1; // the first opener past the limit; its block is kept as opaque tokens
    private readonly CancellationToken _cancel;
    private Token[] _t = [];
    private int _n; // tokens produced from the text itself (excludes re-tokenized unicode-range values)
    private int _total; // _n plus re-tokenized tokens
    private int[] _match = [];
    private int[] _comments = [];
    private int _commentInts;
    private Item[] _items = new Item[16];
    private int _itemCount;
    private Frame[] _frames = new Frame[8];
    private int _sp;
    private int[] _matchStack = new int[64];

    private struct Frame
    {
        public int Parent;
        public int Pos;
        public int End;
        public int Last;
        public byte Mode;
    }

    private CssTreeBuilder(string text, CssParseOptions options, CancellationToken cancel)
    {
        _text = text;
        _options = options;
        _cancel = cancel;
    }

    public static CssTree Build(string text, CssEntry entry, CssParseOptions? options, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= CssParseOptions.Default;
        if (text.Length > options.MaxInputLength)
        {
            throw new CssLimitExceededException(CssLimit.InputLength, options.MaxInputLength);
        }

        var b = new CssTreeBuilder(text, options, cancel);
        return b.Run(entry);
    }

    private CssTree Run(CssEntry entry)
    {
        var tk = new CssTokenizer(_text, 0, _text.Length, _options.MaxTokens, false, true, null, 0, _cancel);
        tk.Run();
        _t = tk.Tokens;
        _n = _total = tk.Count;
        _comments = tk.Comments;
        _commentInts = tk.CommentCount;
        _match = new int[_n];
        ComputeMatch(0, _n);
        _items = new Item[(_n / 6) + 16];

        _items[0] = new Item { Kind = CssNodeKind.Root, Parent = -1, FirstChild = -1, NextSibling = -1, Start = 0, End = _text.Length, Name = -1, Colon = -1, Open = -1, Close = -1, Semi = -1 };
        _itemCount = 1;

        var error = CssParseError.None;
        int valuesFrom = 0;
        int valuesTo = 0;
        int[]? groups = null;
        switch (entry)
        {
            case CssEntry.Stylesheet:
                RunFrames(ModeStylesheet, _n);
                break;
            case CssEntry.DeclarationList:
                RunFrames(ModeDeclarationList, _n);
                break;
            case CssEntry.BlockContents:
                RunFrames(ModeBlock, StrayBraceIndex());
                break;
            case CssEntry.Rule:
                error = ParseRule();
                break;
            case CssEntry.Declaration:
                error = ParseDeclaration();
                break;
            case CssEntry.ComponentValue:
                error = ParseComponentValue(out valuesFrom, out valuesTo);
                break;
            case CssEntry.ComponentValueList:
                valuesTo = _n;
                break;
            case CssEntry.CommaSeparatedList:
                groups = SplitCommaGroups();
                break;
        }

        return new CssTree(_text, _t, _n, _match, _comments, _commentInts, _items, _itemCount, error, valuesFrom, valuesTo, groups)
        {
            NestingLimitReached = NestingLimitReached,
        };
    }

    // ---- bracket matching (explicit stack; also applies the nesting limit) ----

    private static bool IsOpener(CssTokenKind k) =>
        k is CssTokenKind.LeftBrace or CssTokenKind.LeftParen or CssTokenKind.LeftBracket or CssTokenKind.Function;

    private static bool Closes(CssTokenKind opener, CssTokenKind closer) => closer switch
    {
        CssTokenKind.RightBrace => opener == CssTokenKind.LeftBrace,
        CssTokenKind.RightBracket => opener == CssTokenKind.LeftBracket,
        CssTokenKind.RightParen => opener is CssTokenKind.LeftParen or CssTokenKind.Function,
        _ => false,
    };

    private void ComputeMatch(int from, int to)
    {
        if (_match.Length < to)
        {
            Array.Resize(ref _match, Math.Max(to, _match.Length * 2));
        }

        Array.Fill(_match, -1, from, to - from);
        int sp = 0;
        int max = _options.MaxNestingDepth;
        for (int i = from; i < to; i++)
        {
            CssTokenKind k = _t[i].Kind;
            if (IsOpener(k))
            {
                if (sp >= max)
                {
                    // CSS Syntax 5.5.9 and 5.5.10 close every block still open at the end of the input, so a block that
                    // never closes is ordinary input. Nesting past the limit is treated that way: this opener and
                    // everything after it stay unmatched, which the builders read as blocks running to the end of
                    // their range. The tree stays lossless and its depth stays bounded, and nothing throws.
                    NestingLimitReached = true;
                    _overflowAt = i;
                    break;
                }

                if (sp == _matchStack.Length)
                {
                    Array.Resize(ref _matchStack, sp * 2);
                }

                _matchStack[sp++] = i;
            }
            else if (k is CssTokenKind.RightBrace or CssTokenKind.RightBracket or CssTokenKind.RightParen)
            {
                // CSS Syntax §5.5.9, §5.5.10: a closer that is not the innermost block's ending token is
                // an ordinary token, so it matches nothing.
                if (sp > 0 && Closes(_t[_matchStack[sp - 1]].Kind, k))
                {
                    int o = _matchStack[--sp];
                    _match[o] = i;
                    _match[i] = o;
                }
            }
        }
    }

    // Index just after the component value that starts at i ("consume a component value", §5.5.8), within [i, end).
    private int SkipValue(int i, int end)
    {
        if (IsOpener(_t[i].Kind))
        {
            int m = _match[i];
            return m >= 0 ? m + 1 : end;
        }

        return i + 1;
    }

    private int StrayBraceIndex()
    {
        int i = 0;
        while (i < _n)
        {
            if (_t[i].Kind == CssTokenKind.RightBrace)
            {
                return i;
            }

            i = SkipValue(i, _n);
        }

        return _n;
    }

    // ---- item table ----

    private int NewItem(int fi, in Item item)
    {
        if (_itemCount == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        int idx = _itemCount++;
        _items[idx] = item;
        ref Frame f = ref _frames[fi];
        _items[idx].Parent = f.Parent;
        _items[idx].FirstChild = -1;
        _items[idx].NextSibling = -1;
        if (f.Last < 0)
        {
            _items[f.Parent].FirstChild = idx;
        }
        else
        {
            _items[f.Last].NextSibling = idx;
        }

        f.Last = idx;
        return idx;
    }

    private void PushFrame(int parent, int pos, int end, byte mode)
    {
        if (_sp == _frames.Length)
        {
            Array.Resize(ref _frames, _sp * 2);
        }

        _frames[_sp++] = new Frame { Parent = parent, Pos = pos, End = end, Last = -1, Mode = mode };
    }

    private void RunFrames(byte rootMode, int rootEnd)
    {
        _sp = 0;
        PushFrame(0, 0, rootEnd, rootMode);
        RunUntil(0);
    }

    private void RunUntil(int depth)
    {
        int steps = 0;
        while (_sp > depth)
        {
            if ((++steps & 0xFFF) == 0)
            {
                _cancel.ThrowIfCancellationRequested();
            }

            int fi = _sp - 1;
            if (_frames[fi].Pos >= _frames[fi].End)
            {
                _sp--;
                continue;
            }

            Step(fi);
        }
    }

    private void Step(int fi)
    {
        int pos = _frames[fi].Pos;
        CssTokenKind k = _t[pos].Kind;
        switch (_frames[fi].Mode)
        {
            case ModeStylesheet:
                // §5.5.1 "consume a stylesheet's contents"
                if (k is CssTokenKind.Whitespace or CssTokenKind.Cdo or CssTokenKind.Cdc)
                {
                    _frames[fi].Pos = pos + 1;
                }
                else if (k == CssTokenKind.AtKeyword)
                {
                    ConsumeAtRule(fi, nested: false);
                }
                else
                {
                    ConsumeQualifiedRule(fi, nested: false, stopAtSemicolon: false);
                }

                break;
            case ModeBlock:
                // §5.5.5 "consume a block's contents"
                if (k is CssTokenKind.Whitespace or CssTokenKind.Semicolon)
                {
                    _frames[fi].Pos = pos + 1;
                }
                else if (k == CssTokenKind.AtKeyword)
                {
                    ConsumeAtRule(fi, nested: true);
                }
                else if (TryDeclaration(pos, _frames[fi].End, out Item decl, out int after))
                {
                    NewItem(fi, decl);
                    _frames[fi].Pos = after;
                }
                else
                {
                    ConsumeQualifiedRule(fi, nested: true, stopAtSemicolon: true);
                }

                break;
            default:
                // §5.4.5 "parse a list of declarations"
                if (k is CssTokenKind.Whitespace or CssTokenKind.Semicolon)
                {
                    _frames[fi].Pos = pos + 1;
                }
                else if (k == CssTokenKind.AtKeyword)
                {
                    ConsumeAtRule(fi, nested: false);
                }
                else if (TryDeclaration(pos, _frames[fi].End, out Item decl2, out int after2))
                {
                    NewItem(fi, decl2);
                    _frames[fi].Pos = after2;
                }
                else
                {
                    int e = SkipBadDeclaration(pos, _frames[fi].End, nested: false);
                    AddInvalid(fi, pos, e);
                    _frames[fi].Pos = e;
                }

                break;
        }
    }

    private void AddInvalid(int fi, int fromTok, int toTok)
    {
        int last = Math.Max(fromTok, toTok - 1);
        NewItem(fi, new Item { Kind = CssNodeKind.Invalid, Start = _t[fromTok].Start, End = _t[last].End, Name = -1, Colon = -1, V0 = fromTok, V1 = toTok, Open = -1, Close = -1, Semi = -1 });
    }

    // §5.5.6 "consume the remnants of a bad declaration": returns the index after what it consumes.
    private int SkipBadDeclaration(int i, int end, bool nested)
    {
        while (i < end)
        {
            CssTokenKind k = _t[i].Kind;
            if (k == CssTokenKind.Semicolon)
            {
                return i + 1;
            }

            if (k == CssTokenKind.RightBrace && nested)
            {
                return i;
            }

            i = SkipValue(i, end);
        }

        return end;
    }

    // ---- at-rules and qualified rules ----

    // §5.5.2 "consume an at-rule"
    private void ConsumeAtRule(int fi, bool nested)
    {
        int pos = _frames[fi].Pos;
        int end = _frames[fi].End;
        int i = pos + 1;
        var item = new Item { Kind = CssNodeKind.AtRule, Start = _t[pos].Start, Name = pos, Colon = -1, V0 = i, Open = -1, Close = -1, Semi = -1 };
        while (true)
        {
            if (i >= end)
            {
                item.V1 = i;
                item.End = _t[i - 1].End;
                NewItem(fi, item);
                _frames[fi].Pos = i;
                return;
            }

            CssTokenKind k = _t[i].Kind;
            if (k == CssTokenKind.Semicolon)
            {
                item.V1 = i;
                item.Semi = i;
                item.End = _t[i].End;
                NewItem(fi, item);
                _frames[fi].Pos = i + 1;
                return;
            }

            if (k == CssTokenKind.RightBrace && nested)
            {
                item.V1 = i;
                item.End = _t[i - 1].End;
                NewItem(fi, item);
                _frames[fi].Pos = i;
                return;
            }

            if (k == CssTokenKind.LeftBrace)
            {
                item.V1 = i;
                FinishBlockRule(fi, ref item, i);
                return;
            }

            i = SkipValue(i, end);
        }
    }

    // Records a rule whose block opens at token 'open', moves the parent frame past the block and queues the block's
    // contents for parsing (§5.5.4 "consume a block").
    private void FinishBlockRule(int fi, ref Item item, int open)
    {
        int close = _match[open];
        item.Open = open;
        item.Close = close;
        item.Flags |= Item.HasBlock;
        if (close >= 0)
        {
            item.Flags |= Item.BlockClosed;
            item.End = _t[close].End;
        }
        else
        {
            item.End = _text.Length;
        }

        int idx = NewItem(fi, item);
        _frames[fi].Pos = close >= 0 ? close + 1 : _frames[fi].End;

        // The block past the nesting limit stays unparsed: what follows it was never bracket-matched, so it may hold
        // closers that match nothing.
        if (open != _overflowAt)
        {
            PushFrame(idx, open + 1, close >= 0 ? close : _n, ModeBlock);
        }
    }

    // §5.5.3 "consume a qualified rule"
    private void ConsumeQualifiedRule(int fi, bool nested, bool stopAtSemicolon)
    {
        int start = _frames[fi].Pos;
        int end = _frames[fi].End;
        int i = start;
        while (true)
        {
            if (i >= end)
            {
                AddInvalid(fi, start, i);
                _frames[fi].Pos = i;
                return;
            }

            CssTokenKind k = _t[i].Kind;
            if (k == CssTokenKind.Semicolon && stopAtSemicolon)
            {
                AddInvalid(fi, start, i);
                _frames[fi].Pos = i;
                return;
            }

            if (k == CssTokenKind.RightBrace)
            {
                if (nested)
                {
                    AddInvalid(fi, start, i);
                    _frames[fi].Pos = i;
                    return;
                }

                i++;
                continue;
            }

            if (k == CssTokenKind.LeftBrace)
            {
                if (PreludeLooksLikeCustomProperty(start, i))
                {
                    // The prelude starts "--name:", which would have been a custom property. Declarations and rules
                    // must not overlap, so this is dropped (nested: with the rest of the would-be declaration).
                    int after = nested ? SkipBadDeclaration(i, end, nested: true) : SkipValue(i, end);
                    AddInvalid(fi, start, after);
                    _frames[fi].Pos = after;
                    return;
                }

                var item = new Item { Kind = CssNodeKind.QualifiedRule, Start = _t[start].Start, Name = -1, Colon = -1, V0 = start, V1 = i, Semi = -1 };
                FinishBlockRule(fi, ref item, i);
                return;
            }

            i = SkipValue(i, end);
        }
    }

    // First two non-whitespace prelude values are an ident starting "--" and a colon.
    private bool PreludeLooksLikeCustomProperty(int from, int to)
    {
        int i = from;
        while (i < to && _t[i].Kind == CssTokenKind.Whitespace)
        {
            i++;
        }

        if (i >= to || _t[i].Kind != CssTokenKind.Ident || !IsCustomName(i))
        {
            return false;
        }

        i++;
        while (i < to && _t[i].Kind == CssTokenKind.Whitespace)
        {
            i++;
        }

        return i < to && _t[i].Kind == CssTokenKind.Colon;
    }

    private bool IsCustomName(int tok)
    {
        ref readonly Token t = ref _t[tok];
        if ((t.Flags & Token.HasEscape) == 0)
        {
            return t.End - t.Start >= 2 && _text[t.Start] == '-' && _text[t.Start + 1] == '-';
        }

        return CssChars.Decode(_text.AsSpan(t.Start, t.End - t.Start)).StartsWith("--", StringComparison.Ordinal);
    }

    private bool IsImportantIdent(int tok)
    {
        ref readonly Token t = ref _t[tok];
        if ((t.Flags & Token.HasEscape) == 0)
        {
            return _text.AsSpan(t.Start, t.End - t.Start).Equals("important", StringComparison.OrdinalIgnoreCase);
        }

        return CssChars.Decode(_text.AsSpan(t.Start, t.End - t.Start)).Equals("important", StringComparison.OrdinalIgnoreCase);
    }

    // ---- declarations ----

    // §5.5.6 "consume a declaration". Pure: it only reads, so a failure leaves nothing to undo (the "mark / restore").
    private bool TryDeclaration(int pos, int end, out Item item, out int newPos)
    {
        item = default;
        newPos = pos;
        if (_t[pos].Kind != CssTokenKind.Ident)
        {
            return false;
        }

        int i = pos + 1;
        while (i < end && _t[i].Kind == CssTokenKind.Whitespace)
        {
            i++;
        }

        if (i >= end || _t[i].Kind != CssTokenKind.Colon)
        {
            return false;
        }

        int colon = i++;
        while (i < end && _t[i].Kind == CssTokenKind.Whitespace)
        {
            i++;
        }

        int v0 = i;
        bool custom = IsCustomName(pos);
        int nonWs = 0;
        int last1 = -1;
        int last2 = -1;
        bool braceOnly = false;
        while (i < end)
        {
            CssTokenKind k = _t[i].Kind;
            if (k == CssTokenKind.Semicolon)
            {
                break;
            }

            int vstart = i;
            i = SkipValue(i, end);
            if (k == CssTokenKind.Whitespace)
            {
                continue;
            }

            if (!custom)
            {
                // "A top-level {}-block is only allowed as the entire value of a non-custom property" (§5.5.6).
                // Failing as soon as that is certain keeps declaration attempts on rules linear.
                if (k == CssTokenKind.LeftBrace)
                {
                    if (nonWs > 0)
                    {
                        return false;
                    }

                    braceOnly = true;
                }
                else if (braceOnly)
                {
                    if (nonWs == 1)
                    {
                        if (!(k == CssTokenKind.Delim && _text[_t[vstart].Start] == '!'))
                        {
                            return false;
                        }
                    }
                    else if (nonWs == 2)
                    {
                        if (!(k == CssTokenKind.Ident && IsImportantIdent(vstart)))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            nonWs++;
            last2 = last1;
            last1 = vstart;
        }

        int stop = i;
        int v1 = stop;
        int bang = -1;
        int imp = -1;
        byte flags = custom ? Item.CustomProperty : (byte)0;
        if (last2 >= 0 && _t[last1].Kind == CssTokenKind.Ident && _t[last2].Kind == CssTokenKind.Delim
            && _text[_t[last2].Start] == '!' && IsImportantIdent(last1))
        {
            v1 = last2;
            bang = last2;
            imp = last1;
            flags |= Item.Important;
        }

        while (v1 > v0 && _t[v1 - 1].Kind == CssTokenKind.Whitespace)
        {
            v1--;
        }

        int semi = -1;
        if (stop < end && _t[stop].Kind == CssTokenKind.Semicolon)
        {
            semi = stop;
            newPos = stop + 1;
        }
        else
        {
            newPos = stop;
        }

        int endOffset = semi >= 0 ? _t[semi].End : imp >= 0 ? _t[imp].End : v1 > v0 ? _t[v1 - 1].End : _t[colon].End;
        item = new Item { Kind = CssNodeKind.Declaration, Flags = flags, Start = _t[pos].Start, End = endOffset, Name = pos, Colon = colon, V0 = v0, V1 = v1, Open = bang, Close = imp, Semi = semi };

        if (!custom && v1 > v0 && IsAsciiIgnoreCase(_t[pos], "unicode-range"))
        {
            RetokenizeUnicodeRange(ref item);
        }

        return true;
    }

    private bool IsAsciiIgnoreCase(in Token t, string value)
    {
        if ((t.Flags & Token.HasEscape) == 0)
        {
            return _text.AsSpan(t.Start, t.End - t.Start).Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        return CssChars.Decode(_text.AsSpan(t.Start, t.End - t.Start)).Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    // §5.5.6 + §5.5.11: the value of a unicode-range descriptor is tokenized again with unicode ranges allowed, from
    // the source text of the original value tokens. The new tokens live after the ordinary ones.
    private void RetokenizeUnicodeRange(ref Item item)
    {
        int from = _t[item.V0].Start;
        int to = _t[item.V1 - 1].End;
        var tk = new CssTokenizer(_text, from, to, _options.MaxTokens, true, false, _t, _total, _cancel);
        tk.Run();
        _t = tk.Tokens;
        int first = _total;
        _total = tk.Count;
        ComputeMatch(first, _total);
        item.V0 = first;
        item.V1 = _total;
    }

    // ---- single-construct entry points ----

    private int SkipWhitespace(int i, int end)
    {
        while (i < end && _t[i].Kind == CssTokenKind.Whitespace)
        {
            i++;
        }

        return i;
    }

    // §5.4.6 "parse a rule"
    private CssParseError ParseRule()
    {
        _sp = 0;
        PushFrame(0, SkipWhitespace(0, _n), _n, ModeStylesheet);
        int pos = _frames[0].Pos;
        if (pos >= _n)
        {
            return CssParseError.Empty;
        }

        if (_t[pos].Kind == CssTokenKind.AtKeyword)
        {
            ConsumeAtRule(0, nested: false);
        }
        else
        {
            ConsumeQualifiedRule(0, nested: false, stopAtSemicolon: false);
        }

        RunUntil(1);
        if (_items[_items[0].FirstChild].Kind == CssNodeKind.Invalid)
        {
            return CssParseError.Invalid;
        }

        return SkipWhitespace(_frames[0].Pos, _n) < _n ? CssParseError.ExtraInput : CssParseError.None;
    }

    // §5.4.7 "parse a declaration"
    private CssParseError ParseDeclaration()
    {
        _sp = 0;
        PushFrame(0, 0, _n, ModeDeclarationList);
        int pos = SkipWhitespace(0, _n);
        if (pos >= _n)
        {
            return CssParseError.Empty;
        }

        if (!TryDeclaration(pos, _n, out Item decl, out int after))
        {
            AddInvalid(0, pos, SkipBadDeclaration(pos, _n, nested: false));
            return CssParseError.Invalid;
        }

        NewItem(0, decl);
        return SkipWhitespace(after, _n) < _n ? CssParseError.ExtraInput : CssParseError.None;
    }

    // §5.4.8 "parse a component value"
    private CssParseError ParseComponentValue(out int from, out int to)
    {
        from = SkipWhitespace(0, _n);
        to = from;
        if (from >= _n)
        {
            return CssParseError.Empty;
        }

        to = SkipValue(from, _n);
        return SkipWhitespace(to, _n) < _n ? CssParseError.ExtraInput : CssParseError.None;
    }

    // §5.4.10 "parse a comma-separated list of component values"
    private int[] SplitCommaGroups()
    {
        var bounds = new List<int>();
        int i = 0;
        while (i < _n)
        {
            int start = i;
            while (i < _n && _t[i].Kind != CssTokenKind.Comma)
            {
                i = SkipValue(i, _n);
            }

            bounds.Add(start);
            bounds.Add(i);
            if (i < _n)
            {
                i++; // discard the comma
            }
        }

        return bounds.ToArray();
    }
}
