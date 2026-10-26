"""Offline checks for capture.py against a scripted stand-in for Chrome. No browser is needed.

Run:  python3 -m unittest test_capture     (from this directory)

The stand-in speaks the same pipe protocol (fd 3 in, fd 4 out) and models just enough of Chrome: request
interception, a script-disabled switch, canned accessibility, snapshot and axe answers. It proves our side:
what is served and what is blocked, how the script-disabled mode is chosen, the files written, resuming,
limits, crash restart and timeouts. Whether real Chrome honours each command is shown by the worker smoke run.
"""
import contextlib
import gzip
import hashlib
import io
import json
import os
import subprocess
import sys
import time
import tempfile
import unittest

import capture

FAKE = r'''#!{python}
import base64, json, os, re, sys, urllib.parse

IN, OUT = 3, 4
if os.environ.get("FAKE_PIDFILE"):
    with open(os.environ["FAKE_PIDFILE"], "a") as f:
        f.write("%d\n" % os.getpid())
if os.environ.get("FAKE_SLEEP"):
    import time
    time.sleep(1000)
LOG = os.environ["FAKE_LOG"]
state = dict(disabled=False, url="about:blank", awaiting={{}}, doc=None, loaded=True, scripts_ran=False, rid=0)
buf = b""

def emit(obj):
    os.write(OUT, json.dumps(obj).encode() + b"\0")

def log(**kw):
    with open(LOG, "a") as f:
        f.write(json.dumps(kw) + "\n")

def paused(url, rtype):
    state["rid"] += 1
    rid = "r%d" % state["rid"]
    state["awaiting"][rid] = (url, rtype)
    state["loaded"] = False
    emit({{"method": "Fetch.requestPaused", "sessionId": "S1", "params": {{
        "requestId": rid, "request": {{"url": url, "method": "GET"}}, "frameId": "T1", "resourceType": rtype}}}})

def resolved(rid, action, params):
    url, rtype = state["awaiting"].pop(rid)
    entry = dict(url=url, type=rtype, action=action)
    if action == "fulfill":
        body = base64.b64decode(params.get("body", ""))
        entry.update(code=params["responseCode"], headers={{h["name"].lower(): h["value"] for h in params["responseHeaders"]}},
                     body=body.decode("latin-1"))
        if rtype == "Document" and state["doc"] is None or url == state.get("nav"):
            html = body.decode("latin-1")
            state["doc"] = html
            csp = entry["headers"].get("content-security-policy", "")
            state["scripts_ran"] = ("<script>" in html) and not state["disabled"] and "script-src 'none'" not in csp
            for m in re.finditer(r'(?:href|src)="([^"]+)"', html):
                ref = urllib.parse.urljoin(url, m.group(1))
                kind = "Stylesheet" if m.group(0).startswith("href") else ("Script" if ".js" in ref else "Image")
                paused(ref, kind)
    else:
        entry["reason"] = params.get("errorReason")
    log(**entry)
    if not state["awaiting"]:
        state["loaded"] = True

def handle(msg):
    m, p, mid = msg["method"], msg.get("params", {{}}), msg["id"]
    res = {{}}
    if m == "Browser.getVersion":
        res = dict(product="FakeChrome/1.0", protocolVersion="1.3", revision="r0", userAgent="Fake")
    elif m == "Target.createBrowserContext":
        res = dict(browserContextId="C1")
    elif m == "Target.createTarget":
        res = dict(targetId="T1")
    elif m == "Target.attachToTarget":
        res = dict(sessionId="S1")
    elif m == "Emulation.setScriptExecutionDisabled":
        log(method=m, value=bool(p["value"]))
        if not os.environ.get("FAKE_IGNORE_DISABLE"):
            state["disabled"] = bool(p["value"])
    elif m == "Page.getFrameTree":
        res = dict(frameTree=dict(frame=dict(id="T1", url=state["url"]), childFrames=[dict(frame=dict(id="F2", url="http://x.test/frame"))]))
    elif m == "Page.createIsolatedWorld":
        res = dict(executionContextId=7)
    elif m == "Page.navigate":
        url = p["url"]
        if "/crash" in url and not os.path.exists(os.environ["FAKE_CRASH_MARKER"]):
            open(os.environ["FAKE_CRASH_MARKER"], "w").close()
            os._exit(1)
        state.update(url=url, nav=url, doc=None, scripts_ran=False)
        emit({{"id": mid, "result": {{"frameId": "T1"}}}})
        if not url.endswith("/hang"):
            paused(url, "Document")
        else:
            state["loaded"] = False
        return
    elif m in ("Fetch.fulfillRequest", "Fetch.failRequest"):
        resolved(p["requestId"], "fulfill" if m.endswith("fulfillRequest") else "fail", p)
    elif m == "Runtime.evaluate":
        e = p["expression"]
        if "document.readyState" in e:
            v = state["url"] + "\t" + ("complete" if state["loaded"] else "loading")
        elif e == "document.title":
            v = "ran" if state["scripts_ran"] else "t"
        elif "querySelector('#n')" in e:
            v = state["disabled"]
        elif e == "(6*7)":
            v = 42
        elif e == "location.href":
            v = state["url"]
        elif e == "document.documentElement.outerHTML":
            v = state["doc"] or ""
        elif "serializeToString" in e:
            v = "<!DOCTYPE html>"
        elif e.startswith("/*fake axe*/"):
            v = None
        elif e == "axe.version":
            v = "0.0.test"
        elif "axe.run" in e and (state["disabled"] or os.environ.get("FAKE_AXE_HANG")):
            return   # like real Chrome: axe awaits timers that do not run while scripting is off; never answers
        elif "axe.run" in e:
            v = json.dumps(dict(violations=[dict(id="image-alt", nodes=[dict(target=["img"]), dict(target=["img:nth-child(2)"])])],
                                passes=[dict(id="document-title", nodes=[dict(target=["html"])])], incomplete=[], inapplicable=[dict(id="area-alt", nodes=[])]))
        else:
            emit({{"id": mid, "error": {{"message": "unexpected expression " + e[:40]}}}})
            return
        res = dict(result=dict(type="string", value=v))
        if p.get("contextId") is None and os.environ.get("FAKE_REQUIRE_CONTEXT"):
            emit({{"id": mid, "error": {{"message": "no context"}}}})
            return
    elif m == "Accessibility.getFullAXTree":
        res = dict(nodes=[dict(nodeId="1", ignored=False, role=dict(type="role", value="RootWebArea")),
                          dict(nodeId="2", ignored=False, role=dict(type="role", value="heading"))])
    elif m == "DOMSnapshot.captureSnapshot":
        log(snapshot_params=p)
        res = dict(documents=[dict(nodes=dict(nodeType=[9, 1, 1]), layout=dict(nodeIndex=[1, 2]))], strings=["a"])
    elif m not in ("Page.enable", "Fetch.enable", "Emulation.setDeviceMetricsOverride", "Target.closeTarget",
                   "Target.disposeBrowserContext"):
        emit({{"id": mid, "error": {{"message": "unknown method " + m}}}})
        return
    out = {{"id": mid, "result": res}}
    if msg.get("sessionId"):
        out["sessionId"] = msg["sessionId"]
    emit(out)

while True:
    chunk = os.read(IN, 1 << 20)
    if not chunk:
        break
    buf += chunk
    while b"\0" in buf:
        raw, buf = buf.split(b"\0", 1)
        handle(json.loads(raw))
'''

