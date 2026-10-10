# Rogue World — HOI4-Benzeri Grand Strateji Oyunu

## Geliştirme Planı (Adım Adım, Her Adım Test Edilecek)

Her milestone **Godot headless + editörde oynanarak** test edilecek. Bir sonraki milestoneye geçilmeden önce mevcut milestone %100 çalışır durumda olmalı.

---

### ✅ Milestone 0: Proje İskeleti
- [x] `project.godot`, `RogueWorld.csproj`, `.sln`, `.gitignore`
- [x] `Scenes/main.tscn` + `Scripts/Main.cs` (kök node)
- [x] `icon.svg`
- [x] **Test:** `dotnet build` + `godot --headless --import` + `godot --headless --quit-after 10` → "Rogue World hazır!" çıktısı

---

### ✅ Milestone 1: Core Game Loop (Tarih Sistemi, Ana Menü, Oyun Sahnesi, HUD)
**Hedef:** 1 Ocak 1936 başlangıcı, günlük tick, hız kontrolleri (0-5), Ana Menü → Oyun geçişi, üst HUD (tarih, hız, pause).

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 1.1 | `GameManager` (Autoload): tarih (DateTime), toplam gün, hız (0-5), gün tick sinyalleri | Headless: 10 frame'de ~3 gün ilerler, tarih günlüğü yazdırılır | ✅ Bitti |
| 1.2 | `Scenes/main_menu.tscn` + `Scripts/MainMenu.cs`: START / QUIT butonları | Editörde Play → menü açılır, START tıklandığında oyun sahnesine geçer | ✅ Bitti |
| 1.3 | `Scenes/game.tscn` + `Scripts/Game.cs`: HUD (tarih etiketi, hız butonları 1x-5x, PAUSE) | Oyun sahnesinde tarih güncellenir, hız butonları çalışır, Space = pause | ✅ Bitti |
| 1.4 | Ana menüden oyun sahnesine geçiş, tekrar ana menüye dönüş (ESC → PauseMenu) | Editörde test: Menü → Oyun → ESC → Pause Menü → RESUME / MAIN MENU | ✅ Bitti |
| 1.5 | SettingsSystem: ses, tam ekran, çözünürlük kaydet/yükle | Ayarlar değişir, oyunu kapatıp açınca korunur | ✅ Bitti — `SettingsPanel.cs` + `Scenes/settings_panel.tscn` (ses slider, fullscreen checkbox, çözünürlük OptionButton, BACK); ana menü ve PauseMenu'den açılıyor, değerler `SettingsSystem` üzerinden kaydedilip açılışta panele yansıyor |

---

### ✅ Milestone 2: Dünya Haritası (GeoJSON Ülke Poligonları)
**Hedef:** HOI4 stili provinces yerine **ülke bazlı** poligon haritası (GeoJSON), pan/zoom, ülke seçimi, ülke bilgi paneli.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 2.1 | `Data/countries.geo.json` (ülke poligonları, ~180 ülke) + `Data/countries_data.json` (1936 verisi: GDP, nüfus, asker, kaynaklar) | Dosyalar yerinde, JSON geçerli | ✅ Bitti (180 ülke, 52 ayrıntılı veri) |
| 2.2 | `WorldMap` (Node2D): GeoJSON okur, Polygon2D + Line2D ile çizer, kamera pan/zoom (sağ tık sürükle, tekerlek), WASD pan | Editörde: harita render olur, zoom/pan sorunsuz | ✅ Bitti |
| 2.3 | Ülke tıklama seçimi (sol tık) → seçim alt çubukta "Selected: Ülke Adı" + sol panelde CountryInfoPanel açılır | Tıklanan ülke vurgulanır, panel bilgiler gösterir | ✅ Bitti |
| 2.4 | Ülke renklendirme: temel renk (hash tabanlı), seçili = altın, hover = açık ton | Görsel geri bildirim çalışır | ✅ Bitti (kısık palet + altın pulse çerçeve) |
| 2.5 | `CountryInfoPanel`: bayrak/adı, GDP, nüfus, askerî güç, kaynaklar, askeri fabrikalar, sivil fabrikalar | Panel veriler doğru yüklenir | ✅ Bitti (GDP tahmini, bayrak placeholder, PLAY AS butonu) |

**Görsel iyileştirmeler (7/7 tamam):** ortak tema, okyanus degrade + kısık palet + zoom-adaptif sınırlar, zoom bazlı etiketler + kamera lerp, 3-bölümlü HUD + tooltip, panel/buton animasyonları, ana menü animasyonlu arka plan + altın başlık, era tonu + gündüz/gece terminatörü.

