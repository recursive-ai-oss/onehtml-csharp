# onehtml

HTML and CSS libraries for .NET. **In development: nothing here is usable yet.**

| Package | What it is |
|---|---|
| `RecursiveAI.OneHtml` | HTML and CSS parsing, selectors, styles and lossless editing. |
| `RecursiveAI.OneHtml.Accessibility` | The accessibility tree for a OneHtml document. Builds on `RecursiveAI.OneHtml`. |
| `RecursiveAI.OneHtml.Accessibility.Validation` | Accessibility checks that need no browser. Builds on `RecursiveAI.OneHtml.Accessibility`. |

The libraries target `net10.0`, use only the .NET base class library at runtime, and are written to be NativeAOT- and trim-compatible.

## Build and test

Requires the .NET 10 SDK.

```
dotnet build -c Release
dotnet test -c Release
dotnet publish tests/OneHtml.AotSmoke -r linux-x64 -c Release   # NativeAOT / trim check; use your own RID
```

Build output goes to `artifacts/` in the repo by default. Set the `ONEHTML_ARTIFACTS` environment variable to put it elsewhere.

## Layout

- `src/` the three packages
- `tests/` unit tests and the NativeAOT smoke app
- `bench/` benchmarks
- `fuzz/` fuzz targets
- `tools/` differential and other tooling

## Licence

Licensed under either of [MIT](LICENSE-MIT) or [Apache-2.0](LICENSE-APACHE) at your option. Copyright Recursive AI, LLC.
