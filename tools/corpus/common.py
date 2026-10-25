"""Shared pieces for the corpus tools: polite HTTP, robots.txt, on-disk store.

Standard library only. Everything the tools fetch goes through `Fetcher`, which
enforces the politeness rules in one place so no caller can skip them.
"""
import datetime
import hashlib
import http.client
import json
import os
import re
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

BOT_TOKEN = "OneHtmlCorpusBot"
USER_AGENT = f"{BOT_TOKEN}/1.0 (+https://github.com/recursive-ai-oss/onehtml-csharp)"

# Floor for the gap between two requests to one host. The environment override
# exists only so the local unit test can talk to 127.0.0.1 without sleeping.
MIN_DELAY = float(os.environ.get("ONEHTML_CRAWL_MIN_DELAY_FOR_TESTS", "5"))
# A robots.txt Crawl-delay above this makes a site not worth crawling.
MAX_CRAWL_DELAY = 60.0
MAX_BODY = 8 * 1024 * 1024
MAX_REDIRECTS = 5
TIMEOUT = 30


def utcnow():
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def sha256_hex(data):
    return hashlib.sha256(data).hexdigest()


# --------------------------------------------------------------------------
# robots.txt (RFC 9309)
# --------------------------------------------------------------------------

class Robots:
    """Parsed robots.txt for one origin.

    `mode` is "rules" (parsed from a 2xx file), "absent" (4xx: everything
    allowed) or "unreachable" (5xx or network failure: everything disallowed,
    as RFC 9309 asks).
    """

    def __init__(self, mode="rules", rules=None, crawl_delay=None):
        self.mode = mode
        self.rules = rules or []  # (is_allow, pattern, compiled regex)
        self.crawl_delay = crawl_delay

    @staticmethod
    def _compile(pattern):
        anchored = pattern.endswith("$")
        if anchored:
            pattern = pattern[:-1]
        rx = ".*".join(re.escape(p) for p in pattern.split("*"))
        return re.compile(rx + ("$" if anchored else ""), re.S)

    @classmethod
    def parse(cls, data):
        text = data[:512000].decode("utf-8", "replace").lstrip("\ufeff")
        groups = []  # (set of lowercase agents, [(allow, pattern)], delay)
        agents, rules, delay, in_rules = [], [], None, False
        cur = None
        for raw in text.splitlines():
            line = raw.split("#", 1)[0].strip()
            if ":" not in line:
                continue
            key, value = line.split(":", 1)
            key, value = key.strip().lower(), value.strip()
            if key == "user-agent":
                if cur is None or in_rules:
                    cur = (set(), [], [None])
                    groups.append(cur)
                    in_rules = False
                cur[0].add(value.lower())
            elif key in ("allow", "disallow"):
                if cur is None:
                    continue
                in_rules = True
                if value:  # an empty Disallow means "allow all" and adds nothing
                    cur[1].append((key == "allow", value))
            elif key == "crawl-delay":
                if cur is None:
                    continue
                in_rules = True
                try:
                    cur[2][0] = float(value)
                except ValueError:
                    pass
        mine = [g for g in groups if BOT_TOKEN.lower() in g[0]]
        if not mine:
            mine = [g for g in groups if "*" in g[0]]
        rules_out, delay = [], None
        for g in mine:
            for allow, pat in g[1]:
                rules_out.append((allow, pat, cls._compile(pat)))
            if g[2][0] is not None and delay is None:
                delay = g[2][0]
        return cls("rules", rules_out, delay)

    def allowed(self, url):
        if self.mode == "absent":
            return True
        if self.mode == "unreachable":
            return False
        p = urllib.parse.urlsplit(url)
        target = (p.path or "/") + ("?" + p.query if p.query else "")
        best = None  # (length, allow)
        for allow, pat, rx in self.rules:
            if rx.match(target):
                cand = (len(pat), allow)
                if best is None or cand[0] > best[0] or (cand[0] == best[0] and allow):
                    best = cand
        return True if best is None else best[1]


# --------------------------------------------------------------------------
# Store: content-addressed bodies plus append-only JSONL logs
# --------------------------------------------------------------------------

class Store:
    def __init__(self, root):
        self.root = os.path.abspath(os.path.expanduser(root))
        os.makedirs(os.path.join(self.root, "bodies"), exist_ok=True)
        self._lock = threading.Lock()

    def put_body(self, data):
        sha = sha256_hex(data)
        d = os.path.join(self.root, "bodies", sha[:2])
        path = os.path.join(d, sha)
        if not os.path.exists(path):
            os.makedirs(d, exist_ok=True)
            tmp = path + ".part%d" % threading.get_ident()
            with open(tmp, "wb") as f:
                f.write(data)
            os.replace(tmp, path)
        return sha

    def get_body(self, sha):
        with open(os.path.join(self.root, "bodies", sha[:2], sha), "rb") as f:
            return f.read()

    def append(self, log, obj):
        line = json.dumps(obj, sort_keys=True, ensure_ascii=False) + "\n"
        with self._lock:
            with open(os.path.join(self.root, log), "a", encoding="utf-8") as f:
                f.write(line)

    def read_log(self, log):
        path = os.path.join(self.root, log)
        out = []
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        try:
                            out.append(json.loads(line))
                        except ValueError:
                            pass  # a torn last line from an interrupted run
        return out


# --------------------------------------------------------------------------
# Polite fetcher
# --------------------------------------------------------------------------

class Deadline(Exception):
    """The time budget for this chunk is spent; the work resumes next run."""


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    # Redirects are followed by hand so every hop meets robots.txt and the delay.
    def redirect_request(self, *a, **k):
        return None


