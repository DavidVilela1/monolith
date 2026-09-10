# AutoParts ERP

An integrated ERP for **automotive parts distribution**, built as a modular monolith on .NET 8.

Nine modules are in place and talking to each other, including the one Portuguese law cares
about:

| Module | Schema | Order | What it owns |
|---|---|---|---|
| **Access** | `access` | 0 | Who may use the system: users, the roles they hold, and the permissions those carry |
| **Partners** | `partners` | 1 | Customers and suppliers, addresses, contacts, credit limits, trading status |
| **Inventory** | `inventory` | 5 | Warehouses, balances, reservations, expected deliveries, valuation, counts, transfers, the movement ledger |
| **Catalog** | `catalog` | 10 | Parts, brands, categories, cross-references, vehicle fitment |
| **Pricing** | `pricing` | 12 | Price lists, quantity breaks, customer agreements, price resolution |
| **Purchasing** | `purchasing` | 15 | Purchase orders, goods receipt, replenishment suggestions |
| **Sales** | `sales` | 20 | Customer accounts, sales orders, dispatch, credit control, customer returns |
| **Invoicing** | `invoicing` | 25 | Registered series, ATCUD, the signature chain, the QR code, the SAF-T (PT) export |
| **Finance** | `finance` | 30 | The sales ledger: open items, receipts matched to the documents they pay, ageing |

They share no code beyond two contract assemblies, and no module references another module's
projects. 673 tests, all green — 635 that need nothing but the compiler, and 38 that need a real
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
schema. In Development the host applies all nine on start, then seeds warehouses, brands,
categories, parts and a few partners — so there is something to query immediately.

Access is the one module whose seeding is not a development convenience: it creates the starting
roles and the first administrator wherever it runs, because a database with no users is a system
nobody can sign in to.

Sales and Pricing are deliberately not seeded. A customer account is not Sales' to invent: it
arrives as an event when Partners grants the customer role, so the seeded partners populate it
through the outbox on the first run. A price list is a commercial decision, and inventing a
default one would be inventing what the company charges.

### Configuring Access

`Erp:Access` carries the signing key and the first administrator.

| Setting | What it is |
|---|---|
| `SigningKey` | What every token is signed with. At least 32 characters, and a secret. |
| `AccessTokenMinutes` | How long a token is accepted. 15 by default, and short on purpose. |
| `RefreshTokenDays` | How long a session survives without a password. 14 by default. |
| `BootstrapAdminEmail` | The first administrator, created only when there are no users at all. |
| `BootstrapAdminPassword` | Their password. They must change it on first sign-in. |

**Whoever holds `SigningKey` can mint a token for anybody with any permission.** It belongs in
user secrets or an environment variable, never in a file that goes into version control. It is
validated at startup and the application refuses to start without one, because a deployment that
cannot authenticate anybody should say so while somebody is still watching the console.

The bootstrap administrator exists because a database with no users is a system nobody can sign in
to, and the screen that would create the first user is behind the sign-in. It is created once, on
an empty users table, and never again — a company with an administrator has somebody who can
create the rest.

Six roles are created alongside it: **Administrator**, **Counter sales**, **Warehouse**,
**Warehouse manager**, **Buyer** and **Accounts**. They are real jobs rather than tidy
abstractions, and everything except the administrator can be renamed, re-permissioned or deleted,
because no two distributors divide the work the same way.

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
### Sign in. Everything else needs the token this returns.
POST /api/access/sign-in
{ "email": "admin@autopecas.local", "password": "..." }

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

**A sweep claims its batch before delivering it.** One statement takes the next batch with
`FOR UPDATE SKIP LOCKED` and stamps a lease on it, so a second host running the same sweep steps
over those rows and takes the next ones instead of delivering everything a second time. The lease
is written into `next_attempt_at_utc` — the column that already means "do not look at this
before" — which is what makes a killed host recover on its own: nothing has to notice the crash,
the messages simply become visible again when the lease runs out.

