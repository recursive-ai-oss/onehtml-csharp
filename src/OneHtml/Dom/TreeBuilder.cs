using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Dom;

/// <summary>
/// The HTML tree construction stage (HTML 13.2.6): consumes the tokenizer's tokens and writes nodes straight into
/// an <see cref="Arena"/>. This file holds the state and the machinery the insertion modes share.
/// </summary>
internal sealed partial class TreeBuilder
{
    private enum Mode : byte
    {
        Initial,
        BeforeHtml,
        BeforeHead,
        InHead,
        InHeadNoscript,
        AfterHead,
        InBody,
        Text,
        InTable,
        InTableText,
        InCaption,
        InColumnGroup,
        InTableBody,
        InRow,
        InCell,
        InTemplate,
        AfterBody,
        InFrameset,
        AfterFrameset,
        AfterAfterBody,
        AfterAfterFrameset,
    }

    private enum Scope : byte
    {
        Default,
        ListItem,
        Button,
        Table,
    }

    private enum CharClass : byte
    {
        Whitespace,
        Null,
        Other,
    }

    private enum Adjust : byte
    {
        None,
        MathMl,
        Svg,
    }

    /// <summary>The token being processed, with the changes the rules make to it (an "image" start tag becomes "img").</summary>
    private struct Tok
    {
        public HtmlTokenKind Kind;
        public int Name;
        public bool SelfClosing;

        /// <summary>The attributes of the tokenizer's current token do not belong to this token (it is synthesized or had them dropped).</summary>
        public bool NoAttrs;

        /// <summary>No start tag in the source made this token.</summary>
        public bool Synth;
        public int Start;
        public int End;
    }

    /// <summary>A run of characters of one class, with its source range.</summary>
    private ref struct Chars
    {
        public ReadOnlySpan<char> S;
        public int Start;
        public int End;
        public bool Exact;
        public CharClass Class;
    }

    private const byte Html = (byte)ElementNamespace.Html;
    private const byte MathMl = (byte)ElementNamespace.MathMl;
    private const byte Svg = (byte)ElementNamespace.Svg;

    private SourceText _source;
    private readonly string _text;
    private readonly ParseOptions _options;
    private readonly ParseLimits _limits;
    private readonly HtmlTokenizer _tz;
    private readonly Arena _a;
    private readonly NameTable _names = new();
    private readonly bool _fragment;
    private readonly bool _scripting;

    // Fragment context (HTML 13.4).
    private readonly byte _ctxNs;
    private readonly int _ctxName;
    private readonly bool _ctxIntegrationPoint;
    private readonly bool _ctxInForm;
    private int _rootHtml = -1;

    // The stack of open elements. _ttPrev[i] is the node of the nearest table or template below stack[i], so the
    // "last table or template" of foster parenting is found without a scan.
    private int[] _stack = new int[64];
    private int[] _ttPrev = new int[64];
    private int _sp;
    private int _lastTt = -1;
    private readonly int[] _cnt = new int[N.Count];

    // The list of active formatting elements; -1 is a marker.
    private int[] _afe = new int[16];
    private uint[] _afeHash = new uint[16];
    private int _afeCount;

    private Mode _mode;
    private Mode _origMode;
    private Mode[] _templateModes = new Mode[8];
    private int _templateModeCount;
    private int _head = -1;

    // -1 null, -2 a form that is an ancestor of the fragment context, otherwise a node.
    private int _form = -1;
    private bool _framesetOk = true;
    private bool _foster;
    private QuirksMode _quirks;
    private int _doctype = -1;
    private string _publicId = string.Empty;
    private string _systemId = string.Empty;
    private bool _stop;
    private SourceText? _restart;
    private bool _skipLf;

    // The pending table character tokens of "in table text".
    private char[] _pendChars = new char[64];
    private int _pendLen;
    private int _pendSegs;
    private int[] _pendInfo = new int[16];
    private bool _pendNonWhitespace;

    // Facts about the token being processed.
    private int _tokStart;
    private int _tokEnd;
    private bool _tokIsEnd;
    private CancellationToken _ct;

    public TreeBuilder(SourceText source, ParseOptions options, FragmentContext? context)
    {
        _source = source;
        _text = source.Text;
        _options = options;
        _limits = options.Limits ?? source.Limits;
        _scripting = options.ScriptingEnabled;
        int estimate = Math.Clamp(_text.Length / 16, 16, 1 << 20);
        _a = new Arena(estimate, estimate / 2, _limits.MaxNodes, _limits.MaxTotalAttributes);
        _tz = new HtmlTokenizer(source, new HtmlTokenizerOptions { RecordErrors = options.RecordErrors, Limits = _limits });
        if (context is not null)
        {
            _fragment = true;
            _ctxNs = (byte)context.Namespace;
            _ctxName = _names.Intern(context.LocalName);
            _ctxIntegrationPoint = context.IsHtmlIntegrationPoint;
            _ctxInForm = context.InsideForm;
            _quirks = context.QuirksMode;
        }
    }

