using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Zafer koşulları (M41.1): `Data/victory_conditions.json` senaryo başına zafer koşullarını
/// tanımlar. Koşullar saf hesapla günlük değerlendirilir — rastgelelik YOKTUR (DeterministicRNG
/// kullanılmaz), böylece save/load determinizmi korunur.
/// Kurgu koşullar; gerçek kişi/kurum adı içermez.
/// </summary>
public static class VictorySystem
{
    private const string DataPath = "res://Data/victory_conditions.json";

    /// <summary>Değerlendirilebilen zafer koşulu türleri.</summary>
    public enum VictoryKind
    {
        Unknown,
        /// <summary>Dünya toplam fabrikanın en az Target kadarını kontrol etmek (%).</summary>
        ControlFactoryPercent,
        /// <summary>En az Target major gücü (>= MajorFactoryThreshold fabrika) kapitülasyonuna uğratmak.</summary>
        CapitulateMajors,
        /// <summary>Dünya illerinin en az Target kadarını elinde tutmak (%).</summary>
        ControlProvincePercent,
        /// <summary>Bitişte en yüksek ulusal skora sahip olmak.</summary>
        TopScore,
        /// <summary>Bitiş tarihine kadar bağımsız ve kapitüle olmamış kalmak.</summary>
        SurviveUntilDate
    }

    public class VictoryCondition
    {
        public string Id = "";
        public string Kind = "";
        public string Label = "";
        public string Description = "";
        public float Target = 1f;
        public float Weight = 1f;
    }

    /// <summary>Bir senaryonun zafer koşulu seti + bitiş tarihi.</summary>
    public class VictorySet
    {
        public string Id = "";
        public string EndDate = "";
        public List<VictoryCondition> Conditions = new();
    }

    // Senaryo id → zafer seti
    private static readonly Dictionary<string, VictorySet> _sets = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    /// <summary>Major güç eşiği (toplam fabrika). 30 fabrika ve üzeri = major güç.</summary>
    public const int MajorFactoryThreshold = 30;

    public static int SetCount
    {
        get { EnsureLoaded(); return _sets.Count; }
    }

    /// <summary>Setler henüz okunmadıysa bir kez okur (UI/log çağrıları güvenli olsun).</summary>
    private static void EnsureLoaded()
    {
        if (_loaded) return;
        LoadData();
    }