**Delivered rows are deleted eventually; undelivered ones never are.** Housekeeping keeps thirty
days of delivered messages and ninety days of inbox records. The two numbers are different on
purpose, and the longer one is the inbox: an inbox row is what stops a redelivery being applied
twice, so it has to outlive the publisher's copy of the message it guards against. The host
refuses to start if they are configured the other way round.

| `Erp:Outbox` | What it is for |
|---|---|
| `PollInterval` | How long to wait after a sweep that found nothing. Five seconds. |
| `BatchSize` | Messages claimed per sweep. Fifty. |
| `MaxAttempts` | Failures before a message is left for a person. Ten. |
| `MaxBackoff` | The longest gap between retries. Ten minutes. |
| `ClaimLease` | How long a claimed batch stays invisible to other hosts. Five minutes. |
| `ProcessedRetention` | How long a delivered message is kept. Thirty days; zero keeps everything. |
| `HandledRetention` | How long an inbox record is kept. Ninety days, and never less than the above. |
| `RetentionInterval` | How often housekeeping runs. Six hours. |

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

### Access

**Everything is closed by default.** The API's fallback policy requires an authenticated caller,
so a route added later without a thought about who may call it is refused rather than open. Three
routes say otherwise — sign in, refresh, sign out — because they are where a token comes from.

**Every one of the 152 routes behind a permission names which one.** Not a group-wide check per module:
`GET /api/inventory/stock/replenishment` needs `inventory.stock.read` and
`POST /api/inventory/stock/adjust` needs `inventory.stock.adjust`, and they sit four lines apart
in the same file. The catalogue lives in the shared kernel rather than in Access, and it has to —
Access decides who holds a permission, the other eight modules decide which one each route needs,
and no module in this system references another's projects.

**The tenant is a claim, not a header.** There used to be an `X-Tenant-Id` header here, which was
a way of saying "I am company B" that company A could also say: anybody who could reach the API
could read anybody's data by changing one header. A claim inside a signed token cannot be edited
by whoever is holding it.

**Permissions, grouped into roles.** Thirty-seven permissions named `module.thing.verb`, and roles
are named bundles of them. Permissions live on the role rather than on the user, so giving
somebody an exception means giving them a second role — which keeps "why can she do this?" to a
list of role names instead of an audit of one person's history.

**Counting stock and accepting the count are different permissions.** So are drafting a document
and issuing one, and raising a purchase order and committing the company to it. Every one of those
splits exists because the two halves are different acts with different consequences, and a single
permission would make the separation of duties this system keeps writing about unenforceable.

**Quoting a price and reading the price list are different permissions.** Quoting answers one
question about one part for one customer, which is the counter's whole job. Reading a list is
every price the company charges — the one document a competitor would most like a copy of. The
same argument puts closing a transfer short behind `inventory.stock.adjust` rather than
`inventory.transfer.manage`: accepting that stock never arrived is writing it off, and the driver
should not be the one who signs that.

**Going past a credit limit is its own route, not a flag.** `POST /orders/{id}/confirm-over-limit`
requires both `sales.order.confirm` and `sales.credit.override`, and the order records that it was
let through. A flag on the ordinary route would have put the check inside a handler, where nobody
reading the endpoint can see it. The override moves the limit and nothing else: an account on hold
or closed is still refused, because a limit is a number somebody chose and a hold is a decision
about the relationship.

**There is no `finance.terms.manage`.** Payment terms sit on the partner, on the same value object
as the credit limit and written by the same call, and `partners.credit.manage` already names that
job. Somebody allowed to set ninety-day terms but not the limit has half a lever. That permission
now also covers granting the customer role, which used to sit behind `partners.partner.manage` —
the route carries a credit limit, and correcting an address should not decide how much the company
will let somebody owe.

**Nothing cryptographic is hand-written.** Passwords go through the ASP.NET Core `PasswordHasher`
(PBKDF2, per-password salt, a version byte for the day the parameters change); tokens are signed
with the standard libraries. Both live behind one interface each in the infrastructure, so what a
reviewer has to look at hard is findable, and neither the domain nor the use cases can quietly do
any of it themselves.

