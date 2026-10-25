using System;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Dom;

// The insertion modes other than "in body" (HTML 13.2.6.4). Each handler returns true when the token must be
// processed again (the standard's "reprocess the token"); the dispatcher loops instead of recursing.
internal sealed partial class TreeBuilder
{
    private bool ProcessInMode(Mode m, ref Tok t) => m switch
    {
        Mode.Initial => InitialMode(ref t),
        Mode.BeforeHtml => BeforeHtmlMode(ref t),
        Mode.BeforeHead => BeforeHeadMode(ref t),
        Mode.InHead => InHeadMode(ref t),
        Mode.InHeadNoscript => InHeadNoscriptMode(ref t),
        Mode.AfterHead => AfterHeadMode(ref t),
        Mode.InBody => InBodyMode(ref t),
        Mode.Text => TextMode(ref t),
        Mode.InTable => InTableMode(ref t),
        Mode.InTableText => InTableTextMode(ref t),
        Mode.InCaption => InCaptionMode(ref t),
        Mode.InColumnGroup => InColumnGroupMode(ref t),
        Mode.InTableBody => InTableBodyMode(ref t),
        Mode.InRow => InRowMode(ref t),
        Mode.InCell => InCellMode(ref t),
        Mode.InTemplate => InTemplateMode(ref t),
        Mode.AfterBody => AfterBodyMode(ref t),
        Mode.InFrameset => InFramesetMode(ref t),
        Mode.AfterFrameset => AfterFramesetMode(ref t),
        Mode.AfterAfterBody => AfterAfterBodyMode(ref t),
        _ => AfterAfterFramesetMode(ref t),
    };

    private bool CharsInMode(Mode m, ref Chars c)
    {
        bool ws = c.Class == CharClass.Whitespace;
        switch (m)
        {
            case Mode.Initial:
                if (ws)
                {
                    return false;
                }

                _quirks = QuirksMode.Quirks;
                _mode = Mode.BeforeHtml;
                return true;
            case Mode.BeforeHtml:
                if (ws)
                {
                    return false;
                }

                CreateImpliedHtml(c.Start);
                _mode = Mode.BeforeHead;
                return true;
            case Mode.BeforeHead:
                if (ws)
                {
                    return false;
                }

                _head = InsertHtmlElementNamed(N.Head);
                _mode = Mode.InHead;
                return true;
            case Mode.InHead:
                if (ws)
                {
                    InsertChars(c.S, c.Start, c.End, c.Exact);
                    return false;
                }

                Pop();
                _mode = Mode.AfterHead;
                return true;
            case Mode.InHeadNoscript:
                if (ws)
                {
                    InsertChars(c.S, c.Start, c.End, c.Exact);
                    return false;
                }

                Pop();
                _mode = Mode.InHead;
                return true;
            case Mode.AfterHead:
                if (ws)
                {
                    InsertChars(c.S, c.Start, c.End, c.Exact);
                    return false;
                }

                InsertHtmlElementNamed(N.Body);
                _framesetOk = true;
                _mode = Mode.InBody;
                return true;
            case Mode.InBody:
            case Mode.InCaption:
            case Mode.InCell:
            case Mode.InTemplate:
                BodyChars(ref c);
                return false;
            case Mode.Text:
                InsertChars(c.S, c.Start, c.End, c.Exact);
                return false;
            case Mode.InTable:
            case Mode.InTableBody:
            case Mode.InRow:
                if (_sp > 0 && _a.Ns[Cur] == Html && _a.Name[Cur] is N.Table or N.Tbody or N.Template or N.Tfoot or N.Thead or N.Tr)
                {
                    _pendLen = 0;
                    _pendSegs = 0;
                    _pendNonWhitespace = false;
                    _origMode = _mode;
                    _mode = Mode.InTableText;
                    return true;
                }

                _foster = true;
                BodyChars(ref c);
                _foster = false;
                return false;
            case Mode.InTableText:
                if (c.Class == CharClass.Null)
                {
                    return false;
                }

                AddPending(ref c);
                return false;
            case Mode.InColumnGroup:
                if (ws)
                {
                    InsertChars(c.S, c.Start, c.End, c.Exact);
                    return false;
                }

                return ColumnGroupAnythingElse();
            case Mode.AfterBody:
            case Mode.AfterAfterBody:
                if (ws)
                {
                    BodyChars(ref c);
                    return false;
                }

                _mode = Mode.InBody;
                return true;
            case Mode.InFrameset:
            case Mode.AfterFrameset:
                if (ws)
                {
                    InsertChars(c.S, c.Start, c.End, c.Exact);
                }

                return false;
            default:
                if (ws)
                {
                    BodyChars(ref c);
                }

                return false;
        }
    }

