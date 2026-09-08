# AutoParts ERP

An integrated ERP for **automotive parts distribution**, built as a modular monolith on .NET 8.

Eight modules are in place and talking to each other, including the one Portuguese law cares
about:

| Module | Schema | Order | What it owns |
|---|---|---|---|
| **Partners** | `partners` | 1 | Customers and suppliers, addresses, contacts, credit limits, trading status |
| **Inventory** | `inventory` | 5 | Warehouses, balances, reservations, expected deliveries, valuation, counts, transfers, the movement ledger |
| **Catalog** | `catalog` | 10 | Parts, brands, categories, cross-references, vehicle fitment |
| **Pricing** | `pricing` | 12 | Price lists, quantity breaks, customer agreements, price resolution |
| **Purchasing** | `purchasing` | 15 | Purchase orders, goods receipt, replenishment suggestions |
| **Sales** | `sales` | 20 | Customer accounts, sales orders, dispatch, credit control |
| **Invoicing** | `invoicing` | 25 | Registered series, ATCUD, the signature chain, the QR code, the SAF-T (PT) export |
| **Finance** | `finance` | 30 | The sales ledger: open items, receipts matched to the documents they pay, ageing |

They share no code beyond two contract assemblies, and no module references another module's
projects. 602 tests, all green — 569 that need nothing but the compiler, and 33 that need a real
PostgreSQL because what they check does not exist until there is one.

---

## Getting started

You need PostgreSQL 16 and the .NET 8 SDK.

### PostgreSQL

Install it natively:

```powershell
winget install -e --id PostgreSQL.PostgreSQL.16     # Windows
```

The unattended installer sets the `postgres` superuser password to `postgres`. Then create the
database and user the app expects (`psql` lives in `C:\Program Files\PostgreSQL\16\bin`):

```sql
CREATE USER erp WITH PASSWORD 'erp_dev_password';
CREATE DATABASE autoparts_erp OWNER erp;
ALTER ROLE erp CREATEDB;
```

**That third line is not optional, and it is not for the application.** The integration suite
creates a database of its own for each run and drops it at the end, which is what keeps it from
ever touching `autoparts_erp`. Without `CREATEDB` the whole suite fails at the first line of the
fixture with `42501: permission denied to create database` — every test in it failing at once,
all of them looking like broken code and all of them being one missing grant. The application
itself never creates anything and does not need the attribute.

Point `ConnectionStrings:Erp` in `src/Api/AutoPartsErp.Api/appsettings.json` at any PostgreSQL 16
instance you like.

There is no container anywhere in this project and nothing in it assumes Docker. The development
database is a service on the machine; so is the one the tests use.

### Build and run

```bash
dotnet build
dotnet test --arch x64

dotnet run --project src/Api/AutoPartsErp.Api
```

Open **http://localhost:5150/swagger**.

Each module carries its own migrations and its own `__migrations_history` table inside its own
schema. In Development the host applies all eight on start, then seeds warehouses, brands,
categories, parts and a few partners — so there is something to query immediately.

Sales and Pricing are deliberately not seeded. A customer account is not Sales' to invent: it
arrives as an event when Partners grants the customer role, so the seeded partners populate it
through the outbox on the first run. A price list is a commercial decision, and inventing a
default one would be inventing what the company charges.

### Adding a migration

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/<Module>/AutoPartsErp.Modules.<Module>.Infrastructure \
  --startup-project src/Api/AutoPartsErp.Api \
  --context <Module>DbContext --output-dir Persistence/Migrations
```

`dotnet ef migrations add` prints a `HostAbortedException` as FATAL. It is not a failure — EF
builds the host to read the DbContext configuration and then deliberately aborts it.

### Configuring Invoicing

`Erp:Invoicing` in `appsettings.json` carries the four facts about the company that are the same
for every document a deployment ever issues, plus the company details the SAF-T header names.

| Setting | What it is |
|---|---|
| `IssuerTaxNumber` | The company's own NIF. Field A of every QR code. |
| `TaxRegion` | `Mainland`, `Azores` or `Madeira`. Decides which VAT rates are valid. |
| `CertificateNumber` | The AT's software certification number. `0` means uncertified. |
| `PrivateKeyPem` | The RSA key registered with the AT. Empty in development. |
| `PrivateKeyVersion` | Which key version signed a document, for SAF-T `HashControl`. |
| `Company` | Name, registry ID and address, for the SAF-T header. |
| `Product` | The software **vendor's** NIF, product ID and version. |

The first two are validated at startup and the application refuses to start without them: an
empty NIF produces a QR code that scans perfectly and validates as nothing, and an unknown region
produces the wrong VAT rates on every document a branch ever issues.

`Company` and `Product` are not, deliberately. Everything else works without them, so refusing to
boot would stop somebody invoicing for the sake of a file they may not produce for another month.
Ask for the SAF-T export without them and it names the setting that is missing.

With no `PrivateKeyPem` the signer generates a throwaway 1024-bit key at every startup and logs a
warning saying so. Documents signed with it chain among themselves and verify against nothing,
which is exactly right for a development database — it exercises the whole flow and makes it
impossible to mistake the output for a legal document.

---

## Try it

```http
### Everything that fits a 2015 Golf VII
GET /api/catalog/parts/for-vehicle?make=Volkswagen&model=Golf VII&year=2015

### An OEM number typed with spaces, and without. Same part.
GET /api/catalog/parts?term=5Q0 698 151 A
GET /api/catalog/parts?term=5q0698151a

### Stock for a part across every warehouse
GET /api/inventory/stock/parts/{partId}

### The ledger: every movement, with the balance and the value that followed it
GET /api/inventory/stock/parts/{partId}/movements

### Move stock to another branch. Two steps and a van in between.
POST /api/inventory/transfers
{ "fromWarehouseId": "...", "toWarehouseId": "..." }
POST /api/inventory/transfers/{stockTransferId}/lines
{ "partId": "...", "quantity": 10 }
POST /api/inventory/transfers/{stockTransferId}/dispatch
POST /api/inventory/transfers/{stockTransferId}/lines/{lineId}/receive
{ "quantity": 6 }

### What is on a van right now, between two of our own buildings
GET /api/inventory/transfers/in-transit

