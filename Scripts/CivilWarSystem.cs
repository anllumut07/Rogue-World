using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// M50.1 — İç çatışma: çöküş puanı ve isyan patlak verme.
///
/// Oyun şu ana kadar ülkeleri sonsu dek ayakta tutuyordu. Bu sistem stabilite,
/// halk memnuniyeti, siyasi huzursuzluk, savaş desteği ve ekonomik bunalımı
/// tek bir "çöküş puanı"na toplar; puan eşiği aşınca ülkenin belirli illerinde
/// deterministik bir isyan patlak verir.
///
/// Bu adım YALNIZCA puanı ve isyanın doğduğu illeri üretir. İsyancının ayrı bir
/// ülke olarak doğması, merkez hükümetiyle savaşı ve dünyanın tanıma süreci
/// M50.2'dedir.
///
/// Determinizm: il seçiminde RNG YOK. Aday iller sabit bir FNV-1a hash'iyle
/// sıralanır, tohum il en küçük hashli adaydır ve yayılma komşuluk
/// (<see cref="ProvinceData.NeighborIds"/>) üzerinden aynı hash sırasıyla
/// BFS yapılır. Böylece seçim; koleksiyon sırasından, dosya okuma sırasından
/// ve kayıt/yüklemeden bağımsız olarak her zaman aynı sonucu verir.
/// </summary>
public static class CivilWarSystem
{
    private const string DataPath = "res://Data/civil_war.json";

    private sealed class CivilWarSettings
    {
        // Çöküş puanı ağırlıkları — toplamı 1 olmak zorunda değil, normalize edilir.
        public double StabilityWeight = 0.30;
        public double SatisfactionWeight = 0.25;
        public double UnrestWeight = 0.20;
        public double WarSupportWeight = 0.10;
        public double SlumpWeight = 0.15;

        public double OutbreakThreshold = 55.0;
        public int OutbreakCooldownDays = 180;
        public double MaxProvinceShare = 0.35;
        public int MinProvinces = 1;
        public int KeepHomeProvinces = 1;

        // Huzursuzluk sayaçlarını 0-100'e ölçekler (sayaçlar kümülatiftir).
        public double UnrestProtestScale = 6.0;
        public double UnrestStrikeScale = 12.0;
        public double UnrestVoteScale = 5.0;
        public double UnrestCoupScale = 45.0;
        public double UnrestRecencyDays = 30.0;

        // Ekonomik bunalım eşikleri (tam puana ulaşma noktaları).
        public double SlumpGrowthFloor = -0.5;
        public double SlumpUnemploymentFull = 12.0;
        public double SlumpDebtFull = 90.0;
        public double SlumpDeficitFull = -6.0;

        // Ekonomik bunalımın bileşen tavanları (toplamı 100).
        public double SlumpGrowthMax = 40.0;
        public double SlumpUnemploymentMax = 25.0;
        public double SlumpDebtMax = 20.0;
        public double SlumpDeficitMax = 15.0;
    }

    private sealed class CivilWarRoot
    {
        public int Version { get; set; } = 1;
        public CivilWarSettings Settings { get; set; } = new();
    }

    private static CivilWarSettings S = new();
    private static bool _loaded;

    /// <summary>Çöküş puanının bileşenleri — test ve UI için ayrı ayrı okunabilir.</summary>
    public class CollapseBreakdown
    {
        public string CountryId = "";
        public double Stability;      // 100 - stabilite
        public double Satisfaction;   // 100 - ortalama halk memnuniyeti
        public double Unrest;         // huzursuzluk olaylarının tazeliğe göre ağırlıklı toplamı
        public double WarSupport;     // savaştaysa 100 - savaş desteği, değilse 0
        public double Slump;          // büyüme + işsizlik + borç + açık

        public double Total;
        public double Threshold;
        public bool Reached;

        public double WeightSum => WStability + WSatisfaction + WUnrest + WWarSupport + WSlump;

