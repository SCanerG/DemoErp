# DemoErp · Interactive Portfolio Showcase

Independent static React/TypeScript presentation of the real DemoErp application. No .NET runtime, PostgreSQL, hosted AI, authentication, API keys or paid services are required. The real application in ../frontend and ../backend remains unchanged.

## Local development

Node 22.14.0 (see .node-version), npm:

```sh
cd showcase
npm ci
npm run dev
```

TR: http://127.0.0.1:3400/tr/ · EN: http://127.0.0.1:3400/en/

```sh
npm run lint
npm run typecheck
npm test
npm run build
node scripts/verify-sources.mjs
npx playwright install chromium
npm run test:browser
npm run preview
```

Windows with installed Google Chrome: set PLAYWRIGHT_CHANNEL=chrome. Browser tests start and stop their own production static server; stop a manually running preview before testing. Tests preserve failure traces/screenshots and write an HTML report. No test writes to the real ERP.

## Structure

- src/copy.ts: complete Turkish/English UI copy.
- src/model.ts: pinned source facts, six workflow steps, immutable sample state transitions.
- src/main.tsx: presentation, native-dialog gallery, controls, navigation, theme.
- src/media.json: genuine screenshot provenance, original SHA-256 and dimensions, output sizes.
- public/media/: optimized genuine screenshots and existing SCGENCO monogram.
- scripts/: independent media preparation, static output/source verification and production preview.
- tests/: Node unit tests and Playwright/axe browser checks.
- tr/index.html and en/index.html: localized HTML entry metadata.
- public/404.html: genuine static 404, no catch-all SPA rewrite.
- public/_headers: static security/cache policy; no Pages Functions.

Screenshot regeneration: npm exec node scripts/prepare-media.mjs (or node scripts/prepare-media.mjs). Original captures must remain in ../docs/screenshots. Already optimized files are committed; deployment does not require regenerating them. Brand asset copied unchanged from SCGENCO public/media/brand/scg-monogram.webp on 2026-10-18.

The simulation uses only fictional, in-memory Sample Workshop data. Changing its batch restarts it. Reload clears it; no customer data is collected. Only the theme preference is stored locally; blocked storage is tolerated. Switching language preserves the current section, not a business transaction.

The original ERP screenshot suite uses synthetic data. These are genuine application UI captures, not evidence of a public live backend. Original test counts (417/16/68) are dated 2026-10-11; they were not rerun for this static project.

See [deployment](DEPLOYMENT.md), [recording](RECORDING.md), [integration](SCGENCO_INTEGRATION.md) and [Turkish implementation report](PHASE14_REPORT.md). No external deployment, DNS changes or main-branch update is authorized by this implementation.
