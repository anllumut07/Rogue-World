using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Ekipman sistemi (M6): ekipman tipleri (equipment.json), üretim hatları,
/// etkinlik (efficiency) ve stoklar. Üretim hatları askeri fabrika /
/// tersane havuzundan fabrika çeker.
/// Statik sınıf; GameManager günlük tick'inde çağrılır.
/// HOI4 modeli: hat başlangıç etkinliği %10, günde +0.25 puan, max %100.
/// Variant değişimi cezası: hattın etkinliği %50'ye düşer.
/// </summary>
public static class EquipmentSystem
{
    // ===== Veri modelleri =====

    // Ekipman tipi (equipment.json'dan yüklenir)
    public class EquipmentType
    {
        public string Id { get; set; }        // "infantry_equipment"
        public string Name { get; set; }      // "Infantry Equipment"
        public string Category { get; set; }  // "Land" | "Air" | "Naval"
        public string Factory { get; set; }  // "Military" | "Naval" — hangi fabrika tipi üretir
        public int Cost { get; set; }         // üretim maliyeti (puan); 1 fabrika @ %100 = 5 puan/gün
        public int Attack { get; set; }       // temel saldırı (variant istatistik hesabı + M7)
        public int Defense { get; set; }      // temel savunma

        public bool IsNaval => Factory == "Naval";
    }

    // Basitleştirilmiş variant (M6.4): temel ekipman + modül seçimleri
    public class EquipmentVariant
    {
        public string Id { get; set; }              // benzersiz id (örn. "medium_tank_v1")
        public string Name { get; set; }           // görünen ad
        public string BaseEquipmentId { get; set; } // temel ekipman
        public float CostMult { get; set; }        // maliyet çarpanı (örn. 1.3 → %30 pahalı)
        public float AttackMult { get; set; }      // saldırı çarpanı
        public float DefenseMult { get; set; }     // savunma çarpanı
    }

    // Üretim hattı: tek ekipman tipi/variantı, atanan fabrika sayısı, etkinlik
    public class ProductionLine
    {
        public string EquipmentId { get; set; }   // üretilen temel ekipman id
        public string VariantId { get; set; } = ""; // boşsa temel ekipman
        public int AssignedFactories { get; set; }
        public float Efficiency { get; set; }     // 0.10 → 1.00
        public float OutputAccum { get; set; }    // günlük küsurat birikimi (unit tamamlanınca stok'a)
        public float TotalProduced { get; set; }  // toplam üretilen birim (istatistik)
    }

    // Variant tasarım modülü: tek yuvaya (slot) yerleşen seçenekler
    public class Module
    {
        public string Name { get; set; }
        public float CostMult { get; set; }
        public float AttackMult { get; set; }
        public float DefenseMult { get; set; }
    }

    // ===== Sabitler (HOI4 benzeri) =====
    public const float BaseEfficiency = 0.10f;         // hat başlangıç etkinliği %10
    public const float EfficiencyGainPerDay = 0.0025f; // günde +0.25 puan
    public const float MaxEfficiency = 1.00f;         // max %100
    public const float VariantSwitchPenalty = 0.50f;   // variant değişimi: etkinlik %50'ye düşer
    private const int PointsPerFactoryPerDay = 5;     // 1 fabrika @ %100 etkinlik = 5 puan/gün

    // ===== Senaryo başlangıç verisi (M17.2: equipment_start.json) =====
    private class EquipmentStartData
    {
        // Ülkeye özel stoklar: { "TUR": { "infantry_equipment": 48000, ... } }
        public Dictionary<string, Dictionary<string, long>> StocksByCountry { get; set; } = new();
        // Tümen başına stok çarpanı (>0 ise: başlangıç tümenlerini tam donatan stok)
        public double StockPerDivisionMultiplier { get; set; } = 0;
        // Başlangıç üretim hatları: { EquipmentId, VariantId, AssignedFactories }
        public StartLine[] Lines { get; set; } = new StartLine[0];
    }

