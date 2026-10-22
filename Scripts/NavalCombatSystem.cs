using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Deniz çatışması (M43.3).
///
/// İki ülkenin filosu aynı deniz bölgesinde çarpışır; kaybeden tarafın filo
/// gücü bir oranda düşer. Bu hasar, <see cref="SeaZoneSystem.FleetWeight"/>
/// içinde çarpı olarak okunur — böylece hakimiyet, çarpışmanın SONUCUNDAN
/// türeyen saf bir hesap olarak kalır (M43.1 mimarisi bozulmaz).
///
/// Neden hakimiyet doğrudan yazılmıyor: hakimiyet zaten filo ağırlığının
/// oranıdır. Çatışmayı filoyu zayıflatarak uygulamak, "hakimiyeti elle
/// güncelle" hatasını (iki kaynağın birbirinden ayrılması) engeller.
///
/// Hasar kümülatiftir ve günlük tick'te onarım görür: büyük filo kaybı
/// birkaç günde toparlanır, küçük kayıp hemen. Bu sayede deniz savaşı
/// kalıcı bir fianal yıkım değil, zamanla geri dönen bir yıpranmadır.
///
/// Rastgelelik YALNIZCA <see cref="DeterministicRNG"/> üzerinden gelir
/// (±%10 vurgu zarları), bu yüzden kayıtlar A==B biçiminde round-trip eder.
/// </summary>
public static class NavalCombatSystem
{
    /// <summary>Bir çatışmada yok olabilecek filo oranının üst sınırı.</summary>
    public const float MaxDamage = 0.85f;

    /// <summary>Eşit güçte bile kaybedenin alacağı taban hasar oranı.</summary>
    public const float MinDamage = 0.10f;

    /// <summary>Kazanan tarafın yıpranması (ezici zaferde sıfıra iner).</summary>
    public const float WinnerAttrition = 0.10f;

    /// <summary>Günde onarılan hasar oranı (göreli — büyük hasar daha uzun sürer).</summary>
    public const float DailyRepair = 0.15f;

    /// <summary>Bu değerin altına düşen hasar tamamen onarılmış sayılır.</summary>
    private const float RepairEpsilon = 0.005f;

    // ±%10 vurgu zarı — kara savaşıyla (WarSystem) aynı ölçek.
    private const float RollVariance = 0.10f;

    // Ülke → filo hasarı [0, MaxDamage].
    private static readonly Dictionary<string, float> _damage = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static bool IsInitialized => _initialized;

    /// <summary>Hasarlı ülkeler ve oranları (UI ve test denetimi için).</summary>
    public static IReadOnlyDictionary<string, float> DamageState => _damage;

    // ===== Kurulum =====

    public static void ResetForNewGame()
    {
        _damage.Clear();
        _initialized = true;
        GD.Print("[NavalCombat] Deniz çatışma sistemi sıfırlandı (yeni oyun)");
    }

    public static void Initialize(CountryData data)
    {
        if (!_initialized) ResetForNewGame();
        GD.Print($"[NavalCombat] Deniz çatışma sistemi hazır — hasarlı filo={_damage.Count}");
    }

    // ===== Hasar durumu =====

    /// <summary>Ülkenin filo hasarı [0,1]. Kayıt yoksa 0.</summary>
    public static float DamageFor(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 0f;
        return _damage.TryGetValue(countryId, out var d) ? d : 0f;
    }

    /// <summary>
    /// Filonun KALAN güç çarpanı [1-MaxDamage, 1].
    /// SeaZoneSystem.FleetWeight bunu kullanır: hasar = daha az ağırlık = daha düşük hakimiyet.
    /// </summary>
    public static float StrengthMultiplier(string countryId)
    {
        return 1f - DamageFor(countryId);
    }

    /// <summary>
    /// Ülkeye filo hasarı ekler (kümülatif). Oran [0,1] olmalı;
    /// toplam <see cref="MaxDamage"/> ile sınırlanır.
    /// </summary>
    public static void ApplyDamage(string countryId, float amount)
    {
        if (string.IsNullOrEmpty(countryId) || amount <= 0f) return;
        float total = Mathf.Clamp(DamageFor(countryId) + amount, 0f, MaxDamage);
        _damage[countryId] = total;
    }

