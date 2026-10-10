using Godot;
using System.Collections.Generic;
using System.Text.Json;

// Dünya haritası: GeoJSON'dan ülke poligonları çizer, kamera pan/zoom,
// sol tık ülke seçimi, hover vurgulama, ülke adı etiketleri.
public partial class WorldMap : Node2D
{
    [Signal] public delegate void CountrySelectedEventHandler(string countryId, string countryName);
    [Signal] public delegate void CountryDeselectedEventHandler();

    public static WorldMap Instance { get; private set; }

    [Export] public string GeoJsonPath { get; set; } = "res://Data/countries.geo.json";
    [Export] public Vector2 MapSize { get; set; } = new Vector2(2160, 1280);
    [Export] public float KeyPanSpeed { get; set; } = 900f;

    private static readonly Color SelectedColor = new("C9A227");
    private static readonly Color BorderColor = new("0E141B");
    private static readonly Color GoldBorderColor = new("FFD700");

    public sealed class Country
    {
        public string Id = "?";
        public string Name = "?";
        public readonly List<Vector2[]> Rings = new();
        public readonly List<Polygon2D> Nodes = new();
        public readonly List<Line2D> Outlines = new();
        public Color MapColor;
        public float SmallestRingArea = float.MaxValue;
        public Vector2 LabelAnchor;
        public Vector2 LargestBBox;
        public Label NameLabel;
    }

    private sealed class MapLabel
    {
        public Label Node;
        public Vector2 Anchor;
        public float ShowZoomMin = 0f;
        public float ShowZoomMax = 999f;
    }

    internal readonly List<Country> _countries = new();
    private readonly List<MapLabel> _oceanLabels = new();
    private readonly List<MapLabel> _seaLabels = new();

    private Country _selected;
    private Country _hovered;
    private Camera2D _camera;
    private Label _selectionLabel;
    private CanvasLayer _nameLayer;
    private bool _dragging;
    private Vector2 _lastMousePos;
    private float _goldPulse = 0f;
    private bool _cameraNeedsFit = true; // SubViewport gerçek boyutuna ulaşınca kamerayı oturt
    private static readonly Color OceanColor = new("0A1628");

    public override void _Ready()
    {
        Instance = this;
        _nameLayer = new CanvasLayer { Layer = 1 };
        AddChild(_nameLayer);

        SetProcessInput(true);

        BuildFromGeoJson();
        BuildCamera();
        BuildSelectionLabel();
        BuildZoomLabels();
        RefreshOwnershipColors();
        QueueRedraw();
        GD.Print($"WorldMap: hazır — kamera zoom={_camera?.Zoom.X:0.00} pos={_camera?.Position}, viewport={GetViewportRect().Size}");
    }

    public override void _Draw()
    {
        // Okyanus arka planı: SubViewport'un render ettiğinin görsel kanıtı (gri = hiç render yok)
        DrawRect(new Rect2(-2000f, -1000f, MapSize.X + 4000f, MapSize.Y + 2000f), OceanColor);
        DrawRect(new Rect2(Vector2.Zero, MapSize), new Color(0.12f, 0.25f, 0.45f), false, 2f); // harita sınırı
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        HandleKeyboardPan((float)delta);
        UpdateNameLabels();
        AnimateSelectedBorder((float)delta);

        // SubViewport layout oturana kadar boyut (2,2) olabilir; gerçek boyuta
        // ulaşınca kamerayı bir kez haritaya sığdır.
        if (_cameraNeedsFit)
        {
            var vp = GetViewportRect().Size;
            if (vp.X >= 200 && vp.Y >= 150)
            {
                FitCameraToViewport();
                _cameraNeedsFit = false;
            }
        }
    }

