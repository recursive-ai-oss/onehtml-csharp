#!/usr/bin/env python3
"""Generates the CSS property table of RecursiveAI.OneHtml from the w3c/webref CSS extract.

Input (fetched once; its SHA-256 is checked against the pin below and written into the generated file):
  css.json  `ed/css.json` of the `@webref/css@latest` branch of https://github.com/w3c/webref,
            pinned at commit PIN_COMMIT. The extract is machine-read from the W3C and WHATWG CSS specs' property
            definition tables (MIT licence for the data format; the text stays under the W3C Software and
            Document Licence).
  Fetch:    git clone --filter=blob:none --no-checkout https://github.com/w3c/webref.git webref
            git -C webref fetch origin '+refs/heads/@webref/css@latest:refs/remotes/origin/css-latest'
            git -C webref show PIN_COMMIT:ed/css.json > css.json

Usage: tools/generate-css-properties.py <path to css.json>

Output (relative to the repository root):
  src/OneHtml/Css/Generated/PropertyTableData.g.cs

Deterministic: running it again on the same input changes nothing.
"""
import hashlib
import json
import re
import sys
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PIN_COMMIT = "bb927df3d6cc86f1c0beca204844a170a0843a18"
PIN_DATE = "2026-10-22"
PIN_SHA256 = "30cb8daa786d6c27a1f74c5d413311c785ee0eec1dd34c65e8a4e6681c86baca"

INITIAL_VALUE, INITIAL_SEE_LONGHANDS, INITIAL_USER_AGENT, INITIAL_NOT_DEFINED, INITIAL_MISSING = range(5)
INH_NO, INH_YES, INH_SEE_LONGHANDS, INH_UNSPECIFIED = range(4)
LOGICAL_WORD = re.compile(r"(^|-)(block|inline|start|end)(-|$)")


def cs(s):
    if s is None:
        return "null"
    out = []
    for ch in s:
        o = ord(ch)
        if ch == '"':
            out.append('\\"')
        elif ch == "\\":
            out.append("\\\\")
        elif o < 0x20 or o > 0x7E:
            out.append("\\u%04X" % o if o < 0x10000 else "".join("\\u%04X" % u for u in
                       (0xD800 + ((o - 0x10000) >> 10), 0xDC00 + ((o - 0x10000) & 0x3FF))))
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


def classify_initial(p):
    v = p.get("initial")
    if v is None:
        return INITIAL_MISSING, None
    low = v.strip().lower()
    if low == "see individual properties":
        return INITIAL_SEE_LONGHANDS, None
    if low == "depends on user agent":
        return INITIAL_USER_AGENT, None
    if low == "not defined for shorthand properties":
        return INITIAL_NOT_DEFINED, None
    return INITIAL_VALUE, v.strip()


def classify_inherited(p):
    v = (p.get("inherited") or "").strip().lower()
    if v.startswith("yes"):
        return INH_YES
    if v.startswith("no"):
        return INH_NO
    if v.startswith("see individual"):
        return INH_SEE_LONGHANDS
    return INH_UNSPECIFIED


def main():
    path = sys.argv[1]
    with open(path, "rb") as f:
        raw = f.read()
    digest = hashlib.sha256(raw).hexdigest()
    if digest != PIN_SHA256:
        sys.exit("css.json does not match the pinned SHA-256 (%s)" % digest)
    props = json.loads(raw)["properties"]
    props.sort(key=lambda p: p["name"])
    names = [p["name"] for p in props]
    assert len(set(names)) == len(names)

    rows = []
    stats = {"shorthands": 0, "prose": []}
    for p in props:
        ik, iv = classify_initial(p)
        inh = classify_inherited(p)
        longhands = p.get("longhands") or []
        reset = p.get("resetLonghands") or []
        group = p.get("logicalPropertyGroup")
        logical = bool(group) and bool(LOGICAL_WORD.search(p["name"]))
        flags = (1 if longhands else 0) | (2 if logical else 0)
        if longhands:
            stats["shorthands"] += 1
        if ik != INITIAL_VALUE:
            stats["prose"].append(p["name"])
        rows.append(
            "        P(%s, %s, %d, %d, %s, %s, %s, %s, %s, %s, %s, %s, %d)," % (
                cs(p["name"]), cs(iv), ik, inh,
                cs(p.get("appliesTo")), cs(p.get("percentages")), cs(p.get("computedValue")), cs(p.get("syntax")),
                cs(p.get("legacyAliasOf")),
                cs(" ".join(longhands)) if longhands else "null",
                cs(" ".join(reset)) if reset else "null",
                cs(group), flags))

    chunk = 80
    parts = []
    for i in range(0, len(rows), chunk):
        parts.append(
            "    private static void Fill%d(List<PropertyInfo> list)\n    {\n"
            "        static PropertyInfo P(string name, string? initial, int initialKind, int inherited, string? appliesTo,\n"
            "            string? percentages, string? computedValue, string? syntax, string? aliasOf, string? longhands,\n"
            "            string? resetLonghands, string? logicalGroup, int flags) =>\n"
            "            new(name, initial, (PropertyInitialKind)initialKind, (PropertyInheritance)inherited, appliesTo, percentages,\n"
            "                computedValue, syntax, aliasOf, longhands, resetLonghands, logicalGroup, (flags & 1) != 0, (flags & 2) != 0);\n\n"
            "        PropertyInfo[] rows =\n        [\n%s\n        ];\n        list.AddRange(rows);\n    }\n"
            % (i // chunk, "\n".join(rows[i:i + chunk])))
    calls = "\n".join("        Fill%d(list);" % k for k in range(len(parts)))

    text = """// <auto-generated>
// Generated by tools/generate-css-properties.py. Do not edit.
// Source: ed/css.json of https://github.com/w3c/webref (branch @webref/css@latest)
//   commit %(commit)s, committed %(date)s, SHA-256 %(sha)s.
// The data is extracted from the W3C and WHATWG CSS specifications' property definitions; see
// THIRD-PARTY-NOTICES.md for the licences.
// </auto-generated>
#nullable enable
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

internal static class PropertyTableData
{
    public const string SourceCommit = "%(commit)s";
    public const string SourceDate = "%(date)s";
    public const string SourceSha256 = "%(sha)s";

    public static PropertyInfo[] Create()
    {
        var list = new List<PropertyInfo>(%(count)d);
%(calls)s
        return list.ToArray();
    }

%(parts)s}
""" % {"commit": PIN_COMMIT, "date": PIN_DATE, "sha": PIN_SHA256, "count": len(rows), "calls": calls,
       "parts": "\n".join(parts)}

    out = os.path.join(ROOT, "src/OneHtml/Css/Generated/PropertyTableData.g.cs")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print("properties %d, shorthands %d, prose-initial %d" % (len(rows), stats["shorthands"], len(stats["prose"])))


main()
