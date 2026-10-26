using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>The three result groups of axe-core's output that name nodes. Inapplicable rules have no nodes.</summary>
internal enum AxeClass
{
    None = 0,
    Violation = 1,
    Pass = 2,
    Incomplete = 3,
}

/// <summary>One node axe reported for a rule: where, in axe's own terms (a unique selector and an XPath into the page).</summary>
/// <param name="Rule">The rule id.</param>
/// <param name="Class">The group the node was reported in.</param>
/// <param name="Selector">The node's selector, or null when the target is nested (frames, shadow roots).</param>
/// <param name="XPath">The node's XPath, or null when absent or nested.</param>
/// <param name="Html">The node's serialized start tag as axe reports it (display only).</param>
/// <param name="Message">The first check message axe gave for the node (display only; it says why axe decided as it did).</param>
/// <param name="Nested">The target crosses a frame or shadow root, which our single-document check cannot reach.</param>
internal sealed record AxeNodeRef(string Rule, AxeClass Class, string? Selector, string? XPath, string Html, bool Nested, string Message);

/// <summary>What axe said about one page: every rule that ran and the nodes it reported.</summary>
internal sealed class AxeResultSet
{
    public string Version { get; init; } = string.Empty;

    /// <summary>Every rule id axe ran (it reports one entry per rule in some group, including inapplicable).</summary>
    public HashSet<string> RulesRan { get; } = new(StringComparer.Ordinal);

    public List<AxeNodeRef> Nodes { get; } = [];
}

/// <summary>Reads the <c>axe.json</c> a capture holds (schema <c>onehtml-capture/1/axe</c>).</summary>
internal static class AxeReader
{
    private static readonly (string Group, AxeClass Class)[] s_groups =
    [
        ("violations", AxeClass.Violation), ("passes", AxeClass.Pass), ("incomplete", AxeClass.Incomplete),
    ];

    public static AxeResultSet Read(ReadOnlyMemory<byte> json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        JsonElement results = root.GetProperty("results");
        var set = new AxeResultSet { Version = root.TryGetProperty("axe_version", out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty };
        if (results.TryGetProperty("inapplicable", out JsonElement inapplicable))
        {
            foreach (JsonElement r in inapplicable.EnumerateArray())
            {
                set.RulesRan.Add(r.GetProperty("id").GetString()!);
            }
        }

        foreach ((string group, AxeClass cls) in s_groups)
        {
            if (!results.TryGetProperty(group, out JsonElement list))
            {
                continue;
            }

            foreach (JsonElement r in list.EnumerateArray())
            {
                string id = r.GetProperty("id").GetString()!;
                set.RulesRan.Add(id);
                foreach (JsonElement n in r.GetProperty("nodes").EnumerateArray())
                {
                    string? selector = null, xpath = null;
                    bool nested = false;
                    if (n.TryGetProperty("target", out JsonElement t) && t.ValueKind == JsonValueKind.Array)
                    {
                        // A target is one selector per document level; an inner array means a shadow-root path.
                        if (t.GetArrayLength() == 1 && t[0].ValueKind == JsonValueKind.String)
                        {
                            selector = t[0].GetString();
                        }
                        else
                        {
                            nested = true;
                        }
                    }

                    if (n.TryGetProperty("xpath", out JsonElement x) && x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 1 && x[0].ValueKind == JsonValueKind.String)
                    {
                        xpath = x[0].GetString();
                    }

                    string html = n.TryGetProperty("html", out JsonElement h) && h.ValueKind == JsonValueKind.String ? h.GetString()! : string.Empty;
                    string message = string.Empty;
                    foreach (string kind in new[] { "any", "all", "none" })
                    {
                        if (message.Length == 0 && n.TryGetProperty(kind, out JsonElement checks) && checks.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement c in checks.EnumerateArray())
                            {
                                if (c.TryGetProperty("message", out JsonElement msg) && msg.ValueKind == JsonValueKind.String)
                                {
                                    message = kind + ": " + c.GetProperty("id").GetString() + ": " + msg.GetString();
                                    break;
                                }
                            }
                        }
                    }

                    set.Nodes.Add(new AxeNodeRef(id, cls, selector, xpath, html, nested, message));
                }
            }
        }

        return set;
    }
}

