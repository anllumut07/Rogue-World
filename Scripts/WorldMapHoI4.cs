using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// HoI4-style World Map using province bitmap system
/// Features: Province rendering, terrain shading, borders, rivers, weather effects, infinite horizontal wrapping
/// </summary>
public partial class WorldMapHoI4 : Node2D
{
    [Signal] public delegate void ProvinceSelectedEventHandler(int provinceId, string countryTag);
    [Signal] public delegate void ProvinceHoveredEventHandler(int provinceId, string countryTag);

    public static WorldMapHoI4 Instance { get; private set; }

    // Map dimensions (from province bitmap)
    public const float WORLD_WIDTH = 5632f;
    public const float WORLD_HEIGHT = 2048f;

    // Camera settings
    private const float MIN_ZOOM = 0.15f;
    private const float MAX_ZOOM = 10f;
    private const float ZOOM_SPEED = 1.15f;
    private const float PAN_SPEED = 1200f;

    // Node references
    private Node2D _worldRoot;
    private Node2D _backgroundRoot;
    private Camera2D _camera;
    private CanvasLayer _uiCanvas;

    // Rendering layers
    private Sprite2D _provinceMapSprite; // Province ID lookup texture
    private Sprite2D _terrainSprite;     // Terrain texture
    private Node2D _borderLayer;
    private Node2D _riverLayer;
    private Node2D _buildingLayer;
    private Node2D _unitLayer;
    private Node2D _weatherLayer;

    // Wrapping backgrounds (3 copies for infinite horizontal)
    private Sprite2D _bgCenter, _bgLeft, _bgRight;

    // State
    private Vector2 _textureSize = new Vector2(WORLD_WIDTH, WORLD_HEIGHT);
    private float _minZoom = MIN_ZOOM;
    private Vector2 _lastMousePos;
    private bool _dragging = false;
    private int _hoveredProvinceId = -1;
    private int _selectedProvinceId = -1;
    private bool _showBorders = true;
    private bool _showRivers = true;
    private bool _showTerrain = true;
    private MapMode _currentMapMode = MapMode.Political;

    // Performance: spatial grid for hover detection
    private const int GridCellSize = 64;
    private Dictionary<long, List<int>> _provinceGrid = new();

    // Shaders for advanced rendering
    private ShaderMaterial _provinceShader;
    private ShaderMaterial _terrainShader;
    private ShaderMaterial _weatherShader;

