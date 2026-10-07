# DemoErp reviewer guide

## Türkçe

Bu depo, tam ürünün dağıtımı değil; uygulamanın mühendislik kalitesini doğrulatmak için hazırlanmış kontrollü bir portföy yüzeyidir.

### Ürün odağı

Aktif geliştirme önceliği web uygulamasıdır. İnceleme; tarayıcı tabanlı kullanıcı deneyimi, korunan frontend rotaları, ASP.NET Core API, JWT oturumu, PostgreSQL kalıcılığı ve Docker dağıtımı üzerinden yapılmalıdır.

Platform için web, Windows masaüstü ve Android istemcileri hazırlanmıştır. İstemciler ortak REST API ve JWT kimlik modelini tüketir. Kullanım senaryoları ve özellik kapsamları birebir aynı olmak zorunda değildir; web uygulaması referans istemcidir ve yeni yetenekleri önce alır.

### Önerilen inceleme sırası

| Süre | İncelenecek alan | Gösterdiği yetkinlik |
|---|---|---|
| 1 dakika | README ve ürün görselleri | Problem alanı, ürün düşüncesi ve teslim edilmiş ekranlar |
| 3 dakika | `docs/ARCHITECTURE.md` | Katman sınırları, bağımlılık yönü ve teknik kararlar |
| 3 dakika | `samples/DemoErp.Sample` | C#, .NET 9, Minimal API, DTO ve domain doğrulaması |
| 2 dakika | [`docs/CODE_EXAMPLES.md`](CODE_EXAMPLES.md) | JWT, korunan endpoint, DTO ve hata sözleşmesi örnekleri |
| 2 dakika | `docs/VERIFICATION.md` | Derleme, smoke test ve doğrulama yaklaşımı |
| 1 dakika | Güvenlik özeti | JWT, refresh token, rol ve hata yönetimi farkındalığı |

### Teknik inceleme başlıkları

- ASP.NET Core 9 Minimal API ve açık HTTP sözleşmeleri
- API, Application, Domain ve Infrastructure sorumluluk ayrımı
- EF Core, Npgsql, PostgreSQL migration ve başlangıç verileri
- Kısa ömürlü JWT access token ve döndürülen HttpOnly refresh token
- Backend rol politikaları; frontend korumasından bağımsız yetkilendirme
- Request DTO doğrulaması ve domain iş kuralları
- Problem Details tabanlı global hata yanıtları ve trace ID
- Docker ve kalıcı PostgreSQL volume yapılandırması

### Uygulama kapsamı

- Web ve Windows istemcilerinde karşılaştırılabilir sipariş, palet ve barkod ekranları
- Müşteri bağlantılı sipariş, sipariş satırı ve revizyon
- Halı, iplik, kumaş ve standart malzeme kartları
- Lot/barkod temelli stok ve depo hareketleri
- Reçete/BOM ve üretim ihtiyacı
- HEX, RGB ve WIN kodlu renkler ile canlı palet önizlemesi
- ZPL düzenleme, komut sözlüğü ve senkron barkod önizlemesi
- Desen arşivi ve tezgâh programlama iş akışları

```text
Login
  -> kimlik doğrulama
  -> kısa ömürlü JWT
  -> hashlenmiş ve döndürülen refresh token
  -> korunan API
  -> süre dolunca tek yenileme + istek tekrarı
  -> logout ile refresh token iptali
```

Frontend route guard yalnızca kullanıcı deneyimini yönetir. Asıl erişim kontrolü API içinde JWT imzası, issuer, audience, süre ve rol doğrulamasıyla yapılır.

## English

This repository is a controlled portfolio surface rather than a distribution of the complete product. It makes engineering quality reviewable without turning the private ERP into a cloneable package.

### Product focus

The active development priority is the web application. Review should focus on the browser experience, protected frontend routes, ASP.NET Core API, JWT sessions, PostgreSQL persistence and Docker delivery.

Web, Windows desktop and Android clients have been prepared for the platform. They consume the shared REST API and JWT identity model. Their use cases and feature coverage may differ; the web application is the reference client and receives new capabilities first.

### Suggested review order

| Time | Area | What it shows |
|---|---|---|
| 1 minute | README and product visuals | Domain understanding, product thinking and delivered UI |
| 3 minutes | `docs/ARCHITECTURE.md` | Layer boundaries, dependency direction and decisions |
| 3 minutes | `samples/DemoErp.Sample` | C#, .NET 9, Minimal API, DTOs and domain validation |
| 2 minutes | [`docs/CODE_EXAMPLES.md`](CODE_EXAMPLES.md) | JWT, protected endpoint, DTO and error-contract excerpts |
| 2 minutes | `docs/VERIFICATION.md` | Build, smoke-test and verification discipline |
| 1 minute | Security summary | JWT, refresh rotation, authorization and error handling |

The private application uses ASP.NET Core 9, EF Core, PostgreSQL, JWT authorization, rotating refresh tokens, request validation, Problem Details and Docker. Public assets intentionally omit the complete domain implementation, databases, credentials, customer data, production designs and machine integrations. See [publication scope](PUBLICATION_SCOPE.md).

The README includes three web-client and three Windows-client visuals for the same representative workflows. This makes the shared product model and client-specific interaction choices directly comparable.
