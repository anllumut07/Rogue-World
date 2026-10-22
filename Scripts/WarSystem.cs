using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Savaş sistemi (M7.4-7.6): savaş ilanı, günlük çatışma (ORG/HP hasarı,
/// geri çekilme/yok olma, kayıplar, ekipman kaybı), ofansif/cephe duruşu (M7.5),
/// kapitülasyon → ilhak (toprak değişimi).
/// HOI4 province sistemi yerine ülke-bazlı sadeleştirme: savaş, iki ülke arasında
/// tek "cephede" çözülür; WarProgress (0-100) işgal/VK proxy'sidir.
/// Statik sınıf; GameManager günlük tick'inde çağrılır.
/// </summary>
public static class WarSystem
{
    // Savaş modeli
    public class War
    {
        public string Id { get; set; }
        public string AttackerId { get; set; }      // genelde oyuncu
        public string DefenderId { get; set; }      // düşman ülke
        public string DefenderName { get; set; }
        public int StartDay { get; set; }

        // Düşman durumu (AI ülkesi — CountryData'dan türetilir)
        public int EnemyDivisions { get; set; }
        public float EnemyOrgPool { get; set; }      // düşman kombine ORG havuzu

        // Savaş gidişatı
        public float WarProgress { get; set; }       // 0-100: oyuncunun işgal oranı (VK proxy)
        public bool OffensiveActive { get; set; }   // M7.5: RMB tık → ofansif emri
        public bool FrontlineStance { get; set; }    // M7.5: Shift+RMB → cephe duruşu (savunma)
        public long PlayerCasualties { get; set; }   // manpower kaybı
        public long EnemyCasualties { get; set; }
        public bool IsEnded { get; set; }
        public string EndReason { get; set; } = "";

        // M8: savaş oyuncuyu içeriyor mu? (AI-vs-AI savaşları M11'e kadar sadece diplomatik durum)
        public bool InvolvesPlayer =>
            GameManager.IsAvailable &&
            (AttackerId == GameManager.Instance.PlayerCountryId ||
             DefenderId == GameManager.Instance.PlayerCountryId);

        // ===== M42.1: savaş puanı (WarScoreSystem) verisi =====
        /// <summary>Savaş başındaki düşman tümen sayısı (puan kırılımı için referans).</summary>
        public int StartEnemyDivisions { get; set; }
        /// <summary>Savaş başındaki düşman toplam fabrika sayısı.</summary>
        public int StartEnemyFactories { get; set; }
        /// <summary>Saldıranın kazandığı günlük çatışma sayısı.</summary>
        public int AttackerBattlesWon { get; set; }
        /// <summary>Savunanın kazandığı günlük çatışma sayısı.</summary>
        public int DefenderBattlesWon { get; set; }
        /// <summary>Saldıran kapitülasyonu (savaşı kaybetti).</summary>
        public bool AttackerCapitulated { get; set; }
        /// <summary>Savunan kapitülasyonu (savaşı kaybetti).</summary>
        public bool DefenderCapitulated { get; set; }
    }

    // ===== Sabitler (HOI4 esinli, sadeleştirilmiş) =====
    public const double DeclareWarPPCost = 50;        // savaş ilanı maliyeti
    private const float EnemyDivisionOrg = 60f;       // düşman tümeni başına ORG
    private const float EnemyAttackPerDivision = 10f;
    private const float EnemyDefensePerDivision = 22f;
    private const float CombatIntensity = 0.15f;      // genel hasar ölçeği
    private const float PassiveIntensity = 0.03f;     // ofansifsiz çatışma (çatışma yok denecek kadar az)
    private const float FrontlineIntensity = 0.06f;  // cephe duruşunda az kayıp
    private const float ProgressPerDayBase = 1.2f;    // ofansifte günde temel ilerleme
    private const int MaxRetreatsBeforeDestroyed = 3; // 3 geri çekilme = yok olma

    // ===== Durum =====
    private static bool _initialized;
    private static readonly List<War> _wars = new();

    // M10: War goal PP indirimleri — hedef ülke id → indirim yüzdesi (0-100).
    // Olay/odak efektleri "war goal" verince o hedefe savaş ilanı ucuzlar.
    private static readonly Dictionary<string, int> _warGoalReductions = new();

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    // ===== Başlatma =====

    public static void Initialize(CountryData data)
    {
        _wars.Clear();
        _warGoalReductions.Clear();
        _initialized = data != null;
        GD.Print($"WarSystem: {(data?.Id ?? "—")} için hazır — aktif savaş=0");
    }

    // ===== War goal (M10): hedefe savaş ilanı PP maliyeti indirimi =====

    /// <summary>Olay/odak efekti: hedef ülkeye savaş ilanı maliyetini indirir (yüzde).</summary>
    public static void SetWarGoalCostReduction(string targetId, int reductionPercent)
    {
        targetId = CountryDatabase.NormalizeId(targetId);
        if (string.IsNullOrEmpty(targetId)) return;

        int prev = _warGoalReductions.GetValueOrDefault(targetId, 0);
        _warGoalReductions[targetId] = Math.Clamp(Math.Max(prev, reductionPercent), 0, 100);
        GD.Print($"[War] War goal: {targetId} için savaş ilanı maliyeti -%{_warGoalReductions[targetId]}");
    }

    /// <summary>Hedefe savaş ilanının indirrimli PP maliyeti (war goal varsa ucuzlar).</summary>
    public static int CalculateWarGoalCost(string targetId)
    {
        int reduction = _warGoalReductions.GetValueOrDefault(CountryDatabase.NormalizeId(targetId), 0);
        return (int)Math.Max(0, DeclareWarPPCost * (100 - reduction) / 100.0);
    }

    /// <summary>Hedef için aktif war goal indirimi (yüzde, UI göstergesi için).</summary>
    public static int GetWarGoalReduction(string targetId) =>
        _warGoalReductions.GetValueOrDefault(CountryDatabase.NormalizeId(targetId), 0);

    // ===== Savaş ilanı (CountryInfoPanel DECLARE WAR butonu) =====

    public static bool DeclareWar(string targetId)
    {
        if (!_initialized || Player == null) return false;
        if (targetId == GM.PlayerCountryId) { GD.Print("[War] Kendine savaş ilan edilemez!"); return false; }
        if (IsAtWarWith(targetId)) { GD.Print("[War] Bu ülkeyle zaten savaşta!"); return false; }

        var target = CountryDatabase.Get(targetId);
        if (target == null || target.IsAnnexed)
        {
            GD.Print($"[War] Savaş ilanı başarısız: {targetId} mevcut değil veya zaten ilhak edilmiş");
            return false;
        }

        // === M17A: Casus Belli önkoşul kontrolü ===
        // (Test istisnası yok: --war-test bu metodu kullanır, test kancası
        //  GameManager'da gerekçeyi Grantsız atlamamak için GrantImmediateJustification yapar.)
        var cbCheck = CasusBelliSystem.CheckWarPreconditions(GM.PlayerCountryId, targetId);
        if (!cbCheck.CanDeclare)
        {
            GD.Print($"[War] Savaş ilanı başarısız: Casus Belli önkoşulları eksik");
            foreach (var item in cbCheck.Items.Where(i => !i.Met))
                GD.Print($"[War]   ✗ {item.Label}: {item.Detail}");
            return false;
        }

        // Kullanılan gerekçe türü (geçerli gerekçelerden biri — UI'da gösterilenle aynı olabilir)
        string justificationType = "";
        var validJust = CasusBelliSystem.GetValidJustifications(GM.PlayerCountryId, targetId);
        if (validJust.Count > 0) justificationType = validJust[0].TypeId;

        // PP maliyeti (war goal indirimi uygulanır)
        int cost = CalculateWarGoalCost(targetId);
        if (Player.PoliticalPower < cost)
        {
            GD.Print($"[War] Savaş ilanı başarısız: PP yetersiz ({Player.PoliticalPower:N0}/{cost})");
            return false;
        }

        Player.PoliticalPower -= cost;
        Player.WarSupportPercent = Math.Min(100, Player.WarSupportPercent + 10);
        Player.StabilityPercent = Math.Max(0, Player.StabilityPercent - 5);

        var war = new War
        {
            Id = $"war_{_wars.Count + 1}_{targetId}",
            AttackerId = GM.PlayerCountryId,
            DefenderId = targetId,
            DefenderName = target.Name,
            StartDay = GM.TotalDays,
            EnemyDivisions = Math.Max(1, target.StartingDivisions),
            EnemyOrgPool = Math.Max(1, target.StartingDivisions) * EnemyDivisionOrg,
            WarProgress = 0,
            OffensiveActive = false,
            FrontlineStance = false,
            // M42.1: puan kırılımı için savaş başı düşman durumu
            StartEnemyDivisions = Math.Max(1, target.StartingDivisions),
            StartEnemyFactories = Math.Max(0, target.CivFactories) +
                                  Math.Max(0, target.MilFactories) + Math.Max(0, target.Dockyards)
        };
        _wars.Add(war);

        GD.Print($"[War] SAVAŞ İLANI: {Player.Name} → {target.Name}! " +
                 $"(maliyet: {cost} PP {(cost != DeclareWarPPCost ? $"(taban {DeclareWarPPCost} - indirim)" : "")}, " +
                 $"düşman tümeni: {war.EnemyDivisions}, " +
                 $"WS: +10 → {Player.WarSupportPercent:N0}%, STA: {Player.StabilityPercent:N0}%)");

        // M17A: Casus belli bildirimi — gerekçe tüket, itibar/tension uygula,
        // savunucuya/müttefiklere/garantörlere otomatik gerekçeler ver
        CasusBelliSystem.NotifyWarDeclared(GM.PlayerCountryId, targetId, justificationType);

        if (GameManager.IsAvailable)
            GameManager.Instance.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
        return true;
    }

    // ===== Günlük çatışma (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (!_initialized) return;

        // Tüm bitmemiş savaşları çözümle (oyuncu + AI-AI)
        var activeWars = _wars.Where(w => !w.IsEnded).ToList();
        foreach (var war in activeWars)
        {
            if (war.InvolvesPlayer)
            {
                // Oyuncu savaşları: tam çatışma çözümü
                ResolveCombatDay(war);
            }
            else
            {
                // AI-AI savaşları: basitleştirilmiş çözüm (M11.2)
                ResolveAiVsAiCombat(war);
            }
        }

        // Ayda bir savaş özeti (headless doğrulama)
        if (GM.TotalDays > 0 && GM.TotalDays % 30 == 0 && activeWars.Count > 0)
        {
            foreach (var war in activeWars)
            {
                if (war.InvolvesPlayer)
                {
                    var playerDivs = ArmySystem.DeployedDivisions;
                    GD.Print($"[War] Gün {GM.TotalDays} — {war.DefenderName}: ilerleme %{war.WarProgress:F0}, " +
                             $"düşman tümeni={war.EnemyDivisions}, oyuncu tümeni={playerDivs}, " +
                             $"kayıp: oyuncu={war.PlayerCasualties:N0}, düşman={war.EnemyCasualties:N0}");
                }
                else
                {
                    GD.Print($"[War][AI] Gün {GM.TotalDays} — {war.AttackerId} vs {war.DefenderId}: " +
                             $"ilerleme %{war.WarProgress:F0}, {war.AttackerId} tümeni={CountryAI.GetDivisions(war.AttackerId)}, " +
                             $"{war.DefenderId} tümeni={CountryAI.GetDivisions(war.DefenderId)}");
                }
            }
        }
    }

    // AI-AI savaş basit çözümü: factory/sayısal üstünlük → ilerleme, kazanan ilhak eder
    private static void ResolveAiVsAiCombat(War war)
    {
        int attDivs = CountryAI.GetDivisions(war.AttackerId);
        int defDivs = CountryAI.GetDivisions(war.DefenderId);
        if (attDivs <= 0 && defDivs <= 0) return; // her ikisi de boş

        // Fabrika bazlı güç: sivil+askeri = endüstriyel kapasite
        var attData = CountryDatabase.Get(war.AttackerId);
        var defData = CountryDatabase.Get(war.DefenderId);
        if (attData == null || defData == null) return;

        int attStrength = attDivs + attData.TotalFactories / 3;
        int defStrength = defDivs + defData.TotalFactories / 3;

        // M11.2 front ataması: üstünlüğe göre AI kendi duruşunu seçer
        // (basit: saldıran güçteyse ofansif, zayıfsa cephe duruşu)
        if (attStrength > defStrength * 1.2f)
        {
            if (!war.OffensiveActive)
            {
                war.OffensiveActive = true;
                GD.Print($"[War][AI] {war.AttackerId} OFANSİF emri verdi (güç {attStrength} vs {defStrength})");
            }
        }
        else if (attStrength < defStrength * 0.8f && !war.FrontlineStance)
        {
            war.FrontlineStance = true;
            war.OffensiveActive = false;
            GD.Print($"[War][AI] {war.AttackerId} CEPHE duruşu aldı (savunma, güç {attStrength} vs {defStrength})");
        }

        // Saldırgan ofansifse +%50 avantaj
        float attMult = war.OffensiveActive ? 1.5f : (war.FrontlineStance ? 0.8f : 1.0f);
        float defMult = war.FrontlineStance ? 1.3f : 1.0f;

        float attEff = attStrength * attMult;
        float defEff = defStrength * defMult;

        if (attEff + defEff <= 0) return;

        // Günde ilerleme: üstünlük oranı
        float progressDelta = 0.5f * (attEff - defEff) / (attEff + defEff); // -0.5 ... +0.5
        war.WarProgress = Math.Clamp(war.WarProgress + progressDelta, 0, 100);

        // Tümen kayıpları (günde ~1-2 oranında, üstünlüğe bağlı)
        float lossRatio = Math.Clamp((defEff - attEff) / Math.Max(1, attEff + defEff), -0.05f, 0.05f);
        int attLoss = Math.Max(0, (int)Math.Round(attDivs * lossRatio * 0.01f));
        int defLoss = Math.Max(0, (int)Math.Round(defDivs * -lossRatio * 0.01f));
        // Saldırgan avantajlıysa savunmacı kayıplar daha yüksek
        if (progressDelta > 0.1f)
        {
            defLoss += Math.Max(1, (int)Math.Round(defDivs * 0.005f));
            war.WarProgress += 0.3f; // ek ilerleme
        }
        else if (progressDelta < -0.1f)
        {
            attLoss += Math.Max(1, (int)Math.Round(attDivs * 0.005f));
        }

        if (attLoss > 0)
        {
            CountryAI.ApplyDivisionLoss(war.AttackerId, attLoss);
            war.EnemyCasualties += attLoss * 1000;
            GD.Print($"[War][AI] {war.AttackerId} {attLoss} tümen kaybetti (kalan: {CountryAI.GetDivisions(war.AttackerId)})");
        }
        if (defLoss > 0)
        {
            CountryAI.ApplyDivisionLoss(war.DefenderId, defLoss);
            war.EnemyCasualties += defLoss * 1000;
            GD.Print($"[War][AI] {war.DefenderId} {defLoss} tümen kaybetti (kalan: {CountryAI.GetDivisions(war.DefenderId)})");
        }

        // Kapitülasyon: 0 tümen veya ilerleme 100
        int attDivsNow = CountryAI.GetDivisions(war.AttackerId);
        int defDivsNow = CountryAI.GetDivisions(war.DefenderId);
        if (defDivsNow <= 0 || war.WarProgress >= 100)
        {
            AiCapitulate(war, war.AttackerId, war.DefenderId);
        }
        else if (attDivsNow <= 0 || war.WarProgress <= 0)
        {
            AiCapitulate(war, war.DefenderId, war.AttackerId);
        }
    }

    // AI ilhak: kazanan fabrika/yükümlülükleri alır
    private static void AiCapitulate(War war, string winnerId, string loserId)
    {
        var winner = CountryDatabase.Get(winnerId);
        var loser = CountryDatabase.Get(loserId);
        if (winner == null || loser == null) return;

        // Kazanan fabrika kazanır (HOI4 benzeri: %50 fabrika transferi)
        int civGain = loser.CivFactories / 2;
        int milGain = loser.MilFactories / 2;
        int dockGain = loser.Dockyards / 2;

        winner.CivFactories += civGain;
        winner.MilFactories += milGain;
        winner.Dockyards += dockGain;

        // M41.2: major güç eşiği için toplam fabrika sayısı (kayıt öncesi)
        int loserTotalFactories = loser.CivFactories + loser.MilFactories + loser.Dockyards;
        VictorySystem.RecordCapitulation(winnerId, loserId, loserTotalFactories);

        loser.IsAnnexed = true;
        loser.OwnerId = winnerId;

        // M44.4: ilhak kaydı (fabrika payı burada %50 devredilir)
        ReleasableSystem.RecordSubject(loserId, winnerId, false, civGain, milGain, dockGain);

        war.IsEnded = true;
        war.WarProgress = winnerId == war.AttackerId ? 100 : 0;

        // M42.1: kaybeden tarafı işaretle (savaş puanı)
        if (string.Equals(winnerId, war.AttackerId, StringComparison.OrdinalIgnoreCase))
            war.DefenderCapitulated = true;
        else
            war.AttackerCapitulated = true;

        GD.Print($"[War][AI] KAPİTÜLASYON: {loser.Name} ({loserId}) → {winner.Name} ({winnerId}) " +
                 $"ilhaç edildi. Kazanan: +{civGain} sivil, +{milGain} askeri, +{dockGain} tersane");

        // M16.4: Dünya haber bildirimi (kapitülasyon) + auto-pause
        TimeTempoSystem.QueueImportantNotification("capitulation",
            "CAPITULATION",
            $"{loser.Name} has capitulated to {winner.Name} and been annexed.",
            () => { /* could open map focus on region */ });

        // M17A: Savaş bitti — ateşkes başlat
        CasusBelliSystem.NotifyWarEnded(war.AttackerId, war.DefenderId);

        // Harita sahipliği değişimi sinyali (WorldMap yeniden renklendirir)
        if (GameManager.IsAvailable)
            GameManager.Instance.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);
    }

    private static void ResolveCombatDay(War war)
    {
        var playerDivisions = ArmySystem.Divisions.Where(d => d.IsDeployed).ToList();

        // --- Oyuncu gücü (ORG ağırlıklı) ---
        float attP = 0, defP = 0;
        foreach (var div in playerDivisions)
        {
            var tpl = ArmySystem.GetTemplate(div.TemplateId);
            if (tpl == null) continue;
            float orgFrac = div.OrgPercent;
            // M39.1: İkmal cezası — ikmalsız ordular daha zayıf saldırır/savunulur.
            float supMult = SupplySystem.GetCombatMultiplier(div.ArmyId);
            float morMult = MoraleSystem.GetCombatMoraleMultiplier(div.ArmyId);
            float airMult = AirSuperioritySystem.GetAirSuperiorityMultiplier(div.ArmyId);
            attP += tpl.Attack * orgFrac * supMult * morMult * airMult;
            defP += tpl.Defense * orgFrac * supMult * morMult * airMult;
        }
        // General becerisi: +%5/skill
        float skill = GetAverageGeneralSkill();
        attP *= 1f + skill * 0.05f;
        defP *= 1f + skill * 0.05f;

        // M31.1: Teknoloji modifikatörleri — yeni silahlar savaş istatistiklerini değiştirir.
        // Araştırılan saldırı/savunma teknolojileri direkt olarak gun gücünü etkiler.
        attP *= 1f + TechnologySystem.GetModifier("land_attack");
        defP *= 1f + TechnologySystem.GetModifier("land_defense");

        // M37.3: Uluslararası örgüt askeri verimlilik çarpanı (ör. NATO üyesi %10).
        float milMult = OrganizationSystem.GetMilitaryEfficiencyMultiplier(GM.PlayerCountryId);
        attP *= milMult;
        defP *= milMult;

        // --- Düşman gücü ---
        float enemyOrgFrac = war.EnemyDivisions > 0 ? Mathf.Clamp(war.EnemyOrgPool / (war.EnemyDivisions * EnemyDivisionOrg), 0f, 1f) : 0f;
        float attE = war.EnemyDivisions * EnemyAttackPerDivision * enemyOrgFrac;
        float defE = war.EnemyDivisions * EnemyDefensePerDivision;

        // --- Şiddet: duruşa göre ---
        float intensity;
        if (war.OffensiveActive) intensity = CombatIntensity;
        else if (war.FrontlineStance) intensity = FrontlineIntensity;
        else intensity = PassiveIntensity;

        // Cephe duruşu: savunma bonusu
        if (war.FrontlineStance) defP *= 1.3f;

        // --- Hasar hesabı (HOI4: saldırı, savunma tarafından hafifletilir) ---
        float rollA = 0.9f + (float)DeterministicRNG.NextDouble() * 0.2f; // ±%10 zar
        float rollB = 0.9f + (float)DeterministicRNG.NextDouble() * 0.2f;

        // Oyuncunun düşmana verdiği ORG hasarı
        float dealtToEnemy = attP > 0 && defE + attP > 0
            ? attP * (attP / (attP + defE)) * intensity * rollA : 0;

        // Düşmanın oyuncuya verdiği ORG hasarı
        float dealtToPlayer = attE > 0 && defP + attE > 0
            ? attE * (attE / (attE + defP)) * intensity * rollB : 0;

        // --- Düşman ORG havuzuna uygula → tümen kaybı ---
        if (dealtToEnemy > 0)
        {
            war.EnemyOrgPool -= dealtToEnemy;
            while (war.EnemyOrgPool <= 0 && war.EnemyDivisions > 0)
            {
                war.EnemyDivisions--;
                war.EnemyOrgPool += EnemyDivisionOrg;
                war.EnemyCasualties += 1000;
                war.WarProgress = Math.Min(100, war.WarProgress + 4);
                GD.Print($"[War] {war.DefenderName} bir tümen kaybetti! (kalan: {war.EnemyDivisions}, " +
                         $"düşman kaybı: {war.EnemyCasualties:N0})");
            }
        }

        // --- Oyuncu tümenlerine dağıt → geri çekilme / yok olma ---
        if (dealtToPlayer > 0 && playerDivisions.Count > 0)
            DistributeDamageToPlayer(war, playerDivisions, dealtToPlayer);

        // --- Savaş ilerlemesi (ofansif/cephe) ---
        if (war.OffensiveActive && attP + defE > 0)
            war.WarProgress = Math.Min(100, war.WarProgress + ProgressPerDayBase * attP / (attP + defE));
        else if (war.FrontlineStance)
            war.WarProgress = Math.Min(100, war.WarProgress + 0.2f);

        // --- M42.1: Günlük çatışma kazanımı (savaş puanı) ---
        // %20 üstünlük olmadan "berabere" sayılır — gürültüyü puanlamaya yazmayalım.
        if (dealtToEnemy > dealtToPlayer * 1.2f) war.AttackerBattlesWon++;
        else if (dealtToPlayer > dealtToEnemy * 1.2f) war.DefenderBattlesWon++;

        // --- Kapitülasyon kontrolleri ---
        if (war.EnemyDivisions <= 0)
        {
            EnemyCapitulates(war, "Düşmanın tüm tümenleri yok edildi");
            return;
        }
        if (war.WarProgress >= 100)
        {
            EnemyCapitulates(war, "İşgal tamamlandı (zafer noktaları kontrol altında)");
            return;
        }
        if (ArmySystem.DeployedDivisions == 0)
        {
            PlayerCapitulates(war, "Oyuncunun tüm tümenleri yok edildi");
            return;
        }
    }

    // Hasarı oyuncu tümenlerine orantılı dağıtır; ORG sıfırlanan tümen geri çekilir,
    // MaxRetreatsBeforeDestroyed kez geri çekilen tümen YOK OLUR (kayıp + ekipman kaybı).
    private static void DistributeDamageToPlayer(War war, List<ArmySystem.Division> divisions, float damage)
    {
        float totalOrg = divisions.Sum(d => (float)d.CurrentOrg);
        if (totalOrg <= 0)
        {
            // Tümü bitmişse: hepsi bir adım geri çekilsin
            foreach (var d in divisions) RetreatOrDestroy(war, d);
            return;
        }

        foreach (var div in divisions)
        {
            float share = div.CurrentOrg / totalOrg;
            div.CurrentOrg = Math.Max(0, div.CurrentOrg - (int)(damage * share));
            if (div.CurrentOrg <= 0)
                RetreatOrDestroy(war, div);
        }
    }

    private static void RetreatOrDestroy(War war, ArmySystem.Division div)
    {
        div.Retreats++;
        if (div.Retreats >= MaxRetreatsBeforeDestroyed)
        {
            // Tümen yok oldu: manpower + ekipman kaybı
            var tpl = ArmySystem.GetTemplate(div.TemplateId);
            long manpower = tpl?.Manpower ?? 1000;
            war.PlayerCasualties += manpower;
            if (Player != null) Player.ManpowerPool = Math.Max(0, Player.ManpowerPool - manpower / 2);
            if (tpl != null)
                foreach (var kv in tpl.Equipment)
                    EquipmentSystem.ConsumeStock(kv.Key, Math.Max(1, kv.Value / 2)); // %50 ekipman kaybı

            GD.Print($"[War] Oyuncu tümeni YOK EDİLDİ: {div.DisplayName} " +
                     $"(kayıp: {manpower:N0} adam, savaş kaybı toplam: {war.PlayerCasualties:N0})");
            ArmySystem.DestroyDivision(div.Id);
        }
        else
        {
            div.CurrentOrg = (int)(div.MaxOrg * 0.3); // toparlanıp hattan çekildi
            GD.Print($"[War] Oyuncu tümeni geri çekildi: {div.DisplayName} " +
                     $"(geri çekilme {div.Retreats}/{MaxRetreatsBeforeDestroyed}, ORG %{30})");
        }
    }

    // ===== Kapitülasyon (M7.6) =====

    // Düşman kapitüle → ilhak: IsAnnexed + OwnerId + fabrika kaynak transferi + harita yenilenir
    private static void EnemyCapitulates(War war, string reason)
    {
        war.IsEnded = true;
        war.EndReason = reason;
        war.DefenderCapitulated = true; // M42.1: savaş puanı — savunan kaybetti

        var loser = CountryDatabase.Get(war.DefenderId);
        var winner = CountryDatabase.Get(war.AttackerId) ?? Player;

        if (loser != null && winner != null)
        {
            loser.IsAnnexed = true;
            loser.OwnerId = winner.Id;

            // M44.4: ilhak kaydı — iller devredilmeden önce fabrika sayılarıyla
            ReleasableSystem.RecordSubject(loser.Id, winner.Id, false,
                loser.CivFactories, loser.MilFactories, loser.Dockyards);

            // M41.2: major güç eşiği için fabrika sayısı sıfırlanmadan ÖNCE alınır
            int loserTotalFactories = loser.TotalFactories;

            // Savaş ganimeti: fabrikalar kazananına geçer (basit ilhak modeli)
            winner.CivFactories += loser.CivFactories;
            winner.MilFactories += loser.MilFactories;
            winner.Dockyards += loser.Dockyards;
            loser.CivFactories = loser.MilFactories = loser.Dockyards = 0;

            VictorySystem.RecordCapitulation(winner.Id, loser.Id, loserTotalFactories);

            Player.StabilityPercent = Math.Min(100, Player.StabilityPercent + 5);
            Player.WarSupportPercent = Math.Max(0, Player.WarSupportPercent - 5);
        }

        GD.Print($"[War] KAPİTÜLASYON: {war.DefenderName} teslim oldu ({reason})! " +
                 $"Topraklar {winner?.Name} tarafından ilhak edildi — harita güncelleniyor");

        // M17A: Savaş bitti — ateşkes başlat
        CasusBelliSystem.NotifyWarEnded(war.AttackerId, war.DefenderId);

        if (GameManager.IsAvailable)
        {
            GameManager.Instance.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);
            GameManager.Instance.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
        }
    }

    // Oyuncu kapitüle → oyun kaybedildi (basit model: log + bayrak)
    private static void PlayerCapitulates(War war, string reason)
    {
        war.IsEnded = true;
        war.EndReason = reason;
        // M42.1: oyuncu saldıran taraf olarak kapitüle oluyor (savaş puanı — kaybeden taraf)
        war.AttackerCapitulated = true;

        var winner = CountryDatabase.Get(war.DefenderId);
        if (Player != null)
        {
            Player.IsAnnexed = true;
            Player.OwnerId = war.DefenderId;

            // M44.4: oyuncunun ilhak kaydı (yeniden bağımsız olunabilir)
            ReleasableSystem.RecordSubject(Player.Id, war.DefenderId, false,
                Player.CivFactories, Player.MilFactories, Player.Dockyards);
        }

        GD.Print($"[War] *** OYUNCU KAPİTÜLE OLDU: {Player?.Name} teslim oldu ({reason}) — " +
                 $"OYUN KAYBEDİLDİ, {winner?.Name} kazandı ***");

        // M41.4: Oyun sonu paneli — yenilgi. CONTINUE ile oyuncu izlemeye devam edebilir.
        if (GameManager.IsAvailable)
            GameOverPanel.OpenUnder(GameManager.Instance, false, "Capitulation",
                $"Your nation surrendered: {reason}", Player?.Name ?? "");

        // M17A: Savaş bitti — ateşkes başlat
        CasusBelliSystem.NotifyWarEnded(war.AttackerId, war.DefenderId);

        if (GameManager.IsAvailable)
            GameManager.Instance.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);
    }

    // ===== Emirler (M7.5: RMB tık / Shift+RMB — WorldMap'ten çağrılır) =====

    // Ofansif emrini aç/kapat (sağ tık)
    public static bool SetOffensive(string enemyId, bool active)
    {
        var war = _wars.Find(w => !w.IsEnded && w.DefenderId == enemyId);
        if (war == null) return false;
        if (war.OffensiveActive == active) return false;

        war.OffensiveActive = active;
        if (active) war.FrontlineStance = false; // ofansif cepheyi iptal eder

        GD.Print(active
            ? $"[War] OFANSİF EMRİ: {war.DefenderName} üzerine taarruz başladı (hasar tam, ilerleme hızlı)"
            : $"[War] Ofansif durduruldu: {war.DefenderName} cephesi pasif çatışmaya döndü");
        return true;
    }

    // Cephe duruşunu aç/kapat (Shift+sağ tık): savunma bonusu, düşük kayıp, yavaş ilerleme
    public static bool ToggleFrontline(string enemyId)
    {
        var war = _wars.Find(w => !w.IsEnded && w.DefenderId == enemyId);
        if (war == null) return false;

        war.FrontlineStance = !war.FrontlineStance;
        if (war.FrontlineStance) war.OffensiveActive = false; // cephe ofansifi iptal eder

        GD.Print(war.FrontlineStance
            ? $"[War] CEPHE DURUŞU: {war.DefenderName} karşısında hat tutuluyor (savunma +%30, ilerleme yavaş)"
            : $"[War] Cephe duruşu kaldırıldı: {war.DefenderName} cephesi pasif çatışmaya döndü");
        return true;
    }

    // ===== Getters (UI için) =====

    public static bool IsInitialized => _initialized;
    public static IReadOnlyList<War> Wars => _wars;
    public static IReadOnlyList<War> ActiveWars => _wars.Where(w => !w.IsEnded).ToList();

    // 1-parametreli: OYUNCU bu ülkeyle savaşta mı? (AI-vs-AI savaşları hariç)
    public static bool IsAtWarWith(string countryId) =>
        _wars.Any(w => !w.IsEnded && w.InvolvesPlayer &&
            (w.DefenderId == countryId || w.AttackerId == countryId));

    // OYUNCUNUN bu ülkeyle savaşı (AI-vs-AI değil)
    public static War GetWarWith(string countryId) =>
        _wars.Find(w => !w.IsEnded && w.InvolvesPlayer &&
            (w.DefenderId == countryId || w.AttackerId == countryId));

    private static float GetAverageGeneralSkill()
    {
        var armies = ArmySystem.Armies;
        if (armies.Count == 0) return 0;
        return (float)armies.Average(a => a.GeneralSkill);
    }

    /// <summary>Belirli bir ülkenin savaştığı düşmanları döndürür (AI çağrıları için).</summary>
    public static List<string> GetEnemiesAtWar(string countryId)
    {
        return _wars.Where(w => !w.IsEnded &&
                (w.AttackerId == countryId || w.DefenderId == countryId))
            .Select(w => w.AttackerId == countryId ? w.DefenderId : w.AttackerId)
            .Distinct()
            .ToList();
    }

    // ===== M12: Save/Load =====

    public static SaveSystem.WarSave GetSaveData()
    {
        return new SaveSystem.WarSave
        {
            Wars = _wars.Select(w => new SaveSystem.WarEntry
            {
                Id = w.Id,
                AttackerId = w.AttackerId,
                DefenderId = w.DefenderId,
                DefenderName = w.DefenderName,
                StartDay = w.StartDay,
                EnemyDivisions = w.EnemyDivisions,
                EnemyOrgPool = w.EnemyOrgPool,
                WarProgress = w.WarProgress,
                OffensiveActive = w.OffensiveActive,
                FrontlineStance = w.FrontlineStance,
                PlayerCasualties = w.PlayerCasualties,
                    StartEnemyDivisions = w.StartEnemyDivisions,
                    StartEnemyFactories = w.StartEnemyFactories,
                    AttackerBattlesWon = w.AttackerBattlesWon,
                    DefenderBattlesWon = w.DefenderBattlesWon,
                    AttackerCapitulated = w.AttackerCapitulated,
                    DefenderCapitulated = w.DefenderCapitulated,
                EnemyCasualties = w.EnemyCasualties,
                IsEnded = w.IsEnded,
                EndReason = w.EndReason
            }).ToArray(),
            WarGoalReductions = new Dictionary<string, int>(_warGoalReductions)
        };
    }

    public static void LoadSaveData(SaveSystem.WarSave data)
    {
        if (data == null) return;
        _wars.Clear();
        if (data.Wars != null)
            foreach (var w in data.Wars)
                _wars.Add(new War
                {
                    Id = w.Id,
                    AttackerId = w.AttackerId,
                    DefenderId = w.DefenderId,
                    DefenderName = w.DefenderName,
                    StartDay = w.StartDay,
                    EnemyDivisions = w.EnemyDivisions,
                    EnemyOrgPool = w.EnemyOrgPool,
                    WarProgress = w.WarProgress,
                    OffensiveActive = w.OffensiveActive,
                    FrontlineStance = w.FrontlineStance,
                    PlayerCasualties = w.PlayerCasualties,
                    StartEnemyDivisions = w.StartEnemyDivisions,
                    StartEnemyFactories = w.StartEnemyFactories,
                    AttackerBattlesWon = w.AttackerBattlesWon,
                    DefenderBattlesWon = w.DefenderBattlesWon,
                    AttackerCapitulated = w.AttackerCapitulated,
                    DefenderCapitulated = w.DefenderCapitulated,
                    EnemyCasualties = w.EnemyCasualties,
                    IsEnded = w.IsEnded,
                    EndReason = w.EndReason ?? ""
                });
        _warGoalReductions.Clear();
        if (data.WarGoalReductions != null)
            foreach (var kv in data.WarGoalReductions) _warGoalReductions[kv.Key] = kv.Value;
        _initialized = true;
        GD.Print($"WarSystem: state yüklendi — Savaş={_wars.Count}, WarGoal={_warGoalReductions.Count}");
    }

    // ===== AI Savaşları için M8 overload'ları (basit takip, M11'de genişletilecek) =====

    /// <summary>İki ülkenin savaşta olup olmadığını kontrol eder (AI çiftleri dahil)</summary>
    public static bool IsAtWarWith(string attackerId, string defenderId)
    {
        return _wars.Any(w => !w.IsEnded &&
            ((w.AttackerId == attackerId && w.DefenderId == defenderId) ||
             (w.AttackerId == defenderId && w.DefenderId == attackerId)));
    }

    /// <summary>AI↔AI savaş ilanı — M17A: casus belli önkoşulu da AI için geçerli.</summary>
    public static bool DeclareWar(string attackerId, string defenderId)
    {
        if (string.IsNullOrEmpty(attackerId) || string.IsNullOrEmpty(defenderId)) return false;
        if (attackerId == defenderId) return false;
        if (IsAtWarWith(attackerId, defenderId)) return false;

        var attacker = CountryDatabase.Get(attackerId);
        var defender = CountryDatabase.Get(defenderId);
        if (attacker == null || defender == null || attacker.IsAnnexed || defender.IsAnnexed)
            return false;

        // === M17A: AI da casus belli'ye tabi — gerekçe yoksa savaş açamaz ===
        var cbCheck = CasusBelliSystem.CheckWarPreconditions(attackerId, defenderId);
        if (!cbCheck.CanDeclare)
        {
            // AI'lar sessizce geçer (spam log yok — CountryAI zaten gerekçe üretir)
            return false;
        }
        string justificationType = "";
        var validJust = CasusBelliSystem.GetValidJustifications(attackerId, defenderId);
        if (validJust.Count > 0) justificationType = validJust[0].TypeId;

        var war = new War
        {
            Id = $"war_ai_{attackerId}_{defenderId}_{_wars.Count + 1}",
            AttackerId = attackerId,
            DefenderId = defenderId,
            DefenderName = defender.Name,
            StartDay = GM?.TotalDays ?? 0,
            EnemyDivisions = Math.Max(1, defender.StartingDivisions),
            EnemyOrgPool = Math.Max(1, defender.StartingDivisions) * EnemyDivisionOrg,
            WarProgress = 0,
            OffensiveActive = false,
            FrontlineStance = false
        };
        _wars.Add(war);

        GD.Print($"[War] AI SAVAŞ İLANI: {attacker.Name} → {defender.Name} " +
                 $"(gerekçe: {CasusBelliSystem.GetType(justificationType)?.Name ?? "—"})");

        // M17A: Casus belli bildirimi (gerekçe tüket, otomatik savunma/garanti gerekçeleri)
        CasusBelliSystem.NotifyWarDeclared(attackerId, defenderId, justificationType);

        return true;
    }

    /// <summary>
    /// Faksiyon çağrısı (Call to Arms) ile savaşa katılma — YENİ savaş ilanı değildir:
    /// liderin savaşına müttefik bağlılığıyla katılım. Casus belli kontrolü ATLANIR.
    /// DiplomacySystem (oyuncu faksiyon çağrısı) ve CountryAI (AI çağrı yanıtı) kullanır.
    /// </summary>
    public static bool JoinWarByFactionCall(string attackerId, string defenderId)
    {
        if (string.IsNullOrEmpty(attackerId) || string.IsNullOrEmpty(defenderId)) return false;
        if (attackerId == defenderId) return false;
        if (IsAtWarWith(attackerId, defenderId)) return false;

        var attacker = CountryDatabase.Get(attackerId);
        var defender = CountryDatabase.Get(defenderId);
        if (attacker == null || defender == null || attacker.IsAnnexed || defender.IsAnnexed)
            return false;

        var war = new War
        {
            Id = $"war_call_{attackerId}_{defenderId}_{_wars.Count + 1}",
            AttackerId = attackerId,
            DefenderId = defenderId,
            DefenderName = defender.Name,
            StartDay = GM?.TotalDays ?? 0,
            EnemyDivisions = Math.Max(1, defender.StartingDivisions),
            EnemyOrgPool = Math.Max(1, defender.StartingDivisions) * EnemyDivisionOrg,
            WarProgress = 0,
            OffensiveActive = false,
            FrontlineStance = false
        };
        _wars.Add(war);

        GD.Print($"[War] FAKSİYON ÇAĞRISI: {attacker.Name} savaşa katıldı → {defender.Name} " +
                 $"(casus belli gerekmez — müttefik bağlılığı, NotifyWarDeclared yapılmaz)");
        return true;
    }

    /// <summary>
    /// Garanti tetiklemesi: AI ülkesi OYUNCUYA savaş açar.
    /// Oyuncu-merkezli modelde düşman olarak eklenir (DefenderId=AI) — çatışma normal çözülür.
    /// Oyuncu kazanırsa AI ilhak edilir (M8 sadeleştirmesi: barış konferansı yok).
    /// M17A: Garantör zaten NotifyWarDeclared ile guarantee_protection gerekçesi almıştır.
    /// </summary>
    public static bool DeclareWarOnPlayer(string attackerId)
    {
        if (!_initialized || Player == null) return false;
        if (attackerId == GM.PlayerCountryId) return false;
        if (IsAtWarWith(attackerId)) return false; // oyuncu zaten bu ülkeyle savaşta

        var attacker = CountryDatabase.Get(attackerId);
        if (attacker == null || attacker.IsAnnexed)
        {
            GD.Print($"[War] Savaş ilanı başarısız: {attackerId} mevcut değil veya zaten ilhak edilmiş");
            return false;
        }

        // M17A: Garantörün gerekçesi var mı? (NotifyWarDeclared içinde guarantee_protection verilmiş olmalı)
        var cbCheck = CasusBelliSystem.CheckWarPreconditions(attackerId, GM.PlayerCountryId);
        if (!cbCheck.CanDeclare)
        {
            GD.Print($"[War] Garanti savaşı REDDEDİLDİ: {attackerId} → {Player.Name} — gerekçe yok");
            foreach (var item in cbCheck.Items.Where(i => !i.Met))
                GD.Print($"[War]   ✗ {item.Label}: {item.Detail}");
            return false;
        }
        string justificationType = "";
        var validJust = CasusBelliSystem.GetValidJustifications(attackerId, GM.PlayerCountryId);
        if (validJust.Count > 0) justificationType = validJust[0].TypeId;

        var war = new War
        {
            Id = $"war_{_wars.Count + 1}_{attackerId}",
            AttackerId = GM.PlayerCountryId, // oyuncu-merkezli model: çözümleme hep oyuncu perspektifinden
            DefenderId = attackerId,         // düşman = savaşı başlatan AI (garantör)
            DefenderName = attacker.Name,
            StartDay = GM.TotalDays,
            EnemyDivisions = Math.Max(1, attacker.StartingDivisions),
            EnemyOrgPool = Math.Max(1, attacker.StartingDivisions) * EnemyDivisionOrg,
            WarProgress = 0,
            OffensiveActive = false,
            FrontlineStance = false
        };
        _wars.Add(war);

        GD.Print($"[War] SAVAŞ İLANI (garanti): {attacker.Name} → {Player.Name}! " +
                 $"(gerekçe: {CasusBelliSystem.GetType(justificationType)?.Name ?? "—"}, düşman tümeni: {war.EnemyDivisions})");

        CasusBelliSystem.NotifyWarDeclared(attackerId, GM.PlayerCountryId, justificationType);

        // M16.4: Oyuncuya savaş ilanı bildirimi + auto-pause
        TimeTempoSystem.QueueImportantNotification("war_declaration",
            "WAR DECLARED",
            $"{attacker.Name} has declared war on you!",
            () => { /* could open DiplomacyPanel or WarPanel */ });

        if (GameManager.IsAvailable)
            GameManager.Instance.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
        return true;
    }
}