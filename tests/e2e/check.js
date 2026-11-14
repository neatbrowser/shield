// Drives a real Chromium through the NeatShield proxy and checks what the user would see.
// Run by run.sh (needs the proxy on 127.0.0.1:$E2E_PROXY_PORT, origin.js running and the hosts entries in place).
const { chromium } = require('playwright');
const http = require('http');

const PROXY = `http://127.0.0.1:${process.env.E2E_PROXY_PORT || 18877}`;
let failures = 0;
const check = (ok, what, extra = '') => { console.log(`${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : '  ' + extra}`); if (!ok) failures++; };

const originLog = () => new Promise((resolve, reject) => {
  http.get('http://127.0.0.1:8080/__log', r => { let b = ''; r.on('data', d => b += d); r.on('end', () => resolve(JSON.parse(b))); }).on('error', reject);
});

(async () => {
  const browser = await chromium.launch({ args: [`--proxy-server=${PROXY}`] });
  const ctx = await browser.newContext({ ignoreHTTPSErrors: true });

  // Same page served in several ways: plain, strict CSP header, CSP header without connect-src, gzip, brotli, chunked, CSP in a <meta> tag.
  for (const path of ['/', '/csp', '/csp-connect', '/gz', '/br', '/chunked', '/meta']) {
    const page = await ctx.newPage();
    await page.goto('https://news.test:8443' + path, { waitUntil: 'load' });
    await page.waitForTimeout(1500); // the page script asks the proxy about class/id rules
    const r = await page.evaluate(() => Object.fromEntries(['promo', 'p1', 'p2', 'g1', 'plain'].map(id => [id, getComputedStyle(document.getElementById(id)).display === 'none' ? 'hidden' : 'shown'])));
    check(r.promo === 'hidden', `${path}: element hidden by a user CSS rule`, JSON.stringify(r));
    check(r.p1 === 'hidden' && r.p2 === 'shown', `${path}: ":has-text(Sponsored)" hides only the sponsored post`, JSON.stringify(r));
    check(r.g1 === 'hidden', `${path}: class/id rule answered by the proxy hides .adsbygoogle`, JSON.stringify(r));
    check(r.plain === 'shown', `${path}: other elements untouched`, JSON.stringify(r));
    await page.close();
  }

  const seen = await originLog();
  check(!seen.some(l => l.includes('ads.test')), 'blocked host never reached the origin', seen.filter(l => l.includes('ads.test')).join(', '));

  // Pop-ups: the rule "*$document,3p,domain=bs.to|..." comes from the real filter lists.
  let page = await ctx.newPage();
  await page.goto('https://bs.to:8443/opener', { waitUntil: 'load' });
  let waitPopup = ctx.waitForEvent('page', { timeout: 10000 });
  await page.click('#pl2');
  let popup = await waitPopup;
  let closed = false; popup.on('close', () => closed = true);
  await popup.waitForLoadState('domcontentloaded').catch(() => {});
  await new Promise(r => setTimeout(r, 1500));
  check(closed || popup.isClosed(), 'pop-up opened by a listed site is blocked and closes itself');

  page = await ctx.newPage();
  await page.goto('https://news.test:8443/', { waitUntil: 'load' });
  await page.evaluate(() => { document.getElementById('pl').href = 'https://pop.test:8443/landing'; });
  waitPopup = ctx.waitForEvent('page', { timeout: 10000 });
  await page.click('#pl');
  popup = await waitPopup;
  await popup.waitForLoadState('domcontentloaded').catch(() => {});
  check((await popup.title()) === 'landing-ok', 'same link from a site that is not listed opens normally');

  page = await ctx.newPage();
  await page.goto('https://bs.to:8443/opener', { waitUntil: 'load' });
  await page.click('#same');
  await page.waitForLoadState('domcontentloaded').catch(() => {});
  check((await page.title()).startsWith('Pop-up blocked'), 'same-tab click from a listed site shows the pop-up page');

  // Things that must keep working through the proxy.
  page = await ctx.newPage();
  await page.goto('https://news.test:8443/json');
  const ws = await page.evaluate(() => new Promise(res => {
    const got = []; const s = new WebSocket('wss://news.test:8443/ws');
    s.onmessage = e => { got.push(e.data); if (got.length === 1) s.send('ping'); if (got.length === 2) { s.close(); res(got); } };
    s.onerror = () => res(['error']); setTimeout(() => res(['timeout', ...got]), 8000);
  }));
  check(JSON.stringify(ws) === '["hello-from-server","echo:ping"]', 'WebSocket works through the proxy', JSON.stringify(ws));
  const sse = await page.evaluate(() => new Promise(res => { const t = []; const t0 = performance.now(); const es = new EventSource('/sse'); es.onmessage = () => { t.push(performance.now() - t0); if (t.length === 5) { es.close(); res(t); } }; es.onerror = () => { es.close(); res(['error']); }; }));
  check(sse.length === 5 && sse[4] - sse[0] > 800, 'server-sent events stream (not held back until the end)', JSON.stringify(sse));
  const post = await page.evaluate(async () => (await fetch('/post', { method: 'POST', body: new Uint8Array(5 * 1024 * 1024) })).text());
  check(post === 'got 5242880', '5 MB POST body arrives whole', post);

  await browser.close();
  console.log(failures === 0 ? 'ALL OK' : `${failures} FAILED`);
  process.exit(failures === 0 ? 0 : 1);
})().catch(e => { console.error(e); process.exit(2); });
