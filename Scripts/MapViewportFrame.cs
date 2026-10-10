using Godot;

// Oyun ekranının yerleşimi: üst bar (HUD), sol panel (ülke bilgisi),
// merkez (SubViewport → WorldMap), sağ panel (gelecekteki paneller).
//
// ÖNEMLİ: Godot 4'te kodla oluşturulan anchored Control'lerin boyutu,
// tree'ye girerken otomatik hesaplanmaz. Anchor setter'ları tree'deyken
// çağrılınca _size_changed() tetiklenir. Bu yüzden EnforceLayout() ile
// preset'ler yeniden uygulanır (scene yüklenince ve pencere boyutu değişince).
public partial class MapViewportFrame : Control
{
    public Control TopBar { get; private set; }
    public Control LeftPanel { get; private set; }
    public VBoxContainer LeftStack { get; private set; }
    public SubViewportContainer CenterViewport { get; private set; }
    public Control RightPanel { get; private set; }
    public VBoxContainer RightStack { get; private set; }

    const int _topHeight = 56;
    const int _leftWidth = 340;
    const int _rightWidth = 380;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Pass;

        // Üst bar
        TopBar = new Control();
        TopBar.CustomMinimumSize = new Vector2(0, _topHeight);
        TopBar.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(TopBar);

        // Sol panel
        LeftPanel = new Control();
        LeftPanel.CustomMinimumSize = new Vector2(_leftWidth, 0);
        LeftPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(LeftPanel);

        LeftStack = new VBoxContainer();
        LeftStack.MouseFilter = Control.MouseFilterEnum.Pass;
        LeftPanel.AddChild(LeftStack);

        // Merkez viewport
        CenterViewport = new SubViewportContainer();
        CenterViewport.MouseFilter = Control.MouseFilterEnum.Pass;
        CenterViewport.ZIndex = 10;
        AddChild(CenterViewport);

        // Sağ panel
        RightPanel = new Control();
        RightPanel.CustomMinimumSize = new Vector2(_rightWidth, 0);
        RightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(RightPanel);

        RightStack = new VBoxContainer();
        RightStack.MouseFilter = Control.MouseFilterEnum.Pass;
        RightPanel.AddChild(RightStack);

        // Dekoratif dış çerçeve (en altta çizilir, tıklamaları engellemez)
        var outer = new PanelContainer();
        outer.ZIndex = -1;
        outer.MouseFilter = Control.MouseFilterEnum.Ignore;
        var outerStyle = new StyleBoxFlat();
        outerStyle.BgColor = new Color(0, 0, 0, 0);
        outerStyle.BorderColor = new Color(0.85f, 0.66f, 0.22f);
        outerStyle.SetBorderWidthAll(2);
        outerStyle.SetCornerRadiusAll(10);
        outer.AddThemeStyleboxOverride("panel", outerStyle);
        AddChild(outer);

        // Layout'u hemen ve scene oturunca uygula
        EnforceLayout();
        Callable.From(EnforceLayout).CallDeferred();

        // Pencere boyutu değişince yeniden uygula
        GetTree().Root.SizeChanged += EnforceLayout;

        GD.Print("MapViewportFrame: hazır");
    }

    public override void _ExitTree()
    {
        if (GetTree() != null)
            GetTree().Root.SizeChanged -= EnforceLayout;
    }

    // Anchor preset'lerini yeniden uygula — tree'deyken çağrılınca
    // her setter _size_changed() tetikler ve gerçek boyut hesaplanır.
    public void EnforceLayout()
    {
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        TopBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);

        LeftPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
        LeftStack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        CenterViewport.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        RightPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
        RightStack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }
}
