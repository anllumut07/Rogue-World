using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Kurgu ülke kurma mantığı (M44.3).
///
/// Age of History tarzı "formable nation": var olan bir ülke kendi
/// topraklarında (veya birden fazla ülkenin topraklarında) yeterli kültür
/// iline sahipse, siyasi güç ve gün harcayarak YENİ bir ülke kurabilir.
///
/// Koşul (her biri test edilir):
///   1. Ebe kaynak ülkelerden biri aktif olmalı (başka bir ülkenin toprağından
///      kurulamaz).
///   2. Ülke, formable'ın kültüründen en az <see cref="FormableDataSystem.FormableDefinition.RequiredProvinces"/>
///      iline sahip olmalı.
///   3. Bu iller ülkenin toplam ilinin en az
///      <see cref="FormableDataSystem.FormableDefinition.MinimumControlPercent"/>
///      yüzdesini oluşturmalı.
///   4. Ülke ilhak edilmemiş olmalı.
///   5. Oyuncu için yeterli PP olmalı (AI her zaman deneyebilir — PP'si biterse
///      birikir).
///
/// Kurulum tamamlandığında: yeni ülke CountryDatabase'ye eklenir, kültür
/// illeri devredilir, fabrika payı bölünür ve AI devralır.
/// </summary>
public static class FormableSystem
{
    /// <summary>Kurma sürecinde olan bir ülkenin durumu.</summary>
    public class FormationState
    {
        public string CountryId = "";        // kuran ülke (ebe kaynak)
        public string FormableId = "";       // kurulmakta olan kurgu ülke
        public float DaysElapsed = 0f;       // harcanan gün
        public float DaysRequired = 0f;      // gereken gün (snapshot)
    }

    // Ülke → devam eden kurma süreci
    private static readonly Dictionary<string, FormationState> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Devam eden kurma süreci sayısı.</summary>
    public static int PendingCount => _pending.Count;

    /// <summary>Bir koşul kontrolünün tek bir maddesi.</summary>
    public class ConditionItem
    {
        public string Label = "";
        public bool Met = false;
        public string Detail = "";
    }

    /// <summary>
    /// Bir ülkenin belirli bir kurgu ülkeyi kurma koşullarını değerlendirir.
    /// Sonuç listede döner; <c>CanForm</c> ile özetlenir.
    /// </summary>
    public static List<ConditionItem> EvaluateConditions(string countryId, string formableId)
    {
        var items = new List<ConditionItem>();
        var def = FormableDataSystem.Get(formableId);
        var country = CountryDatabase.Get(countryId);

        if (def == null)
        {
            items.Add(new ConditionItem { Label = "Tanım Var", Met = false, Detail = "Kurgu ülke tanımı bulunamadı" });
            return items;
        }
        if (country == null)
        {
            items.Add(new ConditionItem { Label = "Ülke Var", Met = false, Detail = "Ülke bulunamadı" });
            return items;
        }

        // 1) Ebe kaynak ülkelerden biri bu ülke olmalı
        bool isParent = def.ParentCountryIds != null && def.ParentCountryIds.Any(pid =>
            string.Equals(pid, countryId, StringComparison.OrdinalIgnoreCase));
        items.Add(new ConditionItem
        {
            Label = "Ebe Kaynak",
            Met = isParent,
            Detail = isParent
                ? $"'{country.Name}' tanımın ebe kaynaklarından biri"
                : $"Yalnızca şu ülkeler kurabilir: {string.Join(", ", def.ParentCountryIds ?? new List<string>())}"
        });

        // 2+3) Kültür illeri: sayı ve oran
        var owned = ProvinceCultureSystem.ForCountry(countryId);
        int totalOwned = owned.Count;
        var cultureProvinces = owned
            .Where(r => string.Equals(r.Culture, def.Culture, StringComparison.Ordinal))
            .ToList();
        float share = totalOwned > 0 ? (float)cultureProvinces.Count / totalOwned * 100f : 0f;

        items.Add(new ConditionItem
        {
            Label = "Gerekli İl Sayısı",
            Met = cultureProvinces.Count >= def.RequiredProvinces,
            Detail = $"{cultureProvinces.Count} / {def.RequiredProvinces} il ({def.Culture})"
        });
        items.Add(new ConditionItem
        {
            Label = "Minimum Kontrol Oranı",
            Met = share >= def.MinimumControlPercent,
            Detail = $"{share:N1}% / {def.MinimumControlPercent:N0}% (toplam {totalOwned} il)"
        });

        // 4) İlhak edilmemiş olmalı
        items.Add(new ConditionItem
        {
            Label = "Bağımsız Ülke",
            Met = !country.IsAnnexed,
            Detail = country.IsAnnexed ? $"İlhak edilmiş ({country.OwnerId})" : "Bağımsız"
        });

        // 5) Yeterli PP
        items.Add(new ConditionItem
        {
            Label = "Siyasi Güç",
            Met = country.PoliticalPower >= def.PoliticalPowerCost,
            Detail = $"{country.PoliticalPower:N0} / {def.PoliticalPowerCost:N0} PP"
        });

        return items;
    }

