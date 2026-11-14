// Test origin for the end-to-end run (see run.sh): a few pages on several host names, HTTP on 8080 and HTTPS on 8443.
const https = require('https'), http = require('http'), fs = require('fs'), zlib = require('zlib'), tls = require('tls');
const dir = (process.env.E2E_CERTS || __dirname + '/certs') + '/';
const ctx = h => tls.createSecureContext({ key: fs.readFileSync(dir + h + '.key'), cert: fs.readFileSync(dir + h + '.pem') });
const ctxs = Object.fromEntries(['news.test', 'ads.test', 'pop.test', 'bad.test', 'bs.to'].map(h => [h, ctx(h)]));
const log = [];
const page = `<!doctype html><html><head><title>News</title></head><body>
<div class="promo" id="promo">PROMO</div>
<div class="post" id="p1">Sponsored: buy now</div>
<div class="post" id="p2">Real article</div>
<div class="adsbygoogle" id="g1">GENERIC AD</div>
<img id="px" src="https://ads.test:8443/pixel.png">
<a id="pl" target="_blank" href="https://adsession.com:8443/go">pop</a>
<p id="plain">hello</p>
<script>fetch('https://ads.test:8443/api').then(r=>r.text()).then(t=>{document.title='fetched:'+t},e=>{document.title='blocked'})</script>
</body></html>`;
function handler(req, res) {
  const host = (req.headers.host || '').split(':')[0];
  log.push(req.method + ' ' + host + req.url);
  const u = req.url.split('?')[0];
  if (u === '/__log') { res.end(JSON.stringify(log)); return; }
  if (u === '/pixel.png') { res.writeHead(200, {'content-type':'image/png'}); res.end(Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==','base64')); return; }
  if (u === '/api') { res.writeHead(200, {'content-type':'text/plain','access-control-allow-origin':'*'}); res.end('ok'); return; }
  if (u === '/csp') { res.writeHead(200, {'content-type':'text/html; charset=utf-8','content-security-policy':"default-src 'self'; script-src 'self'; style-src 'self'; img-src * data:"}); res.end(page); return; }
  if (u === '/csp-connect') { res.writeHead(200, {'content-type':'text/html; charset=utf-8','content-security-policy':"default-src 'none'; connect-src 'self'; img-src *"}); res.end(page); return; }
  if (u === '/gz') { res.writeHead(200, {'content-type':'text/html; charset=utf-8','content-encoding':'gzip'}); res.end(zlib.gzipSync(page)); return; }
  if (u === '/br') { res.writeHead(200, {'content-type':'text/html; charset=utf-8','content-encoding':'br'}); res.end(zlib.brotliCompressSync(page)); return; }
  if (u === '/chunked') { res.writeHead(200, {'content-type':'text/html; charset=utf-8'}); const parts = page.match(/[\s\S]{1,40}/g); let i = 0; const t = setInterval(() => { if (i < parts.length) res.write(parts[i++]); else { clearInterval(t); res.end(); } }, 5); return; }
  if (u === '/opener') { res.writeHead(200, {'content-type':'text/html; charset=utf-8'}); res.end('<!doctype html><title>opener</title><a id="pl2" target="_blank" href="https://pop.test:8443/landing">new tab</a> <a id="same" href="https://pop.test:8443/landing">same tab</a>'); return; }
  if (u === '/landing') { res.writeHead(200, {'content-type':'text/html; charset=utf-8'}); res.end('<!doctype html><title>landing-ok</title>landing'); return; }
  if (u === '/big') { res.writeHead(200, {'content-type':'application/octet-stream'}); const chunk = Buffer.alloc(1024*1024, 97); let n = 0; const w = () => { while (n < 300) { n++; if (!res.write(chunk)) { res.once('drain', w); return; } } res.end(); }; w(); return; }
  if (u === '/sse') { res.writeHead(200, {'content-type':'text/event-stream','cache-control':'no-cache'}); let i = 0; const t = setInterval(() => { res.write('data: tick' + (++i) + '\n\n'); if (i >= 5) { clearInterval(t); res.end(); } }, 300); return; }
  if (u === '/post') { let n = 0; req.on('data', d => n += d.length); req.on('end', () => { res.writeHead(200, {'content-type':'text/plain'}); res.end('got ' + n); }); return; }
  if (u === '/meta') { res.writeHead(200, {'content-type':'text/html; charset=utf-8'}); res.end(page.replace('<head>', '<head><meta http-equiv="Content-Security-Policy" content="default-src &#39;self&#39;; script-src &#39;self&#39;; style-src &#39;self&#39;; img-src *">')); return; }
  if (u === '/json') { res.writeHead(200, {'content-type':'application/json'}); res.end('{"a":1}'); return; }
  if (u === '/redirect') { res.writeHead(302, {location: '/'}); res.end(); return; }
  res.writeHead(200, {'content-type': 'text/html; charset=utf-8'}); res.end(page);
}
https.createServer({ SNICallback: (name, cb) => cb(null, ctxs[name] || ctxs['news.test']), key: fs.readFileSync(dir + 'news.test.key'), cert: fs.readFileSync(dir + 'news.test.pem') }, handler).on('upgrade', onUpgrade).listen(8443, '127.0.0.1');
const crypto = require('crypto');
function onUpgrade(req, socket) {
  const key = req.headers['sec-websocket-key'];
  const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
  socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + accept + '\r\n\r\n');
  const send = (txt) => { const b = Buffer.from(txt); socket.write(Buffer.concat([Buffer.from([0x81, b.length]), b])); };
  send('hello-from-server');
  socket.on('data', d => { // client frames are masked
    const len = d[1] & 0x7f; const mask = d.slice(2, 6); const payload = d.slice(6, 6 + len);
    const out = Buffer.alloc(len); for (let i = 0; i < len; i++) out[i] = payload[i] ^ mask[i % 4];
    send('echo:' + out.toString());
  });
}
http.createServer(handler).listen(8080, '127.0.0.1');

console.log('origin up');
