using Godot;
using System.Collections.Generic;

/// <summary>
/// V5SelectionOutline — Tek bir Line2D node ile seçili ülkenin tüm ring'lerini (adalar dahil)
/// parlak altın renkte, ekranda 2.5 px kalınlıkta çizer.
/// Dolguların ve normal sınırların ÜSTÜNDE (ZIndex = 10).
/// Kalınlık = 2.5 / zoom (ekranda sabit 2.5 px).
/// </summary>
public partial class V5SelectionOutline : Node2D
{
    private Line2D _line;
    private WorldMapV5 _map;
    private float _lastZoom = -1f;

    /// <summary>
    /// Internal Line2D for direct access (debug/test)
    /// </summary>
    public Line2D Line => _line;

    public void Initialize(WorldMapV5 map)
    {
        _map = map;

        _line = new Line2D();
        _line.Name = "SelectionOutline";
        _line.DefaultColor = new Color("#FFD700"); // Parlak altın
        _line.Width = 2.5f; // Base width, will scale with zoom
        _line.Antialiased = true;
        _line.ZIndex = 10; // Dolgular (0), sınırlar (1) üzeri
        _line.Visible = false;
        AddChild(_line);

        GD.Print("[V5SelectionOutline] Initialized");
    }

    /// <summary>
    /// Seçili ülke değiştiğinde çağrılır. Tüm exterior ring'leri (adalar dahil) çizer.
    /// </summary>
    public void UpdateSelection(string selectedCountryId)
    {
        if (_line == null || _map == null) return;

        if (string.IsNullOrEmpty(selectedCountryId))
        {
            _line.Visible = false;
            return;
        }

        if (!_map.Countries.TryGetValue(selectedCountryId, out var country))
        {
            _line.Visible = false;
            return;
        }

        var exteriors = country.CachedExteriors;
        if (exteriors == null || exteriors.Count == 0)
        {
            _line.Visible = false;
            return;
        }

        var points = new List<Vector2>();
        bool first = true;

        foreach (var exterior in exteriors)
        {
            if (exterior.Length < 2) continue;

            if (!first)
            {
                points.Add(new Vector2(float.NaN, float.NaN)); // Ring arası kesme
            }

            foreach (var pt in exterior)
            {
                var worldPt = MapProjectionV5.LonLatToWorld(pt.X, pt.Y);
                points.Add(worldPt);
            }

            first = false;
        }

        if (points.Count > 0)
        {
            _line.Points = points.ToArray();
            _line.Visible = true;
            GD.Print($"[V5SelectionOutline] Built {points.Count} points for {selectedCountryId} ({exteriors.Count} rings)");
        }
        else
        {
            _line.Visible = false;
        }
    }

    /// <summary>
    /// Zoom değiştiğinde kalınlığı günceller: ekranda 2.5 px = world'da 2.5/zoom.
    /// </summary>
    public void UpdateZoom(float zoom)
    {
        if (_line == null) return;
        if (Mathf.Abs(zoom - _lastZoom) < 0.001f) return;

        _lastZoom = zoom;
        float worldWidth = 2.5f / zoom;
        _line.Width = worldWidth;
    }
}