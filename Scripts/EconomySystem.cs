using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Ekonomi sistemi (M4): inşaat kuyruğu, tüketim malları (CG), ekonomi yasaları,
/// kaynak stokları ve otomatik ticaret.
/// Statik sınıf; GameManager günlük tick'inde çağrılır.
/// Fabrika sayıları CountryData'da yaşar (tek doğruluk kaynağı); burada yalnızca
/// kuyruk, yasa ve stoklar tutulur.
/// </summary>
public static class EconomySystem
{
    // ===== Yapı tipleri (HOI4 benzeri, basitleştirilmiş) =====
    public enum BuildType
    {
        CivilianFactory,   // Sivil Fabrika — 70 gün
        MilitaryFactory,   // Askeri Fabrika — 70 gün
        Dockyard,          // Tersane — 70 gün
        Infrastructure,    // Altyapı — 25 gün
        Convoy             // Konvoy gemisi — 30 gün
    }

    // Her yapının "15 fabrika ataması ile" bitiş süresi (gün cinsinden)
    private static readonly Dictionary<BuildType, int> BuildDays = new()
    {
        { BuildType.CivilianFactory, 70 },
        { BuildType.MilitaryFactory, 70 },
        { BuildType.Dockyard, 70 },
        { BuildType.Infrastructure, 25 },
        { BuildType.Convoy, 30 }
    };

    // Basit inşaat modeli: her atanan sivil fabrika 5 build point/gün üretir;
    // bir kuyruk öğesine en fazla 15 fabrika atanır (HOI4 kuralı).
    private const int PointsPerFactoryPerDay = 5;
    private const int MaxFactoriesPerLine = 15;

    // ===== Ekonomi yasaları: tüketim malları (CG) sivil fabrikaların yüzdesi =====
    public enum EconomyLaw
    {
        CivilianEconomy,     // %30 CG
        EarlyMobilization,  // %25 CG
        PartialMobilization,// %20 CG
        WarEconomy,          // %15 CG
        TotalMobilization   // %10 CG
    }

    private static readonly Dictionary<EconomyLaw, float> CGPercent = new()
    {
        { EconomyLaw.CivilianEconomy, 0.30f },
        { EconomyLaw.EarlyMobilization, 0.25f },
        { EconomyLaw.PartialMobilization, 0.20f },
        { EconomyLaw.WarEconomy, 0.15f },
        { EconomyLaw.TotalMobilization, 0.10f }
    };

    // ===== Durum =====
    private static EconomyLaw _currentLaw = EconomyLaw.CivilianEconomy;
    private static bool _initialized;
    private static readonly List<BuildItem> _constructionQueue = new();

    // Kaynak stokları (M4.4) — CountryData'daki alanlar üretim değeridir, stok değil
    private static readonly Dictionary<string, long> _stockpiles = new();

    // Ticaret (M4.5): aktif ithalat anlaşmaları
    private static readonly List<TradeDeal> _tradeDeals = new();

    // Konvoy filosu (inşa edilebilir; Convoy tamamlanınca artar)
    private static int _convoyCount = 20; // HOI4 benzeri başlangıç

    // Ticaret anlaşması: belirli bir kaynağı günde X birim ithal eder
    public class TradeDeal
    {
        public string Resource;     // "Oil", "Steel"...
        public int AmountPerDay;    // günlük ithal miktarı (1 konvoy = 1 birim/gün)
        public string ExporterId;   // ihraç eden ülke id
        public string ExporterName; // görünen ad
    }

    // İnşaat kuyruğu öğesi
    public class BuildItem
    {
        public BuildType Type;
        public int PointsNeeded;
        public int PointsDone;
        public int AssignedFactories; // son gün atanan fabrika sayısı (UI gösterimi)

        public float ProgressPercent => PointsNeeded > 0 ? 100f * PointsDone / PointsNeeded : 0f;
        public bool IsComplete => PointsDone >= PointsNeeded;

