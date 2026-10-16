using Godot;

// Pause menüsü (Milestone 1.4/1.5): RESUME / LOAD GAME / SETTINGS / MAIN MENU / QUIT
// Animasyon: backdrop fade + panel slide+fade (UITheme helpers).
public partial class PauseMenu : CanvasLayer
{
    private const string MainMenuScenePath = "res://Scenes/main_menu.tscn";

    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;

        BuildUi();
        // Başlangıçta gizli; OpenMenu animasyonla gösterecek.
        _backdrop.Modulate = new Color(0, 0, 0, 0);
        _panel.Modulate = new Color(1, 1, 1, 0);
        _backdrop.Hide();
        _panel.Hide();
        IsOpen = false;
    }

    public override void _ExitTree()
    {
        IsOpen = false;
        if (GetTree() != null)
            GetTree().Paused = false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            // Herhangi bir overlay paneli açıksa ESC'i panele bırak.
            if (SettingsPanel.IsOpen
                || CountryInfoPanel.IsOpen
                || CountryMiniCard.IsOpen
                || LibraryPanel.IsOpen
                || DecisionPanel.IsOpen
                || FocusPanel.IsOpen
                || ResearchPanel.IsOpen
                || EconomyPanel.IsOpen
                || ProductionPanel.IsOpen
                || ArmyPanel.IsOpen
                || DiplomacyPanel.IsOpen
                || LoadGamePanel.IsOpen
                || EventPopup.IsOpen) return;

            GetViewport().SetInputAsHandled();
            ToggleMenu();
        }
    }

    private void ToggleMenu()
    {
        if (IsOpen) CloseMenu();
        else OpenMenu();
    }

    private void OpenMenu()
    {
        IsOpen = true;
        _backdrop.Show();
        _panel.Show();
        GetTree().Paused = true;

        // Açılış animasyonu: backdrop fade + panel pop-in
        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);
    }

    private void CloseMenu()
    {
        GetTree().Paused = false;
        // Kapanış animasyonu; bitince gizle
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () =>
        {
            _backdrop.Hide();
            _panel.Hide();
        });
        IsOpen = false;
    }

    private void OnResumePressed()
    {
        CloseMenu();
    }

    private void OnSettingsPressed()
    {
        GD.Print("PauseMenu: Ayarlar açılıyor");
        // Panel PauseMenu'nün çocuğu olarak eklenir; ProcessMode.Always miras aldığı
        // için ağaç paused olsa bile panel canlı çalışır.
        SettingsPanel.OpenUnder(this);
    }

    // M15.3: Kayıt/Yükleme paneli — SAVE + LOAD + DELETE (aynı bileşen ana menüde de).
    // Yükleme başarılı olursa afterLoad ile pause menü de kapanır (oyun Speed=0 pause'lu başlar).
    private void OnLoadGamePressed()
    {
        GD.Print("PauseMenu: Kayıt/Yükleme paneli açılıyor");
        LoadGamePanel.OpenUnder(this, fromMainMenu: false, afterLoad: CloseMenu);
    }

    private void OnMainMenuPressed()
    {
        IsOpen = false;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }

    private void BuildUi()
    {
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.7f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_backdrop);

        // Ortak koyu tema (backdrop çocukları: panel + butonlar)
        UITheme.ApplyTheme(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer();
        _panel.CustomMinimumSize = new Vector2(400, 300);
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 40);
        margin.AddThemeConstantOverride("margin_right", 40);
        margin.AddThemeConstantOverride("margin_top", 30);
        margin.AddThemeConstantOverride("margin_bottom", 30);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 20);
        margin.AddChild(vbox);

        var title = new Label();
        title.Text = "PAUSED";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 40);
        vbox.AddChild(title);

        var resume = new Button { Text = "RESUME", FocusMode = Control.FocusModeEnum.None };
        resume.Pressed += OnResumePressed;
        vbox.AddChild(resume);
        UITheme.AddHoverScale(resume);

        var loadGame = new Button { Text = "SAVE / LOAD GAME", FocusMode = Control.FocusModeEnum.None };
        loadGame.Pressed += OnLoadGamePressed;
        vbox.AddChild(loadGame);
        UITheme.AddHoverScale(loadGame);

        var settings = new Button { Text = "SETTINGS", FocusMode = Control.FocusModeEnum.None };
        settings.Pressed += OnSettingsPressed;
        vbox.AddChild(settings);
        UITheme.AddHoverScale(settings);

        var mainMenu = new Button { Text = "MAIN MENU", FocusMode = Control.FocusModeEnum.None };
        mainMenu.Pressed += OnMainMenuPressed;
        vbox.AddChild(mainMenu);
        UITheme.AddHoverScale(mainMenu);

        var quit = new Button { Text = "QUIT", FocusMode = Control.FocusModeEnum.None };
        quit.Pressed += OnQuitPressed;
        vbox.AddChild(quit);
        UITheme.AddHoverScale(quit);
    }
}