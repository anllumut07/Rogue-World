using Godot;
using System.Collections.Generic;

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

    // Ticaret (M4.5): kaynağa atanmış ithalat konvoyu sayısı
    private static readonly Dictionary<string, int> _importConvoys = new();

    // Konvoy filosu (inşa edilebilir; Infrastructure/Convoy tamamlanınca artar)
    private static int _convoyCount = 20; // HOI4 benzeri başlangıç

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
        _importConvoys.Clear();
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
        for (int i = 0; i < _constructionQueue.Count; i++)
        {
            var item = _constructionQueue[i];
            int assign = System.Math.Min(remaining, MaxFactoriesPerLine);
            item.AssignedFactories = assign;
            if (assign <= 0) break;

            item.PointsDone += assign * PointsPerFactoryPerDay;
            remaining -= assign;

            if (item.IsComplete)
            {
                ApplyBuildCompletion(item);
                _constructionQueue.RemoveAt(i);
                i--;
            }
        }

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
        AddToStock("Oil", d.Oil);
        AddToStock("Rubber", d.Rubber);
        AddToStock("Steel", d.Steel);
        AddToStock("Aluminum", d.Aluminum);
        AddToStock("Tungsten", d.Tungsten);
        AddToStock("Chromium", d.Chromium);
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

    // ===== Otomatik ticaret (M4.5, basitleştirilmiş) =====

    // Kural: stok 30 günlük üretimin altına düşen kaynak için konvoy ata;
    // her konvoy günde +1 birim ithal eder (80% ihraç limiti ileride ülke bazında).
    private static void ProcessAutomaticTrade()
    {
        var d = Player;
        if (d == null) return;

        foreach (var key in new[] { "Oil", "Rubber", "Steel", "Aluminum", "Tungsten", "Chromium" })
        {
            int production = GetDailyProduction(key);
            long stock = _stockpiles.GetValueOrDefault(key);

            // 30 günlük üretim eşiği (üretimi 0 olan ülkeler için 100 birim sabit stok)
            long threshold = production > 0 ? production * 30 : 100;

            if (stock < threshold)
            {
                // Konvoy atanmamışsa ata (boş konvoy varsa)
                if (_importConvoys.GetValueOrDefault(key) == 0 && _convoyCount > 0)
                {
                    _importConvoys[key] = 1;
                    _convoyCount--;
                    GD.Print($"[Trade] {key} stoğu düşük ({stock}) → 1 konvoy ithalata atandı");
                }

                // Atanmış konvoy günde +1 ithal eder
                if (_importConvoys.GetValueOrDefault(key) > 0)
                    _stockpiles[key] = stock + _importConvoys[key];
            }
            else if (_importConvoys.GetValueOrDefault(key) > 0)
            {
                // Stok yeterli → konvoyu serbest bırak
                _convoyCount += _importConvoys[key];
                _importConvoys[key] = 0;
                GD.Print($"[Trade] {key} stoğu yeterli ({stock}) → ithalat konvoyu serbest");
            }
        }
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
    public static IReadOnlyDictionary<string, int> ImportConvoys => _importConvoys;

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
}