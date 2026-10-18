# Cloudflare Pages deployment preparation

Status: LOCAL ONLY. Owner approval is required before repository publication, Cloudflare project creation/modification, deployment or DNS changes. Do not change the existing scgenco-com Pages project.

## Exact settings after approval

| Setting | Value |
| --- | --- |
| Repository | SCanerG/DemoErp |
| Suggested separate Pages project | demoerp-showcase |
| Production branch | main only after an explicitly approved merge |
| Framework preset | None / Vite |
| Root directory | showcase |
| Dependency installation | npm ci, package-lock.json committed; development dependencies required |
| Build command | npm run build |
| Build output directory | dist (relative to showcase) |
| Node | 22.14.0 via showcase/.node-version; optional explicit NODE_VERSION=22.14.0 |
| Application environment / secrets | None |
| Functions / Workers / D1 / R2 bindings | None |
| Build watch paths | showcase/** only |
| Production custom domain | demo.scgenco.com, after verification and approval |

Build locally: cd showcase; npm ci; npm run build. Upload only showcase/dist, never the repository or a .env file. Do not set a Pages Functions root. Vite env file loading is disabled, and the public env prefix is deliberately unused. Build output checks enforce <20,000 files and <25 MiB per asset. Existing ERP CI and Docker configuration are unchanged; no auto-deployment workflow was added.

## Approval and release checklist

1. Review and explicitly approve the showcase commit. Push the feature branch only after authorization; merge only when requested.
2. Create a NEW Pages project using these settings. Avoid enabling auto deployments until branch and build watch paths are reviewed. Selecting Git integration can deploy immediately; project creation is therefore itself approval-gated.
3. Inspect the generated pages.dev URL: /tr/, /en/, screenshots, language anchors, native dialog keyboard behavior, preview disclaimer, source links, /404 and security response headers.
4. Only then select Custom domains → Set up a custom domain → demo.scgenco.com on the NEW project.
5. If the dashboard does not create the zone record automatically, add only a CNAME named demo targeting the exact project.pages.dev hostname supplied by Pages. First associate the domain in Pages; adding a CNAME alone can produce a 522. Do not change apex, www, nameservers, MX, mail verification records or existing Pages project.
6. Wait for Active/TLS; check HTTPS responses for /, /tr/, /en/, media and unknown paths. Verify CSP, X-Content-Type-Options, Referrer-Policy, canonical and hreflang metadata. Preview-domain indexing behavior should remain excluded.
7. Verify root TR page's canonical points at /tr/; /tr and /en should resolve consistently to trailing-slash pages under Pages normal serving behavior. A top-level 404.html prevents unknown paths returning the SPA shell.
8. Enable SCGENCO integration links only after demo.scgenco.com is reachable and approved. No links to an unverified host have been inserted in the main site.

## Caching and rollback

Hashed /assets/* cache for a year (immutable); screenshots use a one-hour cache and must be purged or renamed if replaced at the same URL. HTML revalidates. Avoid a zone-wide Cache Everything rule overriding HTML behavior. No service worker or browser cache storage exists.

Rollback: in the NEW project's Deployments choose a previous successful production deployment → Rollback to this deployment. A preview deployment is not a rollback target. Alternatively revert only the showcase commit and redeploy after approval. Keep a known-good deployment ID/commit before each release. This does not require rewriting Git history or modifying SCGENCO production.

## Zero recurring-cost scope and limits

Official documentation reviewed 2026-10-18:

- [Pages limits](https://developers.cloudflare.com/pages/platform/limits/): Free 500 builds/month, one concurrent build, 20-minute build timeout, 20,000 files/site, 25 MiB/file, 100 custom domains/project. Build concurrency is account-wide; budget builds alongside SCGENCO.
- [Build image](https://developers.cloudflare.com/pages/configuration/build-image/): Node can be selected with .node-version or NODE_VERSION.
- [Custom domains](https://developers.cloudflare.com/pages/configuration/custom-domains/): associate a hostname with Pages before adding CNAME.
- [Rollbacks](https://developers.cloudflare.com/pages/configuration/rollbacks/).

The verified bundle is about 1.35 MB / 40 files and fits the documented static Free limits. It requires no additional recurring-cost infrastructure using the owner's existing domain and Free account within applicable limits/terms. This is not an unlimited hosting or permanent pricing guarantee. Recheck limits before launch. No extra credit card, storage, Stream, Images, R2, Workers or database service is required or configured.

Videos do not exist in the output. A future video may exceed 25 MiB or be inappropriate for this static site: propose a smaller short recording or an owner-approved alternative first. Do not silently provision paid media services. Hosting the REAL .NET/PostgreSQL ERP would be a separately scoped operational project, potentially with recurring costs; it is not part of this deployment.
