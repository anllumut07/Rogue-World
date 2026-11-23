using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;

/// <summary>
/// MapView - New map system with pixel-perfect hit-test via ID map.
/// Architecture:
/// - Single Camera2D, single map texture (equirectangular projection)
/// - Horizontal infinite wrap (3 texture copies), vertical clamp
/// - Pixel-perfect hit-test via ID map (Image with unique color per country)
/// - HUD/menu/debug on separate CanvasLayers
/// - No SubViewport, no province grid hit-test
/// </summary>
public partial class MapView : Node2D
{
    [Signal] public delegate void CountrySelectedEventHandler(string countryId, string countryName);
    [Signal] public delegate void CountryHoveredEventHandler(string countryId, string countryName);
    [Signal] public delegate void CountryDeselectedEventHandler();

    public static MapView Instance { get; private set; }

    // ============================================================
    // CONSTANTS & CONFIGURATION
    // ============================================================

    // Map projection: equirectangular, 360° x 180°
    // Texture dimensions (must match ID map and terrain texture)
    public const float MAP_WIDTH = 5632f;
    public const float MAP_HEIGHT = 2048f;

    // Camera settings
    private const float MIN_ZOOM_BASE = 0.1f;
    private const float MAX_ZOOM = 8f;
    private const float ZOOM_SPEED = 1.15f;
    private const float PAN_SPEED = 1000f;

    // Wrap settings
    private const float WRAP_MARGIN = 50f; // Extra margin for smooth wrapping

    // HUD height (for camera vertical clamp calculation)
    private float _hudTopHeight = 64f;

    // ============================================================
    // NODE REFERENCES
    // ============================================================

    private Node2D _worldRoot;
    private Node2D _mapLayer;        // Map texture + overlays (wrap-enabled)
    private Node2D _overlayLayer;    // Province borders, rivers (same world space)
    private Camera2D _camera;
    private CanvasLayer _uiCanvas;   // Debug overlay only

    // Map texture (3 copies for horizontal wrap)
    private Sprite2D _mapCenter, _mapLeft, _mapRight;

    // ID map for pixel-perfect hit-test
    private Image _idMapImage;           // CPU-side ID map (equirectangular, same as map texture)
    private Texture2D _idMapTexture;     // GPU-side for debug visualization
    private Dictionary<Color, string> _colorToCountryId = new();
    private Dictionary<string, Color> _countryIdToColor = new();

    // Country data for rendering/selection
    private Dictionary<string, CountryData> _countries = new();

    // Overlay rendering
    private bool _showBorders = true;
    private bool _showProvinceGrid = false;
    private Node2D _borderDrawer;

    // Camera state
    private float _minZoom = MIN_ZOOM_BASE;
    private Vector2 _lastMousePos;
    private bool _dragging = false;
    private bool _isMouseOverHUD = false;

    // Hover/selection state
    private string _hoveredCountryId = null;
    private string _selectedCountryId = null;

    // Debug
    private Label _debugLabel;
    private bool _debugMode = false;

    // Test mode
    public bool TestMode { get; set; } = false;
    private string[] _testCountries = { "TUR", "FRA", "JPN", "EGY", "USA", "AUS", "OCEAN" };
    private double[] _testLons = { 28.97, 2.35, 139.69, 31.24, -74.01, 151.21, -100.0 };
    private double[] _testLats = { 41.01, 48.86, 35.69, 30.04, 40.71, -33.87, 0.0 };
    private int _testIndex = 0;
    private int _testZoomLevel = 0; // 0=min, 1=mid, 2=max
    private float _testTimer = 0f;

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[MapView] Ready - New map system with pixel-perfect hit-test");

        BuildSceneTree();
        SetupCamera();
        SetupUICanvas();

        // Load ID map and country data
        LoadIdMapAndCountries();

        // Load map texture
        LoadMapTexture();

