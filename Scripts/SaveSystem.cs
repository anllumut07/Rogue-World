using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Kaydet/Yükle merkezi sistemi (M12).
/// - user://saves/slot_1.json ... slot_10.json + auto_save.json
/// - Her slot: Version, tarih, oyuncu ülke, hız, RNG state, tüm sistem state'leri
/// - Atomik yazma: .tmp → Move (bozulma koruması)
/// - Yükleme: parse → validate → apply (başarısızsa "Kayıt yüklenemedi", oyun bozulmaz)
/// - Float/double tam hassasiyet (System.Text.Json default round-trip)
/// - Auto-save: her yıl Ocak 1 günü
/// </summary>
public static class SaveSystem
{
    private const int SaveVersion = 2; // v2: senaryo id + başlangıç tarihi eklendi (M17.1)
    private const string SavesDir = "user://saves";
    public const int MaxSlots = 10; // LoadGamePanel slot listesi için public

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // ===== Pending load (ana menü akışı) =====
    // Ana menüden LOAD seçilince slot buraya yazılır, sahne game.tscn'e değişir,
    // Game._Ready pending'i görüp load eder. -1 = yok, 0 = auto-save, 1-10 = slot.
    public static int PendingLoadSlot { get; set; } = -1;

    // ===== Mevcut slot (M15.3) =====
    // Oyunun yüklendiği / son kaydedildiği slot. -1 = yeni oyun (henüz slot yok),
    // 0 = auto-save, 1-10 = slot. Save paneli açılınca bu slot önseçili gelir.
    public static int CurrentSlot { get; set; } = -1;

    // ===== DTO: Kök kayıt dosyası =====
    public class SaveFile
    {
        public int Version { get; set; }
        public string GameDate { get; set; }
        public int TotalDays { get; set; }
        public string PlayerCountryId { get; set; }
        public string PlayerCountryName { get; set; }
        public int Speed { get; set; }
        public bool IsPlayerCountryLocked { get; set; }
        public ulong RngState { get; set; }
        public string SavedAt { get; set; }
        public string SlotName { get; set; } // M15.3: kullanıcı verdiği kayıt adı
        public string ScenarioId { get; set; }             // M17.1: v2 — aktif senaryo id
        public string ScenarioStartDate { get; set; }      // M17.1: v2 — senaryo başlangıç tarihi (yyyy-MM-dd)
        public CountryData[] Countries { get; set; }
        public EconomySave Economy { get; set; }
        public TechnologySave Technology { get; set; }
        public EquipmentSave Equipment { get; set; }
        public ArmySave Army { get; set; }
        public WarSave War { get; set; }
        public DiplomacySave Diplomacy { get; set; }
        public CasusBelliSave CasusBelli { get; set; } // M17A: gerekçe/ateşkes/meşruiyet
        public FocusSave Focus { get; set; }
        public EventSave Events { get; set; }
        public DecisionSave Decisions { get; set; }
        public AiSave Ai { get; set; }
        public TimeTempoSystem.TimeTempoSave TimeTempo { get; set; } // M16: zaman/tempo/ajanda
        public LeaderSave Leaders { get; set; } // M17.2: kurgusal liderler
        public Dictionary<string, string> ProvinceOwners { get; set; } // M18.5: il sahipliği (provinceId -> countryId)
        public StatisticsSystem.IndicatorsSave Statistics { get; set; } // M20.1: istatistik gösterge değerleri
        public BudgetSystem.BudgetSave Budget { get; set; } // M20.2: bütçe (vergi, harcama, borç, faiz)
        public PublicSatisfactionSystem.SatisfactionSave PublicSatisfaction { get; set; } // M20.3: grup memnuniyeti
        public CabinetSystem.CabinetSave Cabinet { get; set; } // M21.1: bakanlık atamaları (ülke -> ministryId -> ministerId)
        public LawSystem.LawSave Law { get; set; } // M22.2: aktif yasalar ve birikmiş etkilere göre indirimler
        public ParliamentSystem.ParliamentSave Parliament { get; set; } // M22.3: partiler, sandalye, veto, koalisyon, referandum
        public AutoritarianSystem.AutoritarianSave Autoritarian { get; set; } // M22.4: parti kongresi ve darbe riski
        public ElectionSystem.ElectionSave Election { get; set; } // M23.1: seçim takvimi (son seçim günü)
        public ElectionCampaignSystem.CampaignSave ElectionCampaign { get; set; } // M23.2: kampanya aksiyonları ve vaatler
        public PoliticalUnrestSystem.PoliticalUnrestSave PoliticalUnrest { get; set; } // M23.4: protesto, grev, güven oylaması, erken seçim, darbe
        public CharacterSave Character { get; set; } // M24.1: oyuncu karakteri
        public CareerSystem.CareerSave Career { get; set; } // M25.1: oyuncu kariyer durumu
        public MilitaryCareerSystem.MilitaryCareerSave MilitaryCareer { get; set; } // M25.2: asker kariyer durumu
        public EducationSystem.EducationSave Education { get; set; } // M25.3: eğitim modül durumu
        public DisasterSystem.DisasterSave Disasters { get; set; } // M26.1: afetler (aktif + geçmiş kayıt)
        public InterventionSystem.InterventionSave Interventions { get; set; } // M26.3: müdahaleler (aktif + geçmiş kayıt)
    }

