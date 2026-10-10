using Godot;

// Pause menüsü (Milestone 1.4/1.5): RESUME / SETTINGS / MAIN MENU / QUIT
public partial class PauseMenu : CanvasLayer
{
    private const string MainMenuScenePath = "res://Scenes/main_menu.tscn";

    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private Control _panel;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;

        BuildUi();
        _backdrop.Hide();
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
            // Ayarlar paneli açıksa ESC'i panele bırak (panel kendini kapatır).
            if (SettingsPanel.IsOpen) return;

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
        GetTree().Paused = true;
    }

    private void CloseMenu()
    {
        GetTree().Paused = false;
        _backdrop.Hide();
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

        var settings = new Button { Text = "SETTINGS", FocusMode = Control.FocusModeEnum.None };
        settings.Pressed += OnSettingsPressed;
        vbox.AddChild(settings);

        var mainMenu = new Button { Text = "MAIN MENU", FocusMode = Control.FocusModeEnum.None };
        mainMenu.Pressed += OnMainMenuPressed;
        vbox.AddChild(mainMenu);

        var quit = new Button { Text = "QUIT", FocusMode = Control.FocusModeEnum.None };
        quit.Pressed += OnQuitPressed;
        vbox.AddChild(quit);
    }
}