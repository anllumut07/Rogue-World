using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// WorldMap — Compatibility layer providing the interface that game systems expect.
/// The actual visual map is WorldMapV5. This class provides province data access,
/// map mode state, and other utilities without being a visual Node2D.
/// </summary>
public partial class WorldMap : Node
{
    [Signal]
    public delegate void CountrySelectedEventHandler(string countryId, string countryName);

    [Signal]
    public delegate void CountryDeselectedEventHandler();

    [Signal]
    public delegate void ProvinceSelectionChangedEventHandler(string[] provinceIds);

    // Singleton instance for backward compatibility
    public static WorldMap Instance { get; private set; }

    // M39.2: Fast province access for naval invasion system
    public ProvinceData GetProvinceById(string id) => ProvinceDataCache.GetById(id);

    /// <summary>
    /// Returns all provinces belonging to a country.
    /// </summary>
    public List<ProvinceData> GetProvincesForCountry(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return new List<ProvinceData>();
        return ProvinceDataCache.GetForCountry(countryId);
    }

    // M13.3: Map view modes
    public enum MapMode { Political, Terrain, Supply, Factories, Resources, AirZones, Culture }
    public MapMode CurrentMapMode
    {
        get => MapViewSystem.Mode;
        private set => MapViewSystem.SetMode(value);
    }

    // Factories mode max factory count for brightness normalization
    private int _maxFactories = 1;
    public int MaxFactories
    {
        get => _maxFactories;
        set => _maxFactories = Math.Max(1, value);
    }

    // Visual settings (delegated to MapViewSystem or no-op for WorldMapV5)
    private bool _terrainBandsVisible = false;
    private bool _terminatorVisible = false;
    private bool _vignetteVisible = false;

    public bool TerrainBandsVisible
    {
        get => _terrainBandsVisible;
        set { _terrainBandsVisible = value; GD.Print($"WorldMap: arazi bantları {(value ? "AÇIK" : "KAPALI")}"); }
    }

    public bool TerminatorVisible
    {
        get => _terminatorVisible;
        set { _terminatorVisible = value; GD.Print($"WorldMap: terminatör {(value ? "AÇIK" : "KAPALI")}"); }
    }

    public bool VignetteVisible
    {
        get => _vignetteVisible;
        set { _vignetteVisible = value; GD.Print($"WorldMap: vinyet {(value ? "AÇIK" : "KAPALI")}"); }
    }

    // Culture highlight (for CultureLegendPanel, MapViewSystem integration)
    public string CultureHighlight
    {
        get => MapViewSystem.CultureHighlight;
        set => MapViewSystem.SetCultureHighlight(value);
    }

    public void SetCultureHighlight(string culture)
    {
        MapViewSystem.SetCultureHighlight(culture);
    }

    public void SetMapMode(MapMode mode)
    {
        CurrentMapMode = mode;
    }

    // Selected/hovered country (for compatibility)
    private string _selectedCountryId = "";
    private string _hoveredCountryId = "";

    public string SelectedCountryId => _selectedCountryId;
    public string HoveredCountryId => _hoveredCountryId;

    public void SetSelectedCountry(string countryId, string countryName = "")
    {
        _selectedCountryId = countryId ?? "";
        if (!string.IsNullOrEmpty(countryId))
            GD.Print($"WorldMap: Country selected: {countryName} ({countryId})");
        else
            GD.Print("WorldMap: Country deselected");
    }

    public void SetHoveredCountry(string countryId)
    {
        _hoveredCountryId = countryId ?? "";
    }

    // M18.3: Set provinces from procedural source
    private List<ProvinceData> _provinces = new();
    private Dictionary<string, ProvinceData> _provinceById = new();

    public IReadOnlyList<ProvinceData> Provinces => _provinces;

