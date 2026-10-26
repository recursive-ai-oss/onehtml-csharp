using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Everything a tree says, as text, so two trees can be compared field by field.</summary>
internal static class TreeDump
{
    public static string Of(AccessibilityTree tree)
    {
        var sb = new StringBuilder();
        foreach (AxNode n in tree.Nodes)
        {
            sb.Append(n.Id).Append(' ').Append(n.Kind).Append(" dom=").Append(n.DomNode.Index).Append(" parent=").Append(n.Parent?.Id ?? -1);
            sb.Append(" role=").Append(n.Role).Append('/').Append(n.AriaRole?.Name).Append('/').Append(n.RoleOrigin);
            sb.Append(" ignored=").Append(n.IsIgnored).Append('[').AppendJoin(',', n.IgnoredReasons).Append(']');
            Text(sb, " name", n.Name);
            Text(sb, " desc", n.Description);
            sb.Append(" hidden=").Append((int)n.Hidden.Reasons).Append('/').Append(n.Hidden.IsInherited).Append('/').Append(n.Hidden.Origin.IsNull ? -1 : n.Hidden.Origin.Index);
            sb.Append(" focus=").Append(n.Focus.IsFocusable).Append('/').Append(n.Focus.TabIndex).Append('/').Append(n.Focus.SequentialIndex);
            AxStates s = n.States;
            sb.Append(" st=").Append(s.Expanded).Append(s.Checked).Append(s.Selected).Append(s.Pressed).Append(s.Disabled).Append(s.Required).Append(s.Invalid)
                .Append(s.ReadOnly).Append(s.Level).Append('/').Append(s.PosInSet).Append('/').Append(s.SetSize).Append('/').Append(s.Value).Append('/').Append(s.ValueText)
                .Append(s.Live).Append(s.Modal).Append(s.Current).Append(s.HasPopup).Append(s.Url).Append(s.Editable).Append(s.RoleDescription);
            foreach (AxRelation r in n.Relations)
            {
                sb.Append(" rel:").Append(r.Kind).Append('=');
                foreach (Node e in r.Elements)
                {
                    sb.Append(e.Index).Append(',');
                }

                sb.Append('>');
                foreach (AxNode t in r.Targets)
                {
                    sb.Append(t.Id).Append(',');
                }

                sb.Append('!').AppendJoin(',', r.BrokenIds);
            }

            foreach (AxDiagnostic d in n.Diagnostics)
            {
                sb.Append(" diag:").Append(d.Kind).Append('/').Append(d.Node.Index).Append('/').Append(d.Message);
            }

            sb.Append(" kids=");
            foreach (AxNode c in n.Children)
            {
                sb.Append(c.Id).Append(',');
            }

            sb.Append('\n');
        }

        sb.Append("focusOrder=");
        foreach (AxNode f in tree.FocusOrder)
        {
            sb.Append(f.Id).Append(',');
        }

        sb.Append("\nlimit=").Append(tree.LimitReached).Append(" diagnostics=").Append(tree.Diagnostics.Count).Append('\n');
        foreach (AxDiagnostic d in tree.Diagnostics)
        {
            sb.Append(d.Kind).Append('/').Append(d.Node.Index).Append('/').Append(d.Message).Append('\n');
        }

        sb.Append(tree.ToChromeJson());
        return sb.ToString();
    }

    private static void Text(StringBuilder sb, string label, AxText t)
    {
        sb.Append(label).Append("='").Append(t.Value).Append("'/").Append(t.Source).Append('/').Append(t.Attribute).Append('[');
        foreach (Node r in t.RelatedElements)
        {
            sb.Append(r.Index.ToString(CultureInfo.InvariantCulture)).Append(',');
        }

        sb.Append(']');
    }

    /// <summary>The first line that differs between two dumps, for a readable failure.</summary>
    public static string FirstDifference(string a, string b)
    {
        string[] x = a.Split('\n');
        string[] y = b.Split('\n');
        for (int i = 0; i < System.Math.Max(x.Length, y.Length); i++)
        {
            string l = i < x.Length ? x[i] : "<end>";
            string r = i < y.Length ? y[i] : "<end>";
            if (l != r)
            {
                return "line " + i + "\n  incremental: " + Cut(l) + "\n  full:        " + Cut(r);
            }
        }

        return "none";
    }

    private static string Cut(string s) => s.Length > 600 ? s[..600] + "..." : s;

    public static IEnumerable<string> Lines(string dump) => dump.Split('\n');
}
