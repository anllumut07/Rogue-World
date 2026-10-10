using Godot;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Teknoloji sistemi (M5): JSON'dan teknoloji ağacını yükler, araştırma slotlarını
/// yönetir, günlük araştırma ilerlemesini işler, tamamlanan teknolojilerin
/// modifikatörlerini (construction_speed, factory_output, research_speed...) uygular.
/// Statik sınıf; GameManager günlük tick'inde çağrılır.
/// </summary>
public static class TechnologySystem
{
    private const string Path = "res://Data/technologies.json";

    // Teknoloji tanımı (JSON'dan yüklenir)
    public class Technology
    {
        public string Id = "?";
        public string Name = "?";
        public string Description = "";
        public string Category = "Infantry";
        public int Era = 1936;
        public int Cost = 100;              // temel araştırma günü
        public List<string> Prerequisites = new();
        public List<string> Effects = new();            // UI gösterim metinleri
        public Dictionary<string, float> Modifiers = new(); // makine etkileri (toplanır)
    }

    // Aktif araştırma (kuyruk öğesi)
    public class ActiveResearch
    {
        public Technology Tech;
        public float ProgressDays;         // biriken araştırma günü
        public float DaysRemaining => Tech.Cost - ProgressDays;
        public float ProgressPercent => Tech.Cost > 0 ? 100f * ProgressDays / Tech.Cost : 0f;
    }

    // ===== Durum =====
    private static readonly Dictionary<string, Technology> _all = new();
    private static readonly HashSet<string> _researched = new();
    private static readonly List<ActiveResearch> _active = new();
    private static readonly Dictionary<string, float> _modifiers = new(); // toplanmış modifikatörler
    private static bool _loaded;
    private static bool _initialized;

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    // Araştırma hızı: temel 1.0 + research_speed modifikatörleri
    public static float ResearchSpeedMult => 1f + GetModifier("research_speed");

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
            var list = JsonSerializer.Deserialize<List<Technology>>(FileAccess.GetFileAsString(Path));
            foreach (var t in list)
                if (t?.Id != null) _all[t.Id] = t;
            GD.Print($"TechnologySystem hazır: {_all.Count} teknoloji yüklendi.");
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"HATA: {Path} okunamadı: {e.Message}");
        }
    }

    // Oyuncu ülkesi seçilince sıfırla (GameManager.SetPlayerCountry)
    public static void Initialize()
    {
        EnsureLoaded();
        _researched.Clear();
        _active.Clear();
        _modifiers.Clear();
        _initialized = true;
        GD.Print($"TechnologySystem: sıfırlandı — Slot={Player?.ResearchSlots ?? 0}, " +
                 $"Hız={ResearchSpeedMult:F2}x");
    }

    // ===== Günlük tick =====

    public static void DailyTick()
    {
        if (!_initialized || _active.Count == 0) return;

        float gain = 1f * ResearchSpeedMult; // günlük araştırma puanı (slot başına)
        for (int i = 0; i < _active.Count; i++)
        {
            var ar = _active[i];
            ar.ProgressDays += gain;

            if (ar.ProgressDays >= ar.Tech.Cost)
            {
                CompleteResearch(ar);
                _active.RemoveAt(i);
                i--;
            }
        }
    }

    // Tamamlanınca: researched'e ekle, modifikatörleri topla, logla
    private static void CompleteResearch(ActiveResearch ar)
    {
        var t = ar.Tech;
        _researched.Add(t.Id);

        foreach (var kv in t.Modifiers)
            _modifiers[kv.Key] = _modifiers.GetValueOrDefault(kv.Key) + kv.Value;

        GD.Print($"[Tech] Araştırma tamam: {t.Name} ({t.Category}, {t.Era}) — " +
                 $"toplam {t.Effects.Count} etki");

        // Ekonomi modifikatörleri EconomySystem'e sinyalsiz yansır (getter'lar buradan okur)
    }

    // ===== Araştırma API (UI'dan) =====

    // Bu teknoloji araştırılabilir mi? (araştırılmamış, önkoşullar tamam, slot boş değilse UI kararı verir)
    public enum ResearchStatus { Researched, InProgress, Available, MissingPrereq }

    public static ResearchStatus GetStatus(string techId)
    {
        EnsureLoaded();
        if (_researched.Contains(techId)) return ResearchStatus.Researched;
        if (_active.Exists(a => a.Tech.Id == techId)) return ResearchStatus.InProgress;
        if (!PrereqsMet(techId)) return ResearchStatus.MissingPrereq;
        return ResearchStatus.Available;
    }

    public static bool PrereqsMet(string techId)
    {
        if (!_all.TryGetValue(techId, out var t)) return false;
        foreach (var p in t.Prerequisites)
            if (!_researched.Contains(p)) return false;
        return true;
    }

    public static bool StartResearch(string techId)
    {
        EnsureLoaded();
        if (!_all.TryGetValue(techId, out var t)) return false;

        if (GetStatus(techId) != ResearchStatus.Available)
        {
            GD.Print($"[Tech] Başlatılamadı: {t.Name} (durum: {GetStatus(techId)})");
            return false;
        }

        int slots = Player?.ResearchSlots ?? 2;
        if (_active.Count >= slots)
        {
            GD.Print($"[Tech] Slot dolu: {_active.Count}/{slots} — {t.Name} başlatılamadı");
            return false;
        }

        _active.Add(new ActiveResearch { Tech = t });
        GD.Print($"[Tech] Araştırma başladı: {t.Name} ({t.Cost} gün, hız {ResearchSpeedMult:F2}x, " +
                 $"slot {_active.Count}/{slots})");
        return true;
    }

    public static void CancelResearch(string techId)
    {
        var ar = _active.Find(a => a.Tech.Id == techId);
        if (ar != null)
        {
            GD.Print($"[Tech] Araştırma iptal: {ar.Tech.Name} ({ar.ProgressDays:F0}/{ar.Tech.Cost} gün)");
            _active.Remove(ar);
        }
    }

    // ===== Sorgular (UI) =====

    public static IReadOnlyDictionary<string, Technology> All
    {
        get { EnsureLoaded(); return _all; }
    }
    public static IReadOnlyCollection<string> ResearchedIds => _researched;
    public static IReadOnlyList<ActiveResearch> ActiveResearchList => _active;
    public static int ResearchSlots => Player?.ResearchSlots ?? 2;
    public static int UsedSlots => _active.Count;

    // Toplanmış modifikatör (ör. "construction_speed" → 0.10 + 0.10 = 0.20)
    public static float GetModifier(string key) => _modifiers.GetValueOrDefault(key);

    // Kategori listesi (UI filtresi için; JSON'daki benzersiz kategoriler)
    public static List<string> GetCategories()
    {
        EnsureLoaded();
        var set = new SortedSet<string>();
        foreach (var t in _all.Values) set.Add(t.Category);
        return new List<string>(set);
    }
}