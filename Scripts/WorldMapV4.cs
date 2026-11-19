using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// WorldMapV4 — HOI4-style world map with shaded relief terrain, semi-transparent country overlays,
/// coastal glow, LOD system, city markers, and curved labels.
/// Uses Natural Earth raster data (NE1_HR_LC_SR_W_DR, SR_HR, OB_HR) + vector data.
/// </summary>
public partial class WorldMapV4 : Node2D
{
    [Signal] public delegate void CountrySelectedEventHandler(string countryId, string countryName);
    [Signal] public delegate void CountryDeselectedEventHandler();
    [Signal] public delegate void ProvinceSelectionChangedEventHandler(string[] provinceIds);
    [Signal] public delegate void CitySelectedEventHandler(string cityId, string cityName, string countryId);

    public static WorldMapV4 Instance { get; private set; }

    public enum MapMode { Political, Terrain, Supply, Factories, Resources, AirZones, Culture }
    public MapMode CurrentMapMode { get; private set; } = MapMode.Political;

    [Export] public string MapDataPath { get; set; } = "res://Data/map/";
    [Export] public Vector2 MapSize { get; set; } = new Vector2(2160, 1280);
    [Export] public float KeyPanSpeed { get; set; } = 900f;

    // LOD zoom thresholds
    private const float ZOOM_STRATEGIC_MAX = 0.5f;      // Countries only
    private const float ZOOM_OPERATIONAL_MAX = 1.5f;    // + Provinces
    private const float ZOOM_TACTICAL_MAX = 3.0f;       // + Cities
    private const float ZOOM_DETAIL_MIN = 3.0f;         // + All labels

    // Visual constants
    private static readonly Color SelectedColor = new Color("#C9A227");
    private static readonly Color BorderColor = new Color("#0E141B");
    private static readonly Color GoldBorderColor = new Color("#FFD700");
    private static readonly Color PlayerBorderColor = new Color(0.79f, 0.64f, 0.15f, 0.55f);
    private static readonly Color CoastalGlowColor = new Color(0.3f, 0.8f, 0.9f, 0.25f);
    private static readonly Color RiverColor = new Color(0.2f, 0.5f, 0.9f, 0.6f);
    private const float CountryTerrainAlpha = 0.55f;

    // HOI4-style muted country palette
    private static readonly Color[] CountryPalette =
    {
        new Color("#8F8259"), new Color("#7B8471"), new Color("#9C8862"), new Color("#7D5F49"),
        new Color("#6E7F8D"), new Color("#8C6E83"), new Color("#5F8F7A"), new Color("#8A9550"),
        new Color("#6B7B9C"), new Color("#9C7C5B"), new Color("#7A8F6E"), new Color("#8F6E5F"),
        new Color("#5C7A8F"), new Color("#99886B"), new Color("#6E6B8F"), new Color("#8F7A6B"),
        new Color("#4F6E5A"), new Color("#7A6E8F"), new Color("#8E8A7A"), new Color("#6E8F8A"),
        new Color("#9C6B5A"), new Color("#5C8F6B"), new Color("#8F8A6E"), new Color("#6B5E52"),
    };

    // Data structures
    public sealed class Country
    {
        public string Id = "?";
        public string Name = "?";
        public readonly List<Vector2[]> Rings = new();
        public readonly List<Polygon2D> FillNodes = new();
        public readonly List<Line2D> BorderOutlines = new();
        public readonly List<Line2D> GlowOutlines = new();
        public Color MapColor;
        public Vector2 LabelAnchor;
        public Vector2 LabelBBox;
        public Label NameLabel;
        public float FontSize = 13;
    }

    public sealed class City
    {
        public string Id;
        public string Name;
        public string CountryId;
        public Vector2 Position;
        public int Population;
        public string Type; // capital, megacity, major, large, medium, minor, small
        public bool IsCapital;
        public int Priority;
        public Vector2 LabelAnchor;
        public Sprite2D MarkerSprite;
        public Label NameLabel;
        public bool Visible = false;
    }

    public sealed class SeaLabel
    {
        public string Name;
        public Vector2 Anchor;
        public string Type; // ocean, sea
        public float MinZoom;
        public float MaxZoom;
        public Label Label;
    }

    // Member variables
    private readonly List<Country> _countries = new();
    private readonly List<City> _cities = new();
    private readonly List<SeaLabel> _seaLabels = new();

    // Province system (from existing)
    private List<ProvinceData> _provinces = new();
    private Dictionary<string, List<Polygon2D>> _provinceNodes = new();
    private Dictionary<string, Rect2> _provinceBounds = new();
    private const float ProvinceCellSize = 128f;
    private Dictionary<long, List<int>> _provinceGrid = new();
    private Dictionary<string, ProvinceData> _provinceById = new();

    // Layers (CanvasLayers for render order)
    private CanvasLayer _layerOcean;      // Layer -2: Ocean bathymetry
    private CanvasLayer _layerTerrain;    // Layer -1: Terrain (NE1 + shaded relief)
    private CanvasLayer _layerCountries;  // Layer 0: Country fills + borders
    private CanvasLayer _layerProvinces;  // Layer 1: Province borders
    private CanvasLayer _layerRivers;     // Layer 2: Rivers/coastline
    private CanvasLayer _layerCities;     // Layer 3: City markers
    private CanvasLayer _layerLabels;     // Layer 4: Country/city/sea labels
    private CanvasLayer _layerUI;         // Layer 5: UI overlays

