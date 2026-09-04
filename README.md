# AutoParts ERP

An integrated ERP for **automotive parts distribution**, built as a modular monolith on .NET 8.

Six modules are in place and talking to each other:

| Module | Schema | Order | What it owns |
|---|---|---|---|
| **Partners** | `partners` | 1 | Customers and suppliers, addresses, contacts, credit limits, trading status |
| **Inventory** | `inventory` | 5 | Warehouses, stock balances, reservations, the movement ledger |
| **Catalog** | `catalog` | 10 | Parts, brands, categories, cross-references, vehicle fitment |
| **Pricing** | `pricing` | 12 | Price lists, quantity breaks, customer agreements, price resolution |
| **Purchasing** | `purchasing` | 15 | Purchase orders, goods receipt, replenishment suggestions |
| **Sales** | `sales` | 20 | Customer accounts, sales orders, dispatch, credit control |

They share no code beyond two contract assemblies, and no module references another module's
projects. 324 tests, all green.

---

## Getting started

You need PostgreSQL 16 and the .NET 8 SDK.

### PostgreSQL

Either install it natively:

```powershell
winget install -e --id PostgreSQL.PostgreSQL.16     # Windows
```

The unattended installer sets the `postgres` superuser password to `postgres`. Then create the
database and user the app expects (`psql` lives in `C:\Program Files\PostgreSQL\16\bin`):

```sql
CREATE USER erp WITH PASSWORD 'erp_dev_password';
CREATE DATABASE autoparts_erp OWNER erp;
```

Or run it in a container, if Docker is available to you:

```bash
docker compose up -d
```

Docker is not required and nothing in the project assumes it. Point `ConnectionStrings:Erp` in
`src/Api/AutoPartsErp.Api/appsettings.json` at any PostgreSQL 16 instance you like.

### Build and run

```bash
dotnet build
dotnet test --arch x64

dotnet run --project src/Api/AutoPartsErp.Api
```

Open **http://localhost:5150/swagger**.

Each module carries its own migrations and its own `__migrations_history` table inside its own
schema. In Development the host applies all six on start, then seeds warehouses, brands,
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

### The ledger: every movement, with the balance that followed it
GET /api/inventory/stock/parts/{partId}/movements

### What this customer pays for ten of these, and why
GET /api/pricing/quote?partId={partId}&quantity=10&customerId={customerId}

### Raise a sales order line. Part and quantity only - the rest is looked up.
POST /api/sales/orders/{salesOrderId}/lines
{ "partId": "...", "quantity": 4, "unitPrice": 30.00 }

### Confirm it. Checks stock first, then the credit hold, then claims the stock.
POST /api/sales/orders/{salesOrderId}/confirm

### Parts at or below their reorder point, deepest shortfall first
GET /api/purchasing/suggestions
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

Four so far, each implemented by the module that owns the data and registered by that module.
Consumers reference the contract, never the publisher.

| Contract | Answers | Used by |
|---|---|---|
| `IInventoryAvailability` | How much can still be promised | Sales, on confirmation |
| `IPartnerDirectory` | May we trade with them, and what do we call them | Purchasing |
| `ICatalogDirectory` | What is this part, and may we still trade it | Sales, Purchasing, Pricing |
| `IPriceProvider` | What does this cost this customer at this quantity | Sales |

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

**Negative stock is a per-warehouse decision.** Off by default, because negative stock describes
something physically impossible and every downstream valuation inherits the lie.

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
│       └── Sales
│           ├── ....Domain
│           ├── ....Application
│           ├── ....Infrastructure
│           └── ....Presentation
└── tests
    ├── AutoPartsErp.SharedKernel.Tests
    └── AutoPartsErp.Modules.<Module>.Tests   one per module
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

**Done:** Partners, Inventory, Catalog, Pricing, Purchasing, Sales. Transactional outbox and
consumer inbox. Four module query contracts.

**Next, in rough dependency order:**

1. **Invoicing and Portuguese tax compliance** — ATCUD, the QR code, the document hash chain and
   SAF-T export. Gapless sequential numbering per document type is a legal requirement here, and
   the current max-plus-one numbering will not satisfy it.
2. **Finance** — AR/AP, general ledger, VAT returns, period close.
3. **Stock valuation and costing** — FIFO or weighted average over the movement ledger, which
   already carries a unit cost column for it. Also what a margin floor in Pricing would need.
4. **Returns and core credits** — the other half of a parts business, and the reason
   `RequiresCoreReturn` exists on a part already.

**Known issues:**

- A malformed `warehouseId` in a request body returns 500 rather than 400. Bad client input
  should never surface as a server error.
- Order numbering is max-plus-one and will collide under genuine concurrency. A sequence table is
  the real answer, and the ATCUD work needs it anyway.
- Purchase order lines have no concurrency token of their own, so two people editing different
  lines of the same order can still conflict at the aggregate level.
- The outbox assumes a single instance. Two hosts sweeping the same table would deliver some
  messages twice — safe, because consumers are idempotent, but wasteful. `FOR UPDATE SKIP LOCKED`
  is the fix.
- Nothing prunes delivered outbox and inbox rows. They grow forever until a retention job exists.

**Foundation work wanted along the way:**

- Authentication and authorisation. The tenant currently comes from an `X-Tenant-Id` header;
  `ITenantContext` is the seam where a validated token replaces it.
- A proper vehicle taxonomy. Fitment is deliberately flat for now; the industry shapes are
  **TecDoc** in Europe and **ACES/PIES** in North America.
- Full-text and fuzzy part search using PostgreSQL `pg_trgm`, for partial and mistyped numbers.
- Integration tests against a real PostgreSQL container (Testcontainers). Everything currently
  tested runs without a database, which is fast and leaves the EF mappings unverified until
  startup.

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
- One aggregate per repository. Read paths never go through repositories — they project columns.
- Raw SQL in an index filter (`HasFilter("is_deleted = false")`) is correct only because of the
  snake_case convention. Renaming the property without renaming the string gives a migration that
  builds and an index that silently never applies.

### Notes for Windows

- If `dotnet test` fails with an x86 runtime error, an x86 .NET install is ahead of the x64 one on
  `PATH`. Remove it, or pass `--arch x64`.
- Don't keep the working copy inside OneDrive. It syncs `bin/` and `obj/` and causes intermittent
  file locks mid-build.
- After extracting an archive over the working copy, run `dotnet build --no-incremental` once.
  `Expand-Archive` preserves the timestamps stored in the zip, so an extracted file can look older
  than the last build output and MSBuild will skip recompiling it.
