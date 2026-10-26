#!/usr/bin/env python3
"""Reference capture: load corpus pages in pinned headless Chrome and record what Chrome says about them.

    capture.py run     --corpus DIR --out DIR [--jobs N] [--limit N] [--max-seconds S] [...]
    capture.py summary OUT
    capture.py probe   [--chrome PATH]

Python 3.13, standard library only. See README.md for the output format and the offline-serving and
script-disabled rules. The DevTools protocol is spoken over pipes (--remote-debugging-pipe); no port is opened.
"""
import argparse
import base64
import collections
import datetime
import fcntl
import gzip
import hashlib
import json
import os
import queue
import re
import shutil
import signal
import statistics
import subprocess
import sys
import tempfile
import threading
import time
import urllib.parse
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
SCHEMA = "onehtml-capture/1"
PINS_JSON = os.path.join(HERE, "..", "pins", "pins.json")
PINS_ROOT = os.path.expanduser(os.environ.get("ONEHTML_PINS_DIR", "~/onehtml/pins"))

DEFAULT_VIEWPORTS = ["1280x800"]
PROBE_URL = "http://probe.onehtml.invalid/probe.html"
PROBE_HTML = (b"<!doctype html><title>t</title><noscript><p id=n>x</p></noscript>"
              b"<script>document.title='ran'</script><p>hi</p>")
MODES = ["emulation-main", "emulation-isolated", "csp-main"]
MAX_FRAMES = 50

# Computed styles recorded in every DOMSnapshot: what the accessibility tree and the checks read, plus what
# decides visibility, clipping, stacking, text appearance and contrast.
COMPUTED_STYLES = [
    "display", "visibility", "content", "content-visibility", "opacity", "position", "top", "right", "bottom",
    "left", "z-index", "float", "clear", "overflow", "overflow-x", "overflow-y", "clip", "clip-path",
    "pointer-events", "transform", "filter", "mix-blend-mode", "isolation", "contain", "width", "height",
    "min-width", "min-height", "max-width", "max-height", "margin-top", "margin-right", "margin-bottom",
    "margin-left", "padding-top", "padding-right", "padding-bottom", "padding-left",
    "color", "background-color", "background-image", "background-position", "background-size",
    "background-repeat", "background-clip", "background-attachment", "-webkit-text-fill-color",
    "-webkit-text-stroke-color", "-webkit-text-stroke-width", "text-shadow", "box-shadow",
    "border-top-color", "border-top-style", "border-top-width", "border-right-color", "border-right-style",
    "border-right-width", "border-bottom-color", "border-bottom-style", "border-bottom-width",
    "border-left-color", "border-left-style", "border-left-width",
    "outline-color", "outline-style", "outline-width", "outline-offset",
    "font-family", "font-size", "font-style", "font-weight", "font-variant", "line-height", "letter-spacing",
    "word-spacing", "text-align", "text-indent", "text-transform", "text-decoration-line",
    "text-decoration-color", "text-decoration-style", "text-overflow", "white-space", "word-break",
    "overflow-wrap", "vertical-align", "direction", "unicode-bidi", "writing-mode", "cursor", "user-select",
    "appearance", "forced-color-adjust", "color-scheme", "list-style-type", "table-layout", "visibility",
]

# Request types that are served from the store. Everything else is blocked.
SERVED_TYPES = {"Document", "Stylesheet"}
DROP_HEADERS = {"content-encoding", "content-length", "transfer-encoding", "connection", "keep-alive",
                "set-cookie", "set-cookie2", "strict-transport-security", "content-security-policy-report-only",
                "refresh", "public-key-pins", "alt-svc"}

PROBE_MATH = "(6*7)"
EXPR_STATE = "location.href + '\\t' + document.readyState"
EXPR_TITLE = "document.title"
EXPR_NOSCRIPT = "!!document.querySelector('#n')"
EXPR_DOM = "document.documentElement.outerHTML"
EXPR_DOCTYPE = ("document.doctype ? new XMLSerializer().serializeToString(document.doctype) : null")
EXPR_AXE_VERSION = "axe.version"
EXPR_AXE_RUN = """(async () => {
  const rules = {};
  for (const r of axe.getRules()) rules[r.ruleId] = { enabled: true };
  const res = await axe.run(document, {
    rules: rules,
    resultTypes: ['violations', 'passes', 'incomplete', 'inapplicable'],
    xpath: true
  });
  return JSON.stringify(res);
})()"""
AXE_OPTIONS_NOTE = {"rules": "every rule from axe.getRules() enabled (best-practice and experimental included)",
                    "resultTypes": ["violations", "passes", "incomplete", "inapplicable"], "xpath": True}

CHROME_FLAGS = [
    "--headless", "--remote-debugging-pipe", "--no-first-run", "--no-default-browser-check", "--disable-gpu",
    "--disable-background-networking", "--disable-component-update", "--disable-sync", "--disable-extensions",
    "--disable-default-apps", "--disable-breakpad", "--disable-client-side-phishing-detection",
    "--metrics-recording-only", "--mute-audio", "--force-color-profile=srgb", "--force-device-scale-factor=1",
    "--lang=en-US", "--hide-scrollbars",
    # One process per page group so every frame is in the session we intercept.
    "--disable-features=IsolateOrigins,site-per-process,Translate,OptimizationHints,MediaRouter",
    "--disable-site-isolation-trials",
    # Belt and braces: anything that escapes request interception goes to a dead proxy and an empty resolver.
    "--proxy-server=http://127.0.0.1:9", "--host-resolver-rules=MAP * ~NOTFOUND",
]


LAUNCHER = ("import ctypes, os, sys\n"
            "ctypes.CDLL(None, use_errno=True).prctl(1, 9)\n"      # PR_SET_PDEATHSIG, SIGKILL
            "if os.getppid() == 1: os._exit(1)\n"
            "os.execv(sys.argv[1], sys.argv[1:])\n")