**Sign-in tells you nothing you did not already know.** No account, wrong password and closed
account produce one identical error — telling them apart turns the endpoint into a way of
discovering who has an account here. The hash is verified even when there is no account, because
otherwise the endpoint answers unknown addresses faster than known ones and the timing says what
the message would not.

**Five wrong passwords lock the account for fifteen minutes.** Long enough that guessing stops
being worth doing, short enough that somebody who fat-fingered their own password goes for a
coffee.

**Access tokens last fifteen minutes; sessions last a fortnight.** A signed token cannot be
withdrawn, so its lifetime is exactly how long somebody keeps working after their access is taken
away. A refresh handle behind it makes the window short without making people sign in all day.
Each handle works once: presenting one twice ends every session that user has, because it means
either a stolen copy or a client bug.

**An account is closed, never deleted.** Their name is on documents that have to stay explicable
for ten years.

**The last administrator cannot be removed.** A company that takes away the last account holding
`access.role.manage` cannot give it back — the screen that would do it is behind the permission
nobody has — and the only remedy is somebody with a database client.

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

**A return lands at what it cost when it left.** Not at today's average: a part sold in March at
€40 and returned in September onto a shelf that has since averaged down to €31 would come back
worth nine euros more than it cost, and the difference would be a silent profit on a transaction
where the company made nothing. The figure is looked up in this module's own ledger, from the row
the dispatch wrote — Sales never carries a cost, because costing belongs to the module that owns
the shelf. The value is taken as a fraction of what left rather than as a unit cost multiplied
back up, so it rounds once.

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

**What was agreed is data, not something somebody retypes.** A supplier agreement carries the
currency, the rebate and its life; a supplier price carries what one part costs from a given day.
Both are entered once, by the person who negotiated them, and every delivery afterwards prices
itself from them. The two figures on a purchase document worth arguing about are the ones both
sides settled months apart, and a person retyping them is a person who will eventually retype one
of them wrong.

**A price is never edited when it changes.** The supplier announces a rise from the first of
October, the buyer records a second row, and both survive: the old one explains the invoices
already received and the new one prices everything after. There is a `Correct` for the afternoon
somebody notices the 4,50 should have read 45,00, and it is for mistakes, not for changes.

**A rebate scale is one object for both shapes of rappel.** A flat agreed percentage is a scale
with one step starting at nothing, so "desconto de rappel 4%" and a three-step volume ladder are
the same arithmetic rather than two code paths that will disagree in the fifth year.

**The step reached pays on everything, not only on the excess.** 48.000 against a scale of
25.000 → 2% and 50.000 → 3% earns 2% of 48.000. Two thousand euros more and it becomes 3% of the
whole 50.000, first 25.000 included. That is how a distribution rappel is written in this market,
and it is why `ToNextStep` exists: "another 1.850 and the whole year goes to three per cent" is a
decision, and "your rebate is two per cent" is not. A marginal scale, where each slice keeps its
own rate, is a different contract and this object does not express it.

**Each step has to pay more than the one below it.** A scale that pays less for buying more is
percentages typed into the wrong rows, and letting it through stops a buyer ordering at exactly
the wrong moment for a reason nobody can see.

**A rebate settled by credit note takes nothing off the invoice.** The company pays the full
figure and is credited later, so `InvoiceRateOn` answers zero for it. Applying the rate in both
places is how the same discount gets taken twice, and it is the kind of error that shows up as an
unexplained gap on a supplier's statement eleven months later.

**A supplier invoice is their document, not ours.** It never touches the certified series in
Invoicing and never gets a number of ours — numbering it would be forging it. What is stored is
what they sent, alongside what the system worked out they should have sent. Both figures stay:
overwriting ours with theirs would be the system agreeing with the supplier and then losing the
evidence that it ever thought otherwise.

**It is drafted from receipts, one per supplier per delivery day.** A supplier invoices a
delivery, and a delivery is a van on a day. Grouping by order would split one van into three
documents whenever the warehouse had ordered twice; grouping by nothing would leave a draft
accumulating for a month. The warehouse counting the pallet stays a person's job — a person is
the only thing that can tell a full box from an empty one — and nothing else is typed.

