using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Barış konferansı: talep maliyetleri (M42.2) ve anlaşma uygulama (M42.3).
///
/// `Data/peace_terms.json` taleplerin savaş puanı maliyetini tanımlar. Sistem
/// saf hesaptır: rastgelelik YOKTUR ve talep listesi (state) tutulmaz —
/// maliyet her çağrıda aynı veriden üretilir. Böylece ayrı bir DTO gerekmez
/// (uygulama anındaki dünya durumu zaten save/load ile korunur).
///
/// M42.3: `ProposeDeal` / `ApplyDeal` — talepler doğrulanıp sırayla uygulanır:
/// iller `ProvinceOwnershipSystem` üzerinden devredilir (harita sinyal zinciriyle
/// yeniden renklenir), fabrika/PP tazminatı doğrudan `CountryData`'ya yazılır.
/// </summary>
public static class PeaceConferenceSystem
{
    private const string DataPath = "res://Data/peace_terms.json";

    /// <summary>Talep türleri — JSON "Kind" alanından eşlenir.</summary>
    public enum TermKind
    {
        Unknown,
        Province,        // il talebi (birim = il sayısı)
        Annex,           // ülke ilhakı
        Puppet,          // kukla devlet
        Liberate,        // bağımsız devlet çıkarma
        Reparations,     // fabrika/PP tazminatı (birim = fabrika paketi)
        Demilitarize     // askeri fabrika sepeti
    }

    /// <summary>Bir barış talebi türünün tanımı (JSON tek kaydı).</summary>
    public class TermDefinition
    {
        public string Id = "";
        public string Label = "";
        public string Description = "";
        public string Kind = "";
        public double BaseCost = 0;
        public double CostPerUnit = 0;
        public string UnitLabel = "";
        public int MaxUnits = 1;
        public double RequiresScoreAtLeast = 0;
        public double TensionCost = 0;

        public TermKind KindEnum => ParseKind(Kind);
        public static TermKind ParseKind(string kind) => (kind ?? "").ToLowerInvariant() switch
        {
            "province" => TermKind.Province,
            "annex" => TermKind.Annex,
            "puppet" => TermKind.Puppet,
            "liberate" => TermKind.Liberate,
            "reparations" => TermKind.Reparations,
            "demilitarize" => TermKind.Demilitarize,
            _ => TermKind.Unknown
        };
    }

    /// <summary>Deal seviyesinde sınırlar (JSON "Limits" bloğu).</summary>
    public class LimitsDefinition
    {
        public double MinScoreForAnyDemand = 10;
        public int MaxTermsPerDeal = 4;
        public bool LiberateRemovesOverflow = true;
    }

    private class Root
    {
        public int Version = 1;
        public List<TermDefinition> Terms = new();
        public LimitsDefinition Limits = new();
    }

    // Talep türü id → tanım
    private static readonly Dictionary<string, TermDefinition> _terms = new(StringComparer.OrdinalIgnoreCase);
    private static LimitsDefinition _limits = new();
    private static bool _loaded;

    /// <summary>Toplam talep türü sayısı (log/yükleme doğrulaması için).</summary>
    public static int TermCount
    {
        get { EnsureLoaded(); return _terms.Count; }
    }

    /// <summary>Deal sınırları (erken kullanım güvenli olsun diye lazy yüklenir).</summary>
    public static LimitsDefinition Limits
    {
        get { EnsureLoaded(); return _limits; }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        LoadData();
    }

    /// <summary>JSON'u bir kez okur. `IncludeFields=true` olmadan alanlar doldurulmaz.</summary>
    public static void LoadData()
    {
        _terms.Clear();
        _limits = new LimitsDefinition();

        if (!Godot.FileAccess.FileExists(DataPath))
        {
            GD.PrintErr($"[PeaceTerms] HATA: {DataPath} bulunamadı");
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
            if (root?.Terms != null)
            {
                foreach (var t in root.Terms)
                    if (!string.IsNullOrEmpty(t.Id))
                        _terms[t.Id] = t;
                if (root.Limits != null) _limits = root.Limits;
            }
            GD.Print($"[PeaceTerms] {_terms.Count} barış talebi türü yüklendi ({DataPath}, IncludeFields=true)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PeaceTerms] HATA: JSON okunamadı — {ex.Message}");
        }

        _loaded = true;
    }

