# AutoParts ERP

An integrated ERP for **automotive parts distribution**, built as a modular monolith on .NET 8.

This repository is the foundation: the shared kernel, the module system, and one fully built
module (**Catalog**) that establishes the pattern every later module follows.

---

## Getting started

```bash
# 1. Start PostgreSQL (and pgAdmin at http://localhost:8081)
docker compose up -d

# 2. Restore and build
dotnet build

# 3. Create the first migration for the Catalog module
dotnet tool install --global dotnet-ef        # once per machine
dotnet ef migrations add InitialCatalog \
  --project src/Modules/Catalog/AutoPartsErp.Modules.Catalog.Infrastructure \
  --startup-project src/Api/AutoPartsErp.Api \
  --context CatalogDbContext \
  --output-dir Persistence/Migrations

# 4. Run it
dotnet run --project src/Api/AutoPartsErp.Api
```

Then open **https://localhost:7150/swagger**. In Development the app applies migrations and
seeds a small catalogue on start, so there is something to search immediately.

In VS Code, `Ctrl+Shift+B` builds, `F5` runs the API, and the task list (`Ctrl+Shift+P` →
*Run Task*) has entries for starting the database and scaffolding migrations.

### Not using Docker?

Point `ConnectionStrings:Erp` in `src/Api/AutoPartsErp.Api/appsettings.json` at any PostgreSQL
16 instance. Nothing else in the project assumes containers.

---

## Try it

```http
### Everything that fits a 2015 Golf VII
GET /api/catalog/parts/for-vehicle?make=Volkswagen&model=Golf VII&year=2015

### The number a customer read off the old part, typed with spaces
GET /api/catalog/parts?term=5Q0 698 151 A

### The same number typed without them - finds the same part
GET /api/catalog/parts?term=5q0698151a
```

`requests.http` at the repository root has the full set, ready to run with the REST Client
extension.

---

## Why it is shaped this way

### A modular monolith, not microservices

An ERP is one business with many departments, and those departments constantly need each other's
data in the same breath: a sales order checks stock, reserves it, prices it, and posts to the
ledger. Splitting that across services on day one buys distributed transactions, network
failure modes and a deployment pipeline, in exchange for scaling that nobody needs yet.

So: one deployable, hard internal boundaries. Each module owns its own database schema, its own
domain model and its own endpoints, and modules talk through published integration events rather
than by reaching into each other. When a module genuinely needs to scale on its own, it already
has a schema, a contract and an event surface — extracting it becomes a deployment change rather
than a rewrite.

The boundary is enforced by the project graph, not by discipline. `Catalog.Domain` **cannot**
reference `Inventory.Domain`, because the reference does not exist.

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

Business failures — a duplicate SKU, a part that is not ready to activate — return
`Result` / `Result<T>` carrying a stable error code like `catalog.part.sku_exists`. Exceptions
are reserved for bugs and infrastructure faults.

This matters more in an ERP than in most software: "this is not allowed" is an ordinary,
expected outcome that happens hundreds of times a day, and it should not cost a stack unwind or
get swallowed by a `catch`. Every error code maps to an HTTP status in exactly one place, so a
conflict is always a 409 and a broken domain rule is always a 422.

### Money and quantity are types

`decimal`, never `double`, and always paired with a currency or a unit. An ERP that loses a cent
per line loses trust, and `500` litres of oil received as `500` drums is a warehouse incident.
Arithmetic across different currencies or units is rejected rather than guessed at.

### Multi-tenant and auditable from the first table

Every record carries a tenant, a created/modified stamp and an archive flag, applied by an
interceptor and enforced by global query filters. These are close to impossible to retrofit into
a system that already has data, and free to include now.

Deletes are archival. A part referenced by ten years of invoices is never physically removed.

---

## Automotive specifics already modelled

These are the things that make parts distribution its own problem rather than generic inventory:

**Part numbers are stored twice.** Bosch print `0 986 424 815`; the price file says
`0986424815`; the customer reads `0986-424-815` off an old box. Every part number keeps its
printed form for documents and a normalized form (letters and digits, uppercase) for every
lookup and index. Searching on the normalized form is the difference between a counter system
people trust and one they work around.

**Cross-references.** A mechanic quotes the OEM number and expects the aftermarket equivalent on
the shelf. Parts carry OEM, competitor, supersession, interchange and trading-partner numbers,
each indexed on its normalized form.

**Fitment.** Nobody asks for part `BP-1188`; they ask for front pads for a 2014 Golf 2.0 TDI.
Parts carry vehicle applications with make, model, engine code, year range and fitting position.
Wrong-fit parts are the most expensive returns in the business, so position is part of the
identity of an application, not a note on it.

