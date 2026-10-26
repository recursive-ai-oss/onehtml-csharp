# SARIF export

`ToSarifJson` (an extension on `CheckReport`, in `SarifExport`) writes the report as SARIF 2.1.0, the OASIS Static
Analysis Results Interchange Format (Errata 01). The mapping is version 1 (`SarifExport.ExportVersion`, written to the
run's `properties.exportVersion`); a change that alters meaning raises it. The output is deterministic, uses
`System.Text.Json` writers only, and validates against the schema published with the standard
(`testdata/sarif/sarif-schema-2.1.0.json`, checked by the Validation tests for every golden page).

```csharp
string sarif = report.ToSarifJson(new ExportOptions { Url = "https://example.test/page.html", Indented = true });
```

## Options

`ExportOptions` is shared with the other exports.

| Option | Use in SARIF |
|---|---|
| `Url` | the artifact address, written to `artifactLocation.uri`; percent-encoded where it is not a legal URI reference. When absent, `urn:onehtml:document:sha256:<hash of the bytes>` |
| `Indented` | indented output |
| `MaxHtmlLength` | the longest region snippet, in characters (default 300) |
| `IncludePassed` | also write passed results (`kind: pass`); default off, since a pass is not a finding |
| `IncludeInapplicable` | also write one `notApplicable` result per rule that found nothing to check; default off |

## Run

One run. `tool.driver` is `RecursiveAI.OneHtml.Accessibility.Validation` with its semantic version; `columnKind` is
`utf16CodeUnits`. One artifact: the checked document, with its `uri`, `mimeType` `text/html`, byte `length`,
`sourceLanguage` `html`, the `encoding` it was decoded with and its `sha-256`. The run has one invocation, successful;
when a rule reached `CheckOptions.MaxResultsPerRule` it also carries a warning notification `limit-reached`. Run
`properties`: `exportVersion`, `profile` (the profile name or null) and `cssOnly` (the style context had interaction
state).

## Results

| Our outcome | `kind` | `level` | Written |
|---|---|---|---|
| failed | `fail` | from the rule's impact: critical and serious `error`, moderate `warning`, minor `note` | always |
| needs-review | `review` | `none` | always |
| needs-render | `review` | `none` | always |
| passed | `pass` | `none` | with `IncludePassed` |
| inapplicable | `notApplicable` | `none` | with `IncludeInapplicable` |

SARIF (3.27.9) requires level `none` on any result whose kind is not `fail`, which is why a review result carries no
severity; its `rank` (below) and its `properties.outcome` (`needs-review` or `needs-render`) tell a consumer how to
treat it. A consumer that filters by level alone will therefore not see review results; filter on `kind` as well.

Every result has `ruleId` and `ruleIndex`, `message.text` (the result's sentence), and, for fail and review results,
`rank` from the impact (critical 90, serious 70, moderate 50, minor 30). Results are in report order: rule id, then
target in document order.

**Locations.** A result with a target that has a source range gets one location. Results without one (the target is
parser-made and has no bytes, or the outcome is inapplicable) have no `locations`. The region is the target's source
range as the source map has it: an element's start tag, or the construct itself for a text node, comment or doctype. A
start tag the parser implied has an empty region at the token that caused it.

| Region member | Value |
|---|---|
| `startLine`, `startColumn`, `endLine`, `endColumn` | 1-based; columns count UTF-16 code units of the decoded file from the start of the line; the end is the position after the last character, as SARIF defines it |
| `byteOffset`, `byteLength` | offsets into the file's bytes, counting any byte order mark; authoritative for any encoding |
| `snippet.text` | the region's text, cut to `MaxHtmlLength` characters |

Lines end at LF, CR LF or a lone CR. A byte order mark is not counted in any column. Columns count characters of the
file as decoded with the encoding the library chose (byte order mark, then the transport label, then the `<meta>`
prescan, then the default of windows-1252), so they are right for Shift_JIS, windows-1252 and UTF-16 files as well as
UTF-8, and an astral character counts two. Character offsets (`charOffset`, `charLength`) are not written: the
library's decoded text has CR LF and lone CR turned into LF, as HTML parsing requires, so its offsets differ from the
file's. For the same reason a snippet of a start tag that spans lines has LF line ends even when the file uses CR LF.

`logicalLocations` has the target's tag name (`#text` for text), its locator as `fullyQualifiedName`, and kind `element`
or `text`. `partialFingerprints["onehtml/v1"]` is the first 32 hex digits of the SHA-256 of the rule id, a line feed and
the locator (the start offset when there is none); it is stable while the element keeps its place in the document.
`properties` has `outcome`, `locator`, `blocksTask`, `taskImpactReason`, `cssOnly`, `findings`, `factsUsed` and
`factsMissing`, as in the native export.

## Rules

`tool.driver.rules` lists every rule that ran, in id order (`ruleIndex` is the position).

| SARIF member | From |
|---|---|
| `id` | the rule id |
| `shortDescription` | the rule's description |
| `fullDescription` | the description, the success criteria it is relevant to with level and WCAG version (or that it is a best practice no criterion requires), and whether it needs rendered facts |
| `help` | the help sentence, then the suggested edit the rule names or why it has none (for example `aria-valid-attr-value`, `aria-prohibited-attr` and `listitem` always say why they have none) |
| `helpUri` | the ACT rule page if the rule has an ACT source, else the Deque University page of the pinned axe version if it has an axe source, else none |
| `defaultConfiguration` | `enabled`, `level` and `rank` from the impact, as for results |
| `relationships` | one per success criterion, kind `relevant`, targeting the WCAG taxon by `id` and `index` |
| `properties` | `tags` (the rule's tags), `impact`, `bestPractice`, `needs`, `sources` (kind and id: ACT, axe, failure technique, own), `criteria` (number, level, version), `fix`, `noFixReason` |

**Taxonomy.** `run.taxonomies` has one component, `WCAG` (W3C, version 2.2, `isComprehensive` false), whose taxa are the
success criteria that some rule that ran is relevant to, in number order. A taxon has the criterion number as `id`, its
handle as `name`, a `helpUri` to the Understanding page (the WCAG 2.1 pages for 4.1.1, which 2.2 removed), and
`properties`: `level`, `introducedIn`, `removedIn`. A rule maps to a criterion with kind `relevant` because a rule
usually checks one failure condition of a criterion, not the whole criterion.

## Fixes

A failed result with suggested edits has one SARIF `fix` per edit, with the edit's description. The replacements are not
translated from the edit; they are read from running it: the library applies the edit alone to the checked document
(`Document.Apply`, with its re-parse verification) and writes each changed byte range as a replacement. So
applying the replacements to the original bytes gives exactly the bytes `Apply` produces, and nothing outside the edited
ranges changes (a test checks this on every suggested edit of the golden pages and on edited documents in UTF-8,
windows-1252 and Shift_JIS).

- `deletedRegion` has the range's `byteOffset`/`byteLength` and its line and column members; an insertion has length 0.
- `insertedContent` is absent for a deletion. It is `text` when the inserted bytes are valid UTF-8 in a UTF-8 document or
  plain ASCII in an ASCII-compatible one, and otherwise `binary` (base64 of the exact bytes in the document's encoding).
  In UTF-16 and ISO-2022-JP documents it is always `binary`.
- Replacements are in ascending offset order and do not overlap.
- If an edit cannot be applied, which would be a library fault, the result carries no `fix` for it.

Each fix costs one `Apply` (about one parse of the document), cached per distinct edit. The library never applies a fix.

## What is not exported

EARL, which is a separate export. The native export's style context and suggested-edit envelope have no SARIF counterpart.
