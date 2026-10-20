using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// M37: Uluslararası örgütler (NATO, AB, BM, BRICS, ŞİO, AU, ASEAN, Arab Birliği, OPEC).
// Üyelik yönetimi, Madde 5 kolektif savunma, toplantı gündemi, AI oy verme.
// Kurgusal içerik, gerçek kişi adı yok. Tüm veri Data/organizations.json'dan gelir.
public static class OrganizationSystem
{
    private static readonly string DataPath = "res://Data/organizations.json";

    public enum OrgType { Askeri, Politik, Ekonomik, Karma }

    public class OrganizationDef
    {
        public string Id;
        public string Name;
        public string Type;
        public string FoundedDate;
        public string HeadquartersCountryId;
        public string[] Members = Array.Empty<string>();
        public string[] SpecialRules = Array.Empty<string>();
    }

    private class OrganizationList { public OrganizationDef[] Organizations = Array.Empty<OrganizationDef>(); }

    // id -> tanım
    private static readonly Dictionary<string, OrganizationDef> _orgs = new(StringComparer.OrdinalIgnoreCase);
    // orgId -> mevcut üye ülke kümeleri
    private static readonly Dictionary<string, HashSet<string>> _members = new(StringComparer.OrdinalIgnoreCase);
    // ülkeId -> üyesi olduğu org kümeleri (ters lookup)
    private static readonly Dictionary<string, HashSet<string>> _countryOrgs = new(StringComparer.OrdinalIgnoreCase);

    // Madde 5 kolektif-savunma etkinleri: orgId -> (saldırılan ülke, saldırgan ülke, gün)
    private static readonly List<(string OrgId, string AttackedCountryId, string AttackerCountryId, long Day)> _article5Activations = new();

    public static int OrganizationCount => _orgs.Count;
    public static IReadOnlyList<string> OrganizationIds => _orgs.Keys.ToList();

    public static OrganizationDef Get(string orgId) => _orgs.TryGetValue(orgId, out var d) ? d : null;

    public static OrgType GetOrgType(string orgId)
    {
        if (Get(orgId) is { } d)
        {
            return d.Type switch
            {
                "Askeri" => OrgType.Askeri,
                "Ekonomik" => OrgType.Ekonomik,
                "Karma" => OrgType.Karma,
                _ => OrgType.Politik
            };
        }
        return OrgType.Politik;
    }

    public static bool IsMember(string orgId, string countryId)
    {
        if (string.IsNullOrEmpty(orgId) || string.IsNullOrEmpty(countryId)) return false;
        return _members.TryGetValue(orgId, out var set) && set.Contains(countryId);
    }

    public static IReadOnlyList<string> Members(string orgId)
    {
        return _members.TryGetValue(orgId, out var set) ? set.OrderBy(m => m).ToList() : Array.Empty<string>();
    }

    public static IReadOnlyList<string> CountryOrgs(string countryId)
    {
        return _countryOrgs.TryGetValue(countryId, out var os) ? os.OrderBy(o => o).ToList() : Array.Empty<string>();
    }

    public static IReadOnlyList<(string OrgId, string AttackedCountryId, string AttackerCountryId, long Day)> Article5Activations => _article5Activations;

