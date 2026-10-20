using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// M25.5: Kariyer olayları — skandal, fırsat, mentor kaybı, hastalık.
// Her olay karakterin kariyer aşamasını, niteliklerini ve PP'sini etkiler.
// Deterministik (DeterministicRNG), Save/Load entegrasyonlu.
public static class CareerEventSystem
{
    public enum CareerEventType { None, Scandal, Opportunity, MentorLoss, Illness }

    public class CareerEventDef
    {
        public CareerEventType Type;
        public string Name;
        public string Description;
        public double ProbabilityPerDay; // 0-1, her günde tetiklenme şansı
        public int ReputationChange;
        public int RecognitionChange;
        public int PpChange;
        public int StageSetback; // >0 = geri, <0 = ileri (SetbackStage/Avans)
        public int BlockedDays; // kariyer engellenme süresi (gün)
    }

    public class CareerEventRecord
    {
        public CareerEventType Type;
        public int Day;
        public string Description;
    }

    public class CareerEventSave
    {
        public int DaysBlockedRemaining;
        public List<CareerEventRecord> PastEvents { get; set; } = new();
    }

    private static readonly CareerEventDef[] _events = new[]
    {
        new CareerEventDef
        {
            Type = CareerEventType.Scandal,
            Name = "Skandal",
            Description = "Yolsuzluk/ihanet iddiası — itibar ve tanınırlık düşer, kariyer geriler.",
            ProbabilityPerDay = 0.005,
            ReputationChange = -15,
            RecognitionChange = -5,
            PpChange = -50,
            StageSetback = 1,
            BlockedDays = 0
        },
        new CareerEventDef
        {
            Type = CareerEventType.Opportunity,
            Name = "Fırsat",
            Description = "Önemli bağlantı/fırsat — itibar ve tanınırlık artar, kariyer ileri gider.",
            ProbabilityPerDay = 0.003,
            ReputationChange = 10,
            RecognitionChange = 5,
            PpChange = 100,
            StageSetback = -1,
            BlockedDays = 0
        },
        new CareerEventDef
        {
            Type = CareerEventType.MentorLoss,
            Name = "Mentor Kaybı",
            Description = "Mentör kaybı — rehbersizlik, itibar düşer, kariyer engellenir.",
            ProbabilityPerDay = 0.002,
            ReputationChange = -5,
            RecognitionChange = -3,
            PpChange = -25,
            StageSetback = 1,
            BlockedDays = 30
        },
        new CareerEventDef
        {
            Type = CareerEventType.Illness,
            Name = "Hastalık",
            Description = "Sağlık problemi — itibar düşer, kariyer bazı gün engellenir.",
            ProbabilityPerDay = 0.004,
            ReputationChange = -5,
            RecognitionChange = 0,
            PpChange = -30,
            StageSetback = 0,
            BlockedDays = 14
        },
    };

    private static int _daysBlockedRemaining;
    private static readonly List<CareerEventRecord> _pastEvents = new();

    public static int DaysBlockedRemaining => _daysBlockedRemaining;
    public static IReadOnlyList<CareerEventRecord> PastEvents => _pastEvents;

    public static void Initialize()
    {
        GD.Print("[CareerEventSystem] Kariyer olay sistemi hazır.");
    }

    // Günlük tick — engelleme devam ediyorsa azalt, değilse olay tetiklenme denemesi yapar.
    // Dönen değer: olay sonrası güncellenmiş PP.
    public static double DailyTick(int currentDay, Character character, double playerPoliticalPower)
    {
        if (character == null) return playerPoliticalPower;

        if (_daysBlockedRemaining > 0)
        {
            _daysBlockedRemaining--;
            // CareerSystem'deki engelleme de aynı oranda azalır.
            CareerSystem.DailyTick(currentDay, character);
            GD.Print($"[CareerEventSystem] Kariyer engelleme devam ediyor: kalan {_daysBlockedRemaining} gün");
            return playerPoliticalPower;
        }

        double pp = playerPoliticalPower;
        foreach (var def in _events)
        {
            if (def.Type == CareerEventType.None) continue;
            if (DeterministicRNG.NextBool(def.ProbabilityPerDay))
                ApplyEvent(def, character, currentDay, ref pp);
        }
        return pp;
    }

    private static void ApplyEvent(CareerEventDef def, Character character, int currentDay, ref double pp)
    {
        // Niyet stat değişiklikleri (clamp 1-100)
        if (def.ReputationChange != 0)
            character.Reputation = Math.Clamp(character.Reputation + def.ReputationChange, 1, 100);
        if (def.RecognitionChange != 0)
            character.Recognition = Math.Clamp(character.Recognition + def.RecognitionChange, 1, 100);

        // PP değişikliği
        pp += def.PpChange;
        if (pp < 0) pp = 0;

        // Kariyer aşama etkisi
        if (def.StageSetback > 0)
            CareerSystem.SetbackStage(currentDay);
        else if (def.StageSetback < 0)
        {
            // İleri hareket: StartEducation/CompleteEducation üzerinden yok,
            // burada sadece aşama numarasını artıramıyoruz (private). Sadece gün logu.
            GD.Print($"[CareerEventSystem] Fırsat: kariyer aşaması ileri gidebilir (şu an manuel ilerleme yok).");
        }

        // Engelleme
        if (def.BlockedDays > 0)
            CareerSystem.BlockCareer(def.BlockedDays);

        _daysBlockedRemaining = Math.Max(_daysBlockedRemaining, def.BlockedDays);

        var record = new CareerEventRecord
        {
            Type = def.Type,
            Day = currentDay,
            Description = def.Description
        };
        _pastEvents.Add(record);

        GD.Print($"[CareerEventSystem] OLAY: {def.Name} — {def.Description} (gün {currentDay}, PP {def.PpChange:+0;-0})");
    }

    // Manuel olay tetikle (test için)
    public static bool TriggerEvent(CareerEventType type, Character character, int currentDay, ref double pp)
    {
        foreach (var def in _events)
        {
            if (def.Type == type)
            {
                ApplyEvent(def, character, currentDay, ref pp);
                return true;
            }
        }
        return false;
    }

    // --- Save / Load ---
    public static CareerEventSave GetSaveData() => new CareerEventSave
    {
        DaysBlockedRemaining = _daysBlockedRemaining,
        PastEvents = _pastEvents
    };

    public static void LoadSaveData(CareerEventSave data)
    {
        if (data == null) return;
        _daysBlockedRemaining = Math.Max(0, data.DaysBlockedRemaining);
        _pastEvents.Clear();
        if (data.PastEvents != null)
            _pastEvents.AddRange(data.PastEvents);
    }

    public static void ResetForNewGame()
    {
        _daysBlockedRemaining = 0;
        _pastEvents.Clear();
    }
}