        public string DisplayName => Type switch
        {
            BuildType.CivilianFactory => "Civilian Factory",
            BuildType.MilitaryFactory => "Military Factory",
            BuildType.Dockyard => "Dockyard",
            BuildType.Infrastructure => "Infrastructure",
            BuildType.Convoy => "Convoy",
            _ => Type.ToString()
        };
    }

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    // ===== Başlatma =====

    // Oyuncu ülkesi seçilince çağrılır (GameManager.SetPlayerCountry)
    public static void Initialize(CountryData data)
    {
        _constructionQueue.Clear();
        _stockpiles.Clear();
        _tradeDeals.Clear();
        _convoyCount = 20;

        if (data == null)
        {
            _initialized = false;
            return;
        }

        // İdeolojiye göre varsayılan ekonomi yasası (HOI4 1936 başlangıçlarına yakın)
        _currentLaw = data.Ideology switch
        {
            "Fascism" => EconomyLaw.EarlyMobilization, // Almanya/İtalya/Japonya
            _ => EconomyLaw.CivilianEconomy
        };

        _initialized = true;
        GD.Print($"EconomySystem: {data.Id} için hazır — Yasa={_currentLaw} (CG={CGPercent[_currentLaw]*100:F0}%), " +
                 $"Civ={data.CivFactories}, Mil={data.MilFactories}, Dock={data.Dockyards}, Konvoy={_convoyCount}");
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (!_initialized || Player == null) return;

        // 1) Kaynak üretimi → stoklara
        ProduceResources();

        // 2) Boş sivil fabrika = toplam − CG'ye ayrılan
        int freeCivs = FreeCivFactories;

        // 3) İnşaat kuyruğu: öndeki öğelere en fazla 15'er fabrika ata (HOI4 gibi)
        int remaining = freeCivs;
        float constructionMult = 1f + TechnologySystem.GetModifier("construction_speed");
        for (int i = 0; i < _constructionQueue.Count; i++)
        {
            var item = _constructionQueue[i];
            int assign = System.Math.Min(remaining, MaxFactoriesPerLine);
            item.AssignedFactories = assign;
            if (assign <= 0) break;

            // construction_speed teknolojileri inşaatı hızlandırır
            item.PointsDone += (int)(assign * PointsPerFactoryPerDay * constructionMult);
            remaining -= assign;

            if (item.IsComplete)
            {
                ApplyBuildCompletion(item);
                _constructionQueue.RemoveAt(i);
                i--;
            }
        }

        // 3.5) M43.2: Denizaltı baskını — düşman hakimiyetindeki bölgede konvoy kaybı.
        // Ticaretten ÖNCE çalışır: battığı bilinen konvoyların ithalat hacmi zaten azalmış olur.
        if (Player != null && _convoyCount > 0)
            ConvoyRaidSystem.ApplyDailyRaids(Player.Id);

        // 4) Otomatik ticaret (M4.5): eksik kaynak → konvoyla ithalat
        ProcessAutomaticTrade();

        // 5) Ayda bir durum logu
        if (GM.TotalDays > 0 && GM.TotalDays % 30 == 0)
            GD.Print($"[Economy] Gün {GM.TotalDays}: BoşSivil={freeCivs}, CG={CGRate*100:F0}%, " +
                     $"Kuyruk={_constructionQueue.Count}, Petrol={_stockpiles.GetValueOrDefault("Oil")}, " +
                     $"Çelik={_stockpiles.GetValueOrDefault("Steel")}, Konvoy={_convoyCount}");
    }

    // Üretim: CountryData'daki kaynak alanları günlük üretim birimi sayılır (HOI4 modeli)
    private static void ProduceResources()
    {
        var d = Player;
        float resMult = 1f + TechnologySystem.GetModifier("resource_gain");

        // M45.2: özerk bağımlılardan gelen kaynak azalır (kesinti katsayısı)
        resMult *= (float)AutonomySystem.ResourceExtractionFactor(d.Id);

        AddToStock("Oil", (int)(d.Oil * resMult));
        AddToStock("Rubber", (int)(d.Rubber * resMult));
        AddToStock("Steel", (int)(d.Steel * resMult));
        AddToStock("Aluminum", (int)(d.Aluminum * resMult));
        AddToStock("Tungsten", (int)(d.Tungsten * resMult));
        AddToStock("Chromium", (int)(d.Chromium * resMult));
    }

