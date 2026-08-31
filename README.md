# AutoParts ERP

An integrated ERP for **automotive parts distribution**, built as a modular monolith on .NET 8.

Two modules are complete: **Catalog** (parts, brands, cross-references, vehicle fitment) and
**Inventory** (warehouses, stock balances, reservations, movement ledger). They exchange
integration events and share no code beyond a contracts assembly.

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
dotnet test

# EF tooling, once per repository
dotnet tool restore

# One migration per module - each owns its own schema and its own history table
dotnet ef migrations add InitialInventory \
  --project src/Modules/Inventory/AutoPartsErp.Modules.Inventory.Infrastructure \
  --startup-project src/Api/AutoPartsErp.Api \
  --context InventoryDbContext --output-dir Persistence/Migrations

dotnet ef migrations add InitialCatalog \
  --project src/Modules/Catalog/AutoPartsErp.Modules.Catalog.Infrastructure \
  --startup-project src/Api/AutoPartsErp.Api \
  --context CatalogDbContext --output-dir Persistence/Migrations

dotnet run --project src/Api/AutoPartsErp.Api
```

Open **http://localhost:5150/swagger**. In Development the app applies migrations and seeds
warehouses, brands, categories and three parts on start, so there is something to query
immediately.

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

### Everything at or below its reorder point, deepest shortfall first
GET /api/inventory/stock/replenishment
```

`requests.http` at the repository root has the full set for the REST Client extension.

---

## Why it is shaped this way

### A modular monolith, not microservices

An ERP is one business with many departments, and those departments constantly need each other's
data in the same breath: a sales order checks stock, reserves it, prices it, and posts to the
ledger. Splitting that across services on day one buys distributed transactions, network failure
modes and a deployment pipeline, in exchange for scaling nobody needs yet.

So: one deployable, hard internal boundaries. Each module owns its own database schema, its own
domain model and its own endpoints, and modules talk through published integration events rather
than by reaching into each other. When a module genuinely needs to scale on its own, it already
has a schema, a contract and an event surface — extracting it becomes a deployment change rather
than a rewrite.

The boundary is enforced by the project graph, not by discipline. `Inventory.Domain` **cannot**
reference `Catalog.Domain`, because the reference does not exist.

### How modules actually talk

This is the part worth understanding, because everything later depends on it.

```
Catalog                                    Inventory
───────                                    ─────────
Part.Activate()
  raises PartActivatedDomainEvent
        │
        │  (after the transaction commits)
        ▼
PublishPartActivated
  translates it to
  PartActivatedIntegrationEvent ──────►  OpenStockRecordOnPartActivated
        │                                  opens a zero balance in every
        │                                  active warehouse
   IEventBus                                      │
   (in-process today)                             ▼
                                           inventory.stock_items
```

Three deliberate steps:

**Domain events stay private.** `PartActivatedDomainEvent` names Catalog's own types and changes
whenever the aggregate changes. No other module ever sees one.

**A translation step publishes a contract.** `PublishPartActivated` converts it into a record of
primitives in `AutoPartsErp.IntegrationEvents`. Without that seam, the first module to subscribe
directly to Catalog's domain event welds itself to Catalog's internals, and every later change
ripples outward.

**Dispatch happens after commit.** A subscriber can never react to an activation that later rolls
back.

The event carries the stocking unit rather than making Inventory ask for it. An event that forces
the consumer to call the publisher back is not really decoupled.

Consumers must be idempotent. The in-process bus can deliver twice on a retry, and any real
broker guarantees at-least-once — so opening a balance that already exists is treated as success,
not as a conflict.

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
`inventory.stock.insufficient_available`. Exceptions are reserved for bugs and infrastructure
faults.

This matters more in an ERP than in most software: "this is not allowed" is an ordinary, expected
outcome that happens hundreds of times a day, and it should not cost a stack unwind or get
swallowed by a `catch`. Every error code maps to an HTTP status in exactly one place, so a
conflict is always a 409 and a broken domain rule is always a 422.

### Money and quantity are types

`decimal`, never `double`, and always paired with a currency or a unit. An ERP that loses a cent
per line loses trust, and 500 litres of oil received as 500 drums is a warehouse incident.
Arithmetic across different currencies or units is rejected rather than guessed at.

### Multi-tenant and auditable from the first table

Every record carries a tenant, a created/modified stamp and — where it applies — an archive flag,
applied by an interceptor and enforced by global query filters. These are close to impossible to
retrofit into a system that already has data, and free to include now.

Deletes are archival. A part referenced by ten years of invoices is never physically removed.

---

## The domain

### Catalog

**Part numbers are stored twice.** Bosch print `0 986 424 815`; the price file says `0986424815`;
the customer reads `0986-424-815` off an old box. Every part number keeps its printed form for
documents and a normalized form (letters and digits, uppercase) for every lookup and index.
Searching on the normalized form is the difference between a counter system people trust and one
they work around.

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
sold down and still supported for warranty, but no longer purchased. A part that has been live can
never go back to draft, and its stocking unit freezes on activation — every quantity, cost and open
order is denominated in it.

### Inventory

