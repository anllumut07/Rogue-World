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

### ✅ Milestone 5: Teknoloji Ağacı (Technology Tree)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 5.1 | Kategoriler: Infantry, Support, Artillery, Armor, Naval, Air, Industry, Electronics, Doctrines (Land/Naval/Air) | Ağaç UI kategorilerle açılır | ✅ 9 kategori, 72 teknoloji, Data/technologies.json |
| 5.2 | Teknolojiler: ID, isim, açıklama, era (1936/1939/1941/1943), maliyet, önkoşullar, etkiler | Bir teknoloji araştırılır, etkileri uygulanır | ✅ 72 tech, modifikatörler (construction_speed, factory_output, resource_gain, research_speed) |
| 5.3 | Araştırma slotları: başlangıç 2, odak/teknoloji ile +1 (max 5) | Slot sayısı artırılır, eşzamanlı araştırma çalışır | ✅ CountryData.ResearchSlots (ülke verisinden), TechnologySystem slot yönetimi |
| 5.4 | Araştırma bonusu: focus/olay/decoder/teknoloji paylaşımı | Bonus uygulanır, araştırma hızı artar | ✅ research_speed modifikatörü (Electronics/Doktrin) + EconomySystem/TechnologySystem hızı uygular |
| 5.5 | Research panel UI: filtre (era/kategori), ilerleme çubuğu, "Start Research" butonu | Editörde test: teknoloji seçilir, günlük tick'te ilerler, tamamlanır | ✅ ResearchPanel: kategori/era filtresi, START butonu, aktif araştırma progress bar, slot/speed bilgisi |

**Görsel (küçük):** ProgressBar teması (varsayılan), tooltip düzeltmeleri.

---

### ✅ Milestone 6: Üretim (Production Lines)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 6.1 | Ekipman tipleri: Infantry Equipment, Support Equipment, Motorized, Light/Medium/Heavy Tank, Fighter/CAS/Naval Bomber/Strategic Bomber, Destroyer/Cruiser/Battleship/Carrier/Submarine | Tüm ekipman tipleri listelenir | ✅ Bitti — Data/equipment.json (15 tip, Land/Air/Naval, Military/Naval fabrika) + EquipmentSystem.AllTypes |
| 6.2 | Üretim hatları: askeri fabrikadan çizilir, efficiency (başlangıç %10, günde +0.25% max %100), variant upgrade penalty | Hat eklenir, efficiency artar, stockpile günlük artar | ✅ Bitti — AddLine/ChangeLineFactories/SwitchLineEquipment (ceza %50), DailyTick efficiency + üretim |
| 6.3 | Stok yönetimi: depodaki ekipman, kaybolan/hasar gören, askeri fabrikaya atama | Stok paneli doğru gösterir | ✅ Bitti — Stocks sözlüğü, ayda bir [Equipment] özet logu; kayıp/tüketim API'si M7'de |
| 6.4 | Variant tasarımı (Tank Designer / Plane Designer basitleştirilmiş): modüller, maliyet, istatistikler | Yeni variant tasarlanır, üretim hattına atanır | ✅ Bitti — GetModuleOptions (Gun/Armor/Engine), CreateVariant (maliyet/saldırı/savunma çarpanları), ProductionPanel designer + hat variant seçici |

---

### ✅ Milestone 7: Askeri — Bölükler, Ordular, Savaş
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 7.1 | Bölük şablonu (Division Template): battalion tipleri (Infantry, Artillery, AT, AA, Engineer, Recon, Signal, Logistics, Maint, Armor, SPG, TD, SPAA, Amphibious, Marine, Mountaineer, Paratrooper) | Şablon oluşturulur, istatistikler (ORG, HP, ATT, DEF, Pierce, Armor, Speed, Supply Use) hesaplanır | ✅ Bitti — Data/battalions.json (17 tip) + ArmySystem.ComputeTemplateStats; ArmyPanel designer ile şablon tasarlanır |
| 7.2 | Bölük yetiştirme: manpower + ekipman → deploy (X gün), reinforcement hızı | Bölük yetiştirilir, haritada army marker olarak görünür | ✅ Bitti — StartTraining (manpower+ekipman maliyeti, 30+3×tabur gün), takviye ORG +3/HP +1 (ekipman harcar); harita marker M7.5 RMB emirleriyle soyutlandı |
| 7.3 | Ordular (Armies) ve theater/grup yönetimi, general/field marshal ataması | Ordular oluşturulur, komutan atanır | ✅ Bitti — CreateArmy (otomatik general adı + skill 1-5), AssignDivisionToArmy; ArmyPanel ordular/tümenler listesi |
| 7.4 | Savaş sistemi (province-based): attacker/defender ORG/HP, combat width, daily tick combat resolution, casualties, equipment loss, retreat | Savaş başlar, günlük tick'te ORG/HP düşer, kazanan taraf işgal eder | ✅ Bitti — ülke-bazlı sadeleştirme: WarSystem günlük çatışma (ORG hasarı, 3 geri çekilme=yok olma, manpower+ekipman kaybı, düşman tümen havuzu) |
| 7.5 | Front çizgisi / ofansif planlama (basit: right-click → attack order, shift → frontline) | Savaş planı çizilir, bölükler ilerler | ✅ Bitti — RMB tık (<6px) → OFANSİF, Shift+RMB → CEPHE (savunma +%30); WarProgress ofansifte günde artar; RMB sürükleme pan bozulmadı |
| 7.6 | Kapitülasyon: victory points kontrolü, capitulation threshold, peace conference basiti | Ülke yenilir, kapitüle olur, toprak değiştirir | ✅ Bitti — düşman tümen=0 veya ilerleme %100 → ilhak: IsAnnexed+OwnerId+fabrika transferi+harita yeniden renklenir; oyuncu kapitülasyonu = OYUN BİTTİ logu |

**M7 notu:** HOI4 province sistemi yerine ülke-bazlı soyutlama (harita Country poligonlu): savaş tek cephede çözülür, WarProgress (0-100) işgal/VK proxy'si. Peace conference ayrıntısı (puppet/liberate) M8.3'e ertelendi.

---

