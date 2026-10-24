using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Dünya haritası: GeoJSON'dan ülke poligonları çizer, kamera pan/zoom,
// sol tık ülke seçimi, hover vurgulama, ülke adı etiketleri.
public partial class WorldMap : Node2D
{
    [Signal] public delegate void CountrySelectedEventHandler(string countryId, string countryName);
    [Signal] public delegate void CountryDeselectedEventHandler();

    // M19.2: İl/orduluk seçimi değişti (tek tık, kutu, Shift çoklu)
    [Signal] public delegate void ProvinceSelectionChangedEventHandler(string[] provinceIds);

    public static WorldMap Instance { get; private set; }

    // M39.2: Deniz invazyonu sistemi için hızlı il erişimi (kıyı kontrolü vb.)
    public ProvinceData GetProvinceById(string id) => _provinceById.GetValueOrDefault(id);

    // M13.3: Harita görünüm modları — Political (ülke renkleri), Terrain (biyom),
    // Supply (tacviye), Factories (fabrika yoğunluğu), Resources (kaynak), AirZones (hava Bölgesi).
    // M46.1: Culture il bazlı kültür haritası.
    public enum MapMode { Political, Terrain, Supply, Factories, Resources, AirZones, Culture }
    public MapMode CurrentMapMode { get; private set; } = MapMode.Political;

    // Factories modu için en yüksek fabrika sayısı (parlaklık normalizasyonu).
    private int _maxFactories = 1;

    [Export] public string GeoJsonPath { get; set; } = "res://Data/countries.geo.json";
    [Export] public Vector2 MapSize { get; set; } = new Vector2(2160, 1280);
    [Export] public float KeyPanSpeed { get; set; } = 900f;

    private static readonly Color SelectedColor = new("C9A227");
    private static readonly Color BorderColor = new("0E141B");
    private static readonly Color GoldBorderColor = new("FFD700");
    // Oyuncu ülkesi sınır vurgusu: kalıcı, yarı saydam altın (M3 görsel)
    private static readonly Color PlayerBorderColor = new Color(0.79f, 0.64f, 0.15f, 0.55f);

    // HOI4 tarzı kısık (desatüre) ülke paleti: hepsi bir arada uyumlu görünür.
    private static readonly Color[] CountryPalette =
    {
        new Color("#8F8259"), new Color("#7B8471"), new Color("#9C8862"), new Color("#7D5F49"),
        new Color("#6E7F8D"), new Color("#8C6E83"), new Color("#5F8F7A"), new Color("#8A9550"),
        new Color("#6B7B9C"), new Color("#9C7C5B"), new Color("#7A8F6E"), new Color("#8F6E5F"),
        new Color("#5C7A8F"), new Color("#99886B"), new Color("#6E6B8F"), new Color("#8F7A6B"),
        new Color("#4F6E5A"), new Color("#7A6E8F"), new Color("#8E8A7A"), new Color("#6E8F8A"),
        new Color("#9C6B5A"), new Color("#5C8F6B"), new Color("#8F8A6E"), new Color("#6B5E52"),
    };

    public sealed class Country
    {
        public string Id = "?";
        public string Name = "?";
        public readonly List<Vector2[]> Rings = new();
        public List<Vector2[]> CleanRings = new(); // M13.5: triüleyiciye güvenli temizlenmiş halkalar (MiniMapPanel)
        public readonly List<Polygon2D> Nodes = new();
        public readonly List<Line2D> Outlines = new();
        public readonly List<Line2D> GlowOutlines = new(); // M32.2: kıyı parıltısı çizgileri
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

    // M18.3: İl bazlı çizim ve sahiplik
    private List<ProvinceData> _provinces = new();
    private Dictionary<string, List<Polygon2D>> _provinceNodes = new();

    // M18.4: Performans — il sınırları için bounding-box önbelleği (hit-test önfiltresi)
    private Dictionary<string, Rect2> _provinceBounds = new();
    private int _frameCounter;

    // Task 4 (FPS): il uzamsal indeks — düz grid. FindProvinceAt eskiden 1000 ilin
    // tamamını tek tek tarıyordu (her fare tıklamasında O(n)). Artık yalnızca
    // imlecin bulunduğu hücredeki (ve bitişik 8 hücredeki) iller kontrol edilir.
    private const float ProvinceCellSize = 128f;
    private Dictionary<long, List<int>> _provinceGrid = new();

    // M19.2: Kutu seçimi (marquee) — sol tık sürüklemesiyle bölge seçimi
    private Vector2 _selectStart;
    private Vector2 _selectEnd;
    private bool _selecting;

    // M19.2: Seçim durumu — seçili il ID'leri (bu illardaki ordular seçilmiş sayılır)
    private readonly HashSet<string> _selectedProvinceIds = new();
    private const float SelectDragThreshold = 8f; // ekran pikseli: üzeri sürükleme = kutu seçimi
    private bool _lmbPressed;
    private Vector2 _lmbPressScreen;

    // M19.3: hızlı il erişimi (id → ProvinceData)
    private Dictionary<string, ProvinceData> _provinceById = new();

    // M19.6: Cephe hattı ve ofansif plan
    private sealed class FrontlineSegment
    {
        public Vector2 A;
        public Vector2 B;
        public Color Color = new Color(1f, 0.35f, 0.2f, 0.85f); // kırmızı-turuncu cephes
    }

    private sealed class PlanArrow
    {
        public string SourceProvinceId = "";
        public string TargetProvinceId = "";
        public List<Vector2> Points = new();      // kullanıcının sürüklediği dünya noktaları (görsel)
        public List<string> ProvincePath = new();  // BFS yürütme yolu (ilk illen çıkarılmış)
        public float CreatedDay;
        public Color Color = new Color(0.95f, 0.75f, 0.2f, 0.9f); // ambre plan rengi
    }

    private readonly List<FrontlineSegment> _frontlines = new();
    private bool _frontlinesDirty = true;
    private int _lastActiveWarCount = -1;
    private PlanArrow _activePlan;           // aktif ofansif plan oku
    private bool _drawingPlan;               // Ctrl+LMB sürüklemesiyle plan çizimi devam ediyor
    private Vector2 _planPressScreen;
    private string _planSourceProvinceId = ""; // planın başlangıç ili (kaynak ordular buradan seçilir)
    private readonly List<Vector2> _planPoints = new();
    private const float PlanMinDragPixels = 12f;

    private Country _selected;
    private Country _hovered;
    internal Camera2D _camera;

    // M46.3: Kültür modunda imleç altındaki il — kültür ipucu kutusu bu kayıtla beslenir.
    private string _hoverProvinceId = "";

    // M46.3: Kültür eşleşmeyen iller — sahibinin baskın kültüründen farklı kültür
    // taşıyan iller. Kontur vurgusu bu kümeyle çizilir (M45 uzlaşma/direnniş
    // mekaniğinin haritada gösterilen yüzü).
    private readonly HashSet<string> _cultureMismatchProvinces = new(StringComparer.Ordinal);

    // M46.3: İmleci takip eden kültür ipucu kutusu. Control olduğu için Godot'un
    // kendi metin/çerçeve çizimini kullanır; MouseFilter Ignore olduğundan harita
    // girdisini (sağ tık sürükleme, tekerlek, WASD, il seçimi) engellemez.
    private PanelContainer _cultureTip;
    private Label _cultureTipLabel;
    private bool _cultureTipDirty = true;
    private CanvasLayer _nameLayer;
    private bool _dragging;
    private Vector2 _lastMousePos;
    private Vector2 _rmbPressPos; // M7.5: RMB basılma noktası (tık vs sürükleme ayrımı)
    private float _goldPulse = 0f;
    private bool _cameraNeedsFit = true; // SubViewport gerçek boyutuna ulaşınca kamerayı oturt
    private static readonly Color OceanColor = new("0A2B3A"); // M32.2: koyu turkuaz

    // Görsel iyileştirmeler
    private GradientTexture2D _oceanTexture;
    private GradientTexture2D _vignetteTexture; // M32.2: vinyet + merkez parlaklığı
    private ImageTexture _noiseTexture;        // M32.3: arazi doku gürültüsü (tekrar eden)

    // M32.3 düzeltme: Enlem bazlı renkli bantlar + gürültü doku VARSYILAN KAPALI.
    // Kullanıcı şikayeti: haritada "gürültü koyu doku" ve yatay renkli bantlar
    // istenmeyen görünüyordu. Katman bilinçli (TerrainLayer) ama kullanıcıya
    // açma/kapma kontrolü verilmeden sürekli açıktı. Artık ayar kökünde kapalı.
    private TerrainLayer _terrainLayer;
    private bool _terrainBandsVisible = false;

    // Arazi katmanı açma/kapma (minimap ve ayarlar bu özelliği kullanır).
    public bool TerrainBandsVisible
    {
        get => _terrainBandsVisible;
        set
        {
            if (_terrainBandsVisible == value) return;
            _terrainBandsVisible = value;
            if (_terrainLayer != null)
            {
                _terrainLayer.Visible = value;
                _terrainLayer.QueueRedraw();
            }
            GD.Print($"WorldMap: arazi bantları {(_terrainBandsVisible ? "AÇIK" : "KAPALI")}");
        }
    }
    private float _lastBorderZoom = -1f;

    // M32.3: Ülke renk katmanı yarı saydam → arazi renginin altında görünür.
    private const float CountryTerrainAlpha = 0.55f;

    // Yumuşak kamera (zoom lerp): hedefe doğru yumuşak yaklaşma
    internal bool _zoomAnim;
    internal float _targetZoom = 1f;
    internal Vector2 _targetPos = Vector2.Zero;

