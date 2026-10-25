using System;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Dom;

// Parsing tokens in foreign content (HTML 13.2.6.5).
internal sealed partial class TreeBuilder
{
    private bool ForeignChars(ref Chars c)
    {
        switch (c.Class)
        {
            case CharClass.Null:
                // Each NUL becomes U+FFFD.
                {
                    Span<char> fffd = c.S.Length <= 64 ? stackalloc char[c.S.Length] : new char[c.S.Length];
                    fffd.Fill('\uFFFD');
                    InsertChars(fffd, c.Start, c.End, false);
                }

                return false;
            case CharClass.Whitespace:
                InsertChars(c.S, c.Start, c.End, c.Exact);
                return false;
            default:
                InsertChars(c.S, c.Start, c.End, c.Exact);
                _framesetOk = false;
                return false;
        }
    }

    private bool ProcessForeign(ref Tok t)
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
                if (IsBreakout(ref t))
                {
                    PopToHtmlOrIntegrationPoint();
                    return ProcessInMode(_mode, ref t);
                }

                ForeignStartTag(ref t);
                return false;
            case HtmlTokenKind.EndTag:
                if (t.Name is N.Br or N.P)
                {
                    PopToHtmlOrIntegrationPoint();
                    return ProcessInMode(_mode, ref t);
                }

                if (t.Name == N.Script && _a.Ns[Cur] == Svg && _a.Name[Cur] == N.Script)
                {
                    MarkEnd(Pop());
                    return false;
                }

                return ForeignAnyOtherEndTag(ref t);
            default:
                return false;
        }
    }

    private bool IsBreakout(ref Tok t)
    {
        switch (t.Name)
        {
            case N.B or N.Big or N.Blockquote or N.Body or N.Br or N.Center or N.Code or N.Dd or N.Div or N.Dl or N.Dt or N.Em
                or N.Embed or N.H1 or N.H2 or N.H3 or N.H4 or N.H5 or N.H6 or N.Head or N.Hr or N.I or N.Img or N.Li or N.Listing
                or N.Menu or N.Meta or N.Nobr or N.Ol or N.P or N.Pre or N.Ruby or N.S or N.Small or N.Span or N.Strong or N.Strike
                or N.Sub or N.Sup or N.Table or N.Tt or N.U or N.Ul or N.Var:
                return true;
            case N.Font:
                if (t.NoAttrs)
                {
                    return false;
                }

                AttributeViewList attrs = _tz.Attributes;
                return attrs.TryGet("color", out _) || attrs.TryGet("face", out _) || attrs.TryGet("size", out _);
            default:
                return false;
        }
    }

    // "While the current node is not a MathML text integration point, an HTML integration point, or an element in
    // the HTML namespace, pop elements from the stack of open elements."
    private void PopToHtmlOrIntegrationPoint()
    {
        while (_sp > 1)
        {
            int c = Cur;
            byte ns = _a.Ns[c];
            int name = _a.Name[c];
            if (ns == Html
                || (ns == MathMl && name is N.Mi or N.Mo or N.Mn or N.Ms or N.Mtext)
                || (_a.Flags[c] & F.IntegrationPoint) != 0
                || IsSvgIntegrationName(ns, name))
            {
                return;
            }

            Pop();
        }
    }

    private void ForeignStartTag(ref Tok t)
    {
        GetAdjusted(out byte ns, out _, out _);
        int name = t.Name;
        Adjust adjust = Adjust.None;
        if (ns == MathMl)
        {
            adjust = Adjust.MathMl;
        }
        else if (ns == Svg)
        {
            name = ElementInfo.FixSvgTag(name);
            adjust = Adjust.Svg;
        }

        InsertElement(ref t, ns, name, adjust);
        if (t.SelfClosing)
        {
            // An SVG "script" would also run its end tag steps, which only pop the element here.
            Pop();
        }
    }

    private bool ForeignAnyOtherEndTag(ref Tok t)
    {
        ReadOnlySpan<char> tagName = _tz.Name;
        for (int i = _sp - 1; i > 0; i--)
        {
            int node = _stack[i];
            if (NameEqualsIgnoreCase(_names.NameOf(_a.Name[node]), tagName))
            {
                while (_sp > i)
                {
                    Pop();
                }

                MarkEnd(node);
                return false;
            }

            int below = _stack[i - 1];
            if (_a.Ns[below] == Html)
            {
                // Process the token according to the rules of the current insertion mode in HTML content.
                return ProcessInMode(_mode, ref t);
            }
        }

        return false;
    }

    private static bool NameEqualsIgnoreCase(string name, ReadOnlySpan<char> lowerTag)
    {
        if (name.Length != lowerTag.Length)
        {
            return false;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is >= 'A' and <= 'Z')
            {
                c = (char)(c + 0x20);
            }

            if (c != lowerTag[i])
            {
                return false;
            }
        }

        return true;
    }
}
