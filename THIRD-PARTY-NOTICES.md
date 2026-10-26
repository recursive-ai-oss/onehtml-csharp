# Third-party notices

The libraries have no runtime dependencies beyond the .NET base class library. The material below is either
compiled into the libraries as generated tables or used only by the tests.

Each licence text below was compared with, or copied from, the primary source named beside it on 2026-10-26.

## WHATWG Encoding Standard and HTML Standard (generated tables, test data)

Used in: `src/OneHtml/Encodings/Generated/` (encoding labels and indexes, generated from `encodings.json` and
`indexes.json`), `src/OneHtml/Parsing/Generated/` (named character references, generated from `entities.json`), and
`testdata/whatwg/` (copies of `encodings.json` and `indexes.json`, used by the decoder tests), and
`src/OneHtml/Css/Generated/UserAgentStylesheet.g.cs` (the default style sheet, generated from the CSS blocks of the HTML
Standard's Rendering section, which `tools/generate-ua-stylesheet.py` reads; the file records the retrieval date and the
SHA-256 of the page).

Sources: <https://encoding.spec.whatwg.org/> and <https://html.spec.whatwg.org/>.

> Copyright © WHATWG (Apple, Google, Mozilla, Microsoft).
>
> This work is licensed under a Creative Commons Attribution 4.0 International
> License. To the extent portions of it are incorporated into source code,
> such portions in the source code are licensed under the BSD 3-Clause License instead.

Quoted from <https://github.com/whatwg/encoding/blob/main/LICENSE> and <https://github.com/whatwg/html/blob/main/LICENSE>
(the two files are identical; the second is the licence file of the HTML Standard repository, from which `entities.json`
and the Rendering section come). The CC BY 4.0 legal code is at <https://creativecommons.org/licenses/by/4.0/legalcode>.

