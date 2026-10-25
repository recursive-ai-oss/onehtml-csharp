#!/usr/bin/env python3
"""Builds manifest.jsonl and summary.{json,txt} for a corpus version directory.

Reads the append-only logs the crawler and sampler wrote, so it can be run at
any time, including on a partly finished version.
"""
import collections
import json
import os
import sys

import common


def run(out):
    store = common.Store(out)
    links = collections.defaultdict(set)
    for l in store.read_log("links.jsonl"):
        links[l["url"]].add(l["from"])
    sites = {}
    for s in store.read_log("sites.jsonl"):
        sites[s["seed"]] = s

    entries = []
    for r in store.read_log("fetches.crawl.jsonl"):
        e = {k: r.get(k) for k in ("url", "kind", "fetched_at", "status", "error", "content_type",
                                   "body_sha256", "body_bytes", "robots", "refused", "location", "headers")}
        e["source"] = {"kind": "crawl", "site": r.get("site")}
        if r["kind"] in ("stylesheet", "page"):
            e["linked_from"] = sorted(links.get(r["url"], ()))
        entries.append(e)
    for r in store.read_log("fetches.cc.jsonl"):
        e = {k: r.get(k) for k in ("url", "kind", "fetched_at", "status", "error", "content_type",
                                   "body_sha256", "body_bytes", "headers")}
        e["robots"] = "n/a (Common Crawl record)"
        e["source"] = r["source"]
        entries.append(e)
    # Threads write the logs in arbitrary order; sort so the manifest is stable.
    entries.sort(key=lambda e: (e["source"]["kind"], str(e["source"].get("site") or e["source"].get("warc")),
                                str(e["source"].get("offset", "")), e["url"], e["fetched_at"] or ""))
    dedup, seen = [], set()
    for e in entries:
        k = (e["source"]["kind"], e["url"], e["fetched_at"])
        if k not in seen:
            seen.add(k)
            dedup.append(e)
    with open(os.path.join(store.root, "manifest.jsonl"), "w", encoding="utf-8") as f:
        for e in dedup:
            f.write(json.dumps(e, sort_keys=True, ensure_ascii=False) + "\n")

    def cls(e):
        if e.get("refused"):
            return "refused"
        if e["status"] is None:
            return "error"
        return "%dxx" % (e["status"] // 100)

    summary = {
        "records": len(dedup),
        "by_source": dict(collections.Counter(e["source"]["kind"] for e in dedup)),
        "by_kind": dict(collections.Counter(e["kind"] for e in dedup)),
        "by_status": dict(collections.Counter(cls(e) for e in dedup)),
        "stored_bodies": sum(1 for e in dedup if e.get("body_sha256")),
        "stored_bytes": sum(e.get("body_bytes") or 0 for e in dedup),
        "sites": dict(collections.Counter(s["status"] for s in sites.values())),
        "refusals": [{"seed": s["seed"], "status": s["status"], "reason": s["reason"]}
                     for s in sorted(sites.values(), key=lambda s: s["seed"]) if s["status"] != "done"],
        "refused_urls_by_robots": sum(1 for e in dedup if e.get("refused")),
        "commoncrawl_crawls": sorted({e["source"].get("crawl") for e in dedup if e["source"]["kind"] == "commoncrawl"}),
    }
    with open(os.path.join(store.root, "summary.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2, sort_keys=True)
        f.write("\n")
    lines = ["records: %d (stored bodies %d, %d bytes)" % (summary["records"], summary["stored_bodies"], summary["stored_bytes"]),
            "by source: %s" % summary["by_source"], "by kind: %s" % summary["by_kind"],
            "by status: %s" % summary["by_status"], "sites: %s" % summary["sites"],
            "urls refused by robots.txt: %d" % summary["refused_urls_by_robots"]]
    for r in summary["refusals"]:
        lines.append("  site %s: %s %s" % (r["status"], r["seed"], r["reason"]))
    with open(os.path.join(store.root, "summary.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines[:6]))
    return summary


if __name__ == "__main__":
    run(sys.argv[1])
