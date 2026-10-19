using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// M28.2: Gündem Paneli — kurgusal sosyal medya profilleri.
/// - Gerçek kişi adı KULLANILMAZ (Faz 2 kuralı): ad-soyad Data/names/*.json havuzlarından,
///   ülke kültürüne göre üretilir (kurgusal).
/// - Rastgelelik YALNIZCA deterministik: lokal xorshift64* (LocalRng), global
///   DeterministicRNG state'ini kirletmez (LeaderSystem pattern'i).
/// - Avatar: renkli baş harf (AvatarColor hex + baş harf); UI renkli daire ile render eder.
/// - Türler: Citizen (vatandaş), Journalist (gazeteci), Opposition (muhalefet),
///   Official (resmî hesap), Bot.
/// - Kaydedilebilir: GetSaveData/LoadSaveData/ResetForNewGame.
/// UI metinleri İngilizce, yorumlar Türkçe.
/// </summary>
public static class SocialProfileSystem
{
    public enum ProfileType { Citizen, Journalist, Opposition, Official, Bot }

    public class SocialProfile
    {
        public string Id { get; set; } = "";          // "profile-{country}-{n}"
        public string Name { get; set; } = "";        // kurgusal ad-soyad
        public string Handle { get; set; } = "";      // @kullanici
        public ProfileType Type { get; set; } = ProfileType.Citizen;
        public string CountryId { get; set; } = "";   // kültür kaynağı olan ülke
        public string AvatarColor { get; set; } = ""; // "#RRGGBB" — renkli baş harf
        public int Followers { get; set; }
        public bool Verified { get; set; }            // resmî hesaplar
    }

    private class NamePool
    {
        public List<string> FirstNames { get; set; } = new();
        public List<string> LastNames { get; set; } = new();
    }