    private static void AddToStock(string key, int amount)
    {
        if (amount > 0) _stockpiles[key] = _stockpiles.GetValueOrDefault(key) + amount;
    }

    // Yapım bitince etkisini CountryData'ya yaz (tek doğruluk kaynağı)
    private static void ApplyBuildCompletion(BuildItem item)
    {
        var d = Player;
        if (d == null) return;

        switch (item.Type)
        {
            case BuildType.CivilianFactory: d.CivFactories++; break;
            case BuildType.MilitaryFactory: d.MilFactories++; break;
            case BuildType.Dockyard: d.Dockyards++; break;
            case BuildType.Infrastructure: /* TODO M7: tedarik/altyapı etkisi */ break;
            case BuildType.Convoy: _convoyCount++; break;
        }

        GD.Print($"[Economy] İnşaat tamam: {item.DisplayName} → " +
                 $"Civ={d.CivFactories}, Mil={d.MilFactories}, Dock={d.Dockyards}, Konvoy={_convoyCount}");
    }

    // ===== İnşaat API (UI'dan çağrılır) =====

    public static bool StartConstruction(BuildType type)
    {
        if (!_initialized) return false;
        int days = BuildDays[type];
        _constructionQueue.Add(new BuildItem
        {
            Type = type,
            PointsNeeded = days * MaxFactoriesPerLine * PointsPerFactoryPerDay
        });
        GD.Print($"[Economy] İnşaat başladı: {type} ({days} gün @ 15 fabrika, " +
                 $"{days * MaxFactoriesPerLine * PointsPerFactoryPerDay} puan)");
        return true;
    }

    public static void CancelConstruction(int index)
    {
        if (index >= 0 && index < _constructionQueue.Count)
        {
            GD.Print($"[Economy] İnşaat iptal: {_constructionQueue[index].DisplayName}");
            _constructionQueue.RemoveAt(index);
        }
    }

    // ===== Ekonomi yasası (M4.3) =====

    public static bool ChangeLaw(EconomyLaw newLaw)
    {
        if (!_initialized || _currentLaw == newLaw) return false;

        var d = Player;
        const double cost = 150; // HOI4: yasa değişimi 150 PP
        if (d == null || d.PoliticalPower < cost)
        {
            GD.Print($"[Economy] Yasa değişimi başarısız: PP yetersiz ({d?.PoliticalPower:N0}/{cost})");
            return false;
        }

        d.PoliticalPower -= cost;
        _currentLaw = newLaw;
        GD.Print($"[Economy] Yasa değişti: {newLaw} (CG={CGPercent[newLaw]*100:F0}%, maliyet={cost} PP)");

        // HUD'ı anında yenile (PP düştü)
        if (GameManager.IsAvailable)
            GameManager.Instance.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);