### Count a warehouse. Open the sheet, walk the aisle, hand it to somebody
### else to accept. Posting is a separate call so it can be a separate person.
POST /api/inventory/counts
{ "warehouseId": "...", "countedOn": "2026-09-08" }
PUT  /api/inventory/counts/{stockCountId}/lines/{lineId}
{ "countedQuantity": 9 }
POST /api/inventory/counts/{stockCountId}/submit
POST /api/inventory/counts/{stockCountId}/post

### What this customer pays for ten of these, and why
GET /api/pricing/quote?partId={partId}&quantity=10&customerId={customerId}

### Raise a sales order line. Part and quantity only - the catalogue names it,
### Pricing prices it, and the line records which price list answered.
POST /api/sales/orders/{salesOrderId}/lines
{ "partId": "...", "quantity": 4 }

### The same line with the price set by hand, which records no price list behind it
POST /api/sales/orders/{salesOrderId}/lines
{ "partId": "...", "quantity": 4, "unitPrice": 18.00 }

### Confirm it. Checks stock first, then the credit hold, then claims the stock.
POST /api/sales/orders/{salesOrderId}/confirm

### Parts at or below their reorder point, counting what is already on
### order, deepest shortfall first
GET /api/purchasing/suggestions

### Open a series and record the code the tax authority returns for it. Three
### steps, because the middle one involves the AT and can be days later.
POST /api/invoicing/series
{ "type": "FT", "code": "SERIE2026", "year": 2026 }
POST /api/invoicing/series/{seriesId}/validation-code
{ "validationCode": "CSDF7T5H" }
POST /api/invoicing/series/{seriesId}/activate

### Draw a draft invoice for whatever has gone out and not yet been charged
### for. A draft, not an issued document: somebody should see what is about
### to go to the customer. Ship the rest later and draw a second one.
POST /api/invoicing/documents/from-sales-order
{ "salesOrderId": "..." }

### Issue it. Number, ATCUD, signature and QR code, all at once, inside one
### transaction holding a lock on the series. Nothing here can be undone.
POST /api/invoicing/documents/{invoiceId}/issue

### The month's SAF-T file
GET /api/invoicing/saft/2026/9

### What a customer owes, and the documents behind it
GET /api/finance/customers/{customerId}/statement

### Record money that arrived. The allocations are optional: a transfer lands
### with a reference nobody can read, and the balance should show it before
### anybody has worked out which invoices it was for.
POST /api/finance/receipts
{ "customerId": "...", "amount": 500.00, "method": "BankTransfer" }

### Say what it paid, once somebody has worked it out
POST /api/finance/receipts/{receiptId}/allocate
{ "allocations": [ { "openItemId": "...", "amount": 300.00 } ] }

### Every customer with a balance, in thirty-day buckets
GET /api/finance/aging
```

`requests.http` at the repository root has the full set for the REST Client extension, including
a worked session per module and the calls that are supposed to fail.

---

## Why it is shaped this way

### A modular monolith, not microservices

An ERP is one business with many departments, and those departments constantly need each other's
data in the same breath: a sales order checks stock, reserves it, prices it, and posts to the
ledger. Splitting that across services on day one buys distributed transactions, network failure
modes and a deployment pipeline, in exchange for scaling nobody needs yet.

So: one deployable, hard internal boundaries. Each module owns its own database schema, its own
domain model and its own endpoints. When a module genuinely needs to scale on its own, it already
has a schema, a contract and an event surface — extracting it becomes a deployment change rather
than a rewrite.

The boundary is enforced by the project graph, not by discipline. `Inventory.Domain` **cannot**
reference `Catalog.Domain`, because the reference does not exist.

### Modules talk in exactly two ways

This is the part worth understanding, because everything later depends on it.

**Events, for things that have happened.** One module announces a fact; others react to it
eventually. Asynchronous, durable, and the publisher does not know or care who is listening.

**Query contracts, for things you need to know now.** One module publishes a read-only interface;
others call it synchronously. Used only where the answer has to arrive before a decision.

Everything is an event until it cannot be. "Is there enough on the shelf?" cannot be, because a
customer at a counter will not wait for a background sweep — so that one is a contract.

#### Events, end to end

```
Catalog                                    Inventory
───────                                    ─────────
Part.Activate()
  raises PartActivatedDomainEvent
        │
        │  drained into catalog.outbox_messages
        │  in the SAME transaction as the part
        ▼
  OutboxProcessor<CatalogDbContext>
        │  translates to a public contract
        ▼
  PartActivatedIntegrationEvent ────────►  OpenStockRecordOnPartActivated
        │                                    opens a zero balance in every
        │                                    active warehouse
        │                                            │
        │  inventory.inbox_messages                  ▼
        │  short-circuits a redelivery        inventory.stock_items
