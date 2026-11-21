using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

/// <summary>
/// WorldMapV5 — Visual-reference world map built from scratch for HOI4-style appearance.
/// Features: Infinite horizontal wrapping, HOI4-style camera, integrated HUD support.
/// </summary>
public partial class WorldMapV5 : Node2D
{
    [Signal] public delegate void CountrySelectedEventHandler(string countryId, string countryName);
    [Signal] public delegate void CountryDeselectedEventHandler();

    public static WorldMapV5 Instance { get; private set; }

    // Background texture path - using natural_earth_2.png (Natural Earth II, 10800x5400 PNG)
    private const string BackgroundTexturePath = "res://Assets/map_textures/natural_earth_2.png";

    // Camera settings - HOI4 style
    private const float MIN_ZOOM_COVER = 0f; // Calculated at runtime
    private const float MAX_ZOOM = 10f;      // HOI4 goes to ~10x
    private const float ZOOM_SPEED = 1.15f;  // Smoother zoom
    private const float PAN_SPEED_KEYBOARD = 1200f; // WASD pan speed
    private const float MIN_ZOOM_CLAMP = 0.3f; // Don't zoom out past world coverage

    // Map wrapping - HOI4 style infinite horizontal
    private const float WORLD_WIDTH = 10800f;  // Texture width in world pixels
    private const float WORLD_HEIGHT = 5400f;  // Texture height
    private const float WRAP_MARGIN = 100f;    // Margin for smooth wrapping

    // HUD height offset (top bar) for vertical coverage calculation - updated dynamically
    private float _hudTopHeight = 50f; // Default fallback, updated from GameHud

    // Node references
    private Node2D _worldRoot;       // Single parent for all world-space content
    private Node2D _backgroundRoot;  // Root for wrapped backgrounds
    private Camera2D _camera;        // Single camera for map
    private CanvasLayer _uiCanvas;   // Debug overlay only
    private Label _debugLabel;       // Debug info display

    // State
    private Vector2 _textureSize;    // Background texture pixel size
    private float _minZoom;          // Calculated cover zoom
    private Vector2 _lastMousePos;   // For RMB drag
    private bool _dragging = false;
    private bool _debugMode = true;  // Toggle with F3

    // Background sprites for wrapping (3 copies: center, left, right)
    private Sprite2D _bgCenter;
    private Sprite2D _bgLeft;
    private Sprite2D _bgRight;

    // Debug visualization
    private Node2D _debugDrawNode;
    private bool _showGraticule = true;
    private bool _showCoastline = true;
    private bool _showReferencePoints = true;

    // Phase 5: Selection state
    private string _selectedCountryId = null;
    private string _hoveredCountryId = null;

    // Performance optimization: spatial grid for hover detection
    private const int GridCellSize = 256;
    private Dictionary<long, List<string>> _countryGrid = new();
    private bool _gridBuilt = false;

    // Selection outline
    private V5SelectionOutline _selectionOutlineNode = null;
    private Line2D _hoverOutline = null;

    // Performance: track zoom changes for border width updates
    private float _lastZoomForBorderUpdate = -1f;

    // Performance: cache parsed polygons for hit testing
    private Dictionary<string, List<Vector2[]>> _cachedPolygons = new();

    // Public properties for test access
    public Vector2 TextureSize => _textureSize;
    public Camera2D Camera => _camera;
    public string SelectedCountryId => _selectedCountryId;
    public IReadOnlyDictionary<string, CountryData> Countries => _countries;
    public string HoveredCountryId => _hoveredCountryId;

    // Test mode fields (Phase 5)
    public bool TestMode { get; set; } = false;
    public (string name, double lon, double lat)[] TestCases { get; set; }
    public int TestIndex { get; set; } = 0;
    public int TestPhase { get; set; } = 0;
    public float TestTimer { get; set; } = 0f;
    private bool _testStepRunning = false;

    // 12 Reference points for alignment verification
    private static readonly (string name, double lon, double lat)[] ReferencePoints =
    {
        ("Istanbul", 28.97, 41.01),
        ("London", -0.13, 51.51),
        ("Cairo", 31.24, 30.04),
        ("Cape Town", 18.42, -33.92),
        ("Tokyo", 139.69, 35.69),
        ("Sydney", 151.21, -33.87),
        ("Rio", -43.17, -22.91),
        ("New York", -74.00, 40.71),
        ("Moscow", 37.62, 55.75),
        ("Singapore", 103.85, 1.29),
        ("Reykjavik", -21.90, 64.15),
        ("Panama City", -79.52, 8.98),
    };

    // ===== PHASE 4: Country Fills & Borders =====
    public sealed class CountryData
    {
        public string Id;
        public string Name;
        public float[] Bbox;
        public List<List<float[]>> Polygons;
        public List<int> PolygonSizes;
        public Vector2 LabelPoint;
        public float Area;
        public Color FillColor;
        public Color BorderColor = new Color("#0E141B");
        public MeshInstance2D FillMesh;
        public Line2D BorderLine;
        public bool Selected = false;
        public bool Hovered = false;
        public List<Vector2[]> CachedExteriors = new();
    }

    // 24-color muted earth-tone palette (HOI4 style)
    private static readonly Color[] CountryPalette = new Color[]
    {
        new Color("#8B7355"), new Color("#7A8A6D"), new Color("#9A7B5E"), new Color("#7D5F4D"),
        new Color("#6B8A9C"), new Color("#8A6E8C"), new Color("#5D9A7A"), new Color("#8A9A5E"),
        new Color("#6B7BA8"), new Color("#9A7C5E"), new Color("#7A8F6E"), new Color("#8F6E5F"),
        new Color("#5C7A8F"), new Color("#9A8E6B"), new Color("#6E6B8F"), new Color("#8F7A6B"),
        new Color("#4F6E5A"), new Color("#7A6E8F"), new Color("#8E8A7A"), new Color("#6E8F8A"),
        new Color("#9C6B5A"), new Color("#5C8F6B"), new Color("#8F8A6E"), new Color("#6B5E52"),
    };

    private const string CountriesDataPath = "res://Data/map/v5/countries_lonlat.json";
    private readonly Dictionary<string, CountryData> _countries = new();
    private int _drawnCountryCount = 0;
    private int _skippedRingCount = 0;

    // Input handling
    private const float DRAG_THRESHOLD = 3f; // Lower threshold for more responsive drag start
private bool _dragStarted = false; // Track if drag has actually started moving

    public override void _Ready()
    {
        Instance = this;

        ApplyWindowSizeForShot();
        CreateSceneTree();
        LoadBackground();
        SetupCamera();
        SetupUICanvas();

        Input.MouseMode = Input.MouseModeEnum.Visible;
        GD.Print("WorldMapV5: Ready (Infinite wrapping enabled)");
    }