        return true;
    }

    // ===== Otomatik ticaret (M4.5) =====

    private static readonly string[] TrackedResources = { "Oil", "Rubber", "Steel", "Aluminum", "Tungsten", "Chromium" };

    // Ülkenin kaynak üretim alanı adı → CountryData alanı eşlemesi
    private static int GetProductionOf(CountryData d, string key) => key switch
    {
        "Oil" => d.Oil,
        "Rubber" => d.Rubber,
        "Steel" => d.Steel,
        "Aluminum" => d.Aluminum,
        "Tungsten" => d.Tungsten,
        "Chromium" => d.Chromium,
        _ => 0
    };

    // Kural: stok, 30 günlük üretimin (veya üretim 0 ise 100 birim) altına düşerse
    // en büyük üreticiden ithalat anlaşması kur; stok 2 katına çıkınca iptal et.
    // HOI4 kuralı: ihraç eden ülke üretiminin en fazla %80'ini satabilir.
    private static void ProcessAutomaticTrade()
    {
        var d = Player;
        if (d == null) return;

        foreach (var key in TrackedResources)
        {
            int production = GetDailyProduction(key);
            long stock = _stockpiles.GetValueOrDefault(key);
            long threshold = production > 0 ? production * 30 : 100;

            var deal = _tradeDeals.Find(t => t.Resource == key);

            if (stock < threshold && deal == null)
            {
                // Yeni anlaşma kur: en büyük üreticiyi bul (%80 limitini aşmayacak kadar)
                var exporter = FindBestExporter(d, key);
                if (exporter == null)
                {
                    // Dünyada satılacak kaynak yok → bu günlük atla (log'u spam'lemek için bir kez yaz)
                    continue;
                }

                int amount = 2; // 2 birim/gün (2 konvoy)
                deal = new TradeDeal
                {
                    Resource = key,
                    AmountPerDay = amount,
                    ExporterId = exporter.Id,
                    ExporterName = exporter.Name
                };
                _tradeDeals.Add(deal);
                _convoyCount -= amount;
                GD.Print($"[Trade] Anlaşma kuruldu: {exporter.Name} → {key} {amount}/gün " +
                         $"(kalan konvoy: {_convoyCount}, stok: {stock}/{threshold})");
            }
            else if (stock >= threshold * 2 && deal != null)
            {
                // Stok bol → anlaşmayı iptal et, konvoyları serbest bırak
                _convoyCount += deal.AmountPerDay;
                GD.Print($"[Trade] Anlaşma iptal: {key} (stok yeterli: {stock}) — konvoylar serbest");
                _tradeDeals.Remove(deal);
            }
        }

        // M37.3: Uluslararası örgüt tek pazar ticaret çarpanı (ör. AB üyesi %15).
        float tradeMult = OrganizationSystem.GetTradeMultiplier(Player?.Id);

        // Aktif anlaşmaların ithalatını uygula
        foreach (var deal in _tradeDeals)
            _stockpiles[deal.Resource] = _stockpiles.GetValueOrDefault(deal.Resource) + (int)(deal.AmountPerDay * tradeMult);
    }

    // Kaynak için en iyi ihracatçı: oyuncu değil, bu kaynağı en çok üreten ülke.
    // %80 limit: seçilen miktar ihracatçının üretiminin %80'ini aşmamalı.
    private static CountryData FindBestExporter(CountryData player, string key)
    {
        CountryData best = null;
        int bestProd = 2; // en az 2 birim/gün satabilmeli (2 birimlik anlaşma için)
        foreach (var c in CountryDatabase.All())
        {
            if (c == player || c.IsAnnexed) continue;
            int prod = GetProductionOf(c, key);
            // %80 ihraç limiti: 2 birim satmak için üretimin ≥ 2/0.8 = 2.5 olması gerekir → 3
            if (prod * 8 >= 2 * 10 && prod > bestProd) // prod*0.8 >= amount (2)
            {
                best = c;
                bestProd = prod;
            }
        }
        return best;
    }

    // ===== Getters (UI için) =====

    public static bool IsInitialized => _initialized;
    public static IReadOnlyList<BuildItem> ConstructionQueue => _constructionQueue;
    public static EconomyLaw CurrentLaw => _currentLaw;
    public static float CGRate => CGPercent[_currentLaw];

    public static int FreeCivFactories
    {
        get
        {
            var d = Player;
            if (d == null) return 0;
            int cg = (int)(d.CivFactories * CGPercent[_currentLaw]);
            return d.CivFactories - cg;
        }
    }

    public static int ConvoyCount => _convoyCount;
    public static IReadOnlyDictionary<string, long> Stockpiles => _stockpiles;
    public static IReadOnlyList<TradeDeal> TradeDeals => _tradeDeals;

    // ===== M43.2: Denizaltı baskını API'si (ConvoyRaidSystem için) =====

    /// <summary>Konvoyları batır (denizaltı blandığı çağrılır). Sayı negatif olamaz.</summary>
    public static int LoseConvoys(int amount)
    {
        if (amount <= 0) return 0;
        int lost = Math.Min(amount, _convoyCount);
        _convoyCount -= lost;
        return lost;
    }

    /// <summary>Bir ticaret anlaşmasını kıs: miktarı düşür ve farkı konvoy havuzuna ver.</summary>
    public static bool ReduceTradeDeal(string resource, int newAmountPerDay)
    {
        var deal = _tradeDeals.Find(t => t.Resource == resource);
        if (deal == null) return false;

        int old = deal.AmountPerDay;
        if (newAmountPerDay >= old) return false;

        deal.AmountPerDay = Math.Max(1, newAmountPerDay);
        _convoyCount += old - deal.AmountPerDay; // kullanılmayan konvoylar geri döner
        return true;
    }

    /// <summary>Bir ticaret anlaşmasını tamamen çöz ve konvoylarını geri ver.</summary>
    public static bool ReleaseTradeDeal(string resource)
    {
        var deal = _tradeDeals.Find(t => t.Resource == resource);
        if (deal == null) return false;

        _convoyCount += deal.AmountPerDay;
        _tradeDeals.Remove(deal);
        return true;
    }

    // Fabrika sayıları CountryData'dan okunur (tek doğruluk kaynağı)
    public static int TotalCivFactories => Player?.CivFactories ?? 0;
    public static int TotalMilFactories => Player?.MilFactories ?? 0;
    public static int TotalDockyards => Player?.Dockyards ?? 0;

    // CountryData'dan günlük üretim (UI gösterimi için)
    public static int GetDailyProduction(string key)
    {
        var d = Player;
        if (d == null) return 0;
        return key switch
        {
            "Oil" => d.Oil,
            "Rubber" => d.Rubber,
            "Steel" => d.Steel,
            "Aluminum" => d.Aluminum,
            "Tungsten" => d.Tungsten,
            "Chromium" => d.Chromium,
            _ => 0
        };
    }

    // ===== M12: Save/Load =====

    /// <summary>EconomySystem state'ini DTO olarak döndür (SaveSystem toplar).</summary>
    public static SaveSystem.EconomySave GetSaveData()
    {
        return new SaveSystem.EconomySave
        {
            CurrentLaw = (int)_currentLaw,
            ConvoyCount = _convoyCount,
            Queue = _constructionQueue.Select(q => new SaveSystem.BuildItemSave
            {
                Type = (int)q.Type,
                PointsNeeded = q.PointsNeeded,
                PointsDone = q.PointsDone,
                AssignedFactories = q.AssignedFactories
            }).ToArray(),
            Stockpiles = _stockpiles.ToDictionary(kv => kv.Key, kv => kv.Value),
            TradeDeals = _tradeDeals.Select(d => new SaveSystem.TradeDealSave
            {
                Resource = d.Resource,
                AmountPerDay = d.AmountPerDay,
                ExporterId = d.ExporterId,
                ExporterName = d.ExporterName
            }).ToArray()
        };
    }

    /// <summary>EconomySystem state'ini DTO'dan yükle.</summary>
    public static void LoadSaveData(SaveSystem.EconomySave data)
    {
        if (data == null) return;
        _currentLaw = (EconomyLaw)data.CurrentLaw;
        _convoyCount = data.ConvoyCount;
        _constructionQueue.Clear();
        if (data.Queue != null)
            foreach (var q in data.Queue)
                _constructionQueue.Add(new BuildItem
                {
                    Type = (BuildType)q.Type,
                    PointsNeeded = q.PointsNeeded,
                    PointsDone = q.PointsDone,
                    AssignedFactories = q.AssignedFactories
                });
        _stockpiles.Clear();
        if (data.Stockpiles != null)
            foreach (var kv in data.Stockpiles) _stockpiles[kv.Key] = kv.Value;
        _tradeDeals.Clear();
        if (data.TradeDeals != null)
            foreach (var d in data.TradeDeals)
                _tradeDeals.Add(new TradeDeal
                {
                    Resource = d.Resource,
                    AmountPerDay = d.AmountPerDay,
                    ExporterId = d.ExporterId,
                    ExporterName = d.ExporterName
                });
        _initialized = true;
        GD.Print($"EconomySystem: state yüklendi — Yasa={_currentLaw}, Kuyruk={_constructionQueue.Count}, Konvoy={_convoyCount}");
    }
}