#!/usr/bin/env python3
"""Generates src/OneHtml.Accessibility.Validation/Rules/Data/allowed-roles.json from ARIA in HTML.

Input: the W3C "ARIA in HTML" Recommendation page (https://www.w3.org/TR/html-aria/), downloaded by the caller;
its SHA-256 is recorded in the output. For every row of the "Document conformance requirements for use of ARIA
attributes in HTML" table the output keeps the row id, whether any role is allowed, and the role names the row
lists (including the ones it allows but does not recommend). Rows whose rule depends on context (a div inside a dl,
a td in a grid, a header outside sectioning content) are resolved in code; the row text is kept for them.

Usage: generate-allowed-roles.py <html-aria.html> <aria-roles.json-dir> > allowed-roles.json
"""
import hashlib
import html
import json
import re
import sys


def text(fragment):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", "", fragment))).strip()


def main():
    page_path, roles_dir = sys.argv[1], sys.argv[2]
    raw = open(page_path, "rb").read()
    page = raw.decode("utf-8")
    role_names = set()
    import glob
    for path in glob.glob(roles_dir + "/*.json"):
        data = json.load(open(path))
        if isinstance(data, dict) and "roles" in data:
            for r in data["roles"]:
                role_names.add(r["name"])
    role_names.add("image")
    rows = re.findall(r'<tr>\s*<th id="(el-[^"]+)"[^>]*>(.*?)</th>\s*<td>(.*?)</td>\s*<td>(.*?)</td>\s*</tr>', page, re.S)
    out = {}
    for row_id, th, _implicit, allowed in rows:
        body = text(allowed)
        roles = []
        for p in re.findall(r"<p>(.*?)</p>", allowed, re.S):
            t = text(p)
            if t.startswith("Global") or "aria-" in t.split(".")[0] and not re.match(r"(DPub )?Roles?:|No role other", t):
                continue
            for code in re.findall(r"<code>([a-z-]+)</code>", p):
                if code in role_names and code not in roles:
                    roles.append(code)
        # Rows written as running text rather than paragraphs.
        if not roles:
            for code in re.findall(r"<code>([a-z-]+)</code>", allowed.split("Global")[0]):
                if code in role_names and code not in roles:
                    roles.append(code)
        entry = {
            "label": text(th),
            "any": bool(re.match(r"Any role", body)),
            "roles": sorted(roles),
            "text": body,
        }
        out[row_id] = entry
    doc = {
        "version": 1,
        "source": {
            "url": "https://www.w3.org/TR/html-aria/",
            "sha256": hashlib.sha256(raw).hexdigest(),
            "retrieved": "2026-10-25",
        },
        "rows": dict(sorted(out.items())),
    }
    json.dump(doc, sys.stdout, indent=1, ensure_ascii=False)
    sys.stdout.write("\n")


main()