    public override void _Input(InputEvent e)
    {
        // ----- Sağ tık sürükleme ile pan -----
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _dragging = mb.Pressed;
                if (mb.Pressed) _lastMousePos = mb.Position;
                else _lastMousePos = Vector2.Zero;
                GetViewport().SetInputAsHandled();
                return;
            }
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
            {
                ZoomAt(mb.Position, 1.15f);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
            {
                ZoomAt(mb.Position, 1f / 1.15f);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // ----- Sürüklerken haritayı kaydır -----
        if (e is InputEventMouseMotion motion && _dragging)
        {
            Vector2 deltaPos = motion.Position - _lastMousePos;
            _lastMousePos = motion.Position;
            if (_camera != null && deltaPos != Vector2.Zero)
            {
                _camera.Position -= deltaPos / _camera.Zoom.X;
                ClampCamera();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        // ----- Sol tık seçim / hover -----
        if (!_dragging)
        {
            if (e is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
            {
                var hovered = GetViewport().GuiGetHoveredControl();
                if (HoveredBlocksMap(hovered)) return;

                Vector2 viewportPos = GetViewport().GetMousePosition();
                Vector2 worldPos = _camera.Position + (viewportPos - GetViewportRect().Size / 2f) / _camera.Zoom;

                SelectAt(worldPos);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (e is InputEventMouseMotion mm)
            {
                Vector2 viewportPos = GetViewport().GetMousePosition();
                Vector2 worldPos = _camera.Position + (viewportPos - GetViewportRect().Size / 2f) / _camera.Zoom;
                UpdateHover(worldPos);
            }
        }
    }

    // Üzerinde duran UI elemanı harita tıklamasını engelliyor mu?
    private static bool HoveredBlocksMap(Control hovered)
    {
        if (hovered == null) return false;
        if (hovered is Label) return false;
        for (Node node = hovered; node != null; node = node.GetParent())
        {
            if (node is Control control && control.MouseFilter == Control.MouseFilterEnum.Ignore)
                return false;

            if (node is Button or Slider or OptionButton or CheckBox or
                PanelContainer or LineEdit or ScrollContainer or TextEdit)
                return true;

            if (node is CanvasLayer || !(node is Control))
                break;
        }
        return false;
    }

    private void HandleKeyboardPan(float delta)
    {
        if (_camera == null) return;
        Vector2 dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (dir == Vector2.Zero) return;
        _camera.Position += dir.Normalized() * (KeyPanSpeed / _camera.Zoom.X) * delta;
        ClampCamera();
    }

    private void ClampCamera()
    {
        _camera.Position = new Vector2(
            Mathf.Clamp(_camera.Position.X, -400f, MapSize.X + 400f),
            Mathf.Clamp(_camera.Position.Y, -400f, MapSize.Y + 400f));
    }

    public void RefreshOwnershipColors()
    {
        foreach (var c in _countries)
        {
            Color target = c.MapColor;
            if (c == _selected) target = SelectedColor;
            else if (c == _hovered) target = c.MapColor.Lightened(0.25f);
            foreach (var node in c.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = target;
            foreach (var outline in c.Outlines) if (GodotObject.IsInstanceValid(outline) && c != _selected) outline.DefaultColor = BorderColor;
        }
    }

    private static Color ColorForId(string id)
    {
        unchecked
        {
            int hash = 0; foreach (char ch in id) hash = hash * 31 + ch;
            float hue = Mathf.PosMod(hash, 360) / 360f;
            float sat = 0.38f + Mathf.PosMod(hash >> 8, 20) / 100f;
            float val = 0.42f + Mathf.PosMod(hash >> 16, 14) / 100f;
            return Color.FromHsv(hue, sat, val);
        }
    }

    private void BuildFromGeoJson()
    {
        if (!FileAccess.FileExists(GeoJsonPath)) { GD.PrintErr($"HATA: {GeoJsonPath} yok!"); return; }
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(GeoJsonPath));
        int parts = 0;
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var props = feature.GetProperty("properties");
            string id = "?"; if (feature.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String) id = idProp.GetString();
            string name = "?"; foreach (var key in new[] { "name", "NAME", "ADMIN" }) if (props.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) { name = v.GetString(); break; }
            var geom = feature.GetProperty("geometry");
            if (geom.ValueKind != JsonValueKind.Object) continue;
            string type = geom.GetProperty("type").GetString() ?? "";
            var coords = geom.GetProperty("coordinates");
            var country = new Country { Id = id, Name = name, MapColor = ColorForId(id) };
            if (type == "Polygon") AddRing(coords, country);
            else if (type == "MultiPolygon") foreach (var poly in coords.EnumerateArray()) AddRing(poly, country);
            if (country.Rings.Count == 0) continue;
            foreach (var ring in country.Rings)
            {
                var body = new Polygon2D { Polygon = ring, Color = country.MapColor };
                AddChild(body); country.Nodes.Add(body);
                var outline = new Line2D { Points = ring, DefaultColor = BorderColor, Width = 2f, Closed = true };
                AddChild(outline); country.Outlines.Add(outline);
                parts++;
            }
            _countries.Add(country);
            SetupNameLabel(country);
        }
        GD.Print($"WorldMap hazır: {_countries.Count} ülke, {parts} parça çizildi.");
    }

    private void AddRing(JsonElement polygon, Country country)
    {
        if (polygon.GetArrayLength() == 0) return;
        var outer = polygon[0];
        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in outer.EnumerateArray()) { double lon = p[0].GetDouble(); if (lon < min) min = lon; if (lon > max) max = lon; }
        bool crosses = max - min > 180.0;
        var points = new List<Vector2>();
        foreach (var p in outer.EnumerateArray())
        {
            double lon = p[0].GetDouble(); double lat = p[1].GetDouble();
            if (crosses && lon < 0) lon += 360.0;
            points.Add(Project(lon, lat));
        }
        if (points.Count >= 3) country.Rings.Add(points.ToArray());
    }

    private Vector2 Project(double lon, double lat)
    {
        float x = (float)((lon + 180.0) / 360.0) * MapSize.X;
        float topMargin = 80f;
        float usableH = MapSize.Y - topMargin * 2;
        float y = topMargin + (float)((90.0 - lat) / 180.0) * usableH;
        return new Vector2(x, y);
    }

    private void SetupNameLabel(Country c)
    {
        Vector2[] biggest = null; double biggestArea = -1;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var maxPt = new Vector2(float.MinValue, float.MinValue);
        foreach (var ring in c.Rings)
        {
            double area = Mathf.Abs(RingArea(ring));
            if (area > 0 && area < c.SmallestRingArea) c.SmallestRingArea = (float)area;
            if (area > biggestArea)
            {
                biggestArea = area; biggest = ring;
                min = new Vector2(float.MaxValue, float.MaxValue); maxPt = new Vector2(float.MinValue, float.MinValue);
                foreach (var pt in ring) { if (pt.X < min.X) min.X = pt.X; if (pt.Y < min.Y) min.Y = pt.Y; if (pt.X > maxPt.X) maxPt.X = pt.X; if (pt.Y > maxPt.Y) maxPt.Y = pt.Y; }
            }
        }
        if (biggest == null) return;
        c.LabelAnchor = (min + maxPt) / 2f; c.LargestBBox = maxPt - min;
        var label = new Label { Text = c.Name, Visible = false };
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.92f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        c.NameLabel = label; _nameLayer.AddChild(label);
    }

    private static double RingArea(Vector2[] ring)
    {
        double area = 0; for (int i = 0; i < ring.Length; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Length]; area += (double)a.X * b.Y - (double)b.X * a.Y; }
        return area / 2.0;
    }

    private void BuildZoomLabels()
    {
        AddMapLabel(_oceanLabels, "Atlantic Ocean", new Vector2(900, 650), 0f, 0.6f);
        AddMapLabel(_oceanLabels, "Pacific Ocean", new Vector2(1600, 650), 0f, 0.6f);
        AddMapLabel(_oceanLabels, "Indian Ocean", new Vector2(1300, 950), 0f, 0.6f);
        AddMapLabel(_seaLabels, "Mediterranean Sea", new Vector2(1100, 450), 1.0f, 999f);
        AddMapLabel(_seaLabels, "Black Sea", new Vector2(1150, 480), 1.0f, 999f);
        AddMapLabel(_seaLabels, "Red Sea", new Vector2(1250, 750), 1.0f, 999f);
    }

    private void AddMapLabel(List<MapLabel> list, string text, Vector2 anchor, float minZoom, float maxZoom)
    {
        var label = new Label { Text = text, Visible = false };
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f, 0.9f));
        _nameLayer.AddChild(label);
        list.Add(new MapLabel { Node = label, Anchor = anchor, ShowZoomMin = minZoom, ShowZoomMax = maxZoom });
    }

