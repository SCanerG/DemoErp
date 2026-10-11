# Yeni bilgisayarda kurulum — Windows / PowerShell

[Türkçe README](../README.tr.md) · [English README](../README.md) · [Güvenlik ve Admin kurulumu](SECURITY.md)

Bu kılavuz, GitHub'dan indirilen DemoErp sürümünü yerel bilgisayarda çalıştırır. Komutları sırasıyla uygulayın. İlk kurulum internet bağlantısı gerektirir; imaj ve bağımlılık indirmeleri bağlantıya göre zaman alabilir. Bu akış yerel demo içindir, halka açık üretim dağıtımı değildir.

## 1. Gerekli programları kurun

| Sıra | Program | Kurulum ve amacı |
| --- | --- | --- |
| 1 | Git for Windows | [Resmi indirme](https://git-scm.com/install/windows); kaynak kodu indirmek için |
| 2 | Docker Desktop | [Resmi Windows kurulumu](https://docs.docker.com/desktop/setup/install/windows-install/); uygulama ve veritabanını çalıştırmak için |
| 3 | Tarayıcı | Edge, Chrome veya Firefox; arayüzü açmak için |

Docker'ın resmi sayfasından bilgisayarınızın Windows, WSL ve sanallaştırma gereksinimlerini kontrol edin. Docker Desktop'ı kurduktan sonra açın ve motorun hazır olmasını bekleyin. Bu proje **Linux konteynerleri** kullanır. Kurulum yeniden başlatma istiyorsa tamamlayın.

Git için, bilgisayarda WinGet varsa resmi Git sayfasındaki alternatif komut kullanılabilir:

```powershell
winget install --id Git.Git -e --source winget
```

Program kurulumlarından sonra yeni bir PowerShell penceresi açın:

```powershell
git --version
docker version
docker compose version
```

Üç komut da çalışmalı; `docker version` hem Client hem Server bilgisi göstermeli. Server bağlantı hatası varsa devam etmeden Docker Desktop'ı çalıştırın.

Docker ile çalıştırmak için bilgisayara ayrıca .NET SDK, Node.js, PostgreSQL, Visual Studio veya VS Code kurmanız gerekmez. Derleme araçları Docker imajlarında bulunur. Kaynak kod üzerinde geliştirme/test yapmak isterseniz [geliştirme kılavuzuna](DEVELOPMENT.md) bakın.

## 2. Projeyi indirin

Yazma izniniz olan bir çalışma klasöründe PowerShell açın:

```powershell
git clone https://github.com/SCanerG/DemoErp.git
cd DemoErp
git log -1 --oneline
```

Bundan sonraki Docker komutlarını `docker-compose.yml` dosyasının bulunduğu bu klasörde çalıştırın. `git log` çıktısı, indirdiğiniz sürümü gösterir.

## 3. Gerekli dosyaları tanıyın

| Dosya | İşlevi | Yapacağınız işlem |
| --- | --- | --- |
| [docker-compose.yml](../docker-compose.yml) | PostgreSQL, API ve arayüz servislerini bağlar | Varsayılan kurulumda değiştirmeyin |
| [.env.example](../.env.example) | Yerel ortam ayarlarının örneği | `.env` olarak kopyalayın |
| `.env` | Bilgisayarınıza özel portlar ve ilk Admin ayarları | Aşağıdaki bootstrap satırlarını düzenleyin; Git'e eklemeyin |
| [backend/Dockerfile](../backend/Dockerfile) | ASP.NET Core uygulamasını derler ve çalıştırır | Compose otomatik kullanır |
| [frontend/Dockerfile](../frontend/Dockerfile) | React arayüzünü derler ve Nginx üzerinden sunar | Compose otomatik kullanır |

## 4. İlk yönetici hesabını yapılandırın

**Yeni kurulum boş veritabanıyla başlar.** GitHub'daki demo hesapları tablosu kullanıcıları veya örnek iş kayıtlarını otomatik oluşturmaz. İlk Admin'i aşağıdaki mekanizma ile, diğer hesapları arayüzden oluşturun.

İlk kurulumda:

```powershell
Copy-Item .env.example .env
notepad .env
```

Zaten kullandığınız bir `.env` varsa üzerine kopyalamayın; mevcut dosyayı düzenleyin.

Dosyadaki mevcut dört bootstrap satırını şu değerlerle değiştirip kaydedin:

```dotenv
BOOTSTRAP_ADMIN_ENABLED=true
BOOTSTRAP_ADMIN_NAME=Demo Admin
BOOTSTRAP_ADMIN_EMAIL=admin@example.com
BOOTSTRAP_ADMIN_PASSWORD=qwe123456789 # gitleaks:allow
```

Diğer satırları varsayılan bırakın. `AI_ENABLED=false` kalabilir; ERP'yi başlatmak için AI anahtarı gerekmez. Bu herkese açık parola yalnızca yerel, sentetik demo içindir.

Bootstrap yalnızca ilk Admin'i oluşturur. Mevcut kullanıcının parolasını veya rolünü değiştirmez; başka Admin varsa yeni bootstrap hesabı oluşturmaz. **Bu e-postayı önceden normal kayıt ekranıyla oluşturmayın.** Normal kayıt Viewer verir; aynı e-posta çakışması bootstrap başlangıcını engelleyebilir. Mevcut veritabanında bir Admin varsa onunla kullanıcı yönetimini kullanın.

## 5. Derleyin ve çalıştırın

```powershell
docker compose up -d --build --wait
docker compose ps
```

İlk komut kaynak kodu derler, servisleri başlatır ve sağlık kontrollerini bekler. İkinci komutta `postgres`, `backend` ve `frontend` servislerinin çalıştığını, sağlık kontrollerinin `healthy` olduğunu kontrol edin. İlk derleme tamamlanmadan tarayıcıda sonuç beklemeyin.

API başlangıçta EF Core migration'larını uygular; elle veritabanı veya tablo oluşturmanız gerekmez. Veriler Docker'ın isimli PostgreSQL volume'unda tutulur. PostgreSQL portu bilgisayara açılmaz.

## 6. Açın ve doğrulayın

```powershell
Invoke-RestMethod http://localhost:5080/health/ready
```

Sağlık kontrolünden başarılı yanıt bekleyin. Sonra tarayıcıda:

| Adres | Kullanım |
| --- | --- |
| [http://localhost:3000](http://localhost:3000) | Uygulama |
| [http://localhost:5080/swagger](http://localhost:5080/swagger) | API dokümantasyonu; varsayılan Development ortamında |
| [http://localhost:5080/health/ready](http://localhost:5080/health/ready) | API/veritabanı hazır olma kontrolü |
| [http://localhost:5080/health/live](http://localhost:5080/health/live) | API sürecinin çalıştığını gösterir |

`admin@example.com` / `qwe123456789` ile giriş yapın. Boş dashboard normaldir; yeni kurulumda önce iş kayıtları oluşturulmalıdır.

## 7. İlk yönetici oluşturmayı kapatın

Başarılı Admin girişinden sonra `.env` dosyasını yeniden açın:

```powershell
notepad .env
```

Şu iki satırı değiştirin:

```dotenv
BOOTSTRAP_ADMIN_ENABLED=false
BOOTSTRAP_ADMIN_PASSWORD=
```

Kaydedip API'yi yeni ayarlarla yeniden oluşturun:

```powershell
docker compose up -d --wait
docker compose ps
```

Admin hesabı veritabanında kalır. Bu işlem yalnızca başlangıçta kullanılan parola ayarını kaldırır ve bootstrap'ı kapatır.

## 8. Diğer demo kullanıcılarını oluşturun

Admin hesabıyla kullanıcı yönetimini açın ve iki kullanıcı oluşturun. Uygulama rolünü aşağıdaki gibi seçin; ikisini de aktif bırakın:

| Görünen ad | E-posta | Rol | Parola |
| --- | --- | --- | --- |
| Demo Viewer | `viewer@example.com` | `Viewer` | `qwe123456789` |
| Demo Operations | `operation@example.com` | `Manager` | `qwe123456789` |

Viewer yalnızca görüntüler. Manager iş kayıtları oluşturabilir/güncelleyebilir. Admin ayrıca kullanıcı yönetimi ve yönetici işlemlerini yapar. Rol değişikliğinden sonra çıkış yapıp yeniden giriş yapın.

Yeni sürüm; satın alma, çoklu depo, stok transferi, ürün reçetesi ve üretim emri ekranlarını da içerir.

Kısa doğrulama senaryosu: Admin ile kategori → ürün → müşteri → stok girişi → sipariş oluşturun. Siparişi onaylayarak stok azalmasını kontrol edin. Viewer ile tekrar giriş yaparak yazma kontrollerinin görünmediğini doğrulayın.

### Üretim ve satın alma demosu

Önce kategori ve ürünleri oluşturun; üretim için hammadde ve mamul sınıflarını doğru seçin.

1. Tedarikçi ve satın alma siparişi oluşturun; siparişi gönderip mal kabulüyle seçilen depoya stok alın.
2. Mamul için hammaddeleri ve miktarlarını içeren reçete oluşturup Admin olarak etkinleştirin.
3. Etkin reçete, miktar ve kaynak/hedef depolarla üretim emri oluşturun.
4. Emri serbest bırakın, başlatın ve tamamlayın. Başlatma hammaddeleri tüketir; tamamlama mamul stokunu artırır. Yetersiz stok varsa önce stok sağlayın.
5. Mamulü satış siparişine ekleyip onaylayın; depodaki çıkışı ve hareket geçmişini gösterin.

Depolar arası hareketi ayrıca stok transferi ekranından gösterebilirsiniz. İş kayıtları otomatik doldurulmaz. [İş akışı ve mimari](ARCHITECTURE.md), [testler](TESTING.md) ek açıklamalar içerir.

## 9. Durdurma ve tekrar açma

Durdurun:

```powershell
docker compose stop
```

Aynı konteynerleri tekrar başlatın:

```powershell
docker compose start
```

Konteynerler kaldırılmışsa `.env` ayarlarını kullanarak yeniden oluşturun:

```powershell
docker compose up -d --wait
```

`docker compose down` konteynerleri kaldırır; isimli veritabanı volume'unu korur. **Verileri saklamak istiyorsanız `docker compose down -v` kullanmayın:** `-v` veritabanı volume'unu da kaldırır. Proje klasörünü başka bilgisayara kopyalamak veritabanını taşımaz.

## 10. Sorun giderme

Önce servis durumunu ve son logları kontrol edin:

```powershell
docker compose ps -a
docker compose logs --tail 100 backend postgres frontend
```

| Belirti | Kontrol / çözüm |
| --- | --- |
| `git` veya `docker` bulunamıyor | Programı kurun, yeni PowerShell açın |
| Docker motoruna bağlanamıyor | Docker Desktop'ı açın; Linux konteynerleri ve WSL durumunu resmi kılavuzdan kontrol edin |
| `no configuration file provided` | `docker-compose.yml` bulunan proje klasörüne geçin |
| İmaj / NuGet / npm indirme hatası | İnternet, DNS, kurum proxy'si ve paket kaynaklarına erişimi kontrol edin |
| Backend sağlıksız | Backend/PostgreSQL loglarını inceleyin; bootstrap alanlarının ve `.env` ayarlarının doğru olduğunu kontrol edin |
| Port zaten kullanımda | Aşağıdaki dört ayarı birlikte değiştirip yeniden derleyin |
| Arayüz açılıyor ama veri gelmiyor | API sağlık kontrolü, API adresi ve CORS origin'ini kontrol edin |
| Admin girişi başarısız | İlk açılışta bootstrap açık mıydı? Yeni veritabanında kullanıcı oluşturuldu mu? Mevcut Admin/e-posta çakışması var mı? |
| Oluşturma düğmeleri görünmüyor | Kullanıcının rolünü kontrol edin; Viewer yazamaz |

3000/5080 portları doluysa `.env` içinde şu dört satırı birlikte değiştirin:

```dotenv
FRONTEND_PORT=3001
BACKEND_PORT=5081
FRONTEND_ORIGIN=http://localhost:3001
VITE_API_BASE_URL=http://localhost:5081/api
```

Ardından:

```powershell
docker compose up -d --build --wait
Invoke-RestMethod http://localhost:5081/health/ready
```

Arayüzü bu kez `http://localhost:3001` üzerinden açın. `VITE_API_BASE_URL` derleme sırasında JavaScript'e gömülür; değiştirdikten sonra yeniden derlemek gerekir. Tarayıcı API adresinde Docker'ın iç servis adı `backend` kullanılmaz.

Mevcut PostgreSQL volume'unda `.env` içindeki `POSTGRES_PASSWORD` değerini değiştirmek veritabanı parolasını otomatik değiştirmez. Çalışan kurulumdaki parolayı rastgele değiştirmeyin veya sorunu çözmek için volume'u silmeyin.

## Kurulum kontrol listesi

- [ ] Git ve Docker komutları çalışıyor; Docker Server hazır.
- [ ] Kaynak kod GitHub'dan alındı; commit kontrol edildi.
- [ ] `.env` hazır; ilk Admin başlangıçtan önce yapılandırıldı.
- [ ] Üç servis sağlıklı; `/health/ready` başarılı.
- [ ] Admin girişi başarılı; bootstrap kapatıldı ve parola ayarı temizlendi.
- [ ] Viewer ve Manager hesapları oluşturuldu; roller doğrulandı.
- [ ] Bir iş akışı ve durdurma/başlatma sonrası veri kalıcılığı gösterildi.

Bu kılavuzun akışı Compose, Dockerfile, ortam örneği ve bootstrap davranışına göre hazırlanmıştır. Kurulumu doğrulamak için ayrı bir bilgisayarda veya temiz sanal makinede aynı yayımlanmış sürümü kullanın.
