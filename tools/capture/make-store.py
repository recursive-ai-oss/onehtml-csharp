#!/usr/bin/env python3
"""Builds a small corpus store (manifest.jsonl + bodies/ab/<sha256>) for tools/capture from loose HTML files, or a
sample of an existing store. Usage:
  make-store.py files <out-dir> <file-or-dir>...            # each page is served at https://pages.test/<name>
  make-store.py sample <corpus-dir> <out-dir> <count> [seed]  # every stride-th page, plus all stylesheets
"""
import hashlib
import json
import os
import random
import sys


def put(out, data):
    sha = hashlib.sha256(data).hexdigest()
    d = os.path.join(out, "bodies", sha[:2])
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, sha), "wb") as f:
        f.write(data)
    return sha


def files(out, paths):
    os.makedirs(out, exist_ok=True)
    names = []
    for p in paths:
        if os.path.isdir(p):
            names += [os.path.join(p, n) for n in sorted(os.listdir(p)) if n.endswith(".html")]
        else:
            names.append(p)
    with open(os.path.join(out, "manifest.jsonl"), "w", encoding="utf-8") as m:
        for p in names:
            data = open(p, "rb").read()
            sha = put(out, data)
            rec = {"kind": "page", "status": 200, "url": "https://" + os.environ.get("STORE_HOST", "pages.test") + "/" + os.path.basename(p),
                   "content_type": "text/html; charset=utf-8", "headers": [["content-type", "text/html; charset=utf-8"]],
                   "body_sha256": sha, "body_bytes": len(data), "error": None}
            m.write(json.dumps(rec) + "\n")


def sample(src, out, count, seed):
    os.makedirs(out, exist_ok=True)
    recs = [json.loads(l) for l in open(os.path.join(src, "manifest.jsonl"), encoding="utf-8")]
    pages = [r for r in recs if r.get("kind") == "page" and r.get("status") == 200 and r.get("body_sha256")
             and "text/html" in (r.get("content_type") or "") and r.get("body_bytes", 0) < 400000]
    random.Random(seed).shuffle(pages)
    chosen, seen = [], set()
    for r in pages:
        if r["body_sha256"] not in seen:
            seen.add(r["body_sha256"])
            chosen.append(r)
        if len(chosen) == count:
            break
    keep = chosen + [r for r in recs if r.get("kind") == "stylesheet" and r.get("body_sha256")]
    with open(os.path.join(out, "manifest.jsonl"), "w", encoding="utf-8") as m:
        for r in keep:
            sha = r["body_sha256"]
            dst = os.path.join(out, "bodies", sha[:2], sha)
            if not os.path.exists(dst):
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                os.symlink(os.path.abspath(os.path.join(src, "bodies", sha[:2], sha)), dst)
            m.write(json.dumps(r) + "\n")
    print(len(chosen), "pages,", len(keep) - len(chosen), "stylesheets")


if __name__ == "__main__":
    a = sys.argv[1:]
    if a and a[0] == "files":
        files(a[1], a[2:])
    elif a and a[0] == "sample":
        sample(a[1], a[2], int(a[3]), int(a[4]) if len(a) > 4 else 1)
    else:
        sys.exit(__doc__)