    /// <summary>
    /// Runs the parse. Returns the document, or null with <paramref name="restart"/> set to the text to parse again
    /// when a <c>&lt;meta&gt;</c> changed the encoding.
    /// </summary>
    public Document? Run(CancellationToken cancellationToken, out SourceText? restart)
    {
        _ct = cancellationToken;
        restart = null;
        Init();
        while (!_stop)
        {
            _tz.InForeignContent = AdjustedIsForeign();
            HtmlTokenKind kind = _tz.Next(cancellationToken);
            bool skipLf = _skipLf;
            _skipLf = false;
            _tokStart = _tz.Start;
            _tokEnd = _tz.End;
            _tokIsEnd = false;
            var t = new Tok { Kind = kind, Start = _tokStart, End = _tokEnd };
            switch (kind)
            {
                case HtmlTokenKind.Character:
                    HandleCharacterToken(skipLf);
                    break;
                case HtmlTokenKind.StartTag:
                    t.Name = _names.Intern(_tz.Name);
                    t.SelfClosing = _tz.SelfClosing;
                    Dispatch(ref t);
                    break;
                case HtmlTokenKind.EndTag:
                    t.Name = _names.Intern(_tz.Name);
                    _tokIsEnd = true;
                    Dispatch(ref t);
                    break;
                case HtmlTokenKind.EndOfFile:
                    t.Start = t.End = _tokStart = _tokEnd = _text.Length;
                    Dispatch(ref t);
                    _stop = true;
                    break;
                default:
                    Dispatch(ref t);
                    break;
            }

            if (_restart is not null)
            {
                restart = _restart;
                return null;
            }
        }

        return Finish();
    }

    private void Init()
    {
        _a.AddNode(_fragment ? NodeKind.DocumentFragment : NodeKind.Document, Html, -1);
        if (!_fragment)
        {
            _mode = Mode.Initial;
            return;
        }

        // HTML 13.4: a root html element holds the parsed nodes; they are redirected to the fragment.
        _rootHtml = _a.AddNode(NodeKind.Element, Html, N.Html);
        Push(_rootHtml);
        if (_ctxNs == Html && _ctxName == N.Template)
        {
            PushTemplateMode(Mode.InTemplate);
        }

        ResetInsertionMode();
        _form = _ctxInForm ? -2 : -1;
        if (_ctxNs == Html)
        {
            switch (_ctxName)
            {
                case N.Title or N.Textarea:
                    _tz.SetState(TokenizerState.Rcdata);
                    break;
                case N.Style or N.Xmp or N.Iframe or N.Noembed or N.Noframes:
                    _tz.SetState(TokenizerState.Rawtext);
                    break;
                case N.Script:
                    _tz.SetState(TokenizerState.ScriptData);
                    break;
                case N.Noscript when _scripting:
                    _tz.SetState(TokenizerState.Rawtext);
                    break;
                case N.Plaintext:
                    _tz.SetState(TokenizerState.Plaintext);
                    break;
                default:
                    break;
            }
        }
    }

    private Document Finish()
    {
        while (_sp > 0)
        {
            Pop();
        }

        for (int i = 0; i < _afeCount; i++)
        {
            if (_afe[i] >= 0)
            {
                _a.Flags[_afe[i]] &= unchecked((ushort)~F.InAfe);
            }
        }

        int documentElement = -1;
        if (!_fragment)
        {
            for (int c = _a.First[0]; c >= 0; c = _a.Next[c])
            {
                if (_a.Kind[c] == (byte)NodeKind.Element)
                {
                    documentElement = c;
                    break;
                }
            }
        }

        IReadOnlyList<ParseError> errors = _tz.Errors is { } list ? list.ToArray() : [];
        return new Document(_a, _names, _source, _quirks, _fragment, documentElement, _doctype, _publicId, _systemId, errors)
        {
            ScriptingEnabled = _scripting,
        };
    }

    // ---- The tree construction dispatcher (HTML 13.2.6) ----

    private void Dispatch(ref Tok t)
    {
        while (UseHtmlRules(t.Kind == HtmlTokenKind.StartTag, t.Kind == HtmlTokenKind.EndOfFile, t.Name) ? ProcessInMode(_mode, ref t) : ProcessForeign(ref t))
        {
            // The rules asked to reprocess the token; the mode or the stack changed, so the choice is made again.
        }
    }

    private void DispatchChars(ref Chars c)
    {
        while (UseHtmlRulesForCharacter() ? CharsInMode(_mode, ref c) : ForeignChars(ref c))
        {
            // Reprocess.
        }
    }

    private bool UseHtmlRules(bool startTag, bool eof, int name)
    {
        if (_sp == 0 || eof)
        {
            return true;
        }

        GetAdjusted(out byte ns, out int adjName, out bool integrationPoint);
        if (ns == Html)
        {
            return true;
        }

        if (ns == MathMl && adjName is N.Mi or N.Mo or N.Mn or N.Ms or N.Mtext)
        {
            if (startTag && name is not (N.Mglyph or N.Malignmark))
            {
                return true;
            }
        }

        if (ns == MathMl && adjName == N.AnnotationXml && startTag && name == N.Svg)
        {
            return true;
        }

        return integrationPoint && startTag;
    }

