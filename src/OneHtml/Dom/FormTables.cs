using System.Collections.Generic;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Dom;

/// <summary>
/// Document-wide facts about form controls that need one pass over the tree: which submit button is each form's default
/// button, and which radio button groups have a checked member (HTML 4.10).
/// </summary>
internal sealed class FormTables
{
    private FormTables(HashSet<int> defaults, Dictionary<(int Owner, int Root, string Name), bool> radios, Dictionary<int, int> groupOf, int[][] members, int[] baseChecked)
    {
        DefaultButtons = defaults;
        RadioGroupChecked = radios;
        RadioGroupOf = groupOf;
        RadioGroupMembers = members;
        RadioGroupBaseChecked = baseChecked;
    }

    public HashSet<int> DefaultButtons { get; }

    /// <summary>(form owner or -1, root of the tree, name) to whether some member is checked.</summary>
    public Dictionary<(int Owner, int Root, string Name), bool> RadioGroupChecked { get; }

    /// <summary>Radio button (arena index) to its named group's number; radios without a name are in no group.</summary>
    public Dictionary<int, int> RadioGroupOf { get; }

    /// <summary>The members of each named radio group, in document order, by group number.</summary>
    public int[][] RadioGroupMembers { get; }

    /// <summary>The number of members of each group whose <c>checked</c> attribute is present, by group number.</summary>
    public int[] RadioGroupBaseChecked { get; }

    public static FormTables Build(Document doc)
    {
        Arena a = doc.Arena;
        var defaults = new HashSet<int>();
        var formsDone = new HashSet<int>();
        var radios = new Dictionary<(int Owner, int Root, string Name), bool>();
        var groupIds = new Dictionary<(int Owner, int Root, string Name), int>();
        var groupOf = new Dictionary<int, int>();
        var members = new List<List<int>>();
        var baseChecked = new List<int>();
        for (int i = Node.NextPreorder(a, 0, 0); i >= 0; i = Node.NextPreorder(a, i, 0))
        {
            if (a.Kind[i] != (byte)NodeKind.Element || a.Ns[i] != (byte)ElementNamespace.Html)
            {
                continue;
            }

            var n = new Node(doc, i);
            if (ElementState.IsSubmitButton(n))
            {
                int owner = ElementState.FormOwner(n);
                if (owner >= 0 && formsDone.Add(owner))
                {
                    defaults.Add(i);
                }
            }
            else if (ElementState.GetInputType(n) == InputType.Radio)
            {
                string name = ElementState.AttrString(n, "name");
                if (name.Length > 0)
                {
                    var key = (ElementState.FormOwner(n), Node.RootIndex(a, i), name);
                    radios.TryGetValue(key, out bool had);
                    bool isChecked = ElementState.Has(n, "checked");
                    radios[key] = had || isChecked;
                    if (!groupIds.TryGetValue(key, out int g))
                    {
                        groupIds[key] = g = members.Count;
                        members.Add([]);
                        baseChecked.Add(0);
                    }

                    groupOf[i] = g;
                    members[g].Add(i);
                    baseChecked[g] += isChecked ? 1 : 0;
                }
            }
        }

        var memberArrays = new int[members.Count][];
        for (int g = 0; g < memberArrays.Length; g++)
        {
            memberArrays[g] = [.. members[g]];
        }

        return new FormTables(defaults, radios, groupOf, memberArrays, [.. baseChecked]);
    }
}

/// <summary>The state of an <c>input</c> element's <c>type</c> attribute (HTML 4.10.5.1).</summary>
internal enum InputType : byte
{
    NotInput,
    Text,
    Hidden,
    Search,
    Tel,
    Url,
    Email,
    Password,
    Date,
    Month,
    Week,
    Time,
    DatetimeLocal,
    Number,
    Range,
    Color,
    Checkbox,
    Radio,
    File,
    Submit,
    Image,
    Reset,
    Button,
}
