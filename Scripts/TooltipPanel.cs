using Godot;

// M13.2: Detaylı tooltip paneli — imlecin yanında görünen, koyu temalı açıklama paneli.
// Tek bir örnek TooltipSystem tarafından yönetilir; tüm UI elemanlarında ortak kullanılır.
public partial class TooltipPanel : Control
{
    private Label _label;
    private string _pendingText = "";
    private double _pendingUntil = -1;
    private const float DelaySeconds = 0.22f;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = Control.MouseFilterEnum.Ignore; // fare olaylarını geçirir (engellemez)
        UITheme.ApplyTheme(this);

        var panelStyle = new StyleBoxFlat
        {
            BgColor = UITheme.PanelBg,
            BorderColor = UITheme.BorderDark,
        };
        panelStyle.SetBorderWidthAll(1);
        panelStyle.SetCornerRadiusAll(5);
        AddThemeStyleboxOverride("panel", panelStyle);

        _label = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        _label.AddThemeFontSizeOverride("font_size", 13);
        _label.AddThemeColorOverride("font_color", UITheme.TextMuted);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        var now = Time.GetTicksMsec() / 1000.0;

        if (!string.IsNullOrEmpty(_pendingText) && _pendingUntil >= 0 && now >= _pendingUntil)
        {
            Reveal(_pendingText);
            _pendingText = "";
            _pendingUntil = -1;
        }

        if (!Visible) return;

        // Her karede boyutu etikete göre ayarla (layout ilk karede netleşir)
        var labelSize = _label.GetSize();
        Size = new Vector2(Mathf.Max(160, labelSize.X + 24), Mathf.Max(30, labelSize.Y + 14));

        // İmleci takip et (küçük ofsetle), viewport sınırları içinde kal
        var mp = GetViewport().GetMousePosition();
        var desired = mp + new Vector2(16, 18);
        var vpSize = GetViewportRect().Size;
        Position = desired.Clamp(Vector2.Zero, new Vector2(vpSize.X, vpSize.Y));
    }

    public void Show(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Hide();
            return;
        }
        _pendingText = text;
        _pendingUntil = Time.GetTicksMsec() / 1000.0 + DelaySeconds;
        if (_label != null)
            _label.Text = text;
    }

    public new void Hide()
    {
        Visible = false;
        _pendingText = "";
        _pendingUntil = -1;
    }

    private void Reveal(string text)
    {
        _label.Text = text;
        Visible = true;
    }
}
