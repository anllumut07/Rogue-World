using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// M23.2: Kampanya aksiyonları (miting, reklam, TV, ittifak, vaat).
// Kampanya aksiyonları bir süreyle oy tahminini etkiler. Verilen vaatler
// takip edilir ve yerine getirilebilir. Deterministik (new Random() yok).
public static class ElectionCampaignSystem
{
    public class CampaignActionRecord
    {
        public string ActionId = "";      // rally/advertisement/tv_speech/alliance/promise
        public string ActionName = "";
        public long DayCompleted = 0;     // TotalDays tamamlanma günü
        public int DurationDays = 0;      // oy bonusunun sürdüğü gün
        public double VoteBonus = 0.0;    // oy tahmini bonusu (yüzde puan)
        public bool IsPromise = false;    // vaat mı?
        public bool IsFulfilled = false;  // vaat yerine getirildi mi?
    }

    public class CampaignSave
    {
        public CampaignCountrySave[] Countries = Array.Empty<CampaignCountrySave>();
    }

    public class CampaignCountrySave
    {
        public string CountryId = "";
        public CampaignActionRecord[] Actions = Array.Empty<CampaignActionRecord>();
    }

    private static readonly Dictionary<string, List<CampaignActionRecord>> _campaignHistory = new();

    public static IReadOnlyDictionary<string, List<CampaignActionRecord>> CampaignHistory => _campaignHistory;

    static ElectionCampaignSystem()
    {
        LoadData();
    }

    public static void LoadData()
    {
        _campaignHistory.Clear();
        GD.Print("[ElectionCampaignSystem] Kampanya sistemi hazır.");
    }

    public static void ResetForNewGame()
    {
        _campaignHistory.Clear();
        foreach (var c in CountryDatabase.All())
            _campaignHistory[c.Id] = new List<CampaignActionRecord>();
        GD.Print($"[ElectionCampaignSystem] {_campaignHistory.Count} ülke kampanya geçmişi sıfırlandı.");
    }

    /// <summary>Tamamlanan kampanya aksiyonunu kaydeder (TimeTempoSystem tarafından çağrılır).</summary>
    public static void RecordCampaignAction(string countryId, string actionId, string actionName, long dayCompleted, int durationDays, double voteBonus, bool isPromise)
    {
        if (string.IsNullOrEmpty(countryId)) return;
        if (!_campaignHistory.ContainsKey(countryId))
            _campaignHistory[countryId] = new List<CampaignActionRecord>();
        _campaignHistory[countryId].Add(new CampaignActionRecord
        {
            ActionId = actionId,
            ActionName = actionName,
            DayCompleted = dayCompleted,
            DurationDays = durationDays,
            VoteBonus = voteBonus,
            IsPromise = isPromise,
            IsFulfilled = false
        });
        GD.Print($"[ElectionCampaignSystem] {countryId}: kampanya '{actionName}' ({actionId}) günde {dayCompleted} tamamlandı, +{voteBonus:F1} oy, {durationDays} gün");
    }

    /// <summary>Belirli günde aktif kampanya oy bonusunu döner (toplam, 0-100 clampe tabi değildir — GetVotePrediction clamped).</summary>
    public static double GetCampaignBonus(string countryId, long currentDay)
    {
        if (!_campaignHistory.TryGetValue(countryId, out var acts)) return 0.0;
        double bonus = 0.0;
        foreach (var a in acts)
        {
            if (currentDay >= a.DayCompleted && currentDay <= a.DayCompleted + a.DurationDays)
                bonus += a.VoteBonus;
        }
        return bonus;
    }

    /// <summary>Hala yerine getirilmemiş vaatleri döner.</summary>
    public static List<CampaignActionRecord> GetActivePromises(string countryId)
    {
        var result = new List<CampaignActionRecord>();
        if (!_campaignHistory.TryGetValue(countryId, out var acts)) return result;
        foreach (var a in acts)
            if (a.IsPromise && !a.IsFulfilled) result.Add(a);
        return result;
    }

    /// <summary>İndis'e göre bir vaatı yerine getirildi olarak işaretler.</summary>
    public static bool FulfillPromise(string countryId, int promiseIndex)
    {
        var promises = GetActivePromises(countryId);
        if (promiseIndex < 0 || promiseIndex >= promises.Count) return false;
        var target = promises[promiseIndex];
        var hist = _campaignHistory[countryId];
        var real = hist.FirstOrDefault(r => r.ActionId == target.ActionId && r.ActionName == target.ActionName && r.DayCompleted == target.DayCompleted && !r.IsFulfilled);
        if (real == null) return false;
        real.IsFulfilled = true;
        GD.Print($"[ElectionCampaignSystem] {countryId}: vaat yerine getirildi '{target.ActionName}'");
        return true;
    }

    // ===== M12: Save/Load =====
    public static CampaignSave GetSaveData() => new CampaignSave
    {
        Countries = _campaignHistory.Select(kv => new CampaignCountrySave
        {
            CountryId = kv.Key,
            Actions = kv.Value.ToArray()
        }).ToArray()
    };

    public static void LoadSaveData(CampaignSave data)
    {
        if (data == null) return;
        _campaignHistory.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                _campaignHistory[c.CountryId] = c.Actions?.ToList() ?? new List<CampaignActionRecord>();
            }
        }
        GD.Print($"[ElectionCampaignSystem] {_campaignHistory.Count} ülke kampanya geçmişi yüklendi.");
    }
}
