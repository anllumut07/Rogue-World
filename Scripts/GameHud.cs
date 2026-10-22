using Godot;
using System;
using System.Collections.Generic;

// Üst HUD: sol=ülke+bayrak, orta=tarih+hız göstergesi, sağ=PP/Stability/WarSupport.
// Alt çubuk: seçim bilgisi, oyun hızı metni.
// Tüm elemanlarda tooltip; tema UITheme üzerinden.
public partial class GameHud : CanvasLayer
{
    private Label _dateLabel;
    private Label _countryLabel;
    private Label _ppLabel;
    private Label _stabilityLabel;
    private Label _warSupportLabel;
    private Label _stateLabel;
    private Button _pauseButton;
    private Button[] _speedButtons;
    private Button _nextEventBtn; // M16.4: NEXT EVENT butonu

    // Alt çubuk
    private Label _selectedLabel;
    private Label _speedTextLabel;

    // Üst bar panel düğmeleri: sığdığı kadarını satırda gösterir, kalanını
    // "MORE ▾" açılır menüsüne taşır. (Kullanıcı şikayeti: sağ taraftaki uzun
    // düğme listesi 1152x648'de pencereden taşıyordu.)
    private HBoxContainer _sectionRow;
    private MenuButton _moreBtn;
    private PopupMenu _morePopup;
    private readonly List<(string Text, string Tooltip, Action Open)> _sections = new();
    private readonly List<Button> _sectionBtns = new();
    private readonly List<Action> _moreActions = new();

    private GameManager GM => GameManager.Instance;

    public override void _Ready()
    {
        BuildUi();

        GM.DateChanged += OnDateChanged;
        GM.SpeedChanged += OnSpeedChanged;
        GM.PlayerCountryChanged += OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged += OnPlayerCountryDataChanged;

        OnDateChanged(GM.GetDateText());
        OnSpeedChanged(GM.Speed);
        RefreshCountryLabel();
        RefreshDataLabels();
    }

    public override void _ExitTree()
    {
        if (!GameManager.IsAvailable) return;
        GM.DateChanged -= OnDateChanged;
        GM.SpeedChanged -= OnSpeedChanged;
        GM.PlayerCountryChanged -= OnPlayerCountryChanged;
        GM.PlayerCountryDataChanged -= OnPlayerCountryDataChanged;
    }

    public override void _Process(double delta)
    {
        if (!GameManager.IsAvailable || !GameManager.Instance.HasPlayer) return;

        // M16.3: Aylık rapor bekliyorsa panel otomatik aç (zaten açıksa tekrar açma)
        if (TimeTempoSystem.IsMonthlyReportPending && !MonthlyReportPanel.IsOpen)
        {
            GD.Print("[GameHud] Monthly report pending — opening MonthlyReportPanel");
            MonthlyReportPanel.OpenUnder(this);
        }

        // M16.4: NEXT EVENT butonu — bekleyen bildirim varsa göster
        if (_nextEventBtn != null)
        {
            bool hasNotifications = TimeTempoSystem.HasPendingNotifications;
            if (hasNotifications && !_nextEventBtn.Visible)
                _nextEventBtn.Show();
            else if (!hasNotifications && _nextEventBtn.Visible)
                _nextEventBtn.Hide();
        }
    }

    private void BuildUi()
    {
        Layer = 10;

        // === ÜST BAR ===
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;

        UITheme.ApplyTheme(margin);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        margin.AddChild(row);

        var panel = new PanelContainer();
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(panel);

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 16);
        panel.AddChild(bar);

        // --- SOL: Ülke bayrağı + isim ---
        var leftSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        leftSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftSection.AddThemeConstantOverride("separation", 8);
        bar.AddChild(leftSection);

        var flagBox = new FlagBox { CustomMinimumSize = new Vector2(28, 18) };
        leftSection.AddChild(flagBox);