    public void SetProvinces(IReadOnlyList<ProvinceData> provinces)
    {
        _provinces = provinces?.ToList() ?? new List<ProvinceData>();
        _provinceById.Clear();
        if (_provinces != null)
        {
            foreach (var p in _provinces)
            {
                if (!string.IsNullOrEmpty(p.Id))
                    _provinceById[p.Id] = p;
            }
        }
        ProvinceDataCache.Rebuild(_provinces);
        GD.Print($"WorldMap: {_provinces.Count} provinces loaded");
    }

    // M19.2: Province selection
    private readonly HashSet<string> _selectedProvinceIds = new();

    public IReadOnlySet<string> SelectedProvinceIds => _selectedProvinceIds;

    public void SetSelectedProvinces(IEnumerable<string> provinceIds)
    {
        _selectedProvinceIds.Clear();
        if (provinceIds != null)
        {
            foreach (var id in provinceIds)
                _selectedProvinceIds.Add(id);
        }
        GD.Print($"WorldMap: Province selection changed - {_selectedProvinceIds.Count} selected");
    }

    // M19.6: Frontlines (empty for WorldMapV5 compatibility)
    public void RebuildFrontlines() { }
    public void ClearFrontlines() { }

    // M7: Ownership colors refresh (no-op for WorldMapV5)
    public void RefreshOwnershipColors()
    {
        GD.Print("WorldMap: RefreshOwnershipColors called (no-op for WorldMapV5)");
    }

    // M46.3: Culture mismatch provinces
    private readonly HashSet<string> _cultureMismatchProvinces = new(StringComparer.Ordinal);

    public HashSet<string> CultureMismatchProvinces => _cultureMismatchProvinces;

    public void SetCultureMismatchProvinces(IEnumerable<string> provinceIds)
    {
        _cultureMismatchProvinces.Clear();
        if (provinceIds != null)
        {
            foreach (var id in provinceIds)
                _cultureMismatchProvinces.Add(id);
        }
    }

    // Apply saved map view state (mode + culture highlight)
    public void ApplyMapViewState()
    {
        // MapViewSystem already holds the state; just ensure WorldMapV5 knows about it if needed
        GD.Print($"WorldMap: ApplyMapViewState - Mode={MapViewSystem.Mode}, Highlight={MapViewSystem.CultureHighlight}");
    }

    // M39.2: Issue army move order (stub for CountryAI compatibility)
    public static void IssueArmyMoveOrder(object army, string targetProvinceId)
    {
        GD.Print($"WorldMap: IssueArmyMoveOrder called (stub) - army={army}, target={targetProvinceId}");
    }

    // HOI4-style muted country palette (for MiniMapPanel, CultureLegendPanel)
    private static readonly Color[] CountryPalette = new Color[]
    {
        new Color("#8F8259"), new Color("#7B8471"), new Color("#9C8862"), new Color("#7D5F49"),
        new Color("#6E7F8D"), new Color("#8C6E83"), new Color("#5F8F7A"), new Color("#8A9550"),
        new Color("#6B7B9C"), new Color("#9C7C5B"), new Color("#7A8F6E"), new Color("#8F6E5F"),
        new Color("#5C7A8F"), new Color("#99886B"), new Color("#6E6B8F"), new Color("#8F7A6B"),
        new Color("#4F6E5A"), new Color("#7A6E8F"), new Color("#8E8A7A"), new Color("#6E8F8A"),
        new Color("#9C6B5A"), new Color("#5C8F6B"), new Color("#8F8A6E"), new Color("#6B5E52"),
    };

    /// <summary>
    /// Returns a deterministic color for a country ID (for minimap, legend).
    /// </summary>
    public Color ColorForCountry(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return Colors.Gray;
        int hash = countryId.GetHashCode();
        int index = Math.Abs(hash) % CountryPalette.Length;
        return CountryPalette[index];
    }

    /// <summary>
    /// Returns a color for a culture name (for culture map mode, legend).
    /// </summary>
    public static Color ColorForCulture(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return Colors.Gray;
        int hash = culture.GetHashCode();
        int index = Math.Abs(hash) % CountryPalette.Length;
        return CountryPalette[index];
    }

