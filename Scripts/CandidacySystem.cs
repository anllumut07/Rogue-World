using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// M25.4: Aday olma kriterleri (anayasadan): yaş, eğitim seviyesi, parti üyeliği,
// tanınırlık, itibar, sicil. Kriterler sağlanmıyorsa bir sonraki seçime kadar
// eğitime devam eder. Deterministik (new Random() yok), Save/Load entegrasyonlu.
public static class CandidacySystem
{
    public class CandidacyCriteria
    {
        public int MinAge { get; set; } = 18;
        public int MinEducationCompleted { get; set; } = 1;
        public bool RequiresPartyMembership { get; set; } = true;
        public int MinRecognition { get; set; } = 30;
        public int MinReputation { get; set; } = 40;
        public bool RequiresCleanRecord { get; set; } = true;
    }

    public class CandidacyResult
    {
        public bool IsEligible { get; set; }
        public List<string> MissingCriteria { get; set; } = new();
    }

    public class CandidacySave
    {
        public CandidacyCriteria Criteria { get; set; } = new();
    }

    private static CandidacyCriteria _criteria = new();

    public static void Initialize()
    {
        GD.Print("[CandidacySystem] Aday olma kriter sistemi hazır.");
    }

    public static void SetCriteria(CandidacyCriteria criteria)
    {
        _criteria = criteria ?? new CandidacyCriteria();
    }

    public static CandidacyCriteria GetCriteria() => _criteria;

    // Karakterin aday olabilme kriterlerini denetler; eksikleri listeler.
    public static CandidacyResult CheckEligibility(Character character, string countryId)
    {
        var result = new CandidacyResult { IsEligible = true };
        if (character == null)
        {
            result.IsEligible = false;
            result.MissingCriteria.Add("Karakter tanımlı değil.");
            return result;
        }

        var constitution = ConstitutionSystem.GetConstitution(countryId);
        int minAge = Math.Max(_criteria.MinAge, constitution.VotingAge);

        if (character.Age < minAge)
            result.MissingCriteria.Add($"Yaş: {character.Age} < {minAge} (anayasa minimum)");

        if (EducationSystem.CompletedCount < _criteria.MinEducationCompleted)
            result.MissingCriteria.Add($"Eğitim: tamamlanmış modül {EducationSystem.CompletedCount} < {_criteria.MinEducationCompleted}");

        if (_criteria.RequiresPartyMembership && string.IsNullOrEmpty(character.PartyId))
            result.MissingCriteria.Add("Parti üyeligi gerekli.");

        if (character.Recognition < _criteria.MinRecognition)
            result.MissingCriteria.Add($"Tanınırlık: {character.Recognition} < {_criteria.MinRecognition}");

        if (character.Reputation < _criteria.MinReputation)
            result.MissingCriteria.Add($"Itibar: {character.Reputation} < {_criteria.MinReputation}");

        if (_criteria.RequiresCleanRecord && character.HasCriminalRecord)
            result.MissingCriteria.Add("Temiz sicil gerekli.");

        result.IsEligible = result.MissingCriteria.Count == 0;
        return result;
    }

    public static bool IsEligible(Character character, string countryId)
        => CheckEligibility(character, countryId).IsEligible;

    // --- Save / Load ---
    public static CandidacySave GetSaveData() => new() { Criteria = _criteria };

    public static void LoadSaveData(CandidacySave data)
    {
        if (data?.Criteria != null) _criteria = data.Criteria;
    }

    public static void ResetForNewGame()
    {
        _criteria = new CandidacyCriteria();
    }
}
