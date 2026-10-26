using System;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>The labeled control of a <c>label</c> element (HTML 4.10.4), used to give a label's control the label's hover and active state.</summary>
internal static class LabelControl
{
    /// <summary>
    /// The labeled control of the nearest <c>label</c> at or above <paramref name="node"/>, or the null node. With a
    /// <c>for</c> attribute it is the first element in tree order with that id, if labelable; otherwise it is the first
    /// labelable descendant of the label.
    /// </summary>
    public static Node Of(Node node)
    {
        if (node.IsNull || !node.IsElement)
        {
            return default;
        }

        Document doc = node.Document!;
        Arena a = doc.Arena;
        int label = -1;
        for (int i = node.Index; i >= 0 && a.Kind[i] == (byte)NodeKind.Element; i = a.Parent[i])
        {
            if (IsLabel(new Node(doc, i)))
            {
                label = i;
                break;
            }
        }

        if (label < 0)
        {
            return default;
        }

        var labelNode = new Node(doc, label);
        string? forId = labelNode.GetAttribute("for");
        if (forId is not null)
        {
            int target = doc.Index.FirstWithId(forId);
            return target >= 0 && IsLabelable(new Node(doc, target)) ? new Node(doc, target) : default;
        }

        foreach (Node d in labelNode.DescendantElements)
        {
            if (IsLabelable(d))
            {
                return d;
            }
        }

        return default;
    }

    private static bool IsLabel(Node n) => n.Namespace == ElementNamespace.Html && n.LocalName == "label";

    private static bool IsLabelable(Node n)
    {
        if (!n.IsElement || n.Namespace != ElementNamespace.Html)
        {
            return false;
        }

        return n.LocalName switch
        {
            "button" or "meter" or "output" or "progress" or "select" or "textarea" => true,
            "input" => !string.Equals(n.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
