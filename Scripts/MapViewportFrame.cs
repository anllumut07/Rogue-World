using Godot;

// Oyun ekranının yerleşimi: merkez (SubViewportContainer → WorldMap) TAM EKRAN.
// HUD, CountryInfoPanel ve diğer paneller ayrı CanvasLayer/Control olarak overlay'dir.
// SubViewportContainer Stretch=true ile pencere boyutunu tam doldurur.
public partial class MapViewportFrame : Control
{
    public SubViewportContainer CenterViewport { get; private set; }

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Pass;

        // Merkez viewport: TAM EKRAN (FullRect)
        CenterViewport = new SubViewportContainer();
        CenterViewport.MouseFilter = Control.MouseFilterEnum.Pass;
        CenterViewport.Stretch = true; // SubViewport boyutunu container yönetsin
        AddChild(CenterViewport);

        // Layout'u hemen ve scene oturunca uygula
        EnforceLayout();
        Callable.From(EnforceLayout).CallDeferred();

        // Pencere boyutu değişince yeniden uygula
        GetTree().Root.SizeChanged += EnforceLayout;

        GD.Print("MapViewportFrame: hazır — CenterViewport FullRect + Stretch");
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
    }
}