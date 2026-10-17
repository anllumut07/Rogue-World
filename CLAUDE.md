# Rogue World — Stüdyo Yönergesi

Her zaman Türkçe cevap ver.

## 1. Proje
- **Oyun:** Rogue World, HOI4 / Age of History benzeri grand strateji oyunu.
- **Motor/dil:** Godot 4 + C# (.NET).
- **Repo:** https://github.com/anllumut07/Rogue-World
- **Hedef:** Mekanikler, ekonomi, harita yönetimi ve arayüzü ajanlarla geliştirmek, test etmek ve GitHub'a senkronize etmek.
- **Plan:** `TODO.md` içindeki milestone'lar, sırayla.

## 2. Altyapı (gerçek durum)
Claude Code doğrudan Anthropic'e değil, yerel **LiteLLM proxy**'ye (`http://localhost:4000`) bağlıdır. Proxy istekleri **NVIDIA NIM** üzerindeki ücretsiz modellere yönlendirir. Claude'un `opus` / `sonnet` / `haiku` adları şu modellere eşlenmiştir:

| Claude adı | Proxy takma adı | NVIDIA modeli | Ne için |
|---|---|---|---|
| opus | `opus-nim` | `nemotron-3-ultra-550b-a55b` | Mimari, planlama, çok belgeli analiz |
| sonnet | `sonnet-nim` | `step-5-preview` | Asıl kod ve tasarım işi |
| haiku | `haiku-nim` | `nemotron-3.5-lightning-30b-a3b` | Hafif, hızlı, salt okunur işler |
| (yedek) | `sonnet-backup` | `nemotron-3.5-super-vl-120b-a12b` | Diğerleri hata verirse otomatik devreye girer |

- İki NVIDIA hesabının key'i (`NVIDIA_KEY_1`, `NVIDIA_KEY_2`) her model için dönüşümlü kullanılır. Limit yememek için.
- Ayarlar `config.yaml` içindedir: `drop_params: true`, 3 deneme, 30 sn cooldown, fallback zinciri.
- Key'ler `.env` benzeri dosyadadır. **Key'leri asla commit etme, sohbete yazma, loglama.**
- Başlatma: `BASLAT.bat` (proxy + Claude Code birlikte açılır).

## 3. Ajanlar ve Skill'ler
Kaynak: `Donchitos/Claude-Code-Game-Studios`. `.claude/` altında **48 ajan** ve **37 skill** vardır. Tam liste için `.claude/agents/` ve `.claude/skills/` klasörlerine bak, ajan adlarını tahmin etme.

**Ajanlar 3 katmanlıdır** (gerçek bir stüdyo gibi):
- **Katman 1: Direktörler (opus)** — yön, vizyon ve kritik kararlar.
- **Katman 2: Departman liderleri (sonnet)** — örn. game-designer, lead-programmer, art-director.
- **Katman 3: Uzmanlar (sonnet/haiku)** — tek bir alanda iş yapan ajanlar.

**Çalışma düzeni:** ajanlar önce soru sorar (Ask), sonra taslak gösterir (Draft), onaydan sonra uygular. Görev yetki sırasına göre delege edilir: direktör → lider → uzman.

**Önemli skill'ler** (`/` yazınca listelenir):
- Hafif, hızlı (haiku): `/help`, `/sprint-status`, `/story-readiness`, `/scope-check`, `/changelog`, `/patch-notes`, `/onboard`
- Ağır analiz (opus): `/review-all-gdds`, `/architecture-review`, `/gate-check`
- Diğerleri (sonnet): `/start`, `/brainstorm`, `/sprint-plan`, `/code-review`, `/design-review`, `/asset-audit`, `/security-audit` ve daha fazlası.

Ek kaynaklar (varsa ve işe yarıyorsa): `msitarzewski/agency-agents` (rol bazlı uzman promptları).

## 4. Çalışma Kuralları
**Genel**
- Bir milestone %100 çalışmadan sonrakine geçme.
- Aynı anda **tek** alt görev üzerinde çalış.
- Büyük değişiklikten önce ne yapacağını kısaca açıkla.
- Her alt görev bitince `TODO.md`'de ilgili kutuyu `[x]` yap. `PROGRESS.md` ve `TODO.md`'yi güncel tut.
- Aynı hatayı 3 kez düzeltemezsen "takıldı" yaz ve sonrakine geç.

**Kod**
- Derleme kontrolü: `dotnet build`. Her alt görevden sonra çalıştır.
- **`dotnet run` ve editörü açma.**
- Kod stili: Türkçe yorumlar, İngilizce UI metinleri, `GD.Print` debug logları.
- Godot 4 C# standartlarına, bellek yönetimine ve performansa uy.
- Test kodu (`--focus-test` vb.) normal oyunda çalışmasın.
- Veri yükleyen sistemler başlangıçta kaç kayıt yüklediğini `GD.Print` ile logla. JSON okurken `IncludeFields`'i kontrol et.
- Dosyalar **BOM'suz UTF-8** olmalı (YAML/JSON dahil).