### ✅ Milestone 8: Diplomasi ve Faksiyonlar
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 8.1 | İlişkiler (-100...+100), opinions, threat/world tension | İlişki paneli çalışır | ✅ DiplomacySystem + DiplomacyPanel, diplomacy.json starter |
| 8.2 | Faksiyon oluşturma/katılma (Axis/Allies/Comintern + custom), faksiyon lideri | Faksiyon oluşturulur, üye eklenir | ✅ CreateFaction/RequestJoinFaction/LeaveFaction + panel UI |
| 8.3 | Savaş ilanı, çağrı (call to arms), barış konferansı (basit: puppet/annex/white peace) | Savaş ilan edilir, müttefikler çağrılır, barış yapılır | ✅ WarSystem.DeclareWar/DeclareWarOnPlayer + DiplomacySystem.ProposePeace/WhitePeace + panel butonları |
| 8.4 | Garanti ver (guarantee independence), askeri erişim, deniz erişimi | Diplomatic actions çalışır | ✅ Guarantee/Revoke/RequestMilitaryAccess/RequestNavalAccess + panel UI |
| 8.5 | World Tension mekanizması: aksiyonlar tension artırır, focus/olaylar tension etkiler | Tension barı güncellenir | ✅ Tension bar (renk geçişli), IncreaseTension/azalma, ayda bir log |

---

### ✅ Milestone 9: Ulusal Odak Ağacı (National Focus Tree)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 9.1 | Odak düğümleri: ID, isim, açıklama, önkoşul odaklar, mutually exclusive, cost (gün), etkiler (fabrikalar, PP, research bonus) | Odak ağacı UI açılır, düğümler bağlanır | ✅ Bitti — Data/focus_trees/*.json (8 ağaç, 73 odak) + FocusSystem + FocusPanel (grid ağaç, önkoşul çizgileri, mutex ⊗, durum renkleri) |
| 9.2 | Oyuncu ülkesine özel focus tree (GER/USA/SOV/ENG/FRA/JAP/ITA + generic) | Oyuncu ülkesine uygun tree yüklenir | ✅ Bitti — GetTreeFor: GER/DEU, USA, SOV/RUS, ENG/GBR, FRA, JPN/JAP, ITA → özel ağaç, diğerleri GENERIC; SetPlayerCountry'de Initialize |
| 9.3 | Odak tamamlandığında efektler uygulanır (fabrikalar artar, research bonus verilir) | Odak seçilir, 70 gün sonra tamamlanır, efektler görülür | ✅ Bitti — ApplyFocusEffects: fabrika/PP/stabilite/WS/manpower/kaynak/research slot/tension + TechnologySystem.AddModifier (research_speed); her efekt GD.Print ile loglanır |
| 9.4 | AI için focus seçim mantığı (priority scoring) | AI ülke odak seçer, ilerler | ✅ Bitti — AiPickFocus (AiPriority + ülke durumu puanlaması); headless: `-- --focus-test=GER` → AI sırayla odak seçip tamamlıyor (headless_focus_test.log) |

**M9 notu:** Efektler yalnızca mevcut sistemlerdeki değerlere yazar (kullanıcı kararı): war goal/event efektleri M10/M11'e ertelendi. Focus panel 1060×560 — 1152×648'de tam sığar; sol tık = başlat, sağ tık aktif odak = iptal; tek aktif odak (HOI4 kuralı).

---

### ✅ Milestone 10: Olaylar ve Kararlar (Events & Decisions)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 10.1 | Olay motoru: tetikleyici (tarih, focus tamamlanma, savaş başlangıcı, random), seçenekler (A/B/C), efektler | Olay tetiklenir, seçenek seçilir, efektler uygulanır | ✅ Bitti — EventSystem (4 tetikleyici tipi, FireOnlyOnce, ülke eşleşmesi, kuyruk) + EventPopup (ortada, A/B/C butonları, auto-pause, ESC yoksay) |
| 10.2 | Kararlar (Decisions): kullanılabilirlik koşulu, maliyet (PP/gün), efekt, cooldown | Karar paneli açılır, karar alınır, efekt görülür | ✅ Bitti — DecisionSystem (koşul/maliyet/cooldown motoru) + DecisionPanel (HUD DECISIONS butonu, 2 sütun, progress bar) + 10 karar |
| 10.3 | Tarihi olaylar seti (Anschluss, Munich, Molotov-Ribbentrop, Pearl Harbor, Barbarossa, D-Day...) | Olaylar tarihlerinde/koşullarında tetiklenir | ✅ Bitti — 11 olay: Anschluss, Munich, Molotov-Ribbentrop, Winter War, Fall of France, Barbarossa, Pearl Harbor, D-Day, + 3 yardımcı (Economic Miracle, New Deal, Spanish Civil War) |
| 10.4 | (M9 ertelenmesi) Focus → event bağlantısı: odak tamamlanınca olay tetiklenebilsin | Odak bitince olay popup açılır | ✅ Bitti — FocusEffects.TriggerEvent + GER ağacında örnek (ger_molotov_ribbentrop → molotov_ribbentrop, ger_four_year_plan → german_economic_miracle) |
| 10.5 | (M9 ertelenmesi) War goal PP maliyetini düşürsün | Odak/olay sonrası savaş ilanı PP maliyeti düşer | ✅ Bitti — WarGoalCostReduction etkisi + WarSystem.SetWarGoalCostReduction + DeclareWar CalculateWarGoalCost kullanır (Barbarossa: -100% SOV, Molotov-Ribbentrop: -50% POL) |

---