LIVE = set()
LIVE_LOCK = threading.Lock()


def kill_all_chromes():
    with LIVE_LOCK:
        live = list(LIVE)
    for c in live:
        c.kill()


def install_exit_handlers():
    """Chromes die with us: on SIGTERM/SIGINT/SIGHUP and normal exit here, on SIGKILL by the kernel (see LAUNCHER)."""
    import atexit
    atexit.register(kill_all_chromes)

    def handler(signum, frame):
        kill_all_chromes()
        os._exit(128 + signum)
    for sig in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP):
        signal.signal(sig, handler)


def utcnow():
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def log(msg):
    print(time.strftime("%H:%M:%S ") + msg, flush=True)


class ChromeDied(Exception):
    pass


class CdpError(Exception):
    pass


class CdpTimeout(Exception):
    pass


class PageTimeout(Exception):
    pass


class PageError(Exception):
    pass


# --------------------------------------------------------------------------------------------------------------
# DevTools protocol over pipes
# --------------------------------------------------------------------------------------------------------------

class Cdp:
    """One Chrome process. A reader thread routes responses to waiters and events to handlers."""

    def __init__(self, chrome, profile, extra_flags, stderr_path):
        to_chrome = os.pipe()
        from_chrome = os.pipe()
        # Keep our ends clear of fds 3 and 4, which Chrome expects for the protocol.
        hi = [fcntl.fcntl(fd, fcntl.F_DUPFD_CLOEXEC, 100) for fd in (*to_chrome, *from_chrome)]
        for fd in (*to_chrome, *from_chrome):
            os.close(fd)
        c_in, self.w, self.r, c_out = hi[0], hi[1], hi[2], hi[3]
        # hi = [to_chrome.read, to_chrome.write, from_chrome.read, from_chrome.write]
        os.makedirs(os.path.dirname(stderr_path), exist_ok=True)
        os.makedirs(profile, exist_ok=True)
        actions = [
            (os.POSIX_SPAWN_DUP2, c_in, 3),
            (os.POSIX_SPAWN_DUP2, c_out, 4),
            (os.POSIX_SPAWN_OPEN, 0, os.devnull, os.O_RDONLY, 0),
            (os.POSIX_SPAWN_OPEN, 1, os.devnull, os.O_WRONLY, 0),
            (os.POSIX_SPAWN_OPEN, 2, stderr_path, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o644),
        ]
        # The launcher asks the kernel to kill Chrome when this process dies (even by SIGKILL), then becomes Chrome.
        argv = [sys.executable, "-c", LAUNCHER, chrome, *CHROME_FLAGS, "--user-data-dir=" + profile, *extra_flags,
                "about:blank"]
        self.pid = os.posix_spawn(sys.executable, argv, os.environ, file_actions=actions, setpgroup=0)
        with LIVE_LOCK:
            LIVE.add(self)
        os.close(c_in)
        os.close(c_out)
        self.killed = False
        self.wlock = threading.Lock()
        self.cv = threading.Condition()
        self.results = {}
        self.nowait = set()
        self.next_id = 0
        self.dead = False
        self.handlers = []
        self.errors = []
        self.reader = threading.Thread(target=self._read_loop, daemon=True)
        self.reader.start()

    def _read_loop(self):
        buf = bytearray()
        scan = 0
        try:
            while True:
                chunk = os.read(self.r, 1 << 20)
                if not chunk:
                    break
                buf += chunk
                while True:
                    i = buf.find(b"\0", scan)
                    if i < 0:
                        scan = len(buf)
                        break
                    raw = bytes(buf[:i])
                    del buf[:i + 1]
                    scan = 0
                    self._dispatch(json.loads(raw))
        except (OSError, ValueError) as e:
            self.errors.append("reader: %r" % (e,))
        finally:
            with self.cv:
                self.dead = True
                self.cv.notify_all()

    def _dispatch(self, msg):
        if "id" in msg:
            with self.cv:
                if msg["id"] in self.nowait:
                    self.nowait.discard(msg["id"])
                    if "error" in msg and len(self.errors) < 20:
                        self.errors.append("nowait: %s" % (msg["error"],))
                else:
                    self.results[msg["id"]] = msg
                    self.cv.notify_all()
            return
        for h in list(self.handlers):
            try:
                h(msg)
            except Exception as e:  # a bad handler must not kill the reader
                if len(self.errors) < 20:
                    self.errors.append("handler: %r" % (e,))

    def _write(self, method, params, session, wait):
        with self.wlock:
            self.next_id += 1
            mid = self.next_id
            if not wait:
                self.nowait.add(mid)
            msg = {"id": mid, "method": method, "params": params or {}}
            if session:
                msg["sessionId"] = session
            data = json.dumps(msg).encode() + b"\0"
            try:
                view = memoryview(data)
                while view:
                    n = os.write(self.w, view)
                    view = view[n:]
            except OSError as e:
                raise ChromeDied("write failed: %r" % (e,))
        return mid

    def send_nowait(self, method, params=None, session=None):
        self._write(method, params, session, False)

    def send(self, method, params=None, session=None, timeout=30.0):
        mid = self._write(method, params, session, True)
        end = time.monotonic() + timeout
        with self.cv:
            while mid not in self.results:
                if self.dead:
                    raise ChromeDied("Chrome closed the pipe during %s" % method)
                rem = end - time.monotonic()
                if rem <= 0:
                    raise CdpTimeout("%s timed out after %.0fs" % (method, timeout))
                self.cv.wait(min(rem, 1.0))
            msg = self.results.pop(mid)
        if "error" in msg:
            raise CdpError("%s: %s" % (method, msg["error"].get("message", msg["error"])))
        return msg.get("result", {})

    def kill(self):
        if self.killed:
            return
        self.killed = True
        try:
            os.killpg(self.pid, signal.SIGKILL)   # Chrome and its children: the group was created for it
        except OSError:
            try:
                os.kill(self.pid, signal.SIGKILL)
            except OSError:
                pass

    def close(self):
        self.kill()
        try:
            os.waitpid(self.pid, 0)
        except OSError:
            pass
        self.reader.join(5)
        for fd in (self.w, self.r):
            try:
                os.close(fd)
            except OSError:
                pass
        with LIVE_LOCK:
            LIVE.discard(self)


