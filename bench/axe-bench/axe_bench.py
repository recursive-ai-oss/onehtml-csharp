#!/usr/bin/env python3
"""axe-core in Chrome, timed on the pages the checker benchmark uses.

    axe_bench.py run     --corpus DIR --ours OURS.jsonl --out OUT.jsonl [--repeats N] [--focus-cap N]
    axe_bench.py compare OURS.jsonl AXE.jsonl

`run` loads each page named by OURS.jsonl (the file `OneHtml.Benchmarks --check-per-page` writes) in the pinned headless
Chrome, the way tools/capture does (pages served offline from the corpus, page script off, then axe-core injected and
run), and times three things, each as the median-able list of `--repeats` runs after one warm-up:

  full_ms   axe.run over the whole document, every rule enabled, all result types, no xpath
  edit_ms   setAttribute('data-bench', '1') on the first element inside <body>, then a full axe.run
            (axe has no incremental mode, so a re-check is a full run)
  focus_ms  for the first --focus-cap focusable elements: focus() it, then axe.run with only the color-contrast rule
            (the rule a focus style changes); focus_n is how many elements were measured

The times are taken inside the page with performance.now() around the awaited axe.run calls; `wall_ms` is the protocol
round trip of the full run. axe.min.js is the pinned release, injected as an opaque string and never read. Python 3.13,
standard library only; nothing here is part of the libraries.
"""
import argparse
import hashlib
import json
import os
import statistics
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "tools", "capture"))
import capture  # noqa: E402  (the capture tool's Chrome driver and offline serving)

FOCUS_SELECTOR = ("a[href],button,input:not([type=hidden]),select,textarea,summary,[tabindex],"
                  "[contenteditable=true],audio[controls],video[controls]")

JS_SETUP = """(() => {
  window.__rules = {};
  for (const r of axe.getRules()) window.__rules[r.ruleId] = { enabled: true };
  window.__full = async () => {
    const t0 = performance.now();
    const r = await axe.run(document, { rules: window.__rules,
      resultTypes: ['violations', 'passes', 'incomplete', 'inapplicable'], xpath: false });
    return { ms: performance.now() - t0, violations: r.violations.length, incomplete: r.incomplete.length };
  };
  window.__edit = async () => {
    const t0 = performance.now();
    const el = document.querySelector('body *');
    if (!el) return null;
    el.setAttribute('data-bench', '1');
    await axe.run(document, { rules: window.__rules,
      resultTypes: ['violations', 'passes', 'incomplete', 'inapplicable'], xpath: false });
    return { ms: performance.now() - t0 };
  };
  window.__focus = async (cap, selector) => {
    const els = [...document.querySelectorAll(selector)].filter(e => !e.disabled && e.tabIndex >= 0).slice(0, cap);
    const t0 = performance.now();
    for (const e of els) {
      e.focus();
      await axe.run(document, { runOnly: { type: 'rule', values: ['color-contrast'] }, resultTypes: ['violations'] });
    }
    return { n: els.length, ms: performance.now() - t0 };
  };
  return axe.version;
})()"""


def evaluate_json(tab, expr, timeout):
    raw = tab.eval("(async () => JSON.stringify(await (%s)))()" % expr, await_promise=True, timeout=timeout)
    return json.loads(raw) if raw else None


def measure_page(tab, rec, args, axe_source):
    out = {"side": "axe-core", "sha256": rec["body_sha256"], "url": rec["url"]}
    deadline = time.monotonic() + args.page_timeout
    tab.set_viewport(1280, 800)
    tab.navigate(rec["url"], deadline, args.settle)
    out["nodes"] = tab.eval("document.getElementsByTagName('*').length")
    if tab.mode.startswith("emulation"):
        # axe waits on timers that do not run while script is disabled; the page has finished loading with it off.
        tab.send("Emulation.setScriptExecutionDisabled", {"value": False}, timeout=30.0)
    tab.eval(axe_source + "\n;void 0", timeout=60.0)
    out["axe_version"] = tab.eval(JS_SETUP)
    limit = args.axe_timeout

    def series(expr):
        evaluate_json(tab, expr, limit)  # warm-up
        values = []
        for _ in range(args.repeats):
            v = evaluate_json(tab, expr, limit)
            values.append(None if v is None else v["ms"])
        return None if any(v is None for v in values) else values

    t0 = time.monotonic()
    first = evaluate_json(tab, "window.__full()", limit)
    out["wall_ms"] = (time.monotonic() - t0) * 1000.0
    out["violations"], out["incomplete"] = first["violations"], first["incomplete"]
    out["full_ms"] = series("window.__full()")
    out["focus_n"] = 0
    focus = []
    expr_focus = "window.__focus(%d, %s)" % (args.focus_cap, json.dumps(FOCUS_SELECTOR))
    evaluate_json(tab, expr_focus, limit)
    for _ in range(args.repeats):
        v = evaluate_json(tab, expr_focus, limit)
        out["focus_n"] = v["n"]
        focus.append(v["ms"])
    out["focus_ms"] = focus
    # The edit mutates the page, so it runs last; repeated edits set the same attribute again.
    out["edit_ms"] = series("window.__edit()")
    return out


