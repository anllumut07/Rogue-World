using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// M23.1: Seçim takvimi ve oy tahmini.
// - ConstitutionSystem.TermLength'e göre bir sonraki seçim gününü hesaplar (gün cinsinden TotalDays).
// - Monarşi (TermLength=0) ve tek parti rejimleri için genel seçim yok (M22.4 kongre ile ayrı).
// - Oy tahmini: halk memnuniyeti ortalaması + gösterge/bütçe etkileri (StatisticsSystem/BudgetSystem)
//   + kabine skandal cezası (CabinetSystem) + deterministik jitter (DeterministicRNG).
// - Save/Load: SaveSystem.SaveFile.Election. Deterministik (new Random() yok).
public static class ElectionSystem
{
    public class ElectionConfig
    {
        public int DefaultTermYears { get; set; } = 4;
        public Dictionary<string, double> VotePredictionWeights { get; set; } = new();
        public double ScandalPenaltyPerScandal { get; set; } = 3.0;
        public double JitterRange { get; set; } = 5.0;
        public double MinVote { get; set; } = 0.0;
        public double MaxVote { get; set; } = 100.0;
        public double WinThreshold { get; set; } = 50.0; // oy >= eşik → iktidar korunur; altında lider değişir
    }

    public class ElectionSave
    {
        public CountryElectionSave[] Countries { get; set; } = Array.Empty<CountryElectionSave>();
        // M23.3: geçmiş seçim sonuçları (kazanan/kaybeden, lider değişimi, yeni lider adı)
        public ElectionResult[] Results { get; set; } = Array.Empty<ElectionResult>();
    }

    public class CountryElectionSave
    {
        public string CountryId { get; set; } = "";
        public long LastElectionDay { get; set; } = 0;
    }

    public class VotePrediction
    {
        public string CountryId { get; set; } = "";
        public string CountryName { get; set; } = "";
        public double PredictedVote { get; set; }   // 0-100, iktidar partisi için tahmini oy oranı
        public double BaseSatisfaction { get; set; } // 9 grubun ortalama memnuniyeti
        public double CampaignBonus { get; set; }    // aktif kampanya aksiyonlarından gelen oy bonusu
        public int DaysUntilElection { get; set; }   // -1 = seçim yok (monarşi/tek parti)
        public bool ElectionDue { get; set; }        // seçim günü geldi / geçti
        public string GovernmentType { get; set; }
        public int TermLengthYears { get; set; }
    }

    // M23.3: Seçim sonucu kaydı — kim kazandı, lider değişti mi, yeni lider kim.
    public class ElectionResult
    {
        public string CountryId { get; set; } = "";
        public string CountryName { get; set; } = "";
        public long Day { get; set; }                 // TotalDays (seçim günü)
        public double PredictedVote { get; set; }     // iktidar partisi tahmini oy %
        public bool Won { get; set; }                 // true = iktidar korundu, false = lider değişti
        public bool LeaderChanged { get; set; }       // yeni lider seçildi mi
        public string NewLeader { get; set; } = "";   // değiştiyse yeni liderin tam adı
    }

    private const string Path = "res://Data/elections.json";
    private static ElectionConfig _config = new();
    private static readonly Dictionary<string, long> _lastElectionDay = new(); // countryId -> TotalDays
    // M23.3: ülke -> seçim sonucu geçmişi (deterministik, kaydedilebilir)
    private static readonly Dictionary<string, List<ElectionResult>> _electionResults = new();

    public static IReadOnlyDictionary<string, long> LastElectionDay => _lastElectionDay;
    // M23.3: dışarıdan salt okunur erişim (UI / test)
    public static IReadOnlyDictionary<string, List<ElectionResult>> ElectionResults => _electionResults;

    static ElectionSystem()
    {
        LoadData();
    }