# --------------------------------------------------------------------------------------------------------------
# Corpus store and offline serving
# --------------------------------------------------------------------------------------------------------------

def norm_url(url):
    """Key for the URL index: lower-case scheme and host, default port dropped, empty path is '/', no fragment."""
    try:
        p = urllib.parse.urlsplit(url)
        host = (p.hostname or "").lower()
        port = p.port if p.netloc else None
    except ValueError:
        return url
    if port and not ((p.scheme == "http" and port == 80) or (p.scheme == "https" and port == 443)):
        host = "%s:%d" % (host, port)
    return urllib.parse.urlunsplit((p.scheme.lower(), host, p.path or "/", p.query, ""))


class Corpus:
    def __init__(self, root):
        self.root = os.path.abspath(os.path.expanduser(root))
        self.index = {}   # normalized url -> record that can be served (first one wins)
        self.pages = []   # page records, one per distinct body, in manifest order
        seen = set()
        path = os.path.join(self.root, "manifest.jsonl")
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                rec = json.loads(line)
                url, status = rec.get("url"), rec.get("status")
                if not url or not isinstance(status, int):
                    continue
                key = norm_url(url)
                if key not in self.index:
                    self.index[key] = rec
                sha = rec.get("body_sha256")
                if (rec.get("kind") == "page" and status == 200 and sha and sha not in seen
                        and "text/html" in (rec.get("content_type") or "").lower()
                        and os.path.exists(self.body_path(sha))):
                    seen.add(sha)
                    self.pages.append(rec)

    def body_path(self, sha):
        return os.path.join(self.root, "bodies", sha[:2], sha)

    def body(self, rec):
        sha = rec.get("body_sha256")
        if not sha:
            return b""
        with open(self.body_path(sha), "rb") as f:
            return f.read()


def decode_body(headers, body):
    """Undo a transfer compression the store kept. Returns (bytes, ok)."""
    enc = ""
    for k, v in headers:
        if k.lower() == "content-encoding":
            enc = v.strip().lower()
    if not enc or enc == "identity":
        return body, True
    try:
        if enc in ("gzip", "x-gzip"):
            return gzip.decompress(body), True
        if enc == "deflate":
            return zlib.decompress(body), True
    except (OSError, zlib.error, EOFError):
        pass
    return b"", False


def header_pairs(rec):
    h = rec.get("headers") or []
    if isinstance(h, dict):
        return [(k, v) for k, v in h.items()]
    return [(k, v) for k, v in h]


