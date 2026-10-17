# M17B — Uluslararası Örgütler (Tasarım Belgesi)

**Durum:** Yalnızca belge — kod yazılmaz.  
**Senaryo:** Modern Dünya (M17) üzerine kurulur. Gerçek kişi adı kullanılmaz (Faz 2 kuralı). Tüm içerik kurgusaldır.

## 1. Amaç

NATO, AB, BM, BRICS, ŞİÖ, Afrika Birliği, ASEAN, Arab Birliği, OPEC gibi uluslararası örgütleri modellemek. Örgütler, ülkeler arası ilişkileri, güvenlik/ticaret yükümlülüklerini ve kolektif karar mekanizmasını yürütür. Oyuncu, örgüt gündemine müdahale edebilir, oy kullanabilir, lobi yapabilir.

## 2. Veri Modeli (17B.1)

`Data/organizations.json` tek kaynak. Her örgüt:

| Alan | Tür | Açıklama |
|------|-----|----------|
| Id | string | Örgüt kimliği (nato, eu, brics, sco, au, asean, arab_league, opec, un) |
| Name | string | Görünen isim |
| Type | enum | Askeri / Politik / Ekonomik / Karma |
| FoundedDate | date | Kurulma tarihi |
| HeadquartersCountryId | string | Merkez ülke (ISO A3) |
| Members | string[] | Başlangıç üye ülke ID'leri |
| SpecialRules | string[] | Özel kural açıklamaları (metin) |

Örgüt listesi: NATO (toplu savunma, Madde 5), AB (ekonomik/politik, tek oylama), BM (Güvenlik Konseyi veto: USA, RUS, CHN, FRA, UK), BRICS (ekonomik/işbirliği), ŞİÖ (güvenlik/ekonomik), Afrika Birliği, ASEAN, Arab Birliği, OPEC (petrol politikası).

Kayıt sayısı başlangıçta `GD.Print` ile loglanır (`IncludeFields=true` kontrolü).

## 3. Üyelik Başvurusu ve Kriterleri (17B.2)

Her örgüt için ayrı, parametreli kriterler:

- **NATO:** Demokrasi eşiği (Polity IV ≥ 7), savunma harcaması ≥ %2 GSYİH, toprak anlaşmazlığı yok, mevcut üyelerin %100 oy onayı.
- **AB:** Ekonomik kriterler (GSYİH başına, enflasyon, borç), hukuk üstünlüğü, tek oylama, uzun katılım süreci.
- **BM:** Herkese açık üyelik; Güvenlik Konseyi daimi üyeleri (USA, RUS, CHN, FRA, UK) veto hakkı.
- **BRICS / ŞİÖ / Afrika Birliği / ASEAN / Arab Birliği / OPEC:** Blok özel kriterleri (coğrafya, ideoloji, ekonomi, kaynak).

Kriterler veride parametreli tanımlıdır; kod sabit değer içermez.

## 4. Ayrılma, Askıya Alma ve İhlal Cevazası (17B.3)

- Ülke herhangi bir örgütten ayrılabilir. NATO: 1 yıl bildirim süresi.
- Örgüt üyelik askıya alabilir: AB Madde 7, NATO toplu savunma reddi.
- BM Güvenlik Konseyi veto ile ihbar edilebilir.
- Etkiler (veride parametreli):
  - NATO ayrılma: toplam savunma bonusu kaybı.
  - AB ayrılma (Brexit tarzı): ticaret cezası + fon kaybı.

## 5. Toplantılar ve Gündem (17B.4)

M16 Takvim/Etkinlik Sistemi'ne bağımlı. Her örgüt için planlı toplantılar:

| Toplantı Türü | Sıklık | Örnek |
|---------------|--------|-------|
| Zirve | Yıllık | Yılbaşı zirfesi |
| Bakanlar toplantısı | Aylık | Örgüt bakanları |
| Kriz toplantısı | Gerektiği anda | Acil karar |

Gündem maddeleri: yaptırım kararı, bütçe onayı, genişleme, ortak operasyon, kriz yönetimi.

Oyuncu: gündem maddesi önerir / lobi yapar / oy kullanır. AI: çıkarlarına göre oy (blok sadakati, ilişki, ideoloji, ekonomik bağımlılık). Kararlar bağlayıcı: yaptırım uygulanır, fon dağıtılır, ortak operasyon tetiklenir.

## 6. Yükümlülükler ve Avantajlar (17B.5)

Veride bonus/ceza çarpanları parametreli:

- **NATO:** Madde 5 (saldırı = hepsi saldırı), askeri standartizasyon (+%10 askerî verimlilik), ortak altyapı fonları.
- **AB:** Tek pazar (ticaret +%15), yapısal fonlar (yıllık GDP %0.5), hukuki uyum (+%5 istikrar).
- **BM:** Meşruiyet bonusu (+%5 dünya itibar/legitimacy), barış güçleri katkısı.
- **BRICS / ŞİÖ:** Alternatif ticaret rotaları, para birimi swap hatları.
- **Afrika Birliği:** AfCFTA (Kıta Serbest Ticaret Alanı) potansiyeli.
- **OPEC:** Petrol kota/fiyat koordinasyonu.

## 7. Bağımlılıklar (17B.6)

- M16: Takvim/Etkinlik Sistemi (toplantı programı).
- M17: Modern Senaryo (blok verisi, üye listesi).
- M22: Kriz/Meşruiyet (yaptırım ve oylama bağlamında).
- M31: Savaş Sonrası Düzen / BM Güvenlik Konseyi (veto ve nükleer sorunlarda).

## 8. Notlar

- Gerçek dünya örgüt isimleri kurgusal bağda kullanılır; gerçek kişi/eski devlet lideri adı yok.
- Kod yazılmaz; bu belge M22+ sürümlerinde referans alınır.
- Her veri değişikliği `GD.Print` ile kayıt sayısı loglanır.