    public static bool CanForm(string countryId, string formableId)
        => EvaluateConditions(countryId, formableId).All(i => i.Met);

    /// <summary>Bir ülkenin kurabileceği tüm kurgu ülkeler.</summary>
    public static List<FormableDataSystem.FormableDefinition> AvailableFor(string countryId)
    {
        var result = new List<FormableDataSystem.FormableDefinition>();
        if (string.IsNullOrEmpty(countryId)) return result;

        foreach (var def in FormableDataSystem.All)
            if (CanForm(countryId, def.Id))
                result.Add(def);
        return result;
    }

    /// <summary>
    /// Kurma sürecini başlatır. Sadece koşullar sağlanıyorsa başlar.
    /// PP burada harcanmaz — kurulum tamamlanınca harcanır (gün sayacının
    /// içinde PP bitse bile süreç iptal edilmemesi için).
    /// </summary>
    public static bool TryStart(string countryId, string formableId, out string reason)
    {
        reason = "";
        if (string.IsNullOrEmpty(countryId) || string.IsNullOrEmpty(formableId))
        {
            reason = "eksik bilgi";
            return false;
        }
        if (_pending.ContainsKey(countryId))
        {
            reason = "bu ülkede zaten bir kurma süreci var";
            return false;
        }

        var def = FormableDataSystem.Get(formableId);
        if (def == null)
        {
            reason = "kurgu ülke tanımı yok";
            return false;
        }
        if (!CanForm(countryId, formableId))
        {
            reason = "koşullar sağlanmıyor";
            return false;
        }

        _pending[countryId] = new FormationState
        {
            CountryId = countryId,
            FormableId = formableId,
            DaysElapsed = 0f,
            DaysRequired = def.DaysRequired
        };
        GD.Print($"[Formable] Kurma başladı: {countryId} → {formableId} ({def.DaysRequired} gün)");
        return true;
    }

    /// <summary>
    /// Kurma sürecini iptal eder.
    /// </summary>
    public static bool Cancel(string countryId)
    {
        if (!_pending.Remove(countryId, out var state)) return false;
        GD.Print($"[Formable] Kurma iptal edildi: {state.CountryId} → {state.FormableId}");
        return true;
    }

    /// <summary>
    /// Bir gün ilerletir. Süreç tamamlanınca <see cref="Complete"/> çağrılır.
    /// </summary>
    public static void AdvanceDay(string countryId)
    {
        if (!_pending.TryGetValue(countryId, out var state)) return;
        state.DaysElapsed += 1f;
        if (state.DaysElapsed < state.DaysRequired) return;

        Complete(state.CountryId, state.FormableId);
    }

    public static bool IsPending(string countryId) => _pending.ContainsKey(countryId);

    public static FormationState State(string countryId)
        => _pending.TryGetValue(countryId, out var s) ? s : null;

    /// <summary>
    /// Kurma ilerleme oranı [0,1].
    /// </summary>
    public static float Progress(string countryId)
    {
        var s = State(countryId);
        if (s == null || s.DaysRequired <= 0f) return 0f;
        return Mathf.Clamp(s.DaysElapsed / s.DaysRequired, 0f, 1f);
    }

