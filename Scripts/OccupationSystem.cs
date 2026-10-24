using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// İl işgali ve savaşın haritaya yansıması (M49.1).
///
/// Daha önce savaşlar haritada hiçbir şey değiştirmiyordu: çatışma soyut
/// aşınmaya indirgeniyor, il devri yalnızca barış masasında ve kaybedenin il
/// kimliği sırasına göre yapılıyordu — bir ülke hiç savaşmadığı ili de alabiliyordu.
/// Bu sistem savaşı il seviyesine indirir:
///
///   1. Her savaş için iki ülkenin topraklarının birleştiği iler "cephe"
///      köşesi belirlenir (komşuluk grafından, RNG yok).
///   2. Saldıran ve savunan tarafın gücüne göre "baskı" birikir; bir ilin
///      ele geçirilmesi için <see cref="OccupationSettings.PressureCost"/> kadar
///      baskı gerekir. Baskı birikimli olduğu için sonuç tamamen deterministiktir.
///   3. Ele geçirilen ilin sahipliği işgalciye geçer (harita ekonomisi ve
///      kültür katmanı gerçek sahiplikten beslenir), ama savaş öncesi gerçek
///      sahip ayrıca tutulur — il geri kazanıldığında doğru ülkeye döner.
///
/// Baskı ve hedef seçimi RNG kullanmaz; hedefler il kimliğine göre sıralanır,
/// böylece aynı durum her zaman aynı sonucu verir (save/load A==B).
/// </summary>
public static class OccupationSystem
{
    /// <summary>Ele geçirilmiş bir ilin kaydı.</summary>
    public class Occupation
    {
        public string ProvinceId = "";
        public string OccupierId = "";      // şu anki işgalci (sahiplik de bu ülkede)
        public string PreWarOwnerId = "";   // savaş öncesi gerçek sahip
        public string WarKey = "";          // "ATKC|DEFN" — hangi savaşta işgal edildi
        public long Day = 0;                // işgalin yapıldığı SAVAŞ GÜNÜ (yerleşme kuralı)
    }

    /// <summary>Savaş başına birikmiş baskı (determinizm için kayda yazılır).</summary>
    public class WarPressure
    {
        public string WarKey = "";
        public double Attacker = 0;
        public double Defender = 0;
        /// <summary>Savaşın kaçıncı günü — bu sayı her AdvanceDay'de 1 artar.</summary>
        public long Day = 0;
    }

    private const string DataPath = "res://Data/occupation.json";

    /// <summary>İşgal ayarları (Data/occupation.json).</summary>
    public class OccupationSettings
    {
        /// <summary>Bir ilin ele geçirilmesi için gereken birikmiş baskı.</summary>
        public double PressureCost = 400;
        /// <summary>
        /// Taraf başına günde ele geçirilen il sayısı bundan türetilir:
        /// birikmiş baskı / <see cref="PressureCost"/>. Bu değer yalnızca
        /// patolojik durumları (tek günde bütün haritanın dönmesi) önleyen
        /// güvenlik sınırıdır — asıl sınırı baskı belirler.
        /// </summary>
        public int MaxDailyProvinces = 8;
        /// <summary>Ofensif emri verildiğinde saldıran güc çarpanı.</summary>
        public double AttackerOffensiveMultiplier = 1.6;
        /// <summary>Bir tümenin saha gücüne katkısı.</summary>
        public double DivisionWeight = 10;
        /// <summary>Bir fabrikanın saha gücüne katkısı.</summary>
        public double FactoryWeight = 2;
        /// <summary>Hiç tümeni olmayan ülkenin taban gücü.</summary>
        public double MinPower = 1;
    }

    /// <summary>occupation.json kök DTO'su (IncludeFields=true ile okunur).</summary>
    private class OccupationDataFile
    {
        public OccupationSettings Settings;
    }

    // İl → işgal kaydı. Kayıt yalnızca sahibi savaş öncesi sahibinden farklıysa durur.
    private static readonly Dictionary<string, Occupation> _occupations =
        new(StringComparer.Ordinal);

    // Savaş anahtarı → birikmiş baskı
    private static readonly Dictionary<string, WarPressure> _pressures =
        new(StringComparer.Ordinal);

    // İl → komşu iller (sıralı; deterministik tarama için)
    private static readonly Dictionary<string, string[]> _neighbors =
        new(StringComparer.Ordinal);

    private static OccupationSettings D = new();
    private static bool _worldReady;

    /// <summary>Ele geçirilmiş il sayısı.</summary>
    public static int OccupiedCount => _occupations.Count;

    /// <summary>Dünya komşuluk grafı kuruldu mu?</summary>
    public static bool IsReady => _worldReady;

