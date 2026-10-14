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
[ "$BAL" = "600" ] && echo "SMOKE OK (transfer + scheduled payment, balance $BAL)" || { echo "SMOKE FAIL: balance $BAL, expected 600"; exit 1; }
