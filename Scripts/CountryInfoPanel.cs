using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Country Info Panel (M3.2 + M7 + M17A + M46.3 + Stage 2 UI Upgrade):
/// Left-side overlay panel shown when clicking a country.
/// Uses GameTheme colors and reusable UI components (Chip, TooltipBuilder).
/// Preserves overlay positioning, ESC handling (CloseRequested), animations.
/// </summary>
public partial class CountryInfoPanel : PanelContainer
{
    // UI elements
    private FlagBox _flagBox;
    private Label _nameLabel;
    private Label _capitalLabel;
    private Label _ideologyLabel;
    private Label _detailNoteLabel;
    private Label _stabilityLabel;
    private Label _warSupportLabel;
    private Label _ppLabel;
    private Label _populationLabel;
    private Label _manpowerLabel;
    private Label _cultureLabel;
    private Label _gdpLabel;
    private Label _factoriesLabel;
    private Label _resourcesLabel;
    private Label _divisionsLabel;
    private Label _warStatusLabel;
    private Button _playButton;
    private Button _declareWarButton;
    private Button _closeButton;

    // War justification UI
    private VBoxContainer _warJustificationBox;
    private Label _warJustificationTitle;
    private VBoxContainer _checklistContainer;
    private VBoxContainer _buildCaseContainer;

    private CountryData _currentData;
    private string _currentCountryId = "";
    private Tween _fadeTween;

    // Singleton for ESC handling
    private static CountryInfoPanel _instance;
    public static bool IsOpen =>
        _instance != null && GodotObject.IsInstanceValid(_instance) && _instance.Visible;

    // Close request event (handled by Game → WorldMap.ClearSelection)
    public event Action CloseRequested;

    private GameManager GM => GameManager.Instance;