SITE = "http://site.test"
PAGE_A = (b'<!doctype html><html><head><title>A</title><link rel="stylesheet" href="/s.css">'
          b'<link rel="stylesheet" href="/missing.css"><script src="/x.js"></script></head>'
          b'<body><script>document.title="ran"</script><img src="/i.png"><h1>Hi</h1></body></html>')
PAGE_B = b'<!doctype html><html><head><link rel="stylesheet" href="/r.css"></head><body>b</body></html>'
PAGE_C = b"<!doctype html><title>c</title><p>crash me"
PAGE_D = b"<!doctype html><title>d</title><p>hang"
CSS = b"h1{color:red}"


class CaptureTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="capture-test-")
        t = self.tmp.name
        self.corpus, self.out = os.path.join(t, "corpus"), os.path.join(t, "out")
        self.chrome = os.path.join(t, "fake-chrome")
        with open(self.chrome, "w") as f:
            f.write(FAKE.format(python=sys.executable))
        os.chmod(self.chrome, 0o755)
        self.axe = os.path.join(t, "axe.min.js")
        with open(self.axe, "w") as f:
            f.write("/*fake axe*/window.axe={};")
        os.environ["FAKE_LOG"] = os.path.join(t, "fake.log")
        os.environ["FAKE_CRASH_MARKER"] = os.path.join(t, "crashed")
        for k in ("FAKE_IGNORE_DISABLE", "FAKE_REQUIRE_CONTEXT", "FAKE_AXE_HANG", "FAKE_SLEEP", "FAKE_PIDFILE"):
            os.environ.pop(k, None)
        self.shas = {}
        recs = []

        def put(url, kind, body, status=200, ctype="text/html", extra=None, location=None):
            sha = hashlib.sha256(body).hexdigest() if body is not None else None
            if body is not None:
                d = os.path.join(self.corpus, "bodies", sha[:2])
                os.makedirs(d, exist_ok=True)
                with open(os.path.join(d, sha), "wb") as f:
                    f.write(body)
            hdr = [["Content-Type", ctype], ["Set-Cookie", "a=b"]] + (extra or [])
            recs.append({"url": url, "kind": kind, "status": status, "content_type": ctype, "headers": hdr,
                         "body_sha256": sha, "body_bytes": len(body or b""), "location": location})
            return sha

        self.shas["a"] = put(SITE + "/", "page", PAGE_A)
        put(SITE + "/s.css", "stylesheet", gzip.compress(CSS), ctype="text/css", extra=[["Content-Encoding", "gzip"]])
        put(SITE + "/x.js", "stylesheet", b"alert(1)", ctype="text/javascript")
        put(SITE + "/r.css", "stylesheet", None, status=302, location="/s.css", extra=[["Location", "/s.css"]])
        self.shas["b"] = put(SITE + "/b", "page", PAGE_B)
        self.shas["c"] = put(SITE + "/crash", "page", PAGE_C)
        self.shas["d"] = put(SITE + "/hang", "page", PAGE_D)
        put(SITE + "/dup", "page", PAGE_A)                       # same bytes as /: captured once
        put(SITE + "/doc.pdf", "page", b"%PDF", ctype="application/pdf")
        put(SITE + "/gone", "page", b"<html>", status=404)
        put("http://other.test/robots.txt", "robots", None, status=None)
        with open(os.path.join(self.corpus, "manifest.jsonl"), "w") as f:
            for r in recs:
                f.write(json.dumps(r) + "\n")

    def tearDown(self):
        self.tmp.cleanup()

    def run_capture(self, *extra):
        argv = ["run", "--corpus", self.corpus, "--out", self.out, "--chrome", self.chrome, "--axe", self.axe,
                "--settle", "0", "--page-timeout", "3", "--jobs", "1", *extra]
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            capture.main(argv)
        return buf.getvalue()

    def fake_log(self):
        p = os.environ["FAKE_LOG"]
        if not os.path.exists(p):
            return []
        with open(p) as f:
            return [json.loads(x) for x in f]

    def manifest(self):
        with open(os.path.join(self.out, "manifest.jsonl")) as f:
            return [json.loads(x) for x in f]

    def env(self):
        with open(os.path.join(self.out, "environment.json")) as f:
            return json.load(f)

    def load(self, sha, name, vp="1280x800"):
        with gzip.open(os.path.join(self.out, "captures", sha[:2], sha, vp, name)) as f:
            return json.load(f)

    def test_offline_serving_and_outputs(self):
        out = self.run_capture("--limit", "2")
        self.assertIn("REMAINING=0", out)
        log = self.fake_log()
        by = {e["url"]: e for e in log if "url" in e}
        css = by[SITE + "/s.css"]
        self.assertEqual((css["action"], css["code"], css["body"]), ("fulfill", 200, CSS.decode()))
        self.assertEqual(css["headers"].get("content-type"), "text/css")
        self.assertNotIn("content-encoding", css["headers"])      # stored gzip was decoded
        self.assertNotIn("set-cookie", css["headers"])
        self.assertEqual(by[SITE + "/x.js"]["action"], "fail")     # scripts are never served, even when stored
        self.assertEqual(by[SITE + "/x.js"]["reason"], "BlockedByClient")
        self.assertEqual(by[SITE + "/i.png"]["action"], "fail")
        miss = by[SITE + "/missing.css"]
        self.assertEqual((miss["action"], miss["code"], miss["body"]), ("fulfill", 200, ""))   # empty answer
        redirect = by[SITE + "/r.css"]
        self.assertEqual(redirect["code"], 302)
        self.assertEqual(redirect["headers"]["location"], "/s.css")

        sha = self.shas["a"]
        with open(os.path.join(self.out, "captures", sha[:2], sha, "1280x800", "meta.json")) as f:
            meta = json.load(f)
        self.assertEqual(meta["schema"], "onehtml-capture/1/meta")
        self.assertTrue(meta["ok"])
        self.assertEqual(meta["script_mode"], "emulation-main")
        self.assertEqual(meta["requests"]["blocked"], 2)
        self.assertEqual(meta["axe"]["violations"], 2)
        self.assertEqual(meta["axe"]["rules"]["violations"], {"image-alt": 2})
        self.assertEqual(meta["axe"]["rules"]["inapplicable"], {"area-alt": 0})
        self.assertEqual(meta["ax"], {"frames": 2, "nodes": 4, "nodes_main": 2})
        self.assertEqual(meta["snapshot"]["nodes"], 3)
        for k in ("ax", "snapshot", "dom", "axe"):
            self.assertEqual(len(meta["files"][k]["sha256"]), 64)

        ax = self.load(sha, "ax.json.gz")
        self.assertEqual(ax["schema"], "onehtml-capture/1/ax")
        self.assertEqual(ax["frames"][0]["frameId"], "T1")
        self.assertEqual(ax["frames"][1]["parentFrameId"], "T1")
        snap = self.load(sha, "snapshot.json.gz")
        self.assertEqual(snap["format"]["method"], "captureSnapshot")
        self.assertEqual(snap["format"]["protocolVersion"], "1.3")
        self.assertTrue(snap["format"]["parameters"]["includePaintOrder"])
        self.assertTrue(snap["format"]["parameters"]["includeDOMRects"])
        self.assertIn("background-color", snap["format"]["parameters"]["computedStyles"])
        self.assertIn("documents", snap["snapshot"])
        dom = self.load(sha, "dom.json.gz")
        self.assertEqual(dom["doctype"], "<!DOCTYPE html>")
        self.assertIn("<h1>Hi</h1>", dom["outerHTML"])
        axe = self.load(sha, "axe.json.gz")
        self.assertEqual(axe["axe_version"], "0.0.test")
        self.assertEqual(axe["results"]["violations"][0]["id"], "image-alt")

        env = self.env()
        self.assertEqual(env["chrome"]["product"], "FakeChrome/1.0")
        self.assertEqual(env["viewports"], ["1280x800"])
        self.assertIn("--proxy-server=http://127.0.0.1:9", env["flags"])
        self.assertEqual(env["script_probe"]["evaluate_works"], True)
        pages = [r for r in self.manifest() if r["type"] == "page"]
        self.assertEqual({r["body_sha256"] for r in pages}, {self.shas["a"], self.shas["b"]})
        self.assertTrue(all(r["status"] == "ok" for r in pages))

    def test_resume_limit_and_remaining(self):
        out = self.run_capture("--max-seconds", "0")
        self.assertIn("REMAINING=4", out)       # a, b, c, d; the duplicate, PDF and 404 are not pages
        self.assertEqual([r for r in self.manifest() if r["type"] == "page"], [])
        out = self.run_capture("--limit", "1")
        self.assertIn("REMAINING=0", out)
        n = len([r for r in self.manifest() if r["type"] == "page"])
        out = self.run_capture("--limit", "1")
        self.assertEqual(len([r for r in self.manifest() if r["type"] == "page"]), n)   # nothing redone
        out = self.run_capture()
        self.assertIn("REMAINING=0", out)

    def test_crash_restart_and_timeout(self):
        self.run_capture()
        pages = {r["body_sha256"]: r for r in self.manifest() if r["type"] == "page"}
        self.assertEqual(pages[self.shas["c"]]["status"], "ok")        # Chrome died once, was restarted, page retried
        self.assertTrue(os.path.exists(os.environ["FAKE_CRASH_MARKER"]))
        self.assertEqual(pages[self.shas["d"]]["status"], "timeout")   # never finished loading
        self.assertEqual(pages[self.shas["a"]]["status"], "ok")        # and the run carried on
        # failed pages are not retried by default, only on request
        n = len(self.manifest())
        out = self.run_capture()
        self.assertIn("REMAINING=0", out)
        self.assertEqual(len(self.manifest()), n + 2)                  # run + run-end only
        self.run_capture("--retry-failed")
        self.assertGreater(len(self.manifest()), n + 2)

    def test_script_mode_fallback_when_emulation_is_ignored(self):
        os.environ["FAKE_IGNORE_DISABLE"] = "1"
        self.run_capture("--limit", "1")
        env = self.env()
        self.assertEqual(env["script_mode"], "csp-main")
        self.assertEqual([r["mode"] for r in env["script_modes_rejected"]], ["emulation-main", "emulation-isolated"])
        doc = [e for e in self.fake_log() if e.get("url") == SITE + "/" and e["action"] == "fulfill"][-1]
        self.assertEqual(doc["headers"]["content-security-policy"], "script-src 'none'")

    def test_isolated_world_mode(self):
        os.environ["FAKE_REQUIRE_CONTEXT"] = "1"
        self.run_capture("--limit", "1", "--script-mode", "emulation-isolated")
        env = self.env()
        self.assertEqual(env["script_mode"], "emulation-isolated")

    def test_two_viewports_and_summary(self):
        self.run_capture("--limit", "1", "--viewport", "1280x800", "--viewport", "390x844")
        sha = self.shas["a"]
        for vp in ("1280x800", "390x844"):
            self.assertTrue(os.path.exists(os.path.join(self.out, "captures", sha[:2], sha, vp, "meta.json")))
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            capture.main(["summary", self.out])
        text = buf.getvalue()
        self.assertIn("pages: 1  ok=1", text)
        self.assertIn("image-alt", text)

    def test_environment_mismatch_is_refused(self):
        self.run_capture("--limit", "1")
        with self.assertRaises(SystemExit):
            self.run_capture("--limit", "1", "--viewport", "800x600")

    def test_axe_runs_with_scripting_switched_back_on(self):
        self.run_capture("--limit", "1")
        sets = [e for e in self.fake_log() if "setScriptExecutionDisabled" in e.get("method", "")]
        self.assertEqual([e["value"] for e in sets][-2:], [True, False])

    def test_axe_hang_is_a_failed_step_not_a_lost_page(self):
        os.environ["FAKE_AXE_HANG"] = "1"
        self.run_capture("--limit", "2", "--axe-timeout", "1")
        pages = [r for r in self.manifest() if r["type"] == "page"]
        self.assertEqual([r["status"] for r in pages], ["failed", "failed"])
        self.assertIn("axe: CdpTimeout", pages[0]["error"])
        self.assertEqual(len(pages[0]["viewports"]), 1)               # the other outputs are still there
        sha = pages[0]["body_sha256"]
        base = os.path.join(self.out, "captures", sha[:2], sha, "1280x800")
        self.assertTrue(os.path.exists(os.path.join(base, "ax.json.gz")))
        self.assertTrue(os.path.exists(os.path.join(base, "errors.json")))
        self.assertFalse(os.path.exists(os.path.join(base, "meta.json")))

    def test_debug_log_explains_the_page(self):
        os.environ["FAKE_AXE_HANG"] = "1"
        self.run_capture("--limit", "1", "--axe-timeout", "1")
        sha = self.shas["a"]
        with open(os.path.join(self.out, "debug", sha + ".log")) as f:
            text = f.read()
        for want in ("page http://site.test/", "-> Page.navigate", "request Document http://site.test/",
                     "served 200", "not served: blocked", "step snapshot done", "step axe failed",
                     "last command: Runtime.evaluate"):
            self.assertIn(want, text)

    def test_chrome_dies_with_its_parent(self):
        pidfile = os.path.join(self.tmp.name, "pids")
        env = dict(os.environ, FAKE_SLEEP="1", FAKE_PIDFILE=pidfile, PYTHONPATH=os.path.dirname(capture.__file__))
        code = ("import capture, time, os\n"
                "c = capture.Cdp(%r, %r, [], %r)\n"
                "print('up', flush=True); time.sleep(1000)\n"
                % (self.chrome, os.path.join(self.tmp.name, "prof"), os.path.join(self.tmp.name, "err")))
        p = subprocess.Popen([sys.executable, "-c", code], env=env, stdout=subprocess.PIPE, text=True)
        try:
            self.assertEqual(p.stdout.readline().strip(), "up")
            deadline = time.time() + 10
            while not os.path.exists(pidfile) and time.time() < deadline:
                time.sleep(0.05)
            with open(pidfile) as f:
                chrome_pid = int(f.read().split()[0])
            os.kill(chrome_pid, 0)                      # alive while the parent lives
            p.kill()                                    # SIGKILL: no handler can run
            p.wait()
            deadline = time.time() + 10
            gone = False
            while time.time() < deadline and not gone:
                try:
                    os.kill(chrome_pid, 0)
                    with open("/proc/%d/stat" % chrome_pid) as f:
                        gone = f.read().split(")")[-1].split()[0] == "Z"   # a zombie nobody waits for is dead
                except (ProcessLookupError, FileNotFoundError):
                    gone = True
                time.sleep(0.05)
            self.assertTrue(gone, "Chrome outlived its parent")
        finally:
            p.kill()
            p.wait()
            p.stdout.close()

    def test_sigterm_kills_chromes(self):
        pidfile = os.path.join(self.tmp.name, "pids")
        env = dict(os.environ, FAKE_SLEEP="1", FAKE_PIDFILE=pidfile, PYTHONPATH=os.path.dirname(capture.__file__))
        code = ("import capture, time\ncapture.install_exit_handlers()\n"
                "c = capture.Cdp(%r, %r, [], %r)\nprint('up', flush=True); time.sleep(1000)\n"
                % (self.chrome, os.path.join(self.tmp.name, "prof"), os.path.join(self.tmp.name, "err")))
        p = subprocess.Popen([sys.executable, "-c", code], env=env, stdout=subprocess.PIPE, text=True)
        try:
            self.assertEqual(p.stdout.readline().strip(), "up")
            deadline = time.time() + 10
            while not os.path.exists(pidfile) and time.time() < deadline:
                time.sleep(0.05)
            with open(pidfile) as f:
                chrome_pid = int(f.read().split()[0])
            p.terminate()
            p.wait(10)
            time.sleep(0.3)
            try:
                os.kill(chrome_pid, 0)
                with open("/proc/%d/stat" % chrome_pid) as f:
                    self.assertEqual(f.read().split(")")[-1].split()[0], "Z")
            except (ProcessLookupError, FileNotFoundError):
                pass
        finally:
            p.kill()
            p.wait()
            p.stdout.close()

    def test_norm_url(self):
        self.assertEqual(capture.norm_url("HTTP://Site.Test:80#x"), "http://site.test/")
        self.assertEqual(capture.norm_url("https://a.test:8443/p?q=1#f"), "https://a.test:8443/p?q=1")


if __name__ == "__main__":
    unittest.main()
