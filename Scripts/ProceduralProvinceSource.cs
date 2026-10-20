using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// M18: Prosedürel il üretimi. Mevcut ülke poligonlarını (GeoJSON) DeterministicRNG +
// sabit seed ile dengeli bölgelere böler. Aynı seed her zaman aynı iller üretir,
// bu yüzden üretim tekrar edilebilir ve Save/Load'da seed yeterli.
public class ProceduralProvinceSource : IProvinceSource
{
    private const string GeoJsonPath = "res://Data/countries.geo.json";
    private const int TargetProvinceCount = 1000;
    private const ulong FixedSeed = 0xC0FFEE_C0FFEE_C0FFUL;
    private const float MinProvinceArea = 400f;      // küçük parçalar atılır
    private const float MinSplitArea = MinProvinceArea * 4f; // bölünebilmek için minimum alan
    private const int MaxSplitAttempts = 80;         // tek poligon için bölme denemesi
    private const int MapSizeX = 2160;
    private const int MapSizeY = 1280;
    private const float TopMargin = 80f;

    private List<ProvinceData> _provinces;
    private bool _loaded;

    public int ProvinceCount => _provinces?.Count ?? 0;

    public IReadOnlyList<ProvinceData> LoadProvinces()
    {
        if (_loaded) return _provinces;

        // Sabit seed → deterministik: her çalıştırmada aynı iller
        DeterministicRNG.InitializeForNewGame(FixedSeed);

        var countries = LoadCountries();
        double totalArea = 0;
        foreach (var c in countries) totalArea += c.TotalArea;

        _provinces = new List<ProvinceData>();

        if (totalArea > 0)
        {
            foreach (var country in countries)
            {
                int target = Mathf.Clamp(
                    Mathf.CeilToInt((float)(country.TotalArea / totalArea * TargetProvinceCount)), 1, 60);
                SplitCountry(country, target);
            }
        }

        BuildNeighborGraph(_provinces);

        _loaded = true;
        GD.Print($"[ProceduralProvinceSource] {_provinces.Count} il üretildi (hedef ~{TargetProvinceCount}, " +
                 $"ülke={countries.Count}, seed={FixedSeed})");
        return _provinces;
    }

    // İller arası komşuluk grafi: iki ilin poligonları ortak bir kenar paylaşmışsa komşu sayar.
    // Bölme sonucu kardeş iller kesim hattı üzerinden aynı kenar noktalarına sahiptir (bit eşli),
    // bu yüzden yuvarlanmış kenar anahtarlarıyla eşleştirme deterministik ve hızlıdır.
    public void BuildNeighborGraph(List<ProvinceData> provinces)
    {
        if (provinces == null) return;
        var edgeMap = new Dictionary<string, List<int>>();
        for (int i = 0; i < provinces.Count; i++)
        {
            foreach (var ring in provinces[i].Rings)
            {
                int n = ring.Length;
                for (int a = 0; a < n; a++)
                {
                    var p1 = ring[a];
                    var p2 = ring[(a + 1) % n];
                    string key = EdgeKey(p1, p2);
                    if (!edgeMap.TryGetValue(key, out var list)) edgeMap[key] = list = new List<int>();
                    list.Add(i);
                }
            }
        }

        foreach (var kvp in edgeMap)
        {
            var owners = kvp.Value;
            if (owners.Count < 2) continue;
            // tekilleştir (aynı iki il birden fazla ortak kenar paylaşabilir)
            var uniq = new HashSet<int>(owners);
            if (uniq.Count < 2) continue;
            var arr = new List<int>(uniq);
            for (int x = 0; x < arr.Count; x++)
                for (int y = x + 1; y < arr.Count; y++)
                {
                    AddNeighbor(provinces[arr[x]], provinces[arr[y]].Id);
                    AddNeighbor(provinces[arr[y]], provinces[arr[x]].Id);
                }
        }

        int totalLinks = 0;
        foreach (var p in provinces) totalLinks += p.NeighborIds.Count;
        GD.Print($"[ProceduralProvinceSource] Komşuluk grafi hazır: {provinces.Count} il, {totalLinks / 2} yönsüz bağlantı");

        // M38.3: ülke sınırlarını aşan deniz/liman bağlantıları (kara kenar paylaşımından bağımsız).
        BuildSeaLinks(provinces);
    }

