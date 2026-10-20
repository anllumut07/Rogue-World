using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileAccess = Godot.FileAccess;

/// <summary>
/// Zaman ve Tempo Sistemi (M16).
/// - Oyun hızları Data/game_settings.json'dan (1x=0.5 gün/sn, 5x=1 gün/sn).
/// - Haftalık ajanda: 6 slot/hafta, 5 aksiyon türü (Data/agenda_actions.json).
/// - Ay başı raporu (1. gün) + otomatik duraklama.
/// - Önemli bildirimler kuyruğu + NEXT EVENT butonu.
/// - Save/Load: TimeTempoSave DTO (System.Text.Json, IncludeFields=true).
/// </summary>
public static class TimeTempoSystem
{
    // ===== Hız ayarları (game_settings.json) =====
    private static GameSpeedSettings _speedSettings;
    private static bool _speedSettingsLoaded = false;

    // ===== Ajanda yapılandırması (game_settings.json içinden) =====
    private static AgendaConfig _agendaConfig;
    private static List<AgendaActionDef> _actionDefs = new();

    // ===== Haftalık Ajanda State =====
    private static readonly List<AgendaSlot> _weeklyAgenda = new(); // 6 slot
    private static readonly List<ActiveAgendaAction> _activeActions = new(); // devam eden
    private static int _currentWeekNumber = 0;
    private static int _slotsUsedThisWeek = 0;
    private const int MaxSlotsPerWeek = 6;
    private const int MaxConcurrentActions = 3;

    // ===== Aylık Rapor =====
    private static MonthlyReportData _lastMonthlyReport;
    private static bool _monthlyReportPending = false;

    // ===== Otomatik Duraklama / Bildirim Kuyruğu =====
    private static bool _autoPauseRequested = false;
    private static string _autoPauseReason = "";
    private static readonly Queue<NotificationEvent> _notificationQueue = new();

