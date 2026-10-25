using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>Serialization of selectors per CSSOM ("serialize a selector") and CSS Syntax for &lt;an+b&gt;.</summary>
internal static class SelectorSerializer
{
    public static string Serialize(SelectorList list)
    {
        var sb = new StringBuilder();
        Write(sb, list);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, SelectorList list)
    {
        for (int i = 0; i < list.Items.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            WriteComplex(sb, list.Items[i]);
        }
    }

    internal static string SerializeComplex(ComplexSelector c)
    {
        var sb = new StringBuilder();
        WriteComplex(sb, c);
        return sb.ToString();
    }

    private static void WriteComplex(StringBuilder sb, ComplexSelector c)
    {
        if (c.Invalid)
        {
            sb.Append(c.Raw);
            return;
        }

        if (c.IsRelative && c.Leading != Combinator.Descendant)
        {
            sb.Append(CombinatorText(c.Leading, false)).Append(' ');
        }

        for (int i = 0; i < c.Compounds.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(CombinatorText(c.Combs[i - 1], true));
            }

            WriteCompound(sb, c.Compounds[i]);
        }
    }

    private static string CombinatorText(Combinator c, bool spaced) => c switch
    {
        Combinator.Child => spaced ? " > " : ">",
        Combinator.NextSibling => spaced ? " + " : "+",
        Combinator.SubsequentSibling => spaced ? " ~ " : "~",
        Combinator.Column => spaced ? " || " : "||",
        _ => " ",
    };

    private static void WriteCompound(StringBuilder sb, Compound k)
    {
        foreach (Simple s in k.Parts)
        {
            // CSSOM: a universal selector with no namespace is left out when other simple selectors follow it.
            if (s.Kind == SimpleKind.Type && s.IsUniversal && s.Ns is NsKind.Unspecified or NsKind.Any && k.Parts.Length > 1)
            {
                continue;
            }

            WriteSimple(sb, s);
        }
    }

    private static void WriteSimple(StringBuilder sb, Simple s)
    {
        switch (s.Kind)
        {
            case SimpleKind.Type:
                WriteNs(sb, s);
                sb.Append(s.IsUniversal ? "*" : CssText.Ident(s.Name));
                break;
            case SimpleKind.Id:
                sb.Append('#').Append(CssText.Ident(s.Name));
                break;
            case SimpleKind.Class:
                sb.Append('.').Append(CssText.Ident(s.Name));
                break;
            case SimpleKind.Attribute:
                sb.Append('[');
                WriteNs(sb, s);
                sb.Append(CssText.Ident(s.Name));
                if (s.Op != AttrOp.Exists)
                {
                    sb.Append(s.Op switch
                    {
                        AttrOp.Equals => "=",
                        AttrOp.Includes => "~=",
                        AttrOp.DashMatch => "|=",
                        AttrOp.Prefix => "^=",
                        AttrOp.Suffix => "$=",
                        _ => "*=",
                    });
                    sb.Append(CssSerializer.SerializeString(s.Value));
                    if (s.Flag == 1)
                    {
                        sb.Append(" i");
                    }
                    else if (s.Flag == 2)
                    {
                        sb.Append(" s");
                    }
                }

                sb.Append(']');
                break;
            case SimpleKind.PseudoClass:
                sb.Append(':').Append(s.Name);
                if (s.ArgText is not null)
                {
                    sb.Append('(').Append(s.ArgText).Append(')');
                }
                else if (s.Sub is not null)
                {
                    sb.Append('(');
                    Write(sb, s.Sub);
                    sb.Append(')');
                }

                break;
            case SimpleKind.Logical:
                sb.Append(':').Append(s.Name).Append('(');
                Write(sb, s.Sub!);
                sb.Append(')');
                break;
            case SimpleKind.Nth:
                sb.Append(':').Append(s.Name).Append('(');
                AnPlusB(sb, s.A, s.B);
                if (s.Sub is not null)
                {
                    sb.Append(" of ");
                    Write(sb, s.Sub);
                }

                sb.Append(')');
                break;
            case SimpleKind.Lang:
                sb.Append(":lang(");
                for (int i = 0; i < s.Strings!.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    string v = s.Strings[i];
                    string id = CssText.Ident(v);
                    sb.Append(v.Length > 0 && id == v ? id : CssSerializer.SerializeString(v));
                }

                sb.Append(')');
                break;
            case SimpleKind.Dir:
                sb.Append(":dir(").Append(s.Value).Append(')');
                break;
            case SimpleKind.Heading:
                sb.Append(":heading(");
                for (int i = 0; i < s.Pairs!.Length; i += 2)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    AnPlusB(sb, s.Pairs[i], s.Pairs[i + 1]);
                }

                sb.Append(')');
                break;
            case SimpleKind.PseudoElement:
                sb.Append("::").Append(s.Name);
                if (s.Sub is not null)
                {
                    sb.Append('(');
                    Write(sb, s.Sub);
                    sb.Append(')');
                }
                else if (s.Strings is not null)
                {
                    sb.Append('(');
                    for (int i = 0; i < s.Strings.Length; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(' ');
                        }

                        sb.Append(CssText.Ident(s.Strings[i]));
                    }

                    sb.Append(')');
                }
                else if (s.ArgText is not null)
                {
                    sb.Append('(').Append(s.ArgText).Append(')');
                }

                break;
        }
    }

    private static void WriteNs(StringBuilder sb, Simple s)
    {
        switch (s.Ns)
        {
            case NsKind.Any when !(s.Kind == SimpleKind.Type && s.IsUniversal):
                sb.Append("*|");
                break;
            case NsKind.None when s.Kind == SimpleKind.Type:
                sb.Append('|');
                break;
            case NsKind.Specific when s.NsPrefix is not null:
                sb.Append(CssText.Ident(s.NsPrefix)).Append('|');
                break;
        }
    }

    /// <summary>A lone b when a is 0; "n" and "-n" for a of 1 and -1; the sign of b always written.</summary>
    internal static void AnPlusB(StringBuilder sb, int a, int b)
    {
        if (a == 0)
        {
            sb.Append(b.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (a == 1)
        {
            sb.Append('n');
        }
        else if (a == -1)
        {
            sb.Append("-n");
        }
        else
        {
            sb.Append(a.ToString(CultureInfo.InvariantCulture)).Append('n');
        }

        if (b > 0)
        {
            sb.Append('+').Append(b.ToString(CultureInfo.InvariantCulture));
        }
        else if (b < 0)
        {
            sb.Append(b.ToString(CultureInfo.InvariantCulture));
        }
    }
}
