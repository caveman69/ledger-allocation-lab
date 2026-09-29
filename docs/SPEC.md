# Ledger Allocation Lab: specification

A small, test-first lab for one narrow class of problem: splitting money exactly across the
parties that own it, and proving two reports built from the same data agree. The worked domain is
property tax collection, where one payment on a parcel is owed to several taxing districts.

**The problem in plain terms.** When a county collects a property tax payment, it passes that
money on to the school district, the city, and other taxing districts. The split has to be exact
to the cent, a recorded payment can never be quietly changed, and when two reports disagree,
someone has to explain why. This lab builds each of those pieces and tests them.

Sized for a developer who knows C# and SQL: about 18 to 24 hours for M1 to M5, spread over a few
evenings or weekends. That includes time to read up on the techniques, not just write the code. 
Milestones are in priority order; each one is useful on its own if time runs out.

## 1. Goals

1. Split any payment across districts so the parts **always** add back to the whole, to the cent.
2. Never change or delete a recorded payment. Mistakes are fixed by recording a reversal, the way
   an accountant posts a correcting entry (append-only posting).
3. If the same payment is submitted twice, for example by a double click, record it once
   (idempotent posting).
4. Show, with a deliberately defective second report, how two reports over the same data drift
   apart, and write a reconciliation that finds and explains every difference.
5. Count each payment on the right business day, including payments made late at night, in a time
   zone with no daylight saving time.

## 2. Non-goals

- No real tax rules. Rates, due dates, and the leftover-cent rule are illustrative and configurable.
- No interest, penalties, liens, escrow files, partial refunds, or abatements. Full reversals only
  (see the deferred note in section 6).
- No auth, no multi-user concurrency beyond the idempotency key, no UI required (optional M6).
- No real data. Everything is synthetic.

## 3. Conventions

These rules apply to every part of the code.

- **Money** is stored as whole cents, never as a floating-point number, which can drift by
  fractions of a cent. In code: `long` cents in C# and `BIGINT` cents in SQL. No `float`/`double`
  anywhere near money. `decimal` is allowed only for rates and intermediate weights.
- **Time**: each payment keeps two things, the exact moment it arrived and the business day it
  counts toward. The moment is stored in UTC (universal time, `DATETIME2(3)`). The business day
  (`BusinessDate`, a `DATE`) is worked out once, when the payment is recorded, in the configured
  business time zone. The default is `America/Phoenix`, chosen because it has no DST, which makes
  the UTC offset trap easy to see. Reports filter on `BusinessDate`, never on a UTC timestamp.
- **Ranges** include the start day and stop just before the end day, so back-to-back ranges never
  count the same day twice. In SQL that's a half-open range: `>= @start AND < @end`. No `BETWEEN`
  on a date or datetime, except in the deliberately defective report.
- **Transactions**: anything that writes more than one row saves all of it or none of it, using one
  connection and one transaction.
- Code style follows `.editorconfig` at the repository root (file-scoped namespaces, Allman
  braces, explicit access modifiers).

## 4. Solution layout

```
LedgerAllocationLab.slnx
Directory.Build.props, Directory.Packages.props, global.json
docker-compose.yml, .env.example
db/                                     numbered SQL scripts, run in order
src/LedgerAllocationLab.Core            pure domain: allocator, money, business-date clock
src/LedgerAllocationLab.Data            Dapper + SQL Server: posting, reports, reconciliation
tests/LedgerAllocationLab.Core.Tests    fast unit tests, no database
tests/LedgerAllocationLab.Data.Tests    integration tests against the Docker SQL Server
docs/
  SPEC.md (this file), WALKTHROUGH.md
```

Everything lives at the repository root.

## 5. Domain