    // M38.3: Kıyı illeri (kara ile okyanus arasında sınır paylaşmayan kenarı olan iller),
    // farklı ülkeden ve birbirine yakın olanları deniz/liman hattı ile bağlar. Bu sayede
    // dar boğaz / kalelik gibi denizden geçilen geçitler komşuluk grafiğine girer.
    private void BuildSeaLinks(List<ProvinceData> provinces)
    {
        const float SeaLinkDistance = 40f; // projection birimi (~740 km); dar boğaz/kalelik mesafesi

        // Kenar haritası: tek seferlik appearing eden kenar = deniz kıyısı (kara ile paylaşılmamış).
        var edgeMap = new Dictionary<string, List<int>>();
        for (int i = 0; i < provinces.Count; i++)
        {
            foreach (var ring in provinces[i].Rings)
            {
                int n = ring.Length;
                for (int a = 0; a < n; a++)
                {
                    var p1 = ring[a];
                    var p2 = ring[(a + 1) % n];
                    string key = EdgeKey(p1, p2);
                    if (!edgeMap.TryGetValue(key, out var list)) edgeMap[key] = list = new List<int>();
                    list.Add(i);
                }
            }
        }

        var coastal = new HashSet<int>();
        foreach (var kvp in edgeMap)
            if (kvp.Value.Count == 1) coastal.Add(kvp.Value[0]);

        var coastalList = new List<int>(coastal);
        int links = 0;
        for (int i = 0; i < coastalList.Count; i++)
        {
            var a = provinces[coastalList[i]];
            for (int j = i + 1; j < coastalList.Count; j++)
            {
                var b = provinces[coastalList[j]];
                if (a.CountryId == b.CountryId) continue;       // aynı ülke → kara komşusu olabilir
                if (a.NeighborIds.Contains(b.Id)) continue;     // zaten komşu (kara sınırı)
                if ((a.Centroid - b.Centroid).Length() <= SeaLinkDistance)
                {
                    AddNeighbor(a, b.Id);
                    AddNeighbor(b, a.Id);
                    links++;
                }
            }
        }
        foreach (var idx in coastal) provinces[idx].IsCoastal = true;
        GD.Print($"[ProceduralProvinceSource] Deniz/liman hattı: {coastal.Count} kıyı ili, {links} sınır-aşan deniz bağlantısı");
    }

    private static void AddNeighbor(ProvinceData prov, string neighborId)
    {
        if (!prov.NeighborIds.Contains(neighborId)) prov.NeighborIds.Add(neighborId);
    }

    // Yönsüz kenar anahtarı: iki uç noktayı sıralıyor, float gürültüsünü önlemek için
    // 0.001 hassasiyetle yuvarlanıyor.
    private static string EdgeKey(Vector2 a, Vector2 b)
    {
        long ax = (long)Math.Round(a.X * 1000), ay = (long)Math.Round(a.Y * 1000);
        long bx = (long)Math.Round(b.X * 1000), by = (long)Math.Round(b.Y * 1000);
        if (ax > bx || (ax == bx && ay > by)) (ax, ay, bx, by) = (bx, by, ax, ay);
        return $"|{ax},{ay}|{bx},{by}|";
    }

    public IReadOnlyList<ProvinceData> GetProvincesForCountry(string countryId)
    {
        LoadProvinces();
        var result = new List<ProvinceData>();
        foreach (var p in _provinces)
            if (p.CountryId == countryId) result.Add(p);
        return result;
    }

    public void ResetForNewGame()
    {
        _provinces = null;
        _loaded = false;
    }

    // ---- ülke yükleme (GeoJSON) ----

    private sealed class CountryGeo
    {
        public string Id = "?";
        public string Name = "?";
        public List<Vector2[]> Rings = new();
        public double TotalArea;
    }

