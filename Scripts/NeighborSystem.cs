using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Komşuluk sistemi: GeoJSON poligonlarından ülke komşuluk grafiı üretir.
/// Kullanım: AI savaş hedefi kısıtlaması (yalnızca komşular/iddia/ideolojik düşman).
/// Bir kez hesaplanır ve bellekte önbelleklenir (ilk çağrıda).
/// </summary>
public static class NeighborSystem
{
    // countryId -> komşu ülke id listesi
    private static Dictionary<string, HashSet<string>> _neighbors = new();
    private static bool _computed = false;

    // Vertex yakınlık eşiği (derece): iki ülkenin sınır noktaları bu mesafeden
    // yakınsa komşu sayılır. GeoJSON basitleştirmesi ~0.5-1 derece hatalı olabilir.
    private const double VertexProximityDegrees = 1.2;

    // Basitleştirme: her N. noktayı al (performans)
    private const int VertexStep = 2;

    public static bool IsReady => _computed;

    /// <summary>Belirli ülkenin komşuları (id listesi). Hesaplanmadıysa boş.</summary>
    public static HashSet<string> GetNeighbors(string countryId)
    {
        EnsureComputed();
        return _neighbors.GetValueOrDefault(countryId) ?? new HashSet<string>();
    }

    /// <summary>İki ülke komşu mu?</summary>
    public static bool AreNeighbors(string countryIdA, string countryIdB)
    {
        EnsureComputed();
        if (!_neighbors.TryGetValue(countryIdA, out var set)) return false;
        return set.Contains(countryIdB);
    }

    /// <summary>Ham erişim (özet/log için).</summary>
    public static IReadOnlyDictionary<string, HashSet<string>> AllNeighbors
    {
        get { EnsureComputed(); return _neighbors; }
    }

    // ===== Hesaplama =====

    private class CountryGeom
    {
        public string Id;
        public List<double[]> Points = new(); // basitleştirilmiş [lon, lat] noktaları
        public double MinLon = double.MaxValue, MaxLon = double.MinValue;
        public double MinLat = double.MaxValue, MaxLat = double.MinValue;
    }

    private static void EnsureComputed()
    {
        if (_computed) return;
        _computed = true;

        const string geoPath = "res://Data/countries.geo.json";
        if (!FileAccess.FileExists(geoPath))
        {
            GD.PrintErr($"NeighborSystem: {geoPath} bulunamadı — komşuluk grafiı boş");
            return;
        }

        try
        {
            var countries = new List<CountryGeom>();

            using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(geoPath));
            foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                string id = "?";
                if (feature.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    id = idProp.GetString();

                var geom = feature.GetProperty("geometry");
                if (geom.ValueKind != JsonValueKind.Object) continue;
                string type = geom.GetProperty("type").GetString() ?? "";
                var coords = geom.GetProperty("coordinates");

                var cg = new CountryGeom { Id = id };

                void ProcessRing(JsonElement ring)
                {
                    if (ring.GetArrayLength() == 0) return;
                    for (int i = 0; i < ring.GetArrayLength(); i += VertexStep)
                    {
                        var p = ring[i];
                        double lon = p[0].GetDouble();
                        double lat = p[1].GetDouble();
                        cg.Points.Add(new[] { lon, lat });
                        if (lon < cg.MinLon) cg.MinLon = lon;
                        if (lon > cg.MaxLon) cg.MaxLon = lon;
                        if (lat < cg.MinLat) cg.MinLat = lat;
                        if (lat > cg.MaxLat) cg.MaxLat = lat;
                    }
                }

                if (type == "Polygon")
                {
                    // dış halka (ring[0])
                    var ring0 = coords[0];
                    ProcessRing(ring0);
                }
                else if (type == "MultiPolygon")
                {
                    foreach (var poly in coords.EnumerateArray())
                    {
                        if (poly.GetArrayLength() == 0) continue;
                        ProcessRing(poly[0]); // dış halka
                    }
                }

                if (cg.Points.Count > 0) countries.Add(cg);
            }

            // Çapraz bbox + nokta yakınlık kontrolü
            double threshold = VertexProximityDegrees;
            double thresholdSq = threshold * threshold;

            int pairCount = 0;
            for (int a = 0; a < countries.Count; a++)
            {
                for (int b = a + 1; b < countries.Count; b++)
                {
                    var ca = countries[a];
                    var cb = countries[b];
                    if (ca.Id == cb.Id) continue;

                    // Bbox yakınlığı: genişletilmiş kutular kesişmiyorsa komşu olamaz
                    if (ca.MaxLon + threshold < cb.MinLon || cb.MaxLon + threshold < ca.MinLon) continue;
                    if (ca.MaxLat + threshold < cb.MinLat || cb.MaxLat + threshold < ca.MinLat) continue;

                    // Nokta yakınlığı: herhangi bir sınır noktası çifti yakınsa komşu
                    bool found = false;
                    foreach (var pa in ca.Points)
                    {
                        if (found) break;
                        foreach (var pb in cb.Points)
                        {
                            double dLon = pa[0] - pb[0];
                            double dLat = pa[1] - pb[1];
                            // Enlem/boylam metre karşılığı farklı ama oyun için yeterli
                            if (dLon * dLon + dLat * dLat < thresholdSq)
                            {
                                found = true;
                                break;
                            }
                        }
                    }

                    if (found)
                    {
                        AddNeighbor(ca.Id, cb.Id);
                        AddNeighbor(cb.Id, ca.Id);
                        pairCount++;
                    }
                }
            }

            int totalLinks = 0;
            foreach (var kv in _neighbors) totalLinks += kv.Value.Count;

            GD.Print($"NeighborSystem hazır: {countries.Count} ülke, {pairCount} komşu çifti " +
                     $"(toplam {totalLinks} tek yönlü bağlantı, eşik {threshold}°)");
        }
        catch (Exception e)
        {
            GD.PrintErr($"NeighborSystem: GeoJSON işlenemedi: {e.Message}");
        }
    }

    private static void AddNeighbor(string a, string b)
    {
        if (!_neighbors.TryGetValue(a, out var set))
        {
            set = new HashSet<string>();
            _neighbors[a] = set;
        }
        set.Add(b);
    }

    // Coğrafya oturum boyunca değişmez — yeni oyunda yeniden hesaplamaya gerek yok.
    public static void ResetForNewGame() { }
}
