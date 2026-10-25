#!/usr/bin/env python3
"""Polite, resumable crawler for the school and university test corpus.

    crawl.py --seeds seeds.txt --out ~/onehtml/corpus/<version> [--max-seconds N]

Rerunning the same command continues where the last run stopped. See README.md.
"""
import argparse
import concurrent.futures
import heapq
import html.parser
import os
import re
import sys
import time
import urllib.parse

import common
import finalize

SKIP_EXT = re.compile(
    r"\.(pdf|docx?|xlsx?|pptx?|zip|gz|tgz|rar|7z|jpe?g|png|gif|svg|webp|ico|bmp|tiff?|"
    r"mp[34]|mov|avi|wmv|webm|wav|ogg|css|js|json|xml|rss|atom|txt|csv|exe|dmg|apk|ics)$", re.I)
SKIP_PATH = re.compile(r"(log-?out|log-?in|sign-?(in|out)|wp-admin|wp-login|/cart|/feed/?$)", re.I)


class _Links(html.parser.HTMLParser):
    """Pulls out what discovery needs: anchors, stylesheets, <base>, <style> text."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.anchors, self.sheets, self.base, self.style_text = [], [], None, []
        self._in_style = False

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == "a" and a.get("href"):
            self.anchors.append(a["href"])
        elif tag == "link" and a.get("href") and "stylesheet" in (a.get("rel") or "").lower().split():
            self.sheets.append(a["href"])
        elif tag == "base" and a.get("href") and self.base is None:
            self.base = a["href"]
        elif tag == "style":
            self._in_style = True

    def handle_endtag(self, tag):
        if tag == "style":
            self._in_style = False

    def handle_data(self, data):
        if self._in_style:
            self.style_text.append(data)


IMPORT_RE = re.compile(r"""@import\s+(?:url\(\s*)?["']?([^"')\s;]+)""", re.I)
COMMENT_RE = re.compile(r"/\*.*?\*/", re.S)


def css_imports(text):
    return [m.group(1) for m in IMPORT_RE.finditer(COMMENT_RE.sub("", text))]


def decode(data):
    return data.decode("utf-8", "replace")


def host_key(url):
    h = (urllib.parse.urlsplit(url).hostname or "").lower()
    return h[4:] if h.startswith("www.") else h


def http_url(base, ref):
    u = urllib.parse.urljoin(base, ref.strip())
    u, _ = urllib.parse.urldefrag(u)
    return u if urllib.parse.urlsplit(u).scheme in ("http", "https") else None


def is_html(rec):
    ct = (rec.get("content_type") or "").lower()
    return rec.get("status") == 200 and rec.get("body_sha256") and (
        "text/html" in ct or "application/xhtml" in ct)


class SiteCrawler:
    def __init__(self, fetcher, store, pages, max_sheets):
        self.f, self.store, self.pages, self.max_sheets = fetcher, store, pages, max_sheets

    def link(self, url, frm, via):
        self.store.append("links.jsonl", {"url": url, "from": frm, "via": via})

    def run(self, seed):
        f = self.f
        chain = f.get(seed, "page", seed)
        home = chain[-1]
        if home.get("refused"):
            return "refused", "robots: " + home["robots"], 0, 0
        if not is_html(home):
            return "failed", "home page: status=%s error=%s type=%s" % (
                home.get("status"), home.get("error"), home.get("content_type")), 0, 0
        if f.crawl_delay_too_long(home["url"]):
            return "refused", "crawl-delay above %ds" % common.MAX_CRAWL_DELAY, 0, 0
        site = host_key(home["url"])
        pages = [home]
        seen = {home["url"], seed}
        heap, counter = [], 0
        sheets = []  # (url, from)

        def harvest(rec):
            nonlocal counter
            p = _Links()
            try:
                p.feed(decode(f.body(rec)))
                p.close()
            except Exception:  # a hostile page must never stop the crawl
                pass
            base = http_url(rec["url"], p.base) if p.base else rec["url"]
            base = base or rec["url"]
            for href in p.anchors:
                u = http_url(base, href)
                if not u or u in seen or host_key(u) != site:
                    continue
                path = urllib.parse.urlsplit(u).path
                if SKIP_EXT.search(path) or SKIP_PATH.search(u):
                    continue
                seen.add(u)
                counter += 1
                segs = len([s for s in path.split("/") if s])
                heapq.heappush(heap, (segs, 1 if "?" in u else 0, counter, u, rec["url"]))
            for href in p.sheets:
                u = http_url(base, href)
                if u:
                    sheets.append((u, rec["url"], "link"))
            for u in (http_url(base, x) for x in css_imports("\n".join(p.style_text))):
                if u:
                    sheets.append((u, rec["url"], "import"))

        harvest(home)
        while heap and len(pages) < self.pages:
            _, _, _, u, frm = heapq.heappop(heap)
            self.link(u, frm, "a")
            recs = f.get(u, "page", seed)
            final = recs[-1]
            if is_html(final) and host_key(final["url"]) == site and final["url"] not in {p["url"] for p in pages}:
                pages.append(final)
                harvest(final)
        # Stylesheets: those the pages link, then their @imports, depth-limited.
        got, n_sheets, queue, depth = set(), 0, [(u, frm, via, 0) for u, frm, via in sheets], 0
        sheets.clear()
        while queue and n_sheets < self.max_sheets:
            u, frm, via, d = queue.pop(0)
            self.link(u, frm, via)
            if u in got:
                continue
            got.add(u)
            recs = f.get(u, "stylesheet", seed)
            n_sheets += 1
            final = recs[-1]
            if d < 3 and final.get("status") == 200 and final.get("body_sha256"):
                for x in css_imports(decode(f.body(final))):
                    iu = http_url(final["url"], x)
                    if iu:
                        queue.append((iu, final["url"], "import", d + 1))
        return "done", "", len(pages), n_sheets


def read_seeds(path):
    out = []
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.split("#", 1)[0].strip()
            if line:
                cat, url = line.split(None, 1) if " " in line else ("", line)
                out.append((cat, url.strip()))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--seeds", required=True)
    ap.add_argument("--out", required=True, help="corpus version directory")
    ap.add_argument("--max-seconds", type=float, default=0, help="stop starting work after this long (0 = no limit)")
    ap.add_argument("--pages", type=int, default=15, help="HTML pages per site, home page included")
    ap.add_argument("--max-sheets", type=int, default=60, help="stylesheets per site")
    ap.add_argument("--workers", type=int, default=24, help="sites crawled in parallel")
    ap.add_argument("--limit", type=int, default=0, help="only the first N seeds")
    a = ap.parse_args()

    store = common.Store(a.out)
    deadline = time.monotonic() + a.max_seconds if a.max_seconds else None
    fetcher = common.Fetcher(store, deadline)
    finished = {s["seed"] for s in store.read_log("sites.jsonl")}
    seeds = read_seeds(a.seeds)
    if a.limit:
        seeds = seeds[: a.limit]
    todo = [(c, u) for c, u in seeds if u not in finished]
    print("seeds=%d already-finished=%d todo=%d" % (len(seeds), len(finished), len(todo)), flush=True)
    crawler = SiteCrawler(fetcher, store, a.pages, a.max_sheets)

    def one(cu):
        cat, seed = cu
        try:
            status, reason, pages, sheets = crawler.run(seed)
        except common.Deadline:
            return False
        except Exception as e:  # record and move on; one site cannot stop the run
            status, reason, pages, sheets = "failed", "%s: %s" % (type(e).__name__, str(e)[:200]), 0, 0
        store.append("sites.jsonl", {"seed": seed, "category": cat, "status": status, "reason": reason,
                                     "pages": pages, "stylesheets": sheets, "at": common.utcnow()})
        print("%-8s %s %s" % (status, seed, reason), flush=True)
        return True

    with concurrent.futures.ThreadPoolExecutor(a.workers) as ex:
        results = list(ex.map(one, todo))
    remaining = results.count(False)
    finalize.run(a.out)
    print("REMAINING=%d" % remaining)
    return 0


if __name__ == "__main__":
    sys.exit(main())
