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
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Encodings;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

/// <summary>
/// The SARIF 2.1.0 export of a <see cref="CheckReport"/> (OASIS Static Analysis Results Interchange Format). The output
/// is deterministic and validates against the published 2.1.0 JSON schema. The mapping is in <c>docs/sarif.md</c>.
/// </summary>
public static class SarifExport
{
    /// <summary>The SARIF version written.</summary>
    public const string SarifVersion = "2.1.0";

    /// <summary>The version of this mapping from a report to SARIF; recorded in the run's properties.</summary>
    public const int ExportVersion = 1;

    /// <summary>The schema address written to <c>$schema</c>.</summary>
    public const string SchemaUri = "https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json";

    private const string TaxonomyName = "WCAG";
    private static readonly JavaScriptEncoder s_encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>
    /// Writes the report as SARIF 2.1.0 JSON with one run. Failed results become <c>fail</c> results with a level from the
    /// rule's impact; needs-review and needs-render results become <c>review</c> results; passed and inapplicable results
    /// are written only when <see cref="ExportOptions.IncludePassed"/> or <see cref="ExportOptions.IncludeInapplicable"/> is set.
    /// Columns are UTF-16 code units (<c>columnKind</c> <c>utf16CodeUnits</c>) of the decoded file, counted from 1 on each
    /// line and not counting a byte order mark; <c>byteOffset</c> and <c>byteLength</c> are offsets into the file's bytes.
    /// CR, LF and CR LF each end one line. Character offsets are not written, because the library's decoded text has
    /// its line ends normalised, as HTML parsing requires.
    /// </summary>
    /// <param name="report">The report.</param>
    /// <param name="options">Export settings; <see cref="ExportOptions.Url"/> becomes the artifact address.</param>
    public static string ToSarifJson(this CheckReport report, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new ExportOptions();
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = options.Indented, Encoder = s_encoder }))
        {
            new Writer(report, options).Write(w);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private sealed class Writer
    {
        private readonly CheckReport _report;
        private readonly ExportOptions _options;
        private readonly SourceText _source;
        private readonly string _artifactUri;
        private readonly int[] _lineStarts;
        private readonly Dictionary<string, int> _ruleIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _taxonIndex = new(StringComparer.Ordinal);
        private readonly List<SuccessCriterion> _taxa = [];
        private readonly Dictionary<(EditOperation, int, string?, string?), FixData?> _fixes = [];

        internal Writer(CheckReport report, ExportOptions options)
        {
            _report = report;
            _options = options;
            _source = report.Document.Source;
            _artifactUri = options.Url is { Length: > 0 } url
                ? EscapeUriReference(url)
                : "urn:onehtml:document:sha256:" + Convert.ToHexStringLower(SHA256.HashData(_source.Bytes.Span));
            _lineStarts = LineStarts(_source.Text);
            for (int i = 0; i < report.RulesRun.Count; i++)
            {
                _ruleIndex[report.RulesRun[i].Descriptor.Id] = i;
            }

            foreach (SuccessCriterion c in WcagCriteria.All)
            {
                if (report.RulesRun.Any(r => r.Descriptor.Criteria.Any(x => x.Number == c.Number)))
                {
                    _taxonIndex[c.Number] = _taxa.Count;
                    _taxa.Add(c);
                }
            }
        }

        internal void Write(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            w.WriteString("$schema", SchemaUri);
            w.WriteString("version", SarifVersion);
            w.WriteStartArray("runs");
            w.WriteStartObject();
            WriteTool(w);
            w.WriteString("columnKind", "utf16CodeUnits");
            WriteArtifacts(w);
            WriteInvocations(w);
            w.WriteStartArray("results");
            foreach (RuleResult r in _report.Results)
            {
                if (r.Outcome == Outcome.Passed && !_options.IncludePassed || r.Outcome == Outcome.Inapplicable && !_options.IncludeInapplicable)
                {
                    continue;
                }

                WriteResult(w, r);
            }

            w.WriteEndArray();
            WriteTaxonomies(w);
            w.WriteStartObject("properties");
            w.WriteNumber("exportVersion", ExportVersion);
            w.WriteString("profile", _report.Profile?.Name);
            w.WriteBoolean("cssOnly", _report.StyleContext.HasInteractionState);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }

        // ---- tool, rules, taxonomy (SARIF 3.18, 3.19, 3.49) ----

        private void WriteTool(Utf8JsonWriter w)
        {
            w.WriteStartObject("tool");
            w.WriteStartObject("driver");
            w.WriteString("name", PackageInfo.Name);
            w.WriteString("organization", "Recursive AI");
            string? version = typeof(SarifExport).Assembly.GetName().Version?.ToString(3);
            if (version is not null)
            {
                w.WriteString("semanticVersion", version);
            }

            w.WriteStartArray("supportedTaxonomies");
            w.WriteStartObject();
            w.WriteString("name", TaxonomyName);
            w.WriteNumber("index", 0);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteStartArray("rules");
            foreach (IAccessibilityRule rule in _report.RulesRun)
            {
                WriteRule(w, rule);
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }

        private void WriteRule(Utf8JsonWriter w, IAccessibilityRule rule)
        {
            RuleDescriptor d = rule.Descriptor;
            w.WriteStartObject();
            w.WriteString("id", d.Id);
            w.WriteStartObject("shortDescription");
            w.WriteString("text", rule.Description);
            w.WriteEndObject();
            w.WriteStartObject("fullDescription");
            w.WriteString("text", FullDescription(rule));
            w.WriteEndObject();
            w.WriteStartObject("help");
            w.WriteString("text", HelpText(rule));
            w.WriteEndObject();
            if (HelpUri(d) is { } uri)
            {
                w.WriteString("helpUri", uri);
            }

            w.WriteStartObject("defaultConfiguration");
            w.WriteBoolean("enabled", true);
            w.WriteString("level", Level(rule.Impact));
            w.WriteNumber("rank", Rank(rule.Impact));
            w.WriteEndObject();
            if (d.Criteria.Count > 0)
            {
                w.WriteStartArray("relationships");
                foreach (SuccessCriterion c in d.Criteria)
                {
                    w.WriteStartObject();
                    w.WriteStartObject("target");
                    w.WriteString("id", c.Number);
                    w.WriteNumber("index", _taxonIndex[c.Number]);
                    w.WriteStartObject("toolComponent");
                    w.WriteString("name", TaxonomyName);
                    w.WriteNumber("index", 0);
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteStartArray("kinds");
                    w.WriteStringValue("relevant");
                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteStartObject("properties");
            w.WriteStartArray("tags");
            foreach (string t in d.Tags)
            {
                w.WriteStringValue(t);
            }

            w.WriteEndArray();
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
                w.WriteString("level", c.Level.ToString());
                w.WriteString("version", VersionText(c.IntroducedIn));
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteString("fix", d.Fix);
            w.WriteString("noFixReason", d.NoFixReason);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        private static string FullDescription(IAccessibilityRule rule)
        {
            RuleDescriptor d = rule.Descriptor;
            var sb = new StringBuilder(rule.Description);
            if (d.Criteria.Count > 0)
            {
                sb.Append(" Relevant WCAG success criteria: ");
                sb.AppendJoin("; ", d.Criteria.Select(c => c.Number + " " + c.Handle + " (level " + c.Level + ", WCAG " + VersionText(c.IntroducedIn) + ")"));
                sb.Append('.');
            }
            else
            {
                sb.Append(" A best-practice check that no success criterion requires.");
            }

            if (d.Needs == RuleNeeds.RenderedFacts)
            {
                sb.Append(" Needs rendered facts (layout or painting) for a verdict; without them it reports needs-render.");
            }

            return sb.ToString();
        }

        private static string HelpText(IAccessibilityRule rule)
        {
            RuleDescriptor d = rule.Descriptor;
            var sb = new StringBuilder(rule.Help);
            if (d.Fix is { Length: > 0 } fix)
            {
                sb.Append(" Suggested edit: ").Append(fix);
            }
            else if (d.NoFixReason is { Length: > 0 } reason)
            {
                sb.Append(" No suggested edit: ").Append(reason);
            }

            return sb.ToString();
        }

        // ACT rule pages and Deque University pages are the public documentation of those sources.
        private static string? HelpUri(RuleDescriptor d)
        {
            foreach (RuleSource s in d.Sources)
            {
                if (s.Kind == RuleSourceKind.Act)
                {
                    return "https://act-rules.github.io/rules/" + s.Id;
                }
            }

            foreach (RuleSource s in d.Sources)
            {
                if (s.Kind == RuleSourceKind.Axe)
                {
                    string v = AxeRules.AxeVersion;
                    int second = v.IndexOf('.', v.IndexOf('.', StringComparison.Ordinal) + 1);
                    return "https://dequeuniversity.com/rules/axe/" + (second < 0 ? v : v[..second]) + "/" + d.Id;
                }
            }

            return null;
        }

        private void WriteTaxonomies(Utf8JsonWriter w)
        {
            w.WriteStartArray("taxonomies");
            w.WriteStartObject();
            w.WriteString("name", TaxonomyName);
            w.WriteString("fullName", "Web Content Accessibility Guidelines");
            w.WriteString("organization", "W3C");
            w.WriteString("version", "2.2");
            w.WriteString("informationUri", "https://www.w3.org/TR/WCAG22/");
            w.WriteBoolean("isComprehensive", false);
            w.WriteStartArray("taxa");
            foreach (SuccessCriterion c in _taxa)
            {
                w.WriteStartObject();
                w.WriteString("id", c.Number);
                w.WriteString("name", c.Handle);
                w.WriteStartObject("shortDescription");
                w.WriteString("text", c.Handle);
                w.WriteEndObject();
                string track = c.RemovedIn is null ? "WCAG22" : "WCAG21";
                w.WriteString("helpUri", "https://www.w3.org/WAI/" + track + "/Understanding/" + c.Id + ".html");
                w.WriteStartObject("properties");
                w.WriteString("level", c.Level.ToString());
                w.WriteString("introducedIn", VersionText(c.IntroducedIn));
                w.WriteString("removedIn", c.RemovedIn is { } r ? VersionText(r) : null);
                w.WriteEndObject();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();
        }

        // ---- artifact and invocation (SARIF 3.24, 3.20) ----

        private void WriteArtifacts(Utf8JsonWriter w)
        {
            w.WriteStartArray("artifacts");
            w.WriteStartObject();
            w.WriteStartObject("location");
            w.WriteString("uri", _artifactUri);
            w.WriteEndObject();
            w.WriteString("mimeType", "text/html");
            w.WriteNumber("length", _source.Bytes.Length);
            w.WriteString("sourceLanguage", "html");
            w.WriteString("encoding", _source.Encoding.Name);
            w.WriteStartObject("hashes");
            w.WriteString("sha-256", Convert.ToHexStringLower(SHA256.HashData(_source.Bytes.Span)));
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();
        }

        private void WriteInvocations(Utf8JsonWriter w)
        {
            w.WriteStartArray("invocations");
            w.WriteStartObject();
            w.WriteBoolean("executionSuccessful", true);
            if (_report.LimitReached)
            {
                w.WriteStartArray("toolExecutionNotifications");
                w.WriteStartObject();
                w.WriteString("level", "warning");
                w.WriteStartObject("message");
                w.WriteString("text", "A rule reached the per-rule result limit; some results were dropped.");
                w.WriteEndObject();
                w.WriteStartObject("descriptor");
                w.WriteString("id", "limit-reached");
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndArray();
            }

            w.WriteEndObject();
            w.WriteEndArray();
        }

        // ---- results (SARIF 3.27) ----

        private void WriteResult(Utf8JsonWriter w, RuleResult r)
        {
            IAccessibilityRule rule = r.Rule;
            w.WriteStartObject();
            w.WriteString("ruleId", r.RuleId);
            if (_ruleIndex.TryGetValue(r.RuleId, out int index))
            {
                w.WriteNumber("ruleIndex", index);
            }

            // SARIF 3.27.9: a result whose kind is not "fail" has level "none".
            (string kind, string level) = r.Outcome switch
            {
                Outcome.Failed => ("fail", Level(rule.Impact)),
                Outcome.Passed => ("pass", "none"),
                Outcome.Inapplicable => ("notApplicable", "none"),
                _ => ("review", "none"),
            };
            w.WriteString("kind", kind);
            w.WriteString("level", level);
            if (r.Outcome is Outcome.Failed or Outcome.NeedsReview or Outcome.NeedsRender)
            {
                w.WriteNumber("rank", Rank(rule.Impact));
            }

            w.WriteStartObject("message");
            WriteSafe(w, "text", r.Message);
            w.WriteEndObject();
            if (!r.Target.IsNull && r.Target.Source.Range is { IsPresent: true } range)
            {
                w.WriteStartArray("locations");
                w.WriteStartObject();
                w.WriteStartObject("physicalLocation");
                WriteArtifactLocation(w);
                SourceRange bytes = r.SourceBytes;
                w.WritePropertyName("region");
                WriteRegion(w, range, bytes, snippet: true);
                w.WriteEndObject();
                if (r.Locator is { } locator)
                {
                    w.WriteStartArray("logicalLocations");
                    w.WriteStartObject();
                    w.WriteString("name", r.Target.IsElement ? r.Target.LocalName : "#text");
                    WriteSafe(w, "fullyQualifiedName", locator);
                    w.WriteString("kind", r.Target.IsElement ? "element" : "text");
                    w.WriteEndObject();
                    w.WriteEndArray();
                }

                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteStartObject("partialFingerprints");
                string basis = r.RuleId + "\n" + (r.Locator ?? range.Start.ToString(CultureInfo.InvariantCulture));
                w.WriteString("onehtml/v1", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(basis)))[..32]);
                w.WriteEndObject();
            }

            if (r.Outcome == Outcome.Failed && r.SuggestedEdits.Count > 0)
            {
                WriteFixes(w, r);
            }

            w.WriteStartObject("properties");
            w.WriteString("outcome", OutcomeName(r.Outcome));
            WriteSafe(w, "locator", r.Locator);
            w.WriteBoolean("blocksTask", r.TaskImpact == TaskImpact.BlocksTask);
            w.WriteString("taskImpactReason", r.TaskImpactReason);
            w.WriteBoolean("cssOnly", r.CssOnly);
            w.WriteStartArray("findings");
            foreach (Finding f in r.Findings)
            {
                w.WriteStartObject();
                WriteSafe(w, "attribute", f.Attribute);
                WriteSafe(w, "value", f.Value);
                WriteSafe(w, "message", f.Message);
                w.WriteEndObject();
            }

            w.WriteEndArray();
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
            w.WriteEndObject();
            w.WriteEndObject();
        }

        private void WriteArtifactLocation(Utf8JsonWriter w)
        {
            w.WriteStartObject("artifactLocation");
            w.WriteString("uri", _artifactUri);
            w.WriteNumber("index", 0);
            w.WriteEndObject();
        }

        // ---- fixes (SARIF 3.55): replacements computed by applying the edit and reading the changed byte ranges ----

        private sealed record FixData(string Description, List<Replacement> Replacements);

        private sealed record Replacement(SourceRange OldText, SourceRange OldBytes, ReadOnlyMemory<byte> NewBytes, string NewText);

        private void WriteFixes(Utf8JsonWriter w, RuleResult r)
        {
            bool started = false;
            foreach (SuggestedEdit s in r.SuggestedEdits)
            {
                FixData? fix = ComputeFix(s);
                if (fix is null)
                {
                    continue;
                }

                if (!started)
                {
                    w.WriteStartArray("fixes");
                    started = true;
                }

                w.WriteStartObject();
                w.WriteStartObject("description");
                WriteSafe(w, "text", fix.Description);
                w.WriteEndObject();
                w.WriteStartArray("artifactChanges");
                w.WriteStartObject();
                WriteArtifactLocation(w);
                w.WriteStartArray("replacements");
                foreach (Replacement rep in fix.Replacements)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("deletedRegion");
                    WriteRegion(w, rep.OldText, rep.OldBytes, snippet: false);
                    if (rep.NewBytes.Length > 0)
                    {
                        w.WriteStartObject("insertedContent");
                        if (IsPlainText(rep.NewBytes.Span))
                        {
                            w.WriteString("text", Encoding.UTF8.GetString(rep.NewBytes.Span));
                        }
                        else
                        {
                            // Bytes in the document's own encoding, kept exact.
                            w.WriteString("binary", Convert.ToBase64String(rep.NewBytes.Span));
                        }

                        w.WriteEndObject();
                    }

                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            }

            if (started)
            {
                w.WriteEndArray();
            }
        }

        // Inserted bytes are written as text when every consumer will read the same characters back: valid UTF-8 in a
        // UTF-8 document, or plain ASCII in a document whose encoding is ASCII-compatible. Anything else is written as
        // the exact bytes (SARIF 3.3 artifactContent.binary).
        private bool IsPlainText(ReadOnlySpan<byte> bytes)
        {
            string name = _source.Encoding.Name;
            if (name is "UTF-16LE" or "UTF-16BE" or "ISO-2022-JP" or "replacement")
            {
                return false;
            }

            if (System.Text.Ascii.IsValid(bytes))
            {
                return true;
            }

            if (!ReferenceEquals(_source.Encoding, WhatwgEncoding.Utf8))
            {
                return false;
            }

            try
            {
                _ = new UTF8Encoding(false, true).GetCharCount(bytes);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private FixData? ComputeFix(SuggestedEdit s)
        {
            Edit e = s.Edit;
            var key = (e.Operation, e.Target.Index, e.Name, e.Value);
            if (_fixes.TryGetValue(key, out FixData? cached))
            {
                return cached is null ? null : cached with { Description = s.Description };
            }

            FixData? data = null;
            EditResult applied = _report.Document.Apply(new[] { e });
            if (applied.Succeeded && applied.Changes is { } changes)
            {
                var reps = new List<Replacement>();
                foreach (EditedRange range in changes.Ranges.OrderBy(x => x.OldByteRange.Start))
                {
                    reps.Add(new Replacement(range.OldTextRange, range.OldByteRange, range.NewBytes, range.NewText));
                }

                data = new FixData(s.Description, reps);
            }

            _fixes[key] = data;
            return data;
        }

        // ---- regions (SARIF 3.30) ----

        private void WriteRegion(Utf8JsonWriter w, SourceRange text, SourceRange bytes, bool snippet)
        {
            (int startLine, int startColumn) = Position(text.Start);
            (int endLine, int endColumn) = Position(text.End);
            w.WriteStartObject();
            w.WriteNumber("startLine", startLine);
            w.WriteNumber("startColumn", startColumn);
            w.WriteNumber("endLine", endLine);
            w.WriteNumber("endColumn", endColumn);
            w.WriteNumber("byteOffset", bytes.Start);
            w.WriteNumber("byteLength", bytes.End - bytes.Start);
            if (snippet)
            {
                string raw = _source.Text.Substring(text.Start, text.End - text.Start);
                int max = Math.Max(0, _options.MaxHtmlLength);
                if (raw.Length > max)
                {
                    int cut = max;
                    if (cut > 0 && char.IsHighSurrogate(raw[cut - 1]))
                    {
                        cut--;
                    }

                    raw = raw[..cut];
                }

                w.WriteStartObject("snippet");
                WriteSafe(w, "text", raw);
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        // 1-based line and column; the column counts UTF-16 code units from the start of the line. The decoded text has
        // CR LF and CR turned into LF, which changes no line number and no column (a column never counts the line end).
        private (int Line, int Column) Position(int textOffset)
        {
            int lo = 0, hi = _lineStarts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >>> 1;
                if (_lineStarts[mid] <= textOffset)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return (lo + 1, textOffset - _lineStarts[lo] + 1);
        }
    }

    // A line ends at LF, CRLF or a lone CR, as the HTML input stream's newline normalisation treats them.
    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                starts.Add(i + 1);
            }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    // Lone surrogates cannot be written as JSON text; they become U+FFFD.
    private static void WriteSafe(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
        {
            w.WriteNull(name);
            return;
        }

        w.WriteString(name, Sanitize(value));
    }

    private static string Sanitize(string value)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool bad = false;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb?.Append(c).Append(value[i + 1]);
                    i++;
                    continue;
                }

                bad = true;
            }
            else if (char.IsLowSurrogate(c))
            {
                bad = true;
            }

            if (bad && sb is null)
            {
                sb = new StringBuilder(value.Length).Append(value, 0, i);
            }

            if (bad)
            {
                sb!.Append('\uFFFD');
            }
            else
            {
                sb?.Append(c);
            }
        }

        return sb?.ToString() ?? value;
    }

    // The artifact address must be a URI reference (RFC 3986): keep what is legal and percent-encode the rest.
    private static string EscapeUriReference(string url)
    {
        var sb = new StringBuilder(url.Length);
        foreach (byte b in Encoding.UTF8.GetBytes(Sanitize(url)))
        {
            char c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~:/?#[]@!$&'()*+,;=%".Contains(c, StringComparison.Ordinal)))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static string Level(Impact impact) => impact switch
    {
        Impact.Critical or Impact.Serious => "error",
        Impact.Moderate => "warning",
        _ => "note",
    };

    private static int Rank(Impact impact) => impact switch
    {
        Impact.Critical => 90,
        Impact.Serious => 70,
        Impact.Moderate => 50,
        _ => 30,
    };

    private static string OutcomeName(Outcome o) => o switch
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

    private static string Kebab(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 2);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsAsciiLetterUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('-');
                }

                sb.Append((char)(c | 0x20));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