    /// <summary>Talep türü tanımı; yoksa null.</summary>
    public static TermDefinition Get(string termId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(termId)) return null;
        return _terms.TryGetValue(termId, out var def) ? def : null;
    }

    /// <summary>Tüm talep türleri (UI listesi).</summary>
    public static IReadOnlyList<TermDefinition> All =>
        (IReadOnlyList<TermDefinition>)EnsureAll();

    private static List<TermDefinition> EnsureAll()
    {
        EnsureLoaded();
        return _terms.Values
            .OrderByDescending(t => t.RequiresScoreAtLeast)
            .ToList();
    }

    // ===== Maliyet hesabı =====

    /// <summary>
    /// Bir talebin savaş puanı maliyeti: taban + birim × adet.
    /// Adet 1'in altına düşürülmez, türün üst sınırı aşılamaz.
    /// </summary>
    public static double CostFor(string termId, int units)
    {
        var def = Get(termId);
        if (def == null) return 0;
        return CostFor(def, units);
    }

    public static double CostFor(TermDefinition def, int units)
    {
        if (def == null) return 0;
        int u = Math.Clamp(units, 1, Math.Max(1, def.MaxUnits));
        return def.BaseCost + def.CostPerUnit * u;
    }

    /// <summary>Bir talep türünün bu savaş puanında talep edilebilir olup olmadığı.</summary>
    public static bool IsAvailable(string termId, float warScore)
    {
        var def = Get(termId);
        if (def == null) return false;
        if (warScore + 0.001f < def.RequiresScoreAtLeast) return false;
        if (warScore + 0.001f < Limits.MinScoreForAnyDemand) return false;
        return true;
    }

    /// <summary>Doğrulama sonucu — neden reddedildiği loglanabilir olsun.</summary>
    public class CheckResult
    {
        public bool Ok { get; set; }
        public string Reason { get; set; } = "";
        public double Cost { get; set; }
        public double RemainingScore { get; set; }
        public int Units { get; set; }
        public TermDefinition Definition { get; set; }
    }

    /// <summary>
    /// Talebi savaş puanına karşı doğrular: tür biliniyor mu, adet geçerli mi,
    /// puan eşiği ve toplam maliyet karşılanıyor mu. Sonuç tek nesnede döner.
    /// </summary>
    public static CheckResult Check(string termId, int units, float warScore)
    {
        var res = new CheckResult { Units = units };

        var def = Get(termId);
        if (def == null)
        {
            res.Reason = $"unknown term '{termId}'";
            return res;
        }
        res.Definition = def;

        if (warScore + 0.001f < Limits.MinScoreForAnyDemand)
        {
            res.Reason = $"score {warScore:F1} below minimum for any demand ({Limits.MinScoreForAnyDemand:F1})";
            return res;
        }

        if (warScore + 0.001f < def.RequiresScoreAtLeast)
        {
            res.Reason = $"score {warScore:F1} below requirement for '{def.Id}' ({def.RequiresScoreAtLeast:F1})";
            return res;
        }

        // Adet [1, MaxUnits] arasına kırpılır — 0 adet talep anlamsızdır.
        int clamped = Math.Clamp(units, 1, Math.Max(1, def.MaxUnits));
        res.Units = clamped;

        double cost = CostFor(def, clamped);

        if (cost > warScore + 0.001)
        {
            res.Cost = cost;
            res.RemainingScore = 0;
            res.Reason = $"score {warScore:F1} insufficient for '{def.Id}' (needs {cost:F1})";
            return res;
        }

        res.Ok = true;
        res.Cost = cost;
        res.RemainingScore = Math.Max(0, warScore - cost);
        return res;
    }

    /// <summary>Deal'de izin verilen en fazla talep sayısı.</summary>
    public static int MaxTermsPerDeal => Limits.MaxTermsPerDeal;

    // ===== M42.3: Anlaşma uygulama =====

    /// <summary>Tek bir onaylanmış talep (UI listesinden veya AI'dan gelir).</summary>
    public class Demand
    {
        public string TermId = "";
        public int Units = 1;

        public Demand() { }
        public Demand(string termId, int units) { TermId = termId; Units = units; }
    }

    /// <summary>Anlaşmanın uygulanma sonucu — UI ve test için özet.</summary>
    public class DealResult
    {
        public bool Ok { get; set; }
        public string Reason { get; set; } = "";
        public float StartingScore { get; set; }
        public float ScoreSpent { get; set; }
        public float ScoreLeft { get; set; }
        public int ProvincesTransferred { get; set; }
        public int FactoriesTaken { get; set; }
        public int FactoriesScrapped { get; set; }
        public double PoliticalPowerTransferred { get; set; }
        public bool Annexed { get; set; }
        public bool Puppeted { get; set; }
        public bool Liberated { get; set; }
        public List<string> TransferredProvinceIds { get; } = new();
        public List<string> Rejected { get; } = new();
    }

    /// <summary>
    /// Talepleri savaş puanına karşı doğrular ve uygular. Reddedilen talepler
    /// sonuçta `Rejected` olarak döner (loglanır), kabul edilenler sırayla
    /// etki eder. Puan bütçesi tükendikçe kalan talep reddedilir.
    /// </summary>
    public static DealResult ApplyDeal(WarSystem.War war, List<Demand> demands, string winnerId, string loserId)
    {
        var res = new DealResult();

        if (war == null || demands == null || string.IsNullOrEmpty(winnerId) || string.IsNullOrEmpty(loserId))
        {
            res.Reason = "invalid deal (war/demands/countries)";
            return res;
        }

        var winner = CountryDatabase.Get(winnerId);
        var loser = CountryDatabase.Get(loserId);
        if (winner == null || loser == null)
        {
            res.Reason = $"unknown country ({winnerId}/{loserId})";
            return res;
        }

        // Kazanan savaşı bitiren taraf mı? Puanı onun bakış açısından alıyoruz.
        float score = WarScoreSystem.ScoreForSide(war, winnerId);
        res.StartingScore = score;
        float budget = score;

        int applied = 0;
        foreach (var d in demands)
        {
            if (applied >= MaxTermsPerDeal)
            {
                res.Rejected.Add($"{d.TermId}: deal already has max terms ({MaxTermsPerDeal})");
                continue;
            }

            var check = Check(d.TermId, d.Units, budget);
            if (!check.Ok)
            {
                res.Rejected.Add($"{d.TermId}: {check.Reason}");
                continue;
            }

            // M49.2: il talebi yalnızca gerçekten işgal edilmiş toprak için
            // geçerli. Aksi halde kazanan, hiç girmemiş olduğu ili de konuşup
            // alabiliyordu — savaş puanı ise (M49.2) aynı orana bağlandığı
            // için bu tutarsızlık doğrudan puanla da çelişiyordu.
            if (check.Definition.KindEnum == TermKind.Province)
            {
                int claimable = OccupiedClaimCount(war, winner, loser);
                if (claimable >= 0 && d.Units > claimable)
                {
                    res.Rejected.Add(
                        $"{d.TermId}: {d.Units} il talep edildi ama yalnızca {claimable} il gerçekten işgal edildi");
                    continue;
                }
            }

            ApplyTerm(war, check.Definition, check.Units, winner, loser, res);
            budget -= (float)check.Cost;
            res.ScoreSpent += (float)check.Cost;
            applied++;
        }

        res.ScoreLeft = Math.Max(0, budget);
        res.Ok = applied > 0 || demands.Count == 0;

        FinishWar(war, winner, loser, res);

        if (GameManager.IsAvailable)
        {
            GameManager.Instance.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);
            GameManager.Instance.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
        }

        return res;
    }

    /// <summary>
    /// Kaybeden topraklarından kazananın GERÇEKTEN işgal ettiği iller.
    ///
    /// İşgal edilen ilin sahipliği işgalciye geçtiği için bunlar kaybedenin
    /// "şu anki illeri" listesinde bulunmaz — yetkili kaynak savaş öncesi
    /// sahiptir. Komşuluk grafiği kurulmamışsa (harita hiç üretilmeyen
    /// salt-test koşuları) işgal verisi yoktur; bu durumda eski deterministik
    /// id sırası korunur, çünkü kısıtlama yapacak veri de yoktur.
    /// </summary>
    private static List<string> ClaimableProvinces(WarSystem.War war, CountryData winner,
        CountryData loser, List<string> loserProvinces)
    {
        if (!OccupationSystem.IsReady || OccupationSystem.OccupiedCount == 0)
            return loserProvinces;

        // Yalnızca BU savaşın kayıtları: bitmiş savaştan kalan kayıtlar
        // yeni barış masasında sahte kapasite üretmesin.
        string warKey = OccupationSystem.WarKeyOf(war);
        return OccupationSystem.OccupiedBy(winner.Id)
            .Where(pid => string.Equals(OccupationSystem.ProvinceWarKey(pid), warKey, StringComparison.Ordinal) &&
                string.Equals(OccupationSystem.PreWarOwner(pid), loser.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Kazananın talep edebileceği işgal edilmiş il sayısı; -1 = sınır yok.</summary>
    private static int OccupiedClaimCount(WarSystem.War war, CountryData winner, CountryData loser)
    {
        if (!OccupationSystem.IsReady) return -1;
        if (OccupationSystem.OccupiedCount == 0) return 0;
        return ClaimableProvinces(war, winner, loser, new List<string>()).Count;
    }

    /// <summary>Barış devriyle ilin işgal kaydını düşürür (kayıt yoksa sessizce geçer).</summary>
    private static void ClearOccupation(string provinceId)
    {
        if (OccupationSystem.IsOccupied(provinceId))
            OccupationSystem.Release(provinceId);
    }

    /// <summary>Doğrulanmış tek talebi dünya durumuna uygular.</summary>
    private static void ApplyTerm(WarSystem.War war, TermDefinition def, int units,
        CountryData winner, CountryData loser, DealResult res)
    {
        switch (def.KindEnum)
        {
            case TermKind.Province:
            {
                // Kaybedenin illerinden deterministik sırayla (id sırası) `units` kadarını devret.
                // Id sırası sabit olduğu için RNG gerekmez — aynı durum her zaman aynı sonucu verir.
                var owned = ProvinceOwnershipSystem.GetProvincesByOwner(loser.Id);
                var candidates = ClaimableProvinces(war, winner, loser, owned)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
                int take = Math.Min(units, candidates.Count);
                for (int i = 0; i < take; i++)
                {
                    ProvinceOwnershipSystem.SetProvinceOwner(candidates[i], winner.Id);
                    res.TransferredProvinceIds.Add(candidates[i]);
                    // Barış yoluyla geçen il artık "işgal altında" değil: kayıt düşer,
                    // böylece sonraki barış masasında ikinci kez talep edilemez.
                    ClearOccupation(candidates[i]);
                }
                res.ProvincesTransferred += take;
                GD.Print($"[PeaceTerms] İl talebi: {take} il {loser.Id} → {winner.Id}");
                break;
            }

            case TermKind.Annex:
            {
                int civ = loser.CivFactories, mil = loser.MilFactories, dock = loser.Dockyards;
                // M44.4: iller devredilmeden önce kayıt düş (sonra serbest bırakılabilir)
                ReleasableSystem.RecordSubject(loser.Id, winner.Id, false, civ, mil, dock);
                loser.IsAnnexed = true;
                loser.OwnerId = winner.Id;
                winner.CivFactories += civ;
                winner.MilFactories += mil;
                winner.Dockyards += dock;
                loser.CivFactories = loser.MilFactories = loser.Dockyards = 0;
                res.FactoriesTaken += civ + mil + dock;
                res.Annexed = true;

                // İlhak: kaybedenin kalan tüm illeri de devredilir
                var rest = ProvinceOwnershipSystem.GetProvincesByOwner(loser.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
                foreach (var p in rest)
                {
                    ProvinceOwnershipSystem.SetProvinceOwner(p, winner.Id);
                    res.TransferredProvinceIds.Add(p);
                }
                res.ProvincesTransferred += rest.Count;

                // M41.2: major güç kapitülasyonu kaydı (fabrikalar sıfırlanmadan önce)
                VictorySystem.RecordCapitulation(winner.Id, loser.Id, civ + mil + dock);

                GD.Print($"[PeaceTerms] İLHAK: {loser.Id} → {winner.Id} " +
                         $"({civ + mil + dock} fabrika, {rest.Count} il devredildi)");
                break;
            }

            case TermKind.Puppet:
            {
                // Kukla: fabrikalar kaybedenin kalır, kontrol kazananda.
                // M44.4: kayıt düş — iller zaten kaybedende kalır, fabrika payı alınmaz.
                ReleasableSystem.RecordSubject(loser.Id, winner.Id, true, 0, 0, 0);
                loser.IsAnnexed = true;
                loser.OwnerId = winner.Id;
                res.Puppeted = true;
                GD.Print($"[PeaceTerms] KUKLA: {loser.Id} → {winner.Id} (fabrikalar korundu)");
                break;
            }

            case TermKind.Liberate:
            {
                // Bağımsız çıkarma: ilhak/puppet bayraçları kalkar, iller kendisine kalır.
                loser.IsAnnexed = false;
                loser.OwnerId = "";
                res.Liberated = true;
                GD.Print($"[PeaceTerms] BAĞIMSIZ: {loser.Id} serbest bırakıldı (ilhak kaldırıldı)");
                break;
            }

            case TermKind.Reparations:
            {
                int civTake = Math.Min(loser.CivFactories, units);
                int milTake = Math.Min(loser.MilFactories, Math.Max(0, units - civTake));

                loser.CivFactories -= civTake;
                loser.MilFactories -= milTake;
                winner.CivFactories += civTake;
                winner.MilFactories += milTake;

                // PP tazminatı: paket başına 25 PP (kaybedenin elindeki kadar)
                double pp = Math.Min(loser.PoliticalPower, units * 25.0);
                loser.PoliticalPower = Math.Max(0, loser.PoliticalPower - pp);
                winner.PoliticalPower += pp;

                res.FactoriesTaken += civTake + milTake;
                res.PoliticalPowerTransferred += pp;
                GD.Print($"[PeaceTerms] TAZMİNAT: {loser.Id} → {winner.Id} " +
                         $"({civTake} sivil + {milTake} askeri fabrika, {pp:N0} PP)");
                break;
            }

            case TermKind.Demilitarize:
            {
                int scrapped = loser.MilFactories;
                loser.MilFactories = 0;
                res.FactoriesScrapped += scrapped;
                GD.Print($"[PeaceTerms] ASKERSİZLEŞTİRME: {loser.Id} {scrapped} askeri fabrikayı sepetledi");
                break;
            }

            default:
                res.Rejected.Add($"{def.Id}: kind '{def.Kind}' is not applicable");
                break;
        }

        // Dünya gerilimi etkisi (tazminat/ilhak gerilimi artırır, bağımsızlaştırma azaltır)
        if (Math.Abs(def.TensionCost) > 0.001f)
            DiplomacySystem.IncreaseTension((float)def.TensionCost);
    }

    /// <summary>
    /// Statüko barışı: hiç talep uygulanmadan savaşı bitirir. Kapitülasyon bayrağı
    /// KONMAZ (kimse teslim olmadı) — yalnızca ateşkes ve ilişki yumuşaması olur.
    /// Oyuncu–AI ve AI–AI arasında kullanılabilir (AI beyaz barış isteği buradan geçer).
    /// </summary>
    public static bool WhitePeaceDeal(WarSystem.War war)
    {
        if (war == null || war.IsEnded) return false;

        var attacker = CountryDatabase.Get(war.AttackerId);
        var defender = CountryDatabase.Get(war.DefenderId);
        if (attacker == null || defender == null) return false;

        war.IsEnded = true;
        war.EndReason = "Peace conference (white peace)";

        // M49.2: işgal kaydı savaşa özgüdür — savaş bitince düşer. Aksi halde
        // aynı ülke çifti arasındaki yeni savaşta eski kayıtlar kapasite verir.
        OccupationSystem.EndWar(war);

        // Statüko: kapitülasyon yok, yalnızca ateşkes + ilişki toparlanması
        CasusBelliSystem.NotifyWarEnded(war.AttackerId, war.DefenderId);
        DiplomacySystem.ModifyRelation(war.AttackerId, war.DefenderId, +15);

        GD.Print($"[PeaceTerms] BEYAZ BARIŞ: {war.AttackerId} ↔ {war.DefenderId} — statüko, " +
                 $"kapitülasyon yok, ateşkes başladı");

        if (GameManager.IsAvailable)
        {
            GameManager.Instance.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);
            GameManager.Instance.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
        }
        return true;
    }

    /// <summary>Savaşı barışla bitirir: kapitülasyon bayrakları + ateşkes + ilişki yumuşaması.</summary>
    private static void FinishWar(WarSystem.War war, CountryData winner, CountryData loser, DealResult res)
    {
        war.IsEnded = true;
        war.EndReason = "Peace conference";

        // M49.2: işgal kaydı savaşa özgüdür — savaş bitince düşer.
        OccupationSystem.EndWar(war);

        bool attackerWon = string.Equals(war.AttackerId, winner.Id, StringComparison.OrdinalIgnoreCase);
        if (attackerWon) war.DefenderCapitulated = true;
        else war.AttackerCapitulated = true;

        // Kaybeden kapitülasyon kaydı (zafer koşulları)
        int loserFactories = loser.CivFactories + loser.MilFactories + loser.Dockyards;
        VictorySystem.RecordCapitulation(winner.Id, loser.Id, loserFactories);

        // M17A: Barış antlaşması — ateşkes başlat
        CasusBelliSystem.NotifyWarEnded(war.AttackerId, war.DefenderId);

        DiplomacySystem.ModifyRelation(winner.Id, loser.Id, +10);

        GD.Print($"[PeaceTerms] BARIŞ: {war.AttackerId} ↔ {war.DefenderId} — " +
                 $"harcanan puan {res.ScoreSpent:F1}, il devri {res.ProvincesTransferred}");
    }

    /// <summary>
    /// Barış konferansı teklifi (AI kullanımı için): kazananın puanına göre
    /// en pahalı uygulanabilir talebi önerir. Deterministik — RNG yok.
    /// </summary>
    public static Demand SuggestTerm(WarSystem.War war, string winnerId)
    {
        if (war == null) return null;
        float score = WarScoreSystem.ScoreForSide(war, winnerId);

        // Puanı yetmeyen talep için: beyaz barışçı talep (hiç talep yok) null döner.
        foreach (var def in EnsureAll())
        {
            if (def.KindEnum == TermKind.Liberate) continue; // AI liberate önermez
            var check = Check(def.Id, 1, score);
            if (check.Ok) return new Demand(def.Id, 1);
        }
        return null;
    }
}
