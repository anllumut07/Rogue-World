# Rogue World — FAZ 2: Gerçek Dünya Simülasyonu

> Bu bölüm TODO.md'nin sonuna "Faz 2" olarak eklenir. Eski **M13 (Cila/UI/Ses/Performans)** ve **M14 (Dağıtım)** Faz 2'den sonra yapılır.
> Milestone numaraları **uygulama sırasıdır**. Her milestone %100 çalışmadan sonrakine geçilmez.

---

## Yön Değişikliği (Temel Kararlar)

1. **Gerçek dünya:** Oyun artık 1936 yerine güncel dünyayı simüle eder. Mevcut 1936 içeriği silinmez, "Tarihi Senaryo" olarak korunur (M17'de senaryo seçici).
2. **Oyuncu bir karakterdir:** Oyuncu ülke değil, kendi karakteriyle başlar; eğitim/kariyer/seçimle ülkeyi yönetir (M24-M25). Sandbox seçeneği: "Doğrudan lider olarak başla".
3. **Gerçek kişi yok:** Liderler, bakanlar, sosyal medya profilleri **kurgusaldır** (rastgele ad-soyad havuzları, ülke kültürüne göre). Gerçek siyasetçi adı kullanılmaz.
4. **Tempo:** Zaman bugünkünden belirgin şekilde yavaş akar, oyuncu haftalık "ajanda" ile oynar (M16).
5. **Sıkıcılık yok:** Hedef, oyuncuyu bağlayan çok sayıda alternatif karar. Her sistem birbirinden farklı seçimler ve sonuçlar sunar, uzun vadeli sonuçlar hatırlanır (olay bayrakları).
6. **Veri odaklı:** Her şey `Data/*.json`. Her sistem başlangıçta yüklediği kayıt sayısını `GD.Print` ile loglar (sessiz yükleme hatası olmasın, `IncludeFields` kontrolü).
7. **Save/Load:** Her yeni sistem kendi `GetSaveData/LoadSaveData` fonksiyonunu ve doğrulama testine eklenmesini içerir (M12'deki A==B testi bozulmaz).
8. **Rastgelelik:** Yalnızca `DeterministicRNG`. `new Random()` yasak.
9. **Mouse/UI:** Mevcut kontroller (sağ tık sürükleme, tekerlek, WASD, ülke seçimi) hiçbir milestone'da bozulmaz. Paneller 1152x648'e sığar, MouseFilter bilinçli seçilir.

---

## Uygulama Sırası

| Sıra | Milestone | Neden bu sırada |
|------|-----------|-----------------|
| 1 | M15 Hızlı İyileştirmeler | Küçük, hızlı kazanç |
| 2 | M16 Zaman ve Tempo | Sonraki her sistem tempoya göre ayarlanır |
| 3 | M17 Modern Dünya Senaryosu | Tüm veri tabanı buna bağlı |
| 4 | M18 İl Sistemi | Birlik hareketi için şart, kayıt yapısını erken sabitler |
| 5 | M19 Birlik Hareketi ve Komut | Kullanıcının görsel hedefi, en riskli parça erken çözülür |
| 6 | M20 İstatistikler ve Bütçe | Simülasyonun çekirdeği |
| 7 | M21 Bakanlıklar | İstatistikleri etkiler |
| 8 | M22 Yasalar, Anayasa, Parlamento | Seçim ve bakanlıklarla bağlı |
| 9 | M23 Seçimler ve Halk | Oyuncunun hedefi |
| 10 | M24-M25 Karakter, Kariyer | Oyun başlangıç akışını değiştirir |
| 11 | M26 Afetler ve Beklenmedik Olaylar | Oyunu canlı tutar |
| 12 | M27 Lider Diplomasisi ve Basın | |
| 13 | M28 Gündem Paneli (X benzeri) | Olay sistemine bağlanır |
| 14 | M29 Casusluk v2 | |
| 15 | M30 Uzay Programı | |
| 16 | M31 Teknoloji ve Nükleer | |
| 17 | M32 Görsel Kimlik ve Harita | Arazi dokuları hazır olunca, sırası esnek |

---

### 📋 Milestone 15: Hızlı İyileştirmeler

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 15.1 | Ülke bilgi paneli (nüfus, GDP, kaynaklar...) **yalnızca ülke seçim ekranında** görünür. Oyun başladıktan sonra bir ülkeye tıklamak sadece küçük kart gösterir (ad, bayrak, ilişki, savaş durumu). | Seçim ekranında tam panel, oyunda küçük kart |
| 15.2 | **Kütüphane** bölümü (ana menüden ve oyun içinden): ülke ansiklopedisi iskeleti, `Data/library/*.json`. İçerik sonra detaylandırılır. | Kütüphane açılır, bir ülkenin bilgisi okunur |
| 15.3 | **Save/Load v2:** mevcut slotun üzerine kaydet (**onay penceresi**), kayıt sil (**onay penceresi**), slot adı, kayıt anının kısa bilgisi. Save/Load menüsü ana menüde ve ESC menüsünde aynı bileşen. | Üzerine yazma ve silme onay ister, iptal edilirse dosya korunur |
| 15.4 | LoadGamePanel'de kullanıcıya görünen hata mesajı ("Kayıt yüklenemedi"). | Bozuk kayıt çökme yapmaz, mesaj gösterilir |
| 15.5 | **Deniz ve okyanus adları:** okyanuslar uzak zoom'da, denizler/körfezler yakın zoom'da görünür (`Data/sea_labels.json`: ad, konum, tür, min/max zoom). Üst üste binme engellenir. | Zoom out → okyanuslar, zoom in → denizler |
| 15.6 | Bilinen sorunlar listesi PROGRESS.md'ye aktarılır (aşağıdaki "Bilinen Sorunlar"). | Liste güncel |

---

### 📋 Milestone 16: Zaman ve Tempo
**Hedef:** Oyun gerçek bir ülke yönetmek gibi hissettirmeli; zaman yavaş, kararlar çok.

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 16.1 | Hızları yavaşlat: 1x ≈ gerçek saniyede yarım gün civarı, 5x bugünkü 1x civarı (değerler ayarlanabilir). | Bir yıl oynamak belirgin şekilde daha uzun sürer |
| 16.2 | **Haftalık ajanda:** liderin haftada sınırlı sayıda etkinlik slotu var (toplantı, miting, basın konuşması, zirve, eğitim, kabine). Slotlar dolunca hafta sonuna kadar beklenir. | Slot sayısı sınırlı, aksiyonlar slota yerleşir |
| 16.3 | **Aylık rapor:** her ay başı danışman toplantısı özeti (istatistik değişimleri, uyarılar, 3-5 karar). | Ay başında rapor penceresi açılır |
| 16.4 | Önemli olayda otomatik duraklama, "sonraki olaya atla" butonu. | Olayda oyun durur, atla butonu çalışır |
| 16.5 | Aksiyonlar süre alır (miting hazırlığı birkaç gün, zirve organizasyonu 1-2 hafta). | Süreli aksiyon günlük ilerler |

---

### 📋 Milestone 17: Modern Dünya Senaryosu
**Hedef:** Gerçek dünyayı simüle eden güncel başlangıç.

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 17.1 | **Senaryo sistemi:** "Modern Dünya" (varsayılan) ve "Tarihi 1936". Başlangıç tarihi, veri dosyaları, olay/odak/teknoloji setleri senaryoya bağlı. | Menüde senaryo seçilir, doğru veri yüklenir |
| 17.2 | Modern ülke verileri: nüfus, GSYİH, ordu, kaynaklar, bloklar (NATO, AB, BRICS, ŞİÖ vb.) yaklaşık gerçek değerler. 50 ayrıntılı ülke ile başla, kalanlar genel değerlerle. Veri kaynağı ve "yaklaşık değerler" notu dosyada belirtilir. | Ülke paneli modern veriyi gösterir |
| 17.3 | Kurgusal liderler: her ülke için rastgele ad-soyad (ülke kültürüne göre), yaş, parti, özellikler. | Her ülkenin kurgusal lideri var |
| 17.4 | Modern teknoloji tabanı (başlangıç teknolojileri güncel seviyede). | Tech ağacı modern başlar |
| 17.5 | Modern faksiyonlar ve ilişkiler başlangıçta kurulu. | Diplomasi paneli bloklar gösterir |
| 17.6 | Eski tarihi olay/odak içerikleri yalnızca Tarihi senaryoda aktif. | Modern senaryoda 1936 olayları tetiklenmez |

---

### 📋 Milestone 18: İl (Province) Sistemi
**Hedef:** Birlik hareketi için ülkeleri illere böl. **Bu, haritanın en riskli teknik parçasıdır.**

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 18.1 | İl verisi: kamu malı idari bölge verisi (ör. Natural Earth admin-1) ya da ülke poligonlarını dengeli bölgelere ayıran üretim. Hedef: yaklaşık 1000-2500 il (performansa göre). | İller yüklenir, sayı loglanır |
| 18.2 | Komşuluk grafı (kara bağlantıları, deniz/liman bağlantıları). | Her ilin komşu listesi doğru |
| 18.3 | İl sahipliği (ülke → iller), savaşta il el değiştirir, ülke rengi illerden türetilir. | İlhak sonrası harita doğru renklenir |
| 18.4 | Performans: toplu çizim, seçim/hover için hızlı hit-test. Hız 5'te 60 FPS. | FPS ölçümü logda |
| 18.5 | Kaydet/Yükle: il sahipliği ve il durumu kayıtlara eklenir. | A==B testi geçer |

---

### 📋 Milestone 19: Birlik Hareketi ve Komut (HOI4 tarzı)

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 19.1 | Haritada birlik sayaçları (NATO tarzı ikonlar, ülke renginde, zoom'a göre boyut). | Orduların konumunda sayaç görünür |
| 19.2 | Seçim: tek tık, **sürükleyerek kutu seçimi**, Shift ile çoklu. Mevcut harita kaydırma ile çakışmaz. | Birden çok birlik seçilebilir, harita kontrolleri bozulmaz |
| 19.3 | **Komut:** sağ tık ile hedefe hareket, Shift ile ara noktalar, yol önizleme okları. Sağ tık harita kaydırmasıyla çakışmayacak biçimde ayırt edilir (kısa tık = komut, sürükleme = kaydırma). | Birlik hedefe yürür, yol çizgisi görünür |
| 19.4 | Hareket hızı (arazi, hava, ikmal), il başına geçiş süresi. | Hareket süresi ile logla doğrulanır |
| 19.5 | Temas ve savaş: düşman ile karşılaşınca mevcut WarSystem savaş çözümü tetiklenir, hareket durur. | Temasta savaş başlar |
| 19.6 | Cephe hattı gösterimi, ofansif plan (sürükle-bırak ile saldırı oku). | Plan çizilir, birlikler ilerler |
| 19.7 | Deniz ve hava birlikleri (filo, uçak) için aynı komut altyapısı (temel). | Filo denizde hareket eder |
| 19.8 | AI birlikleri aynı sistemi kullanır. | AI orduları illerde hareket eder |

---

### 📋 Milestone 20: İstatistikler ve Bütçe
**Hedef:** Bir ülkenin gerçek göstergeleri.

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 20.1 | Göstergeler: GSYİH, büyüme, **enflasyon, işsizlik, yolsuzluk, suç oranı**, kamu borcu, bütçe açığı, gelir eşitsizliği (Gini), eğitim, sağlık, yaşam süresi, nüfus (doğum/ölüm/göç), enerji, çevre, medya özgürlüğü, yargı bağımsızlığı. | Hepsi panelde görünür, günlük/aylık güncellenir |
| 20.2 | Bütçe: vergi oranları, harcama kalemleri (savunma, sağlık, eğitim, altyapı, sosyal, güvenlik, Ar-Ge), faiz ve borç. | Bütçe değişince göstergeler tepki verir |
| 20.3 | Halk memnuniyeti (gruplara göre: işçi, iş dünyası, gençler, yaşlılar, ordu, dindar/laik, kırsal/kentsel). | Gruplar farklı tepki verir |
| 20.4 | Göstergeler arası bağlantılar (enflasyon↔işsizlik↔memnuniyet, yolsuzluk↔yatırım, eğitim↔teknoloji hızı vb.). Formüller `Data/formulas.json` içinde ayarlanabilir. | Bir göstergeyi değiştirince bağlantılar loglanır |
| 20.5 | İstatistik paneli (grafik, geçmiş eğilim çizgisi). | Panel 1152x648'e sığar |

---

### 📋 Milestone 21: Bakanlıklar ve Kabine

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 21.1 | Bakanlıklar: İçişleri, Maliye, Savunma, Dışişleri, Adalet, Eğitim, Sağlık, Ekonomi/Ticaret, Enerji, Çevre, Tarım, İstihbarat Başkanı. | Kabine paneli açılır |
| 21.2 | Bakan havuzu: kurgusal kişiler (yetenek, dürüstlük, sadakat, popülerlik, parti/ideoloji, özellik: reformcu/şahin/teknokrat...). | Aday listesi görünür |
| 21.3 | **Atama / görevden alma**; bakanın nitelikleri ilgili göstergeleri etkiler (ör. dürüst İçişleri → suç ve yolsuzluk düşer). | Bakan değişince göstergeler değişir |
| 21.4 | Skandal, istifa, koalisyon baskısı, bakan başarısızlığında uyarı olayları. | Skandal olayı tetiklenir |
| 21.5 | Kabine toplantısı (ajanda slotu kullanır). | Toplantı etkisi uygulanır |

---

### 📋 Milestone 22: Yasalar, Anayasa ve Parlamento

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 22.1 | **Anayasa:** yönetim biçimi (başkanlık, parlamenter, yarı başkanlık, monarşi, tek parti), görev süresi ve limit, seçim yaşı, olağanüstü hal kuralları. | Anayasa panelinde görünür |
| 22.2 | **Yasalar:** önerme, ekleme, kaldırma; her yasanın kategorisi, maliyeti ve etkileri (`Data/laws.json`). | Yasa çıkar, etkisi göstergelerde görülür |
| 22.3 | **Parlamento:** partiler, sandalye dağılımı, oylama, veto, koalisyon, referandum. | Oylamada sandalyelere göre sonuç çıkar |
| 22.4 | Otoriter sistemlerde farklı mekanik (parlamento yerine parti kongresi, darbe riski). | Rejim türüne göre panel değişir |

---

### 📋 Milestone 23: Seçimler ve Halk

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 23.1 | Seçim takvimi ve oy tahmini (memnuniyet, ekonomi, skandallar, kampanya). | Seçim günü sonuç hesaplanır |
| 23.2 | Kampanya aksiyonları: miting, reklam, TV, ittifak, vaat (verilen vaatlerin takibi). | Aksiyonlar oy tahminini etkiler |
| 23.3 | Seçimi kaybetmek/kazanmak: devam ya da iktidarın el değiştirmesi, yeni hükümet/kabine. | Kaybedince lider değişir |
| 23.4 | Protesto, grev, güven oylaması, erken seçim, darbe girişimi. | Düşük memnuniyette olaylar tetiklenir |

---

### 📋 Milestone 24: Karakter Oluşturma

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 24.1 | Karakter ekranı: ad, yaş, cinsiyet, doğum ülkesi/kültürü, **görünüm** (katmanlı prosedürel portre: yüz, saç, renkler, kıyafet), kökeni (asker, hukukçu, iş insanı, akademisyen, aktivist). | Karakter oluşturulur, portre görünür |
| 24.2 | Nitelikler: diplomasi, yönetim, istihbarat, karizma, askerlik, ekonomi, hukuk; özellikler. | Nitelikler kayıtlı ve görünür |
| 24.3 | Oyun akışı: ülke seçimi → karakter → (seçenek) "kariyerle başla" ya da "doğrudan lider ol (sandbox)". | İki başlangıç yolu çalışır |
| 24.4 | Karakter kayıt/yükleme, yaşlanma, sağlık, aile, halefiyet (basit). | Karakter yaşlanır, kaydedilir |

---

### 📋 Milestone 25: Kariyer ve Eğitim (Detaylı)
**Hedef:** Üstünkörü değil, uzun soluklu ve çok seçenekli bir gelişim.

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 25.1 | **Siyasetçi yolu:** üniversite (hukuk/iktisat/siyaset), parti gençlik kolu, yerel yönetim, milletvekilliği, bakanlık deneyimi, parti içi yükselme. | Adımlar sırayla açılır |
| 25.2 | **Asker yolu:** harp okulu, rütbeler, görev yerleri, savaş deneyimi, genelkurmay; darbe seçeneği (ağır sonuçlu). | Rütbe yükselir |
| 25.3 | Eğitim ögeleri: dersler, staj, mentor, sınav, tez/proje, ağ kurma. Her seçim zaman, para ve bağlantı kaybı/kazancı getirir; başarısızlık ve tekrar mümkün. | Sınav sonucu nitelikleri değiştirir |
| 25.4 | **Aday olma kriterleri** (anayasadan): yaş, eğitim seviyesi, parti üyeliği, tanınırlık, itibar, sicil. Kriterler sağlanmıyorsa bir sonraki seçime kadar eğitime devam. | Eksik kriter listesi gösterilir |
| 25.5 | Kariyer olayları (skandal, fırsat, mentor kaybı, hastalık). | Olaylar kariyeri etkiler |
| 25.6 | Seçimi kazanınca lider olarak ülkeyi devralma. | Karakter lider olur, kabine devralınır |

---

### 📋 Milestone 26: Doğal Afetler ve Beklenmedik Olaylar

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 26.1 | Afetler: deprem, sel, kuraklık, orman yangını, fırtına, volkan, salgın; bölgeye özel olasılık ve şiddet. | Afet tetiklenir, bölge etkilenir |
| 26.2 | Etkiler: nüfus, ekonomi, altyapı, teknoloji ilerlemesi, memnuniyet. İyileşme süreci. | Göstergeler düşer, zamanla toparlanır |
| 26.3 | Müdahale: acil yardım, uluslararası yardım, yeniden inşa, tahliye; yönetim kalitesi sonucu etkiler. | Seçimler sonucu değiştirir |
| 26.4 | Beklenmedik olaylar: finansal kriz, terör saldırısı, siber saldırı, enerji krizi, endüstriyel kaza, suikast girişimi, sızıntı/skandal. | Olaylar oyuncuya karar sunar |
| 26.5 | **Olay yoğunluğu hedefi:** hafta başına en az bir önemli karar; olay zincirleri ve uzun vadeli sonuçlar (bayrak takibi). | Log: kararlar/hafta ölçülür |

---

### 📋 Milestone 27: Lider Diplomasisi ve Basın

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 27.1 | İkili görüşme, devlet ziyareti, zirve (G20/BM benzeri), hat görüşmesi; ilişki puanına etki. | İlişki değeri değişir |
| 27.2 | Anlaşmalar: ticaret, savunma, vize, enerji, iklim. | Anlaşma yapılır, etkisi görülür |
| 27.3 | Basın toplantısı, TV konuşması, miting; ton seçimi (sert/uzlaşmacı/popülist), etkiler ve risk. | Konuşma göstergeleri etkiler |
| 27.4 | Yaptırımlar ve ticaret savaşları (verme/alma). | Yaptırım ekonomiye yansır |

---

### 📋 Milestone 28: Gündem Paneli (X benzeri)

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 28.1 | Olay → tweet üreteci: her olay türü için şablon cümleler, değişkenli. | Olayda tweetler üretilir |
| 28.2 | **Rastgele kurgusal profiller** (ad-soyad ülke kültürüne göre, avatar: renkli baş harf), türler: vatandaş, gazeteci, muhalefet, resmî hesap, bot. | Profiller çeşitli |
| 28.3 | Gündem (trend) listesi, beğeni/paylaşım sayısı, duygu (olumlu/olumsuz) eğilimi. | Trend listesi güncellenir |
| 28.4 | Tweetlere göre aksiyon: açıklama yap, soruşturma aç, görmezden gel, yasakla (sonuçlu). | Aksiyonlar memnuniyeti etkiler |
| 28.5 | Küfür/nefret içeriği filtresi, günlük tweet sayısı sınırı (performans). | Panel akıcı çalışır |
| 28.6 | Oyunda bu kişiler oyun karakteri olarak yer almaz, yalnızca panelde görünür. | |

---

### 📋 Milestone 29: Casusluk v2

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 29.1 | Casus niteliği: doğum kültürü, **diller** (0-100), **kültür bilgisi**, bölge deneyimi, eğitim, sadakat, gözlem, soğukkanlılık. | Casus kartı görünür |
| 29.2 | **Yatkınlık matrisi** (`Data/espionage_affinity.json`): kültür grupları arası benzerlik (ör. Rus doğumlu casus: Rusya 100%, Belarus ~80%, Ukrayna ~75%; Arap ülkeleri arasında benzer şekilde, diğer gruplar için de). Her ülke için "yatkınlık %" hesaplanır. | Casus-hedef ülke yatkınlığı gösterilir |
| 29.3 | **Yakalanma olasılığı:** düşük yatkınlıkta çok artar; formül taban × (1.5 − yatkınlık/100) × karşı istihbarat × (1 − eğitim bonusu). Düşük yatkınlıkta uyarı. | Aynı görev farklı casuslarla farklı risk |
| 29.4 | **Eğitim kursları:** dil kursu, kültür eğitimi, saha eğitimi (haftalar sürer, para maliyeti, süre boyunca casus müsait değil). | Kurs bitince nitelik artar |
| 29.5 | Casus kiralama havuzu (farklı kültür/diaspora kökenli), maliyet. | Havuzdan casus kiralanır |
| 29.6 | Karşı istihbarat, yakalanınca diplomatik sonuç, casus değişimi. | Yakalanma ilişkiyi etkiler |

---

### 📋 Milestone 30: Uzay Programı
**Hedef:** Çok maliyetli, çok teknoloji isteyen, seviye seviye zor görevler.

| Seviye | Görev | Not |
|--------|-------|-----|
| S1 | Sondaj roketi (suborbital) | Başlangıç roketçilik teknolojisi |
| S2 | İlk uydu (alçak yörünge) | Fırlatma başarısızlık olasılığı |
| S3 | Haberleşme/gözlem uyduları | Ekonomi ve askerî bonus |
| S4 | Ay'a insansız sonda (yakın geçiş/çarpma) | |
| S5 | Ay yörüngesi ve insansız iniş | |
| S6 | İnsanlı yörünge uçuşu | Büyük prestij |
| S7 | Uzay istasyonu | Uzun vadeli maliyet |
| S8 | İnsanlı Ay'a iniş | Çok yüksek maliyet |
| S9 | Mars sondası | |
| S10 | İnsanlı Mars görevi | En üst seviye |

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 30.1 | Uzay teknolojileri ve önkoşulları (teknoloji ağacına bağlı). | Önkoşul yoksa görev kilitli |
| 30.2 | Proje yönetimi: bütçe, süre, başarısızlık olasılığı, fırlatma tekrarı. | Fırlatma başarılı/başarısız olur |
| 30.3 | Ödüller: prestij, teknoloji bonusu, memnuniyet; askerî kullanım (keşif uydusu, uydu karşıtı silah). | Bonuslar uygulanır |
| 30.4 | Uluslararası işbirliği/rekabet. | |

---

### 📋 Milestone 31: Teknoloji Genişlemesi ve Nükleer

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 31.1 | Modern teknoloji dalları: siber, yapay zekâ, insansız araçlar, güdümlü füzeler, hipersonik, füze savunma, yenilenebilir enerji, biyoteknoloji. Silah icadı ve savaşta üstünlük olarak kullanım. | Yeni silahlar savaş istatistiklerini değiştirir |
| 31.2 | **Nükleer program:** uranyum zenginleştirme, reaktör, silah tasarımı, deneme, taşıyıcı (bombardıman, kıtalararası füze, denizaltı), doktrin. | Aşamalar sırayla ilerler |
| 31.3 | **Yaptırımlar ve diplomasi:** BM yaptırımı, ticaret kısıtı, dünya gerilimi, komşu tepkisi, anlaşmadan çıkış. | Program yaptırım tetikler |
| 31.4 | **Kullanım:** nükleer saldırı çok ağır sonuçlu (radyasyon bölgesi, nüfus ve ekonomi kaybı, uluslararası tepki, karşı saldırı, caydırıcılık). | Kullanım sonuçlarla loglanır |
| 31.5 | AI nükleer davranış (caydırıcılık, yaptırım tepkisi). | AI ülkeler sonuçlara tepki verir |

---

### 📋 Milestone 32: Görsel Kimlik ve Harita Güzelleştirme
**Durum:** Arazi dokuları (ChatGPT ile üretilen kesintisiz dokular) hazır olunca. Sırası esnektir.

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 32.1 | **Arayüz stil rehberi** (HOI4 koyu strateji havası + CK3 sıcak/altın detaylar + Age of History 3 temiz harita): palet, yazı tipleri (lisansı uygun, ör. OFL), çerçeveler, düğme stilleri tek temada. Telifli görseller kullanılmaz. | Tüm paneller aynı temada |
| 32.2 | **Deniz görünümü:** koyu turkuaz okyanus, kıyı parıltısı, hafif vinyet. | Okyanus düz renk değil |
| 32.3 | **Arazi dokusu:** enlem/gürültü ile biyom karışımı (çöl, orman, dağ, tundra, kar); ülke renk katmanı yarı saydam. | Arazi renklerin altından görünür |
| 32.4 | Mini harita (+/- zoom, kamera dikdörtgeni). | Tıklayınca kamera gider |
| 32.5 | Birlik ikonları, il sınırları, yol/okların görsel cilası. | |
| 32.6 | Ana menü arka planı (yavaş kayan görsel, üstünde koyu katman). | |

---

## Önerilen Ek Özellikler (Sonra Değerlendirilecek)

- Danışman ekibi (ekonomi, güvenlik, medya danışmanları ve tavsiyeleri)
- İklim ve çevre politikası, enerji geçişi
- Göç ve mülteci krizleri
- Gizli operasyonlar, sızıntılar, WikiLeaks tipi olaylar
- Yargı sistemi ve anayasa mahkemesi etkileri
- Ordu morali ve ikmal hatları, özel kuvvetler
- Dil desteği (TR/EN yerelleştirme dosyası)
- Başarımlar, "Tarih" sayfası (aylık gazete özeti), zorluk seviyeleri, öğretici (tutorial)
- Mod desteği (JSON veri klasörü)

---

## Bilinen Sorunlar / Teknik Borç

- DECISIONS paneli arayüzü yeniden tasarlanacak (HOI4 tarzı kategori tabanlı görünüm).
- LoadGamePanel'de kullanıcıya görünen hata mesajı yok (M15.4).
- Kayıt silme butonu yok (M15.3).
- AI yalnızca ayrıntılı verisi olan ~49 ülkeyi yönetiyor, kalan ülkeler pasif.
- Gerçek FPS değerleri headless testle ölçülemez, her milestone sonunda editörde elle kontrol edilir.
- Savaş dengesi (kayıp ve ilerleme hızı) henüz ayarlanmadı.
- Eski tarihi içerik (1936 odak/olay/teknoloji) yalnızca Tarihi senaryoda kalır.
- Ses dosyaları (müzik, tık sesleri) henüz yok, lisansı uygun dosyalar kullanıcı tarafından eklenecek.
- Harita arazi dokusu ve deniz görünümü ertelendi (M32).