    private bool UseHtmlRulesForCharacter()
    {
        if (_sp == 0)
        {
            return true;
        }

        GetAdjusted(out byte ns, out int adjName, out bool integrationPoint);
        return ns == Html || (ns == MathMl && adjName is N.Mi or N.Mo or N.Mn or N.Ms or N.Mtext) || integrationPoint;
    }

    /// <summary>
    /// The adjusted current node (HTML 13.2.4.2): the fragment context element when the stack holds only the root,
    /// otherwise the current node. <paramref name="integrationPoint"/> is true for an HTML integration point.
    /// </summary>
    private void GetAdjusted(out byte ns, out int name, out bool integrationPoint)
    {
        if (_fragment && _sp == 1)
        {
            ns = _ctxNs;
            name = _ctxName;
            integrationPoint = _ctxIntegrationPoint || IsSvgIntegrationName(ns, name);
            return;
        }

        int node = _stack[_sp - 1];
        ns = _a.Ns[node];
        name = _a.Name[node];
        integrationPoint = (_a.Flags[node] & F.IntegrationPoint) != 0 || IsSvgIntegrationName(ns, name);
    }

    private static bool IsSvgIntegrationName(byte ns, int name) => ns == Svg && name is N.CcForeignObject or N.Desc or N.Title;

    private bool AdjustedIsForeign()
    {
        if (_sp == 0)
        {
            return false;
        }

        GetAdjusted(out byte ns, out _, out _);
        return ns != Html;
    }

    // ---- Characters ----

    private void HandleCharacterToken(bool skipLf)
    {
        ReadOnlySpan<char> data = _tz.Data;
        int start = _tz.Start;
        int end = _tz.End;
        bool exact = data.Length == end - start && (data.Length != 1 || data[0] == _text[start]);
        if (skipLf && data.Length > 0 && data[0] == '\n')
        {
            data = data[1..];
            if (exact)
            {
                start++;
            }
        }

        int i = 0;
        while (i < data.Length)
        {
            CharClass cls = Classify(data[i]);
            int j = i + 1;
            while (j < data.Length && Classify(data[j]) == cls)
            {
                j++;
            }

            var chars = new Chars
            {
                S = data[i..j],
                Start = exact ? start + i : start,
                End = exact ? start + j : end,
                Exact = exact,
                Class = cls,
            };
            _tokStart = chars.Start;
            DispatchChars(ref chars);
            if (_stop || _restart is not null)
            {
                return;
            }

            i = j;
        }
    }

    private static CharClass Classify(char c) => c switch
    {
        '\t' or '\n' or '\f' or '\r' or ' ' => CharClass.Whitespace,
        '\0' => CharClass.Null,
        _ => CharClass.Other,
    };

    // ---- Stack of open elements ----

    private int Cur => _stack[_sp - 1];

    private bool IsHtmlNamed(int node, int name) => _a.Ns[node] == Html && _a.Name[node] == name;

    private bool CurIs(int name) => _sp > 0 && IsHtmlNamed(_stack[_sp - 1], name);