    public static VictorySet Get(string scenarioId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(scenarioId)) return null;
        return _sets.TryGetValue(scenarioId, out var set) ? set : null;
    }

    /// <summary>Aktif senaryonun zafer seti; tanımsızsa null.</summary>
    public static VictorySet Active => Get(ScenarioSystem.ActiveId);

    public static IReadOnlyList<VictoryCondition> ActiveConditions =>
        Active?.Conditions ?? (IReadOnlyList<VictoryCondition>)Array.Empty<VictoryCondition>();

    /// <summary>Aktif senaryonun bitiş tarihi; tanımsızsa null.</summary>
    public static DateTime? ActiveEndDate => ParseDate(Active?.EndDate);

    /// <summary>Aktif senaryonun bitiş tarihine kalan gün sayısı (negatif = geçti).</summary>
    public static int DaysUntilEnd(int totalDays)
    {
        var end = ActiveEndDate;
        if (end == null) return int.MaxValue;
        return (int)(end.Value - ScenarioSystem.ActiveStartDate).TotalDays - totalDays;
    }

    public static VictoryKind ParseKind(string kind)
    {
        if (string.IsNullOrEmpty(kind)) return VictoryKind.Unknown;
        return kind.Trim().ToLowerInvariant() switch
        {
            "control_factory_percent" => VictoryKind.ControlFactoryPercent,
            "capitulate_majors" => VictoryKind.CapitulateMajors,
            "control_province_percent" => VictoryKind.ControlProvincePercent,
            "top_score" => VictoryKind.TopScore,
            "survive_until_date" => VictoryKind.SurviveUntilDate,
            _ => VictoryKind.Unknown
        };
    }

    public static DateTime? ParseDate(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return DateTime.TryParse(text, out var dt) ? dt : (DateTime?)null;
    }

    // ===== Yükleme =====

    /// <summary>
    /// Veri dosyasını yükler (idempotent). Dosya yoksa veya bozuksa boş kalır;
    /// oyun bozulmaz, hata loglanır.
    /// </summary>
    public static void LoadData()
    {
        _sets.Clear();
        _loaded = true;

        try
        {
            if (!Godot.FileAccess.FileExists(DataPath))
            {
                GD.PrintErr($"[VictorySystem] Veri dosyası yok: {DataPath}");
                GD.Print("[VictorySystem] 0 zafer koşulu seti yüklendi.");
                return;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            var json = Godot.FileAccess.GetFileAsString(DataPath);
            var list = JsonSerializer.Deserialize<List<VictorySet>>(json, options);
            if (list == null || list.Count == 0)
            {
                GD.Print("[VictorySystem] 0 zafer koşulu seti yüklendi.");
                return;
            }

            foreach (var set in list)
            {
                if (string.IsNullOrEmpty(set.Id)) continue;
                set.Conditions ??= new List<VictoryCondition>();
                set.Conditions = set.Conditions.Where(c => c != null && !string.IsNullOrEmpty(c.Id)).ToList();
                _sets[set.Id] = set;
            }

            LogSummary();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[VictorySystem] Koşul yükleme HATA: {e.Message}");
            GD.Print($"[VictorySystem] {_sets.Count} zafer koşulu seti yüklendi (kısmi).");
        }
    }

    private static void LogSummary()
    {
        var active = Active;
        GD.Print($"[VictorySystem] {_sets.Count} zafer koşulu seti yüklendi: " +
                 string.Join(", ", _sets.Values.Select(s =>
                     $"{s.Id}({s.Conditions.Count} koşul, bitiş {s.EndDate})")));
        if (active != null)
        {
            GD.Print($"[VictorySystem] Aktif senaryo '{active.Id}': {active.Conditions.Count} koşul — " +
                     string.Join(", ", active.Conditions.Select(c => c.Label)));
        }
        else
        {
            GD.Print($"[VictorySystem] Aktif senaryo '{ScenarioSystem.ActiveId}' için koşul seti tanımsız.");
        }
    }

    // ===== Senaryo / yeni oyun yaşam döngüsü =====

    /// <summary>Senaryo değişiminde cache temizlenir (LoadScenarioData yeniden okur).</summary>
    public static void ResetForScenario()
    {
        _loaded = false;
    }

    /// <summary>Senaryo verisini yükler (--scenario-test de çağırır).</summary>
    public static void LoadScenarioData()
    {
        LoadData();
    }

    /// <summary>Yeni oyun başlangıcı: setler yeniden yüklenir.</summary>
    public static void ResetForNewGame()
    {
        LoadData();
        _capitulations.Clear();
        _achievedByPlayer.Clear();
        _achievedByAi.Clear();
        PlayerVictoryDeclared = false;
        _lastAiCheckDay = -30;
    }

    // ===== Değerlendirme (M41.2) =====
    // Saf hesap — DeterministicRNG KULLANILMAZ (save/load determinizmi korunur).

    private static readonly List<(string WinnerId, string LoserId, int LoserFactories, long Day)> _capitulations = new();
    private static readonly HashSet<string> _achievedByPlayer = new(StringComparer.OrdinalIgnoreCase);
    // "KOŞUL:ÜLKE" biçiminde — AI tarafında tamamlanan koşullar (log bir kez gelsin).
    private static readonly HashSet<string> _achievedByAi = new(StringComparer.OrdinalIgnoreCase);
    private static int _lastAiCheckDay = -30;
    private const int AiCheckIntervalDays = 30;

    public static bool PlayerVictoryDeclared { get; private set; }
    public static IReadOnlyCollection<string> PlayerAchievedConditions => _achievedByPlayer;
    public static int AiAchievedConditionCount => _achievedByAi.Count;

    // ===== Save/Load =====

    public class CapitulationSave
    {
        public string WinnerId = "";
        public string LoserId = "";
        public int LoserFactories;
        public long Day;
    }

    public class VictorySave
    {
        public bool PlayerVictoryDeclared;
        public List<string> PlayerAchieved = new();
        public List<string> AiAchieved = new();
        public List<CapitulationSave> Capitulations = new();
        public int LastAiCheckDay;
    }

    /// <summary>Zafer durumunu kaydeder (koşul tamamlamalar + kapitülasyon geçmişi).</summary>
    public static VictorySave GetSaveData()
    {
        return new VictorySave
        {
            PlayerVictoryDeclared = PlayerVictoryDeclared,
            PlayerAchieved = _achievedByPlayer.ToList(),
            AiAchieved = _achievedByAi.ToList(),
            Capitulations = _capitulations.Select(c => new CapitulationSave
            {
                WinnerId = c.WinnerId,
                LoserId = c.LoserId,
                LoserFactories = c.LoserFactories,
                Day = c.Day
            }).ToList(),
            LastAiCheckDay = _lastAiCheckDay
        };
    }

    /// <summary>Kayıtlı zafer durumunu yükler; eksik alanlar mevcut değeri korur.</summary>
    public static void ApplySave(VictorySave save)
    {
        if (save == null) return;

        _achievedByPlayer.Clear();
        foreach (var id in save.PlayerAchieved ?? new List<string>())
            if (!string.IsNullOrEmpty(id)) _achievedByPlayer.Add(id);

        _achievedByAi.Clear();
        foreach (var key in save.AiAchieved ?? new List<string>())
            if (!string.IsNullOrEmpty(key)) _achievedByAi.Add(key);

        _capitulations.Clear();
        foreach (var c in save.Capitulations ?? new List<CapitulationSave>())
        {
            if (c == null || string.IsNullOrEmpty(c.WinnerId) || string.IsNullOrEmpty(c.LoserId)) continue;
            _capitulations.Add((c.WinnerId, c.LoserId, c.LoserFactories, c.Day));
        }

        PlayerVictoryDeclared = save.PlayerVictoryDeclared;
        _lastAiCheckDay = save.LastAiCheckDay;
    }

    /// <summary>Kapitülasyon kaydı — WarSystem kapitülasyon yollarından çağrılır.</summary>
    public static void RecordCapitulation(string winnerId, string loserId, int loserFactories)
    {
        if (string.IsNullOrEmpty(winnerId) || string.IsNullOrEmpty(loserId)) return;
        long day = GameManager.IsAvailable ? GameManager.Instance.TotalDays : 0;
        _capitulations.Add((winnerId, loserId, loserFactories, day));
    }

    /// <summary>Bir ülkenin kapitülasyona uğrattığı major güç sayısı.</summary>
    public static int CapitulatedMajorCount(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return 0;
        return _capitulations
            .Where(c => c.WinnerId == countryId && c.LoserFactories >= MajorFactoryThreshold)
            .Select(c => c.LoserId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static long WorldFactories()
    {
        long total = 0;
        foreach (var c in CountryDatabase.All())
        {
            if (c == null || c.IsAnnexed) continue;
            total += Math.Max(0, c.TotalFactories);
        }
        return total;
    }

    private static long CountryFactories(string countryId)
    {
        var c = CountryDatabase.Get(countryId);
        if (c == null || c.IsAnnexed) return 0;
        return Math.Max(0, c.TotalFactories);
    }

    private static long WorldProvinces() => ProvinceOwnershipSystem.TotalProvinceCount;

    private static long CountryProvinces(string countryId) =>
        ProvinceOwnershipSystem.ProvinceCountFor(countryId);

    /// <summary>Ulusal skor (M41.3: ScoreSystem'e delege edilir).</summary>
    public static double ComputeNationalScore(string countryId) => ScoreSystem.ComputeScore(countryId);

    /// <summary>Skor sıralamasında 1 mi? (top_score koşulu).</summary>
    public static bool IsTopScoring(string countryId) => ScoreSystem.IsTopCountry(countryId);

    /// <summary>Bir koşulun ülke için sağlanıp sağlanmadığını hesaplar.</summary>
    public static bool IsSatisfied(VictoryCondition cond, string countryId, int totalDays)
    {
        if (cond == null || string.IsNullOrEmpty(countryId)) return false;
        var data = CountryDatabase.Get(countryId);
        if (data == null || data.IsAnnexed) return false;

        switch (ParseKind(cond.Kind))
        {
            case VictoryKind.ControlFactoryPercent:
                {
                    long world = WorldFactories();
                    if (world <= 0) return false;
                    return CountryFactories(countryId) * 100.0 / world >= cond.Target;
                }
            case VictoryKind.CapitulateMajors:
                return CapitulatedMajorCount(countryId) >= cond.Target;
            case VictoryKind.ControlProvincePercent:
                {
                    long world = WorldProvinces();
                    if (world <= 0) return false;
                    return CountryProvinces(countryId) * 100.0 / world >= cond.Target;
                }
            case VictoryKind.TopScore:
                return IsTopScoring(countryId);
            case VictoryKind.SurviveUntilDate:
                {
                    // Bitiş tarihine ulaşıldığında, ülke hâlâ bağımsız ve en güçlü sırada mı?
                    if (totalDays < cond.Target) return false;
                    return IsTopScoring(countryId);
                }
            default:
                return false;
        }
    }

    /// <summary>Koşulun ilerleme yüzdesi (0-100, UI göstergesi için).</summary>
    public static float ConditionProgress(VictoryCondition cond, string countryId, int totalDays)
    {
        if (cond == null || string.IsNullOrEmpty(countryId)) return 0f;
        switch (ParseKind(cond.Kind))
        {
            case VictoryKind.ControlFactoryPercent:
                {
                    long world = WorldFactories();
                    if (world <= 0) return 0f;
                    return (float)Math.Clamp(CountryFactories(countryId) * 100.0 / world, 0, 100);
                }
            case VictoryKind.CapitulateMajors:
                return (float)Math.Clamp(CapitulatedMajorCount(countryId) * 100.0 / Math.Max(1, cond.Target), 0, 100);
            case VictoryKind.ControlProvincePercent:
                {
                    long world = WorldProvinces();
                    if (world <= 0) return 0f;
                    return (float)Math.Clamp(CountryProvinces(countryId) * 100.0 / world, 0, 100);
                }
            case VictoryKind.TopScore:
                return IsTopScoring(countryId) ? 100f : 0f;
            case VictoryKind.SurviveUntilDate:
                return (float)Math.Clamp(totalDays * 100.0 / Math.Max(1, cond.Target), 0, 100);
            default:
                return 0f;
        }
    }

    /// <summary>
    /// Oyuncu için koşulları değerlendirir; yeni tamamlanan her koşulda bildirim + auto-pause.
    /// </summary>
    public static void EvaluatePlayer(int totalDays)
    {
        var set = Active;
        if (set == null || !GameManager.IsAvailable) return;
        string playerId = GameManager.Instance.PlayerCountryId;
        if (string.IsNullOrEmpty(playerId)) return;

        foreach (var cond in set.Conditions)
        {
            if (cond == null || _achievedByPlayer.Contains(cond.Id)) continue;
            if (!IsSatisfied(cond, playerId, totalDays)) continue;

            _achievedByPlayer.Add(cond.Id);
            GD.Print($"[Zafer] ✔ KOŞUL TAMAMLANDI: {cond.Id} — {cond.Label} (hedef {cond.Target})");

            if (!PlayerVictoryDeclared)
            {
                PlayerVictoryDeclared = true;
                GD.Print($"[Zafer] *** ZAFER: {cond.Label} — {GameManager.Instance.PlayerCountryData?.Name ?? playerId} ***");
                TimeTempoSystem.RequestAutoPause($"Zafer koşulu tamamlandı: {cond.Label}");

                // M41.4: Oyun sonu paneli — zafer tipi, süre, skor dökümü ve sıralama ile açılır.
                GameOverPanel.OpenUnder(GameManager.Instance, true, cond.Label, cond.Description,
                    GameManager.Instance.PlayerCountryData?.Name ?? playerId);
            }
        }
    }

    /// <summary>
    /// AI ülkelerini 30 günde bir değerlendirir (performans: günlük ağ hesap yok).
    /// </summary>
    public static void EvaluateAi(int totalDays)
    {
        var set = Active;
        if (set == null) return;
        if (totalDays - _lastAiCheckDay < AiCheckIntervalDays) return;
        _lastAiCheckDay = totalDays;

        foreach (var c in CountryDatabase.All())
        {
            if (c == null || c.IsAnnexed) continue;
            if (GameManager.IsAvailable && c.Id == GameManager.Instance.PlayerCountryId) continue;
            foreach (var cond in set.Conditions)
            {
                string key = $"{cond.Id}:{c.Id}";
                if (_achievedByAi.Contains(key)) continue;
                if (!IsSatisfied(cond, c.Id, totalDays)) continue;
                _achievedByAi.Add(key);
                GD.Print($"[Zafer][AI] ✔ {c.Name} koşulu tamamladı: {cond.Id} — {cond.Label}");
            }
        }
    }

    /// <summary>Günlük tick'ten çağrılır — oyuncu her gün, AI 30 günde bir.</summary>
    public static void DailyTick(int totalDays)
    {
        EvaluatePlayer(totalDays);
        EvaluateAi(totalDays);
    }
}
