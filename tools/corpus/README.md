# Corpus tools

Scripts that build a local, never-committed test corpus of real pages for round-trip, differential, fuzz-seed and benchmark runs. Python 3.13, standard library only. Nothing they fetch is ever committed: only these scripts, the seed list and the docs are.

| File | What it does |
|---|---|
| `seeds.txt` | 300 start URLs (home pages), one per line: `<category> <url>` |
| `crawl.py` | Polite, resumable crawl of the seed sites: pages plus linked stylesheets |
| `cc_sample.py` | Reproducible random sample of `text/html` records from one Common Crawl crawl |
| `finalize.py` | Builds `manifest.jsonl` and the summary (the other two run it when they finish) |
| `common.py` | Fetcher, robots.txt parser, on-disk store shared by the above |
| `test_corpus.py` | Offline checks against a throwaway local server: `python3 -m unittest test_corpus` |
| `smoke_seeds.txt` | Four sites for proving the crawler |

## Seed list

Compiled by hand from public knowledge, not copied from a dataset: well-known US K-12 districts and schools (large urban, suburban, small and independent), US colleges and universities of every size (research, regional, liberal arts, HBCU), community and technical colleges, and a minority of international universities. The first draft held more than 550 sites and was thinned to 300 by taking every n-th entry per category, so each category keeps its spread: 100 K-12, 105 colleges, 50 community colleges, 45 international. Start URLs are home pages; a site that redirects elsewhere is followed (each hop under the rules below).

## Crawl politeness

- **robots.txt (RFC 9309).** Fetched once per origin before anything else. The group for the crawler's own product token (`OneHtmlCorpusBot`) applies if present, otherwise `*`; the longest matching rule wins, `Allow` wins a tie, `*` and `$` work. A 4xx robots.txt means everything is allowed; a 5xx or network failure means nothing is (the RFC's "unreachable" rule). A disallowed URL is recorded as refused and never requested.
- **Delay.** One request at a time per host, and at least 5 s between the end of one request and the start of the next. A larger `Crawl-delay` is honoured. A site asking for more than 60 s is skipped and recorded. Different hosts run in parallel (24 sites at once by default). Stylesheets on another host (a CDN) obey that host's robots.txt and delay.
- **Identity.** `OneHtmlCorpusBot/1.0 (+https://github.com/recursive-ai-oss/onehtml-csharp)`.
- **Redirects** are followed by hand (at most 5), so each hop meets robots.txt and the delay.
- **Bounds.** Home page plus up to 14 same-site HTML pages found through links (shallowest paths first, no files that are obviously not pages), and every stylesheet those pages link, including `@import`s up to 3 deep, to 60 stylesheets per site. Bodies over 8 MiB are not stored.
- **No retry storms.** A failure (timeout, 4xx, 5xx, refusal) is recorded once and not retried on later runs.
- Requests ask for `Accept-Encoding: identity` so the stored bytes are directly usable; whatever the server sends is stored exactly as received.

## Usage

```
python3 crawl.py --seeds seeds.txt --out ~/onehtml/corpus/<version> [--max-seconds 6600]
python3 cc_sample.py --crawl CC-MAIN-2026-39 --out ~/onehtml/corpus/<version> --target 10000 [--max-seconds 6600]
python3 finalize.py ~/onehtml/corpus/<version>
```

Both tools are **resumable**: run the same command again and it continues. `--max-seconds` stops starting new work after that long (requests in flight finish), so a chunk fits a time box; the last line says `REMAINING=<n>` (crawl) or `COMPLETE`/`CONTINUE` (sampler). The crawl derives its state from its logs: sites marked finished are skipped, and a half-done site is re-walked from the stored responses without touching the network.

### Common Crawl sample

One pinned crawl (record its id with the corpus version). The crawl's `cluster.idx` (one download, cached under `~/onehtml/cc/`) lists the index blocks of about 3,000 records. A seeded shuffle orders the blocks; each is fetched by byte range, up to 4 HTTP 200 `text/html` records (at most 2 per host) are chosen with a seed derived from the block number, and each record's WARC bytes are fetched by byte range from `data.commoncrawl.org`. The same crawl and `--seed` give the same sample. Politeness: 4 concurrent requests, a 0.2 s pause per record, exponential backoff and `Retry-After` on 429/5xx. The sampler stops within a few records of `--target`.

## Output layout

```
~/onehtml/corpus/<version>/
  bodies/ab/<sha256>          response bodies, content-addressed, byte-for-byte as received
  fetches.crawl.jsonl         one line per crawler request (append-only log; the crawler's state)
  fetches.cc.jsonl            one line per Common Crawl record
  links.jsonl                 who linked what: {"url", "from", "via": a|link|import}
  sites.jsonl                 one line per finished seed: done / refused / failed, with the reason
  cc-blocks.jsonl             Common Crawl blocks already processed
  manifest.jsonl              the manifest (generated, sorted, one record per line)
  summary.json, summary.txt   counts by source, kind, status; site outcomes; refusals
```

A manifest record has: `url`, `kind` (`page`, `stylesheet`, `robots`), `fetched_at` (UTC; the WARC date for Common Crawl), `status` (null for refusals and network errors), `error`, `refused` (`robots`), `robots` (verdict: `allowed`, `disallowed`, `absent-allow`, `unreachable-disallow`, `n/a`), `content_type`, `headers` (all response headers, in order), `body_sha256` and `body_bytes`, `location` (for redirects), `linked_from` (the pages that linked it), and `source`: `{"kind": "crawl", "site": <seed>}` or `{"kind": "commoncrawl", "crawl": <id>, "warc": <file>, "offset": n, "length": n}`.