        // Start test if flag provided
        var args = OS.GetCmdlineUserArgs();
        if (Array.Exists(args, a => a == "--map-click-test"))
        {
            TestMode = true;
            GD.Print("[MapView] --map-click-test enabled");
        }
    }

    /// <summary>
    /// Initialize MapView asynchronously (called from Game.cs)
    /// </summary>
    public async Task InitializeAsync()
    {
        GD.Print("[MapView] Initializing MapView...");
        // Map is already built in _Ready, just verify everything is loaded
        if (_idMapImage == null)
        {
            GD.PrintErr("[MapView] ID map not loaded!");
        }
        else
        {
            GD.Print($"[MapView] ID map ready: {_idMapImage.GetWidth()}x{_idMapImage.GetHeight()}");
        }

        if (_countries.Count == 0)
        {
            GD.PrintErr("[MapView] No countries loaded!");
        }
        else
        {
            GD.Print($"[MapView] {_countries.Count} countries ready");
        }

        await Task.Delay(1); // Allow one frame for UI to settle
        GD.Print("[MapView] Initialization complete");
    }

    private void BuildSceneTree()
    {
        _worldRoot = new Node2D { Name = "WorldRoot" };
        AddChild(_worldRoot);

        // Map layer (wraps horizontally)
        _mapLayer = new Node2D { Name = "MapLayer", ZIndex = -10 };
        _worldRoot.AddChild(_mapLayer);

        // Overlay layer (borders, rivers - same world space, no wrap)
        _overlayLayer = new Node2D { Name = "OverlayLayer", ZIndex = 5 };
        _worldRoot.AddChild(_overlayLayer);

        // Map texture sprites (3 copies for horizontal wrap)
        _mapCenter = CreateMapSprite("MapCenter", Vector2.Zero);
        _mapLeft = CreateMapSprite("MapLeft", new Vector2(-MAP_WIDTH, 0));
        _mapRight = CreateMapSprite("MapRight", new Vector2(MAP_WIDTH, 0));

        // Border/province overlay drawer
        _borderDrawer = new Node2D { Name = "BorderDrawer" };
        _overlayLayer.AddChild(_borderDrawer);

        // Camera
        _camera = new Camera2D
        {
            Name = "MapCamera",
            Zoom = Vector2.One
        };
        _worldRoot.AddChild(_camera);
        _camera.MakeCurrent();

        // UI Canvas (debug only)
        _uiCanvas = new CanvasLayer { Name = "UICanvas", Layer = 100 };
        AddChild(_uiCanvas);
    }

    private Sprite2D CreateMapSprite(string name, Vector2 position)
    {
        var sprite = new Sprite2D
        {
            Name = name,
            Centered = false,
            Position = position,
            Scale = Vector2.One,
            ZIndex = -5
        };
        _mapLayer.AddChild(sprite);
        return sprite;
    }

    private void SetupCamera()
    {
        _camera.Zoom = Vector2.One;
    }

    private void SetupUICanvas()
    {
        _debugLabel = new Label
        {
            Name = "DebugLabel",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Position = new Vector2(10, 10),
            Size = new Vector2(450, 400),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false
        };
        _debugLabel.AddThemeColorOverride("font_color", Colors.White);
        _debugLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _debugLabel.AddThemeConstantOverride("outline_size", 2);
        _uiCanvas.AddChild(_debugLabel);

        // Toggle debug with F3
    }

    // ============================================================
    // ID MAP LOADING (Step A)
    // ============================================================

    private void LoadIdMapAndCountries()
    {
        GD.Print("[MapView] Loading ID map and country data...");

        string idMapPath = "user://map_id_map.png";
        string countryDataPath = "res://Data/map/countries.json";

        // Try load cached ID map
        if (FileAccess.FileExists(idMapPath))
        {
            GD.Print("[MapView] Loading cached ID map...");
            _idMapImage = Image.LoadFromFile(idMapPath);
            if (_idMapImage != null)
            {
                GD.Print($"[MapView] ID map loaded: {_idMapImage.GetWidth()}x{_idMapImage.GetHeight()}");
                BuildColorToCountryMap();
                LoadCountryData(countryDataPath);
                return;
            }
        }

        // Generate ID map from country polygons
        GD.Print("[MapView] Generating ID map from country polygons...");
        GenerateIdMapFromPolygons(countryDataPath);
    }

    private void BuildColorToCountryMap()
    {
        _colorToCountryId.Clear();
        _countryIdToColor.Clear();

        if (_idMapImage == null) return;

        // Scan entire ID map and build color -> countryId mapping
        var colorCounts = new Dictionary<Color, int>();

        for (int y = 0; y < _idMapImage.GetHeight(); y++)
        {
            for (int x = 0; x < _idMapImage.GetWidth(); x++)
            {
                Color c = _idMapImage.GetPixel(x, y);
                // Skip ocean (black/transparent) and near-black
                if (c.R < 0.02f && c.G < 0.02f && c.B < 0.02f) continue;
                if (c.A < 0.5f) continue;

                // Quantize color to avoid floating point issues
                Color qc = new Color(
                    Mathf.Round(c.R * 255) / 255f,
                    Mathf.Round(c.G * 255) / 255f,
                    Mathf.Round(c.B * 255) / 255f,
                    1f
                );

                if (!colorCounts.ContainsKey(qc))
                    colorCounts[qc] = 0;
                colorCounts[qc]++;
            }
        }

        GD.Print($"[MapView] Found {colorCounts.Count} unique colors in ID map");

        // Load country data to map colors to country IDs
        string countryDataPath = "res://Data/map/countries.json";
        if (FileAccess.FileExists(countryDataPath))
        {
            var json = FileAccess.GetFileAsString(countryDataPath);
            var countriesData = Json.ParseString(json).AsGodotArray();

            foreach (var countryVariant in countriesData)
            {
                var country = countryVariant.AsGodotDictionary();
                string id = country["id"].AsString();
                string name = country["name"].AsString();

                // Assign a unique color to each country
                Color countryColor = GenerateCountryColor(id);
                _countryIdToColor[id] = countryColor;
                _colorToCountryId[QuantizeColor(countryColor)] = id;
            }
        }

        GD.Print($"[MapView] Mapped {_colorToCountryId.Count} colors to country IDs");
    }

    private Color GenerateCountryColor(string countryId)
    {
        // Deterministic color from country ID hash
        uint hash = 0;
        foreach (char c in countryId) hash = hash * 31 + (uint)c;

        // Xorshift for color (use ulong for intermediate to avoid overflow)
        ulong x = hash;
        x ^= x >> 12; x ^= x << 25; x ^= x >> 27; x *= 0x2545F4914F6CDD1DUL;
        float r = ((x >> 8) & 0xFF) / 255f;
        x ^= x >> 12; x ^= x << 25; x ^= x >> 27; x *= 0x2545F4914F6CDD1DUL;
        float g = ((x >> 8) & 0xFF) / 255f;
        x ^= x >> 12; x ^= x << 25; x ^= x >> 27; x *= 0x2545F4914F6CDD1DUL;
        float b = ((x >> 8) & 0xFF) / 255f;

        // Ensure not too dark (ocean is black)
        r = 0.2f + r * 0.8f;
        g = 0.2f + g * 0.8f;
        b = 0.2f + b * 0.8f;

        return new Color(r, g, b, 1f);
    }

    private Color QuantizeColor(Color c)
    {
        return new Color(
            Mathf.Round(c.R * 255) / 255f,
            Mathf.Round(c.G * 255) / 255f,
            Mathf.Round(c.B * 255) / 255f,
            1f
        );
    }

    private void GenerateIdMapFromPolygons(string countryDataPath)
    {
        if (!FileAccess.FileExists(countryDataPath))
        {
            GD.PrintErr($"[MapView] Country data not found: {countryDataPath}");
            return;
        }

        var json = FileAccess.GetFileAsString(countryDataPath);
        var countriesData = Json.ParseString(json).AsGodotArray();

        // Create ID map image
        _idMapImage = Image.CreateEmpty((int)MAP_WIDTH, (int)MAP_HEIGHT, false, Image.Format.Rgba8);

        // Fill with ocean color (black)
        var oceanColor = new Color(0, 0, 0, 1f);
        for (int y = 0; y < MAP_HEIGHT; y++)
        {
            for (int x = 0; x < MAP_WIDTH; x++)
            {
                _idMapImage.SetPixel(x, y, oceanColor);
            }
        }

        // Assign colors to countries and draw polygons
        foreach (var countryVariant in countriesData)
        {
            var country = countryVariant.AsGodotDictionary();
            string id = country["id"].AsString();
            string name = country["name"].AsString();

            Color countryColor = GenerateCountryColor(id);
            _countryIdToColor[id] = countryColor;
            _colorToCountryId[QuantizeColor(countryColor)] = id;

            // Draw country polygons on ID map
            if (country.ContainsKey("polygons"))
            {
                var polygons = country["polygons"].AsGodotArray();
                foreach (var polyVariant in polygons)
                {
                    var polygon = polyVariant.AsGodotArray();
                    var points = new Vector2[polygon.Count];
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        var pt = polygon[i].AsGodotArray();
                        double lon = pt[0].AsDouble();
                        double lat = pt[1].AsDouble();
                        points[i] = LonLatToPixel(lon, lat);
                    }

                    // Draw filled polygon on ID map
                    DrawPolygonOnIdMap(points, countryColor);
                }
            }
        }

        // Save ID map for next launch
        string savePath = "user://map_id_map.png";
        _idMapImage.SavePng(savePath);
        GD.Print($"[MapView] ID map generated and saved: {_idMapImage.GetWidth()}x{_idMapImage.GetHeight()}");

        // Create texture for GPU debug
        _idMapTexture = ImageTexture.CreateFromImage(_idMapImage);
    }

    private Vector2 LonLatToPixel(double lon, double lat)
    {
        // Equirectangular: lon [-180,180] -> x [0, MAP_WIDTH], lat [90,-90] -> y [0, MAP_HEIGHT]
        float x = (float)((lon + 180.0) / 360.0 * MAP_WIDTH);
        float y = (float)((90.0 - lat) / 180.0 * MAP_HEIGHT);
        return new Vector2(
            Mathf.Clamp(x, 0, MAP_WIDTH - 1),
            Mathf.Clamp(y, 0, MAP_HEIGHT - 1)
        );
    }

    private void DrawPolygonOnIdMap(Vector2[] points, Color color)
    {
        if (points.Length < 3) return;

        // Find bounding box
        int minX = (int)MAP_WIDTH, maxX = 0, minY = (int)MAP_HEIGHT, maxY = 0;
        foreach (var p in points)
        {
            if (p.X < minX) minX = (int)p.X;
            if (p.X > maxX) maxX = (int)p.X;
            if (p.Y < minY) minY = (int)p.Y;
            if (p.Y > maxY) maxY = (int)p.Y;
        }

        minX = Mathf.Clamp(minX, 0, (int)MAP_WIDTH - 1);
        maxX = Mathf.Clamp(maxX, 0, (int)MAP_WIDTH - 1);
        minY = Mathf.Clamp(minY, 0, (int)MAP_HEIGHT - 1);
        maxY = Mathf.Clamp(maxY, 0, (int)MAP_HEIGHT - 1);

        // Scanline fill
        for (int y = minY; y <= maxY; y++)
        {
            var intersections = new List<float>();

            for (int i = 0; i < points.Length; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % points.Length];

                if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                {
                    float t = (y - p1.Y) / (p2.Y - p1.Y);
                    float x = p1.X + t * (p2.X - p1.X);
                    intersections.Add(x);
                }
            }

            intersections.Sort();

            for (int i = 0; i < intersections.Count - 1; i += 2)
            {
                int startX = (int)Mathf.Ceil(intersections[i]);
                int endX = (int)Mathf.Floor(intersections[i + 1]);
                startX = Mathf.Clamp(startX, 0, (int)MAP_WIDTH - 1);
                endX = Mathf.Clamp(endX, 0, (int)MAP_WIDTH - 1);

                for (int x = startX; x <= endX; x++)
                {
                    _idMapImage.SetPixel(x, y, color);
                }
            }
        }
    }

    private void LoadCountryData(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[MapView] Country data not found: {path}");
            return;
        }

        var json = FileAccess.GetFileAsString(path);
        var countriesData = Json.ParseString(json).AsGodotArray();

        _countries.Clear();
        foreach (var countryVariant in countriesData)
        {
            var country = countryVariant.AsGodotDictionary();
            string id = country["id"].AsString();
            string name = country["name"].AsString();

            _countries[id] = new CountryData
            {
                Id = id,
                Name = name
            };
        }

        GD.Print($"[MapView] Loaded {_countries.Count} countries");
    }

    private void LoadMapTexture()
    {
        // Load terrain/map texture (Natural Earth or similar)
        var texture = GD.Load<Texture2D>("res://Assets/map_textures/natural_earth_2.png");
        if (texture != null)
        {
            _mapCenter.Texture = texture;
            _mapLeft.Texture = texture;
            _mapRight.Texture = texture;

            GD.Print($"[MapView] Map texture loaded: {texture.GetWidth()}x{texture.GetHeight()}");
        }
        else
        {
            GD.PrintErr("[MapView] Map texture not found!");
        }
    }

    // ============================================================
    // CAMERA & COORDINATE CONVERSION (Step B)
    // ============================================================

    public override void _Process(double delta)
    {
        if (_camera == null) return;

        // Initialize camera on first frame
        if (_minZoom == MIN_ZOOM_BASE)
        {
            InitializeCamera();
        }

        HandleKeyboardPan((float)delta);
        UpdateCameraLimits();
        ClampCamera();
        UpdateMapWrapPositions();
        UpdateDebugLabel();

        HandleCountryHover();

        if (TestMode)
        {
            RunClickTest((float)delta);
        }
    }

    private void InitializeCamera()
    {
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        // Account for HUD height
        float effectiveHeight = viewportSize.Y - _hudTopHeight;

        // Min zoom to cover full map vertically
        float zoomY = effectiveHeight / MAP_HEIGHT;
        _minZoom = Mathf.Max(zoomY, MIN_ZOOM_BASE);

        _camera.Zoom = new Vector2(_minZoom, _minZoom);
        _camera.Position = new Vector2(MAP_WIDTH / 2f, MAP_HEIGHT / 2f);

        UpdateCameraLimits();
        ClampCamera();

        GD.Print($"[MapView] Camera initialized: Zoom={_minZoom:F4}, MinZoom={_minZoom:F4}, Pos={_camera.Position}");
    }

    private void HandleKeyboardPan(float delta)
    {
        Vector2 dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;

        if (dir != Vector2.Zero)
        {
            float speed = PAN_SPEED / _camera.Zoom.X;
            _camera.Position += dir * speed * delta;
        }
    }

    public void UpdateCameraLimits()
    {
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        if (!IsFinite(viewportSize) || !IsFinite(_camera.Zoom.X) || _camera.Zoom.X <= 0)
            return;

        float effectiveHeight = viewportSize.Y - _hudTopHeight;
        Vector2 visibleWorld = new Vector2(viewportSize.X, effectiveHeight) / _camera.Zoom.X;
        float halfW = visibleWorld.X / 2f;
        float halfH = visibleWorld.Y / 2f;

        // Vertical limits only (horizontal wraps)
        float limitTop = halfH;
        float limitBottom = MAP_HEIGHT - halfH;

        if (limitTop > limitBottom)
        {
            float centerY = MAP_HEIGHT / 2f;
            limitTop = limitBottom = centerY;
        }

        _camera.LimitTop = (int)Mathf.Ceil(limitTop);
        _camera.LimitBottom = (int)Mathf.Floor(limitBottom);
        _camera.LimitLeft = 0;
        _camera.LimitRight = 0;
    }

    public void ClampCamera()
    {
        Vector2 pos = _camera.Position;

        // Horizontal wrapping
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        float halfVisibleW = viewportWidth / _camera.Zoom.X / 2f;
        float wrapMin = -halfVisibleW;
        float wrapMax = MAP_WIDTH + halfVisibleW;

        if (pos.X < wrapMin)
        {
            pos.X += MAP_WIDTH;
        }
        else if (pos.X > wrapMax)
        {
            pos.X -= MAP_WIDTH;
        }

        // Vertical clamp
        pos.Y = Mathf.Clamp(pos.Y, _camera.LimitTop, _camera.LimitBottom);

        _camera.Position = pos;
    }

    private void UpdateMapWrapPositions()
    {
        float camX = _camera.Position.X;
        float centerTileX = Mathf.Floor(camX / MAP_WIDTH) * MAP_WIDTH;

        _mapCenter.Position = new Vector2(centerTileX, 0);
        _mapLeft.Position = new Vector2(centerTileX - MAP_WIDTH, 0);
        _mapRight.Position = new Vector2(centerTileX + MAP_WIDTH, 0);
    }

    // ============================================================
    // COORDINATE CONVERSION (Rule 1: use get_global_mouse_position)
    // ============================================================

    /// <summary>
    /// Screen (viewport) -> World (map) coordinates
    /// Uses get_global_mouse_position() for accurate conversion
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        // Get the actual viewport the camera is rendering to
        var viewport = _camera.GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        // Convert screen position to world using camera
        return _camera.Position + (screenPos - viewportSize / 2f) / _camera.Zoom.X;
    }

    /// <summary>
    /// World (map) -> Screen (viewport) coordinates
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        var viewport = _camera.GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        return (worldPos - _camera.Position) * _camera.Zoom.X + viewportSize / 2f;
    }

    /// <summary>
    /// Convert world position to lon/lat
    /// </summary>
    public Vector2 WorldToLonLat(Vector2 worldPos)
    {
        double lon = -180.0 + (worldPos.X / MAP_WIDTH) * 360.0;
        double lat = 90.0 - (worldPos.Y / MAP_HEIGHT) * 180.0;
        return new Vector2((float)lon, (float)lat);
    }

    /// <summary>
    /// Convert lon/lat to world position
    /// </summary>
    public Vector2 LonLatToWorld(double lon, double lat)
    {
        float x = (float)((lon + 180.0) / 360.0 * MAP_WIDTH);
        float y = (float)((90.0 - lat) / 180.0 * MAP_HEIGHT);
        return new Vector2(Mathf.Clamp(x, 0, MAP_WIDTH - 1), Mathf.Clamp(y, 0, MAP_HEIGHT - 1));
    }

    // ============================================================
    // PIXEL-PERFECT HIT-TEST (Rule 2: ID map with get_pixel)
    // ============================================================

    /// <summary>
    /// Get country ID at world position using ID map (pixel-perfect)
    /// </summary>
    public string GetCountryIdAtWorldPos(Vector2 worldPos)
    {
        if (_idMapImage == null) return null;

        // Convert world position to pixel coordinates on ID map
        int x = Mathf.Clamp((int)worldPos.X, 0, _idMapImage.GetWidth() - 1);
        int y = Mathf.Clamp((int)worldPos.Y, 0, _idMapImage.GetHeight() - 1);

        Color pixelColor = _idMapImage.GetPixel(x, y);
        pixelColor = QuantizeColor(pixelColor);

        // Ocean check (black/transparent)
        if (pixelColor.R < 0.02f && pixelColor.G < 0.02f && pixelColor.B < 0.02f)
            return null; // Ocean

        if (_colorToCountryId.TryGetValue(pixelColor, out string countryId))
            return countryId;

        return null;
    }

    /// <summary>
    /// Get country ID at screen position (for click/hover)
    /// </summary>
    public string GetCountryIdAtScreenPos(Vector2 screenPos)
    {
        Vector2 worldPos = ScreenToWorld(screenPos);
        return GetCountryIdAtWorldPos(worldPos);
    }

    // ============================================================
    // INPUT HANDLING (Rule 3, 4)
    // ============================================================

    public override void _UnhandledInput(InputEvent @event)
    {
        // Check if mouse is over HUD (top area)
        var mousePos = GetViewport().GetMousePosition();
        _isMouseOverHUD = mousePos.Y < _hudTopHeight;

        // F3 toggles debug
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.F3)
            {
                _debugMode = !_debugMode;
                _debugLabel.Visible = _debugMode;
                GD.Print($"[MapView] Debug mode: {_debugMode}");
            }
        }

        // Don't handle map input if mouse is over HUD
        if (_isMouseOverHUD) return;

        // Wheel zoom (cursor-anchored)
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                HandleWheelZoom(1f / ZOOM_SPEED, mb.Position);
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                HandleWheelZoom(ZOOM_SPEED, mb.Position);
                GetViewport().SetInputAsHandled();
            }
        }

        // RMB drag pan
        if (@event is InputEventMouseButton mb3 && mb3.ButtonIndex == MouseButton.Right)
        {
            if (mb3.Pressed)
            {
                _dragging = true;
                _lastMousePos = mb3.Position;
                GetViewport().SetInputAsHandled();
            }
            else
            {
                _dragging = false;
            }
        }

        if (_dragging && @event is InputEventMouseMotion mm)
        {
            Vector2 delta = mm.Position - _lastMousePos;
            Vector2 worldDelta = -delta / _camera.Zoom.X;
            _camera.Position += worldDelta;
            ClampCamera();
            _lastMousePos = mm.Position;
            GetViewport().SetInputAsHandled();
        }

        // LMB click - country selection
        if (@event is InputEventMouseButton mb4 && mb4.ButtonIndex == MouseButton.Left && mb4.Pressed)
        {
            string countryId = GetCountryIdAtScreenPos(mb4.Position);
            HandleCountryClick(countryId);
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleCountryClick(string countryId)
    {
        if (countryId == null)
        {
            // Clicked on ocean - deselect
            if (_selectedCountryId != null)
            {
                _selectedCountryId = null;
                EmitSignal(SignalName.CountryDeselected);
                GD.Print("[MapView] Country deselected (ocean clicked)");
            }
            return;
        }

        if (_selectedCountryId == countryId)
        {
            // Clicked same country - deselect
            _selectedCountryId = null;
            EmitSignal(SignalName.CountryDeselected);
            GD.Print($"[MapView] Country deselected: {countryId}");
        }
        else
        {
            // Select new country
            _selectedCountryId = countryId;
            string countryName = _countries.TryGetValue(countryId, out var cd) ? cd.Name : countryId;
            EmitSignal(SignalName.CountrySelected, countryId, countryName);
            GD.Print($"[MapView] Country selected: {countryId} ({countryName})");
        }
    }

    private void HandleWheelZoom(float zoomFactor, Vector2 screenPos)
    {
        Vector2 worldBefore = ScreenToWorld(screenPos);
        float newZoom = Mathf.Clamp(_camera.Zoom.X * zoomFactor, _minZoom, MAX_ZOOM);
        _camera.Zoom = new Vector2(newZoom, newZoom);
        UpdateCameraLimits();
        Vector2 worldAfter = ScreenToWorld(screenPos);
        _camera.Position += worldBefore - worldAfter;
        ClampCamera();
    }

    // ============================================================
    // HOVER HANDLING
    // ============================================================

    private void HandleCountryHover()
    {
        if (_isMouseOverHUD) return;

        var mouseScreen = GetViewport().GetMousePosition();
        string countryId = GetCountryIdAtScreenPos(mouseScreen);

        if (countryId != _hoveredCountryId)
        {
            _hoveredCountryId = countryId;
            if (countryId != null)
            {
                string countryName = _countries.TryGetValue(countryId, out var cd) ? cd.Name : countryId;
                EmitSignal(SignalName.CountryHovered, countryId, countryName);
            }
        }
    }

    // ============================================================
    // DEBUG & TEST
    // ============================================================

    private void UpdateDebugLabel()
    {
        if (!_debugMode || _debugLabel == null) return;

        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;
        var mouseScreen = viewport.GetMousePosition();
        var mouseWorld = ScreenToWorld(mouseScreen);
        var (lon, lat) = WorldToLonLat(mouseWorld);
        string countryId = GetCountryIdAtWorldPos(mouseWorld);

        _debugLabel.Text = $@"MapView Debug
Mode: Debug (F3 to toggle)
Zoom: {_camera.Zoom.X:F4} (min: {_minZoom:F4}, max: {MAX_ZOOM})
Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0})
Viewport: {viewportSize.X}x{viewportSize.Y} (HUD: {_hudTopHeight})
Mouse Screen: ({mouseScreen.X:F0}, {mouseScreen.Y:F0})
Mouse World: ({mouseWorld.X:F0}, {mouseWorld.Y:F0})
Mouse Lon/Lat: ({lon:F2}°, {lat:F2}°)
Hovered Country: {countryId ?? "OCEAN"}
Selected Country: {_selectedCountryId ?? "none"}
HUD Block: {_isMouseOverHUD}

