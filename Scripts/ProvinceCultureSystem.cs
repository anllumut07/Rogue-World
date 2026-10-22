using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// İl kültür ve nüfus katmanı (M44.2).
///
/// Age of History tarzı formable nation kurgusunun temeli: her ilin bir
/// "baskın kültürü" ve bir "nüfusu" vardır. Bir ülkenin bir bölgesindeki
/// kültür dağılımı, o bölge için kurulabilir ülkelerin koşulunu (M44.3)
/// belirler.
///
/// Türetme tamamen deterministik olmalı ki save/load sonrası (A==B) aynı
/// sonucu versin ve farklı makinelerde aynı dünya üretilsin. Bu yüzden:
/// kültür seçimi il kimliğinin hash'ine, nüfus da il alanına ve ülkenin
/// toplam nüfusuna bağlanır — rastgelelik için <see cref="DeterministicRNG"/>
/// kullanılır (GlobalRandom yasak).
/// </summary>
public static class ProvinceCultureSystem
{
    /// <summary>Bir ilin kültür kaydı.</summary>
    public class CultureRecord
    {
        public string ProvinceId = "";
        public string CountryId = "";   // kültürün atandığı sahip ülke
        public string Culture = "";
        public long Population = 0;     // il nüfusu (kişi)
    }

    // İl kimliği → kültür kaydı
    private static readonly Dictionary<string, CultureRecord> _records =
        new(StringComparer.Ordinal);

    // Ülke kimliği → il kimlikleri (sahiplik değişince temizlenir)
    private static readonly Dictionary<string, List<string>> _byCountry =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Kültür kaydı olan il sayısı.</summary>
    public static int RecordCount
    {
        get { return _records.Count; }
    }

    /// <summary>Bir ilin kültür kaydı (yoksa null).</summary>
    public static CultureRecord Get(string provinceId)
    {
        if (string.IsNullOrEmpty(provinceId)) return null;
        return _records.GetValueOrDefault(provinceId);
    }

    /// <summary>Bir ilin kültürü (kayıt yoksa "").</summary>
    public static string CultureOf(string provinceId)
    {
        var rec = Get(provinceId);
        return rec?.Culture ?? "";
    }

    /// <summary>Bir ilin nüfusu (kayıt yoksa 0).</summary>
    public static long PopulationOf(string provinceId)
    {
        var rec = Get(provinceId);
        return rec?.Population ?? 0;
    }

    /// <summary>Bir ülkenin tüm kültür kayıtları.</summary>
    public static List<CultureRecord> ForCountry(string countryId)
    {
        var result = new List<CultureRecord>();
        if (string.IsNullOrEmpty(countryId)) return result;

        if (_byCountry.TryGetValue(countryId, out var ids))
            foreach (var id in ids)
                if (_records.TryGetValue(id, out var rec))
                    result.Add(rec);

        return result;
    }

