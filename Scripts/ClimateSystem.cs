using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// İklim, Çevre ve Enerji Geçişi Sistemi (M55).
/// - Ülke emisyon profilleri (enerji/endüstri/ulaşım/tarım/arazi kullanımı)
/// - Küresel karbon birikimi ve sıcaklık anomali (deterministik)
/// - İklim etkileri: sağlık, gdp_growth, environment, enerji, tarım göstergeleri
/// - Tipping point kontrolü (permafrost, buzuller, Amazon, metan hidratları)
/// - Çevresel politikalar (karbon vergisi, yenilenebilir teşvik, vb.)
/// - Uluslararası iklim anlaşmaları (Paris, Glasgow, Kigali)
/// - AI politika seçimi (ideoloji/ekonomi/basınç)
/// - Save/Load + DailyTick entegrasyonu
/// </summary>
public static class ClimateSystem
{
    // ===== Veri yapıları =====
    public class GlobalSettings
    {
        public double PreIndustrialCO2 { get; set; } = 280.0;
        public double CurrentCO2 { get; set; } = 420.0;
        public double CarbonBudgetGtCO2 { get; set; } = 1000.0;
        public double WarmingPerDoubling { get; set; } = 3.0; // ECS: Equilibrium Climate Sensitivity
        public double WarmingPerYearBase { get; set; } = 0.02;
        public List<TippingPoint> TippingPoints { get; set; } = new();
        public int CopFrequencyYears { get; set; } = 1;
        public double BaseAdaptationCost { get; set; } = 50.0;
    }

