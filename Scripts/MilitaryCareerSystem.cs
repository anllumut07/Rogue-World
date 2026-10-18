using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// M25.2: Asker kariyer yolu — harp okulu → rütbeler → görev yerleri → savaş deneyimi → genelkurmay;
// darbe seçeneği (ağır sonuçlu). Deterministik, aşamalı ilerleme.
// Karakter sınıfına eklenmez; burada ayrı tutulur (CareerSystem ile aynı pattern).
public static class MilitaryCareerSystem
{
    public enum MilitaryRank { Private, Corporal, Sergeant, Lieutenant, Captain, Major, Colonel, General, FieldMarshal }
    public enum MilitaryBranch { Infantry, Armor, Artillery, AirForce, Naval, SpecialForces }
    public enum DutyStation { WarAcademy, BorderPost, Garrison, FrontLine, GeneralStaff, StaffCollege }

    public class MilitaryCareerSave
    {
        public int Rank { get; set; }
        public int Branch { get; set; }
        public int DutyStation { get; set; }
        public bool AcademyCompleted { get; set; }
        public int DaysInCurrentRank { get; set; }
        public int TotalServiceDays { get; set; }
        public int CombatExperience { get; set; } // 0-100
        public bool WarCollegeCompleted { get; set; }
        public int CoupRisk { get; set; } // 0-100
        public bool CareerActive { get; set; }
        public bool DishonorablyDischarged { get; set; }
    }

    // Terfi gereksinimleri: (gerekli gün, akademi gerekir mi, staf kolajı gerekir mi)
    // İndeks = hedef rütbenin enum değeri. Private (0) başlangıç rütbesidir, terfi gerekmez.
    private static readonly (int days, bool academy, bool warCollege)[] _promoReqs = new[]
    {
        /* Private  */ (0,  false, false),
        /* Corporal */ (30, false, false),
        /* Sergeant */ (60, false, false),
        /* Lieutenant */ (90, true,  false),
        /* Captain  */ (120, false, false),
        /* Major    */ (180, false, false),
        /* Colonel  */ (240, false, true),
        /* General  */ (360, false, false),
        /* FieldMarshal */ (720, false, false),
    };

    private static MilitaryRank _rank = MilitaryRank.Private;
    private static MilitaryBranch _branch = MilitaryBranch.Infantry;
    private static DutyStation _duty = DutyStation.WarAcademy;
    private static bool _academyCompleted;
    private static int _daysInCurrentRank;
    private static int _totalServiceDays;
    private static int _combatExperience;
    private static bool _warCollegeCompleted;
    private static int _coupRisk;
    private static bool _careerActive;
    private static bool _dishonorablyDischarged;

    public static MilitaryRank Rank => _rank;
    public static MilitaryBranch Branch => _branch;
    public static DutyStation Duty => _duty;
    public static bool AcademyCompleted => _academyCompleted;
    public static int DaysInCurrentRank => _daysInCurrentRank;
    public static int TotalServiceDays => _totalServiceDays;
    public static int CombatExperience => _combatExperience;
    public static bool WarCollegeCompleted => _warCollegeCompleted;
    public static int CoupRisk => _coupRisk;
    public static bool CareerActive => _careerActive;
    public static bool DishonorablyDischarged => _dishonorablyDischarged;

    public static void Initialize()
    {
        GD.Print("[MilitaryCareerSystem] Asker kariyer sistemi hazır.");
    }

    // Asker kariyerine başla (harp okulu). Asker kökenli karakter küçük bir deneyim avantajı ile başlar.
    public static void StartMilitaryCareer(MilitaryBranch branch, Character character, int currentDay)
    {
        if (character == null) return;
        if (_careerActive) return;
        if (_dishonorablyDischarged) return;

        _branch = branch;
        _rank = MilitaryRank.Private;
        _duty = DutyStation.WarAcademy;
        _careerActive = true;
        _daysInCurrentRank = 0;
        _totalServiceDays = 0;
        _combatExperience = 0;
        _academyCompleted = false;
        _warCollegeCompleted = false;
        _coupRisk = 5; // taban darbe riski

        if (character.Background == "military")
            _combatExperience = 5;

        GD.Print($"[MilitaryCareerSystem] Asker kariyer başlatıldı: dal={branch}, rütbe={_rank}, görev={_duty} (gün {currentDay})");
    }

    // Harp okulu bitir → Garrisson'a geç, Private ise Corporal'a terfi.
    public static void CompleteAcademy(int currentDay)
    {
        if (!_careerActive || _dishonorablyDischarged) return;
        if (_duty != DutyStation.WarAcademy) return;

        _academyCompleted = true;
        _duty = DutyStation.Garrison;
        _daysInCurrentRank = 0;

        // Mezuniyet özel terfi: Private → Corporal (gün şartı olmadan)
        if (_rank == MilitaryRank.Private)
        {
            _rank = MilitaryRank.Corporal;
            _daysInCurrentRank = 0;
            GD.Print($"[MilitaryCareerSystem] Harp okulu mezunu → Corporal (özel terfi, gün {currentDay})");
        }
        else
        {
            GD.Print($"[MilitaryCareerSystem] Harp okulu tamamlandı → görev={_duty} (gün {currentDay})");
        }
    }

    // Görev yerini değiştir. FrontLine → savaş deneyimi kazanır; GeneralStaff → darbe riski artar.
    public static void ChangeDutyStation(DutyStation station, int currentDay)
    {
        if (!_careerActive || _dishonorablyDischarged) return;
        _duty = station;
        if (station == DutyStation.GeneralStaff)
            _coupRisk = Math.Clamp(_coupRisk + 5, 0, 100);
        GD.Print($"[MilitaryCareerSystem] Görev yeri değişti: {station} (darbe riski % {_coupRisk}, gün {currentDay})");
    }

