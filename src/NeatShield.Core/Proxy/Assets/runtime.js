// NeatShield page runtime. Evaluated as a function expression and called with a config object:
//   { rules: [{selector:[{type,arg}...], action?:{type,arg?}}], generic: bool, endpoint: string, exceptions: [], nonce: string }
// Does two jobs the CSS rules from the engine cannot do:
//   1. Procedural rules (:has-text, :upward, :matches-attr, :xpath, ...) and rule actions (remove, remove-attr, ...).
//   2. Generic class/id hiding: sends the classes and ids seen on the page to the local proxy, which answers with selectors.
(function (cfg) {
  'use strict';
  var win = window, doc = document;
  if (win.__neatShieldRuntime) return;
  try { Object.defineProperty(win, '__neatShieldRuntime', { value: true }); } catch (e) { return; }

  var MIN_WAIT = 150;

  // ---------- small helpers ----------
  function toArray(list) { return Array.prototype.slice.call(list); }
  function escapeRe(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }
  function unquote(s) {
    s = String(s).trim();
    if (s.length >= 2 && ((s[0] === '"' && s[s.length - 1] === '"') || (s[0] === "'" && s[s.length - 1] === "'"))) {
      return s.slice(1, -1);
    }
    return s;
  }
  function asRegExp(s) {
    var m = /^\/((?:\\.|[^\/\\])+)\/([a-z]*)$/.exec(String(s).trim());
    if (!m) return null;
    try { return new RegExp(m[1], m[2]); } catch (e) { return null; }
  }
  // /regex/ or glob with *; a plain string must match whole.
  function pattern(s) {
    var re = asRegExp(s);
    if (re) return function (v) { return re.test(v); };
    var p = unquote(s);
    if (p.indexOf('*') < 0) return function (v) { return v === p; };
    var rx = new RegExp('^' + p.split('*').map(escapeRe).join('.*') + '$');
    return function (v) { return rx.test(v); };
  }

  // ---------- operators ----------
  function hasText(arg) {
    var re = asRegExp(arg);
    var text = re ? null : unquote(arg);
    return function (el) {
      var t = el.textContent || '';
      return re ? re.test(t) : t.indexOf(text) !== -1;
    };
  }

  function matchesCss(arg, pseudo) {
    var i = arg.indexOf(':');
    if (i < 0) return function () { return false; };
    var prop = arg.slice(0, i).trim().toLowerCase();
    var want = pattern(arg.slice(i + 1));
    return function (el) {
      var v;
      try { v = win.getComputedStyle(el, pseudo || null).getPropertyValue(prop); } catch (e) { return false; }
      if (v == null) return false;
      return want(v) || (prop === 'content' && want(unquote(v)));
    };
  }

  function matchesAttr(arg) {
    var m = /^\s*("[^"]*"|'[^']*'|\/(?:\\.|[^\/\\])+\/[a-z]*|[^=]+?)\s*(?:=\s*(.*?))?\s*$/.exec(arg);
    if (!m) return function () { return false; };
    var nameOk = pattern(m[1]);
    var valueOk = m[2] === undefined ? null : pattern(m[2]);
    return function (el) {
      var attrs = el.attributes;
      for (var i = 0; i < attrs.length; i++) {
        if (nameOk(attrs[i].name) && (valueOk === null || valueOk(attrs[i].value))) return true;
      }
      return false;
    };
  }

  function matchPath(arg) {
    var path = win.location.pathname + win.location.search;
    var re = asRegExp(arg);
    if (re) return re.test(path);
    var neg = arg.charAt(0) === '!';
    var needle = neg ? arg.slice(1) : arg;
    var hit = path.indexOf(needle) !== -1;
    return neg ? !hit : hit;
  }

  function upward(arg) {
    var n = /^\s*\d+\s*$/.test(arg) ? parseInt(arg, 10) : -1;
    return function (el) {
      if (n >= 0) {
        var e = el;
        for (var i = 0; i < n && e; i++) e = e.parentElement;
        return e && e !== el ? e : null;
      }
      var p = el.parentElement;
      try { return p ? p.closest(arg) : null; } catch (e2) { return null; }
    };
  }

  function xpathAll(expr, ctx) {
    var out = [];
    var snap = doc.evaluate(expr, ctx, null, 7 /* ORDERED_NODE_SNAPSHOT_TYPE */, null);
    for (var i = 0; i < snap.snapshotLength; i++) {
      var n = snap.snapshotItem(i);
      if (n && n.nodeType === 1) out.push(n);
      else if (n && n.parentElement) out.push(n.parentElement);
    }
    return out;
  }

  function pushUnique(out, seen, el) {
    if (el && !seen.has(el)) { seen.add(el); out.push(el); }
  }

  // Elements a rule's selector chain finds right now.
  function evaluate(rule) {
    var ops = rule.selector, cur = null, i, j;
    for (i = 0; i < ops.length; i++) {
      var t = ops[i].type, a = ops[i].arg == null ? '' : String(ops[i].arg);
      if (t === 'matches-path') {
        if (!matchPath(a)) return [];
        continue;
      }
      if (t === 'css-selector') {
        if (cur === null) { cur = toArray(doc.querySelectorAll(a)); continue; }
        var next = [], seen = new Set();
        for (j = 0; j < cur.length; j++) {
          toArray(cur[j].querySelectorAll(':scope ' + a)).forEach(function (el) { pushUnique(next, seen, el); });
        }
        cur = next;
        continue;
      }
      if (t === 'xpath') {
        if (cur === null) { cur = xpathAll(a, doc); continue; }
        var xs = [], xseen = new Set();
        for (j = 0; j < cur.length; j++) xpathAll(a, cur[j]).forEach(function (el) { pushUnique(xs, xseen, el); });
        cur = xs;
        continue;
      }
      if (cur === null) cur = toArray(doc.querySelectorAll('*'));
      if (t === 'upward') {
        var up = upward(a), ups = [], useen = new Set();
        cur.forEach(function (el) { pushUnique(ups, useen, up(el)); });
        cur = ups;
        continue;
      }
      var test;
      if (t === 'has-text') test = hasText(a);
      else if (t === 'matches-attr') test = matchesAttr(a);
      else if (t === 'matches-css') test = matchesCss(a, null);
      else if (t === 'matches-css-before') test = matchesCss(a, '::before');
      else if (t === 'matches-css-after') test = matchesCss(a, '::after');
      else if (t === 'min-text-length') { var min = parseInt(a, 10) || 0; test = function (el) { return (el.textContent || '').length >= min; }; }
      else return []; // an operator we do not know: do nothing rather than hide something wrong
      cur = cur.filter(test);
    }
    return cur || [];
  }

  // ---------- actions ----------
  function setStyle(el, css) {
    String(css).split(';').forEach(function (decl) {
      var k = decl.indexOf(':');
      if (k < 0) return;
      var prop = decl.slice(0, k).trim();
      var val = decl.slice(k + 1).trim();
      var important = /\s*!important\s*$/i.test(val);
      if (important) val = val.replace(/\s*!important\s*$/i, '');
      if (prop) el.style.setProperty(prop, val, important ? 'important' : '');
    });
  }

  function act(el, action) {
    if (!action) { el.style.setProperty('display', 'none', 'important'); return; }
    switch (action.type) {
      case 'remove': if (el.parentNode) el.parentNode.removeChild(el); break;
      case 'style': setStyle(el, action.arg); break;
      case 'remove-attr': el.removeAttribute(action.arg); break;
      case 'remove-class': el.classList.remove(action.arg); break;
    }
  }

  var rules = (cfg.rules || []).map(function (r) { return { rule: r, done: new WeakSet() }; });

  function runRules() {
    for (var i = 0; i < rules.length; i++) {
      var entry = rules[i], found;
      try { found = evaluate(entry.rule); } catch (e) { continue; }
      for (var j = 0; j < found.length; j++) {
        var el = found[j];
        var a = entry.rule.action;
        // remove-attr / remove-class are cheap and idempotent; the others are done once per element.
        if (entry.done.has(el)) continue;
        entry.done.add(el);
        try { act(el, a); } catch (e2) { /* keep going */ }
      }
    }
  }

  // ---------- generic class / id hiding ----------
  var seenClasses = new Set(), seenIds = new Set();
  var inFlight = false, failures = 0;
  var genericOn = !!cfg.generic && !!cfg.endpoint && typeof win.fetch === 'function';

  function addCss(selectors) {
    var text = '';
    for (var i = 0; i < selectors.length; i++) {
      if (typeof selectors[i] === 'string') text += selectors[i] + '{display:none!important}\n';
    }
    if (!text) return;
    try {
      var sheet = new win.CSSStyleSheet();
      sheet.replaceSync(text);
      doc.adoptedStyleSheets = doc.adoptedStyleSheets.concat([sheet]);
    } catch (e) {
      var st = doc.createElement('style');
      if (cfg.nonce) st.setAttribute('nonce', cfg.nonce);
      st.textContent = text;
      (doc.head || doc.documentElement).appendChild(st);
    }
  }

  function runGeneric() {
    if (!genericOn || inFlight) return;
    var classes = [], ids = [];
    var els = doc.querySelectorAll('[class],[id]');
    for (var i = 0; i < els.length && classes.length + ids.length < 2000; i++) {
      var el = els[i];
      if (el.id && !seenIds.has(el.id)) { seenIds.add(el.id); ids.push(el.id); }
      var cl = el.classList;
      if (cl) for (var k = 0; k < cl.length; k++) {
        if (!seenClasses.has(cl[k])) { seenClasses.add(cl[k]); classes.push(cl[k]); }
      }
    }
    if (!classes.length && !ids.length) return;
    inFlight = true;
    win.fetch(cfg.endpoint, {
      method: 'POST',
      body: JSON.stringify({ classes: classes, ids: ids, exceptions: cfg.exceptions || [] }),
      credentials: 'omit', cache: 'no-store', referrerPolicy: 'no-referrer'
    }).then(function (r) {
      if (!r.ok) throw new Error('status ' + r.status);
      return r.json();
    }).then(function (selectors) {
      inFlight = false;
      if (Array.isArray(selectors)) addCss(selectors);
      schedule(); // classes may have arrived while we waited
    }).catch(function () {
      inFlight = false;
      if (++failures >= 3) genericOn = false; // blocked by the page's policy or the proxy is gone: stop asking
    });
  }

  // ---------- scheduling ----------
  var scheduled = false, wait = MIN_WAIT;
  function run() {
    scheduled = false;
    var t0 = Date.now();
    try { runRules(); } catch (e) { /* never break the page */ }
    try { runGeneric(); } catch (e2) { /* same */ }
    wait = Math.max(MIN_WAIT, (Date.now() - t0) * 4); // heavy pages are visited less often
  }
  function schedule() {
    if (scheduled) return;
    scheduled = true;
    win.setTimeout(run, wait);
  }

  try {
    new win.MutationObserver(schedule).observe(doc.documentElement, {
      childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class', 'id']
    });
  } catch (e) { /* no observer: the runs below still happen */ }
  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', run, { once: true });
  schedule();
  win.addEventListener('load', run, { once: true });
})
