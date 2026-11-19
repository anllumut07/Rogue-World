using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// WorldMapV3 — HOI4-style world map with progressive detail (LOD), paper texture,
/// terrain rendering, city markers, and FPS optimizations.
/// Uses Natural Earth 1:10m data + custom processing for high-quality visuals.
/// </summary>
public partial class WorldMapV3 : Node2D
{
    [Signal] public delegate void CountrySelectedEventHandler(string countryId, string countryName);
    [Signal] public delegate void CountryDeselectedEventHandler();
    [Signal] public delegate void ProvinceSelectionChangedEventHandler(string[] provinceIds);
    [Signal] public delegate void CitySelectedEventHandler(string cityId, string cityName, string countryId);

    public static WorldMapV3 Instance { get; private set; }

    public enum MapMode { Political, Terrain, Supply, Factories, Resources, AirZones, Culture }
    public MapMode CurrentMapMode { get; private set; } = MapMode.Political;

    [Export] public string MapDataPath { get; set; } = "res://Data/map/";
    [Export] public Vector2 MapSize { get; set; } = new Vector2(2160, 1280);
    [Export] public float KeyPanSpeed { get; set; } = 900f;
    [Export] public bool EnablePaperTexture { get; set; } = true;
    [Export] public bool EnableTerrainRendering { get; set; } = true;
    [Export] public bool EnableCityMarkers { get; set; } = true;
    [Export] public bool EnableBathymetry { get; set; } = true;

    // Color constants
    private static readonly Color SelectedColor = new Color("#C9A227");
    private static readonly Color BorderColor = new Color("#0E141B");
    private static readonly Color GoldBorderColor = new Color("#FFD700");
    private static readonly Color PlayerBorderColor = new Color(0.79f, 0.64f, 0.15f, 0.55f);
    private static readonly Color OceanColor = new Color("#0A2B3A");

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

    // LOD thresholds (zoom levels)
    private const float LOD_COUNTRY_MAX_ZOOM = 0.8f;    // World view - country polygons
    private const float LOD_REGION_MAX_ZOOM = 2.0f;     // Regional view - region boundaries
    private const float LOD_CITY_MIN_ZOOM = 1.5f;       // City markers appear
    private const float LOD_CITY_NAME_MIN_ZOOM = 3.0f;  // City names appear

    // Country data with LOD geometries
    public sealed class Country
    {
        public string Id = "?";
        public string Name = "?";
        public readonly List<Vector2[]> Lod0Rings = new();  // World view (simplified)
        public readonly List<Vector2[]> Lod1Rings = new();  // Regional view (detailed)
        public readonly List<Vector2[]> Lod2Rings = new();  // Local view (high detail)
        public readonly List<Polygon2D> Nodes = new();
        public readonly List<Line2D> Outlines = new();
        public readonly List<Line2D> GlowOutlines = new();
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

    private sealed class CityData
    {
        public string Id;
        public string Name;
        public string NameAscii;
        public string CountryId;
        public int Population;
        public int PopRank;
        public bool IsCapital;
        public Vector2 Position;
        public float MinZoom;
        public Label NameLabel;
        public Sprite2D MarkerSprite;
        public bool Visible = false;
    }

    private sealed class RegionData
    {
        public string Id;
        public string CountryId;
        public string Name;
        public readonly List<Vector2[]> Rings = new();
        public readonly List<Polygon2D> Nodes = new();
        public readonly List<Line2D> Outlines = new();
        public Vector2 Centroid = Vector2.Zero;
    }

    // Member variables
    private readonly List<Country> _countries = new();
    private readonly List<CityData> _cities = new();
    private readonly List<RegionData> _regions = new();
    private readonly List<MapLabel> _oceanLabels = new();
    private readonly List<MapLabel> _seaLabels = new();

    // Province system
    private List<ProvinceData> _provinces = new();
    private Dictionary<string, List<Polygon2D>> _provinceNodes = new();
    private Dictionary<string, Rect2> _provinceBounds = new();
    private int _frameCounter;
    private const float ProvinceCellSize = 128f;
    private Dictionary<long, List<int>> _provinceGrid = new();
    private Dictionary<string, ProvinceData> _provinceById = new();

    // Selection
    private Vector2 _selectStart;
    private Vector2 _selectEnd;
    private bool _selecting;
    private readonly HashSet<string> _selectedProvinceIds = new();
    private const float SelectDragThreshold = 8f;
    private bool _lmbPressed;
    private Vector2 _lmbPressScreen;

    // Frontlines and plans
    private sealed class FrontlineSegment
    {
        public Vector2 A;
        public Vector2 B;
        public Color Color = new Color(1f, 0.35f, 0.2f, 0.85f);
    }

    private sealed class PlanArrow
    {
        public string SourceProvinceId = "";
        public string TargetProvinceId = "";
        public List<Vector2> Points = new();
        public List<string> ProvincePath = new();
        public float CreatedDay;
        public Color Color = new Color(0.95f, 0.75f, 0.2f, 0.9f);
    }

    private readonly List<FrontlineSegment> _frontlines = new();
    private bool _frontlinesDirty = true;
    private int _lastActiveWarCount = -1;
    private PlanArrow _activePlan;
    private bool _drawingPlan;
    private Vector2 _planPressScreen;
    private string _planSourceProvinceId = "";
    private readonly List<Vector2> _planPoints = new();
    private const float PlanMinDragPixels = 12f;

    private Country _selected;
    private Country _hovered;
    internal Camera2D _camera;
    private string _hoverProvinceId = "";
    private readonly HashSet<string> _cultureMismatchProvinces = new(StringComparer.Ordinal);
    private PanelContainer _cultureTip;
    private Label _cultureTipLabel;
    private bool _cultureTipDirty = true;
    private CanvasLayer _nameLayer;
    private CanvasLayer _cityLayer;
    private bool _dragging;
    private Vector2 _lastMousePos;
    private Vector2 _rmbPressPos;
    private float _goldPulse = 0f;
    private bool _cameraNeedsFit = true;

    // Visual enhancements
    private Texture2D _paperTexture;
    private GradientTexture2D _oceanTexture;
    private GradientTexture2D _vignetteTexture;
    private ImageTexture _noiseTexture;
    private ImageTexture _terrainTexture;
    private bool _terrainBandsVisible = false;
    private float _lastBorderZoom = -1f;
    private const float CountryTerrainAlpha = 0.55f;

    // Smooth camera
    internal bool _zoomAnim;
    internal float _targetZoom = 1f;
    internal Vector2 _targetPos = Vector2.Zero;

    // Era tone + day/night terminator
    private float _eraAlpha;
    private Color _eraColor = Colors.Transparent;
    private float _terminatorX;
    private GradientTexture2D _terminatorTexture;
    private MapEffects _effects;
    private const float TerminatorPeriod = 60f;
    private const float TerminatorBandWidthRatio = 0.35f;
    private bool _terminatorVisible = false;
    private bool _vignetteVisible = false;
    private bool _factoriesComputed;
    private int _maxFactories = 1;

    // Culture highlight
    private string _cultureHighlight = "";

    // LOD state
    private float _currentLodLevel = 0f;
    private bool _lodDirty = true;

    // Performance tracking
    private int _visibleCountries = 0;
    private int _visibleCities = 0;
    private int _visibleRegions = 0;

    public override void _Ready()
    {
        Instance = this;

        // Layer 0: Map (base)
        // Layer 1: Cities
        // Layer 2: Country/Region names
        // Layer 3: Effects (terminator, vignette, era)
        _cityLayer = new CanvasLayer { Layer = 1 };
        AddChild(_cityLayer);

        _nameLayer = new CanvasLayer { Layer = 2 };
        AddChild(_nameLayer);

        // Load textures
        LoadTextures();

        // Build procedural textures if needed
        BuildProceduralTextures();

        _effects = new MapEffects();
        AddChild(_effects);

        SetProcessInput(true);

        // Load map data from JSON files
        LoadMapData();
        BuildCamera();

        // Subscribe to events
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryChanged += OnPlayerCountryChanged;
        if (GameManager.IsAvailable)
            GameManager.Instance.CountryOwnershipChanged += OnCountryOwnershipChanged;

        // Load provinces
        var provinceSource = new ProceduralProvinceSource();
        SetProvinces(provinceSource.LoadProvinces());
        ProvinceOwnershipSystem.OnOwnerChanged += OnProvinceOwnerChanged;

        BuildCultureTip();
        ApplyMapViewState();

        RefreshOwnershipColors();
        QueueRedraw();

        GD.Print($"WorldMapV3: ready — {_countries.Count} countries, {_cities.Count} cities, {_regions.Count} regions loaded");
    }

    private void LoadTextures()
    {
        // Try to load paper texture from assets
        string paperPath = "res://Assets/map_textures/paper/paper_texture.png";
        if (FileAccess.FileExists(paperPath))
        {
            _paperTexture = GD.Load<Texture2D>(paperPath);
            GD.Print("WorldMapV3: Loaded paper texture from assets");
        }
        else
        {
            GD.Print("WorldMapV3: Paper texture not found at " + paperPath + ", will generate procedural");
        }

        // Try to load terrain texture
        string terrainPath = "res://Assets/map_textures/terrain/terrain_texture.png";
        if (FileAccess.FileExists(terrainPath))
        {
            _terrainTexture = GD.Load<Texture2D>(terrainPath) as ImageTexture;
            GD.Print("WorldMapV3: Loaded terrain texture from assets");
        }
    }

    private void BuildProceduralTextures()
    {
        // Build ocean gradient texture
        _oceanTexture = new GradientTexture2D
        {
            Width = 16,
            Height = 512,
            Fill = 0,
            FillFrom = new Vector2(0f, 0f),
            FillTo = new Vector2(0f, 1f),
            Gradient = BuildOceanGradient(),
        };

        // Noise texture for terrain detail
        var noise = new FastNoiseLite
        {
            Seed = 202401,
            Frequency = 0.08f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
        };
        var noiseSize = new Vector2I(512, 512); // Higher resolution for 4K
        var noiseImg = Image.CreateEmpty(noiseSize.X, noiseSize.Y, false, Image.Format.Rgba8);
        for (int y = 0; y < noiseSize.Y; y++)
        {
            for (int x = 0; x < noiseSize.X; x++)
            {
                float n = noise.GetNoise2D(x, y);
                float g = Mathf.Clamp(n * 0.5f + 0.5f, 0f, 1f);
                noiseImg.SetPixel(x, y, new Color(g, g, g, 1f));
            }
        }
        _noiseTexture = ImageTexture.CreateFromImage(noiseImg);
        GD.Print($"WorldMapV3: terrain noise texture ready ({noiseSize.X}x{noiseSize.Y})");

        // If no terrain texture loaded, create a procedural one based on elevation
        if (_terrainTexture == null)
        {
            CreateProceduralTerrainTexture();
        }

        // Terminator gradient
        _terminatorTexture = new GradientTexture2D
        {
            Width = 64,
            Height = 1,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0f, 0f),
            FillTo = new Vector2(1f, 0f),
            Gradient = BuildTerminatorGradient(),
        };