class Tab:
    """One page in a fresh browser context, with every request answered from the corpus."""

    def __init__(self, cdp, corpus, mode, debug=None):
        self.cdp, self.corpus, self.mode = cdp, corpus, mode
        self.debug, self.t0, self.stuck = debug, time.monotonic(), False
        self.dlock = threading.Lock()
        self.last_command = None
        self.requests = collections.Counter()
        self.unserved = []
        self.extra = {PROBE_URL: PROBE_HTML}   # in-memory documents, used by the probe
        self.ctx_id = None
        self.world = None
        self.main_frame = None
        self.closed = False
        r = cdp.send("Target.createBrowserContext", {"disposeOnDetach": True})
        self.context = r["browserContextId"]
        self.target = cdp.send("Target.createTarget", {"url": "about:blank", "browserContextId": self.context})["targetId"]
        self.session = cdp.send("Target.attachToTarget", {"targetId": self.target, "flatten": True})["sessionId"]
        cdp.handlers.append(self._on_event)
        self.send("Page.enable")
        self.send("Fetch.enable", {"patterns": [{"urlPattern": "*"}]})
        if mode.startswith("emulation"):
            self.send("Emulation.setScriptExecutionDisabled", {"value": True})
        self.main_frame = self.send("Page.getFrameTree")["frameTree"]["frame"]["id"]

    def dlog(self, msg):
        if self.debug:
            with self.dlock:
                self.debug.write("%8.3f %s\n" % (time.monotonic() - self.t0, msg))
                self.debug.flush()

    @staticmethod
    def _brief(method, params):
        if method == "Runtime.evaluate":
            e = params.get("expression", "")
            return "%s len=%d %r%s%s" % (method, len(e), e[:70].replace("\n", " "),
                                       " awaitPromise" if params.get("awaitPromise") else "",
                                       " ctx=%s" % params["contextId"] if "contextId" in params else "")
        return "%s %s" % (method, json.dumps(params)[:160])

    def send(self, method, params=None, timeout=30.0):
        brief = self._brief(method, params or {})
        self.last_command = brief
        t = time.monotonic()
        self.dlog("-> %s (timeout %.0fs)" % (brief, timeout))
        try:
            r = self.cdp.send(method, params, self.session, timeout)
        except Exception as e:
            if isinstance(e, (CdpTimeout, ChromeDied)):
                self.stuck = True
            self.dlog("<- %s after %.3fs: %s" % (type(e).__name__, time.monotonic() - t, str(e)[:300]))
            raise
        self.dlog("<- ok %.3fs %s" % (time.monotonic() - t, json.dumps(r)[:120]))
        return r

    # ---- request interception (runs on the reader thread: must not wait for responses)

    def _on_event(self, msg):
        if msg.get("method") != "Fetch.requestPaused" or msg.get("sessionId") != self.session:
            return
        p = msg["params"]
        rid, req = p["requestId"], p["request"]
        url, rtype = req["url"], p.get("resourceType", "Other")
        is_main = p.get("frameId") == self.main_frame
        s = self.session
        self.dlog("request %s %s frame=%s" % (rtype, url[:200], "main" if is_main else "sub"))
        send = self.cdp.send_nowait
        if url in self.extra and rtype == "Document":
            self.requests["probe"] += 1
            send("Fetch.fulfillRequest", {"requestId": rid, "responseCode": 200,
                                         "responseHeaders": [{"name": k, "value": v} for k, v in self._doc_headers([("Content-Type", "text/html")])],
                                         "body": base64.b64encode(self.extra[url]).decode()}, s)
            return
        rec = self.corpus.index.get(norm_url(url)) if rtype in SERVED_TYPES and req.get("method", "GET") == "GET" else None
        if rec is not None:
            headers = header_pairs(rec)
            code = rec["status"]
            body, ok = decode_body(headers, self.corpus.body(rec) if rec.get("body_sha256") else b"")
            if ok and 100 <= code <= 599:
                out = [(k, v) for k, v in headers if k.lower() not in DROP_HEADERS]
                if rtype == "Document":
                    out = self._doc_headers(out)
                self.requests["served"] += 1
                self.dlog("  served %d %d bytes" % (code, len(body)))
                send("Fetch.fulfillRequest", {"requestId": rid, "responseCode": code,
                                             "responseHeaders": [{"name": k, "value": v} for k, v in out],
                                             "body": base64.b64encode(body).decode()}, s)
                return
            self._unserved(url, "undecodable")
        else:
            self._unserved(url, rtype)
        # Not in the store, or not a kind we serve.
        self.dlog("  not served: " + ("empty answer" if rtype == "Stylesheet" or (rtype == "Document" and not is_main)
                                 else "blocked"))
        if rtype == "Stylesheet" or (rtype == "Document" and not is_main):
            ctype = "text/css" if rtype == "Stylesheet" else "text/html"
            self.requests["empty"] += 1
            send("Fetch.fulfillRequest", {"requestId": rid, "responseCode": 200,
                                         "responseHeaders": [{"name": "Content-Type", "value": ctype}],
                                         "body": ""}, s)
        else:
            self.requests["blocked"] += 1
            send("Fetch.failRequest", {"requestId": rid, "errorReason": "BlockedByClient"}, s)

    def _unserved(self, url, why):
        if len(self.unserved) < 100:
            self.unserved.append("%s %s" % (why, url[:300]))

    def _doc_headers(self, headers):
        # In csp-main mode the page's scripts are stopped by a policy added to every document.
        out = list(headers)
        if self.mode == "csp-main":
            out.append(("Content-Security-Policy", "script-src 'none'"))
        return out

    # ---- driving the page

    def set_viewport(self, width, height):
        self.send("Emulation.setDeviceMetricsOverride", {"width": width, "height": height,
                                                         "deviceScaleFactor": 1, "mobile": False})

    def navigate(self, url, deadline, settle):
        self.ctx_id = None
        r = self.send("Page.navigate", {"url": url}, timeout=max(1.0, min(30.0, deadline - time.monotonic())))
        if r.get("errorText"):
            raise PageError("navigation failed: " + r["errorText"])
        while True:
            if time.monotonic() > deadline:
                raise PageTimeout("page did not finish loading")
            try:
                href, _, state = self.eval(EXPR_STATE, timeout=5.0).partition("\t")
            except CdpError:
                state, href = "", ""   # context replaced by the navigation; ask again
            if state == "complete" and href != "about:blank":
                break
            self.ctx_id = None
            time.sleep(0.05)
        time.sleep(settle)

    def eval(self, expr, await_promise=False, timeout=30.0):
        params = {"expression": expr, "returnByValue": True, "awaitPromise": await_promise}
        if self.mode == "emulation-isolated":
            if self.ctx_id is None:
                self.ctx_id = self.send("Page.createIsolatedWorld", {
                    "frameId": self.send("Page.getFrameTree")["frameTree"]["frame"]["id"],
                    "worldName": "onehtml-capture", "grantUniveralAccess": True})["executionContextId"]
            params["contextId"] = self.ctx_id
        r = self.send("Runtime.evaluate", params, timeout)
        if "exceptionDetails" in r:
            d = r["exceptionDetails"]
            raise CdpError("evaluation threw: %s" % (d.get("exception", {}).get("description") or d.get("text")))
        return r.get("result", {}).get("value")

    def close(self):
        if self.closed:
            return
        self.closed = True
        try:
            self.cdp.handlers.remove(self._on_event)
        except ValueError:
            pass
        try:
            self.cdp.send("Target.closeTarget", {"targetId": self.target}, timeout=5.0)
            self.cdp.send("Target.disposeBrowserContext", {"browserContextId": self.context}, timeout=5.0)
        except (CdpError, CdpTimeout, ChromeDied):
            pass


class Browser:
    def __init__(self, chrome, workdir, name, corpus, mode, no_sandbox, logdir=None):
        self.corpus, self.mode = corpus, mode
        flags = ["--no-sandbox"] if no_sandbox else []
        profile = tempfile.mkdtemp(prefix="profile-", dir=workdir)
        self.profile = profile
        self.cdp = Cdp(chrome, profile, flags, os.path.join(logdir or workdir, "chrome-%s.stderr" % name))
        self.pages = 0
        self.stuck = False
        try:
            self.version = self.cdp.send("Browser.getVersion", timeout=60.0)
        except Exception:
            self.close()
            raise

    def tab(self, debug=None):
        self.pages += 1
        return Tab(self.cdp, self.corpus, self.mode, debug)

    def close(self):
        self.cdp.close()
        shutil.rmtree(self.profile, ignore_errors=True)


