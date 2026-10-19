using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// M12 Doğrulama Testi: Kaydet/Yükle determinizm kontrolü.
/// --save-load-test arg'ıyla çalışır; normal oyunda ASLA çağrılmaz.
/// Akış: 100 gün → kaydet → 50 gün → özet A → yükle → 50 gün → özet B → A==B ?
/// </summary>
public static class SaveLoadTest
{
    private const ulong TestSeed = 0x20240101DEADC0DEUL; // Sabit test tohumu
    private const int DaysPhase1 = 100; // Kaydetmeden önce
    private const int DaysPhase2 = 50;  // Kaydettikten / yükledikten sonra

    public static void Run()
    {
        GD.Print("=========================================");
        GD.Print("  M12 SAVE/LOAD DOĞRULAMA TESTİ BAŞLADI");
        GD.Print("=========================================");

        // Önceki test dosyası varsa temizle
        SaveSystem.DeleteTestFile();

        // 1) Deterministik RNG
        DeterministicRNG.InitializeForNewGame(TestSeed);
        GD.Print($"[Test] RNG tohum sabitlendi: {TestSeed:X}");

        // 2) Oyuncu ülkesini seç (senaryoya göre: 1936=GER, modern=DEU — M17.4 iki senaryo testi)
        var gm = GameManager.Instance;
        string testCountry = ScenarioSystem.ActiveId switch
        {
            "modern_world" => "DEU",
            _ => "GER"
        };
        gm.SetPlayerCountry(testCountry);
        GD.Print($"[Test] Oyuncu ülke: {gm.PlayerCountryId} ({gm.PlayerCountryData?.Name ?? "?"}) — senaryo: {ScenarioSystem.ActiveId}");

        // 3) Test modları: event auto-choose (popup bloklamasın), focus auto-pick (zengin state)
        EventSystem.EnableAutoChooseTest();
        FocusSystem.EnableAutoPickTest();
        GD.Print("[Test] Event auto-choose + Focus auto-pick etkin");

        // 4) 100 gün ilerlet
        GD.Print($"[Test] Faz 1: {DaysPhase1} gün ilerletiliyor...");
        gm.AdvanceDaysForTest(DaysPhase1);
        GD.Print($"[Test] Faz 1 tamam — Tarih={gm.CurrentDate:yyyy-MM-dd}, Gün={gm.TotalDays}");

        // 5) Kaydet
        GD.Print("[Test] Kayıt yazılıyor (test_save.json)...");
        SaveSystem.SaveTestFile();

        // 6) 50 gün daha ilerlet (oynanış A)
        GD.Print($"[Test] Faz 2A: {DaysPhase2} gün ilerletiliyor...");
        gm.AdvanceDaysForTest(DaysPhase2);
        string summaryA = BuildSummaryCore(); // label'sız çekirdek özet
        GD.Print($"[Test] ÖZET A:\n=== ÖZET [A (kaydet sonrası)] ===\n{summaryA}");

        // 7) Kaydı yükle
        GD.Print("[Test] Kayıt yükleniyor...");
        bool loaded = SaveSystem.LoadTestFile();
        if (!loaded)
        {
            GD.PrintErr("[Test] YÜKLEME BAŞARISIZ — test iptal");
            return;
        }
        GD.Print($"[Test] Yükleme başarılı — Tarih={gm.CurrentDate:yyyy-MM-dd}, Gün={gm.TotalDays}");

        // 8) AYNI 50 günü tekrar ilerlet (oynanış B)
        GD.Print($"[Test] Faz 2B: {DaysPhase2} gün tekrar ilerletiliyor...");
        gm.AdvanceDaysForTest(DaysPhase2);
        string summaryB = BuildSummaryCore(); // label'sız çekirdek özet
        GD.Print($"[Test] ÖZET B:\n=== ÖZET [B (yükle sonrası)] ===\n{summaryB}");

        // 9) Karşılaştır (label'sız çekirdek)
        bool pass = summaryA == summaryB;
        GD.Print("=========================================");
        if (pass)
        {
            GD.Print("  SONUÇ: PASS ✓  (A == B)");
            GD.Print("=========================================");
            SaveSystem.DeleteTestFile();
        }
        else
        {
            GD.PrintErr("  SONUÇ: FAIL ✗  (A != B)");
            GD.Print("=========================================");
            // Farkı bul
            FindDiff(summaryA, summaryB);
            // test_save.json'i bırak (inceleme için)
        }
        GameManager.Instance.GetTree().Quit(pass ? 0 : 1);
    }

