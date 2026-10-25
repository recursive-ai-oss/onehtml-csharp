# Pins

Pinned black-box references for the accessibility differentials, installed user-locally and never committed. Python 3.13, standard library only.

`pins.json` names the pinned versions and checksums:

- **Chrome for Testing** 156.0.8078.47 (stable when pinned), `linux64` build plus the headless shell, from the official Chrome for Testing downloads.
- **axe-core** 4.14.0, the npm release tarball, checked against the registry's SHA-512 integrity value and its SHA-1, and against a SHA-256 recorded here. The package is MPL-2.0. Only `axe.min.js` and `LICENSE` are unpacked, and `axe.min.js` is treated as an opaque file that is injected into a page and run; its contents are never read.

## Usage

```
python3 install.py              # install or verify under ~/onehtml/pins/
python3 install.py --record     # first time for a new pin: also write the SHA-256s into pins.json
python3 install.py --latest     # show the current stable Chrome and latest axe-core, to choose a new pin
python3 smoke.py                # prove the pins work
```

Layout: `~/onehtml/pins/chrome-for-testing-<version>/{chrome-linux64,chrome-headless-shell-linux64}/` and `~/onehtml/pins/axe-core-<version>/{axe.min.js,LICENSE}`.

`smoke.py` starts headless Chrome with `--remote-debugging-pipe` (the DevTools protocol over a pipe, no network port), loads a small local HTML file, calls `Accessibility.getFullAXTree`, injects `axe.min.js` with `Runtime.evaluate`, runs `axe.run()` and prints the node roles, names and the violation counts. It uses `--no-sandbox` because it only ever opens a local file we wrote; the real differential harness opens crawled pages and must not.

To bump a pin: `--latest`, edit the versions and URLs in `pins.json`, blank the `sha256` fields, run `install.py --record` and `smoke.py`, then re-run the differentials and record what changed.