    // Camera reference (for systems that need it)
    public Camera2D _camera;

    public Camera2D Camera => _camera;

    public void SetCamera(Camera2D camera)
    {
        _camera = camera;
    }

    // Static accessors for backward compatibility
    public static Camera2D StaticCamera => Instance?._camera;
    public static void StaticSetCamera(Camera2D camera)
    {
        if (Instance != null) Instance._camera = camera;
    }

    // Map size (for systems that reference it)
    public static Vector2 MapSize => new Vector2(2160, 1280);

    // Forward country data access to WorldMapV5 for MiniMapPanel
    public static IReadOnlyDictionary<string, WorldMapV5.CountryData> Countries
    {
        get
        {
            if (WorldMapV5.Instance != null)
                return WorldMapV5.Instance.Countries;
            return new Dictionary<string, WorldMapV5.CountryData>();
        }
    }

    // Get country at world position (for MiniMapPanel)
    public static string GetCountryAt(Vector2 worldPos)
    {
        if (WorldMapV5.Instance != null)
            return WorldMapV5.Instance.GetCountryAt(worldPos);
        return null;
    }

    // Get country at screen position (for MiniMapPanel)
    public static string GetCountryAtScreenPos(Vector2 screenPos)
    {
        if (WorldMapV5.Instance != null && WorldMapV5.Instance.Camera != null)
        {
            var viewport = WorldMapV5.Instance.GetViewport();
            var viewportSize = viewport.GetVisibleRect().Size;
            var worldPos = WorldMapV5.Instance.Camera.Position + (screenPos - viewportSize / 2f) / WorldMapV5.Instance.Camera.Zoom.X;
            return WorldMapV5.Instance.GetCountryAt(worldPos);
        }
        return null;
    }

    // Stub members for test compatibility (--map-mode-test, --culture-test, etc.)
    // These are no-ops since WorldMapV5 uses a different architecture.

    public Color ProvinceFillColor(string provinceId) => Colors.White;

    public void UpdateHover(Vector2 screenPos) { }

    public string HoveredProvinceId => "";

    public string CultureTipText => "";

    public bool IsCultureMismatchProvince(string provinceId) => false;

    public bool CultureTipVisible => false;

    public IReadOnlySet<string> CultureMismatchProvinceIds => new HashSet<string>();

    public Control.MouseFilterEnum CultureTipMouseFilter => Control.MouseFilterEnum.Ignore;

    public void SelectAt(Vector2 screenPos) { }

    public Rect2 GetViewportRect() => new Rect2(Vector2.Zero, MapSize);

    public override void _Ready()
    {
        Instance = this;
        GD.Print("WorldMap: Compatibility layer ready (visual map is WorldMapV5)");
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
}

/// <summary>
/// ProvinceDataCache - Static cache for fast province lookups by ID and country.
/// Rebuilds when WorldMap.SetProvinces is called.
/// </summary>
public static class ProvinceDataCache
{
    private static Dictionary<string, ProvinceData> _byId = new();
    private static Dictionary<string, List<ProvinceData>> _byCountry = new();

    public static void Rebuild(List<ProvinceData> provinces)
    {
        _byId.Clear();
        _byCountry.Clear();

        if (provinces == null) return;

        foreach (var p in provinces)
        {
            if (!string.IsNullOrEmpty(p.Id))
                _byId[p.Id] = p;

            if (!string.IsNullOrEmpty(p.CountryId))
            {
                if (!_byCountry.TryGetValue(p.CountryId, out var list))
                    _byCountry[p.CountryId] = list = new List<ProvinceData>();
                list.Add(p);
            }
        }
    }

    public static ProvinceData GetById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        _byId.TryGetValue(id, out var result);
        return result;
    }

    public static List<ProvinceData> GetForCountry(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return new List<ProvinceData>();
        if (_byCountry.TryGetValue(countryId, out var list))
            return list;
        return new List<ProvinceData>();
    }

    public static IReadOnlyDictionary<string, ProvinceData> AllById => _byId;
}