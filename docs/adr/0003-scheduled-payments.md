# 3. Scheduled payments via an in-process worker

**Status:** accepted (revisit at multi-instance scale)

A `BackgroundService` polls for due schedules and runs them through `TransferService`. Failed runs (e.g. insufficient funds) record `LastError` and skip to the next interval.

**Known ceiling:** with several API instances, concurrent workers may pick the same schedule; the per-run idempotency key keeps this safe, but a proper job queue or `SELECT ... FOR UPDATE SKIP LOCKED` is the upgrade path.