```

Four deliberate steps:

**Domain events stay private.** `PartActivatedDomainEvent` names Catalog's own types and changes
whenever the aggregate changes. No other module ever sees one.

**A translation step publishes a contract.** The handler converts it into a record of primitives
in `AutoPartsErp.IntegrationEvents`. Without that seam, the first module to subscribe directly to
Catalog's domain event welds itself to Catalog's internals.

**The outbox makes it survive a crash.** The event row is written in the same transaction as the
data that produced it. A process that dies between commit and publish loses nothing: the row is
still there, and the next sweep delivers it. Each module has its own outbox table in its own
schema, drained by its own `OutboxProcessor`.

**The inbox makes redelivery safe.** At-least-once delivery is the only guarantee worth designing
for, so consumers record what they have already handled and short-circuit a repeat. Handlers are
still written to be idempotent on top of that — belt and braces, because the cost of getting this
wrong is a duplicate stock movement nobody can explain.

A message that fails ten times is not deleted and not marked processed. It sits in the table with
its error, which is the honest state for something the system could not deliver and a person now
has to look at.

#### Query contracts

Six so far, each implemented by the module that owns the data and registered by that module.
Consumers reference the contract, never the publisher.

| Contract | Answers | Used by |
|---|---|---|
| `IInventoryAvailability` | How much can still be promised | Sales, on confirmation |
| `IPartnerDirectory` | May we trade with them, and what do we call them | Purchasing |
| `ICatalogDirectory` | What is this part, and may we still trade it | Sales, Purchasing, Pricing |
| `IPriceProvider` | What does this cost this customer at this quantity | Sales |
| `IBillingPartyDirectory` | Who are they, for putting on a legal document | Invoicing |
| `ISalesOrderDirectory` | Is this order ready to bill, and what is on it | Invoicing |

The last two are worth a note each.

`IBillingPartyDirectory` is deliberately a second contract rather than four more fields on
`IPartnerDirectory`, which says in its own remarks that it does not hand out tax numbers.
Purchasing asks the trading directory a hundred times a day and has no business receiving
anybody's NIF in the answer. Two contracts, two reasons to be told, and a grep for who reads
billing identities returns exactly the module that prints them.

`ISalesOrderDirectory` is the first one that runs the other way round: the other five are asked
by the module holding a document, about parts, stock, partners and prices. This one is Invoicing
asking Sales for the thing it is about to turn into a legal document — and Sales answers "can
this be invoiced" itself, rather than exposing a status for Invoicing to judge.

They live in `AutoPartsErp.ModuleContracts`, which has **no dependencies at all** — not even the
SharedKernel. A contract that referenced `Money` would drag the value-object model across the
boundary and stop being a contract. Everything crossing it is a flat record of primitives.

The price of a contract is coupling to availability: the caller needs the publisher to be
reachable. That is the honest cost, and it buys the thing events cannot give — an answer before
the decision. If Inventory ever moves out to its own service, its adapter becomes an HTTP call
behind the same interface and nothing that consumes it changes.

Together they mean a sales line now names a part and a quantity, and everything else about it —
the SKU, the description, the unit, whether there is stock, what it costs — comes from the module
that owns the answer rather than from whoever is typing.

### The layers inside a module

```
Domain          the rules. No EF Core, no ASP.NET, no NuGet packages at all.
Application     one command or query per thing a user can do. Depends only on Domain.
Infrastructure  the only project that knows a database exists.
Presentation    HTTP routes, and the IModule entry point the host sees.
```

Dependencies point inwards. If `Domain` ever needs a package reference, a rule has leaked out of
the domain and into the plumbing.

### Failures are values, not exceptions

Business failures return `Result` / `Result<T>` carrying a stable code like
`inventory.stock.insufficient_available` or `pricing.quote.below_minimum`. Exceptions are reserved
for bugs and infrastructure faults.

This matters more in an ERP than in most software: "this is not allowed" is an ordinary, expected
outcome that happens hundreds of times a day, and it should not cost a stack unwind or get
swallowed by a `catch`. Every error code maps to an HTTP status in exactly one place, so a
conflict is always a 409 and a broken domain rule is always a 422.

### Money and quantity are types

`decimal`, never `double`, and always paired with a currency or a unit. An ERP that loses a cent
per line loses trust, and 500 litres of oil received as 500 drums is a warehouse incident.
Arithmetic across different currencies or units is rejected rather than guessed at. Rounding is
banker's rounding, applied at every step it is defined for rather than once at the end.

### Multi-tenant and auditable from the first table

Every record carries a tenant, a created/modified stamp and — where it applies — an archive flag,
applied by an interceptor and enforced by global query filters. These are close to impossible to
retrofit into a system that already has data, and free to include now.

Deletes are archival. A part referenced by ten years of invoices is never physically removed.

### Document numbers are handed out by the database, never computed in C#

Every number a person will quote down a telephone — `SO-2026-00042`, `PO-2026-00017`, `FT 2026/1` —
comes from a counter that PostgreSQL increments, not from reading the highest one taken and adding
one.

The reason is that reading and adding one is correct exactly until two people do it at the same
moment, and then it fails in the worst available way. Both read the same highest number, both get
the same next one, both succeed, and nothing anywhere complains: the number was only unique
because of the order the reads happened to fall in. Nobody notices until somebody looks up an
order and finds two.

There are two mechanisms, and the difference between them is the difference between untidy and
illegal:

- **Sales and Purchasing** use `number_sequences`, a table in each module's own schema, driven by
  a single `INSERT ... ON CONFLICT DO UPDATE ... RETURNING`. One statement creates the run, or
  increments it, and returns the number taken. There is no window between a read and a write
  because there is no read. If the operation that took the number then fails outside a
  transaction, the number is spent and the sequence has a gap — which for a commercial document is
  untidy and nothing more.
- **Invoicing** cannot accept a gap, because a missing invoice number is a question from the tax
  authority. It holds a `FOR UPDATE` lock on the series row for the whole issue, inside a
  transaction it opens itself, so a document that fails after taking a number puts the number
  back. That is also why it is the one place in the system that opens a transaction by hand.

`ModuleDbContext.TakeNextNumberAsync` is the first; `DocumentSeriesRepository.GetForIssuingAsync`
is the second. Both are exercised under real concurrency by the integration suite.

---

## The domain

### Partners

One table for customers and suppliers, because in this trade they are frequently the same company
— a garage that buys parts and sells you back cores, a factor you both buy from and supply. The
roles are flags, not types, and a partner can hold both.

**Trading status is a rule, not a field.** `CanTakeNewOrders` and `CanPlacePurchaseOrders` are
computed on the aggregate — a customer role plus no hold, a supplier role plus no hold — and
published through `IPartnerDirectory` rather than reimplemented by each consumer. Two copies of
"a supplier, and not on hold" is how two parts of a system start disagreeing about who is allowed
to buy.

### Catalog

**Part numbers are stored twice.** Bosch print `0 986 424 815`; the price file says `0986424815`;
the customer reads `0986-424-815` off an old box. Every part number keeps its printed form for
documents and a normalized form (letters and digits, uppercase) for every lookup and index.

**Cross-references.** A mechanic quotes the OEM number and expects the aftermarket equivalent on
the shelf. Parts carry OEM, competitor, supersession, interchange and trading-partner numbers,
each indexed on its normalized form.

**Fitment.** Nobody asks for part `BP-1188`; they ask for front pads for a 2014 Golf 2.0 TDI.
Parts carry vehicle applications with make, model, engine code, year range and fitting position.
Wrong-fit parts are the most expensive returns in the business, so position is part of the
identity of an application, not a note on it.

**Core charges.** Remanufactured starters, alternators and calipers are sold against a returnable
core with a refundable deposit. A part sold against a core cannot go live without one.

**Lifecycle is one-way.** `Draft → Active → Discontinued → Obsolete`. A discontinued part is still
sold down and still supported for warranty, but no longer purchased — which is why "sellable" and
"purchasable" are two different questions with two different answers, and why `ICatalogDirectory`
returns both.

### Inventory

**Three quantities, never conflated.** *On hand* is what is physically on the shelf, *reserved* is
how much of that is already promised, *available* is the difference — the only number a
salesperson should ever be shown. A part with 10 on hand and 10 reserved is not "in stock".

**Balances are per warehouse.** Distributors run branches, vans and quarantine areas. "How many do
we have?" is never a single number in this business.

**Reservations expire.** A quote nobody converts gives its stock back automatically. Without that,
the shelf slowly fills with quantity reserved against orders that will never happen — which looks,
to everyone using the system, exactly like being out of stock. `ReservationSweeper` runs the sweep
on a timer.

**The ledger is append-only.** Every receipt, issue and count is a row, kept forever, never edited.
A mistake is corrected with a compensating movement, the way an accountant would. Each row stores
the balance that followed it, which turns "what did we think we had on the 14th?" into an indexed
lookup instead of a full replay.

**Adjustments require a written reason**, unlike receipts and issues. Those explain themselves
through their source document; an adjustment is someone overriding the system, and in six months
that sentence is the only thing that will explain it.

**A physical count is a document, not a command.** Correcting stock used to be one call: type a
number, the balance becomes that number, done. That is the mechanism by which stock quietly
disappears from a distributor — nothing to review, no way to tell a real difference from stock
that legitimately moved, and the person who miscounted signing off their own miscount. A
`StockCount` closes all three. It snapshots what the system believed **before** anybody walked the
aisle, records who counted each line and when, and separates submitting from posting so the
figures can be accepted by somebody else. The ad-hoc adjustment survives — a part dropped on the
floor this morning does not need a sheet — but the two are now different reference types in the
ledger rather than one bucket.

**An empty shelf and an unvisited shelf are different answers.** The counted quantity is nullable:
zero means the shelf was empty, null means nobody looked, and posting skips the second. A design
that used zero for both would write off every part the counter did not reach before going home,
and the write-off would look exactly like a real count. The sheet reports its uncounted line
count for the same reason — four hundred counted and sixty not is not a finished count of the
warehouse, however complete the totals look.

**Counting does not freeze the warehouse**, because in a parts distributor nothing ever stops.
Posting applies each counted figure against the **live** balance, so the correction is always
"make it what was found"; the snapshot stays on the line, so a difference between the two is
visible afterwards. That is what separates a stock loss from a sale nobody had entered yet.

**A sheet posts all at once or not at all.** A line that cannot be applied — most often a count
below what is already reserved — fails the whole posting. A partly posted count is the worst
outcome available: some balances corrected, some not, and a sheet that claims it was applied.

**Negative stock is a per-warehouse decision.** Off by default, because negative stock describes
something physically impossible and every downstream valuation inherits the lie.

**A fourth quantity, and it never counts as available.** *On order* is what a submitted purchase
order is bringing and has not delivered. It exists for one decision — whether buying more would be
buying the same thing twice — and `ProjectedAvailable` is the only place the two are added
together. A counter that promised on-order stock would be promising goods that are not in the
building, on a date nobody has confirmed.

**On order is backed by rows, not by a counter.** Every expected delivery is an `IncomingStock`
line naming the purchase order and the order line behind it, shaped deliberately like a
reservation: they are the same kind of thing — a commitment against stock — pointing in opposite
directions. A receipt takes the arrival off the line it arrived against; a cancellation or a short
close drops what that specific order was still bringing. "Why does it say fourteen coming?" has an
answer with an order number in it, which is the only kind of answer worth having when somebody is
standing there disagreeing with the screen.

**The reorder point is measured against the projected position.** A part with two on the shelf, a
reorder point of ten and twenty arriving on Thursday does not need buying. Measured against
available alone it would produce a fresh suggestion every morning until the lorry arrived, and a
list that is wrong every morning is a list nobody reads — which also hides the parts that
genuinely do need ordering. The check runs again when an order is cancelled or closed short: no
stock moves at that moment, and it is exactly the moment the part may need buying in a hurry.

**Over-delivery does not drive the figure negative.** A delivery larger than the order absorbs
only what was outstanding. The extra is real stock and the receipt books it, but it was never on
order and cannot come off a figure that never counted it.

**Stock is valued at moving weighted average**, and the value is what is stored — the average is
derived from it, not the other way round. That is the opposite of how it is usually described and
it is the only version that stays correct: `Money` rounds to the currency's decimal places, so a
stored per-unit cost would round on every receipt and compound for the life of the part. A total
in euros rounds once, against a figure that is actually denominated in euros. Same reason the
ledger stores what a movement was worth and derives its unit cost: three thousand units at 35
cents is a movement worth €1,050.65, and a stored €0.35 would report €1,050.00 for it every time.

**Costing lives in two methods and nowhere else.** A receipt adds value, one private method takes
it out, and outside Inventory a cost only ever appears as a value stamped on a ledger row. Moving
to FIFO means rewriting those two and adding a layer table — Sales, Pricing and Finance do not
find out. An interface here would have been flexibility in name only: FIFO is not different
arithmetic, it is different *state*, and an interface with nowhere to keep layers solves nothing.

**A price is optional and its absence means something.** A purchase receipt knows what was paid; a
transfer, a customer return and a count that found more do not. Those come in at what the shelf is
already worth, so the average does not move. Treating an unknown price as zero would dilute the
average towards nothing, and the first customer return would wreck the valuation of a part sold
for years. Into a shelf that was never priced there is no average to apply, and the ledger says so
with no cost rather than a cost of zero.

**Emptying the shelf empties the value, to the cent.** The last issue takes whatever is left
instead of its proportional share. Proportions round, and rounding leaves a few cents against a
shelf with nothing on it — a balance sheet claiming the company owns €0.03 of a part it has none
of.

**A receipt priced in another currency is refused, not converted.** There is no exchange rate
anywhere in this system. Converting with an invented one puts a number on the balance sheet that
nobody can trace back to a decision.

**Stock between two warehouses is on a document, not in a transaction.** A transfer that issued
from the branch and received into the depot in one breath would put the goods on the depot's shelf
on Monday, where a salesperson can promise them to a customer collecting that afternoon — and if
the van never arrives, the loss surfaces weeks later as an unexplained count variance. So a
`StockTransfer` has two steps and a middle: dispatch takes the stock off the sending shelf,
receipt puts what turned up on the receiving one, and what is in between belongs to neither.
`GET /api/inventory/transfers/in-transit` is the only thing that knows where it is.

**Transit is not a shelf.** There is no in-transit warehouse, deliberately: one would appear in
availability, in the replenishment list and on count sheets, and every one of those would have to
learn to exclude it. What is on the van lives on the transfer instead, as a quantity and a value
per line.

**Value travels with the goods.** Moving stock between two of the company's own shelves must not
change what the company owns, so the exact value comes off the sender, rides on the document, and
lands on the receiver. That is why the receiving end takes a total rather than a unit price —
deriving a price per unit and multiplying back would round twice, and every van journey would
quietly gain or lose the company a few cents.

**A shortfall in transit is a loss with a reason on it**, and writes no stock movement. Nothing
moves at either end: the sending shelf gave the goods up at dispatch and the receiving shelf never
had them. The loss is the gap between the `TransferOut` and the `TransferIn`, and the transfer
document is what explains it — the same role a count sheet plays for an adjustment.

### Pricing

**Three things and the rules that turn them into one number.** A `PriceList` (named, one currency,
a period), a `PriceListEntry` (what one part costs in it, at every quantity that matters), and a
`CustomerPricing` agreement (which list a customer buys from, and what comes off it).

**A quantity break is a floor, not a band.** "10+ is €22" and "50+ is €20" completely describe
every quantity from 10 upwards. The price is the *highest* break that still applies — getting that
backwards charges somebody buying fifty the price of buying one, and it looks right in every test
that only ever buys one.

**Below the smallest break there is no price**, and that is a real answer. "We do not sell fewer
than five of these" is a normal thing for a distributor to say, and the refusal names the minimum.

**Lists are ranked before they are compared.** Promotion beats a customer's own list beats the
standard one. A promotion applies *because it is a promotion*, not because it came out lower —
otherwise a campaign silently does nothing for exactly the customers who negotiated hardest.

**A promotion must have a last day.** One that never ends is a price change in a costume, and the
costume is what stops anybody noticing it is still running in November.

**The list and the discount are separate.** A workshop on the trade list with 5% off is not the
same arrangement as a workshop on a list where every price is already 5% lower, even when today's
figures agree — the first follows the trade list when it moves and the second does not.

**The discount comes off after the break.** 5% off the fifty-up price, not off the price of one.
Both orderings are defensible and only one is what everybody assumes.

`PriceListEntry` is its own aggregate root rather than a child collection of `PriceList`, the one
place this system departs from "children through the root". A standard list is tens of thousands
of parts and correcting one price should not mean loading all of them. The resolver checks the
list's state before quoting from an entry — that check is the boundary, in place of the graph.

### Purchasing

**Suggestions, not automatic orders.** Inventory's reorder-point signal becomes a standing note
that a part has run low. A buyer decides which are real and raises one order covering several —
which is also how you avoid four separate €30 orders to the same supplier in one week.

**At most one open suggestion per part per warehouse**, enforced by a partial unique index. Stock
crossing the reorder point repeatedly — which it will, every time something is picked — refreshes
the existing suggestion instead of building a pile of duplicates.

**A suggestion counts what is already coming.** It carries the available quantity and the on-order
quantity separately, and the list is ranked by the gap between the reorder point and their sum. A
buyer reading "2 available, reorder point 10" orders ten; the same buyer reading "2 available, 8
coming" orders two, or nothing. Netting the two into one number takes that judgement away, and
usually takes it the expensive way.

**The supplier is verified, not assumed.** Creating an order asks Partners whether the partner is
an active supplier and takes their code from there.

**Receipts are idempotent.** A redelivered goods-receipt event does not book the stock twice.

### Sales

**Customer accounts are a projection.** Sales does not own the customer — Partners does. It keeps
its own account, fed by Partners' events, carrying the credit limit, the committed amount and the
hold state. That is what makes credit control a local decision instead of a cross-module call in
the middle of a confirmation.

**Confirmation is where everything meets.** It asks Inventory whether the stock is there, checks
the account's hold and its credit, and only then commits the order and claims the stock. A refusal
says which line and by how much: *"Only 4 EA of BP-1188 is available and this order needs 10."*
That used to be a reservation failing silently in a background sweep an hour later.

**Back-orders are deliberate.** Pass `allowBackorder` and the flag travels all the way through to
Inventory, which then holds whatever is there instead of refusing. Not honoured for a counter
sale — those are goods leaving now.

**Line arithmetic is fixed and rounded at each step.** Extend, discount, net, VAT, each rounded as
it is computed, because that is the order a customer can check with a calculator.

### Invoicing

The part of Portuguese invoicing that is law rather than design, end to end: schema, endpoints,
the Sales bridge and the monthly SAF-T file.

**A series is a registered run of gapless numbers**, for one document type and one year. It has
to be declared to the tax authority before anything is issued in it, and what comes back is a
validation code that becomes half of every ATCUD the series produces. Without that code the
series refuses to go live, rather than letting somebody find out at the first audit.

**Numbers come out one at a time.** No reserve, no batch, no peek at the next one. A number taken
and not used is a gap, and a gap is the thing the whole mechanism exists to prevent — so taking
one mutates the series, and a caller that fails afterwards must roll the whole transaction back.

**A document is built in two moves.** Lines while it is a draft; then issuing takes the number,
computes the totals, signs the result and freezes everything. That shape is forced by the law
rather than chosen: the number cannot be taken until the document is complete, and the signature
covers the total, so the total cannot move afterwards.

**Each document signs onto the one before it** in the same series — the last field of the signed
string is the previous document's signature. Altering document 35 invalidates 36 and everything
after it, which is the entire point.

**Voiding keeps everything**: the number, the figures, the signature, the place in the chain.
Only the status changes and a reason is added. A missing number is worse than a cancelled one.

**Two tills issuing at once queue rather than collide.** Taking a number holds a row lock on the
series — `SELECT ... FOR UPDATE` inside an explicit transaction — so the second one waits a few
milliseconds and gets the next number. Without it both would read the same number and one would
lose on the concurrency token: safe, but a failed request in the middle of a customer's
transaction. The repository refuses to run outside a transaction rather than silently taking no
lock at all.

It is also the only module registered **without** `EnableRetryOnFailure`. Mechanically, a
retrying execution strategy will not run a user-initiated transaction, so the two together break
every issue. More seriously, a retry re-runs against an aggregate the failed attempt has already
mutated in memory — it would find a document that believes it has been issued having committed
nothing, or take a second number for the same document. A transient fault here should surface as
a failed request somebody repeats deliberately.

**An invoice can be drawn from a sales order for whatever has shipped**, and the bridge runs both
ways by two different mechanisms. Invoicing asks Sales a synchronous question, because it cannot
draw a document without knowing what is on the order. Sales learns the outcome by integration
event, because it is only being told something that already happened — and putting a second
module inside the transaction that takes a document number is the one thing that transaction must
not do. The honest cost is a window, a few hundred milliseconds wide, in which a duplicate *draft*
could be raised. Not a duplicate document: that would need the series lock.

An order is billable when something on it has gone out, that something has not already been
charged for, and the order was not cancelled. It does not have to be finished. Each line carries
an `InvoicedQuantity` alongside its dispatched one, and what is billable is the difference; the
order itself carries `NotInvoiced` / `PartiallyInvoiced` / `Invoiced` derived from the lines, and
no invoice reference at all. An order that ships in three lorries is billed by three documents, so
the order keeps the quantities and Invoicing keeps the documents. Each invoice line records the
order line it bills, which is what lets a void give back exactly what that document charged for
and leave the others alone.

`SalesOrder.RecordBilling` is the last thing standing between a redelivered message and a double
charge: it refuses a quantity larger than what is left to bill, and refuses it before applying any
of the document's lines, so a document that fails on its third line bills none of them. The
refusal fails the message, which goes to the inbox's retries and then to its dead letters, where a
person sees it. Detection rather than prevention, and deliberate: preventing it would mean holding
the order inside the numbering transaction.

The reverse leg is looser on purpose. A void that gives back more than the line still carries is
applied as far as it goes rather than refused — by the time it arrives the line may have been
credited by another route, and refusing would send a correct void to the dead letters.

**A sales order records a VAT rate; a document has to declare a VAT category.** Nothing in the
number itself says which: 13 is intermediate on the mainland and nothing at all in Madeira, where
the intermediate rate is 12. `PortugueseVatRates` does the translation from the establishment's
region, and refuses rather than guessing — a rate the region does not use, or a zero that is
really an exemption missing its M-code. Getting this wrong does not produce a wrong total. It
produces a correct total filed under the wrong heading, which reconciles perfectly until somebody
compares the VAT return with the SAF-T file.

**The SAF-T (PT) export** is schema 1.04_01, `TaxAccountingBasis` F — billing software, documents
and no accounts. `GET /api/invoicing/saft/2026/9`. Drafts are excluded and voided documents
included: a draft has no number and does not exist as far as the tax authority is concerned,
while a voided one has a number that was reported and leaving it out would put a gap in the
sequence. Products are built from the document lines rather than from Catalog, so a part
withdrawn since it was sold still appears exactly as the document names it; customers come from
Partners, because the SAF-T customer table is a master file, with the document's own snapshot
standing in for anyone Partners no longer knows.

**Nothing here was written from memory.** Every prescribed detail — the exact string that gets
signed, the four characters taken from positions 1, 11, 21 and 31 of the base64 signature, the
ATCUD's hyphen, the QR code's field list, the order of every element in the SAF-T schema — was
checked against a primary source and is tested against the worked examples those sources publish.
The two signature examples from the DGCI specification are in the suite character for character.

**This is not certified software and nothing here makes it certified.** Issuing real invoices in
Portugal also needs a certification number from the tax authority, a private key registered under
it, and each series declared. Configure `Erp:Invoicing` before anything real: without a
`PrivateKeyPem` the signer generates a throwaway key at every startup and says so in the log, and
documents signed with it verify against nothing — which is the correct behaviour for a
development database and a disaster anywhere else.

---

## Layout

```
AutoPartsErp.sln
├── src
│   ├── Api/AutoPartsErp.Api                  composition root; wires modules, serves HTTP
│   ├── Shared
│   │   ├── AutoPartsErp.SharedKernel         entities, value objects, Result, CQRS contracts
│   │   ├── AutoPartsErp.Modules.Abstractions IModule, pipeline behaviours, event bus, DI
│   │   ├── AutoPartsErp.Persistence          ModuleDbContext, outbox, inbox, auditing
│   │   ├── AutoPartsErp.IntegrationEvents    facts modules announce. Records only.
│   │   └── AutoPartsErp.ModuleContracts      questions modules answer. Zero dependencies.
│   └── Modules
│       ├── Partners
│       ├── Inventory
│       ├── Catalog
│       ├── Pricing
│       ├── Purchasing
│       ├── Sales
│       └── Invoicing
│           ├── ....Domain
│           ├── ....Application
│           ├── ....Infrastructure
│           └── ....Presentation
└── tests
    ├── AutoPartsErp.SharedKernel.Tests
    ├── AutoPartsErp.Modules.<Module>.Tests   one per module, no database
    └── AutoPartsErp.IntegrationTests         the real application on real PostgreSQL