    /// <summary>Bir çatışmanın sonucu (test ve UI kaydı için).</summary>
    public class BattleResult
    {
        public string ZoneId = "";
        public string ZoneName = "";
        public string AttackerId = "";
        public string DefenderId = "";
        public string WinnerId = "";
        public string LoserId = "";

        /// <summary>Çatışma öncesi filo ağırlıkları (hasar çarpanı DAHİL).</summary>
        public float AttackerStrength;
        public float DefenderStrength;

        /// <summary>Uygulanan hasar oranları [0,1].</summary>
        public float DamageToAttacker;
        public float DamageToDefender;

        /// <summary>Saldıran perspektifinden hakimiyet (çatışma öncesi / sonrası).</summary>
        public float SupremacyBefore;
        public float SupremacyAfter;

        /// <summary>Karşılaşma gerçekleşti mi (iki filo da vardı)?</summary>
        public bool WasFought => AttackerStrength > 0f && DefenderStrength > 0f;

        /// <summary>Kazanan taraf var mı? Tam eşit güçte beraberlik olur.</summary>
        public bool HasWinner => WasFought && !string.IsNullOrEmpty(WinnerId);

        /// <summary>Hakimiyet değişti mi? (M43.3 test kriteri)</summary>
        public bool SupremacyChanged => Mathf.Abs(SupremacyAfter - SupremacyBefore) > 0.01f;
    }

    // ===== Çatışma çözümleme =====

    /// <summary>
    /// Bir bölgede iki ülkenin filo çatışmasını çözümler ve hasarı UYGULAR.
    /// Saf hesap değildir: <see cref="DeterministicRNG"/> vurgu zarlarını tüketir.
    /// </summary>
    public static BattleResult ResolveBattle(
        SeaZoneSystem.ZoneDefinition zone,
        string attackerId,
        string defenderId)
    {
        var r = new BattleResult
        {
            ZoneId = zone?.Id ?? "",
            ZoneName = zone?.Name ?? "",
            AttackerId = attackerId ?? "",
            DefenderId = defenderId ?? ""
        };

        if (zone == null ||
            string.IsNullOrEmpty(attackerId) || string.IsNullOrEmpty(defenderId) ||
            string.Equals(attackerId, defenderId, StringComparison.OrdinalIgnoreCase))
        {
            return r;
        }

        // Çatışma öncesi ağırlıklar (önceden alınan hasar dâhil).
        r.AttackerStrength = SeaZoneSystem.FleetWeight(attackerId);
        r.DefenderStrength = SeaZoneSystem.FleetWeight(defenderId);
        r.SupremacyBefore = SeaZoneSystem.Supremacy(zone, attackerId);

        if (!r.WasFought) return r;

        // ±%10 vurgu: aynı filo her savaşta aynı sonucu vermesin.
        float effA = r.AttackerStrength * (1f + RollVariance * (DeterministicRNG.NextFloat() * 2f - 1f));
        float effB = r.DefenderStrength * (1f + RollVariance * (DeterministicRNG.NextFloat() * 2f - 1f));

        if (effA > effB)
        {
            r.WinnerId = attackerId;
            r.LoserId = defenderId;
        }
        else if (effB > effA)
        {
            r.WinnerId = defenderId;
            r.LoserId = attackerId;
        }
        // Tam eşit → beraberlik: iki taraf da taban hasar alır.

        // Ezilme oranı: 0 = eşit güç, 1 = rakip tamamen ezildi.
        float crush = 0f;
        if (r.HasWinner)
        {
            float win = Math.Max(effA, effB);
            float lose = Math.Min(effA, effB);
            crush = Mathf.Clamp(1f - (lose / win), 0f, 1f);
        }

        float loserDamage = Mathf.Clamp(MinDamage + (MaxDamage - MinDamage) * crush, 0f, MaxDamage);
        // Kazanan yıpranması: ne kadar kolay kazandıysa o kadar az yıpranır.
        float winnerDamage = Mathf.Clamp(WinnerAttrition * (1f - crush), 0f, MaxDamage);

        if (r.HasWinner)
        {
            if (r.WinnerId == attackerId)
            {
                r.DamageToAttacker = winnerDamage;
                r.DamageToDefender = loserDamage;
            }
            else
            {
                r.DamageToAttacker = loserDamage;
                r.DamageToDefender = winnerDamage;
            }
        }
        else
        {
            // Beraberlik: iki taraf da eşit yıpranır → hakimiyet değişmez.
            r.DamageToAttacker = MinDamage;
            r.DamageToDefender = MinDamage;
        }

        ApplyDamage(attackerId, r.DamageToAttacker);
        ApplyDamage(defenderId, r.DamageToDefender);

        r.SupremacyAfter = SeaZoneSystem.Supremacy(zone, attackerId);

        string outcome = r.HasWinner
            ? $"kazanan {r.WinnerId}"
            : "beraberlik";
        GD.Print($"[NavalCombat] {r.ZoneName} ({r.ZoneId}): {attackerId} ({r.AttackerStrength:F1}) vs " +
                 $"{defenderId} ({r.DefenderStrength:F1}) → {outcome}, " +
                 $"hasar {r.DamageToAttacker * 100f:F0}% / {r.DamageToDefender * 100f:F0}%, " +
                 $"hakimiyet {r.SupremacyBefore:F1} → {r.SupremacyAfter:F1}");

        return r;
    }