| Concept | Meaning |
|---|---|
| District | A taxing entity that receives a share (county, city, school district, special district). |
| Parcel | A taxable property. |
| ParcelDistrictRate | The rate each district levies on a parcel for a tax year. These rates decide each district's share of a payment. |
| Payment | Money received against a parcel. Carries an idempotency key, a unique ID supplied by the caller so a repeat submission can be recognized. |
| PaymentAllocation | One row per (payment, district): the cents that district is owed from that payment. |
| Reversal | A returned payment (for example, a bounced check, or NSF). Records negative allocation rows that cancel the original; the original rows stay untouched. |

## 6. The allocator (Core)

**In plain terms:** splitting $1.00 three ways gives 33.33 cents each, and nobody can be paid a
third of a cent. The allocator rounds every share down, then hands out the leftover cents one at a
time to the shares that lost the most in rounding. The parts always add back to exactly $1.00
(34 + 33 + 33), and the same payment always splits the same way.

```csharp
public static class Allocator
{
    // Splits totalCents across weights by the largest-remainder method.
    public static long[] Allocate(long totalCents, IReadOnlyList<decimal> weights);
}
```

Algorithm (the largest-remainder method):
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

> **Deferred, may come back to this:** negative totals for partial refunds, partial chargebacks, and
> abatements. The intended design is to allocate the negative amount using the original payment's
> stored allocation amounts as the weights, so a partial refund follows the original split exactly
> even if rates changed since. If added, allocate `abs(total)` and negate every part, so that
> `Allocate(-t, w) == -Allocate(t, w)`. Flooring a negative number rounds away from zero, so running
> negatives through the same math would not mirror the positive split. Use `checked` negation
> (`-long.MinValue` overflows).

**Invariants**, the rules the tests check on every split:
- `sum(result) == totalCents`, always. The parts add back to the whole.
- Each `result[i]` is within one cent of `exact[i]`. No share is off by more than a cent.
- Same inputs, same outputs (deterministic).
- Every `result[i]` is zero or greater.

## 7. Schema (Data)

**In plain terms:** five tables hold the districts, the parcels, each district's rate on each
parcel, the payments, and each payment's split by district. The database itself refuses to change
or delete a recorded payment or its split, and refuses to reverse the same payment twice.

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

CREATE UNIQUE INDEX UX_Payments_ReversesPaymentId
    ON dbo.Payments (ReversesPaymentId)
    WHERE ReversesPaymentId IS NOT NULL;

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

A payment can be reversed only once. The filtered unique index `UX_Payments_ReversesPaymentId`
enforces this, the same way `UQ_Payment_IdempotencyKey` enforces a single post.

## 8. Posting (Data)

**In plain terms:** recording a payment saves the payment and its split by district together. If
any step fails, nothing is saved. If the same payment arrives twice with the same key, the second
request gets the original payment back, and nothing new is written.

`PostPaymentAsync(idempotencyKey, parcelId, taxYear, amountCents, receivedOnUtc)`:
1. Open one connection, begin one transaction.
2. Compute `BusinessDate` from `receivedOnUtc` in the business time zone.
3. Insert `Payment`. If the idempotency key already exists, return the existing payment and write
   nothing (a double submit is a no-op, not an error).
4. Load the parcel's rates for the tax year, call `Allocator.Allocate`, insert one
   `PaymentAllocation` row per district.
5. Commit. Any failure rolls back everything: no payment without its allocations.

**Reversals in plain terms:** undoing a payment, for example a bounced check, records a new
negative payment that cancels the original exactly, district by district. The original payment is
never touched, so the history shows both.

`ReversePaymentAsync(idempotencyKey, paymentId, receivedOnUtc)`: in one connection and one
transaction, posts a new `Payment` with the negated amount and `ReversesPaymentId` set, and
allocation rows copied from the original's stored rows and negated. It does not call `Allocator`
(which rejects negative totals), so the reversal mirrors the original split exactly even if rates
have changed. The reversal's `BusinessDate` comes from its own `receivedOnUtc`, not the
original's.
- A repeated idempotency key returns the existing reversal and writes nothing.
- Reversing a payment that has already been reversed under a different key fails (unique
  violation on `UX_Payments_ReversesPaymentId`).
