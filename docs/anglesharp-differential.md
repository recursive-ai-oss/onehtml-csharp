# AngleSharp tree differential

How our trees compare with AngleSharp's on a corpus of real pages, and why each remaining difference is ours being right. AngleSharp is used as a black box (its output only), in the `tools/OneHtml.Differential` harness and never in the library. Every cause below is tied to the text of the WHATWG HTML Standard (parsing, §13.2), the Encoding Standard or the DOM Standard, and has a hand-written test of our side in `tests/OneHtml.Tests/AngleSharpDivergenceTests.cs`.

## Method

Both parsers get the same decoded text (the text our sniffer chose), so the tree comparison is not muddied by encoding; the two sniffed encodings are compared as a field of their own. Trees are compared in document order: node kind, namespace, local name, attributes (paired by namespace and local name, then qualified name, value, order), text, comments, doctype fields, template contents, child counts and quirks mode. Below a structural difference the trees no longer line up by position, so that subtree is not compared further. Each difference is traced to a cause: from the field and node types, or, for structural ones, from a minimized reproducer. A difference with no known cause is reported as `unexplained`.

## Result on corpus 2026-10a (13,857 pages)

12,469 pages (89.98%) have identical trees. Every difference in the other 1,388 pages falls into one of the causes below; none is unexplained. Rates are pages with at least one difference:

| field | pages | rate | cause |
|---|---:|---:|---|
| `attr.prefix` | 1,248 | 9.0% | `xlink-prefix` |
| `text` | 74 | 0.53% | `raw-text-end-tag-case`, `character-reference-bare-ampersand` |
| `attr.name` | 62 | 0.45% | `xlink-attributes` |
| `childCount` | 53 | 0.38% | `declarative-shadow-root`, `select-content`, `menuitem-element`, `search-element`, `cell-end-tag`, `nesting-depth-cap`, `adoption-agency-time-element`, `form-end-tag-in-template` |
| `encoding` | 33 | 0.24% | `encoding-gbk-name`, `encoding-xml-declaration`, `encoding-charset-parameter-case` |
| `localName` | 32 | 0.23% | `svg-tag-names` |
| `kind` | 30 | 0.22% | `processing-instruction` and the structural causes |
| `attr.value` | 10 | 0.07% | `character-reference-bare-ampersand` |
| `quirks`, `namespace`, `attr.namespace`, `attr.order`, `comment`, `doctype.*`, `template` | 0 | 0% | |

A page may show several causes; the page counts per cause are in the cause sections. A further 50 pages differ only in the last-resort encoding (below) and count as identical.

## Causes

Each section gives the input, the standard's text, what each parser does, and the pages affected on the corpus.

### `xlink-prefix`, `xlink-attributes` (1,231 and 58 pages)

`<svg><use xlink:href="#a">`. HTML 13.2.6.5 "adjust foreign attributes" maps `xlink:href` to the attribute with prefix `xlink`, local name `href`, in the XLink namespace. We keep the prefix; AngleSharp reports the attribute as plain `href` (it keeps the namespace but drops the prefix). When an element has both `href` and `xlink:href`, which the DOM Standard allows since they differ in namespace, AngleSharp keeps one (`xlink-attributes`).

### `processing-instruction` (21 pages)

`<?p q?>`. The parsing section of the standard we implement (the current text, with the "processing instruction open", "target", "after target", "data" and "questionable" states, 13.2.5.72–76) tokenizes `<?` followed by a valid target as a processing instruction and inserts a ProcessingInstruction node; only an invalid target becomes a bogus comment. AngleSharp implements the older rule: every `<?` is a bogus comment. Browsers have not all adopted the new text; a consumer that wants comments instead should treat our `ProcessingInstruction` nodes as comments.

### `svg-tag-names` (3 pages)

`<svg><feDropShadow>`. The "adjust SVG tag names" table in 13.2.6.5 includes `feDropShadow` (`fedropshadow` → `feDropShadow`). AngleSharp's table lacks it and leaves the name lowercase.

### `raw-text-end-tag-case` (45 pages)

`<style></C` at the end of the input. In 13.2.5.14 "RAWTEXT end tag name state", an ASCII upper alpha appends its lowercase form to the tag name and the character as typed to the temporary buffer; when the end tag is not appropriate or the input ends, the buffer's characters (not the tag name) are emitted as text, so the text is `</C`. AngleSharp emits `</c`. The same states exist for RCDATA, script data and escaped script data.

### `character-reference-bare-ampersand` (8 pages)

