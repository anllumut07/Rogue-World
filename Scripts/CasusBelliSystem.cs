using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Savaş Meşruiyeti (Casus Belli) Sistemi v1 (M17A):
/// - Gerekçe türleri: Savunma/Karşılık, Toprak İddiası, İdeolojik Çatışma, Himaye/Garanti, Fabrike Edilmiş Olay, Tarihi Olay
/// - Gerekçe üretme: "Build Case / Fabricate Incident" — belirli gün sürer, PP maliyeti, başarı/ifşa şansı DeterministicRNG
/// - Önkoşullar: geçerli gerekçe, aynı faksiyon olmama, savaş desteği eşiği, ültimatom/bekleme, ateşkes, PP maliyeti
/// - Dünya itibarı (legitimacy): gerekçeli savaş az maliyet, gerekçesiz yol yok
/// - AI aynı kurallara tabi, ama gerekçeler olasılıkla otomatik üretilir
/// - Save/Load destekli, A==B determinizm testi
/// </summary>
public static class CasusBelliSystem
{
    // ===== Veri modelleri =====

    public class CasusBelliType
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsAutomatic { get; set; }           // otomatik verilen (savunma, garanti, tarihî)
        public bool RequiresClaim { get; set; }         // toprak iddiası gerektirir
        public bool RequiresDifferentIdeology { get; set; } // farklı ideoloji gerektirir
        public float MinWorldTension { get; set; }      // minimum dünya gerilimi
        public int BuildDays { get; set; }              // inşa/gerekçe üretme süresi
        public int PPCost { get; set; }                 // PP maliyeti
        public float ExposeChance { get; set; }         // ifşa olasılığı (0-1)
        public float LegitimacyCostOnUse { get; set; }  // savaş ilanında itibar maliyeti
        public float TensionOnUse { get; set; }         // savaş ilanında tension artışı
    }

    public class TerritorialClaim
    {
        public string OwnerId { get; set; }
        public string TargetId { get; set; }
        public string Region { get; set; }
        public string Note { get; set; }
    }

    public class Justification
    {
        public string CountryId { get; set; }       // kim üretiyor
        public string TargetId { get; set; }        // kime karşı
        public string TypeId { get; set; }          // gerekçe türü
        public int BuildDaysLeft { get; set; }      // kalan inşa günü
        public bool IsAutomatic { get; set; }       // otomatik verildiyse build/ultimatum yok
        public bool Exposed { get; set; }           // fabrike olay ifşa oldu mu
        public int UltimatumDaysLeft { get; set; }  // ültimatom/bekleme kalan gün
        public bool IsConsumed { get; set; }        // savaş ilanında tüketildi mi

        public string Key => $"{CountryId}->{TargetId}:{TypeId}";
    }

    // ===== AI Savaş Ayarları (Data/ai_war_settings.json) =====
    private class AiWarSettings
    {
        public double JustificationStartChance { get; set; } = 0.015;
        public int MaxAiCountriesAtWarPercent { get; set; } = 10;
        public TargetSelectionSettings TargetSelection { get; set; } = new();
    }

    private class TargetSelectionSettings
    {
        public bool OnlyNeighbors { get; set; } = true;
        public bool OnlyWithClaims { get; set; } = true;
        public bool OnlyIdeologicalEnemies { get; set; } = true;
        public bool OnlyFactionCalls { get; set; } = true;
    }

    private static AiWarSettings _aiWarSettings = new();

    // ===== Sabitler =====
    private static readonly Dictionary<string, CasusBelliType> _types = new();
    private static readonly List<TerritorialClaim> _claims = new();
    private static readonly List<Justification> _justifications = new();
    private static readonly Dictionary<string, int> _truces = new(); // "A-B" (sıralı) -> bitiş günü
    private static readonly Dictionary<string, float> _legitimacy = new(); // countryId -> 0-100 (varsayılan 50)

    private static Dictionary<string, int> _warSupportThresholds = new();
    private static int _ultimatumDays = 10;
    private static int _truceDays = 90;

    private static bool _initialized;
    private static bool _settingsLoaded = false;

    // M17.1: yollar aktif senaryodan
    private static string CurrentCasusBelliPath => ScenarioSystem.ActiveCasusBelliPath;
    private static string CurrentTerritorialClaimsPath => ScenarioSystem.ActiveTerritorialClaimsPath;

    private static GameManager GM => GameManager.Instance;

    // ===== Başlatma =====

    public static void Initialize(CountryData data)
    {
        _justifications.Clear();
        _truces.Clear();
        _legitimacy.Clear();
        _initialized = data != null;

        if (data == null) return;

        LoadFromFile();
        LoadAiWarSettings();
        InitializeLegitimacy();

        GD.Print($"CasusBelliSystem: {data.Id} için hazır — Gerekçe türü={_types.Count}, Toprak iddiası={_claims.Count}, Legitimacy={_legitimacy.Count} ülke");
    }

    /// <summary>AI savaş ayarlarını Data/ai_war_settings.json'dan yükle (bir kez).</summary>
    private static void LoadAiWarSettings()
    {
        if (_settingsLoaded) return;
        _settingsLoaded = true;

        const string path = "res://Data/ai_war_settings.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"CasusBelliSystem: {path} bulunamadı — varsayılan AI savaş ayarları kullanılıyor");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            var settings = JsonSerializer.Deserialize<AiWarSettings>(FileAccess.GetFileAsString(path), options);
            if (settings != null)
            {
                _aiWarSettings = settings;
            }
            GD.Print($"[AIWarSettings] Yüklendi: gerekçe şansı=%{_aiWarSettings.JustificationStartChance * 100:F1}, " +
                     $"maks savaşta AI=%{_aiWarSettings.MaxAiCountriesAtWarPercent}, " +
                     $"komşu={_aiWarSettings.TargetSelection.OnlyNeighbors}, iddia={_aiWarSettings.TargetSelection.OnlyWithClaims}, " +
                     $"ideoloji={_aiWarSettings.TargetSelection.OnlyIdeologicalEnemies}, faksiyon={_aiWarSettings.TargetSelection.OnlyFactionCalls}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"CasusBelliSystem: ai_war_settings.json okunamadı: {e.Message}");
        }
    }

    private static void LoadFromFile()
    {
        // M17.1: her Initialize'da idempotent yükleme — eski liste yeniden yüklenirken çoğalmasın
        _types.Clear();
        _claims.Clear();
        _warSupportThresholds.Clear();

        // casus_belli.json (aktif senaryo)
        string path = CurrentCasusBelliPath;
        if (FileAccess.FileExists(path))
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
                var json = FileAccess.GetFileAsString(path);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("Types", out var types))
                {
                    foreach (var t in types.EnumerateArray())
                    {
                        var type = new CasusBelliType
                        {
                            Id = t.GetProperty("Id").GetString(),
                            Name = t.GetProperty("Name").GetString(),
                            Description = t.GetProperty("Description").GetString(),
                            IsAutomatic = t.GetProperty("IsAutomatic").GetBoolean(),
                            RequiresClaim = t.GetProperty("RequiresClaim").GetBoolean(),
                            RequiresDifferentIdeology = t.GetProperty("RequiresDifferentIdeology").GetBoolean(),
                            MinWorldTension = t.GetProperty("MinWorldTension").GetSingle(),
                            BuildDays = t.GetProperty("BuildDays").GetInt32(),
                            PPCost = t.GetProperty("PPCost").GetInt32(),
                            ExposeChance = t.GetProperty("ExposeChance").GetSingle(),
                            LegitimacyCostOnUse = t.GetProperty("LegitimacyCostOnUse").GetSingle(),
                            TensionOnUse = t.GetProperty("TensionOnUse").GetSingle()
                        };
                        _types[type.Id] = type;
                    }
                }

                if (doc.RootElement.TryGetProperty("WarSupportThreshold", out var wsThreshold))
                {
                    foreach (var kv in wsThreshold.EnumerateObject())
                        _warSupportThresholds[kv.Name] = kv.Value.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("UltimatumDays", out var ult))
                    _ultimatumDays = ult.GetInt32();
                if (doc.RootElement.TryGetProperty("TruceDays", out var trc))
                    _truceDays = trc.GetInt32();
            }
            catch (Exception e)
            {
                GD.PrintErr($"CasusBelliSystem: {path} okunamadı: {e.Message}");
            }
        }
        else
        {
            GD.PrintErr($"CasusBelliSystem: {path} bulunamadı!");
        }

        // territorial_claims.json (aktif senaryo)
        string claimsPath = CurrentTerritorialClaimsPath;
        if (FileAccess.FileExists(claimsPath))
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
                var json = FileAccess.GetFileAsString(claimsPath);
                var claims = JsonSerializer.Deserialize<List<TerritorialClaim>>(json, options);
                if (claims != null)
                {
                    foreach (var c in claims)
                    {
                        c.OwnerId = CountryDatabase.NormalizeId(c.OwnerId);
                        c.TargetId = CountryDatabase.NormalizeId(c.TargetId);
                        _claims.Add(c);
                    }
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"CasusBelliSystem: {claimsPath} okunamadı: {e.Message}");
            }
        }
        else
        {
            GD.PrintErr($"CasusBelliSystem: {claimsPath} bulunamadı!");
        }
    }

    // M17.1: Senaryo değişince cache'i temizle (types/claims/thresholds yeniden yüklensin)
    public static void ResetForScenario()
    {
        _types.Clear();
        _claims.Clear();
        _warSupportThresholds.Clear();
    }

    // M17.1: Senaryo verisi yükle (ülke gerekmez) — LogLoadCountsForAll için.
    // LoadFromFile idempotent: başında _types/_claims/_warSupportThresholds temizlenir.
    public static void LoadScenarioData()
    {
        LoadFromFile();
    }

    // Yüklenen kayıt sayıları (senaryo testi için)
    public static int TypesCount => _types.Count;
    public static int ClaimsCount => _claims.Count;

    private static void InitializeLegitimacy()
    {
        foreach (var c in CountryDatabase.All())
        {
            if (c != null && !c.IsAnnexed)
                _legitimacy[c.Id] = 50f; // varsayılan 50
        }
    }

    // ===== Günlük tick =====

    public static void DailyTick()
    {
        if (!_initialized || GM == null) return;

        // 1) Gerekçe inşaatlarını ilerlet
        for (int i = _justifications.Count - 1; i >= 0; i--)
        {
            var j = _justifications[i];
            if (j.IsConsumed) continue;

            if (j.BuildDaysLeft > 0)
            {
                j.BuildDaysLeft--;
                if (j.BuildDaysLeft == 0)
                {
                    // İnşa bitti — ifşa kontrolü (sadece fabrike olay için)
                    var type = _types.GetValueOrDefault(j.TypeId);
                    if (type != null && type.ExposeChance > 0 && !j.IsAutomatic)
                    {
                        double roll = DeterministicRNG.NextDouble();
                        if (roll < type.ExposeChance)
                        {
                            j.Exposed = true;
                            // İfa: itibar -10, tension +10
                            ModifyLegitimacy(j.CountryId, -10f);
                            DiplomacySystem.IncreaseTension(10f);
                            GD.Print($"[CasusBelli] İFŞA: {j.CountryId} → {j.TargetId} ({j.TypeId}) zarfı ifşa edildi! (roll={roll:F3} < {type.ExposeChance}) Legitimacy -10, Tension +10");
                        }
                        else
                        {
                            GD.Print($"[CasusBelli] Gerekçe tamamlandı: {j.CountryId} → {j.TargetId} ({j.TypeId}) — ifşa edilmedi (roll={roll:F3})");
                        }
                    }
                    else
                    {
                        GD.Print($"[CasusBelli] Gerekçe tamamlandı: {j.CountryId} → {j.TargetId} ({j.TypeId})");
                    }

                    // Ültimatom süresi başlat
                    j.UltimatumDaysLeft = _ultimatumDays;
                }
            }
            else if (j.UltimatumDaysLeft > 0)
            {
                j.UltimatumDaysLeft--;
                if (j.UltimatumDaysLeft == 0)
                {
                    GD.Print($"[CasusBelli] Ültimatom süresi doldu: {j.CountryId} → {j.TargetId} ({j.TypeId}) — savaş ilanı artık mümkün");
                }
            }
        }

        // 2) Ateşkes (truce) günlerini azalt
        var today = GM.TotalDays;
        foreach (var key in _truces.Keys.ToList())
        {
            if (_truces[key] <= today)
                _truces.Remove(key);
        }
    }

    // ===== Public API =====

    /// <summary>Belirli bir ülke çifti için geçerli (tüketilmemiş, inşa/ültimatom bitmiş) gerekçeleri döndürür.</summary>
    public static List<Justification> GetValidJustifications(string countryId, string targetId)
    {
        return _justifications
            .Where(j => j.CountryId == countryId && j.TargetId == targetId && !j.IsConsumed && IsReady(j))
            .ToList();
    }

    /// <summary>Herhangi bir geçerli gerekçe var mı?</summary>
    public static bool HasValidCasusBelli(string countryId, string targetId)
    {
        return _justifications.Any(j => j.CountryId == countryId && j.TargetId == targetId && !j.IsConsumed && IsReady(j));
    }

    private static bool IsReady(Justification j)
    {
        if (j.IsAutomatic) return true; // otomatikler anında hazır
        return j.BuildDaysLeft == 0 && j.UltimatumDaysLeft == 0;
    }

    /// <summary>Hangi gerekçe türlerini bu ülke bu hedefe karşı başlatabilir?</summary>
    public static List<CasusBelliType> GetStartableTypes(string countryId, string targetId)
    {
        var result = new List<CasusBelliType>();
        var country = CountryDatabase.Get(countryId);
        var target = CountryDatabase.Get(targetId);
        if (country == null || target == null || country.IsAnnexed || target.IsAnnexed) return result;
        if (countryId == targetId) return result;
        if (WarSystem.IsAtWarWith(countryId, targetId)) return result;

        foreach (var type in _types.Values)
        {
            if (type.IsAutomatic) continue; // otomatikler manuel başlatılamaz
            if (!CanStartBuildCase(country, target, type)) continue;
            result.Add(type);
        }
        return result;
    }

    private static bool CanStartBuildCase(CountryData country, CountryData target, CasusBelliType type)
    {
        // Zaten aynı çift/tür için aktif gerekçe var mı?
        if (_justifications.Any(j => j.CountryId == country.Id && j.TargetId == target.Id && j.TypeId == type.Id && !j.IsConsumed))
            return false;

        // Toprak iddiası gerektiriyor mu?
        if (type.RequiresClaim)
        {
            bool hasClaim = _claims.Any(c => c.OwnerId == country.Id && c.TargetId == target.Id);
            if (!hasClaim) return false;
        }

        // Farklı ideoloji gerektiriyor mu?
        if (type.RequiresDifferentIdeology)
        {
            if (country.Ideology == target.Ideology) return false;
        }

        // Minimum dünya gerilimi
        if (DiplomacySystem.WorldTension < type.MinWorldTension)
            return false;

        // Aynı faksiyonda olmamak (özel kontrol — UI'da da gösterilecek)
        var facA = DiplomacySystem.GetFactionOf(country.Id);
        var facB = DiplomacySystem.GetFactionOf(target.Id);
        if (facA != null && facA == facB) return false;

        // PP yeterli mi?
        if (country.PoliticalPower < type.PPCost) return false;

        return true;
    }

    /// <summary>Gerekçe inşaatını başlat (Build Case / Fabricate Incident).</summary>
    public static bool StartBuildCase(string countryId, string targetId, string typeId)
    {
        if (!_initialized) return false;
        var country = CountryDatabase.Get(countryId);
        var target = CountryDatabase.Get(targetId);
        if (country == null || target == null) return false;
        if (!_types.TryGetValue(typeId, out var type)) return false;
        if (type.IsAutomatic) return false;

        if (!CanStartBuildCase(country, target, type)) return false;

        // PP kes
        country.PoliticalPower -= type.PPCost;

        var j = new Justification
        {
            CountryId = countryId,
            TargetId = targetId,
            TypeId = typeId,
            BuildDaysLeft = type.BuildDays,
            IsAutomatic = false,
            Exposed = false,
            UltimatumDaysLeft = 0,
            IsConsumed = false
        };
        _justifications.Add(j);

        GD.Print($"[CasusBelli] Gerekçe inşaatı BAŞLATILDI: {countryId} → {targetId} ({type.Name}) — {type.BuildDays} gün, {type.PPCost} PP, kalan PP={country.PoliticalPower:N1}");
        return true;
    }

    /// <summary>Otomatik/Anında gerekçe ver (tarihi olaylar, savunma, garanti tetiklemesi).</summary>
    public static void GrantImmediateJustification(string countryId, string targetId, string typeId)
    {
        if (!_initialized) return;
        if (!_types.TryGetValue(typeId, out var type)) return;

        // Zaten varsa tekrar ekleme
        if (_justifications.Any(j => j.CountryId == countryId && j.TargetId == targetId && j.TypeId == typeId && !j.IsConsumed))
            return;

        var j = new Justification
        {
            CountryId = countryId,
            TargetId = targetId,
            TypeId = typeId,
            BuildDaysLeft = 0,
            IsAutomatic = true,
            Exposed = false,
            UltimatumDaysLeft = 0,
            IsConsumed = false
        };
        _justifications.Add(j);

        GD.Print($"[CasusBelli] OTOMATİK GEREKÇE VERİLDİ: {countryId} → {targetId} ({type.Name}) — anında geçerli");
    }

    /// <summary>Savaş ilanı öncesi tam önkoşul kontrolü (UI checklist ve click-log için).</summary>
    public class WarCheckResult
    {
        public bool CanDeclare { get; set; }
        public List<CheckItem> Items { get; set; } = new();
        public string Summary => CanDeclare ? "Tüm şartlar karşılanıyor — savaş ilanı MÜMKÜN" : "Savaş ilanı BLOKLU — eksikler: " + string.Join(", ", Items.Where(i => !i.Met).Select(i => i.Label));

        public override string ToString() => Summary;
    }

    public class CheckItem
    {
        public string Label { get; set; }
        public bool Met { get; set; }
        public string Detail { get; set; } // neden met/met değil
        public Color Color => Met ? Colors.LightGreen : Colors.IndianRed;
    }

    /// <summary>Savaş ilanı için tüm önkoşulları kontrol et ve detaylı listele.</summary>
    public static WarCheckResult CheckWarPreconditions(string countryId, string targetId)
    {
        var result = new WarCheckResult();
        var country = CountryDatabase.Get(countryId);
        var target = CountryDatabase.Get(targetId);

        if (country == null || target == null)
        {
            result.Items.Add(new CheckItem { Label = "Hedef/Ülke Geçerli", Met = false, Detail = "Ülke verisi bulunamadı" });
            result.CanDeclare = false;
            return result;
        }

        if (countryId == targetId)
        {
            result.Items.Add(new CheckItem { Label = "Kendine Savaş", Met = false, Detail = "Kendine savaş ilan edilemez" });
            result.CanDeclare = false;
            return result;
        }

        if (target.IsAnnexed)
        {
            result.Items.Add(new CheckItem { Label = "Hedef Var", Met = false, Detail = $"Hedef zaten ilhak edilmiş ({target.OwnerId})" });
            result.CanDeclare = false;
            return result;
        }

        if (WarSystem.IsAtWarWith(countryId, targetId))
        {
            result.Items.Add(new CheckItem { Label = "Zaten Savaşta", Met = false, Detail = "Bu ülkeyle zaten savaş halindesiniz" });
            result.CanDeclare = false;
            return result;
        }

        // 1) Geçerli bir gerekçe var mı?
        var validJust = GetValidJustifications(countryId, targetId);
        if (validJust.Count > 0)
        {
            var names = string.Join(", ", validJust.Select(j => _types.GetValueOrDefault(j.TypeId)?.Name ?? j.TypeId));
            result.Items.Add(new CheckItem { Label = "Geçerli Gerekçe (Casus Belli)", Met = true, Detail = names });
        }
        else
        {
            // Hangi türler başlatılabilirdi?
            var startable = GetStartableTypes(countryId, targetId);
            string detail = startable.Count > 0
                ? $"Henüz yok. Başlatılabilir: {string.Join(", ", startable.Select(t => t.Name))}"
                : "Hiçbir gerekçe türü başlatılamıyor (iddia/ideoloji/tension/faşiyon/PP eksik)";
            result.Items.Add(new CheckItem { Label = "Geçerli Gerekçe (Casus Belli)", Met = false, Detail = detail });
        }

        // 2) Aynı faksiyonda olmamak
        var facA = DiplomacySystem.GetFactionOf(countryId);
        var facB = DiplomacySystem.GetFactionOf(targetId);
        bool sameFaction = facA != null && facA == facB;
        result.Items.Add(new CheckItem
        {
            Label = "Aynı Faksiyonda Değil",
            Met = !sameFaction,
            Detail = sameFaction ? $"Her ikisi de '{facA.Name}' faksiyonunda" : "Farklı faksiyonlar / faksiyonsuz"
        });

        // 3) Savaş desteği eşiği (ideolojiye göre)
        int wsThreshold = _warSupportThresholds.GetValueOrDefault(country.Ideology, 25);
        bool wsOk = country.WarSupportPercent >= wsThreshold;
        result.Items.Add(new CheckItem
        {
            Label = $"Savaş Desteği ≥ %{wsThreshold} ({country.Ideology})",
            Met = wsOk,
            Detail = $"Mevcut: %{country.WarSupportPercent:N0} {(wsOk ? "✓" : "✗")}"
        });

        // 4) PP yeterli mi? (war goal indirimi dahil)
        int warCost = WarSystem.CalculateWarGoalCost(targetId);
        bool ppOk = country.PoliticalPower >= warCost;
        result.Items.Add(new CheckItem
        {
            Label = $"Political Power ≥ {warCost} (War Goal indirimi uygulanmış)",
            Met = ppOk,
            Detail = $"Mevcut PP: {country.PoliticalPower:N1} {(ppOk ? "✓" : "✗")}"
        });

        // 5) Ateşkes (truce) kontrolü
        string truceKey = GetTruceKey(countryId, targetId);
        int truceEndDay = _truces.GetValueOrDefault(truceKey, 0);
        bool truceOk = truceEndDay <= GM.TotalDays;
        int truceLeft = Math.Max(0, truceEndDay - GM.TotalDays);
        result.Items.Add(new CheckItem
        {
            Label = "Ateşkes Süresi Doldu",
            Met = truceOk,
            Detail = truceOk ? "Ateşkes yok" : $"Ateşkes aktif — {truceLeft} gün kaldı"
        });

        // 6) Hedef mevcut ve ilhak edilmemiş (yukarıda zaten kontrol edildi ama checklist için)
        result.Items.Add(new CheckItem
        {
            Label = "Hedef Mevcut ve Bağımsız",
            Met = !target.IsAnnexed,
            Detail = target.IsAnnexed ? $"İlhak edilmiş: {target.OwnerId}" : "Bağımsız ülke"
        });

        result.CanDeclare = result.Items.All(i => i.Met);
        return result;
    }

    /// <summary>Tıklama anında her koşulu GD.Print ile logla (Task 1 isteği).</summary>
    public static void LogWarDeclarationCheck(string countryId, string targetId)
    {
        var check = CheckWarPreconditions(countryId, targetId);
        GD.Print($"[WarCheck] {countryId} → {targetId} savaş ilanı koşul kontrolü:");
        foreach (var item in check.Items)
        {
            GD.Print($"[WarCheck]   {(item.Met ? "✓" : "✗")} {item.Label}: {item.Detail}");
        }
        GD.Print($"[WarCheck] SONUÇ: {(check.CanDeclare ? "İZİN VERİLİYOR" : "REDDEDİLDİ")} — {check.Summary}");
    }

    /// <summary>Savaş ilan edildiğinde çağrılır: gerekçe tüket, itibar/tension uygula, otomatik gerekçeler ver (savunma/garanti/müttefik).</summary>
    public static void NotifyWarDeclared(string attackerId, string defenderId, string justificationTypeId = "")
    {
        if (!_initialized) return;

        // 1) Kullanılan gerekçeyi tüket (build edilmişse)
        if (!string.IsNullOrEmpty(justificationTypeId))
        {
            var used = _justifications.FirstOrDefault(j => j.CountryId == attackerId && j.TargetId == defenderId && j.TypeId == justificationTypeId && !j.IsConsumed);
            if (used != null)
            {
                used.IsConsumed = true;
                GD.Print($"[CasusBelli] Gerekçe TÜKETİLDİ: {attackerId} → {defenderId} ({justificationTypeId})");
            }
        }

        // 2) Saldırgan itibar/tension
        var attType = _types.GetValueOrDefault(justificationTypeId);
        if (attType != null)
        {
            ModifyLegitimacy(attackerId, -attType.LegitimacyCostOnUse);
            DiplomacySystem.IncreaseTension(attType.TensionOnUse);
        }

        // 3) Savunucu itibar +2 (müttefik sempati)
        ModifyLegitimacy(defenderId, 2f);

        // 4) Otomatik gerekçeler: Savunucu (defense_retaliation)
        GrantImmediateJustification(defenderId, attackerId, "defense_retaliation");

        // 5) Savunucunun faksiyon üyeleri (müttefikler) -> defense_retaliation
        // M17.2: Yalnızca DefensePact faksiyonları üyelerine gerekçe verir
        // (PoliticalEconomic bloklar — AB, BRICS, ŞİÖ — askeri bağlılık değildir)
        var defFaction = DiplomacySystem.GetFactionOf(defenderId);
        if (defFaction != null && defFaction.IsDefensePact)
        {
            foreach (var memberId in defFaction.MemberIds)
            {
                if (memberId != defenderId && memberId != attackerId)
                    GrantImmediateJustification(memberId, attackerId, "defense_retaliation");
            }
        }

        // 6) Savunucunun garantörleri -> guarantee_protection
        var guarantors = DiplomacySystem.GetGuaranteesFor(defenderId);
        foreach (var gId in guarantors)
        {
            if (gId != attackerId)
                GrantImmediateJustification(gId, attackerId, "guarantee_protection");
        }

        // 7) Eğer gerekçe fabrike edilmiş VE ifşa edilmişse: demokrasiler itibar cezası + ek tension
        if (attType?.Id == "fabricated_incident")
        {
            var usedJust = _justifications.FirstOrDefault(j => j.CountryId == attackerId && j.TargetId == defenderId && j.TypeId == "fabricated_incident" && j.IsConsumed);
            if (usedJust != null && usedJust.Exposed)
            {
                // Demokrasiler saldırganla ilişkileri düşürsün
                foreach (var c in CountryDatabase.All())
                {
                    if (c != null && c.Ideology == "Democracy" && !c.IsAnnexed && c.Id != attackerId)
                    {
                        DiplomacySystem.ModifyRelation(c.Id, attackerId, -20);
                    }
                }
                DiplomacySystem.IncreaseTension(15f); // ek tension
                GD.Print($"[CasusBelli] İFŞA EDİLMİŞ FABRIKE GEREKÇE İLE SAVAŞ: Demokrasiler {attackerId} ile ilişkileri -20, Tension +15 ek");
            }
        }
    }

    /// <summary>Savaş bittiğinde ateşkes (truce) başlat.</summary>
    public static void NotifyWarEnded(string countryA, string countryB)
    {
        if (!_initialized) return;
        string key = GetTruceKey(countryA, countryB);
        int endDay = GM.TotalDays + _truceDays;
        _truces[key] = endDay;
        GD.Print($"[CasusBelli] ATEŞKES BAŞLADI: {countryA} ↔ {countryB} — {_truceDays} gün (bitiş: gün {endDay})");
    }

    private static string GetTruceKey(string a, string b)
    {
        return a.CompareTo(b) < 0 ? $"{a}-{b}" : $"{b}-{a}";
    }

    /// <summary>Ateşkes kalan günü (0 = ateşkes yok).</summary>
    public static int GetTruceDaysLeft(string countryA, string countryB)
    {
        string key = GetTruceKey(countryA, countryB);
        if (!_truces.TryGetValue(key, out var endDay)) return 0;
        return Math.Max(0, endDay - (GM?.TotalDays ?? 0));
    }

    /// <summary>
    /// İki ülke arasındaki ateşkesi kaldırır. İç savaş bir ülkenin kendi
    /// bağırsaklarındaki çatışmadır: ebe ile isyancı arasındaki eski ateşkes
    /// (önceki isyanın sonu) yeni isyanı bağlamaz — yoksa ikinci isyan hiç
    /// savaş açamaz.
    /// </summary>
    public static void ClearTruce(string countryA, string countryB)
    {
        if (string.IsNullOrEmpty(countryA) || string.IsNullOrEmpty(countryB)) return;
        string key = GetTruceKey(countryA, countryB);
        if (_truces.Remove(key))
            GD.Print($"[CasusBelli] ATEŞKES KALKTI: {countryA} ↔ {countryB} — iç çatışma eski barışı geçersiz kıldı.");
    }

    // ===== Legitimacy (Dünya İtibarı) =====

    public static float GetLegitimacy(string countryId)
    {
        return _legitimacy.GetValueOrDefault(countryId, 50f);
    }

    public static void ModifyLegitimacy(string countryId, float delta)
    {
        if (!_legitimacy.ContainsKey(countryId))
            _legitimacy[countryId] = 50f;
        _legitimacy[countryId] = Math.Clamp(_legitimacy[countryId] + delta, 0f, 100f);
    }

    // ===== AI Desteği =====

    /// <summary>AI için: bu hedefe karşı en uygun gerekçe türünü seç (iddia > ideoloji > fabrike).</summary>
    public static string GetBestJustificationTypeForAI(string countryId, string targetId)
    {
        var startable = GetStartableTypes(countryId, targetId);
        if (startable.Count == 0) return null;

        // Tercih sırası: territorial_claim > ideological_conflict > fabricated_incident
        var preferred = startable.FirstOrDefault(t => t.Id == "territorial_claim")
                     ?? startable.FirstOrDefault(t => t.Id == "ideological_conflict")
                     ?? startable.FirstOrDefault(t => t.Id == "fabricated_incident");
        return preferred?.Id;
    }

    /// <summary>AI gerekçe başlatma kısıtlamaları: komşuluk, iddia, ideoloji, faksiyon çağrısı.</summary>
    public static bool CanAIStartJustification(string countryId, string targetId)
    {
        if (!_settingsLoaded) return true; // ayar yoksa eski davranış

        var s = _aiWarSettings.TargetSelection;
        var country = CountryDatabase.Get(countryId);
        var target = CountryDatabase.Get(targetId);
        if (country == null || target == null) return false;

        // Hedef şu koşullardan EN AZ BİRİNİ sağlamalı (OR mantığı):
        // 1) Komşu ülke
        // 2) Toprak iddiası var (yönlü veya ters)
        // 3) Farklı ideoloji
        // 4) Faksiyon çağrısı (bu kontrol CountryAI'de yapılıyor)
        bool ok = false;

        if (s.OnlyNeighbors)
        {
            if (NeighborSystem.AreNeighbors(countryId, targetId)) ok = true;
        }

        if (!ok && s.OnlyWithClaims)
        {
            foreach (var c in _claims)
            {
                if ((c.OwnerId == countryId && c.TargetId == targetId) ||
                    (c.OwnerId == targetId && c.TargetId == countryId))
                {
                    ok = true;
                    break;
                }
            }
        }

        if (!ok && s.OnlyIdeologicalEnemies)
        {
            if (!string.Equals(country.Ideology, target.Ideology, StringComparison.OrdinalIgnoreCase))
                ok = true;
        }

        // Hiçbir kısıtlama aktif değilse (ayarda false ise) her hedefe izin ver
        if (!s.OnlyNeighbors && !s.OnlyWithClaims && !s.OnlyIdeologicalEnemies)
            ok = true;

        return ok;
    }

    /// <summary>AI: gerekçe yoksa ayarlarla sınırlı olasılıkla inşaat başlat.</summary>
    public static void MaybeStartAIJustification(string countryId, string targetId)
    {
        if (!_initialized) return;
        var country = CountryDatabase.Get(countryId);
        var target = CountryDatabase.Get(targetId);
        if (country == null || target == null) return;

        // Zaten geçerli gerekçe veya inşaat varsa yapma
        if (HasValidCasusBelli(countryId, targetId)) return;
        if (_justifications.Any(j => j.CountryId == countryId && j.TargetId == targetId && !j.IsConsumed)) return;

        // Yeni kısıtlamalar: komşuluk/iddia/ideoloji/faksiyon
        if (!CanAIStartJustification(countryId, targetId)) return;

        string typeId = GetBestJustificationTypeForAI(countryId, targetId);
        if (typeId == null) return;

        // Olasılıkla başlat (ayar dosyasından: %2-3)
        double chance = _aiWarSettings.JustificationStartChance;
        if (!DeterministicRNG.NextBool(chance)) return;

        var type = _types[typeId];
        if (country.PoliticalPower < type.PPCost) return;

        StartBuildCase(countryId, targetId, typeId);
    }

    // ===== Getters (UI için) =====

    public static IReadOnlyDictionary<string, CasusBelliType> Types => _types;
    public static IReadOnlyList<TerritorialClaim> Claims => _claims;
    public static IReadOnlyList<Justification> Justifications => _justifications;
    public static IReadOnlyDictionary<string, int> Truces => _truces;
    public static IReadOnlyDictionary<string, float> Legitimacy => _legitimacy;
    public static int UltimatumDays => _ultimatumDays;
    public static int TruceDays => _truceDays;
    public static IReadOnlyDictionary<string, int> WarSupportThresholds => _warSupportThresholds;

    /// <summary>AI eş zamanlı savaş üst sınırı (ai_war_settings.json: MaxAiCountriesAtWarPercent).</summary>
    public static int MaxAiAtWarPercent => _aiWarSettings.MaxAiCountriesAtWarPercent;

    public static CasusBelliType GetType(string id) => _types.GetValueOrDefault(id);

    public static List<TerritorialClaim> GetClaimsBy(string ownerId) =>
        _claims.Where(c => c.OwnerId == ownerId).ToList();

    public static List<TerritorialClaim> GetClaimsOn(string targetId) =>
        _claims.Where(c => c.TargetId == targetId).ToList();

    // ===== M12: Save/Load =====

    public static SaveSystem.CasusBelliSave GetSaveData()
    {
        return new SaveSystem.CasusBelliSave
        {
            Justifications = _justifications.Where(j => !j.IsConsumed || j.BuildDaysLeft > 0 || j.UltimatumDaysLeft > 0).Select(j => new SaveSystem.CasusBelliJustificationSave
            {
                CountryId = j.CountryId,
                TargetId = j.TargetId,
                TypeId = j.TypeId,
                BuildDaysLeft = j.BuildDaysLeft,
                IsAutomatic = j.IsAutomatic,
                Exposed = j.Exposed,
                UltimatumDaysLeft = j.UltimatumDaysLeft,
                IsConsumed = j.IsConsumed
            }).ToArray(),
            Truces = _truces.Select(kv => new SaveSystem.CasusBelliTruceSave { Key = kv.Key, EndDay = kv.Value }).ToArray(),
            Legitimacy = new Dictionary<string, float>(_legitimacy)
        };
    }

    public static void LoadSaveData(SaveSystem.CasusBelliSave data)
    {
        if (data == null) return;
        _justifications.Clear();
        if (data.Justifications != null)
        {
            foreach (var j in data.Justifications)
            {
                _justifications.Add(new Justification
                {
                    CountryId = j.CountryId,
                    TargetId = j.TargetId,
                    TypeId = j.TypeId,
                    BuildDaysLeft = j.BuildDaysLeft,
                    IsAutomatic = j.IsAutomatic,
                    Exposed = j.Exposed,
                    UltimatumDaysLeft = j.UltimatumDaysLeft,
                    IsConsumed = j.IsConsumed
                });
            }
        }
        _truces.Clear();
        if (data.Truces != null)
            foreach (var t in data.Truces) _truces[t.Key] = t.EndDay;
        _legitimacy.Clear();
        if (data.Legitimacy != null)
            foreach (var kv in data.Legitimacy) _legitimacy[kv.Key] = kv.Value;
        _initialized = true;
        GD.Print($"CasusBelliSystem: state yüklendi — Gerekçe={_justifications.Count}, Ateşkes={_truces.Count}, Legitimacy={_legitimacy.Count}");
    }
}