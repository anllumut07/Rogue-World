using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Uzay programı sistemi (M30.1).
// - Data/space_missions.json'dan görev tanımlarını yükler (seviye, önkoşul teknolojisi, maliyet, süre, başarı olasılığı, ödüller).
// - Bir görev başlatılabilmesi için önkoşul teknolojisinin araştırılmış olması gerekir (TechnologySystem.GetStatus == Researched).
// - Günlük tick'te aktif görevlerin süresi azalır, süre dolunca DeterministicRNG ile başarı/başarısızlık çözülür,
//   ödüller (prestij, teknoloji bonusu) uygulanır.
// - Save/Load: SaveSystem.SaveFile.SpaceSave + WriteSave/LoadGameInternal bağlama.
// Statik sınıf; GameManager günlük tick'inde DailyTick() çağrılır.
// Test kancası: --space-test → EnableTestMode() (başarı olasılığını garantileştirir).
public static class SpaceSystem
{
    public class MissionDefinition
    {
        public string Id = "?";
        public string Name = "?";
        public string Description = "";
        public string Level = "S1";        // S1..S10
        public string TechPrerequisiteId = ""; // ilgili teknoloji ID'si (boş = her zaman kullanılabilir)
        public int CostPP = 0;             // PP maliyeti
        public int DurationDays = 30;      // görev süresi (gün)
        public double BaseSuccessProbability = 0.5; // 0-1
        public int RewardPrestige = 0;     // tamamlandığında kazanılan prestij
        public Dictionary<string, float> RewardTechBonus = new(); // modifikatör bonusları
    }

    public enum MissionStatus { Locked, Available, InProgress, Completed, Failed }

    public class ActiveMission
    {
        public string MissionId = "";
        public MissionStatus Status = MissionStatus.Locked;
        public int StartDay = 0;
        public int DaysLeft = 0;
        public bool Success = false;
    }

    // DTO (Save/Load)
    public class SpaceSave
    {
        public ActiveMissionSave[] Missions = Array.Empty<ActiveMissionSave>();
    }

    public class ActiveMissionSave
    {
        public string MissionId = "";
        public int Status = 0; // (int)MissionStatus
        public int StartDay = 0;
        public int DaysLeft = 0;
        public bool Success = false;
    }

