#!/usr/bin/env python3
"""Black-box smoke test for the pins: Chrome returns an AX tree and axe-core runs in a page.

    smoke.py [path/to/chrome]

Loads a small local HTML file in headless Chrome, talks the DevTools protocol over
pipes (no sockets), calls Accessibility.getFullAXTree, then injects axe.min.js as an
opaque script and prints a short summary of the results JSON it returns.
"""
import fcntl
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PINS = json.load(open(os.path.join(HERE, "pins.json")))
ROOT = os.path.expanduser(os.environ.get("ONEHTML_PINS_DIR", "~/onehtml/pins"))
CFT = PINS["chrome_for_testing"]["version"]
AXE = os.path.join(ROOT, "axe-core-" + PINS["axe_core"]["version"], "axe.min.js")

PAGE = b"""<!doctype html><html><head><title>Smoke</title></head><body>
<main><h1>Smoke page</h1><img src="x.png"><button></button>
<a href="/a">link</a><label>Name <input type="text"></label></main></body></html>"""


class Cdp:
    def __init__(self, chrome, profile):
        to_chrome = os.pipe()
        from_chrome = os.pipe()
        # Keep our ends clear of fds 3 and 4, which Chrome expects for the protocol.
        hi = [fcntl.fcntl(fd, fcntl.F_DUPFD, 100) for fd in (to_chrome[0], to_chrome[1], from_chrome[0], from_chrome[1])]
        for fd in to_chrome + from_chrome:
            os.close(fd)
        self.c_in, self.w, self.r, self.c_out = hi

        def child():
            os.dup2(self.c_in, 3)
            os.dup2(self.c_out, 4)

        self.proc = subprocess.Popen(
            [chrome, "--headless", "--remote-debugging-pipe", "--user-data-dir=" + profile,
             "--no-first-run", "--disable-gpu", "--no-sandbox", "about:blank"],
            preexec_fn=child, close_fds=False,  # only the dup2ed fds 3 and 4 are inheritable
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        os.close(self.c_in)
        os.close(self.c_out)
        self.buf, self.next_id, self.events = b"", 0, []

    def send(self, method, params=None, session=None):
        self.next_id += 1
        msg = {"id": self.next_id, "method": method, "params": params or {}}
        if session:
            msg["sessionId"] = session
        os.write(self.w, json.dumps(msg).encode() + b"\0")
        while True:
            m = self.read()
            if m.get("id") == self.next_id:
                if "error" in m:
                    raise RuntimeError("%s: %s" % (method, m["error"]))
                return m["result"]
            self.events.append(m)

    def read(self):
        while b"\0" not in self.buf:
            chunk = os.read(self.r, 1 << 20)
            if not chunk:
                raise RuntimeError("Chrome closed the pipe")
            self.buf += chunk
        raw, self.buf = self.buf.split(b"\0", 1)
        return json.loads(raw)

    def close(self):
        self.proc.kill()
        self.proc.wait()


def main():
    chrome = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        ROOT, "chrome-for-testing-" + CFT, "chrome-linux64", "chrome")
    tmp = os.path.expanduser("~/onehtml/u1")
    os.makedirs(tmp, exist_ok=True)
    work = tempfile.mkdtemp(prefix="smoke-", dir=tmp)
    page = os.path.join(work, "page.html")
    open(page, "wb").write(PAGE)
    cdp = Cdp(chrome, os.path.join(work, "profile"))
    try:
        print("browser:", cdp.send("Browser.getVersion")["product"])
        tid = cdp.send("Target.createTarget", {"url": "file://" + page})["targetId"]
        sid = cdp.send("Target.attachToTarget", {"targetId": tid, "flatten": True})["sessionId"]
        cdp.send("Page.enable", session=sid)
        for _ in range(100):
            st = cdp.send("Runtime.evaluate", {"expression": "document.readyState"}, sid)["result"].get("value")
            if st == "complete":
                break
        tree = cdp.send("Accessibility.getFullAXTree", session=sid)["nodes"]
        print("AX nodes:", len(tree))
        for n in tree:
            if n.get("ignored"):
                continue
            role = n.get("role", {}).get("value")
            name = n.get("name", {}).get("value", "")
            print("  %-12s %r" % (role, name))
        src = open(AXE, encoding="utf-8").read()
        cdp.send("Runtime.evaluate", {"expression": src}, sid)  # defines window.axe; never inspected
        res = cdp.send("Runtime.evaluate", {
            "expression": "axe.run().then(r => JSON.stringify(r))",
            "awaitPromise": True, "returnByValue": True}, sid)
        out = json.loads(res["result"]["value"])
        print("axe version:", out.get("testEngine", {}).get("version"))
        print("axe counts: violations=%d passes=%d incomplete=%d inapplicable=%d" % tuple(
            len(out[k]) for k in ("violations", "passes", "incomplete", "inapplicable")))
        for v in out["violations"]:
            print("  violation %-18s impact=%-9s nodes=%d" % (v["id"], v.get("impact"), len(v["nodes"])))
    finally:
        cdp.close()


if __name__ == "__main__":
    main()
