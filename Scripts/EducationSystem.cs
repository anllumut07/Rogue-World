using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// M25.3: Eğitim öğeleri — dersler, staj, mentor, sınav, tez/proje, ağ kurma.
// Her seçim zaman (gün) + para (PP) + bağlantı (attribute) kaybı/kazancı getirir;
// başarısızlık ve tekrar mümkün. Sınav sonucu nitelikleri değiştirir.
// Deterministik (DeterministicRNG). Karakter sınıfına eklenmez; burada ayrı tutulur.
public static class EducationSystem
{
    public enum EducationModuleType
    {
        None, Course, Internship, Mentor, Exam, Thesis, Networking
    }

    public class EducationModuleDef
    {
        public string Name;
        public int DurationDays;   // kaç gün sürer
        public int CostPP;         // başlangıçta ödenen PP
        public double SuccessChance; // 0-1 başarılılma olasılığı
        public string SuccessStat;   // başarılıysa artırılan nitelik
        public int SuccessBonus;     // +N
        public string FailureStat;   // başarısızsa düşürülen nitelik
        public int FailurePenalty;   // -N
    }

    // Tanımlı modüller: (ad, süre, PP maliyet, başarı şansı, başarı stat/bonus, hata stat/ceza)
    private static readonly EducationModuleDef[] _modules = new[]
    {
        /* None           */ new EducationModuleDef { Name = "None", DurationDays = 0, CostPP = 0, SuccessChance = 0, SuccessStat = null, SuccessBonus = 0, FailureStat = null, FailurePenalty = 0 },
        /* Ders / Course  */ new EducationModuleDef { Name = "Course",           DurationDays = 7,  CostPP = 10, SuccessChance = 0.80, SuccessStat = "Intelligence", SuccessBonus = 3,  FailureStat = null,            FailurePenalty = 0 },
        /* Staj / Internship */ new EducationModuleDef { Name = "Internship",       DurationDays = 14, CostPP = 20, SuccessChance = 0.70, SuccessStat = "Administration", SuccessBonus = 5,  FailureStat = null,            FailurePenalty = 0 },
        /* Mentor         */ new EducationModuleDef { Name = "Mentor",           DurationDays = 10, CostPP = 15, SuccessChance = 0.65, SuccessStat = "Diplomacy",    SuccessBonus = 4,  FailureStat = null,            FailurePenalty = 0 },
        /* Sınav / Exam   */ new EducationModuleDef { Name = "Exam",             DurationDays = 5,  CostPP = 25, SuccessChance = 0.60, SuccessStat = "Law",        SuccessBonus = 6,  FailureStat = "Intelligence",  FailurePenalty = 2 },
        /* Tez / Thesis   */ new EducationModuleDef { Name = "Thesis",           DurationDays = 21, CostPP = 40, SuccessChance = 0.55, SuccessStat = "Intelligence", SuccessBonus = 8,  FailureStat = "Administration",FailurePenalty = 3 },
        /* Ağ Kurma / Networking */ new EducationModuleDef { Name = "Networking",       DurationDays = 12, CostPP = 30, SuccessChance = 0.75, SuccessStat = "Charisma",   SuccessBonus = 5,  FailureStat = null,            FailurePenalty = 0 },
    };

    public class EducationSave
    {
        public int CurrentModule { get; set; }      // enum değeri, -1 = yok
        public int DaysRemaining { get; set; }
        public int StartedDay { get; set; }
        public int CompletedCount { get; set; }
        public int TotalSpentPP { get; set; }
        public bool LastSuccess { get; set; }
    }

    private static int _currentModule = (int)EducationModuleType.None; // 0 = aktif modül yok
    private static int _daysRemaining;
    private static int _startedDay;
    private static int _completedCount;
    private static int _totalSpentPP;
    private static bool _lastSuccess;

    public static int CurrentModule => _currentModule;
    public static int DaysRemaining => _daysRemaining;
    public static int StartedDay => _startedDay;
    public static int CompletedCount => _completedCount;
    public static int TotalSpentPP => _totalSpentPP;
    public static bool LastSuccess => _lastSuccess;
    public static bool IsActive => _currentModule != (int)EducationModuleType.None;

    public static void Initialize()
    {
        GD.Print("[EducationSystem] Eğitim modül sistemi hazır.");
    }

    public static EducationModuleDef GetModule(EducationModuleType type)
    {
        int idx = (int)type;
        if (idx < 0 || idx >= _modules.Length) return null;
        return _modules[idx];
    }

