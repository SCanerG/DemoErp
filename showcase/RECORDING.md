# Genuine video recording procedure and storyboard

No video was created or claimed. No current Docker service/database/customer record was modified. Genuine source UI screenshots are the current alternative. The gallery explains that verified recordings are unavailable; no dead Watch demo button or fake player exists.

## Isolated recording prerequisites

Use a disposable development machine or a dedicated Docker Compose project, separate host ports and NEW volumes. Follow the repository README/README.tr.md, docs/DEPLOYMENT.md and docs/TESTING.md at fbada64d67859a526529368c440b80fc6a3dc32a. Requires Docker Desktop Linux containers, enough local CPU/RAM/disk, a real backend and PostgreSQL, and a screen recorder or verified Playwright capture setup.

Do not run commands against the existing demoproject runtime/volumes. Do not reuse .env, owner credentials or production data. Generate unique disposable secrets locally; never put them into recordings or Git.

1. Inspect docker compose config locally without exposing its resolved environment in shared output.
2. Use a dedicated project name (e.g. demoerp-showcase-recording), change frontend/backend published ports to unused loopback ports in a local override, and verify it resolves to a fresh named volume.
3. Bootstrap an isolated administrator using the documented optional BOOTSTRAP_ADMIN_* settings. Do not publish the password or record login typing. Disable bootstrap after initial setup.
4. Seed only clearly fictional products, warehouses, BOMs, customers and suppliers using supported APIs/UI and the repository's test documentation. Never point seed scripts at an existing owner database.
5. Check /health/ready, login, and real persisted effects before capture. Reload each completed workflow and confirm the database-backed state, rather than relying on client-only animation.
6. Record with browser chrome, email, password managers, notifications, network authorization headers and environment files hidden. Review every frame for secrets or personal information.
7. Verify recording matches actual API effects; document source commit, date, language, isolated dataset and covered steps. A screenshot test using mocked API data cannot be substituted as a real backend recording.
8. Keep recordings short, without audio if unnecessary, compressed MP4/WebM with poster, preload=none and captions/transcript. Verify every asset remains below 25 MiB and the Pages free limits. If unsuitable, propose an alternative before adding hosting/services.
9. Add a player/Watch demonstration action only after authentic recordings and accessible captions are available and manually reviewed.
10. Stop/remove ONLY the explicitly verified disposable project if cleanup is wanted; never use a broad volume prune. Owner data must remain intact.

## Storyboard

| Video | Length target | Genuine operations | Verification |
| --- | --- | --- | --- |
| Overview | 30–45 s | Login before recording → dashboard → modules | Real health/API responses; no credentials on screen |
| Inventory and warehouses | 30–45 s | Stock balance → warehouse detail → movement | Refresh and compare actual warehouse/product balance |
| Procurement | 45–60 s | Purchase order → goods receipt → stock movement | Receipt posted once; resulting persisted balance |
| Manufacturing | 60–90 s | Versioned BOM → production order → release/start → consume → complete | Real lifecycle, consumption rollback guard, single finished-goods posting |
| Sales | 30–45 s | Create order → confirmed transition → inventory effect | Correct stock delta; repeated state does not deduct twice |

These are planned recordings, not performed tests. Do not claim partial production, recursive BOM planning, scheduling or any capability not implemented. The static browser scenario remains explicitly labeled and separate from these videos.
