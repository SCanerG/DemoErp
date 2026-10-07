# DemoErp — Manufacturing ERP / MRP Portfolio

**C# · .NET 9 · ASP.NET Core Minimal API · EF Core 9 · SQLite · JavaScript · WPF**

DemoErp is an actively developed manufacturing management project for carpet and
yarn businesses. This repository is a **public technical showcase**, not the
ERP distribution. Selected, simplified code demonstrates implementation choices
without publishing operational integrations, customer data or the full source.

**Author / contact:** [SCanerG](https://github.com/SCanerG)

## Türkçe özet

Halı ve iplik üretimi için geliştirilen ERP/MRP projesinin teknik portföyüdür.
Amaç; iş kurallarının modellenmesini, katmanlı mimariyi, API geliştirmeyi,
veri bütünlüğü kontrollerini ve test yaklaşımını göstermektir.
Ürün geliştirme aşamasındadır; tüm modüller üretime hazır değildir.
Geliştirme sürecinde AI destekli araçlardan yararlanılmaktadır.

## Technology evidence

| Area | Current implementation |
|---|---|
| Server | C#, ASP.NET Core 9, Minimal APIs |
| Business logic | Domain entities and application use cases |
| Persistence | Entity Framework Core 9, SQLite, migrations |
| Browser client | HTML/CSS and vanilla JavaScript, hash-based SPA navigation |
| Authentication | Cookies and role/policy checks |
| Windows client | WPF project targeting .NET 9 Windows; incomplete feature parity |
| Label design | ZPL source editor, local partial preview, SVG |
| Verification | C# regression runner and Node.js tests |

See the runnable [ASP.NET Core 9 sample](samples/DemoErp.Sample/DemoErp.Sample.csproj)
and [architecture notes](docs/ARCHITECTURE.md). The sample's `net9.0` target and
`Microsoft.NET.Sdk.Web` SDK make the backend technology directly inspectable.

## Implemented workflows in the private project

- Customer and product master data; carpet technical cards.
- Sales order lines, approval/revision flows and snapshots.
- Stock receipts, issues, transfers, lot/barcode tracking and stock counts.
- BOM/recipe components and production planning foundations.
- Design, blank-design and carving archives with original files and versions.
- Technical quality/size definitions and set/package composition.
- Label templates and a partial bidirectional ZPL editor.

These are implemented workflows, **not a claim of complete enterprise readiness**.
See [verification and limitations](docs/VERIFICATION.md).

## What to review first

1. [Architecture and trade-offs](docs/ARCHITECTURE.md).
2. [Domain validation sample](samples/DemoErp.Sample/SizeRule.cs).
3. [Minimal API sample](samples/DemoErp.Sample/Program.cs).
4. [Executable checks](samples/DemoErp.Sample/Program.cs) via `--self-test`.

The example is adapted from the private project's ebat/size validation.
Its namespace and hosting code are simplified for independent review.
It does not include ERP authentication, persistence or production integrations.

## Run the isolated example

Requires a .NET 9 SDK:

```sh
dotnet run --project samples/DemoErp.Sample -- --self-test
dotnet run --project samples/DemoErp.Sample -- --urls http://localhost:5095
```

Send `POST /samples/size/validate` with JSON:

```json
{"widthCm":160,"lengthCm":230,"shape":"Rectangle"}
```

Returns a gross bounding-rectangle area of `3.68` m². This is deliberately not
a net cut-area or commercial billing calculation.

## Publication boundary

No database, credentials, original design files, machine communication code,
customer records or complete ERP source are included. Screenshots and demo video
will be added only after anonymization; none are claimed as present yet.

This is not an open-source release. See [NOTICE](NOTICE.md).