    // Görsel iyileştirme #7: Dönem (era) tonu + gündüz/gece terminatörü
    private float _eraAlpha;
    private Color _eraColor = Colors.Transparent;
    private float _terminatorX;
    private GradientTexture2D _terminatorTexture;
    private MapEffects _effects;
    private const float TerminatorPeriod = 60f;       // saniye cinsinden bir tam tur
    private const float TerminatorBandWidthRatio = 0.35f;

    // Düzeltme (kullanıcı şikayeti): haritada sabit duran dikey GRİ BANT görünüyordu
    // (x≈717). Kaynak: gündüz/gece terminatörü — 756 birim genişliğinde yumuşak
    // kenarlı gece bandı haritayı ikiye böldüğü için "boşluk/bant" gibi duruyor.
    // Vinyet de köşeleri koyutup haritayı kirli gösteriyordu.
    // Her iki katman da bilinçli ama VARSYILAN KAPALI; açma/kapama ayarı aşağıda.
    private bool _terminatorVisible = false; // gündüz/gece bandı (varsayılan kapalı)
    private bool _vignetteVisible = false;   // vinyet + merkez parıltısı (varsayılan kapalı)

    /// Gündüz/gece terminatör bandı açık mı? (varsayılan kapalı)
    public bool TerminatorVisible
    {
        get => _terminatorVisible;
        set { _terminatorVisible = value; _effects?.QueueRedraw(); }
    }

    /// Vinyet + merkez parıltısı açık mı? (varsayılan kapalı)
    public bool VignetteVisible
    {
        get => _vignetteVisible;
        set { _vignetteVisible = value; _effects?.QueueRedraw(); }
    }

    public override void _Ready()
    {
        Instance = this;
        _nameLayer = new CanvasLayer { Layer = 1 };
        AddChild(_nameLayer);

        // M32.3: Arazi doku katmanı — ülkelerin ALTINDA çizilir (BuildFromGeoJson'dan önce eklendi).
        _terrainLayer = new TerrainLayer();
        _terrainLayer.Visible = _terrainBandsVisible; // varsayılan kapalı (kullanıcı isteği)
        AddChild(_terrainLayer);

        // Küçük gürültü doku (FastNoiseLite, tek seferlik üretim, haritaya döşenir).
        var noise = new FastNoiseLite
        {
            Seed = 202401,
            Frequency = 0.08f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
        };
        var noiseSize = new Vector2I(256, 256);
        var noiseImg = Image.CreateEmpty(noiseSize.X, noiseSize.Y, false, Image.Format.Rgba8);
        for (int y = 0; y < noiseSize.Y; y++)
        {
            for (int x = 0; x < noiseSize.X; x++)
            {
                float n = noise.GetNoise2D(x, y);
                float g = Mathf.Clamp(n * 0.5f + 0.5f, 0f, 1f); // -1..1 → 0..1 gri tonu
                noiseImg.SetPixel(x, y, new Color(g, g, g, 1f));
            }
        }
        _noiseTexture = ImageTexture.CreateFromImage(noiseImg);
        GD.Print($"WorldMap: arazi gürültü doku hazır ({noiseSize.X}x{noiseSize.Y})");

        // Okyanus degrade dokusu (kutuplar koyu, ekvator daha mavi)
        _oceanTexture = new GradientTexture2D
        {
            Width = 16,
            Height = 512,
            Fill = 0, // GradientFillMode.Linear = 0
            FillFrom = new Vector2(0f, 0f),
            FillTo = new Vector2(0f, 1f),
            Gradient = BuildOceanGradient(),
        };

        SetProcessInput(true);

        BuildFromGeoJson();
        BuildCamera();
        BuildZoomLabels();

        // Görsel #7: gündüz/gece terminatörü + dönem tonu (era) — ülkelerin ÜSTÜNDE,
        // etiketlerin (CanvasLayer 1) ALTINDA çizilir. Yılda bir kez hesaplanır,
        // sahne başına bir kez dokusu üretilir → performans maliyeti yok.
        _terminatorTexture = new GradientTexture2D
        {
            Width = 64,
            Height = 1,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0f, 0f),
            FillTo = new Vector2(1f, 0f),
            Gradient = BuildTerminatorGradient(),
        };

        // M32.2: Vinyet + merkez parlaklığı — merkez hafif turkuaz parıltı, kenarlar koyu.
        _vignetteTexture = new GradientTexture2D
        {
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 1f),
            Gradient = BuildVignetteGradient(),
        };
        _effects = new MapEffects();
        AddChild(_effects); // BuildFromGeoJson'dan SONRA eklendi → ülkelerin üstüne çizer

        // M3: Oyuncu ülkesi değişince harita vurgusunu yenile
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryChanged += OnPlayerCountryChanged;

        // M7: Toprak değişimi (ilhak/kapitülasyon) sonrası haritayı yeniden boya
        if (GameManager.IsAvailable)
            GameManager.Instance.CountryOwnershipChanged += OnCountryOwnershipChanged;

        RefreshOwnershipColors();
        QueueRedraw();

        // M18.3: İlleri yükle ve çiz; sahiplik değişikliklerini dinle
        var provinceSource = new ProceduralProvinceSource();
        SetProvinces(provinceSource.LoadProvinces());
        ProvinceOwnershipSystem.OnOwnerChanged += OnProvinceOwnerChanged;

        BuildCultureTip();

        // M46.4: Kayıttan yüklenen harita görünümünü uygula (mod + kültür vurgusu).
        // Yeni oyunda MapViewSystem başlangıç değerinde olduğundan bu bir no-op'tur.
        ApplyMapViewState();