        // Vignette + center glow
        _vignetteTexture = new GradientTexture2D
        {
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 1f),
            Gradient = BuildVignetteGradient(),
        };
    }

    private void CreateProceduralTerrainTexture()
    {
        // Create a terrain color ramp texture (elevation-based coloring)
        var terrainImg = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);

        // HOI4-style terrain colors (from deep ocean to high mountains)
        var terrainColors = new (float elevation, Color color)[]
        {
            (-1.0f, new Color("#082030")),  // Deep ocean
            (-0.5f, new Color("#0E4A5A")),  // Shallow ocean
            (0.0f, new Color("#1A5C2E")),   // Coast/lowland
            (0.1f, new Color("#2E7D32")),   // Plains
            (0.2f, new Color("#3E8E42")),   // Grassland
            (0.3f, new Color("#689F38")),   // Light forest
            (0.4f, new Color("#558B2F")),   // Forest
            (0.5f, new Color("#827717")),   // Hills
            (0.6f, new Color("#A1880F")),   // Dry hills
            (0.7f, new Color("#BF360C")),   // Mountains
            (0.8f, new Color("#D84315")),   // High mountains
            (0.9f, new Color("#E65100")),   // Snow line
            (1.0f, new Color("#FFF8E1")),   // Snow peaks
        };

        for (int x = 0; x < 256; x++)
        {
            float t = x / 255f;
            Color c = Colors.Transparent;

            for (int i = 0; i < terrainColors.Length - 1; i++)
            {
                if (t >= terrainColors[i].elevation && t <= terrainColors[i + 1].elevation)
                {
                    float localT = (t - terrainColors[i].elevation) / (terrainColors[i + 1].elevation - terrainColors[i].elevation);
                    c = terrainColors[i].color.Lerp(terrainColors[i + 1].color, localT);
                    break;
                }
            }

            if (c == Colors.Transparent)
                c = terrainColors[^1].color;

            terrainImg.SetPixel(x, 0, c);
        }

        _terrainTexture = ImageTexture.CreateFromImage(terrainImg);
        GD.Print("WorldMapV3: Procedural terrain texture created");
    }

    private void LoadMapData()
    {
        LoadCountries();
        LoadRegions();
        LoadCities();
        LoadSeaLabels();
        GD.Print($"WorldMapV3: map data loaded — {_countries.Count} countries, {_regions.Count} regions, {_cities.Count} cities");
    }

    private void LoadCountries()
    {
        string path = MapDataPath + "countries.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"WorldMapV3: {path} not found!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var countriesData = JsonSerializer.Deserialize<List<CountryDataJson>>(FileAccess.GetFileAsString(path), options);

            if (countriesData == null) return;

            // First pass: load all countries with basic info
            foreach (var data in countriesData)
            {
                var country = new Country
                {
                    Id = data.iso3,
                    Name = data.name,
                    MapColor = ColorForId(data.iso3),
                };

                // Load LOD geometries from countries.json if available
                if (data.lod != null)
                {
                    if (data.lod.TryGetValue("lod0", out var lod0) && lod0.ValueKind != JsonValueKind.Null)
                    {
                        foreach (var ring in lod0.EnumerateArray())
                        {
                            var vecRing = ConvertRing(ring);
                            if (vecRing != null && vecRing.Length >= 3)
                            {
                                var clipped = ClipRingToMap(vecRing, MapSize);
                                country.Lod0Rings.Add(clipped);
                            }
                        }
                    }
                    if (data.lod.TryGetValue("lod1", out var lod1) && lod1.ValueKind != JsonValueKind.Null)
                    {
                        foreach (var ring in lod1.EnumerateArray())
                        {
                            var vecRing = ConvertRing(ring);
                            if (vecRing != null && vecRing.Length >= 3)
                            {
                                var clipped = ClipRingToMap(vecRing, MapSize);
                                country.Lod1Rings.Add(clipped);
                            }
                        }
                    }
                    if (data.lod.TryGetValue("lod2", out var lod2) && lod2.ValueKind != JsonValueKind.Null)
                    {
                        foreach (var ring in lod2.EnumerateArray())
                        {
                            var vecRing = ConvertRing(ring);
                            if (vecRing != null && vecRing.Length >= 3)
                            {
                                var clipped = ClipRingToMap(vecRing, MapSize);
                                country.Lod2Rings.Add(clipped);
                            }
                        }
                    }
                }

                // Always add country to list - geometry will be loaded from country_borders.json if needed
                CreateCountryVisuals(country);
                SetupNameLabel(country);
                _countries.Add(country);
            }

            // Second pass: load high-detail borders from country_borders.json for all countries
            LoadCountryBorders();

            GD.Print($"WorldMapV3: loaded {_countries.Count} countries from {path}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV3: failed to load countries: {e.Message}");
        }
    }

    private void LoadCountryBorders()
    {
        string path = MapDataPath + "country_borders.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"WorldMapV3: {path} not found!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var bordersData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FileAccess.GetFileAsString(path), options);

            if (bordersData == null) return;

            int loaded = 0;
            foreach (var kvp in bordersData)
            {
                var country = _countries.FirstOrDefault(c => c.Id == kvp.Key);
                if (country == null) continue;

                // Use borders as LOD0 (world view) and LOD2 (local view)
                // If country already has LOD data from countries.json, use borders as additional detail
                if (kvp.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var polygon in kvp.Value.EnumerateArray())
                    {
                        var vecRing = ConvertRing(polygon);
                        if (vecRing != null && vecRing.Length >= 3)
                        {
                            var clipped = ClipRingToMap(vecRing, MapSize);
                            // Add to LOD0 if empty, otherwise to LOD2
                            if (country.Lod0Rings.Count == 0)
                                country.Lod0Rings.Add(clipped);
                            else
                                country.Lod2Rings.Add(clipped);
                            loaded++;
                        }
                    }
                }
            }

            // Update visuals for all countries with their loaded rings
            foreach (var country in _countries)
            {
                // Clear existing visuals
                foreach (var node in country.Nodes)
                    if (GodotObject.IsInstanceValid(node)) node.QueueFree();
                foreach (var outline in country.Outlines)
                    if (GodotObject.IsInstanceValid(outline)) outline.QueueFree();
                foreach (var glow in country.GlowOutlines)
                    if (GodotObject.IsInstanceValid(glow)) glow.QueueFree();

                country.Nodes.Clear();
                country.Outlines.Clear();
                country.GlowOutlines.Clear();

                // Recreate with loaded rings
                CreateCountryVisuals(country);
            }

            GD.Print($"WorldMapV3: loaded {loaded} border rings from {path} for {_countries.Count} countries");
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV3: failed to load country borders: {e.Message}");
        }
    }

    private void LoadRegions()
    {
        string path = MapDataPath + "regions.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"WorldMapV3: {path} not found!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var regionsData = JsonSerializer.Deserialize<List<RegionDataJson>>(FileAccess.GetFileAsString(path), options);

            if (regionsData == null) return;

            foreach (var data in regionsData)
            {
                var region = new RegionData
                {
                    Id = data.id,
                    CountryId = data.country_iso3,
                    Name = data.name,
                };

                // Load geometry from country_borders or regions_by_country
                // For now, skip region rendering - can be added later
            }

            GD.Print($"WorldMapV3: loaded {_regions.Count} regions from {path}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV3: failed to load regions: {e.Message}");
        }
    }

    private void LoadCities()
    {
        string path = MapDataPath + "cities.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"WorldMapV3: {path} not found!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var citiesData = JsonSerializer.Deserialize<List<CityDataJson>>(FileAccess.GetFileAsString(path), options);

            if (citiesData == null) return;

            foreach (var data in citiesData)
            {
                if (data.latitude < -85 || data.latitude > 85) continue; // Skip polar cities

                var city = new CityData
                {
                    Id = data.id,
                    Name = data.name,
                    NameAscii = data.name_ascii,
                    CountryId = data.country_iso3,
                    Population = data.population_max,
                    PopRank = data.pop_rank,
                    IsCapital = data.is_capital || data.is_admin0_capital,
                    Position = Project(data.longitude, data.latitude),
                    MinZoom = data.min_zoom > 0 ? data.min_zoom : LOD_CITY_MIN_ZOOM,
                };

                // Create city marker sprite
                CreateCityMarker(city);
                _cities.Add(city);
            }

            GD.Print($"WorldMapV3: loaded {_cities.Count} cities from {path}");
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV3: failed to load cities: {e.Message}");
        }
    }

    private void CreateCityMarker(CityData city)
    {
        // Create a simple circle marker for cities
        // In production, you'd use a texture
        city.MarkerSprite = new Sprite2D
        {
            Visible = false,
            Scale = new Vector2(0.1f, 0.1f),
            Position = city.Position,
            Modulate = city.IsCapital ? new Color(1f, 0.85f, 0.1f, 0.9f) : new Color(1f, 1f, 1f, 0.7f),
        };

        // Create a procedural circle texture for the marker
        var markerImg = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        var center = new Vector2(16, 16);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                float dist = new Vector2(x, y).DistanceTo(center);
                if (dist < 12f)
                {
                    float alpha = 1f - (dist / 12f);
                    alpha = Mathf.Clamp(alpha, 0f, 1f);
                    markerImg.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            }
        }
        var markerTex = ImageTexture.CreateFromImage(markerImg);
        city.MarkerSprite.Texture = markerTex;

        _cityLayer.AddChild(city.MarkerSprite);

        // Create city name label
        city.NameLabel = new Label
        {
            Text = city.Name,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        city.NameLabel.AddThemeFontSizeOverride("font_size", 10);
        city.NameLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.9f));
        city.NameLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
        city.NameLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        city.NameLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        _cityLayer.AddChild(city.NameLabel);
    }

    private void LoadSeaLabels()
    {
        string path = "res://Data/sea_labels.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"WorldMapV3: {path} not found — sea labels disabled");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var labels = JsonSerializer.Deserialize<List<SeaLabelJson>>(FileAccess.GetFileAsString(path), options);
            if (labels == null) return;

            int oceanCount = 0, seaCount = 0;
            foreach (var d in labels)
            {
                Vector2 anchor = Project(d.Lon, d.Lat);
                float minZoom = d.MinZoom > 0 ? d.MinZoom : 0f;
                float maxZoom = d.MaxZoom > 0 ? d.MaxZoom : 999f;

                AddMapLabel(
                    d.Type == "ocean" ? _oceanLabels : _seaLabels,
                    d.Name,
                    anchor,
                    minZoom,
                    maxZoom,
                    d.Type == "ocean"
                );

                if (d.Type == "ocean") oceanCount++; else seaCount++;
            }

            GD.Print($"WorldMapV3: {oceanCount} ocean + {seaCount} sea labels loaded");
        }
        catch (Exception e)
        {
            GD.PrintErr($"WorldMapV3: failed to load sea labels: {e.Message}");
        }
    }

    private void AddMapLabel(List<MapLabel> list, string text, Vector2 anchor, float minZoom, float maxZoom, bool isOcean)
    {
        string spacing = isOcean ? " " : " ";
        text = string.Join(spacing, text.ToCharArray());

        var label = new Label { Text = text, Visible = false };
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.AddThemeFontSizeOverride("font_size", isOcean ? 18 : 14);
        label.AddThemeColorOverride("font_color", new Color(0.65f, 0.82f, 1f, isOcean ? 0.85f : 0.75f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0.1f, 0.2f, 0.85f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);

        _nameLayer.AddChild(label);
        list.Add(new MapLabel { Node = label, Anchor = anchor, ShowZoomMin = minZoom, ShowZoomMax = maxZoom });
    }

    public override void _Draw()
    {
        var camSize = _camera != null
            ? GetViewportRect().Size / _camera.Zoom
            : GetViewportRect().Size + MapSize;
        var camPos = _camera != null ? _camera.Position : MapSize / 2f;
        float halfW = Mathf.Max(camSize.X, MapSize.X) * 0.5f + 2000f;
        float halfH = Mathf.Max(camSize.Y, MapSize.Y) * 0.5f + 2000f;
        var big = new Rect2(camPos.X - halfW, camPos.Y - halfH, halfW * 2f, halfH * 2f);

        // 1. Ocean background with bathymetry gradient
        if (_oceanTexture != null)
            DrawTextureRect(_oceanTexture, big, false);
        else
            DrawRect(big, OceanColor);

        // 2. Paper texture overlay (HOI4 style)
        if (EnablePaperTexture && _paperTexture != null)
        {
            DrawTextureRect(_paperTexture, big, true); // Tile the paper texture
        }

        // 3. Map border
        DrawRect(new Rect2(Vector2.Zero, MapSize), new Color(0.12f, 0.25f, 0.45f), false, 2f);

        // 4. Armies
        DrawArmies();

        // 5. Selection box and highlights
        DrawSelection();

        // 6. Province borders
        DrawProvinceBorders();

        // 7. Movement paths
        DrawMovementPaths();

        // 8. Frontlines and plans
        EnsureFrontlines();
        DrawFrontlines();
        DrawPlan();

        // 9. Culture mismatch contours
        DrawCultureMismatchContours();
    }

    private void DrawArmies()
    {
        if (!ArmySystem.IsInitialized) return;
        foreach (var army in ArmySystem.Armies)
        {
            if (string.IsNullOrEmpty(army.ProvinceId)) continue;

            Vector2 drawPos = Vector2.Zero;
            bool havePos = false;

            if (army.IsMoving && army.MovementPathIndex < army.WaypointIds.Count)
            {
                var start = _provinceById.GetValueOrDefault(army.ProvinceId);
                var next = _provinceById.GetValueOrDefault(army.WaypointIds[army.MovementPathIndex]);
                if (start != null && next != null)
                {
                    float t = 1f - army.DaysRemainingInHop / army.DaysPerProvince;
                    t = Mathf.Clamp(t, 0f, 1f);
                    drawPos = start.Centroid.Lerp(next.Centroid, t);
                    havePos = true;
                }
            }
            else
            {
                var prov = _provinceById.GetValueOrDefault(army.ProvinceId);
                if (prov != null) { drawPos = prov.Centroid; havePos = true; }
            }

            if (!havePos) continue;

            Color armyColor;
            float armyRadius;
            switch (army.Domain)
            {
                case ArmySystem.UnitDomain.Naval:
                    armyColor = new Color(0.2f, 0.6f, 1f, 0.85f);
                    armyRadius = 7f;
                    break;
                case ArmySystem.UnitDomain.Air:
                    armyColor = new Color(1f, 0.9f, 0.2f, 0.85f);
                    armyRadius = 5f;
                    break;
                default:
                    armyColor = new Color(1f, 0.2f, 0.2f, 0.8f);
                    armyRadius = 6f;
                    break;
            }
            DrawCircle(drawPos, armyRadius, armyColor);

            Vector2 dir = Vector2.Up;
            if (army.IsMoving && army.MovementPathIndex < army.WaypointIds.Count)
            {
                var start = _provinceById.GetValueOrDefault(army.ProvinceId);
                var next = _provinceById.GetValueOrDefault(army.WaypointIds[army.MovementPathIndex]);
                if (start != null && next != null)
                    dir = (next.Centroid - start.Centroid).Normalized();
            }
            float iconR = armyRadius * 0.55f;
            var tip = drawPos + dir * iconR;
            var left = drawPos - dir * iconR * 0.5f + new Vector2(-dir.Y, dir.X) * iconR * 0.6f;
            var right = drawPos - dir * iconR * 0.5f - new Vector2(-dir.Y, dir.X) * iconR * 0.6f;
            DrawPolygon(new[] { tip, left, right }, new[] { armyColor, armyColor, armyColor });
        }
    }

    private void DrawSelection()
    {
        if (_selecting)
        {
            var rect = Rect2FromPoints(_selectStart, _selectEnd);
            DrawRect(rect, new Color(1f, 1f, 1f, 0.10f), true);
            DrawRect(rect, new Color(1f, 1f, 1f, 0.80f), false, 1.5f);
        }

        foreach (var pid in _selectedProvinceIds)
        {
            if (!_provinceBounds.TryGetValue(pid, out var box)) continue;
            DrawRect(box, new Color(1f, 0.8f, 0.1f, 0.10f), true);
            DrawRect(box, new Color(1f, 0.85f, 0.2f, 0.90f), false, 1.5f);
        }
    }

    private void DrawProvinceBorders()
    {
        if (_provinceNodes == null) return;
        if (_camera != null && _camera.Zoom.X < 0.5f) return;

        foreach (var kvp in _provinceNodes)
        {
            foreach (var node in kvp.Value)
            {
                if (!GodotObject.IsInstanceValid(node)) continue;
                var polygon = node.Polygon;
                if (polygon.Length < 2) continue;
                for (int i = 0; i < polygon.Length; i++)
                {
                    var p1 = polygon[i];
                    var p2 = polygon[(i + 1) % polygon.Length];
                    DrawLine(p1, p2, new Color(0.25f, 0.25f, 0.25f, 0.35f), 0.75f);
                }
            }
        }
    }

    private void DrawMovementPaths()
    {
        if (!ArmySystem.IsInitialized) return;
        foreach (var army in ArmySystem.Armies)
        {
            if (string.IsNullOrEmpty(army.TargetProvinceId) || army.WaypointIds.Count == 0) continue;
            var pts = new List<Vector2>();
            var start = _provinceById.GetValueOrDefault(army.ProvinceId);
            if (start != null) pts.Add(start.Centroid);
            foreach (var pid in army.WaypointIds)
                if (_provinceById.TryGetValue(pid, out var p)) pts.Add(p.Centroid);
            if (pts.Count < 2) continue;
            for (int i = 1; i < pts.Count; i++)
            {
                DrawLine(pts[i - 1], pts[i], new Color(0.2f, 0.7f, 1f, 0.85f), 2f);
                var seg = (pts[i] - pts[i - 1]).Normalized();
                if (seg != Vector2.Zero)
                {
                    var tip = pts[i];
                    var back = pts[i] - seg * 7f;
                    var perp = new Vector2(-seg.Y, seg.X) * 3f;
                    DrawPolygon(new[] { tip, back + perp, back - perp }, new[] { new Color(0.2f, 0.7f, 1f, 0.85f), new Color(0.2f, 0.7f, 1f, 0.85f), new Color(0.2f, 0.7f, 1f, 0.85f) });
                }
            }
            for (int i = 0; i < pts.Count; i++)
            {
                bool isLast = i == pts.Count - 1;
                DrawCircle(pts[i], 5f, isLast ? new Color(0.2f, 0.9f, 0.4f, 0.9f) : new Color(1f, 0.9f, 0.2f, 0.9f));
            }
        }
    }

    private void EnsureFrontlines()
    {
        if (!_frontlinesDirty) return;
        RebuildFrontlines();
    }

    private void RebuildFrontlines()
    {
        _frontlines.Clear();
        if (_provinces.Count == 0) { _frontlinesDirty = false; return; }

        var visited = new HashSet<string>();
        foreach (var prov in _provinces)
        {
            string ownerA = ProvinceOwnershipSystem.GetProvinceOwner(prov.Id);
            if (string.IsNullOrEmpty(ownerA) || ownerA == "?") continue;

            foreach (var nbId in prov.NeighborIds)
            {
                string pairKey = prov.Id.CompareTo(nbId) < 0
                    ? $"{prov.Id}|{nbId}"
                    : $"{nbId}|{prov.Id}";
                if (!visited.Add(pairKey)) continue;

                if (!_provinceById.TryGetValue(nbId, out var nb)) continue;
                string ownerB = ProvinceOwnershipSystem.GetProvinceOwner(nb.Id);
                if (string.IsNullOrEmpty(ownerB) || ownerB == "?" || ownerA == ownerB) continue;

                if (!WarSystem.IsAtWarWith(ownerA, ownerB)) continue;

                _frontlines.Add(new FrontlineSegment
                {
                    A = prov.Centroid,
                    B = nb.Centroid,
                });
            }
        }

        _frontlinesDirty = false;
        GD.Print($"[WorldMapV3] Frontlines rebuilt — segments={_frontlines.Count}");
    }

    private void DrawFrontlines()
    {
        foreach (var seg in _frontlines)
        {
            DrawLine(seg.A, seg.B, seg.Color, 3f);
            DrawCircle(seg.A, 2.5f, seg.Color);
            DrawCircle(seg.B, 2.5f, seg.Color);
        }
    }

    private void DrawPlan()
    {
        var plan = _activePlan;
        List<Vector2> pts = null;
        Color color = new Color(0.95f, 0.75f, 0.2f, 0.9f);

        if (_drawingPlan && _planPoints.Count > 0)
        {
            pts = _planPoints;
        }
        else if (plan != null && plan.Points.Count > 0)
        {
            pts = plan.Points;
            color = plan.Color;
        }

        if (pts == null || pts.Count < 2) return;

        for (int i = 1; i < pts.Count; i++)
            DrawLine(pts[i - 1], pts[i], color, 2.5f);

        for (int i = 0; i < pts.Count; i++)
            DrawCircle(pts[i], 4f, i == pts.Count - 1 ? color : new Color(color.R, color.G, color.B, 0.6f));

        var last = pts[^1];
        var prev = pts[^2];
        var dir = (last - prev).Normalized();
        var left = new Vector2(-dir.Y, dir.X);
        DrawLine(last, last - dir * 14f + left * 7f, color, 2.5f);
        DrawLine(last, last - dir * 14f - left * 7f, color, 2.5f);
    }

    private void DrawCultureMismatchContours()
    {
        if (CurrentMapMode != MapMode.Culture) return;
        if (_cultureMismatchProvinces.Count == 0) return;
        if (_camera != null && _camera.Zoom.X < 0.5f) return;

        foreach (var pid in _cultureMismatchProvinces)
        {
            if (!_provinceNodes.TryGetValue(pid, out var nodes)) continue;
            foreach (var node in nodes)
            {
                if (!GodotObject.IsInstanceValid(node)) continue;
                var polygon = node.Polygon;
                if (polygon.Length < 2) continue;
                for (int i = 0; i < polygon.Length; i++)
                {
                    var p1 = polygon[i];
                    var p2 = polygon[(i + 1) % polygon.Length];
                    DrawLine(p1, p2, new Color(0f, 0f, 0f, 0.55f), 3f);
                    DrawLine(p1, p2, new Color(1f, 0.45f, 0.12f, 0.95f), 1.25f);
                }
            }
        }
    }

    // ===== Core logic =====

    public override void _ExitTree()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryChanged -= OnPlayerCountryChanged;
        if (GameManager.IsAvailable)
            GameManager.Instance.CountryOwnershipChanged -= OnCountryOwnershipChanged;
        ProvinceOwnershipSystem.OnOwnerChanged -= OnProvinceOwnerChanged;
        if (Instance == this) Instance = null;
    }

    private void OnPlayerCountryChanged(string id)
    {
        _lastBorderZoom = -1f;
        RefreshOwnershipColors();
    }

    private void OnCountryOwnershipChanged()
    {
        _lastBorderZoom = -1f;
        RefreshOwnershipColors();
        GD.Print("WorldMapV3: ownership colors updated");
    }

    private static bool IsPlayerCountry(Country c)
    {
        return GameManager.IsAvailable &&
               !string.IsNullOrEmpty(GameManager.Instance.PlayerCountryId) &&
               c.Id == GameManager.Instance.PlayerCountryId;
    }

    public override void _Process(double delta)
    {
        float fDelta = (float)delta;
        HandleKeyboardPan(fDelta);
        UpdateNameLabels();
        UpdateCityLabels();
        UpdateLOD(fDelta);
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
            float processMs = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess);
            int drawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            int prims = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            bool provincesOn = _provinceNodes != null && _provinceNodes.Count > 0;
            bool bordersVisible = _camera == null || _camera.Zoom.X >= 0.5f;
            GD.Print($"[PERF] WorldMapV3 FPS={fps:F1} process={processMs:F2}ms drawcalls={drawCalls} prims={prims} zoom={zoom:F2} countries={_visibleCountries} cities={_visibleCities} regions={_visibleRegions} provinces={_provinces.Count} (fill={provincesOn}, borders={bordersVisible})");
        }

        int activeWarCount = WarSystem.ActiveWars.Count;
        if (activeWarCount != _lastActiveWarCount)
        {
            _lastActiveWarCount = activeWarCount;
            _frontlinesDirty = true;
        }

        if (GameManager.IsAvailable)
        {
            int year = GameManager.Instance.CurrentDate.Year;
            float targetAlpha = year <= 1938 ? 0f : (year <= 1941 ? 0.04f : 0.07f);
            Color targetColor = year <= 1938 ? Colors.Transparent
                                : (year <= 1941 ? new Color(1f, 0.4f, 0.2f, 1f)
                                               : new Color(0.6f, 0.2f, 0.15f, 1f));
            float lerpSpeed = Mathf.Clamp(fDelta * 2f, 0f, 1f);
            _eraAlpha = Mathf.Lerp(_eraAlpha, targetAlpha, lerpSpeed);
            _eraColor = _eraColor.Lerp(targetColor, lerpSpeed);
        }

        float t = (Time.GetTicksMsec() / 1000f / TerminatorPeriod) % 1f;
        _terminatorX = t * MapSize.X;

        _effects?.QueueRedraw();

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

    private void UpdateLOD(float delta)
    {
        if (_camera == null) return;

        float zoom = _camera.Zoom.X;

        // Determine current LOD level
        float newLodLevel = 0f; // 0 = country, 1 = region, 2 = local
        if (zoom > LOD_REGION_MAX_ZOOM) newLodLevel = 2f;
        else if (zoom > LOD_COUNTRY_MAX_ZOOM) newLodLevel = 1f;

        if (Mathf.Abs(newLodLevel - _currentLodLevel) > 0.01f)
        {
            _currentLodLevel = newLodLevel;
            _lodDirty = true;
        }

        if (!_lodDirty) return;
        _lodDirty = false;

        // Update country visuals based on LOD
        UpdateCountryLOD(zoom);

        // Update city visibility
        UpdateCityVisibility(zoom);

        // Update region visibility (if implemented)
        UpdateRegionVisibility(zoom);
    }

    private void UpdateCountryLOD(float zoom)
    {
        _visibleCountries = 0;

        foreach (var c in _countries)
        {
            // Determine which LOD rings to use
            List<Vector2[]> ringsToUse;
            if (zoom >= LOD_REGION_MAX_ZOOM && c.Lod2Rings.Count > 0)
                ringsToUse = c.Lod2Rings;
            else if (zoom >= LOD_COUNTRY_MAX_ZOOM && c.Lod1Rings.Count > 0)
                ringsToUse = c.Lod1Rings;
            else
                ringsToUse = c.Lod0Rings.Count > 0 ? c.Lod0Rings : c.Lod1Rings;

            // Update polygon nodes
            for (int i = 0; i < c.Nodes.Count && i < ringsToUse.Count; i++)
            {
                if (GodotObject.IsInstanceValid(c.Nodes[i]))
                {
                    c.Nodes[i].Polygon = ringsToUse[i];
                }
            }

            // Update outlines
            for (int i = 0; i < c.Outlines.Count && i < ringsToUse.Count; i++)
            {
                if (GodotObject.IsInstanceValid(c.Outlines[i]))
                {
                    c.Outlines[i].Points = ringsToUse[i];
                }
            }

            // Update glow outlines
            for (int i = 0; i < c.GlowOutlines.Count && i < ringsToUse.Count; i++)
            {
                if (GodotObject.IsInstanceValid(c.GlowOutlines[i]))
                {
                    c.GlowOutlines[i].Points = ringsToUse[i];
                }
            }

            // Check if visible on screen
            if (IsCountryOnScreen(c, zoom))
                _visibleCountries++;
        }
    }

    private bool IsCountryOnScreen(Country c, float zoom)
    {
        if (_camera == null) return true;
        var vp = GetViewportRect().Size;
        Vector2 screen = (c.LabelAnchor - _camera.Position) * zoom + vp / 2f;
        return screen.X > -100f && screen.X < vp.X + 100f && screen.Y > -100f && screen.Y < vp.Y + 100f;
    }

    private void UpdateCityVisibility(float zoom)
    {
        _visibleCities = 0;

        foreach (var city in _cities)
        {
            bool shouldShowMarker = zoom >= city.MinZoom && zoom >= LOD_CITY_MIN_ZOOM;
            bool shouldShowName = zoom >= LOD_CITY_NAME_MIN_ZOOM;

            bool onScreen = true;
            if (_camera != null)
            {
                var vp = GetViewportRect().Size;
                Vector2 screen = (city.Position - _camera.Position) * zoom + vp / 2f;
                onScreen = screen.X > -50f && screen.X < vp.X + 50f && screen.Y > -50f && screen.Y < vp.Y + 50f;
            }

            if (city.MarkerSprite != null && GodotObject.IsInstanceValid(city.MarkerSprite))
            {
                city.MarkerSprite.Visible = shouldShowMarker && onScreen;
                if (shouldShowMarker && onScreen)
                {
                    city.MarkerSprite.Position = city.Position;
                    // Scale marker with zoom
                    float scale = Mathf.Clamp(0.1f * zoom, 0.05f, 0.3f);
                    city.MarkerSprite.Scale = new Vector2(scale, scale);
                }
            }

            if (city.NameLabel != null && GodotObject.IsInstanceValid(city.NameLabel))
            {
                city.NameLabel.Visible = shouldShowName && onScreen;
                if (shouldShowName && onScreen)
                {
                    var vp = GetViewportRect().Size;
                    Vector2 screen = (city.Position - _camera.Position) * zoom + vp / 2f;
                    city.NameLabel.Position = screen - city.NameLabel.Size / 2f + new Vector2(0, -16);
                }
            }

            if (shouldShowMarker && onScreen) _visibleCities++;
        }
    }

    private void UpdateRegionVisibility(float zoom)
    {
        // Region rendering can be implemented here
        // For now, just count
        _visibleRegions = 0;
    }

    private void UpdateCityLabels()
    {
        // Handled in UpdateCityVisibility
    }

    private void AnimateZoom(float delta)
    {
        if (!_zoomAnim || _camera == null) return;
        float t = Mathf.Clamp(delta * 14f, 0f, 1f);
        var target = ClampedTargetPos();
        _camera.Zoom = _camera.Zoom.Lerp(new Vector2(_targetZoom, _targetZoom), t);
        _camera.Position = _camera.Position.Lerp(target, t);
        if (Mathf.Abs(_camera.Zoom.X - _targetZoom) < 0.004f &&
            _camera.Position.DistanceTo(target) < 1.5f / _targetZoom)
        {
            _camera.Zoom = new Vector2(_targetZoom, _targetZoom);
            _camera.Position = target;
            _zoomAnim = false;
        }
    }

    private Vector2 ClampedTargetPos() => ClampCameraPos(_targetPos);

    private void UpdateBorderWidthsByZoom()
    {
        if (_camera == null) return;
        float zoom = _camera.Zoom.X;
        if (Mathf.Abs(zoom - _lastBorderZoom) < 0.03f) return;
        _lastBorderZoom = zoom;

        float w = Mathf.Clamp(1.1f / zoom, 0.35f, 3f);

        foreach (var c in _countries)
        {
            if (c == _selected) continue;
            Color borderColor = IsPlayerCountry(c) ? PlayerBorderColor : BorderColor;
            foreach (var outline in c.Outlines)
            {
                if (!GodotObject.IsInstanceValid(outline)) continue;
                outline.Width = w;
                outline.DefaultColor = borderColor;
            }
            float glowW = Mathf.Clamp(w * 3.2f, 1f, 10f);
            foreach (var glow in c.GlowOutlines)
            {
                if (!GodotObject.IsInstanceValid(glow)) continue;
                glow.Width = glowW;
            }
        }
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _dragging = mb.Pressed;
                if (mb.Pressed)
                {
                    _lastMousePos = mb.Position;
                    if (_drawingPlan)
                    {
                        _drawingPlan = false;
                        _planPoints.Clear();
                        _planSourceProvinceId = null;
                        QueueRedraw();
                    }
                    _rmbPressPos = mb.Position;
                }
                else
                {
                    _lastMousePos = Vector2.Zero;
                    if (mb.Position.DistanceTo(_rmbPressPos) < 6f)
                    {
                        var worldPos = _camera.Position + (mb.Position - GetViewportRect().Size / 2f) / _camera.Zoom;
                        if (!IssueMoveOrders(worldPos, mb.ShiftPressed))
                            HandleRightClickOrder(mb.ShiftPressed);
                    }
                }
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

        if (e is InputEventMouseMotion motion && _dragging)
        {
            Vector2 deltaPos = motion.Position - _lastMousePos;
            _lastMousePos = motion.Position;
            if (_camera != null && deltaPos != Vector2.Zero)
            {
                _zoomAnim = false;
                _camera.Position -= deltaPos / _camera.Zoom.X;
                _targetPos = _camera.Position;
                ClampCamera();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (!_dragging)
        {
            if (e is InputEventMouseButton lmb && lmb.ButtonIndex == MouseButton.Left)
            {
                var hovered = GetViewport().GuiGetHoveredControl();
                if (HoveredBlocksMap(hovered)) return;

                Vector2 viewportPos = GetViewport().GetMousePosition();
                Vector2 worldPos = _camera.Position + (viewportPos - GetViewportRect().Size / 2f) / _camera.Zoom;

                if (lmb.Pressed)
                {
                    if (Input.IsKeyPressed(Key.Ctrl))
                    {
                        _drawingPlan = true;
                        _planPressScreen = viewportPos;
                        _planPoints.Clear();
                        _planSourceProvinceId = null;
                        var prov = FindProvinceAt(worldPos);
                        if (prov != null)
                        {
                            _planSourceProvinceId = prov.Id;
                            _planPoints.Add(prov.Centroid);
                        }
                        GetViewport().SetInputAsHandled();
                        return;
                    }

                    if (lmb.DoubleClick)
                    {
                        var c = FindCountryAt(worldPos);
                        if (c != null && GameManager.IsAvailable)
                        {
                            GameManager.Instance.SetPlayerCountry(c.Id);
                            GD.Print($"WorldMapV3: Double-click → SetPlayerCountry({c.Id})");
                        }
                        GetViewport().SetInputAsHandled();
                        return;
                    }

                    _lmbPressed = true;
                    _lmbPressScreen = viewportPos;
                    _selectStart = worldPos;
                    _selectEnd = worldPos;
                    _selecting = false;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (_drawingPlan)
                {
                    _drawingPlan = false;
                    if (viewportPos.DistanceTo(_planPressScreen) > PlanMinDragPixels &&
                        !string.IsNullOrEmpty(_planSourceProvinceId))
                    {
                        var endProv = FindProvinceAt(worldPos);
                        if (endProv != null && endProv.Id != _planSourceProvinceId)
                            FinalizePlan(_planSourceProvinceId, endProv.Id);
                    }
                    _planSourceProvinceId = null;
                    _planPoints.Clear();
                    QueueRedraw();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (_lmbPressed)
                {
                    _lmbPressed = false;
                    _selecting = false;
                    bool shift = lmb.ShiftPressed;

                    if (viewportPos.DistanceTo(_lmbPressScreen) < SelectDragThreshold)
                    {
                        var prov = FindProvinceAt(worldPos);
                        if (prov != null)
                        {
                            if (shift) _selectedProvinceIds.Add(prov.Id);
                            else { _selectedProvinceIds.Clear(); _selectedProvinceIds.Add(prov.Id); }
                        }
                        else if (!shift)
                        {
                            _selectedProvinceIds.Clear();
                        }

                        SelectAt(worldPos);
                    }
                    else
                    {
                        if (!shift) _selectedProvinceIds.Clear();
                        var rect = Rect2FromPoints(_selectStart, _selectEnd);
                        foreach (var prov in _provinces)
                            if (rect.HasPoint(prov.Centroid)) _selectedProvinceIds.Add(prov.Id);
                    }

                    EmitSignal(SignalName.ProvinceSelectionChanged, ProvinceIdsToArray());
                    QueueRedraw();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            if (e is InputEventMouseMotion mm)
            {
                Vector2 viewportPos = GetViewport().GetMousePosition();
                Vector2 worldPos = _camera.Position + (viewportPos - GetViewportRect().Size / 2f) / _camera.Zoom;
                UpdateHover(worldPos);

                if (_lmbPressed && _camera != null)
                {
                    _selectEnd = worldPos;
                    if (viewportPos.DistanceTo(_lmbPressScreen) > SelectDragThreshold) _selecting = true;
                }

                if (_drawingPlan && _camera != null)
                {
                    if (_planPoints.Count == 0 || _planPoints[_planPoints.Count - 1].DistanceTo(worldPos) > 8f)
                        _planPoints.Add(worldPos);
                    QueueRedraw();
                }
            }
        }
    }

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

    private void HandleRightClickOrder(bool shiftPressed)
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer) return;
        if (HoveredBlocksMap(GetViewport().GuiGetHoveredControl())) return;
        if (_camera == null) return;

        Vector2 viewportPos = GetViewport().GetMousePosition();
        Vector2 worldPos = _camera.Position + (viewportPos - GetViewportRect().Size / 2f) / _camera.Zoom;
        var country = FindCountryAt(worldPos);
        if (country == null) return;

        string playerId = GameManager.Instance.PlayerCountryId;
        if (country.Id == playerId) return;

        var war = WarSystem.GetWarWith(country.Id);
        if (war == null)
        {
            GD.Print($"[MapV3] {country.Name}: not at war — need DECLARE WAR ({WarSystem.DeclareWarPPCost} PP)");
            return;
        }

        if (shiftPressed)
        {
            WarSystem.ToggleFrontline(country.Id);
        }
        else
        {
            bool offensiveNow = !war.OffensiveActive;
            WarSystem.SetOffensive(country.Id, offensiveNow);
        }
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
        _zoomAnim = false;
        _camera.Position += dir.Normalized() * (KeyPanSpeed / _camera.Zoom.X) * delta;
        _targetPos = _camera.Position;
        ClampCamera();
    }

    private void ClampCamera()
    {
        _camera.Position = ClampCameraPos(_camera.Position);
    }

    public void SetMapMode(MapMode mode)
    {
        if (CurrentMapMode == mode) return;
        CurrentMapMode = mode;
        // Convert to WorldMap.MapMode for MapViewSystem
        MapViewSystem.SetMode((WorldMap.MapMode)(int)mode);
        _factoriesComputed = false;
        RefreshOwnershipColors();
        RefreshProvinceColors();
        QueueRedraw();
        GD.Print($"WorldMapV3: map mode = {mode}");
    }

    private Color ModeColor(Country c)
    {
        if (c == null) return Colors.Transparent;
        var data = CountryDatabase.Get(c.Id);

        switch (CurrentMapMode)
        {
            case MapMode.Political:
                return c.MapColor;

            case MapMode.Terrain:
                return Colors.Transparent;

            case MapMode.Factories:
            {
                int factories = data?.TotalFactories ?? 0;
                EnsureMaxFactories();
                float t = _maxFactories > 0 ? Mathf.Clamp((float)factories / (float)_maxFactories, 0f, 1f) : 0f;
                var from = new Color("#333333");
                var to = new Color("#FFD700");
                return new Color(
                    from.R + (to.R - from.R) * t,
                    from.G + (to.G - from.G) * t,
                    from.B + (to.B - from.B) * t,
                    from.A + (to.A - from.A) * t);
            }

            case MapMode.Resources:
            {
                if (data == null) return Colors.Transparent;
                if (data.Oil > 0) return new Color("#8B4513");
                if (data.Steel > 0) return new Color("#708090");
                if (data.Rubber > 0) return new Color("#2E8B57");
                if (data.Aluminum > 0) return new Color("#B0C4DE");
                if (data.Tungsten > 0) return new Color("#4B0082");
                if (data.Chromium > 0) return new Color("#DC143C");
                return Colors.Transparent;
            }

            case MapMode.Supply:
                return new Color(0.15f, 0.45f, 0.85f, 0.45f);

            case MapMode.AirZones:
                return new Color(0.5f, 0.35f, 0.9f, 0.35f);

            case MapMode.Culture:
                return Colors.Transparent;

            default:
                return c.MapColor;
        }
    }

    private Color ModeColorForCountryId(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return Colors.Transparent;
        var country = _countries.FirstOrDefault(c => c.Id == countryId);
        if (country == null) return Colors.Transparent;
        return ModeColor(country);
    }

    private void EnsureMaxFactories()
    {
        if (_factoriesComputed) return;
        _maxFactories = 1;
        foreach (var data in CountryDatabase.All())
        {
            int f = data.TotalFactories;
            if (f > _maxFactories) _maxFactories = f;
        }
        _factoriesComputed = true;
        GD.Print($"WorldMapV3: factories mode normalization — max factories={_maxFactories}");
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

                if (c == _selected) target = SelectedColor;
                else if (c == _hovered) target = c.MapColor.Lightened(0.25f);
                else
                    target = new Color(target.R, target.G, target.B, CountryTerrainAlpha);
            }

            foreach (var node in c.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = target;
            foreach (var outline in c.Outlines)
                if (GodotObject.IsInstanceValid(outline) && c != _selected)
                    outline.DefaultColor = IsPlayerCountry(c) ? PlayerBorderColor : BorderColor;
        }
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
        if (c.LabelAnchor == Vector2.Zero) return;
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
            bool bigEnough = wpx > 55f && hpx > 26f;
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
        _targetZoom = _camera.Zoom.X;
        _targetPos = _camera.Position;
        _zoomAnim = false;
    }

    private void FitCameraToViewport()
    {
        if (_camera == null) return;
        var vp = GetViewportRect().Size;
        if (vp.X <= 0 || vp.Y <= 0) vp = MapSize;
        float fit = Mathf.Min(vp.X / MapSize.X, vp.Y / MapSize.Y) * 0.95f;
        _camera.Zoom = new Vector2(fit, fit);
        _camera.Position = new Vector2(MapSize.X / 2f, MapSize.Y / 2f - 80f);
        _targetZoom = fit;
        _targetPos = _camera.Position;
        _zoomAnim = false;
    }

    public void EnsureCameraCurrent()
    {
        if (_camera == null || !GodotObject.IsInstanceValid(_camera)) return;
        if (!_camera.IsCurrent()) _camera.MakeCurrent();
    }

    private void ZoomAt(Vector2 screenPos, float factor)
    {
        _cameraNeedsFit = false;
        float z = Mathf.Clamp(_targetZoom * factor, 0.25f, 6f);
        if (Mathf.IsEqualApprox(z, _targetZoom) && !_zoomAnim) return;

        var center = GetViewportRect().Size / 2f;
        var before = (screenPos - center) / _targetZoom + _targetPos;
        var after = (screenPos - center) / z + _targetPos;
        _targetZoom = z;
        _targetPos = ClampCameraPos(_camera.Position + (before - after));
        _zoomAnim = true;
    }

    internal Vector2 ClampCameraPos(Vector2 pos)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, -400f, MapSize.X + 400f),
            Mathf.Clamp(pos.Y, -400f, MapSize.Y + 400f));
    }

    internal void UpdateHover(Vector2 worldPos)
    {
        UpdateProvinceHover(worldPos);

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
        {
            var rings = c.Lod0Rings.Count > 0 ? c.Lod0Rings : (c.Lod1Rings.Count > 0 ? c.Lod1Rings : c.Lod2Rings);
            foreach (var ring in rings)
                if (Geometry2D.IsPointInPolygon(worldPos, ring))
                {
                    if (c.SmallestRingArea < bestArea)
                    {
                        best = c;
                        bestArea = c.SmallestRingArea;
                    }
                    break;
                }
        }
        return best;
    }

    internal void SelectAt(Vector2 worldPos) => SetSelected(FindCountryAt(worldPos));

    private void SetSelected(Country c)
    {
        if (_selected == c) return;
        var old = _selected; _selected = c;

        GD.Print($"[Diag] WorldMapV3.SetSelected: {(old?.Id ?? "—")} → {(c?.Id ?? "—")}");

        if (old != null)
        {
            _lastBorderZoom = -1f;
            foreach (var node in old.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = old == _hovered ? old.MapColor.Lightened(0.25f) : old.MapColor;
            if (c == null)
                EmitSignal(SignalName.CountryDeselected);
        }
        if (_selected != null)
        {
            foreach (var node in _selected.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = SelectedColor;
            foreach (var outline in _selected.Outlines) if (GodotObject.IsInstanceValid(outline)) { outline.DefaultColor = GoldBorderColor; outline.Width = 2.5f; }
            EmitSignal(SignalName.CountrySelected, _selected.Id, _selected.Name);
        }
    }

    public void ClearSelection() => SetSelected(null);

    public IReadOnlySet<string> SelectedProvinceIds => _selectedProvinceIds;

    public void SelectProvince(string provinceId, bool add = false)
    {
        if (!add) _selectedProvinceIds.Clear();
        if (!string.IsNullOrEmpty(provinceId)) _selectedProvinceIds.Add(provinceId);
        EmitSignal(SignalName.ProvinceSelectionChanged, ProvinceIdsToArray());
        QueueRedraw();
    }

    public void ClearProvinceSelection()
    {
        if (_selectedProvinceIds.Count == 0) return;
        _selectedProvinceIds.Clear();
        EmitSignal(SignalName.ProvinceSelectionChanged, ProvinceIdsToArray());
        QueueRedraw();
    }

    private string[] ProvinceIdsToArray()
    {
        var ids = new string[_selectedProvinceIds.Count];
        _selectedProvinceIds.CopyTo(ids);
        return ids;
    }

    private static Rect2 Rect2FromPoints(Vector2 a, Vector2 b) =>
        new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y));

    private bool IssueMoveOrders(Vector2 worldPos, bool shift)
    {
        if (_camera == null) return false;
        var target = FindProvinceAt(worldPos);
        if (target == null) return false;

        var armies = ArmySystem.Armies
            .Where(a => !string.IsNullOrEmpty(a.ProvinceId) && _selectedProvinceIds.Contains(a.ProvinceId))
            .ToList();
        if (armies.Count == 0) return false;

        foreach (var army in armies)
        {
            if (!shift)
            {
                army.WaypointIds.Clear();
                army.MovementPathIndex = 0;
            }
            var remaining = army.WaypointIds.Skip(army.MovementPathIndex).ToList();
            remaining.Add(target.Id);
            var route = BuildFullRoute(army.ProvinceId, remaining);
            if (route.Count == 0) continue;

            var fullPath = new List<string> { army.ProvinceId };
            fullPath.AddRange(route);
            ApplyRouteToArmy(army, fullPath);

            army.TargetProvinceId = target.Id;
            army.DaysRemainingInHop = army.DaysPerProvince;
            GD.Print($"[MapV3] {army.Name} → {target.Name} (route={army.WaypointIds.Count} provinces)");
        }

        QueueRedraw();
        return true;
    }

    private List<string> BuildFullRoute(string startId, IReadOnlyList<string> waypoints)
    {
        var route = new List<string>();
        string from = startId;
        foreach (var wp in waypoints)
        {
            var seg = BfsPath(from, wp);
            foreach (var pid in seg.Skip(1)) route.Add(pid);
            from = wp;
        }
        return route;
    }

    private List<string> BfsPath(string startId, string targetId)
    {
        if (startId == targetId) return new List<string> { targetId };
        if (!_provinceById.ContainsKey(startId) || !_provinceById.ContainsKey(targetId))
            return new List<string> { targetId };

        var visited = new HashSet<string> { startId };
        var parent = new Dictionary<string, string>();
        var queue = new Queue<string>();
        queue.Enqueue(startId);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var prov = _provinceById.GetValueOrDefault(cur);
            if (prov == null) continue;
            foreach (var nb in prov.NeighborIds)
            {
                if (visited.Contains(nb)) continue;
                visited.Add(nb);
                parent[nb] = cur;
                if (nb == targetId)
                {
                    var path = new List<string> { targetId };
                    var node = cur;
                    while (node != startId)
                    {
                        path.Add(node);
                        node = parent[node];
                    }
                    path.Add(startId);
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(nb);
            }
        }
        return new List<string> { targetId };
    }

    private void FinalizePlan(string sourceProvinceId, string targetProvinceId)
    {
        var path = BfsPath(sourceProvinceId, targetProvinceId);
        if (path.Count < 2)
        {
            GD.Print($"[MapV3] Plan cancelled: no route {sourceProvinceId} → {targetProvinceId}");
            return;
        }

        _activePlan = new PlanArrow
        {
            SourceProvinceId = sourceProvinceId,
            TargetProvinceId = targetProvinceId,
            Points = new List<Vector2>(_planPoints),
            ProvincePath = new List<string>(path),
            CreatedDay = GameManager.IsAvailable ? (float)GameManager.Instance.TotalDays : 0f
        };

        QueueRedraw();
        GD.Print($"[MapV3] Plan saved: {sourceProvinceId} → {targetProvinceId} ({path.Count} steps)");

        ApplyPlanToSelectedArmies(sourceProvinceId, path);
    }

    private void ApplyPlanToSelectedArmies(string sourceProvinceId, IReadOnlyList<string> provincePath)
    {
        if (!ArmySystem.IsInitialized) return;
        int moved = 0;
        foreach (var army in ArmySystem.Armies)
        {
            if (army.ProvinceId == sourceProvinceId && _selectedProvinceIds.Contains(sourceProvinceId))
            {
                ApplyRouteToArmy(army, provincePath);
                moved++;
            }
        }

        if (moved == 0)
            GD.Print($"[MapV3] Plan: no selected armies in {sourceProvinceId} (route saved)");
    }

    private void ApplyRouteToArmy(ArmySystem.Army army, IReadOnlyList<string> fullPath)
    {
        army.WaypointIds.Clear();
        for (int i = 1; i < fullPath.Count; i++)
            army.WaypointIds.Add(fullPath[i]);
        army.MovementPathIndex = 0;
        army.TargetProvinceId = fullPath[^1];
        army.DaysRemainingInHop = army.DaysPerProvince;
    }

    public static void IssueArmyMoveOrder(ArmySystem.Army army, string targetProvinceId)
    {
        if (Instance == null || army == null) return;
        if (string.IsNullOrEmpty(army.ProvinceId)) return;
        if (string.IsNullOrEmpty(targetProvinceId) || army.ProvinceId == targetProvinceId) return;

        var route = Instance.BuildFullRoute(army.ProvinceId, new List<string> { targetProvinceId });
        if (route.Count == 0) return;

        var fullPath = new List<string> { army.ProvinceId };
        fullPath.AddRange(route);
        Instance.ApplyRouteToArmy(army, fullPath);

        GD.Print($"[AI Move V3] {army.Name} → {targetProvinceId} (route={army.WaypointIds.Count})");
    }

    private void AnimateSelectedBorder(float delta)
    {
        if (_selected == null || _camera == null) return;
        float zoom = _camera.Zoom.X;
        _goldPulse += delta * 2f; float pulse = 0.5f + 0.5f * Mathf.Sin(_goldPulse);
        float selW = Mathf.Clamp(2.2f / zoom, 0.8f, 6f);
        foreach (var outline in _selected.Outlines)
        {
            if (!GodotObject.IsInstanceValid(outline)) continue;
            outline.Width = selW * (0.85f + pulse * 0.3f);
            outline.DefaultColor = GoldBorderColor.Lightened(pulse * 0.2f);
        }
    }

    private static Gradient BuildOceanGradient()
    {
        var g = new Gradient();
        g.SetColor(0, new Color("#082030"));
        g.AddPoint(0.2f, new Color("#0A2B3A"));
        g.AddPoint(0.45f, new Color("#0E4A5A"));
        g.AddPoint(0.6f, new Color("#115E6E"));
        g.AddPoint(0.75f, new Color("#0C3A4A"));
        g.SetColor(1, new Color("#082030"));
        return g;
    }

    private static Gradient BuildTerminatorGradient()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(0f, 0f, 0f, 0f));
        g.AddPoint(0.2f, new Color(0f, 0f, 0f, 0.03f));
        g.AddPoint(0.5f, new Color(0f, 0f, 0f, 0.12f));
        g.AddPoint(0.8f, new Color(0f, 0f, 0f, 0.03f));
        g.SetColor(1, new Color(0f, 0f, 0f, 0f));
        return g;
    }

    private static Gradient BuildVignetteGradient()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(0.12f, 0.40f, 0.50f, 0.08f));
        g.AddPoint(0.5f, new Color(0f, 0f, 0f, 0f));
        g.SetColor(1, new Color(0f, 0f, 0f, 0.30f));
        return g;
    }

    // ===== Province System =====

    public void SetProvinces(IReadOnlyList<ProvinceData> provinces)
    {
        _provinces = new List<ProvinceData>(provinces);
        ProvinceOwnershipSystem.InitializeFromProvinces(_provinces);
        OccupationSystem.SetWorld(_provinces);
        CivilWarSystem.SetWorld(_provinces);

        if (ProvinceCultureSystem.RecordCount == 0)
        {
            ProvinceCultureSystem.GenerateFor(_provinces, ScenarioSystem.ActiveId ?? "historical_1936");
            GD.Print($"WorldMapV3: {ProvinceCultureSystem.RecordCount} province culture records generated.");
        }

        if (ProvinceEconomySystem.RecordCount == 0)
        {
            ProvinceEconomySystem.GenerateFor(_provinces, ScenarioSystem.ActiveId ?? "historical_1936");
            GD.Print($"WorldMapV3: {ProvinceEconomySystem.RecordCount} province economy records generated.");
        }

        _provinceBounds.Clear();
        foreach (var prov in _provinces)
            _provinceBounds[prov.Id] = ComputeBounds(prov);
        _provinceById.Clear();
        foreach (var prov in _provinces)
            _provinceById[prov.Id] = prov;
        BuildProvinceGrid();
        DrawProvinces();
        RefreshProvinceColors();
        QueueRedraw();
        GD.Print($"WorldMapV3: {_provinces.Count} provinces drawn.");
    }

    private static Rect2 ComputeBounds(ProvinceData prov)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var ring in prov.Rings)
            foreach (var p in ring)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private void DrawProvinces()
    {
        foreach (var prov in _provinces)
        {
            var nodes = new List<Polygon2D>();
            foreach (var ring in prov.Rings)
            {
                var node = new Polygon2D { Polygon = ring, Color = ModeColorForProvince(prov) };
                AddChild(node);
                nodes.Add(node);
            }
            _provinceNodes[prov.Id] = nodes;
        }
    }

    private Color ModeColorForProvince(ProvinceData prov)
    {
        if (CurrentMapMode == MapMode.Political)
            return ColorForId(prov.CountryId);
        if (CurrentMapMode == MapMode.Culture)
            return ModeColorForProvinceCulture(prov);
        return ModeColorForCountryId(prov.CountryId);
    }

    private Color ModeColorForProvinceCulture(ProvinceData prov)
    {
        if (prov == null) return Colors.Transparent;
        string culture = ProvinceCultureSystem.CultureOf(prov.Id);
        var color = ColorForCulture(culture);

        if (_cultureHighlight.Length > 0 &&
            !string.Equals(culture, _cultureHighlight, StringComparison.Ordinal))
            return DimColor(color);

        return color;
    }

    private static Color DimColor(Color c)
    {
        const float k = 0.2f;
        return new Color(c.R * k, c.G * k, c.B * k, 1f);
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
        GD.Print($"WorldMapV3: culture highlight = {(next.Length > 0 ? next : "(none)")}");
    }

    public void ApplyMapViewState()
    {
        var mode = MapViewSystem.Mode;
        SetMapMode((MapMode)(int)mode);
        string highlight = mode == WorldMap.MapMode.Culture ? MapViewSystem.CultureHighlight : "";
        if (mode != WorldMap.MapMode.Culture) MapViewSystem.SetCultureHighlight("");
        SetCultureHighlight(highlight);
        GD.Print($"[MapView V3] Applied: mode={mode}, highlight={(highlight.Length > 0 ? highlight : "(none)")}");
    }

    private static readonly Dictionary<string, Color> CultureColorCache = new(StringComparer.Ordinal);

    public static Color ColorForCulture(string culture)
    {
        if (string.IsNullOrEmpty(culture))
            return new Color(0.29f, 0.29f, 0.32f, 1f);
        if (CultureColorCache.TryGetValue(culture, out var cached)) return cached;

        unchecked
        {
            uint h = 2166136261u;
            foreach (char ch in culture)
            {
                h ^= (byte)ch; h *= 16777619u;
                h ^= (byte)(ch >> 8); h *= 16777619u;
            }
            float hue = (float)(h >> 8) * (1f / 4194304f);
            var color = Color.FromHsv(hue, 0.62f, 0.88f, 1f);
            CultureColorCache[culture] = color;
            return color;
        }
    }

    public Color ProvinceFillColor(string provinceId)
    {
        if (_provinceNodes.TryGetValue(provinceId, out var nodes) && nodes.Count > 0)
            return nodes[0].Color;
        return Colors.Transparent;
    }

    public void RefreshProvinceColors()
    {
        ComputeCultureMismatches();

        foreach (var kvp in _provinceNodes)
        {
            Color c = CurrentMapMode == MapMode.Culture
                ? ModeColorForProvinceCulture(_provinceById.GetValueOrDefault(kvp.Key))
                : ModeColorForCountryId(ProvinceOwnershipSystem.GetProvinceOwner(kvp.Key));
            foreach (var node in kvp.Value)
                if (GodotObject.IsInstanceValid(node)) node.Color = c;
        }
    }

    public string HoveredProvinceId => _hoverProvinceId;
    public string SelectedCountryId => _selected?.Id ?? "";
    public int CultureMismatchProvinceCount => _cultureMismatchProvinces.Count;

    public bool IsCultureMismatchProvince(string provinceId)
    {
        return !string.IsNullOrEmpty(provinceId) && _cultureMismatchProvinces.Contains(provinceId);
    }

    public IReadOnlyList<string> CultureMismatchProvinceIds
    {
        get
        {
            var list = new List<string>(_cultureMismatchProvinces);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }

    public string CultureTipText => _cultureTipLabel?.Text ?? "";
    public bool CultureTipVisible => _cultureTip?.Visible ?? false;
    public Control.MouseFilterEnum CultureTipMouseFilter => _cultureTip?.MouseFilter ?? Control.MouseFilterEnum.Stop;

    private void ComputeCultureMismatches()
    {
        _cultureMismatchProvinces.Clear();
        if (_provinceNodes.Count == 0) return;

        var byCountry = new Dictionary<string, List<ProvinceCultureSystem.CultureRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in ProvinceCultureSystem.All())
        {
            if (rec == null) continue;
            string owner = rec.CountryId ?? "";
            if (!byCountry.TryGetValue(owner, out var list))
            {
                list = new List<ProvinceCultureSystem.CultureRecord>();
                byCountry[owner] = list;
            }
            list.Add(rec);
        }

        foreach (var kv in byCountry)
        {
            var dist = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rec in kv.Value)
            {
                string culture = rec.Culture ?? "";
                dist.TryGetValue(culture, out int n);
                dist[culture] = n + 1;
            }
            if (dist.Count <= 1) continue;

            string dominant = dist
                .OrderByDescending(d => d.Value)
                .ThenBy(d => d.Key, StringComparer.Ordinal)
                .First().Key;

            foreach (var rec in kv.Value)
                if (!string.Equals(rec.Culture ?? "", dominant, StringComparison.Ordinal))
                    _cultureMismatchProvinces.Add(rec.ProvinceId);
        }
    }

    private void BuildCultureTip()
    {
        _cultureTipLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _cultureTipLabel.AddThemeFontSizeOverride("font_size", 14);
        _cultureTipLabel.AddThemeColorOverride("font_color", UITheme.TextMain);

        _cultureTip = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        _cultureTip.AddThemeConstantOverride("margin_left", 8);
        _cultureTip.AddThemeConstantOverride("margin_right", 8);
        _cultureTip.AddThemeConstantOverride("margin_top", 6);
        _cultureTip.AddThemeConstantOverride("margin_bottom", 6);
        _cultureTip.AddChild(_cultureTipLabel);
        AddChild(_cultureTip);
    }

    private void UpdateProvinceHover(Vector2 worldPos)
    {
        if (CurrentMapMode != MapMode.Culture)
        {
            if (_hoverProvinceId.Length > 0 || _cultureTip.Visible)
            {
                _hoverProvinceId = "";
                _cultureTipLabel.Text = "";
                _cultureTip.Visible = false;
                _cultureTipDirty = false;
            }
            return;
        }

        var prov = FindProvinceAt(worldPos);
        string next = prov?.Id ?? "";
        if (next == _hoverProvinceId) return;

        _hoverProvinceId = next;
        _cultureTipDirty = true;
        QueueRedraw();
    }

    private void RefreshCultureTipText()
    {
        var rec = ProvinceCultureSystem.Get(_hoverProvinceId);
        if (rec == null)
        {
            _cultureTipLabel.Text = "";
            _cultureTip.Visible = false;
            _cultureTipDirty = false;
            return;
        }

        string culture = string.IsNullOrEmpty(rec.Culture) ? "(no culture)" : rec.Culture;
        string owner = rec.CountryId ?? "";
        string ownerName = CountryDatabase.Get(owner)?.Name ?? owner;
        bool mismatch = _cultureMismatchProvinces.Contains(rec.ProvinceId);
        string dominant = "";
        if (mismatch)
        {
            var dist = ProvinceCultureSystem.GetCultureDistribution(owner);
            dominant = dist.Count > 0
                ? dist.OrderByDescending(kv => kv.Value)
                      .ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key
                : "";
        }

        _cultureTipLabel.Text =
            $"{rec.ProvinceId} — {ownerName} ({owner})\n" +
            $"Culture: {culture}   ({rec.Population:N0})\n" +
            (mismatch
                ? $"Foreign culture in {owner} (dominant: {dominant})"
                : $"Dominant culture of {owner}");

        _cultureTip.Visible = true;
        _cultureTipDirty = false;
    }

    private void PositionCultureTip()
    {
        if (_cultureTip == null || !_cultureTip.Visible) return;

        var viewport = GetViewportRect().Size;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 size = _cultureTip.Size;
        if (size == Vector2.Zero) size = _cultureTip.GetCombinedMinimumSize();

        const float offset = 16f;
        Vector2 pos = mouse + new Vector2(offset, offset);
        if (pos.X + size.X > viewport.X) pos.X = mouse.X - size.X - offset;
        if (pos.Y + size.Y > viewport.Y) pos.Y = mouse.Y - size.Y - offset;
        pos.X = Mathf.Clamp(pos.X, 4f, Mathf.Max(4f, viewport.X - size.X));
        pos.Y = Mathf.Clamp(pos.Y, 4f, Mathf.Max(4f, viewport.Y - size.Y));

        _cultureTip.Position = pos;
    }

    private void OnProvinceOwnerChanged(string provinceId, string oldCountryId, string newCountryId)
    {
        RefreshProvinceColors();
        QueueRedraw();
        GD.Print($"[WorldMapV3] Province owner changed: {provinceId} {oldCountryId} → {newCountryId}");
    }

    // ===== Spatial Index for Hit Testing =====

    public ProvinceData FindProvinceAt(Vector2 worldPos)
    {
        if (_provinces.Count == 0) return null;

        int cx = (int)Mathf.Floor(worldPos.X / ProvinceCellSize);
        int cy = (int)Mathf.Floor(worldPos.Y / ProvinceCellSize);

        ProvinceData best = null;
        float bestArea = float.MaxValue;

        for (int gy = cy - 1; gy <= cy + 1; gy++)
        {
            for (int gx = cx - 1; gx <= cx + 1; gx++)
            {
                if (!_provinceGrid.TryGetValue(CellKey(gx, gy), out var bucket)) continue;
                foreach (int idx in bucket)
                {
                    var prov = _provinces[idx];
                    if (!_provinceBounds.TryGetValue(prov.Id, out var box)) continue;
                    if (!box.HasPoint(worldPos)) continue;
                    foreach (var ring in prov.Rings)
                        if (Geometry2D.IsPointInPolygon(worldPos, ring))
                        {
                            float area = Mathf.Abs((float)RingArea(ring));
                            if (area < bestArea) { best = prov; bestArea = area; }
                            break;
                        }
                }
            }
        }
        return best;
    }

    private static long CellKey(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

    private void BuildProvinceGrid()
    {
        _provinceGrid.Clear();
        for (int i = 0; i < _provinces.Count; i++)
        {
            if (!_provinceBounds.TryGetValue(_provinces[i].Id, out var box)) continue;
            int x0 = (int)Mathf.Floor(box.Position.X / ProvinceCellSize);
            int y0 = (int)Mathf.Floor(box.Position.Y / ProvinceCellSize);
            int x1 = (int)Mathf.Floor(box.End.X / ProvinceCellSize);
            int y1 = (int)Mathf.Floor(box.End.Y / ProvinceCellSize);
            for (int gy = y0; gy <= y1; gy++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    long key = CellKey(gx, gy);
                    if (!_provinceGrid.TryGetValue(key, out var bucket))
                        _provinceGrid[key] = bucket = new List<int>(8);
                    bucket.Add(i);
                }
        }
    }

    // ===== Effects Layer =====

    public partial class MapEffects : Node2D
    {
        public override void _Draw()
        {
            var wm = WorldMapV3.Instance;
            if (wm == null) return;

            if (wm._eraAlpha > 0.001f)
            {
                var bigRect = new Rect2(-1000f, -1000f, wm.MapSize.X + 2000f, wm.MapSize.Y + 2000f);
                DrawRect(bigRect, new Color(wm._eraColor.R, wm._eraColor.G, wm._eraColor.B, wm._eraAlpha));
            }

            if (wm._terminatorVisible && wm._terminatorTexture != null)
            {
                float bandW = wm.MapSize.X * TerminatorBandWidthRatio;
                var rect = new Rect2(
                    wm._terminatorX - bandW / 2f, -1000f,
                    bandW, wm.MapSize.Y + 2000f);
                DrawTextureRect(wm._terminatorTexture, rect, false);
            }

            if (wm._vignetteVisible && wm._vignetteTexture != null)
            {
                var bigRect = new Rect2(-1000f, -1000f, wm.MapSize.X + 2000f, wm.MapSize.Y + 2000f);
                DrawTextureRect(wm._vignetteTexture, bigRect, false);
            }
        }
    }

    // ===== JSON DTOs =====

    private class CountryDataJson
    {
        public string iso3 { get; set; }
        public string name { get; set; }
        public string sovereign { get; set; }
        public double area_km2 { get; set; }
        public double[] bbox { get; set; }
        public Dictionary<string, JsonElement> lod { get; set; }
    }

    private class RegionDataJson
    {
        public string id { get; set; }
        public string country_iso3 { get; set; }
        public string name { get; set; }
        public string iso_3166_2 { get; set; }
        public double area_km2 { get; set; }
        public double[] bbox { get; set; }
        public Dictionary<string, JsonElement> lod { get; set; }
    }

    private class CityDataJson
    {
        public string id { get; set; }
        public string name { get; set; }
        public string name_ascii { get; set; }
        public string country_iso3 { get; set; }
        public int population_max { get; set; }
        public int pop_rank { get; set; }
        public bool is_capital { get; set; }
        public bool is_admin0_capital { get; set; }
        public double latitude { get; set; }
        public double longitude { get; set; }
        public float min_zoom { get; set; }
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

    // ===== Ring Clipping =====

    private Vector2[] ConvertRing(JsonElement ring)
    {
        if (ring.ValueKind != JsonValueKind.Array || ring.GetArrayLength() == 0)
            return null;

        var points = new List<Vector2>();
        foreach (var coord in ring.EnumerateArray())
        {
            if (coord.ValueKind == JsonValueKind.Array && coord.GetArrayLength() >= 2)
            {
                double lon = coord[0].GetDouble();
                double lat = coord[1].GetDouble();
                points.Add(Project(lon, lat));
            }
        }
        return points.Count >= 3 ? points.ToArray() : null;
    }

    private void CreateCountryVisuals(Country country)
    {
        // Use LOD based on current zoom - start with LOD0
        var rings = country.Lod0Rings.Count > 0 ? country.Lod0Rings :
                   (country.Lod1Rings.Count > 0 ? country.Lod1Rings : country.Lod2Rings);

        foreach (var ring in rings)
        {
            var body = new Polygon2D { Polygon = ring, Color = country.MapColor };
            AddChild(body);
            country.Nodes.Add(body);

            // Glow outline (coastal glow)
            var glow = new Line2D
            {
                Points = ring,
                DefaultColor = new Color(0.3f, 0.8f, 0.9f, 0.20f),
                Width = 3.6f,
                Closed = true,
            };
            AddChild(glow);
            country.GlowOutlines.Add(glow);

            // Main border
            var outline = new Line2D { Points = ring, DefaultColor = BorderColor, Width = 1.1f, Closed = true };
            AddChild(outline);
            country.Outlines.Add(outline);
        }

        // Compute label anchor and bbox from largest ring
        Vector2[] biggest = null;
        double biggestArea = -1;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var maxPt = new Vector2(float.MinValue, float.MinValue);

        foreach (var ring in rings)
        {
            double area = Mathf.Abs(RingArea(ring));
            if (area > 0 && area < country.SmallestRingArea) country.SmallestRingArea = (float)area;
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
            country.LargestBBox = maxPt - min;
        }
    }

    private static Vector2[] ClipRingToMap(Vector2[] ring, Vector2 mapSize)
    {
        const float pad = 1.5f;
        var bounds = new Rect2(0f, 0f, mapSize.X, mapSize.Y);
        bool inside = true;
        foreach (var p in ring)
        {
            if (!new Rect2(0f, 0f, mapSize.X + pad, mapSize.Y + pad).HasPoint(p)) { inside = false; break; }
        }
        if (inside) return ring;

        var poly = new List<Vector2>(ring);

        Vector2 Intersect(Vector2 a, Vector2 b, float edge, bool horizontal, bool greater)
        {
            float t = horizontal
                ? (b.Y - a.Y == 0f ? 0f : (edge - a.Y) / (b.Y - a.Y))
                : (b.X - a.X == 0f ? 0f : (edge - a.X) / (b.X - a.X));
            t = Mathf.Clamp(t, 0f, 1f);
            var pt = a.Lerp(b, t);
            return horizontal ? new Vector2(pt.X, edge) : new Vector2(edge, pt.Y);
        }

        List<Vector2> ClipSide(List<Vector2> input, float edge, bool horizontal, bool greater, bool keepGreater)
        {
            var output = new List<Vector2>();
            for (int i = 0; i < input.Count; i++)
            {
                var cur = input[i];
                var prev = input[(i + input.Count - 1) % input.Count];
                bool curIn = greater ? (horizontal ? cur.X >= edge : cur.Y >= edge)
                                     : (horizontal ? cur.X <= edge : cur.Y <= edge);
                bool prevIn = greater ? (horizontal ? prev.X >= edge : prev.Y >= edge)
                                      : (horizontal ? prev.X <= edge : prev.Y <= edge);
                if (curIn)
                {
                    if (!prevIn) output.Add(Intersect(prev, cur, edge, horizontal, greater));
                    output.Add(cur);
                }
                else if (prevIn)
                {
                    output.Add(Intersect(prev, cur, edge, horizontal, greater));
                }
            }
            return output;
        }

        poly = ClipSide(poly, 0f, false, false, true);
        if (poly.Count < 3) return ring;
        poly = ClipSide(poly, mapSize.X, false, true, false);
        if (poly.Count < 3) return ring;
        poly = ClipSide(poly, 0f, true, false, true);
        if (poly.Count < 3) return ring;
        poly = ClipSide(poly, mapSize.Y, true, true, false);
        if (poly.Count < 3) return ring;

        return poly.ToArray();
    }
}