**The rebate rate is stamped when the draft opens and never recomputed.** It depends on what the
period had bought when the delivery arrived. A figure that quietly followed the year's running
total would make a document raised in March re-explain itself with November's numbers every time
somebody opened it.

**VAT comes after the rebate, per rate.** A rebate reduces the taxable amount, so VAT charged on
the pre-rebate figure is VAT the company would be deducting without having paid it. A van carrying
parts at 23 and books at 6 is ordinary, so the rebate is spread across the bands in proportion to
what each is worth — one blended percentage would match neither the supplier's document nor the
return the company has to file.

**Automatic entry is not automatic acceptance.** The whole reason for computing the figure
independently is to have something to disagree with. Their number, their date and their total are
the only things entered; if the two totals agree the document settles, and if they do not it goes
into dispute owing nothing to anybody. The tolerance is for the cents both sides round differently
— a tolerance of zero would put every delivery in front of somebody to approve a cent, which is
how people learn to approve everything without looking.

**Leaving a dispute has two doors, and both leave a trace.** Accepting their figure needs a
sentence somebody wrote, because in a year that sentence is the only thing that will explain
paying more than was counted at prices that were agreed. Or the line is corrected to their price —
which does not touch the agreed price. A supplier overcharging on one delivery is a conversation
about that delivery; a supplier who has genuinely raised their prices is a new agreed price with a
date on it. Otherwise one unchallenged invoice quietly becomes the new contract.

**What is owed is their figure, not ours.** A payable opened for the computed total would never
match the money leaving the bank, and reconciling those two afterwards is the job this whole
document exists to avoid.

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

**A line records what the shelf was worth when it was priced.** Not what the sale cost: what a
dispatch actually takes off the balance is stamped on the ledger row at the moment it happens, and
the two differ whenever the shelf moves in between. The line's figure is what the decision was
made against — "was that price worth taking?" — and it stays what it was, so the answer keeps
reading the same way next month. Anything reconciling margin to the accounts wants the ledger;
anything deciding a price wants the line.

**A cost nobody knows is not a cost of zero.** Inventory answers null for a part with no record in
that warehouse, an empty shelf, or one that has never been through a priced receipt — and the line
then has no margin rather than a margin of a hundred per cent. The order says how many of its
lines are in that state, because a margin figure quietly covering four lines of six is a number
somebody decides on and then cannot reproduce.

**Margin is of revenue, not of cost.** A part bought at 10 and sold at 15 is a third of the
selling price and half the buying price, and both get called "fifty per cent" by somebody. This
one is of revenue, which is what a distributor's accounts are built on.

**A core deposit is outside the margin on both sides.** It is revenue with no cost, and counting
it would make every part sold on a core look like the best margin in the branch — right up until
the old unit comes back and the money goes out again.

**The margin floor lives on the price list, with a company figure behind it.** A promotion sold
deliberately thin and a fleet contract where two per cent on volume is the whole point are lists
with an opinion; most lists have none and fall back to `Erp:Pricing:DefaultMinimumMarginPercent`.
A customer with an agreement whose list says nothing falls back to the company figure and not to
the default list's — the default list is where walk-ins land, and borrowing its floor for a
negotiated account would apply a number chosen for strangers to somebody the company signed with.

**No floor is not a floor of zero, and the difference is what makes upgrading safe.** Null means
nobody has made this decision, and nothing is refused. Zero means somebody made it and the answer
is "never below cost" — which is a real policy and a real refusal, and not one a system should
adopt on an installation's behalf the first time it is updated. Clearing obsolete stock at a loss
is something a distributor does on purpose.

**The floor is compared in money, never in the percentage people read.** `MarginPercent` is
rounded to two places for a screen, and a line making 29.995 per cent displays as 30.00. A check
written against the displayed figure would let that line past a floor of 30 — which is exactly the
kind of hole somebody finds by accident and then starts using on purpose. Multiplying out is
exact.

**A line Inventory cannot cost is a line the floor says nothing about.** Refusing every part with
no stock record would stop a counter dead on the first part a warehouse has never received. The
line reports no margin, the order reports that it has uncosted lines, and nobody is blocked on a
figure nobody has. The round trip into Pricing is not even made.

