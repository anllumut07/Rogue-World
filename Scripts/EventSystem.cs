using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Olay motoru (M10.1): olayları yükler (aktif senaryonun events.json dosyası),
/// tetikleyicileri kontrol eder (tarih, focus tamamlanma, savaş başlangıcı, random),
/// olayları ateşler, EventPopup UI üzerinden seçenekleri sunar, efektleri uygular.
/// Statik sınıf; GameManager günlük tick'inde CheckTriggers() çağrılır.
/// </summary>
public static class EventSystem
{
    // M17.1: yol aktif senaryodan; senaryo değişince ResetForScenario cache'i temizler
    private static string CurrentPath => ScenarioSystem.ActiveEventsPath;

    // ===== Veri modelleri (JSON'dan field-tabanlı yüklenir) =====

    public class GameEvent
    {
        public string Id = "?";
        public string Name = "?";
        public string Description = "";
        public EventTrigger Trigger = new();
        public List<EventOption> Options = new();
        public bool FireOnlyOnce = true;
        public bool IsHistorical = false;
    }

    public class EventTrigger
    {
        public string Type = "Date";           // Date, FocusCompleted, WarStarted, Random
        public string Date = "";               // "1938-03-12" (Date)
        public string Country = "";            // hedef ülke (ISO/HOI4 kodu)
        public string FocusId = "";            // FocusCompleted için
        public string WarAttacker = "";        // WarStarted için
        public string WarDefender = "";        // WarStarted için
        public float ChancePerDay = 0f;        // Random için (0-1)
    }

    public class EventOption
    {
        public string Label = "?";
        public EventEffects Effects = new();
    }

    public class EventEffects
    {
        // Temel değerler (CountryData alanları)
        public double PoliticalPower = 0;
        public double Stability = 0;
        public double WarSupport = 0;
        public int CivFactories = 0;
        public int MilFactories = 0;
        public int Dockyards = 0;
        public int ResearchSlots = 0;
        public long Manpower = 0;
        public int Oil = 0;
        public int Rubber = 0;
        public int Steel = 0;
        public int Aluminum = 0;
        public int Tungsten = 0;
        public int Chromium = 0;
        public double WorldTension = 0;

        // Modifikatörler (research_speed vb.)
        public Dictionary<string, float> Modifiers = new();

        // Özel olay etkileri (uygulama sırasında özel işlemler)
        public string TriggerEvent = "";              // başka bir olayı tetikle
        public string DeclareWar = "";                // hedef ülkeye savaş ilanı
        public bool DeclareWarAllies = false;         // hedefin müttefiklerine de savaş
        public string AnnexCountry = "";              // ülkeyi ilhak et
        public WarGoalCostReduction WarGoalCostReduction = null; // war goal PP indirimi
        public string GrantCasusBelliTarget = "";     // M17A: savaş ilanından önce gerekçe ver (tarihî olay)
        public string GrantCasusBelliType = "historical_event"; // M17A: gerekçe türü (varsayılan: tarihî)
    }

    public class WarGoalCostReduction
    {
        public string Target = "";
        public int ReductionPercent = 0; // 0-100
    }

    // Ülke bazlı olay durumu
    public class CountryEventState
    {
        public string CountryId = "";
        public HashSet<string> FiredEventIds = new();     // ateşlenen olaylar (FireOnlyOnce)
        public HashSet<string> CompletedEventIds = new(); // tamamlanan olaylar
    }

    // ===== Durum =====
    private static bool _loaded;
    private static readonly List<GameEvent> _allEvents = new();
    private static readonly Dictionary<string, GameEvent> _eventsById = new();
    private static readonly Dictionary<string, CountryEventState> _states = new();
    private static readonly List<PendingEvent> _pendingEvents = new(); // bekleyen olaylar (UI'ya gönderilmeden önce)
    private static bool _autoChooseTest = false; // --event-test / --event-auto: popup yerine ilk seçenek

    private static GameManager GM => GameManager.Instance;

