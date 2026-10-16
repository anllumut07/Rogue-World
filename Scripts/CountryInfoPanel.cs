using Godot;
using System;
using System.Linq;

// Seçili ülkenin bilgi paneli (sol overlay): HUD altında, sabit genişlikte, ScrollContainer ile taşan içerik kaydırılır.
// M3.2: PLAY AS butonu → GameManager.SetPlayerCountry + buton durumu geri bildirimi.
// M11: Overlay olarak Game köküne eklenir; LeftStack/LeftPanel KALDIRILDI.
public partial class CountryInfoPanel : PanelContainer
{
    private FlagBox _flagBox;
    private Label _nameLabel;
    private Label _capitalLabel;
    private Label _ideologyLabel;
    private Label _detailNoteLabel;     // fallback ülkeler: "ayrıntılı veri yok" notu
    private Label _stabilityLabel;
    private Label _warSupportLabel;
    private Label _ppLabel;
    private Label _populationLabel;
    private Label _manpowerLabel;
    private Label _gdpLabel;
    private Label _factoriesLabel;
    private Label _resourcesLabel;
    private Label _divisionsLabel;
    private Label _warStatusLabel;      // M7: savaş durumu
    private Button _playButton;
    private Button _declareWarButton;   // M7: savaş ilanı
    private Button _closeButton;        // X: paneli kapat

    // M17A: WAR JUSTIFICATION UI
    private VBoxContainer _warJustificationBox;
    private Label _warJustificationTitle;
    private VBoxContainer _checklistContainer;
    private VBoxContainer _buildCaseContainer;

    private CountryData _currentData;
    private string _currentCountryId = "";

    private Tween _fadeTween; // aktif fade animasyonu — yeni fade öncesi Kill() edilir (yarış önleme)

    // PauseMenu ESC çakışması için: panel görünür mü? (SettingsPanel.IsOpen deseni)
    private static CountryInfoPanel _instance;
    public static bool IsOpen =>
        _instance != null && GodotObject.IsInstanceValid(_instance) && _instance.Visible;

    // X / ESC kapatma isteği → Game dinler → WorldMap.ClearSelection() (seçim + panel kalkar)
    public event Action CloseRequested;

    private GameManager GM => GameManager.Instance;

    public override void _Ready()
    {
        _instance = this;

        // Ortak koyu tema
        UITheme.ApplyTheme(this);

        BuildUi();

        // Oyuncu ülkesi değişirse buton durumunu güncelle (ör. başka yerden değişirse)
        if (GameManager.IsAvailable)
        {
            GM.PlayerCountryChanged += OnPlayerCountryChanged;
            GM.PlayerCountryDataChanged += OnPlayerCountryDataChanged;
        }

        Hide();
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
        if (!GameManager.IsAvailable) return;
        GM.PlayerCountryChanged -= OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged -= OnPlayerCountryDataChanged;
    }