    // Textures
    private Texture2D _oceanBathymetryTex;
    private Texture2D _terrainBaseTex;        // NE1_HR_LC_SR_W_DR
    private Texture2D _terrainReliefTex;      // SR_HR (shaded relief)
    private Texture2D _terrainIdealizedTex;   // NE2_HR_LC_SR_W_DR (optional)
    private GradientTexture2D _oceanGradientTex;
    private GradientTexture2D _vignetteTex;

    // Shaders
    private ShaderMaterial _terrainShaderMat;
    private ShaderMaterial _oceanShaderMat;

    // State
    private Camera2D _camera;
    private Country _selectedCountry;
    private Country _hoveredCountry;
    private City _hoveredCity;
    private readonly HashSet<string> _selectedProvinceIds = new();
    private Vector2 _selectStart, _selectEnd;
    private bool _selecting, _lmbPressed, _dragging;
    private Vector2 _lastMousePos, _rmbPressPos;
    private float _goldPulse = 0f;
    private bool _cameraNeedsFit = true;
    private float _lastBorderZoom = -1f;
    private int _frameCounter;

    // Camera animation
    private bool _zoomAnim;
    private float _targetZoom = 1f;
    private Vector2 _targetPos = Vector2.Zero;

    // Factories mode
    private int _maxFactories = 1;
    private bool _factoriesComputed = false;

    // Frontlines/Plans
    private sealed class FrontlineSegment { public Vector2 A, B; public Color Color = new Color(1f, 0.35f, 0.2f, 0.85f); }
    private sealed class PlanArrow { public string SourceProvinceId, TargetProvinceId; public List<Vector2> Points = new(); public List<string> ProvincePath = new(); public float CreatedDay; public Color Color = new Color(0.95f, 0.75f, 0.2f, 0.9f); }
    private readonly List<FrontlineSegment> _frontlines = new();
    private bool _frontlinesDirty = true;
    private int _lastActiveWarCount = -1;
    private PlanArrow _activePlan;
    private bool _drawingPlan;
    private Vector2 _planPressScreen;
    private string _planSourceProvinceId = "";
    private readonly List<Vector2> _planPoints = new();
    private const float PlanMinDragPixels = 12f;
    private const float SelectDragThreshold = 8f;

    // Culture
    private readonly HashSet<string> _cultureMismatchProvinces = new(StringComparer.Ordinal);
    private PanelContainer _cultureTip;
    private Label _cultureTipLabel;
    private bool _cultureTipDirty = true;
    private string _cultureHighlight = "";

    // Fonts
    private FontFile _fontCountry;      // Cinzel
    private FontFile _fontProvince;     // Oswald
    private FontFile _fontSea;          // Cormorant
    private FontFile _fontCity;         // Oswald

    public override void _Ready()
    {
        Instance = this;

        // Initialize layers in render order (lowest first)
        _layerOcean = CreateLayer(-2, "Ocean");
        _layerTerrain = CreateLayer(-1, "Terrain");
        _layerCountries = CreateLayer(0, "Countries");
        _layerProvinces = CreateLayer(1, "Provinces");
        _layerRivers = CreateLayer(2, "Rivers");
        _layerCities = CreateLayer(3, "Cities");
        _layerLabels = CreateLayer(4, "Labels");
        _layerUI = CreateLayer(5, "UI");

        // Load fonts
        LoadFonts();

        // Load textures
        LoadTextures();

        // Create shaders
        CreateShaders();

        // Build ocean gradient
        _oceanGradientTex = CreateOceanGradientTexture();
        _vignetteTex = CreateVignetteTexture();

        // Build camera
        BuildCamera();

        // Load map data
        LoadMapData();

        // Subscribe to events
        if (GameManager.IsAvailable)
        {
            GameManager.Instance.PlayerCountryChanged += OnPlayerCountryChanged;
            GameManager.Instance.CountryOwnershipChanged += OnCountryOwnershipChanged;
        }

        // Load provinces
        var provinceSource = new ProceduralProvinceSource();
        SetProvinces(provinceSource.LoadProvinces());
        ProvinceOwnershipSystem.OnOwnerChanged += OnProvinceOwnerChanged;

        // Build UI
        BuildCultureTip();
        ApplyMapViewState();

        SetProcessInput(true);
        SetProcess(true);

        RefreshOwnershipColors();
        QueueRedraw();

        GD.Print($"WorldMapV4: ready — {_countries.Count} countries, {_cities.Count} cities loaded");
    }

    private CanvasLayer CreateLayer(int layer, string name)
    {
        var layerNode = new CanvasLayer { Layer = layer, Name = name };
        AddChild(layerNode);
        return layerNode;
    }

    private void LoadFonts()
    {
        _fontCountry = GD.Load<FontFile>("res://Assets/fonts/Cinzel-Variable.ttf");
        _fontProvince = GD.Load<FontFile>("res://Assets/fonts/Oswald-Variable.ttf");
        _fontSea = GD.Load<FontFile>("res://Assets/fonts/Cormorant-Variable.ttf");
        _fontCity = GD.Load<FontFile>("res://Assets/fonts/Oswald-Variable.ttf");

        if (_fontCountry == null) GD.PrintErr("WorldMapV4: Cinzel font not found!");
        if (_fontProvince == null) GD.PrintErr("WorldMapV4: Oswald font not found!");
        if (_fontSea == null) GD.PrintErr("WorldMapV4: Cormorant font not found!");
    }

