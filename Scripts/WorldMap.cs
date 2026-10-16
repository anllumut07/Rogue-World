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

    // M18.3: İl bazlı çizim ve sahiplik
    private List<ProvinceData> _provinces = new();
    private Dictionary<string, List<Polygon2D>> _provinceNodes = new();

    // M18.4: Performans — il sınırları için bounding-box önbelleği (hit-test önfiltresi)
    private Dictionary<string, Rect2> _provinceBounds = new();
    private int _frameCounter;

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

    private Country _selected;
    private Country _hovered;
    private Camera2D _camera;
    private CanvasLayer _nameLayer;
    private bool _dragging;
    private Vector2 _lastMousePos;
    private Vector2 _rmbPressPos; // M7.5: RMB basılma noktası (tık vs sürükleme ayrımı)
    private float _goldPulse = 0f;
    private bool _cameraNeedsFit = true; // SubViewport gerçek boyutuna ulaşınca kamerayı oturt
    private static readonly Color OceanColor = new("0A1628");

    // Görsel iyileştirmeler
    private GradientTexture2D _oceanTexture;
    private float _lastBorderZoom = -1f;

    // Yumuşak kamera (zoom lerp): hedefe doğru yumuşak yaklaşma
    private bool _zoomAnim;
    private float _targetZoom = 1f;
    private Vector2 _targetPos = Vector2.Zero;

    // Görsel iyileştirme #7: Dönem (era) tonu + gündüz/gece terminatörü
    private float _eraAlpha;
    private Color _eraColor = Colors.Transparent;
    private float _terminatorX;
    private GradientTexture2D _terminatorTexture;
    private MapEffects _effects;
    private const float TerminatorPeriod = 60f;       // saniye cinsinden bir tam tur
    private const float TerminatorBandWidthRatio = 0.35f;

    public override void _Ready()
    {
        Instance = this;
        _nameLayer = new CanvasLayer { Layer = 1 };
        AddChild(_nameLayer);

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

        GD.Print($"WorldMap: hazır — kamera zoom={_camera?.Zoom.X:0.00} pos={_camera?.Position}, viewport={GetViewportRect().Size}");
    }

    // Dikey okyanus degradesi: kutuplar (üst/alt) koyu, ekvator (orta) mavi
    private static Gradient BuildOceanGradient()
    {
        var g = new Gradient();
        g.SetColor(0, new Color("#060B12"));
        g.AddPoint(0.45f, new Color("#10233C"));
        g.AddPoint(0.75f, new Color("#0C1B30"));
        g.SetColor(1, new Color("#050A10"));
        return g;
    }

    public override void _Draw()
    {
        // Okyanus: koyu mavi degrade (kutuplar koyu -> ekvator hafif mavi)
        var big = new Rect2(-2000f, -1000f, MapSize.X + 4000f, MapSize.Y + 2000f);
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

        // M19.3: Hareket emri rotaları (çizgi + hedef/ara nokta çemberleri)
        DrawMovementPaths();
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
            DrawCircle(drawPos, 6f, new Color(1f, 0.2f, 0.2f, 0.8f));
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

        // M18.4: FPS ölçümü (her 120 karede bir)
        _frameCounter++;
        if (_frameCounter % 120 == 0)
        {
            float fps = (float)Engine.GetFramesPerSecond();
            GD.Print($"[WorldMap] FPS={fps:F1} provinces={_provinces.Count}");
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

    public void RefreshOwnershipColors()
    {
        foreach (var c in _countries)
        {
            Color target = c.MapColor;

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
                // İnce başlangıç kalınlığı; zoom'a göre _Process'te güncellenecek
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
    private Vector2 ClampCameraPos(Vector2 pos)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, -400f, MapSize.X + 400f),
            Mathf.Clamp(pos.Y, -400f, MapSize.Y + 400f));
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
            army.WaypointIds = BuildFullRoute(army.ProvinceId, remaining);
            if (army.WaypointIds.Count == 0) continue; // rota kurulamadı (ulaşılamıyor)

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
                DrawLine(pts[i - 1], pts[i], new Color(0.2f, 0.7f, 1f, 0.85f), 2f);
            for (int i = 0; i < pts.Count; i++)
            {
                bool isLast = i == pts.Count - 1;
                DrawCircle(pts[i], 5f, isLast ? new Color(0.2f, 0.9f, 0.4f, 0.9f) : new Color(1f, 0.9f, 0.2f, 0.9f));
            }
        }
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

    // Görsel #7: Harita efekt katmanı — ülkelerin üstünde, etiketlerin altında.
    // WorldMap._Draw'tan değil, ayrı child node'dan çizilir çünkü _Draw ebeveyn
    // node'un çocuklarından ÖNCE çalışır; biz ülke poligonlarının ÜSTÜNE çizmek istiyoruz.
    // M18.3: İlleri yükler, her birini Polygon2D olarak çizer (rengi = sahip ülkenin rengi).
    public void SetProvinces(IReadOnlyList<ProvinceData> provinces)
    {
        _provinces = new List<ProvinceData>(provinces);
        ProvinceOwnershipSystem.InitializeFromProvinces(_provinces);
        _provinceBounds.Clear();
        foreach (var prov in _provinces)
            _provinceBounds[prov.Id] = ComputeBounds(prov);
        _provinceById.Clear();
        foreach (var prov in _provinces)
            _provinceById[prov.Id] = prov;
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
                var node = new Polygon2D { Polygon = ring, Color = ColorForId(prov.CountryId) };
                AddChild(node);
                nodes.Add(node);
            }
            _provinceNodes[prov.Id] = nodes;
        }
    }

    // İl sahipliği değişince tüm illerin rengini günceller (ilhak sonrası harita yeniden boyanır).
    public void RefreshProvinceColors()
    {
        foreach (var kvp in _provinceNodes)
        {
            string owner = ProvinceOwnershipSystem.GetProvinceOwner(kvp.Key);
            Color c = ColorForId(owner);
            foreach (var node in kvp.Value)
                if (GodotObject.IsInstanceValid(node)) node.Color = c;
        }
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
        ProvinceData best = null;
        float bestArea = float.MaxValue;
        foreach (var prov in _provinces)
        {
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
        return best;
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
            if (wm._terminatorTexture != null)
            {
                float bandW = wm.MapSize.X * TerminatorBandWidthRatio;
                var rect = new Rect2(
                    wm._terminatorX - bandW / 2f, -1000f,
                    bandW, wm.MapSize.Y + 2000f);
                DrawTextureRect(wm._terminatorTexture, rect, false);
            }
        }
    }
}