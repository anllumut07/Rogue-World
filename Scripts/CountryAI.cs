using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// AI ülke sistemi (M11): Oyuncu dışı ülkelerin ekonomi, askeri, diplomasi ve
/// odak kararlarını yürütür.
///
/// Mimari: AI, oyuncu-merkezli sistemlerin (EconomySystem vb.) HAFİF bir
/// simülasyonunu CountryData (tek doğruluk kaynağı) üzerinden yürütür.
/// - PP/manpower doğrudan CountryData'da birikir (oyuncuyla aynı kural: +2 PP/gün).
/// - İnşaat: boş sivil fabrika × 5 puan/gün, 70 gün × 15 fabrika = 5250 puan → +1 bina.
/// - Odak: FocusSystem'e delege edilir (AiPickFocus seçer, TickAiStates ilerletir).
/// - Araştırma: slot bazlı sayaç, tamamlanınca küçük pasif bonus + log.
///
/// Performans (kullanıcı şartı): ~50 AI ülke için günlük tick'te ağır hesap YOK.
/// - Hafif sayaç güncellemeleri: her gün TÜM AI ülkeleri (O(1) ülke başına).
/// - Karar verme: her gün yalnızca MaxDecisionsPerDay ülke (round-robin) →
///   50 ülkede her ülke ~5 günde bir karar alır, hız 5'te FPS düşmez.
///
/// Oyuncu ülkesi ASLA AI tarafından yönetilmez (Initialize'da hariç tutulur).
/// </summary>
public static class CountryAI
{
    // ===== AI ülke durumu (CountryData dışındaki simülasyon sayaçları) =====
    private class AIState
    {
        public CountryData Data;

        // Ekonomi (M11.1)
        public int BuildType;            // 0=yok, 1=Sivil, 2=Askeri, 3=Tersane, 4=Altyapı, 5=Konvoy
        public int BuildPoints;           // Birikmiş inşaat puanı

        // Araştırma (M11.1) — soyut sayaç: 140 dolunca 1 teknoloji "tamamlanır"
        public float ResearchProgress;
        public int ResearchCompleted;

        // Askeri (M11.2)
        public int Divisions;            // Saha tümeni
        public int TrainingDaysLeft;     // Aktif eğitim kalan gün
        public int DivisionsInTraining;

        // Diplomasi (M11.3) — sonraki adımda kullanılacak
        public string FactionId = "";    // Üye olunan faksiyon (boş = bağımsız)

        // İlk karar hemen verilebilsin (int.MinValue ile fark hesabı taşar!)
        public int LastDecisionDay = -1000;
        public bool AiExhausted;         // Odak ağacı tükendi (tekrar arama spam yok)

        // M19.8: Askeri hareket cooldown'u (her X günde bir harekete geçsin)
        public int LastMoveDay = -1000;
        public int LastPeaceDay = -1000;   // M42.4: barış değerlendirme cooldown'u

        // M48.1: ulus kurma değerlendirme cooldown'u — koşullar bir günde
        // birden fazla kez değerlendirilip PP harcanmasın.
        public int LastFormableDay = -1000;
    }

    // ===== Yapı tipleri — EconomySystem.BuildType ile aynı sıra/ Günler =====
    // 0=None, 1=CivilianFactory(70), 2=MilitaryFactory(70), 3=Dockyard(70), 4=Infrastructure(25), 5=Convoy(30)
    private static readonly int[] BuildDays = { 0, 70, 70, 70, 25, 30 };
    private static readonly string[] BuildNames = { "—", "Civilian Factory", "Military Factory", "Dockyard", "Infrastructure", "Convoy" };
    private const int PointsPerFactoryPerDay = 5;   // EconomySystem ile aynı
    private const int MaxFactoriesPerLine = 15;     // HOI4: kuyruk öğesi başına 15 fabrika
    private const int BuildPointsNeeded = 70 * 5 * 15; // 5250 — 15 fabrikayla 70 gün

    private const int MaxDecisionsPerDay = 10;      // Günde karar verilecek AI ülke sayısı
    private const int MinDecisionInterval = 5;      // Aynı ülke en erken kaç günde bir karar alır

    // Detaylı "başladı" logları yalnızca bu nüfus üstü ülkelerde (log spam kontrolü)
    private const long MajorPopulationThreshold = 20_000_000;

    private const double ResearchTarget = 140.0;    // "teknoloji" başına puan (≈175 gün @3 slot)
    private const int TrainingDaysPerDivision = 40; // Basit: 30 + 3×tabur ≈ 40
    private const int DivisionManpowerCost = 5000;

    // ===== M42.4: AI barış eşikleri =====
    // Puanı bu düzeyde AI "savaşı kazandım" deyip ilhak/kukla ister
    private const float WinOnScore = 75f;
    // Puanı bu düzeyde AI ilhak çok ağır bulur, kukla tercih eder
    private const int PuppetInsteadFactories = 40;
    // Puanı bu düzeyde AI toprak talebi + askersizleştirme ister
    private const float PushOnScore = 45f;
    // Rakipte bu puan varsa ve kendi puanı düşükse AI beyaz barış ister
    private const float FearOnScore = 60f;
    // Beyaz barış için kendi puanı bu sınırın altında olmalı
    private const float WhitePeaceMaxScore = 25f;
    // Savunan tarafta bu puan farkı varsa beyaz barış ister
    private const float WhitePeaceGap = 30f;
    // Tükendi sayma sınırları: fabrika / tümen / tümen kaybı oranı
    private const int ExhaustedFactories = 3;
    private const int ExhaustedDivisions = 3;
    private const float ExhaustedDivLoss = 0.6f;
    // AI'nin bir savaşta en fazla isteyebileceği il sayısı
    private const int MaxAiProvinceDemand = 6;

    // ===== Durum =====
    private static bool _initialized;
    private static readonly Dictionary<string, AIState> _states = new();
    private static readonly List<string> _aiCountryIds = new(); // round-robin listesi (ilhak edilen düşer)
    private static int _cursor;