    private void ApplyMapViewState()
    {
        var mode = MapViewSystem.Mode;
        SetMapMode((MapMode)(int)mode);
        string highlight = mode == WorldMap.MapMode.Culture ? MapViewSystem.CultureHighlight : "";
        if (mode != WorldMap.MapMode.Culture) MapViewSystem.SetCultureHighlight("");
        SetCultureHighlight(highlight);
        GD.Print($"[MapView V4] Applied: mode={mode}, highlight={(highlight.Length > 0 ? highlight : "(none)")}");
    }

    public string CultureHighlight => _cultureHighlight;

    public void SetCultureHighlight(string culture)
    {
        string next = culture ?? "";
        if (_cultureHighlight == next) return;
        _cultureHighlight = next;
        MapViewSystem.SetCultureHighlight(next);

        if (CurrentMapMode != MapMode.Culture)
        {
            if (next.Length > 0) SetMapMode(MapMode.Culture);
        }
        else
        {
            RefreshProvinceColors();
        }

        QueueRedraw();
        GD.Print($"WorldMapV4: culture highlight = {(next.Length > 0 ? next : "(none)")}");
    }

    private void LoadTextures()
    {
        // Ocean bathymetry
        _oceanBathymetryTex = GD.Load<Texture2D>("res://Assets/map_textures/ocean_bathymetry.png");
        if (_oceanBathymetryTex == null) GD.PrintErr("WorldMapV4: Ocean bathymetry texture not found!");

        // Terrain base (Natural Earth I - cross-blended hypsometric + shaded relief)
        _terrainBaseTex = GD.Load<Texture2D>("res://Assets/map_textures/natural_earth_1.png");
        if (_terrainBaseTex == null) GD.PrintErr("WorldMapV4: Terrain base texture (NE1) not found!");

        // Terrain relief (Shaded relief grayscale)
        _terrainReliefTex = GD.Load<Texture2D>("res://Assets/map_textures/shaded_relief.png");
        if (_terrainReliefTex == null) GD.PrintErr("WorldMapV4: Terrain relief texture (SR_HR) not found!");

        // Terrain idealized (Natural Earth II - optional)
        _terrainIdealizedTex = GD.Load<Texture2D>("res://Assets/map_textures/natural_earth_2.png");
        if (_terrainIdealizedTex == null) GD.Print("WorldMapV4: Terrain idealized texture (NE2) not found (optional)");

        GD.Print("WorldMapV4: Textures loaded");
    }

    private void CreateShaders()
    {
        // Terrain shader: multiply blend shaded relief + saturation control
        _terrainShaderMat = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/terrain_shader.gdshader")
        };
        if (_terrainShaderMat.Shader == null)
        {
            // Create inline if file doesn't exist
            _terrainShaderMat.Shader = new Shader
            {
                Code = @"
shader_type canvas_item;
render_mode blend_mix;

uniform sampler2D terrain_texture : source_color;
uniform sampler2D shaded_relief : source_color;
uniform float shaded_relief_strength : hint_range(0, 1) = 0.3;
uniform float terrain_saturation : hint_range(0, 2) = 1.0;

void fragment() {
    vec4 terrain = texture(terrain_texture, UV);
    vec4 relief = texture(shaded_relief, UV);

    // Multiply blend for shaded relief
    vec3 result = terrain.rgb * (1.0 - shaded_relief_strength) +
                  terrain.rgb * relief.r * shaded_relief_strength;

    // Saturation control
    float gray = dot(result, vec3(0.299, 0.587, 0.114));
    result = mix(vec3(gray), result, terrain_saturation);

    COLOR = vec4(result, terrain.a);
}"
            };
        }
        _terrainShaderMat.SetShaderParameter("terrain_texture", _terrainBaseTex);
        _terrainShaderMat.SetShaderParameter("shaded_relief", _terrainReliefTex);
        _terrainShaderMat.SetShaderParameter("shaded_relief_strength", 0.35f);
        _terrainShaderMat.SetShaderParameter("terrain_saturation", 1.0f);