    public override void _Ready()
    {
        _instance = this;

        // Apply GameTheme
        GameTheme.Apply(this);

        BuildUi();

        if (GameManager.IsAvailable)
        {
            GM.PlayerCountryChanged += OnPlayerCountryChanged;
            GM.PlayerCountryDataChanged += OnPlayerCountryDataChanged;
        }

        Hide();
        GD.Print("CountryInfoPanel: hazır (Stage 2 UI)");
    }

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
        if (!GameManager.IsAvailable) return;
        GM.PlayerCountryChanged -= OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged -= OnPlayerCountryDataChanged;
    }

    // ESC: trigger close request (Game handles ClearSelection)
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            GD.Print("[CountryInfoPanel] ESC → kapatma isteği");
            CloseRequested?.Invoke();
        }
    }

    // Update overlay position/size on resize
    public override void _Process(double delta)
    {
        UpdateOverlayRect();
    }

    private void UpdateOverlayRect()
    {
        if (!Visible) return;
        var winSize = GetViewportRect().Size;
        float top = Game.HudHeight;
        float height = Mathf.Max(0f, winSize.Y - top - 16);
        float width = Mathf.Clamp(winSize.X * 0.9f, 280f, 360f);

        Position = new Vector2(16, top);
        Size = new Vector2(width, height);
    }

    private void OnPlayerCountryChanged(string id) => UpdatePlayButton();

    private void OnPlayerCountryDataChanged()
    {
        if (_currentData != null && !string.IsNullOrEmpty(_currentCountryId))
            UpdateWarStatus();
    }

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
        // Overlay panel: positioned by _Process, hidden initially
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        MouseFilter = Control.MouseFilterEnum.Ignore; // hidden initially, ignore input
        AddThemeStyleboxOverride("normal", GameTheme.CardBox());

        var scroll = new ScrollContainer();
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        scroll.AddChild(vbox);

        // --- Header: flag + name + close button ---
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        vbox.AddChild(headerRow);

        _flagBox = new FlagBox { CustomMinimumSize = new Vector2(42, 28) };
        headerRow.AddChild(_flagBox);

        _nameLabel = NewLabel("No Country Selected", GameTheme.FontSizeLarge, GameTheme.Gold);
        _nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerRow.AddChild(_nameLabel);

        _closeButton = new Button
        {
            Text = "×",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Close panel (ESC)",
            CustomMinimumSize = new Vector2(30, 30)
        };
        _closeButton.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _closeButton.AddThemeColorOverride("font_color", GameTheme.Red);
        _closeButton.AddThemeColorOverride("font_hover_color", GameTheme.RedBright);
        _closeButton.Pressed += () =>
        {
            GD.Print("[CountryInfoPanel] X butonu → kapatma isteği");
            CloseRequested?.Invoke();
        };
        headerRow.AddChild(_closeButton);

        _capitalLabel = NewLabel("", GameTheme.FontSizeNormal);
        vbox.AddChild(_capitalLabel);

        _ideologyLabel = NewLabel("", GameTheme.FontSizeNormal);
        vbox.AddChild(_ideologyLabel);

        // Fallback note
        _detailNoteLabel = NewLabel("", GameTheme.FontSizeSmall, GameTheme.TextMuted);
        vbox.AddChild(_detailNoteLabel);

        vbox.AddChild(new HSeparator());

        // --- Stats grid ---
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", GameTheme.SpaceL);
        grid.AddThemeConstantOverride("v_separation", GameTheme.SpaceS);
        vbox.AddChild(grid);

        _stabilityLabel = NewLabel("Stability: —", GameTheme.FontSizeNormal);
        grid.AddChild(_stabilityLabel);
        _warSupportLabel = NewLabel("War Support: —", GameTheme.FontSizeNormal);
        grid.AddChild(_warSupportLabel);
        _ppLabel = NewLabel("Political Power: —", GameTheme.FontSizeNormal);
        grid.AddChild(_ppLabel);

        vbox.AddChild(new HSeparator());

        _gdpLabel = NewLabel("GDP: —", GameTheme.FontSizeNormal);
        vbox.AddChild(_gdpLabel);
        _populationLabel = NewLabel("Population: —", GameTheme.FontSizeNormal);
        vbox.AddChild(_populationLabel);
        _manpowerLabel = NewLabel("Manpower: —", GameTheme.FontSizeNormal);
        vbox.AddChild(_manpowerLabel);

        // Culture profile (M46.3)
        _cultureLabel = NewLabel("Culture: —", GameTheme.FontSizeNormal);
        vbox.AddChild(_cultureLabel);

        vbox.AddChild(new HSeparator());

        _factoriesLabel = NewLabel("Factories: —", GameTheme.FontSizeNormal);
        vbox.AddChild(_factoriesLabel);

        vbox.AddChild(new HSeparator());

        _resourcesLabel = NewLabel("Resources: —", GameTheme.FontSizeNormal);
        vbox.AddChild(_resourcesLabel);

        vbox.AddChild(new HSeparator());

        _divisionsLabel = NewLabel("Divisions: —", GameTheme.FontSizeNormal);
        vbox.AddChild(_divisionsLabel);

        // War status
        _warStatusLabel = NewLabel("", GameTheme.FontSizeNormal, GameTheme.Red);
        vbox.AddChild(_warStatusLabel);

        // War justification box
        _warJustificationBox = new VBoxContainer();
        _warJustificationBox.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        vbox.AddChild(_warJustificationBox);

        _warJustificationTitle = NewLabel("WAR JUSTIFICATION", GameTheme.FontSizeSmall, GameTheme.Gold);
        _warJustificationBox.AddChild(_warJustificationTitle);

        _checklistContainer = new VBoxContainer();
        _checklistContainer.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        _warJustificationBox.AddChild(_checklistContainer);

        _buildCaseContainer = new VBoxContainer();
        _buildCaseContainer.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        _warJustificationBox.AddChild(_buildCaseContainer);

        // DECLARE WAR button
        _declareWarButton = new Button
        {
            Text = $"DECLARE WAR ({WarSystem.DeclareWarPPCost} PP)",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Declare war on this country (costs Political Power)"
        };
        _declareWarButton.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _declareWarButton.AddThemeColorOverride("font_disabled_color", GameTheme.TextMuted);
        _declareWarButton.Pressed += () =>
        {
            if (!string.IsNullOrEmpty(_currentCountryId))
            {
                bool ok = DiplomacySystem.DeclareWar(_currentCountryId);
                if (ok)
                {
                    UpdateWarStatus();
                    UpdatePlayButton();
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
        GameTheme.StyleButton(_declareWarButton);

        // PLAY AS button (prominent, at bottom)
        _playButton = new Button
        {
            Text = "PLAY AS THIS COUNTRY",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 46),
            TooltipText = "Select this country as your player country"
        };
        _playButton.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        _playButton.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        _playButton.AddThemeColorOverride("font_hover_color", GameTheme.GoldBright);
        _playButton.AddThemeColorOverride("font_pressed_color", GameTheme.TextOnGold);
        _playButton.AddThemeColorOverride("font_disabled_color", GameTheme.TextMuted);

        // Gold border style
        var playNormal = new StyleBoxFlat
        {
            BgColor = new Color("#1A2410"),
            BorderColor = GameTheme.Gold,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        };
        playNormal.SetBorderWidthAll(2);
        playNormal.SetCornerRadiusAll(GameTheme.RadiusNormal);
        var playHover = new StyleBoxFlat
        {
            BgColor = new Color("#2A3A18"),
            BorderColor = GameTheme.GoldBright,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        };
        playHover.SetBorderWidthAll(2);
        playHover.SetCornerRadiusAll(GameTheme.RadiusNormal);
        var playPressed = new StyleBoxFlat
        {
            BgColor = new Color("#3A4A20"),
            BorderColor = GameTheme.GoldBright,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        };
        playPressed.SetBorderWidthAll(3);
        playPressed.SetCornerRadiusAll(GameTheme.RadiusNormal);
        var playDisabled = new StyleBoxFlat
        {
            BgColor = new Color("#0D1210"),
            BorderColor = GameTheme.BorderMedium,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8
        };
        playDisabled.SetBorderWidthAll(1);
        playDisabled.SetCornerRadiusAll(GameTheme.RadiusNormal);
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
        vbox.AddChild(_playButton);
        GameTheme.StyleButton(_playButton);
    }

    private Label NewLabel(string text, int size, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    // M7 + M17A: War status + casus belli checklist + BUILD CASE
    private void UpdateWarStatus()
    {
        if (_warStatusLabel == null || _declareWarButton == null ||
            !GodotObject.IsInstanceValid(_warStatusLabel) || !GodotObject.IsInstanceValid(_declareWarButton))
            return;

        _warStatusLabel.Text = "";
        _declareWarButton.Disabled = true;
        ClearJustificationUi();

        if (_currentData == null || string.IsNullOrEmpty(_currentCountryId)) return;

        // Annexed country
        if (_currentData.IsAnnexed)
        {
            var owner = CountryDatabase.Get(_currentData.OwnerId);
            _warStatusLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            _warStatusLabel.Text = $"ANNEXED by {owner?.Name ?? _currentData.OwnerId}";
            return;
        }

        // Player's own country
        if (GM.HasPlayer && _currentCountryId == GM.PlayerCountryId) return;

        // At war
        var war = WarSystem.GetWarWith(_currentCountryId);
        if (war != null)
        {
            _warStatusLabel.AddThemeColorOverride("font_color", GameTheme.Red);
            _warStatusLabel.Text = $"AT WAR — Progress: {war.WarProgress:F0}% | Enemy divisions: {war.EnemyDivisions}";
            _declareWarButton.Disabled = true;
            _declareWarButton.Text = "AT WAR";
            return;
        }

        // Can declare war?
        if (!GM.HasPlayer || GM.PlayerCountryData == null) return;

        // Casus belli checklist
        int warCost = WarSystem.CalculateWarGoalCost(_currentCountryId);
        _declareWarButton.Text = $"DECLARE WAR ({warCost} PP)";

        var check = CasusBelliSystem.CheckWarPreconditions(GM.PlayerCountryId, _currentCountryId);
        BuildJustificationUi(check);

        _declareWarButton.Disabled = !check.CanDeclare;
        if (!check.CanDeclare)
        {
            var missing = check.Items.Where(i => !i.Met).Select(i => $"✗ {i.Label}: {i.Detail}");
            _declareWarButton.TooltipText = "War declaration blocked:\n" + string.Join("\n", missing);
        }
        else
        {
            _declareWarButton.TooltipText = "Declare war on this country (justified)";
        }
    }

    private void ClearJustificationUi()
    {
        if (_checklistContainer == null || !GodotObject.IsInstanceValid(_checklistContainer)) return;
        if (_buildCaseContainer == null || !GodotObject.IsInstanceValid(_buildCaseContainer)) return;
        foreach (var child in _checklistContainer.GetChildren()) child.QueueFree();
        foreach (var child in _buildCaseContainer.GetChildren()) child.QueueFree();
        if (_warJustificationBox != null && GodotObject.IsInstanceValid(_warJustificationBox))
            _warJustificationBox.Visible = false;
    }

    private void BuildJustificationUi(CasusBelliSystem.WarCheckResult check)
    {
        _warJustificationBox.Visible = true;

        // Checklist: ✓ (green) / ✗ (red) using Chip component
        foreach (var item in check.Items)
        {
            var chip = Chip.CreateStatus($"{(item.Met ? "✓" : "✗")} {item.Label}",
                item.Met ? Chip.Variant.Success : Chip.Variant.Danger);
            chip.FontSize = GameTheme.FontSizeSmall;
            chip.Clickable = false;
            if (!item.Met && !string.IsNullOrEmpty(item.Detail))
            {
                chip.TooltipText = item.Detail;
            }
            _checklistContainer.AddChild(chip);
        }

        // In-progress justifications
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
            var l = NewLabel(status, GameTheme.FontSizeSmall, GameTheme.GoldBright);
            _checklistContainer.AddChild(l);
        }

        // BUILD CASE buttons
        var startable = CasusBelliSystem.GetStartableTypes(GM.PlayerCountryId, _currentCountryId);
        foreach (var type in startable)
        {
            var b = new Button
            {
                Text = $"BUILD CASE: {type.Name} ({type.BuildDays}d, {type.PPCost} PP)",
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = type.Description
            };
            b.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            b.Pressed += () =>
            {
                bool ok = CasusBelliSystem.StartBuildCase(GM.PlayerCountryId, _currentCountryId, type.Id);
                if (ok)
                {
                    UpdateWarStatus();
                    if (_currentData != null)
                        _ppLabel.Text = $"Political Power: {_currentData.PoliticalPower:N1}";
                }
            };
            _buildCaseContainer.AddChild(b);
            GameTheme.StyleButton(b);
        }
    }

    public void ShowCountry(CountryData data, string name)
    {
        GD.Print($"[CountryInfoPanel] ShowCountry: id={data?.Id ?? "null"}, name={name}");

        _currentData = data;
        _currentCountryId = data?.Id ?? "";
        _nameLabel.Text = $"{name} [{_currentCountryId}]";
        _capitalLabel.Text = $"Capital: {data.Capital}";
        _ideologyLabel.Text = $"Ideology: {data.Ideology}";

        // Enable mouse input when panel is shown
        MouseFilter = Control.MouseFilterEnum.Stop;

        _detailNoteLabel.Text = data.IsDetailed ? "" : "No detailed data — shown values are estimates";

        _stabilityLabel.Text = $"Stability: {data.StabilityPercent:N0}%";
        _warSupportLabel.Text = $"War Support: {data.WarSupportPercent:N0}%";
        _ppLabel.Text = $"Political Power: {data.PoliticalPower:N1}";

        _gdpLabel.Text = $"GDP: {data.EstimatedGDP:N0}";
        _populationLabel.Text = $"Population: {data.Population:N0}";
        _manpowerLabel.Text = $"Manpower Pool: {data.ManpowerPool:N0}";

        // Culture profile
        RefreshCultureLabel(data.Id);

        _factoriesLabel.Text = $"Factories: {data.CivFactories} Civ / {data.MilFactories} Mil / {data.Dockyards} Dock";

        _resourcesLabel.Text = $"Resources: Oil {data.Oil} | Rubber {data.Rubber} | Steel {data.Steel} | Al {data.Aluminum} | W {data.Tungsten} | Cr {data.Chromium}";

        _divisionsLabel.Text = $"Starting Divisions: {data.StartingDivisions}";

        _flagBox.SetCountry(data.Id);

        UpdatePlayButton();
        UpdateWarStatus();

        UpdateOverlayRect();

        _fadeTween?.Kill();
        _fadeTween = UITheme.FadeIn(this, 0.12f);
    }

    /// <summary>
    /// M46.3: Country culture profile from province culture layer.
    /// </summary>
    private void RefreshCultureLabel(string countryId)
    {
        var records = ProvinceCultureSystem.ForCountry(countryId);
        if (records.Count == 0)
        {
            _cultureLabel.Text = "Culture: no province records";
            _cultureLabel.AddThemeColorOverride("font_color", GameTheme.TextDisabled);
            return;
        }

        var dist = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rec in records)
        {
            if (rec == null) continue;
            string culture = string.IsNullOrEmpty(rec.Culture) ? "(none)" : rec.Culture;
            dist.TryGetValue(culture, out int n);
            dist[culture] = n + 1;
        }

        var ranked = dist.OrderByDescending(kv => kv.Value)
                         .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                         .ToList();
        string dominant = ranked[0].Key;
        int dominantCount = ranked[0].Value;
        int foreignCount = records.Count - dominantCount;

        _cultureLabel.Text = $"Culture: {dominant} ({dist.Count} total) " +
                            $"— {dominantCount}/{records.Count} provinces, " +
                            $"{foreignCount} foreign";
        _cultureLabel.AddThemeColorOverride("font_color",
            foreignCount > dominantCount ? GameTheme.Orange : GameTheme.TextPrimary);
    }

    public string CultureSummary => _cultureLabel?.Text ?? "";

    public void HidePanel()
    {
        _fadeTween?.Kill();
        _fadeTween = UITheme.FadeOut(this, 0.08f, () => {
            Hide();
            MouseFilter = Control.MouseFilterEnum.Ignore;
        });
    }
}