**Core charges.** Remanufactured starters, alternators and calipers are sold against a
returnable core with a refundable deposit. The flag and the deposit live on the part, and a part
sold against a core cannot go live without one.

**Dangerous goods.** Brake fluid, batteries, airbags and aerosols carry a UN number and cannot be
flagged as dangerous without it. Weight and dimensions drive carrier rating and pallet building.

**Lifecycle is one-way.** `Draft → Active → Discontinued → Obsolete`. A discontinued part is still
sold down and still supported for warranty, but is no longer purchased. A part that has been live
can never go back to draft, and its stocking unit freezes on activation — every quantity, cost
and open order is denominated in it.

---

## Layout

```
AutoPartsErp.sln
├── src
│   ├── Api/AutoPartsErp.Api                 composition root; wires modules, serves HTTP
│   ├── Shared
│   │   ├── AutoPartsErp.SharedKernel        entities, value objects, Result, CQRS contracts
│   │   └── AutoPartsErp.Modules.Abstractions IModule, pipeline behaviours, event bus, DI
│   └── Modules
│       └── Catalog                          parts, brands, categories, cross-refs, fitments
│           ├── ....Domain
│           ├── ....Application
│           ├── ....Infrastructure
│           └── ....Presentation
└── tests
    ├── AutoPartsErp.SharedKernel.Tests
    └── AutoPartsErp.Modules.Catalog.Tests
```

### The pieces worth knowing about

| Thing | Where | What it does |
|---|---|---|
| `IModule` | `Modules.Abstractions` | The whole contract between the host and a module |
| `Dispatcher` | `SharedKernel/Messaging` | A ~40-line mediator, so no third-party licence can strand the codebase |
| `IPipelineBehavior` | `SharedKernel/Messaging` | Cross-cutting steps; logging and validation ship with it |
| `IEventBus` | `SharedKernel/Messaging` | In-process today; swapping in a broker is one registration |
| `Result` / `Error` | `SharedKernel/Results` | Expected failures as values with stable codes |
| `AuditingInterceptor` | `Catalog.Infrastructure` | Stamps who/when, assigns tenant, archives instead of deleting |
| `ICatalogReadStore` | `Catalog.Application` | The read side; grids never load aggregates |

---

## Adding the next module

1. Create four projects under `src/Modules/<Name>/` mirroring Catalog.
2. Implement `IModule`; claim a schema name no other module uses.
3. Reference the new `.Presentation` project from `AutoPartsErp.Api`.
4. Add one line to `Program.cs`:

```csharp
builder.Services.AddErpModules(
    builder.Configuration,
    new CatalogModule(),
    new InventoryModule());   // <- that is the whole integration
```

Nothing else in the host changes. If a module needs to react to something another module did, it
subscribes to that module's integration event; it never adds a project reference.

---

## Roadmap

**Next, in rough dependency order:**

1. **Inventory** — stock by location and bin, reservations, movements, stock take. Subscribes to
   `PartActivated` to open stock records.
2. **Partners** — customers and suppliers, addresses, credit limits, price tiers.
3. **Purchasing** — supplier price files, purchase orders, goods receipt, landed cost.
4. **Sales** — quotes, counter sales, orders, allocation, picking, invoicing, returns and core
   credits.
5. **Pricing** — cost methods, margin rules, customer-specific and quantity-break pricing.
6. **Finance** — AR/AP, general ledger, VAT, period close.

**Foundation work that will be wanted along the way:**

- Authentication and authorisation (the tenant currently comes from a header; `ITenantContext`
  is the seam where a validated token replaces it).
- A proper vehicle taxonomy. Fitment is deliberately flat for now; the industry shapes are
  **TecDoc** in Europe and **ACES/PIES** in North America, and moving to a normalized vehicle
  tree is a planned migration rather than an afterthought.
- Full-text and fuzzy part search using PostgreSQL `pg_trgm`, for partial and mistyped numbers.
- An outbox table, so integration events survive a process crash between commit and publish.
- Integration tests against a real PostgreSQL container (Testcontainers).
- Document numbering sequences per tenant and document type.

---

## Conventions

- **C# 12**, nullable enabled, warnings as errors in `src`.
- File-scoped namespaces, `_camelCase` private fields, explicit types over `var` where the type
  is not obvious. Enforced by `.editorconfig` at build time.
- Database identifiers are `snake_case`, applied automatically — the database is meant to be
  pleasant to query directly, because finance staff and integrations will.
- Package versions live in `Directory.Packages.props` only. Never put a `Version` on a
  `PackageReference`.
- One aggregate per repository. Read paths never go through repositories.
