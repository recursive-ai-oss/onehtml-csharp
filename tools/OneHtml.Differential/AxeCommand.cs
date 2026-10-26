using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Tree;
using RecursiveAI.OneHtml.Accessibility.Validation;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// The axe-core differential: for each captured page, runs the checker on Chrome's serialized DOM and compares its results with the axe
/// results recorded in the capture, per rule and per element. Resumable: a finished capture leaves <c>parts/&lt;sha&gt;-&lt;viewport&gt;.json</c>
/// and is not redone. See <c>docs/axe-differential.md</c>.
/// </summary>
internal static class AxeCommand
{
    /// <summary>Version of the output files.</summary>
    public const string Schema = "onehtml-axe-differential/1";

    private static readonly Checker s_checker = new();

    // Investigation aid: ONEHTML_AXE_DUMP_RULE=<rule id> and ONEHTML_AXE_DUMP_DIR=<dir> write one node listing per page for that rule.
    private static readonly string? DumpRule = Environment.GetEnvironmentVariable("ONEHTML_AXE_DUMP_RULE");
    private static readonly string? DumpDir = Environment.GetEnvironmentVariable("ONEHTML_AXE_DUMP_DIR");

    public static int Run(Options o)
    {
        string partsDir = System.IO.Path.Combine(o.Out, "parts");
        if (o.Fresh && Directory.Exists(partsDir))
        {
            Directory.Delete(partsDir, true);
        }

        Directory.CreateDirectory(partsDir);
        List<AxCommand.CaptureRef> captures = AxCommand.FindCaptures(o.Corpus);
        if (Environment.GetEnvironmentVariable("ONEHTML_AXE_ONLY") is { Length: > 0 } only)
        {
            // Investigation aid: comma-separated sha prefixes.
            string[] prefixes = only.Split(',');
            captures.RemoveAll(c => !prefixes.Any(p => c.Sha.StartsWith(p, StringComparison.Ordinal)));
        }

        if (o.Skip > 0)
        {
            captures.RemoveRange(0, Math.Min(o.Skip, captures.Count));
        }

        if (captures.Count > o.Limit)
        {
            captures.RemoveRange(o.Limit, captures.Count - o.Limit);
        }

        StylesheetStore? styles = o.StyleCorpus is { Length: > 0 } sc ? StylesheetStore.Load(sc) : null;
        var todo = captures.Where(c => !File.Exists(PartPath(partsDir, c))).ToList();
        Console.WriteLine($"axe: {captures.Count} captures, {captures.Count - todo.Count} already done, facts {o.Facts}, jobs {o.Jobs}, timeout {o.TimeoutSeconds}s, {styles?.Count ?? 0} stylesheets");
        var clock = Stopwatch.StartNew();
        int done = 0, skippedForTime = 0;
        Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = o.Jobs }, cap =>
        {
            if (o.MaxSeconds > 0 && clock.Elapsed.TotalSeconds > o.MaxSeconds)
            {
                Interlocked.Increment(ref skippedForTime);
                return;
            }

            JsonObject part = RunCapture(cap, styles, o.TimeoutSeconds, o.Facts);
            string path = PartPath(partsDir, cap);
            File.WriteAllText(path + ".tmp", part.ToJsonString());
            File.Move(path + ".tmp", path, true);
            int n = Interlocked.Increment(ref done);
            if (n % 50 == 0)
            {
                Console.WriteLine($"  {n}/{todo.Count} after {clock.Elapsed.TotalSeconds:0}s");
            }
        });

        AxeReport.Write(o, partsDir, o.Facts, AxCommand.ReadEnvironment(o.Corpus));
        if (skippedForTime > 0)
        {
            Console.WriteLine($"REMAINING={skippedForTime}");
        }

        return 0;
    }

    private static string PartPath(string partsDir, AxCommand.CaptureRef c) => System.IO.Path.Combine(partsDir, c.Sha + "-" + c.Viewport + ".json");

    private static JsonObject RunCapture(AxCommand.CaptureRef cap, StylesheetStore? sheets, int timeoutSeconds, string facts)
    {
        var part = new JsonObject { ["schema"] = Schema + "/part", ["sha"] = cap.Sha, ["viewport"] = cap.Viewport };
        (JsonObject? res, string? failure) = Guard.Run(ct => Compare(cap, sheets, facts, part, ct), timeoutSeconds);
        if (failure is not null)
        {
            part["status"] = "error";
            part["detail"] = failure;
            return part;
        }

        return res!;
    }

    private static JsonObject Compare(AxCommand.CaptureRef cap, StylesheetStore? sheets, string factsMode, JsonObject part, CancellationToken ct)
    {
        byte[]? meta = AxCommand.ReadFile(cap.Dir, "meta");
        byte[]? axeJson = AxCommand.ReadFile(cap.Dir, "axe");
        byte[]? domJson = AxCommand.ReadFile(cap.Dir, "dom");
        if (meta is null || axeJson is null || domJson is null)
        {
            part["status"] = "skipped";
            part["detail"] = "capture incomplete";
            return part;
        }

        using JsonDocument metaDoc = JsonDocument.Parse(meta);
        JsonElement m = metaDoc.RootElement;
        part["url"] = m.TryGetProperty("url", out JsonElement u) ? u.GetString() ?? string.Empty : string.Empty;
        if (m.TryGetProperty("ok", out JsonElement okv) && okv.ValueKind == JsonValueKind.False)
        {
            part["status"] = "skipped";
            part["detail"] = "capture not ok";
            return part;
        }

        double width = 1280, height = 800, dpr = 1;
        if (m.TryGetProperty("viewport", out JsonElement vp))
        {
            width = Num(vp, "width", width);
            height = Num(vp, "height", height);
            dpr = Num(vp, "deviceScaleFactor", dpr);
        }

        string baseUrl = m.TryGetProperty("final_url", out JsonElement fu) && fu.ValueKind == JsonValueKind.String ? fu.GetString()! : (string)part["url"]!;
        AxeResultSet axe = AxeReader.Read(axeJson);
        part["axeVersion"] = axe.Version;

        using JsonDocument domDoc = JsonDocument.Parse(domJson);
        string outer = domDoc.RootElement.GetProperty("outerHTML").GetString() ?? string.Empty;
        string doctype = domDoc.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        var treeClock = Stopwatch.StartNew();
        Document doc = Document.Parse(doctype + outer, cancellationToken: ct);

        var context = StyleContext.Default.WithMedia(MediaFeatures.Default with { Width = width, Height = height, DevicePixelRatio = dpr });
        var options = new StyleSourceOptions { BaseUrl = baseUrl, Resolver = sheets is null ? null : sheets.Resolve };
        DocumentStyles styles = DocumentStyles.Create(doc, context, options, ct);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles, null, new AccessibilityOptions { BaseUrl = baseUrl }, ct);

        // Resolve axe's nodes once; both modes compare against the same list.
        string page = cap.Sha[..Math.Min(12, cap.Sha.Length)];
        var theirs = new Dictionary<string, List<TheirNode>>(StringComparer.Ordinal);
        foreach (AxeNodeRef n in axe.Nodes)
        {
            Node e = AxeTargets.Resolve(doc, n, out string how);
            string where = n.Selector ?? n.XPath ?? "(nested)";
            if (!theirs.TryGetValue(n.Rule, out List<TheirNode>? list))
            {
                theirs[n.Rule] = list = [];
            }

            // axe passes a contrast check whose measured ratio is null (it could not read the colours, for instance behind a gradient);
            // a pass without a ratio is an undecided check, so it counts as incomplete.
            AxeClass cls = n.Class == AxeClass.Pass && n.Rule.StartsWith("color-contrast", StringComparison.Ordinal) && n.Message.Contains("contrast of null", StringComparison.Ordinal) ? AxeClass.Incomplete : n.Class;
            list.Add(new TheirNode(e.IsNull ? -1 : e.Index, cls, where, n.Html, how, n.Message));
        }

        // Our rule id -> the axe id it corresponds to (the same id unless a rule names another axe source).
        var axeIdOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (IAccessibilityRule r in s_checker.Rules)
        {
            RuleSource? src = r.Descriptor.Sources.Where(s => s.Kind == RuleSourceKind.Axe).Cast<RuleSource?>().FirstOrDefault();
            axeIdOf[r.Descriptor.Id] = src?.Id ?? r.Descriptor.Id;
        }

        part["treeMs"] = treeClock.ElapsedMilliseconds;
        var modes = new JsonObject();
        if (factsMode is "off" or "both")
        {
            modes["off"] = RunMode(null, tree, doc, axe, theirs, axeIdOf, page, ct);
        }

        if (factsMode is "on" or "both")
        {
            DomSnapshotMapResult? map = null;
            byte[]? snapJson = AxCommand.ReadFile(cap.Dir, "snapshot");
            JsonObject on;
            if (snapJson is null)
            {
                on = new JsonObject { ["status"] = "skipped", ["detail"] = "no snapshot" };
            }
            else
            {
                map = DomSnapshotAdapter.Map(DomSnapshot.Parse(snapJson), doc, new DomSnapshotMapOptions { ViewportWidth = width, ViewportHeight = height, Context = context }, ct);
                on = map.Succeeded
                    ? RunMode(map.Facts, tree, doc, axe, theirs, axeIdOf, page, ct)
                    : new JsonObject { ["status"] = "refused", ["detail"] = map.Refusal is { } rf ? rf.Reason + ": " + rf.Message : "refused" };
            }

            modes["on"] = on;
        }

        part["status"] = "ok";
        part["modes"] = modes;
        part["axeRules"] = new JsonArray([.. axe.RulesRan.Order(StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s))]);
        return part;
    }

    private static JsonObject RunMode(
        RenderedFacts? facts,
        AccessibilityTree tree,
        Document doc,
        AxeResultSet axe,
        Dictionary<string, List<TheirNode>> theirs,
        Dictionary<string, string> axeIdOf,
        string page,
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        CheckReport report = s_checker.Check(tree, new CheckOptions { Facts = facts }, ct);
        long checkMs = clock.ElapsedMilliseconds;
        if (Environment.GetEnvironmentVariable("ONEHTML_AXE_NATIVE_DIR") is { Length: > 0 } nativeDir)
        {
            // Investigation aid: the native result JSON of each page, to compare two builds byte for byte.
            Directory.CreateDirectory(nativeDir);
            File.WriteAllText(System.IO.Path.Combine(nativeDir, page + "-" + (facts is null ? "off" : "on") + ".json"), report.ToNativeJson());
        }

        if (Environment.GetEnvironmentVariable("ONEHTML_AXE_PROFILE") is { Length: > 0 })
        {
            // Investigation aid: the time each rule takes alone on this page (the tree and the cascade are already built).
            var times = new List<(long Ms, string Id)>();
            foreach (IAccessibilityRule r in s_checker.Rules)
            {
                clock.Restart();
                s_checker.Check(tree, new CheckOptions { Facts = facts, RuleIds = [r.Descriptor.Id] }, ct);
                times.Add((clock.ElapsedMilliseconds, r.Descriptor.Id));
            }

            Console.Error.WriteLine($"PROFILE {page} facts={(facts is null ? "off" : "on")} all={checkMs}ms: " + string.Join(", ", times.OrderByDescending(t => t.Ms).Take(8).Select(t => $"{t.Id}={t.Ms}")));
        }
        var ours = new Dictionary<string, Dictionary<int, OurNode>>(StringComparer.Ordinal);
        foreach (IAccessibilityRule r in report.RulesRun)
        {
            ours[axeIdOf[r.Descriptor.Id]] = [];
        }

        foreach (RuleResult r in report.Results)
        {
            if (r.Target.IsNull)
            {
                continue;
            }

            // A text-node target counts for its element; axe only ever reports elements.
            Node t = r.Target.IsElement ? r.Target : r.Target.Parent;
            if (t.IsNull)
            {
                continue;
            }

            (AxeClass cls, string detail) = r.Outcome switch
            {
                Outcome.Failed => (AxeClass.Violation, "fail"),
                Outcome.Passed => (AxeClass.Pass, "pass"),
                Outcome.NeedsReview => (AxeClass.Incomplete, "review"),
                _ => (AxeClass.Incomplete, "render"),
            };
            AxeCompare.Add(ours[axeIdOf[r.RuleId]], t.Index, new OurNode(cls, detail, Environment.GetEnvironmentVariable("ONEHTML_AXE_EXPLAIN") is { Length: > 0 } ? r.Message + " {" + string.Join("; ", r.Findings.Select(f => f.Value)) + "}" : r.Message));
        }

        // Where axe reports an AA contrast violation it reports the AAA rule as a pass; our AAA failure there is the same finding.
        HashSet<int> aaSubsumed = theirs.TryGetValue("color-contrast", out List<TheirNode>? aa) ? [.. aa.Where(n => n.Index >= 0 && n.Class == AxeClass.Violation).Select(n => n.Index)] : [];
        var rules = new JsonObject();
        foreach (string id in ours.Keys.Concat(theirs.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            ours.TryGetValue(id, out Dictionary<int, OurNode>? o);
            RuleTally t = AxeCompare.CompareRule(o, theirs.GetValueOrDefault(id) ?? [], page, i => Describe(doc.GetNode(i)), i => { Node pn = doc.GetNode(i).Parent; return pn.IsNull ? -1 : pn.Index; }, id, id == "color-contrast-enhanced" ? aaSubsumed : null);
            rules[id] = t.ToJson();
            if (DumpRule is { Length: > 0 } dr && id == dr && DumpDir is { Length: > 0 } dd)
            {
                // Node-level listing of one rule on one page, for investigating a class: ours, then axe's, in document order.
                var lines = new List<string>();
                foreach (KeyValuePair<int, OurNode> kv in (IEnumerable<KeyValuePair<int, OurNode>>?)o ?? [])
                {
                    lines.Add($"{kv.Key,7} ours {kv.Value.Detail,-6} {AxCommand.DomPath(doc.GetNode(kv.Key))}  {kv.Value.Message}{Explain(tree, facts, doc.GetNode(kv.Key))}");
                }

                foreach (TheirNode n in theirs.GetValueOrDefault(id) ?? [])
                {
                    lines.Add($"{n.Index,7} axe  {n.Class,-10} {n.Where}  {n.Message}");
                }

                lines.Sort(StringComparer.Ordinal);
                Directory.CreateDirectory(dd);
                File.WriteAllLines(System.IO.Path.Combine(dd, page + "-" + id + ".txt"), lines);
            }
        }

        return new JsonObject { ["status"] = "ok", ["checkMs"] = checkMs, ["implemented"] = new JsonArray([.. ours.Keys.Order(StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s))]), ["rules"] = rules };
    }

    /// <summary>Dump aid: the colour-related computed values of an element and the rule each came from, with the same for its parent.</summary>
    private static string Explain(AccessibilityTree tree, RenderedFacts? facts, Node n)
    {
        if (!n.IsElement || Environment.GetEnvironmentVariable("ONEHTML_AXE_EXPLAIN") is not { Length: > 0 })
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder("  [");
        Node cur = n;
        for (int depth = 0; depth < (int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_AXE_DEPTH"), out int dp) ? dp : 1) && cur.IsElement; depth++, cur = cur.Parent)
        {
            ComputedStyle cs = tree.Styles.GetStyle(cur);
            sb.Append(cur.LocalName).Append(facts is not null && facts.TryGet(cur, out NodeFacts nf) && nf.HasLayout ? " blended=" + nf.Layout.BlendedBackgroundColor + " box=" + nf.Layout.BorderBox : string.Empty).Append(':');
            foreach (string p in Environment.GetEnvironmentVariable("ONEHTML_AXE_EXPLAIN")!.Split(','))
            {
                DeclarationSource? src = cs.GetSource(p);
                sb.Append(' ').Append(p).Append('=').Append(cs.GetPropertyValue(p)).Append(" <").Append(src?.SelectorText ?? "-").Append(src is { IsImportant: true } ? " !" : string.Empty).Append('@').Append(src?.SheetUrl ?? "-").Append('>');
            }

            sb.Append(" | ");
        }

        return sb.Append(']').ToString();
    }

    private static string Describe(Node n)
    {
        if (!n.IsElement)
        {
            return n.Kind.ToString();
        }

        string id = n.GetAttribute("id") is { Length: > 0 } v ? "#" + v : string.Empty;
        return AxCommand.DomPath(n);
    }

    private static double Num(JsonElement o, string name, double fallback) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
}