    // ===== Economy DTOs =====
    public class EconomySave
    {
        public int CurrentLaw { get; set; }
        public int ConvoyCount { get; set; }
        public BuildItemSave[] Queue { get; set; }
        public Dictionary<string, long> Stockpiles { get; set; }
        public TradeDealSave[] TradeDeals { get; set; }
    }
    public class BuildItemSave
    {
        public int Type { get; set; }
        public int PointsNeeded { get; set; }
        public int PointsDone { get; set; }
        public int AssignedFactories { get; set; }
    }
    public class TradeDealSave
    {
        public string Resource { get; set; }
        public int AmountPerDay { get; set; }
        public string ExporterId { get; set; }
        public string ExporterName { get; set; }
    }

    // ===== Technology DTOs =====
    public class TechnologySave
    {
        public string[] Researched { get; set; }
        public ActiveResearchSave[] Active { get; set; }
        public Dictionary<string, float> Modifiers { get; set; }
    }
    public class ActiveResearchSave
    {
        public string TechId { get; set; }
        public float ProgressDays { get; set; }
    }

    // ===== Equipment DTOs =====
    public class EquipmentSave
    {
        public int VariantCounter { get; set; }
        public EquipmentSystem.ProductionLine[] Lines { get; set; }
        public EquipmentSystem.EquipmentVariant[] Variants { get; set; }
        public Dictionary<string, long> Stocks { get; set; }
    }

    // ===== Army DTOs =====
    public class ArmySave
    {
        public int TemplateCounter { get; set; }
        public int DivisionCounter { get; set; }
        public int ArmyCounter { get; set; }
        public ArmySystem.DivisionTemplate[] Templates { get; set; }
        public ArmySystem.Division[] Divisions { get; set; }
        public ArmySystem.Army[] Armies { get; set; }
        public TrainingItemSave[] TrainingQueue { get; set; }
    }
    public class TrainingItemSave
    {
        public string Id { get; set; }
        public string TemplateId { get; set; }
        public string TemplateName { get; set; }
        public int TotalDays { get; set; }
        public int DaysLeft { get; set; }
    }

    // ===== War DTOs =====
    public class WarSave
    {
        public WarEntry[] Wars { get; set; }
        public Dictionary<string, int> WarGoalReductions { get; set; }
    }
    public class WarEntry
    {
        public string Id { get; set; }
        public string AttackerId { get; set; }
        public string DefenderId { get; set; }
        public string DefenderName { get; set; }
        public int StartDay { get; set; }
        public int EnemyDivisions { get; set; }
        public float EnemyOrgPool { get; set; }
        public float WarProgress { get; set; }
        public bool OffensiveActive { get; set; }
        public bool FrontlineStance { get; set; }
        public long PlayerCasualties { get; set; }
        public long EnemyCasualties { get; set; }
        public bool IsEnded { get; set; }
        public string EndReason { get; set; }
    }