**Going under the floor is its own route, like going over the credit limit.**
`POST /orders/{id}/lines/below-floor` and `PUT /orders/{id}/lines/{lineId}/pricing-below-floor`
need `sales.margin.override` on top of `sales.order.manage`, and the line records that it was let
through. The flag is a record of one decision, not an exemption: pricing the line back above the
floor clears it, because a flag that stayed set would wave the next edit past unexamined.

**`sales.margin.override` is not `sales.credit.override`.** One is about whether the company gets
paid, the other about whether the sale was worth making, and the people trusted with each are not
always the same people.

**A core deposit is a line, not a field.** A remanufactured starter motor is sold twice over: the
part, and a sum held until the old one comes back. The deposit prints as its own line, is credited
on its own, and is what a customer pays when they keep the old unit — none of which a number
tucked onto the goods line can express. Adding a part the catalogue marks as sold on a core adds
both lines or neither.

**The deposit is money, not stock.** Nothing is reserved for it, nothing is picked, and the ledger
never hears about it. It is dispatched when the part is, because that is when the deposit is
charged — without that it would be a line nobody ever dispatched and therefore a line nobody could
ever invoice, and the company would hand over a starter motor and forget the thirty euros. It
carries the part's VAT rate: a deposit taken at one rate and given back at another leaves the
company holding the difference.

**A returned core is its own disposition.** Not back to stock — a used starter motor is not the
remanufactured one that was sold — and not scrap either, because it goes to the remanufacturer and
is worth money to somebody. Like a scrapped line, nothing reaches Inventory: what the company does
with a pile of old cores is a supplier conversation this system does not have yet.

**A return is a document, not a credit note with stock attached.** A credit note can be issued
because the price was wrong, because the invoice went to the wrong company, or because a discount
was agreed after the fact — none of which involve a single part moving, and a system that put
stock back on every credit note would invent stock. So goods coming back are their own fact and
the credit follows from them.

**Raising a return and receiving it are two steps.** Raising records what the customer says is
coming; receiving records what actually turned up, and that is the step that moves stock. A
counter return does both in the same minute — but a workshop ringing to say a pump is coming back
next Tuesday must not put a pump on the shelf for a salesperson to promise somebody on Monday.

**Not everything that comes back goes back on the shelf.** Each line says what was decided about
it: a sealed box returns to stock, a fitted and scratched alternator does not. The customer is
credited either way, because that is a conversation with them and not a fact about the shelf. Only
the saleable lines reach Inventory — booking a scrapped part in and adjusting it straight back out
would put two movements in the ledger for stock that was never on a shelf.

**The credit note is drawn from the return, and names its document.** An order billed across
three invoices could have a return credited against any of them, and picking one would be this
system choosing which legal document to reverse. A credit note names exactly one original — that
is what the tax authority reads — so the person raising it names it too. The lines are matched on
the sales order line that both documents carry: the invoice line copied it when the document was
drawn, and the return line has it because the goods came off that line. Neither module knows the
other's keys.

**A return produces one credit note, and the database says so.** The check is a unique index on
the return reference, not a read followed by a write — two people reaching for the same return in
the same second are separated by PostgreSQL rather than by the order two queries happened to fall
in. Drafts count: a draft is a credit note somebody is in the middle of, and abandoning it
unblocks the return.

**Crediting an order is not invoicing more of it.** Credit note lines carry the invoice line they
credit, not the order line the original was drawn from — so nothing is reported back to Sales as
billed, and an order credited in full does not look over-invoiced. Sales hears about the credit
through a different event, and all it does with it is mark the return settled.

**Goods that never left cannot come back.** The order line counts what has been returned against
what was dispatched, and the two aggregates move in the same transaction. The dispatched figure
itself does not go down: a line that quietly un-dispatched itself would make the order outstanding
again, put it back on the picking list, and promise the customer goods they have just sent back.

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
| `OutboxProcessor<T>` | `Persistence/Outbox` | One background sweep per module, claiming and draining that module's table |
| `OutboxRetentionService<T>` | `Persistence/Outbox` | Deletes delivered messages and old inbox records; never anything still owed |
| `InboxMessage` | `Persistence/Inbox` | What a consumer has already handled, so redelivery is free |
| `DatabaseExceptionHandler` | `Api/Infrastructure` | Turns a lost race into 409 instead of 500 |
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

