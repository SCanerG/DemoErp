# Verification / Doğrulama

## Public evidence / Public kanıt

```sh
dotnet run --project samples/DemoErp.Sample -- --self-test
```

The public sample checks rectangle area, invalid dimensions, round/square equality and allowed shapes. Its source proves the ASP.NET Core 9 target and a domain-validation example. It does not independently demonstrate the private JWT, CRUD, database or Docker implementation.

## Private development checks / Özel uygulama kontrolleri

Checks reported on 2026-10-07:

- Solution build: zero warnings and errors.
- Existing MRP regression scenarios: passed.
- Frontend JavaScript syntax check: passed.
- Isolated SQLite HTTP smoke: JWT issuance, successful renewal, logout cookie clearing, anonymous 401, role-based 403, product create/read/update/soft-delete and static assets passed.
- PostgreSQL and SQLite migration snapshots: no pending model changes.
- Docker Compose configuration validation: passed.

These tests are not published in this repository. No CI badge, coverage percentage or production-readiness certification is claimed.

## Remaining verification / Kalan doğrulama

- Actual PostgreSQL migration, seed, persistence and restart acceptance.
- Docker image build and full Compose startup, including first-user setup.
- Refresh replay, parallel renewal and expired token behavior.
- Immediate access-token invalidation after logout, password changes or session revocation.
- Security checks for CSRF, XSS and deployment cookie settings.
- Real-browser route protection, error states and accessibility.
- WPF authentication renewal and feature parity.
- Manufacturing integrations, backup/restore and multi-user acceptance.

Refresh-token renewal succeeds in the smoke test, but replay and concurrency safety are not established by that result. Logout revokes the current refresh token; an already issued access token can remain valid until expiry.

React/TypeScript, .NET 10 and Swagger remain planned work. They must not be presented as implemented technologies.

Türkçe: Başarılı yerel kontroller tam üretim kabulü anlamına gelmez. Özellikle gerçek PostgreSQL/Docker çalıştırması, güvenlik senaryoları ve tarayıcı kabul testleri tamamlanmalıdır.