def cmd_run(args):
    capture.install_exit_handlers()
    chrome_default, axe_default, cft, axe_ver = capture.find_pins()
    chrome = args.chrome or chrome_default
    axe_path = args.axe or axe_default
    with open(axe_path, "rb") as f:
        axe_bytes = f.read()
    axe_source = axe_bytes.decode("utf-8")
    axe_sha = hashlib.sha256(axe_bytes).hexdigest()
    del axe_bytes

    wanted = []
    with open(args.ours, encoding="utf-8") as f:
        for line in f:
            if line.strip():
                wanted.append(json.loads(line)["sha256"])
    if args.limit:
        wanted = wanted[:args.limit]

    corpus = capture.Corpus(args.corpus)
    by_sha = {p["body_sha256"]: p for p in corpus.pages}
    work = os.path.join(os.path.dirname(os.path.abspath(args.out)), "axe-bench-work")
    os.makedirs(work, exist_ok=True)

    class A:  # the options launch_probe reads
        no_sandbox = args.no_sandbox
        script_mode = "auto"

    br, mode, no_sandbox, info, tried = capture.launch_probe(A, corpus, work, chrome, work)
    env = {"chrome": br.version.get("product"), "chrome_pinned": cft, "axe_pinned": axe_ver, "axe_sha256": axe_sha,
           "script_mode": mode, "no_sandbox": no_sandbox, "repeats": args.repeats, "focus_cap": args.focus_cap,
           "python": sys.version.split()[0]}
    done = 0
    with open(args.out, "w", encoding="utf-8") as out:
        out.write(json.dumps({"environment": env}) + "\n")
        for sha in wanted:
            rec = by_sha.get(sha)
            if rec is None:
                out.write(json.dumps({"side": "axe-core", "sha256": sha, "error": "not an HTML page of the corpus"}) + "\n")
                continue
            tab = None
            try:
                tab = br.tab()
                result = measure_page(tab, rec, args, axe_source)
                done += 1
            except (capture.CdpError, capture.CdpTimeout, capture.PageError, capture.PageTimeout, capture.ChromeDied,
                    json.JSONDecodeError, TypeError, KeyError) as e:
                result = {"side": "axe-core", "sha256": sha, "url": rec["url"], "error": "%s: %s" % (type(e).__name__, e)}
                if isinstance(e, capture.ChromeDied):
                    br.close()
                    br, mode, no_sandbox, info, tried = capture.launch_probe(A, corpus, work, chrome, work)
            finally:
                if tab is not None:
                    tab.close()
            out.write(json.dumps(result) + "\n")
            out.flush()
    br.close()
    print("measured %d of %d pages; environment %s" % (done, len(wanted), json.dumps(env)))
    return 0


def load(path):
    rows = {}
    env = None
    with open(path, encoding="utf-8") as f:
        for line in f:
            if not line.strip():
                continue
            r = json.loads(line)
            if "environment" in r:
                env = r["environment"]
            else:
                rows[r["sha256"]] = r
    return rows, env


def median(xs):
    return statistics.median(xs) if xs else None


def p95(xs):
    if not xs:
        return None
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(round(0.95 * (len(xs) - 1))))]


def cmd_compare(args):
    ours, _ = load(args.ours)
    axe, env = load(args.axe)
    shared = [s for s in ours if s in axe and "error" not in axe[s] and "error" not in ours[s]]
    errors = [s for s in axe if "error" in axe[s]]
    print("pages: %d compared, %d with an axe error, %d in the onehtml file" % (len(shared), len(errors), len(ours)))
    if env:
        print("axe %s, %s, script mode %s, %d repeats, focus cap %d"
              % (env["axe_pinned"], env["chrome"], env["script_mode"], env["repeats"], env["focus_cap"]))
    print("%-28s %12s %12s %12s %12s %9s" % ("per page, ms (median of repeats)", "ours median", "ours p95", "axe median", "axe p95", "axe/ours"))
    for label, key in (("full check", "full_ms"), ("re-check after one edit", "edit_ms"), ("focus overlays (page total)", "focus_ms")):
        a = [median(ours[s][key]) for s in shared if ours[s].get(key) and axe[s].get(key)]
        b = [median(axe[s][key]) for s in shared if ours[s].get(key) and axe[s].get(key)]
        if not a:
            print("%-28s no data" % label)
            continue
        ratio = sum(b) / sum(a) if sum(a) else float("inf")
        print("%-28s %12.2f %12.2f %12.2f %12.2f %8.1fx" % (label, median(a), p95(a), median(b), p95(b), ratio))
    fo = [(ours[s]["focus_n"], axe[s]["focus_n"]) for s in shared]
    print("focusables measured: ours %d, axe %d (cap %s)" % (sum(x for x, _ in fo), sum(y for _, y in fo), env and env["focus_cap"]))
    return 0


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run")
    r.add_argument("--corpus", required=True)
    r.add_argument("--ours", required=True, help="per-page file written by OneHtml.Benchmarks --check-per-page")
    r.add_argument("--out", required=True)
    r.add_argument("--repeats", type=int, default=3)
    r.add_argument("--focus-cap", type=int, default=20)
    r.add_argument("--limit", type=int, default=0)
    r.add_argument("--page-timeout", type=float, default=120.0)
    r.add_argument("--axe-timeout", type=float, default=120.0)
    r.add_argument("--settle", type=float, default=0.3)
    r.add_argument("--no-sandbox", choices=["auto", "always", "never"], default="auto")
    r.add_argument("--chrome")
    r.add_argument("--axe")
    c = sub.add_parser("compare")
    c.add_argument("ours")
    c.add_argument("axe")
    args = p.parse_args(argv)
    return cmd_run(args) if args.cmd == "run" else cmd_compare(args)


if __name__ == "__main__":
    sys.exit(main())
