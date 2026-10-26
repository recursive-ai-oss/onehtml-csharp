# RecursiveAI.OneHtml.Accessibility.Validation

Accessibility checks over a `RecursiveAI.OneHtml` document and its accessibility tree, with no browser. One catalogue of rules covers WCAG 2.0, 2.1 and 2.2 at every level plus best practices, built from the W3C ACT Rules, the published axe-core rule list (ids, tags and impact), WCAG failure techniques and our own checks. Every rule records its sources, its success criteria with version and level (or `best-practice`), whether it needs rendered facts, and its fix or why it has none.

- **Results** carry an outcome (`Failed`, `Passed`, `NeedsReview`, `NeedsRender`, `Inapplicable`), labels, task impact, the facts used and missing, the style context, and the target as a node, a locator and a source range.
- **Profiles** select labels: WCAG 2.0 AA, 2.1 AA, 2.2 AA, or all. A profile names the standard it follows; it is not a legal claim.
- **Suggested edits** appear only where exactly one judgement-free fix exists (for example removing an undefined `aria-*` attribute). They are in the OneHtml edit format, are never applied by the library, and applying one clears the finding. `aria-valid-attr-value`, `aria-prohibited-attr` and `listitem` have no public fix: the correct edit depends on what the author meant (a mistyped value, a name meant for another element, a layout choice), so they report the problem and offer no suggestion.
- **Contrast** is computed where the cascade or supplied rendered facts show a solid background; it never guesses one.
- **Output:** native versioned JSON, axe-compatible JSON (lossy, documented; `NeedsRender` maps to `incomplete`) and SARIF 2.1.0.
- **Your own rules** implement a public interface and are registered with `new Checker(privateRules)`.

```csharp
using RecursiveAI.OneHtml;
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

The checker covers what markup, the cascade and supplied facts can decide. See the [coverage matrix](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/docs/coverage.md) for each criterion's rules and how far it can be automated, and the [ACT coverage](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/docs/act-coverage.md) for the per-rule test-case results.

## More

- Family overview, conformance numbers and benchmarks: [README](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/README.md).
- Behaviour documents: [docs/](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/docs).
- Third-party material and licences: [THIRD-PARTY-NOTICES.md](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/THIRD-PARTY-NOTICES.md).

Licensed under either of MIT or Apache-2.0 at your option. Copyright Recursive AI, LLC.
