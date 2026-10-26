using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

/// <summary>Settings for the JSON exports.</summary>
public sealed class ExportOptions
{
    /// <summary>Write indented JSON. Default <see langword="false"/>.</summary>
    public bool Indented { get; init; }

    /// <summary>The address of the page, recorded as given; the library never reads it. Default none.</summary>
    public string? Url { get; init; }

    /// <summary>The longest <c>html</c> excerpt, in characters, in the axe-compatible export. Default 300.</summary>
    public int MaxHtmlLength { get; init; } = 300;
}

/// <summary>
/// The two JSON exports of a <see cref="CheckReport"/>: the native format <c>onehtml-results</c> version 1 and an
/// axe-compatible shape. Both are deterministic: the same report gives the same bytes. See <c>docs/results.md</c>.
/// </summary>
public static class ResultsExport
{
    /// <summary>The native format's name.</summary>
    public const string NativeFormat = "onehtml-results";

    /// <summary>The native format's version.</summary>
    public const int NativeVersion = 1;

    private static readonly JavaScriptEncoder s_encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>Writes the report as native JSON (<c>onehtml-results</c> version 1).</summary>
    /// <param name="report">The report.</param>
    /// <param name="options">Export settings.</param>
    public static string ToNativeJson(this CheckReport report, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new ExportOptions();
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = options.Indented, Encoder = s_encoder }))
        {
            WriteNative(w, report, options);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Writes the report in the axe-compatible shape (<c>violations</c>, <c>passes</c>, <c>incomplete</c>, <c>inapplicable</c>).</summary>
    /// <param name="report">The report.</param>
    /// <param name="options">Export settings.</param>
    public static string ToAxeJson(this CheckReport report, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new ExportOptions();
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = options.Indented, Encoder = s_encoder }))
        {
            WriteAxe(w, report, options);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // ---- native ----

    private static void WriteNative(Utf8JsonWriter w, CheckReport report, ExportOptions options)
    {
        w.WriteStartObject();
        w.WriteString("format", NativeFormat);
        w.WriteNumber("version", NativeVersion);
        w.WriteStartObject("document");
        w.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(report.Document.Source.Bytes.Span)));
        if (options.Url is not null)
        {
            w.WriteString("url", options.Url);
        }

        w.WriteEndObject();
        WriteContext(w, report);
        if (report.Profile is { } profile)
        {
            w.WriteString("profile", profile.Name);
        }
        else
        {
            w.WriteNull("profile");
        }

        w.WriteBoolean("limitReached", report.LimitReached);
        w.WriteStartArray("rules");
        foreach (IAccessibilityRule rule in report.RulesRun)
        {
            WriteRule(w, rule);
        }

        w.WriteEndArray();
        IReadOnlyList<Edit> edits = report.SuggestedEditList();
        var index = new Dictionary<(EditOperation, int, string?, string?), int>();
        for (int i = 0; i < edits.Count; i++)
        {
            index[EditKey(edits[i])] = i;
        }

        w.WriteStartArray("results");
        foreach (RuleResult r in report.Results)
        {
            WriteResult(w, r, index);
        }

        w.WriteEndArray();
        w.WritePropertyName("suggestedEdits");
        using JsonDocument envelope = JsonDocument.Parse(EditJson.Create(report.Document, edits).ToJson());
        envelope.RootElement.WriteTo(w);
        w.WriteEndObject();
    }

    private static void WriteContext(Utf8JsonWriter w, CheckReport report)
    {
        StyleContext c = report.StyleContext;
        MediaFeatures m = c.Media;
        w.WriteStartObject("styleContext");
        w.WriteNumber("viewportWidth", m.Width);
        w.WriteNumber("viewportHeight", m.Height);
        w.WriteString("media", Kebab(m.Type.ToString()));
        w.WriteString("colorScheme", Kebab(m.ColorScheme.ToString()));
        w.WriteString("reducedMotion", Kebab(m.ReducedMotion.ToString()));
        w.WriteString("contrast", Kebab(m.Contrast.ToString()));
        w.WriteBoolean("forcedColors", m.ForcedColors);
        w.WriteBoolean("cssOnly", c.HasInteractionState);
        WriteNodeLocator(w, "focus", report, c.Focus);
        WriteNodeLocator(w, "hover", report, c.Hover);
        WriteNodeLocator(w, "active", report, c.Active);
        WriteNodeLocator(w, "target", report, c.Target);
        w.WriteEndObject();
    }

    private static void WriteNodeLocator(Utf8JsonWriter w, string name, CheckReport report, Node node)
    {
        if (node.IsNull)
        {
            return;
        }

        w.WriteString(name, report.Tree.Find(node)?.Locator);
    }

    private static void WriteRule(Utf8JsonWriter w, IAccessibilityRule rule)
    {
        RuleDescriptor d = rule.Descriptor;
        w.WriteStartObject();
        w.WriteString("id", d.Id);
        w.WriteString("description", rule.Description);
        w.WriteString("help", rule.Help);
        w.WriteString("impact", Kebab(rule.Impact.ToString()));
        w.WriteBoolean("bestPractice", d.BestPractice);
        w.WriteString("needs", d.Needs == RuleNeeds.Static ? "static" : "rendered-facts");
        w.WriteStartArray("sources");
        foreach (RuleSource s in d.Sources)
        {
            w.WriteStartObject();
            w.WriteString("kind", Kebab(s.Kind.ToString()));
            w.WriteString("id", s.Id);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("criteria");
        foreach (SuccessCriterion c in d.Criteria)
        {
            w.WriteStartObject();
            w.WriteString("number", c.Number);
            w.WriteString("handle", c.Handle);
            w.WriteString("level", c.Level.ToString());
            w.WriteString("version", VersionText(c.IntroducedIn));
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("tags");
        foreach (string t in d.Tags)
        {
            w.WriteStringValue(t);
        }

        w.WriteEndArray();
        w.WriteString("fix", d.Fix);
        w.WriteString("noFixReason", d.NoFixReason);
        w.WriteEndObject();
    }

    private static void WriteResult(Utf8JsonWriter w, RuleResult r, Dictionary<(EditOperation, int, string?, string?), int> index)
    {
        w.WriteStartObject();
        w.WriteString("rule", r.RuleId);
        w.WriteString("outcome", OutcomeName(r.Outcome));
        w.WriteString("message", r.Message);
        if (!r.Target.IsNull)
        {
            w.WriteStartObject("target");
            w.WriteString("tag", r.Target.IsElement ? r.Target.LocalName : null);
            w.WriteString("locator", r.Locator);
            if (r.SourceBytes.IsPresent)
            {
                w.WriteStartObject("source");
                w.WriteNumber("start", r.SourceBytes.Start);
                w.WriteNumber("end", r.SourceBytes.End);
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("source");
            }

            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("target");
        }

        w.WriteStartArray("findings");
        foreach (Finding f in r.Findings)
        {
            w.WriteStartObject();
            w.WriteString("attribute", f.Attribute);
            w.WriteString("value", f.Value);
            w.WriteString("message", f.Message);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartObject("taskImpact");
        w.WriteBoolean("blocksTask", r.TaskImpact == TaskImpact.BlocksTask);
        w.WriteString("reason", r.TaskImpactReason);
        w.WriteEndObject();
        w.WriteBoolean("cssOnly", r.CssOnly);
        w.WriteStartArray("factsUsed");
        foreach (string f in r.FactsUsed)
        {
            w.WriteStringValue(f);
        }

        w.WriteEndArray();
        w.WriteStartArray("factsMissing");
        foreach (string f in r.FactsMissing)
        {
            w.WriteStringValue(f);
        }

        w.WriteEndArray();
        w.WriteStartArray("suggested");
        foreach (SuggestedEdit s in r.SuggestedEdits)
        {
            w.WriteStartObject();
            w.WriteString("description", s.Description);
            w.WriteNumber("edit", index[EditKey(s.Edit)]);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static (EditOperation, int, string?, string?) EditKey(Edit e) => (e.Operation, e.Target.Index, e.Name, e.Value);

    // ---- axe-compatible ----

    private static void WriteAxe(Utf8JsonWriter w, CheckReport report, ExportOptions options)
    {
        w.WriteStartObject();
        w.WriteStartObject("testEngine");
        w.WriteString("name", "onehtml-validation");
        w.WriteString("version", NativeVersion.ToString(CultureInfo.InvariantCulture));
        w.WriteEndObject();
        w.WriteStartObject("testRunner");
        w.WriteString("name", "onehtml-validation");
        w.WriteEndObject();
        w.WriteStartObject("testEnvironment");
        w.WriteNumber("windowWidth", report.StyleContext.Media.Width);
        w.WriteNumber("windowHeight", report.StyleContext.Media.Height);
        w.WriteEndObject();
        w.WriteStartObject("toolOptions");
        w.WriteEndObject();
        if (options.Url is not null)
        {
            w.WriteString("url", options.Url);
        }

        WriteAxeGroup(w, "inapplicable", report, options, r => r.Outcome == Outcome.Inapplicable);
        WriteAxeGroup(w, "passes", report, options, r => r.Outcome == Outcome.Passed);
        WriteAxeGroup(w, "incomplete", report, options, r => r.Outcome is Outcome.NeedsReview or Outcome.NeedsRender);
        WriteAxeGroup(w, "violations", report, options, r => r.Outcome == Outcome.Failed);
        w.WriteEndObject();
    }

    private static void WriteAxeGroup(Utf8JsonWriter w, string name, CheckReport report, ExportOptions options, Func<RuleResult, bool> select)
    {
        w.WriteStartArray(name);
        foreach (IGrouping<string, RuleResult> group in report.Results.Where(select).GroupBy(r => r.RuleId, StringComparer.Ordinal))
        {
            IAccessibilityRule rule = group.First().Rule;
            bool withImpact = name is "violations" or "incomplete";
            w.WriteStartObject();
            w.WriteString("id", rule.Descriptor.Id);
            if (withImpact)
            {
                w.WriteString("impact", Kebab(rule.Impact.ToString()));
            }
            else
            {
                w.WriteNull("impact");
            }

            w.WriteStartArray("tags");
            foreach (string t in rule.Descriptor.Tags)
            {
                w.WriteStringValue(t);
            }

            w.WriteEndArray();
            w.WriteString("description", rule.Description);
            w.WriteString("help", rule.Help);
            w.WriteString("helpUrl", rule.Descriptor.Sources.Any(s => s.Kind == RuleSourceKind.Axe)
                ? "https://dequeuniversity.com/rules/axe/" + AxeVersionPath() + "/" + rule.Descriptor.Id
                : string.Empty);
            w.WriteStartArray("nodes");
            foreach (RuleResult r in group)
            {
                if (r.Outcome == Outcome.Inapplicable)
                {
                    continue;
                }

                WriteAxeNode(w, report, r, withImpact, options);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static string AxeVersionPath()
    {
        string v = AxeRules.AxeVersion;
        int second = v.IndexOf('.', v.IndexOf('.', StringComparison.Ordinal) + 1);
        return second < 0 ? v : v[..second];
    }

    private static void WriteAxeNode(Utf8JsonWriter w, CheckReport report, RuleResult r, bool withImpact, ExportOptions options)
    {
        w.WriteStartObject();
        w.WriteString("html", StartTagExcerpt(report.Document, r.Target, options.MaxHtmlLength));
        if (withImpact)
        {
            w.WriteString("impact", Kebab(r.Rule.Impact.ToString()));
        }
        else
        {
            w.WriteNull("impact");
        }

        w.WriteStartArray("target");
        if (r.Locator is { } loc)
        {
            w.WriteStringValue(loc);
        }

        w.WriteEndArray();
        bool failing = r.Outcome != Outcome.Passed;
        w.WriteStartArray("any");
        if (failing)
        {
            w.WriteStartObject();
            w.WriteString("id", r.RuleId);
            w.WriteStartArray("data");
            foreach (Finding f in r.Findings)
            {
                w.WriteStartObject();
                w.WriteString("attribute", f.Attribute);
                w.WriteString("value", f.Value);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("relatedNodes");
            w.WriteEndArray();
            w.WriteString("impact", Kebab(r.Rule.Impact.ToString()));
            w.WriteString("message", r.Message);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("all");
        w.WriteEndArray();
        w.WriteStartArray("none");
        w.WriteEndArray();
        if (failing)
        {
            w.WriteString("failureSummary", string.Join("\n", new[] { r.Message }.Concat(r.Findings.Select(f => f.Message))));
        }

        w.WriteStartObject("onehtml");
        w.WriteString("outcome", OutcomeName(r.Outcome));
        w.WriteBoolean("blocksTask", r.TaskImpact == TaskImpact.BlocksTask);
        w.WriteBoolean("cssOnly", r.CssOnly);
        w.WriteStartArray("factsMissing");
        foreach (string f in r.FactsMissing)
        {
            w.WriteStringValue(f);
        }

        w.WriteEndArray();
        w.WriteNumber("suggestedEdits", r.SuggestedEdits.Count);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static string StartTagExcerpt(Document doc, Node target, int max)
    {
        if (target.IsNull || target.Source.Range is not { IsPresent: true } range)
        {
            return string.Empty;
        }

        string text = doc.Source.Text;
        int end = Math.Min(range.End, text.Length);
        int start = Math.Min(range.Start, end);
        string s = text[start..end];
        return s.Length > max ? s[..max] : s;
    }

    // ---- names ----

    internal static string OutcomeName(Outcome o) => o switch
    {
        Outcome.Failed => "failed",
        Outcome.Passed => "passed",
        Outcome.NeedsReview => "needs-review",
        Outcome.NeedsRender => "needs-render",
        _ => "inapplicable",
    };

    private static string VersionText(WcagVersion v) => v switch
    {
        WcagVersion.Wcag20 => "2.0",
        WcagVersion.Wcag21 => "2.1",
        _ => "2.2",
    };

    /// <summary>PascalCase to lower-case words joined by hyphens (<c>FailureTechnique</c> → <c>failure-technique</c>), by ASCII rules.</summary>
    internal static string Kebab(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (c is >= 'A' and <= 'Z')
            {
                if (i > 0)
                {
                    sb.Append('-');
                }

                sb.Append((char)(c + 32));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
