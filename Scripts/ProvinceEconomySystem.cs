using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// M47.1 — İl bazlı ekonomi ağırlık katmanı.
///
/// Problem (M47'nin çıkış noktası): `CountryData` üzerindeki fabrika/kaynak/nüfus
/// alanları sabittir. Bir il devredildiğinde (M42 barış konferansı, M44.4 serbest
/// bırakma, M44.3 kurgu kurma) ekonomi yeniden hesaplanmaz — dünyanın yarısını
/// alan ülke, küçük bir ülkenin tüm fabrikalarını da alabilir.
///
/// Bu sistem her il için üç ayrı pay üretir:
///   • kaynak payı  — alan ağırlıklı (uzak/büyük araziler kaynak demektir)
///   • fabrika payı — kent + kıyı ağırlıklı (sanayi kentlerde ve kıyıda)
///   • nüfus payı   — il nüfusu ağırlıklı, alan ile hafif harman
///
/// Paylar ülke içinde normalize edilir ve `CountryData` ile *harmanlanır*:
/// `ReconcileCountry` ülkenin gerçek değerlerini bu paylara göre illere böler,
/// son il kalan farkı alır → il toplamı ülke toplamına tam eşittir. Üretim
/// kaybolmaz; il devrinde (M47.2) verenin payı azalır, alanın payı artar.
///
/// Determinizm: kentsel faktör `DeterministicRNG` ile çekilir ama çekiliş öncesi
/// global state kaydedilip geri yüklenir → dünya simülasyonunun rastgelelik
/// akışı bozulmaz (save/load A==B korunur). Tohum il kimliğinden türetilir.
/// </summary>
public static class ProvinceEconomySystem
{
    private const string DataPath = "res://Data/province_economy.json";

    /// <summary>Ayarlar (`Data/province_economy.json`).</summary>
    private sealed class EconomySettings
    {
        public double TerritoryWeight = 0.35;
        public double UrbanWeight = 0.45;
        public double CoastWeight = 0.20;
        public double ResourceTerritoryBias = 0.60;
        public double ResourceUrbanBias = 0.40;
        public double FactoryUrbanBias = 0.55;
        public double FactoryTerritoryBias = 0.25;
        public double FactoryCoastBias = 0.20;
        public double PopulationCultureWeight = 0.85;
        public double PopulationAreaWeight = 0.15;
        public int BlendTolerancePercent = 5;

        // M47.3 — kaynak açğı/tokluk dengesi
        public double NeedPerFactory = 0.25;   // fabrika başına kaynak ihtiyacı
        public double NeedPerMillionPop = 0.04; // milyon kişi başına kaynak ihtiyacı
        public double MinEfficiency = 0.5;     // çıkarım verimliliği tabanı (ölümsüz spirali önler)
        public double UnitsPerConvoy = 2.0;    // bir konvoyun günlük taşıyabildiği kaynak
        public double ImportPriceBase = 1.0;   // birim ithalat maliyetinin tabanı
        public double DeficitPriceScale = 0.6; // açık derinleştikçe fiyat artış katsayısı
        public double IncomePerSurplusUnit = 0.5; // fazla birim kaynağın ihracat geliri
    }

    private sealed class EconomyRoot
    {
        public int Version { get; set; } = 1;
        public EconomySettings Settings { get; set; } = new();
    }

    private static EconomySettings S = new();
    private static bool _loaded;

    /// <summary>Bir ilin ekonomi ağırlığı ve `CountryData` ile harmanlanmış payı.</summary>
    public class Weight
    {
        public string ProvinceId = "";
        public string CountryId = "";    // sahip ülke
        public float Territory = 0f;     // ham alan (il birimi)
        public float Urban = 0f;         // 0-1 kentsel faktör (şehir)
        public float Coast = 0f;         // 0-1 kıyı faktörü
        public float ResourceW = 0f;     // normalize edilmiş kaynak payı (ülke içi)
        public float FactoryW = 0f;      // normalize edilmiş fabrika payı (ülke içi)
        public float PopulationW = 0f;   // normalize edilmiş nüfus payı (ülke içi)

        // Harmanlanmış pay (ReconcileCountry sonrası) — il devrinde buna göre
        // veren/alan ülkenin CountryData alanları güncellenir (M47.2).
        public int Civ;
        public int Mil;
        public int Dock;
        public int Oil;
        public int Rubber;
        public int Steel;
        public int Aluminum;
        public int Tungsten;
        public int Chromium;
        public long Population;

        public int TotalFactories => Civ + Mil + Dock;
    }

