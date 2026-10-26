using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// Whole-document questions the state of one element needs: they read other elements, so the answers are worked out once per tree
/// build and kept.
/// </summary>
internal sealed class StateHelpers
{
    private readonly Document _doc;
    private readonly Dictionary<int, bool> _siblingSelected = [];
    private readonly Dictionary<int, byte> _editableMode = [];
    private Dictionary<(int Form, string Name), bool>? _radioGroups;

    private readonly Func<Node, bool> _isContainer;

    /// <summary>Creates the helpers for one tree build; <paramref name="isSelectionContainer"/> says whether an element is a list box, tab list or tree.</summary>
    public StateHelpers(Document doc, Func<Node, bool> isSelectionContainer)
    {
        _doc = doc;
        _isContainer = isSelectionContainer;
    }

    /// <summary>
    /// True when an item of the same list box, tab list or tree (the nearest ancestor the tree builder counts as one, else the parent)
    /// carries an <c>aria-selected</c> attribute, whatever its value. Chromium leaves the unselected state off an item that says nothing when
    /// the others do say (profile entry <c>selected-states</c>).
    /// </summary>
    public bool SiblingHasAriaSelected(Node el)
    {
        Node container = el.ParentElement;
        bool found = false;
        int steps = 0;
        for (Node a = el.ParentElement; !a.IsNull && a.IsElement && steps++ < 100_000; a = a.ParentElement)
        {
            if (_isContainer(a))
            {
                container = a;
                found = true;
                break;
            }
        }

        if (container.IsNull || !found)
        {
            // An item outside any list box, tab list or tree stands alone.
            return el.HasAttribute("aria-selected");
        }

        if (_siblingSelected.TryGetValue(container.Index, out bool known))
        {
            return known;
        }

        bool any = false;
        int visited = 0;
        foreach (Node d in container.DescendantElements)
        {
            if (visited++ > 200_000)
            {
                break;
            }

            if (d.HasAttribute("aria-selected"))
            {
                any = true;
                break;
            }
        }

        _siblingSelected[container.Index] = any;
        return any;
    }

    /// <summary>The editing state an element has from <c>contenteditable</c> on itself or an ancestor (HTML section 6.8), or null.</summary>
    public string? InheritedEditable(Node el)
    {
        // 0 none, 1 richtext, 2 plaintext; unknown elements are filled in from the first known ancestor down.
        var chain = new List<Node>();
        byte mode = 0;
        int steps = 0;
        for (Node a = el; !a.IsNull && a.IsElement; a = a.ParentElement)
        {
            if (_editableMode.TryGetValue(a.Index, out byte cached))
            {
                mode = cached;
                break;
            }

            if (steps++ > 100_000)
            {
                break;
            }

            chain.Add(a);
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            Node a = chain[i];
            if (a.Namespace == ElementNamespace.Html && a.GetAttribute("contenteditable") is { } ce)
            {
                if (ce.Length == 0 || TreeText.EqualsIgnoreCase(ce, "true"))
                {
                    mode = 1;
                }
                else if (TreeText.EqualsIgnoreCase(ce, "plaintext-only"))
                {
                    mode = 2;
                }
                else if (TreeText.EqualsIgnoreCase(ce, "false"))
                {
                    mode = 0;
                }
            }

            _editableMode[a.Index] = mode;
        }

        return mode switch { 1 => "richtext", 2 => "plaintext", _ => null };
    }

    /// <summary>
    /// True when the radio button belongs to a group (the same name inside the same form, or none) that has a required member and no checked one
    /// (HTML 4.10.5.1.16). Chromium reports every button of such a group as invalid.
    /// </summary>
    public bool RadioGroupMissing(Node radio)
    {
        _radioGroups ??= IndexRadioGroups();
        string name = radio.GetAttribute("name") ?? string.Empty;
        if (name.Length == 0)
        {
            return HtmlSemantics.Has(radio, "required") && !HtmlSemantics.Has(radio, "checked");
        }

        return _radioGroups.TryGetValue((FormOf(radio), name), out bool missing) && missing;
    }

    private static int FormOf(Node el)
    {
        int steps = 0;
        for (Node a = el.ParentElement; !a.IsNull && steps++ < 100_000; a = a.ParentElement)
        {
            if (a.Namespace == ElementNamespace.Html && a.LocalName == "form")
            {
                return a.Index;
            }
        }

        return -1;
    }

    private Dictionary<(int, string), bool> IndexRadioGroups()
    {
        var required = new HashSet<(int, string)>();
        var checkedGroups = new HashSet<(int, string)>();
        foreach (Node e in _doc.Root.DescendantElements)
        {
            if (e.Namespace != ElementNamespace.Html || e.LocalName != "input" || HtmlSemantics.GetInputType(e) != HtmlInputType.Radio
                || e.GetAttribute("name") is not { Length: > 0 } name)
            {
                continue;
            }

            (int, string) key = (FormOf(e), name);
            if (HtmlSemantics.Has(e, "required"))
            {
                required.Add(key);
            }

            if (HtmlSemantics.Has(e, "checked"))
            {
                checkedGroups.Add(key);
            }
        }

        var result = new Dictionary<(int, string), bool>();
        foreach ((int, string) key in required)
        {
            result[key] = !checkedGroups.Contains(key);
        }

        return result;
    }
}