    private void ApplyWindowSizeForShot()
    {
        string shotName = GameManager.V5ShotName;
        if (shotName == null) return;

        // Extract size suffix (e.g., "1143x672", "1280x720", "1920x1080") from shot name
        Vector2I size = new Vector2I(1280, 720); // Default
        var parts = shotName.Split('_');
        string sizePart = parts[parts.Length - 1]; // Last part after last underscore
        if (sizePart.Contains('x'))
        {
            var wh = sizePart.Split('x');
            if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h))
            {
                size = new Vector2I(w, h);
            }
        }

        DisplayServer.WindowSetSize(size);
        GD.Print($"WorldMapV5: Window size set to {size.X}x{size.Y} for shot: {shotName}");

        // Set camera position and zoom for specific scenarios
        SetupCameraForShot(shotName);
    }

    private void SetupCameraForShot(string shotName)
    {
        // Camera setup will be applied in InitializeCamera after texture loads
        // Store the shot name for later use
        _pendingShotName = shotName;
    }

    private string _pendingShotName = null;

    private void ApplyShotCameraSettings()
    {
        if (string.IsNullOrEmpty(_pendingShotName)) return;

        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        string shotName = _pendingShotName;

        if (shotName.StartsWith("world_"))
        {
            // Full world - already centered at cover zoom
        }
        else if (shotName.StartsWith("cover_"))
        {
            // Handle corner panning for cover tests: cover_tl_WxH, cover_tr_WxH, cover_bl_WxH, cover_br_WxH
            // Also handle centered: cover_WxH
            string afterCover = shotName.Substring(6); // e.g., "tl_1280x720" or "1280x720"
            string corner = "center";
            if (afterCover.Length >= 3 && afterCover[2] == '_')
            {
                corner = afterCover.Substring(0, 2); // tl, tr, bl, br
            }

            var vp = GetViewport();
            var vpSize = vp.GetVisibleRect().Size;
            Vector2 visibleWorld = vpSize / _camera.Zoom.X;
            float halfW = visibleWorld.X / 2f;
            float halfH = visibleWorld.Y / 2f;

            float targetX, targetY;
            switch (corner)
            {
                case "tl": // Top-left
                    targetX = halfW;
                    targetY = halfH;
                    break;
                case "tr": // Top-right
                    targetX = _textureSize.X - halfW;
                    targetY = halfH;
                    break;
                case "bl": // Bottom-left
                    targetX = halfW;
                    targetY = _textureSize.Y - halfH;
                    break;
                case "br": // Bottom-right
                    targetX = _textureSize.X - halfW;
                    targetY = _textureSize.Y - halfH;
                    break;
                default: // center
                    targetX = _textureSize.X / 2f;
                    targetY = _textureSize.Y / 2f;
                    break;
            }
            _camera.Position = new Vector2(targetX, targetY);
            GD.Print($"Cover corner pan: {corner} -> Pos=({targetX:F1},{targetY:F1})");
        }
        else if (shotName.StartsWith("europe_"))
        {
            // Europe/Middle East: center around (30°E, 45°N), zoom ~0.5
            _camera.Position = MapProjectionV5.LonLatToWorld(30, 45);
            _camera.Zoom = new Vector2(0.5f, 0.5f);
        }
        else if (shotName.StartsWith("istanbul_"))
        {
            // Istanbul/Bosporus: center around (29°E, 41°N), zoom ~1.5
            _camera.Position = MapProjectionV5.LonLatToWorld(29, 41);
            _camera.Zoom = new Vector2(1.5f, 1.5f);
        }
        else if (shotName.StartsWith("uk_"))
        {
            // UK: center around (0°, 54°N), zoom ~1.0
            _camera.Position = MapProjectionV5.LonLatToWorld(0, 54);
            _camera.Zoom = new Vector2(1.0f, 1.0f);
        }
        else if (shotName.StartsWith("japan_"))
        {
            // Japan: center around (137°E, 36°N), zoom ~1.0
            _camera.Position = MapProjectionV5.LonLatToWorld(137, 36);
            _camera.Zoom = new Vector2(1.0f, 1.0f);
        }

        // Clamp zoom to valid range
        float clampedZoom = Mathf.Clamp(_camera.Zoom.X, _minZoom, MAX_ZOOM);
        _camera.Zoom = new Vector2(clampedZoom, clampedZoom);

        UpdateCameraLimits();
        ClampCamera();

        GD.Print($"WorldMapV5: Shot camera applied for {_pendingShotName}: Pos={_camera.Position}, Zoom={_camera.Zoom.X:F4}");
        _pendingShotName = null;
    }

    private void CreateSceneTree()
    {
        // WorldRoot: single Node2D containing ALL world-space content
        _worldRoot = new Node2D();
        _worldRoot.Name = "WorldRoot";
        AddChild(_worldRoot);

        // Background root for wrapping (3 copies: left, center, right)
        _backgroundRoot = new Node2D();
        _backgroundRoot.Name = "BackgroundRoot";
        _worldRoot.AddChild(_backgroundRoot);

        // Background sprites for infinite horizontal wrapping (HOI4 style)
        _bgCenter = CreateBackgroundSprite("BackgroundCenter", Vector2.Zero);
        _bgLeft = CreateBackgroundSprite("BackgroundLeft", new Vector2(-WORLD_WIDTH, 0));
        _bgRight = CreateBackgroundSprite("BackgroundRight", new Vector2(WORLD_WIDTH, 0));

        // Camera2D for map navigation - HOI4 style
        _camera = new Camera2D();
        _camera.Name = "MapCamera";
        _camera.Zoom = Vector2.One;
        _worldRoot.AddChild(_camera);
        _camera.MakeCurrent();

        // Selection outline: V5SelectionOutline node handles all rings (islands included), gold color, 2.5px screen width
        _selectionOutlineNode = new V5SelectionOutline();
        _selectionOutlineNode.Name = "SelectionOutline";
        _selectionOutlineNode.Initialize(this);
        _worldRoot.AddChild(_selectionOutlineNode);

        // Hover outline: white border for hovered country
        _hoverOutline = new Line2D();
        _hoverOutline.Name = "HoverOutline";
        _hoverOutline.DefaultColor = new Color("#FFFFFF"); // White
        _hoverOutline.Width = 1.5f; // Base width, will scale with zoom
        _hoverOutline.Antialiased = true;
        _hoverOutline.ZIndex = 5; // Above fills (0) and borders (1), below selection (10)
        _hoverOutline.Visible = false;
        _worldRoot.AddChild(_hoverOutline);

        // Debug draw node: draws graticule, coastline, reference points in world space
        _debugDrawNode = new DebugDrawNode();
        _debugDrawNode.Name = "DebugDraw";
        _worldRoot.AddChild(_debugDrawNode);
        (_debugDrawNode as DebugDrawNode).Initialize(this);

        // UI CanvasLayer: ONLY for debug overlay, NOT for map content
        _uiCanvas = new CanvasLayer();
        _uiCanvas.Name = "UICanvas";
        _uiCanvas.Layer = 100; // Above everything
        AddChild(_uiCanvas);
    }

    private Sprite2D CreateBackgroundSprite(string name, Vector2 position)
    {
        var sprite = new Sprite2D();
        sprite.Name = name;
        sprite.Centered = false;
        sprite.Position = position;
        sprite.Scale = Vector2.One;
        sprite.ZIndex = -100; // Behind everything
        _backgroundRoot.AddChild(sprite);
        return sprite;
    }

    private void LoadBackground()
    {
        var texture = GD.Load<Texture2D>(BackgroundTexturePath);
        if (texture == null)
        {
            GD.PrintErr($"WorldMapV5: CRITICAL - Failed to load background texture: {BackgroundTexturePath}");
            _textureSize = new Vector2(WORLD_WIDTH, WORLD_HEIGHT);
            return;
        }

        // Assign texture to all 3 background sprites
        _bgCenter.Texture = texture;
        _bgLeft.Texture = texture;
        _bgRight.Texture = texture;

        _textureSize = new Vector2(texture.GetWidth(), texture.GetHeight());

        GD.Print($"WorldMapV5: Background loaded: {_textureSize.X}x{_textureSize.Y} (wrapping: {WORLD_WIDTH}x{WORLD_HEIGHT})");

        // Initialize MapProjectionV5 with measured texture bounds
        MapProjectionV5.Initialize((int)_textureSize.X, (int)_textureSize.Y);

        GD.Print("[WorldMapV5] Starting LoadCountries...");
        LoadCountries();
        GD.Print($"[WorldMapV5] LoadCountries completed. Countries count: {_countries.Count}");
    }

    // ===== PHASE 4: Load Countries from JSON =====
    private void LoadCountries()
    {
        if (!ResourceLoader.Exists(CountriesDataPath))
        {
            GD.PrintErr($"WorldMapV5: Countries data not found at {CountriesDataPath}");
            return;
        }

        var json = FileAccess.GetFileAsString(CountriesDataPath);
        if (string.IsNullOrEmpty(json))
        {
            GD.PrintErr("WorldMapV5: Empty countries JSON");
            return;
        }

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("countries", out var countriesArray))
        {
            GD.PrintErr("WorldMapV5: No 'countries' array in JSON");
            return;
        }

        int loaded = 0;
        int paletteIndex = 0;

        foreach (var countryElem in countriesArray.EnumerateArray())
        {
            var bboxLonLat = ParseFloatArray(countryElem.GetProperty("bbox")); // [minLon, minLat, maxLon, maxLat]
            
            // Convert bbox from lon/lat to world pixels
            // MapProjectionV5.LonLatToWorld: lon [-180,180] -> x [0, imgW], lat [90,-90] -> y [0, imgH]
            // So minLat (more south) -> larger Y, maxLat (more north) -> smaller Y
            var bottomLeft = MapProjectionV5.LonLatToWorld(bboxLonLat[0], bboxLonLat[1]); // minLon, minLat -> maxY
            var topRight = MapProjectionV5.LonLatToWorld(bboxLonLat[2], bboxLonLat[3]);   // maxLon, maxLat -> minY
            
            // World pixels: minX, minY, maxX, maxY
            var bboxWorld = new float[] { 
                bottomLeft.X,    // minX
                topRight.Y,      // minY (north = smaller Y)
                topRight.X,      // maxX
                bottomLeft.Y     // maxY (south = larger Y)
            }; 
            
            // Debug first few countries
            if (loaded < 5)
            {
                GD.Print($"[BBox] {countryElem.GetProperty("id").GetString()}: LonLat=[{bboxLonLat[0]:F2},{bboxLonLat[1]:F2},{bboxLonLat[2]:F2},{bboxLonLat[3]:F2}] -> World=[{bboxWorld[0]:F1},{bboxWorld[1]:F1},{bboxWorld[2]:F1},{bboxWorld[3]:F1}]");
            }
            
            var country = new CountryData
            {
                Id = countryElem.GetProperty("id").GetString() ?? "?",
                Name = countryElem.GetProperty("name").GetString() ?? "?",
                Bbox = bboxWorld, // Now in world pixels: [minX, minY, maxX, maxY]
                Polygons = ParsePolygons(countryElem.GetProperty("polygons")),
                LabelPoint = new Vector2(
                    countryElem.GetProperty("label_point")[0].GetSingle(),
                    countryElem.GetProperty("label_point")[1].GetSingle()
                ),
                Area = countryElem.GetProperty("area").GetSingle(),
            };

            // Assign palette color (hash-based for consistency)
            country.FillColor = CountryPalette[paletteIndex % CountryPalette.Length];
            country.FillColor = new Color(country.FillColor.R, country.FillColor.G, country.FillColor.B, 0.5f); // ~50% alpha
            paletteIndex++;

            // Cache exterior polygons for hit testing (convert from lon/lat to world pixels)
            var cachedExteriors = new List<Vector2[]>();
            var json2 = FileAccess.GetFileAsString(CountriesDataPath);
            var doc2 = JsonDocument.Parse(json2);
            var countriesArray2 = doc2.RootElement.GetProperty("countries");
            foreach (var c in countriesArray2.EnumerateArray())
            {
                if (c.GetProperty("id").GetString() == country.Id)
                {
                    if (c.TryGetProperty("polygons", out var polysElem))
                    {
                        foreach (var polyElem in polysElem.EnumerateArray())
                        {
                            if (polyElem.TryGetProperty("exterior", out var extElem))
                            {
                                var exterior = ParseRing(extElem);
                                if (exterior.Length >= 3)
                                {
                                    cachedExteriors.Add(exterior);
                                }
                            }
                        }
                    }
                    break;
                }
            }
            country.CachedExteriors = cachedExteriors;
            _cachedPolygons[country.Id] = cachedExteriors;

            _countries[country.Id] = country;
            loaded++;
        }

        GD.Print($"WorldMapV5: Loaded {loaded} countries from {CountriesDataPath}");

        // Build spatial grid for fast hover detection
        BuildSpatialGrid();

        // Create visual meshes for all countries
        CreateCountryMeshes();
    }

    private float[] ParseFloatArray(JsonElement elem)
    {
        var arr = new float[elem.GetArrayLength()];
        int i = 0;
        foreach (var v in elem.EnumerateArray())
        {
            arr[i++] = v.GetSingle();
        }
        return arr;
    }

    private List<List<float[]>> ParsePolygons(JsonElement elem)
    {
        var polygons = new List<List<float[]>>();
        try
        {
            foreach (var polyElem in elem.EnumerateArray())
            {
                // Handle both formats:
                // 1. Array of rings (flat): [[x,y], [x,y], ...]
                // 2. Polygon feature with exterior/holes: {exterior: [...], holes: [...]}
                if (polyElem.ValueKind == JsonValueKind.Array)
                {
                    // Format 1: flat ring
                    var ring = new List<float[]>();
                    foreach (var pointElem in polyElem.EnumerateArray())
                    {
                        var coords = pointElem.EnumerateArray().ToArray();
                        ring.Add(new float[] { coords[0].GetSingle(), coords[1].GetSingle() });
                    }
                    polygons.Add(ring);
                }
                else if (polyElem.ValueKind == JsonValueKind.Object)
                {
                    // Format 2: polygon feature with exterior/holes
                    if (polyElem.TryGetProperty("exterior", out var extElem))
                    {
                        var extRing = new List<float[]>();
                        foreach (var pointElem in extElem.EnumerateArray())
                        {
                            var coords = pointElem.EnumerateArray().ToArray();
                            extRing.Add(new float[] { coords[0].GetSingle(), coords[1].GetSingle() });
                        }
                        polygons.Add(extRing);
                    }
                    // Also add holes
                    if (polyElem.TryGetProperty("holes", out var holesElem))
                    {
                        foreach (var holeElem in holesElem.EnumerateArray())
                        {
                            var holeRing = new List<float[]>();
                            foreach (var pointElem in holeElem.EnumerateArray())
                            {
                                var coords = pointElem.EnumerateArray().ToArray();
                                holeRing.Add(new float[] { coords[0].GetSingle(), coords[1].GetSingle() });
                            }
                            polygons.Add(holeRing);
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[ParsePolygons] Error: {e.Message}");
        }
        return polygons;
    }

    // Build spatial grid for fast hover detection
    private void BuildSpatialGrid()
    {
        _countryGrid.Clear();
        foreach (var kvp in _countries)
        {
            var country = kvp.Value;
            if (country.Bbox != null && country.Bbox.Length == 4)
            {
                int minCellX = (int)(country.Bbox[0] / GridCellSize);
                int maxCellX = (int)(country.Bbox[2] / GridCellSize);
                int minCellY = (int)(country.Bbox[1] / GridCellSize);
                int maxCellY = (int)(country.Bbox[3] / GridCellSize);

                for (int x = minCellX; x <= maxCellX; x++)
                {
                    for (int y = minCellY; y <= maxCellY; y++)
                    {
                        long key = ((long)x << 32) | (uint)y;
                        if (!_countryGrid.TryGetValue(key, out var list))
                        {
                            list = new List<string>();
                            _countryGrid[key] = list;
                        }
                        list.Add(country.Id);
                    }
                }
            }
        }
        _gridBuilt = true;
        GD.Print($"[WorldMapV5] Built spatial grid with {_countryGrid.Count} cells");
    }

    // ===== PHASE 4: Create Country Meshes (Triangulation + Borders) =====
    private void CreateCountryMeshes()
    {
        _drawnCountryCount = 0;
        _skippedRingCount = 0;

        foreach (var kvp in _countries)
        {
            var country = kvp.Value;
            try
            {
                CreateCountryFillMesh(country);
                CreateCountryBorderLine(country);
                _drawnCountryCount++;
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"WorldMapV5: Failed to create mesh for {country.Id}: {e.Message}");
            }
        }

        GD.Print($"WorldMapV5: Created meshes for {_drawnCountryCount} countries, skipped {_skippedRingCount} rings");
    }

    private void CreateCountryFillMesh(CountryData country)
    {
        var fillNode = new Node2D();
        fillNode.Name = $"Fill_{country.Id}";
        _worldRoot.AddChild(fillNode);

        var json = FileAccess.GetFileAsString(CountriesDataPath);
        var doc = JsonDocument.Parse(json);
        var countriesArray = doc.RootElement.GetProperty("countries");

        foreach (var c in countriesArray.EnumerateArray())
        {
            if (c.GetProperty("id").GetString() == country.Id)
            {
                if (c.TryGetProperty("polygons", out var polysElem))
                {
                    foreach (var polyElem in polysElem.EnumerateArray())
                    {
                        if (polyElem.TryGetProperty("exterior", out var extElem) &&
                            polyElem.TryGetProperty("holes", out var holesElem))
                        {
                            // Convert lon/lat to world pixels for rendering
                            var exterior = ParseRingToWorld(extElem);
                            var holes = new List<Vector2[]>();
                            foreach (var holeElem in holesElem.EnumerateArray())
                            {
                                holes.Add(ParseRingToWorld(holeElem));
                            }

                            // Exterior polygon
                            var fillPoly = new Polygon2D();
                            fillPoly.Polygon = exterior;
                            fillPoly.Color = country.FillColor;
                            fillNode.AddChild(fillPoly);

                            // Holes (inverse winding handled by Godot)
                            foreach (var hole in holes)
                            {
                                var holePoly = new Polygon2D();
                                holePoly.Polygon = hole;
                                holePoly.Color = country.FillColor;
                                fillNode.AddChild(holePoly);
                            }
                        }
                        else
                        {
                            var ring = ParseRingToWorld(polyElem);
                            if (ring.Length >= 3)
                            {
                                var poly2D = new Polygon2D();
                                poly2D.Polygon = ring;
                                poly2D.Color = country.FillColor;
                                fillNode.AddChild(poly2D);
                            }
                        }
                    }
                }
                break;
            }
        }
    }

    // Parse ring as lon/lat (for hit-testing cache)
    private Vector2[] ParseRing(JsonElement elem)
    {
        var points = new List<Vector2>();
        foreach (var pointElem in elem.EnumerateArray())
        {
            var coords = pointElem.EnumerateArray().ToArray();
            points.Add(new Vector2(coords[0].GetSingle(), coords[1].GetSingle()));
        }
        return points.ToArray();
    }

    // Parse ring and convert to WORLD PIXELS (for rendering)
    private Vector2[] ParseRingToWorld(JsonElement elem)
    {
        var points = new List<Vector2>();
        foreach (var pointElem in elem.EnumerateArray())
        {
            var coords = pointElem.EnumerateArray().ToArray();
            double lon = coords[0].GetSingle();
            double lat = coords[1].GetSingle();
            var worldPos = MapProjectionV5.LonLatToWorld(lon, lat);
            points.Add(worldPos);
        }
        return points.ToArray();
    }

    private void CreateCountryBorderLine(CountryData country)
    {
        var borderNode = new Line2D();
        borderNode.Name = $"Border_{country.Id}";
        borderNode.DefaultColor = country.BorderColor;
        borderNode.Width = 1.0f; // Base width, will be scaled by zoom in _Process
        borderNode.Antialiased = true;
        borderNode.ZIndex = 1; // Above fills
        _worldRoot.AddChild(borderNode);
        country.BorderLine = borderNode;

        var json = FileAccess.GetFileAsString(CountriesDataPath);
        var doc = JsonDocument.Parse(json);
        var countriesArray = doc.RootElement.GetProperty("countries");

        foreach (var c in countriesArray.EnumerateArray())
        {
            if (c.GetProperty("id").GetString() == country.Id)
            {
                if (c.TryGetProperty("polygons", out var polysElem))
                {
                    var allPoints = new List<Vector2>();
                    bool first = true;
                    foreach (var polyElem in polysElem.EnumerateArray())
                    {
                        if (polyElem.TryGetProperty("exterior", out var extElem))
                        {
                            // Convert to world pixels for rendering
                            var exterior = ParseRingToWorld(extElem);
                            if (exterior.Length >= 2)
                            {
                                if (!first)
                                {
                                    allPoints.Add(new Vector2(float.NaN, float.NaN)); // Break line
                                }
                                allPoints.AddRange(exterior);
                                first = false;
                            }
                        }
                    }
                    if (allPoints.Count > 0)
                    {
                        borderNode.Points = allPoints.ToArray();
                    }
                }
                break;
            }
        }
    }

    // Debug visualization drawing (Phase 3)
    private void QueueDebugRedraw()
    {
        if (_debugDrawNode != null)
        {
            _debugDrawNode.QueueRedraw();
        }
    }

    // Called by debug draw node to draw graticule, coastline, reference points
    private void DrawDebugOverlay()
    {
        if (_debugDrawNode == null) return;

        // Only draw if debug mode is on and at least one layer is enabled
        if (!_debugMode || (!_showGraticule && !_showCoastline && !_showReferencePoints))
            return;

        var draw = _debugDrawNode;
        var texW = _textureSize.X;
        var texH = _textureSize.Y;

        // Colors
        Color graticuleColor = new Color(1.0f, 1.0f, 1.0f, 0.15f);  // White, semi-transparent
        Color graticuleMajorColor = new Color(1.0f, 1.0f, 1.0f, 0.25f); // Major lines (0, ±30, ±60, ±90)
        Color coastlineColor = new Color(1.0f, 0.3f, 0.3f, 0.6f);  // Red for coastline
        Color refPointColor = new Color(1.0f, 0.0f, 0.0f, 1.0f);   // Red for reference points
        Color refLabelColor = new Color(1.0f, 1.0f, 1.0f, 0.9f);   // White for labels

        // Line widths (in world pixels, will scale with zoom)
        float graticuleWidth = 0.5f;
        float graticuleMajorWidth = 1.0f;
        float coastlineWidth = 1.0f;
        float refPointRadius = 4.0f;

        // ===== GRATICULE (30° grid) =====
        if (_showGraticule)
        {
            // Meridians (longitude lines) every 30°
            for (double lon = -180; lon <= 180; lon += 30)
            {
                bool isMajor = (lon == 0 || lon == 180 || lon == -180 || lon == 90 || lon == -90 || lon == 60 || lon == -60 || lon == 120 || lon == -120);
                var color = isMajor ? graticuleMajorColor : graticuleColor;
                float width = isMajor ? graticuleMajorWidth : graticuleWidth;

                Vector2 p1 = MapProjectionV5.LonLatToWorld(lon, 90);
                Vector2 p2 = MapProjectionV5.LonLatToWorld(lon, -90);
                draw.DrawLine(p1, p2, color, width);
            }

            // Parallels (latitude lines) every 30°
            for (double lat = -90; lat <= 90; lat += 30)
            {
                bool isMajor = (lat == 0 || lat == 90 || lat == -90 || lat == 60 || lat == -60 || lat == 30 || lat == -30);
                var color = isMajor ? graticuleMajorColor : graticuleColor;
                float width = isMajor ? graticuleMajorWidth : graticuleWidth;

                Vector2 p1 = MapProjectionV5.LonLatToWorld(-180, lat);
                Vector2 p2 = MapProjectionV5.LonLatToWorld(180, lat);
                draw.DrawLine(p1, p2, color, width);
            }
        }

        // ===== COASTLINE OVERLAY =====
        if (_showCoastline)
        {
            // Draw country borders as coastline approximation
            // This uses the country polygons from countries_lonlat.json if available
            // For now, draw a simple coast outline from the texture edge detection
            // TODO: Load actual coastline from NE coastline data
            // Placeholder: draw texture border as reference
            draw.DrawRect(new Rect2(0, 0, texW, texH), coastlineColor, false, coastlineWidth);
        }

        // ===== REFERENCE POINTS =====
        if (_showReferencePoints)
        {
            foreach (var (name, lon, lat) in ReferencePoints)
            {
                Vector2 pos = MapProjectionV5.LonLatToWorld(lon, lat);

                // Draw red dot
                draw.DrawCircle(pos, refPointRadius, refPointColor);

                // Draw crosshair
                float crossSize = refPointRadius * 2;
                draw.DrawLine(pos + new Vector2(-crossSize, 0), pos + new Vector2(crossSize, 0), refPointColor, 1.5f);
                draw.DrawLine(pos + new Vector2(0, -crossSize), pos + new Vector2(0, crossSize), refPointColor, 1.5f);

                // Draw label (name + coordinates)
                string label = $"{name} ({lon:F2}°, {lat:F2}°)";
                // Note: Drawing text in _Draw requires font - skip for now, use debug label instead
            }
        }
    }

    /// <summary>
    /// Update HUD top bar height from GameHud (called by Game.cs after HUD is created)
    /// </summary>
    public void UpdateHudTopHeight(float height)
    {
        _hudTopHeight = Mathf.Max(40f, height); // Minimum 40px
        GD.Print($"[WorldMapV5] HUD top height updated: {_hudTopHeight:F1}px");

        // Recalculate camera limits with new HUD height
        if (_textureSize.X > 0 && _camera != null)
        {
            UpdateCameraLimits();
            ClampCamera();
        }
    }

    private void SetupCamera()
    {
        // Camera limits will be set in UpdateCameraLimits() after we know viewport size
        // Don't set horizontal limits - infinite wrapping handled by ClampCamera()
        // Only vertical limits will be set in UpdateCameraLimits()
        _camera.LimitTop = 0;
        _camera.LimitBottom = (int)_textureSize.Y;
    }

    private void SetupUICanvas()
    {
        // Debug panel (toggleable with F3, default hidden, positioned below HUD)
        var debugPanel = new PanelContainer
        {
            Name = "DebugPanel",
            ThemeTypeVariation = "InnerPanel",
            MouseFilter = Control.MouseFilterEnum.Stop, // Block input when debug panel is open
            Visible = false // Default hidden, toggled with F3
        };
        debugPanel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        debugPanel.AddThemeConstantOverride("margin_top", 10); // Will be updated with HUD height
        debugPanel.AddThemeConstantOverride("margin_left", 10);
        debugPanel.AddThemeConstantOverride("margin_right", 10);
        debugPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        _uiCanvas.AddChild(debugPanel);

        var debugVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        debugVBox.AddThemeConstantOverride("separation", 4);
        debugPanel.AddChild(debugVBox);

        _debugLabel = new Label();
        _debugLabel.Name = "DebugLabel";
        _debugLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _debugLabel.VerticalAlignment = VerticalAlignment.Top;
        _debugLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _debugLabel.AddThemeColorOverride("font_color", Colors.White);
        _debugLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _debugLabel.AddThemeConstantOverride("outline_size", 2);
        _debugLabel.Text = "WorldMapV5 Debug\nWaiting for viewport...";
        debugVBox.AddChild(_debugLabel);

        // Instructions label (always visible at bottom)
        var instructions = new Label();
        instructions.Name = "Instructions";
        instructions.HorizontalAlignment = HorizontalAlignment.Left;
        instructions.VerticalAlignment = VerticalAlignment.Bottom;
        instructions.Position = new Vector2(10, -120);
        instructions.Size = new Vector2(500, 100);
        instructions.AddThemeColorOverride("font_color", Colors.LightGray);
        instructions.AddThemeColorOverride("font_outline_color", Colors.Black);
        instructions.AddThemeConstantOverride("outline_size", 1);
        instructions.Text = "Controls: Wheel=Zoom (cursor-anchored) | RMB Drag=Pan | WASD/Arrows=Pan | F3=Toggle Debug | F11=Screenshot";
        instructions.Visible = _debugMode;
        _uiCanvas.AddChild(instructions);

        // Store reference for toggle
        _debugPanel = debugPanel;

        // Update debug panel top margin after HUD height is known
        CallDeferred(nameof(UpdateDebugPanelPosition));
    }

    private void UpdateDebugPanelPosition()
    {
        if (_debugPanel != null && _hudTopHeight > 0)
        {
            _debugPanel.AddThemeConstantOverride("margin_top", (int)_hudTopHeight + 10);
        }
    }

    private PanelContainer _debugPanel;

    public override void _Process(double delta)
    {
        // Test mode execution (Phase 5)
        if (TestMode)
        {
            RunTestStep(delta);
            return;
        }

        // On first frame, calculate min zoom and center camera
        if (_minZoom == 0f && _textureSize.X > 0)
        {
            InitializeCamera();
        }

        // Handle continuous keyboard panning (WASD/Arrow keys) - HOI4 style
        HandleKeyboardPan((float)delta);

        // Update border line widths based on zoom (only when zoom changes)
        UpdateBorderWidths();

        // Handle country hover (mouse motion) - throttled
        HandleCountryHover();

        // Update debug label with performance metrics
        if (_debugMode && _debugLabel != null)
        {
            UpdateDebugLabel();
        }
    }

    private void HandleKeyboardPan(float delta)
    {
        // Continuous panning with WASD/Arrow keys (checked every frame)
        Vector2 dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
            dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
            dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
            dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
            dir.X += 1;

        if (dir != Vector2.Zero)
        {
            // Pan speed in world pixels per second (adjusted for zoom)
            float speed = PAN_SPEED_KEYBOARD / _camera.Zoom.X;
            _camera.Position += dir * speed * delta;
            ClampCamera(); // Handles wrapping
        }
    }

    private async void RunTestStep(double delta)
    {
        if (TestCases == null || TestIndex >= TestCases.Length)
        {
            GD.Print("=== Country Selection Test Complete ===");
            GD.Print("[CountrySelectTest] SONUÇ: PASS ✓");
            TestMode = false;
            GetTree().Quit();
            return;
        }

        // Prevent re-entry while awaiting
        if (_testStepRunning)
            return;
        _testStepRunning = true;

        var currentTest = TestCases[TestIndex];
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;
        var worldPos = MapProjectionV5.LonLatToWorld(currentTest.lon, currentTest.lat);
        var screenPos = MapProjectionV5.WorldToScreen(worldPos, Camera, viewportSize);

        switch (TestPhase)
        {
            case 0: // Send click event
                GD.Print($"[Test] Running test {TestIndex + 1}/{TestCases.Length}: {currentTest.name}");

                var inputEvent = new InputEventMouseButton
                {
                    Position = new Vector2((float)screenPos.X, (float)screenPos.Y),
                    ButtonIndex = MouseButton.Left,
                    Pressed = true
                };
                _UnhandledInput(new InputEventMouseButton
                {
                    Position = new Vector2((float)screenPos.X, (float)screenPos.Y),
                    ButtonIndex = MouseButton.Left,
                    Pressed = true
                });
                GD.Print($"[Test] Sending click event for {currentTest.name} at screen=({screenPos.X:F1},{screenPos.Y:F1})");
                TestPhase = 1;
                break;

            case 1: // Check result
            {
                var selectedId = SelectedCountryId;
                var selectedName = selectedId != null && Countries.TryGetValue(selectedId, out var c) ? c.Name : "None";

                var viewport2 = GetViewport();
                var viewportSize2 = viewport2.GetVisibleRect().Size;
                var worldPos2 = MapProjectionV5.LonLatToWorld(currentTest.lon, currentTest.lat);
                var screenPos2 = MapProjectionV5.WorldToScreen(worldPos2, Camera, viewportSize2);

                var selectedId2 = SelectedCountryId;
                var selectedName2 = selectedId2 != null && Countries.TryGetValue(selectedId2, out var c2) ? c2.Name : "None";

                GD.Print($"TEST {currentTest.name}: Screen=({screenPos2.X:F1},{screenPos2.Y:F1}) World=({worldPos2.X:F1},{worldPos2.Y:F1}) LonLat=({currentTest.lon:F2},{currentTest.lat:F2}) -> Selected: {selectedName2} (ID: {selectedId2 ?? "null"})");
                var selLine = _selectionOutlineNode?.Line;
                GD.Print($"[Test] Selection outline visible: {selLine?.Visible ?? false}, points: {(selLine?.Points?.Length ?? 0)}");
                GD.Print($"[Test] Hover outline visible: {_hoverOutline.Visible}, points: {(_hoverOutline.Points?.Length ?? 0)}");
                if (_selectedCountryId != null && _countries.TryGetValue(_selectedCountryId, out var selCountry))
                {
                    GD.Print($"[Test] Selected country: {selCountry.Id}, CachedExteriors count: {selCountry.CachedExteriors.Count}");
                    if (selCountry.CachedExteriors.Count > 0)
                    {
                        GD.Print($"[Test] First exterior points: {selCountry.CachedExteriors[0].Length}");
                    }
                }

                // Debug: force update selection outline
                if (_selectedCountryId != null && _countries.TryGetValue(_selectedCountryId, out var selCountry2))
                {
                    GD.Print($"[Test] Forcing UpdateCountryVisual for {selCountry2.Id}");
                    GD.Print($"[Test] Before UpdateCountryVisual - _selectionOutlineNode: {_selectionOutlineNode != null}, line: {selLine != null}, CachedExteriors count: {selCountry2.CachedExteriors.Count}");
                    UpdateCountryVisual(selCountry2);
                    GD.Print($"[Test] After UpdateCountryVisual - Selection outline visible: {selLine?.Visible ?? false}, points: {(selLine?.Points?.Length ?? 0)}");
                    if (selLine?.Points != null && selLine.Points.Length > 0)
                    {
                        GD.Print($"[Test] First point: {selLine.Points[0]}, Last point: {selLine.Points[selLine.Points.Length - 1]}");
                    }
                    else
                    {
                        GD.Print($"[Test] Selection outline points array is null or empty");
                    }
                    // Debug: check if _selectionOutline is in scene tree
                    GD.Print($"[Test] _selectionOutlineNode in tree: {_selectionOutlineNode?.IsInsideTree() ?? false}, line in tree: {selLine?.IsInsideTree() ?? false}, Parent: {selLine?.GetParent()?.Name ?? "null"}");
                }

                // Wait two frames for selection outline to render
                await ToSignal(GetTree(), "process_frame");
                await ToSignal(GetTree(), "process_frame");

                // Capture screenshot for this test (while still selected)
                CaptureTestScreenshot(currentTest.name);

                // Deselect for next test
                if (selectedId2 != null)
                {
                    _UnhandledInput(new InputEventMouseButton
                    {
                        Position = new Vector2(-100, -100),
                        ButtonIndex = MouseButton.Left,
                        Pressed = true
                    });
                    GD.Print($"[Test] Sent deselect event");
                }
                TestPhase = 2;
                break;
            }

            case 2: // Delay before next test
                TestPhase = 0;
                TestIndex++;
                break;
        }
        _testStepRunning = false;
    }

    private void CaptureTestScreenshot(string testName)
    {
        var viewport = GetViewport();
        var tex = viewport.GetTexture();
        var img = tex.GetImage();
        if (img != null && img.GetWidth() > 100 && img.GetHeight() > 100)
        {
            var dir = DirAccess.Open("user://");
            if (dir != null)
            {
                dir.MakeDirRecursive("test-logs/v5");
            }
            var path = $"user://test-logs/v5/sel_{testName}.png";
            img.SavePng(path);
            GD.Print($"[Test] Screenshot saved: {path}");
        }
    }

    private void UpdateBorderWidths()
    {
        // Only update when zoom changes significantly (avoid per-frame updates)
        if (Mathf.Abs(_camera.Zoom.X - _lastZoomForBorderUpdate) < 0.01f)
        {
            return;
        }
        _lastZoomForBorderUpdate = _camera.Zoom.X;

        // Border width in screen pixels: 0.5 to 2px based on zoom
        // At min zoom (cover): ~0.5px, at max zoom: ~2px
        float screenWidth = 0.5f + 1.5f * (_camera.Zoom.X - _minZoom) / (MAX_ZOOM - _minZoom);
        screenWidth = Mathf.Clamp(screenWidth, 0.5f, 2.0f);

        // Convert to world pixels
        float worldWidth = screenWidth / _camera.Zoom.X;

        // Update shared border lines
        foreach (var country in _countries.Values)
        {
            if (country.BorderLine != null)
            {
                country.BorderLine.Width = worldWidth;
            }
        }

        // Update selection outline width via V5SelectionOutline (2.5px on screen)
        if (_selectionOutlineNode != null)
        {
            _selectionOutlineNode.UpdateZoom(_camera.Zoom.X);
        }

        // Update hover outline width (1.5-2px on screen)
        if (_hoverOutline != null)
        {
            float hovScreenWidth = 1.5f + 0.5f * (_camera.Zoom.X - _minZoom) / (MAX_ZOOM - _minZoom);
            hovScreenWidth = Mathf.Clamp(hovScreenWidth, 1.0f, 2.0f);
            _hoverOutline.Width = hovScreenWidth / _camera.Zoom.X;
        }
    }

    private void HandleCountryHover()
    {
        var mouseScreen = GetViewport().GetMousePosition();
        var mouseWorld = ScreenToWorld(mouseScreen);

        // Quick bbox check first
        string hoveredId = null;
        float bestArea = float.MaxValue;

        // Use spatial grid for fast candidate lookup
        var candidates = GetCandidateCountriesAt(mouseWorld);
        foreach (var id in candidates)
        {
            if (_countries.TryGetValue(id, out var country))
            {
                // Point in bbox - do precise polygon test
                if (IsPointInCountry(mouseWorld, country))
                {
                    if (country.Area < bestArea)
                    {
                        bestArea = country.Area;
                        hoveredId = country.Id;
                    }
                }
            }
        }

        // Update hover state
        if (hoveredId != _hoveredCountryId)
        {
            // Clear previous hover
            if (_hoveredCountryId != null && _countries.TryGetValue(_hoveredCountryId, out var prevCountry))
            {
                prevCountry.Hovered = false;
                UpdateCountryVisual(prevCountry);
            }

            _hoveredCountryId = hoveredId;

            // Set new hover
            if (_hoveredCountryId != null && _countries.TryGetValue(_hoveredCountryId, out var newCountry))
            {
                newCountry.Hovered = true;
                UpdateCountryVisual(newCountry);
            }
        }
    }

    // Get candidate countries from spatial grid for fast hover detection
    private List<string> GetCandidateCountriesAt(Vector2 worldPos)
    {
        if (!_gridBuilt) return new List<string>();

        int cellX = (int)(worldPos.X / GridCellSize);
        int cellY = (int)(worldPos.Y / GridCellSize);
        long key = ((long)cellX << 32) | (uint)cellY;

        if (_countryGrid.TryGetValue(key, out var candidates))
        {
            return candidates;
        }
        return new List<string>();
    }


        private bool IsPointInCountry(Vector2 point, CountryData country)
    {
        // Use cached polygons for fast hit testing
        if (!_cachedPolygons.TryGetValue(country.Id, out var cachedExteriors))
        {
            return false;
        }

        // Convert world pixel point to lon/lat for polygon test
        // Polygons in JSON are in lon/lat coordinates
        // Use WorldToLonLat with bounds checking
        var (lon, lat, inBounds) = WorldToLonLat(point);
        if (!inBounds) return false;

        var lonLatPoint = new Vector2((float)lon, (float)lat);

        foreach (var exterior in country.CachedExteriors)
        {
            if (Geometry2D.IsPointInPolygon(lonLatPoint, exterior))
            {
                // Check holes (if any cached)
                // For now, assume no holes or holes are separate cached entries
                return true;
            }
        }
        return false;
    }

            private void UpdateCountryVisual(CountryData country)
    {
        GD.Print($"[UpdateCountryVisual] Called for {country.Id}, Selected={country.Selected}, Hovered={country.Hovered}, _selectionOutlineNode={_selectionOutlineNode != null}, CachedExteriors={country.CachedExteriors?.Count ?? 0}");
        // Update fill color based on state
        // Note: FillMesh may be null since we use Polygon2D directly, so don't return early

        Color targetColor = country.FillColor;
        if (country.Selected)
        {
            // Selected: brighter, more opaque
            targetColor = new Color(
                Mathf.Min(1.0f, country.FillColor.R * 1.3f),
                Mathf.Min(1.0f, country.FillColor.G * 1.3f),
                Mathf.Min(1.0f, country.FillColor.B * 1.3f),
                0.7f
            );
        }
        else if (country.Hovered)
        {
            // Hovered: slightly brighter
            targetColor = new Color(
                Mathf.Min(1.0f, country.FillColor.R * 1.15f),
                Mathf.Min(1.0f, country.FillColor.G * 1.15f),
                Mathf.Min(1.0f, country.FillColor.B * 1.15f),
                0.6f
            );
        }

        // Apply to all fill Polygon2D children
        var fillNode = _worldRoot.GetNodeOrNull<Node2D>($"Fill_{country.Id}");
        if (fillNode != null)
        {
            foreach (var child in fillNode.GetChildren())
            {
                if (child is Polygon2D poly)
                {
                    poly.Color = targetColor;
                }
            }
        }

        // Update selection outline via V5SelectionOutline (handles all rings including islands)
            if (_selectionOutlineNode != null)
            {
                _selectionOutlineNode.UpdateSelection(country.Selected ? country.Id : null);
            }

            // Update hover outline
            if (_hoverOutline != null)
            {
                if (country.Hovered && !country.Selected)
                {
                    var points = new List<Vector2>();
                    bool first = true;
                    foreach (var exterior in country.CachedExteriors)
                    {
                        if (exterior.Length >= 2)
                        {
                            if (!first)
                            {
                                points.Add(new Vector2(float.NaN, float.NaN)); // Break line
                            }
                            // Convert lon/lat to world pixels
                            foreach (var pt in exterior)
                            {
                                points.Add(MapProjectionV5.LonLatToWorld(pt.X, pt.Y));
                            }
                            first = false;
                        }
                    }
                    if (points.Count > 0)
                    {
                        _hoverOutline.Points = points.ToArray();
                        _hoverOutline.Visible = true;
                    }
                    else
                    {
                        _hoverOutline.Visible = false;
                    }
                }
                else
                {
                    _hoverOutline.Visible = false;
                }
            }
            else
            {
                _hoverOutline.Visible = false;
            }

            // Update shared border line color/width (only for non-selected/non-hovered)
            if (country.BorderLine != null)
            {
                if (!country.Selected && !country.Hovered)
                {
                    country.BorderLine.DefaultColor = country.BorderColor; // Default dark
                }
            }
        }

    private void InitializeCamera()
    {
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        // Calculate cover zoom: VERTICAL only (horizontal wraps infinitely)
        // Account for HUD top bar reducing visible map area
        float effectiveViewportHeight = viewportSize.Y - _hudTopHeight;
        float zoomY = effectiveViewportHeight / _textureSize.Y;
        _minZoom = Math.Max(zoomY, MIN_ZOOM_CLAMP); // Don't go below absolute minimum

        GD.Print($"WorldMapV5: Viewport={viewportSize}, EffectiveH={effectiveViewportHeight:F1} (HUD={_hudTopHeight:F1}), Texture={_textureSize}, MinZoom(vertical)={_minZoom:F4} (clamp={MIN_ZOOM_CLAMP})");

        // Set camera zoom to cover
        _camera.Zoom = new Vector2(_minZoom, _minZoom);

        // Center camera on world
        _camera.Position = _textureSize / 2f;

        // Update camera limits for this zoom level (infinite horizontal wrapping)
        UpdateCameraLimits();

        // Apply shot-specific camera settings
        ApplyShotCameraSettings();

        // Ensure camera is current
        _camera.MakeCurrent();
    }

    public void UpdateCameraLimits()
    {
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        // Visible world rect at current zoom
        Vector2 visibleWorld = viewportSize / _camera.Zoom.X;

        // Camera position is center of visible area
        float halfW = visibleWorld.X / 2f;
        float halfH = visibleWorld.Y / 2f;

        // VERTICAL LIMITS (no wrapping vertically - clamp to world bounds)
        // Account for HUD top bar offset
        float effectiveViewportHeight = viewportSize.Y - _hudTopHeight;
        float effectiveHalfH = (effectiveViewportHeight / _camera.Zoom.X) / 2f;

        float limitTop, limitBottom;
        if (visibleWorld.Y >= _textureSize.Y)
        {
            limitTop = limitBottom = _textureSize.Y / 2f;
        }
        else
        {
            // Camera center Y must stay within [effectiveHalfH, textureHeight - effectiveHalfH]
            // But with HUD at top, the top limit is slightly lower
            limitTop = effectiveHalfH + _hudTopHeight / _camera.Zoom.X;
            limitBottom = _textureSize.Y - effectiveHalfH;
        }

        // HORIZONTAL: Infinite wrapping - NO LIMITS (camera can go anywhere)
        // We handle wrapping in ClampCamera() by shifting camera position
        // Don't set horizontal limits - leave them disabled (default 0 = no limit)
        // Only set vertical limits
        _camera.LimitTop = (int)Mathf.Ceil(limitTop);
        _camera.LimitBottom = (int)Mathf.Floor(limitBottom);
        // Don't set LimitLeft/LimitRight - leave at default 0 (disabled) for infinite horizontal

        GD.Print($"[CamLimits] VisibleWorld={visibleWorld}, Tex={_textureSize}, Zoom={_camera.Zoom.X:F4}, HalfH={halfH:F1}, EffHalfH={effectiveHalfH:F1}, Vertical: T{limitTop:F1} B{limitBottom:F1} (range={limitBottom-limitTop:F1}), Horizontal: INFINITE WRAPPING");
    }

    public void ClampCamera()
    {
        Vector2 pos = _camera.Position;

        // Horizontal wrapping (HOI4 style): wrap camera position seamlessly
        float halfVisibleW = GetViewport().GetVisibleRect().Size.X / _camera.Zoom.X / 2f;
        float worldWrapMin = -halfVisibleW;      // Left edge where we wrap right
        float worldWrapMax = WORLD_WIDTH + halfVisibleW; // Right edge where we wrap left

        // Wrap camera X position
        if (pos.X < worldWrapMin)
        {
            pos.X += WORLD_WIDTH;
            GD.Print($"[Wrap] Camera wrapped RIGHT: {pos.X - WORLD_WIDTH:F1} -> {pos.X:F1}");
            UpdateBackgroundPositions();
        }
        else if (pos.X > worldWrapMax)
        {
            pos.X -= WORLD_WIDTH;
            GD.Print($"[Wrap] Camera wrapped LEFT: {pos.X + WORLD_WIDTH:F1} -> {pos.X:F1}");
            UpdateBackgroundPositions();
        }

        // Vertical clamping (no wrapping) - use camera limits directly
        float oldY = pos.Y;
        pos.Y = Mathf.Clamp(pos.Y, _camera.LimitTop, _camera.LimitBottom);
        if (Mathf.Abs(pos.Y - oldY) > 0.1f)
        {
            GD.Print($"[ClampY] Camera Y clamped: {oldY:F1} -> {pos.Y:F1} (limits T{_camera.LimitTop} B{_camera.LimitBottom})");
        }

        _camera.Position = pos;
    }

    private void UpdateBackgroundPositions()
    {
        // Keep 3 backgrounds centered on camera for seamless wrapping
        float camX = _camera.Position.X;

        // Center background on camera's "tile"
        float centerTileX = Mathf.Floor(camX / WORLD_WIDTH) * WORLD_WIDTH;

        _bgCenter.Position = new Vector2(centerTileX, 0);
        _bgLeft.Position = new Vector2(centerTileX - WORLD_WIDTH, 0);
        _bgRight.Position = new Vector2(centerTileX + WORLD_WIDTH, 0);
    }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        // Convert screen position to world using camera transform
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        // Screen to world: camera position + (screen - viewport_center) / zoom
        // Note: screenPos is in viewport coordinates (already accounts for CanvasLayer offsets)
        Vector2 worldPos = _camera.Position + (screenPos - viewportSize / 2f) / _camera.Zoom.X;

        // Wrap world X coordinate to [0, WORLD_WIDTH) for consistent hit-testing
        worldPos.X = Mathf.PosMod(worldPos.X, WORLD_WIDTH);

        return worldPos;
    }

    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        // World to screen: (world - camera_pos) * zoom + viewport_center
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;
        return (worldPos - _camera.Position) * _camera.Zoom.X + viewportSize / 2f;
    }

    /// <summary>
    /// Check if mouse is over any GUI element (HUD, panels, minimap, debug panel)
    /// Returns true if map input should be BLOCKED (GUI has focus)
    /// </summary>
    private bool IsMouseOverGui()
    {
        // Check if any Control is under mouse (excluding the map's own debug/instruction labels)
        var hovered = GetViewport().GuiGetHoveredControl();
        if (hovered != null)
        {
            // Allow map input if hovered control is our debug/instruction labels (they're just info)
            if (hovered.Name == "DebugLabel" || hovered.Name == "Instructions" || hovered.Name == "DebugPanel")
                return false;
            // Block map input for all other GUI elements (HUD, panels, minimap, etc.)
            return true;
        }
        return false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Block map input if mouse is over GUI (HUD, panels, minimap, debug panel)
        if (IsMouseOverGui())
            return;

        // UI Controls consume input first (MouseFilter.Stop on panels)
        // Only handle input if not consumed by UI

        // Wheel zoom (cursor-anchored)
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
        {
            HandleWheelZoom(1f / ZOOM_SPEED, mb.Position);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton mb2 && mb2.ButtonIndex == MouseButton.WheelDown && mb2.Pressed)
        {
            HandleWheelZoom(ZOOM_SPEED, mb2.Position);
            GetViewport().SetInputAsHandled();
        }

        // RMB drag pan - responsive continuous panning
        if (@event is InputEventMouseButton mb3 && mb3.ButtonIndex == MouseButton.Right)
        {
            if (mb3.Pressed)
            {
                _dragging = true;
                _dragStarted = false;
                _lastMousePos = mb3.Position;
                GetViewport().SetInputAsHandled();
            }
            else
            {
                _dragging = false;
                _dragStarted = false;
            }
        }

        if (_dragging && @event is InputEventMouseMotion mm)
        {
            Vector2 delta = mm.Position - _lastMousePos;

            // On first movement, require threshold to distinguish click from drag
            // After drag starts, allow any movement for smooth panning
            if (!_dragStarted)
            {
                if (delta.Length() > DRAG_THRESHOLD)
                {
                    _dragStarted = true;
                }
                else
                {
                    GetViewport().SetInputAsHandled();
                    return; // Wait for threshold to be exceeded
                }
            }

            // Convert screen delta to world delta (inverse of zoom) - continuous panning
            Vector2 worldDelta = -delta / _camera.Zoom.X;
            _camera.Position += worldDelta;
            ClampCamera();
            _lastMousePos = mm.Position;
            GetViewport().SetInputAsHandled();
        }

        // LMB click - country selection
        if (@event is InputEventMouseButton mb4 && mb4.ButtonIndex == MouseButton.Left && mb4.Pressed)
        {
            var mouseScreen = mb4.Position;
            var mouseWorld = ScreenToWorld(mouseScreen); // Handles wrapping

            // Find clicked country
            string clickedId = null;
            float bestArea = float.MaxValue;

            foreach (var kvp in _countries)
            {
                var country = kvp.Value;
                if (IsPointInCountry(mouseWorld, country))
                {
                    if (country.Area < bestArea)
                    {
                        bestArea = country.Area;
                        clickedId = country.Id;
                    }
                }
            }

            // Update selection
            if (clickedId != _selectedCountryId)
            {
                // Clear previous selection
                if (_selectedCountryId != null && _countries.TryGetValue(_selectedCountryId, out var prevCountry))
                {
                    prevCountry.Selected = false;
                    UpdateCountryVisual(prevCountry);
                }

                _selectedCountryId = clickedId;

                // Set new selection
                if (_selectedCountryId != null && _countries.TryGetValue(_selectedCountryId, out var newCountry))
                {
                    newCountry.Selected = true;
                    UpdateCountryVisual(newCountry);

                    // Emit signal for UI integration
                    EmitSignal(SignalName.CountrySelected, newCountry.Id, newCountry.Name);
                    GD.Print($"WorldMapV5: Country selected: {newCountry.Name} ({newCountry.Id})");
                }
                else
                {
                    // Clicked on ocean/empty - deselect
                    _selectedCountryId = null;
                    EmitSignal(SignalName.CountryDeselected);
                    GD.Print("WorldMapV5: Country deselected (clicked on ocean)");
                }
            }
            else
            {
                // Clicked on same country - deselect
                _selectedCountryId = null;
                EmitSignal(SignalName.CountryDeselected);
                GD.Print("WorldMapV5: Country deselected (clicked on same country)");
            }

            GetViewport().SetInputAsHandled();
        }

        // Keyboard pan (WASD / Arrow keys) - with wrapping
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            Vector2 dir = Vector2.Zero;
            switch (key.Keycode)
            {
                case Key.W: case Key.Up: dir.Y = -1; break;
                case Key.S: case Key.Down: dir.Y = 1; break;
                case Key.A: case Key.Left: dir.X = -1; break;
                case Key.D: case Key.Right: dir.X = 1; break;
            }

            if (dir != Vector2.Zero)
            {
                // Pan speed in world pixels per second (adjusted for zoom)
                float speed = PAN_SPEED_KEYBOARD / _camera.Zoom.X;
                _camera.Position += dir * speed * (float)GetProcessDeltaTime();
                ClampCamera(); // Handles wrapping
                GetViewport().SetInputAsHandled();
            }

            // F3: Toggle debug mode (and debug panel)
            if (key.Keycode == Key.F3)
            {
                _debugMode = !_debugMode;
                // Toggle debug panel visibility
                if (_debugPanel != null)
                {
                    _debugPanel.Visible = _debugMode;
                }
                if (_debugLabel != null) _debugLabel.Visible = _debugMode;
                var instructions = _uiCanvas.GetNodeOrNull<Label>("Instructions");
                if (instructions != null) instructions.Visible = _debugMode;
                QueueDebugRedraw();
                GD.Print($"[WorldMapV5] Debug mode: {(_debugMode ? "ON" : "OFF")}");
                GetViewport().SetInputAsHandled();
            }

            // G: Toggle graticule
            if (key.Keycode == Key.G)
            {
                _showGraticule = !_showGraticule;
                GD.Print($"Debug: Graticule = {_showGraticule}");
                QueueDebugRedraw();
                GetViewport().SetInputAsHandled();
            }

            // C: Toggle coastline
            if (key.Keycode == Key.C)
            {
                _showCoastline = !_showCoastline;
                GD.Print($"Debug: Coastline = {_showCoastline}");
                QueueDebugRedraw();
                GetViewport().SetInputAsHandled();
            }

            // R: Toggle reference points
            if (key.Keycode == Key.R)
            {
                _showReferencePoints = !_showReferencePoints;
                GD.Print($"Debug: Reference Points = {_showReferencePoints}");
                QueueDebugRedraw();
                GetViewport().SetInputAsHandled();
            }

            // F11: Screenshot (for manual testing)
            if (key.Keycode == Key.F11)
            {
                RequestScreenshot("manual_f11");
                GetViewport().SetInputAsHandled();
            }

            // Note: WASD/Arrow key panning is now handled in _Process() for continuous movement
            // using Input.IsKeyPressed() for smooth HOI4-style panning
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMSizeChanged)
        {
            CallDeferred(nameof(OnViewportResized));
        }
    }

    /// <summary>
    /// Clears the current country selection (called from Game via X/ESC on panels).
    /// </summary>
    public void ClearSelection()
    {
        if (_selectedCountryId != null && _countries.TryGetValue(_selectedCountryId, out var prevCountry))
        {
            prevCountry.Selected = false;
            UpdateCountryVisual(prevCountry);
        }
        _selectedCountryId = null;
        EmitSignal(SignalName.CountryDeselected);
        GD.Print("WorldMapV5: Country deselected (ClearSelection called)");
    }

    private void HandleWheelZoom(float zoomFactor, Vector2 screenPos)
    {
        // Get world position under cursor BEFORE zoom
        Vector2 worldBefore = ScreenToWorld(screenPos);

        // Apply zoom
        float newZoom = _camera.Zoom.X * zoomFactor;
        newZoom = Mathf.Clamp(newZoom, _minZoom, MAX_ZOOM);
        _camera.Zoom = new Vector2(newZoom, newZoom);

        // Update camera limits for new zoom
        UpdateCameraLimits();

        // Get world position under cursor AFTER zoom (same screen pos)
        Vector2 worldAfter = ScreenToWorld(screenPos);

        // Adjust camera so the same world point stays under cursor
        _camera.Position += worldBefore - worldAfter;

        // Clamp
        ClampCamera();
    }

    private void OnViewportResized()
    {
        if (_textureSize.X > 0)
        {
            // Recalculate min zoom (vertical only - horizontal wraps)
            var viewport = GetViewport();
            var viewportSize = viewport.GetVisibleRect().Size;

            float effectiveViewportHeight = viewportSize.Y - _hudTopHeight;
            float zoomY = effectiveViewportHeight / _textureSize.Y;
            _minZoom = Math.Max(zoomY, MIN_ZOOM_CLAMP);

            // Clamp current zoom to new min
            if (_camera.Zoom.X < _minZoom)
            {
                _camera.Zoom = new Vector2(_minZoom, _minZoom);
            }

            UpdateCameraLimits();
            ClampCamera();

            // Ensure camera is current after resize
            _camera.MakeCurrent();

            GD.Print($"WorldMapV5: Resized - Viewport={viewportSize}, EffH={effectiveViewportHeight:F1}, MinZoom={_minZoom:F4}, Zoom={_camera.Zoom.X:F4}");
        }
    }

    private void UpdateDebugLabel()
    {
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;
        var mouseScreen = GetViewport().GetMousePosition();
        var mouseWorld = ScreenToWorld(mouseScreen);
        var (lon, lat, inBounds) = WorldToLonLat(mouseWorld);

        // Performance metrics
        uint fps = (uint)(1.0 / GetProcessDeltaTime());
        ulong frameTime = (ulong)(GetProcessDeltaTime() * 1000);
        // Note: RenderingServer.GetRenderingInfo API varies by Godot version
        int objectsInFrame = _countries.Count; // Approximate with country count
        var memUsage = OS.GetStaticMemoryUsage();

        // Camera vertical clamp info
        float halfViewH = (viewportSize.Y / _camera.Zoom.X) / 2f;
        float minCamY = _camera.LimitTop;
        float maxCamY = _camera.LimitBottom;
        float mapCoverageH = _textureSize.Y * _camera.Zoom.X;

        _debugLabel.Text = $@"WorldMapV5 Debug
Zoom: {_camera.Zoom.X:F4} (min: {_minZoom:F4}, max: {MAX_ZOOM})
Camera Pos: ({_camera.Position.X:F1}, {_camera.Position.Y:F1})
Camera Limits: L{_camera.LimitLeft} T{_camera.LimitTop} R{_camera.LimitRight} B{_camera.LimitBottom}
Cam Y Range: [{minCamY:F1} .. {maxCamY:F1}] (halfViewH={halfViewH:F1})
Viewport: {viewportSize.X}x{viewportSize.Y} (map coverage H={mapCoverageH:F1} >= {viewportSize.Y}? {mapCoverageH >= viewportSize.Y})
Texture: {_textureSize.X}x{_textureSize.Y}
Mouse Screen: ({mouseScreen.X:F1}, {mouseScreen.Y:F1})
Mouse World: ({mouseWorld.X:F1}, {mouseWorld.Y:F1}) inBounds={inBounds}
Mouse Lon/Lat: ({lon:F4}°, {lat:F4}°)
Dragging: {_dragging}
FPS: {fps} | Frame: {frameTime}ms | Objects: {objectsInFrame} | Mem: {memUsage / 1024 / 1024}MB
Countries: {_countries.Count} | Grid Cells: {_countryGrid.Count}
Debug: F3 | Screenshot: F11 | Grid: G | Coast: C | Ref: R";
    }

    /// <summary>
    /// Convert world space coordinates to longitude/latitude.
    /// Based on texture being full-world equirectangular: 360° x 180°
    /// Returns clamped lat/lon and a flag if position was out of bounds.
    /// </summary>
    public static (double lon, double lat, bool inBounds) WorldToLonLat(Vector2 worldPos, Vector2? textureSize = null)
    {
        Vector2 texSize = textureSize ?? Instance?._textureSize ?? new Vector2(10800, 5400);

        // Wrap X for longitude calculation (horizontal wrapping)
        double wrappedX = Mathf.PosMod(worldPos.X, texSize.X);
        double lon = -180.0 + (wrappedX / texSize.X) * 360.0;

        // Clamp Y for latitude calculation (no vertical wrapping)
        double clampedY = Mathf.Clamp(worldPos.Y, 0, texSize.Y);
        bool inBounds = (worldPos.Y >= 0 && worldPos.Y <= texSize.Y);
        double lat = 90.0 - (clampedY / texSize.Y) * 180.0;

        // Clamp to valid ranges
        lon = Mathf.Clamp(lon, -180.0, 180.0);
        lat = Mathf.Clamp(lat, -90.0, 90.0);

        return (lon, lat, inBounds);
    }

    /// <summary>
    /// Convert longitude/latitude to world space coordinates.
    /// </summary>
    public static Vector2 LonLatToWorld(double lon, double lat, Vector2? textureSize = null)
    {
        Vector2 texSize = textureSize ?? Instance?._textureSize ?? new Vector2(10800, 5400);
        float x = (float)((lon + 180.0) / 360.0 * texSize.X);
        float y = (float)((90.0 - lat) / 180.0 * texSize.Y);
        return new Vector2(x, y);
    }

    public void EnsureCameraCurrent()
    {
        if (!_camera.IsCurrent())
            _camera.MakeCurrent();
        GD.Print("WorldMapV5: Camera set as current");
    }

    // Screenshot support for --v5-shot
    public void LogCameraState(string shotName)
    {
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;
        GD.Print($"[V5Shot:{shotName}] Zoom={_camera.Zoom.X:F4} Pos=({_camera.Position.X:F1},{_camera.Position.Y:F1}) Viewport={viewportSize.X}x{viewportSize.Y}");
    }

    public void RequestScreenshot(string shotName)
    {
        // This is called from Game.SetupV5Shot via CallDeferred
        LogCameraState(shotName);
    }

    /// <summary>
    /// Gets the country ID at the given world position (for MiniMapPanel compatibility).
    /// </summary>
    public string GetCountryAt(Vector2 worldPos)
    {
        // Convert world position to lon/lat
        var (lon, lat, inBounds) = WorldToLonLat(worldPos);
        if (!inBounds) return null;
        var lonLatPoint = new Vector2((float)lon, (float)lat);

        // Check each country's cached exteriors
        foreach (var kvp in _countries)
        {
            var country = kvp.Value;
            foreach (var exterior in country.CachedExteriors)
            {
                if (Geometry2D.IsPointInPolygon(lonLatPoint, exterior))
                {
                    return country.Id;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// DebugDrawNode - Handles drawing of graticule, coastline, reference points in world space.
    /// Attached to _debugDrawNode under WorldRoot.
    /// </summary>
    private partial class DebugDrawNode : Node2D
    {
        private WorldMapV5 _parent;

        public void Initialize(WorldMapV5 parent)
        {
            _parent = parent;
        }

        public override void _Draw()
        {
            if (_parent == null) return;
            _parent.DrawDebugOverlay();
        }
    }
}