**Three quantities, never conflated.** *On hand* is what is physically on the shelf, *reserved* is
how much of that is already promised, *available* is the difference — the only number a
salesperson should ever be shown. A part with 10 on hand and 10 reserved is not "in stock".
Replenishment fires off available, not on hand, because stock already going out the door cannot
fill the next order.

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
something physically impossible and every downstream valuation inherits the lie. A busy trade
counter that sells before the paperwork catches up may switch it on deliberately.

---

## Layout

```
AutoPartsErp.sln
├── src
│   ├── Api/AutoPartsErp.Api                  composition root; wires modules, serves HTTP
│   ├── Shared
│   │   ├── AutoPartsErp.SharedKernel         entities, value objects, Result, CQRS contracts
│   │   ├── AutoPartsErp.Modules.Abstractions IModule, pipeline behaviours, event bus, DI
│   │   └── AutoPartsErp.IntegrationEvents    cross-module contracts. Records only.
│   └── Modules
│       ├── Catalog                           parts, brands, cross-refs, fitments
│       └── Inventory                         warehouses, stock, reservations, ledger
│           ├── ....Domain
│           ├── ....Application
│           ├── ....Infrastructure
│           └── ....Presentation
└── tests
    ├── AutoPartsErp.SharedKernel.Tests
    ├── AutoPartsErp.Modules.Catalog.Tests
    └── AutoPartsErp.Modules.Inventory.Tests
```

### The pieces worth knowing about

| Thing | Where | What it does |
|---|---|---|
| `IModule` | `Modules.Abstractions` | The whole contract between the host and a module |
| `Dispatcher` | `SharedKernel/Messaging` | A ~40-line mediator, so no third-party licence can strand the codebase |
| `IPipelineBehavior` | `SharedKernel/Messaging` | Cross-cutting steps; logging and validation ship with it |
| `IEventBus` | `SharedKernel/Messaging` | In-process today; swapping in a broker is one registration |
| `Result` / `Error` | `SharedKernel/Results` | Expected failures as values with stable codes |
| `IntegrationEvents` | `src/Shared` | The only assembly modules share |
| `StockItem` | `Inventory.Domain` | The consistency boundary for every stock change |
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
    new InventoryModule(),
    new CatalogModule(),
    new PurchasingModule());   // <- that is the whole integration
```

Nothing else in the host changes. If a module needs to react to something another module did, it
subscribes to an integration event; it never adds a project reference.

`IModule.Order` controls registration and seeding order. Inventory is 5 and Catalog is 10, because
warehouses must exist before Catalog activates a part.

---

## Roadmap

**Done:** Catalog, Inventory.

**Next, in rough dependency order:**

1. **Partners** — customers and suppliers, addresses, credit limits, price tiers.
2. **Purchasing** — supplier price files, purchase orders, goods receipt, landed cost. This is the
   first consumer of `StockFellBelowReorderPointIntegrationEvent`, which Inventory already
   publishes and nobody listens to yet.
3. **Pricing** — cost methods, margin rules, customer-specific and quantity-break pricing.
4. **Sales** — quotes, counter sales, orders, allocation, picking, invoicing, returns and core
   credits.
5. **Finance** — AR/AP, general ledger, VAT, period close.

**Known issues:**

- A malformed `warehouseId` in a request body returns 500 rather than 400. Bad client input
  should never surface as a server error.

**Foundation work wanted along the way:**

- Authentication and authorisation. The tenant currently comes from an `X-Tenant-Id` header;
  `ITenantContext` is the seam where a validated token replaces it.
- An outbox table, so integration events survive a process crash between commit and publish.
  Today a crash in that window loses the event silently.
- Stock valuation and costing (FIFO or weighted average) on the movement ledger, which already
  carries a unit cost column for it.
- A proper vehicle taxonomy. Fitment is deliberately flat for now; the industry shapes are
  **TecDoc** in Europe and **ACES/PIES** in North America.
- Full-text and fuzzy part search using PostgreSQL `pg_trgm`, for partial and mistyped numbers.
- Integration tests against a real PostgreSQL container (Testcontainers).
- Document numbering sequences per tenant and document type.

---

## Conventions

- **C# 12**, nullable enabled, warnings as errors in `src`.
- File-scoped namespaces, `_camelCase` private fields, explicit types over `var` where the type is
  not obvious. Enforced by `.editorconfig` at build time.
- `AnalysisLevel` is `latest-recommended`, so an SDK update can introduce new rules and break a
  build that worked yesterday. Pin it if that becomes disruptive.
- EF-generated migrations are exempt from the style rules — they are rewritten on every scaffold.
- Database identifiers are `snake_case` via `EFCore.NamingConventions`. The database is meant to be
  pleasant to query directly, because finance staff and integrations will.
- Package versions live in `Directory.Packages.props` only. Never put a `Version` on a
  `PackageReference`.
- One aggregate per repository. Read paths never go through repositories.

### Notes for Windows

- If `dotnet test` fails with an x86 runtime error, an x86 .NET install is ahead of the x64 one on
  `PATH`. Remove it, or pass `--arch x64`.
- Don't keep the working copy inside OneDrive. It syncs `bin/` and `obj/` and causes intermittent
  file locks mid-build.