    private static GameManager GM => GameManager.Instance;

    // ===== Başlatma / sıfırlama =====

    // GameManager.SetPlayerCountry sonrası çağrılır; yeni oyunda ResetForNewGame temizler.
    public static void Initialize()
    {
        _states.Clear();
        _aiCountryIds.Clear();
        _cursor = 0;

        string playerId = "";
        if (GM != null && !string.IsNullOrEmpty(GM.PlayerCountryId))
            playerId = CountryDatabase.NormalizeId(GM.PlayerCountryId);

        foreach (var data in CountryDatabase.All())
        {
            if (data == null || data.IsAnnexed) continue;
            if (!string.IsNullOrEmpty(playerId) && data.Id == playerId) continue; // oyuncu ülkesi ASLA AI

            _states[data.Id] = new AIState
            {
                Data = data,
                Divisions = Math.Max(1, data.StartingDivisions),
            };
            _aiCountryIds.Add(data.Id);
        }

        _initialized = _states.Count > 0;
        GD.Print($"CountryAI hazır: {_states.Count} AI ülke " +
                 $"(oyuncu: {GM?.PlayerCountryId ?? "seçili değil"} — AI tarafından yönetilmez)");
    }

    // Yeni oyun (GameManager.ResetForNewGame) — eski durum temizlenir.
    public static void Reset() => Initialize();

    public static bool IsAiCountry(string countryId) => _states.ContainsKey(countryId);

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====
    public static void DailyTick()
    {
        if (!_initialized || GM == null) return;

        // --- 1) Hafif güncellemeler: her gün TÜM AI ülkeleri (ülke başına O(1)) ---
        TickLightUpdates();

        // --- 2) Odak ilerlemesi: FocusSystem'deki AI state'leri (oyuncu dışı) ---
        FocusSystem.TickAiStates();

        // --- 3) Kararlar: her gün yalnızca bir DİLİM ülke (round-robin) ---
        TickDecisions();
    }

    private static void TickLightUpdates()
    {
        for (int i = _aiCountryIds.Count - 1; i >= 0; i--)
        {
            string id = _aiCountryIds[i];
            var st = _states[id];
            var data = st.Data;

            // İlhak edilmişse AI listesinden düşür
            if (data.IsAnnexed)
            {
                _states.Remove(id);
                _aiCountryIds.RemoveAt(i);
                continue;
            }

            // PP + manpower (oyuncuyla aynı kural) — CountryData tek doğruluk kaynağı
            data.PoliticalPower += 2.0;
            data.ManpowerPool += data.ManpowerPerDay;

            // İnşaat puanı: boş sivil fabrika × 5/gün (CG ideolojiye göre)
            if (st.BuildType > 0)
            {
                int freeCivs = FreeCivFactories(data);
                if (freeCivs > 0)
                {
                    st.BuildPoints += Math.Min(freeCivs, MaxFactoriesPerLine) * PointsPerFactoryPerDay;
                    if (st.BuildPoints >= BuildPointsNeeded)
                        CompleteBuild(st, data);
                }
            }

            // Araştırma: slot başına ~0.25 puan/gün (3 slot ≈ 187 gün/tech)
            st.ResearchProgress += data.ResearchSlots * 0.25f;
            if (st.ResearchProgress >= ResearchTarget)
                CompleteResearch(st, data);

            // Eğitim
            if (st.TrainingDaysLeft > 0)
            {
                st.TrainingDaysLeft--;
                if (st.TrainingDaysLeft <= 0)
                {
                    st.DivisionsInTraining--;
                    st.Divisions++;
                    if (IsMajor(data))
                        GD.Print($"[AI] {data.Id}: tümen eğitimi tamamlandı → {st.Divisions} tümen");
                }
            }
        }
    }

    private static void TickDecisions()
    {
        int n = _aiCountryIds.Count;
        if (n == 0) return;

        int processed = 0;
        for (int i = 0; i < n && processed < MaxDecisionsPerDay; i++)
        {
            int idx = (_cursor + i) % n;
            string id = _aiCountryIds[idx];
            var st = _states[id];

            if (GM.TotalDays - st.LastDecisionDay < MinDecisionInterval) continue;

            st.LastDecisionDay = GM.TotalDays;
            RunDecisions(st);
            processed++;
        }
        _cursor = (_cursor + processed) % Math.Max(1, n);
    }

    // ===== Kararlar (günde en fazla 10 ülke için) =====
    private static void RunDecisions(AIState st)
    {
        var data = st.Data;

        // 1) İnşaat: boşta → ne inşa edilecek?
        if (st.BuildType == 0 && data.CivFactories > 0)
            DecideBuild(st, data);

        // 2) Odak: aktif yoksa AI seçim mantığı (FocusSystem.AiPickFocus başlatır + loglar)
        if (!st.AiExhausted)
        {
            var fst = FocusSystem.GetOrCreateState(data.Id);
            if (fst?.Active == null)
            {
                if (FocusSystem.AiPickFocus(data.Id) == null)
                    st.AiExhausted = true; // ağaç tükendi — tekrar arama yok
            }
        }

        // 3) Askeri: manpowere göre yeni tümen
        if (st.TrainingDaysLeft <= 0 && data.ManpowerPool > DivisionManpowerCost * 4 && st.Divisions < DivisionCap(data))
            StartTraining(st, data);

        // 4) Diplomasi: M11.3'te (faksiyon/garanti/savaş) — iskelet
        MaybeDiplomacy(st, data);

        // 4b) Barış davranışı (M42.4): ezilen/rakip tarafta savaşı bitirme arzusu.
        // Hergün değil — cooldown'lu çalışır ki savaş ilanı ile çelişmesin.
        MaybePeace(st, data);

        // 4c) Ulus kurma (M48.1): kültürü yeterli il + yeterli PP varsa
        // FormableSystem sürecini başlatır. Sürecin kendisi FormableSystem.AdvanceAll
        // ile GameManager günlük tick'inde ilerler — burada yalnızca başlatma kararı var.
        MaybeFormNation(st, data);

        // 5) Askeri hareket: AI ordularını düşman illere hareket ettir (M19.8)
        MaybeMoveArmies(st, data);
    }