        // Ocean shader: bathymetry coloring + gradient
        _oceanShaderMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"
shader_type canvas_item;
render_mode blend_mix;

uniform sampler2D bathymetry : source_color;
uniform sampler2D gradient : source_color;
uniform float gradient_strength : hint_range(0, 1) = 0.6;

void fragment() {
    vec4 bathy = texture(bathymetry, UV);
    vec4 grad = texture(gradient, UV);

    // Bathymetry is grayscale (depth), use it to color ocean
    float depth = bathy.r;

    // Deep ocean = dark navy, shallow = lighter blue
    vec3 deep = vec3(0.05, 0.15, 0.25);
    vec3 shallow = vec3(0.15, 0.35, 0.55);
    vec3 coast = vec3(0.2, 0.5, 0.7);

    vec3 oceanColor = mix(deep, shallow, depth);
    oceanColor = mix(oceanColor, coast, smoothstep(0.0, 0.1, depth));

    // Blend with gradient (latitude-based)
    vec3 finalColor = mix(oceanColor, grad.rgb, gradient_strength);

    COLOR = vec4(finalColor, 1.0);
}"
            }
        };
        _oceanShaderMat.SetShaderParameter("bathymetry", _oceanBathymetryTex);
        _oceanShaderMat.SetShaderParameter("gradient", _oceanGradientTex);
        _oceanShaderMat.SetShaderParameter("gradient_strength", 0.5f);
    }

    private GradientTexture2D CreateOceanGradientTexture()
    {
        var g = new GradientTexture2D
        {
            Width = 16,
            Height = 512,
            Fill = 0,
            FillFrom = new Vector2(0f, 0f),
            FillTo = new Vector2(0f, 1f),
            Gradient = new Gradient
            {
                Colors = new[]
                {
                    new Color("#082030"), // North pole
                    new Color("#0A2B3A"),
                    new Color("#0E4A5A"), // Equator
                    new Color("#0C3A4A"),
                    new Color("#082030")  // South pole
                }
            }
        };
        return g;
    }

    private GradientTexture2D CreateVignetteTexture()
    {
        var g = new GradientTexture2D
        {
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 1f),
            Gradient = new Gradient
            {
                Colors = new[]
                {
                    new Color(0f, 0f, 0f, 0f),      // Center transparent
                    new Color(0f, 0f, 0f, 0.15f),   // Mid
                    new Color(0f, 0f, 0f, 0.35f)    // Edges
                }
            }
        };
        return g;
    }

    private void LoadMapData()
    {
        LoadCountries();
        LoadCities();
        LoadSeaLabels();
        GD.Print($"WorldMapV4: map data loaded — {_countries.Count} countries, {_cities.Count} cities, {_seaLabels.Count} sea labels");
    }

    private void LoadCountries()
    {
        string path = MapDataPath + "countries.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"WorldMapV4: {path} not found!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var countriesData = JsonSerializer.Deserialize<List<CountryDataJson>>(FileAccess.GetFileAsString(path), options);
            if (countriesData == null) return;

            // Load high-detail borders from country_borders.json
            var borderData = LoadCountryBorders();

            foreach (var data in countriesData)
            {
                var country = new Country
                {
                    Id = data.iso3,
                    Name = data.name,
                    MapColor = ColorForId(data.iso3),
                };

                // Get rings from border data
                if (borderData.TryGetValue(data.iso3, out var rings) && rings.Count > 0)
                {
                    foreach (var ring in rings)
                    {
                        if (ring.Length >= 3)
                        {
                            var clipped = ClipRingToMap(ring, MapSize);
                            country.Rings.Add(clipped);
                        }
                    }
                }

                if (country.Rings.Count == 0) continue;

                CreateCountryVisuals(country);
                SetupNameLabel(country);
                _countries.Add(country);
            }

            GD.Print($"WorldMapV4: loaded {_countries.Count} countries");
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV4: failed to load countries: {e.Message}");
        }
    }

    private Dictionary<string, List<Vector2[]>> LoadCountryBorders()
    {
        string path = MapDataPath + "country_borders.json";
        var result = new Dictionary<string, List<Vector2[]>>();

        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"WorldMapV4: {path} not found!");
            return result;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FileAccess.GetFileAsString(path), options);
            if (data == null) return result;

            foreach (var kvp in data)
            {
                var rings = new List<Vector2[]>();
                if (kvp.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var polygon in kvp.Value.EnumerateArray())
                    {
                        var vecRing = ConvertRing(polygon);
                        if (vecRing != null && vecRing.Length >= 3)
                        {
                            var clipped = ClipRingToMap(vecRing, MapSize);
                            rings.Add(clipped);
                        }
                    }
                }
                if (rings.Count > 0)
                    result[kvp.Key] = rings;
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV4: failed to load country borders: {e.Message}");
        }

        return result;
    }

    private void LoadCities()
    {
        string path = "res://Data/map/cities_runtime.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"WorldMapV4: {path} not found! Run process_cities.py first.");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var data = JsonSerializer.Deserialize<CityRuntimeData>(FileAccess.GetFileAsString(path), options);
            if (data == null || data.Cities == null) return;

            // Create city marker texture
            var markerTex = CreateCityMarkerTexture();

            foreach (var c in data.Cities)
            {
                var city = new City
                {
                    Id = c.Id,
                    Name = c.Name,
                    CountryId = c.Country,
                    Position = new Vector2(c.X, c.Y),
                    Population = c.Pop,
                    Type = c.Type,
                    IsCapital = c.Cap,
                    Priority = c.Pri,
                    LabelAnchor = new Vector2(c.Lanchor[0], c.Lanchor[1]),
                };

                // Create marker sprite
                city.MarkerSprite = new Sprite2D
                {
                    Texture = markerTex,
                    Visible = false,
                    Position = city.Position,
                    Scale = GetCityMarkerScale(c.Type),
                    Modulate = GetCityMarkerColor(c.Type),
                    ZIndex = 10
                };
                _layerCities.AddChild(city.MarkerSprite);

                // Create name label
                city.NameLabel = new Label
                {
                    Text = city.Name,
                    Visible = false,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Position = city.LabelAnchor
                };
                city.NameLabel.AddThemeFontSizeOverride("font_size", GetCityFontSize(c.Type));
                city.NameLabel.AddThemeFontOverride("font", _fontCity);
                city.NameLabel.AddThemeColorOverride("font_color", Colors.White);
                city.NameLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
                city.NameLabel.AddThemeConstantOverride("shadow_offset_x", 1);
                city.NameLabel.AddThemeConstantOverride("shadow_offset_y", 1);
                _layerLabels.AddChild(city.NameLabel);

                _cities.Add(city);
            }

            GD.Print($"WorldMapV4: loaded {_cities.Count} cities");
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV4: failed to load cities: {e.Message}");
        }
    }

    private void LoadSeaLabels()
    {
        string path = "res://Data/sea_labels.json";
        if (!FileAccess.FileExists(path)) return;

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var labels = JsonSerializer.Deserialize<List<SeaLabelJson>>(FileAccess.GetFileAsString(path), options);
            if (labels == null) return;

            foreach (var d in labels)
            {
                var anchor = Project(d.Lon, d.Lat);
                var label = new Label
                {
                    Text = d.Name,
                    Visible = false,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                label.AddThemeFontSizeOverride("font_size", d.Type == "ocean" ? 18 : 14);
                label.AddThemeFontOverride("font", _fontSea);
                label.AddThemeColorOverride("font_color", new Color(0.65f, 0.82f, 1f, d.Type == "ocean" ? 0.85f : 0.75f));
                label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0.1f, 0.2f, 0.85f));
                label.AddThemeConstantOverride("shadow_offset_x", 1);
                label.AddThemeConstantOverride("shadow_offset_y", 1);
                label.AddThemeConstantOverride("italic", 1); // 1 = true, 0 = false

                _layerLabels.AddChild(label);
                _seaLabels.Add(new SeaLabel
                {
                    Name = d.Name,
                    Anchor = anchor,
                    Type = d.Type,
                    MinZoom = d.MinZoom > 0 ? d.MinZoom : 0f,
                    MaxZoom = d.MaxZoom > 0 ? d.MaxZoom : 999f,
                    Label = label
                });
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV4: failed to load sea labels: {e.Message}");
        }
    }

    // ========== Rendering ==========

    public override void _Draw()
    {
        // Note: Most rendering is done via CanvasLayer children (TextureRect, Sprite2D, etc.)
        // This _Draw is for UI overlays: selection, frontlines, plans, etc.

        DrawSelection();
        DrawFrontlines();
        DrawPlan();
        DrawCultureMismatchContours();
    }

    public override void _Process(double delta)
    {
        float fDelta = (float)delta;
        HandleKeyboardPan(fDelta);
        UpdateLabels(fDelta);
        UpdateCityVisibility(fDelta);
        AnimateSelectedBorder(fDelta);
        UpdateBorderWidthsByZoom();
        AnimateZoom(fDelta);

        if (_cultureTipDirty) RefreshCultureTipText();
        PositionCultureTip();

        _frameCounter++;
        if (_frameCounter % 120 == 0)
        {
            float fps = 1f / Mathf.Max((float)delta, 0.0001f);
            float zoom = _camera?.Zoom.X ?? 0f;
            GD.Print($"[PERF] WorldMapV4 FPS={fps:F1} zoom={zoom:F2} countries={_countries.Count} cities={_cities.Count} provinces={_provinces.Count}");
        }

        // War frontlines
        int activeWarCount = WarSystem.ActiveWars.Count;
        if (activeWarCount != _lastActiveWarCount)
        {
            _lastActiveWarCount = activeWarCount;
            _frontlinesDirty = true;
        }

        // Era tone
        if (GameManager.IsAvailable)
        {
            int year = GameManager.Instance.CurrentDate.Year;
            float targetAlpha = year <= 1938 ? 0f : (year <= 1941 ? 0.04f : 0.07f);
            Color targetColor = year <= 1938 ? Colors.Transparent
                                : (year <= 1941 ? new Color(1f, 0.4f, 0.2f, 1f)
                                               : new Color(0.6f, 0.2f, 0.15f, 1f));
            float lerpSpeed = Mathf.Clamp(fDelta * 2f, 0f, 1f);
            // Apply to shader or era overlay
        }

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

    private void UpdateLabels(float delta)
    {
        if (_camera == null) return;
        var vp = GetViewportRect().Size;
        float zoom = _camera.Zoom.X;

        // Country labels
        foreach (var c in _countries)
        {
            if (c.NameLabel == null || !GodotObject.IsInstanceValid(c.NameLabel)) continue;

            Vector2 screen = (c.LabelAnchor - _camera.Position) * zoom + vp / 2f;
            float wpx = c.LabelBBox.X * zoom;
            float hpx = c.LabelBBox.Y * zoom;

            bool bigEnough = wpx > 60f && hpx > 25f;
            bool onScreen = screen.X > -100f && screen.X < vp.X + 100f && screen.Y > -50f && screen.Y < vp.Y + 50f;

            c.NameLabel.Visible = bigEnough && onScreen && zoom >= ZOOM_STRATEGIC_MAX;
            if (c.NameLabel.Visible)
                c.NameLabel.Position = screen - c.NameLabel.Size / 2f;
        }

        // Sea labels
        foreach (var sl in _seaLabels)
        {
            bool inRange = zoom >= sl.MinZoom && zoom <= sl.MaxZoom;
            Vector2 screen = (sl.Anchor - _camera.Position) * zoom + vp / 2f;
            bool onScreen = screen.X > -100f && screen.X < vp.X + 100f && screen.Y > -50f && screen.Y < vp.Y + 50f;
            sl.Label.Visible = inRange && onScreen;
            if (sl.Label.Visible)
                sl.Label.Position = screen - sl.Label.Size / 2f;
        }
    }

    private void UpdateCityVisibility(float delta)
    {
        if (_camera == null) return;
        var vp = GetViewportRect().Size;
        float zoom = _camera.Zoom.X;

        bool showMarkers = zoom >= ZOOM_TACTICAL_MAX;
        bool showNames = zoom >= ZOOM_DETAIL_MIN;

        foreach (var city in _cities)
        {
            bool onScreen = true;
            if (_camera != null)
            {
                Vector2 screen = (city.Position - _camera.Position) * zoom + vp / 2f;
                onScreen = screen.X > -50f && screen.X < vp.X + 50f && screen.Y > -50f && screen.Y < vp.Y + 50f;
            }

            if (city.MarkerSprite != null && GodotObject.IsInstanceValid(city.MarkerSprite))
            {
                city.MarkerSprite.Visible = showMarkers && onScreen;
                if (showMarkers && onScreen)
                {
                    float scale = Mathf.Clamp(0.15f * zoom, 0.08f, 0.5f);
                    city.MarkerSprite.Scale = new Vector2(scale, scale);
                }
            }

            if (city.NameLabel != null && GodotObject.IsInstanceValid(city.NameLabel))
            {
                city.NameLabel.Visible = showNames && onScreen && zoom >= ZOOM_DETAIL_MIN;
                if (city.NameLabel.Visible)
                {
                    Vector2 screen = (city.LabelAnchor - _camera.Position) * zoom + vp / 2f;
                    city.NameLabel.Position = screen - city.NameLabel.Size / 2f;
                }
            }
        }
    }

    // ========== Visual Creation ==========

    private void CreateCountryVisuals(Country country)
    {
        foreach (var ring in country.Rings)
        {
            // Fill (semi-transparent, rendered on Countries layer)
            var fill = new Polygon2D
            {
                Polygon = ring,
                Color = new Color(country.MapColor.R, country.MapColor.G, country.MapColor.B, CountryTerrainAlpha),
                ZIndex = 0
            };
            _layerCountries.AddChild(fill);
            country.FillNodes.Add(fill);

            // Coastal glow (inner, turquoise)
            var glow = new Line2D
            {
                Points = ring,
                DefaultColor = CoastalGlowColor,
                Width = 3.5f,
                Closed = true,
                ZIndex = 1
            };
            _layerCountries.AddChild(glow);
            country.GlowOutlines.Add(glow);

            // Main border
            var outline = new Line2D
            {
                Points = ring,
                DefaultColor = BorderColor,
                Width = 1.1f,
                Closed = true,
                ZIndex = 2
            };
            _layerCountries.AddChild(outline);
            country.BorderOutlines.Add(outline);
        }

        // Compute label anchor from largest ring
        Vector2[] biggest = null;
        double biggestArea = -1;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var maxPt = new Vector2(float.MinValue, float.MinValue);

        foreach (var ring in country.Rings)
        {
            double area = Mathf.Abs(RingArea(ring));
            if (area > 0 && area < float.MaxValue) country.MapColor = country.MapColor; // placeholder
            if (area > biggestArea)
            {
                biggestArea = area;
                biggest = ring;
                min = new Vector2(float.MaxValue, float.MaxValue);
                maxPt = new Vector2(float.MinValue, float.MinValue);
                foreach (var pt in ring)
                {
                    if (pt.X < min.X) min.X = pt.X;
                    if (pt.Y < min.Y) min.Y = pt.Y;
                    if (pt.X > maxPt.X) maxPt.X = pt.X;
                    if (pt.Y > maxPt.Y) maxPt.Y = pt.Y;
                }
            }
        }

        if (biggest != null)
        {
            country.LabelAnchor = (min + maxPt) / 2f;
            country.LabelBBox = maxPt - min;
        }
    }

    private void SetupNameLabel(Country c)
    {
        if (c.LabelAnchor == Vector2.Zero) return;
        var label = new Label { Text = c.Name, Visible = false };
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeFontOverride("font", _fontCountry);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        c.NameLabel = label;
        _layerLabels.AddChild(label);
    }

    private Texture2D CreateCityMarkerTexture()
    {
        // Create procedural city marker (circle with outline)
        var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        var center = new Vector2(32, 32);

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float dist = new Vector2(x, y).DistanceTo(center);
                if (dist < 28f)
                {
                    float alpha = 1f - (dist / 28f);
                    img.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
                else if (dist < 32f)
                {
                    float alpha = (32f - dist) / 4f * 0.5f;
                    img.SetPixel(x, y, new Color(0, 0, 0, alpha));
                }
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private Vector2 GetCityMarkerScale(string type)
    {
        return type switch
        {
            "capital" => new Vector2(0.25f, 0.25f),
            "megacity" => new Vector2(0.22f, 0.22f),
            "major" => new Vector2(0.18f, 0.18f),
            "large" => new Vector2(0.15f, 0.15f),
            "medium" => new Vector2(0.12f, 0.12f),
            _ => new Vector2(0.1f, 0.1f)
        };
    }

    private Color GetCityMarkerColor(string type)
    {
        return type switch
        {
            "capital" => new Color(1f, 0.85f, 0.1f, 1f),      // Gold
            "megacity" => new Color(1f, 0.4f, 0.1f, 1f),       // Orange
            "major" => new Color(1f, 0.7f, 0.2f, 1f),          // Amber
            "large" => new Color(0.9f, 0.9f, 0.9f, 1f),        // White
            "medium" => new Color(0.7f, 0.7f, 0.7f, 1f),       // Light gray
            _ => new Color(0.5f, 0.5f, 0.5f, 1f)               // Gray
        };
    }

    private int GetCityFontSize(string type)
    {
        return type switch
        {
            "capital" => 12,
            "megacity" => 11,
            "major" => 10,
            "large" => 9,
            _ => 8
        };
    }

    // ========== Camera & Input ==========

    private void BuildCamera()
    {
        _camera = new Camera2D { Position = MapSize / 2f };
        AddChild(_camera);
        _camera.MakeCurrent();
        FitCameraToViewport();
    }

    private void FitCameraToViewport()
    {
        if (_camera == null) return;
        var vp = GetViewportRect().Size;
        if (vp.X <= 0 || vp.Y <= 0) vp = MapSize;
        float fit = Mathf.Min(vp.X / MapSize.X, vp.Y / MapSize.Y) * 0.95f;
        _camera.Zoom = new Vector2(fit, fit);
        _camera.Position = new Vector2(MapSize.X / 2f, MapSize.Y / 2f - 80f);
    }

    public void EnsureCameraCurrent()
    {
        if (_camera != null && GodotObject.IsInstanceValid(_camera) && !_camera.IsCurrent())
            _camera.MakeCurrent();
    }

    private void AnimateZoom(float delta)
    {
        // ... (same as WorldMapV3)
    }

    private Vector2 ClampedTargetPos() => ClampCameraPos(_targetPos);

    internal Vector2 ClampCameraPos(Vector2 pos)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, -400f, MapSize.X + 400f),
            Mathf.Clamp(pos.Y, -400f, MapSize.Y + 400f));
    }

    private void HandleKeyboardPan(float delta)
    {
        // ... (same as WorldMapV3)
    }

    private void ZoomAt(Vector2 screenPos, float factor)
    {
        // ... (same as WorldMapV3)
    }

    public override void _Input(InputEvent e)
    {
        // ... (same as WorldMapV3, adapted for V4)
    }

    // ========== Map Modes & Colors ==========

    public void SetMapMode(MapMode mode)
    {
        if (CurrentMapMode == mode) return;
        CurrentMapMode = mode;
        // Convert to WorldMap.MapMode for MapViewSystem
        MapViewSystem.SetMode((WorldMap.MapMode)(int)mode);
        RefreshOwnershipColors();
        RefreshProvinceColors();
        QueueRedraw();
    }

    private Color ModeColor(Country c)
    {
        if (c == null) return Colors.Transparent;
        var data = CountryDatabase.Get(c.Id);

        switch (CurrentMapMode)
        {
            case MapMode.Political: return c.MapColor;
            case MapMode.Terrain: return Colors.Transparent;
            case MapMode.Factories:
                {
                    int factories = data?.TotalFactories ?? 0;
                    EnsureMaxFactories();
                    float t = _maxFactories > 0 ? Mathf.Clamp((float)factories / (float)_maxFactories, 0f, 1f) : 0f;
                    var from = new Color("#333333");
                    var to = new Color("#FFD700");
                    return new Color(from.R + (to.R - from.R) * t, from.G + (to.G - from.G) * t,
                                   from.B + (to.B - from.B) * t, from.A + (to.A - from.A) * t);
                }
            case MapMode.Resources:
                if (data == null) return Colors.Transparent;
                if (data.Oil > 0) return new Color("#8B4513");
                if (data.Steel > 0) return new Color("#708090");
                if (data.Rubber > 0) return new Color("#2E8B57");
                if (data.Aluminum > 0) return new Color("#B0C4DE");
                if (data.Tungsten > 0) return new Color("#4B0082");
                if (data.Chromium > 0) return new Color("#DC143C");
                return Colors.Transparent;
            case MapMode.Supply: return new Color(0.15f, 0.45f, 0.85f, 0.45f);
            case MapMode.AirZones: return new Color(0.5f, 0.35f, 0.9f, 0.35f);
            case MapMode.Culture: return Colors.Transparent;
            default: return c.MapColor;
        }
    }

    private Color ModeColorForCountryId(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return Colors.Transparent;
        var country = _countries.FirstOrDefault(c => c.Id == countryId);
        return country != null ? ModeColor(country) : Colors.Transparent;
    }

    private void EnsureMaxFactories()
    {
        // ... (same)
    }

    public void RefreshOwnershipColors()
    {
        foreach (var c in _countries)
        {
            Color target;
            if (CurrentMapMode != MapMode.Political)
            {
                target = ModeColor(c);
            }
            else
            {
                target = c.MapColor;
                var data = CountryDatabase.Get(c.Id);
                if (data != null && data.IsAnnexed && !string.IsNullOrEmpty(data.OwnerId))
                {
                    var owner = CountryDatabase.Get(data.OwnerId);
                    if (owner != null && owner.Id != c.Id)
                        target = ColorForId(owner.Id).Darkened(0.12f);
                }

                if (c == _selectedCountry) target = SelectedColor;
                else if (c == _hoveredCountry) target = c.MapColor.Lightened(0.25f);
                else target = new Color(target.R, target.G, target.B, CountryTerrainAlpha);
            }

            foreach (var node in c.FillNodes)
                if (GodotObject.IsInstanceValid(node)) node.Color = target;

            foreach (var outline in c.BorderOutlines)
                if (GodotObject.IsInstanceValid(outline) && c != _selectedCountry)
                    outline.DefaultColor = IsPlayerCountry(c) ? PlayerBorderColor : BorderColor;
        }
    }

    private static bool IsPlayerCountry(Country c)
    {
        return GameManager.IsAvailable && !string.IsNullOrEmpty(GameManager.Instance.PlayerCountryId)
            && c.Id == GameManager.Instance.PlayerCountryId;
    }

    private static Color ColorForId(string id)
    {
        if (id == "ATA" || id == "ATF") return new Color("#93A5B1");
        unchecked
        {
            int hash = 0; foreach (char ch in id) hash = hash * 31 + ch;
            var baseColor = CountryPalette[Mathf.Abs(hash) % CountryPalette.Length];
            int v = (hash >> 12) % 5;
            if (v == 0) return baseColor.Darkened(0.08f);
            if (v == 4) return baseColor.Lightened(0.08f);
            return baseColor;
        }
    }

    // ========== Selection & Interaction ==========

    private Country FindCountryAt(Vector2 worldPos)
    {
        Country best = null;
        float bestArea = float.MaxValue;
        foreach (var c in _countries)
        {
            foreach (var ring in c.Rings)
            {
                if (Geometry2D.IsPointInPolygon(worldPos, ring))
                {
                    // Use smallest ring area for nested territories
                    if (c.Rings.Count > 0)
                    {
                        float area = Mathf.Abs((float)RingArea(c.Rings[0]));
                        if (area < bestArea) { best = c; bestArea = area; }
                    }
                    break;
                }
            }
        }
        return best;
    }

    private City FindCityAt(Vector2 worldPos)
    {
        if (_camera == null) return null;
        float zoom = _camera.Zoom.X;
        float clickRadius = 15f / zoom; // Screen-space radius

        City best = null;
        float bestDist = float.MaxValue;
        foreach (var city in _cities)
        {
            float dist = city.Position.DistanceTo(worldPos);
            if (dist < clickRadius && dist < bestDist)
            {
                best = city;
                bestDist = dist;
            }
        }
        return best;
    }

    // ========== Event Handlers ==========

    private void OnPlayerCountryChanged(string id)
    {
        _lastBorderZoom = -1f;
        RefreshOwnershipColors();
    }

    private void OnCountryOwnershipChanged()
    {
        _lastBorderZoom = -1f;
        RefreshOwnershipColors();
        GD.Print("WorldMapV4: ownership colors updated");
    }

    private void OnProvinceOwnerChanged(string provinceId, string oldCountryId, string newCountryId)
    {
        RefreshProvinceColors();
        QueueRedraw();
    }

    // ========== Utility ==========

    private Vector2 Project(double lon, double lat)
    {
        float x = (float)((lon + 180.0) / 360.0) * MapSize.X;
        float topMargin = 80f;
        float usableH = MapSize.Y - topMargin * 2;
        float y = topMargin + (float)((90.0 - lat) / 180.0) * usableH;
        return new Vector2(x, y);
    }

    private static double RingArea(Vector2[] ring)
    {
        double area = 0; for (int i = 0; i < ring.Length; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Length]; area += (double)a.X * b.Y - (double)b.X * a.Y; }
        return area / 2.0;
    }

    private Vector2[] ConvertRing(JsonElement ring)
    {
        if (ring.ValueKind != JsonValueKind.Array || ring.GetArrayLength() == 0) return null;
        var points = new List<Vector2>();
        foreach (var coord in ring.EnumerateArray())
            if (coord.ValueKind == JsonValueKind.Array && coord.GetArrayLength() >= 2)
                points.Add(Project(coord[0].GetDouble(), coord[1].GetDouble()));
        return points.Count >= 3 ? points.ToArray() : null;
    }

    private Vector2[] ClipRingToMap(Vector2[] ring, Vector2 mapSize)
    {
        // ... (same Sutherland-Hodgman clipping as WorldMap)
        return ring; // Simplified
    }

    // ========== JSON DTOs ==========

    private class CountryDataJson
    {
        public string iso3 { get; set; }
        public string name { get; set; }
        public string sovereign { get; set; }
        public double area_km2 { get; set; }
        public double[] bbox { get; set; }
        public Dictionary<string, JsonElement> lod { get; set; }
    }

    private class CityRuntimeData
    {
        public CityJson[] Cities { get; set; }
    }

    private class CityJson
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Country { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public int Pop { get; set; }
        public string Type { get; set; }
        public bool Cap { get; set; }
        public int Pri { get; set; }
        public float[] Lanchor { get; set; }
    }

    private class SeaLabelJson
    {
        public string Name { get; set; }
        public double Lon { get; set; }
        public double Lat { get; set; }
        public string Type { get; set; }
        public float MinZoom { get; set; }
        public float MaxZoom { get; set; }
    }

    // ========== Boilerplate (Selection, Frontlines, Plans, Provinces) ==========
    // ... (Copy from WorldMapV3 for: DrawSelection, DrawFrontlines, DrawPlan,
    //      AnimateSelectedBorder, UpdateBorderWidthsByZoom, AnimateZoom,
    //      SetProvinces, DrawProvinces, RefreshProvinceColors, FindProvinceAt,
    //      BuildProvinceGrid, Culture mismatch, Culture tip, etc.)

    // Placeholder methods - copy full implementation from WorldMapV3
    private void DrawSelection() { /* ... */ }
    private void DrawFrontlines() { /* ... */ }
    private void DrawPlan() { /* ... */ }
    private void DrawCultureMismatchContours() { /* ... */ }
    private void AnimateSelectedBorder(float delta) { /* ... */ }
    private void UpdateBorderWidthsByZoom() { /* ... */ }
    public void SetProvinces(IReadOnlyList<ProvinceData> provinces) { /* ... */ }
    private void DrawProvinces() { /* ... */ }
    public void RefreshProvinceColors() { /* ... */ }
    public ProvinceData FindProvinceAt(Vector2 worldPos) { return null; }
    private void BuildProvinceGrid() { /* ... */ }
    private void ComputeCultureMismatches() { /* ... */ }
    private void BuildCultureTip() { /* ... */ }
    private void RefreshCultureTipText() { /* ... */ }
    private void PositionCultureTip() { /* ... */ }

    public override void _ExitTree()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryChanged -= OnPlayerCountryChanged;
        if (GameManager.IsAvailable)
            GameManager.Instance.CountryOwnershipChanged -= OnCountryOwnershipChanged;
        ProvinceOwnershipSystem.OnOwnerChanged -= OnProvinceOwnerChanged;
        if (Instance == this) Instance = null;
    }
}