    /// <summary>The character rules of "in body" for one run.</summary>
    private void BodyChars(ref Chars c)
    {
        switch (c.Class)
        {
            case CharClass.Null:
                return;
            case CharClass.Whitespace:
                ReconstructFormatting();
                InsertChars(c.S, c.Start, c.End, c.Exact);
                return;
            default:
                ReconstructFormatting();
                InsertChars(c.S, c.Start, c.End, c.Exact);
                _framesetOk = false;
                return;
        }
    }

    // ---- The "in table text" pending list ----

    private void AddPending(ref Chars c)
    {
        if (_pendLen + c.S.Length > _pendChars.Length)
        {
            Array.Resize(ref _pendChars, Math.Max(_pendChars.Length * 2, _pendLen + c.S.Length));
        }

        if ((_pendSegs + 1) * 6 > _pendInfo.Length)
        {
            Array.Resize(ref _pendInfo, _pendInfo.Length * 2);
        }

        c.S.CopyTo(_pendChars.AsSpan(_pendLen));
        int b = _pendSegs * 6;
        _pendInfo[b] = _pendLen;
        _pendInfo[b + 1] = c.S.Length;
        _pendInfo[b + 2] = c.Start;
        _pendInfo[b + 3] = c.End;
        _pendInfo[b + 4] = c.Exact ? 1 : 0;
        _pendInfo[b + 5] = (int)c.Class;
        _pendLen += c.S.Length;
        _pendSegs++;
        if (c.Class != CharClass.Whitespace)
        {
            _pendNonWhitespace = true;
        }
    }

    private void FlushPending()
    {
        bool foster = _pendNonWhitespace;
        if (foster)
        {
            _foster = true;
        }

        for (int i = 0; i < _pendSegs; i++)
        {
            int b = i * 6;
            var c = new Chars
            {
                S = _pendChars.AsSpan(_pendInfo[b], _pendInfo[b + 1]),
                Start = _pendInfo[b + 2],
                End = _pendInfo[b + 3],
                Exact = _pendInfo[b + 4] != 0,
                Class = (CharClass)_pendInfo[b + 5],
            };
            if (foster)
            {
                BodyChars(ref c);
            }
            else
            {
                InsertChars(c.S, c.Start, c.End, c.Exact);
            }
        }

        if (foster)
        {
            _foster = false;
        }

        _pendLen = 0;
        _pendSegs = 0;
        _pendNonWhitespace = false;
    }

    // ---- Shared pieces ----

    private void CreateImpliedHtml(int pos)
    {
        Tok s = SynthStart(N.Html, pos);
        int html = CreateElement(ref s, Html, N.Html, Adjust.None);
        _a.Insert(0, -1, html);
        Push(html);
    }

    private void GenericText(ref Tok t, TokenizerState state)
    {
        InsertHtmlElement(ref t);
        _tz.SetState(state);
        _origMode = _mode;
        _mode = Mode.Text;
    }

    private void InsertVoid(ref Tok t)
    {
        _lastVoid = InsertHtmlElement(ref t);
        Pop();
    }

    private bool IsAttrValue(string name, string expected)
    {
        AttributeViewList attrs = _tz.Attributes;
        return attrs.TryGet(name, out AttributeView v) && Quirks.AsciiEquals(v.Value, expected);
    }

    private bool IsHiddenInput(ref Tok t) => !t.NoAttrs && IsAttrValue("type", "hidden");

    // ---- initial, before html, before head ----

