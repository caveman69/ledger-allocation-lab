# Ledger Allocation Lab: specification

A small, test-first lab for one narrow class of problem: splitting money exactly across the
parties that own it, and proving two reports built from the same data agree. The worked domain is
property tax collection, where one payment on a parcel is owed to several taxing districts.

Sized to be built in an a couple afternoons (about 4 to 5 hours each). Milestones are in priority order; each one is useful on its own if time runs out.

## 1. Goals

1. Allocate any payment across districts so the parts **always** add back to the whole, to the cent.
2. Post payments append-only: a posted row is never updated or deleted. Corrections are reversals.
3. Make a double submit harmless (idempotent posting).
4. Show, with a deliberately defective second report, how two reports over the same data drift
   apart, and write a reconciliation that finds and classifies the drift.
5. Get date boundaries right in a time zone with no daylight saving.

## 2. Non-goals

- No real tax rules. Rates, due dates and the leftover-cent rule are illustrative and configurable.
- No interest, penalties, liens, escrow files, partial refunds or abatements. Full reversals only
  (see the deferred note in section 6).
- No auth, no multi-user concurrency beyond the idempotency key, no UI required (optional M6).
- No real data. Everything is synthetic.

## 3. Conventions

- **Money** is `long` cents in C# domain code and `BIGINT` cents in SQL. No `float`/`double`
  anywhere near money. `decimal` is allowed only for rates and intermediate weights.
- **Time**: every timestamp is stored in UTC (`DATETIME2(3)`). Each payment also stores a
  `BusinessDate` (`DATE`), computed once at posting from the configured business time zone
  (default `America/Phoenix`, chosen because it has no DST, which makes the UTC offset trap easy
  to see). Reports filter on `BusinessDate`, never on a UTC timestamp.
- **Ranges** are half-open: `>= @start AND < @end`. No `BETWEEN` on a date or datetime, except in
  the deliberately defective report.
- **Transactions**: every multi-row write uses one connection and one transaction.
- Code style follows `app/.editorconfig` (file-scoped namespaces, Allman braces, explicit access
  modifiers).

## 4. Solution layout

```
app/
  LedgerAllocationLab.slnx
  Directory.Build.props, Directory.Packages.props, global.json
  docker-compose.yml, .env.example, db/
  src/LedgerAllocationLab.Core      pure domain: allocator, money, business-date clock
  src/LedgerAllocationLab.Data      Dapper + SQL Server: posting, reports, reconciliation
  tests/LedgerAllocationLab.Core.Tests   fast unit tests, no database
  tests/LedgerAllocationLab.Data.Tests   integration tests against the Docker SQL Server
docs/
  SPEC.md (this file), WALKTHROUGH.md
```

## 5. Domain

| Concept | Meaning |
|---|---|
| District | A taxing entity that receives a share (county, city, school district, special district). |
| Parcel | A taxable property. |
| ParcelDistrictRate | The rate each district levies on a parcel for a tax year. The weights for allocation. |
| Payment | Money received against a parcel. Has an idempotency key supplied by the caller. |
| PaymentAllocation | One row per (payment, district): the cents that district is owed from that payment. |
| Reversal | A returned payment (for example NSF). Posts negative allocation rows; originals untouched. |

## 6. The allocator (Core)

```csharp
public static class Allocator
{
    // Splits totalCents across weights by the largest-remainder method.
    public static long[] Allocate(long totalCents, IReadOnlyList<decimal> weights);
}
```

Algorithm:
1. `exact[i] = totalCents * weights[i] / sum(weights)`
2. `result[i] = floor(exact[i])`
3. `left = totalCents - sum(result)` (always `0 <= left < n`)
4. Give one cent each to the `left` indices with the largest `exact[i] - result[i]`; ties broken
   by lowest index (deterministic).

Rules and edge cases:
- Totals must be zero or greater. A negative total throws `ArgumentOutOfRangeException`. Nothing in
  this lab needs one: full reversals copy and negate the original's stored rows (section 8), and
  partial refunds are out of scope (section 2).
