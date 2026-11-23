using Godot;
using System;

// Oyun ekranının yerleşimi: merkez (SubViewportContainer → WorldMap) TAM EKRAN.
// HUD, CountryInfoPanel ve diğer paneller ayrı CanvasLayer/Control olarak overlay'dir.
// SubViewportContainer Stretch=true ile pencere boyutunu tam doldurur.
public partial class MapViewportFrame : Control
{
    [Signal] public delegate void MapFrameReadyEventHandler();

    public SubViewportContainer CenterViewport { get; private set; }
    // SubViewportContainer'ın render hedefi. Harita (MapView) bu SubViewport'in içine
    // eklenir; container Stretch=true ile render target'ını pencereye şeritler.
    public SubViewport MapSubViewport { get; private set; }

    public override void _Ready()
    {
        // Merkez viewport: TAM EKRAN (FullRect)
        CenterViewport = new SubViewportContainer();
        CenterViewport.MouseFilter = Control.MouseFilterEnum.Pass; // Pass input to SubViewport when Stretch=true
        CenterViewport.Stretch = true; // SubViewport boyutunu container yönetsin
        AddChild(CenterViewport);

        // SubViewport yoksa SubViewportContainer rendersiz (boş) kalır.
        // MapView bu SubViewport'in altına eklendiği için harita burada çizilir.
        MapSubViewport = new SubViewport { Name = "MapSubViewport" };
        CenterViewport.AddChild(MapSubViewport);
        // Container'ın hangi SubViewport'ı rendere edeceğini tanımla.
        CenterViewport.Set("sub_viewport", MapSubViewport.GetPath());

        // Layout'u hemen ve scene oturunca uygula
        EnforceLayout();
        Callable.From(EnforceLayout).CallDeferred();

        // Pencere boyutu değişince yeniden uygula (ve SubViewport boyutunu eşitle)
        GetTree().Root.SizeChanged += EnforceLayout;

        GD.Print("MapViewportFrame: hazır — CenterViewport FullRect + Stretch + SubViewport");

        // Emit Ready signal for Game.cs to set up map content
        EmitSignal(SignalName.MapFrameReady);
    }

    public override void _ExitTree()
    {
        if (GetTree() != null)
            GetTree().Root.SizeChanged -= EnforceLayout;
    }

    // CenterViewport'u pencereye sığdır (FullRect, offset 0)
    public void EnforceLayout()
    {
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        CenterViewport.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // Stretch=true iken offset'ler 0 olmalı, yoksa SubViewport boyutu bozulur
        CenterViewport.OffsetLeft = 0;
        CenterViewport.OffsetTop = 0;
        CenterViewport.OffsetRight = 0;
        CenterViewport.OffsetBottom = 0;

        // SubViewport render target'ını pencere boyutuna eşitle (sıfır boyutlu → rendersiz)
        var viewportSize = GetViewport().GetVisibleRect().Size;
        int w = Mathf.Max((int)viewportSize.X, 1);
        int h = Mathf.Max((int)viewportSize.Y, 1);
        if (MapSubViewport != null && (MapSubViewport.Size.X != w || MapSubViewport.Size.Y != h))
            MapSubViewport.Size = new Vector2I(w, h);
    }
}