# 4. Credit score as a transparent weighted scorecard

**Status:** accepted

Score = 300 + 550 x sum(weight x factor), each factor normalised to 0..1. Factors: repayment history 30, income regularity 20 (low variation of 30-day inflow), savings rate 15 (full at 30%), balance cushion 15 (full at 3 months of spending), account age 10, activity 10.

**Why not ML now:** no labeled repayment data exists. A scorecard is explainable to customers and regulators and each factor has a reason string. `CreditScoreService` is the seam to swap in a trained model later.

**Lending policy:** minimum 580 and 30-day-old account; limit and APR by band; one active loan; the lender pool account may go negative (ledger, not a customer).

**Known limits:** weights are judgment-based and unvalidated against real defaults; recalibrate once repayment data exists.