- A reversal can't itself be reversed. The service checks that the original's
  `ReversesPaymentId` is null before posting.

## 9. Reports (Data)

**In plain terms:** two reports answer one question. How much did each district collect each
day? Report A adds up the splits that were stored when each payment was recorded, and it's
correct. Report B recalculates the splits on the fly and makes three common mistakes on purpose,
so the reconciliation in section 10 has real differences to find.

Both reports take `(DateOnly start, DateOnly endExclusive)` and are called with identical
arguments. Report A applies the range half-open on `BusinessDate`. Report B misuses the same
arguments internally, so any drift comes from inside B, not from the caller.

**Report A, Collections by district (the correct one).** Sums stored `PaymentAllocation` rows,
grouped by district, for a business-date range, half-open.

**Report B, Collections by district, recomputed (deliberately defective).** Clearly named and
commented as the counter-example. It ignores the stored rows and recalculates each payment's share
from rates on the fly, and it carries three classic mistakes:
- **A few cents go missing.** It rounds each line (`ROUND(amount * share, 0)`) instead of using
  the stored split.
- **Late-evening payments land on the wrong day.** It converts `ReceivedOnUtc` to a local date
  with a fixed offset that assumes DST (UTC-6), so payments between 11:00 PM and midnight local
  land on the next business date. This applies at both ends of the range, so a late-evening
  payment on the day before `start` is counted on `start`.
- **The report picks up an extra day.** It filters with `BETWEEN @start AND @end` on that
  converted date, so the exclusive end date is counted as a full extra day.

Developer note: B's per-payment logic lives in one inline table-valued function,
`dbo.fn_ReportB_Lines(@start, @endExclusive)`, returning `PaymentId, DistrictId, BDate,
AmountCents`. Report B is a `GROUP BY` over it, and reconciliation reads the same function, so the
reconciler can't drift from the report it checks.

## 10. Reconciliation (Data)

**In plain terms:** reconciliation compares the two reports payment by payment and explains every
difference with one of three labels:
- **Rounding**: same payments on both sides, off by a few cents.
- **Timing/cutoff**: the reports counted a payment on different days, or one report included a
  day the other didn't.
- **Unexplained**: anything else. A person needs to look at it.

`ReconcileAsync(startDate, endDateExclusive)` joins Report A and Report B by district and business
date and returns every row where they differ, with:
- the difference in cents,
- payment counts on each side,
- a classification, using these exact rules:
  - **Rounding**: the same payments on both sides, and `abs(difference)` is no larger than the
    payment count.
  - **Timing/cutoff**: the payment sets differ, and every payment counted on only one side is
    there because B assigns it a different date than its stored `BusinessDate`, or because it
    falls inside one report's window and outside the other's. Once the one-sided payments' amounts
    are removed, whatever difference is left must meet the Rounding rule for the shared payments.
    If it doesn't, the row is Unexplained.
  - **Unexplained**: anything else. This is the bucket a person investigates.
- for Timing rows, a sub-reason: `OffsetShift` (B dated the payment differently) or
  `OutsideRange` (the payment was in only one window).

The comparison is made at the payment level. Report A's lines are the stored `PaymentAllocations`
for payments whose `BusinessDate` is in range. Report B's lines come from `fn_ReportB_Lines`. The
two are matched with a full outer join on `(PaymentId, DistrictId)`, then rolled up to
`(DistrictId, date)`. B rows dated outside the range are kept, not clipped.

**Control total.** A separate check that the district splits add up to the payments:
`sum(PaymentAllocation) == sum(Payment.AmountCents)` for the range. The difference must always be
zero. If it isn't, the recording step is broken, not the reports.

## 11. Tests

What the automated tests prove. Core.Tests run without a database; Data.Tests run against a real
SQL Server in Docker.