/// <summary>
/// Finds the element axe meant in our parse of the same serialized DOM. The XPath comes first (it is positional, so it does not depend
/// on how a selector was abbreviated); the selector is the second opinion and the fallback.
/// </summary>
internal static class AxeTargets
{
    /// <summary>
    /// Resolves a node. <paramref name="how"/> is <c>xpath</c>, <c>selector</c> or <c>xpath+selector</c> (both agree) on success, and
    /// <c>xpath!selector</c> when they name different elements (the XPath wins); on failure <c>nested</c> or <c>unresolved</c>.
    /// </summary>
    public static Node Resolve(Document doc, AxeNodeRef n, out string how)
    {
        if (n.Nested)
        {
            how = "nested";
            return default;
        }

        Node byPath = n.XPath is null ? default : ByXPath(doc, n.XPath);
        Node bySel = default;
        bool selectorError = false;
        if (n.Selector is not null)
        {
            try
            {
                bySel = doc.Root.QuerySelector(n.Selector);
            }
            catch (Exception e) when (e is Selectors.SelectorSyntaxException or FormatException or ArgumentException or InvalidOperationException)
            {
                // A selector our parser rejects is a finding about the selector parser, not about the rule; fall back on the XPath.
                bySel = default;
                selectorError = true;
            }
        }

        if (!byPath.IsNull)
        {
            how = selectorError ? "xpath(selector-error)" : bySel.IsNull ? "xpath" : bySel.Equals(byPath) ? "xpath+selector" : "xpath!selector";
            return byPath;
        }

        if (!bySel.IsNull)
        {
            how = "selector";
            return bySel;
        }

        how = "unresolved";
        return default;
    }

    /// <summary>
    /// Evaluates the XPath subset axe emits: absolute paths of <c>name</c>, <c>name[n]</c> steps, an optional first step
    /// <c>//name[@id='x']</c>, and <c>*[name()='x']</c>/<c>*[local-name()='x']</c> for foreign elements. Returns the null node when a step misses
    /// or the path is not in the subset.
    /// </summary>
    public static Node ByXPath(Document doc, string xpath)
    {
        List<string>? steps = Split(xpath, out bool anywhere);
        if (steps is null || steps.Count == 0)
        {
            return default;
        }

        Node current;
        int first = 0;
        if (anywhere)
        {
            if (!ParseStep(steps[0], out string? name0, out int index0, out string? id0) || id0 is null || index0 != 0)
            {
                return default;
            }

            current = default;
            foreach (Node e in doc.Root.DescendantElements)
            {
                if ((name0 is null || NameIs(e, name0)) && string.Equals(e.GetAttribute("id"), id0, StringComparison.Ordinal))
                {
                    if (!current.IsNull)
                    {
                        return default; // an id that is not unique cannot anchor a path
                    }

                    current = e;
                }
            }

            first = 1;
        }
        else
        {
            current = doc.Root;
        }

        for (int i = first; i < steps.Count; i++)
        {
            if (current.IsNull || !ParseStep(steps[i], out string? name, out int index, out string? id) || id is not null)
            {
                return default;
            }

            int seen = 0;
            Node match = default;
            for (Node c = current.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (!c.IsElement || (name is not null && !NameIs(c, name)))
                {
                    continue;
                }

                seen++;
                if (seen == (index == 0 ? 1 : index))
                {
                    match = c;
                    break;
                }
            }

            current = match;
        }

        return current;
    }

    private static bool NameIs(Node e, string name) => string.Equals(e.LocalName, name, StringComparison.Ordinal);

    private static List<string>? Split(string xpath, out bool anywhere)
    {
        anywhere = false;
        if (!xpath.StartsWith('/'))
        {
            return null;
        }

        int pos = 1;
        if (xpath.StartsWith("//", StringComparison.Ordinal))
        {
            anywhere = true;
            pos = 2;
        }

        var steps = new List<string>();
        int start = pos, depth = 0;
        char quote = '\0';
        for (int i = pos; i <= xpath.Length; i++)
        {
            char c = i < xpath.Length ? xpath[i] : '/';
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
            }
            else if (c == '/' && depth == 0)
            {
                if (i == start)
                {
                    return null; // an empty step ("//" in the middle) is outside the subset
                }

                steps.Add(xpath[start..i]);
                start = i + 1;
            }
        }

        return steps;
    }

    // name is null for "*" without a name() test. index is 0 when the step has no position. id is set for [@id='x'].
    private static bool ParseStep(string step, out string? name, out int index, out string? id)
    {
        name = null;
        index = 0;
        id = null;
        int br = step.IndexOf('[', StringComparison.Ordinal);
        string head = br < 0 ? step : step[..br];
        string pred = br < 0 ? string.Empty : step[(br + 1)..^1];
        if (br >= 0 && !step.EndsWith(']'))
        {
            return false;
        }

        if (head != "*")
        {
            name = head;
        }

        if (pred.Length == 0)
        {
            return name is not null;
        }

        if (int.TryParse(pred, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0)
        {
            index = n;
            return name is not null;
        }

        if (pred.StartsWith("@id=", StringComparison.Ordinal) && Unquote(pred[4..]) is { } v)
        {
            id = v;
            return true;
        }

        foreach (string fn in new[] { "name()=", "local-name()=" })
        {
            if (head == "*" && pred.StartsWith(fn, StringComparison.Ordinal) && Unquote(pred[fn.Length..]) is { } nm)
            {
                name = nm;
                return true;
            }
        }

        return false;
    }

    private static string? Unquote(string s) =>
        s.Length >= 2 && (s[0] is '\'' or '"') && s[^1] == s[0] ? s[1..^1] : null;
}