    private static string BuildSummaryCore()
    {
        var sb = new StringBuilder();
        var gm = GameManager.Instance;
        string playerId = gm.PlayerCountryId;

        sb.AppendLine($"Tarih: {gm.CurrentDate:d MMM yyyy} | Gün: {gm.TotalDays} | RNG: {DeterministicRNG.GetState():X} | Tension: {DiplomacySystem.WorldTension:F1} | Hız: {gm.Speed}");

        // --- Ülkeler ---
        sb.AppendLine("Ülkeler (id | civ mil dock | PP | Tümen):");
        foreach (var c in CountryDatabase.All())
        {
            if (c == null) continue;
            string id = c.Id;
            int civ = c.CivFactories;
            int mil = c.MilFactories;
            int dock = c.Dockyards;
            double pp = c.PoliticalPower;
            int divs = (id == playerId)
                ? ArmySystem.Divisions.Count
                : CountryAI.GetDivisions(id);

            sb.AppendLine($"  {id} | {civ,2} {mil,2} {dock,2} | {pp,7:F1} | {divs}");
        }

        // --- Savaşlar ---
        var wars = WarSystem.ActiveWars.Where(w => !w.IsEnded).ToList();
        sb.AppendLine($"Savaşlar ({wars.Count}):");
        foreach (var w in wars)
        {
            sb.AppendLine($"  {w.Id}: {w.AttackerId}->{w.DefenderId} ({w.DefenderName}) prog={w.WarProgress:F1}% kayıp P:{w.PlayerCasualties}/E:{w.EnemyCasualties} off={w.OffensiveActive} front={w.FrontlineStance}");
        }
        if (wars.Count == 0) sb.AppendLine("  (yok)");

        // --- Odaklar ---
        sb.AppendLine("Odaklar (ülke: tamamlanmış, aktif@gün):");
        foreach (var c in CountryDatabase.All())
        {
            if (c == null) continue;
            var fst = FocusSystem.GetOrCreateState(c.Id);
            if (fst == null) continue;
            string activeInfo = fst.Active != null ? $"{fst.Active.Id}@{fst.ActiveDays}" : "—";
            sb.AppendLine($"  {c.Id}: tam={fst.Completed.Count}, aktif={activeInfo}, aiEx={fst.AiExhausted}");
        }

        // --- Ekonomi ---
        var eq = EconomySystem.CurrentLaw;
        int queueCount = EconomySystem.ConstructionQueue.Count;
        int convoy = EconomySystem.ConvoyCount;
        sb.AppendLine($"Ekonomi: yasa={(int)eq}, kuyruk={queueCount}, konvoy={convoy}");
        if (queueCount > 0)
        {
            foreach (var q in EconomySystem.ConstructionQueue)
                sb.AppendLine($"  {q.DisplayName}: {q.PointsDone}/{q.PointsNeeded} ({q.ProgressPercent:F0}%) fab={q.AssignedFactories}");
        }

        // --- Teknoloji ---
        int techRes = TechnologySystem.ResearchedIds.Count;
        int techActive = TechnologySystem.ActiveResearchList.Count;
        var playerC = gm.PlayerCountryData;
        sb.AppendLine($"Teknoloji: tam={techRes}, aktif={techActive}, slot={playerC?.ResearchSlots ?? 0}, techSeviye={playerC?.TechLevel ?? 1}");
        if (techActive > 0)
        {
            foreach (var a in TechnologySystem.ActiveResearchList)
                sb.AppendLine($"  {a.Tech.Id}: {a.ProgressDays:F1}/{a.Tech.Cost} ({a.ProgressPercent:F0}%)");
        }

        // --- Ekipman ---
        int equipLines = EquipmentSystem.Lines.Count;
        long totalStock = EquipmentSystem.Stocks.Values.Sum();
        sb.AppendLine($"Ekipman: hat={equipLines}, stok={totalStock}");
        if (equipLines > 0)
        {
            foreach (var l in EquipmentSystem.Lines)
                sb.AppendLine($"  {EquipmentSystem.GetDisplayName(l.EquipmentId, l.VariantId)}: {l.AssignedFactories}f %{l.Efficiency*100:F0} prod={l.TotalProduced:F0}");
        }

        // --- Ordu ---
        int playerDivs = ArmySystem.Divisions.Count;
        int playerTpls = ArmySystem.Templates.Count;
        int playerArmys = ArmySystem.Armies.Count;
        int training = ArmySystem.TrainingQueue.Count;
        sb.AppendLine($"Ordu (oyuncu): tümen={playerDivs}, şablon={playerTpls}, ordu={playerArmys}, eğitim={training}");

        // --- Diplomasi ---
        int factions = DiplomacySystem.Factions.Count;
        int relations = DiplomacySystem.Relations.Count;
        int guarantees = DiplomacySystem.Guarantees.Count;
        int agreements = DiplomacySystem.Agreements.Count;
        int pressEvents = DiplomacySystem.PressEvents.Count;
        int sanctions = DiplomacySystem.Sanctions.Count;
        sb.AppendLine($"Diplomasi: faksiyon={factions}, ilişki={relations}, garanti={guarantees}, anlaşma={agreements}, basın={pressEvents}, yaptırım={sanctions}");

        // --- M28.1: Sosyal Medya / Gündem ---
        int socialTweets = SocialMediaSystem.TweetCount;
        sb.AppendLine($"SosyalMedya: tweet={socialTweets}");

        // --- M28.2: Kurgusal Sosyal Medya Profilleri ---
        int socialProfiles = SocialProfileSystem.ProfileCount;
        sb.AppendLine($"SosyalProfiller: profile={socialProfiles}");

        // --- M17A: Casus Belli ---
        int activeJustifications = CasusBelliSystem.Justifications.Count(j => !j.IsConsumed && j.BuildDaysLeft == 0 && j.UltimatumDaysLeft == 0);
        int buildingJustifications = CasusBelliSystem.Justifications.Count(j => !j.IsConsumed && j.BuildDaysLeft > 0);
        int activeTruces = CasusBelliSystem.Truces.Count;
        float playerLegitimacy = CasusBelliSystem.GetLegitimacy(playerId);
        sb.AppendLine($"CasusBelli: aktif_gerekçe={activeJustifications}, inşaat={buildingJustifications}, ateşkes={activeTruces}, oyuncu_meşruiyet={playerLegitimacy:F1}");

        // --- AI ---
        int aiCountries = 0;
        int aiTotalDivs = 0;
        foreach (var c in CountryDatabase.All())
        {
            if (c != null && CountryAI.IsAiCountry(c.Id))
            {
                aiCountries++;
                aiTotalDivs += CountryAI.GetDivisions(c.Id);
            }
        }
        sb.AppendLine($"AI: ülke={aiCountries}, toplam_tümen={aiTotalDivs}");

        // --- M20.1: İstatistikler ---
        var statSave = StatisticsSystem.GetSaveData();
        int statCountries = statSave?.Countries?.Length ?? 0;
        int statValues = statSave?.Countries?.Sum(c => c.Values?.Count ?? 0) ?? 0;
        int statHistoryPoints = statSave?.Countries?.Sum(c => c.History?.Values.Sum(h => h.Count) ?? 0) ?? 0;
        sb.AppendLine($"İstatistikler: ülke={statCountries}, gösterge_değeri={statValues}, geçmiş_kayıt={statHistoryPoints}");

        // --- M26.1: Afetler ---
        var disasterSave = DisasterSystem.GetSaveData();
        int disasterActive = disasterSave?.Active?.Length ?? 0;
        int disasterHistory = disasterSave?.History?.Length ?? 0;
        sb.AppendLine($"Afetler: aktif={disasterActive}, geçmiş_kayıt={disasterHistory}");

        // --- M26.3: Müdahaleler ---
        var interventionSave = InterventionSystem.GetSaveData();
        int interventionActive = interventionSave?.Active?.Length ?? 0;
        int interventionHistory = interventionSave?.History?.Length ?? 0;
        sb.AppendLine($"Müdahaleler: aktif={interventionActive}, geçmiş_kayıt={interventionHistory}");

        // --- M26.4: Beklenmedik olaylar ---
        var unexpectedEventSave = UnexpectedEventSystem.GetSaveData();
        int unexpectedEventActive = unexpectedEventSave?.Active?.Length ?? 0;
        int unexpectedEventHistory = unexpectedEventSave?.History?.Length ?? 0;
        sb.AppendLine($"Beklenmedik Olaylar: aktif={unexpectedEventActive}, geçmiş_kayıt={unexpectedEventHistory}");

        // --- M20.2: Bütçe ---
        var budgetSave = BudgetSystem.GetSaveData();
        int budgetCountries = budgetSave?.Countries?.Length ?? 0;
        double totalDebt = budgetSave?.Countries?.Sum(c => c.GovernmentDebt) ?? 0;
        double avgDebt = budgetCountries > 0 ? totalDebt / budgetCountries : 0;
        sb.AppendLine($"Bütçe: ülke={budgetCountries}, ortalama_borç=%{avgDebt:F1} GDP");

        // --- M20.3: Halk memnuniyeti ---
        var satSave = PublicSatisfactionSystem.GetSaveData();
        int satCountries = satSave?.Countries?.Length ?? 0;
        double avgSat = 0;
        int satCount = 0;
        if (satSave?.Countries != null)
        {
            foreach (var c in satSave.Countries)
            {
                foreach (var v in c.Satisfaction?.Values ?? Enumerable.Empty<double>())
                { avgSat += v; satCount++; }
            }
        }
        avgSat = satCount > 0 ? avgSat / satCount : 0;
        sb.AppendLine($"Memnuniyet: ülke={satCountries}, grup_ortalama=%{avgSat:F1}");

        // --- M20.4: Göstergeler arası bağlantılar ---
        var formulaSave = FormulaSystem.GetSaveData();
        sb.AppendLine($"Formüller: bağlantı={formulaSave?.LinkCount ?? 0}");

        // --- M21.1: Kabine atamaları ---
        var cabinetSave = CabinetSystem.GetSaveData();
        int cabinetCountries = cabinetSave?.Countries?.Length ?? 0;
        int cabinetAssignments = cabinetSave?.Countries?.Sum(c => c.Assignments?.Count ?? 0) ?? 0;
        int cabinetScandals = cabinetSave?.Countries?.Sum(c => c.Scandals?.Length ?? 0) ?? 0;
        sb.AppendLine($"Kabine: ülke={cabinetCountries}, atama={cabinetAssignments}, skandal={cabinetScandals}");

        // --- M21.5: Bakan sadakati (kabine toplantısı etkisi) ---
        int totalLoyalty = 0;
        int ministerCount = 0;
        if (cabinetSave?.Countries != null)
        {
            foreach (var c in cabinetSave.Countries)
            {
                if (c.MinisterStats != null)
                {
                    foreach (var s in c.MinisterStats)
                    {
                        totalLoyalty += s.Loyalty;
                        ministerCount++;
                    }
                }
            }
        }
        int avgLoyalty = ministerCount > 0 ? totalLoyalty / ministerCount : 0;
        sb.AppendLine($"Bakan sadakati: ortalama=%{avgLoyalty}, bakan_sayısı={ministerCount}");

        // --- M22.2: Yasalar ---
        var lawSave = LawSystem.GetSaveData();
        int lawCountries = lawSave?.Countries?.Length ?? 0;
        int lawActive = lawSave?.Countries?.Sum(c => c.ActiveLawIds?.Length ?? 0) ?? 0;
        int lawEffectIndicators = lawSave?.Countries?.Sum(c => c.Effects?.Length ?? 0) ?? 0;
        sb.AppendLine($"Yasalar: ülke={lawCountries}, etkin_yasa={lawActive}, etki_göstergesi={lawEffectIndicators}");

        // --- M22.3: Parlamento ---
        var parliamentSave = ParliamentSystem.GetSaveData();
        int parliamentParties = parliamentSave?.Parties?.Length ?? 0;
        int parliamentVetoes = parliamentSave?.VetoedProposals?.Length ?? 0;
        sb.AppendLine($"Parlamento: parti={parliamentParties}, veto={parliamentVetoes}, sandalye={ParliamentSystem.TotalSeats}");

        // --- M22.4: Otoriter sistemler ---
        var autoritarianSave = AutoritarianSystem.GetSaveData();
        sb.AppendLine($"Otoriter: kongre_onayli={autoritarianSave.ApprovedProposals?.Length ?? 0}, darbe={autoritarianSave.CoupAttempts}, son_risk=%{autoritarianSave.LastCoupRiskPercent:F1}");

        // --- M23.1: Seçim takvimi ---
        var electionSave = ElectionSystem.GetSaveData();
        int electionCountries = electionSave?.Countries?.Length ?? 0;
        long nextElection = electionSave?.Countries?.FirstOrDefault(c => c.CountryId == gm.PlayerCountryId)?.LastElectionDay ?? 0;
        sb.AppendLine($"Seçim: ülke={electionCountries}, oyuncu_son_seçim_gün={nextElection}");

        // --- M23.2: Kampanya aksiyonları ve vaatler ---
        var campaignSave = ElectionCampaignSystem.GetSaveData();
        int campaignCountries = campaignSave?.Countries?.Length ?? 0;
        int campaignActions = campaignSave?.Countries?.Sum(c => c.Actions?.Length ?? 0) ?? 0;
        int activePromises = 0;
        if (campaignSave?.Countries != null)
        {
            foreach (var c in campaignSave.Countries)
            {
                if (c.Actions != null)
                    foreach (var a in c.Actions)
                        if (a.IsPromise && !a.IsFulfilled) activePromises++;
            }
        }
        sb.AppendLine($"Kampanya: ülke={campaignCountries}, aksiyon={campaignActions}, aktif_vaat={activePromises}");

// --- M17.2: Liderler ---
        int leaderCount = LeaderSystem.LoadedCount;
        sb.AppendLine($"Liderler: {leaderCount} ülke");

        // M23.4: Halk refahı (protesto, grev, güven oylaması, darbe)
        var unrestSave = PoliticalUnrestSystem.GetSaveData();
        int unrestCountries = unrestSave?.Countries?.Length ?? 0;
        int totalProtests = unrestSave?.Countries?.Sum(c => c.ProtestCount) ?? 0;
        int totalStrikes = unrestSave?.Countries?.Sum(c => c.StrikeCount) ?? 0;
        int totalConfidenceVotes = unrestSave?.Countries?.Sum(c => c.VoteOfConfidenceCount) ?? 0;
        int totalCoups = unrestSave?.Countries?.Sum(c => c.CoupAttemptCount) ?? 0;
        sb.AppendLine($"HalkRefahı: ülke={unrestCountries}, protesto={totalProtests}, grev={totalStrikes}, güven_oylama={totalConfidenceVotes}, darbe={totalCoups}");

        // M18.5: İl sahipliği özeti
        var provOwners = ProvinceOwnershipSystem.GetSaveData();
        sb.AppendLine($"İller: {provOwners.Count} kayıt (provinceId -> countryId)");
        if (leaderCount > 0)
        {
            foreach (var kv in LeaderSystem.All.OrderBy(k => k.Key))
            {
                var l = kv.Value;
                sb.AppendLine($"  {l.CountryId}: {l.FirstName} {l.LastName} ({l.Age}yr, {l.Party}, {l.Ideology}, Dip={l.Diplomacy}, Adm={l.Administration}, Cha={l.Charisma}, Mil={l.Military}, Int={l.Intelligence}, Eco={l.Economy}, traits={string.Join(",", l.Traits)})");
            }
        }

        return sb.ToString();
    }

    private static void FindDiff(string a, string b)
    {
        var linesA = a.Split('\n');
        var linesB = b.Split('\n');
        int max = Math.Max(linesA.Length, linesB.Length);
        for (int i = 0; i < max; i++)
        {
            string la = i < linesA.Length ? linesA[i] : "(EOF)";
            string lb = i < linesB.Length ? linesB[i] : "(EOF)";
            if (la != lb)
            {
                GD.PrintErr($"[Diff] Satır {i+1}:");
                GD.PrintErr($"  A: {la}");
                GD.PrintErr($"  B: {lb}");
                break; // İlk fark
            }
        }
    }
}