    /// <summary>Veri dosyasını yükler (IDempotent: temizler ve tekrar yükler).</summary>
    public static void LoadData()
    {
        _orgs.Clear();
        _members.Clear();
        _countryOrgs.Clear();

        try
        {
            if (!Godot.FileAccess.FileExists(DataPath))
            {
                GD.PrintErr($"[OrganizationSystem] Veri dosyası yok: {DataPath}");
                GD.Print("[OrganizationSystem] 0 uluslararası örgüt yüklendi.");
                return;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            var json = Godot.FileAccess.GetFileAsString(DataPath);
            var wrapper = JsonSerializer.Deserialize<OrganizationList>(json, options);
            if (wrapper?.Organizations == null)
            {
                GD.Print("[OrganizationSystem] 0 uluslararası örgüt yüklendi.");
                return;
            }

            int loaded = 0;
            foreach (var def in wrapper.Organizations)
            {
                if (string.IsNullOrEmpty(def.Id)) continue;
                _orgs[def.Id] = def;
                var set = new HashSet<string>(def.Members ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _members[def.Id] = set;
                foreach (var m in set)
                {
                    if (!_countryOrgs.TryGetValue(m, out var os))
                    {
                        os = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _countryOrgs[m] = os;
                    }
                    os.Add(def.Id);
                }
                loaded++;
            }

            GD.Print($"[OrganizationSystem] {loaded} uluslararası örgüt yüklendi ({DataPath}, IncludeFields=true).");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[OrganizationSystem] organizations.json okunamadı: {e.Message}");
        }
    }

    /// <summary>Ülkeyi örgüte üye yapar (zaten üyedeyse no-op).</summary>
    public static bool AddMember(string orgId, string countryId)
    {
        if (string.IsNullOrEmpty(orgId) || string.IsNullOrEmpty(countryId) || !_orgs.ContainsKey(orgId)) return false;
        if (!_members.TryGetValue(orgId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _members[orgId] = set;
        }
        bool added = set.Add(countryId);
        if (added)
        {
            if (!_countryOrgs.TryGetValue(countryId, out var os))
            {
                os = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _countryOrgs[countryId] = os;
            }
            os.Add(orgId);
            GD.Print($"[OrganizationSystem] {countryId} -> {orgId} üyesi oldu.");
        }
        return added;
    }

    /// <summary>Ülkeyi örgütten çıkarır.</summary>
    public static bool RemoveMember(string orgId, string countryId)
    {
        if (string.IsNullOrEmpty(orgId) || string.IsNullOrEmpty(countryId)) return false;
        bool removed = false;
        if (_members.TryGetValue(orgId, out var set)) removed = set.Remove(countryId);
        if (_countryOrgs.TryGetValue(countryId, out var os)) os.Remove(orgId);
        if (removed) GD.Print($"[OrganizationSystem] {countryId} -> {orgId} üyesiyeti sona erdi.");
        return removed;
    }

    /// <summary>Madde 5 kolektif savunma: bir üye saldırıya uğarsa tüm üyeler saldırganla savaş girişimine geçer.</summary>
    public static void TriggerArticle5(string attackedCountryId, string attackerCountryId)
    {
        if (string.IsNullOrEmpty(attackedCountryId) || string.IsNullOrEmpty(attackerCountryId)) return;

        foreach (var kv in _orgs)
        {
            var orgId = kv.Key;
            if (GetOrgType(orgId) != OrgType.Askeri) continue;
            if (!IsMember(orgId, attackedCountryId)) continue;

            _article5Activations.Add((orgId, attackedCountryId, attackerCountryId, GameManager.Instance?.TotalDays ?? 0));
            GD.Print($"[OrganizationSystem] MADDE 5 TETİKLENDİ: {orgId} — {attackedCountryId} saldırıya uğradı, tüm üyeler {attackerCountryId}'e karşı oorloga geçiyor.");

            // Her üye saldırganla savaş haline girer (PP/sert önkoşullar WarSystem tarafında değerlendirilir).
            foreach (var member in Members(orgId))
            {
                if (member.Equals(attackerCountryId, StringComparison.OrdinalIgnoreCase)) continue;
                if (member.Equals(attackedCountryId, StringComparison.OrdinalIgnoreCase)) continue;
                TryDeclareWar(member, attackerCountryId);
            }
        }
    }

    private static void TryDeclareWar(string memberId, string targetId)
    {
        try
        {
            WarSystem.DeclareWar(memberId, targetId);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[OrganizationSystem] {memberId} -> {targetId} beyanı başarısız: {e.Message}");
        }
    }

    /// <summary>AI'nin bir gündem maddesine oy verme ağırlığı (0-1, deterministik).</summary>
    public static double AIVoteWeight(string orgId, string countryId, string agendaItemId)
    {
        double weight = 0.5;
        if (!string.IsNullOrEmpty(orgId) && !string.IsNullOrEmpty(countryId) && IsMember(orgId, countryId)) weight = 0.75;
        else if (!string.IsNullOrEmpty(orgId)) weight = 0.25;
        // Kurgusal blok sadakati: deterministik, gerçek ülke/lider adı yok.
        return Math.Clamp(weight, 0, 1);
    }

    /// <summary>Günlük toplantı denetimi (zirve = yıllık, bakanlar = aylık).</summary>
    public static void DailyTick(long totalDays)
    {
        if (totalDays % 365 == 0 && totalDays > 0)
            GD.Print($"[OrganizationSystem] Gün {totalDays}: yıllık örgüt zirveleri planlanabilir.");
        if (totalDays % 30 == 0 && totalDays > 0)
            GD.Print($"[OrganizationSystem] Gün {totalDays}: aylık bakanlar toplantısı gündemi açılabilir (yaptırım, bütçe, genişleme).");
    }

    public static void ResetForNewGame()
    {
        _article5Activations.Clear();
        // Üyelikler veri dosyasındaki başlangıç durumuna döner (LoadData çağrısı GameManager tarafında).
        GD.Print("[OrganizationSystem] Yeni oyun için sıfırlandı.");
    }

    // ===== Save / Load =====
    public class OrgMembershipSave
    {
        public string OrgId;
        public string CountryId;
    }

    public class Article5ActivationSave
    {
        public string OrgId;
        public string AttackedCountryId;
        public string AttackerCountryId;
        public long Day;
    }

    public class OrganizationSave
    {
        public OrgMembershipSave[] Memberships = Array.Empty<OrgMembershipSave>();
        public Article5ActivationSave[] Article5Activations = Array.Empty<Article5ActivationSave>();
    }

    public static OrganizationSave GetSaveData()
    {
        var memberships = new List<OrgMembershipSave>();
        foreach (var kv in _members)
            foreach (var c in kv.Value.OrderBy(m => m))
                memberships.Add(new OrgMembershipSave { OrgId = kv.Key, CountryId = c });

        return new OrganizationSave
        {
            Memberships = memberships.ToArray(),
            Article5Activations = _article5Activations.Select(a => new Article5ActivationSave
            {
                OrgId = a.OrgId,
                AttackedCountryId = a.AttackedCountryId,
                AttackerCountryId = a.AttackerCountryId,
                Day = a.Day
            }).ToArray()
        };
    }

    public static void LoadSaveData(OrganizationSave save)
    {
        _members.Clear();
        _countryOrgs.Clear();
        _article5Activations.Clear();

        if (save == null) return;

        if (save.Memberships != null)
        {
            foreach (var m in save.Memberships)
            {
                if (string.IsNullOrEmpty(m.OrgId) || string.IsNullOrEmpty(m.CountryId)) continue;
                AddMember(m.OrgId, m.CountryId);
            }
        }

        if (save.Article5Activations != null)
        {
            foreach (var a in save.Article5Activations)
                _article5Activations.Add((a.OrgId, a.AttackedCountryId, a.AttackerCountryId, a.Day));
        }

        GD.Print($"[OrganizationSystem] State yüklendi — üyelik={_members.Sum(kv => kv.Value.Count)}, Madde5={_article5Activations.Count}");
    }
}