    private class StartLine
    {
        public string EquipmentId { get; set; }
        public string VariantId { get; set; } = "";
        public int AssignedFactories { get; set; }
    }

    private static EquipmentStartData _startData;
    private static bool _startLoaded;
    private static bool _startLoadLogged;

    // ===== Durum =====
    private static bool _initialized;
    private static readonly Dictionary<string, EquipmentType> _types = new();
    private static readonly Dictionary<string, EquipmentVariant> _variants = new();
    private static readonly List<ProductionLine> _lines = new();
    private static readonly Dictionary<string, long> _stocks = new();
    private static int _variantCounter;

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    // ===== Senaryo başlangıç verisi yükleme (M17.2) =====

    public static void ResetForScenario()
    {
        _startLoaded = false;
        _startLoadLogged = false;
        _startData = null;
    }

    public static void LoadScenarioData()
    {
        EnsureStartLoaded();
        if (!_startLoadLogged)
        {
            _startLoadLogged = true;
            int stockCountries = _startData?.StocksByCountry?.Count ?? 0;
            int lines = _startData?.Lines?.Length ?? 0;
            GD.Print($"EquipmentSystem: senaryo başlangıç verisi yüklendi (senaryo: {ScenarioSystem.ActiveId}) — Stok kaydı={stockCountries} ülke, Tümen stok çarpanı={_startData?.StockPerDivisionMultiplier ?? 0:0.##}, Hat={lines}");
        }
    }