def probe_mode(browser_factory, wanted):
    """Find the first script-disabled mode in which the page's script does not run and our own evaluation works.

    Returns (browser, mode, info). The probe page has an inline script that renames the title and a noscript block."""
    tried = []
    for mode in (MODES if wanted == "auto" else [wanted]):
        br = browser_factory(mode)
        tab = None
        try:
            tab = br.tab()
            tab.navigate(PROBE_URL, time.monotonic() + 30, 0.0)
            title = tab.eval(EXPR_TITLE)
            noscript = tab.eval(EXPR_NOSCRIPT)
            math = tab.eval(PROBE_MATH)
            info = {"title_after_load": title, "noscript_parsed_as_markup": noscript, "evaluate_works": math == 42}
            if title == "t" and math == 42:
                tab.close()
                return br, mode, info, tried
            tried.append({"mode": mode, "result": info})
        except (CdpError, CdpTimeout, PageError, PageTimeout) as e:
            tried.append({"mode": mode, "error": str(e)})
        if tab:
            tab.close()
        br.close()
    raise RuntimeError("no script-disabled mode works: %s" % json.dumps(tried))


# --------------------------------------------------------------------------------------------------------------
# Capture
# --------------------------------------------------------------------------------------------------------------

def gz_json(path, obj):
    data = json.dumps(obj, ensure_ascii=True, separators=(",", ":")).encode("ascii")
    blob = gzip.compress(data, 6, mtime=0)
    tmp = path + ".part"
    with open(tmp, "wb") as f:
        f.write(blob)
    os.replace(tmp, path)
    return {"bytes": len(blob), "json_bytes": len(data), "sha256": hashlib.sha256(blob).hexdigest()}


def flatten_frames(tree):
    out, stack = [], [(tree, None)]
    while stack:
        node, parent = stack.pop()
        fr = node["frame"]
        out.append({"frameId": fr["id"], "parentFrameId": parent, "url": fr.get("url", "")})
        for c in reversed(node.get("childFrames") or []):
            stack.append((c, fr["id"]))
    return out


def count_axe(results):
    out = {}
    for kind in ("violations", "passes", "incomplete", "inapplicable"):
        out[kind] = {r["id"]: len(r.get("nodes") or []) for r in results.get(kind, [])}
    return out


def capture_viewport(tab, page, vp, args, axe_source, outdir, env):
    w, h = vp
    t0 = time.monotonic()
    deadline = t0 + args.page_timeout
    tab.requests.clear()
    tab.unserved.clear()
    tab.set_viewport(w, h)
    tab.navigate(page["url"], deadline, args.settle)
    remaining = lambda: max(1.0, deadline - time.monotonic())
    tab.dlog("viewport %dx%d, url %s" % (w, h, page["url"]))
    os.makedirs(outdir, exist_ok=True)
    meta = {"schema": SCHEMA + "/meta", "url": page["url"], "body_sha256": page["body_sha256"],
            "viewport": {"width": w, "height": h, "deviceScaleFactor": 1}, "captured_at": utcnow(),
            "script_mode": tab.mode, "files": {}, "warnings": [], "errors": {}}
    meta["final_url"] = tab.eval("location.href")
    if norm_url(meta["final_url"]) != norm_url(page["url"]):
        meta["warnings"].append("page navigated away from its URL (redirect or meta refresh)")
    timing = {}

    def step(name, fn):
        s = time.monotonic()
        tab.dlog("step %s" % name)
        try:
            fn()
        except (CdpError, CdpTimeout, PageError) as e:
            meta["errors"][name] = "%s: %s (last command: %s)" % (type(e).__name__, str(e)[:300], tab.last_command)
            tab.dlog("step %s failed: %s" % (name, meta["errors"][name]))
        timing[name] = round(time.monotonic() - s, 3)
        tab.dlog("step %s done in %.3fs" % (name, timing[name]))

    def do_ax():
        frames = flatten_frames(tab.send("Page.getFrameTree", timeout=remaining())["frameTree"])[:MAX_FRAMES]
        nodes = 0
        for i, fr in enumerate(frames):
            try:
                params = {} if i == 0 else {"frameId": fr["frameId"]}
                fr["nodes"] = tab.send("Accessibility.getFullAXTree", params, timeout=remaining())["nodes"]
                nodes += len(fr["nodes"])
            except CdpError as e:
                fr["nodes"], fr["error"] = [], str(e)[:300]
        meta["files"]["ax"] = gz_json(os.path.join(outdir, "ax.json.gz"), {"schema": SCHEMA + "/ax", "frames": frames})
        meta["ax"] = {"frames": len(frames), "nodes": nodes,
                      "nodes_main": len(frames[0]["nodes"]) if frames else 0}

    def do_snapshot():
        params = {"computedStyles": COMPUTED_STYLES, "includePaintOrder": True, "includeDOMRects": True,
                  "includeBlendedBackgroundColors": True, "includeTextColorOpacities": True}
        snap = tab.send("DOMSnapshot.captureSnapshot", params, timeout=remaining())
        fmt = {"domain": "DOMSnapshot", "method": "captureSnapshot", "protocolVersion": env["chrome"]["protocolVersion"],
               "product": env["chrome"]["product"], "parameters": params}
        meta["files"]["snapshot"] = gz_json(os.path.join(outdir, "snapshot.json.gz"),
                                           {"schema": SCHEMA + "/snapshot", "format": fmt, "snapshot": snap})
        docs = snap.get("documents") or []
        meta["snapshot"] = {"documents": len(docs), "strings": len(snap.get("strings") or []),
                            "nodes": sum(len((d.get("nodes") or {}).get("nodeType") or []) for d in docs),
                            "layout_nodes": sum(len((d.get("layout") or {}).get("nodeIndex") or []) for d in docs)}

    def do_dom():
        html = tab.eval(EXPR_DOM, timeout=remaining())
        doctype = tab.eval(EXPR_DOCTYPE, timeout=remaining())
        meta["files"]["dom"] = gz_json(os.path.join(outdir, "dom.json.gz"),
                                      {"schema": SCHEMA + "/dom", "doctype": doctype, "outerHTML": html})
        meta["dom"] = {"chars": len(html)}

    def do_axe():
        if tab.mode.startswith("emulation"):
            # axe awaits timers and promises that do not run while scripting is switched off. The page has finished
            # loading with scripts off and nothing is pending in it, so none of its script runs from here on.
            tab.send("Emulation.setScriptExecutionDisabled", {"value": False}, timeout=remaining())
        tab.eval(axe_source + "\n;void 0", timeout=remaining())   # defines window.axe (or its isolated-world twin); opaque
        version = tab.eval(EXPR_AXE_VERSION, timeout=remaining())
        raw = tab.eval(EXPR_AXE_RUN, await_promise=True, timeout=min(remaining(), args.axe_timeout))
        results = json.loads(raw)
        meta["files"]["axe"] = gz_json(os.path.join(outdir, "axe.json.gz"), {
            "schema": SCHEMA + "/axe", "axe_version": version, "options": AXE_OPTIONS_NOTE, "results": results})
        counts = count_axe(results)
        meta["axe"] = {"version": version, **{k: sum(v.values()) for k, v in counts.items()},
                       "rules": counts}

    step("ax", do_ax)
    step("snapshot", do_snapshot)
    step("dom", do_dom)
    step("axe", do_axe)
    meta["requests"] = dict(tab.requests)
    meta["unserved"] = list(tab.unserved)
    meta["seconds"] = {"total": round(time.monotonic() - t0, 3), **timing}
    if not meta["errors"]:
        meta["ok"] = True
        s = json.dumps(meta, indent=1, ensure_ascii=False)
        tmp = os.path.join(outdir, "meta.json.part")
        with open(tmp, "w", encoding="utf-8") as f:
            f.write(s)
        os.replace(tmp, os.path.join(outdir, "meta.json"))
    else:
        with open(os.path.join(outdir, "errors.json"), "w", encoding="utf-8") as f:
            json.dump(meta, f, indent=1, ensure_ascii=False)
    return meta


