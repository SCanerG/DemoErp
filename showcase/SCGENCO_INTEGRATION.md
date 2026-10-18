# SCGENCO integration — apply only after owner-approved deployment

SCGENCO is a separate private repository. Its routes/components and Cloudflare project have not been changed. Its local Phase 13 branch is preserved. No speculative public link has been added.

After verifying HTTPS and approving release, add ordinary external links (target=_blank, rel=noopener noreferrer) beside the existing DemoErp source/architecture actions in SCGENCO src/components/engineering/demoerp-case.tsx and the DemoErp portfolio item in src/components/engineering/portfolio.tsx. Verify the actual component dependencies first.

| Turkish label | English label | TR URL | EN URL |
| --- | --- | --- | --- |
| Etkileşimli Tanıtımı İncele | Explore Interactive Showcase | https://demo.scgenco.com/tr/ | https://demo.scgenco.com/en/ |
| Gerçek Uygulama Ekranları | View Real Application Screens | https://demo.scgenco.com/tr/#screens | https://demo.scgenco.com/en/#screens |
| Teknik Mimari | Technical Architecture | https://demo.scgenco.com/tr/#architecture | https://demo.scgenco.com/en/#architecture |
| GitHub Kaynak Kodu | GitHub Source Code | https://github.com/SCanerG/DemoErp | https://github.com/SCanerG/DemoErp |

Both locales must say this is a static portfolio showcase; the browser preview does not connect to .NET or PostgreSQL. Avoid Live ERP / Live PostgreSQL / Production ERP claims. Retain the existing DemoErp case route and all Studio routes and request workflows. The standalone footer already returns to the appropriate https://scgenco.com/tr/ or /en/ page.

Run SCGENCO's existing route, bilingual, portfolio and SEO regression checks when applying the integration. Main update/deployment requires the owner's separate explicit instruction.