F3: Debug | WASD/Arrows: Pan | RMB: Drag | Wheel: Zoom";
    }

    private void RunClickTest(float delta)
    {
        _testTimer += delta;

        if (_testTimer < 1.0f) return; // Wait 1 second between tests
        _testTimer = 0f;

        if (_testIndex >= _testCountries.Length)
        {
            // Test wrap-around
            if (_testZoomLevel < 2)
            {
                _testZoomLevel++;
                _testIndex = 0;
                SetTestZoom();
                GD.Print($"[MapView] Test zoom level: {_testZoomLevel}");
                return;
            }

            // Test wrap at x=0 and x=MAP_WIDTH
            TestWrapAround();
            return;
        }

        string expectedId = _testCountries[_testIndex];
        double lon = _testLons[_testIndex];
        double lat = _testLats[_testIndex];

        // Convert lon/lat -> world -> screen
        Vector2 worldPos = LonLatToWorld(lon, lat);
        Vector2 screenPos = WorldToScreen(worldPos);

        // Simulate click at screen position
        string detectedId = GetCountryIdAtScreenPos(screenPos);

        string status = detectedId == expectedId ? "PASS" : "FAIL";
        GD.Print($"[MapView][TEST] {_testCountries[_testIndex]} at ({lon:F2},{lat:F2}) -> world({worldPos.X:F0},{worldPos.Y:F0}) -> screen({screenPos.X:F0},{screenPos.Y:F0}) -> detected: {detectedId ?? "OCEAN"} [{status}]");

        if (status == "FAIL")
        {
            GD.PrintErr($"[MapView][TEST] FAIL: Expected {expectedId}, got {detectedId ?? "null"}");
        }

        _testIndex++;
    }

    private void SetTestZoom()
    {
        float[] zooms = { _minZoom, (_minZoom + MAX_ZOOM) / 2f, MAX_ZOOM };
        float targetZoom = zooms[_testZoomLevel];

        Vector2 worldBefore = _camera.Position;
        _camera.Zoom = new Vector2(targetZoom, targetZoom);
        UpdateCameraLimits();
        Vector2 worldAfter = ScreenToWorld(WorldToScreen(worldBefore));
        _camera.Position += worldBefore - worldAfter;
        ClampCamera();

        GD.Print($"[MapView] Test zoom set to {targetZoom:F4}");
    }

    private void TestWrapAround()
    {
        GD.Print("[MapView][TEST] Testing wrap-around at x=0 and x=MAP_WIDTH...");

        // Test at x near 0
        _camera.Position = new Vector2(100, MAP_HEIGHT / 2f);
        ClampCamera();
        TestSinglePoint("WRAP_LEFT", 28.97, 41.01); // Istanbul

        // Test at x near MAP_WIDTH
        _camera.Position = new Vector2(MAP_WIDTH - 100, MAP_HEIGHT / 2f);
        ClampCamera();
        TestSinglePoint("WRAP_RIGHT", 28.97, 41.01); // Istanbul

        GD.Print("[MapView][TEST] All tests completed!");
        TestMode = false;
    }

    private void TestSinglePoint(string label, double lon, double lat)
    {
        Vector2 worldPos = LonLatToWorld(lon, lat);
        Vector2 screenPos = WorldToScreen(worldPos);
        string detectedId = GetCountryIdAtScreenPos(screenPos);
        string expectedId = "TUR";
        string status = detectedId == expectedId ? "PASS" : "FAIL";
        GD.Print($"[MapView][TEST] {label} at ({lon:F2},{lat:F2}) -> detected: {detectedId ?? "OCEAN"} [{status}]");
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    public void SetHudTopHeight(float height)
    {
        _hudTopHeight = height;
        UpdateCameraLimits();
        ClampCamera();
    }

    public void SetShowBorders(bool show) => _showBorders = show;
    public void SetShowProvinces(bool show) => _showProvinceGrid = show;

    public string GetHoveredCountryId() => _hoveredCountryId;
    public string GetSelectedCountryId() => _selectedCountryId;
    public Camera2D GetCamera() => _camera;
    public Vector2 GetMapSize() => new Vector2(MAP_WIDTH, MAP_HEIGHT);

    // ============================================================
    // UTILITIES
    // ============================================================

    private static bool IsFinite(float f) => float.IsFinite(f);
    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}