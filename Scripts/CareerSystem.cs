using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// M25.1: Siyasetçi kariyer yolu — üniversite → parti gençlik kolu → yerel yönetim → milletvekili → bakanlık.
// Deterministik, aşamalı ilerleme. Karakterin eğitim alanına göre nitelik bonusu uygular.
public static class CareerSystem
{
    public enum CareerStage { None, University, PartyYouth, LocalGovernment, MP, Minister }
    public enum EducationField { None, Law, Economics, PoliticalScience }

    public class CareerSave
    {
        public int Stage { get; set; }
        public int EducationField { get; set; }
        public bool EducationCompleted { get; set; }
        public int StageStartedDay { get; set; }
        public int TotalDaysInCareer { get; set; }
    }

    private static readonly string DataPath = "res://Data/career_stages.json";
    private static bool _loaded;

    // Karakter üzerindeki kariyer durumu (Character sınıfına eklenmez; burada ayrı tutulur).
    private static CareerStage _stage = CareerStage.None;
    private static EducationField _education = EducationField.None;
    private static bool _educationCompleted;
    private static int _stageStartedDay;
    private static int _totalDaysInCareer;

    public static CareerStage Stage => _stage;
    public static EducationField Education => _education;
    public static bool EducationCompleted => _educationCompleted;
    public static int StageStartedDay => _stageStartedDay;
    public static int TotalDaysInCareer => _totalDaysInCareer;

    public static void Initialize()
    {
        if (_loaded) return;
        _loaded = true;
        LoadStages();
        GD.Print("[CareerSystem] Kariyer sistemi hazır.");
    }

    private static void LoadStages()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(DataPath)) return;
            string json = Godot.FileAccess.GetFileAsString(DataPath);
            using var doc = JsonDocument.Parse(json);
            GD.Print($"[CareerSystem] Kariyer aşamaları yüklendi: {DataPath}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[CareerSystem] Aşama yükleme HATA: {e.Message}");
        }
    }

    // Eğitime başla (üniversite alan seçimi). Karakter niteliğine kalıcı bonus verir.
    public static void StartEducation(EducationField field, Character character, int currentDay)
    {
        if (character == null) return;
        if (_stage != CareerStage.None) return; // zaten bir aşamada

        _education = field;
        _stage = CareerStage.University;
        _stageStartedDay = currentDay;
        ApplyEducationBonus(field, character);
        GD.Print($"[CareerSystem] Eğitim başladı: {field} (gün {currentDay})");
    }

    // Eğitimi tamamla → sonraki kariyer aşamasına geçilir.
    public static void CompleteEducation(int currentDay)
    {
        _educationCompleted = true;
        AdvanceStage(currentDay);
    }

    private static void AdvanceStage(int currentDay)
    {
        // Aşama sırası: University → PartyYouth → LocalGovernment → MP → Minister
        switch (_stage)
        {
            case CareerStage.University:
                _stage = CareerStage.PartyYouth; break;
            case CareerStage.PartyYouth:
                _stage = CareerStage.LocalGovernment; break;
            case CareerStage.LocalGovernment:
                _stage = CareerStage.MP; break;
            case CareerStage.MP:
                _stage = CareerStage.Minister; break;
            case CareerStage.Minister:
                break; // son aşama
        }
        _stageStartedDay = currentDay;
        _totalDaysInCareer++;
        GD.Print($"[CareerSystem] Aşama ileri: {_stage} (gün {currentDay})");
    }

    // Günlük tick — aktif aşamada süre geçince ilerleme (basit: her 30 günde bir bir sonraki aşama).
    public static void DailyTick(int currentDay, Character character)
    {
        if (character == null) return;
        if (_stage == CareerStage.None || _stage == CareerStage.Minister) return;

        int daysInStage = currentDay - _stageStartedDay;
        if (daysInStage >= 30)
            AdvanceStage(currentDay);
    }

    public static CareerSave GetSaveData() => new CareerSave
    {
        Stage = (int)_stage,
        EducationField = (int)_education,
        EducationCompleted = _educationCompleted,
        StageStartedDay = _stageStartedDay,
        TotalDaysInCareer = _totalDaysInCareer
    };

    public static void LoadSaveData(CareerSave data)
    {
        if (data == null) return;
        _stage = (CareerStage)Math.Clamp(data.Stage, 0, 5);
        _education = (EducationField)Math.Clamp(data.EducationField, 0, 3);
        _educationCompleted = data.EducationCompleted;
        _stageStartedDay = data.StageStartedDay;
        _totalDaysInCareer = data.TotalDaysInCareer;
    }

    private static void ApplyEducationBonus(EducationField field, Character c)
    {
        switch (field)
        {
            case EducationField.Law: c.Law = Math.Clamp(c.Law + 10, 1, 100); break;
            case EducationField.Economics: c.Economy = Math.Clamp(c.Economy + 10, 1, 100); break;
            case EducationField.PoliticalScience: c.Diplomacy = Math.Clamp(c.Diplomacy + 10, 1, 100); break;
        }
    }
}
