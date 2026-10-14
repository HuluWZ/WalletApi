# 2. Money safety: minor units, idempotency, optimistic concurrency

**Status:** accepted

- Balances are `long` minor units; never floating point.
- Every transfer requires an `Idempotency-Key`; a unique DB index makes retries and races apply once.
- Account rows carry Postgres `xmin` as a concurrency token; conflicting writes fail and are retried by the caller.
- Scheduled payments derive their key as `sched-{id}-{runAt}`, so a worker crash or double tick cannot double-pay.