    private bool InitialMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(0);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(0);
                return false;
            case HtmlTokenKind.Doctype:
                HandleDoctype();
                _mode = Mode.BeforeHtml;
                return false;
            default:
                _quirks = QuirksMode.Quirks;
                _mode = Mode.BeforeHtml;
                return true;
        }
    }

    private void HandleDoctype()
    {
        string? pub = _tz.PublicId;
        string? sys = _tz.SystemId;
        int d = _a.AddNode(NodeKind.DocumentType, Html, _names.Intern(_tz.Name));
        _a.SrcStart[d] = _tz.Start;
        _a.SrcEnd[d] = _tz.End;
        _a.Insert(0, -1, d);
        _doctype = d;
        _publicId = pub ?? string.Empty;
        _systemId = sys ?? string.Empty;
        _quirks = Quirks.FromDoctype(_tz.ForceQuirks, _tz.Name, pub, sys);
    }

    private bool BeforeHtmlMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Doctype:
                return false;
            case HtmlTokenKind.Comment:
                InsertComment(0);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(0);
                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Html:
                int html = CreateElement(ref t, Html, N.Html, Adjust.None);
                _a.Insert(0, -1, html);
                Push(html);
                _mode = Mode.BeforeHead;
                return false;
            case HtmlTokenKind.EndTag when t.Name is not (N.Head or N.Body or N.Html or N.Br):
                return false;
            default:
                CreateImpliedHtml(t.Start);
                _mode = Mode.BeforeHead;
                return true;
        }
    }

    private bool BeforeHeadMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(-1);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(-1);
                return false;
            case HtmlTokenKind.Doctype:
                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Html:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag when t.Name == N.Head:
                _head = InsertHtmlElement(ref t);
                _mode = Mode.InHead;
                return false;
            case HtmlTokenKind.EndTag when t.Name is not (N.Head or N.Body or N.Html or N.Br):
                return false;
            default:
                _head = InsertHtmlElementNamed(N.Head);
                _mode = Mode.InHead;
                return true;
        }
    }

    // ---- in head ----

    private bool InHeadMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(-1);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(-1);
                return false;
            case HtmlTokenKind.Doctype:
                return false;
            case HtmlTokenKind.StartTag:
                switch (t.Name)
                {
                    case N.Html:
                        return InBodyMode(ref t);
                    case N.Base or N.Basefont or N.Bgsound or N.Link:
                        InsertVoid(ref t);
                        return false;
                    case N.Meta:
                        InsertVoid(ref t);
                        if (!t.NoAttrs)
                        {
                            ChangeEncodingFromMeta();
                        }

                        return false;
                    case N.Title:
                        GenericText(ref t, TokenizerState.Rcdata);
                        return false;
                    case N.Noscript when _scripting:
                    case N.Noframes or N.Style:
                        GenericText(ref t, TokenizerState.Rawtext);
                        return false;
                    case N.Noscript:
                        InsertHtmlElement(ref t);
                        _mode = Mode.InHeadNoscript;
                        return false;
                    case N.Script:
                        GenericText(ref t, TokenizerState.ScriptData);
                        return false;
                    case N.Template:
                        AfeAddMarker();
                        _framesetOk = false;
                        _mode = Mode.InTemplate;
                        PushTemplateMode(Mode.InTemplate);
                        InsertHtmlElement(ref t);
                        return false;
                    case N.Head:
                        return false;
                    default:
                        break;
                }

                break;
            case HtmlTokenKind.EndTag:
                switch (t.Name)
                {
                    case N.Head:
                        MarkEnd(Pop());
                        _mode = Mode.AfterHead;
                        return false;
                    case N.Body or N.Html or N.Br:
                        break;
                    case N.Template:
                        if (_cnt[N.Template] == 0)
                        {
                            return false;
                        }

                        GenerateAllImpliedEndTagsThoroughly();
                        PopUntil(N.Template, mark: true);
                        ClearAfeToLastMarker();
                        _templateModeCount--;
                        ResetInsertionMode();
                        return false;
                    default:
                        return false;
                }

                break;
            default:
                break;
        }

        // Anything else.
        Pop();
        _mode = Mode.AfterHead;
        return true;
    }

    /// <summary>HTML 13.2.6.4.4, "meta": a declared encoding changes a tentative one (HTML 13.2.3.4).</summary>
    private void ChangeEncodingFromMeta()
    {
        if (_source.Confidence != EncodingConfidence.Tentative)
        {
            return;
        }

        AttributeViewList attrs = _tz.Attributes;
        WhatwgEncoding? enc = null;
        if (attrs.TryGet("charset", out AttributeView charset))
        {
            enc = WhatwgEncoding.ForLabel(charset.Value);
        }
        else if (attrs.TryGet("http-equiv", out AttributeView equiv)
            && Quirks.AsciiEquals(equiv.Value, "content-type")
            && attrs.TryGet("content", out AttributeView content))
        {
            enc = EncodingSniffer.ExtractFromMetaContent(content.Value);
        }

        if (enc is null)
        {
            return;
        }

        EncodingChange change = _source.ChangeEncoding(enc, _ct);
        if (change.RestartRequired)
        {
            _restart = change.Text;
            _stop = true;
        }
        else
        {
            _source = change.Text;
        }
    }

    private bool InHeadNoscriptMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Doctype:
                return false;
            case HtmlTokenKind.StartTag:
                switch (t.Name)
                {
                    case N.Html:
                        return InBodyMode(ref t);
                    case N.Basefont or N.Bgsound or N.Link or N.Meta or N.Noframes or N.Style:
                        return InHeadMode(ref t);
                    case N.Head or N.Noscript:
                        return false;
                    default:
                        break;
                }

                break;
            case HtmlTokenKind.EndTag:
                if (t.Name == N.Noscript)
                {
                    MarkEnd(Pop());
                    _mode = Mode.InHead;
                    return false;
                }

                if (t.Name != N.Br)
                {
                    return false;
                }

                break;
            case HtmlTokenKind.Comment or HtmlTokenKind.ProcessingInstruction:
                return InHeadMode(ref t);
            default:
                break;
        }

        Pop();
        _mode = Mode.InHead;
        return true;
    }

    private bool AfterHeadMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(-1);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(-1);
                return false;
            case HtmlTokenKind.Doctype:
                return false;
            case HtmlTokenKind.StartTag:
                switch (t.Name)
                {
                    case N.Html:
                        return InBodyMode(ref t);
                    case N.Body:
                        InsertHtmlElement(ref t);
                        _framesetOk = false;
                        _mode = Mode.InBody;
                        return false;
                    case N.Frameset:
                        InsertHtmlElement(ref t);
                        _mode = Mode.InFrameset;
                        return false;
                    case N.Base or N.Basefont or N.Bgsound or N.Link or N.Meta or N.Noframes or N.Script or N.Style or N.Template or N.Title:
                        Push(_head);
                        InHeadMode(ref t);
                        int at = StackIndexOf(_head);
                        if (at >= 0)
                        {
                            RemoveStackAt(at);
                        }

                        return false;
                    case N.Head:
                        return false;
                    default:
                        break;
                }

                break;
            case HtmlTokenKind.EndTag:
                switch (t.Name)
                {
                    case N.Template:
                        return InHeadMode(ref t);
                    case N.Body or N.Html or N.Br:
                        break;
                    default:
                        return false;
                }

                break;
            default:
                break;
        }

        InsertHtmlElementNamed(N.Body);
        _framesetOk = true;
        _mode = Mode.InBody;
        return true;
    }

    // ---- text ----

    private bool TextMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.EndOfFile:
                Pop();
                _mode = _origMode;
                return true;
            case HtmlTokenKind.EndTag:
                MarkEnd(Pop());
                _mode = _origMode;
                return false;
            default:
                return false;
        }
    }

    // ---- tables ----

    private void ClearStackBackToTableContext()
    {
        while (_sp > 1 && !(_a.Ns[Cur] == Html && _a.Name[Cur] is N.Table or N.Template or N.Html))
        {
            Pop();
        }
    }

    private void ClearStackBackToTableBodyContext()
    {
        while (_sp > 1 && !(_a.Ns[Cur] == Html && _a.Name[Cur] is N.Tbody or N.Tfoot or N.Thead or N.Template or N.Html))
        {
            Pop();
        }
    }

    private void ClearStackBackToTableRowContext()
    {
        while (_sp > 1 && !(_a.Ns[Cur] == Html && _a.Name[Cur] is N.Tr or N.Template or N.Html))
        {
            Pop();
        }
    }

    private bool InTableMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(-1);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(-1);
                return false;
            case HtmlTokenKind.Doctype:
                return false;
            case HtmlTokenKind.EndOfFile:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag:
                switch (t.Name)
                {
                    case N.Caption:
                        ClearStackBackToTableContext();
                        AfeAddMarker();
                        InsertHtmlElement(ref t);
                        _mode = Mode.InCaption;
                        return false;
                    case N.Colgroup:
                        ClearStackBackToTableContext();
                        InsertHtmlElement(ref t);
                        _mode = Mode.InColumnGroup;
                        return false;
                    case N.Col:
                        ClearStackBackToTableContext();
                        InsertHtmlElementNamed(N.Colgroup);
                        _mode = Mode.InColumnGroup;
                        return true;
                    case N.Tbody or N.Tfoot or N.Thead:
                        ClearStackBackToTableContext();
                        InsertHtmlElement(ref t);
                        _mode = Mode.InTableBody;
                        return false;
                    case N.Td or N.Th or N.Tr:
                        ClearStackBackToTableContext();
                        InsertHtmlElementNamed(N.Tbody);
                        _mode = Mode.InTableBody;
                        return true;
                    case N.Table:
                        if (!HasInScope(Scope.Table, N.Table))
                        {
                            return false;
                        }

                        PopUntil(N.Table, mark: false);
                        ResetInsertionMode();
                        return true;
                    case N.Style or N.Script or N.Template:
                        return InHeadMode(ref t);
                    case N.Input:
                        if (!IsHiddenInput(ref t))
                        {
                            break;
                        }

                        InsertVoid(ref t);
                        return false;
                    case N.Form:
                        if (_form != -1 && !ParsingTemplateContents)
                        {
                            return false;
                        }

                        {
                            int f = InsertHtmlElement(ref t);
                            if (!ParsingTemplateContents)
                            {
                                _form = f;
                            }

                            Pop();
                        }

                        return false;
                    default:
                        break;
                }

                break;
            case HtmlTokenKind.EndTag:
                switch (t.Name)
                {
                    case N.Table:
                        if (!HasInScope(Scope.Table, N.Table))
                        {
                            return false;
                        }

                        PopUntil(N.Table, mark: true);
                        ResetInsertionMode();
                        return false;
                    case N.Body or N.Caption or N.Col or N.Colgroup or N.Html or N.Tbody or N.Td or N.Tfoot or N.Th or N.Thead or N.Tr:
                        return false;
                    case N.Template:
                        return InHeadMode(ref t);
                    default:
                        break;
                }

                break;
            default:
                break;
        }

        // Anything else: foster parent the content.
        _foster = true;
        bool again = InBodyMode(ref t);
        _foster = false;
        return again;
    }

    private bool InTableTextMode(ref Tok t)
    {
        FlushPending();
        _mode = _origMode;
        return true;
    }

    private bool InCaptionMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.EndTag when t.Name == N.Caption:
                if (!HasInScope(Scope.Table, N.Caption))
                {
                    return false;
                }

                CloseCaption();
                return false;
            case HtmlTokenKind.StartTag when t.Name is N.Caption or N.Col or N.Colgroup or N.Tbody or N.Td or N.Tfoot or N.Th or N.Thead or N.Tr:
            case HtmlTokenKind.EndTag when t.Name == N.Table:
                if (!HasInScope(Scope.Table, N.Caption))
                {
                    return false;
                }

                CloseCaption();
                return true;
            case HtmlTokenKind.EndTag when t.Name is N.Body or N.Col or N.Colgroup or N.Html or N.Tbody or N.Td or N.Tfoot or N.Th or N.Thead or N.Tr:
                return false;
            default:
                return InBodyMode(ref t);
        }
    }

    private void CloseCaption()
    {
        GenerateImpliedEndTags();
        PopUntil(N.Caption, mark: _tokIsEnd && _tz.Name.SequenceEqual("caption"));
        ClearAfeToLastMarker();
        _mode = Mode.InTable;
    }

    private bool ColumnGroupAnythingElse()
    {
        if (!CurIs(N.Colgroup))
        {
            return false;
        }

        Pop();
        _mode = Mode.InTable;
        return true;
    }

    private bool InColumnGroupMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(-1);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(-1);
                return false;
            case HtmlTokenKind.Doctype:
                return false;
            case HtmlTokenKind.EndOfFile:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag when t.Name == N.Html:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag when t.Name == N.Col:
                InsertVoid(ref t);
                return false;
            case HtmlTokenKind.EndTag when t.Name == N.Colgroup:
                if (!CurIs(N.Colgroup))
                {
                    return false;
                }

                MarkEnd(Pop());
                _mode = Mode.InTable;
                return false;
            case HtmlTokenKind.EndTag when t.Name == N.Col:
                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Template:
            case HtmlTokenKind.EndTag when t.Name == N.Template:
                return InHeadMode(ref t);
            default:
                return ColumnGroupAnythingElse();
        }
    }

    private bool InTableBodyMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.StartTag when t.Name == N.Tr:
                ClearStackBackToTableBodyContext();
                InsertHtmlElement(ref t);
                _mode = Mode.InRow;
                return false;
            case HtmlTokenKind.StartTag when t.Name is N.Th or N.Td:
                ClearStackBackToTableBodyContext();
                InsertHtmlElementNamed(N.Tr);
                _mode = Mode.InRow;
                return true;
            case HtmlTokenKind.EndTag when t.Name is N.Tbody or N.Tfoot or N.Thead:
                if (!HasInScope(Scope.Table, t.Name))
                {
                    return false;
                }

                ClearStackBackToTableBodyContext();
                MarkEnd(Pop());
                _mode = Mode.InTable;
                return false;
            case HtmlTokenKind.StartTag when t.Name is N.Caption or N.Col or N.Colgroup or N.Tbody or N.Tfoot or N.Thead:
            case HtmlTokenKind.EndTag when t.Name == N.Table:
                if (!HasInScope(Scope.Table, N.Tbody, N.Thead, N.Tfoot))
                {
                    return false;
                }

                ClearStackBackToTableBodyContext();
                Pop();
                _mode = Mode.InTable;
                return true;
            case HtmlTokenKind.EndTag when t.Name is N.Body or N.Caption or N.Col or N.Colgroup or N.Html or N.Td or N.Th or N.Tr:
                return false;
            default:
                return InTableMode(ref t);
        }
    }

    private bool InRowMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.StartTag when t.Name is N.Th or N.Td:
                ClearStackBackToTableRowContext();
                InsertHtmlElement(ref t);
                _mode = Mode.InCell;
                AfeAddMarker();
                return false;
            case HtmlTokenKind.EndTag when t.Name == N.Tr:
                if (!HasInScope(Scope.Table, N.Tr))
                {
                    return false;
                }

                ClearStackBackToTableRowContext();
                MarkEnd(Pop());
                _mode = Mode.InTableBody;
                return false;
            case HtmlTokenKind.StartTag when t.Name is N.Caption or N.Col or N.Colgroup or N.Tbody or N.Tfoot or N.Thead or N.Tr:
            case HtmlTokenKind.EndTag when t.Name == N.Table:
                if (!HasInScope(Scope.Table, N.Tr))
                {
                    return false;
                }

                ClearStackBackToTableRowContext();
                Pop();
                _mode = Mode.InTableBody;
                return true;
            case HtmlTokenKind.EndTag when t.Name is N.Tbody or N.Tfoot or N.Thead:
                if (!HasInScope(Scope.Table, t.Name) || !HasInScope(Scope.Table, N.Tr))
                {
                    return false;
                }

                ClearStackBackToTableRowContext();
                Pop();
                _mode = Mode.InTableBody;
                return true;
            case HtmlTokenKind.EndTag when t.Name is N.Body or N.Caption or N.Col or N.Colgroup or N.Html or N.Td or N.Th:
                return false;
            default:
                return InTableMode(ref t);
        }
    }

    private bool InCellMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.EndTag when t.Name is N.Td or N.Th:
                if (!HasInScope(Scope.Table, t.Name))
                {
                    return false;
                }

                GenerateImpliedEndTags();
                PopUntil(t.Name, mark: true);
                ClearAfeToLastMarker();
                _mode = Mode.InRow;
                return false;
            case HtmlTokenKind.StartTag when t.Name is N.Caption or N.Col or N.Colgroup or N.Tbody or N.Td or N.Tfoot or N.Th or N.Thead or N.Tr:
                if (!HasInScope(Scope.Table, N.Td, N.Th))
                {
                    return false;
                }

                CloseCell();
                return true;
            case HtmlTokenKind.EndTag when t.Name is N.Body or N.Caption or N.Col or N.Colgroup or N.Html:
                return false;
            case HtmlTokenKind.EndTag when t.Name is N.Table or N.Tbody or N.Tfoot or N.Thead or N.Tr:
                if (!HasInScope(Scope.Table, t.Name))
                {
                    return false;
                }

                CloseCell();
                return true;
            default:
                return InBodyMode(ref t);
        }
    }

    private void CloseCell()
    {
        GenerateImpliedEndTags();
        PopUntilAny(N.Td, N.Th, mark: false);
        ClearAfeToLastMarker();
        _mode = Mode.InRow;
    }

    private bool InTemplateMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Doctype or HtmlTokenKind.Comment or HtmlTokenKind.ProcessingInstruction:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag:
                switch (t.Name)
                {
                    case N.Base or N.Basefont or N.Bgsound or N.Link or N.Meta or N.Noframes or N.Script or N.Style or N.Template or N.Title:
                        return InHeadMode(ref t);
                    case N.Caption or N.Colgroup or N.Tbody or N.Tfoot or N.Thead:
                        return SwitchTemplateMode(Mode.InTable);
                    case N.Col:
                        return SwitchTemplateMode(Mode.InColumnGroup);
                    case N.Tr:
                        return SwitchTemplateMode(Mode.InTableBody);
                    case N.Td or N.Th:
                        return SwitchTemplateMode(Mode.InRow);
                    default:
                        return SwitchTemplateMode(Mode.InBody);
                }

            case HtmlTokenKind.EndTag when t.Name == N.Template:
                return InHeadMode(ref t);
            case HtmlTokenKind.EndTag:
                return false;
            case HtmlTokenKind.EndOfFile:
                if (_cnt[N.Template] == 0)
                {
                    _stop = true;
                    return false;
                }

                PopUntil(N.Template, mark: false);
                ClearAfeToLastMarker();
                _templateModeCount--;
                ResetInsertionMode();
                return true;
            default:
                return false;
        }
    }

    private bool SwitchTemplateMode(Mode m)
    {
        _templateModeCount--;
        PushTemplateMode(m);
        _mode = m;
        return true;
    }

    // ---- after body and the frameset modes ----

    private bool AfterBodyMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(_stack[0]);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(_stack[0]);
                return false;
            case HtmlTokenKind.Doctype:
                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Html:
                return InBodyMode(ref t);
            case HtmlTokenKind.EndTag when t.Name == N.Html:
                if (_fragment)
                {
                    return false;
                }

                MarkEnd(_stack[0]);
                _mode = Mode.AfterAfterBody;
                return false;
            case HtmlTokenKind.EndOfFile:
                _stop = true;
                return false;
            default:
                _mode = Mode.InBody;
                return true;
        }
    }

    private bool InFramesetMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(-1);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(-1);
                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Html:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag when t.Name == N.Frameset:
                InsertHtmlElement(ref t);
                return false;
            case HtmlTokenKind.EndTag when t.Name == N.Frameset:
                if (_sp == 1)
                {
                    return false;
                }

                MarkEnd(Pop());
                if (!_fragment && !CurIs(N.Frameset))
                {
                    _mode = Mode.AfterFrameset;
                }

                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Frame:
                InsertVoid(ref t);
                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Noframes:
                return InHeadMode(ref t);
            case HtmlTokenKind.EndOfFile:
                _stop = true;
                return false;
            default:
                return false;
        }
    }

    private bool AfterFramesetMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(-1);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(-1);
                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Html:
                return InBodyMode(ref t);
            case HtmlTokenKind.EndTag when t.Name == N.Html:
                MarkEnd(_stack[0]);
                _mode = Mode.AfterAfterFrameset;
                return false;
            case HtmlTokenKind.StartTag when t.Name == N.Noframes:
                return InHeadMode(ref t);
            case HtmlTokenKind.EndOfFile:
                _stop = true;
                return false;
            default:
                return false;
        }
    }

    private bool AfterAfterBodyMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(0);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(0);
                return false;
            case HtmlTokenKind.Doctype:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag when t.Name == N.Html:
                return InBodyMode(ref t);
            case HtmlTokenKind.EndOfFile:
                _stop = true;
                return false;
            default:
                _mode = Mode.InBody;
                return true;
        }
    }

    private bool AfterAfterFramesetMode(ref Tok t)
    {
        switch (t.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(0);
                return false;
            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(0);
                return false;
            case HtmlTokenKind.Doctype:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag when t.Name == N.Html:
                return InBodyMode(ref t);
            case HtmlTokenKind.StartTag when t.Name == N.Noframes:
                return InHeadMode(ref t);
            case HtmlTokenKind.EndOfFile:
                _stop = true;
                return false;
            default:
                return false;
        }
    }
}
