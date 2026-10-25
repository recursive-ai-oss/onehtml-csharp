#!/usr/bin/env python3
"""Generates the catalogue data files from the pinned sources (see sources.py).

Usage: tools/catalogue/generate.py CACHE_DIR
Writes src/OneHtml.Accessibility.Validation/Catalogue/Data/{wcag-criteria,act-rules,axe-rules}.json.
Running it twice gives identical bytes. Only facts are taken from the axe-core table (identifiers, tags, impact,
issue types, linked ACT rules); its descriptions are not read into the output.
"""
import hashlib
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "src", "OneHtml.Accessibility.Validation", "Catalogue", "Data")
AXE_VERSION = "4.14.0"


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def num_key(num):
    return tuple(int(p) for p in num.split("."))


def write(name, obj):
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, indent=1, ensure_ascii=False)
        f.write("\n")


def wcag(cache):
    path = os.path.join(cache, "wcag.json")
    data = json.load(open(path, encoding="utf-8"))
    criteria = []
    for principle in data["principles"]:
        for guideline in principle["guidelines"]:
            for sc in guideline["successcriteria"]:
                versions = sc["versions"]
                level = sc["level"]
                handle = sc["handle"]
                removed = None
                if sc["num"] == "4.1.1":
                    # The published data leaves the level empty and appends a note to the handle for the criterion
                    # that WCAG 2.2 removed. The 2.0 and 2.1 Recommendations define it as Level A.
                    level = "A"
                    handle = "Parsing"
                    removed = "2.2"
                criteria.append({
                    "num": sc["num"],
                    "id": sc["id"],
                    "handle": handle,
                    "level": level,
                    "introduced": versions[0],
                    "removedIn": removed,
                })
    criteria.sort(key=lambda c: num_key(c["num"]))
    # Sanity: the counts the Recommendations state.
    counts = {v: sum(1 for c in criteria if c["introduced"] <= v and (c["removedIn"] is None or c["removedIn"] > v))
              for v in ("2.0", "2.1", "2.2")}
    assert counts == {"2.0": 61, "2.1": 78, "2.2": 86}, counts
    write("wcag-criteria.json", {
        "version": 1,
        "source": {
            "url": "https://www.w3.org/WAI/WCAG22/wcag.json",
            "sha256": sha(path),
            "retrieved": "2026-10-25",
            "notes": "Level and handle of 4.1.1 Parsing set from the WCAG 2.0 and 2.1 Recommendations.",
        },
        "criteria": criteria,
    })


ASPECTS = {"dom tree": "DOM Tree", "css styling": "CSS Styling", "accessibility tree": "Accessibility Tree",
           "language": "Language", "audio output": "Audio output", "visual output": "Visual output",
           "source code": "Source code"}


def act(cache):
    path = os.path.join(cache, "act-mapping.json")
    rules = []
    for entry in json.load(open(path, encoding="utf-8"))["act-rules"]:
        f = entry["frontmatter"]
        status = "deprecated" if entry["deprecated"] else ("proposed" if entry["proposed"] else "approved")
        criteria, techniques, other = [], [], []
        for key in (f.get("accessibility_requirements") or {}):
            kind, _, value = key.partition(":")
            if re.fullmatch(r"wcag2[012]", kind):
                if value not in criteria:
                    criteria.append(value)
            elif kind == "wcag-technique":
                techniques.append(value)
            else:
                other.append(key)
        aspects = [ASPECTS[a.lower()] for a in (f.get("input_aspects") or [])]
        rules.append({
            "id": f["id"],
            "name": f["name"],
            "status": status,
            "type": f["rule_type"],
            "criteria": sorted(criteria, key=num_key),
            "techniques": sorted(techniques),
            "otherRequirements": sorted(other),
            "inputAspects": aspects,
            "inputRules": [str(r) for r in f.get("input_rules") or []],
        })
    rules.sort(key=lambda r: r["id"])
    write("act-rules.json", {
        "version": 1,
        "source": {
            "url": "https://github.com/w3c/wcag-act-rules/blob/a7a18b1df1e60b1001fd27e443872ef04ba24c93/wcag-mapping.json",
            "sha256": sha(path),
            "retrieved": "2026-10-25",
            "notes": "Status is deprecated when the rule is flagged deprecated, proposed when flagged proposed, "
                     "otherwise approved. Composite rules list their input rules and no aspects of their own.",
        },
        "rules": rules,
    })


def axe(cache):
    path = os.path.join(cache, "axe-rule-descriptions.md")
    group = None
    groups = {"WCAG 2.0 Level A & AA Rules": "wcag20", "WCAG 2.1 Level A & AA Rules": "wcag21",
              "WCAG 2.2 Level A & AA Rules": "wcag22", "Best Practices Rules": "best-practice",
              "WCAG 2.x level AAA rules": "wcag-aaa", "Experimental Rules": "experimental",
              "Deprecated Rules": "deprecated"}
    rules = {}
    for line in open(path, encoding="utf-8").read().splitlines():
        if line.startswith("## "):
            group = groups.get(line[3:].strip())
            continue
        if group is None or not line.startswith("| ["):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        m = re.match(r"\[([a-z0-9-]+)\]\(", cells[0])
        rule_id = m.group(1)
        impact = cells[2].lower()
        tags = [t.strip() for t in cells[3].split(",") if t.strip()]
        issues = [t.replace("&nbsp;", " ").strip() for t in cells[4].split(",") if t.strip()]
        acts = re.findall(r"act-rules\.github\.io/rules/([0-9a-z]{6})", cells[5]) if len(cells) > 5 else []
        assert rule_id not in rules, rule_id
        rules[rule_id] = {"id": rule_id, "group": group, "impact": impact, "tags": tags,
                          "issueTypes": issues, "actRules": acts}
    out = sorted(rules.values(), key=lambda r: r["id"])
    write("axe-rules.json", {
        "version": 1,
        "axeVersion": AXE_VERSION,
        "source": {
            "url": "https://github.com/dequelabs/axe-core/blob/v4.14.0/doc/rule-descriptions.md",
            "sha256": sha(path),
            "retrieved": "2026-10-25",
            "notes": "Identifiers, tags, impact, issue types and linked ACT rules only. No description text.",
        },
        "rules": out,
    })


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    wcag(sys.argv[1])
    act(sys.argv[1])
    axe(sys.argv[1])