Core.Tests (no database):
- Allocator: the examples in section 6, ties, one-cent payments, zero weights, a single weight,
  and every rejection case (negative total, negative weight with its index in the message, empty
  and all-zero weights).
- Allocator invariant sweep: a fixed-seed `Random` generating several thousand (total, weights)
  cases; assert every invariant in section 6. Fixed seed so a failure reproduces.
- Business date: 11:30 PM local on Sept. 30 is Sept. 30; 12:30 AM local on Oct. 1 is Oct. 1. Read
  with the DST-assuming offset (UTC-6), the 11:30 PM instant lands on Oct. 1, and the 12:30 AM
  instant is unaffected. A fixed offset that is one hour off only misplaces the last hour of the
  local day.

Data.Tests (Docker SQL Server; skipped with a clear message if `LEDGERLAB_SQL` is not set):
- Posting writes a payment and allocations that sum to it, in one transaction.
- Posting twice with the same idempotency key writes one payment.
- Posting the same idempotency key concurrently writes one payment, and every caller gets it back.
- A failure mid-post leaves nothing behind.
- Update or delete on a posted row is refused.
- Reversal mirrors the original exactly; net for the pair is zero per district.
- Reversing an already reversed payment with a new key fails. Repeating the reversal's own key
  returns the existing reversal. Reversing a reversal fails.
- Report B counts the whole `endExclusive` day: range `[9/30, 10/1)` with a payment at
  `2026-10-01T18:00Z`. A returns nothing, B returns an Oct. 1 row.
- Report B pulls in a late-evening payment from the day before `start` and dates it on `start`.
- A reversal through Report B: a 100-cent reversal across three equal districts is -100 in A and
  -99 in B, classified as Rounding.
- A mixed row (one moved payment plus rounding on the shared ones) is classified as Timing. The
  same row with one extra unexplained cent is Unexplained.
- Seeded scenario (about 200 payments across 5 districts, including a late-evening payment on each
  edge of the range, at least one payment on the `endExclusive` day, and one reversal): Report A
  ties to the control total; Report B does not; reconciliation finds every difference and
  classifies it.

## 12. Milestones (in priority order, with time boxes)

| # | Milestone | Time box | Done when |
|---|---|---|---|
| M1 | Allocator + tests | 2 to 3 hours | Invariant sweep green. Stop here and it is still worth showing. |
| M2 | Schema + posting + idempotency + append-only + `ReversePaymentAsync` | 6 to 8 hours | Data.Tests for posting and reversal green against Docker. Reversal comes before M3, since the seeded scenario needs it. |
| M3 | Reports A and B + reconciliation | 7 to 9 hours | Seeded scenario shows B drifting and every difference classified. |
| M4 | Business-date clock + boundary tests | 2 to 3 hours | Boundary tests green in both Core and Data. |
| M5 | README | 1 hour | Description of the project. |
| M6 | Optional: one Blazor Server page | 3 to 4 hours | Shows the reconciliation for a date range. Only if M1 to M5 are done. |

Where the time goes:
- **M1**: the largest-remainder method, `decimal` precision, and writing a fixed-seed invariant sweep.
- **M2**: Docker SQL Server setup, Dapper transactions, append-only triggers, and the concurrent
  idempotency test, which usually takes a few tries to make reliable. Reversals add the filtered
  unique index and their own tests.
- **M3**: the largest milestone. Three planted defects in Report B, a table-valued function, the
  payment-level full outer join, the classification rules, and a seeded scenario big enough to
  show all of it.
- **M4**: time zone conversion, sub-millisecond truncation, and boundary tests in both test projects.

Build order doesn't have to follow the numbering. M4 fits naturally after M1, since posting (M2)
needs the business-date clock.

## 13. Definition of done

The code builds without warnings, and every test passes.

- `dotnet build` clean, no warnings from the editorconfig rules.
- `dotnet test` green; Data.Tests green with Docker running.