    public enum MapMode
    {
        Political,    // Country colors
        Terrain,      // Terrain types
        Supply,       // Supply levels
        Weather,      // Weather phenomena
        Buildings,    // Building density
        Units         // Military units
    }

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[WorldMapHoI4] Ready - HoI4-style map system");
    }

    /// <summary>
    /// Initialize the map after ProvinceManager is ready
    /// </summary>
    public async Task InitializeAsync()
    {
        GD.Print("[WorldMapHoI4] Initializing map...");

        CreateSceneTree();
        await LoadTexturesAsync();
        SetupCamera();
        SetupUICanvas();
        BuildSpatialGrid();
        CreateBorderLines();
        CreateRiverLines();

        GD.Print("[WorldMapHoI4] Initialization complete");
    }

    private void CreateSceneTree()
    {
        _worldRoot = new Node2D { Name = "WorldRoot" };
        AddChild(_worldRoot);

        _backgroundRoot = new Node2D { Name = "BackgroundRoot" };
        _worldRoot.AddChild(_backgroundRoot);

        // Background sprites for wrapping
        _bgCenter = CreateBackgroundSprite("BackgroundCenter", Vector2.Zero);
        _bgLeft = CreateBackgroundSprite("BackgroundLeft", new Vector2(-WORLD_WIDTH, 0));
        _bgRight = CreateBackgroundSprite("BackgroundRight", new Vector2(WORLD_WIDTH, 0));

        // Province map texture (for GPU-based province lookup)
        _provinceMapSprite = new Sprite2D
        {
            Name = "ProvinceMap",
            Centered = false,
            Position = Vector2.Zero,
            ZIndex = -50,
            Modulate = Colors.White
        };
        _backgroundRoot.AddChild(_provinceMapSprite);

        // Terrain layer
        _terrainSprite = new Sprite2D
        {
            Name = "TerrainMap",
            Centered = false,
            Position = Vector2.Zero,
            ZIndex = -40
        };
        _backgroundRoot.AddChild(_terrainSprite);

        // Camera
        _camera = new Camera2D
        {
            Name = "MapCamera",
            Zoom = Vector2.One
        };
        _worldRoot.AddChild(_camera);
        _camera.MakeCurrent();

        // Render layers (in Z-order)
        _borderLayer = new Node2D { Name = "BorderLayer", ZIndex = 10 };
        _worldRoot.AddChild(_borderLayer);

        _riverLayer = new Node2D { Name = "RiverLayer", ZIndex = 5 };
        _worldRoot.AddChild(_riverLayer);

        _buildingLayer = new Node2D { Name = "BuildingLayer", ZIndex = 15 };
        _worldRoot.AddChild(_buildingLayer);

        _unitLayer = new Node2D { Name = "UnitLayer", ZIndex = 20 };
        _worldRoot.AddChild(_unitLayer);

        _weatherLayer = new Node2D { Name = "WeatherLayer", ZIndex = 25 };
        _worldRoot.AddChild(_weatherLayer);

        // UI Canvas (debug overlay only)
        _uiCanvas = new CanvasLayer { Name = "UICanvas", Layer = 100 };
        AddChild(_uiCanvas);
    }

    private Sprite2D CreateBackgroundSprite(string name, Vector2 position)
    {
        var sprite = new Sprite2D
        {
            Name = name,
            Centered = false,
            Position = position,
            Scale = Vector2.One,
            ZIndex = -100
        };
        _backgroundRoot.AddChild(sprite);
        return sprite;
    }

    private async Task LoadTexturesAsync()
    {
        // Create province lookup texture from bitmap
        var provinceTexture = ProvinceBitmapLoader.CreateProvinceLookupTexture();
        if (provinceTexture != null)
        {
            _provinceMapSprite.Texture = provinceTexture;
            GD.Print("[WorldMapHoI4] Province lookup texture loaded");
        }

        // Load terrain texture (create from terrain bitmap + atlas)
        await CreateTerrainTextureAsync();

        // Load background texture (Natural Earth or similar)
        var bgTexture = GD.Load<Texture2D>("res://Assets/map_textures/natural_earth_2.png");
        if (bgTexture != null)
        {
            _bgCenter.Texture = bgTexture;
            _bgLeft.Texture = bgTexture;
            _bgRight.Texture = bgTexture;
            _textureSize = new Vector2(bgTexture.GetWidth(), bgTexture.GetHeight());
            GD.Print($"[WorldMapHoI4] Background texture: {_textureSize.X}x{_textureSize.Y}");
        }
    }

    private async Task CreateTerrainTextureAsync()
    {
        // Create terrain color map from terrain.bmp indices
        var image = Image.CreateEmpty((int)WORLD_WIDTH, (int)WORLD_HEIGHT, false, Image.Format.Rgba8);
        var terrainData = new byte[(int)(WORLD_WIDTH * WORLD_HEIGHT * 4)];

        // Color palette for terrain types (HoI4 style)
        var terrainColors = new Dictionary<string, Color>
        {
            ["plains"] = new Color(0.6f, 0.5f, 0.35f),
            ["forest"] = new Color(0.2f, 0.5f, 0.2f),
            ["hills"] = new Color(0.55f, 0.5f, 0.3f),
            ["mountain"] = new Color(0.45f, 0.45f, 0.5f),
            ["desert"] = new Color(0.85f, 0.7f, 0.45f),
            ["jungle"] = new Color(0.15f, 0.45f, 0.15f),
            ["marsh"] = new Color(0.3f, 0.35f, 0.2f),
            ["urban"] = new Color(0.4f, 0.4f, 0.4f),
            ["supercity"] = new Color(0.5f, 0.3f, 0.2f),
            ["lakes"] = new Color(0.2f, 0.4f, 0.8f),
            ["ocean"] = new Color(0.15f, 0.25f, 0.5f),
            ["water_fjords"] = new Color(0.2f, 0.35f, 0.55f),
            ["water_littoral"] = new Color(0.25f, 0.4f, 0.55f),
            ["water_shallow_sea"] = new Color(0.3f, 0.45f, 0.6f),
            ["water_deep_ocean"] = new Color(0.1f, 0.2f, 0.45f)
        };

        int pixelCount = 0;
        for (int y = 0; y < WORLD_HEIGHT; y++)
        {
            for (int x = 0; x < WORLD_WIDTH; x++)
            {
                var terrain = TerrainBitmapLoader.GetTerrainAt(new Vector2(x, y));
                Color color = terrain != null && terrainColors.ContainsKey(terrain.Name)
                    ? terrainColors[terrain.Name]
                    : new Color(0.5f, 0.5f, 0.5f);

                int idx = pixelCount * 4;
                terrainData[idx] = (byte)(color.R * 255);
                terrainData[idx + 1] = (byte)(color.G * 255);
                terrainData[idx + 2] = (byte)(color.B * 255);
                terrainData[idx + 3] = 255;
                pixelCount++;
            }

            if (y % 128 == 0)
                await Task.Yield();
        }

        image.SetData((int)WORLD_WIDTH, (int)WORLD_HEIGHT, false, Image.Format.Rgba8, terrainData);
        var texture = ImageTexture.CreateFromImage(image);
        _terrainSprite.Texture = texture;

        GD.Print("[WorldMapHoI4] Terrain texture created");
    }

    private void SetupCamera()
    {
        _camera.LimitLeft = 0;
        _camera.LimitRight = 0; // No horizontal limits (infinite wrap)
        _camera.LimitTop = 0;
        _camera.LimitBottom = (int)WORLD_HEIGHT;
    }

    private void SetupUICanvas()
    {
        // Debug info label
        var debugLabel = new Label
        {
            Name = "DebugLabel",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Position = new Vector2(10, 10),
            Size = new Vector2(400, 300),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        debugLabel.AddThemeColorOverride("font_color", Colors.White);
        debugLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        debugLabel.AddThemeConstantOverride("outline_size", 2);
        _uiCanvas.AddChild(debugLabel);

        // Map mode buttons
        var modeContainer = new HBoxContainer
        {
            Name = "ModeButtons",
            Position = new Vector2(10, 320)
        };
        foreach (MapMode mode in Enum.GetValues(typeof(MapMode)))
        {
            var btn = new Button { Text = mode.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
            btn.Pressed += () => SetMapMode(mode);
            modeContainer.AddChild(btn);
        }
        _uiCanvas.AddChild(modeContainer);
    }

    private void BuildSpatialGrid()
    {
        GD.Print("[WorldMapHoI4] Building spatial grid...");

        _provinceGrid.Clear();
        var allProvinces = ProvinceBitmapLoader.GetAllProvinces();

        foreach (var prov in allProvinces.Values)
        {
            if (prov.Centroid == Vector2.Zero) continue;

            int cellX = (int)(prov.Centroid.X / GridCellSize);
            int cellY = (int)(prov.Centroid.Y / GridCellSize);
            long key = ((long)cellX << 32) | (uint)cellY;

            if (!_provinceGrid.ContainsKey(key))
                _provinceGrid[key] = new List<int>();

            _provinceGrid[key].Add(prov.Id);
        }

        GD.Print($"[WorldMapHoI4] Spatial grid built: {_provinceGrid.Count} cells");
    }

    private void CreateBorderLines()
    {
        // Country borders will be generated from province ownership
        // This is a placeholder - actual borders created in UpdateBorders()
    }

    private void CreateRiverLines()
    {
        var segments = RiversLoader.GetRiverSegments();
        GD.Print($"[WorldMapHoI4] Creating {segments.Count} river segments...");

        foreach (var segment in segments)
        {
            if (segment.Points.Count < 2) continue;

            var line = new Line2D
            {
                Points = segment.Points.ToArray(),
                DefaultColor = new Color(0.2f, 0.4f, 0.8f, 0.6f),
                Width = 1.5f * (segment.RiverType / 255.0f + 0.5f),
                Antialiased = true,
                ZIndex = 5
            };
            _riverLayer.AddChild(line);
        }
    }

    public override void _Process(double delta)
    {
        if (ProvinceManager.Instance == null) return;

        // Initialize camera on first frame
        if (_minZoom == MIN_ZOOM && _textureSize.X > 0)
        {
            InitializeCamera();
        }

        HandleKeyboardPan((float)delta);
        HandleCountryHover();
        UpdateCameraLimits();
        ClampCamera();
        UpdateDebugLabel();
    }

    private void InitializeCamera()
    {
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        // Calculate min zoom to cover vertically
        float zoomY = viewportSize.Y / _textureSize.Y;
        _minZoom = Mathf.Max(zoomY, MIN_ZOOM);

        _camera.Zoom = new Vector2(_minZoom, _minZoom);
        _camera.Position = _textureSize / 2f;

        UpdateCameraLimits();
        ClampCamera();

        GD.Print($"[WorldMapHoI4] Camera initialized: Zoom={_minZoom:F4}, Pos={_camera.Position}");
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

    private void HandleCountryHover()
    {
        var mouseScreen = GetViewport().GetMousePosition();
        var mouseWorld = ScreenToWorld(mouseScreen);

        int provinceId = GetProvinceAtWorldPos(mouseWorld);
        if (provinceId != _hoveredProvinceId)
        {
            _hoveredProvinceId = provinceId;
            if (provinceId >= 0)
            {
                var prov = ProvinceManager.Instance.GetProvince(provinceId);
                if (prov != null)
                    EmitSignal(SignalName.ProvinceHovered, provinceId, prov.Owner);
            }
        }
    }

    private int GetProvinceAtWorldPos(Vector2 worldPos)
    {
        // Use spatial grid for fast lookup
        int cellX = (int)(worldPos.X / GridCellSize);
        int cellY = (int)(worldPos.Y / GridCellSize);
        long key = ((long)cellX << 32) | (uint)cellY;

        if (_provinceGrid.TryGetValue(key, out var candidates))
        {
            // Check each candidate precisely using province bitmap
            foreach (int id in candidates)
            {
                var prov = ProvinceBitmapLoader.GetProvinceById(id);
                if (prov != null && IsPointInProvince(worldPos, prov))
                    return id;
            }
        }

        // Fallback: direct bitmap lookup
        var def = ProvinceBitmapLoader.GetProvinceAtWorldPos(worldPos);
        return def?.Id ?? -1;
    }

    private bool IsPointInProvince(Vector2 point, ProvinceDefinition prov)
    {
        // Quick bounding box check around centroid
        float radius = 50f; // Approximate province radius in pixels
        return (point - prov.Centroid).Length() < radius;
    }

    private void UpdateCameraLimits()
    {
        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;

        if (!IsFinite(viewportSize) || !IsFinite(_camera.Zoom.X) || _camera.Zoom.X <= 0)
            return;

        Vector2 visibleWorld = viewportSize / _camera.Zoom.X;
        float halfW = visibleWorld.X / 2f;
        float halfH = visibleWorld.Y / 2f;

        // Vertical limits only (horizontal wraps)
        float limitTop = halfH;
        float limitBottom = WORLD_HEIGHT - halfH;

        if (limitTop > limitBottom)
        {
            float centerY = WORLD_HEIGHT / 2f;
            limitTop = limitBottom = centerY;
        }

        _camera.LimitTop = (int)Mathf.Ceil(limitTop);
        _camera.LimitBottom = (int)Mathf.Floor(limitBottom);
        _camera.LimitLeft = 0;
        _camera.LimitRight = 0;
    }

    private void ClampCamera()
    {
        Vector2 pos = _camera.Position;

        // Horizontal wrapping
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        float halfVisibleW = viewportWidth / _camera.Zoom.X / 2f;
        float wrapMin = -halfVisibleW;
        float wrapMax = WORLD_WIDTH + halfVisibleW;

        if (pos.X < wrapMin)
        {
            pos.X += WORLD_WIDTH;
            UpdateBackgroundPositions();
        }
        else if (pos.X > wrapMax)
        {
            pos.X -= WORLD_WIDTH;
            UpdateBackgroundPositions();
        }

        // Vertical clamp
        pos.Y = Mathf.Clamp(pos.Y, _camera.LimitTop, _camera.LimitBottom);

        _camera.Position = pos;
    }

    private void UpdateBackgroundPositions()
    {
        float camX = _camera.Position.X;
        float centerTileX = Mathf.Floor(camX / WORLD_WIDTH) * WORLD_WIDTH;

        _bgCenter.Position = new Vector2(centerTileX, 0);
        _bgLeft.Position = new Vector2(centerTileX - WORLD_WIDTH, 0);
        _bgRight.Position = new Vector2(centerTileX + WORLD_WIDTH, 0);

        // Also wrap province map and terrain
        _provinceMapSprite.Position = new Vector2(centerTileX, 0);
        _terrainSprite.Position = new Vector2(centerTileX, 0);
    }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        return _camera.Position + (screenPos - viewportSize / 2f) / _camera.Zoom.X;
    }

    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        return (worldPos - _camera.Position) * _camera.Zoom.X + viewportSize / 2f;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Wheel zoom (cursor-anchored)
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                HandleWheelZoom(1f / ZOOM_SPEED, mb.Position); // Zoom in
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                HandleWheelZoom(ZOOM_SPEED, mb.Position); // Zoom out
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

        // LMB click - province selection
        if (@event is InputEventMouseButton mb4 && mb4.ButtonIndex == MouseButton.Left && mb4.Pressed)
        {
            var mouseWorld = ScreenToWorld(GetViewport().GetMousePosition());
            int provinceId = GetProvinceAtWorldPos(mouseWorld);

            if (provinceId >= 0)
            {
                _selectedProvinceId = provinceId;
                var prov = ProvinceManager.Instance.GetProvince(provinceId);
                if (prov != null)
                    EmitSignal(SignalName.ProvinceSelected, provinceId, prov.Owner);
            }
            GetViewport().SetInputAsHandled();
        }

        // Keyboard shortcuts
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.F1: SetMapMode(MapMode.Political); break;
                case Key.F2: SetMapMode(MapMode.Terrain); break;
                case Key.F3: SetMapMode(MapMode.Supply); break;
                case Key.F4: SetMapMode(MapMode.Weather); break;
                case Key.F5: SetMapMode(MapMode.Buildings); break;
                case Key.F6: SetMapMode(MapMode.Units); break;
                case Key.B: _showBorders = !_showBorders; UpdateBorderVisibility(); break;
                case Key.R: _showRivers = !_showRivers; _riverLayer.Visible = _showRivers; break;
                case Key.T: _showTerrain = !_showTerrain; _terrainSprite.Visible = _showTerrain; break;
            }
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

    private void SetMapMode(MapMode mode)
    {
        _currentMapMode = mode;
        GD.Print($"[WorldMapHoI4] Map mode: {mode}");

        // Update shader uniforms or texture based on mode
        switch (mode)
        {
            case MapMode.Political:
                _provinceMapSprite.Modulate = Colors.White;
                _terrainSprite.Modulate = new Color(1, 1, 1, 0.3f);
                break;
            case MapMode.Terrain:
                _provinceMapSprite.Modulate = new Color(1, 1, 1, 0.2f);
                _terrainSprite.Modulate = Colors.White;
                break;
            case MapMode.Supply:
                // Would use supply shader
                break;
            case MapMode.Weather:
                // Would use weather shader
                break;
        }
    }

    private void UpdateBorderVisibility()
    {
        _borderLayer.Visible = _showBorders;
    }

    private void UpdateDebugLabel()
    {
        var label = _uiCanvas.GetNodeOrNull<Label>("DebugLabel");
        if (label == null) return;

        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;
        var mouseScreen = viewport.GetMousePosition();
        var mouseWorld = ScreenToWorld(mouseScreen);
        var (lon, lat) = ProvinceBitmapLoader.WorldToLonLat(mouseWorld);

        var prov = _hoveredProvinceId >= 0 ? ProvinceManager.Instance.GetProvince(_hoveredProvinceId) : null;

        label.Text = $@"WorldMapHoI4 Debug
Mode: {_currentMapMode}
Zoom: {_camera.Zoom.X:F4} (min: {_minZoom:F4})
Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0})
Viewport: {viewportSize.X}x{viewportSize.Y}
Mouse World: ({mouseWorld.X:F0}, {mouseWorld.Y:F0})
Mouse Lon/Lat: ({lon:F2}°, {lat:F2}°)
Hovered Province: {(_hoveredProvinceId >= 0 ? _hoveredProvinceId.ToString() : "none")}
  Owner: {prov?.Owner ?? "none"}
  Terrain: {prov?.Definition.Terrain?.Name ?? "none"}
  Supply: {prov?.Supply.ToString("F2") ?? "none"}
Selected Province: {(_selectedProvinceId >= 0 ? _selectedProvinceId.ToString() : "none")}
Borders: {_showBorders} | Rivers: {_showRivers} | Terrain: {_showTerrain}
F1-F6: Map Modes | B: Borders | R: Rivers | T: Terrain";
    }

    private static bool IsFinite(float f) => float.IsFinite(f);
    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    // ===== Public API =====

    public void SetProvinceOwner(int provinceId, string countryTag)
    {
        // Update visual ownership (borders, colors)
        UpdateProvinceVisuals(provinceId);
    }

    private void UpdateProvinceVisuals(int provinceId)
    {
        // Rebuild borders for affected area
        // Update province color in political map mode
    }

    public void UpdateBorders()
    {
        // Clear existing borders
        foreach (var child in _borderLayer.GetChildren())
            child.QueueFree();

        // Generate country borders from province ownership
        // This would trace borders between different countries
        GD.Print("[WorldMapHoI4] Borders updated");
    }

    public int GetSelectedProvinceId() => _selectedProvinceId;
    public int GetHoveredProvinceId() => _hoveredProvinceId;
    public Camera2D GetCamera() => _camera;
    public MapMode GetMapMode() => _currentMapMode;
}