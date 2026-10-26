#!/usr/bin/env python3
"""Generates src/OneHtml.Accessibility.Validation/Rules/Data/language-subtags.json from the IANA Language Subtag Registry.

Input: the registry file (https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry), downloaded
by the caller. The output keeps the primary language subtags (records of Type: language), as sorted lower-case values plus
the ranges the registry lists (the private-use block qaa..qtz), the registry's File-Date and the input's SHA-256.
The registry is dedicated to the public domain (CC0 1.0) by IANA and the IETF Trust.

Usage: generate-language-subtags.py <language-subtag-registry> > language-subtags.json
"""
import hashlib
import json
import sys


def main():
    raw = open(sys.argv[1], "rb").read()
    text = raw.decode("utf-8")
    file_date = text.split("\n", 1)[0].split(":", 1)[1].strip()
    languages, ranges = [], []
    for record in text.split("\n%%\n")[1:]:
        fields = {}
        for line in record.replace("\n  ", " ").split("\n"):
            if ":" in line:
                k, v = line.split(":", 1)
                fields.setdefault(k.strip(), v.strip())
        if fields.get("Type") != "language":
            continue
        sub = fields["Subtag"].lower()
        if ".." in sub:
            lo, hi = sub.split("..")
            ranges.append([lo, hi])
        else:
            languages.append(sub)
    doc = {
        "version": 1,
        "source": {
            "url": "https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry",
            "fileDate": file_date,
            "sha256": hashlib.sha256(raw).hexdigest(),
            "licence": "CC0 1.0 (IANA and IETF Trust joint statement on the protocol registries, 2021-11-10)",
        },
        "ranges": ranges,
        "languages": sorted(set(languages)),
    }
    json.dump(doc, sys.stdout, indent=1)
    sys.stdout.write("\n")


main()
