using Godot;
using System;

// M15.1: Kilitli oyunda ülkeye tıklayınca açılan küçük kart (ad, bayrak, ilişki, savaş durumu).
// Tam CountryInfoPanel yalnızca seçim aşamasında (oyuncu ülkesi kilitlenmeden) açılır.
// Overlay: HUD altında sol üst köşe, sabit genişlik, X/ESC kapatır → Game ClearSelection.
public partial class CountryMiniCard : PanelContainer
{
    private FlagBox _flagBox;
    private Label _nameLabel;
    private Label _relationLabel;   // oyuncu → hedef ilişkisi
    private Label _warStatusLabel;  // AT WAR / ANNEXED / —

    private CountryData _currentData;
    private string _currentCountryId = "";

    private Tween _fadeTween; // aktif fade animasyonu — yeni fade öncesi Kill()

    // PauseMenu ESC çakışması için: kart görünür mü? (CountryInfoPanel.IsOpen deseni)
    private static CountryMiniCard _instance;
    public static bool IsOpen =>
        _instance != null && GodotObject.IsInstanceValid(_instance) && _instance.Visible;

    // X / ESC kapatma isteği → Game dinler → WorldMap.ClearSelection()
    public event Action CloseRequested;

    private GameManager GM => GameManager.Instance;

    public override void _Ready()
    {
        _instance = this;

        // Ortak koyu tema
        UITheme.ApplyTheme(this);

        BuildUi();

        // Savaş durumu günlük değişebilir → panel görünürken tazele
        if (GameManager.IsAvailable)
            GM.PlayerCountryDataChanged += OnPlayerCountryDataChanged;

        Hide();
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
        if (!GameManager.IsAvailable) return;
        GM.PlayerCountryDataChanged -= OnPlayerCountryDataChanged;
    }

    // ESC: kartı kapat. PauseMenu Layer=100 önce alır ama IsOpen true iken ESC'yi bize bırakır.
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            GD.Print("[CountryMiniCard] ESC → kapatma isteği");
            CloseRequested?.Invoke();
        }
    }

    // Overlay konumlandırma: pencere boyutu değişince kart pozisyonunu güncelle
    public override void _Process(double delta)
    {
        if (!Visible) return;
        var winSize = GetViewportRect().Size;
        // HUD altında başla, sol kenar 16px (CountryInfoPanel ile aynı hizalama)
        Position = new Vector2(16, Game.HudHeight);
        // Genişlik ekrana sığsın (min 240)
        float width = Mathf.Clamp(300f, 240f, winSize.X * 0.9f);
        Size = new Vector2(width, 0); // yükseklik içerikten otomatik
    }

    private void OnPlayerCountryDataChanged()
    {
        // Kart açıksa savaş durumunu tazele (savaş ilanı/kapanışı, ilhak)
        if (Visible && _currentData != null)
            UpdateRelationAndWar();
    }

    private void BuildUi()
    {
        MouseFilter = Control.MouseFilterEnum.Stop; // içindeki X butonu tıklanabilsin
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 6);
        margin.AddChild(vbox);

        // --- Bayrak + ülke adı + X ---
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(headerRow);

        _flagBox = new FlagBox { CustomMinimumSize = new Vector2(36, 24) };
        headerRow.AddChild(_flagBox);

        _nameLabel = new Label();
        _nameLabel.AddThemeFontSizeOverride("font_size", 19);
        _nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.66f, 0.22f));
        _nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; // X'i en sağa iter
        headerRow.AddChild(_nameLabel);

        // X: kartı kapat (seçim de kalkar — CloseRequested → Game → ClearSelection)
        var closeButton = new Button
        {
            Text = "×",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Close (ESC)",
            CustomMinimumSize = new Vector2(26, 26)
        };
        closeButton.AddThemeFontSizeOverride("font_size", 15);
        closeButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.45f, 0.4f));
        closeButton.AddThemeColorOverride("font_hover_color", new Color(1f, 0.35f, 0.3f));
        closeButton.AddThemeColorOverride("font_pressed_color", Colors.White);
        closeButton.Pressed += () =>
        {
            GD.Print("[CountryMiniCard] X → kapatma isteği");
            CloseRequested?.Invoke();
        };
        headerRow.AddChild(closeButton);

        // İlişki satırı (oyuncu → hedef)
        _relationLabel = new Label();
        _relationLabel.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(_relationLabel);

        // Savaş durumu satırı
        _warStatusLabel = new Label();
        _warStatusLabel.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(_warStatusLabel);
    }

    // Kartı doldur ve göster. Yalnızca kilitli oyunda Game tarafından çağrılır.
    public void ShowCountry(CountryData data, string name)
    {
        GD.Print($"[CountryMiniCard] ShowCountry: id={data?.Id ?? "null"}, name={name}");

        _currentData = data;
        _currentCountryId = data?.Id ?? "";
        _nameLabel.Text = $"{name} [{_currentCountryId}]";
        // Bayrak: FlagLibrary (PNG / yedek)
        _flagBox.SetCountry(_currentCountryId);

        UpdateRelationAndWar();

        // Açılış animasyonu: fade in — önceki fade'i öldür (yarış önleme)
        _fadeTween?.Kill();
        _fadeTween = UITheme.FadeIn(this, 0.12f);
    }

    private void UpdateRelationAndWar()
    {
        if (_relationLabel == null || _warStatusLabel == null ||
            !GodotObject.IsInstanceValid(_relationLabel) || !GodotObject.IsInstanceValid(_warStatusLabel))
            return;

        // Varsayılanlar
        _relationLabel.Text = "";
        _relationLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _warStatusLabel.Text = "";
        _warStatusLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);

        if (_currentData == null || string.IsNullOrEmpty(_currentCountryId)) return;

        // Kendi ülken: ilişki/savaş yerine not
        if (GM.HasPlayer && _currentCountryId == GM.PlayerCountryId)
        {
            _relationLabel.Text = "Your country";
            return;
        }

        // İlhaç edilmiş ülke
        if (_currentData.IsAnnexed)
        {
            var owner = CountryDatabase.Get(_currentData.OwnerId);
            _warStatusLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _warStatusLabel.Text = $"ANNEXED by {owner?.Name ?? _currentData.OwnerId}";
            return;
        }

        // İlişki (oyuncu → hedef); renk: pozitif yeşil, negatif kırmızı
        if (GM.HasPlayer)
        {
            int rel = DiplomacySystem.GetRelation(GM.PlayerCountryId, _currentCountryId);
            _relationLabel.Text = $"Relation: {rel:+#;-#;0}";
            _relationLabel.AddThemeColorOverride("font_color",
                rel > 0 ? new Color(0.55f, 0.85f, 0.55f) : rel < 0 ? new Color(0.95f, 0.45f, 0.42f) : UITheme.TextMuted);
        }

        // Savaş durumu
        var war = WarSystem.GetWarWith(_currentCountryId);
        if (war != null)
        {
            _warStatusLabel.AddThemeColorOverride("font_color", new Color(1f, 0.35f, 0.3f));
            _warStatusLabel.Text = $"AT WAR — Progress: {war.WarProgress:F0}% | Enemy div: {war.EnemyDivisions}";
        }
        else
        {
            _warStatusLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _warStatusLabel.Text = "At peace";
        }
    }

    public void HideCard()
    {
        // Kapanış animasyonu: fade out, bitince gizle — önceki fade'i öldür
        _fadeTween?.Kill();
        _fadeTween = UITheme.FadeOut(this, 0.08f, () => Hide());
    }
}