    // Staf kolajı bitir → Colonel ve üzeri rütbede gereklidir; Colonel → General terfisinde lazımdır.
    public static void CompleteWarCollege(int currentDay)
    {
        if (!_careerActive || _dishonorablyDischarged) return;
        if (_rank < MilitaryRank.Major) return; // yalnızca üst-memur rütbe

        _warCollegeCompleted = true;
        _daysInCurrentRank = 0;
        GD.Print($"[MilitaryCareerSystem] Staf kolajı tamamlandı: rütbe={_rank} (gün {currentDay})");
    }

    // Kural bazlı terfi. Gün şartı + önkoşullar (akademi/staf kolajı) denenir.
    private static bool Promote(int currentDay)
    {
        int nextIndex = (int)_rank + 1;
        if (nextIndex >= _promoReqs.Length) return false; // zaten en yüksek rütbe

        var req = _promoReqs[nextIndex];
        if (_daysInCurrentRank < req.days) return false;
        if (req.academy && !_academyCompleted) return false;
        if (req.warCollege && !_warCollegeCompleted) return false;

        _rank = (MilitaryRank)nextIndex;
        _daysInCurrentRank = 0;
        _totalServiceDays++;

        // Daha yüksek rütbe → daha yüksek darbe riski (hırs)
        _coupRisk = Math.Clamp(_coupRisk + 3, 0, 100);

        GD.Print($"[MilitaryCareerSystem] Terfi: {_rank} (gün {currentDay}, darbe riski % {_coupRisk})");
        return true;
    }

    // Günlük tick — rütbede geçen gün,FrontLine'den deneyim, darbe riski, terfi denetimi.
    public static void DailyTick(int currentDay, Character character)
    {
        if (!_careerActive || _dishonorablyDischarged) return;

        _daysInCurrentRank++;
        _totalServiceDays++;

        // FrontLine'de her gün +1 savaş deneyimi (maks 100)
        if (_duty == DutyStation.FrontLine)
            _combatExperience = Math.Clamp(_combatExperience + 1, 0, 100);

        // Yüksek rütbede her gün darbe riski yavaşça artar
        if (_rank >= MilitaryRank.Colonel)
            _coupRisk = Math.Clamp(_coupRisk + 1, 0, 100);

        Promote(currentDay);
    }

    // Darbe girişimi. Albay ve üzeri gerekir. Başarısız olsa da asker kariyer sona erer (ağır sonuç).
    public static (bool success, string message) AttemptCoup()
    {
        if (!_careerActive || _dishonorablyDischarged)
            return (false, "Aktif asker kariyeri yok.");
        if (_rank < MilitaryRank.Colonel)
            return (false, "Darbe için rütbe yetersiz — Albay ve üzeri gerekir.");

        // Deterministik zar: darbe riski oranında başarı
        bool success = DeterministicRNG.NextBool(_coupRisk / 100.0);

        _dishonorablyDischarged = true;
        _careerActive = false;

        if (success)
        {
            _rank = MilitaryRank.Private;
            GD.Print("[MilitaryCareerSystem] DARBE BAŞARILI — askerî müdahale; ağır sonuçlar.");
            return (true, "DARBE BAŞARILI — askerî müdahale gerçekleşti (ağır kurumsal sonuç).");
        }

        _rank = MilitaryRank.Private;
        GD.Print("[MilitaryCareerSystem] DARBE BAŞARISIZ — haysiyesiz çıkarıldı, rütbe kaybedildi.");
        return (false, "DARBE BAŞARISIZ — haysiyesiz çıkarıldı, tüm rütbe ve deneyim kaybedildi.");
    }

    public static MilitaryCareerSave GetSaveData() => new MilitaryCareerSave
    {
        Rank = (int)_rank,
        Branch = (int)_branch,
        DutyStation = (int)_duty,
        AcademyCompleted = _academyCompleted,
        DaysInCurrentRank = _daysInCurrentRank,
        TotalServiceDays = _totalServiceDays,
        CombatExperience = _combatExperience,
        WarCollegeCompleted = _warCollegeCompleted,
        CoupRisk = _coupRisk,
        CareerActive = _careerActive,
        DishonorablyDischarged = _dishonorablyDischarged,
    };

    public static void LoadSaveData(MilitaryCareerSave data)
    {
        if (data == null) return;
        _rank = (MilitaryRank)Math.Clamp(data.Rank, 0, 8);
        _branch = (MilitaryBranch)Math.Clamp(data.Branch, 0, 5);
        _duty = (DutyStation)Math.Clamp(data.DutyStation, 0, 5);
        _academyCompleted = data.AcademyCompleted;
        _daysInCurrentRank = data.DaysInCurrentRank;
        _totalServiceDays = data.TotalServiceDays;
        _combatExperience = Math.Clamp(data.CombatExperience, 0, 100);
        _warCollegeCompleted = data.WarCollegeCompleted;
        _coupRisk = Math.Clamp(data.CoupRisk, 0, 100);
        _careerActive = data.CareerActive;
        _dishonorablyDischarged = data.DishonorablyDischarged;
    }

    public static void ResetForNewGame()
    {
        _rank = MilitaryRank.Private;
        _branch = MilitaryBranch.Infantry;
        _duty = DutyStation.WarAcademy;
        _academyCompleted = false;
        _daysInCurrentRank = 0;
        _totalServiceDays = 0;
        _combatExperience = 0;
        _warCollegeCompleted = false;
        _coupRisk = 0;
        _careerActive = false;
        _dishonorablyDischarged = false;
    }
}
