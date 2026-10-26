using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// Runs the accessible-name and property pages of web-platform-tests statically. Each page states its expectations in the
/// markup (<c>data-expectedlabel</c>, <c>data-expecteddescription</c>, <c>data-expectedproperties</c>, an expected subtree in
/// a script call, or the JSON of a manual test), and those are checked against <see cref="AccessibilityTree"/>. Pages whose
/// markup is built or changed by script are run as far as they can be and otherwise counted as not applicable, each with its
/// reason. Known failures are in <c>testdata/wpt/accname/expectations.tsv</c>.
/// </summary>
public sealed partial class WptAccnameTests
{
    private static readonly string Root = Path.Combine(TestPaths.TestData, "wpt", "accname");

    internal sealed record Case(string Name, string Expected, string Actual, bool Pass, string? NotApplicable);

    public static IEnumerable<string> Files() =>
        Directory.EnumerateFiles(Root, "*.html", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal);

    [GeneratedRegex(@"<script(?<attrs>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline, 2000)]
    private static partial Regex ScriptPattern();

    [GeneratedRegex(@"get_computed_label\(document\.getElementById\('(?<id>\w+)'\)\);\s*assert_equals\(label, ""(?<want>[^""]*)""\);\s*\}, ""(?<name>[^""]+)""", RegexOptions.None, 2000)]
    private static partial Regex LabelAssertPattern();

    [GeneratedRegex(@"verifyAccessibilitySubtree\(\s*(?<q>['""])(?<sel>[^'""]+)\k<q>\s*,\s*(?=\{)", RegexOptions.None, 2000)]
    private static partial Regex SubtreePattern();

    private static string Norm(string s) => TreeNorm(s);

    private static string TreeNorm(string s)
    {
        var sb = new StringBuilder();
        bool pending = false;
        foreach (char c in s)
        {
            if (c is ' ' or '\t' or '\n' or '\f' or '\r')
            {
                pending = sb.Length > 0;
                continue;
            }

            if (pending)
            {
                sb.Append(' ');
                pending = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    internal static AccessibilityTree BuildTree(Document doc) => AccessibilityTree.Build(doc, DocumentStyles.Create(doc));

    private static readonly string[] ScriptBuiltMarkers = ["createElement", "innerHTML", "attachShadow", "insertAdjacent", "appendChild", "setAttribute(", ".remove()", "removeAttribute", ".hidden =", ".style."];

    internal static List<Case> Run(string relative)
    {
        string html = File.ReadAllText(Path.Combine(Root, relative), Encoding.UTF8);
        var cases = new List<Case>();
        if (relative.EndsWith("-manual.html", StringComparison.Ordinal))
        {
            RunManual(html, cases);
            return cases;
        }

        string? scriptReason = null;
        string allScript = string.Empty;
        foreach (Match sm in ScriptPattern().Matches(html))
        {
            if (sm.Groups["attrs"].Value.Contains("src=", StringComparison.Ordinal))
            {
                continue;
            }

            string body = sm.Groups["body"].Value;
            allScript += body + "\n";
        }

        string? generated = AamAccnameGenerator.Generate(html, allScript);
        string pageHtml = html;
        if (generated is not null)
        {
            int bodyEnd = html.LastIndexOf("</body>", StringComparison.Ordinal);
            pageHtml = "<!doctype html><html><head><title>t</title></head><body><h1>t</h1>" + generated + "</body></html>";
            _ = bodyEnd;
        }
        else
        {
            // What remains of a page's script once the harness calls are removed decides whether the markup is final.
            string rest = Regex.Replace(allScript, @"AriaUtils\.verify\w+\((?:[^;]|\n)*?\);", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));
            foreach (string marker in ScriptBuiltMarkers)
            {
                if (rest.Contains(marker, StringComparison.Ordinal))
                {
                    scriptReason = marker == "attachShadow" ? "shadow DOM is built by script" : "script builds or changes the markup before the assertions (" + marker.TrimEnd('(') + ")";
                    break;
                }
            }
        }

        Document doc = Document.Parse(pageHtml);
        AccessibilityTree tree = BuildTree(doc);
        foreach (Node el in doc.Root.QuerySelectorAll("[data-expectedlabel],[data-expecteddescription],[data-expectedproperties]"))
        {
            string testName = el.GetAttribute("data-testname") ?? el.GetAttribute("data-expectedlabel") ?? el.GetAttribute("data-expecteddescription") ?? el.LocalName;
            AxNode? node = tree.Find(el);
            if (el.GetAttribute("data-expectedlabel") is { } label)
            {
                string actual = node is null ? tree.ComputeName(el).Value : node.Name.Value;
                string expected = Norm(label);
                cases.Add(new Case(testName, expected, Norm(actual), Norm(actual) == expected, scriptReason ?? Ambiguous(expected)));
            }

            if (el.GetAttribute("data-expecteddescription") is { } desc)
            {
                string actual = node is null ? tree.ComputeDescription(el).Value : node.Description.Value;
                cases.Add(new Case(testName + " (description)", Norm(desc), Norm(actual), Norm(actual) == Norm(desc), scriptReason));
            }

            if (el.GetAttribute("data-expectedproperties") is { } props && node is not null)
            {
                foreach (KeyValuePair<string, JsonNode?> kv in (JsonObject)JsonNode.Parse(props)!)
                {
                    string expected = (string)kv.Value!;
                    string actual = Property(node, kv.Key);
                    cases.Add(new Case(testName + " [" + kv.Key + "]", expected, actual, actual == expected, scriptReason ?? Ambiguous(expected)));
                }
            }

            if (el.HasAttribute("data-expectedrole") && el.GetAttribute("data-expectedlabel") is not null && node is not null)
            {
                string expected = el.GetAttribute("data-expectedrole")!;
                cases.Add(new Case(testName + " (role)", expected, node.Role, node.Role == expected, scriptReason));
            }
        }

        foreach (Match m in LabelAssertPattern().Matches(allScript))
        {
            Node target = doc.GetElementById(m.Groups["id"].Value);
            AxNode? node = target.IsNull ? null : tree.Find(target);
            string actual = node is null ? "<not in the tree>" : node.Name.Value;
            cases.Add(new Case(m.Groups["name"].Value, m.Groups["want"].Value, actual, actual == m.Groups["want"].Value, scriptReason));
        }

        foreach (Match m in SubtreePattern().Matches(allScript))
        {
            int brace = m.Index + m.Length;
            JsonNode expectedTree = JsLiteral.Parse(JsLiteral.Balanced(allScript, brace))!;
            Node target = doc.Root.QuerySelector(m.Groups["sel"].Value);
            AxNode? start = target.IsNull ? null : tree.Find(target);
            string name = "accessibility tree for " + m.Groups["sel"].Value;
            string? diff = start is null ? "not in the tree" : CompareSubtree(start, expectedTree, m.Groups["sel"].Value);
            cases.Add(new Case(name, "matches", diff ?? "matches", diff is null, scriptReason));
        }

        return cases;
    }

    private static string? Ambiguous(string expected) =>
        expected.Contains("SPEC_AMBIGUOUS", StringComparison.Ordinal) ? "the page marks the expectation as ambiguous in the specification" : null;

    private static string Property(AxNode n, string key) => key switch
    {
        "role" => n.Role,
        "label" => n.Name.Value,
        "pressed" => n.States.Pressed is { } p ? p.ToString().ToLowerInvariant() : "undefined",
        "checked" => n.States.Checked is { } c ? c.ToString().ToLowerInvariant() : "undefined",
        _ => "<unsupported property " + key + ">",
    };

    /// <summary>Compares the platform-visible subtree: ignored nodes are skipped and their children take their place.</summary>
    private static string? CompareSubtree(AxNode node, JsonNode expected, string position)
    {
        var stack = new Stack<(AxNode Node, JsonNode Expected, string Position)>();
        stack.Push((node, expected, position));
        while (stack.Count > 0)
        {
            (AxNode n, JsonNode e, string pos) = stack.Pop();
            JsonObject o = e.AsObject();
            if (o["role"] is { } r && n.Role != (string)r!)
            {
                return pos + " role " + n.Role + " != " + (string)r!;
            }

            if (o["label"] is { } l && n.Name.Value != (string)l!)
            {
                return pos + " label " + n.Name.Value + " != " + (string)l!;
            }

            if (o["children"] is JsonArray kids)
            {
                var actual = new List<AxNode>();
                Flatten(n, actual);
                if (actual.Count != kids.Count)
                {
                    return pos + " children.length " + actual.Count + " != " + kids.Count;
                }

                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    stack.Push((actual[i], kids[i]!, pos + "[" + i + "]"));
                }
            }
        }

        return null;
    }

    private static void Flatten(AxNode n, List<AxNode> into)
    {
        var stack = new Stack<AxNode>();
        for (int i = n.Children.Count - 1; i >= 0; i--)
        {
            stack.Push(n.Children[i]);
        }

        while (stack.Count > 0)
        {
            AxNode c = stack.Pop();
            if (c.IsIgnored)
            {
                for (int i = c.Children.Count - 1; i >= 0; i--)
                {
                    stack.Push(c.Children[i]);
                }
            }
            else
            {
                into.Add(c);
            }
        }
    }

    private static void RunManual(string html, List<Case> cases)
    {
        // The manual pages describe the platform checks as JSON in a script; the name or description of the element
        // with the given id is the one assertion a static run can make.
        int at = html.IndexOf("new ATTAcomm(", StringComparison.Ordinal);
        if (at < 0)
        {
            return;
        }

        int brace = html.IndexOf('{', at);
        JsonNode json = JsonNode.Parse(JsLiteral.Balanced(html, brace))!;
        string bodyHtml = Regex.Replace(html, @"<script.*?</script>", string.Empty, RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        Document doc = Document.Parse(bodyHtml);
        AccessibilityTree tree = BuildTree(doc);
        string title = (string?)json["title"] ?? "manual";
        foreach (JsonNode? step in json["steps"]!.AsArray())
        {
            string? id = (string?)step!["element"];
            if (id is null || step["test"] is not JsonObject test)
            {
                continue;
            }

            Node el = doc.GetElementById(id);
            AxNode? node = el.IsNull ? null : tree.Find(el);
            foreach (string api in new[] { "ATK", "IAccessible2", "UIA", "AXAPI" })
            {
                if (test[api] is not JsonArray checks)
                {
                    continue;
                }

                bool done = false;
                foreach (JsonNode? chk in checks)
                {
                    JsonArray a = chk!.AsArray();
                    if (a.Count >= 4 && (string?)a[0] == "property" && (string?)a[2] == "is")
                    {
                        string prop = (string)a[1]!;
                        string? want = a[3] is JsonValue jv && jv.TryGetValue(out string? sv) ? sv : null;
                        string? actual = prop switch
                        {
                            "name" or "accName" or "Name" or "AXDescription" => node?.Name.Value,
                            "description" or "accDescription" or "Description" or "AXHelp" => node?.Description.Value,
                            _ => null,
                        };
                        if (want is not null && actual is not null && prop is "name" or "description" or "accName" or "accDescription")
                        {
                            cases.Add(new Case(title + " " + prop, Norm(want), Norm(actual), Norm(actual) == Norm(want), null));
                            done = true;
                        }
                    }
                }

                if (done)
                {
                    break;
                }
            }
        }
    }

    private static Dictionary<string, string> LoadExpectations()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string path = Path.Combine(Root, "expectations.tsv");
        if (File.Exists(path))
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                map[parts[0] + "\t" + parts[1]] = parts.Length > 2 ? parts[2] : string.Empty;
            }
        }

        return map;
    }

    [Fact]
    public void Report_and_known_failures()
    {
        Dictionary<string, string> expected = LoadExpectations();
        var unexpected = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var report = new StringBuilder("file\tpass\tfail\tn/a\tn/a reason\n");
        var failList = new StringBuilder();
        int totalPass = 0, totalFail = 0, totalNa = 0;
        foreach (string file in Files())
        {
            int pass = 0, fail = 0, na = 0;
            string? reason = null;
            List<Case> cases;
            try
            {
                cases = Run(file);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                unexpected.Add(file + ": the harness failed: " + ex.GetType().Name + ": " + ex.Message);
                continue;
            }

            foreach (Case c in cases)
            {
                if (c.NotApplicable is not null)
                {
                    na++;
                    reason = c.NotApplicable;
                }
                else if (c.Pass)
                {
                    pass++;
                }
                else
                {
                    fail++;
                    string key = file + "\t" + c.Name;
                    failList.Append(key).Append('\t').Append("expected [").Append(c.Expected).Append("] got [").Append(c.Actual).Append("]\n");
                    if (!expected.ContainsKey(key))
                    {
                        unexpected.Add(key + ": expected [" + c.Expected + "] got [" + c.Actual + "]");
                    }
                    else
                    {
                        used.Add(key);
                    }
                }
            }

            if (cases.Count == 0)
            {
                reason = "no static expectations in the page (the assertions are made by script)";
            }

            report.Append(file).Append('\t').Append(pass).Append('\t').Append(fail).Append('\t').Append(na).Append('\t').Append(reason).Append('\n');
            totalPass += pass;
            totalFail += fail;
            totalNa += na;
        }

        report.Append("TOTAL\t").Append(totalPass).Append('\t').Append(totalFail).Append('\t').Append(totalNa).Append('\n');
        string? outPath = Environment.GetEnvironmentVariable("ONEHTML_WPT_REPORT");
        if (outPath is not null)
        {
            File.WriteAllText(outPath, report.ToString());
            File.WriteAllText(outPath + ".failures", failList.ToString());
        }

        string[] stale = expected.Keys.Where(k => !used.Contains(k)).ToArray();
        Assert.True(unexpected.Count == 0, "Undocumented failures (" + unexpected.Count + "):\n" + string.Join('\n', unexpected.Take(60)));
        Assert.True(stale.Length == 0, "Documented failures that now pass:\n" + string.Join('\n', stale));
    }
}