    // ===== Yükleme =====

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!FileAccess.FileExists(CurrentPath))
        {
            GD.PrintErr($"HATA: {CurrentPath} bulunamadı!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var list = JsonSerializer.Deserialize<List<GameEvent>>(FileAccess.GetFileAsString(CurrentPath), options);
            if (list == null)
            {
                GD.PrintErr("HATA: events.json boş!");
                return;
            }

            foreach (var e in list)
            {
                if (string.IsNullOrEmpty(e.Id)) continue;
                _allEvents.Add(e);
                _eventsById[e.Id] = e;
            }

            GD.Print($"EventSystem: {_allEvents.Count} olay yüklendi (senaryo: {ScenarioSystem.ActiveId})");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"HATA: {CurrentPath} okunamadı: {ex.Message}");
        }
    }

    // M17.1: Senaryo değişince cache'i temizle
    public static void ResetForScenario()
    {
        _allEvents.Clear();
        _eventsById.Clear();
        _states.Clear();
        _pendingEvents.Clear();
        _loaded = false;
    }

    // M17.1: Senaryo verisi yükle (ülke gerekmez) — LogLoadCountsForAll için
    public static void LoadScenarioData()
    {
        EnsureLoaded();
    }

    // ===== Başlatma / Sıfırlama =====

    public static void Initialize(CountryData playerData)
    {
        EnsureLoaded();
        _states.Clear();
        _pendingEvents.Clear();

        if (playerData == null) return;

        var st = GetOrCreateState(playerData.Id);
        GD.Print($"EventSystem: {playerData.Name} ({playerData.Id}) için hazır");
    }

    public static CountryEventState GetOrCreateState(string countryId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(countryId)) return null;
        if (_states.TryGetValue(countryId, out var st)) return st;

        st = new CountryEventState { CountryId = countryId };
        _states[countryId] = st;
        return st;
    }

    // ===== Günlük tick =====

    public static void DailyTick()
    {
        if (!GM.HasPlayer) return;
        var st = GetOrCreateState(GM.PlayerCountryId);
        if (st == null) return;

        // 1) Tetikleyicileri kontrol et
        CheckTriggers(st);

        // 2) Bekleyen olay varsa popup aç (yoksa popup zaten açıksa bekle)
        if (_pendingEvents.Count > 0 && !EventPopup.IsOpen)
        {
            var next = _pendingEvents[0];
            _pendingEvents.RemoveAt(0);
            FireEvent(next);
        }
    }

    // Tüm tetikleyici tiplerini kontrol et
    private static void CheckTriggers(CountryEventState st)
    {
        foreach (var evt in _allEvents)
        {
            // Sadece oyuncu ülkesine ait veya genel (Country boş) olaylar
            if (!string.IsNullOrEmpty(evt.Trigger.Country))
            {
                string evtCountry = NormalizeCountryId(evt.Trigger.Country);
                string playerCountry = NormalizeCountryId(GM.PlayerCountryId);
                if (evtCountry != playerCountry) continue;
            }

            // Zaten ateşlendi mi? (FireOnlyOnce)
            if (evt.FireOnlyOnce && st.FiredEventIds.Contains(evt.Id)) continue;

            bool shouldFire = false;

            switch (evt.Trigger.Type)
            {
                case "Date":
                    shouldFire = CheckDateTrigger(evt, st);
                    break;
                case "FocusCompleted":
                    shouldFire = CheckFocusCompletedTrigger(evt, st);
                    break;
                case "WarStarted":
                    shouldFire = CheckWarStartedTrigger(evt);
                    break;
                case "Random":
                    shouldFire = CheckRandomTrigger(evt);
                    break;
            }

            if (shouldFire)
            {
                QueueEvent(st, evt);
            }
        }
    }

    private static bool CheckDateTrigger(GameEvent evt, CountryEventState st)
    {
        if (string.IsNullOrEmpty(evt.Trigger.Date)) return false;
        if (!DateTime.TryParse(evt.Trigger.Date, out var triggerDate)) return false;

        // Oyun tarihini kontrol et (gün hassasiyeti)
        return GM.CurrentDate.Date == triggerDate.Date;
    }

    private static bool CheckFocusCompletedTrigger(GameEvent evt, CountryEventState st)
    {
        if (string.IsNullOrEmpty(evt.Trigger.FocusId)) return false;

        // Odak tamamlanma durumu FocusSystem'de tutulur (odak id'leri)
        var focusSt = FocusSystem.GetOrCreateState(st.CountryId);
        return focusSt != null && focusSt.Completed.Contains(evt.Trigger.FocusId);
    }

    private static bool CheckWarStartedTrigger(GameEvent evt)
    {
        if (string.IsNullOrEmpty(evt.Trigger.WarAttacker) || string.IsNullOrEmpty(evt.Trigger.WarDefender)) return false;

        // Oyuncu savaş tarafı mı?
        var wars = WarSystem.ActiveWars;
        foreach (var war in wars)
        {
            string att = NormalizeCountryId(war.AttackerId);
            string def = NormalizeCountryId(war.DefenderId);
            string trigAtt = NormalizeCountryId(evt.Trigger.WarAttacker);
            string trigDef = NormalizeCountryId(evt.Trigger.WarDefender);

            if ((att == trigAtt && def == trigDef) || (att == trigDef && def == trigAtt))
                return true;
        }
        return false;
    }

    private static bool CheckRandomTrigger(GameEvent evt)
    {
        if (evt.Trigger.ChancePerDay <= 0) return false;
        return DeterministicRNG.NextDouble() < evt.Trigger.ChancePerDay;
    }

    // Olayı kuyruğa ekle
    private static void QueueEvent(CountryEventState st, GameEvent evt)
    {
        st.FiredEventIds.Add(evt.Id);
        _pendingEvents.Add(new PendingEvent { Event = evt, CountryId = st.CountryId });
        GD.Print($"[Event] Tetiklendi: {evt.Name} ({evt.Id}) — kuyruğa eklendi");
    }

    // Olayı gerçekten ateşle (UI popup aç)
    private static void FireEvent(PendingEvent pending)
    {
        var evt = pending.Event;
        GD.Print($"[Event] ATEŞLENİYOR: {evt.Name} ({evt.Id}) — {evt.Options.Count} seçenek");

        // Oyunu duraklat (auto-pause — eski hız kaydedilir)
        GM?.PauseForEvent();

        // Test kancası (--event-test / --event-auto): popup yerine ilk seçenek
        // otomatik uygulanır. Yalnızca headless test arg'ıyla etkin.
        if (_autoChooseTest)
        {
            GD.Print($"[Event][Test] Auto-choose: ilk seçenek uygulanıyor (popup açılmadan) — {evt.Options[0].Label}");
            OnEventOptionChosen(evt.Id, 0);
            return;
        }

        // EventPopup aç
        EventPopup.OpenAndShow(evt, OnEventOptionChosen);
    }

    // Kullanıcı bir seçenek seçtiğinde çağrılır
    private static void OnEventOptionChosen(string eventId, int optionIndex)
    {
        if (!_eventsById.TryGetValue(eventId, out var evt)) return;
        if (optionIndex < 0 || optionIndex >= evt.Options.Count) return;

        var option = evt.Options[optionIndex];
        ApplyEffects(option.Effects);

        var st = GetOrCreateState(GM.PlayerCountryId);
        if (st != null)
            st.CompletedEventIds.Add(eventId);

        GD.Print($"[Event] Seçim: {evt.Name} → {option.Label}");

        // M28.1: Olay sonucu gündeme tweetler üretilir (deterministik, kayıtlı)
        SocialMediaSystem.OnEventResolved(evt, optionIndex);

        // Oyunu devam ettir (kaydedilmiş hıza)
        GM?.ResumeFromEvent();
    }

    // Etikleri uygula (CountryData + özel etkiler)
    private static void ApplyEffects(EventEffects e)
    {
        var data = GM?.PlayerCountryData;
        if (data == null) return;

        // Temel değerler
        if (e.PoliticalPower != 0)
        {
            data.PoliticalPower += e.PoliticalPower;
            GD.Print($"[Event] PP {data.PoliticalPower:N1} ({e.PoliticalPower:+#;-#})");
        }
        if (e.Stability != 0)
        {
            data.StabilityPercent = Math.Clamp(data.StabilityPercent + e.Stability, 0, 100);
            GD.Print($"[Event] Stabilite %{data.StabilityPercent:N0} ({e.Stability:+#;-#})");
        }
        if (e.WarSupport != 0)
        {
            data.WarSupportPercent = Math.Clamp(data.WarSupportPercent + e.WarSupport, 0, 100);
            GD.Print($"[Event] War Support %{data.WarSupportPercent:N0} ({e.WarSupport:+#;-#})");
        }
        if (e.CivFactories != 0)
        {
            data.CivFactories += e.CivFactories;
            GD.Print($"[Event] Sivil fabrika {data.CivFactories} ({e.CivFactories:+#;-#})");
        }
        if (e.MilFactories != 0)
        {
            data.MilFactories += e.MilFactories;
            GD.Print($"[Event] Askeri fabrika {data.MilFactories} ({e.MilFactories:+#;-#})");
        }
        if (e.Dockyards != 0)
        {
            data.Dockyards += e.Dockyards;
            GD.Print($"[Event] Tersane {data.Dockyards} ({e.Dockyards:+#;-#})");
        }
        if (e.ResearchSlots != 0)
        {
            data.ResearchSlots = Math.Min(5, data.ResearchSlots + e.ResearchSlots);
            GD.Print($"[Event] Araştırma slotu {data.ResearchSlots}");
        }
        if (e.Manpower != 0)
        {
            data.ManpowerPool += e.Manpower;
            GD.Print($"[Event] Manpower {data.ManpowerPool:N0} ({FormatSigned(e.Manpower)})");
        }

        // Kaynaklar
        ApplyResource("Petrol", ref data.Oil, e.Oil);
        ApplyResource("Kauçuk", ref data.Rubber, e.Rubber);
        ApplyResource("Çelik", ref data.Steel, e.Steel);
        ApplyResource("Alüminyum", ref data.Aluminum, e.Aluminum);
        ApplyResource("Tungsten", ref data.Tungsten, e.Tungsten);
        ApplyResource("Krom", ref data.Chromium, e.Chromium);

        // World Tension
        if (e.WorldTension != 0)
        {
            DiplomacySystem.IncreaseTension((float)e.WorldTension);
        }

        // Modifikatörler
        if (e.Modifiers.Count > 0)
        {
            foreach (var kv in e.Modifiers)
                TechnologySystem.AddModifier(kv.Key, kv.Value);
            GD.Print($"[Event] Modifikatörler uygulandı — araştırma hızı {TechnologySystem.ResearchSpeedMult:F2}x");
        }

        // --- Özel olay etkileri ---

        // War goal PP indirimi — ÖNCE uygulanır ki aynı olaydaki DeclareWar
        // indirimli maliyetle ilan edilebilsin (ör. Barbarossa: -100% + savaş ilanı)
        if (e.WarGoalCostReduction != null)
        {
            var wg = e.WarGoalCostReduction;
            string targetId = NormalizeCountryId(wg.Target);
            WarSystem.SetWarGoalCostReduction(targetId, wg.ReductionPercent);
            GD.Print($"[Event] War goal PP indirimi: {targetId} için %{wg.ReductionPercent}");
        }

        // Savaş ilanı
        if (!string.IsNullOrEmpty(e.DeclareWar))
        {
            string targetId = NormalizeCountryId(e.DeclareWar);

            // M17A: Tarihi olaylar savaş ilanından ÖNCE gerekçe alır (casus belli kontrolü
            // WarSystem.DeclareWar içinde yapılır — gerekçe yoksa ilan reddedilir)
            if (!string.IsNullOrEmpty(e.GrantCasusBelliTarget))
            {
                string grantTarget = NormalizeCountryId(e.GrantCasusBelliTarget);
                CasusBelliSystem.GrantImmediateJustification(GM.PlayerCountryId, grantTarget, e.GrantCasusBelliType);
                GD.Print($"[Event] Casus belli verildi: {grantTarget} ({e.GrantCasusBelliType})");
            }
            else
            {
                // DeclareWar var ama GrantCasusBelliTarget yok — tarihi olay varsayılanı
                CasusBelliSystem.GrantImmediateJustification(GM.PlayerCountryId, targetId, "historical_event");
                GD.Print($"[Event] Casus belli verildi (varsayılan): {targetId} (historical_event)");
            }

            WarSystem.DeclareWar(targetId);
            if (e.DeclareWarAllies)
            {
                // Müttefiklerine de savaş (faksiyon üyeleri) — casus belli: savunma gerekçesi
                var faction = DiplomacySystem.GetFactionOf(targetId);
                if (faction != null)
                {
                    foreach (var memberId in faction.MemberIds)
                    {
                        if (memberId != targetId && !WarSystem.IsAtWarWith(memberId))
                        {
                            CasusBelliSystem.GrantImmediateJustification(GM.PlayerCountryId, memberId, "historical_event");
                            WarSystem.DeclareWar(memberId);
                        }
                    }
                }
            }
        }

        // Başka bir olayı tetikle (zincirleme olaylar)
        if (!string.IsNullOrEmpty(e.TriggerEvent))
        {
            if (_eventsById.TryGetValue(e.TriggerEvent, out var nextEvt))
            {
                var st = GetOrCreateState(GM.PlayerCountryId);
                if (st != null && !st.FiredEventIds.Contains(nextEvt.Id))
                {
                    QueueEvent(st, nextEvt);
                    GD.Print($"[Event] Zincirleme olay tetiklendi: {nextEvt.Id}");
                }
            }
            else
            {
                GD.PrintErr($"[Event] UYARI: Zincirleme olay bulunamadı: {e.TriggerEvent}");
            }
        }

        // Ülke ilhak et
        if (!string.IsNullOrEmpty(e.AnnexCountry))
        {
            string targetId = NormalizeCountryId(e.AnnexCountry);
            var target = CountryDatabase.Get(targetId);
            var player = CountryDatabase.Get(GM.PlayerCountryId);
            if (target != null && player != null)
            {
                // M44.4: kayıt düş — iller devredilmeden önce (bağımsız çıkarılabilir)
                ReleasableSystem.RecordSubject(target.Id, player.Id, false,
                    target.CivFactories, target.MilFactories, target.Dockyards);
                target.IsAnnexed = true;
                target.OwnerId = player.Id;
                player.CivFactories += target.CivFactories;
                player.MilFactories += target.MilFactories;
                player.Dockyards += target.Dockyards;
                target.CivFactories = target.MilFactories = target.Dockyards = 0;
                GD.Print($"[Event] {target.Name} ilhak edildi!");

                if (GameManager.IsAvailable)
                    GM.EmitSignal(GameManager.SignalName.CountryOwnershipChanged);
            }
        }

        // HUD güncelle
        if (GameManager.IsAvailable)
            GM.EmitSignal(GameManager.SignalName.PlayerCountryDataChanged);
    }

    private static void ApplyResource(string label, ref int field, int delta)
    {
        if (delta == 0) return;
        int before = field;
        field += delta;
        GD.Print($"[Event] {label} üretimi {before} → {field} ({delta:+#;-#})");
    }

    private static string FormatSigned(long value)
    {
        string sign = value >= 0 ? "+" : "-";
        long abs = Math.Abs(value);
        if (abs >= 1000) return $"{sign}{abs / 1000}k";
        return $"{sign}{abs}";
    }

    // HOI4 ↔ ISO A3 normalizasyonu (CountryDatabase.NormalizeId üzerinden)
    private static string NormalizeCountryId(string id) => CountryDatabase.NormalizeId(id);

    // ===== M12: Save/Load =====

    public static SaveSystem.EventSave GetSaveData()
    {
        return new SaveSystem.EventSave
        {
            States = _states.Values.Select(s => new SaveSystem.CountryEventStateSave
            {
                CountryId = s.CountryId,
                FiredEventIds = s.FiredEventIds.ToArray(),
                CompletedEventIds = s.CompletedEventIds.ToArray()
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.EventSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _states.Clear();
        if (data.States != null)
        {
            foreach (var s in data.States)
            {
                var state = new CountryEventState
                {
                    CountryId = s.CountryId,
                    FiredEventIds = new HashSet<string>(s.FiredEventIds ?? Array.Empty<string>()),
                    CompletedEventIds = new HashSet<string>(s.CompletedEventIds ?? Array.Empty<string>())
                };
                _states[state.CountryId] = state;
            }
        }
        // Bekleyen olayları temizle (yükleme sonrası popup açılmasın)
        _pendingEvents.Clear();
        GD.Print($"EventSystem: state yüklendi — Ülke state={_states.Count}");
    }

    // ===== Public API (UI / Test) =====

    /// <summary>
    /// Dış sistemlerden (FocusSystem odak efektleri vb.) olay tetikleme — M10 bağlantısı.
    /// Olay yoksa veya FireOnlyOnce ise ve zaten ateşlendiyse false döner.
    /// Olay kuyruğa girer; popup bir sonraki DailyTick'te açılır.
    /// </summary>
    public static bool TriggerEventNow(string eventId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(eventId)) return false;
        if (!_eventsById.TryGetValue(eventId, out var evt)) return false;

        if (GM == null || !GM.HasPlayer) return false;
        var st = GetOrCreateState(GM.PlayerCountryId);
        if (st == null) return false;
        if (evt.FireOnlyOnce && st.FiredEventIds.Contains(eventId)) return false;

        QueueEvent(st, evt);
        return true;
    }

    // --event-test / --event-auto arg'ları: normal oyunda asla etkinleşmez
    public static void EnableAutoChooseTest() => _autoChooseTest = true;

    public static IReadOnlyList<GameEvent> AllEvents => _allEvents;

    public static GameEvent GetEvent(string id) => _eventsById.GetValueOrDefault(id);

    // --- Yardımcı sınıf ---
    private class PendingEvent
    {
        public GameEvent Event;
        public string CountryId;
    }
}