    // Lokal deterministik RNG (xorshift64*, LeaderSystem.LocalRng ile aynı çekirdek;
    // global DeterministicRNG'i kirletmez — profil üretimi oyun RNG akışını bozmaz).
    private struct LocalRng
    {
        private ulong _state;
        public LocalRng(ulong seed) { _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }
        public ulong NextUlong()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }
        public int NextInt(int min, int max)
        {
            if (min >= max) return min;
            return min + (int)((NextUlong() >> 33) % (ulong)(max - min));
        }
        public double NextDouble() => (NextUlong() >> 11) * (1.0 / 9007199254740992.0);
    }

    // ===== Durum =====
    private static readonly Dictionary<string, NamePool> _namePools = new();
    private static readonly List<SocialProfile> _profiles = new(); // tüm profiller
    private static readonly Dictionary<string, LocalRng> _authorRng = new(); // ülke bazlı yazar RNG'i
    private static readonly HashSet<string> _usedHandles = new();
    private static bool _loaded;

    // Her ülke için üretilen profil sayısı (türden bazen fazlası)
    private const int ProfilesPerCountry = 8;

    // ===== Public API =====

    public static int ProfileCount => _profiles.Count;
    public static IReadOnlyList<SocialProfile> All => _profiles;

    public static IReadOnlyList<SocialProfile> GetProfiles(string countryId)
    {
        EnsureLoaded();
        return _profiles.Where(p => p.CountryId.Equals(countryId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // Tweet yazarlığı için deterministik bir yazar döndürür. Aynı ülke ve aynı
    // senaryoda her çağrıda farklı (ilerleyen RNG), ama iki oynanışta (A==B) aynı sırayla üretilir.
    public static (string Name, string Handle) GetRandomAuthor(string countryId)
    {
        EnsureLoaded();
        var countryProfiles = GetProfiles(countryId);
        if (countryProfiles.Count == 0)
            return ("News Desk", "@news_desk"); // yedek

        if (!_authorRng.TryGetValue(countryId, out var rng))
        {
            rng = new LocalRng(HashSeed($"{ScenarioSystem.ActiveId}:{countryId}:author"));
            _authorRng[countryId] = rng;
        }

        var p = countryProfiles[rng.NextInt(0, countryProfiles.Count)];
        return (p.Name, p.Handle);
    }

    // ===== Senaryo / yeni oyun sıfırlama =====

    public static void ResetForNewGame()
    {
        _profiles.Clear();
        _authorRng.Clear();
        _usedHandles.Clear();
        _loaded = false;
        GD.Print("SocialProfile: yeni oyun için sıfırlandı.");
    }

    public static void ResetForScenario()
    {
        ResetForNewGame();
    }

    // ===== Save/Load =====

    public static SaveSystem.SocialProfileSave GetSaveData()
    {
        return new SaveSystem.SocialProfileSave
        {
            Profiles = _profiles.Select(p => new SocialProfile
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Type = p.Type,
                CountryId = p.CountryId,
                AvatarColor = p.AvatarColor,
                Followers = p.Followers,
                Verified = p.Verified,
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.SocialProfileSave data)
    {
        _profiles.Clear();
        _authorRng.Clear();
        _usedHandles.Clear();
        if (data?.Profiles != null)
        {
            foreach (var p in data.Profiles)
                if (!string.IsNullOrEmpty(p.Id))
                    _profiles.Add(p);
        }
        _loaded = true;
        GD.Print($"SocialProfile: {_profiles.Count} profil state'i yüklendi (save/load)");
    }

    // ===== Yükleme / üretim =====

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        LoadNamePools();
        GD.Print($"SocialProfile: {_namePools.Count} isim havuzu yüklendi ({string.Join(", ", _namePools.Keys.OrderBy(k => k))})");
    }

    // Belirli bir ülke için profil havuzunu üretir (lazy, ilk ihtiyaçta).
    public static void EnsureProfilesFor(string countryId)
    {
        EnsureLoaded();
        if (_profiles.Any(p => p.CountryId.Equals(countryId, StringComparison.OrdinalIgnoreCase)))
            return; // zaten üretilmiş

        string cultureKey = LeaderSystem.GetCultureKey(countryId);
        var pool = _namePools.GetValueOrDefault(cultureKey)
                   ?? _namePools.GetValueOrDefault("anglo");

        var rng = new LocalRng(HashSeed($"{ScenarioSystem.ActiveId}:{countryId}:profiles"));
        int created = 0;

        // Tür dağılımı: 3 citizen, 2 journalist, 1 opposition, 1 official, 1 bot
        var typePlan = new[]
        {
            ProfileType.Citizen, ProfileType.Citizen, ProfileType.Citizen,
            ProfileType.Journalist, ProfileType.Journalist,
            ProfileType.Opposition,
            ProfileType.Official,
            ProfileType.Bot,
        };

        foreach (var type in typePlan)
        {
            string firstName, lastName;
            if (pool != null && pool.FirstNames.Count > 0 && pool.LastNames.Count > 0)
            {
                firstName = pool.FirstNames[rng.NextInt(0, pool.FirstNames.Count)];
                lastName = pool.LastNames[rng.NextInt(0, pool.LastNames.Count)];
            }
            else
            {
                firstName = "Alex";
                lastName = countryId;
            }

            string handle = MakeHandle(firstName, lastName, type, rng);
            string avatar = $"#{rng.NextInt(0, 256):X2}{rng.NextInt(0, 256):X2}{rng.NextInt(0, 256):X2}";
            int followers = FollowersFor(type, rng);
            bool verified = type == ProfileType.Official || (type == ProfileType.Journalist && rng.NextDouble() < 0.15);

            _profiles.Add(new SocialProfile
            {
                Id = $"profile-{countryId}-{created++}",
                Name = $"{firstName} {lastName}",
                Handle = handle,
                Type = type,
                CountryId = countryId,
                AvatarColor = avatar,
                Followers = followers,
                Verified = verified,
            });
        }

        // Yazar RNG'i bu ülke için başlat (profil oluşturma RNG'sinden bağımsız)
        _authorRng[countryId] = new LocalRng(HashSeed($"{ScenarioSystem.ActiveId}:{countryId}:author"));

        GD.Print($"SocialProfile: {countryId} ({cultureKey}) için {_profiles.Count(p => p.CountryId.Equals(countryId, StringComparison.OrdinalIgnoreCase))} kurgusal profil üretildi");
    }

    // ===== Yardımcılar =====

    private static string MakeHandle(string firstName, string lastName, ProfileType type, LocalRng rng)
    {
        string base1 = CleanHandle(firstName);
        string base2 = CleanHandle(lastName);
        string suffix = type switch
        {
            ProfileType.Journalist => "_reports",
            ProfileType.Opposition => "_voice",
            ProfileType.Official => "_gov",
            ProfileType.Bot => "_bot",
            _ => "", // citizen: sadece isim (veya soyad başlığı)
        };

        string handle;
        int n = 0;
        do
        {
            string extra = n > 0 ? rng.NextInt(1, 100).ToString() : "";
            handle = $"@{base1}{suffix}{extra}";
            if (string.IsNullOrWhiteSpace(base1)) handle = $"@user{suffix}{extra}";
            n++;
            if (n > 50) break; // güvenlik
        } while (_usedHandles.Contains(handle));

        _usedHandles.Add(handle);
        return handle;
    }

    private static string CleanHandle(string s)
    {
        if (string.IsNullOrEmpty(s)) return "user";
        var chars = new List<char>();
        foreach (char c in s.ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                chars.Add(c);
        }
        return chars.Count > 0 ? new string(chars.ToArray()) : "user";
    }

    private static int FollowersFor(ProfileType type, LocalRng rng)
    {
        return type switch
        {
            ProfileType.Citizen => rng.NextInt(100, 5001),
            ProfileType.Journalist => rng.NextInt(5000, 50001),
            ProfileType.Opposition => rng.NextInt(1000, 20001),
            ProfileType.Official => rng.NextInt(20000, 200001),
            ProfileType.Bot => rng.NextInt(50, 2001),
            _ => rng.NextInt(100, 5001),
        };
    }

    // Senaryo+ülke bazlı deterministik seed → aynı senaryoda profil her zaman aynı
    private static ulong HashSeed(string text)
    {
        unchecked
        {
            ulong h = 0x9E3779B97F4A7C15UL;
            foreach (char c in text) h = (h ^ c) * 0x2545F4914F6CDD1DUL;
            return h;
        }
    }

    private static void LoadNamePools()
    {
        if (_namePools.Count > 0) return;

        const string baseDir = "Data/names/";
        if (!DirAccess.DirExistsAbsolute(baseDir))
        {
            GD.PrintErr($"SocialProfile: {baseDir} bulunamadı!");
            return;
        }

        var dir = DirAccess.Open(baseDir);
        if (dir == null) return;

        dir.ListDirBegin();
        string file = dir.GetNext();
        while (file != "")
        {
            if (!dir.CurrentIsDir() && file.EndsWith(".json"))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                string fullPath = Path.Combine(baseDir, file);
                try
                {
                    var json = File.ReadAllText(fullPath);
                    var pool = JsonSerializer.Deserialize<NamePool>(json,
                        new JsonSerializerOptions { IncludeFields = true });
                    if (pool?.FirstNames?.Count > 0 && pool.LastNames?.Count > 0)
                        _namePools[key] = pool;
                }
                catch (Exception e)
                {
                    GD.PrintErr($"SocialProfile: {file} okunamadı: {e.Message}");
                }
            }
            file = dir.GetNext();
        }
        dir.ListDirEnd();
    }
}
