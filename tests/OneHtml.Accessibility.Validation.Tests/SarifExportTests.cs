using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Encodings;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>The SARIF 2.1.0 export: schema validity, source regions, and fixes that equal what Apply produces.</summary>
public sealed class SarifExportTests
{
    private static string GoldenDir { get; } = Path.Combine(AriaActTests.Repo, "tests", "OneHtml.Accessibility.Validation.Tests", "Golden");

    private static readonly string[] s_failLevels = ["error", "warning", "note"];

    private static readonly string[] s_pages = ["aria-page", "names-page", "structure-page", "tables-forms-language-page", "visual-page"];

    private static JsonDocument Schema() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AriaActTests.Repo, "testdata", "sarif", "sarif-schema-2.1.0.json")));

    private static CheckReport Check(byte[] bytes)
    {
        Document doc = Document.Parse(bytes);
        return new Checker().Check(AccessibilityTree.Build(doc, DocumentStyles.Create(doc)));
    }

    private static CheckReport CheckPage(string page) => Check(File.ReadAllBytes(Path.Combine(GoldenDir, page + ".html")));

    private static void AssertValid(string sarif)
    {
        using JsonDocument schema = Schema();
        using JsonDocument instance = JsonDocument.Parse(sarif);
        IReadOnlyList<string> errors = new JsonSchemaValidator(schema.RootElement).Validate(instance.RootElement);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void The_validator_rejects_a_log_that_breaks_the_schema()
    {
        using JsonDocument schema = Schema();
        string[] broken =
        [
            """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"x"}},"results":[{"level":"fatal","message":{"text":"m"}}]}]}""",
            """{"version":"2.0.0","runs":[]}""",
            """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"x"}},"results":[{"message":{"text":"m"},"extra":1}]}]}""",
            """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"x"}},"results":[{"message":{"text":"m"},"locations":[{"physicalLocation":{"region":{"startLine":0}}}]}]}]}""",
            """{"version":"2.1.0","runs":[{"results":[]}]}""",
        ];
        foreach (string text in broken)
        {
            using JsonDocument instance = JsonDocument.Parse(text);
            Assert.NotEmpty(new JsonSchemaValidator(schema.RootElement).Validate(instance.RootElement));
        }

        using JsonDocument good = JsonDocument.Parse("""{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"x"}},"results":[]}]}""");
        Assert.Empty(new JsonSchemaValidator(schema.RootElement).Validate(good.RootElement));
    }

    [Theory]
    [InlineData("aria-page")]
    [InlineData("names-page")]
    [InlineData("structure-page")]
    [InlineData("tables-forms-language-page")]
    [InlineData("visual-page")]
    public void Golden_pages_export_valid_sarif(string page)
    {
        CheckReport report = CheckPage(page);
        var options = new ExportOptions { Indented = true, Url = "https://example.test/" + page };
        string sarif = report.ToSarifJson(options);
        AssertValid(sarif);
        AssertValid(report.ToSarifJson(new ExportOptions { IncludePassed = true, IncludeInapplicable = true }));

        using JsonDocument doc = JsonDocument.Parse(sarif);
        JsonElement run = doc.RootElement.GetProperty("runs")[0];
        int expected = report.Results.Count(r => r.Outcome is Outcome.Failed or Outcome.NeedsReview or Outcome.NeedsRender);
        Assert.Equal(expected, run.GetProperty("results").GetArrayLength());
        Assert.True(expected > 0);
        Assert.Equal(report.RulesRun.Count, run.GetProperty("tool").GetProperty("driver").GetProperty("rules").GetArrayLength());
        Assert.Equal("utf16CodeUnits", run.GetProperty("columnKind").GetString());
        Assert.Equal(sarif, report.ToSarifJson(options));
        AssertRegionsMatchSource(report, sarif, File.ReadAllBytes(Path.Combine(GoldenDir, page + ".html")), Encoding.UTF8);
    }

    [Fact]
    public void Results_point_at_their_rules_and_rules_at_the_wcag_taxonomy()
    {
        using JsonDocument doc = JsonDocument.Parse(CheckPage("visual-page").ToSarifJson());
        JsonElement run = doc.RootElement.GetProperty("runs")[0];
        JsonElement rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules");
        JsonElement taxa = run.GetProperty("taxonomies")[0].GetProperty("taxa");
        foreach (JsonElement result in run.GetProperty("results").EnumerateArray())
        {
            Assert.Equal(result.GetProperty("ruleId").GetString(), rules[result.GetProperty("ruleIndex").GetInt32()].GetProperty("id").GetString());
        }

        int related = 0;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            if (!rule.TryGetProperty("relationships", out JsonElement rels))
            {
                continue;
            }

            foreach (JsonElement rel in rels.EnumerateArray())
            {
                JsonElement target = rel.GetProperty("target");
                Assert.Equal(target.GetProperty("id").GetString(), taxa[target.GetProperty("index").GetInt32()].GetProperty("id").GetString());
                related++;
            }
        }

        Assert.True(related > 20);
        JsonElement contrast = rules.EnumerateArray().First(r => r.GetProperty("id").GetString() == "color-contrast");
        Assert.Equal("1.4.3", contrast.GetProperty("relationships")[0].GetProperty("target").GetProperty("id").GetString());
    }

    [Fact]
    public void Levels_and_kinds_follow_the_outcome()
    {
        CheckReport report = CheckPage("visual-page");
        using JsonDocument doc = JsonDocument.Parse(report.ToSarifJson(new ExportOptions { IncludePassed = true, IncludeInapplicable = true }));
        var kinds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (JsonElement r in doc.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            string outcome = r.GetProperty("properties").GetProperty("outcome").GetString()!;
            string kind = r.GetProperty("kind").GetString()!;
            string level = r.GetProperty("level").GetString()!;
            if (!kinds.TryAdd(outcome, [kind]))
            {
                kinds[outcome].Add(kind);
            }

            if (kind != "fail")
            {
                Assert.Equal("none", level);
            }
            else
            {
                Assert.Contains(level, s_failLevels);
            }
        }

        Assert.Equal(["fail"], kinds["failed"]);
        Assert.Equal(["pass"], kinds["passed"]);
        Assert.Equal(["review"], kinds["needs-render"]);
        Assert.Equal(["notApplicable"], kinds["inapplicable"]);
    }

    // ---- regions ----

    private static readonly Regex s_newline = new("\r\n|\n|\r", RegexOptions.CultureInvariant);

    // Region text from lines and columns over the raw file text (with its own line ends), written independently of the
    // exporter: split into lines and index into them.
    private static string RegionText(string raw, JsonElement region)
    {
        var starts = new List<int> { 0 };
        foreach (Match m in s_newline.Matches(raw))
        {
            starts.Add(m.Index + m.Length);
        }

        int Offset(int line, int column) => starts[line - 1] + column - 1;
        int start = Offset(region.GetProperty("startLine").GetInt32(), region.GetProperty("startColumn").GetInt32());
        int end = Offset(region.GetProperty("endLine").GetInt32(), region.GetProperty("endColumn").GetInt32());
        return raw[start..end];
    }

    private static Encoding Sjis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    // Every region must hold exactly the element's source: by line and column over the raw file text, and by byte range
    // decoded with an encoding other than the library's own.
    private static void AssertRegionsMatchSource(CheckReport report, string sarif, byte[] original, Encoding rawEncoding)
    {
        using JsonDocument doc = JsonDocument.Parse(sarif);
        JsonElement results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");
        RuleResult[] reported = report.Results.Where(r => r.Outcome is Outcome.Failed or Outcome.NeedsReview or Outcome.NeedsRender).ToArray();
        Assert.Equal(reported.Length, results.GetArrayLength());
        SourceText source = report.Document.Source;
        string raw = rawEncoding.GetString(original);
        if (raw.StartsWith('\uFEFF'))
        {
            raw = raw[1..];
        }

        int checkedRegions = 0;
        for (int i = 0; i < reported.Length; i++)
        {
            Node target = reported[i].Target;
            if (target.IsNull || !target.Source.Range.IsPresent)
            {
                Assert.False(results[i].TryGetProperty("locations", out _));
                continue;
            }

            SourceRange range = target.Source.Range;
            string elementText = source.Text[range.Start..range.End];
            JsonElement region = results[i].GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("region");
            string byLines = RegionText(raw, region);
            Assert.Equal(elementText, s_newline.Replace(byLines, "\n"));

            int byteOffset = region.GetProperty("byteOffset").GetInt32();
            int byteLength = region.GetProperty("byteLength").GetInt32();
            Assert.Equal(byLines, rawEncoding.GetString(original, byteOffset, byteLength));
            if (elementText.Length <= 300)
            {
                Assert.Equal(elementText, region.GetProperty("snippet").GetProperty("text").GetString());
            }

            checkedRegions++;
        }

        Assert.True(checkedRegions > 0);
    }

    private static byte[] Latin(string s) => Encoding.Latin1.GetBytes(s);

    [Fact]
    public void Regions_are_right_on_a_crlf_document()
    {
        string html = "<!doctype html><meta charset=utf-8>\r\n<html lang=en>\r\n<head><title>t</title></head>\r\n<body>\r\n<main>\r\n<img src=a.png>\r\n" +
                      "<p>caf\u00e9 <img\r\n  src=b.png> <img src=c.png></p>\r\n</main>\r\n</body></html>";
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        CheckReport report = Check(bytes);
        string sarif = report.ToSarifJson();
        AssertValid(sarif);
        AssertRegionsMatchSource(report, sarif, bytes, Encoding.UTF8);

        // The second image starts on line 7 after "<p>café " (8 UTF-16 units of text).
        using JsonDocument doc = JsonDocument.Parse(sarif);
        JsonElement[] regions = doc.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Where(r => r.GetProperty("ruleId").GetString() == "image-alt")
            .Select(r => r.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("region")).ToArray();
        Assert.Equal(3, regions.Length);
        Assert.Equal((6, 1), (regions[0].GetProperty("startLine").GetInt32(), regions[0].GetProperty("startColumn").GetInt32()));
        Assert.Equal((7, 9), (regions[1].GetProperty("startLine").GetInt32(), regions[1].GetProperty("startColumn").GetInt32()));
        Assert.Equal((8, 13), (regions[1].GetProperty("endLine").GetInt32(), regions[1].GetProperty("endColumn").GetInt32()));
    }

    [Fact]
    public void Regions_are_right_on_a_lone_cr_document()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<!doctype html>\r<html lang=en><title>t</title><body>\r<img src=a.png>\r<img\rsrc=b.png>");
        CheckReport report = Check(bytes);
        string sarif = report.ToSarifJson();
        AssertValid(sarif);
        AssertRegionsMatchSource(report, sarif, bytes, Encoding.UTF8);
    }

    [Fact]
    public void Columns_count_utf16_code_units_after_astral_characters_and_a_bom()
    {
        byte[] body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><html lang=en><title>t</title><body><p>\U0001F600\U0001F600 <img src=a.png></p>");
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. body];
        foreach (byte[] bytes in new[] { body, withBom })
        {
            CheckReport report = Check(bytes);
            string sarif = report.ToSarifJson();
            AssertValid(sarif);
            AssertRegionsMatchSource(report, sarif, bytes, Encoding.UTF8);
            using JsonDocument doc = JsonDocument.Parse(sarif);
            JsonElement region = doc.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
                .First(r => r.GetProperty("ruleId").GetString() == "image-alt")
                .GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("region");
            string prefix = "<!doctype html><meta charset=utf-8><html lang=en><title>t</title><body><p>\U0001F600\U0001F600 ";
            Assert.Equal(prefix.Length + 1, region.GetProperty("startColumn").GetInt32());
            // Bytes: the emoji take four each, and a byte order mark adds three.
            Assert.Equal(Encoding.UTF8.GetByteCount(prefix) + (bytes.Length - body.Length), region.GetProperty("byteOffset").GetInt32());
        }
    }

    [Fact]
    public void Regions_are_right_on_a_windows_1252_document()
    {
        string html = "<!doctype html>\r\n<meta charset=windows-1252><html lang=en><title>caf\u00e9</title><body>\r\n<p>d\u00e9j\u00e0 vu \u00e9t\u00e9 <img src=a.png alt></p>\r\n<p>na\u00efve <img src=b.png> r\u00e9sum\u00e9 <img src=c.png></p>";
        byte[] bytes = Latin(html);
        CheckReport report = Check(bytes);
        Assert.Equal("windows-1252", report.Document.Source.Encoding.Name);
        string sarif = report.ToSarifJson();
        AssertValid(sarif);
        AssertRegionsMatchSource(report, sarif, bytes, Encoding.Latin1);
        using JsonDocument doc = JsonDocument.Parse(sarif);
        Assert.Equal("windows-1252", doc.RootElement.GetProperty("runs")[0].GetProperty("artifacts")[0].GetProperty("encoding").GetString());
    }

    [Fact]
    public void Regions_are_right_on_a_shift_jis_document()
    {
        // 0x93FA 0x967B is the two-byte sequence for two kanji; every character before the images is two bytes.
        byte[] head = Latin("<!doctype html>\r\n<meta charset=Shift_JIS><html lang=ja><title>t</title><body>\r\n<p>");
        byte[] kanji = [0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA];
        byte[] tail = Latin(" <img src=a.png> ");
        byte[] more = Latin("<img src=b.png></p>");
        byte[] bytes = [.. head, .. kanji, .. tail, .. kanji, .. more];
        CheckReport report = Check(bytes);
        Assert.Equal("Shift_JIS", report.Document.Source.Encoding.Name);
        string sarif = report.ToSarifJson();
        AssertValid(sarif);
        AssertRegionsMatchSource(report, sarif, bytes, Sjis());
        using JsonDocument doc = JsonDocument.Parse(sarif);
        JsonElement first = doc.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .First(r => r.GetProperty("ruleId").GetString() == "image-alt")
            .GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("region");
        // Three characters (six bytes) then " ": the column counts characters, the byte offset counts bytes.
        Assert.Equal(3 + 1 + 3 + 1, first.GetProperty("startColumn").GetInt32());
        Assert.Equal(head.Length + kanji.Length + 1, first.GetProperty("byteOffset").GetInt32());
    }

    [Fact]
    public void The_artifact_address_is_a_valid_uri_reference_whatever_the_caller_gives()
    {
        CheckReport report = Check(Encoding.UTF8.GetBytes("<img src=a.png>"));
        foreach (string? url in new[] { null, "https://example.test/a page/\u00e9?q=1#x", "relative path.html" })
        {
            string sarif = report.ToSarifJson(new ExportOptions { Url = url });
            AssertValid(sarif);
        }

        using JsonDocument doc = JsonDocument.Parse(report.ToSarifJson());
        Assert.StartsWith("urn:onehtml:document:sha256:", doc.RootElement.GetProperty("runs")[0].GetProperty("artifacts")[0].GetProperty("location").GetProperty("uri").GetString(), StringComparison.Ordinal);
    }

    // ---- fixes ----

    private static byte[] ApplyFix(byte[] original, JsonElement fix)
    {
        JsonElement change = fix.GetProperty("artifactChanges")[0];
        var replacements = change.GetProperty("replacements").EnumerateArray().ToList();
        // Replacements are in ascending order of offset (SARIF 3.56.2); apply from the end so offsets stay valid.
        int last = -1;
        foreach (JsonElement r in replacements)
        {
            int at = r.GetProperty("deletedRegion").GetProperty("byteOffset").GetInt32();
            Assert.True(at >= last, "replacements are in ascending order");
            last = at + r.GetProperty("deletedRegion").GetProperty("byteLength").GetInt32();
        }

        byte[] bytes = original;
        for (int i = replacements.Count - 1; i >= 0; i--)
        {
            JsonElement r = replacements[i];
            JsonElement del = r.GetProperty("deletedRegion");
            int offset = del.GetProperty("byteOffset").GetInt32();
            int length = del.GetProperty("byteLength").GetInt32();
            byte[] inserted = [];
            if (r.TryGetProperty("insertedContent", out JsonElement content))
            {
                inserted = content.TryGetProperty("binary", out JsonElement bin)
                    ? Convert.FromBase64String(bin.GetString()!)
                    : Encoding.UTF8.GetBytes(content.GetProperty("text").GetString()!);
            }

            bytes = [.. bytes.AsSpan(0, offset), .. inserted, .. bytes.AsSpan(offset + length)];
        }

        return bytes;
    }

    private static int AssertFixesEqualApply(byte[] original, bool? expectBinary = null)
    {
        Document doc = Document.Parse(original);
        CheckReport report = new Checker().Check(AccessibilityTree.Build(doc, DocumentStyles.Create(doc)));
        string sarif = report.ToSarifJson();
        AssertValid(sarif);
        using JsonDocument json = JsonDocument.Parse(sarif);
        JsonElement results = json.RootElement.GetProperty("runs")[0].GetProperty("results");
        RuleResult[] reported = report.Results.Where(r => r.Outcome is Outcome.Failed or Outcome.NeedsReview or Outcome.NeedsRender).ToArray();
        int fixes = 0;
        bool sawBinary = false;
        for (int i = 0; i < reported.Length; i++)
        {
            if (reported[i].SuggestedEdits.Count == 0)
            {
                Assert.False(results[i].TryGetProperty("fixes", out _));
                continue;
            }

            JsonElement list = results[i].GetProperty("fixes");
            Assert.Equal(reported[i].SuggestedEdits.Count, list.GetArrayLength());
            for (int j = 0; j < list.GetArrayLength(); j++)
            {
                EditResult applied = doc.Apply(new[] { reported[i].SuggestedEdits[j].Edit });
                Assert.True(applied.Succeeded, applied.Failure);
                Assert.Equal(applied.Document!.Source.Bytes.ToArray(), ApplyFix(original, list[j]));
                Assert.Equal(reported[i].SuggestedEdits[j].Description, list[j].GetProperty("description").GetProperty("text").GetString());
                sawBinary |= list[j].GetRawText().Contains("\"binary\"", StringComparison.Ordinal);
                fixes++;
            }
        }

        if (expectBinary is { } want)
        {
            Assert.Equal(want, sawBinary);
        }

        return fixes;
    }

    [Fact]
    public void Every_fix_replaces_exactly_the_bytes_apply_changes_on_the_golden_pages()
    {
        int fixes = 0;
        foreach (string page in s_pages)
        {
            fixes += AssertFixesEqualApply(File.ReadAllBytes(Path.Combine(GoldenDir, page + ".html")));
        }

        Assert.True(fixes >= 3, "the golden pages carry suggested edits; found " + fixes);
    }

    [Fact]
    public void Every_fix_replaces_exactly_the_bytes_apply_changes_on_small_documents()
    {
        string[] cases =
        [
            "<body aria-hidden=\"true\"><p>x</p></body>",
            "<div role=\"directory\"><div role=\"listitem\">a</div></div>",
            "<input type=\"checkbox\" aria-checked=\"true\"><input type=\"checkbox\" checked aria-checked=\"false\">",
            "<div aria-foo=\"1\" id=a>x</div>\r\n<p aria-bar=2>y</p>",
            "<button tabindex=3>a</button><button tabindex=\"2\">b</button>",
            "<ul><li>a</li></ul><li>orphan</li>",
        ];
        int fixes = 0;
        foreach (string html in cases)
        {
            fixes += AssertFixesEqualApply(Encoding.UTF8.GetBytes(html));
        }

        Assert.True(fixes >= 5);
    }

    [Fact]
    public void Fixes_in_a_windows_1252_document_carry_exact_bytes()
    {
        byte[] html = Latin("<!doctype html><meta charset=windows-1252><title>caf\u00e9</title><p>\u00e9t\u00e9</p><input type=checkbox checked aria-checked=false title=\"\u00e9\"><div aria-foo=1>\u00e0</div>");
        Assert.True(AssertFixesEqualApply(html, expectBinary: false) >= 2);
    }

    [Fact]
    public void Fixes_in_a_shift_jis_document_carry_exact_bytes()
    {
        byte[] head = Latin("<!doctype html><meta charset=Shift_JIS><title>t</title><p>");
        byte[] kanji = [0x93, 0xFA, 0x96, 0x7B];
        byte[] tail = Latin("</p><input type=checkbox checked aria-checked=false title=\"");
        byte[] end = Latin("\"><div aria-foo=1>x</div>");
        Assert.True(AssertFixesEqualApply([.. head, .. kanji, .. tail, .. kanji, .. end], expectBinary: false) >= 2);
    }

    // A private rule whose suggested edit writes a non-ASCII value: the replacement must carry the document's own bytes.
    private sealed class TitleRule : IAccessibilityRule
    {
        public RuleDescriptor Descriptor { get; } = new("private-title", [RuleSource.Own("private-title")], [], true, RuleNeeds.Static, "Sets a title.", null, ["private"]);

        public string Description => "Images need a title.";

        public string Help => "Add a title.";

        public Impact Impact => Impact.Minor;

        public void Evaluate(CheckContext context, RuleReporter reporter)
        {
            foreach (Node img in context.Document.Root.QuerySelectorAll("img"))
            {
                reporter.Failed(img, "No title.", suggested: [new SuggestedEdit(Edit.SetAttribute(img, "title", "caf\u00e9"), "Set the title.")]);
            }
        }
    }

    [Theory]
    [InlineData("<meta charset=utf-8><img src=a.png>", false)]
    [InlineData("<meta charset=windows-1252><img src=a.png>", true)]
    // Shift_JIS has no é, so the edit writes a character reference: ASCII, hence text.
    [InlineData("<meta charset=Shift_JIS><img src=a.png>", false)]
    public void A_non_ascii_insertion_is_text_in_utf_8_and_exact_bytes_elsewhere(string html, bool binary)
    {
        byte[] original = Encoding.ASCII.GetBytes(html);
        Document doc = Document.Parse(original);
        CheckReport report = new Checker([new TitleRule()]).Check(doc, new CheckOptions { Tags = ["private"] });
        using JsonDocument json = JsonDocument.Parse(report.ToSarifJson());
        JsonElement fix = json.RootElement.GetProperty("runs")[0].GetProperty("results")[0].GetProperty("fixes")[0];
        AssertValid(report.ToSarifJson());
        Assert.Equal(binary, fix.GetRawText().Contains("\"binary\"", StringComparison.Ordinal));
        EditResult applied = doc.Apply(new[] { report.Results[0].SuggestedEdits[0].Edit });
        Assert.Equal(applied.Document!.Source.Bytes.ToArray(), ApplyFix(original, fix));
    }

    [Fact]
    public void Golden_sarif_matches_the_file()
    {
        string path = Path.Combine(GoldenDir, "aria-page.sarif.json");
        string actual = CheckPage("aria-page").ToSarifJson(new ExportOptions { Indented = true, Url = "https://example.test/page" });
        if (Environment.GetEnvironmentVariable("ONEHTML_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        Assert.Equal(File.ReadAllText(path), actual);
    }
}