    // ESC: paneli kapat. PauseMenu Layer=100 önce alır ama IsOpen true iken ESC'yi bize bırakır.
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            GD.Print("[Diag] CountryInfoPanel: ESC → kapatma isteği");
            CloseRequested?.Invoke();
        }
    }

    // Overlay konumlandırma: pencere boyutu değişince panel pozisyonu/boyutunu güncelle
    public override void _Process(double delta)
    {
        UpdateOverlayRect();
    }

    private void UpdateOverlayRect()
    {
        if (!Visible) return;
        var winSize = GetViewportRect().Size;
        // HUD altında başla (Game.HudHeight), alt kenar boşluk 16px
        float top = Game.HudHeight;
        float height = winSize.Y - top - 16;
        // Sabit genişlik: 360px, ama ekran dar ise ekrana sığdır (min 280)
        float width = Mathf.Clamp(360f, 280f, winSize.X * 0.9f);

        Position = new Vector2(16, top);
        Size = new Vector2(width, height);
        // PanelContainer içindeki ScrollContainer zaten ExpandFill → boyut değişince auto layout
    }

    private void OnPlayerCountryChanged(string id) => UpdatePlayButton();

    // Günlük tick / savaş durumu değişince panel verilerini tazele
    private void OnPlayerCountryDataChanged()
    {
        if (_currentData != null && !string.IsNullOrEmpty(_currentCountryId))
            UpdateWarStatus();
    }

    // PLAY AS butonunu oyuncu ülkesine göre günceller (metin + disabled).
    // Kilit: oyuncu ülkesi bir kez seçilince oyun sürerken değiştirilemez —
    // diğer ülkelerde buton kilitli gösterilir (yalnızca yeni oyunda değişir).
    private void UpdatePlayButton()
    {
        if (_playButton == null || !GodotObject.IsInstanceValid(_playButton)) return;

        bool isPlayer = GM.HasPlayer && _currentCountryId == GM.PlayerCountryId;
        bool locked = GM.IsPlayerCountryLocked;

        if (isPlayer)
        {
            _playButton.Text = "PLAYING AS THIS COUNTRY";
            _playButton.Disabled = true;
            _playButton.TooltipText = "You are playing as this country";
        }
        else if (locked)
        {
            _playButton.Text = "COUNTRY LOCKED";
            _playButton.Disabled = true;
            _playButton.TooltipText = "Locked: your country is chosen for this game. Start a new game to play as another country.";
        }
        else
        {
            _playButton.Text = "PLAY AS THIS COUNTRY";
            _playButton.Disabled = string.IsNullOrEmpty(_currentCountryId);
            _playButton.TooltipText = "Select this country as your player country";
        }
    }

    private void BuildUi()
    {
        // Overlay panel: başlangıçta gizli, _Process'te konumlandırılır
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        MouseFilter = Control.MouseFilterEnum.Stop; // içindeki butonlar tıklanabilsin

        var scroll = new ScrollContainer();
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; // sadece dikey scroll
        AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(vbox);

        // --- Bayrak placeholder + ülke adı satırı + X kapat butonu ---
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(headerRow);

        _flagBox = new FlagBox { CustomMinimumSize = new Vector2(42, 28) };
        headerRow.AddChild(_flagBox);

        _nameLabel = NewLabel("No Country Selected", 24, new Color(0.85f, 0.66f, 0.22f));
        _nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; // X butonunu en sağa iter
        headerRow.AddChild(_nameLabel);

        // X: paneli kapat (seçim de kalkar — CloseRequested → Game → ClearSelection)
        _closeButton = new Button
        {
            Text = "×",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Close panel (ESC)",
            CustomMinimumSize = new Vector2(30, 30)
        };
        _closeButton.AddThemeFontSizeOverride("font_size", 16);
        _closeButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.45f, 0.4f));
        _closeButton.AddThemeColorOverride("font_hover_color", new Color(1f, 0.35f, 0.3f));
        _closeButton.AddThemeColorOverride("font_pressed_color", Colors.White);
        _closeButton.Pressed += () =>
        {
            GD.Print("[Diag] CountryInfoPanel: X butonu → kapatma isteği");
            CloseRequested?.Invoke();
        };
        headerRow.AddChild(_closeButton);

        _capitalLabel = NewLabel("", 16);
        vbox.AddChild(_capitalLabel);

        _ideologyLabel = NewLabel("", 16);
        vbox.AddChild(_ideologyLabel);

        // Fallback ülke notu: countries_data.json'da olmayan ülkeler (tahmini değerler)
        _detailNoteLabel = NewLabel("", 13, UITheme.TextMuted);
        vbox.AddChild(_detailNoteLabel);

        vbox.AddChild(new HSeparator());

        var row = new GridContainer { Columns = 2 };
        row.AddThemeConstantOverride("h_separation", 20);
        row.AddThemeConstantOverride("v_separation", 6);
        vbox.AddChild(row);

        _stabilityLabel = NewLabel("Stability: —", 16);
        row.AddChild(_stabilityLabel);
        _warSupportLabel = NewLabel("War Support: —", 16);
        row.AddChild(_warSupportLabel);
        _ppLabel = NewLabel("Political Power: —", 16);
        row.AddChild(_ppLabel);

        vbox.AddChild(new HSeparator());

        _gdpLabel = NewLabel("GDP: —", 16);
        vbox.AddChild(_gdpLabel);
        _populationLabel = NewLabel("Population: —", 16);
        vbox.AddChild(_populationLabel);
        _manpowerLabel = NewLabel("Manpower: —", 16);
        vbox.AddChild(_manpowerLabel);

        vbox.AddChild(new HSeparator());

        _factoriesLabel = NewLabel("Factories: —", 16);
        vbox.AddChild(_factoriesLabel);

        vbox.AddChild(new HSeparator());

        _resourcesLabel = NewLabel("Resources: —", 16);
        vbox.AddChild(_resourcesLabel);

        vbox.AddChild(new HSeparator());

        _divisionsLabel = NewLabel("Divisions: —", 16);
        vbox.AddChild(_divisionsLabel);

        // M7: Savaş durumu etiketi (AT WAR / capitulated)
        _warStatusLabel = NewLabel("", 16, new Color(1f, 0.35f, 0.3f));
        vbox.AddChild(_warStatusLabel);

        // M17A: WAR JUSTIFICATION bölümü — casus belli checklist + BUILD CASE butonları
        _warJustificationBox = new VBoxContainer();
        _warJustificationBox.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_warJustificationBox);

        _warJustificationTitle = NewLabel("WAR JUSTIFICATION", 14, UITheme.Gold);
        _warJustificationBox.AddChild(_warJustificationTitle);

        _checklistContainer = new VBoxContainer();
        _checklistContainer.AddThemeConstantOverride("separation", 2);
        _warJustificationBox.AddChild(_checklistContainer);

        _buildCaseContainer = new VBoxContainer();
        _buildCaseContainer.AddThemeConstantOverride("separation", 4);
        _warJustificationBox.AddChild(_buildCaseContainer);

        // M7: DECLARE WAR butonu
        _declareWarButton = new Button
        {
            Text = $"DECLARE WAR ({WarSystem.DeclareWarPPCost} PP)",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Declare war on this country (costs Political Power)"
        };
        _declareWarButton.AddThemeColorOverride("font_disabled_color", UITheme.TextMuted);
        _declareWarButton.Pressed += () =>
        {
            if (!string.IsNullOrEmpty(_currentCountryId))
            {
                // M8: DiplomacySystem üzerinden — garanti tetiklemesi + faksiyon çağrısı dahil
                bool ok = DiplomacySystem.DeclareWar(_currentCountryId);
                if (ok)
                {
                    UpdateWarStatus();
                    UpdatePlayButton();
                    // Savaş ilanı PP/WS/STA değiştirdi → etiketleri tazele
                    if (_currentData != null)
                    {
                        _ppLabel.Text = $"Political Power: {_currentData.PoliticalPower:N1}";
                        _stabilityLabel.Text = $"Stability: {_currentData.StabilityPercent:N0}%";
                        _warSupportLabel.Text = $"War Support: {_currentData.WarSupportPercent:N0}%";
                    }
                }
            }
        };
        vbox.AddChild(_declareWarButton);
        UITheme.AddHoverScale(_declareWarButton);

        // "Play as" butonu (M3.2: SetPlayerCountry + durum geri bildirimi)
        // EN ALTA + belirgin: altın çerçeve, büyük font, parlak hover.
        _playButton = new Button
        {
            Text = "PLAY AS THIS COUNTRY",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 46),
            TooltipText = "Select this country as your player country"
        };
        _playButton.AddThemeFontSizeOverride("font_size", 18);
        _playButton.AddThemeColorOverride("font_color", UITheme.GoldBright);
        _playButton.AddThemeColorOverride("font_hover_color", UITheme.GoldBright);
        _playButton.AddThemeColorOverride("font_pressed_color", Colors.White);
        _playButton.AddThemeColorOverride("font_disabled_color", UITheme.TextMuted);

        // Belirgin altın çerçeve stili (normal/hover/pressed)
        var playNormal = new StyleBoxFlat
        {
            BgColor = new Color("#1A2410"),
            BorderColor = UITheme.Gold,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        };
        playNormal.SetBorderWidthAll(2);
        playNormal.SetCornerRadiusAll(4);
        var playHover = new StyleBoxFlat
        {
            BgColor = new Color("#2A3A18"),
            BorderColor = UITheme.GoldBright,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        };
        playHover.SetBorderWidthAll(2);
        playHover.SetCornerRadiusAll(4);
        var playPressed = new StyleBoxFlat
        {
            BgColor = new Color("#3A4A20"),
            BorderColor = UITheme.GoldBright,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        };
        playPressed.SetBorderWidthAll(3);
        playPressed.SetCornerRadiusAll(4);
        var playDisabled = new StyleBoxFlat
        {
            BgColor = new Color("#0D1210"),
            BorderColor = new Color("#2A3542"),
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        };
        playDisabled.SetBorderWidthAll(1);
        playDisabled.SetCornerRadiusAll(4);
        _playButton.AddThemeStyleboxOverride("normal", playNormal);
        _playButton.AddThemeStyleboxOverride("hover", playHover);
        _playButton.AddThemeStyleboxOverride("pressed", playPressed);
        _playButton.AddThemeStyleboxOverride("disabled", playDisabled);

        _playButton.Pressed += () =>
        {
            if (!string.IsNullOrEmpty(_currentCountryId))
            {
                GD.Print($"CountryInfoPanel: PLAY AS {_currentCountryId}");
                GM.SetPlayerCountry(_currentCountryId);
            }
        };
        vbox.AddChild(_playButton); // en altta
        UITheme.AddHoverScale(_playButton);
    }

    private Label NewLabel(string text, int size, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    // M7: Savaş durumu + DECLARE WAR butonunun görünürlüğü
    // M17A: casus belli checklist + BUILD CASE butonları + eksik koşul tooltip'i
    private void UpdateWarStatus()
    {
        if (_warStatusLabel == null || _declareWarButton == null ||
            !GodotObject.IsInstanceValid(_warStatusLabel) || !GodotObject.IsInstanceValid(_declareWarButton))
            return;

        // Varsayılan: gizle
        _warStatusLabel.Text = "";
        _declareWarButton.Disabled = true;
        ClearJustificationUi();

        if (_currentData == null || string.IsNullOrEmpty(_currentCountryId)) return;

        // İlhaç edilmiş ülke: buton tamamen kapalı, etiket bilgi verir
        if (_currentData.IsAnnexed)
        {
            var owner = CountryDatabase.Get(_currentData.OwnerId);
            _warStatusLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _warStatusLabel.Text = $"ANNEXED by {owner?.Name ?? _currentData.OwnerId}";
            return;
        }

        // Oyuncu ülkesi seçiliyse savaş ilanı yok
        if (GM.HasPlayer && _currentCountryId == GM.PlayerCountryId) return;

        // Savaşta mı?
        var war = WarSystem.GetWarWith(_currentCountryId);
        if (war != null)
        {
            _warStatusLabel.AddThemeColorOverride("font_color", new Color(1f, 0.35f, 0.3f));
            _warStatusLabel.Text = $"AT WAR — Progress: {war.WarProgress:F0}% | Enemy divisions: {war.EnemyDivisions}";
            _declareWarButton.Disabled = true;
            _declareWarButton.Text = "AT WAR";
            return;
        }

        // Savaş ilanı mümkün mü? (oyuncu olmalı)
        if (!GM.HasPlayer || GM.PlayerCountryData == null) return;

        // === M17A: Casus belli checklist ===
        int warCost = WarSystem.CalculateWarGoalCost(_currentCountryId);
        _declareWarButton.Text = $"DECLARE WAR ({warCost} PP)";

        var check = CasusBelliSystem.CheckWarPreconditions(GM.PlayerCountryId, _currentCountryId);
        BuildJustificationUi(check);

        _declareWarButton.Disabled = !check.CanDeclare;
        if (!check.CanDeclare)
        {
            // Tooltip: eksik koşulları listele
            var missing = check.Items.Where(i => !i.Met).Select(i => $"✗ {i.Label}: {i.Detail}");
            _declareWarButton.TooltipText = "War declaration blocked:\n" + string.Join("\n", missing);
        }
        else
        {
            _declareWarButton.TooltipText = "Declare war on this country (justified)";
        }
    }

    // M17A: casus belli checklist + BUILD CASE UI'ını temizle
    private void ClearJustificationUi()
    {
        if (_checklistContainer == null || !GodotObject.IsInstanceValid(_checklistContainer)) return;
        if (_buildCaseContainer == null || !GodotObject.IsInstanceValid(_buildCaseContainer)) return;
        foreach (var child in _checklistContainer.GetChildren()) child.QueueFree();
        foreach (var child in _buildCaseContainer.GetChildren()) child.QueueFree();
        if (_warJustificationBox != null && GodotObject.IsInstanceValid(_warJustificationBox))
            _warJustificationBox.Visible = false;
    }

    // M17A: checklist etiketleri + BUILD CASE butonları kur
    private void BuildJustificationUi(CasusBelliSystem.WarCheckResult check)
    {
        _warJustificationBox.Visible = true;

        // Checklist: her koşul ✓ (yeşil) / ✗ (kırmızı)
        foreach (var item in check.Items)
        {
            var l = NewLabel($"{(item.Met ? "✓" : "✗")} {item.Label}", 13,
                item.Met ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.9f, 0.4f, 0.35f));
            if (!item.Met && !string.IsNullOrEmpty(item.Detail))
            {
                l.TooltipText = item.Detail;
                // Detay kırmızı koşullarda yanına küçük yazı olarak da gelsin
                if (check.Items.Count(i => !i.Met) <= 3)
                    l.Text += $" — {item.Detail}";
            }
            _checklistContainer.AddChild(l);
        }

        // Devam eden gerekçe inşaatı (build case) varsa göster
        var inProgress = CasusBelliSystem.Justifications
            .Where(j => j.CountryId == GM.PlayerCountryId && j.TargetId == _currentCountryId
                     && !j.IsConsumed && (j.BuildDaysLeft > 0 || j.UltimatumDaysLeft > 0))
            .ToList();
        foreach (var j in inProgress)
        {
            var t = CasusBelliSystem.GetType(j.TypeId);
            string status = j.BuildDaysLeft > 0
                ? $"BUILDING CASE: {t?.Name ?? j.TypeId} — {j.BuildDaysLeft} days left"
                : $"CASE READY — ultimatum: {j.UltimatumDaysLeft} days left";
            var l = NewLabel(status, 13, UITheme.GoldBright);
            _checklistContainer.AddChild(l);
        }

        // BUILD CASE butonları: başlatılabilir gerekçe türleri
        var startable = CasusBelliSystem.GetStartableTypes(GM.PlayerCountryId, _currentCountryId);
        foreach (var type in startable)
        {
            var b = new Button
            {
                Text = $"BUILD CASE: {type.Name} ({type.BuildDays}d, {type.PPCost} PP)",
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = type.Description
            };
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () =>
            {
                bool ok = CasusBelliSystem.StartBuildCase(GM.PlayerCountryId, _currentCountryId, type.Id);
                if (ok)
                {
                    // UI tazele: checklist + buton durumu değişir
                    UpdateWarStatus();
                    if (_currentData != null)
                        _ppLabel.Text = $"Political Power: {_currentData.PoliticalPower:N1}";
                }
            };
            _buildCaseContainer.AddChild(b);
            UITheme.AddHoverScale(b);
        }
    }

    public void ShowCountry(CountryData data, string name)
    {
        // === DEBUG PRINT (istemci isteği) ===
        GD.Print($"[CountryInfoPanel] ShowCountry: id={data?.Id ?? "null"}, name={name}");
        GD.Print($"  panel=null? {this == null}, Visible={Visible}, Pos={Position}, Size={Size}, GlobalPos={GlobalPosition}, parent={GetParent()?.GetType().Name ?? "null"}, ZIndex={ZIndex}");

        _currentData = data;
        _currentCountryId = data?.Id ?? "";
        _nameLabel.Text = $"{name} [{_currentCountryId}]";
        _capitalLabel.Text = $"Capital: {data.Capital}";
        _ideologyLabel.Text = $"Ideology: {data.Ideology}";

        // Fallback notu: countries_data.json'da olmayan ülkeler tahmini değerle gelir
        _detailNoteLabel.Text = data.IsDetailed ? "" : "No detailed data — shown values are estimates";

        _stabilityLabel.Text = $"Stability: {data.StabilityPercent:N0}%";
        _warSupportLabel.Text = $"War Support: {data.WarSupportPercent:N0}%";
        _ppLabel.Text = $"Political Power: {data.PoliticalPower:N1}";

        _gdpLabel.Text = $"GDP: {data.EstimatedGDP:N0}";
        _populationLabel.Text = $"Population: {data.Population:N0}";
        _manpowerLabel.Text = $"Manpower Pool: {data.ManpowerPool:N0}";

        _factoriesLabel.Text = $"Factories: {data.CivFactories} Civ / {data.MilFactories} Mil / {data.Dockyards} Dock";

        _resourcesLabel.Text = $"Resources: Oil {data.Oil} | Rubber {data.Rubber} | Steel {data.Steel} | Al {data.Aluminum} | W {data.Tungsten} | Cr {data.Chromium}";

        _divisionsLabel.Text = $"Starting Divisions: {data.StartingDivisions}";

        // Bayrak: FlagLibrary üzerinden önbellekli PNG / yedek
        _flagBox.SetCountry(data.Id);

        // Buton durumunu yeni seçime göre güncelle
        UpdatePlayButton();
        UpdateWarStatus();

        // Overlay rect'i hemen güncelle
        UpdateOverlayRect();

        // Açılış animasyonu: fade in — önceki fade'i öldür (paralel FadeOut yarışını önle)
        _fadeTween?.Kill();
        _fadeTween = UITheme.FadeIn(this, 0.12f);
    }

    public void HidePanel()
    {
        // Kapanış animasyonu: fade out, bitince gizle — önceki fade'i öldür
        _fadeTween?.Kill();
        _fadeTween = UITheme.FadeOut(this, 0.08f, () => Hide());
    }
}