    // Takip edilen kaynak alanları (EconomySystem.TrackedResources ile aynı sıra)
    public static readonly string[] ResourceKeys =
        { "Oil", "Rubber", "Steel", "Aluminum", "Tungsten", "Chromium" };

    // il kimliği → ağırlık kaydı
    private static readonly Dictionary<string, Weight> _weights =
        new(StringComparer.Ordinal);

    // ülke kimliği → il kimlikleri (sahiplik değişince temizlenir)
    private static readonly Dictionary<string, List<string>> _byCountry =
        new(StringComparer.OrdinalIgnoreCase);

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        using var file = Godot.FileAccess.Open(DataPath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("[ProvEconomy] province_economy.json açılamadı — varsayılan ayarlar kullanılıyor.");
            return;
        }

        var text = file.GetAsText();
        var root = JsonSerializer.Deserialize<EconomyRoot>(text, new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        });
        if (root?.Settings != null)
        {
            S = root.Settings;
            GD.Print($"[ProvEconomy] Ayarlar yüklendi: kaynak alan/kent = " +
                     $"{S.ResourceTerritoryBias}/{S.ResourceUrbanBias}, " +
                     $"fabrika kent/alan/kıyı = {S.FactoryUrbanBias}/" +
                     $"{S.FactoryTerritoryBias}/{S.FactoryCoastBias}.");
        }
    }

    /// <summary>Ağırlık kaydı olan il sayısı.</summary>
    public static int RecordCount => _weights.Count;

    /// <summary>Ağırlık kaydı (yoksa null).</summary>
    public static Weight Get(string provinceId)
    {
        if (string.IsNullOrEmpty(provinceId)) return null;
        return _weights.GetValueOrDefault(provinceId);
    }

    /// <summary>Bir ülkenin tüm ağırlık kayıtları.</summary>
    public static List<Weight> ForCountry(string countryId)
    {
        var result = new List<Weight>();
        if (string.IsNullOrEmpty(countryId)) return result;

        if (_byCountry.TryGetValue(countryId, out var ids))
            foreach (var id in ids)
                if (_weights.TryGetValue(id, out var w))
                    result.Add(w);

        return result;
    }

    /// <summary>
    /// Tüm ağırlık kayıtları (il kimliğine göre sıralı — deterministik).
    /// </summary>
    public static List<Weight> All()
    {
        var result = new List<Weight>(_weights.Count);
        foreach (var key in _weights.Keys.OrderBy(k => k, StringComparer.Ordinal))
            result.Add(_weights[key]);
        return result;
    }

    /// <summary>
    /// Verilen iller için ağırlık katmanını üretir ve `CountryData` ile harmanlar.
    ///
    /// Kültür katmanı (`ProvinceCultureSystem`) bu çağrıdan ÖNCE üretilmiş
    /// olmalıdır — nüfus payı il nüfusuna bağlanır. Kültür katmanı yoksa nüfus
    /// payı yalnızca alana göre dağıtılır.
    /// </summary>
    public static void GenerateFor(List<ProvinceData> provinces, string scenarioName = "")
    {
        EnsureLoaded();
        _weights.Clear();
        _byCountry.Clear();

        if (provinces == null || provinces.Count == 0)
        {
            GD.Print("[ProvEconomy] Üretilecek il yok — ekonomi katmanı boş");
            return;
        }

        // 1) Ham faktörler: iller sıralı dolaşılır → deterministik akış.
        var valid = provinces
            .Where(p => p != null && !string.IsNullOrEmpty(p.Id) && p.Id != "?" &&
                        !string.IsNullOrEmpty(p.CountryId) && p.CountryId != "?")
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var p in valid)
        {
            var w = new Weight
            {
                ProvinceId = p.Id,
                CountryId = p.CountryId,
                Territory = Mathf.Max(0f, p.Area),
                Urban = UrbanScore(p),
                Coast = p.IsCoastal ? 1f : 0f
            };

            // Ham (normalize edilmemiş) puanlar aynı alanda tutulur.
            w.ResourceW = (float)(S.ResourceTerritoryBias * w.Territory +
                                  S.ResourceUrbanBias * w.Urban);
            w.FactoryW = (float)(S.FactoryUrbanBias * w.Urban +
                                 S.FactoryTerritoryBias * w.Territory +
                                 S.FactoryCoastBias * w.Coast);

            // Nüfus: il nüfusu ağırlıklı; kültür katmanı yoksa alana düşer.
            var cult = ProvinceCultureSystem.Get(p.Id);
            if (cult != null && cult.Population > 0)
                w.PopulationW = (float)(S.PopulationCultureWeight * cult.Population +
                                        S.PopulationAreaWeight * w.Territory);
            else
                w.PopulationW = w.Territory;

            _weights[p.Id] = w;
            if (!_byCountry.TryGetValue(p.CountryId, out var list))
            {
                list = new List<string>();
                _byCountry[p.CountryId] = list;
            }
            list.Add(p.Id);
        }

        // 2) Ülke içinde normalize et (her kanalın payları toplamı 1 olsun)
        foreach (var kv in _byCountry)
            NormalizeShares(kv.Value);

        ReconcileAll();

        GD.Print($"[ProvEconomy] Ekonomi katmanı üretildi: {_weights.Count} il, " +
                 $"{_byCountry.Count} ülke (senaryo={scenarioName})");
    }

    /// <summary>
    /// Ülke içi payları normalize eder: her kanal için o ülkedeki illerin
    /// toplamı 1 olacak şekilde ölçeklenir. Toplam 0 ise eşit dağılıma düşülür
    /// ki iller hiç pay alamayan duruma düşmesin.
    /// </summary>
    private static void NormalizeShares(List<string> provinceIds)
    {
        if (provinceIds == null || provinceIds.Count == 0) return;

        float resSum = 0f, facSum = 0f, popSum = 0f;
        foreach (var id in provinceIds)
        {
            if (!_weights.TryGetValue(id, out var w)) continue;
            resSum += w.ResourceW;
            facSum += w.FactoryW;
            popSum += w.PopulationW;
        }

        int n = provinceIds.Count;
        bool resEven = resSum <= 0f;
        bool facEven = facSum <= 0f;
        bool popEven = popSum <= 0f;

        foreach (var id in provinceIds)
        {
            if (!_weights.TryGetValue(id, out var w)) continue;

            w.ResourceW = resEven ? 1f / n : w.ResourceW / resSum;
            w.FactoryW = facEven ? 1f / n : w.FactoryW / facSum;
            w.PopulationW = popEven ? 1f / n : w.PopulationW / popSum;
        }
    }

    /// <summary>
    /// Kentsel faktör (0-1): il kimliğinden türetilen sabit tohum +
    /// `DeterministicRNG` ilk çekilişi. Global RNG state'i kaydedilip geri
    /// yüklenir, böylece çağrı dünya simülasyonunun rastgelelik akışını bozmaz.
    /// </summary>
    private static float UrbanScore(ProvinceData p)
    {
        ulong saved = DeterministicRNG.GetState();
        DeterministicRNG.LoadState((ulong)StableHash(p.Id + "|urban"));
        float value = DeterministicRNG.NextFloat();
        DeterministicRNG.LoadState(saved);
        return value;
    }

    /// <summary>
    /// Bir ülkenin `CountryData` değerlerini il paylara göre dağıtır (harmanlama).
    /// Son il kalan farkı alır → il toplamı ülke toplamına tam eşittir.
    /// </summary>
    public static void ReconcileCountry(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return;

        var ids = _byCountry.GetValueOrDefault(countryId);
        if (ids == null || ids.Count == 0) return;

        var data = CountryDatabase.Get(countryId);
        if (data == null)
        {
            // CountryDatabase'de kaydı yok (minör ülke) — payları sıfırla.
            foreach (var id in ids)
            {
                if (!_weights.TryGetValue(id, out var w)) continue;
                w.Civ = w.Mil = w.Dock = 0;
                w.Oil = w.Rubber = w.Steel = w.Aluminum = w.Tungsten = w.Chromium = 0;
                w.Population = 0;
            }
            return;
        }

        DistributeInts(ids, data.CivFactories, w => w.FactoryW, (w, v) => w.Civ = v);
        DistributeInts(ids, data.MilFactories, w => w.FactoryW, (w, v) => w.Mil = v);
        DistributeInts(ids, data.Dockyards, w => w.FactoryW, (w, v) => w.Dock = v);
        DistributeInts(ids, data.Oil, w => w.ResourceW, (w, v) => w.Oil = v);
        DistributeInts(ids, data.Rubber, w => w.ResourceW, (w, v) => w.Rubber = v);
        DistributeInts(ids, data.Steel, w => w.ResourceW, (w, v) => w.Steel = v);
        DistributeInts(ids, data.Aluminum, w => w.ResourceW, (w, v) => w.Aluminum = v);
        DistributeInts(ids, data.Tungsten, w => w.ResourceW, (w, v) => w.Tungsten = v);
        DistributeInts(ids, data.Chromium, w => w.ResourceW, (w, v) => w.Chromium = v);

        DistributePopulation(ids, data.Population);
    }

    /// <summary>Tüm ülkelerin harmanlanmasını yapar.</summary>
    public static void ReconcileAll()
    {
        foreach (var countryId in _byCountry.Keys.ToList())
            ReconcileCountry(countryId);
    }

    /// <summary>
    /// Bir tam sayı değerini il paylarına böler (kal-artık yöntemi): her il
    /// payının tabanını alır, kalan birimler en büyük küsüratlı illere verilir.
    /// Böylece toplam tam korunur *ve* yüksek ağırlıklı il düşük ağırlıklına göre
    /// avantajlı kalır — "son il her şeyi alır" sapması olmaz.
    /// </summary>
    private static void DistributeInts(
        List<string> ids, long total,
        Func<Weight, float> share, Action<Weight, int> setter)
    {
        if (ids == null || ids.Count == 0) return;

        if (total <= 0)
        {
            foreach (var id in ids)
                if (_weights.TryGetValue(id, out var w0)) setter(w0, 0);
            return;
        }

        var entries = new List<(Weight W, int Value, double Frac)>();
        long assigned = 0;
        foreach (var id in ids)
        {
            if (!_weights.TryGetValue(id, out var w)) continue;
            // DİKKAT: çarpım double'da yapılmalı. long * float → float olur ve
            // milyarlık nüfusta 128'lik adımlarla yuvarlanır → toplam kayar.
            double exact = (double)total * share(w);
            int v = (int)Math.Floor(exact);
            if (v < 0) v = 0;
            entries.Add((w, v, exact - v));
            assigned += v;
        }

        long leftover = total - assigned;
        if (leftover > 0)
        {
            var order = entries.Select((e, i) => (E: e, I: i))
                .OrderByDescending(t => t.E.Frac)
                .ThenBy(t => t.E.W.ProvinceId, StringComparer.Ordinal)
                .ToList();
            for (int k = 0; k < leftover && k < order.Count; k++)
            {
                int i = order[k].I;
                entries[i] = (entries[i].W, entries[i].Value + 1, entries[i].Frac);
            }
            // Pay toplamı 1'e tam eşit olmadığında (float32 normalize) fazlalık
            // tekrar oluşabilir; kalanı en ağırlıklı ile ver ki toplam korunsun.
            long stillLeft = leftover - order.Count;
            if (stillLeft > 0 && order.Count > 0)
            {
                int top = order[0].I;
                entries[top] = (entries[top].W, entries[top].Value + (int)stillLeft, entries[top].Frac);
            }
        }

        foreach (var e in entries) setter(e.W, e.Value);
    }

    /// <summary>
    /// Ülke nüfusunu il nüfus paylarına böler (kal-artık yöntemi — aynı gerekçe).
    /// Ülke nüfusu CountryData'dan, il payları kültür/nüfus katmanından gelir.
    /// </summary>
    private static void DistributePopulation(List<string> ids, long total)
    {
        if (ids == null || ids.Count == 0) return;

        if (total <= 0)
        {
            foreach (var id in ids)
                if (_weights.TryGetValue(id, out var w0)) w0.Population = 0;
            return;
        }

        var entries = new List<(Weight W, long Value, double Frac)>();
        long assigned = 0;
        foreach (var id in ids)
        {
            if (!_weights.TryGetValue(id, out var w)) continue;
            // DİKKAT: çarpım double'da yapılmalı — aksi hâlde float32 yuvarlaması
            // milyarlık nüfusta toplamı kaydırır.
            double exact = (double)total * w.PopulationW;
            long v = (long)Math.Floor(exact);
            if (v < 0) v = 0;
            entries.Add((w, v, exact - v));
            assigned += v;
        }

        long leftover = total - assigned;
        if (leftover > 0)
        {
            var order = entries.Select((e, i) => (E: e, I: i))
                .OrderByDescending(t => t.E.Frac)
                .ThenBy(t => t.E.W.ProvinceId, StringComparer.Ordinal)
                .ToList();
            for (int k = 0; k < leftover && k < order.Count; k++)
            {
                int i = order[k].I;
                entries[i] = (entries[i].W, entries[i].Value + 1, entries[i].Frac);
            }
            long stillLeft = leftover - order.Count;
            if (stillLeft > 0 && order.Count > 0)
            {
                int top = order[0].I;
                entries[top] = (entries[top].W, entries[top].Value + stillLeft, entries[top].Frac);
            }
        }

        foreach (var e in entries) e.W.Population = e.Value;
    }


    // ===== Sorgu API'sı (M47.3 ve M47.2 tüketicileri) =====

    /// <summary>İlin fabrika payı (ülke toplamına oranla, 0-1).</summary>
    public static float FactoryShareOf(string provinceId) => Get(provinceId)?.FactoryW ?? 0f;

    /// <summary>İlin nüfus payı (ülke toplamına oranla, 0-1).</summary>
    public static float PopulationShareOf(string provinceId) => Get(provinceId)?.PopulationW ?? 0f;

    /// <summary>İlin kaynak payı (ülke toplamına oranla, 0-1).</summary>
    public static float ResourceShareOf(string provinceId) => Get(provinceId)?.ResourceW ?? 0f;

    /// <summary>Bir ülkenin harmanlanmış toplam fabrika sayısı.</summary>
    public static int TotalFactoriesOf(string countryId)
    {
        int total = 0;
        foreach (var w in ForCountry(countryId))
            total += w.TotalFactories;
        return total;
    }

    /// <summary>Bir ülkenin harmanlanmış toplam kaynağı (il toplamı).</summary>
    public static int TotalResourceOf(string countryId, string key)
    {
        int total = 0;
        foreach (var w in ForCountry(countryId))
            total += ResourceOf(w, key);
        return total;
    }

    /// <summary>Bir ülkenin harmanlanmış toplam nüfusu (il toplamı).</summary>
    public static long TotalPopulationOf(string countryId)
    {
        long total = 0;
        foreach (var w in ForCountry(countryId))
            total += w.Population;
        return total;
    }

    private static int ResourceOf(Weight w, string key) => key switch
    {
        "Oil" => w.Oil,
        "Rubber" => w.Rubber,
        "Steel" => w.Steel,
        "Aluminum" => w.Aluminum,
        "Tungsten" => w.Tungsten,
        "Chromium" => w.Chromium,
        _ => 0
    };

    /// <summary>
    /// Harmanlama tutarlılık kontrolü: bir ülkenin il toplamı `CountryData`
    /// değerine eşit mi? (Tolerans: yalnızca son ilde kalan yuvarlama farkı.)
    /// </summary>
    public static bool IsReconciled(string countryId, int tolerancePercent)
    {
        var data = CountryDatabase.Get(countryId);
        if (data == null) return true;

        int[] actual =
        {
            TotalFactoriesOf(countryId),
            TotalResourceOf(countryId, "Oil"),
            TotalResourceOf(countryId, "Rubber"),
            TotalResourceOf(countryId, "Steel"),
            TotalResourceOf(countryId, "Aluminum"),
            TotalResourceOf(countryId, "Tungsten"),
            TotalResourceOf(countryId, "Chromium")
        };
        int[] expected =
        {
            data.CivFactories + data.MilFactories + data.Dockyards,
            data.Oil, data.Rubber, data.Steel,
            data.Aluminum, data.Tungsten, data.Chromium
        };

        for (int i = 0; i < actual.Length; i++)
        {
            int tol = Math.Max(1, expected[i] * tolerancePercent / 100);
            if (Math.Abs(actual[i] - expected[i]) > tol)
                return false;
        }
        return true;
    }

    /// <summary>
    /// İlleri yeni sahibine taşır (M44.3/M44.4/M42 il devri). Ekonomi katmanı
    /// zaten üretilmiş olmalı — kayıt yoksa hiç taşımaz.
    ///
    /// Dev sonrası her iki tarafın harmanlaması geçersizleşir → yenilenir.
    /// Harmanlama `CountryData`'yı ellemez; il payları taşınır ve görünür,
    /// ülke toplamının yeniden hesaplanması M47.2'nin işidir.
    /// </summary>
    public static bool ReassignCountry(List<string> provinceIds, string newCountryId)
    {
        if (provinceIds == null || string.IsNullOrEmpty(newCountryId)) return false;

        int moved = 0;
        foreach (var id in provinceIds)
        {
            if (!_weights.TryGetValue(id, out var w)) continue;

            if (_byCountry.TryGetValue(w.CountryId, out var oldList))
                oldList.Remove(id);

            w.CountryId = newCountryId;
            moved++;

            if (!_byCountry.TryGetValue(newCountryId, out var newList))
            {
                newList = new List<string>();
                _byCountry[newCountryId] = newList;
            }
            if (!newList.Contains(id)) newList.Add(id);
        }

        // Boşalmış eski ülke anahtarlarını temizle
        var empty = _byCountry.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
        foreach (var key in empty) _byCountry.Remove(key);

        ReconcileAll();
        return moved > 0;
    }

    /// <summary>Yeni oyunda sıfırlar.</summary>
    public static void ResetForNewGame()
    {
        _weights.Clear();
        _byCountry.Clear();
        GD.Print("[ProvEconomy] Yeni oyun için sıfırlandı.");
    }

    // ===== M47.2: İl devrinde ekonomik yeniden hesaplama =====

    /// <summary>
    /// M47.2: Tek ilin sahibi değiştiğinde ekonomiyi günceller.
    ///
    /// `ProvinceOwnershipSystem.OnOwnerChanged` olayından çağrılır — böylece M42
    /// barış konferansı, M44.3 kurgu kurma, M44.4 serbest bırakma ve ilhak gibi
    /// tüm devir noktaları tek merkezden geçer (15 ayrı çağrı yerine tek yerde).
    ///
    /// Üretim taşınır: ilin o ana kadarki harmanlanmış payı (fabrika/kaynak/nüfus)
    /// veren ülkenin `CountryData` toplamından düşülüp alan ülkesine eklenir.
    /// Böylece dünya genelinde toplam üretim korunur — ilhak eden ülke kurbanının
    /// fabrikalarını kazanır, kurbanı boşalmaz.
    /// </summary>
    public static void HandleOwnershipChange(string provinceId, string oldCountryId, string newCountryId)
    {
        // Ekonomi katmanı henüz üretilmediyse (kurulumdan önce devir) hiçbir şey
        // yapma — bu katman oyun kurulumunda üretilir.
        if (_weights.Count == 0) return;
        if (!_weights.TryGetValue(provinceId, out var w)) return;

        // GetProvinceOwner sahipsiz il için "?" döndürür — bu da gerçek bir ülke
        // kimliği değil, "kayıt yok" anlamına gelir. Bu yüzden hem boş hem "?"
        // durumunda ilin kendi ağırlık kaydındaki ülkeye düşülür.
        bool knownOld = !string.IsNullOrEmpty(oldCountryId) && oldCountryId != "?";
        string from = knownOld ? oldCountryId : w.CountryId;
        string to = newCountryId;
        if (string.IsNullOrEmpty(to) || from == to) return;

        // 1) İlin mevcut payını oku (hâlâ veren ülkenin içinde)
        int civ = w.Civ, mil = w.Mil, dock = w.Dock;
        int oil = w.Oil, rubber = w.Rubber, steel = w.Steel;
        int alu = w.Aluminum, tung = w.Tungsten, chrome = w.Chromium;
        long pop = w.Population;

        // 2) Üretimi CountryData'dan taşı (sadece il gerçekten pay taşıyorsa)
        var fromData = CountryDatabase.Get(from);
        var toData = CountryDatabase.Get(to);
        bool moveValues = fromData != null && toData != null;

        if (moveValues)
        {
            fromData.CivFactories -= civ;
            fromData.MilFactories -= mil;
            fromData.Dockyards -= dock;
            fromData.Oil -= oil;
            fromData.Rubber -= rubber;
            fromData.Steel -= steel;
            fromData.Aluminum -= alu;
            fromData.Tungsten -= tung;
            fromData.Chromium -= chrome;
            fromData.Population -= pop;

            toData.CivFactories += civ;
            toData.MilFactories += mil;
            toData.Dockyards += dock;
            toData.Oil += oil;
            toData.Rubber += rubber;
            toData.Steel += steel;
            toData.Aluminum += alu;
            toData.Tungsten += tung;
            toData.Chromium += chrome;
            toData.Population += pop;
        }

        // 3) Kültür ve ağırlık katmanını taşı (ReassignCountry içindeki
        //    ReconcileAll, CountryData taşınmasından SONRA çalıştığı için tüm
        //    ülkeler yeni toplamlara göre yeniden adımlanır).
        ProvinceCultureSystem.ReassignCountry(new List<string> { provinceId }, to);
        ReassignCountry(new List<string> { provinceId }, to);

        GD.Print($"[ProvEconomy] Devir: {provinceId} {from} → {to} — taşınan " +
                 $"{civ + mil + dock} fabrika, {pop} kişi" +
                 (moveValues ? "" : " (CountryData taşınmadı: kayıt yok)"));
    }

    // ===== M47.3: Kaynak açığının ekonomik sonucu =====

    /// <summary>
    /// M47.3 — Bir kaynağın ülkedeki durumu: ihtiyaç, arz, açık/tokluk ve bedeli.
    ///
    /// İhtiyaç ülkenin sanayi ve nüfus hacminden türetilir (fabrika + milyon kişi);
    /// arz `CountryData`'daki üretim alanıdır. M47.2 sayesinde il devri bu arzı
    /// fiilen taşıdığı için, bir ülke zengin illerini kaybettiğinde açık burada
    /// kendiliğinden ortaya çıkar — ayrı bir olay dinlemeye gerek yoktur.
    ///
    /// Açık derinleştikçe birim ithalat maliyeti artar (kıt piyasa), toklukta ise
    /// ihracat geliri doğar. Hiçbir yerde rastgelelik kullanılmaz.
    /// </summary>
    public sealed class ResourceBalance
    {
        public string CountryId = "";
        public string Key = "";           // "Oil", "Steel"...
        public double Need;               // günlük ihtiyaç
        public double Supply;             // ülke üretimi (CountryData)
        public double Deficit;            // max(0, Need − Supply)
        public double Surplus;            // max(0, Supply − Need)
        public double ImportPricePerUnit; // açık derinliğine göre artan birim fiyat
        public int ConvoyCost;            // açığı karşılamak için gereken konvoy
    }

    // Kaynağın endüstriyel ağırlığı: aynı fabrika/nüfus petrolu çelikten çok tüketir.
    private static readonly Dictionary<string, double> NeedWeight = new(StringComparer.Ordinal)
    {
        { "Oil", 1.20 },
        { "Rubber", 1.00 },
        { "Steel", 1.00 },
        { "Aluminum", 0.80 },
        { "Tungsten", 0.70 },
        { "Chromium", 0.60 }
    };

    private static double SupplyOf(CountryData d, string key) => key switch
    {
        "Oil" => d.Oil,
        "Rubber" => d.Rubber,
        "Steel" => d.Steel,
        "Aluminum" => d.Aluminum,
        "Tungsten" => d.Tungsten,
        "Chromium" => d.Chromium,
        _ => 0
    };

    /// <summary>Ülkenin bir kaynağa günlük ihtiyacı (fabrika + nüfus hacminden).</summary>
    public static double ResourceNeedOf(string countryId, string key)
    {
        var d = CountryDatabase.Get(countryId);
        if (d == null) return 0;
        EnsureLoaded();
        if (!NeedWeight.TryGetValue(key, out double w)) return 0;

        double million = d.Population / 1_000_000.0;
        return w * (d.TotalFactories * S.NeedPerFactory + million * S.NeedPerMillionPop);
    }

    /// <summary>Bir kaynağın açık/tokluk dökümü.</summary>
    public static ResourceBalance BalanceFor(string countryId, string key)
    {
        var bal = new ResourceBalance { CountryId = countryId, Key = key };
        var d = CountryDatabase.Get(countryId);
        if (d == null) return bal;

        EnsureLoaded();
        bal.Need = ResourceNeedOf(countryId, key);
        bal.Supply = SupplyOf(d, key);
        bal.Deficit = Math.Max(0.0, bal.Need - bal.Supply);
        bal.Surplus = Math.Max(0.0, bal.Supply - bal.Need);

        // Açık, ihtiyacın ne kadarıysa fiyat o oranda artar (0 → taban fiyat).
        double depth = bal.Need > 0 ? bal.Deficit / bal.Need : 0.0;
        bal.ImportPricePerUnit = S.ImportPriceBase * (1.0 + depth * S.DeficitPriceScale);
        bal.ConvoyCost = (int)Math.Ceiling(bal.Deficit / Math.Max(0.0001, S.UnitsPerConvoy));
        return bal;
    }

    /// <summary>Takip edilen tüm kaynaklar için döküm (deterministik sırada).</summary>
    public static List<ResourceBalance> BalancesOf(string countryId)
    {
        var list = new List<ResourceBalance>();
        foreach (var key in ResourceKeys) list.Add(BalanceFor(countryId, key));
        return list;
    }

    /// <summary>Ülkenin toplam kaynak açığı (birim/gün).</summary>
    public static double TotalDeficitOf(string countryId)
    {
        double sum = 0;
        foreach (var key in ResourceKeys) sum += BalanceFor(countryId, key).Deficit;
        return sum;
    }

    /// <summary>Ülkenin toplam kaynak tokluğu (birim/gün).</summary>
    public static double TotalSurplusOf(string countryId)
    {
        double sum = 0;
        foreach (var key in ResourceKeys) sum += BalanceFor(countryId, key).Surplus;
        return sum;
    }

    /// <summary>
    /// Çıkarım verimliliği (1.0 → tabana kadar). Açık, ülkenin kendi
    /// üretim kapasitesinden (`CountryData`) hesaplanır — stoktan değil. Bu yüzden
    /// ceza üretime uygulansa bile bir ölüm spirali oluşmaz: kapasite değişmedikçe
    /// verimlilik sabit kalır, yalnızca M47.2 il devriyle birlikte değişir.
    /// </summary>
    public static double ExtractionEfficiency(string countryId)
    {
        EnsureLoaded();
        double need = 0, deficit = 0;
        foreach (var key in ResourceKeys)
        {
            var bal = BalanceFor(countryId, key);
            need += bal.Need;
            deficit += bal.Deficit;
        }
        if (need <= 0) return 1.0;

        double eff = 1.0 - deficit / need;
        return Math.Clamp(eff, Math.Min(S.MinEfficiency, 1.0), 1.0);
    }

    /// <summary>Zengin kaynakların ihracat geliri (günlük).</summary>
    public static double TradeIncomeOf(string countryId)
    {
        EnsureLoaded();
        var d = CountryDatabase.Get(countryId);
        if (d == null) return 0;

        double income = 0;
        foreach (var key in ResourceKeys)
        {
            var bal = BalanceFor(countryId, key);
            income += bal.Surplus * S.IncomePerSurplusUnit;
        }
        return income;
    }

    /// <summary>Açık kaynakları karşılamak için gereken toplam konvoy sayısı.</summary>
    public static int ConvoyDemandOf(string countryId)
    {
        int sum = 0;
        foreach (var key in ResourceKeys) sum += BalanceFor(countryId, key).ConvoyCost;
        return sum;
    }

    // ===== M47.4: Save/Load =====

    /// <summary>
    /// M47.4 — Ağırlık katmanının kaydı.
    ///
    /// Hem ham ağırlıklar (ülke içi normalize paylar) hem de `CountryData` ile
    /// harmanlanmış değerler kaydedilir. Harmanlanmış kısım özellikle önemli:
    /// M47.2 il devri bu değerleri fiilen taşıdığı için, yalnızca payları
    /// kaydetmek devir sonrası durumu geri getiremezdi.
    /// </summary>
    public static SaveSystem.ProvinceEconomySave GetSaveData()
    {
        var list = new List<SaveSystem.ProvinceEconomyEntry>(_weights.Count);
        foreach (var w in _weights.Values)
        {
            list.Add(new SaveSystem.ProvinceEconomyEntry
            {
                ProvinceId = w.ProvinceId,
                CountryId = w.CountryId,
                Territory = w.Territory,
                Urban = w.Urban,
                Coast = w.Coast,
                ResourceW = w.ResourceW,
                FactoryW = w.FactoryW,
                PopulationW = w.PopulationW,
                Civ = w.Civ,
                Mil = w.Mil,
                Dock = w.Dock,
                Oil = w.Oil,
                Rubber = w.Rubber,
                Steel = w.Steel,
                Aluminum = w.Aluminum,
                Tungsten = w.Tungsten,
                Chromium = w.Chromium,
                Population = w.Population
            });
        }
        return new SaveSystem.ProvinceEconomySave { Weights = list.ToArray() };
    }

    public static void LoadSaveData(SaveSystem.ProvinceEconomySave data)
    {
        _weights.Clear();
        _byCountry.Clear();

        if (data?.Weights == null || data.Weights.Length == 0)
        {
            GD.Print("[ProvEconomy] Kayıtta il ekonomi ağırlığı yok — CountryData'dan türetilecek.");
            return;
        }

        foreach (var e in data.Weights)
        {
            if (e == null || string.IsNullOrEmpty(e.ProvinceId)) continue;

            var w = new Weight
            {
                ProvinceId = e.ProvinceId,
                CountryId = e.CountryId ?? "",
                Territory = e.Territory,
                Urban = e.Urban,
                Coast = e.Coast,
                ResourceW = e.ResourceW,
                FactoryW = e.FactoryW,
                PopulationW = e.PopulationW,
                Civ = e.Civ,
                Mil = e.Mil,
                Dock = e.Dock,
                Oil = e.Oil,
                Rubber = e.Rubber,
                Steel = e.Steel,
                Aluminum = e.Aluminum,
                Tungsten = e.Tungsten,
                Chromium = e.Chromium,
                Population = e.Population
            };
            _weights[w.ProvinceId] = w;

            if (!string.IsNullOrEmpty(w.CountryId))
            {
                if (!_byCountry.TryGetValue(w.CountryId, out var list))
                {
                    list = new List<string>();
                    _byCountry[w.CountryId] = list;
                }
                list.Add(w.ProvinceId);
            }
        }

        GD.Print($"[ProvEconomy] İl ekonomi ağırlıkları yüklendi: {_weights.Count}");
    }

    /// <summary>Platformdan bağımsız kararlı hash (FNV-1a).</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in s)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= 16777619;
                hash ^= (byte)(c >> 8);
                hash *= 16777619;
            }
            return (int)hash;
        }
    }
}
