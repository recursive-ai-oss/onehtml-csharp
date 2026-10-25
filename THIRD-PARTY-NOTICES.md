# Third-party notices

The libraries have no runtime dependencies beyond the .NET base class library. The material below is either
compiled into the libraries as generated tables or used only by the tests.

## WHATWG Encoding Standard and HTML Standard (generated tables, test data)

Used in: `src/OneHtml/Encodings/Generated/` (encoding labels and indexes, generated from `encodings.json` and
`indexes.json`), `src/OneHtml/Parsing/Generated/` (named character references, generated from `entities.json`), and
`testdata/whatwg/` (copies of `encodings.json` and `indexes.json`, used by the decoder tests).

Sources: <https://encoding.spec.whatwg.org/> and <https://html.spec.whatwg.org/>.

> Copyright (c) WHATWG (Apple, Google, Mozilla, Microsoft). This work is licensed under a Creative Commons
> Attribution 4.0 International License. To the extent portions of it are incorporated into source code, such
> portions in the source code are licensed under the BSD 3-Clause License instead.

The generated tables are portions of the standards incorporated into source code, so they are under the BSD
3-Clause License; the data files under `testdata/whatwg/` are under CC BY 4.0
(<https://creativecommons.org/licenses/by/4.0/>) and are unchanged copies.

```
BSD 3-Clause License

Copyright (c) WHATWG (Apple, Google, Mozilla, Microsoft)

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
published packages.

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
as a courtesy.

## web-platform-tests, css/css-syntax

Tests from https://github.com/web-platform-tests/wpt at commit `68d964a5f292bc4b2c2eba2f21e2b916190b6ad2`, in
`testdata/css/wpt/css/css-syntax/` (the `charset/` directory is not included). The files are unmodified.

The 3-Clause BSD License

Copyright (c) web-platform-tests contributors

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the
following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following
   disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the
   following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote
   products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