    // ===== Diplomacy DTOs =====
    public class DiplomacySave
    {
        public float WorldTension { get; set; }
        public DiplomacySystem.Faction[] Factions { get; set; }
        public DiplomacySystem.Relation[] Relations { get; set; }
        public DiplomacySystem.Guarantee[] Guarantees { get; set; }
        public string[] NonAggressionPacts { get; set; }
        public string[] MilitaryAccess { get; set; }
        public string[] NavalAccess { get; set; }
    }

    // ===== Casus Belli DTOs (M17A) =====
    public class CasusBelliSave
    {
        public CasusBelliJustificationSave[] Justifications { get; set; }
        public CasusBelliTruceSave[] Truces { get; set; }
        public Dictionary<string, float> Legitimacy { get; set; }
    }
    public class CasusBelliJustificationSave
    {
        public string CountryId { get; set; }
        public string TargetId { get; set; }
        public string TypeId { get; set; }
        public int BuildDaysLeft { get; set; }
        public bool IsAutomatic { get; set; }
        public bool Exposed { get; set; }
        public int UltimatumDaysLeft { get; set; }
        public bool IsConsumed { get; set; }
    }
    public class CasusBelliTruceSave
    {
        public string Key { get; set; }
        public int EndDay { get; set; }
    }

    // ===== Focus DTOs =====
    public class FocusSave
    {
        public CountryFocusStateSave[] States { get; set; }
    }
    public class CountryFocusStateSave
    {
        public string CountryId { get; set; }
        public string[] Completed { get; set; }
        public string ActiveId { get; set; }
        public int ActiveDays { get; set; }
        public bool AiExhausted { get; set; }
    }

    // ===== Event DTOs =====
    public class EventSave
    {
        public CountryEventStateSave[] States { get; set; }
    }
    public class CountryEventStateSave
    {
        public string CountryId { get; set; }
        public string[] FiredEventIds { get; set; }
        public string[] CompletedEventIds { get; set; }
    }

    // ===== Decision DTOs =====
    public class DecisionSave
    {
        public CountryDecisionStateSave[] States { get; set; }
    }
    public class CountryDecisionStateSave
    {
        public string CountryId { get; set; }
        public ActiveDecisionSave[] Active { get; set; }
        public Dictionary<string, int> Cooldowns { get; set; }
        public string[] Completed { get; set; }
    }
    public class ActiveDecisionSave
    {
        public string DecisionId { get; set; }
        public int DaysElapsed { get; set; }
        public int TotalDays { get; set; }
    }

    // ===== AI DTOs =====
    public class AiSave
    {
        public AiStateSave[] States { get; set; }
        public string[] AiCountryIds { get; set; }
        public int Cursor { get; set; }
    }
    public class AiStateSave
    {
        public string CountryId { get; set; }
        public int BuildType { get; set; }
        public int BuildPoints { get; set; }
        public float ResearchProgress { get; set; }
        public int ResearchCompleted { get; set; }
        public int Divisions { get; set; }
        public int TrainingDaysLeft { get; set; }
        public int DivisionsInTraining { get; set; }
        public string FactionId { get; set; }
        public int LastDecisionDay { get; set; }
        public int LastMoveDay { get; set; } // M19.8: AI ordusu hareket cooldown'u
        public bool AiExhausted { get; set; }
    }

    // ===== Leader DTOs (M17.2) =====
    public class LeaderSave
    {
        public LeaderSystem.Leader[] Leaders { get; set; }
    }

