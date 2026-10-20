using Godot;
using System;

// M32.4: Mini harita — dünyanın küçültülmüş görünümü, kamera dikdörtgeni,
// +/- zoom butonları, tıklamayla kamera kaydırması.
// Hızlı erişim için her karede yeniden çizilir (görsel-only, performans çok Kritik değil).
public partial class MiniMapPanel : Node2D
{
    private const float PanelSize = 210f;
    private const float BorderPadding = 12f;
    private const float MinZoom = 0.25f;
    private const float MaxZoom = 6f;

    private Button _zoomInBtn;
    private Button _zoomOutBtn;
    private Rect2 _mapRect;
    private Rect2 _cameraRectScreen;

    public override void _Ready()
    {
        // Sabit ekran konumu: sağ-alt köşe
        var vp = GetViewportRect().Size;
        Position = new Vector2(vp.X - PanelSize - BorderPadding - 4f, vp.Y - PanelSize - BorderPadding - 4f);
        ZIndex = 30; // haritanın üstünde, modal panellerin altında
        BuildUi();
    }

    public override void _Process(double delta)
    {
        UpdateCameraRect();
        QueueRedraw(); // kamera sürekli hareket ediyor
    }

    public override void _Draw()
    {
        var wm = WorldMap.Instance;
        if (wm == null) return;

        _mapRect = new Rect2(4f, 4f, PanelSize - 8f, PanelSize - 8f);

        // Okyanus arkaplanı
        DrawRect(_mapRect, new Color("#0A2B3A"));
        DrawRect(_mapRect, new Color(0.5f, 0.5f, 0.5f, 0.35f), false);

        float sx = (PanelSize - 8f) / wm.MapSize.X;
        float sy = (PanelSize - 8f) / wm.MapSize.Y;

        // Ülke poligonlarını küçültülmüş olarak çiz
        foreach (var c in wm._countries)
        {
            foreach (var ring in c.Rings)
            {
                if (ring.Length < 3) continue;
                var pts = new Vector2[ring.Length];
                for (int i = 0; i < ring.Length; i++)
                    pts[i] = _mapRect.Position + new Vector2(ring[i].X * sx, ring[i].Y * sy);
                var cols = new Color[ring.Length];
                for (int i = 0; i < cols.Length; i++) cols[i] = c.MapColor;
                DrawPolygon(pts, cols);
            }
        }

        // Kamera dikdörtgeni (şeffaf dolgu, sarı kenar)
        if (_cameraRectScreen.HasArea())
        {
            DrawRect(_cameraRectScreen, new Color(1f, 1f, 0.2f, 0.15f), true);
            DrawRect(_cameraRectScreen, new Color(1f, 0.9f, 0.2f, 0.9f), false);
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (!_mapRect.HasPoint(mb.Position - Position)) return;
            var wm = WorldMap.Instance;
            if (wm == null || wm._camera == null) return;

            // Minimap tıklaması -> dünya koordinatı
            var local = mb.Position - Position - _mapRect.Position;
            float sx = wm.MapSize.X / (PanelSize - 8f);
            float sy = wm.MapSize.Y / (PanelSize - 8f);
            Vector2 worldPos = new Vector2(local.X * sx, local.Y * sy);

            wm._targetPos = wm.ClampCameraPos(worldPos);
            wm._zoomAnim = true;
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUi()
    {
        // Arkaplav pane
        var panel = new PanelContainer();
        panel.Size = new Vector2(PanelSize, PanelSize);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore; // butonlara geç
        AddChild(panel);

        // Zoom +
        _zoomInBtn = new Button { Text = "+", Size = new Vector2(24, 24) };
        _zoomInBtn.Position = new Vector2(PanelSize - 30, 8);
        _zoomInBtn.Pressed += () => Zoom(1.15f);
        panel.AddChild(_zoomInBtn);

        // Zoom -
        _zoomOutBtn = new Button { Text = "-", Size = new Vector2(24, 24) };
        _zoomOutBtn.Position = new Vector2(PanelSize - 30, 36);
        _zoomOutBtn.Pressed += () => Zoom(1f / 1.15f);
        panel.AddChild(_zoomOutBtn);
    }

    private void Zoom(float factor)
    {
        var wm = WorldMap.Instance;
        if (wm == null || wm._camera == null) return;
        float z = Mathf.Clamp(wm._targetZoom * factor, MinZoom, MaxZoom);
        var center = GetViewportRect().Size / 2f;
        var before = (center - center) / wm._targetZoom + wm._targetPos;
        var after = (center - center) / z + wm._targetPos;
        wm._targetZoom = z;
        wm._targetPos = wm.ClampCameraPos(wm._camera.Position + (before - after));
        wm._zoomAnim = true;
    }

    private void UpdateCameraRect()
    {
        var wm = WorldMap.Instance;
        if (wm == null || wm._camera == null) return;

        var vp = GetViewportRect().Size;
        float z = wm._camera.Zoom.X;
        float ww = vp.X / z;
        float hh = vp.Y / z;
        Vector2 camTopLeft = wm._camera.Position - new Vector2(ww, hh) / 2f;

        float sx = (PanelSize - 8f) / wm.MapSize.X;
        float sy = (PanelSize - 8f) / wm.MapSize.Y;
        _cameraRectScreen = new Rect2(
            _mapRect.Position + camTopLeft * new Vector2(sx, sy),
            new Vector2(ww * sx, hh * sy));
    }
}
