using System;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Dom;

// The "in body" insertion mode (HTML 13.2.6.4.7) and the adoption agency algorithm.
internal sealed partial class TreeBuilder
{
    private bool InBodyMode(ref Tok t)
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
                return InBodyStartTag(ref t);
            case HtmlTokenKind.EndTag:
                return InBodyEndTag(ref t);
            case HtmlTokenKind.EndOfFile:
                if (_templateModeCount > 0)
                {
                    return InTemplateMode(ref t);
                }

                _stop = true;
                return false;
            default:
                return false;
        }
    }

    private bool InBodyStartTag(ref Tok t)
    {
        switch (t.Name)
        {
            case N.Html:
                if (_cnt[N.Template] > 0)
                {
                    return false;
                }

                MergeAttributes(_stack[0]);
                return false;
            case N.Base or N.Basefont or N.Bgsound or N.Link or N.Meta or N.Noframes or N.Script or N.Style or N.Template or N.Title:
                return InHeadMode(ref t);
            case N.Body:
                if (_sp == 1 || !IsHtmlNamed(_stack[1], N.Body) || _cnt[N.Template] > 0)
                {
                    return false;
                }

                _framesetOk = false;
                MergeAttributes(_stack[1]);
                return false;
            case N.Frameset:
                if (_sp == 1 || !IsHtmlNamed(_stack[1], N.Body) || !_framesetOk)
                {
                    return false;
                }

                _a.Detach(_stack[1]);
                while (_sp > 1)
                {
                    Pop();
                }

                InsertHtmlElement(ref t);
                _mode = Mode.InFrameset;
                return false;
            case N.Address or N.Article or N.Aside or N.Blockquote or N.Center or N.Details or N.Dialog or N.Dir or N.Div or N.Dl
                or N.Fieldset or N.Figcaption or N.Figure or N.Footer or N.Header or N.Hgroup or N.Main or N.Menu or N.Nav or N.Ol
                or N.P or N.Search or N.Section or N.Summary or N.Ul:
                CloseParagraphIfInButtonScope();
                InsertHtmlElement(ref t);
                return false;
            case N.H1 or N.H2 or N.H3 or N.H4 or N.H5 or N.H6:
                CloseParagraphIfInButtonScope();
                if (_a.Ns[Cur] == Html && _a.Name[Cur] >= N.H1 && _a.Name[Cur] <= N.H6)
                {
                    Pop();
                }

                InsertHtmlElement(ref t);
                return false;
            case N.Pre or N.Listing:
                CloseParagraphIfInButtonScope();
                InsertHtmlElement(ref t);
                _skipLf = true;
                _framesetOk = false;
                return false;
            case N.Form:
                if (_form != -1 && !ParsingTemplateContents)
                {
                    return false;
                }

                CloseParagraphIfInButtonScope();
                {
                    int f = InsertHtmlElement(ref t);
                    if (!ParsingTemplateContents)
                    {
                        _form = f;
                    }
                }

                return false;
            case N.Li:
                ListItemStart(ref t, dd: false);
                return false;
            case N.Dd or N.Dt:
                ListItemStart(ref t, dd: true);
                return false;
            case N.Plaintext:
                CloseParagraphIfInButtonScope();
                InsertHtmlElement(ref t);
                _tz.SetState(TokenizerState.Plaintext);
                return false;
            case N.Button:
                if (HasInScope(Scope.Default, N.Button))
                {
                    GenerateImpliedEndTags();
                    PopUntil(N.Button, mark: false);
                }

                ReconstructFormatting();
                InsertHtmlElement(ref t);
                _framesetOk = false;
                return false;
            case N.A:
                for (int i = _afeCount - 1; i >= 0; i--)
                {
                    int e = _afe[i];
                    if (e < 0)
                    {
                        break;
                    }

                    if (_a.Name[e] == N.A)
                    {
                        AdoptionAgency(ref t);
                        int idx = AfeIndexOf(e);
                        if (idx >= 0)
                        {
                            AfeRemoveAt(idx);
                        }

                        int at = StackIndexOf(e);
                        if (at >= 0)
                        {
                            RemoveStackAt(at);
                        }

                        break;
                    }
                }

                ReconstructFormatting();
                AfePush(InsertHtmlElement(ref t));
                return false;
            case N.B or N.Big or N.Code or N.Em or N.Font or N.I or N.S or N.Small or N.Strike or N.Strong or N.Tt or N.U:
                ReconstructFormatting();
                AfePush(InsertHtmlElement(ref t));
                return false;
            case N.Nobr:
                ReconstructFormatting();
                if (HasInScope(Scope.Default, N.Nobr))
                {
                    AdoptionAgency(ref t);
                    ReconstructFormatting();
                }

                AfePush(InsertHtmlElement(ref t));
                return false;
            case N.Applet or N.Marquee or N.Object:
                ReconstructFormatting();
                InsertHtmlElement(ref t);
                AfeAddMarker();
                _framesetOk = false;
                return false;
            case N.Table:
                if (_quirks != QuirksMode.Quirks)
                {
                    CloseParagraphIfInButtonScope();
                }

                InsertHtmlElement(ref t);
                _framesetOk = false;
                _mode = Mode.InTable;
                return false;
            case N.Area or N.Br or N.Embed or N.Img or N.Keygen or N.Wbr:
                ReconstructFormatting();
                InsertVoid(ref t);
                if (t.Name == N.Br)
                {
                    MarkEnd(_lastVoid);
                }

                _framesetOk = false;
                return false;
            case N.Input:
                if (_fragment && _ctxNs == Html && _ctxName == N.Select)
                {
                    return false;
                }

                if (HasInScope(Scope.Default, N.Select))
                {
                    PopUntil(N.Select, mark: false);
                }

                ReconstructFormatting();
                InsertVoid(ref t);
                if (!IsHiddenInput(ref t))
                {
                    _framesetOk = false;
                }

                return false;
            case N.Param or N.Source or N.Track:
                InsertVoid(ref t);
                return false;
            case N.Hr:
                CloseParagraphIfInButtonScope();
                if (HasInScope(Scope.Default, N.Select))
                {
                    GenerateImpliedEndTags();
                }

                InsertVoid(ref t);
                _framesetOk = false;
                return false;
            case N.Image:
                t.Name = N.Img;
                return true;
            case N.Textarea:
                InsertHtmlElement(ref t);
                _skipLf = true;
                _tz.SetState(TokenizerState.Rcdata);
                _origMode = _mode;
                _framesetOk = false;
                _mode = Mode.Text;
                return false;
            case N.Xmp:
                CloseParagraphIfInButtonScope();
                ReconstructFormatting();
                _framesetOk = false;
                GenericText(ref t, TokenizerState.Rawtext);
                return false;
            case N.Iframe:
                _framesetOk = false;
                GenericText(ref t, TokenizerState.Rawtext);
                return false;
            case N.Noembed:
                GenericText(ref t, TokenizerState.Rawtext);
                return false;
            case N.Noscript when _scripting:
                GenericText(ref t, TokenizerState.Rawtext);
                return false;
            case N.Select:
                if (_fragment && _ctxNs == Html && _ctxName == N.Select)
                {
                    return false;
                }

                if (HasInScope(Scope.Default, N.Select))
                {
                    PopUntil(N.Select, mark: false);
                    return false;
                }

                ReconstructFormatting();
                InsertHtmlElement(ref t);
                _framesetOk = false;
                return false;
            case N.Option:
                if (HasInScope(Scope.Default, N.Select))
                {
                    GenerateImpliedEndTags(except: N.Optgroup);
                }
                else if (CurIs(N.Option))
                {
                    Pop();
                }

                ReconstructFormatting();
                InsertHtmlElement(ref t);
                return false;
            case N.Optgroup:
                if (HasInScope(Scope.Default, N.Select))
                {
                    GenerateImpliedEndTags();
                }
                else if (CurIs(N.Option))
                {
                    Pop();
                }

                ReconstructFormatting();
                InsertHtmlElement(ref t);
                return false;
            case N.Rb or N.Rtc:
                if (HasInScope(Scope.Default, N.Ruby))
                {
                    GenerateImpliedEndTags();
                }

                InsertHtmlElement(ref t);
                return false;
            case N.Rp or N.Rt:
                if (HasInScope(Scope.Default, N.Ruby))
                {
                    GenerateImpliedEndTags(except: N.Rtc);
                }

                InsertHtmlElement(ref t);
                return false;
            case N.Math:
                ReconstructFormatting();
                InsertElement(ref t, MathMl, N.Math, Adjust.MathMl);
                if (t.SelfClosing)
                {
                    Pop();
                }

                return false;
            case N.Svg:
                ReconstructFormatting();
                InsertElement(ref t, Svg, N.Svg, Adjust.Svg);
                if (t.SelfClosing)
                {
                    Pop();
                }

                return false;
            case N.Caption or N.Col or N.Colgroup or N.Frame or N.Head or N.Tbody or N.Td or N.Tfoot or N.Th or N.Thead or N.Tr:
                return false;
            default:
                ReconstructFormatting();
                InsertHtmlElement(ref t);
                return false;
        }
    }

    // The element InsertVoid made last, so that a "br" made from an end tag can take that tag as its end range.
    private int _lastVoid;

    private void CloseParagraphIfInButtonScope()
    {
        if (HasInScope(Scope.Button, N.P))
        {
            ClosePElement(mark: false);
        }
    }

    private void ClosePElement(bool mark)
    {
        GenerateImpliedEndTags(except: N.P);
        PopUntil(N.P, mark);
    }

    /// <summary>Adds the token's attributes the element does not have yet (a later duplicate html or body start tag).</summary>
    private void MergeAttributes(int element)
    {
        AttributeViewList list = _tz.Attributes;
        if (list.Count == 0)
        {
            return;
        }

        // The element's rows move to the end of the table so that the new ones stay contiguous with them.
        int oldStart = _a.AttrStart[element];
        int oldCount = _a.AttrCount[element];
        int added = 0;
        for (int i = 0; i < list.Count; i++)
        {
            AttributeView av = list[i];
            int atom = _names.Intern(av.Name);
            bool has = false;
            for (int j = 0; j < oldCount; j++)
            {
                if (_a.AName[oldStart + j] == atom && (_a.AFlags[oldStart + j] & AF.NsMask) == 0)
                {
                    has = true;
                    break;
                }
            }

            if (has)
            {
                continue;
            }

            if (added == 0)
            {
                int newStart = _a.AttrTotal;
                for (int j = 0; j < oldCount; j++)
                {
                    int from = oldStart + j;
                    int row = _a.AddAttribute(_a.AName[from], _a.AFlags[from], _a.ANameStart[from], _a.ANameEnd[from]);
                    _a.AValStart[row] = _a.AValStart[from];
                    _a.AValEnd[row] = _a.AValEnd[from];
                    _a.AValOff[row] = _a.AValOff[from];
                    _a.AValLen[row] = _a.AValLen[from];
                }

                _a.AttrStart[element] = newStart;
            }

            AddTokenAttribute(av, Adjust.None);
            added++;
        }

        if (added > 0)
        {
            _a.AttrCount[element] = oldCount + added;
            _a.Flags[element] |= F.AttrsMerged;
        }
    }

    private void ListItemStart(ref Tok t, bool dd)
    {
        _framesetOk = false;
        for (int i = _sp - 1; i >= 0; i--)
        {
            int node = _stack[i];
            byte ns = _a.Ns[node];
            int name = _a.Name[node];
            if (ns == Html && (dd ? name is N.Dd or N.Dt : name == N.Li))
            {
                if (dd)
                {
                    GenerateImpliedEndTags(except: name);
                    PopUntil(name, mark: false);
                }
                else
                {
                    GenerateImpliedEndTags(except: N.Li);
                    PopUntil(N.Li, mark: false);
                }

                break;
            }

            if (ElementInfo.IsSpecial(ns, name) && !(ns == Html && name is N.Address or N.Div or N.P))
            {
                break;
            }
        }

        CloseParagraphIfInButtonScope();
        InsertHtmlElement(ref t);
    }

    private bool InBodyEndTag(ref Tok t)
    {
        switch (t.Name)
        {
            case N.Template:
                return InHeadMode(ref t);
            case N.Body:
                if (!HasInScope(Scope.Default, N.Body))
                {
                    return false;
                }

                if (_sp > 1 && IsHtmlNamed(_stack[1], N.Body))
                {
                    MarkEnd(_stack[1]);
                }

                _mode = Mode.AfterBody;
                return false;
            case N.Html:
                if (!HasInScope(Scope.Default, N.Body))
                {
                    return false;
                }

                _mode = Mode.AfterBody;
                return true;
            case N.Address or N.Article or N.Aside or N.Blockquote or N.Button or N.Center or N.Details or N.Dialog or N.Dir or N.Div
                or N.Dl or N.Fieldset or N.Figcaption or N.Figure or N.Footer or N.Header or N.Hgroup or N.Listing or N.Main or N.Menu
                or N.Nav or N.Ol or N.Pre or N.Search or N.Section or N.Select or N.Summary or N.Ul:
                if (!HasInScope(Scope.Default, t.Name))
                {
                    return false;
                }

                GenerateImpliedEndTags();
                PopUntil(t.Name, mark: true);
                return false;
            case N.Form:
                if (!ParsingTemplateContents)
                {
                    int node = _form;
                    _form = -1;
                    if (node < 0 || !HasNodeInScope(Scope.Default, node))
                    {
                        return false;
                    }

                    GenerateImpliedEndTags();
                    int at = StackIndexOf(node);
                    if (at >= 0)
                    {
                        MarkEnd(node);
                        RemoveStackAt(at);
                    }

                    return false;
                }

                if (!HasInScope(Scope.Default, N.Form))
                {
                    return false;
                }

                GenerateImpliedEndTags();
                PopUntil(N.Form, mark: true);
                return false;
            case N.P:
                if (!HasInScope(Scope.Button, N.P))
                {
                    InsertHtmlElementNamed(N.P);
                }

                ClosePElement(mark: true);
                return false;
            case N.Li:
                if (!HasInScope(Scope.ListItem, N.Li))
                {
                    return false;
                }

                GenerateImpliedEndTags(except: N.Li);
                PopUntil(N.Li, mark: true);
                return false;
            case N.Dd or N.Dt:
                if (!HasInScope(Scope.Default, t.Name))
                {
                    return false;
                }

                GenerateImpliedEndTags(except: t.Name);
                PopUntil(t.Name, mark: true);
                return false;
            case N.H1 or N.H2 or N.H3 or N.H4 or N.H5 or N.H6:
                if (!HasHeadingInScope())
                {
                    return false;
                }

                GenerateImpliedEndTags();
                PopUntilRange(N.H1, N.H6, mark: true);
                return false;
            case N.A or N.B or N.Big or N.Code or N.Em or N.Font or N.I or N.Nobr or N.S or N.Small or N.Strike or N.Strong or N.Tt or N.U:
                AdoptionAgency(ref t);
                return false;
            case N.Applet or N.Marquee or N.Object:
                if (!HasInScope(Scope.Default, t.Name))
                {
                    return false;
                }

                GenerateImpliedEndTags();
                PopUntil(t.Name, mark: true);
                ClearAfeToLastMarker();
                return false;
            case N.Br:
                // A "br" end tag is treated as a "br" start tag without attributes.
                t.Kind = HtmlTokenKind.StartTag;
                t.NoAttrs = true;
                t.SelfClosing = false;
                t.Synth = true;
                t.Start = t.End = _tokStart;
                return InBodyStartTag(ref t);
            default:
                AnyOtherEndTag(ref t);
                return false;
        }
    }

    /// <summary>The "any other end tag" entry of "in body".</summary>
    private void AnyOtherEndTag(ref Tok t)
    {
        for (int i = _sp - 1; i >= 0; i--)
        {
            int node = _stack[i];
            if (_a.Ns[node] == Html && _a.Name[node] == t.Name)
            {
                GenerateImpliedEndTags(except: t.Name);
                while (_sp > i)
                {
                    Pop();
                }

                MarkEnd(node);
                return;
            }

            if (IsSpecialNode(node))
            {
                return;
            }
        }
    }

    // ---- The adoption agency algorithm (HTML 13.2.6.4.7) ----

    private void AdoptionAgency(ref Tok t)
    {
        int subject = t.Name;
        int current = Cur;
        if (IsHtmlNamed(current, subject) && !InAfe(current))
        {
            MarkEnd(Pop());
            return;
        }

        for (int outer = 0; outer < 8; outer++)
        {
            int fe = -1;
            int feIdx = -1;
            for (int i = _afeCount - 1; i >= 0; i--)
            {
                int e = _afe[i];
                if (e < 0)
                {
                    break;
                }

                if (_a.Name[e] == subject)
                {
                    fe = e;
                    feIdx = i;
                    break;
                }
            }

            if (fe < 0)
            {
                AnyOtherEndTag(ref t);
                return;
            }

            if (!InStack(fe))
            {
                AfeRemoveAt(feIdx);
                return;
            }

            if (!HasNodeInScope(Scope.Default, fe))
            {
                return;
            }

            int feSp = StackIndexOf(fe);
            int fb = -1;
            int fbSp = -1;
            for (int i = feSp + 1; i < _sp; i++)
            {
                if (IsSpecialNode(_stack[i]))
                {
                    fb = _stack[i];
                    fbSp = i;
                    break;
                }
            }

            if (fb < 0)
            {
                while (_sp > feSp)
                {
                    Pop();
                }

                MarkEnd(fe);
                AfeRemoveAt(AfeIndexOf(fe));
                return;
            }

            int common = _stack[feSp - 1];
            int bookmark = feIdx;
            int node = fb;
            int last = fb;
            int nodeSp = fbSp;
            int inner = 0;
            bool bookmarkMoved = false;
            while (true)
            {
                inner++;
                nodeSp--;
                node = _stack[nodeSp];
                if (node == fe)
                {
                    break;
                }

                if (inner > 3 && InAfe(node))
                {
                    int idx = AfeIndexOf(node);
                    AfeRemoveAt(idx);
                    if (idx < bookmark)
                    {
                        bookmark--;
                    }
                }

                if (!InAfe(node))
                {
                    RemoveStackAt(nodeSp);
                    continue;
                }

                int clone = CloneElement(node);
                int ai = AfeIndexOf(node);
                _a.Flags[node] &= unchecked((ushort)~F.InAfe);
                _a.Flags[clone] |= F.InAfe;
                _afe[ai] = clone;
                _a.Flags[node] &= unchecked((ushort)~F.InStack);
                _a.Flags[clone] |= F.InStack;
                _stack[nodeSp] = clone;
                node = clone;
                if (!bookmarkMoved)
                {
                    bookmark = ai + 1;
                    bookmarkMoved = true;
                }

                _a.Detach(last);
                _a.Insert(node, -1, last);
                _a.Flags[last] |= F.Reparented;
                last = node;
            }

            Place(common, out int target, out int refNode, out bool fostered);
            _a.Detach(last);
            _a.Insert(target, refNode, last);
            _a.Flags[last] |= F.Reparented;
            _ = fostered;

            int newElement = CloneElement(fe);
            MoveChildren(fb, newElement);
            _a.Insert(fb, -1, newElement);

            // The list: drop the old element, put the new one at the bookmark.
            int oldIdx = AfeIndexOf(fe);
            uint hash = _afeHash[oldIdx];
            AfeRemoveAt(oldIdx);
            if (oldIdx < bookmark)
            {
                bookmark--;
            }

            AfeInsertAt(bookmark, newElement, hash);

            // The stack: drop the old element, put the new one right below the furthest block.
            MarkEnd(fe);
            RemoveStackAt(StackIndexOf(fe));
            InsertStackAt(StackIndexOf(fb) + 1, newElement);
        }
    }

    private void MoveChildren(int from, int to)
    {
        int c = _a.First[from];
        if (c < 0)
        {
            return;
        }

        _a.First[to] = c;
        _a.Last[to] = _a.Last[from];
        for (; c >= 0; c = _a.Next[c])
        {
            _a.Parent[c] = to;
            _a.Flags[c] |= F.Reparented;
        }

        _a.First[from] = -1;
        _a.Last[from] = -1;
    }
}
