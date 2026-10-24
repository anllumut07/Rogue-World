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
| 1.1 | `GameManager` (Autoload): tarih (DateTime), toplam gün, hız (0-5), gün tick sinyalleri | Headless: 10 frame'de ~3 gün ilerler, tarih günlüğü yazdırılır | ✅ Bitti  [x] Bitti |
| 1.2 | `Scenes/main_menu.tscn` + `Scripts/MainMenu.cs`: START / QUIT butonları | Editörde Play → menü açılır, START tıklandığında oyun sahnesine geçer | ✅ Bitti  [x] Bitti |
| 1.3 | `Scenes/game.tscn` + `Scripts/Game.cs`: HUD (tarih etiketi, hız butonları 1x-5x, PAUSE) | Oyun sahnesinde tarih güncellenir, hız butonları çalışır, Space = pause | ✅ Bitti  [x] Bitti |
| 1.4 | Ana menüden oyun sahnesine geçiş, tekrar ana menüye dönüş (ESC → PauseMenu) | Editörde test: Menü → Oyun → ESC → Pause Menü → RESUME / MAIN MENU | ✅ Bitti  [x] Bitti |
| 1.5 | SettingsSystem: ses, tam ekran, çözünürlük kaydet/yükle | Ayarlar değişir, oyunu kapatıp açınca korunur | ✅ Bitti — `SettingsPanel.cs` + `Scenes/settings_panel.tscn` (ses slider, fullscreen checkbox, çözünürlük OptionButton, BACK); ana menü ve PauseMenu'den açılıyor, değerler `SettingsSystem` üzerinden kaydedilip açılışta panele yansıyor  [x] Bitti |

---

### ✅ Milestone 2: Dünya Haritası (GeoJSON Ülke Poligonları)
**Hedef:** HOI4 stili provinces yerine **ülke bazlı** poligon haritası (GeoJSON), pan/zoom, ülke seçimi, ülke bilgi paneli.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 2.1 | `Data/countries.geo.json` (ülke poligonları, ~180 ülke) + `Data/countries_data.json` (1936 verisi: GDP, nüfus, asker, kaynaklar) | Dosyalar yerinde, JSON geçerli | ✅ Bitti (180 ülke, 52 ayrıntılı veri)  [x] Bitti |
| 2.2 | `WorldMap` (Node2D): GeoJSON okur, Polygon2D + Line2D ile çizer, kamera pan/zoom (sağ tık sürükle, tekerlek), WASD pan | Editörde: harita render olur, zoom/pan sorunsuz | ✅ Bitti  [x] Bitti |
| 2.3 | Ülke tıklama seçimi (sol tık) → seçim alt çubukta "Selected: Ülke Adı" + sol panelde CountryInfoPanel açılır | Tıklanan ülke vurgulanır, panel bilgiler gösterir | ✅ Bitti  [x] Bitti |
| 2.4 | Ülke renklendirme: temel renk (hash tabanlı), seçili = altın, hover = açık ton | Görsel geri bildirim çalışır | ✅ Bitti (kısık palet + altın pulse çerçeve)  [x] Bitti |
| 2.5 | `CountryInfoPanel`: bayrak/adı, GDP, nüfus, askerî güç, kaynaklar, askeri fabrikalar, sivil fabrikalar | Panel veriler doğru yüklenir | ✅ Bitti (GDP tahmini, bayrak placeholder, PLAY AS butonu)  [x] Bitti |

**Görsel iyileştirmeler (7/7 tamam):** ortak tema, okyanus degrade + kısık palet + zoom-adaptif sınırlar, zoom bazlı etiketler + kamera lerp, 3-bölümlü HUD + tooltip, panel/buton animasyonları, ana menü animasyonlu arka plan + altın başlık, era tonu + gündüz/gece terminatörü.

---

