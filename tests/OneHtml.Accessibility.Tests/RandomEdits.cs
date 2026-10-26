using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Random edit transactions over a document, aimed at what the accessibility tree reads.</summary>
internal static class RandomEdits
{
    private static readonly string[] Snippets =
    [
        "<span id=ins1>inserted</span>",
        "<button id=ins2 aria-labelledby=ins1>Go</button>",
        "<label for=ins3>Label</label><input id=ins3>",
        "<ul><li>one<li>two</ul>",
        "<div role=group aria-label=g>x<b>y</b></div>",
        "<p hidden>hidden</p>",
        "<a href=/x>link</a> ",
        "<div aria-owns=ins1 id=ins4>owner</div>",
        "text only",
        "<h2>Head</h2>",
        "<img alt=pic src=a.png>",
        "<table><tr><th>h<td>d</table>",
    ];

    private static readonly string[] Css =
    [
        "p::before{content:'<< '} p::after{content:' >>'}",
        "div.c1::before{content:attr(title)}",
        "[hidden]{display:block}",
        ".c2{display:none}",
        ".c3{visibility:hidden}",
        "span{display:block}",
        "li::before{content:counter(item) '. ';counter-increment:item} ul{counter-reset:item}",
        "a:hover::before{content:'hover '}",
        "",
    ];

    public static List<Edit> Next(Document doc, Random rng, int count)
    {
        var elements = new List<Node>();
        var texts = new List<Node>();
        var ids = new List<string>();
        foreach (Node n in doc.Root.Descendants)
        {
            if (n.IsElement)
            {
                elements.Add(n);
                if (n.Id is { Length: > 0 } id)
                {
                    ids.Add(id);
                }
            }
            else if (n.Kind == NodeKind.Text)
            {
                texts.Add(n);
            }
        }

        var edits = new List<Edit>();
        if (elements.Count == 0)
        {
            return edits;
        }

        for (int i = 0; i < count; i++)
        {
            Node el = elements[rng.Next(elements.Count)];
            switch (rng.Next(14))
            {
                case 0:
                case 1:
                case 2:
                    edits.Add(Edit.SetAttribute(el, AttrName(rng), AttrValue(rng, ids)));
                    break;
                case 3:
                    if (el.Attributes.Count > 0)
                    {
                        edits.Add(Edit.RemoveAttribute(el, el.Attributes[rng.Next(el.Attributes.Count)].LocalName));
                    }

                    break;
                case 4:
                    if (texts.Count > 0)
                    {
                        edits.Add(Edit.ReplaceText(texts[rng.Next(texts.Count)], "changed " + rng.Next(100)));
                    }

                    break;
                case 5:
                    edits.Add(Edit.Append(el, Snippets[rng.Next(Snippets.Length)]));
                    break;
                case 6:
                    edits.Add(Edit.Prepend(el, Snippets[rng.Next(Snippets.Length)]));
                    break;
                case 7:
                    edits.Add(Edit.InsertAfter(el, Snippets[rng.Next(Snippets.Length)]));
                    break;
                case 8:
                    edits.Add(Edit.Remove(el));
                    break;
                case 9:
                    edits.Add(Edit.Unwrap(el));
                    break;
                case 10:
                    edits.Add(Edit.Wrap(el, rng.Next(2) == 0 ? "<div role=group aria-label=w></div>" : "<label></label>"));
                    break;
                case 11:
                    edits.Add(Edit.SetStyle(el, rng.Next(3) == 0 ? "visibility" : "display", rng.Next(2) == 0 ? "none" : rng.Next(2) == 0 ? "block" : "inline"));
                    break;
                case 12:
                    {
                        Node style = FirstStyle(doc);
                        if (!style.IsNull)
                        {
                            edits.Add(Edit.ReplaceText(style, Css[rng.Next(Css.Length)] + " " + Css[rng.Next(Css.Length)]));
                        }
                        else
                        {
                            edits.Add(Edit.Prepend(el, "<style>" + Css[rng.Next(Css.Length)] + "</style>"));
                        }

                        break;
                    }

                default:
                    edits.Add(Edit.Replace(el, Snippets[rng.Next(Snippets.Length)]));
                    break;
            }
        }

        return edits;
    }

    private static Node FirstStyle(Document doc)
    {
        foreach (Node e in doc.Root.DescendantElements)
        {
            if (e.LocalName == "style" && e.Namespace == ElementNamespace.Html)
            {
                return e;
            }
        }

        return default;
    }

    private static string AttrName(Random rng) => rng.Next(18) switch
    {
        0 => "id",
        1 => "class",
        2 => "aria-label",
        3 => "aria-labelledby",
        4 => "aria-describedby",
        5 => "aria-owns",
        6 => "aria-hidden",
        7 => "hidden",
        8 => "role",
        9 => "tabindex",
        10 => "title",
        11 => "alt",
        12 => "for",
        13 => "disabled",
        14 => "aria-disabled",
        15 => "value",
        16 => "aria-expanded",
        _ => "aria-controls",
    };

    private static string AttrValue(Random rng, List<string> ids)
    {
        string pick = ids.Count > 0 && rng.Next(3) > 0 ? ids[rng.Next(ids.Count)] : "ins" + (1 + rng.Next(4));
        return rng.Next(12) switch
        {
            0 => pick,
            1 => pick + " " + pick,
            2 => "true",
            3 => "button",
            4 => "heading",
            5 => "none",
            6 => "0",
            7 => "-1",
            8 => "c1 c2 c3",
            9 => "Some label " + rng.Next(50),
            10 => "",
            _ => pick,
        };
    }
}