def vp_name(vp):
    return "%dx%d" % vp


def parse_viewport(s):
    m = re.fullmatch(r"(\d{2,5})x(\d{2,5})", s)
    if not m:
        raise SystemExit("bad --viewport %r (want WIDTHxHEIGHT)" % s)
    return int(m.group(1)), int(m.group(2))


class Runner:
    def __init__(self, args, corpus, out, env, mode, no_sandbox, chrome, axe_source, first_browser):
        self.args, self.corpus, self.out, self.env = args, corpus, out, env
        self.mode, self.no_sandbox, self.chrome, self.axe_source = mode, no_sandbox, chrome, axe_source
        self.viewports = [parse_viewport(v) for v in args.viewport]
        self.mlock = threading.Lock()
        self.counts = collections.Counter()
        self.spare = [first_browser]
        self.start = time.monotonic()
        self.last_tab = None
        self.work = os.path.join(out, "work")

    def write_manifest(self, rec):
        with self.mlock:
            with open(os.path.join(self.out, "manifest.jsonl"), "a", encoding="utf-8") as f:
                f.write(json.dumps(rec, ensure_ascii=False, sort_keys=True) + "\n")
                f.flush()

    def new_browser(self, name):
        with self.mlock:
            if self.spare:
                return self.spare.pop()
        return Browser(self.chrome, self.work, name, self.corpus, self.mode, self.no_sandbox,
                       os.path.join(self.out, "logs"))

    def capture_page(self, br, page):
        sha = page["body_sha256"]
        base = os.path.join(self.out, "captures", sha[:2], sha)
        results = []
        os.makedirs(os.path.join(self.out, "debug"), exist_ok=True)
        dbg = open(os.path.join(self.out, "debug", sha + ".log"), "w", encoding="utf-8")
        dbg.write("page %s\nsha256 %s\nmode %s\n" % (page["url"], sha, self.mode))
        tab = None
        try:
            tab = br.tab(dbg)
            self.last_tab = tab
            for vp in self.viewports:
                meta = capture_viewport(tab, page, vp, self.args, self.axe_source, os.path.join(base, vp_name(vp)), self.env)
                results.append(meta)
        except BaseException as e:
            dbg.write("FAILED %s: %s\nlast command: %s\n" % (type(e).__name__, e, tab.last_command if tab else None))
            import traceback
            dbg.write(traceback.format_exc())
            raise
        finally:
            if tab:
                tab.close()
                if tab.stuck:
                    br.stuck = True
            dbg.close()
        return results

    def worker(self, name, todo):
        br = None
        try:
            while True:
                if time.monotonic() - self.start >= self.args.max_seconds:
                    return
                try:
                    page = todo.get_nowait()
                except queue.Empty:
                    return
                rec = {"type": "page", "run": self.env["run"], "url": page["url"], "body_sha256": page["body_sha256"],
                       "body_bytes": page.get("body_bytes"), "started": utcnow()}
                t0 = time.monotonic()
                status, error, metas = "failed", None, []
                for attempt in (1, 2):
                    try:
                        if br is None:
                            br = self.new_browser(name)
                        metas = self.capture_page(br, page)
                        bad = [m for m in metas if m["errors"]]
                        status = "ok" if not bad else "failed"
                        if bad:
                            error = "; ".join("%s: %s" % (k, v) for m in bad for k, v in m["errors"].items())[:500]
                        break
                    except ChromeDied as e:
                        error = "chrome died: %s" % e
                        status = "crashed"
                        br = self._drop(br)
                    except CdpTimeout as e:
                        error, status = str(e), "timeout"
                        br = self._drop(br)
                        break
                    except PageTimeout as e:
                        error, status = str(e), "timeout"
                        break
                    except PageError as e:
                        error, status = str(e), "failed"
                        break
                    except CdpError as e:
                        error, status = str(e)[:500], "failed"
                        break
                    except Exception as e:  # keep the run going; the record says what happened
                        error, status = "%s: %s" % (type(e).__name__, e), "failed"
                        br = self._drop(br)
                        break
                rec.update(status=status, error=error, seconds=round(time.monotonic() - t0, 3),
                           viewports=[{"viewport": vp_name((m["viewport"]["width"], m["viewport"]["height"])),
                                       "ok": bool(m.get("ok")), "errors": m["errors"],
                                       "ax_nodes": m.get("ax", {}).get("nodes"),
                                       "snapshot_bytes": m["files"].get("snapshot", {}).get("bytes"),
                                       "axe_violations": m.get("axe", {}).get("violations"),
                                       "bytes": sum(f["bytes"] for f in m["files"].values()),
                                       "seconds": m["seconds"]["total"]} for m in metas])
                self.write_manifest(rec)
                self.counts[status] += 1
                if br is not None and (br.pages >= self.args.recycle or br.stuck):
                    br = self._drop(br)
        finally:
            self._drop(br)

    @staticmethod
    def _drop(br):
        if br is not None:
            br.close()
        return None