**Done:** all nine modules, including authentication and per-route permissions. Transactional
outbox and consumer inbox, safe on more than one host and pruned on a schedule. Six module query
contracts. Invoicing end to end, including partial invoicing over the Sales bridge and the SAF-T
(PT) export. Stock valued at moving weighted average, with count sheets and inter-warehouse
transfers. An integration suite against real PostgreSQL. Document numbering that survives
concurrency in all three modules that hand out numbers.

**Next, in rough dependency order:**

1. **Communicating documents to the AT.** The webservice that reports each document within days
   of issuing it. The paperwork around certification is paperwork; this is the last piece of code
   between here and a legally usable installation.
2. **Accounts payable, the general ledger, VAT returns and period close.** The supplier invoice
   now exists and settles into a figure somebody owes, which was the missing document between a
   purchase order and a goods receipt. What is not built is the other end: nothing in Finance
   opens a payable from it, nothing ages what the company owes, and there is still no general
   ledger for either side to post to.

**Known issues:**

- A user belongs to one company. Somebody who works for two in a group needs two logins.
- A permission taken away from a role reaches somebody already signed in only when their access
  token expires — up to fifteen minutes. Shortening that further, or checking the database on
  every request, is the trade nobody has needed to make yet.
- Signing out withdraws the refresh handle but cannot withdraw the access token, which stays
  valid for its remaining minutes. That is what a signed token is.
- The confirmation that goes past a credit limit records that it did, and not by how much. The
  limit lives on the account and the account moves on; reconstructing the gap months later means
  reading the order total against a limit that has since changed twice.

- Purchase order lines have no concurrency token of their own, so two people editing different
  lines of the same order can still conflict at the aggregate level.
- Losing a race now returns 409 rather than 500, but the message is generic: it says something
  with that value already exists without saying which field. The constraint name is in the log
  and not in the response, because mapping index names to field names is a table somebody has to
  maintain and get wrong.
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
  there is no general ledger.
- The margin floor is checked when a line is added and when it is re-priced, and never again. A
  line raised above the floor and then discounted through the quantity route, or one whose floor
  was raised after the order was taken, is not re-tested. Confirming an order is where a
  whole-order check would belong, and there is none.
- A line that overrode the floor records that it did, and not by how much or against what figure.
  The floor lives on a price list that can be changed the next morning, so reconstructing "how far
  under was it?" months later means reading a margin against a number that has since moved — the
  same gap the credit-limit override has.
- A return is valued proportionally across every shipment of the line it came from. A line
  dispatched twice at two costs has no single unit cost, and the customer bringing three of ten
  back does not say which van they came on — so the figure is the average of what left, and there
  is no more precise one to be had short of serial numbers.
- The supplier agreement, the agreed prices and the supplier invoice are domain and tests only.
  Nothing persists them, no route reaches them, and no goods receipt drafts one yet — the wiring
  is the next delivery. The shapes were settled first on purpose: a rebate scale is cheaper to
  argue about before it has a table and eleven endpoints hanging off it.
- A rebate settled on the invoice is taken at the rate the period had reached on the day. A rate
  reached in October cannot be applied to invoices that went out in March, so crossing a step
  leaves a claim on the difference that nothing here chases.
- A rebate settled by credit note leaves stock overvalued for the whole period, and the credit
  note then lands looking like profit that fell out of the sky. Accruing it as it is earned is
  the piece that makes the margin on every sale in between true, and it is not built.
- A supplier invoice values what is owed; it does not revalue the shelf. Inventory still costs a
  receipt at the purchase order's price, so a delivery invoiced at a different figure leaves a
  price variance nothing posts or reports.
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

- Single sign-on. Users live in the `access` schema and this system issues its own tokens, which
  is right for an on-premises install and wrong for a customer who already has an identity
  provider. `IAccessTokenIssuer` and the API's validation parameters are the two places that
  change.
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
