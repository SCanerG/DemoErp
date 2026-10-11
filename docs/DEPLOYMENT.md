# Deployment readiness

[README](../README.md) · [Security](SECURITY.md) · [Testing](TESTING.md)

## Local topology

`docker compose up --build` starts exactly three services: PostgreSQL, ASP.NET Core and an unprivileged Nginx serving the Vite build. PostgreSQL is not published to the host. Application ports bind to loopback by default. The named PostgreSQL volume persists across container recreation; never remove it as part of an upgrade.

Both application Dockerfiles use multi-stage builds, copy dependency manifests before sources for caching, and run the final process without root. Build/test tools stay outside the runtime images. Frontend `/assets/` files have immutable caching; `index.html` uses no-cache so releases do not retain an obsolete entry point. Security headers are repeated inside Nginx locations that define their own `add_header`, preserving them on assets and the HTML shell.

## Health and startup

| Endpoint | Behavior |
| --- | --- |
| `/health/live` | Process liveness only; does not query PostgreSQL |
| `/health/ready` | PostgreSQL connectivity with a linked two-second deadline; 200 healthy or safe ProblemDetails 503 |
| `/health` | Preserved legacy readiness contract: same response shape as before |

Health responses never disclose connection strings, hostnames or provider details. Compose waits for PostgreSQL's `pg_isready`, then backend readiness, then frontend availability. Backend startup applies migrations with bounded retries and fails rather than serving an incompatible schema. A database outage affects readiness while the process remains live.

## Environment and production checklist

Use `.env.example` only as a development reference. For actual hosting, configure `ASPNETCORE_ENVIRONMENT=Production`, independent database and JWT secrets, exact allowed frontend origins and the browser-reachable API base URL. `VITE_API_BASE_URL` is compiled into public JavaScript; rebuilding is required when it changes. No password, JWT signing secret or AI provider credential may be placed in any `VITE_*` variable.

The application rejects the development JWT secret outside Development. Swagger is enabled only in Development. Bootstrap is optional and should be disabled after initial Admin provisioning. AI remains disabled unless backend-only provider configuration is explicitly supplied; ERP startup and CI must not depend on provider availability.

A public deployment also needs HTTPS termination, correctly scoped reverse-proxy trust, firewalling, secret management, backup storage/access policies, operational alerts and a reviewed image/digest update process. The supplied Compose setup is a local starting point; no managed HTTPS, cloud deployment or production SLA is claimed. In-memory AI quotas are per instance and reset on restart.

## Upgrade and recovery

1. Take a `pg_dump -Fc` backup using the current PostgreSQL service account; store it outside source control with restricted access.
2. Verify the archive with `pg_restore --list` and restore it into a **separate** disposable database. Preserve the original volume.
3. Review migrations before rollout. Phase 10 introduces no schema migration or new index; all nine existing migrations remain intact.
4. Rebuild/recreate backend and frontend, then check readiness and read-only functional routes. Do not run test seeds or destructive verification against the primary database.
5. Compare pre/post data signatures when upgrading a local demo; Phase 10 verification includes all 23 existing business tables/views.

For multiple API instances, move migration execution to a controlled single deployment step. The current startup migration flow is appropriate for this single API demo and is not a distributed rollout coordinator. PostgreSQL guards intentionally reject unsafe historical downgrades; recover through a reviewed forward fix or an explicitly planned backup restore into a separate volume.

## CI

The workflow runs for pull requests, main pushes and manual dispatch. Backend tests use Docker-backed Testcontainers. Frontend runs npm ci, ESLint, TypeScript, Vitest, production build and dependency auditing. A separate smoke job starts a disposable Compose stack, installs Chromium and runs smoke plus axe. Public test-only bootstrap values belong solely to that ephemeral job; they are not deployment secrets.

The workflow has read-only repository permissions and no deployment/push step. Local equivalent commands can be verified without publication. Check the published commit in GitHub Actions for its remote execution result.