def done_shas(out, retry_failed):
    done = {}
    path = os.path.join(out, "manifest.jsonl")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            for line in f:
                try:
                    r = json.loads(line)
                except ValueError:
                    continue
                if r.get("type") == "page":
                    done[r["body_sha256"]] = r["status"]
    return {s for s, st in done.items() if st == "ok" or not retry_failed}


def find_pins():
    with open(PINS_JSON) as f:
        pins = json.load(f)
    cft, axe = pins["chrome_for_testing"]["version"], pins["axe_core"]["version"]
    return (os.path.join(PINS_ROOT, "chrome-for-testing-" + cft, "chrome-linux64", "chrome"),
            os.path.join(PINS_ROOT, "axe-core-" + axe, "axe.min.js"), cft, axe)


def launch_probe(args, corpus, workdir, chrome, logdir):
    """Start Chrome, sandboxed unless it cannot run that way, and settle the script-disabled mode."""
    choices = {"always": [True], "never": [False], "auto": [False, True]}[args.no_sandbox]
    last = None
    for ns in choices:
        try:
            made = []

            def factory(mode):
                b = Browser(chrome, workdir, "probe", corpus, mode, ns, logdir)
                made.append(b)
                return b

            br, mode, info, tried = probe_mode(factory, args.script_mode)
            return br, mode, ns, info, tried
        except (ChromeDied, CdpTimeout, CdpError, OSError) as e:
            last = e
            log("Chrome did not start with no_sandbox=%s: %s" % (ns, e))
    raise SystemExit("cannot start Chrome: %s" % last)


def fonts_fingerprint():
    try:
        out = subprocess.run(["fc-list", "--format", "%{family}|%{style}\n"], capture_output=True, text=True, timeout=20).stdout
    except (OSError, subprocess.SubprocessError):
        return None
    lines = sorted(set(out.splitlines()))
    return {"faces": len(lines), "sha256": hashlib.sha256("\n".join(lines).encode()).hexdigest()}


