# 5. Compliance monitoring and the Regulator role

**Status:** accepted

Rules run inside the transaction's unit of work (`ComplianceService.EvaluateAsync` adds flags before `SaveChanges`), so a flag cannot be lost after money moves. Only a seeded `Regulator` role can read or review flags; registration always creates customers.

**Known limits:** thresholds are placeholders to be set per regulation; no external reporting/export yet; stats and network are aggregated in memory and need SQL rollups at scale.