```

### The pieces worth knowing about

| Thing | Where | What it does |
|---|---|---|
| `IModule` | `Modules.Abstractions` | The whole contract between the host and a module |
| `Dispatcher` | `SharedKernel/Messaging` | A ~40-line mediator, so no third-party licence can strand the codebase |
| `IPipelineBehavior` | `SharedKernel/Messaging` | Cross-cutting steps; logging and validation ship with it |
| `Result` / `Error` | `SharedKernel/Results` | Expected failures as values with stable codes |
| `ModuleDbContext` | `Persistence` | Dispatches domain events and drains them into the outbox, in the transaction |
| `OutboxProcessor<T>` | `Persistence/Outbox` | One background sweep per module, draining that module's table |
| `InboxMessage` | `Persistence/Inbox` | What a consumer has already handled, so redelivery is free |
| `IntegrationEvents` | `src/Shared` | Facts. What one module announces to anyone listening |
| `ModuleContracts` | `src/Shared` | Questions. What one module answers on demand |
| `StockItem` | `Inventory.Domain` | The consistency boundary for every stock change |
| `PriceResolution` | `Pricing.Domain` | Which price wins. A pure function over data somebody else fetched |
| `ReservationSweeper` | `Inventory.Infrastructure` | Returns lapsed reservations to available |
| `DocumentSeries` | `Invoicing.Domain` | The only thing that hands out a document number, one at a time |
| `SignatureSource` | `Invoicing.Domain` | Builds the exact string the tax authority prescribes for signing |
| `StockCount` | `Inventory.Domain` | A counted warehouse: the snapshot, the findings, and who accepted them |
| `StockTransfer` | `Inventory.Domain` | Stock between two warehouses, and the value riding with it |

---

## Adding the next module

1. Create four projects under `src/Modules/<Name>/` mirroring an existing module.
2. Implement `IModule`; claim a schema name no other module uses.
3. Reference the new `.Presentation` project from `AutoPartsErp.Api`.
4. Add one line to `Program.cs`:

```csharp
builder.Services.AddErpModules(
    builder.Configuration,
    new PartnersModule(),
    new InventoryModule(),
    new CatalogModule(),
    new PricingModule(),
    new PurchasingModule(),
    new SalesModule(),
    new FinanceModule());   // <- that is the whole integration
