# Verification and project status

The private solution contains regression scenarios for stock movements and reversals,
reservations, stock counts, customer/product rules, order transitions, design archive
versions, technical definitions, package lines and label-layout validation.
JavaScript checks cover selected UI logic and ZPL parsing/movement.

Local builds and these regressions passed during development. The private tests are
not reproduced here; this public repository independently verifies only its small sample.
No CI badge or numerical coverage claim is presented without public evidence.

## Public sample checks

`dotnet run --project samples/DemoErp.Sample -- --self-test`

Checks rectangle area, invalid dimensions, round/square equality and allowed shapes.

## Not yet complete

- Full MRP net-requirements and procurement workflows.
- Complete production/dispatch/finance integration and enterprise permission matrix.
- PostgreSQL deployment, recovery drills and comprehensive multi-user acceptance.
- WPF feature parity.
- Full ZPL language rendering, direct machine printing and physical scanner tests.
- Real-browser visual and accessibility acceptance across all screens.

The project is intended as evidence of ongoing engineering work, not as a certified
or finished ERP product. Discussion of trade-offs and remaining work is part of the portfolio.
