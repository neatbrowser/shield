# End-to-end test (Linux)

Starts the **real** NeatShield proxy (the command-line app, with the real filter lists and the real Titanium proxy
library), a small test web site, and a **real Chromium** that browses through the proxy. It checks what a person would see:

* the element-hiding rules, `:has-text(...)` and the class/id rules answered by the proxy hide the right elements,
  on pages served plainly, with a strict CSP header, with a CSP header that has no `connect-src`, with a CSP in a
  `<meta>` tag, gzip, brotli and chunked;
* pop-ups from a listed site are blocked, the same link from another site opens;
* WebSocket, server-sent events and a 5 MB POST work through the proxy;
* a site with a self-signed certificate is refused, the block page and the internal address behave;
* `neatshield selftest` passes.

## Run it

You need: Linux, root (it adds `news.test ads.test pop.test bad.test bs.to` to `/etc/hosts` and removes them again),
the .NET 10 SDK, Node.js 22, `openssl`, `curl`, and `libneat_adblock.so` built from
<https://github.com/neatbrowser/neat-adblock> (`cargo build --release`).

```bash
export NEAT_ADBLOCK_SO=/path/to/neat-adblock/target/release/libneat_adblock.so
sudo -E env "PATH=$PATH" bash tests/e2e/run.sh
```

Or on GitHub: *Actions → e2e (manual) → Run workflow* (that workflow has not been run yet; if it fails on a missing tool,
fix the workflow, not the test).

Useful variables: `E2E_CLI_DIR` (use an already built command-line app), `E2E_FILTERS_DATA` (a NeatShield data folder
that already has the filters, instead of downloading them), `E2E_WORK` (keep the logs in this folder),
`E2E_PROXY_PORT`, `E2E_SKIP_BROWSER_INSTALL=1`.

The test does not touch the Windows proxy setting or any real site: everything stays on 127.0.0.1.