        // Ağırlıklar ayar dosyasından gelir; özet satırında görünsün diye burada tutulur.
        public double WStability, WSatisfaction, WUnrest, WWarSupport, WSlump;
    }

    /// <summary>Patlak vermiş bir isyanın durumu.</summary>
    public class RebellionState
    {
        public string CountryId = "";        // merkez hükümet (isyancı değil)
        public long StartDay;                // patlak verdiği gün
        public string RebelCulture = "";     // isyancı illerin baskın kültürü
        public List<string> Provinces = new(); // deterministik seçilmiş il kimlikleri (sıralı)
    }

    private static readonly List<RebellionState> _active =
        new();

    // Ülke → son patlak günü (bekleme süresi için). Kayda girer.
    private static readonly Dictionary<string, long> _lastOutbreakDay =
        new(StringComparer.OrdinalIgnoreCase);

    // İl kimliği → komşular (sıralı, tekil). Dunya haritası bir kez verilir.
    private static readonly Dictionary<string, string[]> _neighbors =
        new(StringComparer.Ordinal);

    private static bool _worldReady;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            using var file = Godot.FileAccess.Open(DataPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PrintErr("[CivilWar] civil_war.json açılamadı — varsayılan ayarlar kullanılıyor.");
                return;
            }
            var root = JsonSerializer.Deserialize<CivilWarRoot>(file.GetAsText(),
                new JsonSerializerOptions
                {
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true
                });
            if (root?.Settings != null)
            {
                S = root.Settings;
                GD.Print($"[CivilWar] Ayarlar yüklendi: eşik={S.OutbreakThreshold}, " +
                         $"ağırlık=({S.StabilityWeight}/{S.SatisfactionWeight}/{S.UnrestWeight}/" +
                         $"{S.WarSupportWeight}/{S.SlumpWeight}), maks_il_payı={S.MaxProvinceShare}");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[CivilWar] civil_war.json okunamadı ({e.Message}) — varsayılan ayarlar.");
        }
    }

    /// <summary>Aktif isyan sayısı (log/test).</summary>
    public static int ActiveCount => _active.Count;

    public static IReadOnlyList<RebellionState> Active => _active;

    public static RebellionState RebellionOf(string countryId) =>
        _active.FirstOrDefault(r =>
            string.Equals(r.CountryId, countryId, StringComparison.OrdinalIgnoreCase));

    public static bool IsRebelling(string countryId) =>
        _active.Any(r => string.Equals(r.CountryId, countryId, StringComparison.OrdinalIgnoreCase));

    public static long LastOutbreakDay(string countryId) =>
        _lastOutbreakDay.TryGetValue(countryId, out var d) ? d : long.MinValue;

    public static void ResetForNewGame()
    {
        _active.Clear();
        _lastOutbreakDay.Clear();
        GD.Print("[CivilWar] Yeni oyun için sıfırlandı.");
    }

    // ===== Çöküş puanı =====

    /// <summary>
    /// Ülkenin çöküş puanını bileşenlerine ayırarak hesaplar. Saf fonksiyondur:
    /// durumu değiştirmez, RNG kullanmaz, aynı girdiye her zaman aynı sonucu verir.
    /// </summary>
    public static CollapseBreakdown Score(string countryId, long currentDay)
    {
        EnsureLoaded();

        var b = new CollapseBreakdown
        {
            CountryId = countryId ?? "",
            Threshold = S.OutbreakThreshold,
            WStability = S.StabilityWeight,
            WSatisfaction = S.SatisfactionWeight,
            WUnrest = S.UnrestWeight,
            WWarSupport = S.WarSupportWeight,
            WSlump = S.SlumpWeight
        };

        var data = CountryDatabase.Get(countryId);
        if (data == null) return b;

        // 1) Stabilite
        b.Stability = Clamp01To100(100.0 - data.StabilityPercent);

        // 2) Halk memnuniyeti — tüm grupların ortalaması
        b.Satisfaction = Clamp01To100(100.0 - AverageSatisfaction(countryId));

        // 3) Siyasi huzursuzluk
        b.Unrest = UnrestPart(countryId, currentDay);

        // 4) Savaş desteği — yalnızca savaş varken baskı yapar
        b.WarSupport = WarSystem.IsAtWarWith(countryId)
            ? Clamp01To100(100.0 - data.WarSupportPercent)
            : 0.0;

        // 5) Ekonomik bunalım
        b.Slump = SlumpPart(countryId);

        double sum = b.WStability * b.Stability
                   + b.WSatisfaction * b.Satisfaction
                   + b.WUnrest * b.Unrest
                   + b.WWarSupport * b.WarSupport
                   + b.WSlump * b.Slump;
        b.Total = sum / b.WeightSum;
        b.Reached = b.Total >= S.OutbreakThreshold;
        return b;
    }

    public static double CollapseScore(string countryId, long currentDay) =>
        Score(countryId, currentDay).Total;

    private static double AverageSatisfaction(string countryId)
    {
        double total = 0;
        int n = 0;
        foreach (var g in PublicSatisfactionSystem.Groups)
        {
            if (g == null || string.IsNullOrEmpty(g.Id)) continue;
            total += PublicSatisfactionSystem.GetSatisfaction(countryId, g.Id);
            n++;
        }
        return n == 0 ? 50.0 : total / n;
    }

    /// <summary>
    /// Kümülatif huzursuzluk sayaçlarını tazeliğe göre ağırlıklandırır. Sayaçlar
    /// oyun boyunca biriktigi için ham değer yerine "son olaydan bu yana geçen
    /// gün" ile sönümlenir: uzun süre sakınan ülke kadim bir kırgınlık taşır,
    /// yeni olanlar ise anında etki eder.
    /// </summary>
    private static double UnrestPart(string countryId, long currentDay)
    {
        var st = PoliticalUnrestSystem.States.TryGetValue(countryId, out var s) ? s : null;
        if (st == null) return 0.0;

        double raw = st.ProtestCount * S.UnrestProtestScale
                   + st.StrikeCount * S.UnrestStrikeScale
                   + st.VoteOfConfidenceCount * S.UnrestVoteScale
                   + st.CoupAttemptCount * S.UnrestCoupScale;
        if (raw <= 0.0) return 0.0;

        long last = Math.Max(
            Math.Max(st.LastProtestDay, st.LastStrikeDay),
            Math.Max(st.LastVoteOfConfidenceDay, long.MinValue));
        double since = Math.Max(0, currentDay - last);
        double freshness = 1.0 - Math.Min(since / S.UnrestRecencyDays, 1.0);

        return Clamp01To100(raw * freshness);
    }

    private static double SlumpPart(string countryId)
    {
        double growth = StatisticsSystem.GetValue(countryId, "gdp_growth");
        double unemployment = StatisticsSystem.GetValue(countryId, "unemployment");
        double debt = StatisticsSystem.GetValue(countryId, "public_debt");
        // Açık için bütçe sisteminin ham dengesi değil, onun beslediği gösterge
        // okunur: ham bakiye her ülkede yapısal olarak doygun (vergi < harcama),
        // kanalı hiç ayırt edemez hale getiriyordu.
        double deficit = StatisticsSystem.GetValue(countryId, "budget_deficit");

        // Büyüme: 0 ve üzeri bunalım sayılmaz, tabana indikçe tavanına ulaşır.
        double growthPart = growth >= 0.0
            ? 0.0
            : Clamp01To100((-growth / -S.SlumpGrowthFloor) * S.SlumpGrowthMax);

        double unempPart = Clamp01To100(
            (unemployment / S.SlumpUnemploymentFull) * S.SlumpUnemploymentMax);

        double debtPart = Clamp01To100(
            (debt / S.SlumpDebtFull) * S.SlumpDebtMax);

        // Açık negatif gelir; tabanına indikçe tavanına ulaşır.
        double deficitPart = deficit >= 0.0
            ? 0.0
            : Clamp01To100((-deficit / -S.SlumpDeficitFull) * S.SlumpDeficitMax);

        return Clamp01To100(growthPart + unempPart + debtPart + deficitPart);
    }

    // ===== İl seçimi (deterministik, RNG yok) =====

    /// <summary>
    /// İsyanın doğacağı illeri seçer: en kalabalık azınlık kültürünün
    /// COĞRAFY olarak bitişik en büyük bölgesi. İmparatorluk dağılması bir
    /// bölgenin kopmasıdır; dağınık tekillikler değil. Azınlık yoksa aynı
    /// yordam baskın kültürün en büyük bitişik bölgesinden alınır. Hiç kültür
    /// kaydı yoksa tüm il havuzu aday olur.
    /// </summary>
    public static List<string> SelectRebelProvinces(string countryId)
    {
        EnsureLoaded();
        var result = new List<string>();
        if (string.IsNullOrEmpty(countryId)) return result;

        var owned = ProvinceOwnershipSystem.GetProvincesByOwner(countryId);
        if (owned == null || owned.Count == 0) return result;

        // Merkez hükümet hiçbir koşulda tamamen kaybolmasın: en az bir il kalır.
        int floor = Math.Min(S.KeepHomeProvinces, owned.Count);
        int shareCap = (int)Math.Round(owned.Count * S.MaxProvinceShare, MidpointRounding.AwayFromZero);
        int cap = Math.Max(Math.Min(S.MinProvinces, owned.Count - floor),
                           Math.Min(shareCap, owned.Count - floor));
        if (cap <= 0) return result;

        var pool = ResolveCandidatePool(countryId, owned, out string rebelCulture);
        _lastCulture = rebelCulture;
        _lastPoolSize = pool.Count;
        if (pool.Count == 0) return result;

        // Sıralı hash: kimliklerin derlenme sırasından bağımsız, kararlı sıra.
        var inPool = new HashSet<string>(pool, StringComparer.Ordinal);
        var components = ConnectedComponents(pool, inPool);

        // En büyük bölge; eşitlikte en düşük hashli tohumu olan kazanır.
        var best = components
            .OrderByDescending(c => c.Count)
            .ThenBy(c => StableHash(countryId + "|" + c[0]))
            .First();

        int take = Math.Min(cap, best.Count);
        var ordered = best
            .OrderBy(id => StableHash(countryId + "|" + id))
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToList();

        var seed = ordered[0];
        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { seed };
        queue.Enqueue(seed);

        while (queue.Count > 0 && result.Count < take)
        {
            string cur = queue.Dequeue();
            result.Add(cur);

            var next = NeighborsOf(cur)
                .Where(nb => inPool.Contains(nb) && !seen.Contains(nb))
                .OrderBy(nb => StableHash(countryId + "|" + nb))
                .ThenBy(nb => nb, StringComparer.Ordinal);
            foreach (var nb in next)
            {
                if (seen.Contains(nb)) continue;
                seen.Add(nb);
                queue.Enqueue(nb);
            }
        }

        return result;
    }

    /// <summary>
    /// Aday havuzunun bağlı bileşenlerini döndürür. Bileşen içindeki sıra
    /// deterministiktir (hash); bileşenlerin kendi sırası çağıran tarafa bırakılır.
    /// </summary>
    private static List<List<string>> ConnectedComponents(List<string> pool, HashSet<string> inPool)
    {
        var components = new List<List<string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in pool.OrderBy(id => StableHash(id), Comparer<ulong>.Create(static (a, b) => a.CompareTo(b))))
        {
            if (seen.Contains(start)) continue;
            var component = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(start);
            seen.Add(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                component.Add(cur);
                foreach (var nb in NeighborsOf(cur))
                {
                    if (!inPool.Contains(nb) || seen.Contains(nb)) continue;
                    seen.Add(nb);
                    queue.Enqueue(nb);
                }
            }
            components.Add(component);
        }

        return components;
    }

    private static string _lastCulture = "";
    private static int _lastPoolSize;

    /// <summary>Son seçimde isyancıların kültürü (log/test).</summary>
    public static string LastRebelCulture => _lastCulture;

    /// <summary>Son seçimde aday havuzun büyüklüğü (log/test).</summary>
    public static int LastPoolSize => _lastPoolSize;

    private static List<string> ResolveCandidatePool(string countryId, List<string> owned, out string rebelCulture)
    {
        var byCulture = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var rec in ProvinceCultureSystem.ForCountry(countryId))
        {
            if (rec == null || string.IsNullOrEmpty(rec.ProvinceId)) continue;
            if (!byCulture.TryGetValue(rec.Culture ?? "", out var list))
            {
                list = new List<string>();
                byCulture[rec.Culture ?? ""] = list;
            }
            list.Add(rec.ProvinceId);
        }

        if (byCulture.Count == 0)
        {
            // Kültür katmanı yoksa bütün ülke aday; kültür etiketi boş kalır.
            rebelCulture = "";
            return owned.OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        var dominant = byCulture
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .First();

        var minority = byCulture
            .Where(kv => !string.Equals(kv.Key, dominant.Key, StringComparison.Ordinal))
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (minority.Key == null)
        {
            // Tek kültürlü ülke: baskın kültürün kendisi isyan eder.
            rebelCulture = dominant.Key;
            return dominant.Value.OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        rebelCulture = minority.Key;
        return minority.Value.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> NeighborsOf(string provinceId) =>
        _neighbors.TryGetValue(provinceId, out var nb) ? nb : Array.Empty<string>();

    /// <summary>
    /// İl komşuluk grafını bir kez kurar. Üretim (WorldMap.SetProvinces) ve
    /// headless testler illeri zaten hazırladığı için burada yalnızca graf
    /// kopyalanır — <c>ProceduralProvinceSource</c>'ün RNG'sine dokunulmaz.
    /// </summary>
    public static void SetWorld(IReadOnlyList<ProvinceData> provinces)
    {
        _neighbors.Clear();
        if (provinces == null)
        {
            _worldReady = false;
            return;
        }

        foreach (var p in provinces)
        {
            if (p == null || string.IsNullOrEmpty(p.Id)) continue;
            var ids = (p.NeighborIds ?? new List<string>())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            _neighbors[p.Id] = ids;
        }

        _worldReady = true;
        GD.Print($"[CivilWar] Komşuluk grafi hazır: {_neighbors.Count} il");
    }

    public static bool IsReady => _worldReady;

    // ===== Günlük değerlendirme =====

    public class OutbreakResult
    {
        public string CountryId = "";
        public double Score;
        public bool Outbroke;
        public RebellionState Rebellion;
        public string Reason = "";
    }

    /// <summary>
    /// Bir ülkeyi değerlendirir: puan eşiği aştıysa, bekleme süresi dolduysa ve
    /// hâlihazırda isyanı yoksa yeni bir isyan kaydeder.
    /// </summary>
    public static OutbreakResult Evaluate(string countryId, long currentDay)
    {
        EnsureLoaded();
        var res = new OutbreakResult { CountryId = countryId ?? "" };

        if (string.IsNullOrEmpty(countryId)) { res.Reason = "gecersiz_ulke"; return res; }
        if (CountryDatabase.Get(countryId) == null) { res.Reason = "ulke_yok"; return res; }

        res.Score = CollapseScore(countryId, currentDay);

        if (IsRebelling(countryId)) { res.Reason = "zaten_isyanda"; return res; }
        if (res.Score < S.OutbreakThreshold) { res.Reason = "esik_alti"; return res; }

        long last = LastOutbreakDay(countryId);
        if (last != long.MinValue && currentDay - last < S.OutbreakCooldownDays)
        {
            res.Reason = "bekleme_suresi";
            return res;
        }

        var provinces = SelectRebelProvinces(countryId);
        if (provinces.Count == 0) { res.Reason = "il_secilemedi"; return res; }

        var rebellion = new RebellionState
        {
            CountryId = countryId,
            StartDay = currentDay,
            RebelCulture = _lastCulture,
            Provinces = provinces
        };
        _active.Add(rebellion);
        _lastOutbreakDay[countryId] = currentDay;

        res.Outbroke = true;
        res.Rebellion = rebellion;
        res.Reason = "isyan";

        GD.Print($"[CivilWar] İSYAN PATLAK VERDİ: {countryId} — çöküş puanı=" +
                 $"{res.Score:F1} (eşik={S.OutbreakThreshold}), gün={currentDay}, " +
                 $"kültür={rebellion.RebelCulture ?? "—"}, il={provinces.Count} " +
                 $"[{string.Join(",", provinces)}]");
        return res;
    }

    /// <summary>İsyancı illeri sisteme bildirir ve kaydı düşürür (M50.3 çözüm yolları).</summary>
    public static void Suppress(string countryId)
    {
        int removed = _active.RemoveAll(r =>
            string.Equals(r.CountryId, countryId, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            GD.Print($"[CivilWar] {countryId}: isyan bastırıldı kayıttan düşürüldü ({removed}).");
    }

    // ===== Save/Load =====

    public static SaveSystem.CivilWarSave GetSaveData()
    {
        EnsureLoaded();
        return new SaveSystem.CivilWarSave
        {
            Rebellions = _active.Select(r => new SaveSystem.CivilWarEntry
            {
                CountryId = r.CountryId,
                StartDay = r.StartDay,
                RebelCulture = r.RebelCulture,
                Provinces = r.Provinces.ToArray()
            }).ToArray(),
            LastOutbreak = _lastOutbreakDay
                .Select(kv => new SaveSystem.CivilWarOutbreakDay
                {
                    CountryId = kv.Key,
                    Day = kv.Value
                }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.CivilWarSave data)
    {
        _active.Clear();
        _lastOutbreakDay.Clear();
        if (data == null) return;

        if (data.Rebellions != null)
        {
            foreach (var e in data.Rebellions)
            {
                if (e == null || string.IsNullOrEmpty(e.CountryId)) continue;
                _active.Add(new RebellionState
                {
                    CountryId = e.CountryId,
                    StartDay = e.StartDay,
                    RebelCulture = e.RebelCulture ?? "",
                    Provinces = (e.Provinces ?? Array.Empty<string>()).ToList()
                });
            }
        }

        if (data.LastOutbreak != null)
        {
            foreach (var e in data.LastOutbreak)
            {
                if (e == null || string.IsNullOrEmpty(e.CountryId)) continue;
                _lastOutbreakDay[e.CountryId] = e.Day;
            }
        }

        GD.Print($"[CivilWar] state yüklendi — isyan={_active.Count}, " +
                 $"patlak geçmişi={_lastOutbreakDay.Count}");
    }

    // ===== Yardımcılar =====

    private static double Clamp01To100(double v) => v < 0 ? 0 : (v > 100 ? 100 : v);

    /// <summary>
    /// FNV-1a 64-bit. Aday sıralaması için kullanılır: dictionary iterasyon
    /// sırasına, platforma ve kayıt/yüklemeye bağlı değildir.
    /// </summary>
    private static ulong StableHash(string s)
    {
        const ulong Offset = 1469598103934665603UL;
        const ulong Prime = 1099511628211UL;
        ulong hash = Offset;
        if (string.IsNullOrEmpty(s)) return hash;
        foreach (char c in s)
        {
            hash ^= (byte)(c & 0xFF);
            hash *= Prime;
            hash ^= (byte)((c >> 8) & 0xFF);
            hash *= Prime;
        }
        return hash;
    }
}