    /// <summary>
    /// İl komşuluk grafını bir kez kurar. Üretim (WorldMap.SetProvinces) ve
    /// headless testler illeri zaha hazırladığı için burada yalnızca graf
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
        GD.Print($"[Occupation] Komşuluk grafi hazır: {_neighbors.Count} il");
    }

    public static void ResetForNewGame()
    {
        _occupations.Clear();
        _pressures.Clear();
        GD.Print("[Occupation] Yeni oyun için sıfırlandı.");
    }

    /// <summary>Ayarları dosyadan yükler (dosya yoksa varsayılan kalır).</summary>
    public static void LoadSettings()
    {
        D = new OccupationSettings();
        if (!ResourceLoader.Exists(DataPath))
        {
            GD.Print($"[Occupation] Ayar dosyası yok, varsayılan kullanılıyor: {DataPath}");
            return;
        }

        try
        {
            // Projedeki diğer veri yükleyicileriyle aynı desen: System.Text.Json,
            // IncludeFields=true (alan tabanlı DTO'lar için zorunlu).
            using var f = FileAccess.Open(DataPath, FileAccess.ModeFlags.Read);
            if (f == null)
            {
                GD.PrintErr($"[Occupation] Ayar dosyası açılamadı, varsayılan kullanılıyor: {DataPath}");
                return;
            }
            var root = JsonSerializer.Deserialize<OccupationDataFile>(f.GetAsText(),
                new JsonSerializerOptions
                {
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true
                });
            if (root?.Settings != null)
            {
                D = root.Settings;
                GD.Print($"[Occupation] Ayar yüklendi: baskı bedeli={D.PressureCost:N0}, " +
                         $"günlük maks il={D.MaxDailyProvinces}, tümen ağırlığı={D.DivisionWeight:N1}");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Occupation] Ayar yüklenemedi ({e.Message}), varsayılan kullanılıyor.");
        }
    }

    // ===== Sorgular =====

    /// <summary>İl şu an işgal altında mı?</summary>
    public static bool IsOccupied(string provinceId)
        => !string.IsNullOrEmpty(provinceId) && _occupations.ContainsKey(provinceId);

    /// <summary>İlin savaş öncesi gerçek sahibi (kayıt yoksa boş).</summary>
    public static string PreWarOwner(string provinceId)
        => _occupations.TryGetValue(provinceId, out var o) ? o.PreWarOwnerId : "";

    /// <summary>İlin işgalcisi (kayıt yoksa boş).</summary>
    public static string Occupier(string provinceId)
        => _occupations.TryGetValue(provinceId, out var o) ? o.OccupierId : "";

    /// <summary>Savaşın kaydını yazmak için kararlı anahtarı.</summary>
    public static string WarKeyOf(WarSystem.War war)
        => war == null ? "" : $"{war.AttackerId}|{war.DefenderId}";

    /// <summary>Saldıran tarafın birikmiş baskısı (kayıt yoksa 0).</summary>
    public static double AttackerPressure(WarSystem.War war)
        => war != null && _pressures.TryGetValue(WarKeyOf(war), out var p) ? p.Attacker : 0;

    /// <summary>Savunan tarafın birikmiş baskısı (kayıt yoksa 0).</summary>
    public static double DefenderPressure(WarSystem.War war)
        => war != null && _pressures.TryGetValue(WarKeyOf(war), out var p) ? p.Defender : 0;

    /// <summary>Bir ülkenin şu an elinde tuttuğu işgal edilmiş iller.</summary>
    public static List<string> OccupiedBy(string countryId)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(countryId)) return list;
        foreach (var kv in _occupations)
            if (string.Equals(kv.Value.OccupierId, countryId, StringComparison.OrdinalIgnoreCase))
                list.Add(kv.Key);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>Savaştaki bir ülkenin işgal ettiği düşman il sayısı.</summary>
    public static int OccupiedEnemyProvinces(string countryId, string enemyId)
    {
        if (string.IsNullOrEmpty(countryId) || string.IsNullOrEmpty(enemyId)) return 0;
        int n = 0;
        foreach (var kv in _occupations)
            if (string.Equals(kv.Value.OccupierId, countryId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(kv.Value.PreWarOwnerId, enemyId, StringComparison.OrdinalIgnoreCase))
                n++;
        return n;
    }

    /// <summary>Düşmanın savaş öncesi toplam il sayısı (işgal oranı payı).</summary>
    public static int EnemyTotalProvinces(string enemyId)
    {
        if (string.IsNullOrEmpty(enemyId)) return 0;
        int now = ProvinceOwnershipSystem.ProvinceCountFor(enemyId);
        int occupied = 0;
        foreach (var kv in _occupations)
            if (string.Equals(kv.Value.PreWarOwnerId, enemyId, StringComparison.OrdinalIgnoreCase))
                occupied++;
        // Şu anki sahibi + savaş öncesi sahibi bu ülke olan işgal edilmiş iller
        return now + occupied;
    }

    /// <summary>Saldıranın savaştaki işgal oranı [0,1].</summary>
    public static float OccupationRatio(WarSystem.War war)
    {
        if (war == null) return 0f;
        int total = EnemyTotalProvinces(war.DefenderId);
        if (total <= 0) return 0f;
        int held = OccupiedEnemyProvinces(war.AttackerId, war.DefenderId);
        return Mathf.Clamp((float)held / total, 0f, 1f);
    }

    /// <summary>Tarafın kendi topraklarını ne kadarını kaybettiği [0,1].</summary>
    public static float LossRatio(WarSystem.War war)
        => OccupationRatio(war);

    // ===== Günlük ilerleme =====

    /// <summary>
    /// Bütün aktif savaşları bir gün ilerletir. GameManager günlük tick'inden çağrılır.
    /// </summary>
    public static void AdvanceAll()
    {
        if (!_worldReady) return;
        foreach (var war in WarSystem.ActiveWars)
            AdvanceDay(war);
    }

    /// <summary>Tek savaşı bir gün ilerletir.</summary>
    public static void AdvanceDay(WarSystem.War war)
    {
        if (war == null || war.IsEnded) return;

        string key = WarKeyOf(war);
        if (!_pressures.TryGetValue(key, out var pr))
        {
            pr = new WarPressure { WarKey = key };
            _pressures[key] = pr;
        }

        // Savaş günü sayacı: her adımda 1 artar ve kayda yazılır. Yerleşme
        // kuralı (bir il aynı gün içinde geri alınamaz) bu sayıya bakıyor;
        // sayaç kayıtta durmadığında yükleme sonrası kuralı bozulurdu.
        pr.Day++;

        double attackerPush = FieldPower(war.AttackerId) *
            (war.OffensiveActive ? D.AttackerOffensiveMultiplier : 1.0);
        double defenderPush = FieldPower(war.DefenderId);

        pr.Attacker += attackerPush;
        pr.Defender += defenderPush;

        // Saldıran önce hamle yapar (rakip hedefler hamleye göre yeniden hesaplanır).
        // Günde kaç il alınacağı baskıya göre belirlenir: üstünlük kurmuş tarap
        // birden fazla il alabilir, eşit güçlerde cephe sabit kalır.
        int taken = 0;
        while (taken < D.MaxDailyProvinces && pr.Attacker >= D.PressureCost)
        {
            var target = NextFrontierProvince(war.AttackerId, war.DefenderId);
            if (target == null) break;
            Occupy(target, war.AttackerId, war.DefenderId, key, pr.Day);
            pr.Attacker -= D.PressureCost;
            taken++;
        }

        int liberated = 0;
        while (liberated < D.MaxDailyProvinces && pr.Defender >= D.PressureCost)
        {
            var target = NextLiberationProvince(war.AttackerId, war.DefenderId, pr.Day);
            if (target == null) break;
            Liberate(target, war.DefenderId);
            pr.Defender -= D.PressureCost;
            liberated++;
        }

        if (taken > 0 || liberated > 0)
        {
            GD.Print($"[Occupation] {war.AttackerId}→{war.DefenderId} (savaş günü {pr.Day}): " +
                     $"+{taken} il ele geçirildi, {liberated} il geri kazanıldı " +
                     $"(işgal oranı %{OccupationRatio(war) * 100f:F0})");
        }
    }

    /// <summary>Saldıranın hamle yapabileceği bir sonraki il (deterministik sıra).</summary>
    private static string NextFrontierProvince(string attackerId, string defenderId)
    {
        foreach (var id in OwnedSorted(attackerId))
        {
            // Saldıran topraklarındaki her ilin komşularına bak: savunan toprağıysa hedef.
            if (!_neighbors.TryGetValue(id, out var nbs)) continue;
            foreach (var nb in nbs)
            {
                if (ProvinceOwnershipSystem.GetProvinceOwner(nb) != defenderId) continue;
                return nb;
            }
        }
        return null;
    }

    /// <summary>Savunanın geri kazanabileceği bir sonraki il (deterministik sıra).</summary>
    /// <summary>
    /// Savunanın geri kazanabileceği bir sonraki il (deterministik sıra).
    ///
    /// Yerleşme kuralı: bir il ancak ele geçirildiği savaş gününden EN AZ bir
    /// gün sonra geri alınabilir. Bu kural olmadan iki taraf aynı il üzerinde
    /// sonsuza kadar salınırdı — cephe hiç ilerlemez, harita hiç değişmezdi.
    /// </summary>
    private static string NextLiberationProvince(string attackerId, string defenderId, long currentWarDay)
    {
        foreach (var id in OwnedSorted(defenderId))
        {
            if (!_neighbors.TryGetValue(id, out var nbs)) continue;
            foreach (var nb in nbs)
            {
                if (ProvinceOwnershipSystem.GetProvinceOwner(nb) != attackerId) continue;
                // Yalnızca savaş öncesi sahibi savunan olan iller geri kazanılır.
                if (!string.Equals(PreWarOwner(nb), defenderId, StringComparison.OrdinalIgnoreCase))
                    continue;
                // En az bir gün işgalci kalmış olmalı.
                if (!_occupations.TryGetValue(nb, out var occ) || occ.Day >= currentWarDay)
                    continue;
                return nb;
            }
        }
        return null;
    }

    private static IEnumerable<string> OwnedSorted(string countryId)
    {
        // GetProvincesByOwner iç listeyi canlı döndürür — sıralama onu bozmasın diye kopyalanır.
        var list = new List<string>(ProvinceOwnershipSystem.GetProvincesByOwner(countryId));
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>İli işgalciye devreder ve savaş öncesi sahibini kaydeder.</summary>
    private static void Occupy(string provinceId, string occupierId,
        string preWarOwnerId, string warKey, long warDay)
    {
        ProvinceOwnershipSystem.SetProvinceOwner(provinceId, occupierId);
        _occupations[provinceId] = new Occupation
        {
            ProvinceId = provinceId,
            OccupierId = occupierId,
            PreWarOwnerId = preWarOwnerId,
            WarKey = warKey,
            Day = warDay
        };
        GD.Print($"[Occupation] {provinceId}: {preWarOwnerId} → {occupierId} " +
                 $"(işgal, savaş günü {warDay})");
    }

    /// <summary>İli savaş öncesi sahibine döndürür ve kaydı siler.</summary>
    private static void Liberate(string provinceId, string rightfulOwnerId)
    {
        ProvinceOwnershipSystem.SetProvinceOwner(provinceId, rightfulOwnerId);
        _occupations.Remove(provinceId);
        GD.Print($"[Occupation] {provinceId}: {rightfulOwnerId} tarafından geri kazanıldı");
    }

    /// <summary>
    /// Ülkenin saha gücü. Tümen ve fabrika sayısından türetilir — RNG yok.
    /// </summary>
    private static double FieldPower(string countryId)
    {
        var c = CountryDatabase.Get(countryId);
        if (c == null) return 0;
        int divisions = Math.Max(CountryAI.GetDivisions(c.Id), c.StartingDivisions);
        return D.MinPower + D.DivisionWeight * divisions + D.FactoryWeight * c.TotalFactories;
    }

    // ===== Save / Load =====

    public static SaveSystem.OccupationSave GetSaveData()
    {
        var list = new List<SaveSystem.OccupationEntry>(_occupations.Count);
        foreach (var o in _occupations.Values)
            list.Add(new SaveSystem.OccupationEntry
            {
                ProvinceId = o.ProvinceId,
                OccupierId = o.OccupierId,
                PreWarOwnerId = o.PreWarOwnerId,
                WarKey = o.WarKey,
                Day = o.Day
            });

        var pressures = new List<SaveSystem.OccupationPressureEntry>(_pressures.Count);
        foreach (var p in _pressures.Values)
            pressures.Add(new SaveSystem.OccupationPressureEntry
            {
                WarKey = p.WarKey,
                Attacker = p.Attacker,
                Defender = p.Defender,
                Day = p.Day
            });

        return new SaveSystem.OccupationSave
        {
            Occupied = list.ToArray(),
            Pressures = pressures.ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.OccupationSave data)
    {
        _occupations.Clear();
        _pressures.Clear();
        if (data == null) return;

        if (data.Occupied != null)
        {
            foreach (var e in data.Occupied)
            {
                if (e == null || string.IsNullOrEmpty(e.ProvinceId)) continue;
                _occupations[e.ProvinceId] = new Occupation
                {
                    ProvinceId = e.ProvinceId,
                    OccupierId = e.OccupierId ?? "",
                    PreWarOwnerId = e.PreWarOwnerId ?? "",
                    WarKey = e.WarKey ?? "",
                    Day = e.Day
                };
            }
        }

        if (data.Pressures != null)
        {
            foreach (var p in data.Pressures)
            {
                if (p == null || string.IsNullOrEmpty(p.WarKey)) continue;
                _pressures[p.WarKey] = new WarPressure
                {
                    WarKey = p.WarKey,
                    Attacker = p.Attacker,
                    Defender = p.Defender,
                    Day = p.Day
                };
            }
        }

        GD.Print($"[Occupation] state yüklendi — işgal={_occupations.Count}, savaş baskısı={_pressures.Count}");
    }
}