        GD.Print($"WorldMap: hazır — kamera zoom={_camera?.Zoom.X:0.00} pos={_camera?.Position}, viewport={GetViewportRect().Size}");
    }

    // Dikey okyanus degradesi: koyu turkuaz (kutuplar koyu, ekrangen orta daha canlı turkuaz)
    private static Gradient BuildOceanGradient()
    {
        var g = new Gradient();
        g.SetColor(0, new Color("#082030"));   // kutup: çok koyu turkuaz-navy
        g.AddPoint(0.45f, new Color("#0E4A5A")); // alt-ekvator: canlı koyu turkuaz
        g.AddPoint(0.75f, new Color("#0C3A4A")); // üst-ekvator: koyu turkuaz
        g.SetColor(1, new Color("#082030"));    // kutup: çok koyu turkuaz-navy
        return g;
    }

    public override void _Draw()
    {
        // Okyanus: koyu mavi degrade (kutuplar koyu -> ekvator hafif mavi)
        // Kameranın gördüğü TÜM alanı kaplayacak kadar geniş: kamera harita
        // sınırlarının dışına taşsa bile (zoom-out, kenarlar) beyaz/gri boşluk kalmasın.
        var camSize = _camera != null
            ? GetViewportRect().Size / _camera.Zoom
            : GetViewportRect().Size + MapSize;
        var camPos = _camera != null ? _camera.Position : MapSize / 2f;
        float halfW = Mathf.Max(camSize.X, MapSize.X) * 0.5f + 2000f;
        float halfH = Mathf.Max(camSize.Y, MapSize.Y) * 0.5f + 2000f;
        var big = new Rect2(camPos.X - halfW, camPos.Y - halfH, halfW * 2f, halfH * 2f);
        if (_oceanTexture != null)
            DrawTextureRect(_oceanTexture, big, false);
        else
            DrawRect(big, OceanColor);

        // Harita sınırı (ince, yumuşak mavi)
        DrawRect(new Rect2(Vector2.Zero, MapSize), new Color(0.12f, 0.25f, 0.45f), false, 2f);

        // M19.1: Birlik sayaçları (ordunun bulunduğu il merkezinde çember)
        DrawArmies();

        // M19.2: Seçim — kutu seçimi çerçevesi + seçili il vurguları
        DrawSelection();

        // M32.5: İl sınırları — her il poligonunun etrafında ince çizgi
        DrawProvinceBorders();

        // M19.3: Hareket emri rotaları (çizgi + hedef/ara nokta çemberleri + ok baştan)
        DrawMovementPaths();

        // M19.6: Cephe hatları (düşman sınırları) + ofansif plan okları
        EnsureFrontlines();
        DrawFrontlines();
        DrawPlan();

        // M46.3: Kültür eşleşmeyen illerin kontur vurgusu (kültür modunda).
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
                // M19.4: hareket sırasında mevcut il ve bir sonraki hedef arası interpolasyon.
                var start = _provinceById.GetValueOrDefault(army.ProvinceId);
                var next = _provinceById.GetValueOrDefault(army.WaypointIds[army.MovementPathIndex]);
                if (start != null && next != null)
                {
                    float t = 1f - army.DaysRemainingInHop / army.DaysPerProvince; // 0=başlangıç, 1=varış
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

            // M32.5: Birlik ikonu — daire içinde ok/elmas işareti (hareket yönünde)
            Color armyColor;
            float armyRadius;
            switch (army.Domain)
            {
                case ArmySystem.UnitDomain.Naval:
                    armyColor = new Color(0.2f, 0.6f, 1f, 0.85f); // mavi — deniz
                    armyRadius = 7f;
                    break;
                case ArmySystem.UnitDomain.Air:
                    armyColor = new Color(1f, 0.9f, 0.2f, 0.85f); // sarı — hava
                    armyRadius = 5f;
                    break;
                default: // Kara (varsayılan — mevcut davranış)
                    armyColor = new Color(1f, 0.2f, 0.2f, 0.8f); // kırmızı — kara
                    armyRadius = 6f;
                    break;
            }
            DrawCircle(drawPos, armyRadius, armyColor);

            // Ok işareti: hareket yönünde (moving) veya sabit yukarı (idle)
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

    public override void _ExitTree()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryChanged -= OnPlayerCountryChanged;
        if (GameManager.IsAvailable)
            GameManager.Instance.CountryOwnershipChanged -= OnCountryOwnershipChanged;
        ProvinceOwnershipSystem.OnOwnerChanged -= OnProvinceOwnerChanged;
        if (Instance == this) Instance = null;
    }

    // Oyuncu ülkesi değişti → sahiplik renkleri + sınır stilleri yenilenir
    private void OnPlayerCountryChanged(string id)
    {
        _lastBorderZoom = -1f; // sınır renkleri yeniden hesaplansın
        RefreshOwnershipColors();
    }

    // M7: Toprak değişti (ilhak/kapitülasyon) → haritayı yeniden boya
    private void OnCountryOwnershipChanged()
    {
        _lastBorderZoom = -1f;
        RefreshOwnershipColors();
        GD.Print("WorldMap: sahiplik renkleri güncellendi (ilhak)");
    }

    // Bu ülke oyuncunun ülkesi mi? (id'ler harita GeoJSON zincirinden gelir, ham eşleşme)
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
        AnimateSelectedBorder(fDelta);
        UpdateBorderWidthsByZoom();
        AnimateZoom(fDelta);

        // M46.3: Kültür ipucu kutusu — metin değiştiyse kur, konumu imlece tut.
        if (_cultureTipDirty) RefreshCultureTipText();
        PositionCultureTip();

        // M18.4 + Task 4: FPS ölçümü (her 120 karede bir).
        // İller açık/kapalı ve zoom seviyesiyle birlikte loglanır; böylece
        // "iller açık/kapalı, farklı zoom seviyelerinde" performans raporu çıkar.
        _frameCounter++;
        if (_frameCounter % 120 == 0)
        {
            float fps = 1f / Mathf.Max((float)delta, 0.0001f);
            float zoom = _camera?.Zoom.X ?? 0f;
            // process: kare başına (fizik+proses) süre ms; çizim tarafı
            // ayrı render thread'inde olduğu için draw-call / primitive sayısıyla ölçülüyor.
            float processMs = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess);
            int drawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            int prims = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            bool provincesOn = _provinceNodes != null && _provinceNodes.Count > 0;
            bool bordersVisible = _camera == null || _camera.Zoom.X >= 0.5f;
            GD.Print($"[PERF] FPS={fps:F1} process={processMs:F2}ms drawcalls={drawCalls} prims={prims} " +
                     $"zoom={zoom:F2} provinces={_provinces.Count} (fill={provincesOn}, borders={bordersVisible})");
        }

        // M19.6: Savaş sayısı değişimi → cephe hatlarını yeniden hesapla
        int activeWarCount = WarSystem.ActiveWars.Count;
        if (activeWarCount != _lastActiveWarCount)
        {
            _lastActiveWarCount = activeWarCount;
            _frontlinesDirty = true;
        }

        // Görsel #7: Dönem (era) tonu — yıl bazlı, yumuşak geçiş.
        if (GameManager.IsAvailable)
        {
            int year = GameManager.Instance.CurrentDate.Year;
            float targetAlpha = year <= 1938 ? 0f : (year <= 1941 ? 0.04f : 0.07f);
            Color targetColor = year <= 1938 ? Colors.Transparent
                                : (year <= 1941 ? new Color(1f, 0.4f, 0.2f, 1f)   // 1939-41: sıcak, savaş öncesi
                                               : new Color(0.6f, 0.2f, 0.15f, 1f)); // 1942+: karanlık, savaş yılları
            float lerpSpeed = Mathf.Clamp(fDelta * 2f, 0f, 1f);
            _eraAlpha = Mathf.Lerp(_eraAlpha, targetAlpha, lerpSpeed);
            _eraColor = _eraColor.Lerp(targetColor, lerpSpeed);
        }

        // Terminatör: gerçek zamanla (oyun hızı bağımsız) yavaş doğu→batı sweep.
        float t = (Time.GetTicksMsec() / 1000f / TerminatorPeriod) % 1f;
        _terminatorX = t * MapSize.X;

        // Efekt katmanını her kare yeniden çiz (terminatör hareket + era değişimi)
        _effects?.QueueRedraw();

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

    // Yumuşak zoom: kamerayı hedef zoom/pozisyona lerp ile yaklaştırır.
    private void AnimateZoom(float delta)
    {
        if (!_zoomAnim || _camera == null) return;
        float t = Mathf.Clamp(delta * 14f, 0f, 1f); // ~0.07 saniyede oturur
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

    // Zoom değiştikçe görünür alan genişler; pan sırasında hedef de sınırlansın
    private Vector2 ClampedTargetPos()
    {
        return ClampCameraPos(_targetPos);
    }

    // Zoom'a göre sınır çizgisi kalınlığını güncelle (ekranda ~1.1px kalınlıkta kalır).
    // Seçili ülkenin altın çerçevesi AnimateSelectedBorder'da ayrı yönetilir.
    private void UpdateBorderWidthsByZoom()
    {
        if (_camera == null) return;
        float zoom = _camera.Zoom.X;
        if (Mathf.Abs(zoom - _lastBorderZoom) < 0.03f) return;
        _lastBorderZoom = zoom;

        float w = Mathf.Clamp(1.1f / zoom, 0.35f, 3f); // normal sınır: ince

        foreach (var c in _countries)
        {
            if (c == _selected) continue; // seçili: altın pulse ayrı
            Color borderColor = IsPlayerCountry(c) ? PlayerBorderColor : BorderColor;
            foreach (var outline in c.Outlines)
            {
                if (!GodotObject.IsInstanceValid(outline)) continue;
                outline.Width = w;
                outline.DefaultColor = borderColor;
            }
            // M32.2: kıyı parıltısı kalınlığını ana sınırla orantılı tut (daha geniş, şeffaf).
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
        // ----- Sağ tık sürükleme ile pan -----
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _dragging = mb.Pressed;
                if (mb.Pressed)
                {
                    _lastMousePos = mb.Position;
                    // M19.6: RMB pan başlar → açık olan plan çizimini iptal et
                    if (_drawingPlan)
                    {
                        _drawingPlan = false;
                        _planPoints.Clear();
                        _planSourceProvinceId = null;
                        QueueRedraw();
                    }
                    _rmbPressPos = mb.Position; // M7.5: tık mı sürükleme mi ayrımı için
                }
                else
                {
                    _lastMousePos = Vector2.Zero;
                    // M7.5 / M19.3: hareket yoksa tık say
                    if (mb.Position.DistanceTo(_rmbPressPos) < 6f)
                    {
                        var worldPos = _camera.Position + (mb.Position - GetViewportRect().Size / 2f) / _camera.Zoom;
                        // M19.3: seçili illerde ordu varsa önce hareket emrini dene
                        if (!IssueMoveOrders(worldPos, mb.ShiftPressed))
                            HandleRightClickOrder(mb.ShiftPressed); // M7.5 savaş/cephe emri (yoksa sessiz dönüş)
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

        // ----- Sürüklerken haritayı kaydır -----
        if (e is InputEventMouseMotion motion && _dragging)
        {
            Vector2 deltaPos = motion.Position - _lastMousePos;
            _lastMousePos = motion.Position;
            if (_camera != null && deltaPos != Vector2.Zero)
            {
                _zoomAnim = false; // manuel pan -> zoom animasyonu iptal
                _camera.Position -= deltaPos / _camera.Zoom.X;
                _targetPos = _camera.Position; // hedefi de senkronize et
                ClampCamera();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        // ----- Sol tık seçim / kutu seçimi / hover (M19.2) -----
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
                    // M19.6: Ctrl+LMB → ofansif plan çizimi (normal il seçimiyle çakışmaz)
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

                    // Çift tık → ülke ataması (kutu seçimi ile çakışmaz)
                    if (lmb.DoubleClick)
                    {
                        var c = FindCountryAt(worldPos);
                        if (c != null && GameManager.IsAvailable)
                        {
                            GameManager.Instance.SetPlayerCountry(c.Id);
                            GD.Print($"WorldMap: Double-click → SetPlayerCountry({c.Id})");
                        }
                        GetViewport().SetInputAsHandled();
                        return;
                    }

                    // Sol tık basım: başlangıç noktası kaydı. Tek tık mı sürükleme mi, bırakılınca belli olur.
                    _lmbPressed = true;
                    _lmbPressScreen = viewportPos;
                    _selectStart = worldPos;
                    _selectEnd = worldPos;
                    _selecting = false;
                    GetViewport().SetInputAsHandled();
                    return;
                }

                // M19.6: Ctrl+LMB bırakıldı → ofansif planı finalize et
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

                // Sol tık bırakıldı: tek tık → il seçimi; sürükle → kutu seçimi
                if (_lmbPressed)
                {
                    _lmbPressed = false;
                    _selecting = false;
                    bool shift = lmb.ShiftPressed;

                    if (viewportPos.DistanceTo(_lmbPressScreen) < SelectDragThreshold)
                    {
                        // TEK TIK: imlecin altındaki il (var olan ülke seçimiyle birlikte çalışır)
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

                        SelectAt(worldPos); // mevcut ülke seçimi (CountryInfoPanel vb.)
                    }
                    else
                    {
                        // KUTU SEÇİMİ: dikdörtgenin içinde merkez noktası olan tüm iller
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

                // M19.2: sol tıkla sürükle → kutu seçimi bitiş noktasını takip et (RMB pan ile çakışmaz)
                if (_lmbPressed && _camera != null)
                {
                    _selectEnd = worldPos;
                    if (viewportPos.DistanceTo(_lmbPressScreen) > SelectDragThreshold) _selecting = true;
                }

                // M19.6: Ctrl+LMB sürüklemesi → plan ok noktalarını takip et
                if (_drawingPlan && _camera != null)
                {
                    if (_planPoints.Count == 0 || _planPoints[_planPoints.Count - 1].DistanceTo(worldPos) > 8f)
                        _planPoints.Add(worldPos);
                    QueueRedraw();
                }
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

    // M7.5: Sağ tık (sürüklemesiz) savaş emri:
    //   Normal RMB tık → düşman ülkesine OFANSİF EMRİ (aç/kapat)
    //   Shift+RMB tık  → CEPHE DURUŞU (savunma, aç/kapat)
    // Pan davranışına dokunmaz: sadece <6px hareketli tıklar buralara gelir.
    private void HandleRightClickOrder(bool shiftPressed)
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer) return;
        if (HoveredBlocksMap(GetViewport().GuiGetHoveredControl())) return;
        if (_camera == null) return;

        // Tıklanan dünyada konumu bul
        Vector2 viewportPos = GetViewport().GetMousePosition();
        Vector2 worldPos = _camera.Position + (viewportPos - GetViewportRect().Size / 2f) / _camera.Zoom;
        var country = FindCountryAt(worldPos);
        if (country == null) return;

        string playerId = GameManager.Instance.PlayerCountryId;

        // Kendi ülkesine emir yok
        if (country.Id == playerId) return;

        var war = WarSystem.GetWarWith(country.Id);
        if (war == null)
        {
            GD.Print($"[Map] {country.Name}: savaşta değilsiniz — önce DECLARE WAR gerekli " +
                     $"(CountryInfoPanel, {WarSystem.DeclareWarPPCost} PP)");
            return;
        }

        if (shiftPressed)
        {
            // Shift+RMB → cephe duruşu
            WarSystem.ToggleFrontline(country.Id);
        }
        else
        {
            // RMB → ofansif emri aç/kapat
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
        _zoomAnim = false; // kullanıcı pan yaparken zoom animasyonu iptal
        _camera.Position += dir.Normalized() * (KeyPanSpeed / _camera.Zoom.X) * delta;
        _targetPos = _camera.Position; // hedefi senkron tut
        ClampCamera();
    }

    private void ClampCamera()
    {
        _camera.Position = ClampCameraPos(_camera.Position);
    }

    // M13.3: Harita görünüm modunu değiştirir ve haritayı yeniden boyar.
    public void SetMapMode(MapMode mode)
    {
        if (CurrentMapMode == mode) return;
        CurrentMapMode = mode;
        MapViewSystem.SetMode(mode); // M46.4: mod Save/Load'da korunsun
        _factoriesComputed = false; // fabrika sayımı veriye bağlı → yeniden hesapla
        RefreshOwnershipColors();
        RefreshProvinceColors();
        QueueRedraw();
        GD.Print($"WorldMap: harita modu = {mode}");
    }

    // M13.3: Geçerli harita moduna göre bir ülkenin dolgu rengi.
    private Color ModeColor(Country c)
    {
        if (c == null) return Colors.Transparent;
        var data = CountryDatabase.Get(c.Id);

        switch (CurrentMapMode)
        {
            case MapMode.Political:
                return c.MapColor;

            case MapMode.Terrain:
                // Biyom katmanının görünmesi için ülke dolgusu tamamen şeffaf.
                return Colors.Transparent;

            case MapMode.Factories:
            {
                int factories = data?.TotalFactories ?? 0;
                EnsureMaxFactories();
                float t = _maxFactories > 0 ? Mathf.Clamp((float)factories / (float)_maxFactories, 0f, 1f) : 0f;
                // Az → koyu gri, çok → parlak altın (fabrika yoğunluğu haritası)
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
                // Baskın kaynağa göre renk (öncelik sırası). Kaynak yok → şeffaf.
                if (data.Oil > 0) return new Color("#8B4513");     // koyu kahverengi — petrol
                if (data.Steel > 0) return new Color("#708090");   // slate gri — çelik
                if (data.Rubber > 0) return new Color("#2E8B57");  // deniz yeşili — lastik
                if (data.Aluminum > 0) return new Color("#B0C4DE");// açık mavi — alüminyum
                if (data.Tungsten > 0) return new Color("#4B0082");// indigo — volfram
                if (data.Chromium > 0) return new Color("#DC143C");// kızıl — kurya
                return Colors.Transparent;
            }

            case MapMode.Supply:
                // Tacviye/comşuluk: soluk mavi vurgu
                return new Color(0.15f, 0.45f, 0.85f, 0.45f);

            case MapMode.AirZones:
                // Hava bölge overlay'i: soluk mavi-purpur
                return new Color(0.5f, 0.35f, 0.9f, 0.35f);

            case MapMode.Culture:
                // M46.1: kültür bilgisini il katmanı taşır; ülke dolgusu şeffaf
                // bırakılmazsa renkler kültürü gizler.
                return Colors.Transparent;

            default:
                return c.MapColor;
        }
    }

    // M13.3: Ülke ID'sine göre mod rengi (İl çizimi için).
    private Color ModeColorForCountryId(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return Colors.Transparent;
        var country = _countries.FirstOrDefault(c => c.Id == countryId);
        if (country == null) return Colors.Transparent;
        return ModeColor(country);
    }

    private bool _factoriesComputed;
    // M13.3: Factories modu için tüm ülkeler arasında en yüksek fabrika sayısı (parlaklık normalizasyonu).
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
        GD.Print($"WorldMap: factories modu normalizasyonu — max fabrika={_maxFactories}");
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

                // M7: İlhaç edilmiş ülke → sahibinin rengiyle boyanır (toprak değişimi)
                var data = CountryDatabase.Get(c.Id);
                if (data != null && data.IsAnnexed && !string.IsNullOrEmpty(data.OwnerId))
                {
                    var owner = CountryDatabase.Get(data.OwnerId);
                    if (owner != null && owner.Id != c.Id)
                    {
                        // Sahibin harita rengi (palet üretimi) — hafif koyulaştır ki "ilhak" okunsun
                        target = ColorForId(owner.Id).Darkened(0.12f);
                    }
                }

                if (c == _selected) target = SelectedColor;
                else if (c == _hovered) target = c.MapColor.Lightened(0.25f);
                else
                {
                    // M32.3: ülke renk katmanı yarı saydam — arazi renginin altında görünür.
                    target = new Color(target.R, target.G, target.B, CountryTerrainAlpha);
                }
            }

            foreach (var node in c.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = target;
            // Sınır: normal koyu, oyuncu ülkesi → yarı saydam altın vurgu (seçili hariç)
            foreach (var outline in c.Outlines)
                if (GodotObject.IsInstanceValid(outline) && c != _selected)
                    outline.DefaultColor = IsPlayerCountry(c) ? PlayerBorderColor : BorderColor;
        }
    }

    private static Color ColorForId(string id)
    {
        // Antarktika ve buzul toprakları: gri-buz tonu (renkli duruyor, çirkin olur)
        if (id == "ATA" || id == "ATF") return new Color("#93A5B1");

        unchecked
        {
            int hash = 0; foreach (char ch in id) hash = hash * 31 + ch;
            var baseColor = CountryPalette[Mathf.Abs(hash) % CountryPalette.Length];
            // Deterministik hafif ton kayması: aynı paletten komşular ayrışsın
            int v = (hash >> 12) % 5;
            if (v == 0) return baseColor.Darkened(0.08f);
            if (v == 4) return baseColor.Lightened(0.08f);
            return baseColor;
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

                // M32.2: Kıyı parıltısı — sınırın arkasında geniş, şeffaf turkuaz çizgi.
                // Ana sınır çizgisinden önce eklendiğinden ana sınır üstte görünür.
                var glow = new Line2D
                {
                    Points = ring,
                    DefaultColor = new Color(0.3f, 0.8f, 0.9f, 0.20f), // turkuaz parlak, şeffaf
                    Width = 3.6f,
                    Closed = true,
                };
                AddChild(glow); country.GlowOutlines.Add(glow);

                // İnce ana sınır çizgisi; zoom'a göre _Process'te güncellenecek
                var outline = new Line2D { Points = ring, DefaultColor = BorderColor, Width = 1.1f, Closed = true };
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
        if (points.Count >= 3)
        {
            var ring = points.ToArray();
            // Antarktika (ve kutup bantlarında çok uzayan poligonlar) harita
            // sınırının çok ötesine taşar: endüstri/güney kutup halkaları -90
            // enlemine kadar iner. Project() bunu MapSize.Y'nin çok altına
            // yazar → harita tahta çerçevesinin dibini aşan devasa gri blok.
            // Bu yüzden her halkayı harita sınırı içine kırpıyoruz (Sutherland-
            // Hodgman tarzı dikey kırpma: sol/sağ/üst/alt sınırlara karşı).
            ring = ClipRingToMap(ring, MapSize);
            country.Rings.Add(ring);
            var cleaned = CleanRing(ring);
            if (cleaned != null) country.CleanRings.Add(cleaned);
        }
    }

    // Halkayı [0..MapSize] dikdörtgenine kırpar (Antarktika kırpma sorunu).
    // Sutherland-Hodgman: sınırın dışında kalan kısımlar kesilir, köşelerde
    // sınır noktası eklenir. Çok küçük/bozulmuş halkada orijinali döndürür.
    private static Vector2[] ClipRingToMap(Vector2[] ring, Vector2 mapSize)
    {
        const float pad = 1.5f; // kenar hizasını örterek kırpma toleransı
        var bounds = new Rect2(0f, 0f, mapSize.X, mapSize.Y);
        bool inside = true;
        foreach (var p in ring)
        {
            if (!new Rect2(0f, 0f, mapSize.X + pad, mapSize.Y + pad).HasPoint(p)) { inside = false; break; }
        }
        if (inside) return ring; // tamamen içeride: değiştirme

        var poly = new List<Vector2>(ring);

        Vector2 Intersect(Vector2 a, Vector2 b, float edge, bool horizontal, bool greater)
        {
            // Yatay kenar: y=edge; dikey kenar: x=edge.
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

        // Alt (y >= 0), üst (y <= Y), sol (x >= 0), sağ (x <= X) sınırlarına karşı kırp.
        poly = ClipSide(poly, 0f, false, false, true);          // x >= 0
        if (poly.Count < 3) return ring;
        poly = ClipSide(poly, mapSize.X, false, true, false);   // x <= X
        if (poly.Count < 3) return ring;
        poly = ClipSide(poly, 0f, true, false, true);          // y >= 0
        if (poly.Count < 3) return ring;
        poly = ClipSide(poly, mapSize.Y, true, true, false);   // y <= Y
        if (poly.Count < 3) return ring;

        return poly.ToArray();
    }

    // GeoJSON halkasını DrawPolygon için temizler: neredeyse aynı noktaları,
    // kapalı-döngü baş/son çiftini ve collinear (doğrusal) ardışık noktaları kaldırır.
    // Godot'un CanvasItem.DrawPolygon triüleyicisi bu bozuklukları reddeder.
    private static Vector2[] CleanRing(Vector2[] ring)
    {
        const float eps2 = 1e-4f; // ~0.01 dünya birimi karesi
        var clean = new List<Vector2>(ring.Length);
        for (int i = 0; i < ring.Length; i++)
        {
            var p = ring[i];
            if (clean.Count > 0 && (clean[clean.Count - 1] - p).LengthSquared() < eps2)
                continue;
            clean.Add(p);
        }
        if (clean.Count > 1 && (clean[0] - clean[clean.Count - 1]).LengthSquared() < eps2)
            clean.RemoveAt(clean.Count - 1);
        if (clean.Count < 3) return null;

        // Collinear ardışık noktaları çıkar (triangulator'a zero-area üçgen doğurur).
        int n = clean.Count;
        var simplified = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            var prev = clean[(i - 1 + n) % n];
            var cur = clean[i];
            var next = clean[(i + 1) % n];
            var a = cur - prev;
            var b = next - cur;
            if (Mathf.Abs(a.X * b.Y - a.Y * b.X) < 1e-6f) continue;
            simplified.Add(cur);
        }
        if (simplified.Count < 3) return null;
        return simplified.ToArray();
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

    // M15.5: Deniz/okyanus etiketi veri yapısı (Data/sea_labels.json)
    private class SeaLabelData
    {
        public string Name { get; set; }
        public double Lon { get; set; }
        public double Lat { get; set; }
        public string Type { get; set; } // "ocean" | "sea"
        public float MinZoom { get; set; }
        public float MaxZoom { get; set; }
    }

    private void BuildZoomLabels()
    {
        // Data/sea_labels.json'dan yükle (hardcoded yerine veri odaklı)
        const string path = "res://Data/sea_labels.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[WorldMap] {path} bulunamadı — deniz etiketleri yüklenemedi");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            var labels = JsonSerializer.Deserialize<List<SeaLabelData>>(FileAccess.GetFileAsString(path), options);
            if (labels == null) return;

            int oceanCount = 0, seaCount = 0;
            foreach (var d in labels)
            {
                // Projeksiyon: lon/lat → dünya koordinatları
                Vector2 anchor = Project(d.Lon, d.Lat);
                float minZoom = d.MinZoom > 0 ? d.MinZoom : 0f;
                float maxZoom = d.MaxZoom > 0 ? d.MaxZoom : 999f;

                // Okyanus/deniz tipine göre stil (aynı AddMapLabel, farklı görünüm)
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

            GD.Print($"WorldMap: {oceanCount} okyanus + {seaCount} deniz etiketi yüklendi (sea_labels.json)");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[WorldMap] sea_labels.json okunamadı: {e.Message}");
        }
    }

    private void AddMapLabel(List<MapLabel> list, string text, Vector2 anchor, float minZoom, float maxZoom, bool isOcean)
    {
        // "Harfler hafif aralıklı" görünüm: karakterler arasına ince boşluk (U+2009) ekle.
        // Okyanuslar biraz daha geniş aralıklı, denizler daha sıkı.
        string spacing = isOcean ? " " : " "; // thin space / hair space
        text = string.Join(spacing, text.ToCharArray());

        var label = new Label { Text = text, Visible = false };
        label.MouseFilter = Control.MouseFilterEnum.Ignore;

        // M15.5: Font — koyu temaya uygun, soluk; ülke etiketlerinden (beyaz 13pt) ayırt edilir
        label.AddThemeFontSizeOverride("font_size", isOcean ? 18 : 14);
        label.AddThemeColorOverride("font_color", new Color(0.65f, 0.82f, 1f, isOcean ? 0.85f : 0.75f));
        // Hafif gölge — harita mavi tonlarında okunur
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0.1f, 0.2f, 0.85f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);

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
            bool bigEnough = wpx > 55f && hpx > 26f; // küçük ülkelerin etiketleri zoom artınca görünür
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
        // Animasyon hedeflerini başlangıç durumuna ayarla
        _targetZoom = _camera.Zoom.X;
        _targetPos = _camera.Position;
        _zoomAnim = false;
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
        // Animasyon hedeflerini senkronize et (anında uygulanacak)
        _targetZoom = fit;
        _targetPos = _camera.Position;
        _zoomAnim = false;
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
        float z = Mathf.Clamp(_targetZoom * factor, 0.25f, 6f);
        if (Mathf.IsEqualApprox(z, _targetZoom) && !_zoomAnim) return;

        // Zoom, imleç altındaki noktayı sabit tutar; hedefe doğru lerp ile yumuşak gider.
        // Ardışık tekerlek hareketleri hedef üzerinde birikir (kekleşme yok).
        var center = GetViewportRect().Size / 2f;
        var before = (screenPos - center) / _targetZoom + _targetPos;
        var after = (screenPos - center) / z + _targetPos;
        _targetZoom = z;
        _targetPos = ClampCameraPos(_camera.Position + (before - after));
        _zoomAnim = true;
    }

    // Pozisyonu harita sınırları içinde tut (lerp hedefi ve anlık pozisyon için)
    internal Vector2 ClampCameraPos(Vector2 pos)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, -400f, MapSize.X + 400f),
            Mathf.Clamp(pos.Y, -400f, MapSize.Y + 400f));
    }

    /// <summary>
    /// Fare dünya konumuna göre hover + kültür ipucu takibini günceller.
    /// Internal: headless test, viewport fare konumunu sentezleyemediğinden
    /// (WarpMouse headless'te etkisiz) `_Input` yerine doğrudan çağırır.
    /// </summary>
    internal void UpdateHover(Vector2 worldPos)
    {
        // M46.3: Kültür modunda imleç altındaki il takibi — ipucu kutusu bu kayıtla beslenir.
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

    /// <summary>
    /// Dünya konumundaki ülkeyi seçer (sol tık yolu). Internal: headless test
    /// viewport fare konumunu sentezleyemediğinden `_Input` yerine doğrudan çağırır.
    /// </summary>
    internal void SelectAt(Vector2 worldPos) => SetSelected(FindCountryAt(worldPos));

    private void SetSelected(Country c)
    {
        if (_selected == c) return;
        var old = _selected; _selected = c;

        // TANI: panel kaybolma teşhisi için seçim değişimlerini logla
        GD.Print($"[Diag] WorldMap.SetSelected: {(old?.Id ?? "—")} → {(c?.Id ?? "—")}");

        if (old != null)
        {
            // Eski ülkenin sınırı normal stile dönsün (zoom'a göre hesaplanacak)
            _lastBorderZoom = -1f;
            foreach (var node in old.Nodes) if (GodotObject.IsInstanceValid(node)) node.Color = old == _hovered ? old.MapColor.Lightened(0.25f) : old.MapColor;
            // DÜZELTME: ülke→ülke geçişinde CountryDeselected emit ETME —
            // paneli HidePanel()+FadeOut() yarışıyla kendiliğinden kayboluyordu.
            // Deselected yalnızca seçim TAMAMEN kaldırılınca (boş alan tıklaması) emit et.
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

    // Seçimi tamamen kaldır (panelde X / ESC / boş alan) — Deselected tetiklenir.
    public void ClearSelection() => SetSelected(null);

    // M19.2: Seçili iller (ve üzerindeki ordular). Salt okunur dışa açılır.
    public IReadOnlySet<string> SelectedProvinceIds => _selectedProvinceIds;

    // Tek tık / kutu sonucu il seçimi. add=true → mevcut seçime ekler, false → değiştirir.
    public void SelectProvince(string provinceId, bool add = false)
    {
        if (!add) _selectedProvinceIds.Clear();
        if (!string.IsNullOrEmpty(provinceId)) _selectedProvinceIds.Add(provinceId);
        EmitSignal(SignalName.ProvinceSelectionChanged, ProvinceIdsToArray());
        QueueRedraw();
    }

    // Seçimi temizle (UI / ESC / boş-alan işlemlerinden çağrılabilir).
    public void ClearProvinceSelection()
    {
        if (_selectedProvinceIds.Count == 0) return;
        _selectedProvinceIds.Clear();
        EmitSignal(SignalName.ProvinceSelectionChanged, ProvinceIdsToArray());
        QueueRedraw();
    }

    // HashSet -> string[] (CopyTo: ToArray uzantısı bağımlılığı olmadan çalışır).
    private string[] ProvinceIdsToArray()
    {
        var ids = new string[_selectedProvinceIds.Count];
        _selectedProvinceIds.CopyTo(ids);
        return ids;
    }

    // İki noktadan mondeksele göre eksen-parallel dikdörtgen.
    private static Rect2 Rect2FromPoints(Vector2 a, Vector2 b) =>
        new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y));

    // M19.2: Kutu seçimi çerçevesi (sürüklemede) + seçili illerin vurguları (_Draw'dan çağrılır).
    private void DrawSelection()
    {
        if (_selecting)
        {
            var rect = Rect2FromPoints(_selectStart, _selectEnd);
            // Yarı saydam dolgu + beyaz kenar çizgisi
            DrawRect(rect, new Color(1f, 1f, 1f, 0.10f), true);
            DrawRect(rect, new Color(1f, 1f, 1f, 0.80f), false, 1.5f);
        }

        // Seçili iller: hafif altın dolgu + net altın kenar
        foreach (var pid in _selectedProvinceIds)
        {
            if (!_provinceBounds.TryGetValue(pid, out var box)) continue;
            DrawRect(box, new Color(1f, 0.8f, 0.1f, 0.10f), true);
            DrawRect(box, new Color(1f, 0.85f, 0.2f, 0.90f), false, 1.5f);
        }
    }

    // M32.5: İl sınırları — her il poligonunun etrafında ince şeffaf çizgi.
    // Çizim HaritaGirlenın _Draw()'unda DrawArmies'den önce çağrılır (sınırlar askerin altında).
    private void DrawProvinceBorders()
    {
        if (_provinceNodes == null) return;
        // Task 4 (LOD): Uzak zoom'da (küyçük harita görünümü) il sınırları
        // ~30.000 DrawLine çağrısı demek. Zoom 0.5 altındayken sınırları atla:
        // bu mesafede bir pikselin altındalar ve hiçbir bilgi katmıyorlar.
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

    // M19.3/19.4: Sağ tık kısa tıklamasıyla seçili illardaki tüm orduları hedef ili hareket ettirir.
    // shift=true → hedefi mevcut rotanın sonuna ekler (ara nokta). false → yeni rota.
    // Dönüş: true = hareket emri verildi (savaş emri devreye girmez), false = yok.
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
            // Henüz gidilmemiş kalan waypoint'ler + yeni hedef; mevcut konumdan BFS zinciriyle tam rota.
            var remaining = army.WaypointIds.Skip(army.MovementPathIndex).ToList();
            remaining.Add(target.Id);
            var route = BuildFullRoute(army.ProvinceId, remaining); // [..., target], başlangıç hariç
            if (route.Count == 0) continue; // rota kurulamadı (ulaşılamıyor)

            var fullPath = new List<string> { army.ProvinceId };
            fullPath.AddRange(route);
            ApplyRouteToArmy(army, fullPath);

            army.TargetProvinceId = target.Id;
            // Anında varış YOK: ProvinceId günlük tick'te adım adım ilerler (M19.4).
            army.DaysRemainingInHop = army.DaysPerProvince;
            GD.Print($"[Map] {army.Name} → {target.Name} (rota={army.WaypointIds.Count} il, hız={army.DaysPerProvince:F1} gün/il)");
        }

        QueueRedraw();
        return true;
    }

    // M19.4: mevcut konumdan waypoint'ler üzerinden hedefe BFS zinciriyle tam yürütme rotası.
    // Döndürülen liste başlangıç ilini içermez, waypoint/hedef illeri sırayla içerir.
    private List<string> BuildFullRoute(string startId, IReadOnlyList<string> waypoints)
    {
        var route = new List<string>();
        string from = startId;
        foreach (var wp in waypoints)
        {
            var seg = BfsPath(from, wp); // [from, ..., wp]
            foreach (var pid in seg.Skip(1)) route.Add(pid); // 'from' zaten mevcut konum → atla
            from = wp;
        }
        return route;
    }

    // M19.3: Komşuluk grafiği üzerinden start → target en kısa yolu (BFS). İD listesi döner.
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
        // Ulaşılamıyor → doğrudan hedef
        return new List<string> { targetId };
    }

    // M19.3/19.4: Orduların hareket emri rotalarını çizer (polyline + ara/hedef nokta çemberleri).
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
                // M32.5: Ok başı — hareket yönünü gösterir
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

    // M19.6: Cephe hattı — farklı-sahipli ve savaşta olan komşu il ikilisi arasında kalın çizgi.
    // Yeniden hesaplama yalnızca sahiilik/savaş değişikliği olduğunda (_frontlinesDirty).
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
                // Her komşu çifti bir kez işle (sıralı anahtar)
                string pairKey = prov.Id.CompareTo(nbId) < 0
                    ? $"{prov.Id}|{nbId}"
                    : $"{nbId}|{prov.Id}";
                if (!visited.Add(pairKey)) continue;

                if (!_provinceById.TryGetValue(nbId, out var nb)) continue;
                string ownerB = ProvinceOwnershipSystem.GetProvinceOwner(nb.Id);
                if (string.IsNullOrEmpty(ownerB) || ownerB == "?" || ownerA == ownerB) continue;

                // İki ülke arasındaki herhangi bir savaş durumu cepheyi oluşturur
                if (!WarSystem.IsAtWarWith(ownerA, ownerB)) continue;

                _frontlines.Add(new FrontlineSegment
                {
                    A = prov.Centroid,
                    B = nb.Centroid,
                });
            }
        }

        _frontlinesDirty = false;
        GD.Print($"[WorldMap] Cephe hatları yeniden hesaplandı — segment={_frontlines.Count}");
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

    // M19.6: Ofansif plan çizimi — kullanıcının Ctrl+LMB sürüklemesiyle çizdiği oku (veya kaydedilmiş planı) çizer.
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

        // Ok başı (son noktada)
        var last = pts[^1];
        var prev = pts[^2];
        var dir = (last - prev).Normalized();
        var left = new Vector2(-dir.Y, dir.X);
        DrawLine(last, last - dir * 14f + left * 7f, color, 2.5f);
        DrawLine(last, last - dir * 14f - left * 7f, color, 2.5f);
    }

    // M19.6: Oluşturulan ofansif planı finalize eder ve kaynak illerdeki seçili orduları rota üzerine yükler.
    private void FinalizePlan(string sourceProvinceId, string targetProvinceId)
    {
        var path = BfsPath(sourceProvinceId, targetProvinceId); // [kaynak, ..., hedef]
        if (path.Count < 2)
        {
            GD.Print($"[Map] Ofansif plan iptal: {sourceProvinceId} → {targetProvinceId} arasında rota yok");
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
        GD.Print($"[Map] Ofansif plan kaydedildi: {sourceProvinceId} → {targetProvinceId} ({path.Count} il adım)");

        // Kaynak illerdeki seçili orduları plana bağla
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
            GD.Print($"[Map] Plan: {sourceProvinceId} bölgesinde seçili ordu bulunamadı (rota kaydedildi; el ile hareket emri verilebilir)");
    }

    // Rota listesini (ilk il dahil) ordunun yürütme yoluna döküyor (M19.4 hop mantığıyla uyumlu).
    private void ApplyRouteToArmy(ArmySystem.Army army, IReadOnlyList<string> fullPath)
    {
        army.WaypointIds.Clear();
        for (int i = 1; i < fullPath.Count; i++)
            army.WaypointIds.Add(fullPath[i]);
        army.MovementPathIndex = 0;
        army.TargetProvinceId = fullPath[^1];
        army.DaysRemainingInHop = army.DaysPerProvince;
    }

    // M19.8: AI bu yöntemle aynı BFS rotası + M19.4 hop altyapısını kullanarak
    // ordularını bir hedef iline hareket ettirir. Oyuncu emriyle aynı yol guzergahı.
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

        GD.Print($"[AI Move] {army.Name} → {targetProvinceId} (rota={army.WaypointIds.Count} il)");
    }

    private void AnimateSelectedBorder(float delta)
    {
        if (_selected == null || _camera == null) return;
        float zoom = _camera.Zoom.X;
        _goldPulse += delta * 2f; float pulse = 0.5f + 0.5f * Mathf.Sin(_goldPulse);
        float selW = Mathf.Clamp(2.2f / zoom, 0.8f, 6f); // altın çerçeve: ekranda kalın
        foreach (var outline in _selected.Outlines)
        {
            if (!GodotObject.IsInstanceValid(outline)) continue;
            // Pulsing: kalınlık yumuşakça oscille eder, renk hafif parlar
            outline.Width = selW * (0.85f + pulse * 0.3f);
            outline.DefaultColor = GoldBorderColor.Lightened(pulse * 0.2f);
        }
    }

    // Gündüz/gece terminatörü gradyanı: çok hafif koyu gölge, yumuşak kenarlar.
    // Alpha ≤ 0.15: harita okunurluğunu bozmaz. Beyaz renk YOK.
    private static Gradient BuildTerminatorGradient()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(0f, 0f, 0f, 0f));              // tam şeffaf kenar
        g.AddPoint(0.2f, new Color(0f, 0f, 0f, 0.03f));         // yumuşak yükseliş
        g.AddPoint(0.5f, new Color(0f, 0f, 0f, 0.12f));         // gece kalbi: %12 karartma
        g.AddPoint(0.8f, new Color(0f, 0f, 0f, 0.03f));         // yumuşak düşüş
        g.SetColor(1, new Color(0f, 0f, 0f, 0f));               // tam şeffaf kenar
        return g;
    }

    // M32.2: Vinyet + merkez parlaklığı — merkez hafif turkuaz parıltı, kenarlar koyu (radial).
    private static Gradient BuildVignetteGradient()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(0.12f, 0.40f, 0.50f, 0.08f));   // merkez: şeffaf turkuaz parıltı
        g.AddPoint(0.5f, new Color(0f, 0f, 0f, 0f));            // orta: tam şeffaf
        g.SetColor(1, new Color(0f, 0f, 0f, 0.30f));            // kenar: hafif koyu vinyet
        return g;
    }

    // Görsel #7: Harita efekt katmanı — ülkelerin üstünde, etiketlerin altında.
    // WorldMap._Draw'tan değil, ayrı child node'dan çizilir çünkü _Draw ebeveyn
    // node'un çocuklarından ÖNCE çalışır; biz ülke poligonlarının ÜSTÜNE çizmek istiyoruz.
    // M18.3: İlleri yükler, her birini Polygon2D olarak çizer (rengi = sahip ülkenin rengi).
    public void SetProvinces(IReadOnlyList<ProvinceData> provinces)
    {
        _provinces = new List<ProvinceData>(provinces);
        ProvinceOwnershipSystem.InitializeFromProvinces(_provinces);

        // M49.1: il komşuluk grafiği — savaş cephesi ve işgal hedefleri buradan türetilir.
        OccupationSystem.SetWorld(_provinces);

        // M50.1: iç çatışma — isyan bölgesi seçimi aynı komşuluk grafını kullanır.
        CivilWarSystem.SetWorld(_provinces);

        // M44.2: il kültür/nüfus katmanı — sahiplikle aynı anda üretilir ki
        // formable koşulları ve kültür paneli normal oyunda da dolu olsun.
        if (ProvinceCultureSystem.RecordCount == 0)
        {
            ProvinceCultureSystem.GenerateFor(_provinces, ScenarioSystem.ActiveId ?? "historical_1936");
            GD.Print($"WorldMap: {ProvinceCultureSystem.RecordCount} il kültür kaydı üretildi.");
        }

        // M47.1: il ekonomi ağırlık katmanı — kültür katmanından SONRA üretilir,
        // çünkü nüfus payı il nüfus kaydına bağlanır.
        if (ProvinceEconomySystem.RecordCount == 0)
        {
            ProvinceEconomySystem.GenerateFor(_provinces, ScenarioSystem.ActiveId ?? "historical_1936");
            GD.Print($"WorldMap: {ProvinceEconomySystem.RecordCount} il ekonomi ağırlık kaydı üretildi.");
        }

        _provinceBounds.Clear();
        foreach (var prov in _provinces)
            _provinceBounds[prov.Id] = ComputeBounds(prov);
        _provinceById.Clear();
        foreach (var prov in _provinces)
            _provinceById[prov.Id] = prov;
        BuildProvinceGrid(); // Task 4: hit-test uzaysal indeksi
        DrawProvinces();
        RefreshProvinceColors();
        QueueRedraw();
        GD.Print($"WorldMap: {_provinces.Count} il çizildi.");
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

    // M13.3: Bir ilin sahibi ülkesine göre mod-rengi.
    private Color ModeColorForProvince(ProvinceData prov)
    {
        if (CurrentMapMode == MapMode.Political)
            return ColorForId(prov.CountryId);
        if (CurrentMapMode == MapMode.Culture)
            return ModeColorForProvinceCulture(prov);
        return ModeColorForCountryId(prov.CountryId);
    }

    // M46.1: Kültür harita modu — ilin KENDİ kültürüne göre renk.
    // Bir ülke içinde birden çok kültür olabileceği için ülke rengi kullanılmaz.
    private Color ModeColorForProvinceCulture(ProvinceData prov)
    {
        if (prov == null) return Colors.Transparent;
        // Kültür kaydı il kimliğiyle tutulur: sahiplik değişse de ilin kültürü korunur.
        string culture = ProvinceCultureSystem.CultureOf(prov.Id);
        var color = ColorForCulture(culture);

        // M46.2: kültür efsanesi panelinden bir kültür vurgulandıysa, o kültüre
        // ait olmayan iller kararır — vurgulu kültür haritada tek başına kalır.
        if (_cultureHighlight.Length > 0 &&
            !string.Equals(culture, _cultureHighlight, StringComparison.Ordinal))
            return DimColor(color);

        return color;
    }

    /// <summary>Rengi kömürleştirir (vurgulanmayan bölgeler için).</summary>
    private static Color DimColor(Color c)
    {
        // Doymuş renkler harita zeminde tamamen kaybolmasın diye üste katman
        // tamamen sıfırlanmaz, sabit bir oranda kısılır.
        const float k = 0.2f;
        return new Color(c.R * k, c.G * k, c.B * k, 1f);
    }

    /// <summary>
    /// M46.2: Vurgulanan kültür (boş = vurgu yok). Kültür efsanesi panelinde
    /// bir kültüre tıklandığında kurulur, harita culture moduna geçer.
    /// </summary>
    public string CultureHighlight => _cultureHighlight;

    private string _cultureHighlight = "";

    /// <summary>
    /// M46.2: Bir kültürü haritada vurgular (diğerlerini karartır).
    /// Boş metin vurguyu kaldırır; harita modu culture'a çevrilir.
    /// </summary>
    public void SetCultureHighlight(string culture)
    {
        string next = culture ?? "";
        if (_cultureHighlight == next) return;
        _cultureHighlight = next;
        MapViewSystem.SetCultureHighlight(next); // M46.4: vurgu Save/Load'da korunsun

        // Vurgu yalnızca culture modunda görünür: vurgu EKLENİRKEN modu oraya
        // çeken eski davranış, vurguyu KALDIRIRKEN de modu culture'ya çekiyordu
        // ve kullanıcıyı political modundan kopartıyordu. Artık yalnızca eklemede.
        if (CurrentMapMode != MapMode.Culture)
        {
            if (next.Length > 0) SetMapMode(MapMode.Culture);
        }
        else
        {
            RefreshProvinceColors();
        }

        QueueRedraw();
        GD.Print($"WorldMap: kültür vurgusu = {(next.Length > 0 ? next : "(yok)")}");
    }

    /// <summary>
    /// M46.4: Kayıttan gelen harita görünümünü uygular. Vurgu yalnızca culture
    /// modunda anlamlı — başka modda kayıtlı kalmış bir vurgu haritayı yanlış
    /// boyayacağından düşürülür.
    /// </summary>
    public void ApplyMapViewState()
    {
        var mode = MapViewSystem.Mode;
        SetMapMode(mode);
        string highlight = mode == MapMode.Culture ? MapViewSystem.CultureHighlight : "";
        if (mode != MapMode.Culture) MapViewSystem.SetCultureHighlight("");
        SetCultureHighlight(highlight);
        GD.Print($"[MapView] Harita görünümü uygulandı: mod={mode}, " +
                 $"vurgu={(highlight.Length > 0 ? highlight : "(yok)")}");
    }

    // M46.1: Kültür kimliğinden deterministik renk (FNV-1a hash → HSV).
    //
    // Renk yalnızca kültür ADINDAN türetilir: aynı kültür her senaryoda, her
    // çalıştırmada aynı rengi alır ve kayıtta ayrıca tutulması gerekmez.
    // Hue seçiminde art arda gelen hash değerleri birbirine çok yakın hue
    // üretebileceğinden altın açı çarpanı kullanılır; doygunluk/parlaklık sabit
    // tutulur çünkü harita zemini koyu ve renkler ayırt edilebilir olmalı.
    private static readonly Dictionary<string, Color> CultureColorCache = new(StringComparer.Ordinal);

    public static Color ColorForCulture(string culture)
    {
        if (string.IsNullOrEmpty(culture))
        {
            // Kültür atanmamış il: nötr koyu gri (boyasız bölge olarak okunur)
            return new Color(0.29f, 0.29f, 0.32f, 1f);
        }
        if (CultureColorCache.TryGetValue(culture, out var cached)) return cached;

        unchecked
        {
            // FNV-1a; UTF-16 char iki byte'a katılır (Türkçe karakterler de kapsanır).
            uint h = 2166136261u;
            foreach (char ch in culture)
            {
                h ^= (byte)ch;      h *= 16777619u;
                h ^= (byte)(ch >> 8); h *= 16777619u;
            }

            // Hash’i kaybetmeden hue’ya indir: 24 bitlik kayma float’ta tam
            // görünür, 2^22 ile bölmek de tam bölme olduğundan 4M ayrık hue
            // değeri çıkar — kayıp yok, çakışma pratikte imkansız.
            float hue = (float)(h >> 8) * (1f / 4194304f);
            var color = Color.FromHsv(hue, 0.62f, 0.88f, 1f);
            CultureColorCache[culture] = color;
            return color;
        }
    }

    // M46.1: Bir ilin o anki dolgu rengi (arayüz ve kültür modu testi için).
    public Color ProvinceFillColor(string provinceId)
    {
        if (_provinceNodes.TryGetValue(provinceId, out var nodes) && nodes.Count > 0)
            return nodes[0].Color;
        return Colors.Transparent;
    }

    // İl sahipliği değişince tüm illerin rengini günceller (ilhak sonrası harita yeniden boyanır).
    public void RefreshProvinceColors()
    {
        // M46.3: Eşleşmeyen il kümesi boyamadan ÖNCE hesaplanır — kontur vurgusu
        // il dolgusundan bağımsız olarak aynı kaynaktan beslenir.
        ComputeCultureMismatches();

        foreach (var kvp in _provinceNodes)
        {
            // M46.1: culture modunda il kendi kültürüne boyanır, sahibine değil.
            Color c = CurrentMapMode == MapMode.Culture
                ? ModeColorForProvinceCulture(_provinceById.GetValueOrDefault(kvp.Key))
                : ModeColorForCountryId(ProvinceOwnershipSystem.GetProvinceOwner(kvp.Key));
            foreach (var node in kvp.Value)
                if (GodotObject.IsInstanceValid(node)) node.Color = c;
        }
    }

    // ===== M46.3: Kültür ipucu + eşleşmeyen il konturu =====

    /// <summary>Kültür modunda imleç altındaki il (yoksa boş).</summary>
    public string HoveredProvinceId => _hoverProvinceId;

    /// <summary>Seçili ülke kimliği (yoksa boş) — test/UI denetim.</summary>
    public string SelectedCountryId => _selected?.Id ?? "";

    /// <summary>Kültür modunda sahibinin baskın kültüründenden farklı kültürü olan il sayısı.</summary>
    public int CultureMismatchProvinceCount => _cultureMismatchProvinces.Count;

    /// <summary>Bir il sahibinin baskın kültüründenden farklı kültür mü taşıyor?</summary>
    public bool IsCultureMismatchProvince(string provinceId)
    {
        return !string.IsNullOrEmpty(provinceId) && _cultureMismatchProvinces.Contains(provinceId);
    }

    /// <summary>Eşleşmeyen il kimlikleri (iletilen sıra — test/UI denetim).</summary>
    public IReadOnlyList<string> CultureMismatchProvinceIds
    {
        get
        {
            var list = new List<string>(_cultureMismatchProvinces);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }

    /// <summary>İpucu kutusunun o anki metni (test/UI denetim).</summary>
    public string CultureTipText => _cultureTipLabel?.Text ?? "";

    /// <summary>İpucu kutusu görünür mü?</summary>
    public bool CultureTipVisible => _cultureTip?.Visible ?? false;

    /// <summary>
    /// İpucu kutusunun fare süzgeci. `Ignore` olmalı: kültür ipucu hiçbir
    /// koşulda harita girdisini (sağ tık sürükleme, tekerlek, WASD, il seçimi)
    /// bloklamaz (test/UI denetim).
    /// </summary>
    public Control.MouseFilterEnum CultureTipMouseFilter =>
        _cultureTip?.MouseFilter ?? Control.MouseFilterEnum.Stop;

    /// <summary>
    /// Sahibinin baskın kültüründen farklı kültür taşıyan illeri işaretler.
    ///
    /// Baskın kültür, ülkenin il sayısına göre en çok tekrarlanan kültürdür
    /// (eşitlikte kimlik — deterministik). Böylece "bu topraklar kimin" sorusu
    /// haritada tek renkle değil, içindeki yabancı kültürlerle birlikte okunur.
    /// </summary>
    private void ComputeCultureMismatches()
    {
        _cultureMismatchProvinces.Clear();
        if (_provinceNodes.Count == 0) return;

        // Ülke → kültür dağılımı (il sayısı) bir kez toplanır.
        var byCountry = new Dictionary<string, List<ProvinceCultureSystem.CultureRecord>>(
            StringComparer.OrdinalIgnoreCase);
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
            if (dist.Count <= 1) continue; // tek kültürlü ülkede eşleşmeyen il olmaz

            string dominant = dist
                .OrderByDescending(d => d.Value)
                .ThenBy(d => d.Key, StringComparer.Ordinal)
                .First().Key;

            foreach (var rec in kv.Value)
                if (!string.Equals(rec.Culture ?? "", dominant, StringComparison.Ordinal))
                    _cultureMismatchProvinces.Add(rec.ProvinceId);
        }
    }

    /// <summary>
    /// Kültür eşleşmeyen illerin etrafına kontur çizer (kültür modunda).
    /// Uzak zoom'da atlanır — il sınırlarıyla aynı LOD kuralı.
    /// </summary>
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
                // Koyu çerçeve + üzerine ince sıcak kontur: karartılmış bölgelerde
                // de okunur kalsın (vurgu seçili kültürle çakışmasın diye kırmızı değil).
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

    /// <summary>Kültür ipucu kutusunu bir kez kurar (imleci takip eden Control).</summary>
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
            MouseFilter = Control.MouseFilterEnum.Ignore, // harita girdisini asla bloklama
            Visible = false
        };
        _cultureTip.AddThemeConstantOverride("margin_left", 8);
        _cultureTip.AddThemeConstantOverride("margin_right", 8);
        _cultureTip.AddThemeConstantOverride("margin_top", 6);
        _cultureTip.AddThemeConstantOverride("margin_bottom", 6);
        _cultureTip.AddChild(_cultureTipLabel);
        AddChild(_cultureTip);
    }

    /// <summary>İmleç altındaki ili günceller ve ipucu metnini kurar (kültür modunda).</summary>
    private void UpdateProvinceHover(Vector2 worldPos)
    {
        if (CurrentMapMode != MapMode.Culture)
        {
            // Moddan çıkınca ipucu kalkar; il kaydı ve metin de temizlenir ki
            // tekrar culture moduna geçildiğinde eski metin bir an görünmesin.
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

    /// <summary>İpucu metnini illerin kültür verisinden kurar.</summary>
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

    /// <summary>İpucu kutusunu imlecin yanına taşır (viewport sınırını aşmaz).</summary>
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
        GD.Print($"[WorldMap] İl sahipliği değişti: {provinceId} {oldCountryId} → {newCountryId}");
    }

    // M18.4: Hızlı hit-test — önce bounding-box önfiltresi, sonra nokta-İçinde-poligon testi.
    public ProvinceData FindProvinceAt(Vector2 worldPos)
    {
        if (_provinces.Count == 0) return null;

        // Grid hücresi ve bitişik 8 hücre (il sınırı hücreden taşabilir).
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

    // SetProvinces sırasında bir kez kurulur: il bbox'ı her katıldığı hücreye yazılır.
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

    public partial class MapEffects : Node2D
    {
        public override void _Draw()
        {
            var wm = WorldMap.Instance;
            if (wm == null) return;

            // Dönem tonu (era): tüm haritayı hafifçe renklendir (1939+ sıcak, 1942+ karanlık)
            if (wm._eraAlpha > 0.001f)
            {
                var bigRect = new Rect2(-1000f, -1000f, wm.MapSize.X + 2000f, wm.MapSize.Y + 2000f);
                DrawRect(bigRect, new Color(wm._eraColor.R, wm._eraColor.G, wm._eraColor.B, wm._eraAlpha));
            }

            // Gündüz/gece terminatörü: yumuşak kenarlı dikey gece bandı
            // VARSYILAN KAPALI — haritayı bölen gri bant şikayeti.
            if (wm._terminatorVisible && wm._terminatorTexture != null)
            {
                float bandW = wm.MapSize.X * TerminatorBandWidthRatio;
                var rect = new Rect2(
                    wm._terminatorX - bandW / 2f, -1000f,
                    bandW, wm.MapSize.Y + 2000f);
                DrawTextureRect(wm._terminatorTexture, rect, false);
            }

            // M32.2: Vinyet + merkez parlaklığı — tüm haritanın üzerine, köşeler koyu, merkez hafif turkuaz.
            // VARSYILAN KAPALI — köşe koyulaşması haritayı kirli gösteriyordu.
            if (wm._vignetteVisible && wm._vignetteTexture != null)
            {
                var bigRect = new Rect2(-1000f, -1000f, wm.MapSize.X + 2000f, wm.MapSize.Y + 2000f);
                DrawTextureRect(wm._vignetteTexture, bigRect, false);
            }
        }
    }

    // M32.3: Enlem bazlı biyom zonları + döşenmiş gürültü doku alt katmanı.
    // Ülke poligonlarının ALTINDA çizilir (WorldMap._Ready'de BuildFromGeoJson'dan önce eklenir).
    public partial class TerrainLayer : Node2D
    {
        private bool _terrainLogged;

        public override void _Draw()
        {
            var wm = WorldMap.Instance;
            if (wm == null) return;

            var mapSize = wm.MapSize;
            float topMargin = 80f;
            float usableH = mapSize.Y - topMargin * 2f;

            if (!_terrainLogged)
            {
                _terrainLogged = true;
                GD.Print($"WorldMap: arazi katmanı hazır — {mapSize.X:F0}x{mapSize.Y:F0}, biyom bantları + gürültü doku");
            }

            // Enlem → y dönüşümü (enlem 90 üstte, -90 altta).
            float LatToY(float lat) => topMargin + (90f - lat) / 180f * usableH;

            // Biyom bantları (enlem aralığı → renk), kuzeyden güneye çizilir.
            var bands = new (float LatTop, float LatBot, Color Color)[]
            {
                (90f, 70f, new Color("#DCE8EE")),   // kuzey kutpu buz
                (70f, 50f, new Color("#8A9B7E")),   // tundra / taiga
                (50f, 30f, new Color("#9CAB7E")),   // çayır
                (30f, 15f, new Color("#C9B48E")),   // çöl (subtropical)
                (15f, -15f, new Color("#6E8F6E")),  // tropikal orman
                (-15f, -30f, new Color("#C9B48E")), // çöl
                (-30f, -50f, new Color("#9CAB7E")), // çayır
                (-50f, -70f, new Color("#8A9B7E")), // tundra / taiga
                (-70f, -90f, new Color("#DCE8EE")), // güney kutpu buz
            };

            foreach (var (latTop, latBot, color) in bands)
            {
                float yTop = LatToY(latTop);
                float yBot = LatToY(latBot);
                DrawRect(new Rect2(0f, yTop, mapSize.X, yBot - yTop), color);
            }

            // Döşenmiş gürültü doku overlay'i — araziye doğal varyasyon katar.
            if (wm._noiseTexture != null)
            {
                var bigRect = new Rect2(-1000f, -1000f, mapSize.X + 2000f, mapSize.Y + 2000f);
                DrawTextureRect(wm._noiseTexture, bigRect, true, new Color(1f, 1f, 1f, 0.30f));
            }
        }
    }
}