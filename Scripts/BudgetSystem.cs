using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Bütçe sistemi (M20.2).
// - Data/budget.json'dan vergi oranları, harcama kalemleri, faiz ve borç parametreleri yükler.
// - Her ülke için vergi oranları, harcamalar, borç (GDP'ye oranlı %) ve faiz oranı tutar.
// - Günlük tick'te gelir/harcama/açık/faiz hesaplanır; borç birikir.
// - Bütçe, istatistik göstergelerine yansır: bütçe açığı → budget_deficit, borç → public_debt,
//   vergi/harcama → gdp_growth, açık → inflation, sağlık/eğitim/infra harcaması → health/education/environment.
// - Save/Load: SaveSystem.SaveFile.Budget + SaveLoadTest özeti.
// - Deterministik: yalnızca DeterministicRNG kullanılır (new Random() yok).
public static class BudgetSystem
{
    public class TaxRates
    {
        public double IncomeTax { get; set; }   // oran (0.25 = %25)
        public double CorporateTax { get; set; }
        public double Vat { get; set; }
    }

    public class SpendingCategory
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double DefaultPercent { get; set; } // yüzde, tüm kalemler toplamı 100
    }

    public class BudgetSave
    {
        public CountryBudgetSave[] Countries { get; set; }
    }
    public class CountryBudgetSave
    {
        public string CountryId { get; set; }
        public double IncomeTax { get; set; }
        public double CorporateTax { get; set; }
        public double Vat { get; set; }
        public Dictionary<string, double> SpendingPercent { get; set; } // categoryId -> yüzde
        public double GovernmentDebt { get; set; } // GDP'ye oranlı borç (%)
        public double InterestRate { get; set; }   // yıllık faiz oranı (0.05 = %5)
    }

    private static readonly string DataPath = "res://Data/budget.json";
    private static TaxRates _taxRates = new();
    private static readonly List<SpendingCategory> _spendingCategories = new();
    private static double _defaultInterestRate = 0.05;
    private static double _defaultDebtPercent = 40.0;

    // countryId -> budget state
    private static readonly Dictionary<string, CountryBudgetState> _budgets = new();

    public class CountryBudgetState
    {
        public double IncomeTax;
        public double CorporateTax;
        public double Vat;
        public readonly Dictionary<string, double> SpendingPercent = new(); // categoryId -> yüzde
        public double GovernmentDebt;
        public double InterestRate;
    }

    public static IReadOnlyList<SpendingCategory> SpendingCategories => _spendingCategories;
    public static TaxRates DefaultTaxRates => _taxRates;
    public static double DefaultInterestRate => _defaultInterestRate;
    public static double DefaultDebtPercent => _defaultDebtPercent;

    static BudgetSystem()
    {
        LoadData();
    }

    private static void LoadData()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(DataPath))
            {
                GD.PrintErr($"[BudgetSystem] Veri dosyası yok: {DataPath}");
                ApplyDefaults();
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _spendingCategories.Clear();

            if (root.TryGetProperty("TaxRates", out var tr))
            {
                _taxRates.IncomeTax = tr.TryGetProperty("IncomeTax", out var it) ? it.GetDouble() : 0.25;
                _taxRates.CorporateTax = tr.TryGetProperty("CorporateTax", out var ct) ? ct.GetDouble() : 0.20;
                _taxRates.Vat = tr.TryGetProperty("Vat", out var v) ? v.GetDouble() : 0.18;
            }

            if (root.TryGetProperty("SpendingCategories", out var cats))
            {
                foreach (var c in cats.EnumerateArray())
                {
                    _spendingCategories.Add(new SpendingCategory
                    {
                        Id = c.GetProperty("Id").GetString(),
                        Name = c.TryGetProperty("Name", out var n) ? n.GetString() : c.GetProperty("Id").GetString(),
                        DefaultPercent = c.TryGetProperty("DefaultPercent", out var p) ? p.GetDouble() : 0
                    });
                }
            }

            _defaultInterestRate = root.TryGetProperty("DefaultInterestRate", out var ir) ? ir.GetDouble() : 0.05;
            _defaultDebtPercent = root.TryGetProperty("DefaultDebtPercent", out var dp) ? dp.GetDouble() : 40.0;

            GD.Print($"[BudgetSystem] Veri yüklendi: vergi (gelir={_taxRates.IncomeTax:P0}, kurum={_taxRates.CorporateTax:P0}, KDV={_taxRates.Vat:P0}), {_spendingCategories.Count} harcama kalemi, faiz={_defaultInterestRate:P0}, varsayılan borç={_defaultDebtPercent:F1}%");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[BudgetSystem] Veri yükleme HATA: {e.Message}");
            ApplyDefaults();
        }
    }

    private static void ApplyDefaults()
    {
        _taxRates = new TaxRates { IncomeTax = 0.25, CorporateTax = 0.20, Vat = 0.18 };
        _spendingCategories.Clear();
        _spendingCategories.AddRange(new[]
        {
            new SpendingCategory { Id = "defense", Name = "Defense", DefaultPercent = 20 },
            new SpendingCategory { Id = "health", Name = "Health", DefaultPercent = 15 },
            new SpendingCategory { Id = "education", Name = "Education", DefaultPercent = 15 },
            new SpendingCategory { Id = "infrastructure", Name = "Infrastructure", DefaultPercent = 15 },
            new SpendingCategory { Id = "social", Name = "Social", DefaultPercent = 20 },
            new SpendingCategory { Id = "security", Name = "Security", DefaultPercent = 10 },
            new SpendingCategory { Id = "r_and_d", Name = "Research & Development", DefaultPercent = 5 }
        });
        _defaultInterestRate = 0.05;
        _defaultDebtPercent = 40.0;
    }

    // ===== Erişim / ayarlama =====
    public static CountryBudgetState GetState(string countryId)
    {
        EnsureCountry(countryId);
        return _budgets[countryId];
    }

    public static double GetGovernmentDebt(string countryId) => GetState(countryId).GovernmentDebt;
    public static double GetBudgetDeficit(string countryId) => CalculateDeficitPercentOfGdp(countryId);

    public static void SetTaxRate(string countryId, string taxId, double rate)
    {
        var st = GetState(countryId);
        rate = Math.Clamp(rate, 0, 1);
        if (taxId == "income_tax") st.IncomeTax = rate;
        else if (taxId == "corporate_tax") st.CorporateTax = rate;
        else if (taxId == "vat") st.Vat = rate;
        GD.Print($"[BudgetSystem] {countryId}: {taxId} vergi oranı → {rate:P0}");
    }

    public static void SetSpendingPercent(string countryId, string categoryId, double percent)
    {
        var st = GetState(countryId);
        percent = Math.Clamp(percent, 0, 100);
        st.SpendingPercent[categoryId] = percent;
        GD.Print($"[BudgetSystem] {countryId}: {categoryId} harcaması → {percent:F1}%");
    }

    public static double GetSpendingPercent(string countryId, string categoryId)
    {
        var st = GetState(countryId);
        return st.SpendingPercent.TryGetValue(categoryId, out var v) ? v : GetCategoryDefault(categoryId);
    }

    private static double GetCategoryDefault(string categoryId) =>
        _spendingCategories.FirstOrDefault(c => c.Id == categoryId)?.DefaultPercent ?? 0;

    public static double GetAverageTaxRate(string countryId)
    {
        var st = GetState(countryId);
        return (st.IncomeTax + st.CorporateTax + st.Vat) / 3.0;
    }

    public static double GetTotalSpendingPercent(string countryId)
    {
        var st = GetState(countryId);
        double total = 0;
        foreach (var cat in _spendingCategories)
            total += st.SpendingPercent.TryGetValue(cat.Id, out var v) ? v : cat.DefaultPercent;
        return total;
    }

    // Bütçe açığı % olarak GDP'ye oranlı (negatif = açık)
    private static double CalculateDeficitPercentOfGdp(string countryId)
    {
        var st = GetState(countryId);
        double gdp = StatisticsSystem.GetValue(countryId, "gdp");
        if (gdp <= 0) return 0;
        double avgTax = GetAverageTaxRate(countryId);
        double revenue = gdp * avgTax;
        double spending = gdp * (GetTotalSpendingPercent(countryId) / 100.0);
        double deficit = revenue - spending;
        return deficit / gdp * 100.0;
    }

    // Bir ülkenin bütçe durumunu yoksa varsayılanlarla oluştur (deterministik)
    private static void EnsureCountry(string countryId)
    {
        if (string.IsNullOrEmpty(countryId) || _budgets.ContainsKey(countryId)) return;
        var st = new CountryBudgetState
        {
            IncomeTax = _taxRates.IncomeTax,
            CorporateTax = _taxRates.CorporateTax,
            Vat = _taxRates.Vat,
            GovernmentDebt = _defaultDebtPercent,
            InterestRate = _defaultInterestRate
        };
        foreach (var cat in _spendingCategories)
            st.SpendingPercent[cat.Id] = cat.DefaultPercent;
        _budgets[countryId] = st;
        GD.Print($"[BudgetSystem] {countryId}: bütçe durumu oluşturuldu (borç={st.GovernmentDebt:F1}% GDP, faiz={st.InterestRate:P0})");
    }

    // ===== Başlatma =====
    public static void InitializeAll()
    {
        LoadData();
        _budgets.Clear();
        foreach (var c in CountryDatabase.All())
            EnsureCountry(c.Id);
        GD.Print($"[BudgetSystem] {_budgets.Count} ülke bütçesi başlatıldı.");
    }

    public static void ResetForNewGame()
    {
        InitializeAll();
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır, StatisticsSystem.DailyTick'ten sonra) =====
    public static void DailyTick()
    {
        LoadData();
        foreach (var c in CountryDatabase.All())
            EnsureCountry(c.Id);

        foreach (var kv in _budgets)
        {
            string countryId = kv.Key;
            var st = kv.Value;

            double gdp = StatisticsSystem.GetValue(countryId, "gdp");
            double avgTax = GetAverageTaxRate(countryId);
            double revenue = gdp * avgTax;
            double spendingPct = GetTotalSpendingPercent(countryId);
            double spending = gdp * (spendingPct / 100.0);
            double deficit = revenue - spending;
            double deficitPctGdp = gdp > 0 ? deficit / gdp * 100.0 : 0;

            // Borç birikimi: günde açık/GDP oranı kadar borç artar (eğer açık ise)
            double debtChangePerDay = -deficitPctGdp / 365.0;
            st.GovernmentDebt = Math.Clamp(st.GovernmentDebt + debtChangePerDay, 0, 300);

            // Faiz gideri (yıllık oran / 365)
            double interestPayment = st.GovernmentDebt * st.InterestRate / 365.0;

            // ---- İstatistik göstergelerine yansıt (gradual) ----
            // budget_deficit: doğrudan açık/GDP %
            SetIndicator(countryId, "budget_deficit", deficitPctGdp, 1.0);

            // public_debt: mevcut borç %
            SetIndicator(countryId, "public_debt", st.GovernmentDebt, 1.0);

            // gdp_growth: yüksek vergi büyümeyi düşürür, körpersel/insani harcanma artırır
            double productiveSpend = (GetSpendingPercent(countryId, "infrastructure")
                                    + GetSpendingPercent(countryId, "education")
                                    + GetSpendingPercent(countryId, "r_and_d")) / 100.0;
            double growthTarget = 2.0 - (avgTax - 0.20) * 4.0 + productiveSpend * 3.0 - (spendingPct / 100.0) * 0.5;
            growthTarget = Math.Clamp(growthTarget, -10, 15);
            SetIndicator(countryId, "gdp_growth", growthTarget, 0.15);

            // inflation: açık (borçlanma) enflasyona dönüşür
            double inflationTarget = 2.0 + Math.Max(0, deficitPctGdp) * 0.4 + interestPayment * 2.0;
            inflationTarget = Math.Clamp(inflationTarget, -5, 50);
            SetIndicator(countryId, "inflation", inflationTarget, 0.15);

            // health / education / environment: ilgili harcamaya göre (0-100)
            SetIndicator(countryId, "health", GetSpendingPercent(countryId, "health") * 3.0 + 25.0, 0.10);
            SetIndicator(countryId, "education", GetSpendingPercent(countryId, "education") * 3.0 + 25.0, 0.10);
            double envTarget = 50.0 - GetSpendingPercent(countryId, "defense") * 0.25 + GetSpendingPercent(countryId, "infrastructure") * 0.20;
            envTarget = Math.Clamp(envTarget, 0, 100);
            SetIndicator(countryId, "environment", envTarget, 0.10);

            // Günlük Hafif deterministik jitter (bütçe parametrelerinde mikro değişim simülasyonu)
            double jitter = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * 0.02;
            st.InterestRate = Math.Clamp(st.InterestRate + jitter, 0.01, 0.20);
        }

        GD.Print("[BudgetSystem] Günlük bütçe tick'i tamamlandı (gelir/harcama/açık/borç/faiz + gösterge yansımaları).");
    }

    private static void SetIndicator(string countryId, string indicatorId, double target, double speed)
    {
        var def = StatisticsSystem.Definitions.FirstOrDefault(d => d.Id == indicatorId);
        if (def == null) return;
        double current = StatisticsSystem.GetValue(countryId, indicatorId);
        double next = current + (target - current) * speed;
        StatisticsSystem.SetValue(countryId, indicatorId, next);
    }

    // ===== M12: Save/Load =====
    public static BudgetSave GetSaveData()
    {
        return new BudgetSave
        {
            Countries = _budgets.Select(kv => new CountryBudgetSave
            {
                CountryId = kv.Key,
                IncomeTax = kv.Value.IncomeTax,
                CorporateTax = kv.Value.CorporateTax,
                Vat = kv.Value.Vat,
                SpendingPercent = kv.Value.SpendingPercent.ToDictionary(p => p.Key, p => p.Value),
                GovernmentDebt = kv.Value.GovernmentDebt,
                InterestRate = kv.Value.InterestRate
            }).ToArray()
        };
    }

    public static void LoadSaveData(BudgetSave data)
    {
        if (data == null) return;
        _budgets.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                var st = new CountryBudgetState
                {
                    IncomeTax = c.IncomeTax,
                    CorporateTax = c.CorporateTax,
                    Vat = c.Vat,
                    GovernmentDebt = c.GovernmentDebt,
                    InterestRate = c.InterestRate
                };
                if (c.SpendingPercent != null)
                    foreach (var kv in c.SpendingPercent)
                        st.SpendingPercent[kv.Key] = kv.Value;
                else
                    foreach (var cat in _spendingCategories)
                        st.SpendingPercent[cat.Id] = cat.DefaultPercent;
                _budgets[c.CountryId] = st;
            }
        }
        GD.Print($"[BudgetSystem] {_budgets.Count} ülke bütçesi yüklendi.");
    }
}
