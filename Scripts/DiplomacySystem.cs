using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Diplomasi sistemi (M8): ilişkiler (-100..+100), görüşler, faksiyonlar,
/// diplomatik aksiyonlar (savaş ilanı, garantı, erişim, barış konferansı),
/// world tension. Statik sınıf; GameManager günlük tick'inde çağrılır.
/// </summary>
public static class DiplomacySystem
{
    // ===== Veri modelleri =====

    public class Faction
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LeaderId { get; set; }
        public List<string> MemberIds { get; set; } = new();
        public string Ideology { get; set; }
        public string Color { get; set; }
        public string Type { get; set; } = "DefensePact"; // DefensePact | PoliticalEconomic — M17.2

        public bool IsMember(string countryId) => MemberIds.Contains(countryId);
        public int MemberCount => MemberIds.Count;
        public bool IsDefensePact => string.Equals(Type, "DefensePact", StringComparison.OrdinalIgnoreCase);
    }

    public class Relation
    {
        public string FromId { get; set; }
        public string ToId { get; set; }
        public int Value { get; set; }        // -100..+100 (ilişki)
        public int Opinion { get; set; }      // -100..+100 (görüş, asimetrik olabilir)

        public string Key => $"{FromId}->{ToId}";
    }

    public class Guarantee
    {
        public string GuarantorId { get; set; }
        public string TargetId { get; set; }
    }

    public class DiplomaticAction
    {
        public enum Type
        {
            DeclareWar,
            GuaranteeIndependence,
            MilitaryAccess,
            NavalAccess,
            NonAggressionPact,
            CallToArms,
            JoinFaction,
            CreateFaction,
            LeaveFaction,
            PeaceConference
        }

        public Type ActionType { get; set; }
        public string InitiatorId { get; set; }
        public string TargetId { get; set; }
        public int PPCost { get; set; }
        public int DaysToComplete { get; set; }
        public int DaysRemaining { get; set; }
        public bool IsPlayerAction { get; set; }
    }

    public class PeaceDeal
    {
        public string WinnerId { get; set; }
        public string LoserId { get; set; }
        public enum Term { Annex, Puppet, Liberate, Demilitarize, WarReparations, TransferState }
        public List<(Term, string)> Terms { get; set; } = new(); // stateId for TransferState
    }

    /// <summary>
    /// M27.1: Lider görüşması / devlet ziyareti / zirve / hat görüşmesi kaydı.
    /// İlişki puanına ve dünya gerilimine kalıcı (normalize dönüşüne kadar) etki eder.
    /// </summary>
    public class DiplomacyMeeting
    {
        public enum Type { BilateralMeeting, StateVisit, Summit, PhoneCall }

        public string HostId { get; set; }
        public string TargetId { get; set; }
        public Type MeetingType { get; set; }
        public int Day { get; set; }            // Oyun günü (GM.TotalDays)
        public int RelationDelta { get; set;}   // Uygulanan iki yönlü ilişki değişimi
        public float TensionEffect { get; set; } // Gerilim etkisi (negatif = azaltma)
    }

    /// <summary>
    /// M27.2: İki ülke arasında imzalanan kalıcı anlaşma (ticaret, savunma, vize, enerji, iklim).
    /// İlişkiye ve türe bağlı diğer göstergelere süresiz veya süresi biter etki eder.
    /// </summary>
    public class Agreement
    {
        public enum Type { Trade, Defense, Visa, Energy, Climate }

        public string Id { get; set; }           // "A-B-TYPE-Day" (sıralı A<B)
        public Type AgreementType { get; set; }
        public string CountryA { get; set; }
        public string CountryB { get; set; }
        public int DaySigned { get; set; }       // Oyun günü (GM.TotalDays)
        public int DurationDays { get; set; }    // 0 = süresiz
        public bool Active { get; set; } = true;
        public int RelationDelta { get; set; }   // Uygulanan iki yönlü ilişki değişimi
        public float TensionEffect { get; set; } // Gerilim etkisi (negatif = azaltma)

        public string Key => $"{CountryA}-{CountryB}-{AgreementType}";
    }

    /// <summary>
    /// M27.3: Tek ülkeden yapılmış basın toplantısı / TV konuşması / miting.
    /// Ton (sert/uzlaşmacı/popülist) ve etkinlik türüne bağlı olarak dünya gerilimi,
    /// istikrar, savaş desteği ve diğer ülkelere olan ilişkileri değiştirir;
    /// deterministik geri-etki riski bulunur.
    /// </summary>
    public class PressEvent
    {
        public enum Type { PressConference, TVSpeech, Rally }
        public enum Tone { Hardline, Conciliatory, Populist }

        public string CountryId { get; set; }
        public Type EventType { get; set; }
        public Tone ToneValue { get; set; }
        public int Day { get; set; }           // Oyun günü (GM.TotalDays)
        public int RelationDelta { get; set; } // Diğer tüm ülkelere uygulanan iki yönlü olmayan genel etki
        public float TensionEffect { get; set; }
        public int StabilityEffect { get; set; }
        public int WarSupportEffect { get; set; }
        public bool Success { get; set; }      // false = geri-etki (backfire)
        public string EffectText { get; set; } // İnsan okunabilir özet
    }

    /// <summary>
    /// M27.4: Bir ulke tarafindan baska bir ulkeye uygulanan yaptirim (ticaret, finansal, teknoloji, enerji, silah).
    /// Yonludur (Initiator -> Target). Iliski ve dünya gerilimine etki eder; sureli veya süresiz olabilir.
    /// </summary>
    public class Sanction
    {
        public enum Type { Trade, Financial, Tech, Energy, Arms }

        public string Id { get; set; }           // "INITIATOR-TARGET-TYPE-Day" (yonlu, siralama yok)
        public string InitiatorId { get; set; }
        public string TargetId { get; set; }
        public Type SanctionType { get; set; }
        public int DayStarted { get; set; }      // Oyun gunu (GM.TotalDays)
        public int DurationDays { get; set; }    // 0 = süresiz
        public bool Active { get; set; } = true;
        public int RelationDelta { get; set; }   // Iki yonlu (iki ulkeye de) uygulanan iliski dususu
        public float TensionEffect { get; set; } // Gerilim artisi (pozitif)
        public string EffectText { get; set; }   // İnsan okunabilir özet
    }

    // ===== Sabitler =====
    private const int MaxRelation = 100;
    private const int MinRelation = -100;
    private const double BaseGuaranteeCost = 25;
    private const double BaseAccessCost = 10;
    private const double BaseFactionCost = 50;
    private const double ImproveRelationCost = 10; // PP/gün
    private const float TensionDecayPerDay = 0.02f; // günde -0.02%

    // ===== Durum =====
    private static bool _initialized;
    private static readonly Dictionary<string, Faction> _factions = new();
    private static readonly Dictionary<string, Relation> _relations = new();
    private static readonly List<Guarantee> _guarantees = new();
    private static readonly List<string> _nonAggressionPacts = new(); // "A-B" (sıralı)
    private static readonly List<string> _militaryAccess = new();     // "A->B"
    private static readonly List<string> _navalAccess = new();        // "A->B"
    private static readonly List<DiplomaticAction> _pendingActions = new();
    private static readonly List<DiplomacyMeeting> _meetings = new(); // M27.1: geçmiş görüşmalar
    private static readonly List<Agreement> _agreements = new();      // M27.2: imzalanmış anlaşmalar
    private static readonly List<PressEvent> _pressEvents = new();    // M27.3: basın toplantısı / TV konuşması / miting
    private static readonly List<Sanction> _sanctions = new();        // M27.4: yaptirimlar ve ticaret savasi kaydi
    private static float _worldTension = 0f;

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    // ===== Senaryo başlangıç verisi önbelleği =====
    private static bool _startLoaded;
    private static bool _startLoadLogged;

    // ===== Yükleme =====

    public static void Initialize(CountryData data)
    {
        _factions.Clear();
        _relations.Clear();
        _guarantees.Clear();
        _nonAggressionPacts.Clear();
        _militaryAccess.Clear();
        _navalAccess.Clear();
        _pendingActions.Clear();
        _worldTension = 0f;

        if (data == null)
        {
            _initialized = false;
            return;
        }

        EnsureStartLoaded();

        // Eksik ülkeler için deterministik varsayılan ilişkiler
        InitializeDefaultRelations();

        _initialized = true;
        GD.Print($"DiplomacySystem: {data.Id} için hazır — Faksiyon={_factions.Count}, " +
                 $"İlişki={_relations.Count}, Tension=%{_worldTension:F1}");
    }

    // M17.2: Senaryo başlangıç verisi (diplomacy_start.json) — senaryo değişince yeniden okunur
    public static void ResetForScenario()
    {
        _startLoaded = false;
        _startLoadLogged = false;
    }

    // M17.2: Test/debug için senaryo verisini yükle ve logla (ülke gerekmez)
    public static void LoadScenarioData()
    {
        EnsureStartLoaded();
        if (!_startLoadLogged)
        {
            _startLoadLogged = true;
            // M17.2: blok türü dağılımı (DefensePact / PoliticalEconomic)
            int defense = 0, political = 0;
            foreach (var f in _factions.Values)
            {
                if (f.IsDefensePact) defense++;
                else political++;
            }
            GD.Print($"DiplomacySystem: senaryo başlangıç verisi yüklendi (senaryo: {ScenarioSystem.ActiveId}) — Faksiyon={_factions.Count} (DefensePact={defense}, PoliticalEconomic={political}), İlişki={_relations.Count}, Garanti={_guarantees.Count}, Tension=%{_worldTension:F1}");
        }
    }

    private static void EnsureStartLoaded()
    {
        if (_startLoaded) return;
        _startLoaded = true;

        // Senaryo dosyasının içeriği tam olarak bunlar — önceki senaryo/oyundan kalma yok
        _factions.Clear();
        _relations.Clear();
        _guarantees.Clear();
        _nonAggressionPacts.Clear();
        _militaryAccess.Clear();
        _navalAccess.Clear();
        _worldTension = 0f;

        string path = ScenarioSystem.ActiveDiplomacyStartPath;
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"DiplomacySystem: {path} bulunamadı!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var json = FileAccess.GetFileAsString(path);
            using var doc = JsonDocument.Parse(json);

            // Factions
            if (doc.RootElement.TryGetProperty("Factions", out var facs))
            {
                foreach (var f in facs.EnumerateArray())
                {
                    var faction = new Faction
                    {
                        Id = f.GetProperty("Id").GetString(),
                        Name = f.GetProperty("Name").GetString(),
                        LeaderId = f.GetProperty("Leader").GetString(),
                        Ideology = f.GetProperty("Ideology").GetString(),
                        Color = f.GetProperty("Color").GetString(),
                        Type = f.TryGetProperty("Type", out var typeProp) ? typeProp.GetString() : "DefensePact"
                    };
                    if (f.TryGetProperty("Members", out var members))
                        foreach (var m in members.EnumerateArray())
                            faction.MemberIds.Add(m.GetString());
                    _factions[faction.Id] = faction;
                }
            }

            // Relations
            if (doc.RootElement.TryGetProperty("Relations", out var rels))
            {
                foreach (var r in rels.EnumerateArray())
                {
                    var rel = new Relation
                    {
                        FromId = r.GetProperty("From").GetString(),
                        ToId = r.GetProperty("To").GetString(),
                        Value = r.GetProperty("Value").GetInt32(),
                        Opinion = r.GetProperty("Opinion").GetInt32()
                    };
                    _relations[rel.Key] = rel;
                }
            }

            // World Tension
            if (doc.RootElement.TryGetProperty("WorldTension", out var tension))
                _worldTension = tension.GetSingle();

            // Guarantees (tek hatalı kayıt kalanı durdurmasın)
            if (doc.RootElement.TryGetProperty("Guarantees", out var guars))
            {
                foreach (var g in guars.EnumerateArray())
                {
                    if (!g.TryGetProperty("Guarantor", out var guarantor) ||
                        !g.TryGetProperty("Target", out var target)) continue;
                    _guarantees.Add(new Guarantee
                    {
                        GuarantorId = guarantor.GetString(),
                        TargetId = target.GetString()
                    });
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"DiplomacySystem: diplomacy_start.json okunamadı: {e.Message}");
        }
    }

    // Dosyada olmayan tüm ülke çiftleri için ideoloji/zararlı komşu/uzaklık tabanlı varsayılan
    private static void InitializeDefaultRelations()
    {
        var all = CountryDatabase.All().ToList();
        foreach (var a in all)
        {
            foreach (var b in all)
            {
                if (a.Id == b.Id) continue;
                string key = $"{a.Id}->{b.Id}";
                if (_relations.ContainsKey(key)) continue;

                int value = CalculateBaseRelation(a, b);
                _relations[key] = new Relation { FromId = a.Id, ToId = b.Id, Value = value, Opinion = value };
            }
        }
    }

    private static int CalculateBaseRelation(CountryData a, CountryData b)
    {
        int rel = 0;
        // Aynı ideoloji: +30
        if (a.Ideology == b.Ideology) rel += 30;
        // Farklı ideoloji: -20
        else if (IsOpposingIdeology(a.Ideology, b.Ideology)) rel -= 20;

        // Aynı faksiyon: +50
        var factionA = GetFactionOf(a.Id);
        var factionB = GetFactionOf(b.Id);
        if (factionA != null && factionA == factionB) rel += 50;

        // Savaşta: -100
        if (WarSystem.IsAtWarWith(a.Id, b.Id) || WarSystem.IsAtWarWith(b.Id, a.Id)) rel = -100;

        // Garanti veren: +20
        if (HasGuarantee(b.Id, a.Id)) rel += 20;

        return Math.Clamp(rel, MinRelation, MaxRelation);
    }

    private static bool IsOpposingIdeology(string a, string b)
    {
        var opposing = new HashSet<(string, string)>
        {
            ("Fascism", "Democracy"), ("Democracy", "Fascism"),
            ("Fascism", "Communism"), ("Communism", "Fascism"),
            ("Democracy", "Communism"), ("Communism", "Democracy")
        };
        return opposing.Contains((a, b));
    }

    // ===== Günlük tick =====

    public static void DailyTick()
    {
        if (!_initialized || Player == null) return;

        // 1) Bekleyen diplomatik aksiyonları ilerlet
        for (int i = _pendingActions.Count - 1; i >= 0; i--)
        {
            var action = _pendingActions[i];
            action.DaysRemaining--;
            if (action.DaysRemaining <= 0)
                CompleteAction(action);
        }

        // 2) World Tension azalma (barış zamanında)
        if (_worldTension > 0 && WarSystem.ActiveWars.Count == 0)
        {
            _worldTension = Math.Max(0, _worldTension - TensionDecayPerDay);
        }

        // 3) İlişkiler: savaşta olanlar -1/gün, barışta olanlar +1/gün (normalize)
        NormalizeRelations();

        // Ayda bir özet
        if (GM.TotalDays > 0 && GM.TotalDays % 30 == 0)
        {
            int playerFaction = GetFactionOf(GM.PlayerCountryId) != null ? 1 : 0;
            GD.Print($"[Diplomacy] Gün {GM.TotalDays}: Tension=%{_worldTension:F1}, " +
                     $"Faksiyon={playerFaction}, İlişkiler={_relations.Count}");
        }
    }

    private static void NormalizeRelations()
    {
        foreach (var kv in _relations)
        {
            var rel = kv.Value;
            // Savaşta olanlar -1/gün
            if (WarSystem.IsAtWarWith(rel.FromId, rel.ToId))
            {
                rel.Value = Math.Max(MinRelation, rel.Value - 1);
                rel.Opinion = Math.Max(MinRelation, rel.Opinion - 1);
            }
            // Barışta ve pozitifse +0.5/gün, negatifse +1/gün (normalize)
            else
            {
                // Bilinmeyen ülke id'leri (veri dosyasında olmayan) — null koruması
                var a = CountryDatabase.Get(rel.FromId);
                var b = CountryDatabase.Get(rel.ToId);
                if (a == null || b == null) continue;

                int target = CalculateBaseRelation(a, b);
                if (rel.Value < target) rel.Value = Math.Min(target, rel.Value + 1);
                else if (rel.Value > target) rel.Value = Math.Max(target, rel.Value - 1);
            }
        }
    }

    // ===== Aksiyonlar (UI'dan çağrılır) =====

    // İlişki geliştir (PP/gün)
    public static bool ImproveRelations(string targetId)
    {
        if (!CanDoDiplomacy(targetId, out var target)) return false;
        if (Player.PoliticalPower < ImproveRelationCost) return false;

        Player.PoliticalPower -= ImproveRelationCost;
        ModifyRelation(GM.PlayerCountryId, targetId, +5);
        GD.Print($"[Diplomacy] İlişki geliştirme: {GM.PlayerCountryId} → {targetId} (+5, maliyet {ImproveRelationCost} PP)");
        return true;
    }

    // Garanti ver (M8.4)
    public static bool GuaranteeIndependence(string targetId)
    {
        if (!CanDoDiplomacy(targetId, out var target)) return false;
        if (HasGuarantee(GM.PlayerCountryId, targetId)) return false;

        double cost = BaseGuaranteeCost * (1 + Math.Max(0, -GetRelation(GM.PlayerCountryId, targetId)) / 100f);
        if (Player.PoliticalPower < cost) return false;

        Player.PoliticalPower -= (int)cost;
        _guarantees.Add(new Guarantee { GuarantorId = GM.PlayerCountryId, TargetId = targetId });
        ModifyRelation(GM.PlayerCountryId, targetId, +10);
        IncreaseTension(2);
        GD.Print($"[Diplomacy] Garanti: {GM.PlayerCountryId} → {target.Id} (maliyet {cost:F0} PP, tension +2)");
        return true;
    }

    // --- AI / Genel diploması yardımcıları (M11.3) ---

    /// <summary>Genel garanti: herhangi bir ülke herhangi bir ülkeyi garanti edebilir.</summary>
    public static bool AddGuarantee(string guarantorId, string targetId)
    {
        var guarantor = CountryDatabase.Get(guarantorId);
        var target = CountryDatabase.Get(targetId);
        if (guarantor == null || target == null || guarantor.IsAnnexed || target.IsAnnexed)
            return false;
        if (guarantorId == targetId) return false;
        if (HasGuarantee(guarantorId, targetId)) return false;

        double cost = BaseGuaranteeCost * (1 + Math.Max(0, -GetRelation(guarantorId, targetId)) / 100f);
        if (guarantor.PoliticalPower < cost) return false;

        guarantor.PoliticalPower -= (int)cost;
        _guarantees.Add(new Guarantee { GuarantorId = guarantorId, TargetId = targetId });
        ModifyRelation(guarantorId, targetId, +10);
        GD.Print($"[Diplomacy] Garanti: {guarantorId} → {targetId} (maliyet {cost:F0} PP)");
        return true;
    }

    /// <summary>Faksiyon oluştur: lider belirtilen ülke olur.</summary>
    public static bool CreateFaction(string factionId, string name, string leaderId, string ideology)
    {
        if (_factions.ContainsKey(factionId)) return false;
        var leader = CountryDatabase.Get(leaderId);
        if (leader == null || leader.IsAnnexed) return false;
        if (GetFactionOf(leaderId) != null) return false;

        double cost = BaseFactionCost;
        if (leader.PoliticalPower < cost) return false;

        leader.PoliticalPower -= (int)cost;
        var faction = new Faction
        {
            Id = factionId,
            Name = name,
            LeaderId = leaderId,
            MemberIds = new List<string> { leaderId },
            Ideology = ideology,
            Color = GetIdeologyColor(ideology)
        };
        _factions[factionId] = faction;
        GD.Print($"[Diplomacy] Faksiyon oluşturuldu: {name} ({factionId}) lider: {leaderId}");
        return true;
    }

    /// <summary>Faksiyona katılma: ülke belirtilen faksiyona katılır.</summary>
    public static bool RequestJoinFaction(string factionId, string countryId)
    {
        if (!_factions.TryGetValue(factionId, out var faction)) return false;
        if (GetFactionOf(countryId) != null) return false;
        var country = CountryDatabase.Get(countryId);
        if (country == null || country.IsAnnexed) return false;

        double cost = BaseFactionCost;
        if (country.PoliticalPower < cost) return false;

        // Liderin onayı: görüş +50 gerekir
        int opinion = GetOpinion(faction.LeaderId, countryId);
        if (opinion < 50)
        {
            GD.Print($"[Diplomacy] Faksiyon reddetti: {faction.Name} (görüş {opinion} < 50)");
            return false;
        }

        country.PoliticalPower -= (int)cost;
        faction.MemberIds.Add(countryId);
        ModifyRelation(countryId, faction.LeaderId, +20);
        GD.Print($"[Diplomacy] Faksiyona katıldı: {countryId} → {faction.Name}");
        return true;
    }

    /// <summary>Belirli bir ülkenin savaştığı düşmanları döndürür.</summary>
    public static List<string> GetEnemiesAtWar(string countryId)
    {
        return WarSystem.GetEnemiesAtWar(countryId);
    }

    // Garanti çek
    public static void RevokeGuarantee(string targetId)
    {
        var g = _guarantees.Find(x => x.GuarantorId == GM.PlayerCountryId && x.TargetId == targetId);
        if (g != null)
        {
            _guarantees.Remove(g);
            ModifyRelation(GM.PlayerCountryId, targetId, -10);
            GD.Print($"[Diplomacy] Garanti geri çekildi: {GM.PlayerCountryId} → {targetId}");
        }
    }

    // Askeri erişim (M8.4)
    public static bool RequestMilitaryAccess(string targetId)
    {
        if (!CanDoDiplomacy(targetId, out _)) return false;
        if (HasMilitaryAccess(GM.PlayerCountryId, targetId)) return false;

        double cost = BaseAccessCost * (1 + Math.Max(0, -GetRelation(GM.PlayerCountryId, targetId)) / 100f);
        if (Player.PoliticalPower < cost) return false;

        Player.PoliticalPower -= (int)cost;
        _militaryAccess.Add($"{GM.PlayerCountryId}->{targetId}");
        ModifyRelation(GM.PlayerCountryId, targetId, +5);
        GD.Print($"[Diplomacy] Askeri erişim: {GM.PlayerCountryId} → {targetId} (maliyet {cost:F0} PP)");
        return true;
    }

    // Deniz erişimi (M8.4)
    public static bool RequestNavalAccess(string targetId)
    {
        if (!CanDoDiplomacy(targetId, out _)) return false;
        if (HasNavalAccess(GM.PlayerCountryId, targetId)) return false;

        double cost = BaseAccessCost * (1 + Math.Max(0, -GetRelation(GM.PlayerCountryId, targetId)) / 100f);
        if (Player.PoliticalPower < cost) return false;

        Player.PoliticalPower -= (int)cost;
        _navalAccess.Add($"{GM.PlayerCountryId}->{targetId}");
        ModifyRelation(GM.PlayerCountryId, targetId, +5);
        GD.Print($"[Diplomacy] Deniz erişimi: {GM.PlayerCountryId} → {targetId} (maliyet {cost:F0} PP)");
        return true;
    }

    // Savaş ilanı (WarSystem.DeclareWar ile entegre - burası ekstra: garantili ülkelere çağrı)
    public static bool DeclareWar(string targetId)
    {
        if (!CanDoDiplomacy(targetId, out var target)) return false;
        if (WarSystem.IsAtWarWith(targetId)) return false;

        // PP maliyeti — war goal indirimi DAHİL (M17A düzeltmesi: önce taban 50 kontrolü
        // yanlıştı; Barbarossa -100% gibi indirimlerde ucuz ilan engelleniyordu)
        int ppCost = WarSystem.CalculateWarGoalCost(targetId);
        if (Player.PoliticalPower < ppCost) return false;

        // ÖNCE: Oyuncu hedefe savaş ilan eder (bu savaşı çözer; casus belli
        // kontrolü WarSystem.DeclareWar içinde yapılır)
        bool warDeclared = WarSystem.DeclareWar(targetId);
        if (!warDeclared) return false;

        // SONRA: Garanti tetiklemesi — garantörler OYUNCUYA savaş açar (Defender=garantör)
        var guarantors = _guarantees.Where(g => g.TargetId == targetId).Select(g => g.GuarantorId).ToList();
        foreach (var gId in guarantors)
        {
            if (gId != GM.PlayerCountryId && !WarSystem.IsAtWarWith(gId, targetId))
            {
                // Garantör oyuncuya savaş açar (gerekçesi NotifyWarDeclared'da otomatik verildi)
                WarSystem.DeclareWarOnPlayer(gId);
                GD.Print($"[Diplomacy] Garanti tetiklendi: {gId} → {GM.PlayerCountryId} savaşa girdi");
            }
        }

        // Faksiyon çağrısı (Call to Arms - M8.3): YALNIZCA DefensePact faksiyonları (NATO vb.) savaş çağrısı yapar.
        // M17.2: PoliticalEconomic bloklar (AB, BRICS, ŞİÖ) üye saldırıldığında çağrı YAPMAZ.
        // M17A: Yeni savaş ilanı değil — JoinWarByFactionCall casus belli kontrolü atlar.
        var myFaction = GetFactionOf(GM.PlayerCountryId);
        if (myFaction != null && myFaction.IsDefensePact)
        {
            foreach (var memberId in myFaction.MemberIds)
            {
                if (memberId != GM.PlayerCountryId && !WarSystem.IsAtWarWith(memberId, targetId))
                {
                    WarSystem.JoinWarByFactionCall(memberId, targetId);
                    GD.Print($"[Diplomacy] Faksiyon çağrısı (DefensePact): {memberId} → {targetId} savaşa girdi");
                }
            }
        }

        return true;
    }

    // Faksiyon oluştur (M8.2)
    public static bool CreateFaction(string name, string ideology)
    {
        if (GetFactionOf(GM.PlayerCountryId) != null) return false;
        if (Player.PoliticalPower < BaseFactionCost) return false;

        Player.PoliticalPower -= BaseFactionCost;
        string id = name.ToLower().Replace(" ", "_");
        var faction = new Faction
        {
            Id = id,
            Name = name,
            LeaderId = GM.PlayerCountryId,
            MemberIds = new List<string> { GM.PlayerCountryId },
            Ideology = ideology,
            Color = GetIdeologyColor(ideology)
        };
        _factions[id] = faction;
        GD.Print($"[Diplomacy] Faksiyon oluşturuldu: {name} ({id}) lider: {GM.PlayerCountryId}");
        return true;
    }

    // Faksiyona katılma isteği
    public static bool RequestJoinFaction(string factionId)
    {
        if (!_factions.TryGetValue(factionId, out var faction)) return false;
        if (GetFactionOf(GM.PlayerCountryId) != null) return false;
        if (Player.PoliticalPower < BaseFactionCost) return false;

        // Liderin onayı: görüş +50 gerekir
        int opinion = GetOpinion(faction.LeaderId, GM.PlayerCountryId);
        if (opinion < 50)
        {
            GD.Print($"[Diplomacy] Faksiyon reddetti: {faction.Name} (görüş {opinion} < 50)");
            return false;
        }

        Player.PoliticalPower -= BaseFactionCost;
        faction.MemberIds.Add(GM.PlayerCountryId);
        ModifyRelation(GM.PlayerCountryId, faction.LeaderId, +20);
        GD.Print($"[Diplomacy] Faksiyona katıldı: {GM.PlayerCountryId} → {faction.Name}");
        return true;
    }

    // Faksiyondan ayrılma
    public static void LeaveFaction()
    {
        var faction = GetFactionOf(GM.PlayerCountryId);
        if (faction == null) return;

        faction.MemberIds.Remove(GM.PlayerCountryId);
        ModifyRelation(GM.PlayerCountryId, faction.LeaderId, -30);
        GD.Print($"[Diplomacy] Faksiyondan ayrıldı: {GM.PlayerCountryId} ← {faction.Name}");
    }

    // Barış konferansı (M8.3 basit)
    public static PeaceDeal ProposePeace(string winnerId, string loserId)
    {
        var deal = new PeaceDeal
        {
            WinnerId = winnerId,
            LoserId = loserId
        };

        // Basit mantık: ilhak (annex) veya puppet
        var loser = CountryDatabase.Get(loserId);
        if (loser != null && (loser.CivFactories + loser.MilFactories + loser.Dockyards) <= 5)
        {
            deal.Terms.Add((PeaceDeal.Term.Annex, ""));
        }
        else
        {
            deal.Terms.Add((PeaceDeal.Term.Puppet, ""));
        }

        GD.Print($"[Diplomacy] Barış teklifi: {winnerId} → {loserId} ({deal.Terms[0].Item1})");
        return deal;
    }

    public static void ApplyPeaceDeal(PeaceDeal deal)
    {
        var loser = CountryDatabase.Get(deal.LoserId);
        var winner = CountryDatabase.Get(deal.WinnerId);
        if (loser == null || winner == null) return;

        foreach (var term in deal.Terms)
        {
            switch (term.Item1)
            {
                case PeaceDeal.Term.Annex:
                    loser.IsAnnexed = true;
                    loser.OwnerId = deal.WinnerId;
                    winner.CivFactories += loser.CivFactories;
                    winner.MilFactories += loser.MilFactories;
                    winner.Dockyards += loser.Dockyards;
                    loser.CivFactories = loser.MilFactories = loser.Dockyards = 0;
                    IncreaseTension(-20);
                    GD.Print($"[Diplomacy] Barış: {deal.LoserId} ilhak edildi → {deal.WinnerId}");
                    break;
                case PeaceDeal.Term.Puppet:
                    loser.IsAnnexed = true;
                    loser.OwnerId = deal.WinnerId;
                    // Puppet: fabrikalar kalır ama kontrol sahibinde
                    IncreaseTension(-10);
                    GD.Print($"[Diplomacy] Barış: {deal.LoserId} puppet yapıldı → {deal.WinnerId}");
                    break;
            }
        }

        // Savaş bitir
        var war = WarSystem.GetWarWith(deal.LoserId);
        if (war != null)
        {
            war.IsEnded = true;
            // M17A: Barış antlaşması — ateşkes başlat
            CasusBelliSystem.NotifyWarEnded(deal.WinnerId, deal.LoserId);
        }

        if (GameManager.IsAvailable)
        {
            GameManager.Instance.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);
            GameManager.Instance.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
        }
    }

    // Beyaz barış (M8.3): hiçbir taraf kazanmaz, savaş statüko ile biter
    public static bool WhitePeace(string enemyId)
    {
        var war = WarSystem.GetWarWith(enemyId);
        if (war == null) return false;

        war.IsEnded = true;
        war.EndReason = "White peace (statüko)";
        // M17A: Beyaz barış — ateşkes başlat
        CasusBelliSystem.NotifyWarEnded(GM.PlayerCountryId, enemyId);
        IncreaseTension(-5);
        ModifyRelation(GM.PlayerCountryId, enemyId, +15); // barış ilişkileri yumuşatır

        GD.Print($"[Diplomacy] BEYAZ BARIŞ: {GM.PlayerCountryId} ↔ {enemyId} — savaş statüko ile bitti, tension -5");
        return true;
    }

    // ===== M27.1: Lider görüşması / devlet ziyareti / zirve / hat görüşmesi =====

    /// <summary>İki lider arasında bir görüşme gerçekleştirir ve iki yönlü ilişkiye etki eder.</summary>
    public static bool HoldMeeting(string hostId, string targetId, DiplomacyMeeting.Type type)
    {
        var host = CountryDatabase.Get(hostId);
        var target = CountryDatabase.Get(targetId);
        if (host == null || target == null || host.IsAnnexed || target.IsAnnexed || hostId == targetId)
            return false;

        var hostLeader = LeaderSystem.GetLeader(hostId);
        int dip = hostLeader?.Diplomacy ?? 50;
        int cha = hostLeader?.Charisma ?? 50;

        // Baz etki: lider diplomasi + karizma ortalaması → deterministik +2..+8 arası.
        int baseDelta = Math.Clamp((dip + cha) / 20, 2, 8);

        // Tür katsayısı (zirve en etkili, telefon görüşmesi en zayıf)
        int typeMult = type switch
        {
            DiplomacyMeeting.Type.PhoneCall => 1,
            DiplomacyMeeting.Type.BilateralMeeting => 1,
            DiplomacyMeeting.Type.StateVisit => 2,
            DiplomacyMeeting.Type.Summit => 3,
            _ => 1
        };

        int delta = Math.Clamp(baseDelta * typeMult, 1, 15);

        // İki yönlü ilişki değişikliği
        ModifyRelation(hostId, targetId, delta);
        ModifyRelation(targetId, hostId, delta);

        // Zirve/devlet ziyareti gerilimi hafifçe azaltır
        float tensionEffect = type switch
        {
            DiplomacyMeeting.Type.Summit => -2f,
            DiplomacyMeeting.Type.StateVisit => -1f,
            _ => 0f
        };
        if (tensionEffect != 0f)
            IncreaseTension(tensionEffect);

        _meetings.Add(new DiplomacyMeeting
        {
            HostId = hostId,
            TargetId = targetId,
            MeetingType = type,
            Day = GM?.TotalDays ?? 0,
            RelationDelta = delta,
            TensionEffect = tensionEffect
        });

        GD.Print($"[Diplomacy] Görüşme: {hostId} ↔ {targetId} ({type}, gün {GM?.TotalDays}, " +
                 $"ilişki {delta:+#;-#;0}, tension {tensionEffect:+#;-#;0})");
        return true;
    }

    public static IReadOnlyList<DiplomacyMeeting> Meetings => _meetings;

    // ===== M27.2: Anlaşmalar (ticaret, savunma, vize, enerji, iklim) =====

    public static IReadOnlyList<Agreement> Agreements => _agreements;

    /// <summary>İki ülke arasında bir anlaşma imzalar ve etkilerini uygular.</summary>
    public static bool SignAgreement(string countryA, string countryB, Agreement.Type type, int durationDays)
    {
        var a = CountryDatabase.Get(countryA);
        var b = CountryDatabase.Get(countryB);
        if (a == null || b == null || a.IsAnnexed || b.IsAnnexed || countryA == countryB)
            return false;

        // Aynı türde aktif anlaşma arasında tekrar imzalanmasın
        if (_agreements.Any(x => x.Active &&
             ((x.CountryA == countryA && x.CountryB == countryB) || (x.CountryA == countryB && x.CountryB == countryA)) &&
             x.AgreementType == type))
            return false;

        // A<B sıralı kimlik
        string first = string.Compare(countryA, countryB, StringComparison.Ordinal) < 0 ? countryA : countryB;
        string second = string.Compare(countryA, countryB, StringComparison.Ordinal) < 0 ? countryB : countryA;
        string id = $"{first}-{second}-{type}-{GM?.TotalDays ?? 0}";

        // Tür bazlı etkiler
        int relationDelta = type switch
        {
            Agreement.Type.Trade => 6,
            Agreement.Type.Defense => 8,
            Agreement.Type.Visa => 4,
            Agreement.Type.Energy => 5,
            Agreement.Type.Climate => 3,
            _ => 2
        };
        float tensionEffect = type == Agreement.Type.Climate ? -1f : 0f;

        // İki yönlü ilişkiye etki
        ModifyRelation(countryA, countryB, relationDelta);
        ModifyRelation(countryB, countryA, relationDelta);

        // Savunma anlaşması: karşılıklı garanti
        if (type == Agreement.Type.Defense)
        {
            AddGuarantee(countryA, countryB);
            AddGuarantee(countryB, countryA);
        }

        if (tensionEffect != 0f)
            IncreaseTension(tensionEffect);

        _agreements.Add(new Agreement
        {
            Id = id,
            AgreementType = type,
            CountryA = countryA,
            CountryB = countryB,
            DaySigned = GM?.TotalDays ?? 0,
            DurationDays = durationDays,
            Active = true,
            RelationDelta = relationDelta,
            TensionEffect = tensionEffect
        });

        GD.Print($"[Diplomacy] Anlaşma: {countryA} ↔ {countryB} ({type}, gün {GM?.TotalDays}, " +
                 $"ilişki {relationDelta:+#;-#;0}, tension {tensionEffect:+#;-#;0}, süre {(durationDays == 0 ? "süresiz" : durationDays + " gün")})");
        return true;
    }

    /// <summary>Bir anlaşmayı iptal eder (etkileri geri alınmaz, ilişki kalıcıdır).</summary>
    public static bool CancelAgreement(string agreementId)
    {
        var agr = _agreements.FirstOrDefault(x => x.Id == agreementId);
        if (agr == null || !agr.Active) return false;
        agr.Active = false;
        GD.Print($"[Diplomacy] Anlaşma iptal edildi: {agreementId}");
        return true;
    }

    /// <summary>Bir ülkeye ait aktif anlaşmaları döndürür.</summary>
    public static List<Agreement> GetActiveAgreementsFor(string countryId) =>
        _agreements.Where(x => x.Active && (x.CountryA == countryId || x.CountryB == countryId)).ToList();

    // ===== M27.3: Basın toplantısı / TV konuşması / miting =====

    public static IReadOnlyList<PressEvent> PressEvents => _pressEvents;

    /// <summary>Ülke lideri bir basın etkinliği düzenler; ton ve türe bağlı göstergelere etki eder.</summary>
    public static bool HoldPressEvent(string countryId, PressEvent.Type type, PressEvent.Tone tone)
    {
        var country = CountryDatabase.Get(countryId);
        if (country == null || country.IsAnnexed) return false;

        // Ton bazlı taban etkiler (İngilizce UI etiketleri, Türkçe yorum)
        int relationDelta = tone switch
        {
            PressEvent.Tone.Hardline => -5,
            PressEvent.Tone.Conciliatory => +6,
            PressEvent.Tone.Populist => -8,
            _ => 0
        };
        float tensionEffect = tone switch
        {
            PressEvent.Tone.Hardline => +6f,
            PressEvent.Tone.Conciliatory => -4f,
            PressEvent.Tone.Populist => +3f,
            _ => 0f
        };
        int stabilityEffect = tone switch
        {
            PressEvent.Tone.Hardline => -4,
            PressEvent.Tone.Conciliatory => +3,
            PressEvent.Tone.Populist => +7,
            _ => 0
        };
        int warSupportEffect = tone switch
        {
            PressEvent.Tone.Hardline => +8,
            PressEvent.Tone.Conciliatory => -2,
            PressEvent.Tone.Populist => +6,
            _ => 0
        };

        // Tür katsayısı (miting en public / en güçlü etki)
        float typeMult = type switch
        {
            PressEvent.Type.PressConference => 1.0f,
            PressEvent.Type.TVSpeech => 1.2f,
            PressEvent.Type.Rally => 1.3f,
            _ => 1.0f
        };

        relationDelta = (int)Math.Round(relationDelta * typeMult);
        tensionEffect = (float)Math.Round(tensionEffect * typeMult, 1);
        stabilityEffect = (int)Math.Round(stabilityEffect * typeMult);
        warSupportEffect = (int)Math.Round(warSupportEffect * typeMult);

        // Deterministik başarı / geri-etki (%80 başarı)
        bool success = DeterministicRNG.NextBool(0.8);
        if (!success)
        {
            // Geri-etki: pozitifler ters çevrilir, negatifler şiddetlenir
            relationDelta = -Math.Abs(relationDelta) - 4;
            tensionEffect += 4f;
            stabilityEffect -= 6;
            warSupportEffect = -Math.Abs(warSupportEffect);
        }

        // Diğer tüm ülkelere genel ilişki etkisi (tek yönlü, ülke -> diğerleri)
        foreach (var other in CountryDatabase.All())
        {
            if (other.Id == countryId || other.IsAnnexed) continue;
            ModifyRelation(countryId, other.Id, relationDelta);
        }

        // Gösterge etkileri (0-100 clamp)
        country.StabilityPercent = Math.Clamp(country.StabilityPercent + stabilityEffect, 0, 100);
        country.WarSupportPercent = Math.Clamp(country.WarSupportPercent + warSupportEffect, 0, 100);
        if (tensionEffect != 0f) IncreaseTension(tensionEffect);

        string effectText = success
            ? $"Success ({tone}): relation {relationDelta:+#;-#;0}, tension {tensionEffect:+#;-#;0}, stability {stabilityEffect:+#;-#;0}, war support {warSupportEffect:+#;-#;0}"
            : $"BACKFIRE ({tone}): relation {relationDelta:+#;-#;0}, tension {tensionEffect:+#;-#;0}, stability {stabilityEffect:+#;-#;0}, war support {warSupportEffect:+#;-#;0}";

        _pressEvents.Add(new PressEvent
        {
            CountryId = countryId,
            EventType = type,
            ToneValue = tone,
            Day = GM?.TotalDays ?? 0,
            RelationDelta = relationDelta,
            TensionEffect = tensionEffect,
            StabilityEffect = stabilityEffect,
            WarSupportEffect = warSupportEffect,
            Success = success,
            EffectText = effectText
        });

        GD.Print($"[Diplomacy] Press event: {countryId} ({type}, {tone}, day {GM?.TotalDays}, success={success}) — {effectText}");
        return true;
    }

    // ===== M27.4: Yaptirimlar ve Ticaret Savlari =====

    public static IReadOnlyList<Sanction> Sanctions => _sanctions;

    /// <summary>
    /// Yaptirim uygular (yonlu: Initiator -> Target). Tur bazli iliski dususu ve gerilim artisi.
    /// Ayni yonlu ayni tur etkinden sadece biri aktif olabilir.
    /// </summary>
    public static bool ImposeSanction(string initiatorId, string targetId, Sanction.Type type, int durationDays)
    {
        var initiator = CountryDatabase.Get(initiatorId);
        var target = CountryDatabase.Get(targetId);
        if (initiator == null || target == null || initiator.IsAnnexed || target.IsAnnexed || initiatorId == targetId)
            return false;

        if (_sanctions.Any(s => s.Active && s.InitiatorId == initiatorId && s.TargetId == targetId && s.SanctionType == type))
            return false;

        int relationDelta = type switch
        {
            Sanction.Type.Trade => -10,
            Sanction.Type.Financial => -15,
            Sanction.Type.Tech => -8,
            Sanction.Type.Energy => -12,
            Sanction.Type.Arms => -18,
            _ => -5
        };
        float tensionEffect = type switch
        {
            Sanction.Type.Trade => 3f,
            Sanction.Type.Financial => 5f,
            Sanction.Type.Tech => 2f,
            Sanction.Type.Energy => 4f,
            Sanction.Type.Arms => 6f,
            _ => 3f
        };

        // Iki yonlu iliski dususu (yasaklayici ve yasaklanan tarafindan da algilanir)
        ModifyRelation(initiatorId, targetId, relationDelta);
        ModifyRelation(targetId, initiatorId, relationDelta);

        if (tensionEffect != 0f) IncreaseTension(tensionEffect);

        _sanctions.Add(new Sanction
        {
            Id = $"{initiatorId}-{targetId}-{type}-{GM?.TotalDays ?? 0}",
            InitiatorId = initiatorId,
            TargetId = targetId,
            SanctionType = type,
            DayStarted = GM?.TotalDays ?? 0,
            DurationDays = durationDays,
            Active = true,
            RelationDelta = relationDelta,
            TensionEffect = tensionEffect,
            EffectText = $"Yaptirim: {initiatorId} -> {targetId} ({type}, gun {GM?.TotalDays ?? 0}), iliski {relationDelta:+#;-#;0}, gerilim {tensionEffect:+#;-#;0}"
        });

        GD.Print($"[Diplomacy] Yaptirim: {initiatorId} -> {targetId} ({type}, day {GM?.TotalDays}, sure={durationDays}) — iliski {relationDelta:+#;-#;0}, gerilim {tensionEffect:+#;-#;0}");
        return true;
    }

    /// <summary>Etkin yaptirimlari bir ulkeden itibaren (veya ona yonelik) dondurur.</summary>
    public static List<Sanction> GetActiveSanctionsFor(string countryId) =>
        _sanctions.Where(s => s.Active && (s.InitiatorId == countryId || s.TargetId == countryId)).ToList();

    /// <summary>Iki ulke arasinda aktif istkili ticaret yaptirimi varsa ticaret savasi sayilir.</summary>
    public static bool IsTradeWar(string a, string b) =>
        _sanctions.Any(s => s.Active && s.InitiatorId == a && s.TargetId == b && s.SanctionType == Sanction.Type.Trade) &&
        _sanctions.Any(s => s.Active && s.InitiatorId == b && s.TargetId == a && s.SanctionType == Sanction.Type.Trade);

    /// <summary>Yaptirimi pasiflestirir ve iliskinin yarisini geri dondurur.</summary>
    public static bool LiftSanction(string sanctionId)
    {
        var s = _sanctions.FirstOrDefault(x => x.Id == sanctionId && x.Active);
        if (s == null) return false;
        s.Active = false;

        int recovery = -s.RelationDelta / 2;
        if (recovery != 0)
        {
            ModifyRelation(s.InitiatorId, s.TargetId, recovery);
            ModifyRelation(s.TargetId, s.InitiatorId, recovery);
        }

        GD.Print($"[Diplomacy] Yaptirim kalkirildi: {s.Id}");
        return true;
    }

    // ===== Yardımcı metodlar =====

    private static bool CanDoDiplomacy(string targetId, out CountryData target)
    {
        target = CountryDatabase.Get(targetId);
        if (!_initialized || Player == null || target == null) return false;
        if (targetId == GM.PlayerCountryId) return false;
        if (target.IsAnnexed) return false;
        return true;
    }

    public static void ModifyRelation(string fromId, string toId, int delta)
    {
        string key = $"{fromId}->{toId}";
        if (_relations.TryGetValue(key, out var rel))
        {
            rel.Value = Math.Clamp(rel.Value + delta, MinRelation, MaxRelation);
            rel.Opinion = Math.Clamp(rel.Opinion + delta, MinRelation, MaxRelation);
        }
        else
        {
            _relations[key] = new Relation { FromId = fromId, ToId = toId, Value = delta, Opinion = delta };
        }
    }

    public static int GetRelation(string fromId, string toId)
    {
        string key = $"{fromId}->{toId}";
        return _relations.TryGetValue(key, out var r) ? r.Value : 0;
    }

    public static int GetOpinion(string fromId, string toId)
    {
        string key = $"{fromId}->{toId}";
        return _relations.TryGetValue(key, out var r) ? r.Opinion : 0;
    }

    public static bool HasGuarantee(string guarantorId, string targetId) =>
        _guarantees.Any(g => g.GuarantorId == guarantorId && g.TargetId == targetId);

    public static bool HasMilitaryAccess(string fromId, string toId) =>
        _militaryAccess.Contains($"{fromId}->{toId}");

    public static bool HasNavalAccess(string fromId, string toId) =>
        _navalAccess.Contains($"{fromId}->{toId}");

    public static bool HasNonAggressionPact(string a, string b)
    {
        var pair = a.CompareTo(b) < 0 ? $"{a}-{b}" : $"{b}-{a}";
        return _nonAggressionPacts.Contains(pair);
    }

    public static Faction GetFactionOf(string countryId)
    {
        return _factions.Values.FirstOrDefault(f => f.IsMember(countryId));
    }

    public static Faction GetFactionById(string id) => _factions.GetValueOrDefault(id);

    private static string GetIdeologyColor(string ideology) => ideology switch
    {
        "Fascism" => "#8B5A2B",
        "Democracy" => "#4A7FB5",
        "Communism" => "#B03A2E",
        _ => "#8E9AA5"
    };

    // World Tension
    public static float WorldTension => _worldTension;

    public static void IncreaseTension(float amount)
    {
        _worldTension = Math.Clamp(_worldTension + amount, 0, 100);
        GD.Print($"[Diplomacy] World Tension: %{_worldTension:F1} (değişim: {amount:+#;-#;0})");
    }

    // Bekleyen aksiyonu tamamla
    private static void CompleteAction(DiplomaticAction action)
    {
        _pendingActions.Remove(action);
        GD.Print($"[Diplomacy] Aksiyon tamamlandı: {action.ActionType} ({action.InitiatorId} → {action.TargetId})");
    }

    // ===== Getters (UI için) =====

    public static bool IsInitialized => _initialized;
    public static IReadOnlyDictionary<string, Faction> Factions => _factions;
    public static IReadOnlyDictionary<string, Relation> Relations => _relations;
    public static IReadOnlyList<Guarantee> Guarantees => _guarantees;
    public static IReadOnlyList<string> MilitaryAccessList => _militaryAccess;
    public static IReadOnlyList<string> NavalAccessList => _navalAccess;
    public static IReadOnlyList<DiplomaticAction> PendingActions => _pendingActions;

    public static Relation GetRelationData(string fromId, string toId)
    {
        _relations.TryGetValue($"{fromId}->{toId}", out var r);
        return r;
    }

    public static List<Guarantee> GetGuaranteesBy(string countryId) =>
        _guarantees.Where(g => g.GuarantorId == countryId).ToList();

    public static List<string> GetGuaranteesFor(string countryId) =>
        _guarantees.Where(g => g.TargetId == countryId).Select(g => g.GuarantorId).ToList();

    // ===== M12: Save/Load =====

    public static SaveSystem.DiplomacySave GetSaveData()
    {
        return new SaveSystem.DiplomacySave
        {
            WorldTension = _worldTension,
            Factions = _factions.Values.ToArray(),
            Relations = _relations.Values.ToArray(),
            Guarantees = _guarantees.ToArray(),
            NonAggressionPacts = _nonAggressionPacts.ToArray(),
            MilitaryAccess = _militaryAccess.ToArray(),
            NavalAccess = _navalAccess.ToArray(),
            Meetings = _meetings.ToArray(),
            Agreements = _agreements.ToArray(),
            PressEvents = _pressEvents.ToArray(),
            Sanctions = _sanctions.ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.DiplomacySave data)
    {
        if (data == null) return;
        _factions.Clear();
        if (data.Factions != null)
            foreach (var f in data.Factions) _factions[f.Id] = f;
        _relations.Clear();
        if (data.Relations != null)
            foreach (var r in data.Relations) _relations[r.Key] = r;
        _guarantees.Clear();
        if (data.Guarantees != null)
            foreach (var g in data.Guarantees) _guarantees.Add(g);
        _nonAggressionPacts.Clear();
        if (data.NonAggressionPacts != null)
            foreach (var p in data.NonAggressionPacts) _nonAggressionPacts.Add(p);
        _militaryAccess.Clear();
        if (data.MilitaryAccess != null)
            foreach (var a in data.MilitaryAccess) _militaryAccess.Add(a);
        _navalAccess.Clear();
        if (data.NavalAccess != null)
            foreach (var a in data.NavalAccess) _navalAccess.Add(a);
        _worldTension = data.WorldTension;
        _initialized = true;

        // M27.1: görüşma geçmişini geri yükle
        _meetings.Clear();
        if (data.Meetings != null)
            foreach (var m in data.Meetings) _meetings.Add(m);

        // M27.2: anlaşma geçmişini geri yükle
        _agreements.Clear();
        if (data.Agreements != null)
            foreach (var a in data.Agreements) _agreements.Add(a);

        // M27.3: basın etkinliği geçmişini geri yükle
        _pressEvents.Clear();
        if (data.PressEvents != null)
            foreach (var p in data.PressEvents) _pressEvents.Add(p);

        // M27.4: yaptirim geçmişini geri yükle
        _sanctions.Clear();
        if (data.Sanctions != null)
            foreach (var s in data.Sanctions) _sanctions.Add(s);

        GD.Print($"DiplomacySystem: state yüklendi — Faksiyon={_factions.Count}, İlişki={_relations.Count}, Tension={_worldTension:F1}, Görüşme={_meetings.Count}, Anlaşma={_agreements.Count}, Basın={_pressEvents.Count}, Yaptırım={_sanctions.Count}");
    }
}