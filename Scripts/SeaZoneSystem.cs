using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Deniz hakimiyeti (M43.1).
///
/// Dünyayı 12 deniz bölgesine ayırır ve her bölgede hangi ülkenin denizleri
/// kontrol ettiğini ülke filo ağırlıklarından saf hesapla üretir.
///
/// Neden saf hesap: hakimiyet, o anda var olan ülke verisinden (tersane sayısı +
/// oyuncunun donanma ekipman stoğu) türetilir; sistemde birikimli state yok.
/// Böylece ayrı bir save/load DTO'su gerekmez ve determinizm korunur
/// (DeterministicRNG kullanılmaz).
///
/// Hakimiyet ölçeği [0,100] ve her zaman bir PERSPEKTIFTEN okunur:
///   50 = çekişmeli / kontrolsüz bölge
///   100 = o ülke için kesin hakimiyet
///   0   = bölge rakibin tam kontrolünde
///
/// Sıradaki adımlar (M43.2-M43.4) bu hakimiyeti tüketir: düşman hakimiyetindeki
/// bölgede konvoy kaybı, iki filonun çarpışması ve müttefik konvoy koruması.
/// </summary>
public static class SeaZoneSystem
{
    private const string DataPath = "res://Data/sea_zones.json";

    // Oyuncunun donanma ekipmanı (EquipmentSystem stoku oyuncuya özeldür).
    // Ağırlık = geminin savaş değerine yakın bir katsayı (Data/equipment.json).
    private static readonly Dictionary<string, float> NavalShipWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["battleship"] = 6f,
        ["carrier"] = 5f,
        ["cruiser"] = 3f,
        ["destroyer"] = 2f,
        ["submarine"] = 1.5f
    };

    /// <summary>Bir deniz bölgesinin tanımı (JSON tek kaydı).</summary>
    public class ZoneDefinition
    {
        public string Id = "";
        public string Name = "";
        public double Lon = 0;
        public double Lat = 0;
        public List<string> AdjacentCountryIds = new();
        public double FleetWeightFactor = 1.0;
        public double ConvoyRouteValue = 1.0;
    }

    private class Root
    {
        public int Version = 1;
        public List<ZoneDefinition> Zones = new();
    }

    // Bölge id → tanım
    private static readonly Dictionary<string, ZoneDefinition> _zones = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    /// <summary>
    /// Konvoy güvenliği eşiği: bu hakimiyetin ÜZERİNDEKİ bölgeler güvenli sayılır
    /// (M43.2 denizaltı baskını bunun altını "güzergâh riski" olarak okur).
    /// 80 = bölgeyi gerçekten biz tutuyoruz; altında düşman ağırlıklıdır ve risk
    /// doğar çünkü kayıp oranı düşman hakimiyetiyle orantılıdır.
    /// </summary>
    public const float SafeSupremacyThreshold = 80f;

    /// <summary>Toplam deniz bölgesi sayısı (log/yükleme doğrulaması için).</summary>
    public static int ZoneCount
    {
        get { EnsureLoaded(); return _zones.Count; }
    }

    public static IReadOnlyCollection<ZoneDefinition> All
    {
        get { EnsureLoaded(); return _zones.Values; }
    }

    public static ZoneDefinition Get(string zoneId)
    {
        EnsureLoaded();
        return zoneId != null ? _zones.GetValueOrDefault(zoneId) : null;
    }

    /// <summary>JSON'u bir kez okur. `IncludeFields=true` olmadan alanlar doldurulmaz.</summary>
    public static void LoadData()
    {
        _zones.Clear();

        if (!Godot.FileAccess.FileExists(DataPath))
        {
            GD.PrintErr($"[SeaZone] HATA: {DataPath} bulunamadı");
            _loaded = true;
            return;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            };
            var json = Godot.FileAccess.GetFileAsString(DataPath);
            var root = JsonSerializer.Deserialize<Root>(json, options);

            if (root?.Zones == null)
            {
                GD.PrintErr($"[SeaZone] HATA: {DataPath} okundu ama bölge listesi boş");
                _loaded = true;
                return;
            }

            foreach (var z in root.Zones)
            {
                if (z == null || string.IsNullOrEmpty(z.Id)) continue;
                z.AdjacentCountryIds ??= new List<string>();
                _zones[z.Id] = z;
            }

            GD.Print($"[SeaZone] Yüklendi: {_zones.Count} deniz bölgesi ({DataPath})");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SeaZone] HATA: {DataPath} çözümlenemedi — {e.Message}");
        }

        _loaded = true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        LoadData();
    }

    // ===== Filo ağırlığı =====

    /// <summary>
    /// Bir ülkenin filo ağırlığı. Tersane sayısı (her ülke için mevcut) taban
    /// sinyaldir; oyuncu için üstüne donanma ekipman stoğu eklenir, çünkü
    /// EquipmentSystem stoku yalnızca oyuncu ülkesine aittir.
    ///
    /// M43.3: Deniz çatışmasında alınan hasar burada çarpı olarak okunur —
    /// hasarlı filo daha az ağırlık, dolayısıyla daha düşük hakimiyet demektir.
    /// Hakimiyetin kendisi yazılmaz; tek kaynak filo ağırlığıdır.
    /// </summary>
    public static float FleetWeight(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 0f;

        var c = CountryDatabase.Get(countryId);
        if (c == null || c.IsAnnexed) return 0f; // ilhak edilmiş ülkenin filosu sayılmaz

        float weight = Math.Max(0, c.Dockyards);

        // Ekipman stoğu yalnızca oyuncu ülkesine aittir; AI için tersane yeterli.
        if (GameManager.Instance != null &&
            string.Equals(GameManager.Instance.PlayerCountryId, countryId, StringComparison.OrdinalIgnoreCase))
        {
            weight += NavalEquipmentWeight();
        }

        // M43.3: Çatışma hasarı filoyu zayıflatır (onarılmamış hasar = eksik gemi).
        return weight * NavalCombatSystem.StrengthMultiplier(countryId);
    }

    /// <summary>Oyuncunun donanma ekipman stoğunun savaş değeri toplamı.</summary>
    private static float NavalEquipmentWeight()
    {
        float total = 0f;
        foreach (var pair in NavalShipWeights)
        {
            long stock = EquipmentSystem.GetStock(pair.Key);
            if (stock > 0) total += stock * pair.Value;
        }
        return total;
    }

    /// <summary>Bir bölgede bir ülkenin bölge çarpanıyla ağırlığı.</summary>
    public static float ZoneWeight(ZoneDefinition zone, string countryId)
    {
        if (zone == null) return 0f;
        return FleetWeight(countryId) * (float)Math.Max(0.01, zone.FleetWeightFactor);
    }

    // ===== Hakimiyet =====

    /// <summary>
    /// Bölgedeki en güçlü ülke ve ağırlığı. Boş/çakişan bölgede null döner.
    /// </summary>
    public static string DominantCountry(string zoneId)
    {
        return DominantCountry(Get(zoneId));
    }

    public static string DominantCountry(ZoneDefinition zone)
    {
        if (zone == null) return null;

        string bestId = null;
        float best = 0f;
        foreach (var id in zone.AdjacentCountryIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var c = CountryDatabase.Get(id);
            if (c == null || c.IsAnnexed) continue;

            float w = ZoneWeight(zone, id);
            if (w > best)
            {
                best = w;
                bestId = id;
            }
        }

        // Hiç filo yoksa bölge sahipsizdir.
        if (bestId == null || best <= 0f) return null;
        return bestId;
    }

    /// <summary>
    /// Bölgenin HAKİMİYETİ, verilen ülkenin perspektifinden [0,100].
    /// 50 = çekişmeli, 100 = tam kontrol, 0 = rakibin kontrolü.
    /// Ülke bölgenin komşusu değilse 50 (nötr) döner.
    /// </summary>
    public static float Supremacy(string zoneId, string countryId)
    {
        return Supremacy(Get(zoneId), countryId);
    }

    public static float Supremacy(ZoneDefinition zone, string countryId)
    {
        if (zone == null || string.IsNullOrEmpty(countryId)) return 50f;

        // Komşu olmayan ülke için bölge doğrudan bir tehdit değildir.
        bool adjacent = zone.AdjacentCountryIds.Any(id =>
            string.Equals(id, countryId, StringComparison.OrdinalIgnoreCase));
        if (!adjacent) return 50f;

        // Perspektifteki ülke + bölgedeki TÜM rakip filo ağırlığının toplamı.
        // Rakibin toplamı alınmasının nedeni: bölgede küçük filolardan oluşan bir
        // koalisyonun tek büyük filo kadar ağırlığı olmalı.
        float mine = ZoneWeight(zone, countryId);
        float theirs = 0f;
        foreach (var id in zone.AdjacentCountryIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (string.Equals(id, countryId, StringComparison.OrdinalIgnoreCase)) continue;

            theirs += ZoneWeight(zone, id);
        }

        // Hiç filo yoksa bölge kontrolsüz.
        if (mine <= 0f && theirs <= 0f) return 50f;

        float sum = mine + theirs;
        // -1 (rakip ezici) → 0, +1 (biz ezici) → 100
        float norm = (mine - theirs) / sum; // [-1, 1]
        return Mathf.Clamp(50f + 50f * norm, 0f, 100f);
    }

    /// <summary>
    /// Bir ülkenin TÜM bölgelerdeki ortalama hakimiyeti (0-100).
    /// Hiç komşu bölgesi yoksa 50 döner.
    /// </summary>
    public static float AverageSupremacy(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 50f;

        EnsureLoaded();

        float total = 0f;
        int count = 0;
        foreach (var zone in _zones.Values)
        {
            if (!zone.AdjacentCountryIds.Any(id =>
                    string.Equals(id, countryId, StringComparison.OrdinalIgnoreCase)))
                continue;

            total += Supremacy(zone, countryId);
            count++;
        }

        return count == 0 ? 50f : total / count;
    }

    /// <summary>
    /// Bir ülkenin kesin olarak hüküm sürdüğü (hakimiyet ≥ eşik) bölgeler.
    /// M43.2 konvoy güvenliği ve M43.4 koruma tahsisi bu listeyi kullanır.
    /// </summary>
    public static List<ZoneDefinition> ControlledZones(string countryId, float threshold = 65f)
    {
        var result = new List<ZoneDefinition>();
        if (string.IsNullOrEmpty(countryId)) return result;

        EnsureLoaded();

        foreach (var zone in _zones.Values)
        {
            if (!zone.AdjacentCountryIds.Any(id =>
                    string.Equals(id, countryId, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (Supremacy(zone, countryId) >= threshold)
                result.Add(zone);
        }

        return result;
    }

    /// <summary>
    /// Bir ülkenin hakimiyetindeki bölgelerdeki konvoy güzergâhı değeri toplamı.
    /// Ticaret koruması / kayıp hesabında (M43.2) ağırlık olarak kullanılır.
    /// </summary>
    public static double ControlledRouteValue(string countryId, float threshold = 65f)
    {
        double total = 0;
        foreach (var zone in ControlledZones(countryId, threshold))
            total += zone.ConvoyRouteValue;
        return total;
    }
}