_OPENER = urllib.request.build_opener(_NoRedirect)


class _Host:
    def __init__(self):
        self.lock = threading.Lock()  # held for a whole request: one at a time
        self.next_ok = 0.0
        self.delay = MIN_DELAY


class Fetcher:
    """Fetches with robots.txt, per-host delay and a resumable record log."""

    LOG = "fetches.crawl.jsonl"

    def __init__(self, store, deadline=None):
        self.store = store
        self.deadline = deadline  # monotonic time, or None
        self._hosts = {}
        self._hosts_lock = threading.Lock()
        self._robots = {}
        self._robots_lock = threading.Lock()
        self.done = {}  # requested url -> record, from earlier runs and this one
        for rec in store.read_log(self.LOG):
            self.done[rec["url"]] = rec

    def _host(self, url):
        name = (urllib.parse.urlsplit(url).hostname or "").lower()
        with self._hosts_lock:
            return self._hosts.setdefault(name, _Host())

    def _check_deadline(self):
        if self.deadline is not None and time.monotonic() > self.deadline:
            raise Deadline()

    def _record(self, rec):
        self.done[rec["url"]] = rec
        self.store.append(self.LOG, {k: v for k, v in rec.items() if not k.startswith("_")})

    def _network(self, url, kind, site, robots_verdict):
        """One real request, under the host's lock and delay. Always records."""
        self._check_deadline()
        host = self._host(url)
        with host.lock:
            wait = host.next_ok - time.monotonic()
            if wait > 0:
                time.sleep(wait)
            self._check_deadline()
            started = utcnow()
            status, headers, body, error = None, [], b"", None
            try:
                req = urllib.request.Request(url, headers={
                    "User-Agent": USER_AGENT,
                    "Accept": "text/html,application/xhtml+xml,text/css;q=0.9,*/*;q=0.5",
                    # Identity keeps the stored bytes directly usable.
                    "Accept-Encoding": "identity",
                })
                try:
                    resp = _OPENER.open(req, timeout=TIMEOUT)
                    status, headers = resp.status, resp.getheaders()
                    body = resp.read(MAX_BODY + 1)
                except urllib.error.HTTPError as e:
                    status, headers = e.code, list(e.headers.items())
                    body = e.read(MAX_BODY + 1)
            except (urllib.error.URLError, OSError, http.client.HTTPException, ValueError) as e:
                error = "%s: %s" % (type(e).__name__, str(e)[:200])
            finally:
                host.next_ok = time.monotonic() + host.delay
        rec = {"url": url, "kind": kind, "site": site, "fetched_at": started,
               "status": status, "error": error, "robots": robots_verdict,
               "headers": [[k, v] for k, v in headers]}
        if len(body) > MAX_BODY:
            rec["error"] = "too-large"
            body = b""
        loc = next((v for k, v in headers if k.lower() == "location"), None)
        if loc and status in (301, 302, 303, 307, 308):
            rec["location"] = urllib.parse.urljoin(url, loc)
        elif status is not None and body is not None and rec["error"] is None:
            rec["body_sha256"] = self.store.put_body(body)
            rec["body_bytes"] = len(body)
        ct = next((v for k, v in headers if k.lower() == "content-type"), None)
        rec["content_type"] = ct
        self._record(rec)
        return rec

    def _chain(self, url, kind, site, check_robots):
        recs = []
        for _ in range(MAX_REDIRECTS + 1):
            rec = self.done.get(url)
            if rec is None:
                verdict = "n/a"
                if check_robots:
                    robots = self.robots_for(url)
                    if robots.mode == "unreachable":
                        verdict = "unreachable-disallow"
                    elif robots.mode == "absent":
                        verdict = "absent-allow"
                    else:
                        verdict = "allowed"
                    if not robots.allowed(url):
                        self._check_deadline()
                        reason = "disallowed" if robots.mode == "rules" else verdict
                        rec = {"url": url, "kind": kind, "site": site, "fetched_at": utcnow(),
                               "status": None, "error": None, "refused": "robots",
                               "robots": reason, "headers": [], "content_type": None}
                        self._record(rec)
                        recs.append(rec)
                        return recs
                rec = self._network(url, kind, site, verdict)
            recs.append(rec)
            if rec.get("location") and urllib.parse.urlsplit(rec["location"]).scheme in ("http", "https"):
                url = rec["location"]
                continue
            return recs
        return recs

    def get(self, url, kind, site):
        """Fetch `url` following redirects. Returns the list of records, last = final."""
        return self._chain(url, kind, site, True)

    def body(self, rec):
        sha = rec.get("body_sha256")
        return self.store.get_body(sha) if sha else b""

    def robots_for(self, url):
        p = urllib.parse.urlsplit(url)
        origin = "%s://%s" % (p.scheme, p.netloc)
        with self._robots_lock:
            lock = self._robots.setdefault(origin, [threading.Lock(), None])
        with lock[0]:
            if lock[1] is None:
                recs = self._chain(origin + "/robots.txt", "robots", origin, False)
                last = recs[-1]
                st = last.get("status")
                if st is not None and 200 <= st < 300 and last.get("body_sha256"):
                    r = Robots.parse(self.body(last))
                elif st is not None and 400 <= st < 500:
                    r = Robots("absent")
                else:
                    r = Robots("unreachable")
                if r.crawl_delay is not None:
                    h = self._host(url)
                    h.delay = max(MIN_DELAY, r.crawl_delay)
                lock[1] = r
            return lock[1]

    def crawl_delay_too_long(self, url):
        r = self.robots_for(url)
        return r.crawl_delay is not None and r.crawl_delay > MAX_CRAWL_DELAY
