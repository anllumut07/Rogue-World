using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Ulusal odak sistemi (M9): ülkeye özel odak ağaçlarını yükler (GER/USA/SOV/ENG/
/// FRA/JPN/ITA + GENERIC), odak seçimini ve günlük ilerlemeyi yönetir, tamamlanan
/// odakların efektlerini uygular (fabrika, PP, stabilite, WS, manpower, kaynak,
/// research slot, tension, research_speed modifikatörü).
/// Efektler yalnızca mevcut sistemlerdeki değerleri değiştirir.
/// Statik sınıf; GameManager günlük tick'inde çağrılır.
/// </summary>
public static class FocusSystem
{
    // M17.1: yol aktif senaryodan; senaryo değişince ResetForScenario cache'i temizler
    private static string CurrentDir => ScenarioSystem.ActiveFocusTreesDir;

    // ===== Veri modelleri (JSON'dan field-tabanlı yüklenir → IncludeFields=true şart) =====

    // Odak tanımı
    public class Focus
    {
        public string Id = "?";
        public string Name = "?";
        public string Description = "";
        public int X = 0;               // sütun (UI grid)
        public int Y = 0;               // satır (0 = en üst)
        public int Cost = 70;           // gün cinsinden süre
        public int AiPriority = 0;      // AI seçim temel önceliği (9.4)
        public List<string> Prerequisites = new();       // önkoşul odak id'leri
        public List<string> MutuallyExclusive = new();   // birbirini kilitleyen odak id'leri
        public FocusEffects Effects = new();
    }

    // Odak efektleri — mevcut sistemler + M10 olay/war goal bağlantıları
    public class FocusEffects
    {
        public double PoliticalPower = 0;  // doğrudan PP ekle
        public double Stability = 0;       // yüzde puanı (0-100 arası sıkıştırılır)
        public double WarSupport = 0;      // yüzde puanı (0-100 arası sıkıştırılır)
        public int CivFactories = 0;
        public int MilFactories = 0;
        public int Dockyards = 0;
        public int ResearchSlots = 0;     // max 5 (TechnologySystem sınırı)
        public long Manpower = 0;          // manpower havuzuna ekle
        public int Oil = 0;                // günlük kaynak üretimine ekle
        public int Rubber = 0;
        public int Steel = 0;
        public int Aluminum = 0;
        public int Tungsten = 0;
        public int Chromium = 0;
        public double WorldTension = 0;    // world tension değişimi (negatif = azalt)
        public Dictionary<string, float> Modifiers = new(); // research_speed vb. → TechnologySystem

        // M10: Odak tamamlandığında başka bir olayı tetikle (zincirleme olaylar)
        public string TriggerEvent = "";              // EventSystem olay id'si

        // M10: War goal — hedef ülkeye savaş ilanı PP maliyeti indirimi
        public string WarGoalTarget = "";             // hedef ülke id (HOI4/ISO)
        public int WarGoalReductionPercent = 0;       // 0-100 arası indirim
    }

    // Odak ağacı
    public class FocusTree
    {
        public string Id = "?";
        public string Name = "?";
        public List<Focus> Focuses = new();

        // Yükleme sonrası doldurulan hızlı erişim (JSON'da yok)
        public Dictionary<string, Focus> ById = new();
    }

    // Ülke bazlı odak durumu (oyuncu + ileride AI ülkeleri)
    public class CountryFocusState
    {
        public string CountryId = "";
        public FocusTree Tree;
        public HashSet<string> Completed = new(); // tamamlanan odak id'leri
        public Focus Active = null;               // yürütülen odak (tek seferde bir tane)
        public int ActiveDays = 0;               // aktif odağa harcanan gün
        // Test kancası (--focus-test): AI ağaç biterse tekrar arama
        public bool AiExhausted = false;
    }

    // ===== Durum =====
    private static bool _loaded;
    private static readonly Dictionary<string, FocusTree> _trees = new();
    private static readonly Dictionary<string, CountryFocusState> _states = new();
    private static bool _autoPickTest = false; // --focus-test: AI mantığıyla otomatik seçim

    private static GameManager GM => GameManager.Instance;

    // ===== Yükleme =====

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        int total = 0;
        var summary = new List<string>();
        var files = ListTreeFiles(CurrentDir);
        foreach (var (treeId, path) in files)
        {
            var tree = LoadTree(treeId, path);
            if (tree == null) continue;
            _trees[treeId] = tree;
            total += tree.Focuses.Count;
            summary.Add($"{treeId}={tree.Focuses.Count}");
        }

        if (_trees.Count == 0)
        {
            // M17.1: senaryo odak ağaçları bilinçli olarak boş olabilir (modern_world placeholder)
            GD.Print($"FocusSystem: {CurrentDir} içinde 0 odak ağacı (senaryo: {ScenarioSystem.ActiveId}) — odak sistemi pasif");
        }
        else
        {
            GD.Print($"FocusSystem hazır: {_trees.Count} ağaç, {total} odak ({string.Join(", ", summary)}) — senaryo: {ScenarioSystem.ActiveId}");
        }
    }

    // Dizindeki *.json dosyalarını (ad, tam yol) listeler; dizin yoksa boş liste döner
    private static List<(string, string)> ListTreeFiles(string dirPath)
    {
        var result = new List<(string, string)>();
        var dir = DirAccess.Open(dirPath);
        if (dir == null)
        {
            GD.PrintErr($"HATA: Odak ağacı dizini bulunamadı: {dirPath}");
            return result;
        }

        dir.ListDirBegin();
        string fileName;
        while ((fileName = dir.GetNext()) != "")
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                result.Add((fileName.Substring(0, fileName.Length - ".json".Length), $"{dirPath}/{fileName}"));
        }
        dir.ListDirEnd();
        return result;
    }

    // Tek ağaç dosyası yükle + doğrula (önkoşul/mutex id'leri ağaçta var mı, tekrarlı id var mı)
    private static FocusTree LoadTree(string treeId, string path)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"HATA: {path} bulunamadı!");
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var tree = JsonSerializer.Deserialize<FocusTree>(FileAccess.GetFileAsString(path), options);
            if (tree == null || tree.Focuses == null || tree.Focuses.Count == 0)
            {
                GD.PrintErr($"HATA: {path} boş veya geçersiz!");
                return null;
            }

            // Hızlı erişim sözlüğü + tekrarlı id kontrolü
            tree.ById.Clear();
            foreach (var f in tree.Focuses)
            {
                if (!tree.ById.TryAdd(f.Id, f))
                    GD.PrintErr($"[Focus] UYARI: {treeId} ağacında tekrarlanan odak id: {f.Id}");
            }

            // Önkoşul / mutex referans doğrulaması (sessiz hata olmasın)
            foreach (var f in tree.Focuses)
            {
                foreach (var p in f.Prerequisites)
                    if (!tree.ById.ContainsKey(p))
                        GD.PrintErr($"[Focus] HATA: {treeId}/{f.Id} bilinmeyen önkoşul: {p}");
                foreach (var m in f.MutuallyExclusive)
                    if (!tree.ById.ContainsKey(m))
                        GD.PrintErr($"[Focus] HATA: {treeId}/{f.Id} bilinmeyen mutex: {m}");
            }

            if (string.IsNullOrEmpty(tree.Id)) tree.Id = treeId;
            return tree;
        }
        catch (Exception e)
        {
            GD.PrintErr($"HATA: {path} okunamadı: {e.Message}");
            return null;
        }
    }

    // ===== Ülke → ağaç eşlemesi (9.2) =====

    // HOI4 kodu ve ISO A3 her iki yazımı da destekler (GER/DEU, SOV/RUS...).
    public static FocusTree GetTreeFor(string countryId)
    {
        EnsureLoaded();
        string key = countryId switch
        {
            "GER" or "DEU" => "GER",
            "USA" or "US" => "USA",
            "SOV" or "RUS" or "USSR" => "SOV",
            "ENG" or "GBR" or "UK" => "ENG",
            "FRA" => "FRA",
            "JPN" or "JAP" => "JPN",
            "ITA" => "ITA",
            _ => "GENERIC"
        };
        return _trees.GetValueOrDefault(key) ?? _trees.GetValueOrDefault("GENERIC");
    }

    // ===== Başlatma =====

    // Oyuncu ülkesi seçilince çağrılır (GameManager.SetPlayerCountry).
    // Tüm ülke durumlarını temizler (yeni oyun) ve oyuncu state'ini kurar.
    public static void Initialize(CountryData data)
    {
        EnsureLoaded();
        _states.Clear();

        if (data == null)
        {
            GD.Print("FocusSystem: veri yok — pasif");
            return;
        }

        // State anahtarı = GameManager'daki oyuncu ülke id'si (GER gibi ham id olabilir)
        string key = GM != null && !string.IsNullOrEmpty(GM.PlayerCountryId) ? GM.PlayerCountryId : data.Id;
        var st = GetOrCreateState(key);

        if (st.Tree == null)
            GD.Print($"FocusSystem: {data.Name} ({key}) için hazır — odak ağacı YOK (senaryo: {ScenarioSystem.ActiveId})");
        else
            GD.Print($"FocusSystem: {data.Name} ({key}) için hazır — ağaç: {st.Tree.Id} " +
                     $"\"{st.Tree.Name}\", {st.Tree.Focuses.Count} odak");
    }

    // M17.1: Senaryo değişince cache'i temizle
    public static void ResetForScenario()
    {
        _trees.Clear();
        _loaded = false;
    }

    // M17.1: Senaryo verisi yükle (ülke gerekmez) — LogLoadCountsForAll için
    public static void LoadScenarioData()
    {
        EnsureLoaded();
    }

    // Ülke durumu (yoksa oluşturulur — AI ülkeleri için 9.4)
    public static CountryFocusState GetOrCreateState(string countryId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(countryId)) return null;
        if (_states.TryGetValue(countryId, out var st)) return st;

        st = new CountryFocusState { CountryId = countryId, Tree = GetTreeFor(countryId) };
        _states[countryId] = st;
        return st;
    }

    // Oyuncunun durumu (ülke seçili değilse null)
    public static CountryFocusState PlayerState
    {
        get
        {
            if (GM == null || !GM.HasPlayer) return null;
            return GetOrCreateState(GM.PlayerCountryId);
        }
    }

    // ===== Durum sorguları =====

    public enum FocusStatus { Completed, InProgress, Available, MutexBlocked, Locked }

    public static FocusStatus GetStatusFor(CountryFocusState st, string focusId)
    {
        if (st == null || st.Tree == null) return FocusStatus.Locked;
        if (st.Completed.Contains(focusId)) return FocusStatus.Completed;
        if (st.Active != null && st.Active.Id == focusId) return FocusStatus.InProgress;

        var f = st.Tree.ById.GetValueOrDefault(focusId);
        if (f == null) return FocusStatus.Locked;

        // Mutex kardeşi tamamlandıysa kalıcı kilit
        foreach (var m in f.MutuallyExclusive)
            if (st.Completed.Contains(m)) return FocusStatus.MutexBlocked;

        // Önkoşullar tamam mı?
        foreach (var p in f.Prerequisites)
            if (!st.Completed.Contains(p)) return FocusStatus.Locked;

        return FocusStatus.Available;
    }

    // Oyuncu ağacındaki odak durumu (UI kısayolu)
    public static FocusStatus GetStatus(string focusId) => GetStatusFor(PlayerState, focusId);

    // ===== Oyuncu API (UI'dan) =====

    public static bool StartFocus(string focusId)
    {
        var st = PlayerState;
        if (st == null)
        {
            GD.Print("[Focus] Odak başlatılamadı: oyuncu ülkesi seçili değil");
            return false;
        }
        return StartFocusFor(st, focusId, isAi: false);
    }

    // Aktif odağı iptal et (ilerleme kaybolur — HOI4 kuralı)
    public static void CancelActiveFocus()
    {
        var st = PlayerState;
        if (st?.Active == null) return;

        GD.Print($"[Focus] Odak İPTAL: {st.Active.Name} ({st.ActiveDays}/{st.Active.Cost} gün ilerleme kaybedildi)");
        st.Active = null;
        st.ActiveDays = 0;
    }

    // Ortak başlatma (oyuncu + AI)
    private static bool StartFocusFor(CountryFocusState st, string focusId, bool isAi)
    {
        var status = GetStatusFor(st, focusId);
        if (status != FocusStatus.Available)
        {
            if (!isAi) GD.Print($"[Focus] Başlatılamadı: {focusId} (durum: {status})");
            return false;
        }

        if (st.Active != null)
        {
            if (!isAi) GD.Print($"[Focus] Zaten aktif odak var: {st.Active.Name} — önce bitir veya iptal et");
            return false;
        }

        var f = st.Tree.ById.GetValueOrDefault(focusId);
        if (f == null) return false;

        st.Active = f;
        st.ActiveDays = 0;
        GD.Print($"[Focus] Odak BAŞLADI: {f.Name} ({st.CountryId}, {f.Cost} gün) — {DescribeEffects(f)}");
        return true;
    }

    // ===== Günlük tick (9.3) =====

    public static void DailyTick()
    {
        var st = PlayerState;
        if (st == null) return;

        // Test kancası (--focus-test): aktif odak yoksa AI seçim mantığı seçer.
        // Yalnızca headless test arg'ıyla etkin — normal oyunda çalışmaz.
        // Ağaç tükenince (mutex zincirleri dahil) günlük tekrar arama/spam yok.
        if (_autoPickTest && st.Active == null && !st.AiExhausted)
        {
            if (AiPickFocus(st.CountryId) == null)
                st.AiExhausted = true;
        }

        if (st.Active == null) return;

        st.ActiveDays++;
        if (st.ActiveDays >= st.Active.Cost)
        {
            var f = st.Active;
            int gameDay = GM != null ? GM.TotalDays : 0;
            st.Completed.Add(f.Id);
            st.Active = null;
            st.ActiveDays = 0;

            ApplyFocusEffects(st, f);
            GD.Print($"[Focus] ✔ ODAK TAMAMLANDI: {f.Name} ({st.CountryId}, oyun günü {gameDay}) — {DescribeEffects(f)}");
        }
    }

    // ===== AI odak ilerlemesi (M11.4) =====
    // Oyuncu DIŞI tüm ülke state'lerini bir gün ilerletir; tamamlanan odakların
    // efektleri ApplyFocusEffects ile CountryData'ya yazılır (isPlayer=false
    // dalı: modifikatörler/olaylar oyuncu sistemine dokunmaz, log düşer).
    // CountryAI.DailyTick çağırır — her ülke O(1), 50 ülke ucuz.
    public static void TickAiStates()
    {
        if (GM == null) return;
        string playerId = GM.PlayerCountryId;

        foreach (var st in _states.Values)
        {
            if (st.CountryId == playerId || st.Active == null) continue;

            st.ActiveDays++;
            if (st.ActiveDays >= st.Active.Cost)
            {
                var f = st.Active;
                st.Completed.Add(f.Id);
                st.Active = null;
                st.ActiveDays = 0;

                ApplyFocusEffects(st, f);
                GD.Print($"[Focus][AI] ✔ {st.CountryId} odak tamamlandı: {f.Name} — {DescribeEffects(f)}");
            }
        }
    }

    // Efektleri gerçekten uygula ve her birini GD.Print ile logla.
    // Yalnızca mevcut sistemlerin alanlarına yazar.
    private static void ApplyFocusEffects(CountryFocusState st, Focus f)
    {
        var data = CountryDatabase.GetOrCreate(st.CountryId, "");
        if (data == null)
        {
            GD.PrintErr($"[Focus] UYARI: {st.CountryId} verisi yok — {f.Name} efektleri uygulanamadı");
            return;
        }

        var e = f.Effects;
        bool isPlayer = GM != null && st.CountryId == GM.PlayerCountryId;

        if (e.PoliticalPower != 0)
        {
            double before = data.PoliticalPower;
            data.PoliticalPower += e.PoliticalPower;
            GD.Print($"[Focus] {f.Name}: PP {before:N1} → {data.PoliticalPower:N1} ({e.PoliticalPower:+#;-#})");
        }

        if (e.Stability != 0)
        {
            double before = data.StabilityPercent;
            data.StabilityPercent = Math.Clamp(data.StabilityPercent + e.Stability, 0, 100);
            GD.Print($"[Focus] {f.Name}: Stabilite %{before:N0} → %{data.StabilityPercent:N0} ({e.Stability:+#;-#})");
        }

        if (e.WarSupport != 0)
        {
            double before = data.WarSupportPercent;
            data.WarSupportPercent = Math.Clamp(data.WarSupportPercent + e.WarSupport, 0, 100);
            GD.Print($"[Focus] {f.Name}: War Support %{before:N0} → %{data.WarSupportPercent:N0} ({e.WarSupport:+#;-#})");
        }

        if (e.CivFactories != 0)
        {
            int before = data.CivFactories;
            data.CivFactories += e.CivFactories;
            GD.Print($"[Focus] {f.Name}: Sivil fabrika {before} → {data.CivFactories} ({e.CivFactories:+#;-#})");
        }

        if (e.MilFactories != 0)
        {
            int before = data.MilFactories;
            data.MilFactories += e.MilFactories;
            GD.Print($"[Focus] {f.Name}: Askeri fabrika {before} → {data.MilFactories} ({e.MilFactories:+#;-#})");
        }

        if (e.Dockyards != 0)
        {
            int before = data.Dockyards;
            data.Dockyards += e.Dockyards;
            GD.Print($"[Focus] {f.Name}: Tersane {before} → {data.Dockyards} ({e.Dockyards:+#;-#})");
        }

        if (e.ResearchSlots != 0)
        {
            int before = data.ResearchSlots;
            data.ResearchSlots = Math.Min(5, data.ResearchSlots + e.ResearchSlots); // max 5 slot
            GD.Print($"[Focus] {f.Name}: Araştırma slotu {before} → {data.ResearchSlots}");
        }

        if (e.Manpower != 0)
        {
            long before = data.ManpowerPool;
            data.ManpowerPool += e.Manpower;
            GD.Print($"[Focus] {f.Name}: Manpower {before:N0} → {data.ManpowerPool:N0} ({FormatSigned(e.Manpower)})");
        }

        // Kaynaklar: CountryData alanları günlük üretim birimi — doğrudan üretimi artırır
        ApplyResource(f, "Petrol", ref data.Oil, e.Oil);
        ApplyResource(f, "Kauçuk", ref data.Rubber, e.Rubber);
        ApplyResource(f, "Çelik", ref data.Steel, e.Steel);
        ApplyResource(f, "Alüminyum", ref data.Aluminum, e.Aluminum);
        ApplyResource(f, "Tungsten", ref data.Tungsten, e.Tungsten);
        ApplyResource(f, "Krom", ref data.Chromium, e.Chromium);

        if (e.WorldTension != 0)
        {
            // DiplomacySystem kendi logunu yazar
            DiplomacySystem.IncreaseTension((float)e.WorldTension);
        }

        if (e.Modifiers.Count > 0)
        {
            // Modifikatörler (research_speed) TechnologySystem'de oyuncu için toplanır
            if (isPlayer)
            {
                foreach (var kv in e.Modifiers)
                    TechnologySystem.AddModifier(kv.Key, kv.Value);
                GD.Print($"[Focus] {f.Name}: Modifikatörler uygulandı — araştırma hızı {TechnologySystem.ResearchSpeedMult:F2}x");
            }
            else
            {
                GD.Print($"[Focus] {f.Name}: AI ülkesi — modifikatör oyuncu sistemine uygulanmadı (M11'de AI ekonomisi)");
            }
        }

        // --- M10: Odak → olay tetikleme (M9'dan ertelenen bağlantı) ---
        if (!string.IsNullOrEmpty(e.TriggerEvent))
        {
            if (isPlayer)
            {
                bool queued = EventSystem.TriggerEventNow(e.TriggerEvent);
                GD.Print(queued
                    ? $"[Focus] {f.Name}: Olay tetiklendi → {e.TriggerEvent}"
                    : $"[Focus] {f.Name}: Olay tetiklenemedi ({e.TriggerEvent} yok veya zaten ateşlendi)");
            }
            else
            {
                GD.Print($"[Focus] {f.Name}: AI ülkesi — olay ({e.TriggerEvent}) yalnızca oyuncu ülkesinde popup açar");
            }
        }

        // --- M10: War goal — hedefe savaş ilanı PP maliyeti indirimi (M9'dan ertelenen bağlantı) ---
        if (!string.IsNullOrEmpty(e.WarGoalTarget) && e.WarGoalReductionPercent > 0)
        {
            if (isPlayer)
            {
                WarSystem.SetWarGoalCostReduction(e.WarGoalTarget, e.WarGoalReductionPercent);
                GD.Print($"[Focus] {f.Name}: War goal — {e.WarGoalTarget} savaş ilanı maliyeti -%{e.WarGoalReductionPercent}");
            }
            else
            {
                GD.Print($"[Focus] {f.Name}: AI ülkesi war goal efekti (M11'de AI savaş ilanı)");
            }
        }
    }

    private static void ApplyResource(Focus f, string label, ref int field, int delta)
    {
        if (delta == 0) return;
        int before = field;
        field += delta;
        GD.Print($"[Focus] {f.Name}: {label} üretimi {before} → {field} ({delta:+#;-#})");
    }

    // ===== AI odak seçimi (9.4) =====

    // Priority scoring: temel AiPriority + ülke durumuna göre puanlama.
    // Skoru en yüksek, seçilebilir odaklarından birini seçip başlatır.
    public static string AiPickFocus(string countryId)
    {
        var st = GetOrCreateState(countryId);
        if (st == null || st.Tree == null) return null;
        if (st.Active != null) return st.Active.Id;

        var data = CountryDatabase.GetOrCreate(countryId, "");

        Focus best = null;
        double bestScore = double.MinValue;
        foreach (var f in st.Tree.Focuses)
        {
            if (GetStatusFor(st, f.Id) != FocusStatus.Available) continue;

            double score = f.AiPriority + ScoreEffectsForAi(f, data);
            if (score > bestScore)
            {
                bestScore = score;
                best = f;
            }
        }

        if (best == null)
        {
            GD.Print($"[Focus][AI] {countryId}: seçilebilir odak kalmadı");
            return null;
        }

        StartFocusFor(st, best.Id, isAi: true);
        GD.Print($"[Focus][AI] {data?.Name ?? countryId} odak SEÇTİ: {best.Name} (skor {bestScore:F1})");
        return best.Id;
    }

    // Ülke durumuna göre efekt puanlaması: az fabrikası olan ülke sanayi,
    // düşük stabilite barış odakları, faşizm ordu/savaş odakları seçer.
    private static double ScoreEffectsForAi(Focus f, CountryData data)
    {
        double s = 0;
        var e = f.Effects;
        string ideology = data?.Ideology ?? "Neutral";

        if (e.CivFactories > 0)
        {
            s += 20;
            if (data != null && data.CivFactories < 20) s += 10; // az sanayi → acil
        }
        if (e.MilFactories > 0) s += 14 + (ideology == "Fascism" ? 6 : 0);
        if (e.Dockyards > 0) s += 8;

        if (e.PoliticalPower > 0) s += 10;

        if (e.Stability > 0) s += (data != null && data.StabilityPercent < 40) ? 28 : 8;
        if (e.WarSupport > 0) s += ideology == "Fascism" ? 15 : 6;

        if (e.ResearchSlots > 0) s += 30;
        if (e.Manpower > 0) s += 10;

        if (e.Modifiers.GetValueOrDefault("research_speed") > 0) s += 18;
        s += 3 * (Math.Abs(e.Oil) + Math.Abs(e.Rubber) + Math.Abs(e.Steel)
                + Math.Abs(e.Aluminum) + Math.Abs(e.Tungsten) + Math.Abs(e.Chromium));

        // Demokrasiler gerilim artıran odaklardan kaçınır
        if (e.WorldTension > 0 && ideology == "Democracy") s -= 10;
        if (e.WorldTension < 0) s += 5;

        // Ucuz odaklar hafif öncelikli (eşitlikte önce kazanır)
        s -= f.Cost * 0.02;

        return s;
    }

    // ===== Test kancası (headless) =====

    // --focus-test arg'ı: normal oyunda asla etkinleşmez
    public static void EnableAutoPickTest() => _autoPickTest = true;

    // ===== UI yardımcıları =====

    // Efekt özeti (tooltip/panel için, İngilizce UI metni)
    public static string DescribeEffects(Focus f)
    {
        var e = f.Effects;
        var parts = new List<string>();

        if (e.PoliticalPower != 0) parts.Add($"{e.PoliticalPower:+#;-#} PP");
        if (e.Stability != 0) parts.Add($"{e.Stability:+#;-#}% Stability");
        if (e.WarSupport != 0) parts.Add($"{e.WarSupport:+#;-#}% War Support");
        if (e.CivFactories != 0) parts.Add($"{e.CivFactories:+#;-#} Civilian Factories");
        if (e.MilFactories != 0) parts.Add($"{e.MilFactories:+#;-#} Military Factories");
        if (e.Dockyards != 0) parts.Add($"{e.Dockyards:+#;-#} Dockyards");
        if (e.ResearchSlots != 0) parts.Add($"{e.ResearchSlots:+#;-#} Research Slot");
        if (e.Manpower != 0) parts.Add($"{FormatSigned(e.Manpower)} Manpower");
        if (e.Oil != 0) parts.Add($"{e.Oil:+#;-#} Oil");
        if (e.Rubber != 0) parts.Add($"{e.Rubber:+#;-#} Rubber");
        if (e.Steel != 0) parts.Add($"{e.Steel:+#;-#} Steel");
        if (e.Aluminum != 0) parts.Add($"{e.Aluminum:+#;-#} Aluminum");
        if (e.Tungsten != 0) parts.Add($"{e.Tungsten:+#;-#} Tungsten");
        if (e.Chromium != 0) parts.Add($"{e.Chromium:+#;-#} Chromium");
        if (e.WorldTension != 0) parts.Add($"{e.WorldTension:+#;-#}% World Tension");
        if (e.Modifiers.GetValueOrDefault("research_speed") > 0)
            parts.Add($"+{e.Modifiers["research_speed"] * 100:N0}% Research Speed");
        if (!string.IsNullOrEmpty(e.TriggerEvent)) parts.Add($"Triggers event: {e.TriggerEvent}");
        if (!string.IsNullOrEmpty(e.WarGoalTarget) && e.WarGoalReductionPercent > 0)
            parts.Add($"War goal: -{e.WarGoalReductionPercent}% war cost vs {e.WarGoalTarget}");

        return parts.Count > 0 ? string.Join(", ", parts) : "No effects";
    }

    // Büyük sayıları kısalt: 250000 → "+250k"
    private static string FormatSigned(long value)
    {
        string sign = value >= 0 ? "+" : "-";
        long abs = Math.Abs(value);
        if (abs >= 1000) return $"{sign}{abs / 1000}k";
        return $"{sign}{abs}";
    }

    // ===== M12: Save/Load =====

    public static SaveSystem.FocusSave GetSaveData()
    {
        return new SaveSystem.FocusSave
        {
            States = _states.Select(kv => new SaveSystem.CountryFocusStateSave
            {
                CountryId = kv.Key,
                Completed = kv.Value.Completed.ToArray(),
                ActiveId = kv.Value.Active?.Id ?? "",
                ActiveDays = kv.Value.ActiveDays,
                AiExhausted = kv.Value.AiExhausted
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.FocusSave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _states.Clear();
        if (data.States != null)
        {
            foreach (var s in data.States)
            {
                var tree = GetTreeFor(s.CountryId);
                var state = new CountryFocusState { CountryId = s.CountryId, Tree = tree };
                if (s.Completed != null)
                    foreach (var id in s.Completed) state.Completed.Add(id);
                if (!string.IsNullOrEmpty(s.ActiveId) && tree?.ById.TryGetValue(s.ActiveId, out var activeFocus) == true)
                {
                    state.Active = activeFocus;
                    state.ActiveDays = s.ActiveDays;
                }
                state.AiExhausted = s.AiExhausted;
                _states[s.CountryId] = state;
            }
        }
        GD.Print($"FocusSystem: state yüklendi — Ülke state={_states.Count}");
    }

    // ===== Sorgular (UI) =====

    public static IReadOnlyDictionary<string, FocusTree> Trees
    {
        get { EnsureLoaded(); return _trees; }
    }

    public static int CompletedCount(CountryFocusState st) => st?.Completed.Count ?? 0;
}