    private void UpdateNameLabels()
    {
        if (_camera == null) return;
        var vp = GetViewportRect().Size; float zoom = _camera.Zoom.X;
        foreach (var c in _countries)
        {
            var label = c.NameLabel;
            if (label == null || !GodotObject.IsInstanceValid(label)) continue;
            Vector2 screen = (c.LabelAnchor - _camera.Position) * zoom + vp / 2f;
            float wpx = c.LargestBBox.X * zoom; float hpx = c.LargestBBox.Y * zoom;
            bool bigEnough = wpx > 45f && hpx > 22f;
            bool onScreen = screen.X > -80f && screen.X < vp.X + 80f && screen.Y > -30f && screen.Y < vp.Y + 30f;
            label.Visible = bigEnough && onScreen;
            if (label.Visible) label.Position = screen - label.Size / 2f;
        }
        UpdateMapLabelSet(_oceanLabels, zoom);
        UpdateMapLabelSet(_seaLabels, zoom);
    }

    private void UpdateMapLabelSet(List<MapLabel> set, float zoom)
    {
        if (_camera == null) return;
        var vp = GetViewportRect().Size;
        foreach (var ml in set)
        {
            bool inRange = zoom >= ml.ShowZoomMin && zoom <= ml.ShowZoomMax;
            Vector2 screen = (ml.Anchor - _camera.Position) * zoom + vp / 2f;
            bool onScreen = screen.X > -80f && screen.X < vp.X + 80f && screen.Y > -30f && screen.Y < vp.Y + 30f;
            ml.Node.Visible = inRange && onScreen;
            if (ml.Node.Visible) ml.Node.Position = screen - ml.Node.Size / 2f;
        }
    }