    /// <summary>
    /// Kurma işlemini tamamlar: yeni ülke oluşur, iller devredilir,
    /// fabrika payı bölünür.
    ///
    /// Burada rastgelelik YOKTUR — tamamen bellek içi deterministik adımlar.
    /// </summary>
    public static bool Complete(string countryId, string formableId)
    {
        var def = FormableDataSystem.Get(formableId);
        var parent = CountryDatabase.Get(countryId);
        if (def == null || parent == null) return false;

        _pending.Remove(countryId);

        // Koşullar bir kez daha doğrulanır (gecede değişmiş olabilir)
        if (!CanForm(countryId, formableId))
        {
            GD.PrintErr($"[Formable] HATA: {countryId} → {formableId} koşulları artık sağlanmıyor, kurulum durduruldu");
            return false;
        }

        // 1) Devredilecek iller: kurucu kültüründeki tüm iller
        var owned = ProvinceCultureSystem.ForCountry(countryId);
        var transferredRecords = owned
            .Where(r => string.Equals(r.Culture, def.Culture, StringComparison.Ordinal))
            .ToList();
        var transferred = transferredRecords.Select(r => r.ProvinceId).ToList();
        long movedPopulation = transferredRecords.Sum(r => r.Population);
        if (transferred.Count == 0)
        {
            GD.PrintErr($"[Formable] HATA: {formableId} için devredilecek il yok — iptal");
            return false;
        }

        // 2) PP harca
        parent.PoliticalPower = Math.Max(0, parent.PoliticalPower - def.PoliticalPowerCost);

        // 3) Yeni ülke oluştur (CountryDatabase) — nüfus devredilen illerin toplamı
        var newCountry = CountryDatabase.GetOrCreate(def.Id, def.Name);
        newCountry.Ideology = parent.Ideology;
        newCountry.Population = movedPopulation;
        newCountry.ManpowerPool = (long)(movedPopulation / 100_000);

        // 4) İlleri devret (sahiplik + kültür kaydı)
        foreach (var provId in transferred)
            ProvinceOwnershipSystem.SetProvinceOwner(provId, def.Id);
        ProvinceCultureSystem.ReassignCountry(transferred, def.Id);
        parent.Population = Math.Max(1, parent.Population - movedPopulation);

        // 5) Fabrika payı: devredilen il oranı kadar bölünür (deterministik)
        int totalOwned = owned.Count;
        float share = totalOwned > 0 ? (float)transferred.Count / totalOwned : 0f;
        int civ = (int)Math.Floor(parent.CivFactories * share);
        int mil = (int)Math.Floor(parent.MilFactories * share);
        int dock = (int)Math.Floor(parent.Dockyards * share);
        int oil = (int)Math.Floor(parent.Oil * share);
        int steel = (int)Math.Floor(parent.Steel * share);
        int alum = (int)Math.Floor(parent.Aluminum * share);

        newCountry.CivFactories = civ;
        newCountry.MilFactories = mil;
        newCountry.Dockyards = dock;
        newCountry.Oil = oil;
        newCountry.Steel = steel;
        newCountry.Aluminum = alum;

        parent.CivFactories = Math.Max(0, parent.CivFactories - civ);
        parent.MilFactories = Math.Max(0, parent.MilFactories - mil);
        parent.Dockyards = Math.Max(0, parent.Dockyards - dock);
        parent.Oil = Math.Max(0, parent.Oil - oil);
        parent.Steel = Math.Max(0, parent.Steel - steel);
        parent.Aluminum = Math.Max(0, parent.Aluminum - alum);

        // 6) Ebe kaynak ilişkisini koparma: yeni ülke için ilişki bakımı yok,
        //    ebe kaynakla ilişki düşürülmez (aynı halkın ayrı devleti).
        GD.Print($"[Formable] ÜLKE KURULDU: {def.Name} ({def.Id}) — il={transferred.Count}, " +
                  $"fabrika c{civ}/m{mil}/t{dock} — kuran: {parent.Name} ({parent.Id})");

        // 7) Yeni ülke için AI devralma: CountryAI Initialize() kendi
        //    _states'ini CountryDatabase.All() üzerinden yeniden kurar, bu
        //    yüzden GetOrCreate ile eklenen ülke sonraki Initialize'da
        //    otomatik AI olur. (Harita rengi de WorldMap.ColorForId ile
        //    hash'ten türetilir; yeni kimlik otomatik renk alır.)
        CountryAI.Reset();

        return true;
    }

    /// <summary>
    /// Yeni oyunda sıfırlar.
    /// </summary>
    public static void ResetForNewGame()
    {
        _pending.Clear();
    }

    /// <summary>Save/Load verisi (M44.5'te SaveSystem'e bağlanır).</summary>
    public static SaveSystem.FormableSave GetSaveData()
    {
        var pending = new List<SaveSystem.FormablePendingEntry>(_pending.Count);
        foreach (var s in _pending.Values)
        {
            pending.Add(new SaveSystem.FormablePendingEntry
            {
                CountryId = s.CountryId,
                FormableId = s.FormableId,
                DaysElapsed = s.DaysElapsed,
                DaysRequired = s.DaysRequired
            });
        }
        return new SaveSystem.FormableSave { Pending = pending.ToArray() };
    }

    public static void LoadSaveData(SaveSystem.FormableSave data)
    {
        _pending.Clear();
        if (data?.Pending == null) return;

        foreach (var e in data.Pending)
        {
            if (e == null || string.IsNullOrEmpty(e.CountryId)) continue;
            _pending[e.CountryId] = new FormationState
            {
                CountryId = e.CountryId,
                FormableId = e.FormableId ?? "",
                DaysElapsed = e.DaysElapsed,
                DaysRequired = e.DaysRequired
            };
        }
        GD.Print($"[Formable] Kurma süreçleri yüklendi: {_pending.Count}");
    }
}