- Zero total returns all zeros. Zero weights receive zero. A single weight receives the whole total.
- Empty weights, all-zero weights, or any negative weight throw, before any arithmetic. The
  message names the index of a negative weight.
- Ties on the leftover cents go to the lowest index. Callers pass districts in a fixed order
  (by `DistrictId`) so the rule is "lowest district ID wins a tie," not an accident of query order.

> **Deferred, may come back to this:** negative totals for partial refunds, partial chargebacks and
> abatements. The intended design is to allocate the negative amount using the original payment's
> stored allocation amounts as the weights, so a partial refund follows the original split exactly
> even if rates changed since. If added, allocate `abs(total)` and negate every part, so that
> `Allocate(-t, w) == -Allocate(t, w)`. Flooring a negative number rounds away from zero, so running
> negatives through the same math would not mirror the positive split. Use `checked` negation
> (`-long.MinValue` overflows).

**Invariants** (the tests enforce these):
- `sum(result) == totalCents`, always.
- Each `result[i]` is within one cent of `exact[i]`.
- Same inputs, same outputs (deterministic).
- Every `result[i]` is zero or greater.

## 7. Schema (Data)

`db/` holds numbered scripts (CRLF, per `.gitattributes`). Sketch:

```sql
CREATE TABLE dbo.Districts (
    Id   INT           NOT NULL PRIMARY KEY,
    Name         NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.Parcels (
    Id     INT          NOT NULL PRIMARY KEY,
    ParcelNumber VARCHAR(20)  NOT NULL UNIQUE
);

CREATE TABLE dbo.ParcelDistrictRates (
    Id           BIGINT IDENTITY PRIMARY KEY,    
    ParcelId     INT            NOT NULL REFERENCES dbo.Parcels(Id),
    DistrictId   INT            NOT NULL REFERENCES dbo.Districts(Id),
    TaxYear      SMALLINT       NOT NULL,
    Rate         DECIMAL(9, 6)  NOT NULL CHECK (Rate >= 0),
    CONSTRAINT UQ_ParcelId_DistrictId_TaxYear UNIQUE (ParcelId, DistrictId, TaxYear)
);

CREATE TABLE dbo.Payments (
    Id        BIGINT IDENTITY PRIMARY KEY,
    IdempotencyKey   UNIQUEIDENTIFIER NOT NULL,
    ParcelId         INT          NOT NULL REFERENCES dbo.Parcels(Id),
    TaxYear          SMALLINT     NOT NULL,
    AmountCents      BIGINT       NOT NULL,          -- negative for a reversal
    ReversesPaymentId BIGINT      NULL REFERENCES dbo.Payments(Id),
    ReceivedOnUtc    DATETIME2(3) NOT NULL,
    BusinessDate     DATE         NOT NULL,
    CONSTRAINT UQ_Payment_IdempotencyKey UNIQUE (IdempotencyKey)
);

CREATE TABLE dbo.PaymentAllocations (
    Id           BIGINT NOT NULL IDENTITY PRIMARY KEY,
    PaymentId    BIGINT NOT NULL REFERENCES dbo.Payments(Id),
    DistrictId   INT    NOT NULL REFERENCES dbo.Districts(Id),
    AmountCents  BIGINT NOT NULL,
    CONSTRAINT UQ_PaymentId_DistrictId UNIQUE (PaymentId, DistrictId)
);
```

Append-only enforcement: an `INSTEAD OF UPDATE, DELETE` trigger (or `DENY UPDATE, DELETE` to the
application login) on `Payment` and `PaymentAllocation` that raises an error. A test proves an
update is refused.

## 8. Posting (Data)

`PostPaymentAsync(idempotencyKey, parcelId, taxYear, amountCents, receivedOnUtc)`:
1. Open one connection, begin one transaction.
2. Compute `BusinessDate` from `receivedOnUtc` in the business time zone.
3. Insert `Payment`. If the idempotency key already exists, return the existing payment and write
   nothing (a double submit is a no-op, not an error).