The generated tables are portions of the standards incorporated into source code, so they are under the BSD
3-Clause License; the data files under `testdata/whatwg/` are under CC BY 4.0
(<https://creativecommons.org/licenses/by/4.0/>) and are unchanged copies.

Copyright © WHATWG (Apple, Google, Mozilla, Microsoft). The BSD 3-Clause License section of that file reads, unchanged:

```
BSD 3-Clause License

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## html5lib-tests (test data only)

Used in: `testdata/html5lib-tests/` (the `tokenizer/` and `encoding/` directories, unchanged), pinned at commit
`c777c408b61078ea2eb4acefc2535f54dbc8b28a` of <https://github.com/html5lib/html5lib-tests>. Not part of the
published packages. The licence text below is the `LICENSE` file at that commit
(<https://github.com/html5lib/html5lib-tests/blob/c777c408b61078ea2eb4acefc2535f54dbc8b28a/LICENSE>), unchanged.

```
Copyright (c) 2006-2013 James Graham, Geoffrey Sneddon, and
other contributors

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## css-parsing-tests

Implementation-independent CSS parsing tests by Simon Sapin, in `testdata/css/css-parsing-tests/`
(from https://github.com/SimonSapin/css-parsing-tests at commit `203ce36bffd617db7f118c551e32794561fb273d`).
The files are unmodified. They are dedicated to the public domain under CC0; the licence text is kept next to them
(`testdata/css/css-parsing-tests/LICENSE`) as a courtesy. It reads, from
<https://github.com/SimonSapin/css-parsing-tests/blob/203ce36bffd617db7f118c551e32794561fb273d/LICENSE>:

```
Written in 2013 by Simon Sapin.

To the extent possible under law, the author(s) have dedicated all copyright
and related and neighboring rights to this work to the public domain worldwide.
This work is distributed without any warranty.

See the CC0 Public Domain Dedication:
http://creativecommons.org/publicdomain/zero/1.0/
```

## web-platform-tests (test data only)

Source: <https://github.com/web-platform-tests/wpt>, commit `68d964a5f292bc4b2c2eba2f21e2b916190b6ad2`. The files are
unmodified and are not part of the published packages. Used in:

- `testdata/wpt/`: the tree construction test files from `html/syntax/parsing/resources/` (the former html5lib-tests
  tree construction suite, which is now maintained there).
- `testdata/wpt/selectors/`: selector tests, unmodified: `dom/nodes/` (`selectors.js`, its content page and
  `Element-closest.html`), `css/selectors/parsing/` (24 files) and `html/semantics/selectors/pseudo-classes/` (36 files).
  `testdata/wpt/selectors/MANIFEST.md` lists each file with its SHA-256.
- `testdata/css/wpt/css/css-syntax/`: CSS Syntax tests (the `charset/` directory is not included).
- `testdata/wpt/aria/`: the role pages of `html-aam/` (13 files) and `wai-aria/role/` (24 files), unmodified, with an
  expectations file we wrote. `testdata/wpt/aria/MANIFEST.md` lists each file with its SHA-256.
- `testdata/wpt/accname/`: the accessible name and property pages of `accname/` (including `manual/`), `html-aam/` (name
  computation by element, names, hidden names, headings), and `wai-aria/` (checked, pressed, heading, subtree),
  unmodified, with an expectations file we wrote. `testdata/wpt/accname/MANIFEST.md` lists each file with its SHA-256.
- `testdata/css/wpt-style/`: cascade tests, unmodified: `css/css-cascade/`, `css/css-variables/`, `css/css-nesting/`, the
  specificity files of `css/selectors/`, the computed-value files of `css/css-fonts/parsing/`, `css-display/parsing/`,
  `css-position/parsing/`, `css-overflow/parsing/`, `css-ui/parsing/`, and the shared `css/support/*.css`.
  `testdata/css/wpt-style/SHA256SUMS` lists each file.

Licence text, copied from <https://github.com/web-platform-tests/wpt/blob/68d964a5f292bc4b2c2eba2f21e2b916190b6ad2/LICENSE.md>
(also kept unchanged as `testdata/wpt/LICENSE.md`):

```
# The 3-Clause BSD License

Copyright © web-platform-tests contributors

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## web-platform-tests, css/css-color/parsing, css/css-values, css/css-variables, css/mediaqueries

Tests from https://github.com/web-platform-tests/wpt at commit `68d964a5f292bc4b2c2eba2f21e2b916190b6ad2`, in
`testdata/css/wpt/css/css-color/parsing/`, `testdata/css/wpt/css/css-values/`, `testdata/css/wpt/css/css-variables/` and
`testdata/css/wpt/css/mediaqueries/`. Only the files that call the parsing and evaluation helpers are included, and
the files are unmodified. They are under the BSD 3-Clause License given above for web-platform-tests.

## w3c/webref (property table source)

`src/OneHtml/Css/Generated/PropertyTableData.g.cs` is generated by `tools/generate-css-properties.py` from `ed/css.json`
of the `@webref/css@latest` branch of https://github.com/w3c/webref, commit
`bb927df3d6cc86f1c0beca204844a170a0843a18` (2026-10-22). The file is a machine extract of the property definitions of
the W3C and WHATWG CSS specifications. webref is under the MIT License (<https://github.com/w3c/webref/blob/main/LICENSE>, text unchanged; the file is not present at the pinned commit):

```
MIT License

Copyright (c) 2020 World Wide Web Consortium

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## W3C CSS specifications (property table, named colors, test material)

The property table, the named-color table (`src/OneHtml/Css/Generated/NamedColors.g.cs`, generated by
`tools/generate-css-colors.py` from CSS Color Level 4) and the behaviour described in `docs/css-values.md` are derived
from W3C CSS specifications (drafts.csswg.org). Those documents are under the W3C Software and Document License
(<https://www.w3.org/copyright/software-license-2023/>, "Software and Document license - 2023 version"). The text
below is that page's, copied on 2026-10-25, with headings and bullets as plain text. w3c/csswg-drafts states "All documents
in this Repository are licensed by contributors under the W3C Software and Document License"
(<https://github.com/w3c/csswg-drafts/blob/main/LICENSE.md>).

```
This work is being provided by the copyright holders under the following license.
License
By obtaining and/or copying this work, you (the licensee) agree that you have read, understood, and will comply with the following terms and conditions.
Permission to copy, modify, and distribute this work, with or without modification, for any purpose and without fee or royalty is hereby granted, provided that you include the following on ALL copies of the work or portions thereof, including modifications:
The full text of this NOTICE in a location viewable to users of the redistributed or derivative work.
Any pre-existing intellectual property disclaimers, notices, or terms and conditions. If none exist, the W3C software and document short notice should be included.
Notice of any changes or modifications, through a copyright statement on the new code or document such as "This software or document includes material copied from or derived from [title and URI of the W3C document]. Copyright © [$year-of-document] World Wide Web Consortium. https://www.w3.org/copyright/software-license-2023/"
Disclaimers
THIS WORK IS PROVIDED "AS IS," AND COPYRIGHT HOLDERS MAKE NO REPRESENTATIONS OR WARRANTIES, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO, WARRANTIES OF MERCHANTABILITY OR FITNESS FOR ANY PARTICULAR PURPOSE OR THAT THE USE OF THE SOFTWARE OR DOCUMENT WILL NOT INFRINGE ANY THIRD PARTY PATENTS, COPYRIGHTS, TRADEMARKS OR OTHER RIGHTS.
COPYRIGHT HOLDERS WILL NOT BE LIABLE FOR ANY DIRECT, INDIRECT, SPECIAL OR CONSEQUENTIAL DAMAGES ARISING OUT OF ANY USE OF THE SOFTWARE OR DOCUMENT.
The name and trademarks of copyright holders may NOT be used in advertising or publicity pertaining to the work without specific, written prior permission. Title to copyright in this work will at all times remain with copyright holders.
```

The generated tables include material derived from "CSS Color Module Level 4", "CSS Values and Units Module Level 4",
"CSS Backgrounds and Borders", "CSS Fonts" and the other CSS modules whose property definitions webref extracts.
Copyright © World Wide Web Consortium. https://www.w3.org/copyright/software-license-2023/

## WCAG success criteria data, ACT Rules list and test cases (reference data and test data)

Used in: `src/OneHtml.Accessibility.Validation/Catalogue/Data/wcag-criteria.json` and `act-rules.json` (generated
from the sources below by `tools/catalogue/`), and `testdata/act/` (the ACT test case index and the test case files,
copied unchanged and used only by the tests).

- Web Content Accessibility Guidelines (WCAG) 2.2, including the criteria that 2.0 and 2.1 define:
  <https://www.w3.org/WAI/WCAG22/wcag.json>. The data keeps each criterion's number, id, short name and level; the
  criteria texts are not copied.
- ACT Rules list: <https://github.com/w3c/wcag-act-rules> (`wcag-mapping.json`, commit
  `a7a18b1df1e60b1001fd27e443872ef04ba24c93`). The data keeps each rule's id, name, status, requirements mapping and
  input aspects.
- ACT Rules test cases: <https://act-rules.github.io/testcases.json> and the files it names, from the ACT-Rules
  Community site built from <https://github.com/act-rules/act-rules.github.io> (develop, commit
  `622e612066a7b17727c3755d041930f8e108f35b`).

This work includes material copied from or derived from the W3C documents above. Copyright © World Wide Web
Consortium. Each part is used under the licence its source states:

- **ACT test case files** (`testdata/act/`, unchanged) carry the licence page of the ACT Rules site,
  <https://github.com/act-rules/act-rules.github.io/blob/622e612066a7b17727c3755d041930f8e108f35b/pages/license.md>
  (published at <https://act-rules.github.io/pages/license>), reproduced in full. This repository does not alter
  the files, so the third bullet (notice of changes) has nothing to add:

```
W3C Software And Document Notice And License

This work is being provided by the copyright holders under the following license.

License

By obtaining and/or copying this work, you _(the licensee)_ agree that you have read, understood, and will comply with the following terms and conditions.

Permission to **copy**, **modify**, and **distribute** this work, with or without modification, for any purpose and without fee or royalty is hereby granted, provided that you include the following on **ALL** copies of the work or portions thereof, including modifications:

- The full text of this **NOTICE** in a location viewable to users of the redistributed or derivative work.
- Any pre-existing intellectual property disclaimers, notices, or terms and conditions. If none exist, the [W3C](https://www.w3.org/) Software and Document Short Notice should be included.
- Notice of any changes or modifications, through a copyright statement on the new code or document such as "This software or document includes material copied from or derived from [title and URI of the W3C document]. Copyright © [YEAR] W3C® (MIT, ERCIM, Keio, Beihang)."

Disclaimer

This work is provided "AS IS," and copyright holders make no representations or warranties, express or implied, including but not limited to, warranties of merchantability or fitness for any particular purpose or that the use of the software or document will not infringe any third party patents, copyrights, trademarks or other rights.

Copyright holders will not be liable for any direct, indirect, special or consequential damages arising out of any use of the software or document.

The name and trademarks of copyright holders may NOT be used in advertising or publicity pertaining to the work without specific, written prior permission. Title to copyright in this work will at all times remain with copyright holders.
```

- **ACT Rules list** (`w3c/wcag-act-rules`), **WCAG 2.2** (`wcag.json`) and **Graphics ARIA** are W3C documents under the
  W3C Document License. `w3c/wcag-act-rules` states "All documents in this Repository are licensed by contributors under
  the W3C Document License" (<https://github.com/w3c/wcag-act-rules/blob/a7a18b1df1e60b1001fd27e443872ef04ba24c93/LICENSE.md>);
  the WCAG 2.2 Recommendation (<https://www.w3.org/TR/WCAG22/>) and Graphics ARIA (<https://www.w3.org/TR/graphics-aria-1.0/>)
  link the same licence. The generated JSON keeps facts only (identifiers, numbers, short names, levels, statuses,
  mappings, role and attribute names), as implementation data in software, which the licence permits with its notice
  ("anyone may prepare and distribute derivative works and portions of this document in software, in supporting materials
  accompanying software, and in documentation of software, PROVIDED that all such works include the notice below").
  The notice is: "Copyright © 2023 W3C®. This software or document includes material copied from or derived from
  [title and URI of the W3C document]." The titles and URIs are the ones listed in this file. The licence text, from
  <https://www.w3.org/copyright/document-license-2023/> (copied 2026-10-25):

```
Public documents on the W3C site are provided by the copyright holders under the following license.
License
By using and/or copying this document, or the W3C document from which this statement is linked, you (the licensee) agree that you have read, understood, and will comply with the following terms and conditions:
Permission to copy, and distribute the contents of this document, or the W3C document from which this statement is linked, in any medium for any purpose and without fee or royalty is hereby granted, provided that you include the following on ALL copies of the document, or portions thereof, that you use:
A link or URL to the original W3C document.
The pre-existing copyright notice of the original author, or if it doesn't exist, a notice (hypertext is preferred, but a textual representation is permitted) of the form: "Copyright © [$date-of-document] World Wide Web Consortium. https://www.w3.org/copyright/document-license-2023/"
If it exists, the STATUS of the W3C document.
When space permits, inclusion of the full text of this NOTICE should be provided. We request that authorship attribution be provided in any software, documents, or other items or products that you create pursuant to the implementation of the contents of this document, or any portion thereof.
No right to create modifications or derivatives of W3C documents is granted pursuant to this license, except as follows: To facilitate implementation of the technical specifications set forth in this document, anyone may prepare and distribute derivative works and portions of this document in software, in supporting materials accompanying software, and in documentation of software, PROVIDED that all such works include the notice below. HOWEVER, the publication of derivative works of this document for use as a technical specification is expressly prohibited.
In addition, "Code Components" —Web IDL in sections clearly marked as Web IDL; and W3C-defined markup (HTML, CSS, etc.) and computer programming language code clearly marked as code examples— are licensed under the W3C Software License.
The notice is:
"Copyright © 2023 W3C®. This software or document includes material copied from or derived from [title and URI of the W3C document]."
Disclaimers
THIS DOCUMENT IS PROVIDED "AS IS," AND COPYRIGHT HOLDERS MAKE NO REPRESENTATIONS OR WARRANTIES, EXPRESS OR IMPLIED, INCLUDING, BUT NOT LIMITED TO, WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, NON-INFRINGEMENT, OR TITLE; THAT THE CONTENTS OF THE DOCUMENT ARE SUITABLE FOR ANY PURPOSE; NOR THAT THE IMPLEMENTATION OF SUCH CONTENTS WILL NOT INFRINGE ANY THIRD PARTY PATENTS, COPYRIGHTS, TRADEMARKS OR OTHER RIGHTS.
COPYRIGHT HOLDERS WILL NOT BE LIABLE FOR ANY DIRECT, INDIRECT, SPECIAL OR CONSEQUENTIAL DAMAGES ARISING OUT OF ANY USE OF THE DOCUMENT OR THE PERFORMANCE OR IMPLEMENTATION OF THE CONTENTS THEREOF.
The name and trademarks of copyright holders may NOT be used in advertising or publicity pertaining to this document or its contents without specific, written prior permission. Title to copyright in this document will at all times remain with copyright holders.
```

## axe-core rule identifiers (reference data)

Used in: `src/OneHtml.Accessibility.Validation/Catalogue/Data/axe-rules.json`, generated by `tools/catalogue/` from
axe-core's published rule table for release 4.14.0
(<https://github.com/dequelabs/axe-core/blob/v4.14.0/doc/rule-descriptions.md>). axe-core is licensed under the
Mozilla Public License 2.0 (<https://www.mozilla.org/MPL/2.0/>), Copyright (c) Deque Systems, Inc.

Only facts needed for interoperability are taken: rule identifiers, tags, impact levels, result kinds and the ACT rule
links. No description or help text and no code from axe-core is copied; the descriptions in this repository are our
own. axe-core itself is not part of this repository.

## WAI-ARIA, ARIA modules and HTML-AAM (role and attribute data)

Used in: `src/OneHtml.Accessibility/Aria/Data/*.json`, generated by `tools/aria/generate-aria.py` from these documents, whose
URLs, titles and SHA-256 hashes are in `tools/aria/pins.json`:

- Accessible Rich Internet Applications (WAI-ARIA) 1.2, W3C Recommendation, 6 June 2023,
  <https://www.w3.org/TR/2023/REC-wai-aria-1.2-20230606/>.
- Digital Publishing WAI-ARIA Module 1.1, W3C Recommendation, 12 June 2025, <https://www.w3.org/TR/dpub-aria-1.1/>.
- WAI-ARIA Graphics Module 1.0, W3C Recommendation, <https://www.w3.org/TR/graphics-aria-1.0/>.
- HTML Accessibility API Mappings 1.0, W3C Working Draft, 21 October 2026, <https://www.w3.org/TR/html-aam-1.0/>
  (the element and attribute mapping tables are kept as text extracts).
- Accessible Rich Internet Applications (WAI-ARIA) 1.3, W3C Editor's Draft, 21 October 2026, <https://w3c.github.io/aria/>
  (three roles and five properties only; listed in `docs/accessibility-roles.md`).
- ARIA in HTML, W3C Recommendation, <https://www.w3.org/TR/html-aria/>, for
  `src/OneHtml.Accessibility.Validation/Rules/Data/allowed-roles.json`, generated by
  `tools/validation/generate-allowed-roles.py` (the document's SHA-256 and retrieval date are in the file).

Each states "Copyright (c) World Wide Web Consortium. W3C liability, trademark and permissive document license rules
apply." The data (role names, characteristics, state and property names, value types and tokens, mapping table text) is
taken from them under the licence each states: the W3C Software and Document Notice and License (the section "W3C CSS
specifications" in this file gives the 2023 text; WAI-ARIA 1.2 links the 2015 version, whose terms are the same
conditions) for WAI-ARIA 1.2 and 1.3, the DPUB module, HTML-AAM and ARIA in HTML, and the W3C Document License for Graphics
ARIA (text in the section above). The descriptive prose of the specifications is not copied.

## IANA Language Subtag Registry (language data)

Used in: `src/OneHtml.Accessibility.Validation/Rules/Data/language-subtags.json`, generated by
`tools/validation/generate-language-subtags.py` from the registry at
<https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry> (File-Date 2026-09-17, SHA-256
`755fad43283be7b41ebe3c89ad054b6eaf928f404f9c0edb74799e0eab74beb1`). Only the primary language subtags are kept.

The IANA and IETF Trust joint statement on copyright in the protocol registries (10 November 2021, <https://www.iana.org/help/licensing-terms>)
dedicates the registries to the public domain under the Creative Commons CC0 1.0 dedication, so no notice is required;
the source is named here as a courtesy.

## SARIF 2.1.0 JSON schema (test data)

Used in: `tests/OneHtml.Accessibility.Validation.Tests`, which validates the SARIF export against
`testdata/sarif/sarif-schema-2.1.0.json`, the schema published with the OASIS Standard "Static Analysis Results
Interchange Format (SARIF) Version 2.1.0, Errata 01"
(<https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json>), copied unchanged. It is
not part of any package. The OASIS notice permits copying and distributing the document in whole or in part without
restriction, provided its copyright notice and notice section are included; `testdata/sarif/NOTICE.txt` carries them:

> Copyright © OASIS Open 2023. All Rights Reserved.
>
> All capitalized terms in the following text have the meanings assigned to them in the OASIS Intellectual Property Rights
> Policy (the "OASIS IPR Policy"). The full Policy may be found at the OASIS website: [https://www.oasis-open.org/policies-guidelines/ipr/].
>
> This document and translations of it may be copied and furnished to others, and derivative works that comment on or
> otherwise explain it or assist in its implementation may be prepared, copied, published, and distributed, in whole or
> in part, without restriction of any kind, provided that the above copyright notice and this section are included on
> all such copies and derivative works. However, this document itself may not be modified in any way, including by
> removing the copyright notice or references to OASIS, except as needed for the purpose of developing any document or
> deliverable produced by an OASIS Technical Committee (in which case the rules applicable to copyrights, as set forth
> in the OASIS IPR Policy, must be followed) or as required to translate it into languages other than English.
>
> The limited permissions granted above are perpetual and will not be revoked by OASIS or its successors or assigns.
>
> This document and the information contained herein is provided on an "AS IS" basis and OASIS DISCLAIMS ALL
> WARRANTIES, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTY THAT THE USE OF THE INFORMATION HEREIN WILL
> NOT INFRINGE ANY OWNERSHIP RIGHTS OR ANY IMPLIED WARRANTIES OF MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE.
> OASIS AND ITS MEMBERS WILL NOT BE LIABLE FOR ANY DIRECT, INDIRECT, SPECIAL OR CONSEQUENTIAL DAMAGES ARISING OUT OF
> ANY USE OF THIS DOCUMENT OR ANY PART THEREOF.

Full notice: <https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/sarif-v2.1.0-errata01-os.html> (Notices).
