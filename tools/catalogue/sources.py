#!/usr/bin/env python3
"""Fetches the pinned sources of the rule catalogue data into a cache directory and checks their hashes.

Usage: tools/catalogue/sources.py CACHE_DIR [--act-testcases]

Sources (all public specification material):
  wcag.json           the W3C's published machine-readable WCAG 2.2 (includes the 2.0 and 2.1 criteria)
  act-mapping.json    the W3C ACT rules list with status flags, from the w3c/wcag-act-rules repository
  act-testcases.json  the ACT Rules community test case index
  axe-rule-descriptions.md  axe-core's published rule table for one release (identifiers, tags, impact)

With --act-testcases the test case files named by the index are downloaded into testdata/act/testcases/.
The catalogue generators (generate.py) read only the cache directory, so they run offline.
"""
import hashlib
import json
import os
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Pins. The WCAG file is published at a stable URL without a commit, so it is pinned by hash and retrieval date.
SOURCES = {
    "wcag.json": (
        "https://www.w3.org/WAI/WCAG22/wcag.json",
        "3a034865a879a7d874b60ff2aec7f430cf37bc1597a1e00985680fd80ef5cf75",
    ),
    "act-mapping.json": (
        "https://raw.githubusercontent.com/w3c/wcag-act-rules/a7a18b1df1e60b1001fd27e443872ef04ba24c93/wcag-mapping.json",
        "5d111710f92de504b978bf77fc5ac488e9230dca36a619c7ad9ef058ff07c4f5",
    ),
    "act-testcases.json": (
        "https://act-rules.github.io/testcases.json",
        "a805d865d61ae2418e56a6a9d303fe60c85089c792b897eb9472ea5513156293",
    ),
    "axe-rule-descriptions.md": (
        "https://raw.githubusercontent.com/dequelabs/axe-core/v4.14.0/doc/rule-descriptions.md",
        "635ad70ee085f78dd8f72ba9d0d89b87f16406353c60b7f114caae7083ecf261",
    ),
}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def get(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "onehtml-catalogue-fetch (data tooling)"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def fetch_sources(cache: str) -> None:
    os.makedirs(cache, exist_ok=True)
    for name, (url, pin) in SOURCES.items():
        path = os.path.join(cache, name)
        data = open(path, "rb").read() if os.path.exists(path) else get(url)
        if pin is not None and sha256(data) != pin:
            sys.exit(f"{name}: hash {sha256(data)} does not match the pin {pin}")
        open(path, "wb").write(data)
        print(f"{name}  {sha256(data)}")


def fetch_act_testcases(cache: str) -> None:
    index = json.load(open(os.path.join(cache, "act-testcases.json"), encoding="utf-8"))
    out = os.path.join(ROOT, "testdata", "act")
    os.makedirs(out, exist_ok=True)
    open(os.path.join(out, "testcases.json"), "wb").write(open(os.path.join(cache, "act-testcases.json"), "rb").read())

    def one(case):
        path = os.path.join(out, case["relativePath"])
        if os.path.exists(path):
            return
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, "wb").write(get(case["url"]))

    with ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(one, index["testcases"]))
    print(f"{len(index['testcases'])} test case files in testdata/act/")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    fetch_sources(sys.argv[1])
    if "--act-testcases" in sys.argv:
        fetch_act_testcases(sys.argv[1])