**Arayüz**
- Mouse davranışını bozma: sağ tık sürükleme, tekerlek, WASD, ülke seçimi. Yeni kontrollerde `MouseFilter`'ı bilinçli seç.
- Paneller 1152x648'e sığsın. Açıkken harita girdisini bloklasın, kapanınca geri versin.

**Git**
- Anlamlı her modül/düzeltmeden sonra commit at ve push yap. Örnek: `git commit -m "feat: harita renklendirme C# mantığı eklendi" && git push`
- **`git push --force` yapma.** Gerekirse dur ve kullanıcıya sor.
- Commit'e key, `.env`, `config.yaml` içindeki gizli bilgi ya da büyük binary koyma.
- github a yüklemeler yaparken commitleri her zaman ingilizce ve açıklayıcı güzel şekilde yaz

**Faz 2 kararları**
- Gerçek kişi adı kullanma.
- Rastgelelik yalnızca `DeterministicRNG` ile.
- Her yeni sistem Save/Load'a eklenir ve doğrulama testine girer.

## 5. Yetkiler
Ajanlar şunları onay istemeden yapabilir: kod yazma/düzenleme, test etme, dosya düzenleme, `TODO.md` görevlerini işleme, Git/GitHub işlemleri, ücretsiz (CC0/ücretsiz lisanslı) asset arama. Lisansı belirsiz asset'i projeye ekleme.

## 6. Görsel Yetenekler ve Sınırlar
- **Görsel analiz:** PNG/JPG ekran görüntülerini (UI, harita render'ı) inceleyebilirsin. Bu özellik modele bağlıdır: `step-5-preview` ve `nemotron-3.5-super-vl` görsel girdiyi destekler. `opus-nim` ve `haiku-nim` için doğrulanmadı. Görsel analiz gerektiren işi `sonnet` katmanına ver.
- **Görsel üretme:** Bu kurulumda resim üreten bir model **bağlı değil**. Grafik ihtiyacını şu yollarla çöz: kod ile prosedürel üretim (Godot çizim, shader), SVG, ücretsiz asset, ya da `art-director` ajanının yazılı tasarım tarifi.

## 7. Her Görev Sonunda
Cevabın sonuna **10 satırı geçmeyen "ÖZET"** ekle:
1. Neyi değiştirdin (dosya adlarıyla)
2. Neyi çözemedin
3. Bilinen sorunlar
4. Kullanıcının test etmesi gerekenler

## OTONOM MOD (bu bölüm diğer tüm kuralların ve ajan iş birliği protokolünün (Ask→Draft→Approve) ÜSTÜNDEDİR)
Kullanıcı tüm yetkileri vermiştir ve uzun süreli kontrolsüz çalışma istemiştir. Kullanıcıya soru sorma, onay bekleme, taslak onayı isteme. Belirsizlikte en makul varsayımı seç, gerekçesini GECE-RAPOR.md'ye yaz ve ilerle. Alt görev bitince cevabını BİTİRME: ÖZET'i yalnızca GECE-RAPOR.md'ye yaz, konuşmada tek satır yaz ve hemen sonraki alt göreve geç. "Aynı anda tek alt görev" sıra anlamındadır, her görevden sonra durmak anlamında değil. Cevabı yalnızca şu durumlarda bitir: TODO.md'deki tüm görevler bitti/bloke oldu, üst üste 5 API hatası alındı, ya da kullanıcı STOP.txt dosyası oluşturdu (her alt görev başlamadan önce STOP.txt var mı kontrol et). Yasaklar değişmez: main'e push yok, force push yok, key dosyalarına dokunma, proje dışında silme yok, dotnet run yok.

Sonra devam et. Yeni oturumdasın, hafızan yok: önce `git branch --show-current`, `git log --oneline -8`, TODO.md, PROGRESS.md ve GECE-RAPOR.md oku, `dotnet build` ile durumu doğrula. Dalın gece-calisma olduğundan emin ol. Sonra M18.3'ten (il sahipliği) başlayarak TODO.md'deki alt görevleri sırayla yap. Önceki kurallar geçerli: dotnet build her görevden sonra, aynı hatayı 3 kez düzeltemezsen [BLOKE: neden], her başarılı görevde TODO.md [x], PROGRESS.md, commit ve `git push origin gece-calisma`, her 2 alt görevde GECE-RAPOR.md güncelle, Save/Load ve DeterministicRNG kuralları, mouse kontrollerini bozma. API hatası/429'da `powershell -Command Start-Sleep 60` ile bekleyip devam et.