```

Nothing else in the host changes. If a module needs to react to something another module did, it
subscribes to an integration event. If it needs an answer before it can decide something, it takes
a dependency on a contract interface. It never adds a project reference to another module.

`IModule.Order` controls registration and seeding order, and reads in the order the business
works: who we trade with, what we stock, what we sell, what it costs, what we buy, what we ship.

---

## Roadmap

**Done:** all eight modules. Transactional outbox and consumer inbox. Six module query
contracts. Invoicing end to end, including partial invoicing over the Sales bridge and the SAF-T
(PT) export. An integration suite against real PostgreSQL. Document numbering that survives
concurrency in all three modules that hand out numbers.

**Next, in rough dependency order:**

1. **Communicating documents to the AT.** The webservice that reports each document within days
   of issuing it. The paperwork around certification is paperwork; this is the last piece of code
   between here and a legally usable installation.
2. **Accounts payable, the general ledger, VAT returns and period close.** Finance covers what
   customers owe and nothing else yet: there is no supplier invoice to owe anything against,
   because Purchasing has an order and a goods receipt and no document between them.
3. **Stock valuation and costing** — FIFO or weighted average over the movement ledger, which
   already carries a unit cost column for it. Also what a margin floor in Pricing would need.
4. **Returns and core credits** — the other half of a parts business, and the reason
   `RequiresCoreReturn` exists on a part already.

**Known issues:**

- A malformed `warehouseId` in a request body returns 500 rather than 400. Bad client input
  should never surface as a server error.
- Purchase order lines have no concurrency token of their own, so two people editing different
  lines of the same order can still conflict at the aggregate level.
- The outbox assumes a single instance. Two hosts sweeping the same table would deliver some
  messages twice — safe, because consumers are idempotent, but wasteful. `FOR UPDATE SKIP LOCKED`
  is the fix.
- Nothing prunes delivered outbox and inbox rows. They grow forever until a retention job exists.
- The SAF-T `HashControl` field carries the signing key's version, which is what the AT's thinner
  guidance on it appears to want. If a validator disagrees, `Erp:Invoicing:PrivateKeyVersion` is
  the setting to change.
- A document drawn from a sales order and then issued leaves a window of a few hundred
  milliseconds in which the order does not yet know. A second *draft* can be raised in it. A
  second issued document cannot, because that needs the series lock.
- SAF-T is generated in memory and returned in one response. A year's file for a busy branch is
  tens of megabytes, which is large for a response and fine for a machine; streaming it is the fix
  if it ever stops being fine.
- The reorder point is a number somebody types. The honest version is consumption over the
  supplier's lead time plus a safety margin, and the movement ledger already holds the consumption
  half — what is missing is a lead time anywhere in the system, on the supplier or on the
  part/supplier pair.
- Cost of sale is on the ledger but nowhere else. Nothing posts it to a general ledger, because
  there is no general ledger; nothing shows margin on a sales order line; and the margin floor
  Pricing would want is now possible and not built.
- A customer return is valued at today's average rather than at what those goods cost when they
  left. The second is correct and needs the sales line to carry its cost back, which is a change
  to Sales.
- A shortfall written off in transit produces no shrinkage posting, because there is no general
  ledger to post it to. The `StockTransferClosedShort` event carries the lost value ready for one.
- Storage bins are recorded but never used: `StockMovement.InBin` has no caller, so no movement
  says where in the warehouse anything went.
- A count sheet records who counted and who posted, but nothing stops them being the same person.
  Real four-eyes needs the authorisation this system does not have yet; `ICurrentUser` is where
  it would be enforced.
- A count sheet covers a whole warehouse. Cycle counting by category or by bin needs Inventory to
  ask Catalog which parts are in a category, which is a query contract that does not exist yet.

**Foundation work wanted along the way:**

- Authentication and authorisation. The tenant currently comes from an `X-Tenant-Id` header;
  `ITenantContext` is the seam where a validated token replaces it.
- A proper vehicle taxonomy. Fitment is deliberately flat for now; the industry shapes are
  **TecDoc** in Europe and **ACES/PIES** in North America.
- Full-text and fuzzy part search using PostgreSQL `pg_trgm`, for partial and mistyped numbers.

---

## Integration tests

29 tests in `tests/AutoPartsErp.IntegrationTests`, against the PostgreSQL 16 this project already
requires. No container, no second service, nothing to start first.

```bash
dotnet test tests/AutoPartsErp.IntegrationTests
```

It connects to the same server as the application, creates a database named after a fresh Guid,
applies every module's migrations to it, runs everything there, and drops it at the end — so it
cannot touch `autoparts_erp`, and two runs at once cannot collide. That is what the `CREATEDB`
grant in **Getting started** is for; without it every test fails with `42501` before reaching any
code worth testing. Point it at a different server with `ERP_TEST_CONNECTION`:

```powershell
$env:ERP_TEST_CONNECTION = "Host=localhost;Port=5433;Username=erp;Password=erp_dev_password"
```

If the server is not reachable the suite fails rather than skipping. That is deliberate: a green
run that silently tested nothing is worse than a red one.

**It does not start the web host.** `ErpFixture` builds the service graph from the same three
calls `Program.cs` makes — `AddErpCore`, `AddErpPersistence`, `AddErpModules` — and applies the
migrations itself. Serilog, Swagger, CORS, health checks and the HTTP pipeline are absent, because
none of them can make a value converter or a row lock behave differently, and reaching them would
mean a `WebApplicationFactory`, a testing package, and a catch block in `Program.cs` for an
exception type that is internal and cannot be named. The one thing this gives up is that the
module list is written down twice; `SchemaTests` asserts on the seven schemas by name so that a
module added to the host and forgotten here does not quietly go untested.

One fixture for the whole suite. Applying seven modules' migrations takes a few seconds and paying
that per test class would make the suite slow enough that nobody runs it. The price is that tests
share a database and none of them may assume it is empty — each creates its own tenant and its own
identifiers.

It exists because of what the unit tests structurally cannot reach. Every mapping in this system
is written against a provider that only has an opinion at runtime: a value converter, an owned
collection, a filtered index predicate that is a raw SQL string EF passes through without reading.
None of it can fail at compile time.

What it checks, and why each one is there rather than in a unit test:

| Test | What only a database can tell you |
|---|---|
| Migrations applied, none pending | The migration files and the model have not drifted apart |
| Filtered index predicates | The predicate still names columns that exist — rename the property, forget the string, and the index is created and never matches |
| `xmin` present | Optimistic concurrency is mapped; if it stopped working, two writers would overwrite each other in silence |
| Eight tills issuing at once | The row lock. Without it all eight read the same number, all eight succeed, and the series counter still lands in the right place |
| A refused issue leaves no gap | The number goes back when the transaction rolls back |
| Sixteen clerks numbering orders at once | The other numbering mechanism, in Sales and in Purchasing. Sixteen because two collide often enough to prove the point and rarely enough to pass a few times first |
| Each year, tenant and module numbers apart | The counter's composite key is the key it was declared to be — one company's trading cannot advance another's numbering |
| An invoice round-trips | Two owned values on the document, three per line, a unit through a converter with a hand-written comparer |
| Two tenants cannot see each other | The global query filter, which is invisible at every call site by design |
|

---

## Conventions

- **C# 12**, nullable enabled, warnings as errors in `src`. An unused `using` fails the build.
- File-scoped namespaces, `_camelCase` private fields, explicit types over `var` where the type is
  not obvious. Enforced by `.editorconfig` at build time.
- `AnalysisLevel` is `latest-recommended`, so an SDK update can introduce new rules and break a
  build that worked yesterday. Pin it if that becomes disruptive.
- EF-generated migrations are exempt from the style rules — they are rewritten on every scaffold.
- Database identifiers are `snake_case` via `EFCore.NamingConventions`. The database is meant to be
  pleasant to query directly, because finance staff and integrations will.
- Package versions live in `Directory.Packages.props` only. Never put a `Version` on a
  `PackageReference`.
- **A new project has to be added to `AutoPartsErp.sln`.** Nothing enforces this and nothing
  complains: the `Api` project references every module's `Presentation`, so the whole source tree
  builds through that graph whether or not the solution lists it. A test project has no such
  parent, and the integration suite sat outside the solution long enough for `dotnet test` to
  report a confidently green number that did not include a single one of its tests.
  `dotnet sln add (Get-ChildItem -Recurse -Filter *.csproj).FullName` re-adds everything missing
  and skips what is already there.
- One aggregate per repository. Read paths never go through repositories — they project columns.
- Raw SQL in an index filter (`HasFilter("is_deleted = false")`) is correct only because of the
  snake_case convention. Renaming the property without renaming the string gives a migration that
  builds and an index that silently never applies.
- **A value object mapped with `OwnsOne` belongs to one owner. Never hand an instance to a
  second.** `Money`, `Quantity` and `VatRate` are owned entities to EF Core, so the owner's
  identifier is part of their key; storing one that another entity already owns asks EF to move
  it, and it throws *"the property 'VatRate.InvoiceLineId' is part of a key and so cannot be
  modified"* at `Add`, naming nothing that would lead you to the line that did it. It is invisible
  to every unit test, because in memory sharing an immutable value is perfectly correct. Copy
  instead — each of those three has a `Copy()` that exists for this and says so.
- `GenerateDocumentationFile` is on, so a `<see cref="..."/>` that does not resolve is a build
  error like any other. The trap is a nested type that shadows a namespace of the same name —
  `InvoicingErrors.Series` hides the `Series` namespace, and the cref inside it has to be
  qualified.
- An Application project references the domain and the two contract assemblies and **nothing
  else** — no NuGet packages, no framework references. Configuration reaches a handler as a plain
  options object registered by the module, not as `IOptions<T>`, so that the layer never learns
  what a web host is.

### Notes for Windows

- If `dotnet test` fails with an x86 runtime error, an x86 .NET install is ahead of the x64 one on
  `PATH`. Remove it, or pass `--arch x64`.
- Don't keep the working copy inside OneDrive. It syncs `bin/` and `obj/` and causes intermittent
  file locks mid-build.
- After extracting an archive over the working copy, run `dotnet build --no-incremental` once.
  `Expand-Archive` preserves the timestamps stored in the zip, so an extracted file can look older
  than the last build output and MSBuild will skip recompiling it.