    /// <summary>
    /// Bir ülkenin kültür dağılımı: kültür → il sayısı.
    /// Toplamı, o ülkenin kültür kaydı olan il sayısına eşittir.
    /// </summary>
    public static Dictionary<string, int> GetCultureDistribution(string countryId)
    {
        var dist = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rec in ForCountry(countryId))
        {
            if (string.IsNullOrEmpty(rec.Culture)) continue;
            dist.TryGetValue(rec.Culture, out int n);
            dist[rec.Culture] = n + 1;
        }
        return dist;
    }

    /// <summary>
    /// Bir ülke topraklarında belirli bir kültürün il sayısı.
    /// Formable koşulu "gerekli il sayısı + kültür" için kullanılır (M44.3).
    /// </summary>
    public static int CultureProvinceCount(string countryId, string culture)
    {
        if (string.IsNullOrEmpty(countryId) || string.IsNullOrEmpty(culture)) return 0;

        int count = 0;
        foreach (var rec in ForCountry(countryId))
            if (string.Equals(rec.Culture, culture, StringComparison.Ordinal))
                count++;
        return count;
    }

    /// <summary>
    /// Bir ülkenin toplam (kayıtlı) nüfusu.
    /// </summary>
    public static long TotalPopulation(string countryId)
    {
        long total = 0;
        foreach (var rec in ForCountry(countryId))
            total += rec.Population;
        return total;
    }

    /// <summary>
    /// Verilen il listesine göre kültür ve nüfus kayıtları üretir.
    ///
    /// Kültür seçimi: il kimliği + sahip ülke kimliği + senaryo adı hash'inden
    /// türetilir. Böylece aynı dünyada aynı il her zaman aynı kültürü alır.
    ///
    /// Nüfus: il alanı ülkenin toplam alanına oranlanır ve ülkenin gerçek
    /// nüfuskusu (CountryData.Population) ile çarpılır. Alan oranları toplamı
    /// tam olarak 1 çıkmayabileceği için en son satırda kalan fark düzeltilir
    /// ki ülke nüfuskusu il nüfuslarının toplamına tam eşit olsun.
    /// </summary>
    public static void GenerateFor(List<ProvinceData> provinces, string scenarioName = "")
    {
        _records.Clear();
        _byCountry.Clear();

        if (provinces == null || provinces.Count == 0)
        {
            GD.Print("[Culture] Üretilecek il yok — kültür katmanı boş");
            return;
        }

        // Ülke bazında il alanı toplamı ve hedef nüfus
        var areaByCountry = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in provinces)
        {
            if (p == null || string.IsNullOrEmpty(p.CountryId)) continue;
            areaByCountry.TryGetValue(p.CountryId, out float a);
            areaByCountry[p.CountryId] = a + Mathf.Max(0f, p.Area);
        }

        // Kültür havuzu: elimizdeki kurgu ülke tanımlarının kültürleri +
        // var olan senaryo ülkeleri için kendi kimliklerinden türetilen kültür.
        var knownCultures = FormableDataSystem.All
            .Select(f => f.Culture)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var p in provinces)
        {
            if (p == null || string.IsNullOrEmpty(p.Id) || p.Id == "?" ||
                string.IsNullOrEmpty(p.CountryId) || p.CountryId == "?")
                continue;

            string culture = DeriveCulture(p, scenarioName, knownCultures);
            var rec = new CultureRecord
            {
                ProvinceId = p.Id,
                CountryId = p.CountryId,
                Culture = culture,
                Population = 0
            };
            _records[p.Id] = rec;

            if (!_byCountry.TryGetValue(p.CountryId, out var list))
            {
                list = new List<string>();
                _byCountry[p.CountryId] = list;
            }
            list.Add(p.Id);
        }

        // Nüfus dağılımı: alan oranı × ülke nüfuskusu.
        // CountryDatabase'de kaydı olmayan ülkeler (çoğu minör) için hedef,
        // bilinen ülkelerin alan başına nüfus yoğunluğunun ortalaması olarak
        // tahmin edilir — sabit bir sayı uydurmak yerine projenin kendi
        // verisinden türetilir, bu yüzden deterministik kalır.
        float densitySum = 0f;
        int densityCount = 0;
        foreach (var kv in areaByCountry)
        {
            var known = CountryDatabase.Get(kv.Key);
            if (known == null || known.Population <= 0 || kv.Value <= 0f) continue;
            densitySum += (float)known.Population / kv.Value;
            densityCount++;
        }
        float fallbackDensity = densityCount > 0 ? densitySum / densityCount : 1f;
        int unknownCountries = 0;
        foreach (var kv in areaByCountry)
            if (CountryDatabase.Get(kv.Key) == null) unknownCountries++;
        GD.Print($"[Culture] Alan başına ortalama nüfus yoğunluğu: {fallbackDensity:N2} " +
                  $"({densityCount} ülke ölçümünden), {unknownCountries} ülke bu yedekle dolduruluyor");

        foreach (var kv in _byCountry)
        {
            string countryId = kv.Key;
            var data = CountryDatabase.Get(countryId);
            long target = data != null
                ? data.Population
                : (long)Math.Round(areaByCountry.TryGetValue(countryId, out float fa) ? fa * fallbackDensity : 0f);
            if (target <= 0) continue;

            float totalArea = areaByCountry.TryGetValue(countryId, out float ta) ? ta : 0f;

            long assigned = 0;
            var ids = kv.Value;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = provinces.First(pp => pp.Id == ids[i]);
                long pop;
                if (i == ids.Count - 1)
                {
                    // Son il: kalan fark — toplam tam eşitlensin
                    pop = target - assigned;
                    if (pop < 1) pop = 1;
                }
                else
                {
                    double share = (double)p.Area / totalArea;
                    pop = (long)Math.Floor(target * share);
                    if (pop < 1) pop = 1;
                }
                _records[ids[i]].Population = pop;
                assigned += pop;
            }
        }

        GD.Print($"[Culture] Üretildi: {_records.Count} il kültür kaydı ({_byCountry.Count} ülke)");
    }

    /// <summary>
    /// İl için deterministik kültür türetir. Kurgu tanım kültürleri öncelikli
    /// denenir; hiçbiri tutmazsa il + ülke kimliğinden üretilen basit bir ad
    /// kullanılır.
    /// </summary>
    private static string DeriveCulture(ProvinceData p, string scenarioName, List<string> knownCultures)
    {
        if (knownCultures.Count > 0)
        {
            int h = StableHash($"{p.Id}|{p.CountryId}|{scenarioName}");
            int idx = (int)((uint)h % (uint)knownCultures.Count);
            return knownCultures[idx];
        }

        return $"{p.CountryId.ToLowerInvariant()}-local";
    }

    /// <summary>
    /// Platformdan ve sürümden bağımsız kararlı hash (FNV-1a).
    /// string.GetHashCode() kullanılamaz — çalışma çalışma değişir.
    /// </summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in s)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= 16777619;
                hash ^= (byte)(c >> 8);
                hash *= 16777619;
            }
            return (int)hash;
        }
    }

    /// <summary>Save/Load verisi (M44.5'te SaveSystem'e bağlanır).</summary>
    public static SaveSystem.CultureSave GetSaveData()
    {
        var records = new List<SaveSystem.CultureEntry>(_records.Count);
        foreach (var rec in _records.Values)
        {
            records.Add(new SaveSystem.CultureEntry
            {
                ProvinceId = rec.ProvinceId,
                CountryId = rec.CountryId,
                Culture = rec.Culture,
                Population = rec.Population
            });
        }

        return new SaveSystem.CultureSave { Records = records.ToArray() };
    }

    public static void LoadSaveData(SaveSystem.CultureSave data)
    {
        _records.Clear();
        _byCountry.Clear();
        if (data?.Records == null)
        {
            GD.Print("[Culture] Kültür kaydı yok (kayıt boş)");
            return;
        }

        foreach (var entry in data.Records)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ProvinceId)) continue;

            var rec = new CultureRecord
            {
                ProvinceId = entry.ProvinceId,
                CountryId = entry.CountryId ?? "",
                Culture = entry.Culture ?? "",
                Population = entry.Population
            };
            _records[rec.ProvinceId] = rec;

            if (!string.IsNullOrEmpty(rec.CountryId))
            {
                if (!_byCountry.TryGetValue(rec.CountryId, out var list))
                {
                    list = new List<string>();
                    _byCountry[rec.CountryId] = list;
                }
                list.Add(rec.ProvinceId);
            }
        }

        GD.Print($"[Culture] Kültür kayıtları yüklendi: {_records.Count}");
    }

    /// <summary>Yeni oyunda sıfırlar.</summary>
    public static void ResetForNewGame()
    {
        _records.Clear();
        _byCountry.Clear();
    }
}