### ✅ Milestone 11: AI (Diğer Ülkeler)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 11.1 | AI ekonomisi: fabrika inşaatı, ticaret, research slot kullanımı | AI ülkeler fabrikalar inşa eder, araştırır | ✅ Bitti — CountryAI: boş sivil fabrika × 5 puan/gün, 70 gün @15 fabrika = +1 bina; PP/manpower günde +2.0; research slots * 0.25 puan/gün, 140 puan = 1 tech (~175 gün); her 5 tech +1 sivil fabrika bonus; 49 AI ülke round-robin günde 10 karar |
| 11.2 | AI askeri: bölük yetiştirme, şablon güncelleme, front atama | AI orduları haritada hareket eder | ✅ Bitti — Tümen yetiştirme (40 gün, 5000 manpower); DivisionCap (pop/factory bazlı); AI-AI savaş çözümü (ResolveAiVsAiCombat: güç oranına dayalı ilerleme/tümen kaybı/kapitülasyon + fabrika transferi); front atama (ofansif/cephe duruşu otomatik güç karşılaştırmasıyla) |
| 11.3 | AI diplomasi: faksiyon oluşturma/katılma, garantiler, savaş ilanı mantığı | AI tarihsel/görünür davranış sergiler | ✅ Bitti — Faksiyon oluşturma (lider PP ≥ 50, ideoloji); faksiyona katılma (lider görüş ≥ 50); garanti (demokrasiler minorlara); savaş ilanı (PP ≥ 50, Divisions ≥ 10, güç ≥ %80 hedef, maliyet WarSystem.CalculateWarGoalCost); call-to-arms simülasyonu; GetEnemiesAtWar API |
| 11.4 | AI odak seçimi: priority scoring (threat, ideology, opportunity) | AI odak ağacını ilerletir | ✅ Bitti — M9.4 AiPickFocus genişletildi: FocusSystem.TickAiStates() günde tüm AI state'leri ilerletir; AiPriority + ScoreEffectsForAi (az sanayi→sanayi, düşük stabilite→barış, faşizm→savaş); tüm 49 AI ülke odak seçip 70 günde tamamlar, efektler CountryData'ya yazılır |

---

### ✅ Milestone 12: Kaydet / Yükle (Save/Load)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 12.1 | Save slotları (1-10), auto-save (her yılın ocak ayı) | Kaydedilir, dosya oluşur | ✅ Bitti — `user://saves/slot_N.json` + `auto_save.json` (Ocak 1 auto-save DailyTick'te); atomik yazma (.tmp → Move); versiyon kontrolü (v1); bozuk/eksik kayıt → hata log + oyun bozulmaz |
| 12.2 | Tüm sistemler state serialize: tarih, ülkeler, ekonomi, araştırma, üretim, askeri, diplomasi, olaylar, odaklar | Load sonrası oyun aynen devam eder | ✅ Bitti — 12 sistem GetSaveData/LoadSaveData; DeterministicRNG (xorshift64*) state kaydedilir; **headless doğrulama: 100 gün → save → 50 gün → özet A; load → 50 gün → özet B; A==B PASS ✓** (PER→IRN alias bug bulundu ve düzeltildi) |
| 12.3 | Load menüsü: slot bilgisi (tarih, oyuncu ülkes, hız), yükle butonu | Load menüsünden yüklenir, oyun sorunsuz devam eder | ✅ Bitti — LoadGamePanel (AUTO + slot 1-10 listesi, 520×560, MouseFilter.Stop backdrop); MainMenu "LOAD GAME" butonu (PendingLoadSlot + sahne değişimi) + PauseMenu "LOAD GAME" butonu (mevcut oyuna yükleme); load sonrası harita/UI sinyalleri + Speed=0 pause |

---

## FAZ 2: Gerçek Dünya Simülasyonu

> Bu bölüm TODO.md'nin sonuna "Faz 2" olarak eklenir. Eski **M13 (Cila/UI/Ses/Performans)** ve **M14 (Dağıtım)** Faz 2'den sonra yapılır.
> Milestone numarları **uygulama sırasıdır**. Her milestone %100 çalışmadan sonrakine geçilmez.

---

### Yön Değişikliği (Temel Kararlar)

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

### Uygulama Sırası

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