    // Modül başlat: PP düşer, süre başlar. Zaten aktif bir modül varsa reddedilir.
    public static bool StartModule(EducationModuleType type, Character character, int currentDay)
    {
        if (character == null) return false;
        if (IsActive) return false; // aynı anda tek modül
        if (type == EducationModuleType.None) return false;

        var def = GetModule(type);
        if (def == null) return false;

        _currentModule = (int)type;
        _daysRemaining = def.DurationDays;
        _startedDay = currentDay;
        _lastSuccess = false;
        GD.Print($"[EducationSystem] Modül başlatıldı: {def.Name} ({def.DurationDays} gün, {def.CostPP} PP, başlangıç gün {currentDay})");
        return true;
    }

    // Modülü iptal et: ilerleme kaybolur, PP iade edilmez.
    public static void CancelModule()
    {
        if (!IsActive) return;
        GD.Print($"[EducationSystem] Modül iptal edildi: {GetModule((EducationModuleType)_currentModule).Name}");
        _currentModule = (int)EducationModuleType.None;
        _daysRemaining = 0;
    }

    // Günlük tick — aktif modülün süresini azaltır, bitince başarı/başarısızlık çözer.
    public static void DailyTick(int currentDay, Character character)
    {
        if (!IsActive || character == null) return;

        _daysRemaining--;
        if (_daysRemaining > 0) return;

        var def = GetModule((EducationModuleType)_currentModule);
        bool success = DeterministicRNG.NextBool(def.SuccessChance);

        if (success)
        {
            _completedCount++;
            ApplyStatChange(character, def.SuccessStat, def.SuccessBonus);
            _lastSuccess = true;
            GD.Print($"[EducationSystem] MODÜL BAŞARILI: {def.Name} → {def.SuccessStat} +{def.SuccessBonus} (gün {currentDay})");
        }
        else
        {
            _lastSuccess = false;
            if (def.FailureStat != null && def.FailurePenalty > 0)
                ApplyStatChange(character, def.FailureStat, -def.FailurePenalty);
            GD.Print($"[EducationSystem] MODÜL BAŞARISIZ: {def.Name} → {def.FailureStat ?? "yok"} {(def.FailurePenalty > 0 ? $"-{def.FailurePenalty}" : "")} (gün {currentDay}, tekrar deneyebilir)");
        }

        _currentModule = (int)EducationModuleType.None;
        _daysRemaining = 0;
    }

    private static void ApplyStatChange(Character c, string stat, int delta)
    {
        if (string.IsNullOrEmpty(stat)) return;
        switch (stat)
        {
            case "Diplomacy": c.Diplomacy = Math.Clamp(c.Diplomacy + delta, 1, 100); break;
            case "Administration": c.Administration = Math.Clamp(c.Administration + delta, 1, 100); break;
            case "Intelligence": c.Intelligence = Math.Clamp(c.Intelligence + delta, 1, 100); break;
            case "Charisma": c.Charisma = Math.Clamp(c.Charisma + delta, 1, 100); break;
            case "Military": c.Military = Math.Clamp(c.Military + delta, 1, 100); break;
            case "Economy": c.Economy = Math.Clamp(c.Economy + delta, 1, 100); break;
            case "Law": c.Law = Math.Clamp(c.Law + delta, 1, 100); break;
        }
    }

    public static EducationSave GetSaveData() => new EducationSave
    {
        CurrentModule = _currentModule,
        DaysRemaining = _daysRemaining,
        StartedDay = _startedDay,
        CompletedCount = _completedCount,
        TotalSpentPP = _totalSpentPP,
        LastSuccess = _lastSuccess,
    };

    public static void LoadSaveData(EducationSave data)
    {
        if (data == null) return;
        _currentModule = Math.Clamp(data.CurrentModule, (int)EducationModuleType.None, _modules.Length - 1);
        _daysRemaining = data.DaysRemaining;
        _startedDay = data.StartedDay;
        _completedCount = data.CompletedCount;
        _totalSpentPP = data.TotalSpentPP;
        _lastSuccess = data.LastSuccess;
    }

    public static void ResetForNewGame()
    {
        _currentModule = (int)EducationModuleType.None;
        _daysRemaining = 0;
        _startedDay = 0;
        _completedCount = 0;
        _totalSpentPP = 0;
        _lastSuccess = false;
    }
}
