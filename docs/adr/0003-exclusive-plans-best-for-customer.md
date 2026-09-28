# ADR 0003 — Resolve exclusive campaigns by comparing plans

**Status:** accepted

## Context

When an exclusive campaign ("40% off, cannot be combined") and several stackable campaigns all apply,
retail systems differ: some apply the highest-priority campaign, others give the customer the best
price. Picking by priority alone produces the classic complaint "the store gave me less discount than
the website".

## Decision

The engine builds one *plan* per exclusive candidate plus one plan containing all stackable
candidates, evaluates each plan fully, and selects:

* `BestForCustomer` (default): the plan with the largest total discount; ties go to the plan whose
  first campaign has the higher priority.
* `HighestPriority`: the plan whose first campaign has the highest priority.

Inside an exclusivity group only the member with the largest stand-alone discount survives.

## Consequences

* Cost is `O(exclusive candidates + 1)` plan evaluations — negligible for realistic campaign counts.
* The result is deterministic and explainable (the losing plans are reported as rejections).
