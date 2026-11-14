#!/usr/bin/env bash
# End-to-end test on Linux: the real NeatShield proxy + a real Chromium + a local test origin. See README.md.
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
WORK=${E2E_WORK:-$(mktemp -d)}
PORT=${E2E_PROXY_PORT:-18877}
: "${NEAT_ADBLOCK_SO:?set NEAT_ADBLOCK_SO to the path of libneat_adblock.so (cargo build --release in neatbrowser/neat-adblock)}"

HOSTS="news.test ads.test pop.test bad.test bs.to"
ADDED_HOSTS=0
PIDS=()
cleanup() {
  for p in "${PIDS[@]:-}"; do [ -n "$p" ] && kill "$p" 2>/dev/null || true; done
  if [ "$ADDED_HOSTS" = 1 ]; then sed -i '/# neatshield-e2e$/d' /etc/hosts || true; fi
}
trap cleanup EXIT

echo "== host names"
if ! grep -q "news.test" /etc/hosts; then
  [ -w /etc/hosts ] || { echo "Cannot write /etc/hosts: run as root, or add '127.0.0.1 $HOSTS' yourself."; exit 2; }
  echo "127.0.0.1 $HOSTS # neatshield-e2e" >> /etc/hosts
  ADDED_HOSTS=1
fi

echo "== test certificates"
CERTS=$WORK/certs; mkdir -p "$CERTS"; cd "$CERTS"
openssl req -x509 -newkey rsa:2048 -nodes -keyout testca.key -out testca.pem -days 30 -subj "/CN=NeatShield e2e origin CA" 2>/dev/null
for h in news.test ads.test pop.test bs.to; do
  openssl req -newkey rsa:2048 -nodes -keyout "$h.key" -out "$h.csr" -subj "/CN=$h" 2>/dev/null
  printf "subjectAltName=DNS:%s\nbasicConstraints=CA:FALSE\n" "$h" > "$h.ext"
  openssl x509 -req -in "$h.csr" -CA testca.pem -CAkey testca.key -CAcreateserial -out "$h.pem" -days 30 -extfile "$h.ext" 2>/dev/null
done
# bad.test is self-signed: the proxy must refuse to pass it on.
openssl req -x509 -newkey rsa:2048 -nodes -keyout bad.test.key -out bad.test.pem -days 30 -subj "/CN=bad.test" -addext "subjectAltName=DNS:bad.test" 2>/dev/null

if [ -n "${E2E_CLI_DIR:-}" ]; then
  echo "== use the command-line app in $E2E_CLI_DIR"
  cp -r "$E2E_CLI_DIR" "$WORK/cli"
else
  echo "== build the command-line app"
  dotnet build "$ROOT/src/NeatShield.Cli" -c Release -o "$WORK/cli" -nologo -v q
fi
cp "$NEAT_ADBLOCK_SO" "$WORK/cli/libneat_adblock.so"
NS="$WORK/cli/neatshield"

export NEATSHIELD_DATA=$WORK/data
mkdir -p "$NEATSHIELD_DATA"
if [ -n "${E2E_FILTERS_DATA:-}" ]; then
  cp -r "$E2E_FILTERS_DATA/filters" "$NEATSHIELD_DATA/filters"   # reuse a data folder that already has the filters
else
  echo "== download the filters"
  "$NS" update
fi
cat > "$NEATSHIELD_DATA/settings.json" <<JSON
{ "Port": $PORT, "Language": "en",
  "CustomFilters": "||ads.test^\nnews.test##.promo\nnews.test##.post:has-text(Sponsored)\n" }
JSON

# The proxy checks the real certificates of the sites it talks to: let it trust the test origin CA.
export SSL_CERT_FILE=$CERTS/testca.pem

echo "== self-test"
"$NS" selftest --no-system-proxy

echo "== start origin and proxy"
(cd "$HERE" && E2E_CERTS=$CERTS node origin.js > "$WORK/origin.log" 2>&1) & PIDS+=($!)
"$NS" run --no-system-proxy > "$WORK/proxy.log" 2>&1 & PIDS+=($!)
for _ in $(seq 1 60); do grep -q "Protection is ON" "$WORK/proxy.log" 2>/dev/null && break; sleep 0.5; done
grep -q "Protection is ON" "$WORK/proxy.log" || { echo "The proxy did not start:"; cat "$WORK/proxy.log"; exit 1; }

echo "== curl checks"
openssl x509 -inform der -in "$NEATSHIELD_DATA/cert/NeatShield-Root-CA.cer" -out "$WORK/ns-ca.pem"
CURL=(curl -s -m 20 --proxy "http://127.0.0.1:$PORT" --cacert "$WORK/ns-ca.pem")
fail=0
check() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1 (got '$2', wanted '$3')"; fail=1; fi; }
check "HTTPS site opens through the proxy with the NeatShield certificate" "$("${CURL[@]}" -o /dev/null -w '%{http_code}' https://news.test:8443/json)" 200
check "site with a self-signed certificate is refused (not passed through)" "$("${CURL[@]}" -o /dev/null -w '%{http_code}' https://bad.test:8443/json)" 502
check "blocked host answers with the block page" "$("${CURL[@]}" -o /dev/null -w '%{http_code}' -H 'Accept: text/html' -H 'Sec-Fetch-Dest: document' https://ads.test:8443/)" 403
check "internal address answers" "$("${CURL[@]}" -o /dev/null -w '%{http_code}' https://neatshield.invalid/__ns/ping)" 200
check "internal address is not an open door" "$("${CURL[@]}" -o /dev/null -w '%{http_code}' https://neatshield.invalid/anything)" 404

echo "== browser checks"
cd "$HERE"
node -e "require('playwright')" 2>/dev/null || npm install --no-audit --no-fund
if [ "${E2E_SKIP_BROWSER_INSTALL:-0}" != 1 ]; then
  npx playwright install ${E2E_PLAYWRIGHT_WITH_DEPS:+--with-deps} chromium >/dev/null
fi
E2E_PROXY_PORT=$PORT node check.js || fail=1

[ "$fail" = 0 ] && echo "E2E: ALL OK" || { echo "E2E: FAILED (logs in $WORK)"; exit 1; }