---

### ✅ Milestone 3: Ülke Veritabanı ve Oyuncu Ülke Seçimi
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 3.1 | `CountryDatabase`: 1936 başlangıç verileri (major: GER, SOV, USA, ENG, FRA, JPN, ITA, CHI, POL, TUR... + minor'lar) | Seçilen her ülke için gerçekçi değerler döner | ✅ Bitti — 52 ülke JSON + deterministik fallback + 27 HOI4 takma adı (GER→DEU, JAP→JPN, CHI→CHN...) |
| 3.2 | "Play as [Ülke]" butonu CountryInfoPanel'de → `GameManager.SetPlayerCountry()` | Oyuncu ülke seçer, HUD'da ülke adı/bayrak görünür | ✅ Bitti — buton durumu geri bildirimli ("PLAYING AS", disabled), GD.Print onay |
| 3.3 | Political Power birikimi (günlük +2.0 PP — HOI4 temel; plandaki +1.5 yerine), Stability %, War Support % | HUD'da PP/S/W güncellenir | ✅ Bitti — DailyTick: PP +2.0/gün + manpower; HUD sinyalle güncellenir |

**Görsel (küçük):** Oyuncu ülkesi sınırına kalıcı yarı saydam altın vurgu (seçim pulse'undan ayrı). ✅

---

### 📋 Milestone 4: Ekonomi — Fabrikalar, İnşaat, Kaynaklar
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 4.1 | Sivil Fabrikalar / Askeri Fabrikalar / Tersaneler sayacı | Ülke verisinden başlangıç sayıları gelir | ✅ EconomySystem.TotalCivFactories/MilFactories/Dockyards (CountryData'dan) |
| 4.2 | İnşaat kuyruğu: 5 yapı tipi, 70/25/30 gün, seri işleme (max 15 fabrika/hat) | İnşaat başlatılır, kuyrukta progress bar ile görülür, günlük ilerler, tamamlandığında fabrika sayısı artar | ✅ EconomySystem + EconomyPanel (5 buton, kuyruk listesi, iptal) |
| 4.3 | Tüketim malları (CG): 5 yasa (Sivil 30% → Toplam 10%), yasa değişimi 150 PP | Yasa OptionButton ile değiştirilir, CG oranı değişir, boş sivil fabrika anında güncellenir | ✅ EconomySystem.ChangeLaw + EconomyPanel law seçici |
| 4.4 | Kaynaklar: 6 kaynak (Oil/Rubber/Steel/Al/Tu/Cr) — üretim, ticaret (%80 ihraç limiti), stok | Kaynak paneli: stok / günlük üretim / ithalat konvoyu görünür | ✅ EconomyPanel kaynak gridinde stok+üretim+ithalat kaynağı; ihracatçı adı |
| 4.5 | Ticaret otomatikleşmesi: eksik kaynak için konvoy atama, %80 ihraç limiti, günlük ticaret günlüğü | Stok eşiğinin altındaki kaynaklar için konvoy atanır, ticaret günlüğü loglanır | ✅ TradeDeal modeli (2 birim/gün), %80 ihraç limiti, ihracatçı seçimi, anlaştırma/iptal logu |

**Tamamlanan:** EconomySystem (core), EconomyPanel (UI), GameHud ECONOMY butonu. M4 işlevsel %100.

---

### 📋 Milestone 5: Araştırma Ağacı (Technology Tree)
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 5.1 | Kategoriler: Infantry, Support, Armor, Artillery, Naval, Air, Industry, Electronics, Doctrines (Land/Naval/Air) | Ağaç UI kategorilerle açılır |
| 5.2 | Teknolojiler: ID, isim, açıklama, era (1936/1939/1941/1943), maliyet (research cost), önkoşullar, etkiler | Bir teknoloji araştırılır, etkileri (ör. +10% org) uygulanır |
| 5.3 | Araştırma slotları: başlangıç 2 slot, odak/teknoloji ile +1 (max 5) | Slot sayısı artırılır, eşzamanlı araştırma çalışır |
| 5.4 | Research bonus: focus/olay/decoder/teknoloji paylaşımı | Bonus uygulanır, araştırma hızı artar |
| 5.5 | Research panel UI: filtre (era/kategori), ilerleme çubuğu, "Start Research" butonu | Editörde test: teknoloji seçilir, günlük tick'te ilerler, tamamlanır |

---

### 📋 Milestone 6: Üretim (Production Lines)
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 6.1 | Ekipman tipleri: Infantry Equipment, Support Equipment, Motorized, Light/Medium/Heavy Tank, Fighter/CAS/Naval Bomber/Strategic Bomber, Destroyer/Cruiser/Battleship/Carrier/Submarine | Tüm ekipman tipleri listelenir |
| 6.2 | Üretim hatları: askeri fabrikadan çizilir, efficiency (başlangıç %10, günde +0.25% max %100), variant upgrade penalty | Hat eklenir, efficiency artar, stockpile günlük artar |
| 6.3 | Stok yönetimi: depodaki ekipman, kaybolan/hasar gören, askeri fabrikaya atama | Stok paneli doğru gösterir |
| 6.4 | Variant tasarımı (Tank Designer / Plane Designer basitleştirilmiş): modüller, maliyet, istatistikler | Yeni variant tasarlanır, üretim hattına atanır |

---

### 📋 Milestone 7: Askeri — Bölükler, Ordular, Savaş
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 7.1 | Bölük şablonu (Division Template): battalion tipleri (Infantry, Artillery, AT, AA, Engineer, Recon, Signal, Logistics, Maint, Armor, SPG, TD, SPAA, Amphibious, Marine, Mountaineer, Paratrooper) | Şablon oluşturulur, istatistikler (ORG, HP, ATT, DEF, Pierce, Armor, Speed, Supply Use) hesaplanır |
| 7.2 | Bölük yetiştirme: manpower + ekipman → deploy (X gün), reinforcement hızı | Bölük yetiştirilir, haritada army marker olarak görünür |
| 7.3 | Ordular (Armies) ve theater/grup yönetimi, general/field marshal ataması | Ordular oluşturulur, komutan atanır |
| 7.4 | Savaş sistemi (province-based): attacker/defender ORG/HP, combat width, daily tick combat resolution, casualties, equipment loss, retreat | Savaş başlar, günlük tick'te ORG/HP düşer, kazanan taraf işgal eder |
| 7.5 | Front çizgisi / ofansif planlama (basit: right-click → attack order, shift → frontline) | Savaş planı çizilir, bölükler ilerler |
| 7.6 | Kapitülasyon: victory points kontrolü, capitulation threshold, peace conference basiti | Ülke yenilir, kapitüle olur, toprak değiştirir |

---

### 📋 Milestone 8: Diplomasi ve Faksiyonlar
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 8.1 | İlişkiler (-100...+100), opinions, threat/world tension | İlişki paneli çalışır |
| 8.2 | Faksiyon oluşturma/katılma (Axis/Allies/Comintern + custom), faksiyon lideri | Faksiyon oluşturulur, üye eklenir |
| 8.3 | Savaş ilanı, çağrı (call to arms), barış konferansı (basit: puppet/annex/liberate) | Savaş ilan edilir, müttefikler çağrılır, barış yapılır |
| 8.4 | Garanti ver (guarantee independence), askeri erişim, deniz erişimi | Diplomatic actions çalışır |
| 8.5 | World Tension mekanizması: aksiyonlar tension artırır, focus/olaylar tension etkiler | Tension barı güncellenir |

---

### 📋 Milestone 9: Ulusal Odak Ağacı (National Focus Tree)
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 9.1 | Odak düğümleri: ID, isim, açıklama, önkoşul odaklar, mutually exclusive, cost (gün), etkiler (fabrikalar, PP, research bonus, war goal, event tetikleme) | Odak ağacı UI açılır, düğümler bağlanır |
| 9.2 | Oyuncu ülkesine özel focus tree (GER/USA/SOV/ENG/FRA/JAP/ITA + generic) | Oyuncu ülkesine uygun tree yüklenir |
| 9.3 | Odak tamamlandığında efektler uygulanır (fabrikalar artar, research bonus verilir, event fire eder) | Odak seçilir, 70 gün sonra tamamlanır, efektler görülür |
| 9.4 | AI için focus seçim mantığı (priority scoring) | AI ülke odak seçer, ilerler |

---

### 📋 Milestone 10: Olaylar ve Kararlar (Events & Decisions)
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 10.1 | Olay motoru: tetikleyici (tarih, focus tamamlanma, savaş başlangıcı, random), seçenekler (A/B/C), efektler | Olay tetiklenir, seçenek seçilir, efektler uygulanır |
| 10.2 | Kararlar (Decisions): kullanılabilirlik koşulu, maliyet (PP/gün), efekt, cooldown | Karar paneli açılır, karar alınır, efekt görülür |
| 10.3 | Tarihi olaylar seti (Anschluss, Munich, Molotov-Ribbentrop, Pearl Harbor, Barbarossa, D-Day...) | Olaylar tarihlerinde/koşullarında tetiklenir |

---

### 📋 Milestone 11: AI (Diğer Ülkeler)
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 11.1 | AI ekonomisi: fabrika inşaatı, ticaret, research slot kullanımı | AI ülkeler fabrikalar inşa eder, araştırır |
| 11.2 | AI askeri: bölük yetiştirme, şablon güncelleme, front atama | AI orduları haritada hareket eder |
| 11.3 | AI diplomasi: faksiyon oluşturma/katılma, garantiler, savaş ilanı mantığı | AI tarihsel/görünür davranış sergiler |
| 11.4 | AI odak seçimi: priority scoring (threat, ideology, opportunity) | AI odak ağacını ilerletir |

---

### 📋 Milestone 12: Kaydet / Yükle (Save/Load)
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 12.1 | Save slotları (1-10), auto-save (her yılın ocak ayı) | Kaydedilir, dosya oluşur |
| 12.2 | Tüm sistemler state serialize: tarih, ülkeler, ekonomi, araştırma, üretim, askeri, diplomasi, olaylar, odaklar | Load sonrası oyun aynen devam eder |
| 12.3 | Load menüsü: slot bilgisi (tarih, oyuncu ülkes, hız), yükle butonu | Load menüsünden yüklenir, oyun sorunsuz devam eder |

---

### 📋 Milestone 13: Polis / UI / Ses / Performans
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 13.1 | Ses: menü müziği, oyun müziği, UI tık/savaş/barış/olay sesleri, volume slider | Sesler çalar, ayarlar kaydedilir |
| 13.2 | Tooltip sistemi: her UI elemanında hover → detaylı açıklama | Tooltip'ler görünür, faydalı bilgi verir |
| 13.3 | Harita modları: Political (ülke renkleri), Terrain, Supply, Factories, Resources, Air zones | Mod seçimi harita görünümünü değiştirir |
| 13.4 | Ayarlar: grafik (vsync, fullscreen, resolution), ses, oyun (hız, difficulty, ironman) | Ayarlar uygulanır, kaydedilir |
| 13.5 | Performans: 1936-1948 arası 5 hızında 60 FPS (yıllık ~10 saniye), 180 ülke + 5000+ bölük | Profiling: CPU/GPU/MS upto date |

---

### 📋 Milestone 14: Çıkış / Dağıtım Hazırlığı
| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 14.1 | Export presetleri: Windows (x64), Linux, macOS | Export alınır, çalışır |
| 14.2 | Versiyon numarası, changelog, README | Dokümantasyon hazır |
| 14.3 | Steam/itch.io için build pipeline (GitHub Actions) | CI/CD çalışır |

---

## Notlar
- **Test stratejisi:** Her milestone bitiminde:
  1. `dotnet build` → hata yok
  2. `godot --headless --import` → hata yok
  3. `godot --headless --quit-after N` → loglarda beklenilen çıktılar (tarih tick, sistem ready mesajları)
  4. **Editörde Play (F5)** → manuel oynanarak görsel/etkileşim testi
- **Kod stili:** Eski projedeki gibi: Türkçe yorumlar, İngilizce UI metinleri, `GD.Print` debug logları, `GodotObject.IsInstanceValid` singleton korumaları.
- **Mimari:** Sistemler `static` sınıflar (CountryDatabase, EconomySystem, ArmySystem, TechnologyTreeSystem, DiplomacySystem, AllianceSystem, UNSystem, WorldEvents, EquipmentSystem, ResourceSystem, PoliticalPartySystem, CountryAI, EventLog) + sahne node'ları (Game, GameHud, WorldMap, CountryInfoPanel, DecisionPanel, EconomyPanel, WarPanel, UNVotePanel, EventFeedPanel, PauseMenu, SettingsPanel, MainMenu, AudioManager, GameManager).
- **Veri:** `Data/*.json` dosyaları (GeoJSON + 1936 başlangıç verileri) — oyunun kalbi.

---

## Sonraki Adım
✅ **Milestone 4 işlevsel %100** (EconomySystem + EconomyPanel + trade). Editörde F5 ile doğrulama: ECONOMY butonu, inşaat butonları, kuyruk progress, yasa değiştirme (150 PP), kaynak ithalatı (ihracatçı adı), ticaret anlaştırma/iptal logları.

Doğrulama sonrası → **Milestone 5**: Teknoloji Ağacı (Technology Tree) — kategoriler, teknoloji öğeleri, araştırma slotları, bonuslar.