    private static void EnsureStartLoaded()
    {
        if (_startLoaded) return;
        _startLoaded = true;

        string path = ScenarioSystem.ActiveEquipmentStartPath;
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr($"EquipmentSystem: {path} bulunamadı — başlangıç stok/hat yok (eski davranış)");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var json = Godot.FileAccess.GetFileAsString(path);
            _startData = JsonSerializer.Deserialize<EquipmentStartData>(json, options);
        }
        catch (Exception e)
        {
            GD.PrintErr($"EquipmentSystem: equipment_start.json okunamadı: {e.Message}");
        }
    }

    // ===== Yükleme =====

    // Ekipman tiplerini Data/equipment.json'dan yükler (ilk kullanımda)
    private static void EnsureLoaded()
    {
        if (_types.Count > 0) return;
        try
        {
            string path = Path.Combine("Data", "equipment.json");
            if (!File.Exists(path))
            {
                GD.PrintErr($"EquipmentSystem: {path} bulunamadı!");
                return;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var list = JsonSerializer.Deserialize<List<EquipmentType>>(File.ReadAllText(path), options);
            foreach (var t in list)
                _types[t.Id] = t;
            GD.Print($"EquipmentSystem: {_types.Count} ekipman tipi yüklendi");
        }
        catch (Exception e)
        {
            GD.PrintErr($"EquipmentSystem: equipment.json okunamadı: {e.Message}");
        }
    }

    // Oyuncu ülkesi seçilince çağrılır (GameManager.SetPlayerCountry)
    public static void Initialize(CountryData data)
    {
        EnsureLoaded();
        EnsureStartLoaded();
        _lines.Clear();
        _variants.Clear();
        _stocks.Clear();
        _variantCounter = 0;

        if (data == null)
        {
            _initialized = false;
            return;
        }

        // Başlangıç stokları: ülkeye özel > formül (M17.4 modern: tümen × tech seviyesi) > tümen çarpanı > yok
        double stockMult = 0;
        if (_startData?.StocksByCountry != null && _startData.StocksByCountry.TryGetValue(data.Id, out var countryStocks))
        {
            foreach (var kv in countryStocks)
                _stocks[kv.Key] = kv.Value;
        }
        else
        {
            // M17.4: modern senaryo — formül çarpanı (ordu büyüklüğü + teknoloji seviyesi)
            double formulaMult = CountryDatabase.GetEquipmentStockMultiplier(data);
            // Eski davranış: equipment_start.json'daki sabit çarpan
            double legacyMult = _startData?.StockPerDivisionMultiplier ?? 0;
            stockMult = formulaMult > 0 ? formulaMult : legacyMult;

            if (stockMult > 0)
            {
                // Şablon ekipmanları × başlangıç tümenleri × çarpan
                var tplEquip = ArmySystem.GetStartTemplateEquipment();
                int divs = ArmySystem.GetStartDivisionCount(data.Id, data.StartingDivisions);
                foreach (var kv in tplEquip)
                {
                    long amount = (long)(kv.Value * divs * stockMult);
                    if (amount > 0)
                        _stocks[kv.Key] = amount;
                }
            }
        }

        // Başlangıç üretim hatları
        if (_startData?.Lines != null)
        {
            foreach (var l in _startData.Lines)
            {
                if (l.AssignedFactories > 0)
                {
                    var line = new ProductionLine
                    {
                        EquipmentId = l.EquipmentId,
                        VariantId = l.VariantId,
                        AssignedFactories = l.AssignedFactories,
                        Efficiency = BaseEfficiency
                    };
                    _lines.Add(line);
                }
            }
        }

        _initialized = true;
        GD.Print($"EquipmentSystem: {data.Id} için hazır — Askeri fabrika={data.MilFactories}, " +
                 $"Tersane={data.Dockyards}, hat={_lines.Count}, variant=0, stok={_stocks.Count}, " +
                 $"stok çarpanı={stockMult:0.##}, tech seviyesi={data.TechLevel}");
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (!_initialized || Player == null) return;

        // factory_output teknolojileri üretimi artırır
        float outputMult = 1f + TechnologySystem.GetModifier("factory_output");

        foreach (var line in _lines)
        {
            // 1) Etkinlik artışı (HOI4: yalnızca üretim yaparken artar)
            if (line.AssignedFactories > 0)
                line.Efficiency = Math.Min(MaxEfficiency, line.Efficiency + EfficiencyGainPerDay);

            // 2) Üretim: atanan fabrika × 5 puan/gün × etkinlik × teknoloji çarpanı
            if (line.AssignedFactories <= 0) continue;
            float cost = EffectiveCost(line);
            if (cost <= 0) continue;

            line.OutputAccum += line.AssignedFactories * PointsPerFactoryPerDay
                                * line.Efficiency * outputMult;

            // 3) Tam birimler stok'a; küsurat bir sonraki güne
            long units = (long)(line.OutputAccum / cost);
            if (units > 0)
            {
                line.OutputAccum -= units * cost;
                line.TotalProduced += units;
                AddToStock(line.EquipmentId, units);
            }
        }

        // Ayda bir durum logu (headless doğrulama için)
        if (GM.TotalDays > 0 && GM.TotalDays % 30 == 0)
        {
            string stockSummary = _stocks.Count == 0
                ? "stok boş"
                : string.Join(", ", _stocks.Select(kv => $"{kv.Key}={kv.Value}"));
            string lineSummary = _lines.Count == 0
                ? "hat yok"
                : string.Join(", ", _lines.Select(l =>
                    $"{GetDisplayName(l.EquipmentId, l.VariantId)}: {l.AssignedFactories}f %{l.Efficiency*100:F0}"));
            GD.Print($"[Equipment] Gün {GM.TotalDays}: hat={_lines.Count} ({lineSummary}), stok: {stockSummary}");
        }
    }

    private static void AddToStock(string id, long amount)
    {
        _stocks[id] = _stocks.GetValueOrDefault(id) + amount;
    }

    // ===== Hareket API (UI'dan çağrılır) =====

    // Yeni üretim hattı ekle; istenen fabrika sayısı havuzu aşarsa kırpılır
    public static bool AddLine(string equipmentId, int factories, string variantId = "")
    {
        if (!_initialized) return false;

        var type = GetEquipmentType(equipmentId);
        if (type == null)
        {
            GD.PrintErr($"[Equipment] Bilinmeyen ekipman: {equipmentId}");
            return false;
        }

        // Havuz kontrolü: askeri fabrika veya tersane
        int free = type.IsNaval ? FreeDockyards : FreeMilFactories;
        if (free <= 0)
        {
            GD.Print($"[Equipment] Hat eklenemedi: boş {type.Factory} fabrikası yok (boş={free})");
            return false;
        }

        int assign = Math.Min(factories, free);

        var line = new ProductionLine
        {
            EquipmentId = equipmentId,
            VariantId = variantId,
            AssignedFactories = assign,
            Efficiency = BaseEfficiency
        };
        _lines.Add(line);
        GD.Print($"[Equipment] Hat eklendi: {GetDisplayName(equipmentId, variantId)} — {assign} fabrika, " +
                 $"etkinlik %{BaseEfficiency*100:F0} (boş kalan: Mil={FreeMilFactories}, Dock={FreeDockyards})");
        return true;
    }

    // Hattı kaldır → fabrikalar havuza geri döner
    public static void RemoveLine(int index)
    {
        if (index < 0 || index >= _lines.Count) return;
        var line = _lines[index];
        GD.Print($"[Equipment] Hat kaldırıldı: {GetDisplayName(line.EquipmentId, line.VariantId)} — " +
                 $"{line.AssignedFactories} fabrika geri havuza");
        _lines.RemoveAt(index);
    }

    // Hattın fabrika sayısını değiştir (+1/−1 butonları için)
    public static bool ChangeLineFactories(int index, int delta)
    {
        if (index < 0 || index >= _lines.Count) return false;
        var line = _lines[index];

        if (delta > 0)
        {
            bool isNaval = GetEquipmentType(line.EquipmentId)?.IsNaval ?? false;
            int free = isNaval ? FreeDockyards : FreeMilFactories;
            if (free <= 0)
            {
                GD.Print("[Equipment] Fabrika artırılamadı: havuzda boş fabrika yok");
                return false;
            }
            line.AssignedFactories++;
        }
        else if (delta < 0)
        {
            if (line.AssignedFactories <= 0) return false;
            line.AssignedFactories--;
        }
        return true;
    }

    // Variant değişimi (M6.4): aynı hatta farklı variant/temel ekipman üret;
    // HOI4 cezası — etkinlik %50'ye düşer
    public static bool SwitchLineEquipment(int lineIndex, string variantId)
    {
        if (lineIndex < 0 || lineIndex >= _lines.Count) return false;
        var line = _lines[lineIndex];

        if (line.VariantId == variantId) return false;

        string oldName = GetDisplayName(line.EquipmentId, line.VariantId);
        line.VariantId = variantId;
        line.OutputAccum = 0;
        line.Efficiency = VariantSwitchPenalty;
        GD.Print($"[Equipment] Hat değişti: {oldName} → {GetDisplayName(line.EquipmentId, variantId)} — " +
                 $"etkinlik cezası: %{VariantSwitchPenalty*100:F0}");
        return true;
    }

    // ===== Variant tasarımı (M6.4) =====

    // Temel ekipmana göre tasarlanabilir modül yuvaları (basitleştirilmiş Tank/Plane Designer)
    // Slot başına seçenekler: yoksay (None) dahil; çarpanlar kümülatif
    public static List<Module> GetModuleOptions(string slot)
    {
        return slot switch
        {
            "Gun" => new List<Module>
            {
                new Module { Name = "Improved Gun",   CostMult = 0.10f, AttackMult = 0.20f, DefenseMult = 0f    },
                new Module { Name = "Advanced Gun",   CostMult = 0.20f, AttackMult = 0.40f, DefenseMult = 0f    }
            },
            "Armor" => new List<Module>
            {
                new Module { Name = "Extra Armor",    CostMult = 0.15f, AttackMult = 0f,    DefenseMult = 0.25f },
                new Module { Name = "Heavy Armor",     CostMult = 0.30f, AttackMult = 0f,    DefenseMult = 0.50f }
            },
            "Engine" => new List<Module>
            {
                new Module { Name = "Improved Engine", CostMult = 0.10f, AttackMult = 0.05f, DefenseMult = 0.05f }
            },
            _ => new List<Module>()
        };
    }

    // Variant oluşturur ve kaydeder; hatta hemen atanmak üzere döner
    public static EquipmentVariant CreateVariant(string baseEquipmentId, List<Module> selectedModules, string customName = "")
    {
        if (!_initialized) return null;

        var type = GetEquipmentType(baseEquipmentId);
        if (type == null)
        {
            GD.PrintErr($"[Equipment] Variant: bilinmeyen temel ekipman {baseEquipmentId}");
            return null;
        }

        _variantCounter++;
        float costMult = 1f, atkMult = 1f, defMult = 1f;
        var names = new List<string>();
        foreach (var m in selectedModules)
        {
            costMult += m.CostMult;
            atkMult += m.AttackMult;
            defMult += m.DefenseMult;
            names.Add(m.Name);
        }

        string name = string.IsNullOrEmpty(customName)
            ? $"{type.Name} Mk.{_variantCounter}"
            : customName;

        var variant = new EquipmentVariant
        {
            Id = $"{baseEquipmentId}_v{_variantCounter}",
            Name = name,
            BaseEquipmentId = baseEquipmentId,
            CostMult = costMult,
            AttackMult = atkMult,
            DefenseMult = defMult
        };
        _variants[variant.Id] = variant;

        GD.Print($"[Equipment] Variant tasarlandı: {variant.Name} (temel: {type.Name}, modüller: " +
                 $"{(names.Count > 0 ? string.Join(", ", names) : "yok")}, maliyet ×{costMult:F2}, " +
                 $"saldırı ×{atkMult:F2}, savunma ×{defMult:F2})");
        return variant;
    }

    // ===== Getters (UI için) =====

    public static bool IsInitialized => _initialized;
    public static IReadOnlyDictionary<string, EquipmentType> AllTypes
    {
        get { EnsureLoaded(); return _types; }
    }
    public static IReadOnlyList<ProductionLine> Lines => _lines;
    public static IReadOnlyDictionary<string, EquipmentVariant> Variants => _variants;
    public static IReadOnlyDictionary<string, long> Stocks => _stocks;

    public static EquipmentType GetEquipmentType(string id)
    {
        EnsureLoaded();
        return _types.GetValueOrDefault(id);
    }

    // Görünen ad: variant varsa variant adı, yoksa ekipman adı
    public static string GetDisplayName(string equipmentId, string variantId)
    {
        if (!string.IsNullOrEmpty(variantId) && _variants.TryGetValue(variantId, out var v))
            return v.Name;
        return GetEquipmentType(equipmentId)?.Name ?? equipmentId;
    }

    // Etkin maliyet: variant varsa temel maliyet × variant çarpanı
    public static float EffectiveCost(ProductionLine line)
    {
        var type = GetEquipmentType(line.EquipmentId);
        if (type == null) return 0;
        float cost = type.Cost;
        if (!string.IsNullOrEmpty(line.VariantId) && _variants.TryGetValue(line.VariantId, out var v))
            cost *= v.CostMult;
        return cost;
    }

    // Hattın günlük üretim tahmini (UI gösterimi: birim/gün)
    public static float GetDailyOutput(ProductionLine line)
    {
        float cost = EffectiveCost(line);
        if (cost <= 0 || line.AssignedFactories <= 0) return 0;
        float outputMult = 1f + TechnologySystem.GetModifier("factory_output");
        return line.AssignedFactories * PointsPerFactoryPerDay * line.Efficiency * outputMult / cost;
    }

    // Temel ekipmana göre kayıtlı variantlar (variant seçici için)
    public static List<EquipmentVariant> GetVariantsFor(string baseEquipmentId) =>
        _variants.Values.Where(v => v.BaseEquipmentId == baseEquipmentId).ToList();

    // Havuz hesapları: toplam − hatlara atanmış
    public static int UsedMilFactories => _lines.Where(l => !(GetEquipmentType(l.EquipmentId)?.IsNaval ?? false))
                                               .Sum(l => l.AssignedFactories);
    public static int UsedDockyards => _lines.Where(l => GetEquipmentType(l.EquipmentId)?.IsNaval ?? false)
                                             .Sum(l => l.AssignedFactories);

    public static int TotalMilFactories => Player?.MilFactories ?? 0;
    public static int TotalDockyards => Player?.Dockyards ?? 0;
    public static int FreeMilFactories => Math.Max(0, TotalMilFactories - UsedMilFactories);
    public static int FreeDockyards => Math.Max(0, TotalDockyards - UsedDockyards);

    // Stok (M6.3): tüketim API'si M7'de (eğitim/kayıplar) kullanılır
    public static long GetStock(string equipmentId) => _stocks.GetValueOrDefault(equipmentId);

    // Ekipman tüket (eğitim/takviye/kayıp); stok yetersizse ne varsa düşer, kalanı döner
    public static long ConsumeStock(string equipmentId, int amount)
    {
        long stock = _stocks.GetValueOrDefault(equipmentId);
        if (stock <= 0 || amount <= 0) return amount;
        long consumed = Math.Min(stock, amount);
        _stocks[equipmentId] = stock - consumed;
        if (consumed < amount)
            GD.Print($"[Equipment] UYARI: {equipmentId} stok yetmedi ({stock} < {amount}), {consumed} tüketildi");
        return amount - consumed; // karşılanamayan miktar
    }

    // Ekipman ekle (stok girişi — iptal/iade M7)
    public static void AddToStock(string equipmentId, int amount)
    {
        if (amount > 0)
            _stocks[equipmentId] = _stocks.GetValueOrDefault(equipmentId) + amount;
    }

    // ===== M12: Save/Load =====

    public static SaveSystem.EquipmentSave GetSaveData()
    {
        return new SaveSystem.EquipmentSave
        {
            VariantCounter = _variantCounter,
            Lines = _lines.Select(l => new ProductionLine
            {
                EquipmentId = l.EquipmentId,
                VariantId = l.VariantId,
                AssignedFactories = l.AssignedFactories,
                Efficiency = l.Efficiency,
                OutputAccum = l.OutputAccum,
                TotalProduced = l.TotalProduced
            }).ToArray(),
            Variants = _variants.Values.Select(v => new EquipmentVariant
            {
                Id = v.Id,
                Name = v.Name,
                BaseEquipmentId = v.BaseEquipmentId,
                CostMult = v.CostMult,
                AttackMult = v.AttackMult,
                DefenseMult = v.DefenseMult
            }).ToArray(),
            Stocks = _stocks.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
    }

    public static void LoadSaveData(SaveSystem.EquipmentSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _variantCounter = data.VariantCounter;
        _lines.Clear();
        if (data.Lines != null)
            foreach (var l in data.Lines)
                _lines.Add(new ProductionLine
                {
                    EquipmentId = l.EquipmentId,
                    VariantId = l.VariantId ?? "",
                    AssignedFactories = l.AssignedFactories,
                    Efficiency = l.Efficiency,
                    OutputAccum = l.OutputAccum,
                    TotalProduced = l.TotalProduced
                });
        _variants.Clear();
        if (data.Variants != null)
            foreach (var v in data.Variants)
                _variants[v.Id] = v;
        _stocks.Clear();
        if (data.Stocks != null)
            foreach (var kv in data.Stocks) _stocks[kv.Key] = kv.Value;
        _initialized = true;
        GD.Print($"EquipmentSystem: state yüklendi — Hat={_lines.Count}, Variant={_variants.Count}, Stok={_stocks.Count}");
    }
}