    // ===== Character DTOs (M24.1) =====
    public class CharacterSave
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
        public string Gender { get; set; }
        public string BirthCountryId { get; set; }
        public string CultureKey { get; set; }
        public string Background { get; set; }
        public int Diplomacy { get; set; }
        public int Administration { get; set; }
        public int Intelligence { get; set; }
        public int Charisma { get; set; }
        public int Military { get; set; }
        public int Economy { get; set; }
        public int Law { get; set; }
        public string[] Traits { get; set; }
        public string SkinTone { get; set; }
        public string HairColor { get; set; }
        public string ClothingStyle { get; set; }
        public int Health { get; set; }
        public string SpouseId { get; set; } = "";
        public bool IsMarried { get; set; }
        // M25.4: aday olma kriterleri alanları
        public string PartyId { get; set; } = "";
        public int Recognition { get; set; }
        public int Reputation { get; set; }
        public bool HasCriminalRecord { get; set; }
    }

    // ===== Yardımcı: Dosya yolları =====
    private static string SlotPath(int slot, bool auto = false)
    {
        if (auto) return $"{SavesDir}/auto_save.json";
        if (slot < 1 || slot > MaxSlots) throw new ArgumentOutOfRangeException(nameof(slot), "Slot 1-10");
        return $"{SavesDir}/slot_{slot}.json";
    }

    private static string Globalized(string path) => ProjectSettings.GlobalizePath(path);

    private static void EnsureSavesDir()
    {
        var dir = Globalized(SavesDir);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    // ===== KAYDET =====
    public static void SaveGame(int slot, string slotName = null)
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer)
        {
            GD.PrintErr("[SaveSystem] Kaydet başarısız: oyuncu ülke seçmemiş");
            return;
        }
        if (slot < 1 || slot > MaxSlots)
        {
            GD.PrintErr($"[SaveSystem] Kaydet başarısız: geçersiz slot {slot} (1-10)");
            return;
        }
        WriteSave(SlotPath(slot), $"slot {slot}", slotName);
        CurrentSlot = slot; // M15.3: son kaydedilen slot
    }

    // Auto-save (Ocak 1 günü çağrılır) — slot yerine auto_save.json kullanır
    public static void AutoSave()
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer) return;
        WriteSave(SlotPath(0, auto: true), "auto-save", "Auto Save");
        CurrentSlot = 0;
    }

    // M15.3: Kayıt silme (slot 0 = auto, 1-10 = slot). UI onay penceresiyle çağırır.
    public static void DeleteSave(int slot)
    {
        string path = Globalized(SlotPath(slot, auto: slot == 0));
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                GD.Print($"[SaveSystem] Kayıt silindi: {(slot == 0 ? "auto-save" : $"slot {slot}")}");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SaveSystem] Silme HATA: {e.Message}");
        }
    }

    private static void WriteSave(string relPath, string label, string slotName = null)
    {
        var gm = GameManager.Instance;

        // Açık EventPopup varsa callback OLMAKSIZIN kapat — seçenekler uygulanmasın,
        // kayıt sırasında state değişmesin
        if (EventPopup.IsOpen)
            EventPopup.ForceCloseForLoad();

        var save = new SaveFile
        {
            Version = SaveVersion,
            GameDate = gm.CurrentDate.ToString("yyyy-MM-dd"),
            TotalDays = gm.TotalDays,
            PlayerCountryId = gm.PlayerCountryId,
            PlayerCountryName = gm.PlayerCountryData?.Name ?? gm.PlayerCountryId,
            Speed = gm.Speed,
            IsPlayerCountryLocked = gm.IsPlayerCountryLocked,
            RngState = DeterministicRNG.GetState(),
            SavedAt = DateTime.UtcNow.ToString("o"),
            SlotName = slotName, // M15.3: kullanıcı adı (null → UI varsayılan üretir)
            ScenarioId = ScenarioSystem.ActiveId, // M17.1: v2 alan
            ScenarioStartDate = ScenarioSystem.ActiveStartDate.ToString("yyyy-MM-dd"), // M17.1
            Countries = CountryDatabase.All().ToArray(),
            Economy = EconomySystem.GetSaveData(),
            Technology = TechnologySystem.GetSaveData(),
            Equipment = EquipmentSystem.GetSaveData(),
            Army = ArmySystem.GetSaveData(),
            War = WarSystem.GetSaveData(),
            Diplomacy = DiplomacySystem.GetSaveData(),
            CasusBelli = CasusBelliSystem.GetSaveData(),
            Focus = FocusSystem.GetSaveData(),
            Events = EventSystem.GetSaveData(),
            Decisions = DecisionSystem.GetSaveData(),
            Ai = CountryAI.GetSaveData(),
            TimeTempo = TimeTempoSystem.GetSaveData(), // M16
            Leaders = LeaderSystem.GetSaveData(), // M17.2: kurgusal liderler
            ProvinceOwners = ProvinceOwnershipSystem.GetSaveData(), // M18.5: il sahipliği
            Statistics = StatisticsSystem.GetSaveData(), // M20.1: istatistik gösterge değerleri
            Budget = BudgetSystem.GetSaveData(), // M20.2: bütçe (vergi, harcama, borç, faiz)
            PublicSatisfaction = PublicSatisfactionSystem.GetSaveData(), // M20.3: grup memnuniyeti
            Cabinet = CabinetSystem.GetSaveData(), // M21.1: bakanlık atamaları
            Law = LawSystem.GetSaveData(), // M22.2: aktif yasalar ve birikmiş etkilere göre indirimler
            Parliament = ParliamentSystem.GetSaveData(), // M22.3: partiler, sandalye, veto, koalisyon, referandum
            Autoritarian = AutoritarianSystem.GetSaveData(), // M22.4: parti Kongresi ve darbe riski
            Election = ElectionSystem.GetSaveData(), // M23.1: seçim takvimi
            ElectionCampaign = ElectionCampaignSystem.GetSaveData(), // M23.2: kampanya aksiyonları ve vaatler
            PoliticalUnrest = PoliticalUnrestSystem.GetSaveData(), // M23.4: protesto, grev, güven oylaması, erken seçim, darbe
            Character = CharacterSaveFrom(gm.PlayerCharacter), // M24.1: oyuncu karakteri
            Career = CareerSystem.GetSaveData(), // M25.1: oyuncu kariyer durumu
            MilitaryCareer = MilitaryCareerSystem.GetSaveData(), // M25.2: asker kariyer durumu
            Education = EducationSystem.GetSaveData(), // M25.3: eğitim modül durumu
            Disasters = DisasterSystem.GetSaveData(), // M26.1: afetler (aktif + geçmiş kayıt)
            Interventions = InterventionSystem.GetSaveData(), // M26.3: müdahaleler (aktif + geçmiş kayıt)
        };

        EnsureSavesDir();
        string dstPath = Globalized(relPath);
        string tmpPath = dstPath + ".tmp";

        try
        {
            string json = JsonSerializer.Serialize(save, _jsonOpts);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, dstPath, true);
            GD.Print($"[SaveSystem] Oyun kaydedildi: {label} → {dstPath} ({new FileInfo(dstPath).Length:N0} byte)");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SaveSystem] Kaydetme HATA ({label}): {e.Message}");
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        }
    }

    // ===== YÜKLE =====
    public static bool LoadGame(int slot)
    {
        bool ok = LoadGameInternal(SlotPath(slot), $"slot {slot}");
        if (ok) CurrentSlot = slot; // M15.3: son yüklenen slot
        return ok;
    }

    public static bool LoadAutoSave()
    {
        bool ok = LoadGameInternal(SlotPath(0, auto: true), "auto-save");
        if (ok) CurrentSlot = 0;
        return ok;
    }

    // M12 doğrulama testi (--save-load-test): slot 1-10'a dokunmadan ayrı dosyaya
    // kaydeder — normal oyunda asla çağrılmaz, Load menüsünde görünmez.
    private const string TestFileName = "test_save.json";
    public static void SaveTestFile() => WriteSave($"{SavesDir}/{TestFileName}", "test_save");
    public static bool LoadTestFile() => LoadGameInternal($"{SavesDir}/{TestFileName}", "test_save");
    public static void DeleteTestFile()
    {
        try { File.Delete(Globalized($"{SavesDir}/{TestFileName}")); } catch { }
    }

    private static bool LoadGameInternal(string relPath, string sourceLabel)
    {
        string path = Globalized(relPath);

        // Parse & validate
        SaveFile save;
        try
        {
            if (!File.Exists(path))
            {
                GD.PrintErr($"[SaveSystem] Yükleme başarısız: {sourceLabel} dosyası yok ({path})");
                return false;
            }

            string json = File.ReadAllText(path);
            save = JsonSerializer.Deserialize<SaveFile>(json, _jsonOpts);
            if (save == null) throw new InvalidDataException("JSON boş/null");

            // M17.1: Sürüm kontrolü — v1 kayıtları historical_1936 olarak kabul et, v2 için senaryo doğrula
            if (save.Version < 1 || save.Version > SaveVersion)
            {
                GD.PrintErr($"[SaveSystem] Yükleme başarısız: sürüm uyumsuz — kayıt v{save.Version}, oyun v{SaveVersion}");
                return false;
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SaveSystem] Yükleme başarısız (parse): {sourceLabel} — {e.Message}");
            return false;
        }

        // M17.1: Aktif senaryoyu kaydedene göre ayarla (v1 kayıtlarda ScenarioId yok → historical_1936)
        string scenarioId = save.Version >= 2 && !string.IsNullOrEmpty(save.ScenarioId) ? save.ScenarioId : "historical_1936";
        if (!ScenarioSystem.SetActive(scenarioId))
        {
            GD.PrintErr($"[SaveSystem] Yükleme başarısız: senaryo bulunamadı: {scenarioId}");
            return false;
        }

        // Apply
        try
        {
            // 0) Açık popup'ı kapat (callback olmadan)
            if (EventPopup.IsOpen)
                EventPopup.ForceCloseForLoad();

            // 1) CountryDatabase önce — her sistem CountryData referansı üzerine kurulu
            CountryDatabase.LoadSaveData(save.Countries);

            // 2) GameManager çekirdek state (tarih, oyuncu ülke, kilit, hız)
            GameManager.Instance.ApplyLoadedState(
                DateTime.Parse(save.GameDate),
                save.TotalDays,
                save.PlayerCountryId
            );
            GameManager.Instance.ApplyLoadedRuntime(save.Speed, save.IsPlayerCountryLocked);

            // 3) RNG state
            DeterministicRNG.LoadState(save.RngState);

            // 4) Sistemler
            EconomySystem.LoadSaveData(save.Economy);
            TechnologySystem.LoadSaveData(save.Technology);
            EquipmentSystem.LoadSaveData(save.Equipment);
            ArmySystem.LoadSaveData(save.Army);
            WarSystem.LoadSaveData(save.War);
            DiplomacySystem.LoadSaveData(save.Diplomacy);
            CasusBelliSystem.LoadSaveData(save.CasusBelli);
            FocusSystem.LoadSaveData(save.Focus);
            EventSystem.LoadSaveData(save.Events);
            DecisionSystem.LoadSaveData(save.Decisions);
            CountryAI.LoadSaveData(save.Ai);
            TimeTempoSystem.LoadSaveData(save.TimeTempo); // M16: ajanda/aylık rapor
            LeaderSystem.LoadSaveData(save.Leaders); // M17.2: kurgusal liderler
            ProvinceOwnershipSystem.LoadSaveData(save.ProvinceOwners); // M18.5: il sahipliği
            StatisticsSystem.LoadSaveData(save.Statistics); // M20.1: istatistik gösterge değerleri
            BudgetSystem.LoadSaveData(save.Budget); // M20.2: bütçe (vergi, harcama, borç, faiz)
            PublicSatisfactionSystem.LoadSaveData(save.PublicSatisfaction); // M20.3: grup memnuniyeti
            CabinetSystem.LoadSaveData(save.Cabinet); // M21.1: bakanlık atamaları
            LawSystem.LoadSaveData(save.Law); // M22.2: aktif yasalar ve birikmiş etkilere göre indirimler
            ParliamentSystem.LoadSaveData(save.Parliament); // M22.3: partiler, sandalye, veto, koalisyon, referandum
            AutoritarianSystem.LoadSaveData(save.Autoritarian); // M22.4: parti Kongresi ve darbe riski
            ElectionSystem.LoadSaveData(save.Election); // M23.1: seçim takvimi
            ElectionCampaignSystem.LoadSaveData(save.ElectionCampaign); // M23.2: kampanya aksiyonları ve vaatler
            PoliticalUnrestSystem.LoadSaveData(save.PoliticalUnrest); // M23.4: protesto, grev, güven oylaması, erken seçim, darbe

            // M24.1: oyuncu karakteri — kayıtta varsa geri yüklenir
            if (save.Character != null)
            {
                var character = CharacterSaveTo(save.Character);
                GameManager.Instance.SetCharacter(character);
                GD.Print($"[SaveSystem] Oyuncu karakteri yüklendi: {character.FullName} (köken={character.Background}, yaş={character.Age})");
            }

            // M25.1: oyuncu kariyer durumu — kayıtta varsa geri yüklenir
            CareerSystem.LoadSaveData(save.Career);

            // M25.2: asker kariyer durumu — kayıtta varsa geri yüklenir
            MilitaryCareerSystem.LoadSaveData(save.MilitaryCareer);

            // M25.3: eğitim modül durumu — kayıtta varsa geri yüklenir
            EducationSystem.LoadSaveData(save.Education);

            // M26.1: afetler — kayıtta varsa geri yüklenir
            DisasterSystem.LoadSaveData(save.Disasters);

            // M26.3: müdahaleler — kayıtta varsa geri yüklenir
            InterventionSystem.LoadSaveData(save.Interventions);

            // 5) Sinyaller: harita sahipliği, UI, tarih
            var gm = GameManager.Instance;
            gm.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);
            gm.EmitSignal(GameManager.SignalName.PlayerCountryChanged, save.PlayerCountryId);
            gm.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
            gm.EmitSignal(GameManager.SignalName.DateChanged, gm.GetDateText());
            gm.EmitSignal(GameManager.SignalName.DayChanged, (long)gm.TotalDays);

            // 6) Yüklenen oyun DURAKLATILMIŞ başlar
            gm.SetSpeed(0);

            GD.Print($"[SaveSystem] Oyun yüklendi: {sourceLabel} — Senaryo={scenarioId}, Tarih={save.GameDate}, Ülke={save.PlayerCountryId}, RngState={save.RngState}");
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[SaveSystem] Yükleme HATA (apply): {sourceLabel} — {e}");
            return false;
        }
    }

    // ===== Character serialization helpers (M24.1) =====
    public static CharacterSave CharacterSaveFrom(Character c)
    {
        if (c == null) return null;
        return new CharacterSave
        {
            Id = c.Id,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Age = c.Age,
            Gender = c.Gender,
            BirthCountryId = c.BirthCountryId,
            CultureKey = c.CultureKey,
            Background = c.Background,
            Diplomacy = c.Diplomacy,
            Administration = c.Administration,
            Intelligence = c.Intelligence,
            Charisma = c.Charisma,
            Military = c.Military,
            Economy = c.Economy,
            Law = c.Law,
            Traits = c.Traits?.ToArray() ?? Array.Empty<string>(),
            SkinTone = c.SkinTone,
            HairColor = c.HairColor,
            ClothingStyle = c.ClothingStyle,
            Health = c.Health,
            SpouseId = c.SpouseId,
            IsMarried = c.IsMarried,
            PartyId = c.PartyId,
            Recognition = c.Recognition,
            Reputation = c.Reputation,
            HasCriminalRecord = c.HasCriminalRecord,
        };
    }

    public static Character CharacterSaveTo(CharacterSave s)
    {
        if (s == null) return null;
        return new Character
        {
            Id = s.Id,
            FirstName = s.FirstName,
            LastName = s.LastName,
            Age = s.Age,
            Gender = s.Gender,
            BirthCountryId = s.BirthCountryId,
            CultureKey = s.CultureKey,
            Background = s.Background,
            Diplomacy = s.Diplomacy,
            Administration = s.Administration,
            Intelligence = s.Intelligence,
            Charisma = s.Charisma,
            Military = s.Military,
            Economy = s.Economy,
            Law = s.Law,
            Traits = s.Traits != null ? new List<string>(s.Traits) : new List<string>(),
            SkinTone = s.SkinTone,
            HairColor = s.HairColor,
            ClothingStyle = s.ClothingStyle,
            Health = s.Health,
            SpouseId = s.SpouseId ?? "",
            IsMarried = s.IsMarried,
            PartyId = s.PartyId ?? "",
            Recognition = s.Recognition,
            Reputation = s.Reputation,
            HasCriminalRecord = s.HasCriminalRecord,
        };
    }

    // ===== SLOT BİLGİSİ (Load menüsü listelemesi için) =====
    public class SlotInfo
    {
        public int Slot { get; set; }           // 1-10 veya 0=auto
        public bool Exists { get; set; }
        public string SlotName { get; set; }    // M15.3: kullanıcı kayıt adı
        public string DateText { get; set; }
        public string CountryName { get; set; }
        public string CountryId { get; set; }
        public int Speed { get; set; }
        public string SavedAt { get; set; }
        public string ScenarioId { get; set; }      // M17.1: kaydın senaryosu (v1 → historical_1936)
        public string ScenarioName { get; set; }    // M17.1: LoadGamePanel satırında gösterilir
        public string SavedAtText { get; set; }  // M15.3: "14 Oct 2026 15:30" gibi kısa gösterim
    }

    public static SlotInfo GetSlotInfo(int slot) => ReadSlotInfo(SlotPath(slot), slot);

    public static SlotInfo GetAutoSaveInfo() => ReadSlotInfo(SlotPath(0, auto: true), 0);

    private static SlotInfo ReadSlotInfo(string relPath, int slot)
    {
        string path = Globalized(relPath);
        var info = new SlotInfo { Slot = slot, Exists = false };
        if (!File.Exists(path)) return info;

        try
        {
            string json = File.ReadAllText(path);
            var save = JsonSerializer.Deserialize<SaveFile>(json, _jsonOpts);
            if (save == null) return info;

            info.Exists = true;
            if (DateTime.TryParse(save.GameDate, out var dt))
                info.DateText = dt.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            else
                info.DateText = save.GameDate;
            info.CountryName = save.PlayerCountryName;
            info.CountryId = save.PlayerCountryId;
            info.Speed = save.Speed;
            info.SavedAt = save.SavedAt;
            info.SlotName = save.SlotName;
            // M17.1: senaryo bilgisi (v1 kayıtlar → historical_1936)
            info.ScenarioId = save.Version >= 2 && !string.IsNullOrEmpty(save.ScenarioId)
                ? save.ScenarioId : "historical_1936";
            info.ScenarioName = ScenarioSystem.GetById(info.ScenarioId)?.Name ?? info.ScenarioId;
            // Kayıt anının kısa bilgisi (gerçek dünya zamanı)
            if (DateTime.TryParse(save.SavedAt, out var savedAt))
                info.SavedAtText = savedAt.ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            else
                info.SavedAtText = save.SavedAt;
        }
        catch { /* bozuk dosya — Exists=false olarak göster */ }
        return info;
    }
}
