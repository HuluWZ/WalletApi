#!/usr/bin/env bash
# End-to-end check: register two users, deposit, transfer, replay the transfer, verify balances.
set -euo pipefail
B=${1:-http://localhost:8080}
j() { python3 -c "import sys,json;print(json.load(sys.stdin)$1)"; }
tok() { curl -sf -X POST $B/api/auth/register -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"password123\"}" | j "['token']"; }
S=$RANDOM
T1=$(tok a$S@x.com); T2=$(tok b$S@x.com)
A=$(curl -sf $B/api/accounts -H "Authorization: Bearer $T1" | j "[0]['id']")
C=$(curl -sf $B/api/accounts -H "Authorization: Bearer $T2" | j "[0]['id']")
curl -sf -X POST $B/api/accounts/$A/deposit -H "Authorization: Bearer $T1" -H 'Content-Type: application/json' -d '{"amount":1000}' >/dev/null
for i in 1 2; do  # same key twice -> applied once
  curl -sf -X POST $B/api/accounts/transfers -H "Authorization: Bearer $T1" -H 'Idempotency-Key: k-'$S -H 'Content-Type: application/json' \
    -d "{\"fromAccountId\":\"$A\",\"toAccountId\":\"$C\",\"amount\":300}" >/dev/null
done
BAL=$(curl -sf $B/api/accounts -H "Authorization: Bearer $T1" | j "[0]['balance']")
[ "$BAL" = "700" ] || { echo "SMOKE FAIL: balance $BAL, expected 700"; exit 1; }

# scheduled payment: due in 3s, worker (2s tick) must pick it up and send exactly once (daily, so not again)
curl -sf -X POST $B/api/scheduled-transfers -H "Authorization: Bearer $T1" -H 'Content-Type: application/json' \
  -d "{\"fromAccountId\":\"$A\",\"toAccountId\":\"$C\",\"amount\":100,\"interval\":\"Daily\",\"firstRunAt\":\"$(python3 -c "import datetime as d;print((d.datetime.utcnow()+d.timedelta(seconds=3)).strftime('%Y-%m-%dT%H:%M:%SZ'))")\"}" >/dev/null
sleep 9
BAL=$(curl -sf $B/api/accounts -H "Authorization: Bearer $T1" | j "[0]['balance']")
[ "$BAL" = "600" ] || { echo "SMOKE FAIL: balance $BAL, expected 600"; exit 1; }

# swagger + credit score
curl -sf $B/swagger/v1/swagger.json >/dev/null || { echo "SMOKE FAIL: swagger"; exit 1; }
SCORE=$(curl -sf $B/api/credit/score -H "Authorization: Bearer $T1" | j "['score']")
[ "$SCORE" -ge 300 ] && [ "$SCORE" -le 850 ] || { echo "SMOKE FAIL: score $SCORE"; exit 1; }

# compliance: a large deposit must be flagged and visible to the regulator only
curl -sf -X POST $B/api/accounts/$A/deposit -H "Authorization: Bearer $T1" -H 'Content-Type: application/json' -d '{"amount":5000000}' >/dev/null
code=$(curl -s -o /dev/null -w '%{http_code}' $B/api/compliance/flags -H "Authorization: Bearer $T1")
[ "$code" = "403" ] || { echo "SMOKE FAIL: customer got $code on regulator endpoint"; exit 1; }
RT=$(curl -sf -X POST $B/api/auth/login -H 'Content-Type: application/json' -d '{"email":"regulator@wallet.local","password":"Regulator#12345"}' | j "['token']")
N=$(curl -sf $B/api/compliance/flags -H "Authorization: Bearer $RT" | python3 -c "import sys,json;print(sum(1 for f in json.load(sys.stdin) if f['rule']=='LargeTransaction'))")
[ "$N" -ge 1 ] || { echo "SMOKE FAIL: no large-transaction flag"; exit 1; }
curl -sf $B/api/compliance/network -H "Authorization: Bearer $RT" >/dev/null && curl -sf $B/api/compliance/stats -H "Authorization: Bearer $RT" >/dev/null || { echo "SMOKE FAIL: graph endpoints"; exit 1; }
curl -sf $B/dashboard.html >/dev/null || { echo "SMOKE FAIL: dashboard"; exit 1; }
echo "SMOKE OK (transfer, scheduled payment, swagger, credit score $SCORE, regulator flag, graphs)"