    private List<CountryGeo> LoadCountries()
    {
        var result = new List<CountryGeo>();
        if (!FileAccess.FileExists(GeoJsonPath))
        {
            GD.PrintErr($"[ProceduralProvinceSource] {GeoJsonPath} bulunamadı.");
            return result;
        }

        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(GeoJsonPath));
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("features", out var features))
        {
            GD.PrintErr("[ProceduralProvinceSource] GeoJSON 'features' dizisi yok.");
            return result;
        }

        foreach (var feature in features.EnumerateArray())
        {
            string id = "?";
            if (feature.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                id = idProp.GetString();

            string name = "?";
            var props = feature.GetProperty("properties");
            foreach (var key in new[] { "name", "NAME", "ADMIN" })
                if (props.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                { name = v.GetString(); break; }

            var geom = feature.GetProperty("geometry");
            if (geom.ValueKind != JsonValueKind.Object) continue;
            string type = geom.GetProperty("type").GetString() ?? "";
            var coords = geom.GetProperty("coordinates");

            var country = new CountryGeo { Id = id, Name = name };
            if (type == "Polygon") AddOuterRing(coords, country);
            else if (type == "MultiPolygon")
                foreach (var poly in coords.EnumerateArray()) AddOuterRing(poly, country);

            foreach (var r in country.Rings) country.TotalArea += Math.Abs(RingArea(r));
            if (country.Rings.Count > 0) result.Add(country);
        }
        return result;
    }

    private static void AddOuterRing(JsonElement polygon, CountryGeo country)
    {
        if (polygon.GetArrayLength() == 0) return;
        var outer = polygon[0];
        var pts = new List<Vector2>();
        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in outer.EnumerateArray())
        {
            double lon = p[0].GetDouble();
            if (lon < min) min = lon;
            if (lon > max) max = lon;
        }
        bool crosses = max - min > 180.0;
        foreach (var p in outer.EnumerateArray())
        {
            double lon = p[0].GetDouble(), lat = p[1].GetDouble();
            if (crosses && lon < 0) lon += 360.0;
            pts.Add(Project(lon, lat));
        }
        if (pts.Count >= 3) country.Rings.Add(pts.ToArray());
    }

    private static Vector2 Project(double lon, double lat)
    {
        float x = (float)((lon + 180.0) / 360.0) * MapSizeX;
        float usableH = MapSizeY - TopMargin * 2;
        float y = TopMargin + (float)((90.0 - lat) / 180.0) * usableH;
        return new Vector2(x, y);
    }

    // ---- ülkeyi bölgelere ayırma (deterministik bisection) ----

    private void SplitCountry(CountryGeo country, int target)
    {
        var polys = new List<Vector2[]>(country.Rings);
        int countryStart = _provinces.Count;
        int nextIndex = 1;

        while (_provinces.Count < countryStart + target && polys.Count > 0)
        {
            // En büyük bölünebilir poligonu seç
            int bestIdx = -1;
            float bestArea = 0;
            for (int i = 0; i < polys.Count; i++)
            {
                float a = Mathf.Abs(RingArea(polys[i]));
                if (a > bestArea && a > MinSplitArea) { bestArea = a; bestIdx = i; }
            }
            if (bestIdx < 0) break;

            Vector2[] parent = polys[bestIdx];
            polys.RemoveAt(bestIdx);

            bool splitOk = false;
            for (int attempt = 0; attempt < MaxSplitAttempts && !splitOk; attempt++)
            {
                Vector2 center = Centroid(parent);
                float angle = DeterministicRNG.NextFloat(0f, 2f * Mathf.Pi);
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                if (TrySplit(parent, center, dir, out Vector2[] left, out Vector2[] right))
                {
                    if (Mathf.Abs(RingArea(left)) > MinProvinceArea && left.Length >= 3 &&
                        Mathf.Abs(RingArea(right)) > MinProvinceArea && right.Length >= 3)
                    {
                        polys.Add(left);
                        polys.Add(right);
                        splitOk = true;
                    }
                }
            }

            if (!splitOk) FinalizePolygon(parent, country, ref nextIndex);
        }

        // Kalan parçaları il olarak kesitle
        foreach (var p in polys) FinalizePolygon(p, country, ref nextIndex);
    }

    private void FinalizePolygon(Vector2[] ring, CountryGeo country, ref int nextIndex)
    {
        float area = Mathf.Abs(RingArea(ring));
        if (area < MinProvinceArea) return;
        var prov = new ProvinceData
        {
            Id = $"{country.Id}-{nextIndex:D2}",
            Name = $"{country.Name} Province {nextIndex}",
            CountryId = country.Id,
            Continent = "",
            Centroid = Centroid(ring),
            Area = area,
            Rings = new List<Vector2[]> { ring },
            NeighborIds = new List<string>(),
            IsCore = true,
        };
        nextIndex++;
        _provinces.Add(prov);
    }

    // ---- yardımcı geometri ----

    private static float RingArea(Vector2[] ring)
    {
        double area = 0;
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return (float)(area / 2.0);
    }

    private static Vector2 Centroid(Vector2[] ring)
    {
        var c = Vector2.Zero;
        foreach (var p in ring) c += p;
        return c / ring.Length;
    }

    // Poligonu bir doğruya (point + dir) göre iki parçaya böler.
    private static bool TrySplit(Vector2[] ring, Vector2 point, Vector2 dir,
                                 out Vector2[] left, out Vector2[] right)
    {
        left = null;
        right = null;
        int n = ring.Length;
        float[] side = new float[n];
        bool hasLeft = false, hasRight = false;
        for (int i = 0; i < n; i++)
        {
            side[i] = Cross(dir, ring[i] - point);
            if (side[i] >= 0) hasLeft = true; else hasRight = true;
        }
        if (!hasLeft || !hasRight) return false; // hepsi aynı tarafta

        var L = new List<Vector2>();
        var R = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var a = ring[i];
            var b = ring[j];
            bool ai = side[i] >= 0, bi = side[j] >= 0;
            if (ai) L.Add(a); if (!ai) R.Add(a);
            if (ai != bi)
            {
                // doğruda kesişim noktası
                float t = side[i] / (side[i] - side[j]);
                var inter = a + (b - a) * t;
                L.Add(inter);
                R.Add(inter);
            }
        }

        left = Clean(L);
        right = Clean(R);
        return left != null && right != null;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector2[] Clean(List<Vector2> pts)
    {
        if (pts.Count < 3) return null;
        var res = new List<Vector2>();
        foreach (var p in pts)
        {
            if (res.Count > 0 && (p - res[res.Count - 1]).LengthSquared() < 1e-6f) continue;
            res.Add(p);
        }
        if (res.Count > 1 && (res[0] - res[res.Count - 1]).LengthSquared() < 1e-6f)
            res.RemoveAt(res.Count - 1);
        if (res.Count < 3) return null;
        return res.ToArray();
    }
}