4. Load the parcel's rates for the tax year, call `Allocator.Allocate`, insert one
   `PaymentAllocation` row per district.
5. Commit. Any failure rolls back everything: no payment without its allocations.

`ReversePaymentAsync(idempotencyKey, paymentId, receivedOnUtc)`: posts a new `Payment` with the
negated amount and `ReversesPaymentId` set, and allocation rows that exactly mirror the original's.
A payment can be reversed once.

## 9. Reports (Data)

**Report A, Collections by district (the correct one).** Sums stored `PaymentAllocation` rows,
grouped by district, for a business-date range, half-open.

**Report B, Collections by district, recomputed (deliberately defective).** Clearly named and
commented as the counter-example. It ignores the stored rows and recalculates each payment's share
from rates on the fly, and it carries three classic mistakes:
- rounds each line (`ROUND(amount * share, 0)`) instead of using the stored split,
- filters on `ReceivedOnUtc BETWEEN @start AND @end` instead of `BusinessDate`,
- converts with a fixed offset that assumes DST (UTC-6), so payments between 11:00 PM and midnight local land on the next business date.
## 10. Reconciliation (Data)

`ReconcileAsync(startDate, endDateExclusive)` joins Report A and Report B by district and business date and
returns every row where they differ, with:
- the difference in cents,
- payment counts on each side,
- a classification:
  - **Rounding**: same payment count, difference no larger than the number of payments (cents).
  - **Timing/cutoff**: payment counts differ, and the missing payments fall near a day boundary.
  - **Unexplained**: anything else. This is the bucket a person investigates.

Plus a control total check: `sum(PaymentAllocation) == sum(Payment.AmountCents)` for the range.
It must always be zero; if it is not, posting is broken, not the reports.

## 11. Tests

Core.Tests (no database):
- Allocator: the examples in section 6, ties, one-cent payments, zero weights, a single weight,
  and every rejection case (negative total, negative weight with its index in the message, empty
  and all-zero weights).
- Allocator invariant sweep: a fixed-seed `Random` generating several thousand (total, weights)
  cases; assert every invariant in section 6. Fixed seed so a failure reproduces.
- Business date: 11:30 PM local on Sept 30 is Sept 30; 12:30 AM local on Oct 1 is Oct 1. Read with the DST-assuming offset (UTC-6), the 11:30 PM instant lands on Oct 1 and the 12:30 AM instant is unaffected. A fixed offset that is one hour off only misplaces the last hour of the local day.

Data.Tests (Docker SQL Server; skipped with a clear message if `LEDGERLAB_SQL` is not set):
- Posting writes a payment and allocations that sum to it, in one transaction.
- Posting twice with the same idempotency key writes one payment.

- Posting the same idempotency key concurrently writes one payment, and every caller gets it back.
- A failure mid-post leaves nothing behind.
- Update or delete on a posted row is refused.
- Reversal mirrors the original exactly; net for the pair is zero per district.
- Seeded scenario (about 200 payments across 5 districts, including late-evening payments on a due
  date and one reversal): Report A ties to the control total; Report B does not; reconciliation
  finds every difference and classifies it.

## 12. Milestones (in priority order, with time boxes)

| # | Milestone | Box | Done when |
|---|---|---|---|
| M1 | Allocator + tests | 60 mn | Invariant sweep green. Stop here and it is still worth showing. |
| M2 | Schema + posting + idempotency + append-only | 90 min | Data.Tests for posting green against Docker. |
| M3 | Reports A and B + reconciliation | 90 min | Seeded scenario shows B drifting and every difference classified. |
| M4 | Business-date clock + boundary tests | 45 min | Boundary tests green in both Core and Data. |
| M5 | README | 30 min | Description of the project. |
| M6 | Optional: one Blazor Server page | 60 min | Shows the reconciliation for a date range. Only if M1 to M5 are done. |

## 13. Definition of done

- `dotnet build` clean, no warnings from the editorconfig rules.
- `dotnet test` green; Data.Tests green with Docker running.