    public static void LoadData()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(Path))
            {
                GD.PrintErr($"[ElectionSystem] Veri dosyası yok: {Path}");
                ApplyDefaults();
                GD.Print($"ElectionSystem hazır: varsayılan ayarlarla (elections.json yok).");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(Path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _config = new ElectionConfig();
            if (root.TryGetProperty("DefaultTermYears", out var t))
                _config.DefaultTermYears = t.GetInt32();

            _config.VotePredictionWeights.Clear();
            if (root.TryGetProperty("VotePredictionWeights", out var w))
                foreach (var wp in w.EnumerateObject())
                    _config.VotePredictionWeights[wp.Name] = wp.Value.GetDouble();

            _config.ScandalPenaltyPerScandal = root.TryGetProperty("ScandalPenaltyPerScandal", out var s) ? s.GetDouble() : 3.0;
            _config.JitterRange = root.TryGetProperty("JitterRange", out var j) ? j.GetDouble() : 5.0;
            _config.MinVote = root.TryGetProperty("MinVote", out var mn) ? mn.GetDouble() : 0.0;
            _config.MaxVote = root.TryGetProperty("MaxVote", out var mx) ? mx.GetDouble() : 100.0;
            _config.WinThreshold = root.TryGetProperty("WinThreshold", out var wt) ? wt.GetDouble() : 50.0;

            GD.Print($"[ElectionSystem] {_config.VotePredictionWeights.Count} oy tahmini ağırlığı yüklendi (elections.json).");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ElectionSystem] Yükleme HATA: {e.Message}");
            ApplyDefaults();
        }
    }

    private static void ApplyDefaults()
    {
        _config.DefaultTermYears = 4;
        _config.VotePredictionWeights.Clear();
        _config.VotePredictionWeights["gdp_growth"] = 2.0;
        _config.VotePredictionWeights["budget_deficit"] = -1.0;
        _config.VotePredictionWeights["public_debt"] = -0.1;
        _config.VotePredictionWeights["inflation"] = -0.5;
        _config.VotePredictionWeights["unemployment"] = -0.5;
        _config.VotePredictionWeights["corruption"] = -0.3;
        _config.ScandalPenaltyPerScandal = 3.0;
        _config.JitterRange = 5.0;
        _config.MinVote = 0.0;
        _config.MaxVote = 100.0;
    }

    public static void InitializeAll()
    {
        LoadData();
        _lastElectionDay.Clear();
        foreach (var c in CountryDatabase.All())
            _lastElectionDay[c.Id] = 0; // 0 = henüz seçim yapılmadı, başlangıçtan dönem sonu
        GD.Print($"[ElectionSystem] {_lastElectionDay.Count} ülke seçim takvimi başlatıldı.");
    }

    public static void ResetForNewGame()
    {
        InitializeAll();
    }

    // Belirli bir ülkenin anayasasına göre dönem süresi (yıl). Tanım yoksa varsayılan.
    private static int GetTermLength(string countryId)
    {
        var consti = ConstitutionSystem.GetConstitution(countryId);
        if (consti != null && consti.TermLength > 0)
            return consti.TermLength;
        return _config.DefaultTermYears;
    }

    // Bir sonraki seçim günü (TotalDays cinsinden). Monarşi/tek parti için 0 (seçim yok).
    public static long GetNextElectionDay(string countryId)
    {
        var consti = ConstitutionSystem.GetConstitution(countryId);
        if (consti == null) return 0;
        if (consti.GovernmentType == ConstitutionSystem.GovernmentType.Monarchy) return 0;
        if (consti.GovernmentType == ConstitutionSystem.GovernmentType.OneParty) return 0;

        int termYears = GetTermLength(countryId);
        if (termYears <= 0) return 0;

        long last = _lastElectionDay.TryGetValue(countryId, out var l) ? l : 0;
        if (last == 0)
            return termYears * 365; // ilk seçim: oyun başlangıcından dönem sonu
        return last + termYears * 365;
    }

    public static bool IsElectionDue(string countryId, long currentDay)
    {
        long next = GetNextElectionDay(countryId);
        return next > 0 && currentDay >= next;
    }

    public static int GetDaysUntilElection(string countryId, long currentDay)
    {
        long next = GetNextElectionDay(countryId);
        if (next <= 0) return -1;
        return (int)Math.Max(0, next - currentDay);
    }

    // Seçim gerçekleşti olarak işaretle (sonraki dönem başlar). M23.3 tarafından çağrılır.
    public static void MarkElectionHeld(string countryId, long currentDay)
    {
        if (string.IsNullOrEmpty(countryId)) return;
        _lastElectionDay[countryId] = currentDay;
        GD.Print($"[ElectionSystem] {countryId}: seçim {currentDay}. günde gerçekleşti, sonraki dönem {currentDay + GetTermLength(countryId) * 365}");
    }

    // M23.3: Seçim günü için sonucu çözümle — iktidar korunsun mu yoksa lider/kabine değişsin mi.
    // Dönüş: seçim sonucu kaydı (null = seçim henüz geçmedi / ülke için seçim yok).
    public static ElectionResult ResolveElection(string countryId, long currentDay)
    {
        if (string.IsNullOrEmpty(countryId)) return null;
        if (!IsElectionDue(countryId, currentDay)) return null;

        var pred = GetVotePrediction(countryId, currentDay);
        bool won = pred.PredictedVote >= _config.WinThreshold;

        var result = new ElectionResult
        {
            CountryId = countryId,
            CountryName = pred.CountryName,
            Day = currentDay,
            PredictedVote = pred.PredictedVote,
            Won = won,
            LeaderChanged = false,
            NewLeader = ""
        };

        if (!won)
        {
            // Lider değişir: mevcut lideri geçersiz kıl, yeni deterministik lider üret.
            var newLeader = LeaderSystem.RegenerateLeader(countryId, currentDay);
            result.LeaderChanged = true;
            result.NewLeader = newLeader.FullName;

            // Kabineyi yeniden dağıt: mevcut atamaları temizle, her bakanlık için en iyi uygun bakanı ata.
            ReshuffleCabinet(countryId);
        }

        // Sonucu kaydet
        if (!_electionResults.TryGetValue(countryId, out var list))
        {
            list = new List<ElectionResult>();
            _electionResults[countryId] = list;
        }
        list.Add(result);

        // Sonraki dönem başlar
        MarkElectionHeld(countryId, currentDay);

        GD.Print($"[ElectionSystem] {countryId}: seçim {currentDay}. günde {(won ? "KAZANDI — iktidar korundu" : "KAYBEDİLDİ — lider değişti: " + result.NewLeader)}, oy=%{pred.PredictedVote:F1}");
        return result;
    }

    // M23.3: Seçim kaybı sonrası kabineyi yeniden dağıt (deterministik, ülke içinden en yetenekli adaylar).
    private static void ReshuffleCabinet(string countryId)
    {
        var ministries = CabinetSystem.Ministries;
        // Mevcut atamaları temizle
        foreach (var m in ministries)
            CabinetSystem.AssignMinister(countryId, m.Id, "");
        // Her bakanlık için ülke içinden en yetenekli, henüz atanmamış uygun bakanı ata
        foreach (var m in ministries)
        {
            var available = CabinetSystem.GetAvailableMinisters(countryId, m.Id);
            if (available.Count > 0)
                CabinetSystem.AssignMinister(countryId, m.Id, available[0].Id);
        }
        GD.Print($"[ElectionSystem] {countryId}: kabine yeniden dağıtıldı ({ministries.Count} bakanlık).");
    }

    // ===== Oy tahmini =====
    public static VotePrediction GetVotePrediction(string countryId, long currentDay)
    {
        var pred = new VotePrediction
        {
            CountryId = countryId,
            CountryName = CountryDatabase.Get(countryId)?.Name ?? countryId,
            GovernmentType = ConstitutionSystem.GetConstitution(countryId)?.GovernmentType.ToString() ?? "Parliamentary",
            TermLengthYears = GetTermLength(countryId),
            DaysUntilElection = GetDaysUntilElection(countryId, currentDay),
            ElectionDue = IsElectionDue(countryId, currentDay)
        };

        // Seçim yoksa (monarşi/tek parti) oy tahmini yok
        if (pred.DaysUntilElection < 0)
        {
            pred.PredictedVote = 0;
            pred.BaseSatisfaction = 0;
            return pred;
        }

        // Halk memnuniyeti ortalaması (9 grup)
        double satSum = 0;
        int satCount = 0;
        foreach (var g in PublicSatisfactionSystem.Groups)
        {
            satSum += PublicSatisfactionSystem.GetSatisfaction(countryId, g.Id);
            satCount++;
        }
        double baseSatisfaction = satCount > 0 ? satSum / satCount : 50.0;
        pred.BaseSatisfaction = baseSatisfaction;

        double vote = baseSatisfaction;

        // Gösterge / bütçe etkileri (deterministik formül)
        foreach (var w in _config.VotePredictionWeights)
        {
            double input = ResolveInput(countryId, w.Key);
            vote += input * w.Value;
        }

        // Kabine skandal cezası: bu ülke'deki atanan bakanlıklardaki aktif skandal sayısı
        int scandalCount = 0;
        foreach (var ministry in CabinetSystem.Ministries)
        {
            string ministerId = CabinetSystem.GetMinister(countryId, ministry.Id);
            if (!string.IsNullOrEmpty(ministerId) && !string.IsNullOrEmpty(CabinetSystem.GetScandalStatus(ministerId)))
                scandalCount++;
        }
        vote -= scandalCount * _config.ScandalPenaltyPerScandal;

        // Kampanya aksiyonu bonusu (miting/reklam/TV/ittifak/vaat — süreli)
        double campaignBonus = ElectionCampaignSystem.GetCampaignBonus(countryId, currentDay);
        vote += campaignBonus;
        pred.CampaignBonus = campaignBonus;

        // Deterministik jitter (±JitterRange)
        double jitter = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * _config.JitterRange;
        vote += jitter;

        vote = Math.Clamp(vote, _config.MinVote, _config.MaxVote);
        pred.PredictedVote = vote;

        return pred;
    }

    private static double ResolveInput(string countryId, string inputId)
    {
        var def = StatisticsSystem.Definitions.FirstOrDefault(d => d.Id == inputId);
        if (def != null)
            return StatisticsSystem.GetValue(countryId, inputId);
        return BudgetSystem.GetSpendingPercent(countryId, inputId);
    }

    public static IEnumerable<VotePrediction> GetAllVotePredictions(long currentDay)
    {
        foreach (var c in CountryDatabase.All())
            yield return GetVotePrediction(c.Id, currentDay);
    }

    // ===== M12: Save/Load =====
    public static ElectionSave GetSaveData() => new ElectionSave
    {
        Countries = _lastElectionDay.Select(kv => new CountryElectionSave
        {
            CountryId = kv.Key,
            LastElectionDay = kv.Value
        }).ToArray(),
        // M23.3: geçmiş seçim sonuçları
        Results = _electionResults.SelectMany(kv => kv.Value.Select(r => new ElectionResult
        {
            CountryId = kv.Key,
            CountryName = r.CountryName,
            Day = r.Day,
            PredictedVote = r.PredictedVote,
            Won = r.Won,
            LeaderChanged = r.LeaderChanged,
            NewLeader = r.NewLeader
        })).ToArray()
    };

    public static void LoadSaveData(ElectionSave data)
    {
        if (data == null) return;
        _lastElectionDay.Clear();
        if (data.Countries != null)
        {
            foreach (var c in data.Countries)
            {
                if (string.IsNullOrEmpty(c.CountryId)) continue;
                _lastElectionDay[c.CountryId] = c.LastElectionDay;
            }
        }
        // M23.3: seçim sonuçlarını geri yükle
        _electionResults.Clear();
        if (data.Results != null)
        {
            foreach (var r in data.Results)
            {
                if (string.IsNullOrEmpty(r.CountryId)) continue;
                if (!_electionResults.TryGetValue(r.CountryId, out var list))
                {
                    list = new List<ElectionResult>();
                    _electionResults[r.CountryId] = list;
                }
                list.Add(r);
            }
        }
        GD.Print($"[ElectionSystem] {_lastElectionDay.Count} ülke seçim tarihi ve {_electionResults.Values.Sum(l => l.Count)} seçim sonucu yüklendi.");
    }
}
