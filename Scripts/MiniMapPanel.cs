using Godot;
using System;
using System.Collections.Generic;

// M32.4 (düzeltme): Mini harita artık kendi CanvasLayer'ında (Layer=50) sabit
// boyutlu bir Control olarak durur. Kullanıcı şikayetleri ve çözümleri:
//  1. "Minimap dünya nodunun altında / harita ortasında" → CanvasLayer harita
//     çiziminden tamamen bağımsız; kök FullRect, yüzey sağalta sabitlenir.
//  2. "Kamerayla birlikte kayıyor" → CanvasLayer kamera etkilemez.
//  3. "Pencere boyutu değişince bozuluyor" → BottomRight preset + sabit offset,
//     resize'da anchor sistemi konumu korur.
//  4. Kamera dikdörtgeni minimap SINIRLARI İÇİNDE kıpraştırılır
//     ((kamera görüşü / harita boyutu) * minimap boyutu).
//  5. Tıklama VE sürükle ile kamera kaydırma (daha önce yalnızca tıklama vardı).
public partial class MiniMapPanel : CanvasLayer
{
    // Modal panellerin (Layer=100) üstünde DEĞİL: harita üstünde, modal altında.
    public const int LayerId = 50;

    private const float PanelSize = 210f;
    private const float Inset = 12f;       // ekran kenarından boşluk (px)
    private const float MapInset = 6f;     // panel içinde harita alanı payı
    private const float BtnSize = 26f;

    private MiniMapSurface _surface;

    private Rect2 _mapRect = new Rect2(MapInset, MapInset, PanelSize - MapInset * 2f, PanelSize - MapInset * 2f);
    private Rect2 _cameraRectScreen;

    // Ülke poligonlarını minimap ölçeğinde BİR KEZ hesaplayıp tutarız:
    //  - 1000+ ülke poligonunu her karede ölçeklemek pahalıydı,
    //  - ölçeklenmiş halkalarda dejener (triangülasyon başarısız) poligonlar
    //    oluşuyordu; bir kez süzüp geçerli listeyi saklıyoruz.
    private List<(Vector2[] Points, Color[] Colors)> _cachedPolys;
    private int _cachedPolyCount = -1;

    // Gereksiz yeniden çizimi önlemek için son bilinen kamera durumu.
    private Vector2 _lastCamPos = new(float.NaN, float.NaN);
    private float _lastZoom = float.NaN;
    private Vector2I _lastViewport = new(-1, -1);
    private bool _dirty = true;

    public override void _Ready()
    {
        Layer = LayerId;

        // Kök: pencereyi kaplayan, girdiyi geçiren katman (sürüklemenin kaybolmaması için).
        var root = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Name = "MiniMapRoot"
        };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Yüzey: sağ-alt köşede sabit boyut (BottomRight preset + sabit offset).
        _surface = new MiniMapSurface(this) { Name = "MiniMapSurface" };
        _surface.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _surface.OffsetLeft = -PanelSize - Inset;
        _surface.OffsetTop = -PanelSize - Inset;
        _surface.OffsetRight = -Inset;
        _surface.OffsetBottom = -Inset;
        root.AddChild(_surface);

