#!/usr/bin/env python3
"""Reproducible random sample of text/html responses from one Common Crawl crawl.

    cc_sample.py --crawl CC-MAIN-2026-39 --out ~/onehtml/corpus/<version> --target 10000

How the sample is drawn: the crawl's index is split into blocks of about three
thousand consecutive records, listed in a small `cluster.idx` file. A seeded
shuffle orders the blocks; each block, taken in that order, is downloaded (one
byte-range request), a few of its HTTP 200 text/html records are picked with a
seed derived from the block number, and each record's WARC bytes are fetched by
byte range. The same seed and crawl give the same sample. Rerunning continues
where the last run stopped.
"""
import argparse
import concurrent.futures
import json
import os
import random
import re
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import zlib

import common
import finalize

BASE = "https://data.commoncrawl.org/"
LOG = "fetches.cc.jsonl"
BLOCKS_LOG = "cc-blocks.jsonl"


def http_get(url, rng=None, tries=7):
    """GET with an optional byte range, backing off politely on 503/429 and errors."""
    delay = 2.0
    for attempt in range(tries):
        req = urllib.request.Request(url, headers={"User-Agent": common.USER_AGENT, "Accept-Encoding": "identity"})
        if rng:
            req.add_header("Range", "bytes=%d-%d" % rng)
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504) and attempt < tries - 1:
                ra = e.headers.get("Retry-After", "")
                time.sleep(min(float(ra) if ra.isdigit() else delay, 120))
                delay = min(delay * 2, 120)
                continue
            raise
        except (urllib.error.URLError, OSError):
            if attempt == tries - 1:
                raise
            time.sleep(delay)
            delay = min(delay * 2, 120)


def load_cluster(crawl, cache_dir):
    """Returns [(cdx file, offset, length)] from the crawl's cluster.idx, cached on disk."""
    os.makedirs(cache_dir, exist_ok=True)
    path = os.path.join(cache_dir, crawl + ".cluster.idx")
    if not os.path.exists(path):
        data = http_get(BASE + "cc-index/collections/%s/indexes/cluster.idx" % crawl)
        with open(path + ".part", "wb") as f:
            f.write(data)
        os.replace(path + ".part", path)
    blocks = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 4 and parts[2].isdigit() and parts[3].isdigit():
                blocks.append((parts[1], int(parts[2]), int(parts[3])))
    return blocks


def parse_warc(data):
    """Splits one gzip-member WARC response record into metadata, HTTP headers and body."""
    raw = zlib.decompress(data, 31)
    cut = raw.find(b"\r\n\r\n")
    warc = {}
    for line in raw[:cut].decode("utf-8", "replace").split("\r\n")[1:]:
        if ":" in line:
            k, v = line.split(":", 1)
            warc[k.strip().lower()] = v.strip()
    payload = raw[cut + 4: cut + 4 + int(warc.get("content-length", len(raw)))]
    hcut = payload.find(b"\r\n\r\n")
    head = payload[:hcut].decode("latin-1").split("\r\n") if hcut >= 0 else payload.decode("latin-1").split("\r\n")
    body = payload[hcut + 4:] if hcut >= 0 else b""
    m = re.match(r"HTTP/\S+\s+(\d{3})", head[0])
    headers = []
    for line in head[1:]:
        if ":" in line:
            k, v = line.split(":", 1)
            headers.append([k.strip(), v.strip()])
    return warc, (int(m.group(1)) if m else None), headers, body