    public class TippingPoint
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double ThresholdCO2 { get; set; }
        public double ThresholdWarming { get; set; }
        public double ExtraWarming { get; set; }
        public bool Triggered { get; set; } = false;
    }

    public class CountryEmissionProfile
    {
        public string CountryId { get; set; }
        public double EnergyShare { get; set; }
        public double IndustryShare { get; set; }
        public double TransportShare { get; set; }
        public double AgricultureShare { get; set; }
        public double LandUseShare { get; set; }
        public double RenewableShare { get; set; }
        public double NuclearShare { get; set; }
        public double CoalShare { get; set; }
        public double OilShare { get; set; }
        public double GasShare { get; set; }
    }

    public class Policy
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public int CostPP { get; set; }
        public double CostBudgetPercent { get; set; }
        public double EmissionReduction { get; set; }
        public double RenewableBoost { get; set; }
        public double NuclearBoost { get; set; }
        public double EnergyCostIncrease { get; set; }
        public double GdpGrowthImpact { get; set; }
        public int PopularityImpact { get; set; }
        public double LandUseImprovement { get; set; }
        public double AdaptationBoost { get; set; }
        public string UnlockTech { get; set; }
        public double MinWarming { get; set; }
    }

    public class Agreement
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double TargetWarmingLimit { get; set; }
        public int ReviewCycleYears { get; set; }
        public bool TransparencyRequirement { get; set; }
        public int MinParticipants { get; set; }
        public int PrestigeBonus { get; set; }
        public double ComplianceBonus { get; set; }
    }

    // Yıllık iklim anlık görüntüsü (grafik geçmişi için)
    public class YearlyClimateSnapshot
    {
        public int Year { get; set; }
        public double CO2 { get; set; }
        public double Warming { get; set; }
        public double AnnualEmissionsGt { get; set; }
        public double CumulativeEmissionsGt { get; set; }
    }

    // ===== Çalışma durumu =====
    private static GlobalSettings _settings;
    private static readonly Dictionary<string, CountryEmissionProfile> _profiles = new();
    private static CountryEmissionProfile _defaultProfile;
    private static readonly List<Policy> _policies = new();
    private static readonly List<Agreement> _agreements = new();

    // Küresel durum
    private static double _globalCO2 = 420.0; // ppm
    private static double _globalWarming = 1.2; // °C above pre-industrial
    private static double _cumulativeEmissionsGt = 0.0; // GtCO2 since pre-industrial
    private static double _annualEmissionsGt = 0.0; // GtCO2/year current
    private static int _yearCounter = 0;
    private static int _nextCopYear = 0;

    // Geçmiş veriler (yıllık anlık görüntüler) - grafikler için
    private static readonly List<YearlyClimateSnapshot> _history = new();
    private const int MaxHistoryYears = 200; // Maksimum 200 yıl geçmişi tut

    // Ülke bazlı durum
    private static readonly Dictionary<string, CountryClimateState> _countryStates = new();

    public class CountryClimateState
    {
        public string CountryId { get; set; }
        public double AnnualEmissionsMt { get; set; } // MtCO2/year
        public double RenewableShare { get; set; }
        public double NuclearShare { get; set; }
        public double CoalShare { get; set; }
        public double OilShare { get; set; }
        public double GasShare { get; set; }
        public List<string> ActivePolicies { get; set; } = new();
        public List<string> SignedAgreements { get; set; } = new();
        public double AdaptationLevel { get; set; } = 0.0; // 0-1
        public int PolicyCooldown { get; set; } = 0; // days
        public double LastEmissionsMt { get; set; } // for trend
    }

    // Test modu
    // private static bool _testMode = false; // Şu an kullanılmıyor, test modu GlobalSettings üzerinden kontrol edilebilir

    // ===== Yükleme =====
    public static void LoadData()
    {
        try
        {
            string json = Godot.FileAccess.GetFileAsString("res://Data/climate.json");
            using var doc = JsonDocument.Parse(json);

            // Global settings
            if (doc.RootElement.TryGetProperty("GlobalSettings", out var gs))
            {
                _settings = JsonSerializer.Deserialize<GlobalSettings>(gs.GetRawText());
                _globalCO2 = _settings.CurrentCO2;
                // Warming from CO2: ΔT = ECS * log2(CO2 / CO2_preindustrial)
                _globalWarming = _settings.WarmingPerDoubling * Math.Log(_globalCO2 / _settings.PreIndustrialCO2, 2);
                _nextCopYear = (int)(GameManager.Instance?.TotalDays / 365) + _settings.CopFrequencyYears;
            }

            // Country emission profiles
            if (doc.RootElement.TryGetProperty("CountryEmissionProfiles", out var cepArr))
            {
                foreach (var item in cepArr.EnumerateArray())
                {
                    var profile = JsonSerializer.Deserialize<CountryEmissionProfile>(item.GetRawText());
                    _profiles[profile.CountryId] = profile;
                }
            }

            // Default profile
            if (doc.RootElement.TryGetProperty("DefaultEmissionProfile", out var dep))
            {
                _defaultProfile = JsonSerializer.Deserialize<CountryEmissionProfile>(dep.GetRawText());
            }

            // Policies
            if (doc.RootElement.TryGetProperty("Policies", out var polArr))
            {
                foreach (var item in polArr.EnumerateArray())
                {
                    _policies.Add(JsonSerializer.Deserialize<Policy>(item.GetRawText()));
                }
            }

            // Agreements
            if (doc.RootElement.TryGetProperty("Agreements", out var agrArr))
            {
                foreach (var item in agrArr.EnumerateArray())
                {
                    _agreements.Add(JsonSerializer.Deserialize<Agreement>(item.GetRawText()));
                }
            }

            GD.Print($"[ClimateSystem] Yüklendi: {_profiles.Count} ülke profili, {_policies.Count} politika, {_agreements.Count} anlaşma. Global CO2: {_globalCO2:F1} ppm, Isınma: {_globalWarming:F2}°C");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ClimateSystem] Veri yükleme HATA: {e.Message}");
        }
    }

    // ===== Başlatma / Sıfırlama =====
    public static void ResetForNewGame()
    {
        _globalCO2 = _settings?.CurrentCO2 ?? 420.0;
        _globalWarming = _settings?.WarmingPerDoubling * Math.Log(_globalCO2 / (_settings?.PreIndustrialCO2 ?? 280.0), 2) ?? 1.2;
        _cumulativeEmissionsGt = 0.0;
        _annualEmissionsGt = 0.0;
        _yearCounter = 0;
        _nextCopYear = _settings?.CopFrequencyYears ?? 1;
        _countryStates.Clear();

        // Tipping point'leri sıfırla
        if (_settings?.TippingPoints != null)
        {
            foreach (var tp in _settings.TippingPoints)
                tp.Triggered = false;
        }

        // Her ülke için başlangıç durumu
        foreach (var c in CountryDatabase.All())
        {
            var profile = GetProfile(c.Id);
            _countryStates[c.Id] = new CountryClimateState
            {
                CountryId = c.Id,
                RenewableShare = profile.RenewableShare,
                NuclearShare = profile.NuclearShare,
                CoalShare = profile.CoalShare,
                OilShare = profile.OilShare,
                GasShare = profile.GasShare,
                AdaptationLevel = 0.0
            };
        }

        // İlk emisyon hesapla
        CalculateGlobalEmissions();
    }

    // ===== Yardımcı =====
    private static CountryEmissionProfile GetProfile(string countryId)
    {
        if (_profiles.TryGetValue(countryId, out var p)) return p;
        return _defaultProfile ?? new CountryEmissionProfile
        {
            EnergyShare = 0.35, IndustryShare = 0.25, TransportShare = 0.20,
            AgricultureShare = 0.10, LandUseShare = 0.10,
            RenewableShare = 0.15, NuclearShare = 0.05,
            CoalShare = 0.30, OilShare = 0.40, GasShare = 0.30
        };
    }

    private static CountryClimateState GetState(string countryId)
    {
        if (!_countryStates.TryGetValue(countryId, out var s))
        {
            var profile = GetProfile(countryId);
            s = new CountryClimateState
            {
                CountryId = countryId,
                RenewableShare = profile.RenewableShare,
                NuclearShare = profile.NuclearShare,
                CoalShare = profile.CoalShare,
                OilShare = profile.OilShare,
                GasShare = profile.GasShare
            };
            _countryStates[countryId] = s;
        }
        return s;
    }

    // ===== Emisyon Hesaplaması =====
    /// <summary>
    /// Tüm ülkelerin yıllık emisyonlarını hesaplar ve küresel toplamı günceller.
    /// MtCO2/year cinsinden.
    /// </summary>
    public static void CalculateGlobalEmissions()
    {
        double totalMt = 0.0;

        foreach (var c in CountryDatabase.All())
        {
            var state = GetState(c.Id);
            var profile = GetProfile(c.Id);
            var data = CountryDatabase.GetOrCreate(c.Id, "");

            // Temel emisyon: GDP ve endüstri yapısından
            // GDP ~ $1000B = ~5000 MtCO2/year (yaklaşık)
            double gdpFactor = Math.Max(0.1, data.EstimatedGDP / 10000.0); // Normalize
            double industryFactor = data.MilFactories / 50.0 + data.CivFactories / 100.0;
            double energyFactor = data.CivFactories / 200.0; // Sivil fabrikalar → enerji tüketimi

            double baseEmissions = (gdpFactor * 3000.0 + industryFactor * 2000.0 + energyFactor * 1500.0); // MtCO2

            // Sektörel paylar
            double emissions = baseEmissions * (
                profile.EnergyShare * (state.CoalShare * 1.0 + state.OilShare * 0.7 + state.GasShare * 0.5 + state.RenewableShare * 0.02 + state.NuclearShare * 0.01) +
                profile.IndustryShare * 0.8 +
                profile.TransportShare * (state.OilShare * 0.9 + state.GasShare * 0.3 + state.RenewableShare * 0.1) +
                profile.AgricultureShare * 0.5 +
                profile.LandUseShare * (1.0 - state.RenewableShare * 0.5) // Orman kaybı
            );

            // Aktif politikalarından düşüş
            double policyReduction = 0.0;
            foreach (var pid in state.ActivePolicies)
            {
                var policy = _policies.FirstOrDefault(p => p.Id == pid);
                if (policy != null)
                    policyReduction += policy.EmissionReduction;
            }
            policyReduction = Math.Min(policyReduction, 0.8); // Max %80

            // Uyumluluk bonusu (anlaşmalara katılım)
            double complianceBonus = 0.0;
            foreach (var aid in state.SignedAgreements)
            {
                var agr = _agreements.FirstOrDefault(a => a.Id == aid);
                if (agr != null)
                    complianceBonus += agr.ComplianceBonus;
            }
            complianceBonus = Math.Min(complianceBonus, 0.15);

            double finalEmissions = emissions * (1.0 - policyReduction - complianceBonus);
            finalEmissions = Math.Max(finalEmissions, baseEmissions * 0.1); // Floor

            state.AnnualEmissionsMt = finalEmissions;
            state.LastEmissionsMt = finalEmissions;
            totalMt += finalEmissions;
        }

        _annualEmissionsGt = totalMt / 1000.0; // GtCO2/year
        _cumulativeEmissionsGt += _annualEmissionsGt / 365.0; // Günlük birikim (günlük tick'te çağrılır)

        // CO2 konsantrasyon güncelleme: ~2.12 GtCO2 = 1 ppm
        double ppmIncrease = (_annualEmissionsGt / 2.12) / 365.0; // Günlük ppm artışı
        _globalCO2 += ppmIncrease;

        // Isınma güncelleme (basitleştirilmiş: transient climate response)
        double targetWarming = _settings.WarmingPerDoubling * Math.Log(_globalCO2 / _settings.PreIndustrialCO2, 2);
        // Isınma gecikmesi (ocean thermal inertia): yıllık %10 kapanış
        _globalWarming += (targetWarming - _globalWarming) * 0.00027; // ~10%/yıl

        // Tipping point kontrolü
        CheckTippingPoints();
    }

    private static void CheckTippingPoints()
    {
        if (_settings?.TippingPoints == null) return;

        foreach (var tp in _settings.TippingPoints)
        {
            if (tp.Triggered) continue;
            if (_globalCO2 >= tp.ThresholdCO2 || _globalWarming >= tp.ThresholdWarming)
            {
                tp.Triggered = true;
                _globalWarming += tp.ExtraWarming; // Ek ısınma
                GD.Print($"[ClimateSystem] TIPPING POINT TETİKLENDİ: {tp.Name} — Ek ısınma: +{tp.ExtraWarming:F2}°C, Toplam: {_globalWarming:F2}°C");
            }
        }
    }

    // ===== İklim Etkilerini Göstergelere Uygula =====
    public static void ApplyClimateImpacts()
    {
        // Küresel ısınma seviyesine göre etki çarpanları
        double warming = _globalWarming;

        // Etki eğrileri (IPCC benzeri)
        double healthImpact = -warming * 2.0; // °C başına -2 sağlık puanı
        double gdpImpact = -warming * warming * 0.5; // Karesel: 1°C=-0.5%, 2°C=-2%, 3°C=-4.5%
        double envImpact = -warming * 5.0; // Çevre kalitesi
        double energyImpact = warming * 1.5; // Soğutma talebi artışı → enerji stresi (pozitif = kötü)
        double agricultureImpact = -warming * 3.0; // Tarım verimi

        // Tipping point'ler ekstra etki
        if (_settings?.TippingPoints != null)
        {
            foreach (var tp in _settings.TippingPoints)
            {
                if (tp.Triggered)
                {
                    healthImpact -= 1.0;
                    gdpImpact -= 0.5;
                    envImpact -= 3.0;
                    agricultureImpact -= 2.0;
                }
            }
        }

        // Her ülkeye uygula (coğrafi konuma göre değişebilir - basitleştirilmiş)
        foreach (var c in CountryDatabase.All())
        {
            var state = GetState(c.Id);

            // Uyumluluk azaltır etkiyi
            double adaptationFactor = 1.0 - state.AdaptationLevel * 0.7; // Max %70 azaltma

            // Sağlık
            double healthDelta = healthImpact * adaptationFactor / 365.0; // Günlük
            StatisticsSystem.SetValue(c.Id, "health", StatisticsSystem.GetValue(c.Id, "health") + healthDelta);

            // GDP Growth
            double gdpDelta = gdpImpact * adaptationFactor / 365.0;
            StatisticsSystem.SetValue(c.Id, "gdp_growth", StatisticsSystem.GetValue(c.Id, "gdp_growth") + gdpDelta);

            // Environment
            double envDelta = envImpact * adaptationFactor / 365.0;
            StatisticsSystem.SetValue(c.Id, "environment", StatisticsSystem.GetValue(c.Id, "environment") + envDelta);

            // Energy (stres = negatif etki)
            double energyDelta = -energyImpact * adaptationFactor / 365.0;
            StatisticsSystem.SetValue(c.Id, "energy", StatisticsSystem.GetValue(c.Id, "energy") + energyDelta);

            // Not: Agriculture göstergesi yok, ama gdp_growth üzerinden dolaylı etki var
        }
    }

    // ===== Politika Yönetimi =====
    public static bool CanEnactPolicy(string countryId, string policyId)
    {
        var state = GetState(countryId);
        var policy = _policies.FirstOrDefault(p => p.Id == policyId);
        if (policy == null) return false;
        if (state.ActivePolicies.Contains(policyId)) return false;
        if (state.PolicyCooldown > 0) return false;
        if (_globalWarming < policy.MinWarming) return false; // Minimum ısınma eşiği

        var data = CountryDatabase.GetOrCreate(countryId, "");
        if (data.PoliticalPower < policy.CostPP) return false;

        return true;
    }

    public static bool EnactPolicy(string countryId, string policyId)
    {
        if (!CanEnactPolicy(countryId, policyId)) return false;

        var state = GetState(countryId);
        var policy = _policies.FirstOrDefault(p => p.Id == policyId);
        var data = CountryDatabase.GetOrCreate(countryId, "");

        data.PoliticalPower -= policy.CostPP;
        state.ActivePolicies.Add(policyId);
        state.PolicyCooldown = 365; // 1 yıl cooldown

        // Enerji mix güncelle
        if (policy.RenewableBoost > 0)
        {
            state.RenewableShare = Math.Min(0.95, state.RenewableShare + policy.RenewableBoost);
            // Fossil paylarını oransal azalt
            double totalFossil = state.CoalShare + state.OilShare + state.GasShare;
            if (totalFossil > 0)
            {
                double reduction = policy.RenewableBoost;
                state.CoalShare = Math.Max(0, state.CoalShare - reduction * (state.CoalShare / totalFossil));
                state.OilShare = Math.Max(0, state.OilShare - reduction * (state.OilShare / totalFossil));
                state.GasShare = Math.Max(0, state.GasShare - reduction * (state.GasShare / totalFossil));
            }
        }
        if (policy.NuclearBoost > 0)
        {
            state.NuclearShare = Math.Min(0.8, state.NuclearShare + policy.NuclearBoost);
        }
        if (policy.LandUseImprovement > 0)
        {
            // Arazi kullanımı iyileşmesi → LandUseShare etkili azalır
        }
        if (policy.AdaptationBoost > 0)
        {
            state.AdaptationLevel = Math.Min(1.0, state.AdaptationLevel + policy.AdaptationBoost);
        }

        GD.Print($"[ClimateSystem] {countryId} politikası uyguladı: {policy.Name} (PP: -{policy.CostPP})");
        return true;
    }

    public static void RevokePolicy(string countryId, string policyId)
    {
        var state = GetState(countryId);
        var policy = _policies.FirstOrDefault(p => p.Id == policyId);
        if (policy == null || !state.ActivePolicies.Contains(policyId)) return;

        state.ActivePolicies.Remove(policyId);
        state.PolicyCooldown = 180; // Yarım yıl

        // Enerji mix geri alma (yavaş)
        if (policy.RenewableBoost > 0)
        {
            state.RenewableShare = Math.Max(GetProfile(countryId).RenewableShare, state.RenewableShare - policy.RenewableBoost * 0.5);
        }
        if (policy.NuclearBoost > 0)
        {
            state.NuclearShare = Math.Max(GetProfile(countryId).NuclearShare, state.NuclearShare - policy.NuclearBoost * 0.5);
        }
        if (policy.AdaptationBoost > 0)
        {
            state.AdaptationLevel = Math.Max(0, state.AdaptationLevel - policy.AdaptationBoost * 0.5);
        }

        GD.Print($"[ClimateSystem] {countryId} politikasını geri çekti: {policy.Name}");
    }

    public static List<Policy> GetAvailablePolicies(string countryId)
    {
        var state = GetState(countryId);
        return _policies.Where(p => !state.ActivePolicies.Contains(p.Id) && _globalWarming >= p.MinWarming).ToList();
    }

    public static List<Policy> GetActivePolicies(string countryId)
    {
        var state = GetState(countryId);
        return _policies.Where(p => state.ActivePolicies.Contains(p.Id)).ToList();
    }

    // ===== Uluslararası Anlaşmalar =====
    public static bool CanSignAgreement(string countryId, string agreementId)
    {
        var state = GetState(countryId);
        var agr = _agreements.FirstOrDefault(a => a.Id == agreementId);
        if (agr == null) return false;
        if (state.SignedAgreements.Contains(agreementId)) return false;

        var data = CountryDatabase.GetOrCreate(countryId, "");
        // Basit kriter: demokrasi veya yüksek prestij
        return true; // Herkes imzalayabilir, AI karar verir
    }

    public static bool SignAgreement(string countryId, string agreementId)
    {
        if (!CanSignAgreement(countryId, agreementId)) return false;

        var state = GetState(countryId);
        state.SignedAgreements.Add(agreementId);

        var data = CountryDatabase.GetOrCreate(countryId, "");
        var agr = _agreements.FirstOrDefault(a => a.Id == agreementId);
        if (agr != null)
        {
            // Prestij bonusu
            data.Prestige += agr.PrestigeBonus;
        }

        GD.Print($"[ClimateSystem] {countryId} anlaşmayı imzaladı: {agr?.Name ?? agreementId}");
        return true;
    }

    public static void WithdrawAgreement(string countryId, string agreementId)
    {
        var state = GetState(countryId);
        if (!state.SignedAgreements.Contains(agreementId)) return;

        state.SignedAgreements.Remove(agreementId);
        var data = CountryDatabase.GetOrCreate(countryId, "");
        var agr = _agreements.FirstOrDefault(a => a.Id == agreementId);
        if (agr != null)
        {
            data.Prestige -= agr.PrestigeBonus;
            // İlişki cezası (diplomasi)
        }

        GD.Print($"[ClimateSystem] {countryId} anlaşmadan çekildi: {agr?.Name ?? agreementId}");
    }

    // ===== COP Toplantısı (Yıllık) =====
    private static void CheckCopMeeting()
    {
        int currentYear = (int)(GameManager.Instance?.TotalDays / 365) + 1;
        if (currentYear >= _nextCopYear)
        {
            _nextCopYear = currentYear + (_settings?.CopFrequencyYears ?? 1);
            RunCopMeeting();
        }
    }

    private static void RunCopMeeting()
    {
        GD.Print($"[ClimateSystem] COP Toplantısı yapılıyor (Yıl: {GameManager.Instance?.TotalDays / 365 + 1}) — Küresel CO2: {_globalCO2:F1} ppm, Isınma: {_globalWarming:F2}°C");

        // AI kararları: imzalama/çekilme/politika
        foreach (var c in CountryDatabase.All())
        {
            if (c.Id == GameManager.Instance?.PlayerCountryId) continue; // Oyuncu hariç
            AiClimateDecisions(c.Id);
        }
    }

    private static void AiClimateDecisions(string countryId)
    {
        var state = GetState(countryId);
        var data = CountryDatabase.GetOrCreate(countryId, "");
        var profile = GetProfile(countryId);

        // Basit AI mantığı: ısınma yüksekse politika, anlaşma imzalama
        double pressure = _globalWarming / 3.0; // 0-1 basınç

        // Anlaşma imzalama
        foreach (var agr in _agreements)
        {
            if (!state.SignedAgreements.Contains(agr.Id))
            {
                double signProb = pressure * 0.3; // %30 şans per °C basınç
                if (pressure > 0.5 && DeterministicRNG.NextDouble() < signProb)
                {
                    SignAgreement(countryId, agr.Id);
                }
            }
        }

        // Politika uygulama
        if (state.PolicyCooldown <= 0 && data.PoliticalPower > 50)
        {
            var available = GetAvailablePolicies(countryId);
            if (available.Count > 0)
            {
                // İdeoloji/ekonomi bazlı seçim (basitleştirilmiş)
                var policy = available[DeterministicRNG.NextInt(0, available.Count)];
                // Maliyet kontrolü
                if (data.PoliticalPower >= policy.CostPP)
                {
                    EnactPolicy(countryId, policy.Id);
                }
            }
        }
    }

    // ===== Günlük Tick =====
    public static void DailyTick()
    {
        _yearCounter++;

        // Yıllık emisyon hesapla (yıl başında bir kez)
        if (_yearCounter % 365 == 1)
        {
            CalculateGlobalEmissions();

            // Yıllık geçmiş kaydı (yılın başında bir anlık görüntü al)
            RecordYearlySnapshot();
        }
        else
        {
            // Günlük birikim
            _cumulativeEmissionsGt += _annualEmissionsGt / 365.0;
            double ppmIncrease = (_annualEmissionsGt / 2.12) / 365.0;
            _globalCO2 += ppmIncrease;
            double targetWarming = _settings.WarmingPerDoubling * Math.Log(_globalCO2 / _settings.PreIndustrialCO2, 2);
            _globalWarming += (targetWarming - _globalWarming) * 0.00027;
            CheckTippingPoints();
        }

        // İklim etkilerini göstergelere uygula
        ApplyClimateImpacts();

        // Politikası cooldown azalt
        foreach (var state in _countryStates.Values)
        {
            if (state.PolicyCooldown > 0)
                state.PolicyCooldown--;
        }

        // COP toplantısı kontrolü
        CheckCopMeeting();
    }

    private static void RecordYearlySnapshot()
    {
        int currentYear = _yearCounter / 365;
        var snapshot = new YearlyClimateSnapshot
        {
            Year = currentYear,
            CO2 = _globalCO2,
            Warming = _globalWarming,
            AnnualEmissionsGt = _annualEmissionsGt,
            CumulativeEmissionsGt = _cumulativeEmissionsGt
        };
        _history.Add(snapshot);

        // Maksimum geçmiş boyutunu sınırla
        if (_history.Count > MaxHistoryYears)
        {
            _history.RemoveAt(0);
        }
    }

    // ===== Test Modu =====
    public static void EnableTestMode()
    {
        // Test modu: şu an GlobalSettings üzerinden kontrol edilebilir
    }

    // ===== Bilgi Getirme =====
    public static double GetGlobalCO2() => _globalCO2;
    public static double GetGlobalWarming() => _globalWarming;
    public static double GetAnnualEmissionsGt() => _annualEmissionsGt;
    public static double GetCumulativeEmissionsGt() => _cumulativeEmissionsGt;
    public static int GetTriggeredTippingPointsCount() => _settings?.TippingPoints?.Count(t => t.Triggered) ?? 0;
    public static List<TippingPoint> GetTippingPoints() => _settings?.TippingPoints ?? new();
    public static List<Policy> GetAllPolicies() => _policies;
    public static List<Agreement> GetAllAgreements() => _agreements;
    public static CountryClimateState GetCountryState(string countryId) => GetState(countryId);

    // Geçmiş verisi (grafikler için)
    public static List<YearlyClimateSnapshot> GetHistory()
    {
        return new List<YearlyClimateSnapshot>(_history);
    }

    // ===== Save/Load =====
    public class ClimateSave
    {
        public double GlobalCO2 { get; set; }
        public double GlobalWarming { get; set; }
        public double CumulativeEmissionsGt { get; set; }
        public double AnnualEmissionsGt { get; set; }
        public int YearCounter { get; set; }
        public int NextCopYear { get; set; }
        public List<TippingPointSave> TippingPoints { get; set; } = new();
        public CountryClimateSave[] Countries { get; set; }
        public List<YearlyClimateSnapshot> History { get; set; } = new(); // M55.3 fix: warming chart history
    }

    public class TippingPointSave
    {
        public string Id { get; set; }
        public bool Triggered { get; set; }
    }

    public class CountryClimateSave
    {
        public string CountryId { get; set; }
        public double AnnualEmissionsMt { get; set; }
        public double RenewableShare { get; set; }
        public double NuclearShare { get; set; }
        public double CoalShare { get; set; }
        public double OilShare { get; set; }
        public double GasShare { get; set; }
        public List<string> ActivePolicies { get; set; } = new();
        public List<string> SignedAgreements { get; set; } = new();
        public double AdaptationLevel { get; set; }
        public int PolicyCooldown { get; set; }
        public double LastEmissionsMt { get; set; }
    }

    public static ClimateSave GetSaveData()
    {
        return new ClimateSave
        {
            GlobalCO2 = _globalCO2,
            GlobalWarming = _globalWarming,
            CumulativeEmissionsGt = _cumulativeEmissionsGt,
            AnnualEmissionsGt = _annualEmissionsGt,
            YearCounter = _yearCounter,
            NextCopYear = _nextCopYear,
            TippingPoints = _settings?.TippingPoints?.Select(tp => new TippingPointSave { Id = tp.Id, Triggered = tp.Triggered }).ToList() ?? new(),
            Countries = _countryStates.Select(kv => new CountryClimateSave
            {
                CountryId = kv.Key,
                AnnualEmissionsMt = kv.Value.AnnualEmissionsMt,
                RenewableShare = kv.Value.RenewableShare,
                NuclearShare = kv.Value.NuclearShare,
                CoalShare = kv.Value.CoalShare,
                OilShare = kv.Value.OilShare,
                GasShare = kv.Value.GasShare,
                ActivePolicies = kv.Value.ActivePolicies,
                SignedAgreements = kv.Value.SignedAgreements,
                AdaptationLevel = kv.Value.AdaptationLevel,
                PolicyCooldown = kv.Value.PolicyCooldown,
                LastEmissionsMt = kv.Value.LastEmissionsMt
            }).ToArray(),
            History = new List<YearlyClimateSnapshot>(_history) // M55.3 fix: warming chart history
        };
    }

    public static void LoadSaveData(ClimateSave save)
    {
        if (save == null) return;

        _globalCO2 = save.GlobalCO2;
        _globalWarming = save.GlobalWarming;
        _cumulativeEmissionsGt = save.CumulativeEmissionsGt;
        _annualEmissionsGt = save.AnnualEmissionsGt;
        _yearCounter = save.YearCounter;
        _nextCopYear = save.NextCopYear;

        if (_settings?.TippingPoints != null && save.TippingPoints != null)
        {
            foreach (var tpSave in save.TippingPoints)
            {
                var tp = _settings.TippingPoints.FirstOrDefault(t => t.Id == tpSave.Id);
                if (tp != null) tp.Triggered = tpSave.Triggered;
            }
        }

        _countryStates.Clear();
        if (save.Countries != null)
        {
            foreach (var cs in save.Countries)
            {
                _countryStates[cs.CountryId] = new CountryClimateState
                {
                    CountryId = cs.CountryId,
                    AnnualEmissionsMt = cs.AnnualEmissionsMt,
                    RenewableShare = cs.RenewableShare,
                    NuclearShare = cs.NuclearShare,
                    CoalShare = cs.CoalShare,
                    OilShare = cs.OilShare,
                    GasShare = cs.GasShare,
                    ActivePolicies = cs.ActivePolicies ?? new(),
                    SignedAgreements = cs.SignedAgreements ?? new(),
                    AdaptationLevel = cs.AdaptationLevel,
                    PolicyCooldown = cs.PolicyCooldown,
                    LastEmissionsMt = cs.LastEmissionsMt
                };
            }
        }

        // M55.3 fix: warming chart history
        _history.Clear();
        if (save.History != null)
        {
            _history.AddRange(save.History);
        }

        GD.Print($"[ClimateSystem] Loaded: CO2={_globalCO2:F1}ppm, Warming={_globalWarming:F2}°C, Countries={_countryStates.Count}, History={_history.Count} years");
    }
}