        var frame = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore, // girdi yüzeyde toplanır
        };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _surface.AddChild(frame);

        // Zoom butonları: üst-sağ köşede üst üste (TopRight preset + farklı tepe ofseti).
        _surface.AddChild(MakeZoomButton("+", 1.15f, MapInset));
        _surface.AddChild(MakeZoomButton("-", 1f / 1.15f, MapInset + 28f));

        // Pencere boyutu değişirse kırpılmış kamera dikdörtgeni değişebilir.
        GetTree().Root.SizeChanged += MarkDirty;
    }

    public override void _ExitTree()
    {
        GetTree().Root.SizeChanged -= MarkDirty;
    }

    private Button MakeZoomButton(string text, float factor, float top)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(BtnSize, 22),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        btn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        btn.OffsetLeft = -BtnSize - MapInset;
        btn.OffsetTop = top;
        btn.OffsetRight = -MapInset;
        btn.OffsetBottom = top + 22f;
        btn.Pressed += () => ZoomFactor(factor);
        return btn;
    }

    private void MarkDirty()
    {
        _dirty = true;
        _surface?.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Yalnızca kamera/zoom/pencere değiştiğinde yeniden çiz (eskiden her kare).
        var wm = WorldMap.Instance;
        if (wm?._camera == null) return;

        bool changed = _dirty
            || !_lastCamPos.IsEqualApprox(wm._camera.Position)
            || !Mathf.IsEqualApprox(_lastZoom, wm._camera.Zoom.X)
            || _lastViewport != _surface.GetViewportRect().Size;
        if (!changed) return;

        _lastCamPos = wm._camera.Position;
        _lastZoom = wm._camera.Zoom.X;
        _lastViewport = (Vector2I)_surface.GetViewportRect().Size;
        _dirty = false;
        _surface.QueueRedraw();
    }

    // Çizim yüzeyde (Control) yapılır: CanvasLayer kendisi çizilebilir CanvasItem değildir.
    private void DrawSurface(MiniMapSurface c)
    {
        var wm = WorldMap.Instance;
        if (wm == null) return;

        float sx = _mapRect.Size.X / wm.MapSize.X;
        float sy = _mapRect.Size.Y / wm.MapSize.Y;

        // Ülke poligonları: minimap ölçeğinde bir kez hesaplanır, önbellekte tutulur.
        // Ülke sayısı değiştiyse (yeni harita yüklendi) önbellek yenilenir.
        if (_cachedPolys == null || _cachedPolyCount != wm._countries.Count)
        {
            _cachedPolyCount = wm._countries.Count;
            var list = new List<(Vector2[], Color[])>(1024);
            foreach (var country in wm._countries)
            {
                foreach (var ring in country.CleanRings)
                {
                    // Sıfır/nötr alanlı poligonu atla — DrawPolygon triüleyemez.
                    if (ring.Length < 3 || Mathf.Abs(PolygonSignedArea(ring)) < 1.0f) continue;
                    var pts = new Vector2[ring.Length];
                    for (int i = 0; i < ring.Length; i++)
                        pts[i] = _mapRect.Position + new Vector2(ring[i].X * sx, ring[i].Y * sy);
                    // Ekran alanında çok küçük / bozuk poligonu da atla.
                    if (Mathf.Abs(PolygonSignedArea(pts)) < 0.5f) continue;
                    // Minimap ölçeğinde sadeleştir (Ramer-Douglas-Peucker):
                    //  - iki kez üst üste binen ve aynı doğru üzerindeki köşeler
                    //    Godot triüleyicisini "Invalid polygon data" ile düşürür,
                    //  - minimap çok küçük olduğu için görsel fark yok,
                    //  - çizim maliyeti de düşer.
                    var cleaned = SimplifyRdp(pts, 0.7f);
                    if (cleaned.Count < 3) continue;
                    if (Mathf.Abs(PolygonSignedArea(cleaned.ToArray())) < 0.5f) continue;
                    var cols = new Color[cleaned.Count];
                    for (int i = 0; i < cols.Length; i++) cols[i] = country.MapColor;
                    list.Add((cleaned.ToArray(), cols));
                }
            }
            _cachedPolys = list;
            GD.Print($"MiniMapPanel: {list.Count} ülke poligonu önbelleğe alındı (minimap)");
        }

        foreach (var poly in _cachedPolys)
            c.DrawPolygon(poly.Points, poly.Colors);

        if (wm._camera == null) return;

        // Kamera dikdörtgeni — minimap SINIRLARI İÇİNDE kıpraştırılır.
        float z = wm._camera.Zoom.X;
        var vp = c.GetViewportRect().Size;
        if (vp.X <= 0f || vp.Y <= 0f || z <= 0f) return;

        float viewW = vp.X / z;
        float viewH = vp.Y / z;
        Vector2 camTopLeft = wm._camera.Position - new Vector2(viewW, viewH) / 2f;
        _cameraRectScreen = new Rect2(
            _mapRect.Position + camTopLeft * new Vector2(sx, sy),
            new Vector2(viewW * sx, viewH * sy));

        var clamped = _cameraRectScreen;
        // Boyut sınırlara sığmıyorsa sınırlara sabitle.
        if (clamped.Size.X > _mapRect.Size.X || clamped.Size.Y > _mapRect.Size.Y)
        {
            clamped.Size = new Vector2(Mathf.Min(clamped.Size.X, _mapRect.Size.X),
                                       Mathf.Min(clamped.Size.Y, _mapRect.Size.Y));
        }
        clamped.Position = new Vector2(
            Mathf.Clamp(clamped.Position.X, _mapRect.End.X - clamped.Size.X, _mapRect.Position.X),
            Mathf.Clamp(clamped.Position.Y, _mapRect.End.Y - clamped.Size.Y, _mapRect.Position.Y));

        c.DrawRect(clamped, new Color(1f, 1f, 0.2f, 0.15f), true);
        c.DrawRect(clamped, new Color(1f, 0.9f, 0.2f, 0.95f), false);
    }

    // Tıklama ve sürükle: minimap yüzeyinde basılı tut + hareket → kamera kaydır.
    private void OnSurfaceInput(InputEvent e)
    {
        var wm = WorldMap.Instance;
        if (wm?._camera == null) return;

        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            JumpTo(mb.Position);
            _surface.GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseMotion mm && (mm.ButtonMask & Godot.MouseButtonMask.Left) != 0)
        {
            JumpTo(mm.Position);
            _surface.GetViewport().SetInputAsHandled();
        }
    }

    // Minimap yerel koordinatını dünya koordinatına çevirip kamerayı oraya sabitler.
    private void JumpTo(Vector2 localPos)
    {
        var wm = WorldMap.Instance;
        if (wm?._camera == null) return;

        var local = localPos - _mapRect.Position;
        float wx = wm.MapSize.X / _mapRect.Size.X;
        float wy = wm.MapSize.Y / _mapRect.Size.Y;
        var worldPos = new Vector2(local.X * wx, local.Y * wy);

        wm._targetPos = wm.ClampCameraPos(worldPos);
        wm._targetZoom = wm._camera.Zoom.X; // zoom'u değiştirme
        wm._zoomAnim = true;
        MarkDirty();
    }

    private void ZoomFactor(float factor)
    {
        var wm = WorldMap.Instance;
        if (wm?._camera == null) return;
        float z = Mathf.Clamp(wm._targetZoom * factor, MinZoom, MaxZoom);
        if (Mathf.IsEqualApprox(z, wm._targetZoom)) return;
        wm._targetZoom = z;
        wm._targetPos = wm._camera.Position;
        wm._zoomAnim = true;
        MarkDirty();
    }

    private const float MinZoom = 0.25f;
    private const float MaxZoom = 6f;

    // Ramer-Douglas-Peucker (kapalı halka): aynı doğru üzerindeki / çok yakın
    // köşeleri atar. Uç noktalar korunur, şekil eps toleransı içinde kalır.
    // Godot'nun DrawPolygon triüleyicisi üst üste binen ve collinear köşeleri
    // "Invalid polygon data" ile reddeder; minimap ölçeğinde bu fark edilmez.
    private static List<Vector2> SimplifyRdp(IReadOnlyList<Vector2> pts, float eps)
    {
        if (pts.Count < 4) return new List<Vector2>(pts);

        int n = pts.Count;
        int a = 0;
        float best = -1f;
        for (int i = 1; i < n; i++)
        {
            float d = pts[0].DistanceSquaredTo(pts[i]);
            if (d > best) { best = d; a = i; }
        }
        int b = (a + 1) % n;

        var keep = new bool[n];
        keep[0] = true; keep[a] = true; keep[b] = true;
        RdpRec(pts, 0, a, eps, keep);
        RdpRec(pts, a, b, eps, keep);
        if (a != n - 1) RdpRec(pts, b, 0, eps, keep);

        var result = new List<Vector2>(n);
        for (int i = 0; i < n; i++) if (keep[i]) result.Add(pts[i]);
        return result;
    }

    private static void RdpRec(IReadOnlyList<Vector2> pts, int start, int end, float eps, bool[] keep)
    {
        if (start == end) return;
        int n = pts.Count;
        var a = pts[start];
        var b = pts[end];
        var ab = b - a;
        float abLen = ab.Length();

        float worst = -1f;
        int worstIdx = -1;
        int i = (start + 1) % n;
        while (i != end)
        {
            var p = pts[i];
            float dist = abLen < 0.0001f
                ? p.DistanceTo(a)
                : Mathf.Abs((p - a).Cross(ab) / abLen);
            if (dist > worst) { worst = dist; worstIdx = i; }
            i = (i + 1) % n;
        }

        if (worstIdx < 0 || worst <= eps) return;
        keep[worstIdx] = true;
        RdpRec(pts, start, worstIdx, eps, keep);
        RdpRec(pts, worstIdx, end, eps, keep);
    }

    // İmzalı poligon alanı (shoelace). Sıfır/nötr alan → DrawPolygon triüleyemez.
    private static float PolygonSignedArea(Vector2[] pts)
    {
        float area = 0f;
        for (int i = 0; i < pts.Length; i++)
        {
            int j = (i + 1) % pts.Length;
            area += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
        }
        return area * 0.5f;
    }

    // Çizim + girdi yüzeyi. Control olmak zorunda: CanvasLayer'in kendi _Draw/_GuiInput'u yoktur.
    private sealed partial class MiniMapSurface : Control
    {
        private readonly MiniMapPanel _owner;

        public MiniMapSurface(MiniMapPanel owner)
        {
            _owner = owner;
            MouseFilter = Control.MouseFilterEnum.Stop;
        }

        public override void _Draw() => _owner.DrawSurface(this);

        public override void _GuiInput(InputEvent e) => _owner.OnSurfaceInput(e);
    }
}