    private void Push(int node)
    {
        if (_sp >= _limits.MaxOpenElements)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxOpenElements), _limits.MaxOpenElements, (long)_sp + 1);
        }

        if (_sp == _stack.Length)
        {
            Array.Resize(ref _stack, _sp * 2);
            Array.Resize(ref _ttPrev, _sp * 2);
        }

        _stack[_sp] = node;
        if (_a.Ns[node] == Html && _a.Name[node] is N.Table or N.Template)
        {
            _ttPrev[_sp] = _lastTt;
            _lastTt = node;
        }
        else
        {
            _ttPrev[_sp] = -1;
        }

        _sp++;
        _a.Flags[node] |= F.InStack;
        CountChange(node, 1);
    }

    private int Pop()
    {
        _sp--;
        int node = _stack[_sp];
        _a.Flags[node] &= unchecked((ushort)~F.InStack);
        CountChange(node, -1);
        if (node == _lastTt)
        {
            _lastTt = _ttPrev[_sp];
        }

        if (_a.Name[node] == N.Option && _a.Ns[node] == Html)
        {
            OnOptionPopped(node);
        }

        return node;
    }

    private void CountChange(int node, int delta)
    {
        if (_a.Ns[node] == Html && (uint)_a.Name[node] < (uint)_cnt.Length)
        {
            _cnt[_a.Name[node]] += delta;
        }
    }

    private void RemoveStackAt(int index)
    {
        int node = _stack[index];
        _a.Flags[node] &= unchecked((ushort)~F.InStack);
        CountChange(node, -1);
        Array.Copy(_stack, index + 1, _stack, index, _sp - index - 1);
        Array.Copy(_ttPrev, index + 1, _ttPrev, index, _sp - index - 1);
        _sp--;
    }

    private void InsertStackAt(int index, int node)
    {
        if (_sp >= _limits.MaxOpenElements)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxOpenElements), _limits.MaxOpenElements, (long)_sp + 1);
        }

        if (_sp == _stack.Length)
        {
            Array.Resize(ref _stack, _sp * 2);
            Array.Resize(ref _ttPrev, _sp * 2);
        }

        Array.Copy(_stack, index, _stack, index + 1, _sp - index);
        Array.Copy(_ttPrev, index, _ttPrev, index + 1, _sp - index);
        _stack[index] = node;
        _ttPrev[index] = -1;
        _sp++;
        _a.Flags[node] |= F.InStack;
        CountChange(node, 1);
    }

    private int StackIndexOf(int node)
    {
        for (int i = _sp - 1; i >= 0; i--)
        {
            if (_stack[i] == node)
            {
                return i;
            }
        }

        return -1;
    }

    private bool InStack(int node) => (_a.Flags[node] & F.InStack) != 0;

    private bool InAfe(int node) => (_a.Flags[node] & F.InAfe) != 0;

    private bool ParsingTemplateContents => _cnt[N.Template] > 0 || (_fragment && _ctxNs == Html && _ctxName == N.Template);

    /// <summary>Records that the current end tag token closed <paramref name="node"/>.</summary>
    private void MarkEnd(int node)
    {
        if (_tokIsEnd && _a.EndStart[node] < 0)
        {
            _a.EndStart[node] = _tokStart;
            _a.EndEnd[node] = _tokEnd;
        }
    }

    private void PopUntil(int name, bool mark)
    {
        while (_sp > 0)
        {
            int n = Pop();
            if (IsHtmlNamed(n, name))
            {
                if (mark)
                {
                    MarkEnd(n);
                }

                return;
            }
        }
    }

    private void PopUntilRange(int lo, int hi, bool mark)
    {
        while (_sp > 0)
        {
            int n = Pop();
            if (_a.Ns[n] == Html && _a.Name[n] >= lo && _a.Name[n] <= hi)
            {
                if (mark)
                {
                    MarkEnd(n);
                }

                return;
            }
        }
    }

    private void PopUntilAny(int a, int b, bool mark)
    {
        while (_sp > 0)
        {
            int n = Pop();
            if (_a.Ns[n] == Html && (_a.Name[n] == a || _a.Name[n] == b))
            {
                if (mark)
                {
                    MarkEnd(n);
                }

                return;
            }
        }
    }

    /// <summary>HTML 13.2.6.3: generate implied end tags, optionally leaving out one element name.</summary>
    private void GenerateImpliedEndTags(int except = -1)
    {
        while (_sp > 0)
        {
            int c = Cur;
            if (_a.Ns[c] == Html && (ElementInfo.Cat(_a.Name[c]) & ElementInfo.ImpliedEnd) != 0 && _a.Name[c] != except)
            {
                Pop();
            }
            else
            {
                break;
            }
        }
    }

    private void GenerateAllImpliedEndTagsThoroughly()
    {
        while (_sp > 0)
        {
            int c = Cur;
            if (_a.Ns[c] == Html && (ElementInfo.Cat(_a.Name[c]) & ElementInfo.ImpliedEndThorough) != 0)
            {
                Pop();
            }
            else
            {
                break;
            }
        }
    }

    // ---- Scopes (HTML 13.2.4.2) ----

    private static bool IsScopeBoundary(Scope scope, byte ns, int name)
    {
        if (scope == Scope.Table)
        {
            return ns == Html && name is N.Html or N.Table or N.Template;
        }

        switch (ns)
        {
            case Html:
                if ((ElementInfo.Cat(name) & ElementInfo.ScopeBoundary) != 0)
                {
                    return true;
                }

                return (scope == Scope.ListItem && name is N.Ol or N.Ul) || (scope == Scope.Button && name == N.Button);
            case MathMl:
                return name is N.Mi or N.Mo or N.Mn or N.Ms or N.Mtext or N.AnnotationXml;
            default:
                return name is N.CcForeignObject or N.Desc or N.Title;
        }
    }

    /// <summary>Whether the stack has an HTML element named <paramref name="a"/>, <paramref name="b"/> or <paramref name="c"/> in the scope.</summary>
    private bool HasInScope(Scope scope, int a, int b = -1, int c = -1)
    {
        if (_cnt[a] == 0 && (b < 0 || _cnt[b] == 0) && (c < 0 || _cnt[c] == 0))
        {
            return false;
        }

        for (int i = _sp - 1; i >= 0; i--)
        {
            int node = _stack[i];
            byte ns = _a.Ns[node];
            int name = _a.Name[node];
            if (ns == Html && (name == a || name == b || name == c))
            {
                return true;
            }

            if (IsScopeBoundary(scope, ns, name))
            {
                return false;
            }
        }

        return false;
    }

    private bool HasHeadingInScope()
    {
        for (int i = _sp - 1; i >= 0; i--)
        {
            int node = _stack[i];
            byte ns = _a.Ns[node];
            int name = _a.Name[node];
            if (ns == Html && name >= N.H1 && name <= N.H6)
            {
                return true;
            }

            if (IsScopeBoundary(Scope.Default, ns, name))
            {
                return false;
            }
        }

        return false;
    }

    private bool HasNodeInScope(Scope scope, int target)
    {
        for (int i = _sp - 1; i >= 0; i--)
        {
            int node = _stack[i];
            if (node == target)
            {
                return true;
            }

            if (IsScopeBoundary(scope, _a.Ns[node], _a.Name[node]))
            {
                return false;
            }
        }

        return false;
    }

    private bool IsSpecialNode(int node) => ElementInfo.IsSpecial(_a.Ns[node], _a.Name[node]);

    // ---- Insertion locations (HTML 13.2.6.1) ----

    /// <summary>
    /// The appropriate place for inserting a node, with the fragment redirect applied. <paramref name="fostered"/>
    /// reports that foster parenting moved the location.
    /// </summary>
    private void Place(int overrideTarget, out int parent, out int before, out bool fostered)
    {
        int target = overrideTarget >= 0 ? overrideTarget : _stack[_sp - 1];
        before = -1;
        fostered = false;
        if (_foster && _a.Kind[target] == (byte)NodeKind.Element && _a.Ns[target] == Html && _a.Name[target] is N.Table or N.Tbody or N.Tfoot or N.Thead or N.Tr)
        {
            fostered = true;
            int lt = _lastTt;
            if (lt < 0)
            {
                target = _stack[0];
            }
            else if (_a.Name[lt] == N.Template)
            {
                target = lt;
            }
            else if (_a.Parent[lt] >= 0)
            {
                target = _a.Parent[lt];
                before = lt;
            }
            else
            {
                int pos = StackIndexOf(lt);
                target = pos > 0 ? _stack[pos - 1] : _stack[0];
            }
        }

        if (_a.Kind[target] == (byte)NodeKind.Element && _a.Ns[target] == Html && _a.Name[target] == N.Template && _a.Aux[target] >= 0)
        {
            target = _a.Aux[target];
            before = -1;
        }

        if (_fragment && target == _rootHtml)
        {
            target = 0;
            before = -1;
        }

        parent = target;
    }

    private void InsertNode(int parent, int before, int node, bool fostered)
    {
        _a.Insert(parent, before, node);
        if (fostered)
        {
            _a.Flags[node] |= F.Reparented;
        }
    }

    // ---- Creating elements ----

    private static Tok SynthStart(int name, int pos) =>
        new() { Kind = HtmlTokenKind.StartTag, Name = name, NoAttrs = true, Synth = true, Start = pos, End = pos };

    /// <summary>Creates an element for the token, inserts it at the appropriate place and pushes it (HTML "insert a foreign element").</summary>
    private int InsertElement(ref Tok t, byte ns, int name, Adjust adjust)
    {
        int e = CreateElement(ref t, ns, name, adjust);
        Place(-1, out int parent, out int before, out bool fostered);
        InsertNode(parent, before, e, fostered);
        Push(e);
        if (ns == Html)
        {
            if (name == N.Option)
            {
                OnOptionInserted(e);
            }
            else if (name == N.Selectedcontent)
            {
                OnSelectedContentInserted(e);
            }
        }

        return e;
    }

    private int InsertHtmlElement(ref Tok t) => InsertElement(ref t, Html, t.Name, Adjust.None);

    /// <summary>Inserts an element for a start tag with no attributes that no source tag stands behind.</summary>
    private int InsertHtmlElementNamed(int name)
    {
        Tok t = SynthStart(name, _tokStart);
        return InsertHtmlElement(ref t);
    }

    private int CreateElement(ref Tok t, byte ns, int name, Adjust adjust)
    {
        int e = _a.AddNode(NodeKind.Element, ns, name);
        _a.SrcStart[e] = t.Start;
        _a.SrcEnd[e] = t.End;
        ushort flags = 0;
        if (t.Synth)
        {
            flags |= F.ImpliedStart;
        }
        else if (t.SelfClosing)
        {
            flags |= F.SelfClosing;
        }

        _a.Flags[e] = flags;
        if (!t.NoAttrs && t.Kind == HtmlTokenKind.StartTag)
        {
            CopyAttributes(e, adjust);
            if (ns == MathMl && name == N.AnnotationXml)
            {
                AttributeViewList attrs = _tz.Attributes;
                if (attrs.TryGet("encoding", out AttributeView enc)
                    && (Quirks.AsciiEquals(enc.Value, "text/html") || Quirks.AsciiEquals(enc.Value, "application/xhtml+xml")))
                {
                    _a.Flags[e] |= F.IntegrationPoint;
                }
            }
        }

        if (ns == Html && name == N.Template)
        {
            int content = _a.AddNode(NodeKind.DocumentFragment, Html, -1);
            _a.Aux[e] = content;
        }

        return e;
    }

    private void CopyAttributes(int e, Adjust adjust)
    {
        AttributeViewList list = _tz.Attributes;
        int n = list.Count;
        _a.AttrStart[e] = _a.AttrTotal;
        _a.AttrCount[e] = n;
        for (int i = 0; i < n; i++)
        {
            AddTokenAttribute(list[i], adjust);
        }
    }

    private void AddTokenAttribute(AttributeView av, Adjust adjust)
    {
        int atom = _names.Intern(av.Name);
        AttributeNamespace ans = AttributeNamespace.None;
        if (adjust != Adjust.None)
        {
            if (adjust == Adjust.MathMl)
            {
                if (atom == N.Definitionurl)
                {
                    atom = N.CcDefinitionURL;
                }
            }
            else
            {
                atom = ElementInfo.FixSvgAttribute(atom);
            }

            // "Adjust foreign attributes": the names with a namespace prefix.
            switch (atom)
            {
                case N.XlinkActuate:
                    atom = N.Actuate;
                    ans = AttributeNamespace.XLink;
                    break;
                case N.XlinkArcrole:
                    atom = N.Arcrole;
                    ans = AttributeNamespace.XLink;
                    break;
                case N.XlinkHref:
                    atom = N.Href;
                    ans = AttributeNamespace.XLink;
                    break;
                case N.XlinkRole:
                    atom = N.Role;
                    ans = AttributeNamespace.XLink;
                    break;
                case N.XlinkShow:
                    atom = N.Show;
                    ans = AttributeNamespace.XLink;
                    break;
                case N.XlinkTitle:
                    atom = N.Title;
                    ans = AttributeNamespace.XLink;
                    break;
                case N.XlinkType:
                    atom = N.Type;
                    ans = AttributeNamespace.XLink;
                    break;
                case N.XmlLang:
                    atom = N.Lang;
                    ans = AttributeNamespace.Xml;
                    break;
                case N.XmlSpace:
                    atom = N.Space;
                    ans = AttributeNamespace.Xml;
                    break;
                case N.Xmlns:
                    ans = AttributeNamespace.Xmlns;
                    break;
                case N.XmlnsXlink:
                    atom = N.Xlink;
                    ans = AttributeNamespace.Xmlns;
                    break;
                default:
                    break;
            }
        }

        byte flags = (byte)((int)av.Quote | ((int)ans << AF.NsShift));
        int row = _a.AddAttribute(atom, flags, av.NameStart, av.NameEnd);
        _a.AValStart[row] = av.ValueStart;
        _a.AValEnd[row] = av.ValueEnd;
        ReadOnlySpan<char> value = av.Value;
        int len = value.Length;
        if (len == av.ValueEnd - av.ValueStart && _text.AsSpan(av.ValueStart, len).SequenceEqual(value))
        {
            _a.AValOff[row] = av.ValueStart;
            _a.AValLen[row] = len;
        }
        else
        {
            _a.AValOff[row] = _a.AppendSide(value);
            _a.AValLen[row] = len;
            _a.AFlags[row] |= AF.ValueSide;
        }
    }

    /// <summary>Copies an element for the adoption agency algorithm and for reopening formatting elements.</summary>
    private int CloneElement(int old)
    {
        int e = _a.AddNode(NodeKind.Element, _a.Ns[old], _a.Name[old]);
        _a.SrcStart[e] = _a.SrcStart[old];
        _a.SrcEnd[e] = _a.SrcEnd[old];
        _a.Flags[e] = (ushort)(F.Cloned | (_a.Flags[old] & F.ImpliedStart));
        int n = _a.AttrCount[old];
        _a.AttrStart[e] = _a.AttrTotal;
        _a.AttrCount[e] = n;
        for (int i = 0; i < n; i++)
        {
            int from = _a.AttrStart[old] + i;
            int row = _a.AddAttribute(_a.AName[from], _a.AFlags[from], _a.ANameStart[from], _a.ANameEnd[from]);
            _a.AValStart[row] = _a.AValStart[from];
            _a.AValEnd[row] = _a.AValEnd[from];
            _a.AValOff[row] = _a.AValOff[from];
            _a.AValLen[row] = _a.AValLen[from];
        }

        return e;
    }

    // ---- Text, comments, doctype ----

    private void InsertChars(ReadOnlySpan<char> s, int srcStart, int srcEnd, bool exact)
    {
        Place(-1, out int parent, out int before, out bool fostered);
        if (_a.Kind[parent] == (byte)NodeKind.Document)
        {
            return;
        }

        int prev = before >= 0 ? _a.Prev[before] : _a.Last[parent];
        if (prev >= 0 && _a.Kind[prev] == (byte)NodeKind.Text)
        {
            AppendText(prev, s, srcStart, srcEnd, exact);
            if (fostered)
            {
                _a.Flags[prev] |= F.Reparented;
            }

            return;
        }

        int t = _a.AddNode(NodeKind.Text, Html, -1);
        _a.SrcStart[t] = srcStart;
        _a.SrcEnd[t] = srcEnd;
        if (exact)
        {
            _a.DataOff[t] = srcStart;
        }
        else
        {
            int off = _a.AppendSide(s);
            _a.DataOff[t] = off;
            _a.Aux[t] = off + s.Length;
            _a.Flags[t] |= F.DataSide;
        }

        _a.DataLen[t] = s.Length;
        InsertNode(parent, before, t, fostered);
    }

    private void AppendText(int t, ReadOnlySpan<char> s, int srcStart, int srcEnd, bool exact)
    {
        bool side = (_a.Flags[t] & F.DataSide) != 0;
        bool contiguous = srcStart == _a.SrcEnd[t] || (srcStart == _a.SrcStart[t] && srcEnd == _a.SrcEnd[t]);
        if (!contiguous)
        {
            _a.Flags[t] |= F.TextMerged;
        }

        if (!side && exact && (_a.Flags[t] & F.Synthesized) == 0 && _a.DataOff[t] + _a.DataLen[t] == srcStart)
        {
            _a.DataLen[t] += s.Length;
        }
        else
        {
            int len = _a.DataLen[t];
            int need = s.Length;
            if (!side || _a.Aux[t] - (_a.DataOff[t] + len) < need)
            {
                int cap = (int)Math.Min(Math.Max(16L, ((long)len + need) * 2), Array.MaxLength / 2);
                int off = _a.ReserveSide(cap);
                ReadOnlySpan<char> old = side ? _a.Side.AsSpan(_a.DataOff[t], len) : _text.AsSpan(_a.DataOff[t], len);
                old.CopyTo(_a.Side.AsSpan(off));
                _a.DataOff[t] = off;
                _a.Aux[t] = off + cap;
                _a.Flags[t] |= F.DataSide;
            }

            s.CopyTo(_a.Side.AsSpan(_a.DataOff[t] + len));
            _a.DataLen[t] = len + need;
        }

        if (_a.SrcStart[t] < 0)
        {
            // A copy made by the selectedcontent steps had no source; the text added to it is the only part with bytes.
            _a.SrcStart[t] = srcStart;
            _a.SrcEnd[t] = srcEnd;
        }
        else if (srcStart < _a.SrcStart[t])
        {
            _a.SrcStart[t] = srcStart;
        }

        if (srcEnd > _a.SrcEnd[t])
        {
            _a.SrcEnd[t] = srcEnd;
        }
    }

    private void InsertComment(int overrideTarget)
    {
        Place(overrideTarget, out int parent, out int before, out bool fostered);
        int c = _a.AddNode(NodeKind.Comment, Html, -1);
        _a.SrcStart[c] = _tz.Start;
        _a.SrcEnd[c] = _tz.End;
        int candidate = _tz.Start + (_text.AsSpan(_tz.Start).StartsWith("<!--") ? 4 : 2);
        SetData(c, _tz.Data, candidate);
        InsertNode(parent, before, c, fostered);
    }

    private void InsertProcessingInstruction(int overrideTarget)
    {
        Place(overrideTarget, out int parent, out int before, out bool fostered);
        int p = _a.AddNode(NodeKind.ProcessingInstruction, Html, _names.Intern(_tz.Name));
        _a.SrcStart[p] = _tz.Start;
        _a.SrcEnd[p] = _tz.End;
        SetData(p, _tz.Data, -1);
        InsertNode(parent, before, p, fostered);
    }

    private void SetData(int node, ReadOnlySpan<char> data, int candidate)
    {
        if (candidate >= 0 && candidate + data.Length <= _text.Length && _text.AsSpan(candidate, data.Length).SequenceEqual(data))
        {
            _a.DataOff[node] = candidate;
        }
        else
        {
            _a.DataOff[node] = _a.AppendSide(data);
            _a.Flags[node] |= F.DataSide;
        }

        _a.DataLen[node] = data.Length;
    }

    // ---- Template modes ----

    private void PushTemplateMode(Mode m)
    {
        if (_templateModeCount == _templateModes.Length)
        {
            Array.Resize(ref _templateModes, _templateModeCount * 2);
        }

        _templateModes[_templateModeCount++] = m;
    }

    private Mode CurrentTemplateMode => _templateModes[_templateModeCount - 1];

    // ---- Reset the insertion mode appropriately (HTML 13.2.4.1) ----

    private void ResetInsertionMode()
    {
        for (int i = _sp - 1; ; i--)
        {
            bool last = i == 0;
            byte ns;
            int name;
            if (last && _fragment)
            {
                ns = _ctxNs;
                name = _ctxName;
            }
            else
            {
                ns = _a.Ns[_stack[i]];
                name = _a.Name[_stack[i]];
            }

            if (ns == Html)
            {
                switch (name)
                {
                    case N.Td or N.Th when !last:
                        _mode = Mode.InCell;
                        return;
                    case N.Tr:
                        _mode = Mode.InRow;
                        return;
                    case N.Tbody or N.Thead or N.Tfoot:
                        _mode = Mode.InTableBody;
                        return;
                    case N.Caption:
                        _mode = Mode.InCaption;
                        return;
                    case N.Colgroup:
                        _mode = Mode.InColumnGroup;
                        return;
                    case N.Table:
                        _mode = Mode.InTable;
                        return;
                    case N.Template:
                        _mode = CurrentTemplateMode;
                        return;
                    case N.Head when !last:
                        _mode = Mode.InHead;
                        return;
                    case N.Body:
                        _mode = Mode.InBody;
                        return;
                    case N.Frameset:
                        _mode = Mode.InFrameset;
                        return;
                    case N.Html:
                        _mode = _head < 0 ? Mode.BeforeHead : Mode.AfterHead;
                        return;
                    default:
                        break;
                }
            }

            if (last)
            {
                _mode = Mode.InBody;
                return;
            }
        }
    }

    // ---- The list of active formatting elements (HTML 13.2.4.3) ----

    private void AfeAddMarker()
    {
        CheckAfeRoom();
        _afe[_afeCount] = -1;
        _afeHash[_afeCount] = 0;
        _afeCount++;
    }

    private void CheckAfeRoom()
    {
        if (_afeCount >= _limits.MaxActiveFormattingElements)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxActiveFormattingElements), _limits.MaxActiveFormattingElements, (long)_afeCount + 1);
        }

        if (_afeCount == _afe.Length)
        {
            Array.Resize(ref _afe, _afeCount * 2);
            Array.Resize(ref _afeHash, _afeCount * 2);
        }
    }

    private void ClearAfeToLastMarker()
    {
        while (_afeCount > 0)
        {
            int e = _afe[--_afeCount];
            if (e < 0)
            {
                return;
            }

            _a.Flags[e] &= unchecked((ushort)~F.InAfe);
        }
    }

    private int AfeIndexOf(int node)
    {
        for (int i = _afeCount - 1; i >= 0; i--)
        {
            if (_afe[i] == node)
            {
                return i;
            }
        }

        return -1;
    }

    private void AfeRemoveAt(int index)
    {
        int e = _afe[index];
        if (e >= 0)
        {
            _a.Flags[e] &= unchecked((ushort)~F.InAfe);
        }

        Array.Copy(_afe, index + 1, _afe, index, _afeCount - index - 1);
        Array.Copy(_afeHash, index + 1, _afeHash, index, _afeCount - index - 1);
        _afeCount--;
    }

    private void AfeInsertAt(int index, int node, uint hash)
    {
        CheckAfeRoom();
        Array.Copy(_afe, index, _afe, index + 1, _afeCount - index);
        Array.Copy(_afeHash, index, _afeHash, index + 1, _afeCount - index);
        _afe[index] = node;
        _afeHash[index] = hash;
        _afeCount++;
        _a.Flags[node] |= F.InAfe;
    }

    /// <summary>Push onto the list of active formatting elements, with the "Noah's Ark" clause: three of a kind at most.</summary>
    private void AfePush(int node)
    {
        uint hash = AttributeHash(node);
        int matches = 0;
        int earliest = -1;
        for (int i = _afeCount - 1; i >= 0; i--)
        {
            int e = _afe[i];
            if (e < 0)
            {
                break;
            }

            if (_afeHash[i] == hash && SameFamily(e, node))
            {
                matches++;
                earliest = i;
            }
        }

        if (matches >= 3)
        {
            AfeRemoveAt(earliest);
        }

        AfeInsertAt(_afeCount, node, hash);
    }

    private uint AttributeHash(int e)
    {
        uint h = (uint)(_a.Name[e] * 31 + _a.Ns[e]);
        int start = _a.AttrStart[e];
        uint sum = 0;
        for (int i = 0; i < _a.AttrCount[e]; i++)
        {
            int at = start + i;
            uint x = (uint)_a.AName[at] * 2654435761u + (uint)(_a.AFlags[at] & AF.NsMask);
            foreach (char c in AttrValue(at))
            {
                x = (x ^ c) * 16777619;
            }

            sum += x ^ (x >> 13);
        }

        return h * 16777619 + sum;
    }

    private ReadOnlySpan<char> AttrValue(int at) =>
        (_a.AFlags[at] & AF.ValueSide) != 0
            ? _a.Side.AsSpan(_a.AValOff[at], _a.AValLen[at])
            : _text.AsSpan(_a.AValOff[at], _a.AValLen[at]);

    private bool SameFamily(int x, int y)
    {
        if (_a.Name[x] != _a.Name[y] || _a.Ns[x] != _a.Ns[y] || _a.AttrCount[x] != _a.AttrCount[y])
        {
            return false;
        }

        for (int i = 0; i < _a.AttrCount[x]; i++)
        {
            int ax = _a.AttrStart[x] + i;
            bool found = false;
            for (int j = 0; j < _a.AttrCount[y]; j++)
            {
                int ay = _a.AttrStart[y] + j;
                if (_a.AName[ax] == _a.AName[ay]
                    && (_a.AFlags[ax] & AF.NsMask) == (_a.AFlags[ay] & AF.NsMask)
                    && AttrValue(ax).SequenceEqual(AttrValue(ay)))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reconstruct the active formatting elements, if any.</summary>
    private void ReconstructFormatting()
    {
        if (_afeCount == 0)
        {
            return;
        }

        int last = _afe[_afeCount - 1];
        if (last < 0 || InStack(last))
        {
            return;
        }

        int idx = _afeCount - 1;
        while (idx > 0)
        {
            int prev = _afe[idx - 1];
            if (prev < 0 || InStack(prev))
            {
                break;
            }

            idx--;
        }

        for (; idx < _afeCount; idx++)
        {
            int old = _afe[idx];
            int clone = CloneElement(old);
            Place(-1, out int parent, out int before, out bool fostered);
            InsertNode(parent, before, clone, fostered);
            Push(clone);
            _a.Flags[old] &= unchecked((ushort)~F.InAfe);
            _a.Flags[clone] |= F.InAfe;
            _afe[idx] = clone;
        }
    }
}