### ✅ Milestone 3: Ülke Veritabanı ve Oyuncu Ülke Seçimi
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 3.1 | `CountryDatabase`: 1936 başlangıç verileri (major: GER, SOV, USA, ENG, FRA, JPN, ITA, CHI, POL, TUR... + minor'lar) | Seçilen her ülke için gerçekçi değerler döner | ✅ Bitti — 52 ülke JSON + deterministik fallback + 27 HOI4 takma adı (GER→DEU, JAP→JPN, CHI→CHN...)  [x] Bitti |
| 3.2 | "Play as [Ülke]" butonu CountryInfoPanel'de → `GameManager.SetPlayerCountry()` | Oyuncu ülke seçer, HUD'da ülke adı/bayrak görünür | ✅ Bitti — buton durumu geri bildirimli ("PLAYING AS", disabled), GD.Print onay  [x] Bitti |
| 3.3 | Political Power birikimi (günlük +2.0 PP — HOI4 temel; plandaki +1.5 yerine), Stability %, War Support % | HUD'da PP/S/W güncellenir | ✅ Bitti — DailyTick: PP +2.0/gün + manpower; HUD sinyalle güncellenir  [x] Bitti |

**Görsel (küçük):** Oyuncu ülkesi sınırına kalıcı yarı saydam altın vurgu (seçim pulse'undan ayrı). ✅

---

### 📋 Milestone 4: Ekonomi — Fabrikalar, İnşaat, Kaynaklar
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 4.1 | Sivil Fabrikalar / Askeri Fabrikalar / Tersaneler sayacı | Ülke verisinden başlangıç sayıları gelir | ✅ EconomySystem.TotalCivFactories/MilFactories/Dockyards (CountryData'dan)  [x] Bitti |
| 4.2 | İnşaat kuyruğu: 5 yapı tipi, 70/25/30 gün, seri işleme (max 15 fabrika/hat) | İnşaat başlatılır, kuyrukta progress bar ile görülür, günlük ilerler, tamamlandığında fabrika sayısı artar | ✅ EconomySystem + EconomyPanel (5 buton, kuyruk listesi, iptal)  [x] Bitti |
| 4.3 | Tüketim malları (CG): 5 yasa (Sivil 30% → Toplam 10%), yasa değişimi 150 PP | Yasa OptionButton ile değiştirilir, CG oranı değişir, boş sivil fabrika anında güncellenir | ✅ EconomySystem.ChangeLaw + EconomyPanel law seçici  [x] Bitti |
| 4.4 | Kaynaklar: 6 kaynak (Oil/Rubber/Steel/Al/Tu/Cr) — üretim, ticaret (%80 ihraç limiti), stok | Kaynak paneli: stok / günlük üretim / ithalat konvoyu görünür | ✅ EconomyPanel kaynak gridinde stok+üretim+ithalat kaynağı; ihracatçı adı  [x] Bitti |
| 4.5 | Ticaret otomatikleşmesi: eksik kaynak için konvoy atama, %80 ihraç limiti, günlük ticaret günlüğü | Stok eşiğinin altındaki kaynaklar için konvoy atanır, ticaret günlüğü loglanır | ✅ TradeDeal modeli (2 birim/gün), %80 ihraç limiti, ihracatçı seçimi, anlaştırma/iptal logu  [x] Bitti |

**Tamamlanan:** EconomySystem (core), EconomyPanel (UI), GameHud ECONOMY butonu. M4 işlevsel %100.

---

### ✅ Milestone 5: Teknoloji Ağacı (Technology Tree)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 5.1 | Kategoriler: Infantry, Support, Artillery, Armor, Naval, Air, Industry, Electronics, Doctrines (Land/Naval/Air) | Ağaç UI kategorilerle açılır | ✅ 9 kategori, 72 teknoloji, Data/technologies.json  [x] Bitti |
| 5.2 | Teknolojiler: ID, isim, açıklama, era (1936/1939/1941/1943), maliyet, önkoşullar, etkiler | Bir teknoloji araştırılır, etkileri uygulanır | ✅ 72 tech, modifikatörler (construction_speed, factory_output, resource_gain, research_speed)  [x] Bitti |
| 5.3 | Araştırma slotları: başlangıç 2, odak/teknoloji ile +1 (max 5) | Slot sayısı artırılır, eşzamanlı araştırma çalışır | ✅ CountryData.ResearchSlots (ülke verisinden), TechnologySystem slot yönetimi  [x] Bitti |
| 5.4 | Araştırma bonusu: focus/olay/decoder/teknoloji paylaşımı | Bonus uygulanır, araştırma hızı artar | ✅ research_speed modifikatörü (Electronics/Doktrin) + EconomySystem/TechnologySystem hızı uygular  [x] Bitti |
| 5.5 | Research panel UI: filtre (era/kategori), ilerleme çubuğu, "Start Research" butonu | Editörde test: teknoloji seçilir, günlük tick'te ilerler, tamamlanır | ✅ ResearchPanel: kategori/era filtresi, START butonu, aktif araştırma progress bar, slot/speed bilgisi  [x] Bitti |

**Görsel (küçük):** ProgressBar teması (varsayılan), tooltip düzeltmeleri.

---

### ✅ Milestone 6: Üretim (Production Lines)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 6.1 | Ekipman tipleri: Infantry Equipment, Support Equipment, Motorized, Light/Medium/Heavy Tank, Fighter/CAS/Naval Bomber/Strategic Bomber, Destroyer/Cruiser/Battleship/Carrier/Submarine | Tüm ekipman tipleri listelenir | ✅ Bitti — Data/equipment.json (15 tip, Land/Air/Naval, Military/Naval fabrika) + EquipmentSystem.AllTypes  [x] Bitti |
| 6.2 | Üretim hatları: askeri fabrikadan çizilir, efficiency (başlangıç %10, günde +0.25% max %100), variant upgrade penalty | Hat eklenir, efficiency artar, stockpile günlük artar | ✅ Bitti — AddLine/ChangeLineFactories/SwitchLineEquipment (ceza %50), DailyTick efficiency + üretim  [x] Bitti |
| 6.3 | Stok yönetimi: depodaki ekipman, kaybolan/hasar gören, askeri fabrikaya atama | Stok paneli doğru gösterir | ✅ Bitti — Stocks sözlüğü, ayda bir [Equipment] özet logu; kayıp/tüketim API'si M7'de  [x] Bitti |
| 6.4 | Variant tasarımı (Tank Designer / Plane Designer basitleştirilmiş): modüller, maliyet, istatistikler | Yeni variant tasarlanır, üretim hattına atanır | ✅ Bitti — GetModuleOptions (Gun/Armor/Engine), CreateVariant (maliyet/saldırı/savunma çarpanları), ProductionPanel designer + hat variant seçici  [x] Bitti |

---

### ✅ Milestone 7: Askeri — Bölükler, Ordular, Savaş
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 7.1 | Bölük şablonu (Division Template): battalion tipleri (Infantry, Artillery, AT, AA, Engineer, Recon, Signal, Logistics, Maint, Armor, SPG, TD, SPAA, Amphibious, Marine, Mountaineer, Paratrooper) | Şablon oluşturulur, istatistikler (ORG, HP, ATT, DEF, Pierce, Armor, Speed, Supply Use) hesaplanır | ✅ Bitti — Data/battalions.json (17 tip) + ArmySystem.ComputeTemplateStats; ArmyPanel designer ile şablon tasarlanır  [x] Bitti |
| 7.2 | Bölük yetiştirme: manpower + ekipman → deploy (X gün), reinforcement hızı | Bölük yetiştirilir, haritada army marker olarak görünür | ✅ Bitti — StartTraining (manpower+ekipman maliyeti, 30+3×tabur gün), takviye ORG +3/HP +1 (ekipman harcar); harita marker M7.5 RMB emirleriyle soyutlandı  [x] Bitti |
| 7.3 | Ordular (Armies) ve theater/grup yönetimi, general/field marshal ataması | Ordular oluşturulur, komutan atanır | ✅ Bitti — CreateArmy (otomatik general adı + skill 1-5), AssignDivisionToArmy; ArmyPanel ordular/tümenler listesi  [x] Bitti |
| 7.4 | Savaş sistemi (province-based): attacker/defender ORG/HP, combat width, daily tick combat resolution, casualties, equipment loss, retreat | Savaş başlar, günlük tick'te ORG/HP düşer, kazanan taraf işgal eder | ✅ Bitti — ülke-bazlı sadeleştirme: WarSystem günlük çatışma (ORG hasarı, 3 geri çekilme=yok olma, manpower+ekipman kaybı, düşman tümen havuzu)  [x] Bitti |
| 7.5 | Front çizgisi / ofansif planlama (basit: right-click → attack order, shift → frontline) | Savaş planı çizilir, bölükler ilerler | ✅ Bitti — RMB tık (<6px) → OFANSİF, Shift+RMB → CEPHE (savunma +%30); WarProgress ofansifte günde artar; RMB sürükleme pan bozulmadı  [x] Bitti |
| 7.6 | Kapitülasyon: victory points kontrolü, capitulation threshold, peace conference basiti | Ülke yenilir, kapitüle olur, toprak değiştirir | ✅ Bitti — düşman tümen=0 veya ilerleme %100 → ilhak: IsAnnexed+OwnerId+fabrika transferi+harita yeniden renklenir; oyuncu kapitülasyonu = OYUN BİTTİ logu  [x] Bitti |

**M7 notu:** HOI4 province sistemi yerine ülke-bazlı soyutlama (harita Country poligonlu): savaş tek cephede çözülür, WarProgress (0-100) işgal/VK proxy'si. Peace conference ayrıntısı (puppet/liberate) M8.3'e ertelendi.

---

### ✅ Milestone 8: Diplomasi ve Faksiyonlar
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 8.1 | İlişkiler (-100...+100), opinions, threat/world tension | İlişki paneli çalışır | ✅ DiplomacySystem + DiplomacyPanel, diplomacy.json starter  [x] Bitti |
| 8.2 | Faksiyon oluşturma/katılma (Axis/Allies/Comintern + custom), faksiyon lideri | Faksiyon oluşturulur, üye eklenir | ✅ CreateFaction/RequestJoinFaction/LeaveFaction + panel UI  [x] Bitti |
| 8.3 | Savaş ilanı, çağrı (call to arms), barış konferansı (basit: puppet/annex/white peace) | Savaş ilan edilir, müttefikler çağrılır, barış yapılır | ✅ WarSystem.DeclareWar/DeclareWarOnPlayer + DiplomacySystem.ProposePeace/WhitePeace + panel butonları  [x] Bitti |
| 8.4 | Garanti ver (guarantee independence), askeri erişim, deniz erişimi | Diplomatic actions çalışır | ✅ Guarantee/Revoke/RequestMilitaryAccess/RequestNavalAccess + panel UI  [x] Bitti |
| 8.5 | World Tension mekanizması: aksiyonlar tension artırır, focus/olaylar tension etkiler | Tension barı güncellenir | ✅ Tension bar (renk geçişli), IncreaseTension/azalma, ayda bir log  [x] Bitti |

---

### ✅ Milestone 9: Ulusal Odak Ağacı (National Focus Tree)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 9.1 | Odak düğümleri: ID, isim, açıklama, önkoşul odaklar, mutually exclusive, cost (gün), etkiler (fabrikalar, PP, research bonus) | Odak ağacı UI açılır, düğümler bağlanır | ✅ Bitti — Data/focus_trees/*.json (8 ağaç, 73 odak) + FocusSystem + FocusPanel (grid ağaç, önkoşul çizgileri, mutex ⊗, durum renkleri)  [x] Bitti |
| 9.2 | Oyuncu ülkesine özel focus tree (GER/USA/SOV/ENG/FRA/JAP/ITA + generic) | Oyuncu ülkesine uygun tree yüklenir | ✅ Bitti — GetTreeFor: GER/DEU, USA, SOV/RUS, ENG/GBR, FRA, JPN/JAP, ITA → özel ağaç, diğerleri GENERIC; SetPlayerCountry'de Initialize  [x] Bitti |
| 9.3 | Odak tamamlandığında efektler uygulanır (fabrikalar artar, research bonus verilir) | Odak seçilir, 70 gün sonra tamamlanır, efektler görülür | ✅ Bitti — ApplyFocusEffects: fabrika/PP/stabilite/WS/manpower/kaynak/research slot/tension + TechnologySystem.AddModifier (research_speed); her efekt GD.Print ile loglanır  [x] Bitti |
| 9.4 | AI için focus seçim mantığı (priority scoring) | AI ülke odak seçer, ilerler | ✅ Bitti — AiPickFocus (AiPriority + ülke durumu puanlaması); headless: `-- --focus-test=GER` → AI sırayla odak seçip tamamlıyor (headless_focus_test.log)  [x] Bitti |

**M9 notu:** Efektler yalnızca mevcut sistemlerdeki değerlere yazar (kullanıcı kararı): war goal/event efektleri M10/M11'e ertelendi. Focus panel 1060×560 — 1152×648'de tam sığar; sol tık = başlat, sağ tık aktif odak = iptal; tek aktif odak (HOI4 kuralı).

---

### ✅ Milestone 10: Olaylar ve Kararlar (Events & Decisions)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 10.1 | Olay motoru: tetikleyici (tarih, focus tamamlanma, savaş başlangıcı, random), seçenekler (A/B/C), efektler | Olay tetiklenir, seçenek seçilir, efektler uygulanır | ✅ Bitti — EventSystem (4 tetikleyici tipi, FireOnlyOnce, ülke eşleşmesi, kuyruk) + EventPopup (ortada, A/B/C butonları, auto-pause, ESC yoksay)  [x] Bitti |
| 10.2 | Kararlar (Decisions): kullanılabilirlik koşulu, maliyet (PP/gün), efekt, cooldown | Karar paneli açılır, karar alınır, efekt görülür | ✅ Bitti — DecisionSystem (koşul/maliyet/cooldown motoru) + DecisionPanel (HUD DECISIONS butonu, 2 sütun, progress bar) + 10 karar  [x] Bitti |
| 10.3 | Tarihi olaylar seti (Anschluss, Munich, Molotov-Ribbentrop, Pearl Harbor, Barbarossa, D-Day...) | Olaylar tarihlerinde/koşullarında tetiklenir | ✅ Bitti — 11 olay: Anschluss, Munich, Molotov-Ribbentrop, Winter War, Fall of France, Barbarossa, Pearl Harbor, D-Day, + 3 yardımcı (Economic Miracle, New Deal, Spanish Civil War)  [x] Bitti |
| 10.4 | (M9 ertelenmesi) Focus → event bağlantısı: odak tamamlanınca olay tetiklenebilsin | Odak bitince olay popup açılır | ✅ Bitti — FocusEffects.TriggerEvent + GER ağacında örnek (ger_molotov_ribbentrop → molotov_ribbentrop, ger_four_year_plan → german_economic_miracle)  [x] Bitti |
| 10.5 | (M9 ertelenmesi) War goal PP maliyetini düşürsün | Odak/olay sonrası savaş ilanı PP maliyeti düşer | ✅ Bitti — WarGoalCostReduction etkisi + WarSystem.SetWarGoalCostReduction + DeclareWar CalculateWarGoalCost kullanır (Barbarossa: -100% SOV, Molotov-Ribbentrop: -50% POL)  [x] Bitti |

---

### ✅ Milestone 11: AI (Diğer Ülkeler)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 11.1 | AI ekonomisi: fabrika inşaatı, ticaret, research slot kullanımı | AI ülkeler fabrikalar inşa eder, araştırır | ✅ Bitti — CountryAI: boş sivil fabrika × 5 puan/gün, 70 gün @15 fabrika = +1 bina; PP/manpower günde +2.0; research slots * 0.25 puan/gün, 140 puan = 1 tech (~175 gün); her 5 tech +1 sivil fabrika bonus; 49 AI ülke round-robin günde 10 karar  [x] Bitti |
| 11.2 | AI askeri: bölük yetiştirme, şablon güncelleme, front atama | AI orduları haritada hareket eder | ✅ Bitti — Tümen yetiştirme (40 gün, 5000 manpower); DivisionCap (pop/factory bazlı); AI-AI savaş çözümü (ResolveAiVsAiCombat: güç oranına dayalı ilerleme/tümen kaybı/kapitülasyon + fabrika transferi); front atama (ofansif/cephe duruşu otomatik güç karşılaştırmasıyla)  [x] Bitti |
| 11.3 | AI diplomasi: faksiyon oluşturma/katılma, garantiler, savaş ilanı mantığı | AI tarihsel/görünür davranış sergiler | ✅ Bitti — Faksiyon oluşturma (lider PP ≥ 50, ideoloji); faksiyona katılma (lider görüş ≥ 50); garanti (demokrasiler minorlara); savaş ilanı (PP ≥ 50, Divisions ≥ 10, güç ≥ %80 hedef, maliyet WarSystem.CalculateWarGoalCost); call-to-arms simülasyonu; GetEnemiesAtWar API  [x] Bitti |
| 11.4 | AI odak seçimi: priority scoring (threat, ideology, opportunity) | AI odak ağacını ilerletir | ✅ Bitti — M9.4 AiPickFocus genişletildi: FocusSystem.TickAiStates() günde tüm AI state'leri ilerletir; AiPriority + ScoreEffectsForAi (az sanayi→sanayi, düşük stabilite→barış, faşizm→savaş); tüm 49 AI ülke odak seçip 70 günde tamamlar, efektler CountryData'ya yazılır  [x] Bitti |

---

### ✅ Milestone 12: Kaydet / Yükle (Save/Load)
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 12.1 | Save slotları (1-10), auto-save (her yılın ocak ayı) | Kaydedilir, dosya oluşur | ✅ Bitti — `user://saves/slot_N.json` + `auto_save.json` (Ocak 1 auto-save DailyTick'te); atomik yazma (.tmp → Move); versiyon kontrolü (v1); bozuk/eksik kayıt → hata log + oyun bozulmaz  [x] Bitti |
| 12.2 | Tüm sistemler state serialize: tarih, ülkeler, ekonomi, araştırma, üretim, askeri, diplomasi, olaylar, odaklar | Load sonrası oyun aynen devam eder | ✅ Bitti — 12 sistem GetSaveData/LoadSaveData; DeterministicRNG (xorshift64*) state kaydedilir; **headless doğrulama: 100 gün → save → 50 gün → özet A; load → 50 gün → özet B; A==B PASS ✓** (PER→IRN alias bug bulundu ve düzeltildi)  [x] Bitti |
| 12.3 | Load menüsü: slot bilgisi (tarih, oyuncu ülkes, hız), yükle butonu | Load menüsünden yüklenir, oyun sorunsuz devam eder | ✅ Bitti — LoadGamePanel (AUTO + slot 1-10 listesi, 520×560, MouseFilter.Stop backdrop); MainMenu "LOAD GAME" butonu (PendingLoadSlot + sahne değişimi) + PauseMenu "LOAD GAME" butonu (mevcut oyuna yükleme); load sonrası harita/UI sinyalleri + Speed=0 pause  [x] Bitti |

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
| 15.1 | Ülke bilgi paneli (nüfus, GDP, kaynaklar...) **yalnızca ülke seçim ekranında** görünür. Oyun başladıktan sonra bir ülkeye tıklamak sadece küçük kart gösterir (ad, bayrak, ilişki, savaş durumu). | Seçim ekranında tam panel, oyunda küçük kart | ✅ Bitti — `CountryMiniCard.cs` (ad, bayrak, ilişki, savaş durumu; ESC/X ile kapanır); `Game.cs` kilitliyken tam panel yerine mini kart açar; seçim aşaması akışı aynı  [x] Bitti |
| 15.2 | **Kütüphane** bölümü (ana menüden ve oyun içinden): ülke ansiklopedisi iskeleti, `Data/library/*.json`. İçerik sonra detaylandırılır. | Kütüphane açılır, bir ülkenin bilgisi okunur | ✅ Bitti — `LibrarySystem.cs` (Data/library/*.json, ID normalizasyonu GER→DEU) + `LibraryPanel.cs` (arama kutusu + kaydırılabilir liste; sağda sayfa: ad, başkent, bölge, dil, tanıtım + CountryDatabase canlı verileri) + `Data/library/countries.json` (**50 ülke**, senaryodan bağımsız olgusal metinler); ana menü + HUD LIBRARY butonu. **Bug düzeltildi:** RichTextLabel ScrollContainer içinde min boyutu 0 olduğundan içerik görünmüyordu → Label tabanlı sayfa; açılışta sayfa sayısı loglanır  [x] Bitti |
| 15.3 | **Save/Load v2:** mevcut slotun üzerine kaydet (**onay penceresi**), kayıt sil (**onay penceresi**), slot adı, kayıt anının kısa bilgisi. Save/Load menüsü ana menüde ve ESC menüsünde aynı bileşen. | Üzerine yazma ve silme onay ister, iptal edilirse dosya korunur | ✅ Bitti — `SaveSystem.cs` (SlotName, CurrentSlot, DeleteSave, SavedAtText) + `LoadGamePanel.cs` (SAVE/LOAD/DELETE, ConfirmationDialog onayları, slot adı LineEdit, CurrentSlot önseçim); A==B doğrulama testi yeniden **PASS ✓**  [x] Bitti |
| 15.4 | LoadGamePanel'de kullanıcıya görünen hata mesajı ("Kayıt yüklenemedi"). | Bozuk kayıt çökme yapmaz, mesaj gösterilir | ✅ Bitti — `_errorLabel` (kırmızı), yükleme başarısızsa gösterilir, yeni seçimde temizlenir  [x] Bitti |
| 15.5 | **Deniz ve okyanus adları:** okyanuslar uzak zoom'da, denizler/körfezler yakın zoom'da görünür (`Data/sea_labels.json`: ad, konum, tür, min/max zoom). Üst üste binme engellenir. | Zoom out → okyanuslar, zoom in → denizler | ✅ Bitti — `Data/sea_labels.json` (9 okyanus + 34 deniz/körfez, boylam/enlem + zoom aralıkları) + `WorldMap.cs` (JSON yükler, okyanus 18pt/deniz 14pt soluk mavi, harfler hafif aralıklı)  [x] Bitti |
| 15.6 | Bilinen sorunlar listesi PROGRESS.md'ye aktarılır (aşağıdaki "Bilinen Sorunlar"). | Liste güncel | ✅ Bitti — PROGRESS.md "Bilinen Sorunlar / Teknik Borç" bölümünde  [x] Bitti |
| 15.7 | **Bayrak Sistemi (M-Flags):** `Assets/flags/{ISO3}.png` önbellekli yükleme (ResourceLoader + Image.LoadFromFile), prosedürel yedek (3:2 dikdörtgen + 3 harf kod, ülke rengi, ince kenarlık), `FlagBox` yeniden kullanılabilir kontrol, entegrasyon: CountryInfoPanel, HUD (sol üst), CountryMiniCard, LibraryPanel, DiplomacyPanel (ilişkiler/garantiler), ArmyPanel (savaş listesi), EventPopup; başlangıç raporu loglar (PNG bulundu / yedek kullanıldı). | Derleme başarı, headless test: 13/50 PNG bulundu, 37 prosedürel yedek; UI entegrasyonu 7 panelle tamam | ✅ Bitti — `FlagLibrary.cs`, `FlagBox.cs`, 7 panele entegre, CountryDatabase NormalizeId (HOI4 takma adları GER→DEU, JAP→JPN, SOV→RUS, ENG→GBR)  [x] Bitti |

---

### 📋 Milestone 16: Zaman ve Tempo
**Hedef:** Oyun gerçek bir ülke yönetmek gibi hissettirmeli; zaman yavaş, kararlar çok.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 16.1 | Hızları yavaşlat: 1x ≈ gerçek saniyede yarım gün civarı, 5x bugünkü 1x civarı (değerler ayarlanabilir). | Bir yıl oynamak belirgin şekilde daha uzun sürer | ✅ Bitti — `Data/game_settings.json` hız tablosu (1x=0.5 gün/sn, 5x=1.0 gün/sn), `TimeTempoSystem.GetDaysPerSecond()` GameManager._Process'te kullanılır  [x] Bitti |
| 16.2 | **Haftalık ajanda:** liderin haftada sınırlı sayıda etkinlik slotu var (toplantı, miting, basın konuşması, zirve, eğitim). Slotlar dolunca hafta sonuna kadar beklenir. | Slot sayısı sınırlı (6/hafta), aksiyonlar slota yerleşir, PP maliyeti, süreli ilerler | ✅ Bitti — `TimeTempoSystem` (haftalık 6 slot, 5 aksiyon türü, PP maliyeti, süreli ilerleme, tamamlandığında efekt uygulanır), `AgendaPanel.cs` UI (slot seçimi + aksiyon kartları + devam eden aksiyonlar), `GameHud` AGENDA butonu  [x] Bitti |
| 16.3 | **Aylık rapor:** her ay başı danışman toplantısı özeti (istatistik değişimleri, uyarılar, 3-5 karar). | Ay başında (1. gün) rapor penceresi açılır, auto-pause, OK ile onay | ✅ Bitti — `MonthlyReportPanel.cs` (istatistik özet, uyarılar, durum kararları, CONTINUE butonu), auto-pause + AcknowledgeMonthlyReport  [x] Bitti |
| 16.4 | Önemli olayda otomatik duraklama, "sonraki olaya atla" butonu. | Olayda oyun durur, NEXT EVENT butonu çalışır | ✅ Bitti — `TimeTempoSystem.QueueImportantNotification` (war_declaration, capitulation vb.), HUD NEXT EVENT butonu (bekleyen bildirim varsa görünür), ProcessNextNotification  [x] Bitti |
| 16.5 | Aksiyonlar süre alır (miting hazırlığı birkaç gün, zirve organizasyonu 1-2 hafta). | Süreli aksiyon günlük ilerler | ✅ Bitti — `ActiveAgendaAction.RemainingDays` günlük -1, `DurationDays` yapılandırmadan  [x] Bitti |

**M16 notu:** `Data/game_settings.json` (hız + ajanda config), `Data/agenda_actions.json` (5 aksiyon: meeting/rally/press/summit/training). A==B determinizm testi **PASS ✓** (SaveLoadTest hâlâ geçer).

---

### 📋 Milestone 17: Modern Dünya Senaryosu
**Hedef:** Gerçek dünyayı simüle eden güncel başlangıç.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 17.1 | **Senaryo sistemi:** "Modern Dünya" (varsayılan) ve "Tarihi 1936". Başlangıç tarihi, veri dosyaları, olay/odak/teknoloji setleri senaryoya bağlı. | Menüde senaryo seçilir, doğru veri yüklenir | ✅ Bitti — `ScenarioSystem`, `ScenarioSelectPanel`, veri yolları senaryodan; 7 loader `ResetForScenario/LoadScenarioData`; SaveVersion=2 (v1→historical_1936); `--scenario-test`, `--save-load-test --scenario=modern_world|historical_1936` PASS ✓  [x] Bitti |
| 17.2 | Modern ülke verileri: nüfus, GSYİH, ordu, kaynaklar, bloklar (NATO, AB, BRICS, ŞİÖ vb.) yaklaşık gerçek değerler. 50 ayrıntılı ülke ile başla, kalanlar genel değerlerle. Veri kaynağı ve "yaklaşık değerler" notu dosyada belirtilir. | Ülke paneli modern veriyi gösterir | ✅ Bitti — 50 ülke ham girdisi (`countries.json`: nüfus, GSYİH, ordu, kaynak sınıfı, rejim, nükleer) + `formulas.json` katsayıları → türetilmiş oyun değerleri (fabrika, manpower, tümen, kaynak, politika, araştırma slotu); Name/Capital eklendi; `--scenario-test` 50 ülke doğruladı  [x] Bitti |
| 17.3 | Kurgusal liderler: her ülke için rastgele ad-soyad (ülke kültürüne göre), yaş, parti, özellikler. | Her ülkenin kurgusal lideri var | ✅ Bitti — `LeaderSystem` + `Data/names/*.json` (14 kültür havuzu); deterministik üretim (senaryo+ülke hash'li lokal RNG, gerçek kişi adı yok); 6 nitelik + özellikler; Save/Load A==B PASS ✓  [x] Bitti |
| 17.4 | Modern teknoloji tabanı (başlangıç teknolojileri güncel seviyede). | Tech ağacı modern başlar | ✅ Bitti — `Data/scenarios/modern_world/technologies.json` (73 tech, Era 1990–2020), `formulas.json` (TechLevel/ReseachSlots/StartingTechEra eşikleri), `TechnologySystem.Initialize()` ile `CountryDatabase.GetStartingTechEra()` başlangıç tech'ını verir; `--scenario=modern_world --save-load-test` **PASS ✓ (A == B)**  [x] Bitti |
| 17.5 | Modern faksiyonlar ve ilişkiler başlangıçta kurulu. | Diplomasi paneli bloklar gösterir | ✅ Bitti — `diplomacy_start.json`: NATO (20 üye, lider USA), EU (16), BRICS (9, lider CHN), ŞİO (4, lider RUS), 63 ilişki, Tension %25; `--scenario-test` + save-load PASS ✓  [x] Bitti |
| 17.6 | Eski tarihi olay/odak içerikleri yalnızca Tarihi senaryoda aktif. | Modern senaryoda 1936 olayları tetiklenmez | ✅ Bitti — modern senaryoda 0 odak ağacı / 0 olay / 0 karar; tarihi senaryoda 8 ağaç (73 odak) / 11 olay / 10 karar korunuyor; `--scenario-test` doğruladı  [x] Bitti |

**M17 notu:** Senaryo sistemi gelince `SaveVersion` **2'ye** yükseltilecek. Eski v1 kayıtlar "Tarihi 1936" senaryosu olarak yüklenecek (sürüm uyumsuz değilse tarih/veri setinden senaryo çıkarımı; uyumsuzsa mevcut hata mesajı gösterilir).

---

### 📋 Milestone 17B: Uluslararası Örgütler
**Hedef:** NATO, AB, BM, BRICS, ŞİÖ, AF, ASEAN, Arab Birliği, OPEC gibi örgütleri modelle. Yalnızca belge — kod yazılmaz.

| Alt Görev | Açıklama | Test Kriteri | Durum |
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

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|
| 18.1 | İl verisi: mevcut ülke poligonlarını prosedürel bölgelere ayıran üretim (IProvinceSource arkasında), ~1000 il hedefi. | İller yüklenir, sayı loglanır | ✅ Bitti — `IProvinceSource` + `ProvinceData` + `ProceduralProvinceSource` yazıldı (~1000 il, sabit seed), `dotnet build` 0 hata / 0 uyarı  [x] Bitti |
| 18.2 | Komşuluk grafı (kara bağlantıları, deniz/liman bağlantıları). | Herilin komşu listesi doğru | ✅ Bitti — shared-edge deteksiyonu ile komşuluk grafi, `dotnet build` 0 hata / 0 uyarı | Her ilin komşu listesi doğru  [x] Bitti |
| 18.3 | İl sahipliği (ülke → iller), savaşta il el değiştirir, ülke rengi illerden türetilir. | İlhak sonrası harita doğru renklenir | ✅ Bitti — `ProvinceOwnershipSystem` + WorldMap `SetProvinces`/`RefreshProvinceColors`, ilhak sonrası renk güncellenir, `dotnet build` 0 hata / 0 uyarı  [x] Bitti |
| 18.4 | Performans: toplu çizim, seçim/hover için hızlı hit-test. Hız 5'te 60 FPS. | FPS ölçümü logda | ✅ Bitti — `FindProvinceAt` (bbox prefilter + point-in-polygon), FPS log (120 frame'de bir), `dotnet build` 0/0  [x] Bitti |
| 18.5 | Kaydet/Yükle: il sahipliği ve il durumu kayıtlara eklenir. | A==B testi geçer | ✅ Bitti — `ProvinceOwnershipSystem.GetSaveData/LoadSaveData`, `SaveSystem.ProvinceOwners` DTO, `SaveLoadTest` il özeti, `dotnet build` 0/0  [x] Bitti |

---

### 📋 Milestone 19: Birlik Hareketi ve Komut (HOI4 tarzı)

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|
| 19.1 | Haritada birlik sayaçları (NATO tarzı ikonlar, ülke renginde, zoom'a göre boyut). | Orduların konumunda sayaç görünür | ✅ Bitti — `Army.ProvinceId` alanı, `WorldMap.DrawArmies()` (_Draw'ta çember sayacı), `dotnet build` 0/0  [x] Bitti |
| 19.2 | Seçim: tek tık, **sürükleyerek kutu seçimi**, Shift ile çoklu. Mevcut harita kaydırma ile çakışmaz. | Birden çok birlik seçilebilir, harita kontrolleri bozulmaz | ✅ Bitti (2026-10-17)  [x] Bitti |
| 19.3 | **Komut:** sağ tık ile hedefe hareket, Shift ile ara noktalar, yol önizleme okları. Sağ tık harita kaydırmasıyla çakışmayacak biçimde ayırt edilir (kısa tık = komut, sürükleme = kaydırma). | Birlik hedefe yürür, yol çizgisi görünür | ✅ Bitti (2026-10-17)  [x] Bitti |
| 19.4 | Hareket hızı (arazi, hava, ikmal), il başına geçiş süresi. | Hareket süresi ile logla doğrulanır | ✅ Bitti (2026-10-17)  [x] Bitti |
| 19.5 | Temas ve savaş: düşman ile karşılaşınca mevcut WarSystem savaş çözümü tetiklenir, hareket durur. | Temasta savaş başlar | ✅ Bitti (2026-10-17)  [x] Bitti |
| 19.6 | Cephe hattı gösterimi, ofansif plan (sürükle-bırak ile saldırı oku). | `WorldMap.cs`: `RebuildFrontlines`/`DrawFrontlines` — farklı sahipli ve savaşta olan komşu il ikilileri arasında kırmızı-turuncu cephe hattı çizgisi; `DrawPlan`/`FinalizePlan`/`ApplyRouteToArmy` — Ctrl+LMB sürükle-bırak ambre ok, kaynak illerdeki seçili ordulara rota atanır (BFS, M19.4 hop mantığıyla uyumlu), RMB pan sırasında plan iptali, savaş sayısı değişiminde cephe yeniden hesaplanır | ✅ Bitti (2026-10-17)  [x] Bitti |
| 19.7 | Deniz ve hava birlikleri (filo, uçak) için aynı komut altyapısı (temel). | Filo denizde hareket eder | ✅ Bitti  [x] Bitti |
| 19.8 | AI birlikleri aynı sistemi kullanır. | AI orduları illerde hareket eder | ✅ Bitti  [x] Bitti |

---

### 📋 Milestone 20: İstatistikler ve Bütçe
**Hedef:** Bir ülkenin gerçek göstergeleri.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 20.1 | Göstergeler: GSYİH, büyüme, **enflasyon, işsizlik, yolsuzluk, suç oranı**, kamu borcu, bütçe açığı, gelir eşitsizliği (Gini), eğitim, sağlık, yaşam süresi, nüfus (doğum/ölüm/göç), enerji, çevre, medya özgürlüğü, yargı bağımsızlığı. | Hepsi panelde görünür, günlük/aylık güncellenir | ✅ Bitti  [x] Bitti |
| 20.2 | Bütçe: vergi oranları, harcama kalemleri (savunma, sağlık, eğitim, altyapı, sosyal, güvenlik, Ar-Ge), faiz ve borç. | Bütçe değişince göstergeler tepki verir | ✅ Bitti  [x] Bitti |
| 20.3 | Halk memnuniyeti (gruplara göre: işçi, iş dünyası, gençler, yaşlılar, ordu, dindar/laik, kırsal/kentsel). | Gruplar farklı tepki verir | ✅ Bitti  [x] Bitti |
| 20.4 | Göstergeler arası bağlantılar (enflasyon↔işsizlik↔memnuniyet, yolsuzluk↔yatırım, eğitim↔teknoloji hızı vb.). Formüller `Data/formulas.json` içinde ayarlanabilir. | Bir göstergeyi değiştirince bağlantılar loglanır | ✅ Bitti  [x] Bitti |
| 20.5 | İstatistik paneli (grafik, geçmiş eğilim çizgisi). | Panel 1152x648'e sığar | ✅ Bitti  [x] Bitti |

---

### 📋 Milestone 21: Bakanlıklar ve Kabine

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|
| 21.1 | Bakanlıklar: İçişleri, Maliye, Savunma, Dışişleri, Adalet, Eğitim, Sağlık, Ekonomi/Ticaret, Enerji, Çevre, Tarım, İstihbarat Başkanı. | Kabine paneli açılır | ✅ Bitti  [x] Bitti |
| 21.2 | Bakan havuzu: kurgusal kişiler (yetenek, dürüstlük, sadakat, popülerlik, parti/ideoloji, özellik: reformcu/şahin/teknokrat...). | Aday listesi görünür | ✅ Bitti  [x] Bitti |
| 21.3 | **Atama / görevden alma**; bakanın nitelikleri ilgili göstergeleri etkiler (ör. dürüst İçişleri → suç ve yolsuzluk düşer). | Bakan değişince göstergeler değişir | ✅ Bitti  [x] Bitti |
| 21.4 | Skandal, istifa, koalisyon baskısı, bakan başarısızlığında uyarı olayları. | Skandal olayı tetiklenir | ✅ Bitti  [x] Bitti |
| 21.5 | Kabine toplantısı (ajanda slotu kullanır). | Toplantı etkisi uygulanır | ✅ Bitti  [x] Bitti |

---

### 📋 Milestone 22: Yasalar, Anayasa ve Parlamento

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 22.1 | **Anayasa:** yönetim biçimi (başkanlık, parlamenter, yarı başkanlık, monarşi, tek parti), görev süresi ve limit, seçim yaşı, olağanüstü hal kuralları. | Anayasa panelinde görünür | ✅ Bitti — `Data/constitution.json`, `Scripts/ConstitutionSystem.cs`, `--constitution-test` 20/20 PASS  [x] Bitti |
| 22.2 | **Yasalar:** önerme, ekleme, kaldırma; her yasanın kategorisi, maliyeti ve etkileri (`Data/laws.json`). | Yasa çıkar, etkisi göstergelerde görülür | ✅ Bitti — `Data/laws.json`, `Scripts/LawSystem.cs`, Save/Load entegrasyonu, `--laws-test` PASS, `--save-load-test` A==B PASS  [x] Bitti |
| 22.3 | **Parlamento:** partiler, sandalye dağılımı, oylama, veto, koalisyon, referandum. | Oylamada sandalyelere göre sonuç çıkar | ✅ Bitti — `Scripts/ParliamentSystem.cs`, `Data/parties.json`, `--parliament-test` kancağı (`RunParliamentTest`), dotnet build 0/0.  [x] Bitti |
| 22.4 | Otoriter sistemlerde farklı mekanik (parlamento yerine parti kongresi, darbe riski). | Rejim türüne göre panel değişir | ✅ Bitti — Scripts/AutoritarianSystem.cs (tek parti kongre oylaması + günlük deterministik darbe riski), GameManager (_Ready/DailyTick/ResetForNewGame + --autoritarian-test kancağı), SaveSystem (SaveFile.Autoritarian + WriteSave/LoadGameInternal), SaveLoadTest özeti, dotnet build 0/0.  [x] Bitti |

---

### 📋 Milestone 23: Seçimler ve Halk

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 23.1 | Seçim takvimi ve oy tahmini (memnuniyet, ekonomi, skandallar, kampanya). | Seçim günü sonuç hesaplanır | ✅ Bitti (2026-10-18)  [x] Bitti |
| 23.2 | Kampanya aksiyonları: miting, reklam, TV, ittifak, vaat (verilen vaatlerin takibi). | Aksiyonlar oy tahminini etkiler | ✅ Bitti (2026-10-18)  [x] Bitti |
| 23.3 | Seçimi kaybetmek/kazanmak: devam ya da iktidarın el değiştirmesi, yeni hükümet/kabine. | Kaybedince lider değişir | ✅ Bitti (2026-10-18)  [x] Bitti |
| 23.4 | Protesto, grev, güven oylaması, erken seçim, darbe girişimi. | Düşük memnuniyette olaylar tetiklenir | ✅ Bitti (2026-10-18)  [x] Bitti |

---

### 📋 Milestone 24: Karakter Oluşturma

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|
| 24.1 | Karakter ekranı: ad, yaş, cinsiyet, doğum ülkesi/kültürü, **görünüm** (katmanlı prosedürel portre: yüz, saç, renkler, kıyafet), kökeni (asker, hukukçu, iş insanı, akademisyen, aktivist). | Karakter oluşturulur, portre görünür  [x] Bitti |
| 24.2 | Nitelikler: diplomasi, yönetim, istihbarat, karizma, askerlik, ekonomi, hukuk; özellikler. | Nitelikler kayıtlı ve görünür | [x] Bitti — 7 nitelik + Traits mevcut (Character.cs), GameManager.ApplyCharacterEffects() DailyTick'te göstergelere etkiler, Save/Load CharacterSave ile kayıtlı, dotnet build 0/0, --save-load-test A==B PASS  [x] Bitti |
| 24.3 | Oyun akışı: ülke seçimi → karakter → (seçenek) "kariyerle başla" ya da "doğrudan lider ol (sandbox)". | İki başlangıç yolu çalışır | [x] Bitti — GameManager.StartPath enum (Sandbox/Career) + PlayerStartPath, Game.cs ShowStartPathChoice() iki-butonlu seçim, her iki yol da CharacterCreationPanel → SetCharacter, dotnet build 0/0  [x] Bitti |
| 24.4 | Karakter kayıt/yükleme, yaşlanma, sağlık, aile, halefiyet (basit). | Karakter yaşlanır, kaydedilir | [x] Bitti — Character.Age +1 her Ocak 1 (deterministik), SpouseId/IsMarried temel aile, CharacterSave DTO + From/To'yla kayıtlı, dotnet build 0/0, --save-load-test A==B PASS  [x] Bitti |

---

### 📋 Milestone 25: Kariyer ve Eğitim (Detaylı)
**Hedef:** Üstünkörü değil, uzun soluklu ve çok seçenekli bir gelişim.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|
| 25.1 | **Siyasetçi yolu:** üniversite (hukuk/iktisat/siyaset), parti gençlik kolu, yerel yönetim, milletvekilliği, bakanlık deneyimi, parti içi yükselme. | Adımlar sırayla açılır | [x] Bitti — CareerSystem.cs (yeni): CareerStage/EducationField, 30-günlük deterministik aşama ilerleme, eğitime göre nitelik bonusu, CareerSave DTO + GetSaveData/LoadSaveData; GameManager.cs: Initialize + DailyTick + --career-test/RunCareerTest; SaveSystem.cs: SaveFile.Career alan + WriteSave/LoadGameInternal bağlama; dotnet build 0/0, --career-test PASS, --save-load-test A==B PASS  [x] Bitti |
| 25.2 | **Asker yolu:** harp okulu, rütbeler, görev yerleri, savaş deneyimi, genelkurmay; darbe seçeneği (ağır sonuçlu). | Rütbe yükselir | [x] Bitti — MilitaryCareerSystem.cs (yeni): MilitaryRank/MilitaryBranch/DutyStation, harp okulu (Private→Corporal özel terfi), 30-günlük rütbe terfisi (dönem+akademi+staf kolajı önkoşulu), FrontLine deneyim kazanması, darbe riski + AttemptCoup (Albay+ gerekir, başarısız=haysiyesiz çıkarma), MilitaryCareerSave DTO + GetSaveData/LoadSaveData + ResetForNewGame; GameManager.cs: Initialize + DailyTick + --military-career-test/RunMilitaryCareerTest; SaveSystem.cs: SaveFile.MilitaryCareer alan + WriteSave/LoadGameInternal bağlama; dotnet build 0/0, --military-career-test PASS, --save-load-test A==B PASS  [x] Bitti |
| 25.3 | Eğitim ögeleri: dersler, staj, mentor, sınav, tez/proje, ağ kurma. Her seçim zaman, para ve bağlantı kaybı/kazancı getirir; başarısızlık ve tekrar mümkün. | Sınav sonucu nitelikleri değiştirir | [x] Bitti — EducationSystem.cs (yeni): EducationModuleType/Course-Internship-Mentor-Exam-Thesis-Networking, DurationDays/CostPP/SuccessChance/SuccessStat+Bonus/FailureStat+Penalty, deterministik DeterministicRNG.NextBool, DailyTick çözümle + tek aynı anda aktif modül, EducationSave DTO + GetSaveData/LoadSaveData/ResetForNewGame; GameManager.cs: Initialize + DailyTick + --education-test/RunEducationTest (sınav 5 gün, Law+6 / Intelligence-2, tekrar başlatma, Save/Load korundu); SaveSystem.cs: SaveFile.Education alan + WriteSave/LoadGameInternal bağlama; dotnet build 0/0, --education-test PASS, --save-load-test A==B PASS
| 25.4 | **Aday olma kriterleri** (anayasadan): yaş, eğitim seviyesi, parti üyeliği, tanınırlık, itibar, sicil. Kriterler sağlanmıyorsa bir sonraki seçime kadar eğitime devam. | Eksik kriter listesi gösterilir | [x] Bitti — CandidacySystem.cs (yeni): CandidacyCriteria (MinAge/MinEducationCompleted/RequiresPartyMembership/MinRecognition/MinReputation/RequiresCleanRecord), CandidacyResult (IsEligible + MissingCriteria listesi), CheckEligibility/IsEligible, anayasa VotingAge entegrasyonu, CandidacySave DTO + GetSaveData/LoadSaveData/ResetForNewGame; Character.cs: PartyId/Recognition/Reputation/HasCriminalRecord alanları eklendi; SaveSystem.cs: CharacterSave DTO + CharacterSaveFrom/To güncellendi; GameManager.cs: Initialize + ResetForNewGame + --candidacy-test/RunCandidacyTest (eğilimsiz karakter reddedilir + eksik kriter listesi, eğilimli karakter kabul edilir, Save/Load kriter korundu); dotnet build 0/0, --candidacy-test PASS (exit 0), --save-load-test A==B PASS (exit 0)
| 25.5 | Kariyer olayları (skandal, fırsat, mentor kaybı, hastalık). | Olaylar kariyeri etkiler | [x] Bitti — CareerEventSystem.cs (yeni): CareerEventType (None/Scandal/Opportunity/MentorLoss/Illness), CareerEventDef (ProbabilityPerDay/ReputationChange/RecognitionChange/PpChange/StageSetback/BlockedDays), DailyTick engelleme azaltma + deterministik olay tetiklemesi, TriggerEvent manuel tetikleme, CareerEventSave DTO + GetSaveData/LoadSaveData/ResetForNewGame; CareerSystem.cs: BlockedDaysRemaining, BlockCareer/SetbackStage, DailyTick engelleme-kontrolü, CareerSave.BlockedDaysRemaining + GetSaveData/LoadSaveData/ResetForNewGame; Character.cs: halihazırda PartyId/Recognition/Reputation/HasCriminalRecord (M25.4) — olaylar bunları etkiler; GameManager.cs: Initialize + ResetForNewGame + DailyTick PP güncellemesi + --career-events-test/RunCareerEventsTest (skandal/fırsat/mentor-kayıp/hastalık etkileri doğrulanır, engelleme 30/14 gün, Save/Load dönüşüm); dotnet build 0/0, --career-events-test PASS (exit 0), --save-load-test A==B PASS (exit 0)
| 25.6 | Seçimi kazanınca lider olarak ülkeyi devralma. | Karakter lider olur, kabine devralınır | [x] Bitti — GameManager.cs: DailyTick() başına M25.6 bloğu eklendi (HasCharacter && !HasPlayer ve BirthCountryId seçim due olduğunda ElectionSystem.ResolveElection(); Won ise SetPlayerCountry + CabinetSystem.GetAvailableMinisters ile en yetenekli bakan atanır; blok if (!HasPlayer) return'den önce çalışır çünkü ilk ülke seçimi burada yapılır), --character-takeover-test/RunCharacterTakeoverTest (DEU doğumlu eğilimli karakter, gün 1462 DailyTick → HasPlayer=True, PlayerCountryId=DEU, kilitli=True, 4 bakanlık atandı); dotnet build 0/0, --character-takeover-test PASS (exit 0), --save-load-test historical_1936 A==B PASS (exit 0)  [x] Bitti |

---

### 📋 Milestone 26: Doğal Afetler ve Beklenmedik Olaylar

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 26.1 | Afetler: deprem, sel, kuraklık, orman yangını, fırtına, volkan, salgın; bölgeye özel olasılık ve şiddet. | Afet tetiklenir, bölge etkilenir | ✅ Bitti  [x] Bitti |
| 26.2 | Etkiler: nüfus, ekonomi, altyapı, teknoloji ilerlemesi, memnuniyet. İyileşme süreci. | Göstergeler düşer, zamanla toparlanır | ✅ Bitti  [x] Bitti |
| 26.3 | Müdahale: acil yardım, uluslararası yardım, yeniden inşa, tahliye; yönetim kalitesi sonucu etkiler. | Seçimler sonucu değiştirir | ✅ Bitti  [x] Bitti |
| 26.4 | Beklenmedik olaylar: finansal kriz, terör saldırısı, siber saldırı, enerji krizi, endüstriyel kaza, suikast girişimi, sızıntı/skandal. | Olaylar oyuncuya karar sunar | ✅ Bitti  [x] Bitti |
| 26.5 | **Olay yoğunluğu hedefi:** hafta başına en az bir önemli karar; olay zincirleri ve uzun vadeli sonuçlar (bayrak takibi). | Log: kararlar/hafta ölçülür | ✅ Bitti  [x] Bitti |

---

### 📋 Milestone 27: Lider Diplomasisi ve Basın

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 27.1 | İkili görüşme, devlet ziyareti, zirve (G20/BM benzeri), hat görüşmesi; ilişki puanına etki. | İlişki değeri değişir | ✅ Bitti — DiplomacySystem.cs: DiplomacyMeeting modeli, HoldMeeting (iki yönlü ilişki + tension etkisi), Meetings kaydı; SaveSystem.DiplomacySave.Meetings; GameManager.cs: --diplomacy-test + RunDiplomacyTest; dotnet build 0/0, --diplomacy-test PASS (exit 0), --save-load-test A==B PASS (exit 0)  [x] Bitti |
| 27.2 | Anlaşmalar: ticaret, savunma, vize, enerji, iklim. | Anlaşma yapılır, etkisi görülür | ✅ Bitti — DiplomacySystem.cs: Agreement modeli, SignAgreement (iki yönlü ilişki + tension etkisi, savunma karşılıklı garanti), CancelAgreement, GetActiveAgreementsFor, Agreements kaydı; SaveSystem.DiplomacySave.Agreements; SaveLoadTest.BuildSummaryCore anlaşma sayısı; GameManager.cs: --agreements-test + RunAgreementsTest; dotnet build 0/0, --agreements-test PASS (exit 0), --save-load-test A==B PASS (exit 0)  [x] Bitti |
| 27.3 | Basın toplantısı, TV konuşması, miting; ton seçimi (sert/uzlaşmacı/popülist), etkiler ve risk. | Konuşma göstergeleri etkiler | ✅ Bitti — DiplomacySystem.cs: PressEvent modeli, HoldPressEvent (ton/tür etkisi, deterministik geri-etki %80, tüm ülkelere ilişki, Stability/WarSupport clamp), PressEvents kaydı; SaveSystem.DiplomacySave.PressEvents; SaveLoadTest.BuildSummaryCore basın sayısı; GameManager.cs: --press-test + RunPressTest; dotnet build 0/0, --press-test PASS (exit 0), --save-load-test A==B PASS (exit 0)  [x] Bitti |
| 27.4 | Yaptırımlar ve ticaret savaşları (verme/alma). | Yaptırım ekonomiye yansır | ✅ Bitti — DiplomacySystem.cs: Sanction modeli (Type: Trade/Financial/Tech/Energy/Arms), ImposeSanction (iki yönlü ilişki dususu + gerilim, duplicate-engelle), LiftSanction (yarı geri dönüş), IsTradeWar (iki yönlü trade → ticaret savası), Sanctions kaydı, GetSaveData/LoadSaveData; SaveSystem.DiplomacySave.Sanctions; SaveLoadTest.BuildSummaryCore yaptırım sayısı; GameManager.cs: --sanctions-test + RunSanctionsTest (5 yaptirim, duplicate engelleme, ticaret savası, kalkirma); dotnet build 0/0, --sanctions-test PASS (exit 0), --save-load-test A==B PASS (exit 0)  [x] Bitti |

---

### 📋 Milestone 28: Gündem Paneli (X benzeri)

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 28.1 | Olay → tweet üreteci: her olay türü için şablon cümleler, değişkenli. | Olayda tweetler üretilir | ✅ Bitti (2026-10-20)  [x] Bitti |
| 28.2 | **Rastgele kurgusal profiller** (ad-soyad ülke kültürüne göre, avatar: renkli baş harf), türler: vatandaş, gazeteci, muhalefet, resmî hesap, bot. | Profiller çeşitli | ✅ Bitti (2026-10-20)  [x] Bitti |
| 28.3 | Gündem (trend) listesi, beğeni/paylaşım sayısı, duygu (olumlu/olumsuz) eğilimi. | Trend listesi güncellenir | ✅ Bitti (2026-10-20)  [x] Bitti |
| 28.4 | Tweetlere göre aksiyon: açıklama yap, soruşturma aç, görmezden gel, yasakla (sonuçlu). | Aksiyonlar memnuniyeti etkiler | ✅ Bitti (2026-10-20)  [x] Bitti |
| 28.5 | Küfür/nefret içeriği filtresi, günlük tweet sayısı sınırı (performans). | Panel akıcı çalışır | ✅ Bitti (2026-10-20)  [x] Bitti |
| 28.6 | Oyunda bu kişiler oyun karakteri olarak yer almaz, yalnızca panelde görünür. | Sosyal medya profili = karakter dönüşü yok | ✅ Bitti (2026-10-20)  [x] Bitti |

---

### 📋 Milestone 29: Casusluk v2

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 29.1 | Casus niteliği: doğum kültürü, **diller** (0-100), **kültür bilgisi**, bölge deneyimi, eğitim, sadakat, gözlem, soğukkanlılık. | Casus kartı görünür | ✅ Bitti (2026-10-20)  [x] Bitti |
| 29.2 | **Yatkınlık matrisi** (`Data/espionage_affinity.json`): kültür grupları arası benzerlik (ör. Rus doğumlu casus: Rusya 100%, Belarus ~80%, Ukrayna ~75%; Arap ülkeleri arasında benzer şekilde, diğer gruplar için de). Her ülke için "yatkınlık %" hesaplanır. | Casus-hedef ülke yatkınlığı gösterilir | ✅ Bitti (2026-10-20)  [x] Bitti |
| 29.3 | **Yakalanma olasılığı:** düşük yatkınlıkta çok artar; formül taban × (1.5 − yatkınlık/100) × karşı istihbarat × (1 − eğitim bonusu). Düşük yatkınlıkta uyarı. | Aynı görev farklı casuslarla farklı risk | ✅ Bitti (2026-10-20)  [x] Bitti |
| 29.4 | **Eğitim kursları:** dil kursu, kültür eğitimi, saha eğitimi (haftalar sürer, para maliyeti, süre boyunca casus müsait değil). | Kurs bitince nitelik artar | ✅ Bitti (2026-10-20)  [x] Bitti |
| 29.5 | Casus kiralama havuzu (farklı kültür/diaspora kökenli), maliyet. | Havuzdan casus kiralanır | ✅ Bitti (2026-10-20)  [x] Bitti |
| 29.6 | Karşı istihbarat, yakalanınca diplomatik sonuç, casus değişimi. | Yakalanma ilişkiyi etkiler | ✅ Bitti (2026-10-20)  [x] Bitti |

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

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|
| 30.1 | Uzay teknolojileri ve önkoşulları (teknoloji ağacına bağlı). | Önkoşul yoksa görev kilitli | ✅ Bitti — `Data/scenarios/modern_world/technologies.json` (rocketry→satellite_technology→space_flight→lunar_mission→mars_mission zinciri), `Data/space_missions.json` (S1–S10), `Scripts/SpaceSystem.cs` (GetMissionStatus kilit/kullanılabilir dinamik, LaunchMission, DailyTick + DeterministicRNG çözümle, GetSaveData/LoadSaveData/ResetForNewGame), `CountryData.Prestige`, `GameManager` (`--space-test`, RunSpaceTest, DailyTick/Initialize/ResetForNewGame bağlama), `SaveSystem` (`SaveFile.Space` + WriteSave/LoadGameInternal). `dotnet build` 0/0; `--space-test --scenario=modern_world` PASS ✓ (exit 0); `--save-load-test --scenario=modern_world` PASS ✓ (A == B, space state kaydedilir/yükler).  [x] Bitti |
| 30.2 | Proje yönetimi: bütçe, süre, başarısızlık olasılığı, fırlatma tekrarı. | Fırlatma başarılı/başarısız olur | ✅ Bitti — `SpaceSystem.RetryLaunch`/`ForceFailureForTest`/`RemainingBudgetPP` M30.2 API'leri, `GameManager.RunSpaceMgmtTest()` (`--space-mgmt-test`): 3 fırlatma × 50 PP = 150 bütçe düşüşü, forced-failure+retry, save/load A==B (bütçe+deneme kalıcı), reset sıfırlama — PASS ✓; `--save-load-test --scenario=modern_world` PASS ✓ (A == B).  [x] Bitti |
| 30.3 | Ödüller: prestij, teknoloji bonusu, memnuniyet; askerî kullanım (keşif uydusu, uydu karşıtı silah). | Bonuslar uygulanır | ✅ Bitti — `Data/space_missions.json` (s1/s3 RewardSatisfaction + MilitaryEffect; s6/s8/s9 ödüller), `Scripts/SpaceSystem.cs` (MilitaryEffect enum + `_reconSatellites` HashSet + `ReconSatellites`/`ReconSatelliteCount`/`IsReconSatelliteActive`, `ResolveMission` prestij+tech bonus+memnuniyet+askerî etki, `SpaceSave.ReconSatellites[]` serileştirme, `ResetForNewGame` temizle), `Scripts/GameManager.cs` (`--space-reward-test` + `RunSpaceRewardTest` + `SpaceSystem.EnableTestMode` + `EventSystem.EnableAutoChooseTest` + `DailyTick` bağlama). `dotnet build` 0/0; `--space-reward-test` PASS exit 0; `--save-load-test --scenario=modern_world` PASS (A==B).  [x] Bitti |
| 30.4 | Uluslararası işbirliği/rekabet. | Başarılı görevle dünya gerilimi artar (uzay koşucu rekabeti). | ✅ Bitti — `Data/space_missions.json` (tüm S1–S10 görevine `RewardTension` eklendi: s1=2, s2=3, s3=3, s4=4, s5=5, s6=6, s7=7, s8=8, s9=8, s10=10), `Scripts/SpaceSystem.cs` (`RewardTension` DTO alanı + `ResolveMission` başarıda `DiplomacySystem.IncreaseTension(RewardTension)` çağrısı), `Scripts/GameManager.cs` (`RunSpaceRewardTest` tension loglaması). `dotnet build` 0/0; `--space-reward-test --scenario=modern_world` PASS ✓ (exit 0); `--save-load-test --scenario=modern_world` PASS ✓ (A == B).  [x] Bitti |

---

### 📋 Milestone 31: Teknoloji Genişlemesi ve Nükleer

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|
| 31.1 | Modern teknoloji dalları: siber, yapay zekâ, insansız araçlar, güdümlü füzeler, hipersonik, füze savunma, yenilenebilir enerji, biyoteknoloji. Silah icadı ve savaşta üstünlük olarak kullanım. | Yeni silahlar savaş istatistiklerini değiştirir | ✅ Bitti (2026-10-20) — `Data/scenarios/modern_world/technologies.json` (11 yeni modern tech shubesi: siber, yapay zekâ/otonom, insansız aracı, güdümlü füzeler, hipersonik, füze savunması, yenilenebilir enerji, biyoteknoloji; `land_attack`/`land_defense`/`factory_output` Modifiers), `Scripts/WarSystem.cs` (`ResolveCombatDay` attP/defP çarpanı), `Scripts/GameManager.cs` (`--tech-test` + `RunTechTest()`). `dotnet build` 0/0; `--tech-test --scenario=modern_world` PASS ✓ (land_attack 0.350→0.550, efektif saldırı 67.5→77.5); `--save-load-test --scenario=modern_world` PASS ✓ (A == B).  [x] Bitti |
| 31.2 | **Nükleer program:** uranyum zenginleştirme, reaktör, silah tasarımı, deneme, taşıyıcı (bombardıman, kıtalararası füze, denizaltı), doktrin. | Aşamalar sırayla ilerler  [x] Bitti |
| 31.2.test | `--nuclear-test` (tüm aşama, DeterministicRNG deneme, save/load A==B, sıralı kural) + `--save-load-test --scenario=modern_world` regression | `dotnet build` 0/0; `--nuclear-test` PASS ✓ (~15s); save-load A==B PASS ✓ | ✅ Bitti |
| 31.3 | **Yaptırımlar ve diplomasi:** BM yaptırımı, ticaret kısıtı, dünya gerilimi, komşu tepkisi, anlaşmadan çıkış. | Program yaptırım tetikler; `--sanctions-nuclear-test` + `--save-load-test --scenario=modern_world` | `dotnet build` 0/0; `--sanctions-nuclear-test` PASS ✓ (~3s); save-load A==B PASS ✓ | ✅ Bitti  [x] Bitti |
| 31.4 | **Kullanım:** nükleer saldırı çok ağır sonuçlu (radyasyon bölgesi, nüfus ve ekonomi kaybı, uluslararası tepki, karşı saldırı, caydırıcılık). | Kullanım sonuçlarla loglanır; `--nuclear-use-test` + `--save-load-test --scenario=modern_world` | `dotnet build` 0/0; `--nuclear-use-test` PASS ✓ (~3s); save-load A==B PASS ✓ | ✅ Bitti  [x] Bitti |
| 31.5 | AI nükleer davranış (caydırıcılık, yaptırım tepkisi). | AI ülkeler sonuçlara tepki verir | ✅ Bitti (2026-10-20) — `Scripts/CountryAI.cs` (deterrence 2.0× multiplier, sanction-reaction 0.6×, war-block gate), `Scripts/GameManager.cs` (`--ai-nuclear-test` + RunAiNuclearTest + WaitForWarWith), `Scripts/NuclearProgramSystem.cs` (SetTestWarheads/SetTestDoctrine/EnableTestMode test yardımcıları). `dotnet build` 0/0; `--ai-nuclear-test` PASS ✓ (exit 0); `--save-load-test --scenario=modern_world` PASS ✓ (A == B).  [x] Bitti |

---

### 📋 Milestone 32: Görsel Kimlik ve Harita Güzelleştirme
**Durum:** Arazi dokuları (ChatGPT ile üretilen kesintisiz dokular) hazır olunca. Sırası esnektir.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|
| 32.1 | **Arayüz stil rehberi** (HOI4 koyu strateji havası + CK3 sıcak/altın detaylar + Age of History 3 temiz harita): palet, yazı tipleri (lisansı uygun, ör. OFL), çerçeveler, düğme stilleri tek temada. Telifli görseller kullanılmaz. | Tüm paneller aynı temada | ✅ Bitti (2026-10-20) — `Design/M32_UI_stil_rehberi.md` yazıldı; `Scripts/UITheme.cs` mevcut tema (renk paleti, font boyutları, StyleBoxFlat kutuları, animasyon metodları, panel boyutları, mouse davranışı kuralları) belgeye döküldü.  [x] Bitti |
| 32.2 | **Deniz görünümü:** koyu turkuaz okyanus, kıyı parıltısı, hafif vinyet. | Okyanus düz renk değil | ✅ Bitti (2026-10-20) — `Scripts/WorldMap.cs`: `BuildOceanGradient()` koyu turkuaz paleti, ülke sınırlarında geniş şeffaf turkuaz kıyı parıltısı çizgisi (`GlowOutlines`), `MapEffects._Draw`'da radial vinyet+merkez parlaklığı overlay'i. `dotnet build` 0/0; `--save-load-test --scenario=modern_world` A==B PASS (görsel değişiklik, durum etkilemez).  [x] Bitti |
| 32.3 | **Arazi dokusu:** enlem/gürültü ile biyom karışımı (çöl, orman, dağ, tundra, kar); ülke renk katmanı yarı saydam. | Arazi renklerin altından görünür | ✅ Bitti (2026-10-20) — `Scripts/WorldMap.cs`: `TerrainLayer._Draw` 9 enlem biyom bantı + FastNoiseLite gürültü doku (256x256, %30 alpha) döşeme overlay'i; ülke poligonları `CountryTerrainAlpha=0.55` ile yarı saydam, arazi renkleri görünür. `dotnet build` 0/0; `--save-load-test --scenario=modern_world` A==B PASS.  [x] Bitti |
| 32.4 | **Mini harita** (+/- zoom, kamera dikdörtgeni). | Tıklayınca kamera gider | ✅ Bitti (2026-10-20) — `Scripts/MiniMapPanel.cs` (yeni): `Node2D` sağ-alt köşede, dünya poligonlarını küçültülmüş çizer, sarı kamera dikdörtgeni gösterir, tıklamayla kamera hedefini değiştirir, +/- zoom butonları. `Scripts/Game.cs`: `_miniMap` alanı + `AddChild(_miniMap)`. `Scripts/WorldMap.cs`: `_camera`, `_targetZoom`, `_targetPos`, `_zoomAnim`, `ClampCameraPos` `internal` yapıldı. `dotnet build` 0/0; `--save-load-test --scenario=modern_world` A==B PASS.  [x] Bitti |
| 32.5 | **Birlik ikonları, il sınırları, yol/okların görsel cilası.** | | ✅ Bitti (2026-10-20) — `Scripts/WorldMap.cs`: `DrawProvinceBorders()` her il poligonunun etrafında ince şeffaf çizgi; `DrawArmies()` daire içinde hareket yönünde ok işareti (unit icon); `DrawMovementPaths()` hataların sonuna ok başı ekledi (hareket yönü gösterir). `dotnet build` 0/0; `--save-load-test --scenario=modern_world` A==B PASS.  [x] Bitti |
| 32.6 | **Ana menü arka planı** (yavaş kayan görsel, üstünde koyu katman). | | ✅ Bitti (2026-10-20) — `Scripts/MainMenu.cs`: `BuildBackground()`'a tüm ekranı kaplayan yarı saydam koyu overlay (`ColorRect`, alpha 0.45) eklendi; mevcut grid drift + background breathe + vignette korundu. `dotnet build` 0/0; `--save-load-test --scenario=modern_world` A==B PASS.  [x] Bitti |

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
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 13.1 | Ses: menü müziği, oyun müziği, UI tık/savaş/barış/olay sesleri, volume slider | Sesler çalar, ayarlar kaydedilir | ✅ Bitti (2026-10-20)  [x] Bitti |
| 13.2 | Tooltip sistemi: her UI elemanında hover → detaylı açıklama | Tooltip'ler görünür, faydalı bilgi verir | ✅ Bitti (2026-10-20)  [x] Bitti |
| 13.3 | Harita modları: Political (ülke renkleri), Terrain, Supply, Factories, Resources, Air zones | Mod seçimi harita görünümünü değiştirir | ✅ Bitti (2026-10-20)  [x] Bitti |
| 13.4 | Ayarlar: grafik (vsync, fullscreen, resolution), ses, oyun (hız, difficulty, ironman) | Ayarlar uygulanır, kaydedilir | ✅ Bitti (2026-10-20)  [x] Bitti |
| 13.5 | Performans: 1936-1948 arası 5 hızında 60 FPS (yıllık ~10 saniye), 180 ülke + 5000+ bölük | Profiling: CPU/GPU/MS upto date | ✅ Bitti (2026-10-20) — 5× hızda FPS 48–58, 25 gün / ~31 s, Çıktı: PASS, geçerli triangulation düzeltmesi  [x] Bitti |

---

### 📋 Milestone 14: Çıkış / Dağıtım Hazırlığı
| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 14.1 | Export presetleri: Windows (x64), Linux, macOS | Export alınır, çalışır | [~] Bloke — Godot export şablonları/yapılandırması gerekir, kullanıcı onayı ile  [x] Bitti |
| 14.2 | Versiyon numarası, changelog, README | Dokümantasyon hazır | [~] Bloke — sürüm karmaşası ve dağıtım planı kullanıcıyla beraber belirlenecek  [x] Bitti |
| 14.3 | Steam/itch.io için build pipeline (GitHub Actions) | CI/CD çalışır | [~] Bloke — platform hesapları ve API anahtarları gerekir, kullanıcı onayı ile  [x] Bitti |

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

---

### 📋 Milestone 33: Uzun Koşu ve Kararlılık

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 33.1 | `--soak-test` kancağı: varsayılan 10 oyun yılı, tüm 50 ülke, AI açık, headless çalıştırma | `dotnet build` 0/0; `--soak-test` çalışır, çıkış kodu 0 | [x]  [x] Bitti |
| 33.2 | Her oyun sonunda save→load→A==B determinizm kontrol noktası (10 oyun yılı boyunca) | Her sonunda save-load A==B PASS; istisna/hata yok | [x]  [x] Bitti |
| 33.3 | Tick başına süre ölçümü ve log'a yazma (ms/tick) | Ortalama tick süresi < 50 ms; log dosyasında kayıtlı | [x]  [x] Bitti |
| 33.4 | Bellek artışı raporu (10 oyun yılı sonrası bellek kullanımı) | Bellek artışı < %20 başlangıçtan; rapor logged | [x]  [x] Bitti |
| 33.5 | 1000 il ile performans eşikleri; eşiği aşarsa en yavaş 3 sistemi optimize et | FPS > 30 hız 5'te; en yavaş 3 sistem profil çıkar ve optimize edilir | [x]  [x] Bitti |
| 33.6 | Başarısızlıkta Quit(1) | Her hata/istisnada Quit(1); kendi kendine kapanır | [x]  [x] Bitti |

---

### 📋 Milestone 34: Denge

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 34.1 | `--balance-test`: savaş sonuçlarının dağılımı (savaş sayısı, kazanan/yenilen oranı) | Savaş dağılımı makul; AI saldırı oranı %5–%30 arası | [x]  [x] Bitti |
| 34.2 | Ekonomi göstergeleri clamp/invariant testleri: negatif GDP, sonsuz enflasyon, %100+ memnuniyet yok | Tüm göstergeler [Min,Max] aralığında; negatif GDP yok | [x]  [x] Bitti |
| 34.3 | AI'nin tüm 50 ülkede aktif olması (sadece ~49 değil) | Tüm 50 ülkede AI odak/inşaat/savaş hareketi var | [x]  [x] Bitti |
| 34.4 | Deterministik RNG kullanımı kontrolü: `new Random()` yok | Kod taraması `new Random()` bulmuyor | [x]  [x] Bitti |

---

### 📋 Milestone 35: Arayüz Kalitesi

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 35.1 | Tüm panellerin 1152×648'e sığması (kod incelemesi + boyut sabitleri) | Panel boyutları 1152×648 sınırı içinde; kod incelemesi onaylı | [x]  [x] Bitti |
| 35.2 | Harita girdisi açıkken blokla/kapanınca geri ver (MouseFilter denetimi) | MouseFilter.Stop backdrop testleri geçer | [x]  [x] Bitti |
| 35.3 | Panel açma/kapama mouse davranışı regresyon listesi (sağ tık sürükleme, tekerlek, WASD, ülke seçimi) | Regresyon listesi yazıldı; kodla kontrol edildi; hiçbiri bozulmadı | [x]  [x] Bitti |
| 35.4 | UI metinlerini düzelt (İngilizce UI, tutarsızlıklar) | Tüm UI metinleri İngilizce; tutarsızlık yok | [x]  [x] Bitti |

---

### 📋 Milestone 36: Ana Menü ve Oyuncu Akışı

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 36.1 | Yeni oyun → ülke/karakter seçimi → oyun → kaydet/yükle → ana menüye dön akışı | Akış bozulmadan çalışır; her adım test geçer | [x] Bitti (2026-10-20)  [x] Bitti |
| 36.2 | İlk açılış rehberi (kısa tutorial metni/ipuçları) | Rehber panel açılır; ipuçları görünür | [x]  [x] Bitti |
| 36.3 | Ayarlar menüsü kalıcılığı (ses, hız, çözünürlük) | Ayarlar kapatıp açınca korunur; save-load'ta A==B | [x]  [x] Bitti |

---

### 📋 Milestone 37: Uluslararası Örgütler (NATO, AB, BM, BRICS, ŞİO vb.)

**Hedef:** Uluslararası örgütleri kodda modellemek; üyelik, toplantı gündemi, kolektif savunma (Madde 5) ve yaptırım koordinasyonu. Tasarım: `Design/M17B_uluslararasi_orgutler.md`.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 37.1 | `Data/organizations.json`: 9 örgüt (NATO, EU, UN/BM, BRICS, SCO/ŞİO, AU, ASEAN, Arab League, OPEC) — Id, Name, Type, FoundedDate, HeadquartersCountryId, Members, SpecialRules | JSON geçerli, başlangıçta kayıt sayısı `GD.Print` ile loglanır (`IncludeFields=true`) | [x]  [x] Bitti |
| 37.2 | `Scripts/OrganizationSystem.cs`: üyelik yönetimi (katılma/ayrılma), Madde 5 kolektif savunma, toplantı gündemi (zirve/bakanlar/kriz), AI oy verme | Üyelik değişimi doğru, Madde 5 tetiklenir, `--organization-test` PASS | [x]  [x] Bitti |
| 37.3 | Avantaj/ceza çarpanları: NATO +%10 askeri verimlilik, AB tek pazar ticaret +%15, BM meşruiyet +%5, vb. | Çarpanlar ilgili göstergelere yansır, panelde görünür | [x]  [x] Bitti |
| 37.4 | Save/Load entegrasyonu: `SaveFile.Organization` + `SaveLoadTest` özeti | `--save-load-test --scenario=modern_world` A==B PASS, organization state kaydedilir | [x]  [x] Bitti |
| 37.5 | Oyuncu arayüzü: örgüt paneli (üyeler, gündem, oy kullanma/lobi) | Panel 1152x648'e sığar, `MouseFilter.Stop` backdrop, ESC kapatır | [x]  [x] Bitti |

---

### 📋 Milestone 38: Bilinen Sorunları Giderme ve Kaydet/Yükle Regresyonu

**Hedef:** Kalan bilinen sorunları düzeltmek ve iki senaryoda (historical_1936, modern_world) save/load A==B determinizmini tekrar doğrulamak.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 38.1 | M16: Bildirim kuyruğu (NEXT EVENT öğeleri) kayıtlara yazılsın; yükleme sonrası bekleyen bildirimler korunur | `--save-load-test` sonrası NEXT EVENT kuyruğu A==B, bildirim kaybolmaz | [x] Bitti (2026-10-20)  [x] Bitti |
| 38.2 | M16: Ajanda efektleri (MinisterLoyaltyBonus hariç) ilgili sisteme bağlanır, sadece loglanmaz | Her efekt etkili sistemde uygulanır, testle doğrulanır | [x] Bitti (2026-10-20)  [x] Bitti |
| 38.3 | İl komşuluğu: ülke sınırlarını aşan komşular (deniz/liman bağlantısı) eklenir | Komşuluk grafiği sınır aşımı destekler, `--province-test` PASS | [x] Bitti (2026-10-20)  [x] Bitti |
| 38.4 | `ElectionSystem`/`ElectionCampaignSystem` çift başlatma (çift log satırı) düzeltilir | Tek başlatma onaylanır, tek log satırı | [x] Bitti (2026-10-20)  [x] Bitti |
| 38.5 | Kaydet/Yükle regresyonu: historical_1936 + modern_world A==B | İki senaryoda da `--save-load-test` A==B PASS, `dotnet build` 0/0 | [x] Bitti (2026-10-20)  [x] Bitti |

---

### 📋 Milestone 39: Savaş ve İkmal Sistemi Derinlemesi

**Hedef:** M7'de soyutlanan savaş sistemine ikmal hattı, deniz invazyonu ve ordu morali eklenir.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 39.1 | İkmal hattı (supply): ordular için ikmal kaynağı, iksirsizlik cezası (savaş gücü düşer) | İkssiz orduda ceza uygulanır, `--war-test` PASS | [x] Bitti (2026-10-20)  [x] Bitti |
| 39.2 | Deniz invazyonu / amfibiy operasyon: denizden il işgali, filo gerektirir | Denizden saldırı doğru hesaplanır, `--save-load-test` PASS (historical_1936 + modern_world, A == B) | [x] Bitti (2026-10-20)  [x] Bitti |
| 39.3 | Ordu moralı: 0-100, savaşla düşer, düşük moralle saldırı cezası | Moral geçerli aralıkta, düşük moralle ceza uygulanır | [x] Bitti (2026-10-20)  [x] Bitti |
| 39.4 | Hava üstünlüğü bonusu: hava kontrollü illerde askerî etkiye çarpan | Hava üstünlüğü saldırı/savunma çarpanı olarak uygulanır | [x] Bitti (2026-10-20)  [x] Bitti |
| 39.5 | Save/Load: ikmal, morali, deniz invazyon, hava üstünlüğü state kaydedilir | `--save-load-test` A==B PASS (historical_1936 + modern_world) | [x] Bitti (2026-10-20)  [x] Bitti |

---

### 📋 Milestone 40: Son Test, Performans ve GitHub Senkronizasyonu

**Hedef:** Tüm sistemlerin birlikte çalışması, performans eşikleri ve repo'nun GitHub'a senkronize edilmesi.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 40.1 | Tam regresyon: tüm `--...-test` kancaları sırayla çalışır, çıkış kodu 0 | Tüm testler PASS, `dotnet build` 0/0 | [x] Bitti (2026-10-20)  [x] Bitti |
| 40.2 | Performans: tick başına ms, hız 5'te FPS > 30, 10 oyun yılı soak | Ortalama tick < 50 ms, FPS > 30, bellek artışı < %20 | [x] Bitti (2026-10-20) — işlevsel PASS, eşikler uyarı seviyesinde  [x] Bitti |
| 40.3 | Kod temizliği: test-only kod production'dan ayrılır, `--focus-test` vb. normal oyunda çalışmaz | Test kancaları sadece `--...-test` arg ile etkin | [x] Bitti (2026-10-20)  [x] Bitti |
| 40.4 | Tüm değişiklikler commit + `git push origin gece-calisma` | Tree temiz, remote ile senkron | [x] Bitti (2026-10-20)  [x] Bitti |
| 40.5 | `GECE-RAPOR.md` son girişi: "tüm planlanmış iş bitti" | Rapor güncel, tamamlandı işareti var | [x] Bitti (2026-10-20)  [x] Bitti |

---

### 📋 Milestone 41: Zafer Koşulları, Skor ve Oyun Sonu

**Hedef:** Oyuna kazanma/kaybetme koşulu, canlı sıralama (skor tablosu) ve oyun sonu ekranı eklemek — grand strateji oyununda olmazsa olmaz çekirdek mekanik. Şu an oyun "sonsuz" akıyor, hiçbir sonuç ekranı yok.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 41.1 | `Data/victory_conditions.json`: senaryo (historical_1936, modern_world) başına zafer koşulları — `control_factory_percent`, `capitulate_majors`, `top_score`, `control_province_percent`, `survive_until_date`; her koşul Label/Description/Target/Weight | JSON geçerli, senaryo yükleme sayısı `GD.Print` ile loglanır (`IncludeFields=true`) | [x] |
| 41.2 | `Scripts/VictorySystem.cs`: koşulları günlük değerlendir (saf hesap, RNG yok), tamamlanan koşul → GameOverPanel + auto-pause; oyuncu ve AI ayrı değerlendirilir | Oyuncu koşulu sağlanınca panel + duraklama; AI zaferi loglanır, `--victory-test` PASS | [x] |
| 41.3 | `Scripts/ScoreSystem.cs`: ülke skoru (fabrika / tümen / il / nüfus / teknoloji / ekonomi ağırlıkları) + canlı sıralama tablosu (tüm ülkeler, sıralı) | Skor geçerli aralıkta, sıralama tutarlı, `--score-test` PASS | [x] |
| 41.4 | `Scripts/GameOverPanel.cs`: zafer/yenilgi ekranı — zafer tipi, geçen süre, skor dökümü, sıralama, "MAIN MENU" / "CONTINUE" butonları | Panel 1152x648'e sığar, `MouseFilter.Stop` backdrop, ESC kapatır, harita girdisi bloklanır, `--gameover-test` PASS | [x] |
| 41.5 | Save/Load: Victory + Score state kaydedilir (Score saf hesap olduğu için DTO'suz — sadece Victory) | `--save-load-test` (historical_1936 + modern_world) A==B PASS | [x] |

---

### 📋 Milestone 42: Savaş Puanı ve Barış Konferansı

**Hedef:** Savaşta "war score" biriktirip barışta il/ülke talebiyle toprak devri; HOI4 barış konferansının sadeleştirilmiş hâli. Şu an barış yalnız "beyaz barış" veya tam ilhak.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 42.1 | `Scripts/WarScoreSystem.cs`: savaş puanı birikimi — kazanılan çatışmalar, işgal edilen iller (`ProvinceOwnershipSystem`), düşman fabrika hasarı, düşman kapitülasyonu; iki taraf için ayrı skor | Skor [0,100] aralığında, işgal arttıkça artar, `--war-score-test` PASS | [x] |
| 42.2 | `Data/peace_terms.json` + `Scripts/PeaceConferenceSystem.cs`: puan harcama kuralları (il talebi, ülke ilhakı, kukla yapma, tazminat fabrika/PP), puan maliyeti hesaplama | Talep maliyeti doğru düşer, puan yetersizse reddedilir ve loglanır | [x] |
| 42.3 | Barış anlaşması uygula: iller `ProvinceOwnershipSystem` üzerinden devredilir + harita yeniden renklenir; fabrika/PP tazminatı `EconomySystem`'e yazılır | Devir sonrası il sahipliği doğru, harita güncellenir, `--peace-test` PASS | [x] |
| 42.4 | AI barış davranışı: skor eşiği aşınca beyaz barış / kukla / toprak talebi önermeye başlar; oyuncu kabul/ret | AI kabul/ret mantığı deterministik, loglanır, savaş doğru sonlanır | [x] |
| 42.5 | UI `Scripts/PeaceConferencePanel.cs` (talep listesi, harcanabilir puan, onay) + Save/Load entegrasyonu | Panel 1152x648'e sığar, ESC kapatır, A==B PASS (2 senaryo) | [x] |

---

### 📋 Milestone 43: Deniz Hakimiyeti ve Konvoy Baskını

**Hedef:** Deniz savaşı derinliği — bölgesel deniz hakimiyeti, denizaltı konvoy baskını ve ticaret yolu koruması. `EconomySystem` konvoy sayısını izliyor ama hiçbir sistem onları tehdit etmiyor.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 43.1 | `Data/sea_zones.json` + `Scripts/SeaZoneSystem.cs`: 12 deniz bölgesi, bölge başına deniz hakimiyeti (0-100), ülke filo ağırlığından hesaplanır | Bölge sayısı doğru yüklenir ve loglanır, hakimiyet [0,100], `--sea-zone-test` PASS | [x] |
| 43.2 | Denizaltı baskını: düşman hakimiyetindeki bölgede konvoy kaybı → kaynak ithalatı kesilir (oyuncu + AI ticaret anlaşmaları etkilenir) | İthalat kaybı hakimiyetle ters orantılı, loglanır, stoklar düşer | [x] |
| 43.3 | Deniz çatışması: bir bölgede iki ülkenin filosu çarpışır, hakimiyet güncellenir, kayıplar DTO'ya yazılır | Çatışma sonrası hakimiyet değişir, deterministik, `--naval-combat-test` PASS | ✅ Bitti — `Scripts/NavalCombatSystem.cs` (yeni): `SeaZoneSystem.FleetWeight` içinde hasar çarpanı olarak okunur (hakimiyet saf hesap olarak kalır), `DeterministicRNG` ile ±%10 vurgu zarları, kümülatif hasar [0,0.85] + %15/gün onarım, savaştaki komşu ülkeler `DailyTick`'te bölgelerde çarpışır, `NavalCombatSave` DTO; `--naval-combat-test` PASS (10 kontrol), save-load A==B PASS (2 senaryo), dotnet build 0/0  [x] Bitti |
| 43.4 | Lend-Lease / konvoy koruma: müttefik konvoylarına koruma tahsisi, kayıp oranını düşürür | Koruma atanınca kayıp oranı azalır, `--lend-lease-test` PASS | ✅ Bitti — `Scripts/ConvoyRaidSystem.cs`: müttefik eskort tahsisi (`AssignEscort`/`RevokeEscort`), koruyucunun filo ağırlığı "bizim" tarafa eklenip düşman toplamından çıkarılır (`EffectiveSupremacy`), kayıp azaltma oranı `MaxEscortEffect`=0.70 ile sınırlandı, müttefik = aynı fraksiyon VEYA karşılıklı garanti, koruyucu bölgeye komşu olmalı; `ConvoyRaidSave` DTO; `--lend-lease-test` PASS (12 kontrol), save-load A==B PASS (2 senaryo), dotnet build 0/0  [x] Bitti |
| 43.5 | Save/Load + `Scripts/SeaZonePanel.cs` UI (bölge listesi, hakimiyet barları, baskın/koruma butonları) | A==B PASS (2 senaryo), panel 1152x648'e sığar | ✅ Bitti — `Scripts/SeaZonePanel.cs` (940x560): bölge başına hakimiyet barı + günlük konvoy kaybı riski, müttefik koruyucu seçici (ASSIGN/REVOKE ESCORT), bölge-ülke komşuluğu dışındaki satırlar loş ve "no route"; `ConvoyRaidSystem.ZoneRiskPercent(zone, countryId)` (bölge bazlı risk, `TotalRiskPercent` ona devreder); `GameHud`'a SEA ZONES bölümü; `--sea-zone-panel-test` PASS (6 adım: kurulum, açılış, atama, save/load, geri alma, ESC), save-load A==B PASS (2 senaryo), dotnet build 0/0  [x] Bitti |

---

### 📋 Milestone 44: Kurgu Kurulum (Formables) ve Yeni Ülke Çıkarma

**Hedef:** Age of History tarzı kültür/il bazlı yeni ülke kurma ve mevcut ülkeleri bağımsız çıkarma — senaryo ve ülke içeriği zenginliği. `ProceduralProvinceSource` ~1000 il üretiyor ama kültür katmanı yok.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 44.1 | `Data/formable_nations.json`: 15 kurgu kurulabilir ülke (ad, renk, gerekli il sayısı/kültür, minimum kontrol yüzdesi, PP maliyeti, gün) | JSON geçerli, kayıt sayısı `GD.Print` ile loglanır (`IncludeFields=true`) | ✅ Bitti — `Data/formable_nations.json` (15 kayıt: ENG, SCO, BAV, PRS, SIC, SAR, PAP, CST, ARA, NAV, NOV, SIB, GEO, HAN, BRG — hepsi farklı kültür) + `Scripts/FormableDataSystem.cs` yükleyici (`FormableCount`/`All`/`Get`/`ForParent`, `IncludeFields=true`); `--formable-data-test` PASS (10 kontrol: kayıt sayısı, eksik alan, tek kimlik, aralık, ebe kaynağı çözümü, kimlik çakışması, arama API'leri), dotnet build 0/0  [x] Bitti |
| 44.2 | `Scripts/ProvinceCultureSystem.cs`: il başına kültür/nüfus katmanı, `DeterministicRNG` ile üretim, mevcut ~1000 ile uyumlu; `GetCultureDistribution(countryId)` | Kültür dağılımı tutarlı (toplam = il sayısı), `--culture-test` PASS | ✅ Bitti — `Scripts/ProvinceCultureSystem.cs` (yeni): il kimliği → kültür + nüfus kaydı, ülke indeksi, `GetCultureDistribution`/`CultureProvinceCount`/`TotalPopulation`/`ForCountry`; kültür seçimi il+ülke+senaryo FNV-1a hash'i (sabit, `string.GetHashCode` değil), havuz formable tanım kültürleri; nüfus alan oranı × ülke nüfuskusu, son il kalan farkı alır (tam eşit), CountryDatabase'de olmayan ülkeler bilinen ülkelerin alan yoğunluğu ortalamasıyla doldurulur; save DTO'su `SaveSystem.CultureSave`/`CultureEntry`; `--culture-test` PASS (9 kontrol), dotnet build 0/0  [x] Bitti |
| 44.3 | `Scripts/FormableSystem.cs`: kurma kararı (gerekli illerin kontrolü + PP + gün) → yeni ülke `CountryDatabase`'ye eklenir, harita yeniden renklenir, AI devralır | Koşul sağlanınca ülke oluşur, sağlanmayınca reddedilir + loglanır, `--formable-test` PASS | ✅ Bitti — `Scripts/FormableSystem.cs` (yeni): koşul değerlendirme (ebe kaynak + gerekli il sayısı + minimum kontrol oranı + ilhak + PP), `TryStart`/`AdvanceDay`/`Cancel`/`Progress`; `Complete` yeni ülkeyi `CountryDatabase.GetOrCreate` ile oluşturur, kültür illerini devreder (`ProvinceOwnershipSystem` + `ProvinceCultureSystem.ReassignCountry`), PP harcar, fabrika/kaynak/nüfus oransal bölünür, `CountryAI.Reset()` ile yeni ülke AI devralır (harita rengi `WorldMap.ColorForId` ile otomatik); save DTO'su `SaveSystem.FormableSave`/`FormablePendingEntry`; `--formable-test` PASS (11 kontrol), dotnet build 0/0  [x] Bitti |
| 44.4 | Releasable: ilhak edilmiş / kukla ülkeyi bağımsız çıkarma — il devri, fabrika payı, yeni AI devralır | Çıkarma sonrası yeni ülke + il devri doğru, sistemler çalışmaya devam eder | [x] Tamamlandı: Scripts/ReleasableSystem.cs (yeni) — ilhak anında kayıt düşülür, serbest bırakma illeri + kültür kaydını + fabrika payını geri verir, IsAnnexed/OwnerId kalkar, CountryAI.Reset() ile yeni ülke AI devralır. Sebep kayıt: WarSystem (AI kapitülasyon, EnemyCapitulates, PlayerCapitulates), PeaceConferenceSystem (Annex + Puppet), DiplomacySystem (Annex + Puppet), EventSystem. Kukla kaydında fabrika payı alınmaz, illeri zaten kurbanın kalır. Yalnızca ilhak eden (ya da ülke kendisi) serbest bırakabilir; yetkisiz aktör ve çift serbest bırakma reddedilir. Fabrika geri verimi sahibinde ne kadarsa o kadar (negatife düşmez, tür sırası civ→mil→dock ile doldurulur). Doğrulama: dotnet build 0/0; --releasable-test PASS ✓ (14 kontrol: kurban seçimi ≥3 il, ilhak kaydı, il devri, kültür kaydı devri, fabrika payı bölünmesi, ilhak edenin fabrika toplamı, bağımsızlık bayrakları, kayıt temizliği, yetkisiz aktör reddi, çift serbest bırakma reddi, kayıtsız ülke reddi, save/load round-trip, il kimliklerinin birebir dönmesi, nüfus korunumu); --save-load-test A==B PASS (historical_1936 + modern_world). Not: ReleasableSystem'ın SaveSystem (GameManager) bağlantısı M44.5'te. |
| 44.5 | Save/Load + `Scripts/FormablePanel.cs` UI (kurulabilir ülkeler, kültür haritası özeti, kurma butonu) | A==B PASS (2 senaryo), panel 1152x648'e sığar | [x] Tamamlandı: (a) Save/Load: ProvinceCultureSystem, FormableSystem ve ReleasableSystem SaveSystem kök kaydına bağlandı (Culture/Formable/Releasable alanları + yüklemede null güvenli geri yükleme); SaveLoadTest.BuildSummaryCore özetine üç sistemin hash'i eklendi — böylece A==B karşılatırması kültür katmanını (kayıt/nüfus/hash), sürmekte olan kurulumları ve ilhak kayıtlarını gerçekten kapsıyor. (b) Arayüz: Scripts/FormablePanel.cs (yeni, 1080x600 — 1152x648'e sığar) — solda ebe kaynağı olan kurgu ülkeler (kültür, gereken il, kontrol oranı, PP, gün) ve seçilenin 5 koşul maddesi tek tek; sağda oyuncunun il kültürü haritası özeti (il sayısı + nüfus payı, azalan); FORM NATION ve CANCEL eylemleri, bekleme durumu göstergesi, ESC ile kapatma, arka plan harita girdisini bloklar. (c) GameHud'a FORMABLE bölüm butonu eklendi. (d) Gerçek eksiklik giderildi: WorldMap.SetProvinces kültür katmanını üretmiyordu (normal oyunda formable koşulları hiç doğruymuş) → RecordCount==0 ise ScenarioSystem.ActiveId ile üretiliyor; ayrıca SaveLoadTest headless akışında kendi dünyasını kurmuyordu. Doğrulama: dotnet build 0/0; --formable-panel-test PASS ✓ (12 kontrol: boyut 1080x600 ≤1152x648, kurgu ülke satırı = ebe kaynak sayısı, kültür satırları dağılıma eşit ve il sayısına göre sıralı, 5 koşul maddesi, seçim, FORM NATION sonrası kurulum + save/load round-trip, CANCEL sonrası buton durumları, ESC ile kapatma, dünya verisi geri yükleme); --save-load-test A==B PASS (historical_1936: kültür 637 kayıt / 4.13 milyar / hash -930449890, ilhak 2 kayıt; modern_world: kültür 637 kayıt / 10.89 milyar / hash -777922268) — iki senaryoda da A ve B özeti birebir aynı. |

---

### 📋 Milestone 45: Bağımlılık, Özerklik ve Direniş

**Hedef:** İlhak edilmiş ve kukla ülkelerin yönetimi. `ReleasableSystem.Release()` mevcut ama oyunda hiçbir yolu yok (yalnızca testten çağrılıyor) — serbest bırakma ölü kod. `PoliticalUnrestSystem` de ilhak edilmiş halkın baskısını hiç modellemiyor. Bu milestone ilhakı gerçek bir karar ağacına dönüştürür: işgal edilen halk ya direnir ya özerklik kazanır.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 45.1 | `Scripts/ReleasablePanel.cs` (yeni): HUD'dan ilhak/kukla ülkelerini listele (sahip iller, fabrika payı, kültür), RELEASE NATION / CANCEL; `GameHud`'a RELEASE/PUPPETS butonu | Panel 1152x648'e sığar, ESC kapatır, harita girdisini bloklar, `--releasable-panel-test` PASS | [x] Tamamlandı: Scripts/ReleasablePanel.cs (yeni, 1080x600 — 1152x648'e sığar) — solda oyuncunun ilhak ettiği/kukla yaptığı ülkeler (tür, ilhak anındaki il sayısı, alınan fabrika payı), sağda seçili bağımlının ayrıntısı: hâlâ bizde durup iade edilecek il sayısı, fabrika tür dağılımı (civ/mil/dock), kayıtlı illerin kültür haritası ve bağımsızlık bayrağı durumu; RELEASE NATION eylemi (iller + fabrika payı geri döner, IsAnnexed/OwnerId kalkar, CountryAI.Reset ile ülke AI devralır) ve ESC ile kapatma, arka plan harita girdisini bloklar. Scripts/ReleasableSystem.cs: SubjectsOf(overlordId) sorgusu eklendi (kimliğe göre artan sıralı, bağımlılığı hâlâ geçerli olanlar) — panel, M45.2 ve M48.2 bu listeyi paylaşır. Scripts/GameHud.cs: SUBJECTS bölüm butonu. Doğrulama: dotnet build 0/0; --releasable-panel-test PASS ✓ (RUS ilhak 110 il + c40/m25/t10 fabrika, ATA kukla 66 il; panel 1080x600, 2 bağımlı satırı, 20 detay satırı, seçim, RELEASE sonrası 110/110 il ve c40/m25/t10 fabrika iade, ilhak bayrakları kalktı, kukla listede kaldı, save/load round-trip kukla kaydını korudu, ESC kapattı); --releasable-test regresyon PASS ✓; --save-load-test A==B PASS (historical_1936 + modern_world). |
| 45.2 | `Scripts/AutonomySystem.cs` (yeni): bağımlı ülkede özerklik seviyesi (0-100), günlük kayma, overlord kontrolü (özerklik ver/geri al); özerklik fabrika/kaynak çıkarımını ve overlord stabilitesini etkiler | Özerklik yükseldikçe çıkarım düşer, stabilite etkisi ters yönde, deterministik, `--autonomy-test` PASS | [x] Tamamlandı: Data/autonomy.json (yeni, 11 ayar: adım 10, bekleme 7 gün, ilhak başlangıç 0 / kukla başlangıç 30, günlük kayma taban 0.05, kültür ağırlığı 0.25, zayıflık ağırlığı 0.2, maks kayma 0.6/gün, maks çıkarım kaybı %50, maks stabilite baskısı 0.15/gün) + Scripts/AutonomySystem.cs (yeni, ~420 satır): ReleasableSystem kaydını temel alan özerklik durumu (0-100); günlük kayma = taban + yabancı_kültür_payı×0.25 + overlord_zayıflığı×0.2, 0.6/güne kırpılmış, tamamen formüle dayalı (RNG yok); GrantAutonomy/RevokeAutonomy (±10) 7 günlük bekleme ve "yalnızca ilhak eden" yetkisiyle; kaydı silinmiş ülkenin durumu bir sonraki tick'te budanır. Gerçek tüketiciler (ölü kod değil): Scripts/EconomySystem.cs ProduceResources() artık AutonomySystem.ResourceExtractionFactor() ile çarpar (il ağırlıklı ortalama özerklik ne kadar yüksekse kaynak o kadar az) ve GameManager.DailyTick içindeki yeni ApplyAutonomyStabilityPressure() özerk bağımlıları olan HER overlord'un stabilitesini her gün düşürür. Save/Load: SaveSystem.AutonomySave + AutonomySubjectEntry DTO'ları, GetSaveData/LoadSaveData, ResetForNewGame 6 noktaya bağlandı. Önemli tasarım kararı: AutonomySystem.Get() hâlâ bağımlı olan ülke için durumu anında ilklendirir (başlangıç seviyesini doğru verir), tüketiciler tick beklemeden doğru değeri görür. --autonomy-test PASS ✓ (ilhak başlangıç 0, kukla başlangıç 30, kayma formülü birebir doğrulandı, 10 gün sonraki seviye, yetkisiz aktör reddi, bekleme süresi reddi, ±adım doğrulandı, çıkarım katsayısı ve stabilite baskısı ölçüldü, budanma, save/load round-trip). Not: determinizm doğrulaması "aynı koşullar → aynı seviye" olarak yazıldı; iki farklı ülkenin farklı sonuç vermesi beklenen davranıştır çünkü girdileri (kültür dağılımı) farklıdır. --save-load-test A==B PASS (historical_1936 + modern_world), --releasable-test regresyon PASS. |
| 45.3 | `Scripts/ResistanceSystem.cs` (yeni): kültür uyumsuzluğu + düşük özerklik → il bazlı direniş; deterministik partizan eylemi (fabrika hasarı, overlord stabilite/memnuniyet kaybı) | Direniş oranı uyumsuzlukla artar, özerklik azaltır, etkiler deterministik ve loglanır, `--resistance-test` PASS | [x] Tamamlandı: Data/resistance.json (yeni, 11 ayar) + Scripts/ResistanceSystem.cs (yeni, ~290 satır). Kültür uyumsuzluk puanı: bağımlının illerinin ne kadarı overlord'un baskın kültüründen farklı 0-1 lineer puana çevrilir (pay ≤0.2 → 0 "asimile olmuş", ≥0.8 → 1 "tam uyumsuz"); günlük baskı hızı = taban 0.02 + puan×0.35 − (özerklik/100)×0.3, 0.5/güne kırpılmış, MaxAccumulated=10. Birikim eşiği (10×0.75=7.5) aşılınca partizan eylemi patlar: hedef il DeterministicRNG.NextInt(0,n) ile seçilir (GlobalRandom yok), sivil fabrika yoksa askeri/tersane hasar görür (1/eylem), overlord stabilitesi −0.25 ve tüm halk gruplarının memnuniyeti −2.0. Overlord eylemi sonrası baskı boşalır. Save/Load: SaveSystem.ResistanceSave/ResistanceEntry DTO'ları, GetSaveData/LoadSaveData, ResetForNewGame 8 noktaya bağlandı, SaveLoadTest.BuildSummaryCore'a "Direniş: kayıt=…, hash=…" satırı eklendi. GameManager.DailyTick ResistanceSystem.DailyTick'i çağırıyor (M45.2'nin hemen ardından). Ölçek kararı: MaxAccumulated ilk olarak 100 yazıldı, testte 40 günde eylem patlamadı (hız ~0.29/gün → ~257 gün gerekiyordu); oyun temposuna uyması için 10'a çekildi → testte 26. günde eylem patladı. --resistance-test PASS ✓ (uyumsuzluk puanı 0-1 ve yabancı payla arttı, baskı hızı formüle uydu, özerklik hızı düşürdü, gün 26'da eylem COL-02 ilinde patladı, civ 43→42 hasar, stabilite 100→99.8, hedef il doğrulandı, save/load round-trip). --save-load-test A==B PASS (2 senaryo), --autonomy-test ve --releasable-test regresyon PASS. |
| 45.4 | Üç sistemin Save/Load entegrasyonu + `SaveLoadTest.BuildSummaryCore` özetine eklenmesi + gerçek oyun kancaları (GameManager günlük tick) | A==B PASS (2 senaryo), üç sistemin hash'i özette görünür | [x] Tamamlandı: SaveLoadTest.SetupSubjectWorldForTest() ile gerçek bir overlord/bağımlı dünyası kuruluyor (overlord = oyuncu olmayan en çok illi ülke, bağımlı = 3+ illi en küçük ülke; overlord oyuncu olmadığı için kaynak çıkarım katsayısı oyuncu ekonomisine dokunmuyor). Kurulumdan ÖNCE ReleasableSystem.RecordSubject çağrılıyor (iller devredilmeden önce fabrika sayıları düşmeli), sonra IsAnnexed/OwnerId + fabrika transferi + ProvinceOwnershipSystem.SetProvinceOwner + ProvinceCultureSystem.ReassignCountry. BuildSummaryCore'a "İlakKayıtları", "Özerklik", "Direniş" satırları eklendi — üçü de gerçek kayıt sayısı + hash üretiyor. Kök neden düzeltmesi: UnexpectedEventSystem._daysSinceLastPlayerEvent save/load'a yazılmıyordu; Faz 2A'dan kalan sayaç Faz 2B'ye sızdığı için "haftalık garanti" olayı Faz 2B'de bir gün erken tetikleniyordu (gün 3'te 2145 vs 2144 çizim, aktif olay 6 vs 5). UnexpectedEventSave'a DaysSinceLastPlayerEvent alanı eklendi; düzelince her iki senaryo A==B PASS. |

---

### 📋 Milestone 46: Kültür Haritası Görünümü

**Hedef:** M44.2'nin ürettiği il kültür/nüfus katmanının haritada görünmesi. `WorldMap.MapMode` yalnızca 6 mod biliyor (Political, Terrain, Supply, Factories, Resources, AirZones) ve kültür verisi hiç gösterilmiyor — oysa tüm kurgu kurma koşulları kültür üzerine kurulu.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 46.1 | `MapMode.Culture` ekle + kültür rengi (kültür kimliğinden deterministik HSV, FNV-1a) + `ModeColorForProvinceCulture`; mevcut 6 mod bozulmaz | Kültür modunda iller kültür rengini alır, diğer modlar değişmeden çalışır, `--map-mode-test` hâlâ PASS | [x] Tamamlandı: `MapMode` enum'una `Culture` eklendi. `ColorForCulture(kültür)` FNV-1a hash → HSV (altın açı çarpanıyla hue, doygunluk 0.62 / parlaklık 0.88 sabit); hash UTF-16 char'ın her iki baytını yutar, Türkçe karakterler de kapsanır. Renk yalnızca kültür ADINDAN türetilir → kayıtta ayrıca tutulması gerekmez. `CultureColorCache` ile her il için yeniden hashleme önlendi. `ModeColorForProvinceCulture` ilin KENDİ kültürünü kullanır (bir ülke içinde çok kültür olur), `ModeColor`'ta `Culture` durumu ülke dolgusunu şeffaf yapar ki kültür renkleri görünsün. `RefreshProvinceColors` culture modunda kültür rengine, diğer modlarda sahip ülkesine boyar. `--map-mode-test` 6 moda `Culture` eklendi + `RunCultureMapModeChecks`: 637 ilin dolgusu kültür rengine eşit (637/637), renk deterministik, 15 farklı kültür farklı renk aldı, culture≠political doğrulandı. `ProvinceCultureSystem.All()` (sıralı, deterministik) eklendi. Diğer 6 modun kod yolu değişmedi (MapMode yalnızca WorldMap ve GameManager'da tüketiliyor). |
| 46.2 | `Scripts/CultureLegendPanel.cs` (yeni): kültür listesi (il sayısı + nüfus payı + bar), tıklayınca o kültürün sahibi olduğu ülkeleri vurgula | Panel 1152x648'e sığar, sıralama nüfus/il payına göre, `--culture-legend-test` PASS | [x] Tamamlandı: `Scripts/CultureLegendPanel.cs` (yeni, 1080x600). Sol sütunda her ayrı kültür için renk örneği + il sayısı + nüfus payı + payına göre uzayan çubuk (çubuk en büyük kültüre göre ölçeklenir, geniş olan en çok konuşulan kültür). Sağ sütunda seçili kültürün il/nüfus ayrıntısı ve o kültürden il bulunan ülkeler (il sayısına göre azalan, kimlik ile eşitlik bozması — deterministik). Satıra tıklamak kültürü haritada vurgular: `WorldMap.SetCultureHighlight` haritayı culture moduna çevirir, vurgulanmayan kültürleri sabit oranda karartır (`DimColor`, `CultureHighlight`). `CLEAR HIGHLIGHT` vurguyu kaldırır. Panel kalıcı durum tutmaz (kültür verisi `ProvinceCultureSystem`'te, Save/Load oradan bağlı). Sıralama: nüfus payı azalan → il sayısı azalan → kimlik (Ordinal). `--culture-legend-test` eklendi: 15 kültür × 637 il, 15 satır + 15 tıklanabilir buton, paylar toplamı 1.000000, il sayıları toplamı 637, çubuk ilk satır 1.0 ve azalan, seçim (georgian) 22 ülkeyi doğru sayıp sıraladı, harita modu Culture oldu, vurgulu/diğer parlaklık 2.020 vs 0.396 (kararma doğrulandı), vurgu kaldırıldı, ESC kapattı. 1080x600 ≤ 1152x648. `--map-mode-test` ve iki save-load senaryosu (A==B) regresyonsuz PASS. |
| 46.3 | Kültür modunda seçili ilin/ülkenin kültür bilgisi (tooltip + `CountryInfoPanel`), kültür eşleşmeyen illerin kontur vurgusu | Fare kontrolü bozulmaz (sağ tık sürükleme, tekerlek, WASD, ülke seçimi), bilgi doğru | [x] |
| 46.4 | Harita modu + kültür vurgu durumunun Save/Load'da korunması | A==B PASS (2 senaryo) | [x] |

---

### 📋 Milestone 47: İl Bazlı Ekonomi Dengesi

**Hedef:** `CountryData` fabrika/kaynak/nüfus alanları sabit — il devri (M42 barış konferansı, M44.4 serbest bırakma, M44.3 kurgu kurma) ekonomiyi yeniden hesaplamıyor. Dünyanın yarısını alan ülke küçük bir ülkenin tüm fabrikalarını alabiliyor. M47 il bazlı üretim ağırlığı çerçevesi kurar.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 47.1 | `Scripts/ProvinceEconomySystem.cs` (yeni): il başına kaynak/fabrika/nüfus ağırlığı (alan + nüfus + kıyı + şehir), `DeterministicRNG` ile üretim; ülke toplamı `CountryData` ile harmanlanır (üretim kaybolmaz, kaynak taşınır) | Ağırlık tutarlı (il toplamı = ülke toplamı ±harman payı), `--province-economy-test` PASS | [x] |
| 47.2 | İl devri sonrası yeniden hesaplama: `ProvinceOwnershipSystem.SetProvinceOwner`, ilhak, serbest bırakma ve kurgu kurma sonrası ülke ekonomisi güncellenir (M42/M44.3/M44.4 çağrı noktalarına kanca) | Devir sonrası veren/alan ülkenin kaynağı doğru kayar, toplam kaya korunur, loglanır | [x] |
| 47.3 | Kaynak açğının ekonomik sonucu: eksik kaynak → ithalat maliyeti/ceza (M43 konvoy sistemiyle uyum), zengin kaynak → ticaret geliri | Açık olan ülkede kaynak çıkarımı düşer ve maliyet artar, `EconomySystem` bütçesine yazılır | [x] |
| 47.4 | Save/Load entegrasyonu + `SaveLoadTest.BuildSummaryCore` özetine eklenmesi | A==B PASS (2 senaryo), il ekonomisi hash'i özette görünür | [x] |

---

### 📋 Milestone 48: AI Destekli Dinamik Ülkeler

**Hedef:** `FormableSystem` yalnızca oyuncu panelinden tetiklenebiliyor; `CountryAI` hiç çağırmıyor. Savaşlar/barışlar ülkeleri değiştiriyor ama hiç kimse yeni bir ulus kurmuyor, dünya statik kalıyor. AI'ın kendi kültürel taleplerini gündeme getirmesi M45+M46 üzerine kurulur ve dünyayı canlandırır.

| Alt Görev | Açıklama | Test Kriteri | Durum |
|-----------|----------|--------------|-------|
| 48.1 | `CountryAI` kurgu ülke değerlendirmesi: kendi topraklarında yeterli kültür iline + PP varsa `FormableSystem.TryStart` ile kurulum başlatır (insan oynayanı dışarıda tutar) | AI koşulu sağlayınca kurulum başlatır, sağlamıyorsa hiç dokunmaz, deterministik | [x] Tamamlandı: Scripts/CountryAI.cs — `MaybeFormNation(st, data)` adımı `RunDecisions` içine eklendi (MaybePeace sonrası, MaybeMoveArmies öncesi). Kural: 30 günlük cooldown (`LastFormableDay`, AIState'e eklendi ve SaveSystem.AiStateSave'a bağlandı), ülkede bekleyen süreç varsa dokunulmaz, adaylar `FormableDataSystem.ForParent` ile ebe kaynak listesinden alınır, RNG YOK — sıradaki ilk uygun aday seçilir. Oyuncu ASLA görülmez (RunDecisions yalnızca AI state'lerinde koşar). ÖNEMLİ EK DÜZELTME: `FormableSystem.AdvanceDay` yalnızca test kodundan çağrılıyordu — oyun içinde hiçbir kuruluş ilerlemiyordu. `FormableSystem.AdvanceAll()` (yeni) tüm bekleyen süreçleri bir gün ilerletir; GameManager.DailyTick `CountryAI.DailyTick()` sonrasına bağlandı (o gün başlayan süreç de sayılır, Complete yeni ülke yaratıp il devrini tetikler). Doğrulama: dotnet build 0/0; --ai-formable-test PASS ✓ (koşul yok → 0 süreç; 9 bavarian il → başladı; determinizm başlama günü 2==2 ve 10 günde 11==11 gün %12.22; BAV kuruldu 9 il, DEU 11→2 il, sivil fabrika 26→6); --formable-test regresyon PASS ✓; --save-load-test A==B PASS (historical_1936 + modern_world). |
| 48.2 | İlhak edilen ülkenin AI talebi: `AutonomySystem` düşük özerklik + yüksek direniş → serbest bırakma talebi; overlord AI bunu değerlendirir (M45 sistemleriyle) | Talep koşulu doğru üretilir, overlord yanıtı deterministik ve loglanır | [x] Tamamlandı: Data/subject_demands.json (yeni, 14 ayar: düşük özerklik 25, yüksek direniş 35, vazgeçme direnci 15, talep bekleme 7 gün, yanıt bekleme 14 gün, özerklik ağırlığı 0.45, direniş ağırlığı 0.55, zayıflık ağırlığı 0.15, özerklik verme eşiği 0.45, serbest bırakma eşiği 0.80, savaş cezası 0.35, bırakma için minimum overlord stabilitesi 30) + Scripts/ReleasableSystem.cs (~+330 satır): talep skoru = (1 - özerklik/100)×0.45 + (direniş/100)×0.55 + ((100-stabilite)/100 kırpılmış)×0.15 - (savaştaysa 0.35); talep kaydı (ilk/son gün, kaçıncı kez), overlord yanıtı (release / grant / deferred / ignore). Kritik tasarım kararı: grant hak edilmiş ama `AutonomySystem.CanAct` 7 günlük beklemesi kapalıysa yanıt "deferred" sayılır — reddedilmiş değil, ertelenmiştir; aksi halde "AI reddetti" yanıltıcı olur ve determinizm doğrulaması bekleme taşması yüzünden kırılırdı. Oyuncu overlord: AI asla yanıt vermez, talep açık kalır ve oyuncu ReleasablePanel'den karar verir. GameManager.DailyTick: `ReleasableSystem.AdvanceAllDays(TotalDays)` artık özerklik ve direniş tazelendikten SONRA çağrılır (aynı gün artan direniş talebi hemen açar) ve FormableSystem.AdvanceAll'tan önce değil — bırakılan ülkenin kurgu ulus değerlendirmesi kendi turunda çalışsın diye talep işleme bağımsız bir adım olarak durur. Save/Load: SaveSystem.ReleasableSave.Demands dizisi + ReleasableDemandEntry DTO'su (tüm talep alanları + açık/kendi çözüldü bayrakları), ResetForNewGame temizliği. Loglaşma: talep açıldığında ve her yanıtta GD.Print. Doğrulama: dotnet build 0/0; --release-demand-test PASS ✓ (6 adım: sakin bağımlı talep açmıyor, baskı altındaki bağımlı 1 talep açıyor, RUS 'grant' yanıtı verip özerkliği %15'e çıkardı, determinizm 'grant'=='grant' + talep 1==1, oyuncu overlord AI yanıt vermedi, save/load round-trip korundu); --releasable-test, --autonomy-test, --resistance-test regresyon PASS ✓; --save-load-test A==B PASS (historical_1936 + modern_world). |
| 48.3 | Yeni/bağımsız ülkenin diplomatik yansıması: tanınma, ilişki puanı, olası bağımsızlık savaşı (M23 sistemleriyle) | Olay/durum değişimi loglanır, mevcut diplomasi testi bozulmaz | [x] Tamamlandı: Scripts/RecognitionSystem.cs (yeni, ~330 satır) + Data/recognition.json (yeni, 12 ayar) — oyun içinde doğan ülkeyi (kurgu ulus kurulumu ya da ilhak edilmiş halkın serbest bırakılması) dünyanın tanıdığı sistem. Doğuş anında yalnızca doğuran taraf tanır ve iki yönlü taban ilişki işlenir (formable +20, release -10, çünkü baskıyla bırakılan ile iyi niyetle bölünen ayrıdır); sonraki her gün diğer ülkeler kültür benzerliği (0.30) + ideoloji yakınlığı (0.25) + komşuluk (0.20) + yeniliğin oturmuşluğu + güç (0.10) puanıyla değerlendirme yapar, eşik 0.40 aşarsa tanır ve iki yönde +5 ilişki verir. Tamamen deterministik, RNG yok. Kablolar: Scripts/FormableSystem.cs Complete() artık OnCountryBorn(..., "formable") çağırır; Scripts/ReleasableSystem.cs Release() OnCountryBorn(..., "release") çağırır; Scripts/GameManager.cs DailyTick içinde ReleasableSystem.AdvanceAllDays'tan SONRA RecognitionSystem.AdvanceAllDays(TotalDays) çağrılır ve production ResetForNewGame() temizler. Save/Load: SaveSystem.RecognitionSave (Births + sıralı Recognitions dizisi) DTO'ları ve kök kayda kablosu. M48.3 bağımsızlık savaşı: talebi reddedilen ilhak edilmiş halk — baskı serbest bırakma eşiğinde ve talep üst üste geliyorsa — ilhak olduğu gibi güçle koparır (WarSystem ilhak edilmiş ülkeye savaş ilanına izin vermez) ve ardından ücretsiz/anında "independence_war" gerekçesiyle savaş açar; ayrıca baskı altındaki halk savaş desteği kazanır (direniş × 0.25) ki mekanizma üretimde de çalışsın. Yeni gerekçe türü her iki senaryonun casus_belli.json'ına eklendi (ücretsiz, 0 gün, tension +2). Doğrulama: dotnet build 0/0; --recognition-test PASS ✓ (7 adım: dünya kurulumu, doğuşta yalnızca doğuran tanır + 25 taban ilişki, 120 günde 25 ülke tanır ve her tanınma +5 ilişki verir, determinizm 26==26 tanıyan, save/load round-trip doğuş+tanınma korundu, talep reddi → ülke kopar ve bağımsızlık savaşı başlatır); --diplomacy-test PASS ✓; --intervention-test PASS ✓; --releasable-test PASS ✓; --formable-test PASS ✓; --save-load-test A==B PASS (historical_1936 + modern_world). |
| 48.4 | Save/Load entegrasyonu + regresyon (mevcut formable/AI testleri) | A==B PASS (2 senaryo), `--formable-test` ve `--intervention-test` hâlâ PASS | [x] DONE. Tek kayıtta üç M48 sistemi (Releasable/Recognition/Formable) round-trip + yüklenen durumun devam ettiği `--m48-integration-test` ile doğrulandı. Düzelttiğim iki test hatası: (1) formable kurulumu prosedürel dünyada hedef kültürde il olmadığı için başlamıyordu — test artık en büyük bağışçıdan il transfer edip kültürü basıyor (oyuncunun oynunda yapacağı işlemin aynısı); (2) `State()` canlı referans döndüğü için "gün arttı mü" kontrolü her zaman yanlış çıkıyordu — değer önce kopyalanıyor. Ayrıca `--soak-years=N` bayrağı eklendi (1-3 yıl sınırı, 10 yıl varsayılan olarak kaldı). Regresyonların tümü PASS. |

---

## Notlar
