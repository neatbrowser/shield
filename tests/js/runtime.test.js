// Run: cd tests/js && npm install && npm test
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const source = fs.readFileSync(path.join(__dirname, '../../src/NeatShield.Core/Proxy/Assets/runtime.js'), 'utf8');

function page(html, cfg, opts = {}) {
  const dom = new JSDOM(html, { url: opts.url || 'https://example.org/article?x=1', runScripts: 'outside-only', pretendToBeVisual: true });
  const w = dom.window;
  if (opts.fetch) w.fetch = opts.fetch;
  w.eval('(' + source + ')(' + JSON.stringify(cfg) + ')');
  return dom;
}
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const hidden = (el) => el.style.display === 'none';
const op = (type, arg) => ({ type, arg });

test('has-text hides only elements containing the text', async () => {
  const dom = page('<div id=a class=x>Sponsored post</div><div id=b class=x>News</div>',
    { rules: [{ selector: [op('css-selector', 'div.x'), op('has-text', '"Sponsored"')] }] });
  await wait(250);
  const d = dom.window.document;
  assert.ok(hidden(d.getElementById('a')));
  assert.ok(!hidden(d.getElementById('b')));
});

test('has-text with regex', async () => {
  const dom = page('<span id=a>Promoted Post</span><span id=b>Post</span>',
    { rules: [{ selector: [op('css-selector', 'span'), op('has-text', '/^(?:Promoted Post|Gesponsert)$/')] }] });
  await wait(250);
  const d = dom.window.document;
  assert.ok(hidden(d.getElementById('a')));
  assert.ok(!hidden(d.getElementById('b')));
});

test('upward by number and by selector', async () => {
  const dom = page('<section id=s1><div><p class=ad>x</p></div></section><section id=s2 class=card><div><p class=ad2>y</p></div></section>',
    { rules: [
      { selector: [op('css-selector', 'p.ad'), op('upward', '2')] },
      { selector: [op('css-selector', 'p.ad2'), op('upward', '.card')] },
    ] });
  await wait(250);
  const d = dom.window.document;
  assert.ok(hidden(d.getElementById('s1')));
  assert.ok(hidden(d.getElementById('s2')));
});

test('matches-attr and matches-path', async () => {
  const dom = page('<a id=a href="/go.php?x" data-t="ad-1">a</a><a id=b href="/ok">b</a>',
    { rules: [
      { selector: [op('matches-path', '/article'), op('css-selector', 'a'), op('matches-attr', '"data-t"="ad-*"')] },
      { selector: [op('matches-path', '/other'), op('css-selector', 'a#b')] },
    ] });
  await wait(250);
  const d = dom.window.document;
  assert.ok(hidden(d.getElementById('a')));
  assert.ok(!hidden(d.getElementById('b')), 'path did not match, rule must not apply');
});

test('actions: remove, remove-attr, remove-class, style', async () => {
  const dom = page('<i id=r>r</i><a id=ra href="/x" onclick="y()">a</a><b id=rc class="keep bad">c</b><u id=st>s</u>',
    { rules: [
      { selector: [op('css-selector', '#r')], action: { type: 'remove' } },
      { selector: [op('css-selector', '#ra')], action: { type: 'remove-attr', arg: 'onclick' } },
      { selector: [op('css-selector', '#rc')], action: { type: 'remove-class', arg: 'bad' } },
      { selector: [op('css-selector', '#st'), op('min-text-length', '1')], action: { type: 'style', arg: 'color: red !important; margin: 0' } },
    ] });
  await wait(250);
  const d = dom.window.document;
  assert.equal(d.getElementById('r'), null);
  assert.equal(d.getElementById('ra').hasAttribute('onclick'), false);
  assert.ok(d.getElementById('ra').hasAttribute('href'));
  assert.equal(d.getElementById('rc').className, 'keep');
  assert.equal(d.getElementById('st').style.getPropertyValue('color'), 'red');
  assert.equal(d.getElementById('st').style.getPropertyPriority('color'), 'important');
  assert.equal(d.getElementById('st').style.getPropertyValue('margin'), '0px');
});

test('xpath', async () => {
  const dom = page('<div id=a><span>Ad</span></div><div id=b><span>Ok</span></div>',
    { rules: [{ selector: [op('xpath', '//div[span="Ad"]')] }] });
  await wait(250);
  const d = dom.window.document;
  assert.ok(hidden(d.getElementById('a')));
  assert.ok(!hidden(d.getElementById('b')));
});

test('elements added later are handled (MutationObserver)', async () => {
  const dom = page('<div id=root></div>', { rules: [{ selector: [op('css-selector', '.late'), op('has-text', 'Ad')] }] });
  await wait(200);
  const d = dom.window.document;
  d.getElementById('root').innerHTML = '<p id=p class=late>Ad here</p>';
  await wait(400);
  assert.ok(hidden(d.getElementById('p')));
});

test('unknown operator and invalid selector do nothing and do not throw', async () => {
  const dom = page('<div id=a>x</div>',
    { rules: [
      { selector: [op('css-selector', 'div'), op('totally-new-op', 'z')] },
      { selector: [op('css-selector', 'div[[')] },
      { selector: [op('css-selector', '#a'), op('has-text', 'x')] },
    ] });
  await wait(250);
  assert.ok(hidden(dom.window.document.getElementById('a')), 'later valid rule still runs');
});

test('generic: sends classes and ids once, hides what the proxy answers', async () => {
  const calls = [];
  const fetch = (url, init) => {
    calls.push({ url, body: JSON.parse(init.body) });
    return Promise.resolve({ ok: true, json: () => Promise.resolve(['.adbox', '#sponsor']) });
  };
  const dom = page('<div class="adbox wide" id=sponsor>x</div><p class=plain>y</p>',
    { rules: [], generic: true, endpoint: 'https://neatshield.invalid/__ns/hidden', exceptions: ['.ok'], nonce: 'N' }, { fetch });
  await wait(400);
  const d = dom.window.document;
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, 'https://neatshield.invalid/__ns/hidden');
  assert.deepEqual(calls[0].body.classes.sort(), ['adbox', 'plain', 'wide']);
  assert.deepEqual(calls[0].body.ids, ['sponsor']);
  assert.deepEqual(calls[0].body.exceptions, ['.ok']);
  const style = [...d.querySelectorAll('style')].find((s) => s.textContent.includes('.adbox'));
  assert.ok(style, 'fallback <style> created (jsdom has no adoptedStyleSheets)');
  assert.equal(style.getAttribute('nonce'), 'N');
  // new class later -> only the new one is sent
  d.body.insertAdjacentHTML('beforeend', '<div class="newclass">z</div>');
  await wait(500);
  assert.equal(calls.length, 2);
  assert.deepEqual(calls[1].body.classes, ['newclass']);
});

test('generic stops asking after repeated failures', async () => {
  let n = 0;
  const fetch = () => { n++; return Promise.reject(new Error('blocked')); };
  const dom = page('<div class=c0></div>', { rules: [], generic: true, endpoint: 'https://x.invalid/h' }, { fetch });
  const d = dom.window.document;
  for (let i = 1; i <= 6; i++) { d.body.insertAdjacentHTML('beforeend', `<div class="c${i}"></div>`); await wait(350); }
  assert.equal(n, 3);
});

test('runs once per window', async () => {
  const dom = page('<div id=a>x</div>', { rules: [] });
  const w = dom.window;
  assert.equal(w.__neatShieldRuntime, true);
  assert.doesNotThrow(() => w.eval('(' + source + ')({rules:[]})'));
});