def pick_records(block_text, block_no, seed, per_block, max_per_host=2, max_len=1500000):
    cands = []
    for line in block_text.splitlines():
        try:
            meta = json.loads(line.split(" ", 2)[2])
        except (IndexError, ValueError):
            continue
        if meta.get("status") == "200" and meta.get("mime") == "text/html" and meta.get("filename") \
                and int(meta.get("length", "0")) <= max_len:
            cands.append(meta)
    random.Random("%s:%d" % (seed, block_no)).shuffle(cands)
    chosen, per_host = [], {}
    for m in cands:
        host = urllib.parse.urlsplit(m["url"]).hostname or ""
        if per_host.get(host, 0) >= max_per_host:
            continue
        per_host[host] = per_host.get(host, 0) + 1
        chosen.append(m)
        if len(chosen) == per_block:
            break
    return chosen


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--crawl", required=True, help="pinned crawl id, e.g. CC-MAIN-2026-39")
    ap.add_argument("--out", required=True)
    ap.add_argument("--target", type=int, default=10000)
    ap.add_argument("--seed", default="onehtml-1", help="sampling seed; keep it fixed for a corpus version")
    ap.add_argument("--per-block", type=int, default=4)
    ap.add_argument("--concurrency", type=int, default=4)
    ap.add_argument("--max-seconds", type=float, default=0)
    ap.add_argument("--cache", default=os.path.expanduser("~/onehtml/cc"))
    a = ap.parse_args()

    store = common.Store(a.out)
    deadline = time.monotonic() + a.max_seconds if a.max_seconds else None
    blocks = load_cluster(a.crawl, a.cache)
    order = list(range(len(blocks)))
    random.Random(a.seed).shuffle(order)
    done_blocks = {b["block"] for b in store.read_log(BLOCKS_LOG)}
    count = [sum(1 for r in store.read_log(LOG) if r.get("body_sha256"))]
    lock = threading.Lock()
    print("crawl=%s blocks=%d done-blocks=%d records=%d target=%d" % (
        a.crawl, len(blocks), len(done_blocks), count[0], a.target), flush=True)

    def work(i):
        with lock:
            if count[0] >= a.target or (deadline and time.monotonic() > deadline):
                return False
        cdx, off, length = blocks[i]
        try:
            text = zlib.decompress(http_get(BASE + "cc-index/collections/%s/indexes/%s" % (a.crawl, cdx),
                                            (off, off + length - 1)), 31).decode("utf-8", "replace")
        except Exception as e:
            print("block %d failed: %s" % (i, type(e).__name__), flush=True)
            store.append(BLOCKS_LOG, {"block": i, "error": type(e).__name__})
            return True
        n = 0
        for m in pick_records(text, i, a.seed, a.per_block):
            o, ln = int(m["offset"]), int(m["length"])
            rec = {"url": m["url"], "kind": "page", "source": {
                "kind": "commoncrawl", "crawl": a.crawl, "warc": m["filename"], "offset": o, "length": ln}}
            try:
                warc, status, headers, body = parse_warc(http_get(BASE + m["filename"], (o, o + ln - 1)))
                rec.update(fetched_at=warc.get("warc-date"), status=status, headers=headers, error=None,
                           content_type=next((v for k, v in headers if k.lower() == "content-type"), None),
                           body_sha256=store.put_body(body), body_bytes=len(body))
                n += 1
            except Exception as e:
                rec.update(fetched_at=None, status=None, headers=[], error=type(e).__name__, content_type=None)
            store.append(LOG, rec)
            time.sleep(0.2)
        store.append(BLOCKS_LOG, {"block": i, "records": n})
        with lock:
            count[0] += n
            if count[0] % 100 < a.per_block:
                print("records=%d" % count[0], flush=True)
        return True

    todo = [i for i in order if i not in done_blocks]
    with concurrent.futures.ThreadPoolExecutor(a.concurrency) as ex:
        # Submit lazily so a reached target does not queue thousands of no-op tasks.
        pending, it, stop = set(), iter(todo), False
        while True:
            while not stop and len(pending) < a.concurrency * 2:
                nxt = next(it, None)
                if nxt is None:
                    stop = True
                    break
                pending.add(ex.submit(work, nxt))
            if not pending:
                break
            fin, pending = concurrent.futures.wait(pending, return_when=concurrent.futures.FIRST_COMPLETED)
            if any(not f.result() for f in fin):
                stop = True
    finalize.run(a.out)
    print("RECORDS=%d TARGET=%d %s" % (count[0], a.target, "COMPLETE" if count[0] >= a.target else "CONTINUE"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
