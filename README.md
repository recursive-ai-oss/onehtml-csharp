# onehtml

**HTML and CSS libraries for .NET: parse HTML the way a browser does, edit it without disturbing a byte the edit didn't touch, compute the accessibility tree Chromium exposes, and check pages for accessibility problems without a browser.** Pure C# with no dependency beyond .NET itself; every input is treated as hostile and read through safe, bounds-checked code with limits checked before the work they guard.

[![CI](https://github.com/recursive-ai-oss/onehtml-csharp/actions/workflows/ci.yml/badge.svg)](https://github.com/recursive-ai-oss/onehtml-csharp/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](#install)
[![License](https://img.shields.io/badge/license-MIT%20OR%20Apache--2.0-blue)](#license)

## Packages

| Package | What it is |
|---|---|
| [`RecursiveAI.OneHtml`](src/OneHtml/README.md) | The WHATWG HTML parser (encoding sniffing, tokenizer, every insertion mode), the DOM as immutable arena snapshots with a source map, Selectors Level 4, locators, a CSS parser, the cascade for every known property under a `StyleContext`, and lossless editing of HTML and CSS. |
| [`RecursiveAI.OneHtml.Accessibility`](src/OneHtml.Accessibility/README.md) | The accessibility tree (WAI-ARIA 1.2, HTML-AAM, AccName 1.2, Core-AAM, SVG-AAM) with Chromium's choices written down, incremental recompute after edits, a reading view, navigation lists, tree diffs and a `RenderedFacts` adapter for Chrome's `DOMSnapshot`. Builds on `RecursiveAI.OneHtml`. |
| [`RecursiveAI.OneHtml.Accessibility.Validation`](src/OneHtml.Accessibility.Validation/README.md) | One rule catalogue covering the WCAG success criteria and best practices, with labelled results, suggested edits where exactly one fix exists, and native, axe-compatible and SARIF 2.1.0 output. Builds on `RecursiveAI.OneHtml.Accessibility`. |

## Install

```
dotnet add package RecursiveAI.OneHtml
dotnet add package RecursiveAI.OneHtml.Accessibility
dotnet add package RecursiveAI.OneHtml.Accessibility.Validation
```

The libraries target `net10.0`, are NativeAOT- and trim-compatible, contain no `unsafe` code, and use only the .NET base class library at runtime. They never read files or the network: linked stylesheets and `DOMSnapshot` JSON are supplied by the caller, and scripts are inert text.

## Quick start

Parse, query and edit. An edit changes only the bytes it touches, the result is re-parsed and verified, and the old snapshot stays valid.

```csharp
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Editing;
Document doc = Document.Parse("<p>Hello <a href=/x>world</a>");
Node link = doc.Root.QuerySelector("a[href]");
EditResult edit = doc.Apply(new[] { Edit.SetAttribute(link, "title", "A link") });
byte[] output = edit.Document!.ToLosslessBytes();
```

Compute styles, with or without a focused element:

```csharp
using RecursiveAI.OneHtml.Css;
Document doc = Document.Parse("<style>a { color: red } a:focus { color: blue }</style><a href=/x>x</a>");
Node link = doc.Root.QuerySelector("a");
DocumentStyles styles = DocumentStyles.Create(doc);
string normal = styles.GetStyle(link).GetPropertyValue("color");
string focused = styles.WithContext(StyleContext.Default.WithFocus(link)).GetStyle(link).GetPropertyValue("color");
```

Build the accessibility tree and what a screen-reader user meets:

```csharp
using RecursiveAI.OneHtml.Accessibility;
Document doc = Document.Parse("<main><h1>Title</h1><img src=a.png alt='Logo'><button>Save</button></main>");
DocumentStyles styles = DocumentStyles.Create(doc);
AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
foreach (AxNode node in tree.Nodes.Where(n => !n.IsIgnored))
{
    Console.WriteLine($"{node.Role} \"{node.Name.Value}\"");
}
string reading = tree.CreateReadingView().ToText();
int headings = tree.CreateNavigationLists().Headings.Count;
```

Check a page and export the results:

```csharp
using RecursiveAI.OneHtml.Accessibility.Validation;
Document doc = Document.Parse("<html lang=en><title>t</title><img src=a.png><button></button>");
CheckReport report = new Checker().Check(doc);
foreach (RuleResult result in report.WithOutcome(Outcome.Failed))
{
    Console.WriteLine($"{result.RuleId}: {result.Message}");
}
string axeJson = report.ToAxeJson();
string sarif = report.ToSarifJson();
```

These samples are compiled by the test suite (`tests/OneHtml.Accessibility.Validation.Tests/ReadmeSamples.cs`).

## Conformance

Every number below comes from a test or a gate run in this repository; the document named in the last column holds the method, the pinned versions and every known failure with its reason. The test data is vendored under `testdata/` with its licence (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)); crawled pages are never redistributed.

| What | Result | Where |
|---|---|---|
| html5lib-tests, tokenizer and encoding | all cases run; deliberate exceptions listed with reasons | `tests/OneHtml.Tests/Html5lib/KnownDifferences.cs` |
| WPT HTML parsing (tree construction) | all cases run; deliberate exceptions listed with reasons | `tests/OneHtml.Tests/Html5lib/TreeConstructionKnownDifferences.cs` |
| WPT selectors (`dom/nodes`, `css/selectors/parsing`, pseudo-classes) | 2,537 pass, 43 known failures (tentative proposals), 96 not applicable | [docs/selectors.md](docs/selectors.md) |
| WPT cascade, variables, nesting, specificity, computed values | 554 pass, 28 known failures, 587 not applicable (script- or layout-dependent) | [docs/cascade.md](docs/cascade.md) |
| WPT accessible names (`accname/`, `html-aam/`) | 1,297 pass, 29 known failures | [docs/accessibility-tree.md](docs/accessibility-tree.md) |
| WPT ARIA roles (`html-aam/`, `wai-aria/role/`) | 536 pass, 18 known failures (tentative pages), 5 not applicable | [docs/accessibility-roles.md](docs/accessibility-roles.md) |
| ACT Rules test cases | all 37 approved rules implemented, 0 failing cases; 36 of 50 proposed rules implemented, the rest documented as not automatable | [docs/act-coverage.md](docs/act-coverage.md) |
| Round trip, no edits | byte-identical on 18,330 of 18,330 corpus pages and stylesheets | `tools/OneHtml.Differential` (`roundtrip`) |
| AngleSharp tree differential | 12,469 of 13,857 pages (89.98%) identical; every difference in the rest has a documented cause tied to the standard | [docs/anglesharp-differential.md](docs/anglesharp-differential.md) |
| Chrome accessibility-tree differential | mismatch rate per field, with every class fixed or documented | [docs/chrome-ax-differential.md](docs/chrome-ax-differential.md) |
| WCAG coverage | the coverage matrix of every criterion, with the rules that check it and whether it is automatable | [docs/coverage.md](docs/coverage.md) |

The corpus is 300 school and university sites crawled under robots.txt with a crawl delay, plus a Common Crawl sample (13,857 pages and 4,473 stylesheets reach the gates).

## Performance

Parsing a 100-page sample (36 MB) of that corpus from bytes, end to end, on an AMD Ryzen 9 7950X3D (32 threads), .NET SDK 10.0.402, BenchmarkDotNet in process. Measured on 2026-10-26 on the code released as 1.0.0 (the latest published run; not repeated for later versions):

| Parser | Time per sweep | Throughput | Allocated per sweep |
|---|---:|---:|---:|
| OneHtml | 116.1 ms | 310 MB/s | 258 MB |
| AngleSharp 1.8.6 | 345.4 ms | 104 MB/s | 385 MB |
| HtmlAgilityPack 1.13.1 | 286.7 ms | 126 MB/s | 375 MB |

That is 2.97× AngleSharp's speed and 2.47× HtmlAgilityPack's. Reading a parsed page costs the same on a fresh snapshot, an old one and an edited one (about 100 µs for a 35,000-node page). One edit on that page, which re-parses and verifies the result, takes about 6 ms (4.1 ms to parse it). The method and harness are in [bench/README.md](bench/README.md).

## What it does not do

- **No layout and no painting.** Layout-dependent behaviour comes only from `RenderedFacts` that a caller captured from a browser.
- **No script.** Scripts are inert text. A page built by script is supplied as the browser's serialized DOM.
- **No fetching.** Nothing in the libraries touches the network or the file system.
- **Script-driven state is invisible,** so results that depend on hover, focus or similar state are labelled "CSS only".
- **The checker does not replace human review.** It checks what can be decided from markup, the cascade and supplied facts, and says `needs-review` or `needs-render` otherwise.

## Behaviour that is written down

Where the specifications leave room, the choice is documented in [docs/](docs): the parser's notes, the Chromium profile ([docs/accessibility-chromium-profile.md](docs/accessibility-chromium-profile.md)), the rule catalogue ([docs/checker.md](docs/checker.md)), the edit format ([docs/edit-format.md](docs/edit-format.md)) and the result formats ([docs/results.md](docs/results.md)). A port to another language should be able to reproduce the behaviour from these documents alone.

## Build and test

Requires the .NET 10 SDK.

```
dotnet build -c Release
dotnet test -c Release
dotnet pack -c Release
dotnet publish tests/OneHtml.AotSmoke -r linux-x64 -c Release   # NativeAOT / trim check; use your own RID
```

Build output goes to `artifacts/` in the repo by default. Set the `ONEHTML_ARTIFACTS` environment variable to put it elsewhere.

## Layout

- `src/` the three packages
- `tests/` unit tests and the NativeAOT smoke app
- `bench/` benchmarks against AngleSharp, HtmlAgilityPack and axe-core in Chrome
- `fuzz/` fuzz targets
- `tools/` conformance runners, differentials and generators
- `docs/` the normative behaviour documents

## Licence

Licensed under either of [MIT](LICENSE-MIT) or [Apache-2.0](LICENSE-APACHE) at your option. Copyright Recursive AI, LLC. Third-party material (test suites, generated tables from specification data) is listed with its licence in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