    private static readonly string Path = "Data/space_missions.json";
    private static readonly Dictionary<string, MissionDefinition> _all = new();
    private static readonly Dictionary<string, ActiveMission> _missions = new(); // id -> active mission
    private static bool _loaded;
    private static bool _initialized;
    private static bool _testMode; // --space-test: başarıyı garantiler

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    // ===== Yükleme =====

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!FileAccess.FileExists(Path))
        {
            GD.PrintErr($"HATA: {Path} bulunamadı!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var list = JsonSerializer.Deserialize<List<MissionDefinition>>(FileAccess.GetFileAsString(Path), options);
            foreach (var m in list)
                if (m?.Id != null) _all[m.Id] = m;
            GD.Print($"SpaceSystem hazır: {_all.Count} uzay görevi yüklendi");
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"HATA: {Path} okunamadı: {e.Message}");
        }
    }

    // Oyuncu ülke seçilince sıfırla (GameManager.SetPlayerCountry)
    public static void Initialize()
    {
        EnsureLoaded();
        _missions.Clear();
        foreach (var kv in _all)
        {
            _missions[kv.Key] = new ActiveMission
            {
                MissionId = kv.Key,
                Status = MissionStatus.Locked,
                StartDay = 0,
                DaysLeft = 0,
                Success = false,
            };
        }
        _initialized = true;
        GD.Print($"SpaceSystem: sıfırlandı — görev={_missions.Count}");
    }

    // ===== Günlük tick =====

    public static void DailyTick(int currentDay)
    {
        if (!_initialized) return;

        // Kilitli görevlerin uygunluluğunu güncelle (teknoloji araştırıldığında kilit açılır)
        foreach (var kv in _missions)
        {
            var mission = kv.Value;
            if (mission.Status == MissionStatus.Locked)
            {
                string prereq = MissionTechPrereq(mission);
                bool prereqOk = string.IsNullOrEmpty(prereq) ||
                                TechnologySystem.GetStatus(prereq) == TechnologySystem.ResearchStatus.Researched;
                if (prereqOk)
                    mission.Status = MissionStatus.Available;
            }
        }

        // Aktif görevlerin süresini azalt
        foreach (var kv in _missions)
        {
            var mission = kv.Value;
            if (mission.Status != MissionStatus.InProgress) continue;

            mission.DaysLeft--;
            if (mission.DaysLeft <= 0)
            {
                ResolveMission(mission, currentDay);
            }
        }
    }

    private static void ResolveMission(ActiveMission mission, int currentDay)
    {
        var def = _all[mission.MissionId];
        double successChance = def.BaseSuccessProbability;
        if (_testMode) successChance = 1.0; // test modunda garanti başarı

        bool success = DeterministicRNG.NextDouble() < successChance;
        mission.Status = success ? MissionStatus.Completed : MissionStatus.Failed;
        mission.Success = success;

        if (success)
        {
            // Ödüller: prestij + teknoloji bonusu
            if (Player != null)
                Player.Prestige += def.RewardPrestige;
            foreach (var kv in def.RewardTechBonus)
                TechnologySystem.AddModifier(kv.Key, kv.Value);

            GD.Print($"[Space] Görev tamamlandı: {def.Name} ({def.Level}) — başarı, prestij +{def.RewardPrestige}");
        }
        else
        {
            GD.Print($"[Space] Görev başarısız: {def.Name} ({def.Level})");
        }
    }

    // ===== API (UI / test) =====

    public static MissionStatus GetMissionStatus(string missionId)
    {
        EnsureLoaded();
        if (!_missions.TryGetValue(missionId, out var m)) return MissionStatus.Locked;
        // İlerideki durumlar (InProgress/Completed/Failed) sabittir; sadece Kilitli/Kullanılabilir
        // durumunu önkoşul teknolojisine göre dinamik olarak yeniden hesapla (tick'e bağımlı değil).
        if (m.Status == MissionStatus.InProgress ||
            m.Status == MissionStatus.Completed ||
            m.Status == MissionStatus.Failed)
            return m.Status;
        string prereq = MissionTechPrereq(m);
        bool prereqOk = string.IsNullOrEmpty(prereq) ||
                        TechnologySystem.GetStatus(prereq) == TechnologySystem.ResearchStatus.Researched;
        return prereqOk ? MissionStatus.Available : MissionStatus.Locked;
    }

    public static IReadOnlyDictionary<string, ActiveMission> AllMissions => _missions;

    public static bool CanLaunch(string missionId)
    {
        if (GetMissionStatus(missionId) != MissionStatus.Available) return false;
        var def = _all[missionId];
        if (Player == null) return false;
        if (Player.PoliticalPower < def.CostPP) return false;
        // Aynı anda tek görev (basit kısıt)
        if (_missions.Values.Any(x => x.Status == MissionStatus.InProgress)) return false;
        return true;
    }

    // Görevi başlat: PP harcar, InProgress'e geçirir, süresi başlar
    public static bool LaunchMission(string missionId)
    {
        if (!CanLaunch(missionId)) return false;

        var def = _all[missionId];
        var mission = _missions[missionId];
        Player.PoliticalPower -= def.CostPP;
        mission.Status = MissionStatus.InProgress;
        mission.StartDay = GameManager.Instance?.TotalDays ?? 0;
        mission.DaysLeft = def.DurationDays;
        mission.Success = false;

        GD.Print($"[Space] Görev başlatıldı: {def.Name} ({def.Level}) — süre {def.DurationDays} gün, PP -{def.CostPP}");
        return true;
    }

    public static bool IsMissionAvailable(string missionId) => GetMissionStatus(missionId) == MissionStatus.Available;

    // Test kancağı (--space-test)
    public static void EnableTestMode() => _testMode = true;

    // ===== Save/Load =====

    public static SpaceSave GetSaveData()
    {
        return new SpaceSave
        {
            Missions = _missions.Values.Select(m => new ActiveMissionSave
            {
                MissionId = m.MissionId,
                Status = (int)m.Status,
                StartDay = m.StartDay,
                DaysLeft = m.DaysLeft,
                Success = m.Success,
            }).ToArray(),
        };
    }

    public static void LoadSaveData(SpaceSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _missions.Clear();
        if (data.Missions != null)
        {
            foreach (var s in data.Missions)
            {
                if (!_all.TryGetValue(s.MissionId, out var def)) continue;
                _missions[s.MissionId] = new ActiveMission
                {
                    MissionId = s.MissionId,
                    Status = (MissionStatus)Math.Clamp(s.Status, 0, 4),
                    StartDay = s.StartDay,
                    DaysLeft = s.DaysLeft,
                    Success = s.Success,
                };
            }
        }
        // Eksik görevleri kilitli olarak ekle (yeni eklenen görevler için uyumluluk)
        foreach (var kv in _all)
            if (!_missions.ContainsKey(kv.Key))
                _missions[kv.Key] = new ActiveMission { MissionId = kv.Key, Status = MissionStatus.Locked };
        _initialized = true;
        GD.Print($"SpaceSystem: state yüklendi — görev={_missions.Count}");
    }

    public static void ResetForNewGame()
    {
        _missions.Clear();
        _all.Clear();
        _loaded = false;
        _initialized = false;
        _testMode = false;
    }

    private static string MissionTechPrereq(ActiveMission m)
    {
        return _all.TryGetValue(m.MissionId, out var d) ? d.TechPrerequisiteId : "";
    }
}
