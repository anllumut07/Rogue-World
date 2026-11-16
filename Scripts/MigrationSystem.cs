using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Göç ve Mülteci Krizleri Sistemi (M56).
/// - Savaş, sivil savaş, iklim zararı → mülteci akışları üretir
/// - Varış ülkeleri: istikrar/ekonomi/ideoloji etkileri alır
/// - Oyuncu: sınır politikası (Açık/Kontrollü/Kısıtlı/Kapalı), yardım paketleri
/// - Save/Load + DeterministicRNG
/// </summary>
public static class MigrationSystem
{
    // ===== Veri yapıları =====
    public class GlobalSettings
    {
        public double BaseRefugeePerCasualty { get; set; } = 0.3;
        public double BaseRefugeePerProvinceLost { get; set; } = 5000;
        public double ClimateRefugeePerWarmingDegree { get; set; } = 100000;
        public double ClimateRefugeePerTippingPoint { get; set; } = 500000;
        public int MaxRefugeesPerCountry { get; set; } = 5000000;
        public double AssimilationRatePerYear { get; set; } = 0.05;
        public double ReturnRatePerYear { get; set; } = 0.02;
    }

    public class BorderPolicy
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public double RefugeeAcceptanceMultiplier { get; set; }
        public double StabilityImpact { get; set; }
        public double GdpGrowthImpact { get; set; }
        public double IdeologyShiftLiberal { get; set; }
        public double IdeologyShiftConservative { get; set; }
        public int DiplomacyBonus { get; set; }
        public int CostPP { get; set; }
        public double MinWarming { get; set; }
    }

    public class CrisisType
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double BaseFlowPerCasualty { get; set; }
        public double BaseFlowPerWarmingDegree { get; set; }
        public double BaseFlowPerEvent { get; set; }
        public int DurationYears { get; set; }
    }

    public class AidPackage
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int CostPP { get; set; }
        public double CostBudgetPercent { get; set; }
        public int RefugeesSupported { get; set; }
        public double StabilityBonus { get; set; }
        public double AssimilationBoost { get; set; }
        public double GdpGrowthBonus { get; set; }
        public double ReturnRateBoost { get; set; }
        public int ReputationBonus { get; set; }
    }

    // ===== Çalışma durumu =====
    private static GlobalSettings _settings;
    private static readonly List<BorderPolicy> _borderPolicies = new();
    private static readonly List<CrisisType> _crisisTypes = new();
    private static readonly List<AidPackage> _aidPackages = new();

    // Ülke bazlı durum
    private static readonly Dictionary<string, CountryMigrationState> _countryStates = new();

    public class CountryMigrationState
    {
        public string CountryId { get; set; }
        public string BorderPolicyId { get; set; } = "controlled"; // Varsayılan
        public int RefugeePopulation { get; set; } = 0;
        public int AsylumSeekersPending { get; set; } = 0;
        public int AsylumGranted { get; set; } = 0;
        public int AsylumRejected { get; set; } = 0;
        public List<ActiveCrisis> ActiveCrises { get; set; } = new();
        public List<ActiveAidPackage> ActiveAidPackages { get; set; } = new();
        public int PolicyCooldown { get; set; } = 0;
        public double Reputation { get; set; } = 0; // Mülteci itibar puanı
    }

    public class ActiveCrisis
    {
        public string Id { get; set; } // Unique crisis ID
        public string TypeId { get; set; } // war, civil_war, climate, persecution
        public string SourceCountryId { get; set; }
        public string DestinationCountryId { get; set; }
        public int RefugeeCount { get; set; }
        public int YearStarted { get; set; }
        public int DurationYears { get; set; }
        public double Intensity { get; set; } // 0-1
    }

    public class ActiveAidPackage
    {
        public string PackageId { get; set; }
        public int YearStarted { get; set; }
        public int RefugeesCovered { get; set; }
    }

    // Mülteci akışları (ülke çiftleri arası)
    private static readonly Dictionary<string, MigrationFlow> _flows = new();

    public class MigrationFlow
    {
        public string SourceCountryId { get; set; }
        public string DestinationCountryId { get; set; }
        public int AnnualFlow { get; set; }
        public string PrimaryCause { get; set; } // war, civil_war, climate, persecution
        public int YearStarted { get; set; }
        public double Pressure { get; set; } // 0-1, baskı seviyesi
    }

    // Test modu
    private static bool _testMode = false;

    // İç gün sayacı (ClimateSystem gibi kendi sayacını kullanır — GameManager.TotalDays'e bağımlı değil)
    private static int _dayCounter = 0;

    // ===== Yükleme =====
    public static void LoadData()
    {
        try
        {
            string json = Godot.FileAccess.GetFileAsString("res://Data/migration.json");
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("GlobalSettings", out var gs))
            {
                _settings = JsonSerializer.Deserialize<GlobalSettings>(gs.GetRawText());
            }

            if (doc.RootElement.TryGetProperty("BorderPolicies", out var bpArr))
            {
                foreach (var item in bpArr.EnumerateArray())
                {
                    _borderPolicies.Add(JsonSerializer.Deserialize<BorderPolicy>(item.GetRawText()));
                }
            }

            if (doc.RootElement.TryGetProperty("CrisisTypes", out var ctArr))
            {
                foreach (var item in ctArr.EnumerateArray())
                {
                    _crisisTypes.Add(JsonSerializer.Deserialize<CrisisType>(item.GetRawText()));
                }
            }

            if (doc.RootElement.TryGetProperty("AidPackages", out var apArr))
            {
                foreach (var item in apArr.EnumerateArray())
                {
                    _aidPackages.Add(JsonSerializer.Deserialize<AidPackage>(item.GetRawText()));
                }
            }

            GD.Print($"[MigrationSystem] Yüklendi: {_borderPolicies.Count} sınır politikası, {_crisisTypes.Count} kriz türü, {_aidPackages.Count} yardım paketi");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[MigrationSystem] Veri yükleme HATA: {e.Message}");
        }
    }

    // ===== Başlatma / Sıfırlama =====
    public static void ResetForNewGame()
    {
        _countryStates.Clear();
        _flows.Clear();

        foreach (var c in CountryDatabase.All())
        {
            _countryStates[c.Id] = new CountryMigrationState
            {
                CountryId = c.Id,
                BorderPolicyId = "controlled"
            };
        }

        if (_testMode)
        {
            EnableTestMode();
        }

        _dayCounter = 0;
    }

    public static void EnableTestMode()
    {
        _testMode = true;
    }

    // ===== Yardımcı =====
    private static CountryMigrationState GetState(string countryId)
    {
        if (!_countryStates.TryGetValue(countryId, out var s))
        {
            s = new CountryMigrationState { CountryId = countryId, BorderPolicyId = "controlled" };
            _countryStates[countryId] = s;
        }
        return s;
    }

    public static BorderPolicy GetBorderPolicy(string policyId)
    {
        return _borderPolicies.FirstOrDefault(p => p.Id == policyId);
    }

    public static List<BorderPolicy> GetAllBorderPolicies()
    {
        return new List<BorderPolicy>(_borderPolicies);
    }

    public static List<CrisisType> GetAllCrisisTypes()
    {
        return new List<CrisisType>(_crisisTypes);
    }

    public static List<AidPackage> GetAllAidPackages()
    {
        return new List<AidPackage>(_aidPackages);
    }

    public static CountryMigrationState GetCountryState(string countryId)
    {
        return GetState(countryId);
    }

    // ===== Sınır Politikası Yönetimi =====
    public static bool CanSetBorderPolicy(string countryId, string policyId)
    {
        var state = GetState(countryId);
        var policy = GetBorderPolicy(policyId);
        if (policy == null || state.BorderPolicyId == policyId) return false;
        if (state.PolicyCooldown > 0) return false;

        var data = CountryDatabase.GetOrCreate(countryId, "");
        if (data.PoliticalPower < policy.CostPP) return false;

        // MinWarming kontrolü (kapalı sınırlar için)
        if (policy.MinWarming > 0)
        {
            double warming = ClimateSystem.GetGlobalWarming();
            if (warming < policy.MinWarming) return false;
        }

        return true;
    }

    public static bool SetBorderPolicy(string countryId, string policyId)
    {
        if (!CanSetBorderPolicy(countryId, policyId)) return false;

        var state = GetState(countryId);
        var policy = GetBorderPolicy(policyId);
        var data = CountryDatabase.GetOrCreate(countryId, "");

        data.PoliticalPower -= policy.CostPP;
        state.BorderPolicyId = policyId;
        state.PolicyCooldown = 365; // 1 yıl cooldown

        // İlişki bonusu/cezası
        if (policy.DiplomacyBonus != 0)
        {
            foreach (var other in CountryDatabase.All())
            {
                if (other.Id != countryId)
                {
                    DiplomacySystem.ModifyRelation(countryId, other.Id, policy.DiplomacyBonus);
                }
            }
        }

        GD.Print($"[MigrationSystem] {countryId} sınır politikasını değiştirdi: {policy.Name} (PP: -{policy.CostPP})");
        return true;
    }

    public static string GetCurrentBorderPolicy(string countryId)
    {
        return GetState(countryId).BorderPolicyId;
    }

    // ===== Mülteci Akışı Hesaplama =====
    /// <summary>
    /// Savaş zararından mülteci üretir (WarSystem'den çağrılır)
    /// </summary>
    public static void GenerateWarRefugees(string sourceCountryId, int casualties, int provincesLost)
    {
        if (casualties <= 0 && provincesLost <= 0) return;

        var sourceState = GetState(sourceCountryId);
        var crisisType = _crisisTypes.FirstOrDefault(c => c.Id == "war");
        if (crisisType == null) return;

        double refugees = casualties * _settings.BaseRefugeePerCasualty * crisisType.BaseFlowPerCasualty;
        refugees += provincesLost * _settings.BaseRefugeePerProvinceLost;

        if (refugees > 0)
        {
            CreateOrUpdateCrisis(sourceCountryId, "war", (int)refugees, crisisType.DurationYears);
            DistributeRefugees(sourceCountryId, (int)refugees, "war");
        }
    }

    /// <summary>
    /// Sivil savaş zararından mülteci üretir (CivilWarSystem'den çağrılır)
    /// </summary>
    public static void GenerateCivilWarRefugees(string sourceCountryId, int casualties, int provincesAffected)
    {
        if (casualties <= 0 && provincesAffected <= 0) return;

        var crisisType = _crisisTypes.FirstOrDefault(c => c.Id == "civil_war");
        if (crisisType == null) return;

        double refugees = casualties * _settings.BaseRefugeePerCasualty * crisisType.BaseFlowPerCasualty;
        refugees += provincesAffected * _settings.BaseRefugeePerProvinceLost * 0.5;

        if (refugees > 0)
        {
            CreateOrUpdateCrisis(sourceCountryId, "civil_war", (int)refugees, crisisType.DurationYears);
            DistributeRefugees(sourceCountryId, (int)refugees, "civil_war");
        }
    }

    /// <summary>
    /// İklim zararından mülteci üretir (ClimateSystem'den çağrılır)
    /// </summary>
    public static void GenerateClimateRefugees(string sourceCountryId, double warmingImpact, bool tippingPointTriggered)
    {
        if (warmingImpact <= 0 && !tippingPointTriggered) return;

        var crisisType = _crisisTypes.FirstOrDefault(c => c.Id == "climate");
        if (crisisType == null) return;

        double refugees = warmingImpact * _settings.ClimateRefugeePerWarmingDegree * crisisType.BaseFlowPerWarmingDegree;
        if (tippingPointTriggered)
        {
            refugees += _settings.ClimateRefugeePerTippingPoint;
        }

        if (refugees > 0)
        {
            CreateOrUpdateCrisis(sourceCountryId, "climate", (int)refugees, crisisType.DurationYears);
            DistributeRefugees(sourceCountryId, (int)refugees, "climate");
        }
    }

    private static void CreateOrUpdateCrisis(string sourceCountryId, string typeId, int refugeeCount, int durationYears)
    {
        var sourceState = GetState(sourceCountryId);
        string crisisId = $"{sourceCountryId}_{typeId}_{sourceState.ActiveCrises.Count}";

        var existing = sourceState.ActiveCrises.FirstOrDefault(c => c.TypeId == typeId && c.SourceCountryId == sourceCountryId);
        if (existing != null)
        {
            existing.RefugeeCount += refugeeCount;
            existing.Intensity = Math.Min(1.0, existing.Intensity + 0.2);
        }
        else
        {
            sourceState.ActiveCrises.Add(new ActiveCrisis
            {
                Id = crisisId,
                TypeId = typeId,
                SourceCountryId = sourceCountryId,
                DestinationCountryId = "", // Dağıtımda doldurulur
                RefugeeCount = refugeeCount,
                YearStarted = _dayCounter / 365,
                DurationYears = durationYears,
                Intensity = 0.5
            });
        }
    }

    private static void DistributeRefugees(string sourceCountryId, int totalRefugees, string cause)
    {
        if (totalRefugees <= 0) return;

        var sourceData = CountryDatabase.GetOrCreate(sourceCountryId, "");
        var neighbors = NeighborSystem.GetNeighbors(sourceCountryId); // Komşu ülkeler
        var allCountries = CountryDatabase.All().Where(c => c.Id != sourceCountryId).ToList();

        // Hedef ülkeleri seç: komşular öncelikli, sonra ideoloji/ekonomi/bağlantı
        var destinations = new List<(string CountryId, double Weight)>();

        foreach (var c in allCountries)
        {
            double weight = 0;

            // Komşuluk bonusu
            bool isNeighbor = neighbors.Contains(c.Id);
            if (isNeighbor) weight += 100;

            // Ekonomik çekim (GDP per capita)
            var destData = CountryDatabase.GetOrCreate(c.Id, "");
            double gdpPerCapita = destData.EstimatedGDP > 0 ? destData.EstimatedGDP / Math.Max(1, destData.Population) : 0;
            weight += gdpPerCapita * 0.01;

            // İstikrar (yüksek istikrar = çekim)
            weight += destData.StabilityPercent * 2;

            // İlişki pozitifse bonus
            double relation = DiplomacySystem.GetRelation(sourceCountryId, c.Id);
            if (relation > 0) weight += relation * 0.5;

            // Sınır politikası çarpanı
            var destState = GetState(c.Id);
            var policy = GetBorderPolicy(destState.BorderPolicyId);
            if (policy != null)
            {
                weight *= policy.RefugeeAcceptanceMultiplier;
            }

            // Maksimum mülteci kapasitesi kontrolü
            int capacity = _settings.MaxRefugeesPerCountry - destState.RefugeePopulation;
            if (capacity <= 0) weight = 0;

            if (weight > 0)
            {
                destinations.Add((c.Id, weight));
            }
        }

        if (destinations.Count == 0) return;

        // Ağırlıklı dağıtım
        double totalWeight = destinations.Sum(d => d.Weight);
        int remaining = totalRefugees;

        foreach (var (destId, weight) in destinations.OrderByDescending(d => d.Weight))
        {
            if (remaining <= 0) break;

            int share = (int)(totalRefugees * (weight / totalWeight));
            share = Math.Min(share, remaining);
            share = Math.Min(share, _settings.MaxRefugeesPerCountry - GetState(destId).RefugeePopulation);

            if (share > 0)
            {
                AddFlow(sourceCountryId, destId, share, cause);
                GetState(destId).RefugeePopulation += share;
                remaining -= share;
            }
        }

        // Kalan mülteçler (kapalı sınırlar vb. için) - kaynak ülke içinde yerleşmez, istatistik olarak kalır
        if (remaining > 0)
        {
            GetState(sourceCountryId).AsylumSeekersPending += remaining;
        }
    }

    private static void AddFlow(string sourceId, string destId, int count, string cause)
    {
        string key = $"{sourceId}->{destId}";
        if (_flows.TryGetValue(key, out var flow))
        {
            flow.AnnualFlow += count;
            flow.Pressure = Math.Min(1.0, flow.Pressure + 0.1);
        }
        else
        {
            _flows[key] = new MigrationFlow
            {
                SourceCountryId = sourceId,
                DestinationCountryId = destId,
                AnnualFlow = count,
                PrimaryCause = cause,
                YearStarted = _dayCounter / 365,
                Pressure = 0.3
            };
        }
    }

    // ===== Yardım Paketleri =====
    public static bool CanActivateAidPackage(string countryId, string packageId)
    {
        var state = GetState(countryId);
        var pkg = _aidPackages.FirstOrDefault(p => p.Id == packageId);
        if (pkg == null || state.ActiveAidPackages.Any(a => a.PackageId == packageId)) return false;

        var data = CountryDatabase.GetOrCreate(countryId, "");
        if (data.PoliticalPower < pkg.CostPP) return false;

        return true;
    }

    public static bool ActivateAidPackage(string countryId, string packageId)
    {
        if (!CanActivateAidPackage(countryId, packageId)) return false;

        var state = GetState(countryId);
        var pkg = _aidPackages.FirstOrDefault(p => p.Id == packageId);
        var data = CountryDatabase.GetOrCreate(countryId, "");

        data.PoliticalPower -= pkg.CostPP;
        state.ActiveAidPackages.Add(new ActiveAidPackage
        {
            PackageId = packageId,
            YearStarted = _dayCounter / 365,
            RefugeesCovered = pkg.RefugeesSupported
        });
        state.Reputation += pkg.ReputationBonus;

        GD.Print($"[MigrationSystem] {countryId} yardım paketi aktif etti: {pkg.Name} (PP: -{pkg.CostPP})");
        return true;
    }

    // ===== Günlük Tick =====
    public static void DailyTick()
    {
        _dayCounter++;
        int currentYear = _dayCounter / 365;

        foreach (var kv in _countryStates)
        {
            var state = kv.Value;
            var data = CountryDatabase.GetOrCreate(state.CountryId, "");

            // Politika cooldown azalt
            if (state.PolicyCooldown > 0)
                state.PolicyCooldown--;

            // Aktif krizleri güncelle
            UpdateActiveCrises(state, currentYear);

            // Mülteci nüfusunu işle (asimilasyon, dönüş, etkiler)
            ProcessRefugeePopulation(state, data);

            // Aktif yardım paketlerini işle
            ProcessActiveAidPackages(state, data);

            // Akış baskılarını azalt (yıllık)
            if (_dayCounter % 365 == 0)
            {
                DecayFlowPressures();
            }
        }
    }

    private static void UpdateActiveCrises(CountryMigrationState state, int currentYear)
    {
        var toRemove = new List<ActiveCrisis>();

        foreach (var crisis in state.ActiveCrises)
        {
            int age = currentYear - crisis.YearStarted;
            if (age >= crisis.DurationYears)
            {
                // Kriz bitti - mülteçler yerleşir veya döner
                int remaining = crisis.RefugeeCount;
                // %70 yerleşir, %30 döner (basitleştirilmiş)
                int settled = (int)(remaining * 0.7);
                int returned = remaining - settled;

                // Yerleşenler nüfusa eklenir (asimilasyon süreci başlar)
                // Dönenler kaynak ülkeye geri gider (eğer güvenliyse)
                toRemove.Add(crisis);
            }
            else
            {
                // Kriz devam ediyor - yıllık yeni mülteci ekle
                if (_dayCounter % 365 == 0)
                {
                    double yearlyFlow = crisis.RefugeeCount * crisis.Intensity / crisis.DurationYears;
                    if (yearlyFlow > 100)
                    {
                        DistributeRefugees(crisis.SourceCountryId, (int)yearlyFlow, crisis.TypeId);
                    }
                }
            }
        }

        foreach (var c in toRemove)
        {
            state.ActiveCrises.Remove(c);
        }
    }

    private static void ProcessRefugeePopulation(CountryMigrationState state, CountryData data)
    {
        if (state.RefugeePopulation <= 0) return;

        // Asimilasyon (yıllık)
        if (_dayCounter % 365 == 0)
        {
            int assimilated = (int)(state.RefugeePopulation * _settings.AssimilationRatePerYear);
            state.RefugeePopulation -= assimilated;
            // Asimile olanlar nüfusa katılır, işgücüne katkı sağlar
            data.Population += assimilated;
            data.ManpowerPool += assimilated / 10; // Kabaca

            // İade (dönüş)
            int returned = (int)(state.RefugeePopulation * _settings.ReturnRatePerYear);
            state.RefugeePopulation -= returned;
        }

        // Mülteci nüfusunun etkileri (günlük)
        double refugeeRatio = (double)state.RefugeePopulation / Math.Max(1, data.Population);

        // İstikrar etkisi: mülteci oranı yüksekse istikrar düşer
        double stabilityImpact = -refugeeRatio * 50.0; // Her %1 mülteci için -0.5 istikrar
        StatisticsSystem.SetValue(state.CountryId, "stability",
            Math.Clamp(StatisticsSystem.GetValue(state.CountryId, "stability") + stabilityImpact / 365.0, 0, 100));

        // Ekonomi etkisi: başlangıçta maliyet, uzun vadede işgücü katkısı
        double gdpImpact = -refugeeRatio * 100.0; // Her %1 için -1% GSYİH büyümesi
        if (state.RefugeePopulation > data.Population * 0.05) // %5'in üzerindeyse
        {
            gdpImpact += refugeeRatio * 50.0; // İşgücü katkısı
        }
        StatisticsSystem.SetValue(state.CountryId, "gdp_growth",
            StatisticsSystem.GetValue(state.CountryId, "gdp_growth") + gdpImpact / 365.0);

        // İdeoloji etkisi: mülteci kabul eden ülkelerde liberal kayma
        var policy = GetBorderPolicy(state.BorderPolicyId);
        if (policy != null && policy.RefugeeAcceptanceMultiplier > 0.5)
        {
            // Liberal kayma (basitleştirilmiş - gerçek implementasyonda ideoloji sistemi ile)
        }

        // İşsizlik etkisi
        double unemploymentImpact = refugeeRatio * 200.0;
        StatisticsSystem.SetValue(state.CountryId, "unemployment",
            Math.Clamp(StatisticsSystem.GetValue(state.CountryId, "unemployment") + unemploymentImpact / 365.0, 0, 100));
    }

    private static void ProcessActiveAidPackages(CountryMigrationState state, CountryData data)
    {
        var toRemove = new List<ActiveAidPackage>();

        foreach (var aid in state.ActiveAidPackages)
        {
            var pkg = _aidPackages.FirstOrDefault(p => p.Id == aid.PackageId);
            if (pkg == null) continue;

            // Yardım paketi etkileri (yıllık)
            if (_dayCounter % 365 == 0)
            {
                int covered = Math.Min(aid.RefugeesCovered, state.RefugeePopulation);
                state.RefugeePopulation -= covered;

                if (pkg.StabilityBonus > 0)
                {
                    StatisticsSystem.SetValue(state.CountryId, "stability",
                        Math.Clamp(StatisticsSystem.GetValue(state.CountryId, "stability") + pkg.StabilityBonus, 0, 100));
                }
                if (pkg.GdpGrowthBonus > 0)
                {
                    StatisticsSystem.SetValue(state.CountryId, "gdp_growth",
                        StatisticsSystem.GetValue(state.CountryId, "gdp_growth") + pkg.GdpGrowthBonus);
                }
            }

            // Paket 3 yıl sonra sona erer
            int currentYear = _dayCounter / 365;
            if (currentYear - aid.YearStarted >= 3)
            {
                toRemove.Add(aid);
            }
        }

        foreach (var a in toRemove)
        {
            state.ActiveAidPackages.Remove(a);
        }
    }

    private static void DecayFlowPressures()
    {
        foreach (var flow in _flows.Values)
        {
            flow.Pressure = Math.Max(0, flow.Pressure - 0.1);
            flow.AnnualFlow = (int)(flow.AnnualFlow * 0.9); // Yıllık %10 azalma
        }

        // Sıfır akışları temizle
        var toRemove = _flows.Where(f => f.Value.AnnualFlow <= 0).Select(f => f.Key).ToList();
        foreach (var key in toRemove)
        {
            _flows.Remove(key);
        }
    }

    // ===== AI Kararları =====
    public static void AiMigrationDecisions(string countryId)
    {
        if (_testMode) return;

        var state = GetState(countryId);
        var data = CountryDatabase.GetOrCreate(countryId, "");
        double pressure = (double)state.RefugeePopulation / Math.Max(1, data.Population);

        // Sınır politikası değiştirme
        if (state.PolicyCooldown <= 0 && DeterministicRNG.NextDouble() < 0.01) // Yılda %1 şans
        {
            var currentPolicy = GetBorderPolicy(state.BorderPolicyId);
            var available = _borderPolicies.Where(p => p.Id != state.BorderPolicyId && CanSetBorderPolicy(countryId, p.Id)).ToList();

            if (available.Count > 0)
            {
                // Basit mantık: baskı yüksekse daha kapalı, düşükse daha açık
                BorderPolicy chosen;
                if (pressure > 0.05) // %5'in üzerinde mülteci
                {
                    chosen = available.OrderBy(p => p.RefugeeAcceptanceMultiplier).First(); // Daha kapalı
                }
                else if (pressure < 0.01 && data.PoliticalPower > 100)
                {
                    chosen = available.OrderByDescending(p => p.RefugeeAcceptanceMultiplier).First(); // Daha açık
                }
                else
                {
                    chosen = available[DeterministicRNG.NextInt(0, available.Count)];
                }

                SetBorderPolicy(countryId, chosen.Id);
            }
        }

        // Yardım paketi aktif etme
        if (data.PoliticalPower > 150 && state.RefugeePopulation > 10000 && DeterministicRNG.NextDouble() < 0.02)
        {
            var availableAid = _aidPackages.Where(p => !state.ActiveAidPackages.Any(a => a.PackageId == p.Id) && CanActivateAidPackage(countryId, p.Id)).ToList();
            if (availableAid.Count > 0)
            {
                var pkg = availableAid[DeterministicRNG.NextInt(0, availableAid.Count)];
                ActivateAidPackage(countryId, pkg.Id);
            }
        }
    }

    // ===== Bilgi Getirme =====
    public static List<MigrationFlow> GetAllFlows()
    {
        return _flows.Values.OrderByDescending(f => f.AnnualFlow).ToList();
    }

    public static List<MigrationFlow> GetFlowsForCountry(string countryId, bool asSource)
    {
        if (asSource)
            return _flows.Values.Where(f => f.SourceCountryId == countryId).OrderByDescending(f => f.AnnualFlow).ToList();
        else
            return _flows.Values.Where(f => f.DestinationCountryId == countryId).OrderByDescending(f => f.AnnualFlow).ToList();
    }

    public static int GetTotalRefugees(string countryId)
    {
        return GetState(countryId).RefugeePopulation;
    }

    public static int GetTotalGlobalRefugees()
    {
        return _countryStates.Values.Sum(s => s.RefugeePopulation);
    }

    public static int GetTotalAsylumPending(string countryId)
    {
        return GetState(countryId).AsylumSeekersPending;
    }

    // Panel için yardımcı metodlar
    public static List<ActiveCrisis> GetAllActiveCrises()
    {
        var result = new List<ActiveCrisis>();
        foreach (var state in _countryStates.Values)
        {
            result.AddRange(state.ActiveCrises);
        }
        return result;
    }

    public static int GetAllCrisesCount()
    {
        return _countryStates.Values.Sum(s => s.ActiveCrises.Count);
    }

    // ===== Save/Load =====
    public class MigrationSave
    {
        public CountryMigrationSave[] Countries { get; set; }
        public MigrationFlowSave[] Flows { get; set; }
        public int DayCounter { get; set; }
    }

    public class CountryMigrationSave
    {
        public string CountryId { get; set; }
        public string BorderPolicyId { get; set; }
        public int RefugeePopulation { get; set; }
        public int AsylumSeekersPending { get; set; }
        public int AsylumGranted { get; set; }
        public int AsylumRejected { get; set; }
        public int PolicyCooldown { get; set; }
        public double Reputation { get; set; }
        public ActiveCrisisSave[] ActiveCrises { get; set; }
        public ActiveAidPackageSave[] ActiveAidPackages { get; set; }
    }

    public class ActiveCrisisSave
    {
        public string Id { get; set; }
        public string TypeId { get; set; }
        public string SourceCountryId { get; set; }
        public string DestinationCountryId { get; set; }
        public int RefugeeCount { get; set; }
        public int YearStarted { get; set; }
        public int DurationYears { get; set; }
        public double Intensity { get; set; }
    }

    public class ActiveAidPackageSave
    {
        public string PackageId { get; set; }
        public int YearStarted { get; set; }
        public int RefugeesCovered { get; set; }
    }

    public class MigrationFlowSave
    {
        public string SourceCountryId { get; set; }
        public string DestinationCountryId { get; set; }
        public int AnnualFlow { get; set; }
        public string PrimaryCause { get; set; }
        public int YearStarted { get; set; }
        public double Pressure { get; set; }
    }

    public static MigrationSave GetSaveData()
    {
        return new MigrationSave
        {
            Countries = _countryStates.Select(kv => new CountryMigrationSave
            {
                CountryId = kv.Key,
                BorderPolicyId = kv.Value.BorderPolicyId,
                RefugeePopulation = kv.Value.RefugeePopulation,
                AsylumSeekersPending = kv.Value.AsylumSeekersPending,
                AsylumGranted = kv.Value.AsylumGranted,
                AsylumRejected = kv.Value.AsylumRejected,
                PolicyCooldown = kv.Value.PolicyCooldown,
                Reputation = kv.Value.Reputation,
                ActiveCrises = kv.Value.ActiveCrises.Select(c => new ActiveCrisisSave
                {
                    Id = c.Id,
                    TypeId = c.TypeId,
                    SourceCountryId = c.SourceCountryId,
                    DestinationCountryId = c.DestinationCountryId,
                    RefugeeCount = c.RefugeeCount,
                    YearStarted = c.YearStarted,
                    DurationYears = c.DurationYears,
                    Intensity = c.Intensity
                }).ToArray(),
                ActiveAidPackages = kv.Value.ActiveAidPackages.Select(a => new ActiveAidPackageSave
                {
                    PackageId = a.PackageId,
                    YearStarted = a.YearStarted,
                    RefugeesCovered = a.RefugeesCovered
                }).ToArray()
            }).ToArray(),
            Flows = _flows.Select(kv => new MigrationFlowSave
            {
                SourceCountryId = kv.Value.SourceCountryId,
                DestinationCountryId = kv.Value.DestinationCountryId,
                AnnualFlow = kv.Value.AnnualFlow,
                PrimaryCause = kv.Value.PrimaryCause,
                YearStarted = kv.Value.YearStarted,
                Pressure = kv.Value.Pressure
            }).ToArray(),
            DayCounter = _dayCounter
        };
    }

    public static void LoadSaveData(MigrationSave save)
    {
        if (save == null) return;

        _countryStates.Clear();
        if (save.Countries != null)
        {
            foreach (var cs in save.Countries)
            {
                _countryStates[cs.CountryId] = new CountryMigrationState
                {
                    CountryId = cs.CountryId,
                    BorderPolicyId = cs.BorderPolicyId,
                    RefugeePopulation = cs.RefugeePopulation,
                    AsylumSeekersPending = cs.AsylumSeekersPending,
                    AsylumGranted = cs.AsylumGranted,
                    AsylumRejected = cs.AsylumRejected,
                    PolicyCooldown = cs.PolicyCooldown,
                    Reputation = cs.Reputation,
                    ActiveCrises = cs.ActiveCrises?.Select(c => new ActiveCrisis
                    {
                        Id = c.Id,
                        TypeId = c.TypeId,
                        SourceCountryId = c.SourceCountryId,
                        DestinationCountryId = c.DestinationCountryId,
                        RefugeeCount = c.RefugeeCount,
                        YearStarted = c.YearStarted,
                        DurationYears = c.DurationYears,
                        Intensity = c.Intensity
                    }).ToList() ?? new(),
                    ActiveAidPackages = cs.ActiveAidPackages?.Select(a => new ActiveAidPackage
                    {
                        PackageId = a.PackageId,
                        YearStarted = a.YearStarted,
                        RefugeesCovered = a.RefugeesCovered
                    }).ToList() ?? new()
                };
            }
        }

        _flows.Clear();
        if (save.Flows != null)
        {
            foreach (var fs in save.Flows)
            {
                string key = $"{fs.SourceCountryId}->{fs.DestinationCountryId}";
                _flows[key] = new MigrationFlow
                {
                    SourceCountryId = fs.SourceCountryId,
                    DestinationCountryId = fs.DestinationCountryId,
                    AnnualFlow = fs.AnnualFlow,
                    PrimaryCause = fs.PrimaryCause,
                    YearStarted = fs.YearStarted,
                    Pressure = fs.Pressure
                };
            }
        }

        _dayCounter = save.DayCounter;

        GD.Print($"[MigrationSystem] Loaded: {_countryStates.Count} countries, {_flows.Count} flows, Total refugees: {_countryStates.Values.Sum(s => s.RefugeePopulation)}");
    }
}