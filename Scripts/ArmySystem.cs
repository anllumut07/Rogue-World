using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Ordu sistemi (M7): tabur tipleri (battalions.json), bölük şablonları,
/// eğitim kuyruğu, takviye, ordular ve generaller.
/// Statik sınıf; GameManager günlük tick'inde çağrılır.
/// </summary>
public static class ArmySystem
{
    // ===== Tabur tipi (battalions.json'dan yüklenir) =====
    public class BattalionType
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }  // "Line" | "Support"
        public int Org { get; set; }
        public int Hp { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int Pierce { get; set; }
        public int Armor { get; set; }
        public int Speed { get; set; }
        public float SupplyUse { get; set; }
        public int Manpower { get; set; }
        public Dictionary<string, int> Equipment { get; set; } = new();
    }

    // Bölük şablonu: tekrarlanan tabur id listesi
    public class DivisionTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> BattalionIds { get; set; } = new(); // "infantry", "artillery"...

        // Hesaplı istatistikler (daha önce GetStats() ile doldurulur)
        public int MaxOrg { get; set; }
        public int MaxHp { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int Pierce { get; set; }
        public int Armor { get; set; }
        public int Speed { get; set; }
        public float SupplyUse { get; set; }
        public int Manpower { get; set; }
        public Dictionary<string, int> Equipment { get; set; } = new();

        // Kullanım kolaylığı için
        public bool IsValid => BattalionIds.Count > 0;
    }

    // Dağıtılmış/dağıtılabilecek bölük
    public class Division
    {
        public string Id { get; set; }
        public string TemplateId { get; set; }
        public string TemplateName { get; set; }
        public int CurrentOrg { get; set; }
        public int MaxOrg { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public string ArmyId { get; set; } = ""; // atanmış ordu
        public int TrainingDaysLeft { get; set; } = 0; // >0 = eğitimde
        public bool IsDeployed => TrainingDaysLeft <= 0;
        public int Retreats { get; set; } = 0; // 3'te yok olma

        public float OrgPercent => MaxOrg > 0 ? (float)CurrentOrg / MaxOrg : 0f;
        public float HpPercent => MaxHp > 0 ? (float)CurrentHp / MaxHp : 0f;

        public string DisplayName => $"{TemplateName} ({Id})";
    }

    // Ordu: genel + bölükler
    public class Army
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string GeneralName { get; set; }
        public int GeneralSkill { get; set; } // 1-5
        public string ProvinceId = ""; // M19.1: ordunun bulunduğu il (harita sayacı konumu)
        public List<string> DivisionIds { get; set; } = new();

        // M19.3: Hareket komutu — hedef il ve Shift ile eklenen ara noktalar (sıra ile)
        public string TargetProvinceId = "";
        public List<string> WaypointIds = new();
        public bool IsMoving => !string.IsNullOrEmpty(TargetProvinceId);

        // M19.4: Hareket ilerleme durumu (günlük il-geçiş, DailyTick ile ilerletilir)
        public int MovementPathIndex;          // WaypointIds yürütme yolundaki şu anki hedef indeksi
        public float DaysPerProvince = 2f;     // bir ilden diğerine geçiş süresi (oyun günü)
        public float DaysRemainingInHop;       // mevcut hop'ta kalan gün
    }

    // Eğitim kuyruğu öğesi
    public class TrainingItem
    {
        public string Id { get; set; }
        public string TemplateId { get; set; }
        public string TemplateName { get; set; }
        public int TotalDays { get; set; }
        public int DaysLeft { get; set; }
    }

    // ===== Sabitler =====
    private const int MaxRetreatsBeforeDestroyed = 3;
    private const float OrgRegenPerDay = 3f;     // ORG günde +3 (maks %100'e kadar)
    private const float HpRegenPerDay = 1f;      // HP günde +1 (ekipman harcar)
    private const int MinOrgFloor = 20;          // şablon ORG floor

    // ===== Senaryo başlangıç verisi (M17.2: army_start.json) =====
    private class ArmyStartData
    {
        public DefaultTemplateData DefaultTemplate = new();
        public Dictionary<string, int> StartingDivisions = new();
    }

    private class DefaultTemplateData
    {
        public string Name = "";
        public List<string> BattalionIds = new();
    }

    private static ArmyStartData _startData;
    private static bool _startLoaded;
    private static bool _startLoadLogged;

    // ===== Durum =====
    private static bool _initialized;
    private static readonly Dictionary<string, BattalionType> _battalions = new();
    private static readonly Dictionary<string, DivisionTemplate> _templates = new();
    private static readonly List<Division> _divisions = new();
    private static readonly List<Army> _armies = new();
    private static readonly List<TrainingItem> _trainingQueue = new();
    private static int _templateCounter;
    private static int _divisionCounter;
    private static int _armyCounter;

    // Rastgele isimler
    private static readonly string[] FirstNames = { "Hans", "Ivan", "George", "Jean", "Hiroshi", "Giuseppe", "Karl", "Oleg", "Bernard", "Fritz", "Dmitri", "Henry", "Antonio", "Vasily", "Friedrich" };
    private static readonly string[] LastNames = { "von Moltke", "Zhukov", "Patton", "de Gaulle", "Yamamoto", "Cadorna", "Guderian", "Rokossovsky", "Montgomery", "Rommel", "Vasilevsky", "Alexander", "Badoglio", "Chuikov", "Manstein" };

    private static GameManager GM => GameManager.Instance;
    private static CountryData Player => GM?.PlayerCountryData;

    // ===== Senaryo başlangıç verisi yükleme (M17.2) =====

    public static void ResetForScenario()
    {
        _startLoaded = false;
        _startLoadLogged = false;
        _startData = null;
    }

    public static void LoadScenarioData()
    {
        EnsureStartLoaded();
        if (!_startLoadLogged)
        {
            _startLoadLogged = true;
            var tpl = _startData?.DefaultTemplate;
            GD.Print($"ArmySystem: senaryo başlangıç verisi yüklendi (senaryo: {ScenarioSystem.ActiveId}) — Şablon='{tpl?.Name ?? "yok"}', Tümen kaydı={_startData?.StartingDivisions.Count ?? 0} ülke");
        }
    }

    private static void EnsureStartLoaded()
    {
        if (_startLoaded) return;
        _startLoaded = true;

        string path = ScenarioSystem.ActiveArmyStartPath;
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr($"ArmySystem: {path} bulunamadı — hardcoded varsayılan kullanılacak");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            var json = Godot.FileAccess.GetFileAsString(path);
            _startData = JsonSerializer.Deserialize<ArmyStartData>(json, options);
        }
        catch (Exception e)
        {
            GD.PrintErr($"ArmySystem: army_start.json okunamadı: {e.Message}");
        }
    }

    // Varsayılan şablon ekipmanlarını getir (EquipmentSystem başlangıç stok hesabı için)
    public static Dictionary<string, int> GetStartTemplateEquipment()
    {
        EnsureLoaded();
        EnsureStartLoaded();
        var tpl = _startData?.DefaultTemplate;
        if (tpl?.BattalionIds == null || tpl.BattalionIds.Count == 0)
        {
            // Hardcoded varsayılanla aynı: 6 infantry, 2 artillery, 1 engineer
            return new Dictionary<string, int>
            {
                ["infantry_equipment"] = 600,
                ["support_equipment"] = 92 // 2*36 + 1*20
            };
        }

        var equip = new Dictionary<string, int>();
        foreach (string bid in tpl.BattalionIds)
        {
            if (_battalions.TryGetValue(bid, out var b))
            {
                foreach (var kv in b.Equipment)
                    equip[kv.Key] = equip.GetValueOrDefault(kv.Key) + kv.Value;
            }
        }
        return equip;
    }

    // Başlangıç tümen sayısını getir (senaryo dosyasından; yoksa ülke verisinden en az 1)
    public static int GetStartDivisionCount(string countryId, int fallbackDivisions)
    {
        EnsureStartLoaded();
        if (_startData?.StartingDivisions != null && _startData.StartingDivisions.TryGetValue(countryId, out var c))
            return Math.Max(1, c);
        return Math.Max(1, fallbackDivisions);
    }

    // ===== Yükleme =====

    private static void EnsureLoaded()
    {
        if (_battalions.Count > 0) return;
        try
        {
            string path = Path.Combine("Data", "battalions.json");
            if (!File.Exists(path))
            {
                GD.PrintErr($"ArmySystem: {path} bulunamadı!");
                return;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var list = JsonSerializer.Deserialize<List<BattalionType>>(File.ReadAllText(path), options);
            foreach (var b in list)
                _battalions[b.Id] = b;
            GD.Print($"ArmySystem: {_battalions.Count} tabur tipi yüklendi");
        }
        catch (Exception e)
        {
            GD.PrintErr($"ArmySystem: battalions.json okunamadı: {e.Message}");
        }
    }

    public static void Initialize(CountryData data)
    {
        EnsureLoaded();
        EnsureStartLoaded();
        _divisions.Clear();
        _armies.Clear();
        _trainingQueue.Clear();
        _templateCounter = _divisionCounter = _armyCounter = 0;

        if (data == null)
        {
            _initialized = false;
            return;
        }

        _initialized = true; // önce aç — CreateTemplate Initialize içinde de kullanılır

        // Varsayılan şablon: senaryo dosyasından (army_start.json) veya hardcoded fallback
        var startTemplate = _startData?.DefaultTemplate;
        List<string> defaultBattalionIds;
        string defaultTemplateName;
        if (startTemplate?.BattalionIds != null && startTemplate.BattalionIds.Count > 0)
        {
            defaultBattalionIds = new List<string>(startTemplate.BattalionIds);
            defaultTemplateName = startTemplate.Name;
        }
        else
        {
            defaultBattalionIds = new List<string> { "infantry", "infantry", "infantry", "infantry", "infantry", "infantry",
                                                  "artillery", "artillery", "engineer" };
            defaultTemplateName = "Infantry Division";
        }
        var defaultTemplate = CreateTemplate(defaultTemplateName, defaultBattalionIds);
        GD.Print($"ArmySystem: Varsayılan şablon '{defaultTemplate.Name}' oluşturuldu (ID={defaultTemplate.Id})");

        // Başlangıç tümenleri: senaryo dosyasından (army_start.json) veya ülke verisinden
        int startDivs = GetStartDivisionCount(data.Id, data.StartingDivisions);
        var template = _templates.Values.FirstOrDefault();
        long manpowerNeeded = 0;
        if (template != null) manpowerNeeded = template.Manpower;

        // Manpower yeterli mi?
        long availableManpower = data.ManpowerPool;
        int actualDivs = startDivs;
        if (manpowerNeeded > 0)
        {
            actualDivs = (int)Math.Min(startDivs, availableManpower / manpowerNeeded);
            if (actualDivs < startDivs)
                GD.Print($"[Army] Manpower yetmez ({availableManpower}/{startDivs * manpowerNeeded}) → {actualDivs} tümen oluşturulacak");
        }

        // Şablon ekipman kontrolü
        if (template != null)
        {
            foreach (var kv in template.Equipment)
            {
                long stock = EquipmentSystem.GetStock(kv.Key);
                if (stock < kv.Value * actualDivs)
                {
                    int possible = (int)(stock / kv.Value);
                    if (possible < actualDivs)
                    {
                        actualDivs = possible;
                        GD.Print($"[Army] Ekipman yetmez ({kv.Key}: stok={stock}, gerek={kv.Value * startDivs}) → {actualDivs} tümen");
                    }
                }
            }
        }

        // Ekipman ve manpower düş
        if (template != null && actualDivs > 0)
        {
            data.ManpowerPool -= actualDivs * template.Manpower;
            foreach (var kv in template.Equipment)
                EquipmentSystem.ConsumeStock(kv.Key, (int)(kv.Value * actualDivs));
        }

        // Tümenleri oluştur
        var army = CreateArmy("1st Army");
        for (int i = 0; i < actualDivs; i++)
        {
            var div = CreateDivision(template.Id);
            div.CurrentOrg = div.MaxOrg;
            div.CurrentHp = div.MaxHp;
            div.ArmyId = army.Id;
            div.TrainingDaysLeft = 0; // dağıtılmış
        }

        _initialized = true;
        GD.Print($"ArmySystem: {data.Id} için hazır — Tümen={actualDivs}, Ordu={_armies.Count}, Şablon={_templates.Count}, Eğitim=0");
    }

    // ===== Günlük tick (GameManager.DailyTick çağırır) =====

    public static void DailyTick()
    {
        if (!_initialized || Player == null) return;

        // 1) Eğitim kuyruğu
        for (int i = _trainingQueue.Count - 1; i >= 0; i--)
        {
            var item = _trainingQueue[i];
            item.DaysLeft--;
            if (item.DaysLeft <= 0)
            {
                // Eğitim tamam → tümen oluştur
                var div = CreateDivision(item.TemplateId);
                div.CurrentOrg = div.MaxOrg;
                div.CurrentHp = div.MaxHp;
                // İlk ordu yoksa oluştur
                if (_armies.Count == 0) CreateArmy("1st Army");
                // Boş orduya at
                var targetArmy = _armies.FirstOrDefault(a => a.DivisionIds.Count < 24) ?? _armies[0];
                div.ArmyId = targetArmy.Id;
                targetArmy.DivisionIds.Add(div.Id);

                GD.Print($"[Army] Eğitim tamam: {item.TemplateName} → {div.DisplayName} (ordu: {targetArmy.Name})");
                _trainingQueue.RemoveAt(i);
            }
        }

        // 2) Takviye (reinforcement): dağıtılmış tümenler
        foreach (var div in _divisions.Where(d => d.IsDeployed))
        {
            // ORG regenerasyonu (her zaman)
            if (div.CurrentOrg < div.MaxOrg)
            {
                div.CurrentOrg = Math.Min(div.MaxOrg, div.CurrentOrg + (int)OrgRegenPerDay);
            }

            // HP regenerasyonu (ekipman harcar)
            if (div.CurrentHp < div.MaxHp)
            {
                var template = _templates.GetValueOrDefault(div.TemplateId);
                if (template != null)
                {
                    bool hasEquipment = true;
                    foreach (var kv in template.Equipment)
                    {
                        if (EquipmentSystem.GetStock(kv.Key) < kv.Value) { hasEquipment = false; break; }
                    }

                    if (hasEquipment)
                    {
                        div.CurrentHp = Math.Min(div.MaxHp, div.CurrentHp + (int)HpRegenPerDay);
                        // Ekipman tüket (basit: her HP için %1 eşdeğer)
                        foreach (var kv in template.Equipment)
                        {
                            int consume = Math.Max(1, kv.Value / 100); // her 100 HP için 1 ekipman
                            EquipmentSystem.ConsumeStock(kv.Key, consume);
                        }
                    }
                }
            }
        }

        // Ayda bir özet
        if (GM.TotalDays > 0 && GM.TotalDays % 30 == 0)
        {
            int deployed = _divisions.Count(d => d.IsDeployed);
            int training = _trainingQueue.Count;
            GD.Print($"[Army] Gün {GM.TotalDays}: Dağıtık={deployed}, Eğitim={training}, Ordu={_armies.Count}, " +
                     $"Manpower={Player.ManpowerPool:N0}");
        }

        // M19.4 + M19.5: Ordular rotada günlük adım adım ilerler; düşman temasında hareket durur.
        foreach (var army in _armies.Where(a => a.IsMoving).ToList())
        {
            army.DaysRemainingInHop -= 1f;
            if (army.DaysRemainingInHop <= 0f)
            {
                // Mevcut hop tamamlandı → WaypointIds[MovementPathIndex]'ine vardık.
                army.ProvinceId = army.WaypointIds[army.MovementPathIndex];
                army.MovementPathIndex++;

                // M19.5: Düşman kontrolündeki bir ile vardıysa hareket durur ve WarSystem çatışması devreye girer.
                if (OnArmyContactEnemy(army, army.ProvinceId))
                {
                    // Hareket durduruldu; bu ordunun rotası bitti.
                    continue;
                }

                if (army.MovementPathIndex < army.WaypointIds.Count)
                {
                    army.DaysRemainingInHop = army.DaysPerProvince;
                    GD.Print($"[Army] {army.Name} {army.ProvinceId} iline vardı (rota {army.MovementPathIndex}/{army.WaypointIds.Count})");
                }
                else
                {
                    // Final hedefe ulaştı.
                    army.TargetProvinceId = "";
                    army.WaypointIds.Clear();
                    army.MovementPathIndex = 0;
                    GD.Print($"[Army] {army.Name} hedefe ulaştı: {army.ProvinceId}");
                }
            }
        }
    }

    // M19.5: Ordunun belirtilen İL'de düşmanla karşılaşmış mı? Eğerse hareket durur.
    // Dönüş: true = hareket durduruldu (tema/cavatış), false = devam edilebilir.
    private static bool OnArmyContactEnemy(Army army, string provinceId)
    {
        if (Player == null) return false;
        string owner = ProvinceOwnershipSystem.GetProvinceOwner(provinceId);
        if (string.IsNullOrEmpty(owner) || owner == "?") return false;
        if (owner == Player.Id) return false; // kendi toprak, devam et

        if (WarSystem.IsAtWarWith(owner))
        {
            army.TargetProvinceId = "";
            army.WaypointIds.Clear();
            army.MovementPathIndex = 0;
            GD.Print($"[Army] {army.Name} düşman toppedu: {provinceId} ({owner}) — hareket durduruldu, WarSystem çatışma çözümü aktif");
            return true;
        }

        // Düşman toprak ama henüz savaş yok → durdur, temas logla
        army.TargetProvinceId = "";
        army.WaypointIds.Clear();
        army.MovementPathIndex = 0;
        GD.Print($"[Army] {army.Name} {owner} ilinin {provinceId} bölgesinde — tema (savaş ilanı ile çatışma başlatılabilir)");
        return true;
    }

    // ===== Şablon hesaplama =====

    // Şablon oluştur ve istatistikleri hesapla
    public static DivisionTemplate CreateTemplate(string name, List<string> battalionIds)
    {
        if (!_initialized) return null;
        _templateCounter++;
        string id = $"tpl_{_templateCounter}";

        var template = new DivisionTemplate
        {
            Id = id,
            Name = name,
            BattalionIds = new List<string>(battalionIds)
        };

        ComputeTemplateStats(template);
        _templates[id] = template;
        GD.Print($"[Army] Şablon oluşturuldu: {template.Name} (ID={id}, Tabur={template.BattalionIds.Count}, " +
                 $"ORG={template.MaxOrg}, HP={template.MaxHp}, ATT={template.Attack}, DEF={template.Defense}, " +
                 $"Spd={template.Speed}, Manpower={template.Manpower})");
        return template;
    }

    // Tabur listesinden şablon istatistiklerini hesapla
    public static void ComputeTemplateStats(DivisionTemplate t)
    {
        if (t.BattalionIds.Count == 0)
        {
            t.MaxOrg = MinOrgFloor; t.MaxHp = 0; t.Attack = 0; t.Defense = 0;
            t.Pierce = 0; t.Armor = 0; t.Speed = 4; t.SupplyUse = 0; t.Manpower = 0;
            t.Equipment.Clear();
            return;
        }

        int totalOrg = 0, totalHp = 0, totalAtt = 0, totalDef = 0, totalPierce = 0, totalArmor = 0;
        int minSpeed = int.MaxValue;
        float totalSup = 0f;
        int totalMan = 0;
        var equip = new Dictionary<string, int>();

        foreach (string bid in t.BattalionIds)
        {
            if (!_battalions.TryGetValue(bid, out var b)) continue;
            totalOrg += b.Org;
            totalHp += b.Hp;
            totalAtt += b.Attack;
            totalDef += b.Defense;
            totalPierce += b.Pierce;
            totalArmor += b.Armor;
            minSpeed = Math.Min(minSpeed, b.Speed);
            totalSup += b.SupplyUse;
            totalMan += b.Manpower;
            foreach (var kv in b.Equipment)
                equip[kv.Key] = equip.GetValueOrDefault(kv.Key) + kv.Value;
        }

        int count = t.BattalionIds.Count;
        int avgOrg = count > 0 ? totalOrg / count : 0;
        t.MaxOrg = Math.Max(MinOrgFloor, MinOrgFloor + (int)(avgOrg * 0.7));
        t.MaxHp = totalHp; // HOI4: bölük HP = tabur HP toplamı
        t.Attack = totalAtt;
        t.Defense = totalDef;
        t.Pierce = totalPierce;
        t.Armor = totalArmor;
        t.Speed = minSpeed == int.MaxValue ? 4 : minSpeed;
        t.SupplyUse = totalSup;
        t.Manpower = totalMan;
        t.Equipment = equip;
    }

    // Şablon kopyala (varyant için)
    public static DivisionTemplate DuplicateTemplate(string templateId, string newName)
    {
        if (!_templates.TryGetValue(templateId, out var src)) return null;
        var copy = new DivisionTemplate
        {
            Id = "", // CreateTemplate atayacak
            Name = newName,
            BattalionIds = new List<string>(src.BattalionIds)
        };
        return CreateTemplate(copy.Name, copy.BattalionIds);
    }

    // ===== Tümen oluşturma =====

    // Şablondan yeni tümen (eğitimde başlar veya anında dağıtık)
    public static Division CreateDivision(string templateId, bool immediateDeploy = false)
    {
        if (!_templates.TryGetValue(templateId, out var t)) return null;

        _divisionCounter++;
        string id = $"div_{_divisionCounter}";

        var div = new Division
        {
            Id = id,
            TemplateId = templateId,
            TemplateName = t.Name,
            MaxOrg = t.MaxOrg,
            MaxHp = t.MaxHp,
            CurrentOrg = immediateDeploy ? t.MaxOrg : 0,
            CurrentHp = immediateDeploy ? t.MaxHp : 0,
            TrainingDaysLeft = immediateDeploy ? 0 : CalculateTrainingDays(t)
        };

        _divisions.Add(div);
        return div;
    }

    private static int CalculateTrainingDays(DivisionTemplate t)
    {
        // HOI4: ~90-120 gün; basit: 30 + 3×tabur
        return 30 + t.BattalionIds.Count * 3;
    }

    // ===== Eğitim API =====

    // Eğitime al (maliyet ödenir: manpower + ekipman)
    public static bool StartTraining(string templateId)
    {
        if (!_initialized || !_templates.TryGetValue(templateId, out var t)) return false;

        // Maliyet kontrolü
        if (Player.ManpowerPool < t.Manpower)
        {
            GD.Print($"[Army] Eğitim başlatılamadı: Manpower yetersiz ({Player.ManpowerPool}/{t.Manpower})");
            return false;
        }

        foreach (var kv in t.Equipment)
        {
            if (EquipmentSystem.GetStock(kv.Key) < kv.Value)
            {
                GD.Print($"[Army] Eğitim başlatılamadı: {kv.Key} stok yetersiz ({EquipmentSystem.GetStock(kv.Key)}/{kv.Value})");
                return false;
            }
        }

        // Maliyeti öde
        Player.ManpowerPool -= t.Manpower;
        foreach (var kv in t.Equipment)
            EquipmentSystem.ConsumeStock(kv.Key, kv.Value);

        var item = new TrainingItem
        {
            Id = $"train_{Guid.NewGuid().ToString("N")[..8]}",
            TemplateId = templateId,
            TemplateName = t.Name,
            TotalDays = CalculateTrainingDays(t),
            DaysLeft = CalculateTrainingDays(t)
        };
        _trainingQueue.Add(item);
        GD.Print($"[Army] Eğitim başladı: {t.Name} ({item.TotalDays} gün) — Manpower={t.Manpower}, Ekipman={string.Join(", ", t.Equipment.Select(kv => $"{kv.Key}×{kv.Value}"))}");
        return true;
    }

    public static void CancelTraining(string trainingId)
    {
        var item = _trainingQueue.Find(t => t.Id == trainingId);
        if (item == null) return;
        // Maliyeti geri ver (HOI4: iptal = %50 geri? Basit: tam geri)
        var tpl = _templates.GetValueOrDefault(item.TemplateId);
        if (tpl != null)
        {
            Player.ManpowerPool += tpl.Manpower;
            foreach (var kv in tpl.Equipment)
                EquipmentSystem.AddToStock(kv.Key, kv.Value);
        }
        _trainingQueue.Remove(item);
        GD.Print($"[Army] Eğitim iptal: {item.TemplateName} — maliyet geri verildi");
    }

    // ===== Ordu API =====

    public static Army CreateArmy(string baseName = "")
    {
        _armyCounter++;
        string name = string.IsNullOrEmpty(baseName) ? $"Army {_armyCounter}" : baseName;
        if (_armies.Any(a => a.Name == name)) name += $" {_armyCounter}";

        var army = new Army
        {
            Id = $"army_{_armyCounter}",
            Name = name,
            GeneralName = GenerateGeneralName(),
            GeneralSkill = DeterministicRNG.NextInt(1, 6)
        };
        _armies.Add(army);
        GD.Print($"[Army] Ordu oluşturuldu: {army.Name} (Gen. {army.GeneralName}, Skill={army.GeneralSkill})");
        return army;
    }

    public static void AssignDivisionToArmy(string divisionId, string armyId)
    {
        var div = _divisions.Find(d => d.Id == divisionId);
        var army = _armies.Find(a => a.Id == armyId);
        if (div == null || army == null) return;

        // Eski ordundan çıkar
        if (!string.IsNullOrEmpty(div.ArmyId))
        {
            var oldArmy = _armies.Find(a => a.Id == div.ArmyId);
            oldArmy?.DivisionIds.Remove(div.Id);
        }
        div.ArmyId = armyId;
        army.DivisionIds.Add(div.Id);
    }

    public static void RemoveArmy(string armyId)
    {
        var army = _armies.Find(a => a.Id == armyId);
        if (army == null) return;
        foreach (var divId in army.DivisionIds)
        {
            var div = _divisions.Find(d => d.Id == divId);
            if (div != null) div.ArmyId = "";
        }
        _armies.Remove(army);
        GD.Print($"[Army] Ordu kaldırıldı: {army.Name} — tümenler atasız kaldı");
    }

    // Tümeni tamamen yok et (savaş kaybı): listeden ve ordusundan çıkar (WarSystem çağırır)
    public static bool DestroyDivision(string divisionId)
    {
        var div = _divisions.Find(d => d.Id == divisionId);
        if (div == null) return false;

        if (!string.IsNullOrEmpty(div.ArmyId))
        {
            var army = _armies.Find(a => a.Id == div.ArmyId);
            army?.DivisionIds.Remove(div.Id);
        }
        _divisions.Remove(div);
        GD.Print($"[Army] Tümen listeden kaldırıldı: {div.DisplayName} (kalan: {_divisions.Count})");
        return true;
    }

    private static string GenerateGeneralName()
    {
        return $"{FirstNames[DeterministicRNG.NextInt(0, FirstNames.Length)]} {LastNames[DeterministicRNG.NextInt(0, LastNames.Length)]}";
    }

    // ===== Getters (UI için) =====

    public static bool IsInitialized => _initialized;
    public static IReadOnlyDictionary<string, BattalionType> AllBattalions => _battalions;
    public static IReadOnlyDictionary<string, DivisionTemplate> Templates => _templates;
    public static IReadOnlyList<Division> Divisions => _divisions;
    public static IReadOnlyList<Army> Armies => _armies;
    public static IReadOnlyList<TrainingItem> TrainingQueue => _trainingQueue;

    public static BattalionType GetBattalion(string id) => _battalions.GetValueOrDefault(id);

    public static DivisionTemplate GetTemplate(string id) => _templates.GetValueOrDefault(id);

    public static List<Division> GetDivisionsInArmy(string armyId) =>
        _divisions.Where(d => d.ArmyId == armyId).ToList();

    public static List<Division> GetUnassignedDivisions() =>
        _divisions.Where(d => d.IsDeployed && string.IsNullOrEmpty(d.ArmyId)).ToList();

    public static int TotalDivisions => _divisions.Count;
    public static int DeployedDivisions => _divisions.Count(d => d.IsDeployed);
    public static int TrainingDivisions => _trainingQueue.Count;

    // Oyuncu ülkesinin savaş gücü (diplomasi/AI için)
    public static float GetMilitaryStrength()
    {
        if (!_initialized) return 0;
        float str = 0;
        foreach (var div in _divisions.Where(d => d.IsDeployed))
        {
            var tpl = _templates.GetValueOrDefault(div.TemplateId);
            if (tpl == null) continue;
            str += div.MaxOrg * 0.1f + tpl.Attack * 0.5f + tpl.Defense * 0.3f;
        }
        return str;
    }

    // ===== M12: Save/Load =====

    public static SaveSystem.ArmySave GetSaveData()
    {
        return new SaveSystem.ArmySave
        {
            TemplateCounter = _templateCounter,
            DivisionCounter = _divisionCounter,
            ArmyCounter = _armyCounter,
            Templates = _templates.Values.ToArray(),
            Divisions = _divisions.ToArray(),
            Armies = _armies.ToArray(),
            TrainingQueue = _trainingQueue.Select(t => new SaveSystem.TrainingItemSave
            {
                Id = t.Id,
                TemplateId = t.TemplateId,
                TemplateName = t.TemplateName,
                TotalDays = t.TotalDays,
                DaysLeft = t.DaysLeft
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.ArmySave data)
    {
        if (data == null) return;
        EnsureLoaded();
        _templateCounter = data.TemplateCounter;
        _divisionCounter = data.DivisionCounter;
        _armyCounter = data.ArmyCounter;

        _templates.Clear();
        if (data.Templates != null)
            foreach (var t in data.Templates)
            {
                // İstatistikleri yeniden hesapla (JSON'da tutulan BattalionIds'ten türetilir)
                ComputeTemplateStats(t);
                _templates[t.Id] = t;
            }

        _divisions.Clear();
        if (data.Divisions != null)
            foreach (var d in data.Divisions) _divisions.Add(d);

        _armies.Clear();
        if (data.Armies != null)
            foreach (var a in data.Armies) _armies.Add(a);

        _trainingQueue.Clear();
        if (data.TrainingQueue != null)
            foreach (var t in data.TrainingQueue)
                _trainingQueue.Add(new TrainingItem
                {
                    Id = t.Id,
                    TemplateId = t.TemplateId,
                    TemplateName = t.TemplateName,
                    TotalDays = t.TotalDays,
                    DaysLeft = t.DaysLeft
                });

        _initialized = true;
        GD.Print($"ArmySystem: state yüklendi — Şablon={_templates.Count}, Tümen={_divisions.Count}, Ordu={_armies.Count}, Eğitim={_trainingQueue.Count}");
    }
}