    private void BuildCamera()
    {
        _camera = new Camera2D { Position = MapSize / 2f };
        AddChild(_camera); _camera.MakeCurrent();
        FitCameraToViewport();
    }

    // Kamerayı haritayı viewport'a sığdıracak şekilde ayarla
    private void FitCameraToViewport()
    {
        if (_camera == null) return;
        var vp = GetViewportRect().Size;
        if (vp.X <= 0 || vp.Y <= 0) vp = MapSize;
        float fit = Mathf.Min(vp.X / MapSize.X, vp.Y / MapSize.Y) * 0.95f;
        _camera.Zoom = new Vector2(fit, fit);
        _camera.Position = new Vector2(MapSize.X / 2f, MapSize.Y / 2f - 80f);
    }

    // İlk kareden sonra çağrılır: SubViewport'ta kamera current düşmüşse düzelt
    public void EnsureCameraCurrent()
    {
        if (_camera == null || !GodotObject.IsInstanceValid(_camera)) return;
        if (!_camera.IsCurrent()) _camera.MakeCurrent();
    }

    private void ZoomAt(Vector2 screenPos, float factor)
    {
        _cameraNeedsFit = false; // kullanıcı kontrolü devraldı
        float z = Mathf.Clamp(_camera.Zoom.X * factor, 0.25f, 6f);
        if (Mathf.IsEqualApprox(z, _camera.Zoom.X)) return;
        var center = GetViewportRect().Size / 2f;
        var before = (screenPos - center) / _camera.Zoom.X + _camera.Position;
        _camera.Zoom = new Vector2(z, z);
        var after = (screenPos - center) / z + _camera.Position;
        _camera.Position += before - after;
    }

    private void UpdateHover(Vector2 worldPos)
    {
        Country hit = FindCountryAt(worldPos);
        if (hit == _hovered) return;
        var old = _hovered; _hovered = hit;
        if (old != null) foreach (var node in old.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = old == _selected ? SelectedColor : old.MapColor;
        if (_hovered != null && _hovered != _selected) foreach (var node in _hovered.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = _hovered.MapColor.Lightened(0.25f);
    }

    private Country FindCountryAt(Vector2 worldPos)
    {
        Country best = null;
        float bestArea = float.MaxValue;
        foreach (var c in _countries)
            foreach (var ring in c.Rings)
                if (Geometry2D.IsPointInPolygon(worldPos, ring))
                {
                    if (c.SmallestRingArea < bestArea)
                    {
                        best = c;
                        bestArea = c.SmallestRingArea;
                    }
                    break;
                }
        return best;
    }

    private void SelectAt(Vector2 worldPos) => SetSelected(FindCountryAt(worldPos));

    private void SetSelected(Country c)
    {
        if (_selected == c) return;
        var old = _selected; _selected = c;
        if (old != null)
        {
            foreach (var outline in old.Outlines) if (GodotObject.IsInstanceValid(outline)) { outline.DefaultColor = BorderColor; outline.Width = 2f; }
            foreach (var node in old.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = old == _hovered ? old.MapColor.Lightened(0.25f) : old.MapColor;
        }
        if (_selected != null)
        {
            foreach (var node in _selected.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = SelectedColor;
            foreach (var outline in _selected.Outlines) if (GodotObject.IsInstanceValid(outline)) { outline.DefaultColor = GoldBorderColor; outline.Width = 2.5f; }
            _selectionLabel.Text = $"Selected: {_selected.Name}";
            EmitSignal(SignalName.CountrySelected, _selected.Id, _selected.Name);
        }
        else
        {
            _selectionLabel.Text = "Selected: -";
            EmitSignal(SignalName.CountryDeselected);
        }
    }

    private void AnimateSelectedBorder(float delta)
    {
        if (_selected == null) return;
        _goldPulse += delta * 2f; float pulse = 0.5f + 0.5f * Mathf.Sin(_goldPulse);
        foreach (var outline in _selected.Outlines) if (GodotObject.IsInstanceValid(outline)) { outline.Width = 2f + pulse * 1.5f; outline.DefaultColor = GoldBorderColor.Lightened(pulse * 0.2f); }
    }

    private void BuildSelectionLabel()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        _selectionLabel = new Label { Text = "Selected: -" };
        _selectionLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _selectionLabel.AddThemeFontSizeOverride("font_size", 18);
        _selectionLabel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _selectionLabel.OffsetLeft = 340; _selectionLabel.OffsetTop = -40;
        _selectionLabel.OffsetBottom = -10; _selectionLabel.OffsetRight = 900;
        layer.AddChild(_selectionLabel);
    }
}