    // --- 11.1 Ekonomi kararları ---

    private static void DecideBuild(AIState st, CountryData data)
    {
        // Heuristik: askeri fabrika azsa veya faşist orduya ağırlık verir;
        // tersane petrol/donanma ülkesinde; yoksa sivil fabrika (ekonomi büyür).
        string ideology = data.Ideology ?? "Neutral";
        int buildType;

        if (data.MilFactories < 10 || (ideology == "Fascism" && DeterministicRNG.NextBool(0.6)))
            buildType = 2; // Askeri
        else if (data.Dockyards < 4 && data.Oil > 0)
            buildType = 3; // Tersane
        else
            buildType = 1; // Sivil

        st.BuildType = buildType;
        st.BuildPoints = 0;

        if (IsMajor(data))
            GD.Print($"[AI] {data.Id} inşaat başlattı: {BuildNames[buildType]} ({BuildDays[buildType]} gün @15 fabrika)");
    }

    private static void CompleteBuild(AIState st, CountryData data)
    {
        switch (st.BuildType)
        {
            case 1: data.CivFactories++; break;
            case 2: data.MilFactories++; break;
            case 3: data.Dockyards++; break;
            case 4: /* altyapı — mekaniği yok, sayı tutulmaz */ break;
            case 5: /* konvoy — EconomySystem oyuncu için tutar */ break;
        }

        // Örnek AI logu (kullanıcı isteği: "GER fabrika inşa etti" biçiminde)
        GD.Print($"[AI] {data.Id} built {BuildNames[st.BuildType]} " +
                 $"(Civ={data.CivFactories}, Mil={data.MilFactories}, Dock={data.Dockyards})");

        st.BuildType = 0;
        st.BuildPoints = 0;
    }

    private static void CompleteResearch(AIState st, CountryData data)
    {
        st.ResearchProgress -= (float)ResearchTarget;
        st.ResearchCompleted++;

        // Her 5. teknolojide küçük ekonomik bonus: +1 sivil fabrika verimi yerine
        // basitçe 1 sivil fabrika (soyut "sanayi gelişimi") — temsilî, ölçülü
        if (st.ResearchCompleted % 5 == 0)
        {
            data.CivFactories++;
            GD.Print($"[AI] {data.Id} araştırma ilerlemesi: {st.ResearchCompleted} tech " +
                     $"(sanayi bonusu: +1 sivil fabrika → {data.CivFactories})");
        }
        else if (IsMajor(data))
        {
            string[] cats = { "Infantry", "Support", "Artillery", "Industry", "Electronics", "Doctrine" };
            GD.Print($"[AI] {data.Id} completed research: {cats[DeterministicRNG.NextInt(0, cats.Length)]} " +
                     $"#{st.ResearchCompleted}");
        }
    }

    // --- 11.2 Askeri kararları (basit) ---

    private static int DivisionCap(CountryData data) =>
        Math.Max(4, (int)(data.Population / 1_000_000) + data.MilFactories / 2);

    private static void StartTraining(AIState st, CountryData data)
    {
        st.TrainingDaysLeft = TrainingDaysPerDivision;
        st.DivisionsInTraining++;
        data.ManpowerPool -= DivisionManpowerCost;

        if (IsMajor(data))
            GD.Print($"[AI] {data.Id} tümen yetiştiriyor ({TrainingDaysPerDivision} gün, " +
                     $"manpower: {data.ManpowerPool:N0})");
    }

    // AI↔AI savaşlarında tümen kaybı (M11.2 savaş çözümü WarSystem'de çalışır,
    // burada sadece AI tümen havuzını güncelleyen API)
    public static void ApplyDivisionLoss(string countryId, int lost)
    {
        if (!_states.TryGetValue(countryId, out var st)) return;
        st.Divisions = Math.Max(0, st.Divisions - lost);
    }

    public static int GetDivisions(string countryId) =>
        _states.TryGetValue(countryId, out var st) ? st.Divisions : 0;

    // --- 11.3 Diplomasi kararları (M11.3) ---