        _countryLabel = new Label();
        _countryLabel.AddThemeFontSizeOverride("font_size", 20);
        _countryLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.66f, 0.22f));
        _countryLabel.TooltipText = "Player country";
        leftSection.AddChild(_countryLabel);

        // --- ORTA: Tarih + Hız göstergesi (butonlar) ---
        var centerSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        centerSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        centerSection.AddThemeConstantOverride("separation", 12);
        bar.AddChild(centerSection);

        _dateLabel = new Label();
        _dateLabel.AddThemeFontSizeOverride("font_size", 20);
        _dateLabel.TooltipText = "Game date";
        centerSection.AddChild(_dateLabel);

        _stateLabel = new Label();
        _stateLabel.AddThemeFontSizeOverride("font_size", 20);
        _stateLabel.AddThemeColorOverride("font_color", Colors.OrangeRed);
        centerSection.AddChild(_stateLabel);

        // Hız butonları (kompakt toolbar varyantı)
        _speedButtons = new Button[5];
        for (int i = 0; i < 5; i++)
        {
            int speed = i + 1;
            var b = new Button
            {
                Text = $"{speed}x",
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                ThemeTypeVariation = "ToolbarButton",
                TooltipText = $"Oyun hızı: {speed}x"
            };
            b.Pressed += () =>
            {
                GM.SetSpeed(speed);
                SyncSpeedButtons();
            };
            centerSection.AddChild(b);
            _speedButtons[i] = b;
            UITheme.AddHoverScale(b, 1.08f);
        }

        // --- SAĞ: PP | Stability | War Support | panel düğmeleri | MORE | NEXT EVENT | Pause ---
        var rightSection = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        rightSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightSection.AddThemeConstantOverride("separation", 12);
        bar.AddChild(rightSection);

        _ppLabel = new Label();
        _ppLabel.AddThemeFontSizeOverride("font_size", 18);
        _ppLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
        _ppLabel.TooltipText = "Political Power (daily +1.5)";
        rightSection.AddChild(_ppLabel);

        _stabilityLabel = new Label();
        _stabilityLabel.AddThemeFontSizeOverride("font_size", 18);
        _stabilityLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 1f));
        _stabilityLabel.TooltipText = "Stability % (low means resistance risk)";
        rightSection.AddChild(_stabilityLabel);

        _warSupportLabel = new Label();
        _warSupportLabel.AddThemeFontSizeOverride("font_size", 18);
        _warSupportLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.4f));
        _warSupportLabel.TooltipText = "War Support % (affects war performance)";
        rightSection.AddChild(_warSupportLabel);

        // Panel düğmeleri satırı: sığdığı kadarını gösterir, kalanı MORE menüsüne taşır.
        _sectionRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _sectionRow.AddThemeConstantOverride("separation", 10);
        rightSection.AddChild(_sectionRow);

        // RESEARCH
        AddSection("RESEARCH", "Technology tree (research slots, progress)",
            () => ResearchPanel.OpenUnder(this));

        // ECONOMY
        AddSection("ECONOMY", "Economy panel (build queue, factories, resources)",
            () => EconomyPanel.OpenUnder(this));

        // PRODUCTION
        AddSection("PRODUCTION", "Production lines, equipment, variant designer",
            () => ProductionPanel.OpenUnder(this));

        // ARMY
        AddSection("ARMY", "Army panel (divisions, templates, training, wars)",
            () => ArmyPanel.OpenUnder(this));

        // DIPLOMACY (M8)
        AddSection("DIPLOMACY", "Diplomacy panel (relations, factions, guarantees, peace)",
            () => DiplomacyPanel.OpenUnder(this));

        // FOCUS (M9)
        AddSection("FOCUS", "National focus tree (country-specific goals)",
            () => FocusPanel.OpenUnder(this));

        // DECISIONS (M10.2)
        AddSection("DECISIONS", "Decisions panel (available decisions, active progress, cooldowns)",
            () =>
            {
                GD.Print("[GameHud] DECISIONS butonu basıldı — DecisionPanel.OpenUnder çağrılıyor");
                DecisionPanel.OpenUnder(this);
            });

        // LIBRARY (M15.2)
        AddSection("LIBRARY", "Country encyclopedia and library",
            () => LibraryPanel.OpenUnder(this));

        // AGENDA (M16): haftalık ajanda — slotlar + aksiyonlar
        AddSection("AGENDA", "Weekly agenda — leader's actions (meetings, rallies, summits)",
            () =>
            {
                GD.Print("[GameHud] AGENDA butonu basıldı — AgendaPanel.OpenUnder çağrılıyor");
                AgendaPanel.OpenUnder(this);
            });

        // STATISTICS (M20.1): ülke istatistik & bütçe göstergeleri
        AddSection("STATISTICS", "Statistics & budget indicators (GDP, inflation, unemployment, corruption, ...)",
            () =>
            {
                GD.Print("[GameHud] STATISTICS butonu basıldı — StatisticsPanel.OpenUnder çağrılıyor");
                StatisticsPanel.OpenUnder(this);
            });

        // SEA ZONES (M43.5): deniz hakimiyeti, konvoy riski ve müttefik koruması
        AddSection("SEA ZONES", "Sea zones — supremacy, convoy raid risk and allied escort assignment",
            () =>
            {
                GD.Print("[GameHud] SEA ZONES butonu basıldı — SeaZonePanel.OpenUnder çağrılıyor");
                SeaZonePanel.OpenUnder(this);
            });

        // CABINET (M21.1): bakanlık atamaları ve bakanlık listesi
        AddSection("CABINET", "Cabinet — ministries and minister assignments",
            () =>
            {
                GD.Print("[GameHud] CABINET butonu basıldı — CabinetPanel.OpenUnder çağrılıyor");
                CabinetPanel.OpenUnder(this);
            });

        // ORGANIZATIONS (M37.5): uluslararası örgütler (NATO, AB, BM...) üyelik + gündem + oy
        AddSection("ORGANIZATIONS", "Organizations — memberships, agenda and voting (M37.5)",
            () =>
            {
                GD.Print("[GameHud] ORGANIZATIONS butonu basıldı — OrganizationPanel.OpenUnder çağrılıyor");
                OrganizationPanel.OpenUnder(this);
            });

        // ELECTION (M23.1): seçim takvimi + oy tahmini paneli
        AddSection("ELECTION", "Elections — calendar and vote prediction",
            () =>
            {
                GD.Print("[GameHud] ELECTION butonu basıldı — ElectionPanel.OpenUnder çağrılıyor");
                ElectionPanel.OpenUnder(this);
            });

        // "MORE ▾": satıra sığmayan panel düğmeleri bu açılır menüde toplanır.
        _moreBtn = new MenuButton
        {
            Text = "MORE ▾",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "More panels (did not fit in the bar)",
            Visible = false
        };
        _morePopup = _moreBtn.GetPopup();
        _morePopup.IdPressed += OnMoreItemPressed;
        rightSection.AddChild(_moreBtn);

        // NEXT EVENT butonu (M16.4): bekleyen bildirim varsa görünür
        _nextEventBtn = new Button
        {
            Text = "⚑ NEXT EVENT",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Process next pending notification (auto-pause was triggered)"
        };
        _nextEventBtn.Pressed += () =>
        {
            GD.Print("[GameHud] NEXT EVENT butonu basıldı");
            TimeTempoSystem.ProcessNextNotification();
            _nextEventBtn.Hide();
        };
        _nextEventBtn.Hide(); // kuyruk boşken gizli
        rightSection.AddChild(_nextEventBtn);
        UITheme.AddHoverScale(_nextEventBtn);

        // Pause/Play butonu (en sağda)
        _pauseButton = new Button { FocusMode = Control.FocusModeEnum.None, TooltipText = "Pause/Resume (Space)" };
        _pauseButton.Pressed += () => GM.TogglePause();
        rightSection.AddChild(_pauseButton);
        UITheme.AddHoverScale(_pauseButton, 1.08f);

        // Üst barı sahneye ekle (BuildBottomBar alt barı ekler).
        AddChild(margin);

        // Pencere boyutu değişince düğme dağılımını yeniden hesapla.
        GetTree().Root.SizeChanged += ReflowSections;
        ReflowSections();
        // İlk yerleşim tamamlandıktan sonra bir kez daha hesapla: ağaç dışındayken
        // ölçülen viewport genişliği 0 olur ve tüm düğmeler MORE menüsüne taşınırdı.
        Callable.From(ReflowSections).CallDeferred();

        BuildBottomBar();
    }

    // Üst bara bir panel düğmesi ekler. Düğme hemen oluşturulur ama görünürlüğü
    // ReflowSections() tarafından pencere genişliğine göre ayarlanır.
    private void AddSection(string text, string tooltip, Action open)
    {
        var btn = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip
        };
        btn.Pressed += open;
        UITheme.AddHoverScale(btn);
        _sectionRow.AddChild(btn);
        _sections.Add((text, tooltip, open));
        _sectionBtns.Add(btn);
    }

    // Pencere genişliğine göre kaç panel düğmesinin satırda gösterileceğini hesaplar;
    // kalanları "MORE ▾" açılır menüsüne taşır. Ölçüler tema bağımsız kabaca
    // yazı uzunluğu + dolgu tahminidir (butonların gerçek min boyutu bu tahmine
    // göre bir miktar büyük olabilir, bu yüzden güvenli pay bırakılır).
    private void ReflowSections()
    {
        if (_sectionRow == null || _moreBtn == null || _morePopup == null) return;

        float viewportW = _sectionRow.GetViewportRect().Size.X;
        // Sabit alanlar: kenar boşlukları, sol bölüm (bayrak + ülke adı),
        // orta bölüm (tarih + durum + 5 hız butonu), PP/Stability/WarSupport
        // etiketleri, NEXT EVENT ve Pause.
        const float fixedW = 40f + 210f + 270f + 250f + 100f + 46f;
        const float moreW = 92f;
        const float sepW = 10f;
        const float charW = 7.6f;
        const float btnPad = 30f;

        float budget = Mathf.Max(0f, viewportW - fixedW - moreW);
        _morePopup.Clear();
        _moreActions.Clear();

        float used = 0f;
        int shown = 0;
        for (int i = 0; i < _sectionBtns.Count; i++)
        {
            float w = _sections[i].Text.Length * charW + btnPad;
            if (used + w + sepW > budget) break;
            used += w + sepW;
            shown++;
        }

        for (int i = 0; i < _sectionBtns.Count; i++)
        {
            bool visible = i < shown;
            _sectionBtns[i].Visible = visible;
            if (!visible)
            {
                int id = _morePopup.ItemCount;
                _morePopup.AddItem(_sections[i].Text, id);
                _moreActions.Add(_sections[i].Open);
            }
        }
        _moreBtn.Visible = shown < _sectionBtns.Count;
    }

    // "MORE ▾" menüsünden bir panel seçildiğde ilgili paneli açar.
    private void OnMoreItemPressed(long id)
    {
        if (id < 0 || id >= _moreActions.Count) return;
        _moreActions[(int)id]();
        _morePopup.Hide();
    }

    private void BuildBottomBar()
    {
        // === ALT ÇUBUK ===
        var bottomMargin = new MarginContainer();
        bottomMargin.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        bottomMargin.AddThemeConstantOverride("margin_bottom", 8);
        bottomMargin.AddThemeConstantOverride("margin_left", 20);
        bottomMargin.AddThemeConstantOverride("margin_right", 20);
        bottomMargin.MouseFilter = Control.MouseFilterEnum.Ignore;
        UITheme.ApplyTheme(bottomMargin);

        var bottomBar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        bottomBar.AddThemeConstantOverride("separation", 24);
        bottomMargin.AddChild(bottomBar);

        var leftBottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        leftBottom.AddThemeConstantOverride("separation", 12);
        bottomBar.AddChild(leftBottom);

        _selectedLabel = new Label();
        _selectedLabel.AddThemeFontSizeOverride("font_size", 16);
        _selectedLabel.TooltipText = "Selected country";
        leftBottom.AddChild(_selectedLabel);

        var rightBottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        rightBottom.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightBottom.AddThemeConstantOverride("separation", 12);
        bottomBar.AddChild(rightBottom);

        _speedTextLabel = new Label();
        _speedTextLabel.AddThemeFontSizeOverride("font_size", 16);
        _speedTextLabel.AddThemeColorOverride("font_color", Colors.Gray);
        _speedTextLabel.TooltipText = "Current game speed";
        rightBottom.AddChild(_speedTextLabel);

        // HUD'u sahneye ekle (üst ve alt margin)
        AddChild(bottomMargin);
    }

    private void OnDateChanged(string text) => _dateLabel.Text = text;

    private void OnPlayerCountryChanged(string id) => RefreshCountryLabel();

    private void OnPlayerCountryDataChanged() => RefreshDataLabels();

    private void RefreshCountryLabel()
    {
        if (GM.HasPlayer && GM.PlayerCountryData != null)
        {
            var d = GM.PlayerCountryData;
            _countryLabel.Text = d.Name;
            // Bayrak: FlagLibrary (PNG / yedek)
            var flagBox = _countryLabel.GetParent().GetChild(0) as FlagBox;
            flagBox?.SetCountry(d.Id);
        }
        else
        {
            _countryLabel.Text = "Rogue World";
        }
    }

    private void RefreshDataLabels()
    {
        if (GM.HasPlayer && GM.PlayerCountryData != null)
        {
            var d = GM.PlayerCountryData;
            _ppLabel.Text = $"PP: {d.PoliticalPower:N1}";
            _stabilityLabel.Text = $"STA: {d.StabilityPercent:N0}%";
            _warSupportLabel.Text = $"WS: {d.WarSupportPercent:N0}%";
        }
        else
        {
            _ppLabel.Text = "";
            _stabilityLabel.Text = "";
            _warSupportLabel.Text = "";
        }
    }

    private void OnSpeedChanged(int speed)
    {
        bool paused = speed == 0;
        _pauseButton.Text = paused ? "PLAY" : "PAUSE";
        _stateLabel.Text = paused ? "PAUSED" : "";
        SyncSpeedButtons();
        _speedTextLabel.Text = paused ? "Speed: Pause" : $"Speed: {speed}x";
    }

    private void SyncSpeedButtons()
    {
        for (int i = 0; i < _speedButtons.Length; i++)
            _speedButtons[i].SetPressedNoSignal(i + 1 == GM.Speed);
    }

    // WorldMap'ten seçim sinyali için public hook
    public void OnCountrySelected(string name)
    {
        _selectedLabel.Text = $"Selected: {name}";
    }

    public void OnCountryDeselected()
    {
        _selectedLabel.Text = "Selected: —";
    }
}