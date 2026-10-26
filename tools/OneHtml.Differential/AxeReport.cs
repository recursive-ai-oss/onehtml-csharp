using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>Aggregates the per-capture parts of the <c>axe</c> command into <c>summary.md</c>, <c>summary.json</c> and <c>classes.json</c>.</summary>
internal static class AxeReport
{
    private const int ExampleCap = 5;

    private sealed class ModeAgg
    {
        public int Pages;
        public readonly Dictionary<string, int> Skipped = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, RuleTally> Rules = new(StringComparer.Ordinal);
        public readonly SortedSet<string> Implemented = new(StringComparer.Ordinal);
        public readonly SortedSet<string> AxeRan = new(StringComparer.Ordinal);
    }

    public static void Write(Options o, string partsDir, string factsMode, string environment)
    {
        var modes = new SortedDictionary<string, ModeAgg>(StringComparer.Ordinal);
        int captures = 0;
        var status = new SortedDictionary<string, int>(StringComparer.Ordinal);
        string axeVersion = string.Empty;
        foreach (string f in Directory.EnumerateFiles(partsDir, "*.json").Order(StringComparer.Ordinal))
        {
            JsonNode part = JsonNode.Parse(File.ReadAllText(f))!;
            captures++;
            string st = (string)part["status"]!;
            status[st] = status.GetValueOrDefault(st) + 1;
            if (st != "ok")
            {
                continue;
            }

            axeVersion = (string?)part["axeVersion"] ?? axeVersion;
            JsonArray ran = part["axeRules"]!.AsArray();
            foreach ((string mode, JsonNode? m) in part["modes"]!.AsObject())
            {
                if (!modes.TryGetValue(mode, out ModeAgg? agg))
                {
                    modes[mode] = agg = new ModeAgg();
                }

                string ms = (string)m!["status"]!;
                if (ms != "ok")
                {
                    agg.Skipped[ms] = agg.Skipped.GetValueOrDefault(ms) + 1;
                    continue;
                }

                agg.Pages++;
                foreach (JsonNode? r in ran)
                {
                    agg.AxeRan.Add((string)r!);
                }

                foreach (JsonNode? r in m["implemented"]!.AsArray())
                {
                    agg.Implemented.Add((string)r!);
                }

                foreach ((string id, JsonNode? t) in m["rules"]!.AsObject())
                {
                    if (!agg.Rules.TryGetValue(id, out RuleTally? rt))
                    {
                        agg.Rules[id] = rt = new RuleTally();
                    }

                    rt.Merge(RuleTally.FromJson(t!), ExampleCap);
                }
            }
        }

        Directory.CreateDirectory(o.Out);
        var summary = new JsonObject { ["schema"] = AxeCommand.Schema, ["axe"] = axeVersion, ["environment"] = environment, ["captures"] = captures, ["factsMode"] = factsMode };
        var statusObj = new JsonObject();
        foreach ((string k, int v) in status)
        {
            statusObj[k] = v;
        }

        summary["captureStatus"] = statusObj;
        var md = new StringBuilder();
        md.AppendLine("# axe-core differential");
        md.AppendLine();
        md.AppendLine($"axe-core {axeVersion}; Chrome {environment}; {captures} captures ({string.Join(", ", status.Select(kv => kv.Key + " " + kv.Value))}); facts mode {factsMode}.");
        var modesJson = new JsonObject();
        var classesJson = new JsonObject();
        foreach ((string mode, ModeAgg a) in modes)
        {
            md.AppendLine();
            md.AppendLine($"## Rendered facts: {mode}");
            md.AppendLine();
            md.AppendLine($"{a.Pages} pages compared" + (a.Skipped.Count > 0 ? "; not compared: " + string.Join(", ", a.Skipped.Select(kv => kv.Key + " " + kv.Value)) : "") + ".");
            WriteMode(md, a, modesJson, classesJson, mode);
        }

        summary["modes"] = modesJson;
        File.WriteAllText(System.IO.Path.Combine(o.Out, "summary.json"), summary.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(System.IO.Path.Combine(o.Out, "classes.json"), classesJson.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(System.IO.Path.Combine(o.Out, "summary.md"), md.ToString());
        Console.WriteLine($"axe: {captures} captures; summary in {System.IO.Path.GetFullPath(o.Out)}");
    }

    // A pass reported by only one side says nothing about disagreement: axe reports a pass for every candidate it examined and we report
    // passes only where a rule has a per-element verdict. These are counted apart (docs/axe-differential.md, normalizations).
    private static bool IsPassOnly(string key) => key is "pass|none" or "none|pass";

    private static long Sum(RuleTally t, Func<string, bool> pick) => t.Classes.Where(kv => pick(kv.Key)).Sum(kv => kv.Value);

    private static void WriteMode(StringBuilder md, ModeAgg a, JsonObject modesJson, JsonObject classesJson, string mode)
    {
        var rulesJson = new JsonObject();
        var both = a.Rules.Where(kv => a.Implemented.Contains(kv.Key) && a.AxeRan.Contains(kv.Key)).ToList();
        md.AppendLine();
        md.AppendLine("### Rules we implement");
        md.AppendLine();
        md.AppendLine("Pages = pages where axe / we / both reported a node. Agree = same element, same group; contained = we and axe report the same group on a container and its content (a table and its cells); collapsed = axe reports a failing group once where we report every member (duplicate ids, access keys) (incomplete = needs-review or needs-render). "
            + "Mismatch columns: `F|P` we fail, axe passes; `P|V` we pass, axe violates; `F|I` we fail, axe incomplete; `I|V` incomplete vs violation; `I|P`; `P|I`; ours-only = we report an element axe does not; axe-only = the reverse; pass-only = a pass one side reports and the other does not (not counted as disagreement); needs-render = we cannot decide without rendered facts, whatever axe says (not compared, not counted in Agree %). "
            + "Unres = axe nodes not found in our parse.");
        md.AppendLine();
        md.AppendLine("| Rule | Pages axe/ours/both | Nodes axe/ours | Agree (+contained, collapsed) | Agree % | F\\|P | P\\|V | F\\|I | I\\|V | I\\|P | P\\|I | ours-only | axe-only | pass-only ours/axe | needs-render | Unres |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach ((string id, RuleTally t) in both)
        {
            long fp = Sum(t, k => k == "fail|pass");
            long pv = Sum(t, k => k == "pass|violation");
            long fi = Sum(t, k => k == "fail|incomplete");
            long iv = Sum(t, k => k == "review|violation");
            long ip = Sum(t, k => k == "review|pass");
            long pi = Sum(t, k => k == "pass|incomplete");
            long deferred = Sum(t, k => k.StartsWith("render|", StringComparison.Ordinal));
            long oursOnly = Sum(t, k => k.EndsWith("|none", StringComparison.Ordinal) && !IsPassOnly(k) && !k.StartsWith("render|", StringComparison.Ordinal));
            long axeOnly = Sum(t, k => k.StartsWith("none|", StringComparison.Ordinal) && !IsPassOnly(k));
            long passOurs = Sum(t, k => k == "pass|none");
            long passAxe = Sum(t, k => k == "none|pass");
            long mism = t.Classes.Where(kv => !kv.Key.StartsWith("unresolved|", StringComparison.Ordinal) && !kv.Key.StartsWith("render|", StringComparison.Ordinal) && !IsPassOnly(kv.Key)).Sum(kv => kv.Value);
            long good = t.Agree + t.Contained + t.Collapsed;
            long denom = good + mism;
            string pct = denom == 0 ? "-" : (100.0 * good / denom).ToString("0.0", CultureInfo.InvariantCulture);
            md.AppendLine($"| {id} | {t.PagesAxe}/{t.PagesOurs}/{t.PagesBoth} | {t.NodesAxe}/{t.NodesOurs} | {t.Agree} (+{t.Contained}, {t.Collapsed}) | {pct} | {fp} | {pv} | {fi} | {iv} | {ip} | {pi} | {oursOnly} | {axeOnly} | {passOurs}/{passAxe} | {deferred} | {t.Unresolved} |");
            JsonObject j = t.ToJson();
            j.Remove("examples");
            j["agreePercent"] = denom == 0 ? null : Math.Round(100.0 * good / denom, 2);
            rulesJson[id] = j;
            foreach ((string k, long n) in t.Classes)
            {
                string key = id + " " + k;
                var cj = new JsonObject { ["rule"] = id, ["class"] = k, ["mode"] = mode, ["nodes"] = n };
                var ex = new JsonArray();
                if (t.Examples.TryGetValue(k, out List<AxeExample>? list))
                {
                    foreach (AxeExample e in list)
                    {
                        ex.Add(new JsonObject { ["page"] = e.Page, ["where"] = e.Where, ["html"] = e.Html, ["ours"] = e.Ours, ["axe"] = e.Axe });
                    }
                }

                cj["examples"] = ex;
                classesJson[mode + " " + key] = cj;
            }
        }

        var axeOnlyRules = a.AxeRan.Where(r => !a.Implemented.Contains(r)).ToList();
        md.AppendLine();
        md.AppendLine("### Rules axe ran that we do not implement");
        md.AppendLine();
        md.AppendLine("| Rule | Pages with nodes | Nodes (violations/passes/incomplete are not split here) |");
        md.AppendLine("|---|---|---|");
        var axeOnlyJson = new JsonObject();
        foreach (string r in axeOnlyRules)
        {
            RuleTally? t = a.Rules.GetValueOrDefault(r);
            md.AppendLine($"| {r} | {t?.PagesAxe ?? 0} | {t?.NodesAxe ?? 0} |");
            axeOnlyJson[r] = new JsonObject { ["pages"] = t?.PagesAxe ?? 0, ["nodes"] = t?.NodesAxe ?? 0 };
        }

        var oursOnlyRules = a.Implemented.Where(r => !a.AxeRan.Contains(r)).ToList();
        md.AppendLine();
        md.AppendLine("### Rules we implement that axe has no counterpart for");
        md.AppendLine();
        md.AppendLine(oursOnlyRules.Count == 0 ? "None." : string.Join(", ", oursOnlyRules.Select(r => $"{r} ({a.Rules.GetValueOrDefault(r)?.NodesOurs ?? 0} nodes on {a.Rules.GetValueOrDefault(r)?.PagesOurs ?? 0} pages)")) + ".");

        // Unfinished classes first: the mismatch classes by size, so the biggest are visible.
        md.AppendLine();
        md.AppendLine("### Mismatch classes, largest first");
        md.AppendLine();
        md.AppendLine("| Rule | Class | Nodes | Example (where) |");
        md.AppendLine("|---|---|---|---|");
        foreach (JsonObject c in classesJson.Where(kv => kv.Value!["mode"]!.GetValue<string>() == mode && !IsPassOnly((string)kv.Value!["class"]!) && !((string)kv.Value!["class"]!).StartsWith("render|", StringComparison.Ordinal)).Select(kv => kv.Value!.AsObject()).OrderByDescending(c => (long)c["nodes"]!).ThenBy(c => (string)c["rule"]!, StringComparer.Ordinal).Take(60))
        {
            string where = c["examples"]!.AsArray().Count > 0 ? ((string)c["examples"]![0]!["where"]!).Replace("|", "\\|", StringComparison.Ordinal) : string.Empty;
            md.AppendLine($"| {(string)c["rule"]!} | {((string)c["class"]!).Replace("|", "\\|", StringComparison.Ordinal)} | {(long)c["nodes"]!} | `{where}` |");
        }

        modesJson[mode] = new JsonObject
        {
            ["pages"] = a.Pages, ["rules"] = rulesJson, ["axeOnlyRules"] = axeOnlyJson,
            ["oursOnlyRules"] = new JsonArray([.. oursOnlyRules.Select(s => (JsonNode?)JsonValue.Create(s))]),
        };
    }
}