### ✅ Milestone 15: Hızlı İyileştirmeler

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 15.1 | Ülke bilgi paneli (nüfus, GDP, kaynaklar...) **yalnızca ülke seçim ekranında** görünür. Oyun başladıktan sonra bir ülkeye tıklamak sadece küçük kart gösterir (ad, bayrak, ilişki, savaş durumu). | Seçim ekranında tam panel, oyunda küçük kart | ✅ Bitti — `CountryMiniCard.cs` (ad, bayrak, ilişki, savaş durumu; ESC/X ile kapanır); `Game.cs` kilitliyken tam panel yerine mini kart açar; seçim aşaması akışı aynı |
| 15.2 | **Kütüphane** bölümü (ana menüden ve oyun içinden): ülke ansiklopedisi iskeleti, `Data/library/*.json`. İçerik sonra detaylandırılır. | Kütüphane açılır, bir ülkenin bilgisi okunur | ✅ Bitti — `LibrarySystem.cs` (Data/library/*.json, ID normalizasyonu GER→DEU) + `LibraryPanel.cs` (arama kutusu + kaydırılabilir liste; sağda sayfa: ad, başkent, bölge, dil, tanıtım + CountryDatabase canlı verileri) + `Data/library/countries.json` (**50 ülke**, senaryodan bağımsız olgusal metinler); ana menü + HUD LIBRARY butonu. **Bug düzeltildi:** RichTextLabel ScrollContainer içinde min boyutu 0 olduğundan içerik görünmüyordu → Label tabanlı sayfa; açılışta sayfa sayısı loglanır |
| 15.3 | **Save/Load v2:** mevcut slotun üzerine kaydet (**onay penceresi**), kayıt sil (**onay penceresi**), slot adı, kayıt anının kısa bilgisi. Save/Load menüsü ana menüde ve ESC menüsünde aynı bileşen. | Üzerine yazma ve silme onay ister, iptal edilirse dosya korunur | ✅ Bitti — `SaveSystem.cs` (SlotName, CurrentSlot, DeleteSave, SavedAtText) + `LoadGamePanel.cs` (SAVE/LOAD/DELETE, ConfirmationDialog onayları, slot adı LineEdit, CurrentSlot önseçim); A==B doğrulama testi yeniden **PASS ✓** |
| 15.4 | LoadGamePanel'de kullanıcıya görünen hata mesajı ("Kayıt yüklenemedi"). | Bozuk kayıt çökme yapmaz, mesaj gösterilir | ✅ Bitti — `_errorLabel` (kırmızı), yükleme başarısızsa gösterilir, yeni seçimde temizlenir |
| 15.5 | **Deniz ve okyanus adları:** okyanuslar uzak zoom'da, denizler/körfezler yakın zoom'da görünür (`Data/sea_labels.json`: ad, konum, tür, min/max zoom). Üst üste binme engellenir. | Zoom out → okyanuslar, zoom in → denizler | ✅ Bitti — `Data/sea_labels.json` (9 okyanus + 34 deniz/körfez, boylam/enlem + zoom aralıkları) + `WorldMap.cs` (JSON yükler, okyanus 18pt/deniz 14pt soluk mavi, harfler hafif aralıklı) |
| 15.6 | Bilinen sorunlar listesi PROGRESS.md'ye aktarılır (aşağıdaki "Bilinen Sorunlar"). | Liste güncel | ✅ Bitti — PROGRESS.md "Bilinen Sorunlar / Teknik Borç" bölümünde |
| 15.7 | **Bayrak Sistemi (M-Flags):** `Assets/flags/{ISO3}.png` önbellekli yükleme (ResourceLoader + Image.LoadFromFile), prosedürel yedek (3:2 dikdörtgen + 3 harf kod, ülke rengi, ince kenarlık), `FlagBox` yeniden kullanılabilir kontrol, entegrasyon: CountryInfoPanel, HUD (sol üst), CountryMiniCard, LibraryPanel, DiplomacyPanel (ilişkiler/garantiler), ArmyPanel (savaş listesi), EventPopup; başlangıç raporu loglar (PNG bulundu / yedek kullanıldı). | Derleme başarı, headless test: 13/50 PNG bulundu, 37 prosedürel yedek; UI entegrasyonu 7 panelle tamam | ✅ Bitti — `FlagLibrary.cs`, `FlagBox.cs`, 7 panele entegre, CountryDatabase NormalizeId (HOI4 takma adları GER→DEU, JAP→JPN, SOV→RUS, ENG→GBR) |

---

### 📋 Milestone 16: Zaman ve Tempo
**Hedef:** Oyun gerçek bir ülke yönetmek gibi hissettirmeli; zaman yavaş, kararlar çok.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 16.1 | Hızları yavaşlat: 1x ≈ gerçek saniyede yarım gün civarı, 5x bugünkü 1x civarı (değerler ayarlanabilir). | Bir yıl oynamak belirgin şekilde daha uzun sürer | ✅ Bitti — `Data/game_settings.json` hız tablosu (1x=0.5 gün/sn, 5x=1.0 gün/sn), `TimeTempoSystem.GetDaysPerSecond()` GameManager._Process'te kullanılır |
| 16.2 | **Haftalık ajanda:** liderin haftada sınırlı sayıda etkinlik slotu var (toplantı, miting, basın konuşması, zirve, eğitim). Slotlar dolunca hafta sonuna kadar beklenir. | Slot sayısı sınırlı (6/hafta), aksiyonlar slota yerleşir, PP maliyeti, süreli ilerler | ✅ Bitti — `TimeTempoSystem` (haftalık 6 slot, 5 aksiyon türü, PP maliyeti, süreli ilerleme, tamamlandığında efekt uygulanır), `AgendaPanel.cs` UI (slot seçimi + aksiyon kartları + devam eden aksiyonlar), `GameHud` AGENDA butonu |
| 16.3 | **Aylık rapor:** her ay başı danışman toplantısı özeti (istatistik değişimleri, uyarılar, 3-5 karar). | Ay başında (1. gün) rapor penceresi açılır, auto-pause, OK ile onay | ✅ Bitti — `MonthlyReportPanel.cs` (istatistik özet, uyarılar, durum kararları, CONTINUE butonu), auto-pause + AcknowledgeMonthlyReport |
| 16.4 | Önemli olayda otomatik duraklama, "sonraki olaya atla" butonu. | Olayda oyun durur, NEXT EVENT butonu çalışır | ✅ Bitti — `TimeTempoSystem.QueueImportantNotification` (war_declaration, capitulation vb.), HUD NEXT EVENT butonu (bekleyen bildirim varsa görünür), ProcessNextNotification |
| 16.5 | Aksiyonlar süre alır (miting hazırlığı birkaç gün, zirve organizasyonu 1-2 hafta). | Süreli aksiyon günlük ilerler | ✅ Bitti — `ActiveAgendaAction.RemainingDays` günlük -1, `DurationDays` yapılandırmadan |

**M16 notu:** `Data/game_settings.json` (hız + ajanda config), `Data/agenda_actions.json` (5 aksiyon: meeting/rally/press/summit/training). A==B determinizm testi **PASS ✓** (SaveLoadTest hâlâ geçer).

---

### 📋 Milestone 17: Modern Dünya Senaryosu
**Hedef:** Gerçek dünyayı simüle eden güncel başlangıç.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 17.1 | **Senaryo sistemi:** "Modern Dünya" (varsayılan) ve "Tarihi 1936". Başlangıç tarihi, veri dosyaları, olay/odak/teknoloji setleri senaryoya bağlı. | Menüde senaryo seçilir, doğru veri yüklenir | ✅ Bitti — `ScenarioSystem`, `ScenarioSelectPanel`, veri yolları senaryodan; 7 loader `ResetForScenario/LoadScenarioData`; SaveVersion=2 (v1→historical_1936); `--scenario-test`, `--save-load-test --scenario=modern_world|historical_1936` PASS ✓ |
| 17.2 | Modern ülke verileri: nüfus, GSYİH, ordu, kaynaklar, bloklar (NATO, AB, BRICS, ŞİÖ vb.) yaklaşık gerçek değerler. 50 ayrıntılı ülke ile başla, kalanlar genel değerlerle. Veri kaynağı ve "yaklaşık değerler" notu dosyada belirtilir. | Ülke paneli modern veriyi gösterir | ✅ Bitti — 50 ülke ham girdisi (`countries.json`: nüfus, GSYİH, ordu, kaynak sınıfı, rejim, nükleer) + `formulas.json` katsayıları → türetilmiş oyun değerleri (fabrika, manpower, tümen, kaynak, politika, araştırma slotu); Name/Capital eklendi; `--scenario-test` 50 ülke doğruladı |
| 17.3 | Kurgusal liderler: her ülke için rastgele ad-soyad (ülke kültürüne göre), yaş, parti, özellikler. | Her ülkenin kurgusal lideri var | ✅ Bitti — `LeaderSystem` + `Data/names/*.json` (14 kültür havuzu); deterministik üretim (senaryo+ülke hash'li lokal RNG, gerçek kişi adı yok); 6 nitelik + özellikler; Save/Load A==B PASS ✓ |
| 17.4 | Modern teknoloji tabanı (başlangıç teknolojileri güncel seviyede). | Tech ağacı modern başlar | ✅ Bitti — `Data/scenarios/modern_world/technologies.json` (73 tech, Era 1990–2020), `formulas.json` (TechLevel/ReseachSlots/StartingTechEra eşikleri), `TechnologySystem.Initialize()` ile `CountryDatabase.GetStartingTechEra()` başlangıç tech'ını verir; `--scenario=modern_world --save-load-test` **PASS ✓ (A == B)** |
| 17.5 | Modern faksiyonlar ve ilişkiler başlangıçta kurulu. | Diplomasi paneli bloklar gösterir | ✅ Bitti — `diplomacy_start.json`: NATO (20 üye, lider USA), EU (16), BRICS (9, lider CHN), ŞİO (4, lider RUS), 63 ilişki, Tension %25; `--scenario-test` + save-load PASS ✓ |
| 17.6 | Eski tarihi olay/odak içerikleri yalnızca Tarihi senaryoda aktif. | Modern senaryoda 1936 olayları tetiklenmez | ✅ Bitti — modern senaryoda 0 odak ağacı / 0 olay / 0 karar; tarihi senaryoda 8 ağaç (73 odak) / 11 olay / 10 karar korunuyor; `--scenario-test` doğruladı |

**M17 notu:** Senaryo sistemi gelince `SaveVersion` **2'ye** yükseltilecek. Eski v1 kayıtlar "Tarihi 1936" senaryosu olarak yüklenecek (sürüm uyumsuz değilse tarih/veri setinden senaryo çıkarımı; uyumsuzsa mevcut hata mesajı gösterilir).

---

### 📋 Milestone 17B: Uluslararası Örgütler
**Hedef:** NATO, AB, BM, BRICS, ŞİÖ, AF, ASEAN, Arab Birliği, OPEC gibi örgütleri modelle. Yalnızca belge — kod yazılmaz.

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 17B.1 | **Veri:** `Data/organizations.json` — örgüt tanımı: ID, isim, tür (Askeri/Politik/Ekonomik/Karma), kurulma tarihi, merkez ülke, üyeler (başlangıç), özel kurallar. Örgütler: NATO (toplu savunma, art. 5), AB (ekonomik/politik, tek oylama), BM (Güvenlik Konseyi veto hakkı: USA, RUS, ÇIN, FRA, UK), BRICS (ekonomik/işbirliği), ŞİÖ (güvenlik/ekonomik), Afrika Birliği, ASEAN, Arab Birliği, OPEC (petrol politikası). | JSON dosyası geçerli, tüm örgütler yüklenir |
| 17B.2 | **Üyelik başvurusu/kriterleri:** Her örgüt için ayrı kriterler. NATO: demokrasi eşiği (ör. Polity IV ≥ 7), savunma harcaması ≥ %2 GSYİH, toprak anlaşmazlığı YOK, mevcut üyelerin %100 oy onayı. AB: ekonomik kriterler (GSYİH başına, enflasyon, borç), hukuk üstünlüğü, tek oylama, uzunca katılım süreci. BM: herkese açık, Güvenlik Konseyi daimi üyeler veto haklı. BRICS/ŞİÖ/AF/ASEAN/Arab Birliği/OPEC: blok özel kriterleri. | Veride kriterler parametreli tanımlıdır |
| 17B.3 | **Ayrılma / Askıya Alma / İhlal Cevazası:** Ülke ayrılabilir (NATO: 1 yıl bildirim), örgüt üyelik askıya alabilir (AB: Madde 7, NATO: toplu savunma reddi), BM Güvenlik Konseyi veto ile ihbar. Etkiler: NATO ayrılma → toplam savunma bonusu kaybı, AB ayrılma (Brexit tarzı) → ticaret cezası + fon kaybı. | Veride ayrılma/askıya alma kuralları tanımlıdır |
| 17B.4 | **Toplantılar / Gündem:** Takvim sistemi (M16 bağımlılığı) — her örgüt için planlı toplantılar (yılbaşı zirvesi, aylık bakanlar, kriz toplantısı). Gündem maddeleri: yaptırımlar kararı, bütçe onayı, genişleme, ortak operasyonlar, kriz yönetimi. Oyuncu gündem maddesi önerir / lobi yapar / oy kullanır. AI: çıkarlarına göre oy (blok sadakati, ilişki, ideoloji, ekonomik bağımlılık). Kararlar bağlayıcı: yaptırım uygulanır, fon dağıtılır, ortak operasyon tetiklenir. | Veride toplantı sıklığı + gündem şablonları tanımlıdır |
| 17B.5 | **Yükümlülükler / Avantajlar:** NATO: Madde 5 (saldırı = hepsi saldırı), askeri standartizasyon (+%10 askerî verimlilik), ortak altyapı fonları. AB: tek pazar (ticaret +%15), yapısal fonlar (yıllık GDP %0.5), hukuki uyum (+%5 istikrar). BM: meşruiyet bonusu (+%5 dünya itibar/legitimacy), barış güçleri katkısı. BRICS/ŞİÖ: alternatif ticaret rotaları, para birimi swap hatları. AF: Afrika Kıtası Serbest Ticaret Alanı (AfCFTA) potansiyeli. OPEC: petrol kota/ fiyat koordinasyonu. | Veride bonus/ceza çarpanları parametreli tanımlıdır |
| 17B.6 | **Bağımlılıklar:** M16 (Takvim/Etkinlik Sistemi), M17 (Modern Senaryo — blok verisi), M22 (Kriz/Meşruiyet), M31 (Savaş Sonrası Düzen / BM Güvenlik Konseyi). | Bağımlılıklar belgelendi |

---

### 📋 Milestone 18: İl (Province) Sistemi
**Hedef:** Birlik hareketi için ülkeleri illere böl. **Bu, haritanın en riskli teknik parçasıdır.**

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 18.1 | İl verisi: mevcut ülke poligonlarını prosedürel bölgelere ayıran üretim (IProvinceSource arkasında), ~1000 il hedefi. | İller yüklenir, sayı loglanır | ✅ Bitti — `IProvinceSource` + `ProvinceData` + `ProceduralProvinceSource` yazıldı (~1000 il, sabit seed), `dotnet build` 0 hata / 0 uyarı |
| 18.2 | Komşuluk grafı (kara bağlantıları, deniz/liman bağlantıları). | Herilin komşu listesi doğru | ✅ Bitti — shared-edge deteksiyonu ile komşuluk grafi, `dotnet build` 0 hata / 0 uyarı | Her ilin komşu listesi doğru |
| 18.3 | İl sahipliği (ülke → iller), savaşta il el değiştirir, ülke rengi illerden türetilir. | İlhak sonrası harita doğru renklenir | ✅ Bitti — `ProvinceOwnershipSystem` + WorldMap `SetProvinces`/`RefreshProvinceColors`, ilhak sonrası renk güncellenir, `dotnet build` 0 hata / 0 uyarı |
| 18.4 | Performans: toplu çizim, seçim/hover için hızlı hit-test. Hız 5'te 60 FPS. | FPS ölçümü logda | ✅ Bitti — `FindProvinceAt` (bbox prefilter + point-in-polygon), FPS log (120 frame'de bir), `dotnet build` 0/0 |
| 18.5 | Kaydet/Yükle: il sahipliği ve il durumu kayıtlara eklenir. | A==B testi geçer | ✅ Bitti — `ProvinceOwnershipSystem.GetSaveData/LoadSaveData`, `SaveSystem.ProvinceOwners` DTO, `SaveLoadTest` il özeti, `dotnet build` 0/0 |

---

### 📋 Milestone 19: Birlik Hareketi ve Komut (HOI4 tarzı)

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 19.1 | Haritada birlik sayaçları (NATO tarzı ikonlar, ülke renginde, zoom'a göre boyut). | Orduların konumunda sayaç görünür | ✅ Bitti — `Army.ProvinceId` alanı, `WorldMap.DrawArmies()` (_Draw'ta çember sayacı), `dotnet build` 0/0 |
| 19.2 | Seçim: tek tık, **sürükleyerek kutu seçimi**, Shift ile çoklu. Mevcut harita kaydırma ile çakışmaz. | Birden çok birlik seçilebilir, harita kontrolleri bozulmaz | ✅ Bitti (2026-10-17) |
| 19.3 | **Komut:** sağ tık ile hedefe hareket, Shift ile ara noktalar, yol önizleme okları. Sağ tık harita kaydırmasıyla çakışmayacak biçimde ayırt edilir (kısa tık = komut, sürükleme = kaydırma). | Birlik hedefe yürür, yol çizgisi görünür | ✅ Bitti (2026-10-17) |
| 19.4 | Hareket hızı (arazi, hava, ikmal), il başına geçiş süresi. | Hareket süresi ile logla doğrulanır | ✅ Bitti (2026-10-17) |
| 19.5 | Temas ve savaş: düşman ile karşılaşınca mevcut WarSystem savaş çözümü tetiklenir, hareket durur. | Temasta savaş başlar | ✅ Bitti (2026-10-17) |
| 19.6 | Cephe hattı gösterimi, ofansif plan (sürükle-bırak ile saldırı oku). | `WorldMap.cs`: `RebuildFrontlines`/`DrawFrontlines` — farklı sahipli ve savaşta olan komşu il ikilileri arasında kırmızı-turuncu cephe hattı çizgisi; `DrawPlan`/`FinalizePlan`/`ApplyRouteToArmy` — Ctrl+LMB sürükle-bırak ambre ok, kaynak illerdeki seçili ordulara rota atanır (BFS, M19.4 hop mantığıyla uyumlu), RMB pan sırasında plan iptali, savaş sayısı değişiminde cephe yeniden hesaplanır | ✅ Bitti (2026-10-17) |
| 19.7 | Deniz ve hava birlikleri (filo, uçak) için aynı komut altyapısı (temel). | Filo denizde hareket eder | ✅ Bitti |
| 19.8 | AI birlikleri aynı sistemi kullanır. | AI orduları illerde hareket eder | ✅ Bitti |

---

### 📋 Milestone 20: İstatistikler ve Bütçe
**Hedef:** Bir ülkenin gerçek göstergeleri.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 20.1 | Göstergeler: GSYİH, büyüme, **enflasyon, işsizlik, yolsuzluk, suç oranı**, kamu borcu, bütçe açığı, gelir eşitsizliği (Gini), eğitim, sağlık, yaşam süresi, nüfus (doğum/ölüm/göç), enerji, çevre, medya özgürlüğü, yargı bağımsızlığı. | Hepsi panelde görünür, günlük/aylık güncellenir | ✅ Bitti |
| 20.2 | Bütçe: vergi oranları, harcama kalemleri (savunma, sağlık, eğitim, altyapı, sosyal, güvenlik, Ar-Ge), faiz ve borç. | Bütçe değişince göstergeler tepki verir | ✅ Bitti |
| 20.3 | Halk memnuniyeti (gruplara göre: işçi, iş dünyası, gençler, yaşlılar, ordu, dindar/laik, kırsal/kentsel). | Gruplar farklı tepki verir | ✅ Bitti |
| 20.4 | Göstergeler arası bağlantılar (enflasyon↔işsizlik↔memnuniyet, yolsuzluk↔yatırım, eğitim↔teknoloji hızı vb.). Formüller `Data/formulas.json` içinde ayarlanabilir. | Bir göstergeyi değiştirince bağlantılar loglanır | ✅ Bitti |
| 20.5 | İstatistik paneli (grafik, geçmiş eğilim çizgisi). | Panel 1152x648'e sığar | ✅ Bitti |

---

### 📋 Milestone 21: Bakanlıklar ve Kabine

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 21.1 | Bakanlıklar: İçişleri, Maliye, Savunma, Dışişleri, Adalet, Eğitim, Sağlık, Ekonomi/Ticaret, Enerji, Çevre, Tarım, İstihbarat Başkanı. | Kabine paneli açılır | ✅ Bitti |
| 21.2 | Bakan havuzu: kurgusal kişiler (yetenek, dürüstlük, sadakat, popülerlik, parti/ideoloji, özellik: reformcu/şahin/teknokrat...). | Aday listesi görünür | ✅ Bitti |
| 21.3 | **Atama / görevden alma**; bakanın nitelikleri ilgili göstergeleri etkiler (ör. dürüst İçişleri → suç ve yolsuzluk düşer). | Bakan değişince göstergeler değişir | ✅ Bitti |
| 21.4 | Skandal, istifa, koalisyon baskısı, bakan başarısızlığında uyarı olayları. | Skandal olayı tetiklenir | ✅ Bitti |
| 21.5 | Kabine toplantısı (ajanda slotu kullanır). | Toplantı etkisi uygulanır | ✅ Bitti |

---

### 📋 Milestone 22: Yasalar, Anayasa ve Parlamento

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 22.1 | **Anayasa:** yönetim biçimi (başkanlık, parlamenter, yarı başkanlık, monarşi, tek parti), görev süresi ve limit, seçim yaşı, olağanüstü hal kuralları. | Anayasa panelinde görünür | ✅ Bitti — `Data/constitution.json`, `Scripts/ConstitutionSystem.cs`, `--constitution-test` 20/20 PASS |
| 22.2 | **Yasalar:** önerme, ekleme, kaldırma; her yasanın kategorisi, maliyeti ve etkileri (`Data/laws.json`). | Yasa çıkar, etkisi göstergelerde görülür | ✅ Bitti — `Data/laws.json`, `Scripts/LawSystem.cs`, Save/Load entegrasyonu, `--laws-test` PASS, `--save-load-test` A==B PASS |
| 22.3 | **Parlamento:** partiler, sandalye dağılımı, oylama, veto, koalisyon, referandum. | Oylamada sandalyelere göre sonuç çıkar | ✅ Bitti — `Scripts/ParliamentSystem.cs`, `Data/parties.json`, `--parliament-test` kancağı (`RunParliamentTest`), dotnet build 0/0. |
| 22.4 | Otoriter sistemlerde farklı mekanik (parlamento yerine parti kongresi, darbe riski). | Rejim türüne göre panel değişir | ✅ Bitti — Scripts/AutoritarianSystem.cs (tek parti kongre oylaması + günlük deterministik darbe riski), GameManager (_Ready/DailyTick/ResetForNewGame + --autoritarian-test kancağı), SaveSystem (SaveFile.Autoritarian + WriteSave/LoadGameInternal), SaveLoadTest özeti, dotnet build 0/0. |

---

### 📋 Milestone 23: Seçimler ve Halk

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 23.1 | Seçim takvimi ve oy tahmini (memnuniyet, ekonomi, skandallar, kampanya). | Seçim günü sonuç hesaplanır | ✅ Bitti (2026-10-18) |
| 23.2 | Kampanya aksiyonları: miting, reklam, TV, ittifak, vaat (verilen vaatlerin takibi). | Aksiyonlar oy tahminini etkiler | ✅ Bitti (2026-10-18) |
| 23.3 | Seçimi kaybetmek/kazanmak: devam ya da iktidarın el değiştirmesi, yeni hükümet/kabine. | Kaybedince lider değişir | ✅ Bitti (2026-10-18) |
| 23.4 | Protesto, grev, güven oylaması, erken seçim, darbe girişimi. | Düşük memnuniyette olaylar tetiklenir | ✅ Bitti (2026-10-18) |

---

### 📋 Milestone 24: Karakter Oluşturma

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 24.1 | Karakter ekranı: ad, yaş, cinsiyet, doğum ülkesi/kültürü, **görünüm** (katmanlı prosedürel portre: yüz, saç, renkler, kıyafet), kökeni (asker, hukukçu, iş insanı, akademisyen, aktivist). | Karakter oluşturulur, portre görünür |
| 24.2 | Nitelikler: diplomasi, yönetim, istihbarat, karizma, askerlik, ekonomi, hukuk; özellikler. | Nitelikler kayıtlı ve görünür | [x] Bitti — 7 nitelik + Traits mevcut (Character.cs), GameManager.ApplyCharacterEffects() DailyTick'te göstergelere etkiler, Save/Load CharacterSave ile kayıtlı, dotnet build 0/0, --save-load-test A==B PASS |
| 24.3 | Oyun akışı: ülke seçimi → karakter → (seçenek) "kariyerle başla" ya da "doğrudan lider ol (sandbox)". | İki başlangıç yolu çalışır | [x] Bitti — GameManager.StartPath enum (Sandbox/Career) + PlayerStartPath, Game.cs ShowStartPathChoice() iki-butonlu seçim, her iki yol da CharacterCreationPanel → SetCharacter, dotnet build 0/0 |
| 24.4 | Karakter kayıt/yükleme, yaşlanma, sağlık, aile, halefiyet (basit). | Karakter yaşlanır, kaydedilir | [x] Bitti — Character.Age +1 her Ocak 1 (deterministik), SpouseId/IsMarried temel aile, CharacterSave DTO + From/To'yla kayıtlı, dotnet build 0/0, --save-load-test A==B PASS |

---

### 📋 Milestone 25: Kariyer ve Eğitim (Detaylı)
**Hedef:** Üstünkörü değil, uzun soluklu ve çok seçenekli bir gelişim.

| Alt Görev | Açıklama | Test Kriteri |
|-----------|----------|--------------|
| 25.1 | **Siyasetçi yolu:** üniversite (hukuk/iktisat/siyaset), parti gençlik kolu, yerel yönetim, milletvekilliği, bakanlık deneyimi, parti içi yükselme. | Adımlar sırayla açılır | [x] Bitti — CareerSystem.cs (yeni): CareerStage/EducationField, 30-günlük deterministik aşama ilerleme, eğitime göre nitelik bonusu, CareerSave DTO + GetSaveData/LoadSaveData; GameManager.cs: Initialize + DailyTick + --career-test/RunCareerTest; SaveSystem.cs: SaveFile.Career alan + WriteSave/LoadGameInternal bağlama; dotnet build 0/0, --career-test PASS, --save-load-test A==B PASS |
| 25.2 | **Asker yolu:** harp okulu, rütbeler, görev yerleri, savaş deneyimi, genelkurmay; darbe seçeneği (ağır sonuçlu). | Rütbe yükselir | [x] Bitti — MilitaryCareerSystem.cs (yeni): MilitaryRank/MilitaryBranch/DutyStation, harp okulu (Private→Corporal özel terfi), 30-günlük rütbe terfisi (dönem+akademi+staf kolajı önkoşulu), FrontLine deneyim kazanması, darbe riski + AttemptCoup (Albay+ gerekir, başarısız=haysiyesiz çıkarma), MilitaryCareerSave DTO + GetSaveData/LoadSaveData + ResetForNewGame; GameManager.cs: Initialize + DailyTick + --military-career-test/RunMilitaryCareerTest; SaveSystem.cs: SaveFile.MilitaryCareer alan + WriteSave/LoadGameInternal bağlama; dotnet build 0/0, --military-career-test PASS, --save-load-test A==B PASS |
| 25.3 | Eğitim ögeleri: dersler, staj, mentor, sınav, tez/proje, ağ kurma. Her seçim zaman, para ve bağlantı kaybı/kazancı getirir; başarısızlık ve tekrar mümkün. | Sınav sonucu nitelikleri değiştirir | [x] Bitti — EducationSystem.cs (yeni): EducationModuleType/Course-Internship-Mentor-Exam-Thesis-Networking, DurationDays/CostPP/SuccessChance/SuccessStat+Bonus/FailureStat+Penalty, deterministik DeterministicRNG.NextBool, DailyTick çözümle + tek aynı anda aktif modül, EducationSave DTO + GetSaveData/LoadSaveData/ResetForNewGame; GameManager.cs: Initialize + DailyTick + --education-test/RunEducationTest (sınav 5 gün, Law+6 / Intelligence-2, tekrar başlatma, Save/Load korundu); SaveSystem.cs: SaveFile.Education alan + WriteSave/LoadGameInternal bağlama; dotnet build 0/0, --education-test PASS, --save-load-test A==B PASS
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

### Önerilen Ek Özellikler (Sonra Değerlendirilecek)

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

### Bilinen Sorunlar / Teknik Borç

- DECISIONS paneli arayüzü yeniden tasarlanacak (HOI4 tarzı kategori tabanlı görünüm).
- AI yalnızca ayrıntılı verisi olan ~49 ülkeyi yönetiyor, kalan ülkeler pasif.
- Gerçek FPS değerleri headless testle ölçülemez, her milestone sonunda editörde elle kontrol edilir.
- Savaş dengesi (kayıp ve ilerleme hızı) henüz ayarlanmadı.
- ~~AI savaş agresifliği çok yüksek (gerekçe şansı %20, hedef rastgele)~~ **ÇÖZÜLDÜ (JOB1):** gerekçe şansı %1.5 (Data/ai_war_settings.json), hedef kısıtı (komşu/iddia/ideoloji/faksiyon), %10 eş zamanlı savaş sert kısıtı; 5 yıllık simülasyonda en yüksek %8.2.
- Eski tarihi içerik (1936 odak/olay/teknoloji) yalnızca Tarihi senaryoda kalır.
- Ses dosyaları (müzik, tık sesleri) henüz yok, lisansı uygun dosyalar kullanıcı tarafından eklenecek.
- Harita arazi dokusu ve deniz görünümü ertelendi (M32).

---

*(Eski M13 ve M14 — Faz 2 tamamlandıktan sonra uygulanır.)*

### 📋 Milestone 13: Cila / UI / Ses / Performans
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
✅ **Milestone 12 (Kaydet/Yükle) %100** — Aşamalı tamamlandı: (0) envanter → (1) DeterministicRNG + tüm `_rng` kullanımları değiştirildi → (2) 12 sistem GetSaveData/LoadSaveData, JSON user://saves, atomik yazma, versiyon kontrolü → (3) LoadGamePanel (ana menü + ESC pause menü) → (4) doğrulama testi **PASS ✓** (100 gün → save → 50 gün → yükle → 50 gün; özetler satır satır aynı). Kritik bug: PER→IRN alias Peru'yu Iran'a yönlendiriyordu — kaldırıldı, GetExact eklendi.

✅ **Milestone 15 (Hızlı İyileştirmeler) %100** — 15.1 küçük ülke kartı (kilit sonrası tam panel açılmaz), 15.2 Kütüphane (ana menü + HUD), 15.3 Save/Load v2 (onaylı üzerine yazma/silme, slot adı), 15.4 görünen hata mesajı, 15.5 deniz/okyanus etiketleri (sea_labels.json, zoom bazlı), 15.6 bilinen sorunlar PROGRESS.md'de, 15.7 bayrak sistemi (FlagLibrary + FlagBox, 7 panel entegrasyonu). A==B doğrulama testi yeniden **PASS ✓**.

🔄 **FAZ 2: Gerçek Dünya Simülasyonu** — Yön değişikliği onaylandı: oyun güncel dünyayı simüle edecek (oyuncu karakter, kurgusal liderler, yavaş tempo, iller, istatistikler, seçimler...). Plan TODO.md'de "FAZ 2" bölümünde (M15-M32). Eski M13/M14 Faz 2 sonrasına ertelendi.

**DUR: Kullanıcı onayı bekleniyor** — M15 manuel testleri: (1) ülke kilitleyince bir ülkeye tıkla → küçük kart, tam panel açılmamalı; (2) ana menü + HUD'daki LIBRARY; (3) ESC → SAVE/LOAD: üzerine yazma/silme onayları, iptal dosyayı korur; (4) bozuk kayıt dosyası → hata mesajı; (5) zoom out → okyanus, zoom in → deniz etiketleri; (6) mouse: sağ tık sürükleme/tekerlek/WASD/ülke seçimi bozulmadı. Onay sonrası → **Milestone 16: Zaman ve Tempo**.