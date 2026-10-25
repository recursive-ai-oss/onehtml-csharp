"""Offline checks for the corpus tools: robots rules, discovery, resume, WARC parsing.

Run:  python3 -m unittest test_corpus     (from this directory)
Talks only to a throwaway server on 127.0.0.1.
"""
import gzip
import http.server
import json
import os
import tempfile
import threading
import unittest

os.environ["ONEHTML_CRAWL_MIN_DELAY_FOR_TESTS"] = "0.05"

import cc_sample  # noqa: E402
import common  # noqa: E402
import crawl  # noqa: E402
import finalize  # noqa: E402

PAGES = {
    "/": b'<html><head><link rel="stylesheet" href="/a.css"><style>@import "/b.css";</style></head>'
         b'<body><a href="/about">x</a><a href="/private/x">p</a><a href="/f.pdf">d</a>'
         b'<a href="http://other.invalid/">o</a><a href="/news/2024/a">n</a></body></html>',
    "/about": b'<html><body><a href="/team">t</a><link rel="stylesheet" href="/a.css"></body></html>',
    "/team": b"<html><body>team</body></html>",
    "/news/2024/a": b"<html><body>n</body></html>",
    "/a.css": b'@import url("c.css"); /* @import "nope.css"; */ body{}',
    "/b.css": b"p{}",
    "/c.css": b"i{}",
}
ROBOTS = b"User-agent: *\nDisallow: /private/\nCrawl-delay: 0\n"
hits = []


class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def do_GET(self):
        hits.append(self.path)
        if self.path == "/robots.txt":
            body, ct = ROBOTS, "text/plain"
        elif self.path in PAGES:
            body, ct = PAGES[self.path], "text/css" if self.path.endswith(".css") else "text/html; charset=utf-8"
        else:
            self.send_response(404)
            self.end_headers()
            return
        self.send_response(200)
        self.send_header("Content-Type", ct)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


class RobotsTests(unittest.TestCase):
    def test_groups_and_precedence(self):
        r = common.Robots.parse(
            b"User-agent: *\nDisallow: /\n\nUser-agent: OneHtmlCorpusBot\nDisallow: /x*.html$\nAllow: /x/ok\n"
            b"Disallow: /x/\nCrawl-delay: 7\n")
        self.assertEqual(r.crawl_delay, 7)
        self.assertTrue(r.allowed("https://a/y"))
        self.assertFalse(r.allowed("https://a/xyz.html"))
        self.assertTrue(r.allowed("https://a/xyz.html5"))
        self.assertTrue(r.allowed("https://a/x/ok"))  # longer Allow beats shorter Disallow
        self.assertFalse(r.allowed("https://a/x/other"))

    def test_star_group_and_modes(self):
        r = common.Robots.parse(b"User-agent: *\nDisallow: /a?q=\n")
        self.assertFalse(r.allowed("https://a/a?q=1"))
        self.assertTrue(r.allowed("https://a/a"))
        self.assertTrue(common.Robots("absent").allowed("https://a/"))
        self.assertFalse(common.Robots("unreachable").allowed("https://a/"))


class CrawlTests(unittest.TestCase):
    def test_crawl_and_resume(self):
        srv = http.server.ThreadingHTTPServer(("127.0.0.1", 0), H)
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        seed = "http://127.0.0.1:%d/" % srv.server_port
        out = tempfile.mkdtemp()
        store = common.Store(out)
        f = common.Fetcher(store)
        status, reason, pages, sheets = crawl.SiteCrawler(f, store, 15, 60).run(seed)
        self.assertEqual((status, pages, sheets), ("done", 4, 3), reason)
        self.assertNotIn("/private/x", hits)
        self.assertNotIn("/f.pdf", hits)
        self.assertNotIn("/nope.css", hits)
        n = len(hits)
        # A second run with a fresh fetcher repeats nothing on the network.
        f2 = common.Fetcher(store)
        crawl.SiteCrawler(f2, store, 15, 60).run(seed)
        self.assertEqual(len(hits), n)
        summary = finalize.run(out)
        self.assertEqual(summary["by_kind"]["stylesheet"], 3)
        man = [json.loads(l) for l in open(os.path.join(out, "manifest.jsonl"))]
        a = next(m for m in man if m["url"].endswith("/a.css"))
        self.assertEqual(sorted(a["linked_from"]), [seed, seed + "about"])
        self.assertEqual(len(a["body_sha256"]), 64)
        srv.shutdown()


class WarcTests(unittest.TestCase):
    def test_parse_warc(self):
        http_part = b"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=iso-8859-1\r\n\r\n<p>caf\xe9"
        warc = (b"WARC/1.0\r\nWARC-Type: response\r\nWARC-Date: 2026-09-10T01:02:03Z\r\n"
                b"Content-Length: %d\r\n\r\n" % len(http_part)) + http_part + b"\r\n\r\n"
        w, status, headers, body = cc_sample.parse_warc(gzip.compress(warc))
        self.assertEqual(status, 200)
        self.assertEqual(body, b"<p>caf\xe9")
        self.assertEqual(w["warc-date"], "2026-09-10T01:02:03Z")
        self.assertEqual(headers[0][0], "Content-Type")

    def test_pick_is_reproducible(self):
        lines = "\n".join('k 2026 ' + json.dumps({"url": "http://h%d.com/" % (i % 3), "status": "200", "mime": "text/html",
                                                   "filename": "f", "offset": "1", "length": "5"}) for i in range(30))
        a = cc_sample.pick_records(lines, 7, "s", 4)
        b = cc_sample.pick_records(lines, 7, "s", 4)
        self.assertEqual(a, b)
        self.assertEqual(len(a), 4)


if __name__ == "__main__":
    unittest.main()
