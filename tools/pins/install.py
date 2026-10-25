#!/usr/bin/env python3
"""Installs the pinned Chrome for Testing and axe-core under ~/onehtml/pins/.

    install.py            install (or verify) what pins.json names
    install.py --record   also write the SHA-256 of what was downloaded into pins.json
                          (do this once when a pin is first chosen or bumped)
    install.py --latest   print the current stable Chrome for Testing version and the
                          latest axe-core release, to help choose new pins

Downloads are checked against the SHA-256 in pins.json, and axe-core's tarball
also against the npm registry's SHA-512 integrity value. axe-core is only ever
unpacked to one opaque file, `axe.min.js`, plus its licence; nothing in it is read.
"""
import base64
import hashlib
import io
import json
import os
import stat
import sys
import tarfile
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
PINS = os.path.join(HERE, "pins.json")
ROOT = os.path.expanduser(os.environ.get("ONEHTML_PINS_DIR", "~/onehtml/pins"))
UA = "OneHtmlPins/1.0 (+https://github.com/recursive-ai-oss/onehtml-csharp)"


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=300) as r:
        return r.read()


def extract_zip(data, dest):
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for info in z.infolist():
            target = os.path.realpath(os.path.join(dest, info.filename))
            if not target.startswith(os.path.realpath(dest) + os.sep):
                raise SystemExit("unsafe path in zip: " + info.filename)
            z.extract(info, dest)
            mode = info.external_attr >> 16
            if mode and not info.is_dir():
                os.chmod(target, stat.S_IMODE(mode))


def main():
    if "--latest" in sys.argv:
        j = json.loads(get("https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions.json"))
        print("chrome-for-testing stable:", j["channels"]["Stable"]["version"])
        print("axe-core latest:", json.loads(get("https://registry.npmjs.org/axe-core/latest"))["version"])
        return
    record = "--record" in sys.argv
    pins = json.load(open(PINS))
    cft, axe = pins["chrome_for_testing"], pins["axe_core"]
    changed = False

    for name, d in cft["downloads"].items():
        dest = os.path.join(ROOT, "chrome-for-testing-" + cft["version"])
        marker = os.path.join(dest, "." + name + ".ok")
        if os.path.exists(marker) and d["sha256"]:
            print("have", name, cft["version"])
            continue
        data = get(d["url"])
        digest = hashlib.sha256(data).hexdigest()
        if d["sha256"] and digest != d["sha256"]:
            raise SystemExit("%s: sha256 mismatch: got %s, pinned %s" % (name, digest, d["sha256"]))
        if not d["sha256"]:
            if not record:
                raise SystemExit("%s: no sha256 pinned (got %s); rerun with --record" % (name, digest))
            d["sha256"], changed = digest, True
        os.makedirs(dest, exist_ok=True)
        extract_zip(data, dest)
        open(marker, "w").write(digest + "\n")
        print("installed %s %s sha256=%s" % (name, cft["version"], digest))

    adest = os.path.join(ROOT, "axe-core-" + axe["version"])
    if not (os.path.exists(os.path.join(adest, "axe.min.js")) and axe["sha256"]):
        data = get(axe["tarball"])
        algo, _, b64 = axe["npm_integrity"].partition("-")
        if base64.b64encode(hashlib.new(algo, data).digest()).decode() != b64:
            raise SystemExit("axe-core: npm integrity mismatch")
        if hashlib.sha1(data).hexdigest() != axe["npm_shasum"]:
            raise SystemExit("axe-core: npm shasum mismatch")
        digest = hashlib.sha256(data).hexdigest()
        if axe["sha256"] and digest != axe["sha256"]:
            raise SystemExit("axe-core: sha256 mismatch")
        if not axe["sha256"]:
            if not record:
                raise SystemExit("axe-core: no sha256 pinned (got %s); rerun with --record" % digest)
            axe["sha256"], changed = digest, True
        os.makedirs(adest, exist_ok=True)
        with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as t:
            for want in ("package/axe.min.js", "package/LICENSE"):
                member = t.extractfile(want)
                with open(os.path.join(adest, os.path.basename(want)), "wb") as f:
                    f.write(member.read())
        print("installed axe-core %s sha256=%s" % (axe["version"], digest))
    else:
        print("have axe-core", axe["version"])

    if changed:
        with open(PINS, "w") as f:
            json.dump(pins, f, indent=2)
            f.write("\n")
        print("recorded checksums in pins.json")

    chrome = os.path.join(ROOT, "chrome-for-testing-" + cft["version"], "chrome-linux64", "chrome")
    shell = os.path.join(ROOT, "chrome-for-testing-" + cft["version"], "chrome-headless-shell-linux64", "chrome-headless-shell")
    print("chrome:", chrome)
    print("chrome-headless-shell:", shell)
    print("axe.min.js:", os.path.join(adest, "axe.min.js"))
    print("axe sha256 of axe.min.js:", hashlib.sha256(open(os.path.join(adest, "axe.min.js"), "rb").read()).hexdigest())


if __name__ == "__main__":
    main()
