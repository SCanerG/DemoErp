# Phase 14 — DemoErp bağımsız etkileşimli tanıtımı

Tarih: 18 Ekim 2026. Durum: yerel geliştirme ve doğrulama tamamlandı; dış yayın yapılmadı.

## A. Uygulama ve dosyalar

Çalışma kopyası: C:/Users/sabri/OneDrive/Belgeler/ChatGPT/DemoErp-Phase14. Dal: codex/phase14-demoerp-showcase. Denetlenen gerçek ERP tabanı: fbada64d67859a526529368c440b80fc6a3dc32a.

Mevcut DemoProject klasöründe dosyalar Git tarafından izlenmiyordu ve remote yoktu. Oradaki kaydedilmemiş dosyaları sahiplenmek/değiştirmek yerine gerçek SCanerG/DemoErp deposu ayrı klasöre klonlandı. Önceki SCGENCO çalışması codex/phase13-developer-portfolio dalında a74d45ca682da03e79abb003ddac2c8df23e512f olarak temiz kaldı.

Yalnızca showcase/ altında yeni dosyalar eklendi:

| Dosya/grup | İşlev |
| --- | --- |
| index.html, tr/index.html, en/index.html | TR kök sayfa, iki yerel giriş, canonical/hreflang/başlık/açıklama |
| src/main.tsx | Marka başlığı, menü/tema/dil, iş akışı gezgini, örnek senaryo, galeri, mimari, mühendislik kararları, kanıt ve kurulum bölümleri |
| src/copy.ts | Türkçe/İngilizce arayüz metinleri |
| src/model.ts | Tarihli/pinlenmiş kaynaklar, modüller, iş kurallarıyla uyumlu küçük ve deterministik eğitim senaryosu |
| src/style.css | Mevcut SCGENCO kimliğiyle uyumlu koyu/açık tema ve responsive düzen |
| src/media.json | Orijinal ekran yolu, Git revizyonu, SHA-256, gerçek boyutlar ve optimize çıktı listesi |
| public/media/*.webp | 10 gerçek yakalamanın 640/960/1440 hedef genişlikleri ve değişmeden kullanılan mevcut SCGENCO monogramı |
| public/_headers, robots.txt, sitemap.xml, 404.html | Güvenlik/önbellek, üretim URL'leri ve gerçek statik 404 |
| package.json, package-lock.json, .node-version, tsconfig.json, vite.config.ts, eslint.config.js | Kendi bağımsız kurulum/derleme zinciri |
| scripts/prepare-media.mjs | Gerçek depodaki ekranları yeniden optimize etme |
| scripts/verify-static.mjs | Dosya/boyut/medya/bağımlılık ve bilinen gizli anahtar kalıpları denetimi |
| scripts/verify-sources.mjs | Kaynak yolları, orijinal ekran SHA-256 ve ERP izlenen dosya bütünlüğü |
| scripts/preview.mjs | Güvenlik başlıklı yerel statik sunucu; 404 ve trailing-slash yönlendirmeleri |
| tests/model.test.ts, tests/browser/showcase.spec.ts, playwright.config.ts | Bağımsız birim, gerçek tarayıcı ve axe kontrolleri |
| README.md, DEPLOYMENT.md, RECORDING.md, SCGENCO_INTEGRATION.md, PHASE14_REPORT.md | Kurulum, yayın, kayıt planı, entegrasyon ve rapor |

TR: /tr/; EN: /en/; kök / Türkçedir ve canonical /tr/ adresine işaret eder. Dil değişimi mevcut bölüm hash'ini korur. Tema tercihi isteğe bağlı localStorage kaydıdır; erişim engellenirse sayfa çalışmaya devam eder. İş örneği yalnızca bellektedir ve yenilemede sıfırlanır.

Altı adım: satın alma → mal kabul → depo stoku → üretime başlama → mamul girişi → satış. Her adımda iş sonucu, modül, veri değişimi, gerçek backend yaklaşımı ve pinlenmiş kaynak bağlantısı bulunur. Örnek Atölye senaryosunda 60 birim kurgu çelik, 1/5/10 ürün partisi ve ürün başına 4 birim tüketim kullanılır; parti değişimi sıfırlar. Hiçbir gerçek işlem veya müşteri kaydı oluşmaz.

Galeri: dashboard-en/tr, inventory-en, warehouses-en, purchase-orders-en, manufacturing-en, production-detail-en, production-detail-tr-mobile, orders-en, products-tr. Gerçek uygulama arayüzünün depodaki sentetik verili yakalamalarıdır; canlı backend dağıtımı veya yeni bir gerçek API kaydı olduğu iddia edilmez. Modül ve gerçek cihaz filtresi, responsive/lazy WebP, native dialog, Tab/Shift+Tab odağı, Escape ve açan düğmeye odak dönüşü vardır.

Video üretilmedi. Gerçek backend/PostgreSQL ile izole kayıt yapılmadığı için Watch demo düğmesi veya sahte oynatıcı yoktur. RECORDING.md beş storyboard ve gerçek kayıt doğrulama/izolasyon adımlarını içerir.

## B. Mimari ve korunma

Gerçek ERP: React/TypeScript → ASP.NET Core REST API → iş servisleri → EF Core 10 → PostgreSQL 17. Katmanlı monolittir; mikroservis veya fazladan repository/Unit of Work katmanı iddia edilmez.

Yeni tanıtım: React/TypeScript → Vite statik çıktı → planlanan ayrı Cloudflare Pages projesi. Runtime bağımlılıkları yalnızca react ve react-dom. Backend, veritabanı, kullanıcı oturumu, API anahtarı, AI sağlayıcı veya ağ isteği gerekmez. Vite ortam dosyaları yüklenmez. CSP connect-src 'none' olarak hazırlanmıştır.

Mimari katmanları ve eşzamanlı stok, idempotent mal kabul, üretim tutarlılığı, sunucuda yetkilendirme ve ölçülmüş sorgu kapsamı kararları açılabilir detaylarda açıklanır. Pinlenmiş uygulama/test/belge bağlantıları bulunur.

Gerçek ERP'nin backend/frontend/Docker/migration/test/CI dosyaları değiştirilmedi. Mevcut veritabanına bağlanılmadı; veri oluşturulmadı, silinmedi. Ana SCGENCO dosyaları ve Cloudflare projesi değiştirilmedi.

## C. Gerçekten çalıştırılan doğrulama

| Kontrol | Sonuç |
| --- | --- |
| npm ci | Temiz kilit dosyası kurulumu başarılı |
| npm run typecheck | Başarılı; ayrıca build içinde tekrar çalışır |
| npm run lint | Başarılı |
| npm test | 6/6; deterministik adımlar, farklı partiler, stok korunumu, mutasyonsuz geçiş, sıfırlama, dil/kaynak kuralları |
| npm run build | Başarılı; .NET/PostgreSQL/gerçek API olmadan |
| npm run test:browser, PLAYWRIGHT_CHANNEL=chrome | 15/15, 27,9 saniye; 0 atlanan/kararsız test |
| axe seçili WCAG 2 A/AA ve 2.1 AA kuralları | TR/EN, 390/1440, koyu/açık ve galeri dialog kontrollerinde 0 ihlal |
| Ek responsive | 320/768/1024 genişliklerinde belge taşması yok |
| Dil/gezinti | Gerçek sayfa geçişi, bölüm hash'i, mobil menü Escape ve odak dönüşü geçti |
| Galeri | 10 görselin yüklenmesi, filtreler, gerçek mobil yakalama, dialog ve klavye geçti |
| Senaryo | Altı adım, stok sonuçları, parti değişimi ve sıfırlama geçti |
| Statik HTTP | /, /tr/, /en/, robots ve sitemap 200; bilinmeyen yol 404; /tr ve /en trailing-slash 308; yerel güvenlik başlıkları geçti |
| Medya/kaynak | 20 benzersiz kod/belge yolu Git ağacında mevcut; 10 orijinal yakalama pinlenmiş commit ile byte/SHA-256 eşleşiyor |
| Ağ ve çıktı taraması | Runtime backend çağrısı yok; müşteri formu/iframe/video yok; bilinen anahtar kalıpları ve yanlış ortam dosyası bulunmadı |
| npm audit | 0 bildirilen güvenlik açığı |
| Özgün dosya kapsamı | ERP izlenen dosyaları ve SCGENCO çalışma alanı değişmedi |

Çıktı: 40 dosya, 1.353.820 byte toplam; en büyük dosya 261.753 byte. JS 261,75 kB / gzip 82,17 kB; CSS 16,13 kB / gzip 4,09 kB. Görseller yalnızca ihtiyaç halinde yüklenir; tüm medya ilk render'da zorunlu olarak indirilmez.

Yerel kanıtlar Git'e eklenmeyen verification/ ve playwright-report/ altındadır: browser-results.json, ekran görüntüleri, static-summary.json ve source-verification.json. Testlerde timeout veya assertion zayıflatılmadı. İlk çalıştırmalarda araçların farklı playwright-core sürümleri eşitlendi; cihaz seçicinin adı netleştirildi, test görsel seçicisi tam dosya yoluna düzeltildi; genişletilmiş klavye testi gerçek odak döngüsü iyileştirmesine yol açtı. Son tur 15/15 geçti.

Gerçek ERP'nin 417 backend / 16 bileşen / 68 tarayıcı sonucu **11 Ekim 2026 tarihli tarihsel kanıttır**, bu fazda yeniden çalıştırılmadı. Gösterilen 0,421 → 0,031 ms değeri de sıcak önbellek, yerel sentetik SQL ölçümüdür; HTTP/üretim SLA iddiası değildir.

Doğrulanmayanlar: gerçek Cloudflare dağıtımı, DNS/TLS/Pages production yönlendirmeleri ve cache davranışı, Safari/Firefox, tam ekran okuyucu değerlendirmesi, gerçek backend videosu. Otomatik axe taraması erişilebilirlik sertifikası; kaynak/bundle taraması kapsamlı güvenlik denetimi değildir.

## D. Cloudflare hazırlığı

Ayrıntı: DEPLOYMENT.md. Repository SCanerG/DemoErp; ayrı önerilen proje demoerp-showcase; root showcase; build npm run build; output dist; Node 22.14.0; uygulama sırrı veya binding yok. npm ci geliştirme bağımlılıklarını da kurmalıdır. HTML revalidate, hash'li assets immutable; yeniden kullanılan medya adlarında bir saat cache/purge stratejisi belgelidir.

Önce sahibi yayın/merge ve ayrı proje oluşturmayı onaylamalıdır. Yayın sonrasında pages.dev kontrolü yapılır; sonra aynı yeni projenin Custom domains ekranında demo.scgenco.com eklenir. Gerekirse yalnızca demo CNAME kaydı, Pages'in verdiği tam hedefe ayarlanır. Apex/www/MX/nameserver ve mevcut proje korunur. Önceden Pages ilişkilendirmesi olmadan CNAME eklemek 522 oluşturabilir. Önceki başarılı production deployment üzerinden rollback adımları belgelidir. Hiçbir adım dış ortamda uygulanmadı.

## E. Maliyet

Mevcut alan adı ve Cloudflare Free hesap sınırları içinde bu statik çıktı ek düzenli maliyetli servis gerektirmez. 18 Ekim 2026'da kontrol edilen [resmî limitler](https://developers.cloudflare.com/pages/platform/limits/): ayda 500 build, tek eşzamanlı build, 20 dakika build süresi, 20.000 dosya/site ve dosya başına 25 MiB. Yaklaşık 1,35 MB/40 dosya bu sınırların altındadır. Hesabın SCGENCO ile ortak build bütçesi dikkate alınmalıdır.

Sınırsız kaynak veya değişmeyecek fiyat garantisi verilmez; yayın öncesinde limitler/koşullar yeniden kontrol edilir. Workers/Functions, R2, Stream, Images, ücretli API veya veritabanı kurulmadı. Gelecekte gerçek backend barındırma veya büyük video servisleri ayrı onay/maliyet değerlendirmesi gerektirir.

[Node seçimi](https://developers.cloudflare.com/pages/configuration/build-image/), [Custom domains](https://developers.cloudflare.com/pages/configuration/custom-domains/) ve [rollback](https://developers.cloudflare.com/pages/configuration/rollbacks/) resmî belgeleri incelendi.

## F. SCGENCO entegrasyonu

SCGENCO_INTEGRATION.md iki dilde tam etiket, URL, hedef bileşen ve regresyon adımlarını içerir. Ana sitede henüz çalışmayan demo alan adına bağlantı eklenmedi.

Yayın doğrulandıktan sonra: Etkileşimli Tanıtımı İncele / Explore Interactive Showcase → dil kökü; Gerçek Uygulama Ekranları / View Real Application Screens → #screens; Teknik Mimari / Technical Architecture → #architecture; GitHub Kaynak Kodu / GitHub Source Code → gerçek depo.

## G. Kalan işler

- Sahibin dış yayın ve GitHub push/merge onayı.
- Ayrı Pages projesi, gerçek dağıtım, custom-domain/DNS/TLS ve dış HTTP kontrolü.
- Doğrulanmış gerçek backend video kayıtları ve altyazılar, istenirse.
- Yayından sonra SCGENCO bağlantıları ve ilgili ana site regresyonları.
- İstenirse kapsamlı manuel erişilebilirlik/diğer tarayıcı kontrolleri.
- Gerçek ERP backend barındırma bu fazın dışındadır; ayrıca planlanmalıdır.

Yerel önizleme: http://127.0.0.1:3400/tr/ ve /en/. main güncellenmedi, remote push yapılmadı, üretime dağıtılmadı.
