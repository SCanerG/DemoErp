# Yayın öncesi inceleme

Hedef: https://github.com/SCanerG/DemoErp — PUBLIC, main dalı.
İncelenen başlangıç commit: 17dcffbef56881a9dca5e6fa102144dd54dd923d.
Bu rapor yayın öncesi yerel hazırlığın kaydıdır. Hazırlık anında commit/push yapılmamıştı.
Kullanıcı 8 Ekim 2026 tarihinde yayını onayladı; aşağıdaki uzak doğrulama durumları yayın öncesine aittir.
Yayın gerçekleşti. Sonuçlar [VERIFICATION.md](../VERIFICATION.md#publication-verification--8-october-2026) dosyasına kaydedildi.

## Yedek ve Git ayarları

- Altı mevcut commit ve tüm mevcut referanslar mirror clone ve doğrulanmış Git bundle ile yedeklendi.
- Yerel yedek: .tools/publication/backups/DemoErp.bundle; araç/yedekler yayınlanmaz.
- Hazırlık checkout: .tools/publication/review; origin mevcut HTTPS URL, dal main.
- Ana çalışma alanının Git ayarları ve global kullanıcı ayarları değiştirilmedi.
- Hazırlık checkout clone işleminin origin/main takip ayarlarını içerir; geçmiş korunur.
- Kaynak yayını ve yayın sonrası doğrulama kaydı gerçek commitlerle main dalına normal fast-forward push kullanır.
- Push öncesi uzak HEAD yeniden kontrol edilecek; değişmişse yeni değişiklikler incelenecek.
- Force push, depo silme, görünürlük değişikliği ve uydurma commit geçmişi yok.
- Kullanıcı tercihi: açık kaynak lisansı eklenmeyecek; NOTICE kullanım koşulu korunacak.

## Dosya değişiklikleri

94 yayın dosyası: 90 eklenecek, 4 güncellenecek; eski ağaçtan 13 dosya kaldırılacak.

Güncellenecek:

- .gitignore
- NOTICE.md
- README.md
- docs/ARCHITECTURE.md

Kaldırılacak (mevcut demo ile ilgisi olmayan eski örnekler ve görseller):

- docs/CODE_EXAMPLES.md
- docs/PUBLICATION_SCOPE.md
- docs/REVIEWER_GUIDE.md
- docs/VERIFICATION.md
- docs/screenshots/web-barcode-designer.png
- docs/screenshots/web-order-detail.png
- docs/screenshots/web-palette-color-preview.png
- docs/screenshots/windows-barcode-designer.svg
- docs/screenshots/windows-order-detail.svg
- docs/screenshots/windows-palette-color-preview.svg
- samples/DemoErp.Sample/DemoErp.Sample.csproj
- samples/DemoErp.Sample/Program.cs
- samples/DemoErp.Sample/SizeRule.cs

<details>
<summary>Eklenecek dosyaların tam listesi</summary>

- .env.example
- .gitattributes
- .github/workflows/ci.yml
- Catalog.slnx
- README.tr.md
- VERIFICATION.md
- backend/.dockerignore
- backend/Demo.Api.Tests/ApiFixture.cs
- backend/Demo.Api.Tests/ApiTests.cs
- backend/Demo.Api.Tests/BusinessTests.cs
- backend/Demo.Api.Tests/Demo.Api.Tests.csproj
- backend/Demo.Api.Tests/MigrationTests.cs
- backend/Demo.Api.Tests/ValidationTests.cs
- backend/Demo.Api/Contracts/BusinessContracts.cs
- backend/Demo.Api/Contracts/Requests.cs
- backend/Demo.Api/Controllers/AuthController.cs
- backend/Demo.Api/Controllers/CategoriesController.cs
- backend/Demo.Api/Controllers/CustomersController.cs
- backend/Demo.Api/Controllers/OrdersController.cs
- backend/Demo.Api/Controllers/ProductsController.cs
- backend/Demo.Api/Data/AppDbContext.cs
- backend/Demo.Api/Data/Migrations/20261007185344_InitialCreate.Designer.cs
- backend/Demo.Api/Data/Migrations/20261007185344_InitialCreate.cs
- backend/Demo.Api/Data/Migrations/20261007205005_BusinessWorkflow.Designer.cs
- backend/Demo.Api/Data/Migrations/20261007205005_BusinessWorkflow.cs
- backend/Demo.Api/Data/Migrations/AppDbContextModelSnapshot.cs
- backend/Demo.Api/Demo.Api.csproj
- backend/Demo.Api/Domain/BusinessEntities.cs
- backend/Demo.Api/Domain/Product.cs
- backend/Demo.Api/Domain/User.cs
- backend/Demo.Api/Errors/ApiExceptionHandler.cs
- backend/Demo.Api/Errors/BusinessException.cs
- backend/Demo.Api/Program.cs
- backend/Demo.Api/Services/AuthService.cs
- backend/Demo.Api/Services/BusinessServices.cs
- backend/Demo.Api/Services/ProductService.cs
- backend/Demo.Api/Services/TokenService.cs
- backend/Demo.Api/Swagger/BearerOperationFilter.cs
- backend/Demo.Api/appsettings.json
- backend/Dockerfile
- docker-compose.yml
- docs/API.md
- docs/DATABASE.md
- docs/DEVELOPMENT.md
- docs/PUBLICATION_PLAN.md
- docs/screenshots/categories-en.png
- docs/screenshots/customers-en.png
- docs/screenshots/login-en.png
- docs/screenshots/order-create-en.png
- docs/screenshots/order-detail-tr.png
- docs/screenshots/orders-en.png
- docs/screenshots/products-en.png
- docs/screenshots/products-tr.png
- frontend/.dockerignore
- frontend/.env.example
- frontend/Dockerfile
- frontend/Dockerfile.e2e
- frontend/index.html
- frontend/nginx.conf
- frontend/package-lock.json
- frontend/package.json
- frontend/playwright.config.ts
- frontend/scripts/capture-screenshots.mjs
- frontend/src/App.tsx
- frontend/src/Auth.tsx
- frontend/src/DeleteConfirmation.tsx
- frontend/src/api.ts
- frontend/src/components.tsx
- frontend/src/i18n.tsx
- frontend/src/main.tsx
- frontend/src/pages/AuthPage.tsx
- frontend/src/pages/Orders.tsx
- frontend/src/pages/ProductDetail.tsx
- frontend/src/pages/ProductEditor.tsx
- frontend/src/pages/ProductList.tsx
- frontend/src/pages/Records.tsx
- frontend/src/session.ts
- frontend/src/styles.css
- frontend/src/translations.ts
- frontend/src/types.ts
- frontend/tests/business.spec.ts
- frontend/tests/catalog.spec.ts
- frontend/tests/container-runner.mjs
- frontend/tests/localization.spec.ts
- frontend/tests/states.spec.ts
- frontend/tests/ui.spec.ts
- frontend/tsconfig.json
- frontend/vite.config.ts
- global.json
- scripts/verify.mjs

</details>

## Doğrulama

- Backend restore/build: PASS, 0 uyarı / 0 hata.
- Backend testleri: PASS, 37/37, 0 atlanan; gerçek PostgreSQL/Testcontainers.
- Frontend npm ci/build: PASS; npm audit 0 bulgu bildirdi.
- Docker Compose config ve temiz kaynak kopyasından up --build --wait: PASS, üç servis healthy.
- İki migration uygulanmış: InitialCreate ve BusinessWorkflow; eski ürün upgrade testi PASS.
- Chromium: PASS, 12/12; yeni geçici stack üzerinde 26.0 saniye.
- API/schema/sipariş/yeniden başlatma/hata ve log doğrulama betiği: PASS.
- Sekiz gerçek EN/TR ekran görüntüsü: PASS, sentetik veriler; görsel olarak incelendi.
- Gitleaks 8.30.1: altı commitlik tüm uzak geçmiş ve hazırlanmış kaynak ağacı temiz.
- Yayın adaylarında .env, bin/obj, node_modules/dist, araç/yedek, DB/log yok.
- Mermaid: PASS, altı diyagram Mermaid 12.1.0 ile Chromium üzerinde SVG/PNG render edildi.
- Uzaktaki CI ve yayınlanan yeni README/source tree: UNVERIFIED, henüz yayın yapılmadı.

## GitHub sunumu önerisi

About: Full-stack business demo with ASP.NET Core 10, React/TypeScript, PostgreSQL, JWT, transactional orders and TR/EN localization.

Topics: csharp, dotnet, aspnet-core, react, typescript, postgresql, entity-framework-core, docker, jwt, fullstack, portfolio.

About/topics yalnızca öneridir; uzak depo ayarları bu hazırlıkta değiştirilmedi.

## Sınırlar ve riskler

Gitleaks ve kaynak incelemesi temiz olsa da her olası sır biçiminin yokluğunu kanıtlamaz.
Örnek ayarlardaki geliştirme parolası/JWT anahtarı bilerek işaretli yer tutucudur.
Gerçek .env, DB içeriği ve araç raporları kaynak ağacına alınmaz.
Geçmiş korunacağı için eski ERP anlatımı eski commitlerde görülebilir; yeni README kapsam değişimini açıklar.
GitHub Actions yalnız yayın sonrası çalışabilir; hazırlanmış kaynakların uzak clone testi de o zaman yapılabilir.
Şu anki temiz başlangıç testi yerel kaynak exportundan yapıldı; uzak clone ile aynı olduğu iddia edilmez.
Üretim dağıtımı, yük testi ve doğrudan interaktif Windows tarayıcı kontrolü doğrulanmadı.
Uygulamanın rol/tenant, sayfalama ve token yenileme gibi sınırları README’de belirtilir.
