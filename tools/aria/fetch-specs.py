#!/usr/bin/env python3
"""Downloads the specification documents named in pins.json into a cache directory and checks their SHA-256.

The W3C /TR/ URLs of Recommendations are stable; the Working Draft and Editor's Draft change over time, so a hash
mismatch on those means a newer draft: review the diff in the generated data, then update pins.json.

usage: fetch-specs.py CACHE_DIR
"""
import hashlib, json, os, sys, urllib.request

here = os.path.dirname(os.path.abspath(__file__))
cache = sys.argv[1]
os.makedirs(cache, exist_ok=True)
pins = json.load(open(os.path.join(here, "pins.json")))
bad = 0
for name, spec in pins["specs"].items():
    path = os.path.join(cache, spec["file"])
    if not os.path.exists(path):
        req = urllib.request.Request(spec["url"], headers={"User-Agent": "onehtml-spec-fetch"})
        with urllib.request.urlopen(req, timeout=120) as r, open(path, "wb") as f:
            f.write(r.read())
    digest = hashlib.sha256(open(path, "rb").read()).hexdigest()
    ok = digest == spec["sha256"]
    bad += not ok
    print(("ok       " if ok else "MISMATCH ") + name + " " + digest)
sys.exit(1 if bad else 0)