    public static BattleResult ResolveBattle(string zoneId, string attackerId, string defenderId)
    {
        return ResolveBattle(SeaZoneSystem.Get(zoneId), attackerId, defenderId);
    }

    // ===== Günlük tick =====

    /// <summary>
    /// Günlük onarım + savaştaki komşu ülkelerin bölgelerde çarpışması.
    /// Ekonomi tick'inden ÖNCE çağrılır: o günkü konvoy baskını (M43.2)
    /// çatışma SONRASI hakimiyeti görür.
    /// </summary>
    public static void DailyTick()
    {
        if (!_initialized) return;

        RepairFleets();

        foreach (var zone in SeaZoneSystem.All)
        {
            ResolveWarZoneBattles(zone);
        }
    }

    private static void RepairFleets()
    {
        if (_damage.Count == 0) return;

        foreach (var key in _damage.Keys.ToList())
        {
            // Göreli onarım: %85 hasar %72'ye, %20 hasar %17'ye düşer.
            float repaired = _damage[key] * (1f - DailyRepair);
            if (repaired < RepairEpsilon) repaired = 0f;
            _damage[key] = repaired;
        }
    }

    /// <summary>
    /// Bölgeye komşu, biribirine savaş ilan etmiş ve ikisinin de filosu olan
    /// ülke çiftleri için tek tek bir çatışma çözümler (çift başına 1).
    /// </summary>
    private static void ResolveWarZoneBattles(SeaZoneSystem.ZoneDefinition zone)
    {
        var ids = zone.AdjacentCountryIds;
        for (int a = 0; a < ids.Count; a++)
        {
            string idA = ids[a];
            if (string.IsNullOrEmpty(idA)) continue;

            for (int b = a + 1; b < ids.Count; b++)
            {
                string idB = ids[b];
                if (string.IsNullOrEmpty(idB)) continue;

                // Aynı ülke birden fazla kez komşu sayılmış olabilir.
                if (string.Equals(idA, idB, StringComparison.OrdinalIgnoreCase)) continue;

                // Yalnızca gerçek savaş: barıştaki filolar aynı denizde yanyana geçer.
                if (!WarSystem.IsAtWarWith(idA, idB)) continue;

                // İki tarafın da filosu olmalı (yoksa "çarpışma" değil, devriye).
                if (SeaZoneSystem.FleetWeight(idA) <= 0f || SeaZoneSystem.FleetWeight(idB) <= 0f)
                    continue;

                ResolveBattle(zone, idA, idB);
            }
        }
    }

    // ===== M43.3: Save/Load =====

    public static SaveSystem.NavalCombatSave GetSaveData()
    {
        return new SaveSystem.NavalCombatSave
        {
            Damage = _damage
                .Select(kv => new SaveSystem.NavalCombatEntry { CountryId = kv.Key, Damage = kv.Value })
                .ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.NavalCombatSave data)
    {
        _damage.Clear();

        if (data?.Damage != null)
        {
            foreach (var e in data.Damage)
            {
                if (string.IsNullOrEmpty(e.CountryId)) continue;
                _damage[e.CountryId] = Mathf.Clamp(e.Damage, 0f, MaxDamage);
            }
        }

        _initialized = true;
        GD.Print($"[NavalCombat] state yüklendi — hasarlı filo={_damage.Count}");
    }
}
