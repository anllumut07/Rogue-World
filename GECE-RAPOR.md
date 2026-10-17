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

## Sonraki adım
- M21.2: Bakan havuzu (kurgusal kişiler: yetenek, dürüstlük, sadakat, popülerlik, parti/ideoloji, özellik).