    // ===== JSON Seçenekleri (kod tabanı standardı) =====
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        WriteIndented = true
    };

    // ===== DTO Sınıfları (JSON deserialize için) =====

    public class GameSpeedSettings
    {
        public GameSpeedLevel[] GameSpeeds = Array.Empty<GameSpeedLevel>();
        public AgendaConfig Agenda = new();
        public MonthlyReportConfig MonthlyReport = new();
        public AutoPauseConfig AutoPause = new();
    }

    public class GameSpeedLevel
    {
        public int Level;
        public string Name = "";
        public double DaysPerSecond;
        public string Description = "";
    }

    public class AgendaConfig
    {
        public int SlotsPerWeek = 6;
        public int MaxConcurrentActions = 3;
        public string ActionsFile = "";
    }

    public class AgendaActionDef
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public int DurationDays;
        public int CostPP;
        public Dictionary<string, object> Effects = new();
        public Dictionary<string, object> Requirements = new();
        public string Icon = "";
    }

    public class MonthlyReportConfig
    {
        public bool AutoPause = true;
        public int ShowOnDay = 1;
    }

    public class AutoPauseConfig
    {
        public bool OnEvent = true;
        public bool OnWarDeclaration = true;
        public bool OnCapitulation = true;
    }

    // ===== Runtime Yapılar =====

    public class AgendaSlot
    {
        public int Index;
        public string ActionId = "";
        public bool IsLocked;
    }

    public class ActiveAgendaAction
    {
        public string ActionId = "";
        public string ActionName = "";
        public int RemainingDays;
        public int TotalDays;
        public Dictionary<string, object> Effects = new();
    }

    public class MonthlyReportData
    {
        public DateTime Date;
        public Dictionary<string, long> StatChanges = new();
        public List<string> Warnings = new();
        public List<string> Decisions = new();
    }

    public class NotificationEvent
    {
        public string Type = "";
        public string Title = "";
        public string Description = "";
        public DateTime TriggerDate;
        public Action OnConfirm;
    }

    // ===== Save/Load DTO =====

    public class TimeTempoSave
    {
        public int CurrentSpeedLevel = 1;
        public int CurrentWeekNumber = 0;
        public int SlotsUsedThisWeek = 0;
        public AgendaSlotSave[] WeeklyAgenda = Array.Empty<AgendaSlotSave>();
        public ActiveAgendaActionSave[] ActiveActions = Array.Empty<ActiveAgendaActionSave>();
        public NotificationEventSave[] Notifications = Array.Empty<NotificationEventSave>(); // M38.1: bekleyen bildirimler kayıtta korunur
        public bool MonthlyReportPending = false;
        public MonthlyReportDataSave LastMonthlyReport = null;
        public bool AutoPauseRequested = false;
        public string AutoPauseReason = "";
    }

    public class NotificationEventSave
    {
        public string Type = "";
        public string Title = "";
        public string Description = "";
        public long TriggerDateTicks = 0;
    }

    public class AgendaSlotSave
    {
        public int Index;
        public string ActionId = "";
        public bool IsLocked;
    }

    public class ActiveAgendaActionSave
    {
        public string ActionId = "";
        public string ActionName = "";
        public int RemainingDays;
        public int TotalDays;
        public Dictionary<string, object> Effects = new();
    }

    public class MonthlyReportDataSave
    {
        public long DateTicks;
        public Dictionary<string, long> StatChanges = new();
        public List<string> Warnings = new();
        public List<string> Decisions = new();
    }

    // ===== Public API =====

    public static IReadOnlyList<AgendaSlot> WeeklyAgenda => _weeklyAgenda;
    public static IReadOnlyList<AgendaActionDef> ActionDefs => _actionDefs;
    public static IReadOnlyList<ActiveAgendaAction> ActiveActions => _activeActions;
    public static int SlotsUsedThisWeek => _slotsUsedThisWeek;
    public static int MaxSlotsPerWeekConst => MaxSlotsPerWeek;
    public static int MaxConcurrentActionsConst => MaxConcurrentActions;
    public static int CurrentWeekNumber => _currentWeekNumber;
    public static bool IsMonthlyReportPending => _monthlyReportPending;
    public static MonthlyReportData LastMonthlyReport => _lastMonthlyReport;
    public static bool AutoPauseRequested => _autoPauseRequested;
    public static string AutoPauseReason => _autoPauseReason;
    public static bool HasPendingNotifications => _notificationQueue.Count > 0;
    public static int PendingNotificationCount => _notificationQueue.Count;

    /// <summary>Sistemi başlatır (GameManager._Ready'de çağrılır).</summary>
    public static void Initialize()
    {
        LoadSpeedSettings();
        LoadAgendaActions();
        ResetWeeklyAgenda();
        _currentWeekNumber = 0;
        _monthlyReportPending = false;
        _autoPauseRequested = false;
        _notificationQueue.Clear();
        GD.Print($"[TimeTempo] Initialize: {_speedSettings?.GameSpeeds?.Length ?? 0} hız seviyesi, {_actionDefs.Count} aksiyon türü");
    }

    private static void LoadSpeedSettings()
    {
        const string path = "res://Data/game_settings.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[TimeTempo] {path} bulunamadı — varsayılan hızlar kullanılacak");
            _speedSettings = new GameSpeedSettings();
            return;
        }
        try
        {
            string json = FileAccess.GetFileAsString(path);
            _speedSettings = JsonSerializer.Deserialize<GameSpeedSettings>(json, _jsonOpts);
            if (_speedSettings == null) _speedSettings = new GameSpeedSettings();
            _speedSettingsLoaded = true;
            _agendaConfig = _speedSettings.Agenda ?? new AgendaConfig(); // M16: ajanda yapılandırması
            GD.Print($"[TimeTempo] Hız ayarları yüklendi: {_speedSettings.GameSpeeds.Length} seviye, " +
                     $"ajanda aksiyon dosyası: {_agendaConfig.ActionsFile}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TimeTempo] Hız ayarları parse hatası: {e.Message}");
            _speedSettings = new GameSpeedSettings();
        }
    }

    private static void LoadAgendaActions()
    {
        if (_agendaConfig?.ActionsFile == null || string.IsNullOrEmpty(_agendaConfig.ActionsFile))
        {
            _agendaConfig = new AgendaConfig(); // varsayılan
            _actionDefs.Clear();
            GD.PrintErr("[TimeTempo] AgendaConfig boş — aksiyonlar yüklenemedi");
            return;
        }
        string path = _agendaConfig.ActionsFile;
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[TimeTempo] {path} bulunamadı");
            return;
        }
        try
        {
            string json = FileAccess.GetFileAsString(path);
            var wrapper = JsonSerializer.Deserialize<AgendaActionsWrapper>(json, _jsonOpts);
            if (wrapper?.Actions != null)
            {
                _actionDefs = wrapper.Actions;
                GD.Print($"[TimeTempo] Ajanda aksiyonları yüklendi: {_actionDefs.Count} tür");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[TimeTempo] Ajanda aksiyonları parse hatası: {e.Message}");
        }
    }

    private class AgendaActionsWrapper
    {
        public List<AgendaActionDef> Actions = new();
    }

    // ===== Hız API (GameManager kullanır) =====

    /// <summary>Verilen hız seviyesi için gün/saniye döner. Yüklenmediyse -1 (fallback: eski formül).</summary>
    public static double GetDaysPerSecond(int speedLevel)
    {
        if (!_speedSettingsLoaded || _speedSettings?.GameSpeeds == null) return -1;
        var level = _speedSettings.GameSpeeds.FirstOrDefault(s => s.Level == speedLevel);
        return level?.DaysPerSecond ?? -1;
    }

    // ===== Günlük Tick (GameManager.DailyTick içinden çağrılır) =====

    public static void DailyTick(GameManager gm)
    {
        if (!_speedSettingsLoaded) return;

        // 1) Aktif aksiyonları ilerlet
        foreach (var action in _activeActions)
        {
            action.RemainingDays--;
        }
        var completed = _activeActions.FindAll(a => a.RemainingDays <= 0);
        foreach (var action in completed)
        {
            ApplyActionEffects(action);
            _activeActions.Remove(action);
            GD.Print($"[Agenda] Aksiyon tamamlandı: {action.ActionName} (etkiler uygulandı)");
        }

        // 2) Hafta sonu kontrolü (Pazartesi = DayOfWeek.Monday)
        // 1936-01-01 Çarşamba → DayOfWeek.Wednesday
        if (gm.CurrentDate.DayOfWeek == DayOfWeek.Monday)
        {
            if (_currentWeekNumber == 0 || gm.CurrentDate.DayOfYear > 1) // oyun başı hariç
            {
                _currentWeekNumber++;
                ResetWeeklyAgenda();
                GD.Print($"[Agenda] Yeni hafta #{_currentWeekNumber} başladı — 6 slot yenilendi");
            }
        }

        // 3) Ay başı raporu (1. gün)
        if (gm.CurrentDate.Day == 1)
        {
            GenerateMonthlyReport(gm);
        }

        // 4) Bildirim kuyruğu işleme (auto-pause zaten yapıldıysa NEXT EVENT bekler)
        // Burada sadece kuyruk durumunu güncelleriz; NEXT EVENT UI ile kullanıcı işler.
    }

    private static void ResetWeeklyAgenda()
    {
        _weeklyAgenda.Clear();
        _slotsUsedThisWeek = 0;
        for (int i = 0; i < MaxSlotsPerWeek; i++)
        {
            _weeklyAgenda.Add(new AgendaSlot { Index = i, ActionId = "", IsLocked = false });
        }
    }

    // ===== Ajanda İşlemleri =====

    /// <summary>Slot'a aksiyon yerleştirir. Başarılıysa true.</summary>
    public static bool AddActionToSlot(int slotIndex, string actionId)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlotsPerWeek) return false;
        var slot = _weeklyAgenda[slotIndex];
        if (!string.IsNullOrEmpty(slot.ActionId) || slot.IsLocked) return false;
        if (_slotsUsedThisWeek >= MaxSlotsPerWeek) return false;
        if (_activeActions.Count >= MaxConcurrentActions) return false;

        var def = _actionDefs.Find(a => a.Id == actionId);
        if (def == null) return false;

        // PP kontrolü
        if (GameManager.IsAvailable && GameManager.Instance.PlayerCountryData != null)
        {
            if (GameManager.Instance.PlayerCountryData.PoliticalPower < def.CostPP)
            {
                GD.Print($"[Agenda] Yetersiz PP: {def.CostPP} gerekli");
                return false;
            }
        }

        // Gereksinimler kontrolü
        if (!CheckRequirements(def)) return false;

        // Slot'a yerleştir
        slot.ActionId = actionId;
        slot.IsLocked = true;
        _slotsUsedThisWeek++;

        // Aktif aksiyona ekle
        var active = new ActiveAgendaAction
        {
            ActionId = def.Id,
            ActionName = def.Name,
            RemainingDays = def.DurationDays,
            TotalDays = def.DurationDays,
            Effects = new Dictionary<string, object>(def.Effects)
        };
        _activeActions.Add(active);

        // PP kes
        if (GameManager.IsAvailable && GameManager.Instance.PlayerCountryData != null)
        {
            GameManager.Instance.PlayerCountryData.PoliticalPower -= def.CostPP;
        }

        GD.Print($"[Agenda] Slot {slotIndex} → {def.Name} (süre: {def.DurationDays} gün, maliyet: {def.CostPP} PP)");
        return true;
    }

    private static bool CheckRequirements(AgendaActionDef def)
    {
        if (!GameManager.IsAvailable || GameManager.Instance.PlayerCountryData == null) return true;
        var data = GameManager.Instance.PlayerCountryData;

        foreach (var req in def.Requirements)
        {
            double numVal = 0;
            if (req.Value is JsonElement je)
                numVal = je.ValueKind == JsonValueKind.Number ? je.GetDouble() : 0;
            else
                numVal = Convert.ToDouble(req.Value);

            switch (req.Key)
            {
                case "MinWarSupport":
                    if (data.WarSupportPercent < numVal) return false;
                    break;
                case "MinStability":
                    if (data.StabilityPercent < numVal) return false;
                    break;
                case "HasAllies":
                    break;
            }
        }
        return true;
    }

    private static void ApplyActionEffects(ActiveAgendaAction action)
    {
        if (!GameManager.IsAvailable || GameManager.Instance.PlayerCountryData == null) return;
        var data = GameManager.Instance.PlayerCountryData;

        foreach (var eff in action.Effects)
        {
            // JSON deserialize ettiğinde değerler JsonElement olabilir — güvenli dönüşüm
            double val = 0;
            if (eff.Value is JsonElement je)
            {
                val = je.ValueKind == JsonValueKind.Number ? je.GetDouble() : 0;
            }
            else
            {
                val = Convert.ToDouble(eff.Value);
            }

            switch (eff.Key)
            {
                case "Stability":
                    data.StabilityPercent = Math.Clamp(data.StabilityPercent + val, 0, 100);
                    break;
                case "WarSupport":
                    data.WarSupportPercent = Math.Clamp(data.WarSupportPercent + val, 0, 100);
                    break;
                case "PoliticalPower":
                    data.PoliticalPower += val;
                    break;
                case "MinisterLoyaltyBonus":
                    // M21.5: Kabine toplantısı — atanan bakanların sadakasına bonus uygular
                    CabinetSystem.ApplyMinisterLoyaltyBonus(GameManager.Instance.PlayerCountryId, (int)val);
                    break;
                default:
                    // Diğer efektler (ResearchSpeedBonus, AdminEfficiencyBonus vb.) ilgili sistemlerde ele alınacak
                    GD.Print($"[Agenda] Etki (sistem entegrasyonu bekliyor): {eff.Key} = {val}");
                    break;
            }
        }

        // M23.2: Kampanya aksiyonu tamamlandı → oy tahmini bonusu ve vaat kaydı
        if (GameManager.IsAvailable && GameManager.Instance.PlayerCountryData != null
            && action.Effects.TryGetValue("CampaignActionId", out var campaignIdObj))
        {
            string actionId = campaignIdObj?.ToString() ?? "";
            string actionName = action.Effects.TryGetValue("CampaignActionName", out var nameObj) ? nameObj?.ToString() ?? actionId : actionId;
            double voteBonus = ToDouble(action.Effects.TryGetValue("CampaignVoteBonus", out var vbObj) ? vbObj : 0);
            int durationDays = ToInt(action.Effects.TryGetValue("CampaignDurationDays", out var ddObj) ? ddObj : 0);
            bool isPromise = actionId == "promise";
            ElectionCampaignSystem.RecordCampaignAction(GameManager.Instance.PlayerCountryId, actionId, actionName, GameManager.Instance.TotalDays, durationDays, voteBonus, isPromise);
        }
    }

    private static double ToDouble(object v)
    {
        if (v == null) return 0;
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetDouble();
        try { return Convert.ToDouble(v); } catch { return 0; }
    }

    private static int ToInt(object v)
    {
        return (int)ToDouble(v);
    }

    // ===== Aylık Rapor =====

    private static void GenerateMonthlyReport(GameManager gm)
    {
        if (!GameManager.IsAvailable || GameManager.Instance.PlayerCountryData == null) return;
        var data = GameManager.Instance.PlayerCountryData;

        var report = new MonthlyReportData
        {
            Date = gm.CurrentDate,
            StatChanges = new Dictionary<string, long>
            {
                ["Population"] = data.Population,
                ["GDP"] = data.EstimatedGDP,
                ["Manpower"] = data.ManpowerPool,
                ["CivFactories"] = data.CivFactories,
                ["MilFactories"] = data.MilFactories,
                ["Dockyards"] = data.Dockyards,
                ["Stability"] = (long)data.StabilityPercent,
                ["WarSupport"] = (long)data.WarSupportPercent,
                ["PoliticalPower"] = (long)data.PoliticalPower
            },
            Warnings = new List<string>(),
            Decisions = new List<string>()
        };

        // Uyarılar
        if (data.StabilityPercent < 30) report.Warnings.Add($"Düşük istikrar: %{data.StabilityPercent:F0}");
        if (data.WarSupportPercent < 20) report.Warnings.Add($"Düşük savaş desteği: %{data.WarSupportPercent:F0}");
        if (data.PoliticalPower < 10) report.Warnings.Add($"Düşük Politika Gücü: {data.PoliticalPower:F0} PP");
        if (data.ManpowerPool < 10000) report.Warnings.Add($"Kritik asker havuzu: {data.ManpowerPool:N0}");

        // Aktif savaşlar
        var wars = WarSystem.ActiveWars;
        if (wars.Count > 0)
        {
            report.Decisions.Add($"Aktif savaş: {wars.Count} ({string.Join(", ", wars.Select(w => w.DefenderName))})");
        }

        _lastMonthlyReport = report;
        _monthlyReportPending = true;

        // Otomatik duraklama
        if (_speedSettings?.MonthlyReport?.AutoPause == true && GameManager.IsAvailable && GameManager.Instance.Speed > 0)
        {
            RequestAutoPause("Aylık rapor hazırlandı");
        }

        GD.Print($"[MonthlyReport] {gm.CurrentDate:MMMM yyyy} raporu oluşturuldu. Uyarı: {report.Warnings.Count}, Karar: {report.Decisions.Count}");
    }

    public static void AcknowledgeMonthlyReport()
    {
        _monthlyReportPending = false;
        ClearAutoPause();
        GD.Print("[MonthlyReport] Rapor onaylandı, oyun devam ediyor");
    }

    // ===== Otomatik Duraklama =====

    public static void RequestAutoPause(string reason)
    {
        if (!GameManager.IsAvailable) return;
        if (GameManager.Instance.Speed == 0) return;

        _autoPauseRequested = true;
        _autoPauseReason = reason;
        GameManager.Instance.SetSpeed(0);
        GD.Print($"[TimeTempo] OTOMATİK DURAKLAMA: {reason}");
    }

    public static void ClearAutoPause()
    {
        _autoPauseRequested = false;
        _autoPauseReason = "";
    }

    // ===== Önemli Bildirim Kuyruğu (NEXT EVENT) =====

    /// <summary>Önemli bir olay bildirimi kuyruğa ekler ve otomatik duraklatır.</summary>
    public static void QueueImportantNotification(string type, string title, string description, Action onConfirm = null)
    {
        _notificationQueue.Enqueue(new NotificationEvent
        {
            Type = type,
            Title = title,
            Description = description,
            TriggerDate = GameManager.IsAvailable ? GameManager.Instance.CurrentDate : DateTime.Now,
            OnConfirm = onConfirm
        });

        // Auto-pause (eğer event auto-pause açıksa)
        if (_speedSettings?.AutoPause?.OnEvent == true && GameManager.IsAvailable && GameManager.Instance.Speed > 0)
        {
            RequestAutoPause($"Önemli olay: {title}");
        }

        GD.Print($"[TimeTempo] Bildirim kuyruğa eklendi: {title} (tip: {type})");
    }

    public static void ProcessNextNotification()
    {
        if (_notificationQueue.Count == 0) return;
        var evt = _notificationQueue.Dequeue();
        evt.OnConfirm?.Invoke();
        GD.Print($"[TimeTempo] NEXT EVENT işlendi: {evt.Title}");
    }

    public static NotificationEvent PeekNextNotification()
    {
        return _notificationQueue.Count > 0 ? _notificationQueue.Peek() : null;
    }

    // ===== Save/Load =====

    public static TimeTempoSave GetSaveData()
    {
        var save = new TimeTempoSave
        {
            CurrentSpeedLevel = GameManager.IsAvailable ? GameManager.Instance.Speed : 1,
            CurrentWeekNumber = _currentWeekNumber,
            SlotsUsedThisWeek = _slotsUsedThisWeek,
            WeeklyAgenda = _weeklyAgenda.Select(s => new AgendaSlotSave
            {
                Index = s.Index,
                ActionId = s.ActionId,
                IsLocked = s.IsLocked
            }).ToArray(),
            ActiveActions = _activeActions.Select(a => new ActiveAgendaActionSave
            {
                ActionId = a.ActionId,
                ActionName = a.ActionName,
                RemainingDays = a.RemainingDays,
                TotalDays = a.TotalDays,
                Effects = new Dictionary<string, object>(a.Effects)
            }).ToArray(),
            Notifications = _notificationQueue.Select(n => new NotificationEventSave
            {
                Type = n.Type,
                Title = n.Title,
                Description = n.Description,
                TriggerDateTicks = n.TriggerDate.Ticks
            }).ToArray(),
            MonthlyReportPending = _monthlyReportPending,
            LastMonthlyReport = _lastMonthlyReport != null ? new MonthlyReportDataSave
            {
                DateTicks = _lastMonthlyReport.Date.Ticks,
                StatChanges = new Dictionary<string, long>(_lastMonthlyReport.StatChanges),
                Warnings = new List<string>(_lastMonthlyReport.Warnings),
                Decisions = new List<string>(_lastMonthlyReport.Decisions)
            } : null,
            AutoPauseRequested = _autoPauseRequested,
            AutoPauseReason = _autoPauseReason
        };
        return save;
    }

    public static void LoadSaveData(TimeTempoSave data)
    {
        if (data == null) return;

        _currentWeekNumber = data.CurrentWeekNumber;
        _slotsUsedThisWeek = data.SlotsUsedThisWeek;

        _weeklyAgenda.Clear();
        foreach (var s in data.WeeklyAgenda)
        {
            _weeklyAgenda.Add(new AgendaSlot
            {
                Index = s.Index,
                ActionId = s.ActionId,
                IsLocked = s.IsLocked
            });
        }

        _activeActions.Clear();
        foreach (var a in data.ActiveActions)
        {
            _activeActions.Add(new ActiveAgendaAction
            {
                ActionId = a.ActionId,
                ActionName = a.ActionName,
                RemainingDays = a.RemainingDays,
                TotalDays = a.TotalDays,
                Effects = new Dictionary<string, object>(a.Effects)
            });
        }

        _monthlyReportPending = data.MonthlyReportPending;

        if (data.LastMonthlyReport != null)
        {
            _lastMonthlyReport = new MonthlyReportData
            {
                Date = new DateTime(data.LastMonthlyReport.DateTicks),
                StatChanges = new Dictionary<string, long>(data.LastMonthlyReport.StatChanges),
                Warnings = new List<string>(data.LastMonthlyReport.Warnings),
                Decisions = new List<string>(data.LastMonthlyReport.Decisions)
            };
        }

        _autoPauseRequested = data.AutoPauseRequested;
        _autoPauseReason = data.AutoPauseReason;

        // M38.1: Beklenen bildirim kuyruğunu geri yükle (OnConfirm delegate'i serileştirilemez, null kalır)
        _notificationQueue.Clear();
        if (data.Notifications != null)
        {
            foreach (var n in data.Notifications)
            {
                _notificationQueue.Enqueue(new NotificationEvent
                {
                    Type = n.Type,
                    Title = n.Title,
                    Description = n.Description,
                    TriggerDate = new DateTime(n.TriggerDateTicks),
                    OnConfirm = null
                });
            }
        }

        // Hız GameManager tarafından ApplyLoadedRuntime ile ayarlanır
        GD.Print($"[TimeTempo] LoadSaveData: Week={_currentWeekNumber}, ActiveActions={_activeActions.Count}, MonthlyPending={_monthlyReportPending}");
    }

    public static void ResetForNewGame()
    {
        _currentWeekNumber = 0;
        _slotsUsedThisWeek = 0;
        _weeklyAgenda.Clear();
        _activeActions.Clear();
        _monthlyReportPending = false;
        _lastMonthlyReport = null;
        _autoPauseRequested = false;
        _autoPauseReason = "";
        _notificationQueue.Clear();
    }
}