def cmd_run(args):
    install_exit_handlers()
    chrome_default, axe_default, cft, axe_ver = find_pins()
    chrome = args.chrome or chrome_default
    axe_path = args.axe or axe_default
    out = os.path.abspath(os.path.expanduser(args.out))
    os.makedirs(os.path.join(out, "work"), exist_ok=True)
    corpus = Corpus(args.corpus)
    log("corpus: %d distinct HTML pages, %d servable URLs" % (len(corpus.pages), len(corpus.index)))
    with open(axe_path, "rb") as f:
        axe_bytes = f.read()
    axe_source = axe_bytes.decode("utf-8")   # passed to the browser as-is
    axe_sha = hashlib.sha256(axe_bytes).hexdigest()
    del axe_bytes

    br, mode, no_sandbox, info, tried = launch_probe(args, corpus, os.path.join(out, "work"), chrome, os.path.join(out, "logs"))
    version = br.version
    env = {
        "schema": SCHEMA + "/environment", "created": utcnow(),
        "chrome": {"product": version.get("product"), "protocolVersion": version.get("protocolVersion"),
                   "revision": version.get("revision"), "userAgent": version.get("userAgent"), "pinned": cft},
        "axe": {"pinned": axe_ver, "sha256": axe_sha, "options": AXE_OPTIONS_NOTE},
        "viewports": sorted(args.viewport), "computed_styles": COMPUTED_STYLES,
        "flags": CHROME_FLAGS + (["--no-sandbox"] if no_sandbox else []),
        "script_mode": mode, "script_probe": info, "script_modes_rejected": tried,
        "serving": {"served_types": sorted(SERVED_TYPES), "dropped_response_headers": sorted(DROP_HEADERS)},
        "settle_seconds": args.settle, "fonts": fonts_fingerprint(),
        "python": sys.version.split()[0], "corpus": os.path.basename(corpus.root),
    }
    env_path = os.path.join(out, "environment.json")
    if os.path.exists(env_path):
        with open(env_path) as f:
            old = json.load(f)
        for k in ("chrome", "axe", "viewports", "computed_styles"):
            a = old.get(k)
            b = env[k]
            if k == "chrome":
                a, b = (a or {}).get("product"), b["product"]
            if k == "axe":
                a, b = (a or {}).get("sha256"), b["sha256"]
            if a != b:
                br.close()
                raise SystemExit("environment.json differs from this run in %r; use a new --out directory" % k)
    else:
        with open(env_path, "w", encoding="utf-8") as f:
            json.dump(env, f, indent=1)
    env["run"] = time.strftime("%Y%m%dT%H%M%S")

    done = done_shas(out, args.retry_failed)
    pages = corpus.pages[:args.limit] if args.limit else corpus.pages
    todo_pages = [p for p in pages if p["body_sha256"] not in done]
    log("%d pages in scope, %d done, %d to capture; mode=%s sandbox=%s jobs=%d"
        % (len(pages), len(pages) - len(todo_pages), len(todo_pages), mode, "off" if no_sandbox else "on", args.jobs))
    runner = Runner(args, corpus, out, env, mode, no_sandbox, chrome, axe_source, br)
    runner.write_manifest({"type": "run", "run": env["run"], "started": utcnow(), "pages_in_scope": len(pages),
                           "todo": len(todo_pages), "jobs": args.jobs, "max_seconds": None if args.max_seconds == float("inf") else args.max_seconds})
    todo = queue.Queue()
    for p in todo_pages:
        todo.put(p)
    threads = [threading.Thread(target=runner.worker, args=("w%d" % i, todo)) for i in range(args.jobs)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    for b in runner.spare:
        b.close()
    remaining = todo.qsize()
    runner.write_manifest({"type": "run-end", "run": env["run"], "finished": utcnow(), "counts": dict(runner.counts),
                           "remaining": remaining, "seconds": round(time.monotonic() - runner.start, 1)})
    shutil.rmtree(os.path.join(out, "work"), ignore_errors=True)
    log("captured: %s" % dict(runner.counts))
    print("REMAINING=%d" % remaining)
    return 0


def cmd_probe(args):
    install_exit_handlers()
    chrome, _, cft, _ = find_pins()
    chrome = args.chrome or chrome
    work = tempfile.mkdtemp(prefix="probe-")
    try:
        class Empty:
            index = {}
        br, mode, ns, info, tried = launch_probe(args, Empty(), work, chrome, work)
        print(json.dumps({"product": br.version.get("product"), "protocolVersion": br.version.get("protocolVersion"),
                          "script_mode": mode, "no_sandbox": ns, "probe": info, "rejected_modes": tried}, indent=1))
        br.close()
    finally:
        shutil.rmtree(work, ignore_errors=True)
    return 0


def cmd_summary(args):
    out = os.path.abspath(os.path.expanduser(args.out))
    status = {}
    path = os.path.join(out, "manifest.jsonl")
    errors = collections.Counter()
    with open(path, encoding="utf-8") as f:
        for line in f:
            r = json.loads(line)
            if r.get("type") == "page":
                status[r["body_sha256"]] = r
    by = collections.Counter(r["status"] for r in status.values())
    for r in status.values():
        if r["status"] != "ok":
            errors[(r.get("error") or "")[:100]] += 1
    print("pages: %d  %s" % (len(status), "  ".join("%s=%d" % kv for kv in sorted(by.items()))))
    for e, n in errors.most_common(10):
        print("  %3d  %s" % (n, e))
    ax, snap, dom, viol, axe_files = [], [], [], collections.Counter(), 0
    rules_pass, rules_inc = collections.Counter(), collections.Counter()
    for sha, r in status.items():
        for meta_path in sorted(_metas(out, sha)):
            with open(meta_path) as f:
                m = json.load(f)
            ax.append(m["ax"]["nodes"])
            snap.append(m["files"]["snapshot"]["bytes"])
            dom.append(m["dom"]["chars"])
            axe_files += 1
            for k, v in m["axe"]["rules"]["violations"].items():
                viol[k] += v
            for k, v in m["axe"]["rules"]["incomplete"].items():
                rules_inc[k] += v
    print("captures (page x viewport): %d" % len(ax))

    def dist(name, xs, unit=""):
        if xs:
            print("%-22s total %d  min %d  median %d  max %d%s" % (name, sum(xs), min(xs), statistics.median(xs), max(xs), unit))
    dist("AX nodes", ax)
    dist("snapshot bytes (gz)", snap)
    dist("serialized DOM chars", dom)
    print("axe violation nodes by rule (%d rules, %d nodes):" % (len(viol), sum(viol.values())))
    for k, v in sorted(viol.items(), key=lambda kv: (-kv[1], kv[0])):
        print("  %-36s %d" % (k, v))
    print("axe incomplete nodes: %d across %d rules" % (sum(rules_inc.values()), len(rules_inc)))
    return 0


def _metas(out, sha):
    base = os.path.join(out, "captures", sha[:2], sha)
    if os.path.isdir(base):
        for vp in os.listdir(base):
            p = os.path.join(base, vp, "meta.json")
            if os.path.exists(p):
                yield p


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p):
        p.add_argument("--chrome", help="Chrome binary (default: the pinned build under ~/onehtml/pins)")
        p.add_argument("--no-sandbox", choices=["auto", "always", "never"], default="auto",
                       help="auto: sandboxed, and only without the sandbox if Chrome cannot start with it")
        p.add_argument("--script-mode", choices=["auto"] + MODES, default="auto")

    r = sub.add_parser("run")
    common(r)
    r.add_argument("--corpus", required=True)
    r.add_argument("--out", required=True)
    r.add_argument("--axe", help="axe.min.js (default: the pinned release under ~/onehtml/pins)")
    r.add_argument("--viewport", action="append", help="WIDTHxHEIGHT, repeatable (default 1280x800)")
    r.add_argument("--jobs", type=int, default=2)
    r.add_argument("--limit", type=int, default=0, help="only the first N pages of the corpus, in manifest order")
    r.add_argument("--max-seconds", type=float, default=float("inf"))
    r.add_argument("--page-timeout", type=float, default=120.0, help="seconds per page and viewport")
    r.add_argument("--axe-timeout", type=float, default=60.0, help="seconds for axe.run on one page")
    r.add_argument("--settle", type=float, default=0.3, help="seconds to wait after the load event")
    r.add_argument("--recycle", type=int, default=100, help="restart a Chrome after this many pages")
    r.add_argument("--retry-failed", action="store_true")
    r.set_defaults(fn=cmd_run)
    s = sub.add_parser("summary")
    s.add_argument("out")
    s.set_defaults(fn=cmd_summary)
    p = sub.add_parser("probe")
    common(p)
    p.set_defaults(fn=cmd_probe)
    args = ap.parse_args(argv)
    if args.cmd == "run":
        args.viewport = args.viewport or DEFAULT_VIEWPORTS
    return args.fn(args)


if __name__ == "__main__":
    sys.exit(main())