`a&;b` and `<p title="&;">`. 13.2.5.77 "character reference state": a `&` followed by something other than an alphanumeric or `#` flushes the code points consumed so far as text (or attribute value) and reconsumes the next character in the return state, so the `;` stays: `a&;b`. AngleSharp drops the `;`.

### `encoding-gbk-name` (23 pages)

A page labelled `gb2312` or `gbk`. The Encoding Standard's label table maps these labels to the encoding named `GBK`; `gb18030` is a separate encoding (the GBK decoder is the gb18030 decoder). AngleSharp reports `gb18030`. The decoded text is the same.

### `encoding-xml-declaration` (5 pages)

`<?xml version="1.0" encoding="Shift_JIS"?>` at the start of a page served without a charset. 13.2.3.2's prescan step "looking for syntax resembling an XML declaration, even in text/html" takes the declared encoding when no BOM, transport charset or `<meta>` decides first. AngleSharp ignores the declaration and uses UTF-8.

### `encoding-charset-parameter-case` (5 pages)

`Content-Type: text/html; Charset=utf-8` with a `<meta>` declaring another encoding. Parameter names of a MIME type are matched ASCII case-insensitively (MIME Sniffing, "parse a MIME type", which lowercases them), so the transport layer says UTF-8 and wins over the `<meta>`. AngleSharp ignores a `Charset` parameter spelled with a capital.

### The last-resort encoding (50 pages, not counted)

With no BOM, header, `<meta>` or XML declaration, 13.2.3.2 leaves the encoding "implementation-defined or user-specified", and recommends a locale-dependent default: windows-1252 for most locales. Ours is windows-1252 and is not changed; AngleSharp's is UTF-8. A page where only this default differs is listed in the summary but is not a mismatch.

### `declarative-shadow-root` (24 pages)

`<div><template shadowrootmode=open><b></b></template></div>`. In the "in body" and "in head" template start-tag steps the `shadowrootmode` attribute attaches a shadow root only when the parser's "allow declarative shadow roots" flag is true; it is initially false (13.2.4.5) and is set only by the parseHTMLUnsafe family of APIs. A document parsed from bytes therefore keeps the `template` element and its content fragment, which is what we produce. AngleSharp always attaches a shadow root, so the host has no child.

### `select-content` (9 pages)

`<select><option><n></select>`. The current standard has no "in select" insertion mode: a `select`'s content is parsed as in body, and elements other than `option` and `optgroup` stay inside it. We follow that; AngleSharp implements the older mode and drops or relocates such elements.

### `menuitem-element` (8 pages)

`<div><menuitem><`. `menuitem` no longer appears in the parsing algorithm, so it is an ordinary element that stays open. AngleSharp closes it like a void element.

### `search-element` (3 pages)

`<div><search><div></search>x`. `search` is in the lists of 13.2.6.4.7 "in body" that close a `p` element in button scope and whose end tag pops to the element (and in the "special" category). AngleSharp does not know `search`, so `</search>` is an "any other end tag" that stops at the special `div` and is ignored.

### `cell-end-tag` (5 pages)

`<table><tr><th></td>x`. In 13.2.6.4.15 "in cell" an end tag `td` or `th` is acted on only if an element with that tag name is in table scope; otherwise it is a parse error and ignored. A `</td>` does not close a `th`. AngleSharp closes the cell.

### `adoption-agency-time-element` (1 page)

`<ul><li><a><time></a>x`. In the adoption agency algorithm (13.2.6.4.7), the furthest block is the topmost *special* element below the formatting element; `time` is not special, so there is none, and the algorithm pops the stack up to and including the formatting element and removes it from the list of active formatting elements. `time` ends up inside the `a`, and the text after `</a>` is outside. AngleSharp treats `time` like a block and clones the `a` inside it.

### `form-end-tag-in-template` (1 page)

`<form><div><template><o></form>x</template>y</div>z</form>w`. In "in body", a `</form>` end tag with a `template` on the stack of open elements is ignored unless a `form` element is in scope, and `template` is a scope boundary; later, with no template open, `</form>` removes the form element the form element pointer names. So `z` stays in the form and `w` is outside it. AngleSharp keeps `w` inside the form.

### `nesting-depth-cap` (3 pages)

Three non-HTML bodies (PDFs served as `text/html`) whose binary content makes thousands of unclosed unknown elements. The standard sets no nesting limit. We nest up to `ParseLimits.MaxOpenElements` (4096, a hostile-input guard that fails cleanly with `HtmlLimitExceededException`); AngleSharp stops nesting at 512 and appends later elements as siblings.

## Reproducers

Reproducers are minimized from the crawled pages for the harness's own use and are never committed (they are derived from third-party pages). The inputs above were written by hand.
