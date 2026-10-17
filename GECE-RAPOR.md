# GECE-RAPOR — 2026-10-17 (gece-calisma dalı, otonom)

## M18.1 — ✅ DONE
- `Scripts/IProvinceSource.cs`, `Scripts/ProvinceData.cs`, `Scripts/ProceduralProvinceSource.cs`: ~1000 il, DeterministicRNG + sabit seed, dotnet build 0 hata / 0 uyarı.
- Commit: aed6298 (push'ed origin gece-calisma).

## M18.2 — ✅ DONE
- `ProceduralProvinceSource.BuildNeighborGraph`: shared-edge komşuluk deteksiyonu, dotnet build 0/0.
- Commit: bfa1218 (push'ed origin gece-calisma).

## M18.3 — ✅ DONE
- `Scripts/ProvinceOwnershipSystem.cs` (yeni), `WorldMap.cs` (SetProvinces/DrawProvinces/RefreshProvinceColors): il sahipliği + harita renklendirme, ilhak sonrası yeniden boya, dotnet build 0/0.
- Commit: 95c8e0e (push'ed origin gece-calisma).

## Bilinen sorunlar
- Komşuluk yalnızca ortak-kenar tabanlı; ülke sınırlarını aşan komşular kapsanmıyor.
- İl çizimi mevcut ülke çiziminin üzerine eklenir (katmanlı Polygon2D node'ları).

## M18.4 — ✅ DONE (2026-10-17)
- `WorldMap.FindProvinceAt`: hızlı hit-test (bounding-box önfiltresi + point-in-polygon), FPS log (120 frame'de bir), dotnet build 0/0.

## M18.5 — ✅ DONE (2026-10-17)
- `ProvinceOwnershipSystem.GetSaveData/LoadSaveData`, `SaveSystem.ProvinceOwners` DTO, `SaveLoadTest` il özeti — il sahipliği kayıtlara eklenir, A==B testi geçer.

## M19.1 — ✅ DONE (2026-10-17)
- `Army.ProvinceId` alanı, `WorldMap.DrawArmies()` (_Draw'ta çember sayacı), dotnet build 0/0.

## M19.2 — ✅ DONE (2026-10-17)
- `Scripts/WorldMap.cs`: `SelectedProvinceIds` (HashSet), tek tık / Shift ile çoklu il seçimi, fareyle sürükle-kutu (marquee) seçimi; `DrawSelection` kutu çerçevesi + seçili-il vurgusu (_Draw'da), `ProvinceSelectionChanged` string[] sinyali; RMB pan ve mevcut LMB ülke seçimi ile çakışmaz. dotnet build 0/0.
- Commit: push'ed origin gece-calisma.

## M19.3 — ✅ DONE (2026-10-17)
- `Scripts/ArmySystem.cs`: `Army.TargetProvinceId`, `Army.WaypointIds` (sıralı ara nokta), `Army.IsMoving` (Save/Load'a otomatik dahil).
- `Scripts/WorldMap.cs`: `_provinceById` (id → ProvinceData) + `_movementPaths` (army id → rota noktaları); RMB kısa tık (`<6px`, pan'dan ayrı) seçili illardaki tüm orduları hedef ili hareket ettirir (`IssueMoveOrders`); shift=true ara nokta ekler, shift=false yol sıfırlar; BFS komşuluk grafiği üzerinden rota (`ComputeMovementPath`/`BfsPath`); `_Draw`'da `DrawMovementPaths` (mavi polyline + yeşil hedef / sarı ara-nokta çemberi); seçili ordu yoksa veya il yoksa mevcut `HandleRightClickOrder` (savaş/cephe emri) devreye girer — davranış değişikliği yok. dotnet build 0/0.
- Commit: (hazırlanacak).

## M19.4 — ✅ DONE (2026-10-17)
- `Scripts/ArmySystem.cs`: `Army.MovementPathIndex`, `Army.DaysPerProvince` (varsayılan 2 oyun günü/il), `Army.DaysRemainingInHop` eklendi; `DailyTick` seçili/ hareket eden her ordunun `DaysRemainingInHop`'ını günde 1 azaltır, sıfıra ulaştığında `ProvinceId` bir sonraki yoldaki ille güncellenir, son hedefte hareket durur ve loglanır.
- `Scripts/WorldMap.cs`: `IssueMoveOrders` anında varışı kaldırdı, `BuildFullRoute` (BFS zinciri) ile `WaypointIds`'i tam yürütme rotası olarak doldurdu; `_movementPaths`/`ComputeMovementPath` kaldırıldı, `DrawMovementPaths` `ProvinceId`+`WaypointIds`'den çizgi+çember çizer; `DrawArmies` hareket eden orduları mevcut ve bir sonraki il centroid'u arasında interpolasyonla çizer. dotnet build 0/0.
- Commit: (hazırlanacak).

## M19.5 — ✅ DONE (2026-10-17)
- `Scripts/ArmySystem.cs`: `DailyTick`'teki hop döngüsünde `OnArmyContactEnemy` eklendi — ordunun bir ilde durduğunda `ProvinceOwnershipSystem.GetProvinceOwner` ile sahip kontrol edilir; sahip oyuncu değilse hareket durdurulur, `WarSystem.IsAtWarWith` true ise WarSystem çatışma çözümüne yönlendirilir, false ise tema loglanır. dotnet build 0/0.
- Commit: push'ed origin gece-calisma.

## M19.6 — ✅ DONE (2026-10-17)
- `Scripts/WorldMap.cs`: `FrontlineSegment`/`PlanArrow` sınıfları; `RebuildFrontlines()` (farklı sahipli + savaşta olan komşu il ikilileri arasında kırmızı-turuncu cephe hattı), `DrawFrontlines()` (_Draw'da çizim), `EnsureFrontlines()`/_frontlinesDirty (sahilik/savaş değişikliğinde yeniden hesaplama), `DrawPlan()`/`FinalizePlan()`/`ApplyPlanToSelectedArmies()`/`ApplyRouteToArmy()` (Ctrl+LMB sürükle-bırak ambre ok, kaynak illerdeki seçili ordulara rota atanır, M19.4 hop mantığıyla uyumlu), `IssueMoveOrders` refactor (`ApplyRouteToArmy` üzerinden), `_Input` Ctrl+LMB basım/sürükle/bırak + RMB pan plan-iptali. dotnet build 0/0.
- Commit: push'ed origin gece-calisma.

## M19.7 — ✅ DONE (2026-10-17)
- `Scripts/ArmySystem.cs`: `UnitDomain` enum (Land/Naval/Air), `CountryId` ve `Domain` alanları `Army`'ye eklendi; `CreateNavy()` ve `CreateAirForce()` factory metodları (aynı Army alanı, farklı Domain).
- `Scripts/WorldMap.cs`: `DrawArmies`'te domain'e göre farklı işaret — kara kırmızı (6px), deniz mavi (7px), hava sarı-kırmızı (5px); `MouseFilter` degrade etmiyor. dotnet build 0/0.
- Commit: 3a801cb (push'ed origin gece-calisma).

## M19.8 — ✅ DONE (2026-10-17)
- `Scripts/CountryAI.cs`: `LastMoveDay` AIState'ye eklendi; `MaybeMoveArmies()` — her 7 günde bir, savaşta olan düşmanın rastgele iline ilk kara ordusunu hareket ettirir (deterministik Shuffle, `WorldMap.IssueArmyMoveOrder` üzerinden BFS rota + M19.4 hop mantığı); `TickDecisions`'e eklendi; `LastMoveDay` serialize edildi (GetSaveData/LoadSaveData).
- `Scripts/ProvinceOwnershipSystem.cs`: `GetProvincesByOwner(countryId)` — bir ülkenin illerinin id listesi (reverse lookup).
- `Scripts/SaveSystem.cs`: `AiStateSave.LastMoveDay` DTO alanı.
- `Scripts/WorldMap.cs`: `IssueArmyMoveOrder(army, targetProvinceId)` — public static, AI'nın aynı BFS rotası + hop altyapısını kullanmasını sağlar. dotnet build 0/0.
- Commit: 3a801cb (push'ed origin gece-calisma).

## M20.1 — ✅ DONE (2026-10-17)
- `Data/statistics_indicators.json`, `Scripts/StatisticsSystem.cs`, `Scripts/StatisticsPanel.cs`: 17 gösterge tanımı, deterministik ±0.3 günlük dalga, [Min,Max] clamp, Save/Load DTO, SaveLoadTest A==B kapsamında, HUD STATISTICS butonu, GameManager init+tick, dotnet build 0/0.
- Commit: 7d3cfeb (push'ed origin gece-calisma).

## M20.2 — ✅ DONE (2026-10-17)
- `Data/budget.json`, `Scripts/BudgetSystem.cs`: vergi oranları, harcama kalemleri, borç/faiz, günlük gelir-harcama-açık borç birikimi, göstergelere yansıma (budget_deficit, public_debt, gdp_growth, inflation, health, education, environment), Save/Load DTO, SaveLoadTest kapsamında, dotnet build 0/0.
- Commit: c75ef3d (push'ed origin gece-calisma).

## M20.3 — ✅ DONE (2026-10-17)
- `Data/public_satisfaction.json`, `Scripts/PublicSatisfactionSystem.cs`: 9 toplum grubu + tepki ağırlıkları, günlük memnuniyet doğruşu (gösterge+bütçe tabanlı), Save/Load DTO, SaveLoadTest kapsamında, dotnet build 0/0.
- Commit: (hazırlanacak).

## M20.4 — ✅ DONE (2026-10-17)
- `Data/formulas.json`, `Scripts/FormulaSystem.cs`: gösterge bağlantıları (inflation→unemployment, unemployment→gdp_growth, corruption→gdp_growth, education→gdp_growth, vb.), deterministik günlük etkisi (Weight × Kaynak × LinkSpeed, [Min,Max] clamp), GameManager sırası Budget→Formül→Memnuniyet, SaveLoadTest özeti, dotnet build 0/0.
- Commit: 3adac99 (push'ed origin gece-calisma).

## M20.5 — ✅ DONE (2026-10-17)
- `Scripts/StatisticsSystem.cs`: her gösterge için son 60 gün değer geçmişi, Save/Load'ta History DTO, GetHistory.
- `Scripts/GraphControl.cs` (yeni): 60 günlük polyline grafik, [Min,Max] ölçeklendirme, eşik etiketleri, son değer vurgusu.
- `Scripts/StatisticsPanel.cs`: gösterge seçici OptionButton + eğilim grafiği; panel 860×560 → 1152×648'de sığar; ESC kapatır.
- `Scripts/SaveLoadTest.cs`: istatistik özetine geçmiş kayıt sayısı; dotnet build 0/0; SaveLoadTest A==B geçer.
- Commit: (hazırlanacak).

## M21.1 — ✅ DONE (2026-10-17)
- `Data/cabinet_ministries.json` (yeni): 12 bakanlık tanımı (İçişleri, Maliye, Savunma, Dışişleri, Adalet, Eğitim, Sağlık, Ekonomi/Ticaret, Enerji, Çevre, Tarım, İstihbarat).
- `Scripts/CabinetSystem.cs` (yeni): bakanlık tanımları + ülke→bakanlık→bakan atama sözlüğü; `InitializeAll`/`ResetForNewGame`, `AssignMinister`, `GetSaveData`/`LoadSaveData`; deterministik (yeni RNG yok).
- `Scripts/CabinetPanel.cs` (yeni): overlay paneli (860×520), backdrop MouseFilter.Stop harita girişini bloke eder, ESC kapatır, STATISTICS butonu yanında HUD CABINET butonu.
- `GameManager.cs`, `SaveSystem.cs`, `SaveLoadTest.cs`, `GameHud.cs` güncellendi (ResetForNewGame, SaveFile.Cabinet alanı, kabine özeti, HUD butonu).
- `dotnet build` 0 hata / 0 uyarı.
- Commit: push'ed origin gece-calisma.

## M21.2 — ✅ DONE (2026-10-17)
- `Data/cabinet_ministers.json` (yeni): 41 kurgusal bakan (DEU, USA, GBR, FRA, RUS, JPN, ITA, TUR) — Skill/Honesty/Loyalty/Popularity, Party/Ideology, Traits.
- `Scripts/CabinetSystem.cs` (güncellendi): `Minister` sınıfı, `LoadMinisters()` JSON parse (`MinistryIds` → `string[]` via `List<string>` accumulator, CS0019 düzeltildi), `GetMinisters(countryId)`, `GetAvailableMinisters(countryId, ministryId)`; GD.Print yüklenen kayıt sayısını loglar.
- `Scripts/CabinetPanel.cs` (güncellendi): atanmamış bakanlıklar için uygun aday listesi (skill/honesty/loyalty/popularity/traits), "No available ministers" uyarısı.
- `dotnet build` 0 hata / 0 uyarı.
- Commit: 65fd2b4 (push'ed origin gece-calisma).

## M21.3 — ✅ DONE (2026-10-17)
- `Scripts/CabinetPanel.cs` (güncellendi): atanmamış bakanlıklar için aday başına ASSIGN butonu, atanmış bakanlıklar için bakan nitelikleri (skill/honesty/loyalty/popularity + Traits) ve DISMISS butonu.
- `Scripts/CabinetSystem.cs` (güncellendi): `ApplyMinisterEffects(countryId)` — günlük deterministik etki: bakan nitelikleri ilgili StatisticsSystem gösterge(lerine) delta uygular (ör. dürüst İçişleri → corruption & crime_rate↓, yetkin Maliye → budget_deficit & inflation↓, gdp_growth↑).
- `Scripts/GameManager.cs` (güncellendi): `DailyTick`'te oyuncu ülkesi için `CabinetSystem.ApplyMinisterEffects(PlayerCountryId)` çağrılır.
- `dotnet build` 0 hata / 0 uyarı.
- Commit: 36504e9 (push'ed origin gece-calisma).

## Sonraki adım
- M21.4: Skandal, istifa, koalisyon baskısı, bakan başarısızlığında uyarı olayları.

## M21.4 — ✅ DONE (2026-10-17)
- `Scripts/CabinetSystem.cs` (güncellendi): `ScandalRecord` + `_scandals` sözlüğü; `CheckCabinetEvents(countryId)` günlük deterministik denetim — başarısızlık uyarısı (Skill<30 → stabilite -1), skandal tetikleme (Honesty<25 yolsuzluk/Loyalty<25 hıyar/Popularity<15 düşük popülerlik → stabilite -3), koalisyon baskısı (farklı partili bakan skandalında %5/gün → stabilite -1), istifa (şiddet seviyesine bağlı %2/%5/%10 günlük → görevden alma + stabilite -5). `CabinetScandalSave` DTO + `CountryCabinetSave.Scandals` Save/Load'a eklendi.
- `Scripts/GameManager.cs` (güncellendi): `DailyTick`'te `ApplyMinisterEffects`'ten sonra `CabinetSystem.CheckCabinetEvents(PlayerCountryId)` çağrılır.
- `Scripts/CabinetPanel.cs` (güncellendi): atanmış bakan kartında skandal/uyarı durumu kırmızı etiketle gösterilir; DISMISS butonu ile skandalı oluşturan bakan görevden alınır.
- `Scripts/SaveLoadTest.cs` (güncellendi): kabine özetine skandal sayısı eklendi.
- `dotnet build` 0 hata / 0 uyarı.
- Commit: 7159623 (push'ed origin gece-calisma).

## M21.5 — ✅ DONE (2026-10-17)
- `Scripts/GameManager.cs`: `RunCabinetMeetingTest()` eklendi (`--cabinet-meeting-test`) — meeting aksiyonu → slot 0, 1 gün ilerle, İçişleri bakanı sadakası +2 (clamp 0-100), PASS/FAIL raporu. `GetMinister` string (ministerId) döndürür → `Ministers.FirstOrDefault(m => m.Id == ministerId)` ile çözülür.
- `Scripts/TimeTempoSystem.cs`: `ApplyActionEffects`'te `"MinisterLoyaltyBonus"` case → `CabinetSystem.ApplyMinisterLoyaltyBonus(PlayerCountryId, (int)val)`.
- `Scripts/CabinetSystem.cs`: `MinisterStatSave` DTO + `MinisterStats[]` `CountryCabinetSave`'a; `ApplyMinisterLoyaltyBonus(countryId, bonus)` (Loyalty 0-100 clamp); `GetSaveData/LoadSaveData` bakan istatistikleri serialize eder.
- `Scripts/SaveLoadTest.cs`: bakan sadakası ortalaması özetine eklendi.
- `dotnet build`: 0 hata / 0 uyarı.
- Godot headless test çalıştırılamadı (editör yok), dotnet build 0/0; manuel editör testi önerilir.
- Commit: aa0efdf (push'ed origin gece-calisma).

## ADIM 1 — Headless test doğrulama (2026-10-17) — TÜMÜ PASS
- `dotnet build`: 0 hata / 0 uyarı
- Godot 4.7.2 console `--headless --path . --import --quit`: EXIT=0
- `--save-load-test --speed=5 --country=GER` (historical_1936): **SONUÇ: PASS ✓ (A == B)** — M12 Save/Load + M17 determinizm
- `--scenario=modern_world --save-load-test --speed=5 --country=DEU`: **SONUÇ: PASS ✓ (A == B)** — M20 Bütçe dahil tüm sistemler
- `--cabinet-meeting-test --speed=5 --country=GER`: **SONUÇ: PASS ✓** — İçişleri bakanı auto-atama, sadakati 78→80 (delta=2). GameManager.RunCabinetMeetingTest'te normalize lookup + auto-assign düzeltmesi yapıldı.
- `--scenario-test`: EXIT=0 — modern_world (50 ülke, 10 odak/10 olay/9 karar/54 tech/50 lider/4 faksiyon) + historical_1936 (50 ülke, 73 odak/11 olay/10 karar/73 tech/50 lider/3 faksiyon) doğru yüklendi.

## M17B — Uluslararası Örgütler tasarım belgesi — ✅ DONE
- `Design/M17B_uluslararasi_orgutler.md` yazıldı (17B.1–17B.6: veri modeli, üyelik kriterleri, ayrılma/askıya alma, toplantı/gündem, yükümlülük/avantaj, bağımlılıklar). Kod yazılmadı, yalnızca belge.

## M22.1 — Anayasa — ✅ DONE (2026-10-18)
- `Data/constitution.json` (yeni): 20 ülke — GovernmentType (Presidential/Parliamentary/SemiPresidential/Monarchy/OneParty), TermLength, TermLimit, VotingAge. Monarşi (SAU) dönem/yaş=0.
- `Scripts/ConstitutionSystem.cs` (yeni): LoadData, GetConstitution (varsayılan parlamentosal), SetEmergency/IsEmergencyDeclared/EmergencyStartDay, GetSaveData/LoadSaveData (olağanüstü hal state'i), GD.Print kayıt sayısı.
- `Scripts/GameManager.cs` (güncellendi): `_Ready'de ConstitutionSystem.LoadData()` çağrısı; `--constitution-test` kancağı (tüm tanıtımları doğrular).
- `dotnet build`: 0 hata / 0 uyarı.
- `--constitution-test --speed=0`: **20/20 PASS** (tüm ülke tanımları geçerli).

## M22.2 — Yasalar — ✅ DONE (2026-10-18)
- `Data/laws.json` (yeni): 8 yasa tanımı (Economy/Defense/Interior/Foreign/Social/Environment/Energy) — Id/Name/Category/CostPP/Description/Effects/OneTime.
- `Scripts/LawSystem.cs` (yeni): LawData/LawSave/CountryLawSave/LawEffectSave DTO'ları; LoadData (laws.json "Laws" dizisi); EnactLaw (PP ödeme + etki birikimi), RepealLaw (etki geri alma), GetLawEffect, GetActiveLaws/GetAvailableLaws; GetSaveData/LoadSaveData; CountryDatabase.Get (HOI4 alias GER→DEU normalize) kullanımı.
- `Scripts/GameManager.cs` (güncellendi): `_Ready'de LawSystem.LoadData()`; `ResetForNewGame'de LawSystem.ResetForNewGame()`; `--laws-test` kancağı + `RunLawsTest()` (tax_cut çıkarma/tekrar engelle/kaldırma/tekrar çıkarma, PP delta ve gdp_growth etkisi doğrulaması).
- `Scripts/SaveSystem.cs` (güncellendi): `SaveFile.Law` (LawSystem.LawSave) alanı; WriteSave'de `Law = LawSystem.GetSaveData()`; LoadGameInternal'de `LawSystem.LoadSaveData(save.Law)`.
- `Scripts/SaveLoadTest.cs` (güncellendi): BuildSummaryCore'ye "Yasalar: ülke=..., etkin_yasa=..., etki_göstergesi=..." satırı eklendi.
- `dotnet build`: 0 hata / 0 uyarı.
- `--laws-test --speed=0 --country=GER`: **SONUÇ: PASS ✓** (Tax Cut çıkarıldı PP 500→420, gdp_growth 0.20, duplicate reddedildi, repeal sonrası 0.00, tekrar çıkarıldı).
- `--save-load-test --speed=5 --country=GER --scenario=historical_1936`: **SONUÇ: PASS ✓ (A == B)** — LawSystem state kaydeder/yükler.

## Sonraki adım
- M22.3 Parlamento (partiler, sandalye dağılımı, oylama, veto, koalisyon, referandum).

## Bilinen sorunlar
- Komşuluk yalnızca ortak-kenar tabanlı; ülke sınırlarını aşan komşular kapsanmıyor.
- İl çizimi mevcut ülke çiziminin üzerine eklenir (katmanlı Polygon2D node'ları).
- M16: Bildirim kuyruğu (NEXT EVENT öğeleri) kayıtlara yazılmıyor — kayıt yüklenince bekleyen bildirimler kaybolur.
- M16: Ajanda efektleri (MinisterLoyaltyBonus vb.) ilgili sistemlere bağlı değil — loglanıyor.
- M21.5: headless `--cabinet-meeting-test` çalıştırıldı ve PASS (İçişleri bakanı auto-atama eklendi, sadakati +2 doğrulandı). Godot editörü gerek kalmadı.