    /// <summary>
    /// AI savaş hedefi uygunluk kontrolü (JOB1 kısıtlaması):
    /// hedef yalnızca şu koşullardan en az birini sağlamalı:
    /// 1) Komşu ülke (GeoJSON komşuluk grafiı)
    /// 2) Aralarında toprak iddiası var (territorial_claims.json)
    /// 3) İdeolojik düşman (farklı ideoloji)
    /// 4) Faksiyon liderinin savaştığı düşman (faksiyon çağrısı)
    /// </summary>
    private static bool IsAiWarTargetEligible(CountryData attacker, CountryData target)
    {
        // 1) Komşu mu?
        if (NeighborSystem.AreNeighbors(attacker.Id, target.Id)) return true;

        // 2) Toprak iddiası var mı? (yönlü veya ters yönlü)
        var claims = CasusBelliSystem.Claims;
        foreach (var c in claims)
        {
            if ((c.OwnerId == attacker.Id && c.TargetId == target.Id) ||
                (c.OwnerId == target.Id && c.TargetId == attacker.Id))
                return true;
        }

        // 3) İdeolojik düşman mı?
        if (!string.Equals(attacker.Ideology, target.Ideology, StringComparison.OrdinalIgnoreCase)) return true;

        // 4) Faksiyon çağrısı: saldırganın faksiyon lideri bu hedefle savaşıyor mu?
        var faction = DiplomacySystem.GetFactionOf(attacker.Id);
        if (faction != null && faction.LeaderId != attacker.Id)
        {
            foreach (var enemyId in DiplomacySystem.GetEnemiesAtWar(faction.LeaderId))
            {
                if (enemyId == target.Id) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// JOB1: Eş zamanlı savaş kısıtı. Bu ülke + hedef ile yeni bir savaş başlatmak
    /// savaşta olan AI ülke sayısını aşırı yükseltecekse true döner (savaş adımı atlanır).
    /// Kısıt: ai_war_settings.json → MaxAiCountriesAtWarPercent (varsayılan %10).
    /// Not: Tarihi olaylar/garanti tetiklemeleri bu kısıta tabi değildir (hikaye akışı korunur).
    /// </summary>
    private static bool AiAtWarCapReached(CountryData target)
    {
        // Şu an savaşta olan ülkeleri topla
        var atWar = new HashSet<string>();
        foreach (var w in WarSystem.ActiveWars)
        {
            if (w.IsEnded) continue;
            atWar.Add(w.AttackerId);
            atWar.Add(w.DefenderId);
        }

        // Yeni savaş kaç AI ülkesi daha savaşa sokar?
        int added = 0;
        if (!atWar.Contains(target.Id) && IsAiCountry(target.Id)) added++;
        // (saldıran bizzat bu ülke — çağıranın savaşta olup olmadığı da sayılır)
        // Not: Bu metod yalnız MaybeDiplomacy'nin savaş adımından çağrılır; saldırgan
        // henüz savaşta değilse +1.

        int aiAtWar = 0;
        foreach (var id in atWar)
            if (IsAiCountry(id)) aiAtWar++;

        int aiTotal = _aiCountryIds.Count;
        if (aiTotal == 0) return false;

        // Saldırgan yeni savaşa giriyorsa +1 (savunan yukarıda eklendi)
        int newTotal = aiAtWar + added + 1; // +1 saldırgan
        int cap = Math.Max(1, aiTotal * CasusBelliSystem.MaxAiAtWarPercent / 100);
        return newTotal > cap;
    }

    private static void MaybeDiplomacy(AIState st, CountryData data)
    {
        // PP yetersizse diplomasi yok
        if (data.PoliticalPower < 25) return;

        string ideology = data.Ideology ?? "Neutral";

        // 1) Faksiyon: faşist → Axis, kominist → Comintern, demokrasi → Allies
        if (string.IsNullOrEmpty(st.FactionId))
        {
            string targetFaction = ideology switch
            {
                "Fascism" => "Axis",
                "Communism" => "Comintern",
                "Democracy" => "Allies",
                _ => ""
            };

            if (!string.IsNullOrEmpty(targetFaction) && DiplomacySystem.GetFactionById(targetFaction) != null)
            {
                bool joined = DiplomacySystem.RequestJoinFaction(targetFaction, data.Id);
                if (joined)
                {
                    st.FactionId = targetFaction;
                    GD.Print($"[AI] {data.Id} faksiyona katıldı: {targetFaction} (İdeoloji: {ideology})");
                    return; // bu tur diplomaside kalan PP yeterli olmayabilir
                }
            }
            // Kendi faksiyonu kurma (büyük ülkeler)
            else if (data.TotalFactories >= 25 && DeterministicRNG.NextBool(0.02))
            {
                string newFactionId = $"{data.Id}_Faction";
                bool created = DiplomacySystem.CreateFaction(newFactionId, data.Name + " Faction", data.Id, ideology);
                if (created)
                {
                    st.FactionId = newFactionId;
                    GD.Print($"[AI] {data.Id} faksiyon KURDU: {newFactionId} (İdeoloji: {ideology})");
                    return;
                }
            }
        }

        // 2) Garanti: küçük demokrasileri koru (büyük demokrasiler)
        if (ideology == "Democracy" && data.TotalFactories >= 15 && DeterministicRNG.NextBool(0.05))
        {
            // Rastgele bir minor demokrasiyi garanti et (deterministik shuffle)
            var candidates = CountryDatabase.All()
                .Where(c => c != null && !c.IsAnnexed && c.Ideology == "Democracy"
                         && c.TotalFactories < 10 && c.Id != data.Id)
                .ToList();
            DeterministicRNG.Shuffle(candidates);
            foreach (var target in candidates.Take(3))
            {
                if (DiplomacySystem.GetRelation(data.Id, target.Id) >= 0
                    && !DiplomacySystem.HasGuarantee(data.Id, target.Id)
                    && data.PoliticalPower >= 25)
                {
                    if (DiplomacySystem.AddGuarantee(data.Id, target.Id))
                    {
                        data.PoliticalPower -= 25;
                        GD.Print($"[AI] {data.Id} garanti verdi: {target.Name} ({target.Id})");
                        break; // bir garanti yeter
                    }
                }
            }
        }

        // 3) Savaş ilanı: casus belli gerektirir — gerekçe yoksa olasılıkla "Build Case" başlat
        if (data.PoliticalPower >= 50 && st.Divisions >= 10)
        {
            // Hedef seçimi: yalnızca komşular, toprak iddiası olanlar ve ideolojik düşmanlar
            // (aynı ideoloji + komşu değil + iddia yok = hedeflenmez)
            var targets = CountryDatabase.All()
                .Where(c => c != null && !c.IsAnnexed && c.Id != data.Id
                         && !WarSystem.IsAtWarWith(data.Id, c.Id)
                         && IsAiWarTargetEligible(data, c)) // yeni kısıtlama
                .ToList();
            DeterministicRNG.Shuffle(targets);
            targets = targets.Take(5).ToList();

            foreach (var target in targets)
            {
                // JOB1: eş zamanlı savaş kısıtı — bu hedefle savaş açılırsa
                // savaşta AI yüzdesi aşıldıysa bu hedefe atla (diğer hedefler denenebilir)
                if (AiAtWarCapReached(target)) continue;

                // Güç karşılaştırması: saldırgan en az %80 güçte olmalı
                int attStrength = st.Divisions + data.TotalFactories / 3;
                var targetSt = _states.GetValueOrDefault(target.Id);
                int defDivs = targetSt?.Divisions ?? target.StartingDivisions;
                int defStrength = defDivs + target.TotalFactories / 3;

                // M31.5: Nükleer caydırıcılık — hedef nükleer-silahlı ve caydırıcılık doktrinindeyse
                // çok daha güçlü sayılır (AI onu saldırıdan kaçınır). Yalnızca oyuncu 핵 durumu
                // takip edilir (NuclearProgramSystem oyuncuya özeldir).
                bool targetIsNuclearDeterrence = target.Id == GM.PlayerCountryId
                    && NuclearProgramSystem.Warheads > 0
                    && NuclearProgramSystem.Doctrine == NuclearProgramSystem.NuclearDoctrine.Deterrence;
                float deterrenceMultiplier = targetIsNuclearDeterrence ? 2.0f : 1.0f;

                // M31.5: Yaptırım tepkisi — hedef, AI'yi aktif yaptırıyorsa AI onu daha zayıf görür
                // (yaptırım = casus belli benzeri tepki; saldırıyı daha cazip kılar)
                bool sanctionedByTarget = DiplomacySystem.Sanctions.Any(s =>
                    s.Active && s.InitiatorId == target.Id && s.TargetId == data.Id);
                float sanctionReactionMultiplier = sanctionedByTarget ? 0.6f : 1.0f;

                int effectiveDefStrength = (int)(defStrength * deterrenceMultiplier * sanctionReactionMultiplier);

                if (attStrength < effectiveDefStrength * 0.8) continue; // çok güçlü hedef

                // Savaş maliyeti (war goal indirimi olabilir)
                int warCost = WarSystem.CalculateWarGoalCost(target.Id);
                if (data.PoliticalPower < warCost) continue;

                // M17A: Casus belli kontrolü — geçerli gerekçe var mı?
                if (CasusBelliSystem.HasValidCasusBelli(data.Id, target.Id))
                {
                    // Savaş ilanı (WarSystem casus belli kontrolünü yapar)
                    bool declared = WarSystem.DeclareWar(data.Id, target.Id);
                    if (declared)
                    {
                        data.PoliticalPower -= warCost;
                        data.WarSupportPercent = Math.Min(100, data.WarSupportPercent + 10);
                        data.StabilityPercent = Math.Max(0, data.StabilityPercent - 5);

                        // Ofansif emri (güçlüysen)
                        if (attStrength > defStrength * 1.2f)
                            WarSystem.SetOffensive(target.Id, true);

                        GD.Print($"[AI] {data.Id} SAVAŞ İLAN ETTİ: {target.Name} ({target.Id}) " +
                                 $"maliyet={warCost} PP, güç={attStrength}/{defStrength}, " +
                                 $"PP={data.PoliticalPower:N0}, WS={data.WarSupportPercent:N0}%");
                        break; // bir savaş yeterli
                    }
                }
                else
                {
                    // Gerekçe yoksa olasılıkla inşaat başlat (iddia > ideoloji > fabrike)
                    CasusBelliSystem.MaybeStartAIJustification(data.Id, target.Id);
                }
            }
        }

        // 4) Faksiyon üyesiysen, faksiyon liderinin savaşı varsa katıl (call to arms simülasyonu)
        // M17A: Faksiyon çağrısı casus belli gerektirmez (JoinWarByFactionCall)
        // M17.2: Yalnızca DefensePact faksiyonları çağrı yapar (PoliticalEconomic bloklar çağrı yapmaz)
        if (!string.IsNullOrEmpty(st.FactionId))
        {
            var faction = DiplomacySystem.GetFactionById(st.FactionId);
            if (faction != null && faction.IsDefensePact && faction.LeaderId != data.Id && data.PoliticalPower >= 50)
            {
                // Liderle savaşta mıyız? Değilsek ve lider savaşı varsa katıl
                if (!WarSystem.IsAtWarWith(data.Id, faction.LeaderId))
                {
                    // Basit: %10 şans call to arms simülasyonu
                    if (DeterministicRNG.NextBool(0.1))
                    {
                        var leader = CountryDatabase.Get(faction.LeaderId);
                        if (leader != null && !leader.IsAnnexed)
                        {
                            // Liderin savaştığı bir düşman varsa ona da savaş ilan et
                            foreach (var enemyId in DiplomacySystem.GetEnemiesAtWar(faction.LeaderId))
                            {
                                if (!WarSystem.IsAtWarWith(data.Id, enemyId)
                                    && data.PoliticalPower >= 25)
                                {
                                    WarSystem.JoinWarByFactionCall(data.Id, enemyId);
                                    data.PoliticalPower -= 25;
                                    GD.Print($"[AI] {data.Id} FAKSİYON LİDERİNİ DESTEKLİYOR: {enemyId} ile savaş (Call to Arms / Faksiyon Çağrısı)");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // ===== M42.4: AI barış davranışı =====

    /// <summary>
    /// Savaştaki AI'nin barış isteğini değerlendirir ve (rakip AI ise) uygular.
    /// Cooldown: aynı ülke en erken 20 günde bir barış düşünür; bir savaş her halde
    /// değerlendirilir ama savaş ilanı günü yapılmaz (aynı gün hem savaş hem barış
    /// çelişmesini önlemek için savaş ilan edilmişse turu atlar).
    /// </summary>
    private static void MaybePeace(AIState st, CountryData data)
    {
        if (GM.TotalDays - st.LastPeaceDay < 20) return;

        // Savaşta değilse barış değerlendirmesi yok
        bool atWar = WarSystem.Wars.Any(w => w != null && !w.IsEnded &&
            (string.Equals(w.AttackerId, data.Id, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(w.DefenderId, data.Id, StringComparison.OrdinalIgnoreCase)));
        if (!atWar) return;

        // Bu turda savaş ilan ettiyse barış düşünmesin (aynı gün ters istikamet olmaz)
        // — MaybeDiplomacy aynı RunDecisions turunda koştuğu için elde PP değişti,
        // savaş ilanı FF tabanında cooldown gerektirir. Kısa koruma: savaş sayısı
        // son 3 günde arttıysa bu turu atla.
        // WarSystem.Wars tamamı gezildi; yeni ilan edilmiş savaşları tespit etmek için
        // yol: başlangıç günü bugüne çok yakın olan savaşları düşünümsüz say.
        bool freshWar = WarSystem.Wars.Any(w => w != null && !w.IsEnded &&
            GM.TotalDays - w.StartDay <= 2 &&
            (string.Equals(w.AttackerId, data.Id, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(w.DefenderId, data.Id, StringComparison.OrdinalIgnoreCase)));
        if (freshWar) return;

        st.LastPeaceDay = GM.TotalDays;

        string decision = EvaluatePeace(data.Id);
        if (decision == "none") return;

        // Rakip oyuncuysa anlaşma YAPILMAZ (panel karar verir) — yalnız log + sinyal
        if (decision.Split(':') is { Length: >= 2 } p && p[1] == GM.PlayerCountryId)
        {
            GD.Print($"[AIPeace] {data.Id} oyuncuya barış İSTEMİK (kabul edilmedi, panel bekliyor): {decision}");
            return;
        }

        ApplyPeaceDecision(data.Id, decision);
    }

    /// <summary>
    /// M48.1: AI ulus kurma değerlendirmesi.
    ///
    /// Kurallar:
    /// - Cooldown: aynı ülke en erken 30 günde bir kurmayı dener (PP'yi aynın
    ///   günde birden fazla süreçte harcamamak için).
    /// - O ülkede zaten bekleyen süreç varsa dokunulmaz.
    /// - Adaylar <see cref="FormableDataSystem.ForParent"/> ile ebe kaynak
    ///   listesinden alınır; RNG YOKTUR — sıradaki İLK uygun aday seçilir
    ///   (deterministik: aynı dünya + aynı gün → aynı karar).
    /// - Koşullar tam değilse hiçbir şey yapılmaz (PP harcanmaz, süreç başlamaz).
    ///
    /// Oyuncu ülkesi bu metod tarafından ASLA görülmez: RunDecisions yalnızca
    /// AI <see cref="AIState"/>'leri için çağrılır (M11 kuralı).
    /// </summary>
    private static void MaybeFormNation(AIState st, CountryData data)
    {
        if (GM.TotalDays - st.LastFormableDay < 30) return;

        // Bu turda kurma deneniyor — koşul sağlansa da tek deneme
        st.LastFormableDay = GM.TotalDays;

        if (FormableSystem.IsPending(data.Id)) return;

        var candidates = FormableDataSystem.ForParent(data.Id);
        if (candidates == null || candidates.Count == 0) return;

        foreach (var def in candidates)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) continue;
            if (!FormableSystem.CanForm(data.Id, def.Id)) continue;

            if (FormableSystem.TryStart(data.Id, def.Id, out var reason))
            {
                GD.Print($"[AIFormable] {data.Name} ({data.Id}) kurma sürecini başlattı: " +
                         $"{def.Name} ({def.Id}) — gerekli {def.RequiredProvinces} il, " +
                         $"{def.DaysRequired} gün, PP {def.PoliticalPowerCost}");
                return; // Aynı anda tek süreç
            }

            GD.Print($"[AIFormable] {data.Id} → {def.Id} başlatılamadı: {reason}");
        }
    }

    /// <summary>
    /// Savaştaki AI ülkesinin barış isteğini değerlendirir. Deterministik:
    /// eşikler puan/fabrika/askeri güce bakar, RNG yalnızca "bu gün değerlendir"
    /// anında kullanılır ve sonuç puan gibi kalıcı duruma yazılmaz.
    ///
    /// Kurallar (SADECE kendi lehine olan taraf teklif eder):
    /// - Kendi puanı >= WinOnScore      → ilhak/kukla talebi (ApplyDeal)
    /// - Kendi puanı >= PushOnScore     → il talebi + askersizleştirme
    /// - Karşı taraf puanı >= FearOnScore ve kendi gücü düşüyor → beyaz barış
    ///   (rakip daha da puan toplamadan savaştan çıkma arzusu)
    /// - Kendi fabrikası tükendiyse / tümeni çok düştüyse ve puanı negative → beyaz barış
    ///
    /// Oyuncuya karşı ASLA otonom anlaşma YAPILMAZ (sadece log + sinyal);
    /// oyuncu M42.5 panelinden karar verir. AI–AI arasında anlaşma doğrudan uygulanır.
    /// </summary>
    public static string EvaluatePeace(string countryId)
    {
        var data = CountryDatabase.Get(countryId);
        if (data == null) return "none";

        // Savaşları taraflarına göre ayr (aynı savaş iki kez değerlendirilmez)
        foreach (var w in WarSystem.Wars)
        {
            if (w == null || w.IsEnded) continue;
            bool isAttacker = string.Equals(w.AttackerId, countryId, StringComparison.OrdinalIgnoreCase);
            bool isDefender = string.Equals(w.DefenderId, countryId, StringComparison.OrdinalIgnoreCase);
            if (!isAttacker && !isDefender) continue;

            string enemyId = isAttacker ? w.DefenderId : w.AttackerId;
            if (string.IsNullOrEmpty(enemyId)) continue;
            var enemy = CountryDatabase.Get(enemyId);
            if (enemy == null) continue;

            float myScore = WarScoreSystem.ScoreForSide(w, countryId);
            float enemyScore = WarScoreSystem.ScoreForSide(w, enemyId);
            string myKind = isAttacker ? "saldıran" : "savunan";

            var st = _states.GetValueOrDefault(countryId);
            int myDivs = st?.Divisions ?? data.StartingDivisions;

            // Rakipten sonra kendi durumu ne kadar kötü? (tümen kaybı oranı)
            float divLoss = data.StartingDivisions > 0
                ? 1f - (float)myDivs / data.StartingDivisions
                : 1f;

            // ---- 1) Ezici uzunluk: kendi puanı yüksek → ilhak ----
            if (myScore >= WinOnScore && !enemy.IsAnnexed)
            {
                // Büyük ülkeyi ilhak etmek pahalı: fabrikası çoksa kukla daha makul
                if (enemy.TotalFactories >= PuppetInsteadFactories)
                    return $"puppet:{enemyId}";
                return $"annex:{enemyId}";
            }

            // ---- 2) Üstünlük: il talebi + askersizleştirme ----
            if (myScore >= PushOnScore)
            {
                int demand = ProvinceDemandCount(enemyId);
                if (demand > 0) return $"demand:{enemyId}:{demand}";
            }

            // ---- 3) Racı kötüleşiyor ve rakip puan topluyorsa → beyaz barış ----
            //    (dayanıksız taraf savaştan çıkmak ister; kendi puanı düşük)
            if (enemyScore >= FearOnScore && myScore <= WhitePeaceMaxScore)
            {
                return $"white:{enemyId}";
            }

            // ---- 4) Bütünüyle tükendiyse ve savaştan bir kazanç yoksa → beyaz barış ----
            bool exhausted = myDivs <= ExhaustedDivisions || data.TotalFactories <= ExhaustedFactories;
            if (exhausted && divLoss >= ExhaustedDivLoss && myScore < PushOnScore)
            {
                return $"white:{enemyId}";
            }

            // ---- 5) Savunan, uzun süredir ilerleme yoksa ve askeri üstünlük rakipte → beyaz barış ----
            if (isDefender && enemyScore - myScore >= WhitePeaceGap)
            {
                return $"white:{enemyId}";
            }

            GD.Print($"[AIPeace] {countryId} ({myKind}) barış düşünülmedi: puanım={myScore:F1}, " +
                     $"rakip={enemyScore:F1}, tümen kaybı=%{divLoss * 100f:N0}, fabrika={data.TotalFactories}");
        }

        return "none";
    }

    /// <summary>Barış eylemini uygular (EvaluatePeace sonucu). OOn-plLayer hedefinde çalışmaz.</summary>
    public static void ApplyPeaceDecision(string countryId, string decision)
    {
        if (string.IsNullOrEmpty(decision) || decision == "none") return;

        string[] parts = decision.Split(':');
        string action = parts[0];
        if (parts.Length < 2) return;
        string enemyId = parts[1];

        // Oyuncuya karşı otonom anlaşma yapılmaz — panel (M42.5) karar verir
        if (enemyId == GM.PlayerCountryId) return;
        if (enemyId == countryId) return;

        var enemy = CountryDatabase.Get(enemyId);
        if (enemy == null) return;

        // Savaşı bul (hâlâ aktif mi?)
        var war = WarSystem.Wars.FirstOrDefault(w => !w.IsEnded &&
            ((string.Equals(w.AttackerId, countryId, StringComparison.OrdinalIgnoreCase) &&
              string.Equals(w.DefenderId, enemyId, StringComparison.OrdinalIgnoreCase)) ||
             (string.Equals(w.DefenderId, countryId, StringComparison.OrdinalIgnoreCase) &&
              string.Equals(w.AttackerId, enemyId, StringComparison.OrdinalIgnoreCase))));
        if (war == null) return;

        var me = CountryDatabase.Get(countryId);
        if (me == null) return;

        float myScore = WarScoreSystem.ScoreForSide(war, countryId);

        switch (action)
        {
            case "annex":
            {
                // Puan tükendiyse kapılmış olabilir: yeniden doğrula
                var demands = new List<PeaceConferenceSystem.Demand>
                {
                    new PeaceConferenceSystem.Demand("annex_country", 1)
                };
                PeaceConferenceSystem.ApplyDeal(war, demands, countryId, enemyId);
                GD.Print($"[AIPeace] {countryId} İLHAK teklif etti → {enemyId} (puan={myScore:F1})");
                break;
            }

            case "puppet":
            {
                var demands = new List<PeaceConferenceSystem.Demand>
                {
                    new PeaceConferenceSystem.Demand("puppet_state", 1)
                };
                PeaceConferenceSystem.ApplyDeal(war, demands, countryId, enemyId);
                GD.Print($"[AIPeace] {countryId} KUKLA teklif etti → {enemyId} (puan={myScore:F1})");
                break;
            }

            case "demand":
            {
                int n = parts.Length >= 3 ? int.Parse(parts[2]) : 0;
                if (n <= 0) break;
                var demands = new List<PeaceConferenceSystem.Demand>
                {
                    new PeaceConferenceSystem.Demand("province_demand", n),
                    new PeaceConferenceSystem.Demand("demilitarize", 1)
                };
                var res = PeaceConferenceSystem.ApplyDeal(war, demands, countryId, enemyId);
                GD.Print($"[AIPeace] {countryId} TALEP etti → {enemyId}: {n} il + askersizleştirme " +
                         $"(puan={myScore:F1}, red={res.Rejected.Count})");
                break;
            }

            case "white":
            {
                // Beyaz barış: statüko — kapitülasyon bayrağı konmaz (WhitePeaceDeal)
                PeaceConferenceSystem.WhitePeaceDeal(war);
                GD.Print($"[AIPeace] {countryId} BEYAZ BARIŞ istedi → {enemyId} (puanım={myScore:F1})");
                break;
            }

            default:
                break;
        }
    }

    /// <summary>Rakibin ili sayısından talep boyutu (en fazla MaxAiProvinceDemand).</summary>
    private static int ProvinceDemandCount(string enemyId)
    {
        int count = ProvinceOwnershipSystem.ProvinceCountFor(enemyId);
        return Math.Clamp(count / 4, 1, MaxAiProvinceDemand);
    }

    // ===== M19.8: AI askeri hareket =====

    /// <summary>
    /// AI ordularını düşman illere hareket ettirir. Her 7 günde bir,
    /// savaşta olan bir düşmanın rastgele ilini hedef alır.
    /// Sadece kara orduları; deniz/hava M20+'de.
    /// </summary>
    private static void MaybeMoveArmies(AIState st, CountryData data)
    {
        // Cooldown: her 7 günde bir harekete geç
        if (GM.TotalDays - st.LastMoveDay < 7) return;

        if (data.IsAnnexed) return;

        // Sadece savaşta olan ülke hareket eder (düşman yoksa gitmeyecek yer yok)
        bool anyEnemyAtWar = CountryDatabase.All()
            .Any(c => c != null && !c.IsAnnexed && c.Id != data.Id
                 && WarSystem.IsAtWarWith(data.Id, c.Id));
        if (!anyEnemyAtWar) return;

        // Bu ülkenin kara ordularını bul (hareket etmeyen, bu ülkeye ait veya atanmamış)
        var myArmies = ArmySystem.Armies
            .Where(a => a.Domain == ArmySystem.UnitDomain.Land
                 && !a.IsMoving
                 && (string.IsNullOrEmpty(a.CountryId) || a.CountryId == data.Id))
            .ToList();

        if (myArmies.Count == 0) return;

        // Düşman illerini topla
        var enemyProvinces = new List<string>();
        foreach (var enemy in CountryDatabase.All())
        {
            if (enemy == null || enemy.IsAnnexed || enemy.Id == data.Id) continue;
            if (!WarSystem.IsAtWarWith(data.Id, enemy.Id)) continue;
            enemyProvinces.AddRange(ProvinceOwnershipSystem.GetProvincesByOwner(enemy.Id));
        }

        if (enemyProvinces.Count == 0) return;

        // Rastgele bir düşman ilini hedef al (deterministik)
        DeterministicRNG.Shuffle(enemyProvinces);
        string targetProvince = enemyProvinces[0];

        // İlk kullanılabilir orduyu hareket ettir
        var army = myArmies[0];
        if (string.IsNullOrEmpty(army.CountryId)) army.CountryId = data.Id;

        WorldMap.IssueArmyMoveOrder(army, targetProvince);
        st.LastMoveDay = GM.TotalDays;

        GD.Print($"[AI] {data.Id} ordusu hareket ediyor: {army.Name} → {targetProvince} (gün {GM.TotalDays})");
    }

    // ===== Yardımcılar =====

    // CG ideolojiye göre (EconomySystem yasalarının AI soyutlaması)
    private static float CGPercent(CountryData data) => data.Ideology switch
    {
        "Fascism" => 0.15f,
        "Communism" => 0.20f,
        "Democracy" => 0.30f,
        _ => 0.25f
    };

    private static int FreeCivFactories(CountryData data) =>
        Math.Max(0, data.CivFactories - (int)Math.Round(data.CivFactories * CGPercent(data)));

    private static bool IsMajor(CountryData data) =>
        data.Population >= MajorPopulationThreshold || data.IsDetailed && data.TotalFactories >= 30;

    // ===== M12: Save/Load =====

    // AI state'leri DTO'ya kopyala (Data referansı KAYDEDİLMEZ — id üzerinden yeniden bağlanır)
    public static SaveSystem.AiSave GetSaveData()
    {
        return new SaveSystem.AiSave
        {
            States = _states.Values.Select(st => new SaveSystem.AiStateSave
            {
                CountryId = st.Data.Id,
                BuildType = st.BuildType,
                BuildPoints = st.BuildPoints,
                ResearchProgress = st.ResearchProgress,
                ResearchCompleted = st.ResearchCompleted,
                Divisions = st.Divisions,
                TrainingDaysLeft = st.TrainingDaysLeft,
                DivisionsInTraining = st.DivisionsInTraining,
                FactionId = st.FactionId,
                LastDecisionDay = st.LastDecisionDay,
                LastMoveDay = st.LastMoveDay,
                LastPeaceDay = st.LastPeaceDay,
                LastFormableDay = st.LastFormableDay,
                AiExhausted = st.AiExhausted
            }).ToArray(),
            AiCountryIds = _aiCountryIds.ToArray(),
            Cursor = _cursor
        };
    }

    // Yükleme: Initialize() çağrılmaz — state birebir restore edilir.
    // Data referansı CountryDatabase'den id ile yeniden bağlanır.
    public static void LoadSaveData(SaveSystem.AiSave data)
    {
        if (data == null) return;
        _states.Clear();
        _aiCountryIds.Clear();
        _cursor = data.Cursor;

        if (data.States != null)
        {
            foreach (var s in data.States)
            {
                // GetExact: normalizasyonsuz lookup (PER/IRN takma ad çiftleri için kritik)
                var countryData = CountryDatabase.GetExact(s.CountryId);
                if (countryData == null)
                {
                    GD.PrintErr($"[AI] Yükleme: ülke bulunamadı: {s.CountryId} — state atlandı");
                    continue;
                }
                _states[s.CountryId] = new AIState
                {
                    Data = countryData,
                    BuildType = s.BuildType,
                    BuildPoints = s.BuildPoints,
                    ResearchProgress = s.ResearchProgress,
                    ResearchCompleted = s.ResearchCompleted,
                    Divisions = s.Divisions,
                    TrainingDaysLeft = s.TrainingDaysLeft,
                    DivisionsInTraining = s.DivisionsInTraining,
                    FactionId = s.FactionId ?? "",
                    LastDecisionDay = s.LastDecisionDay,
                    LastMoveDay = s.LastMoveDay,
                    LastPeaceDay = s.LastPeaceDay,
                    AiExhausted = s.AiExhausted
                };
            }
        }

        if (data.AiCountryIds != null)
            foreach (var id in data.AiCountryIds)
                if (_states.ContainsKey(id))
                    _aiCountryIds.Add(id);

        _initialized = _states.Count > 0;
        GD.Print($"CountryAI: state yüklendi — AI ülke={_states.Count}, round-robin={_aiCountryIds.Count}, cursor={_cursor}");
    }
}
