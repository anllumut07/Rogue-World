using Godot;
using System;

// Ayarlar paneli (Milestone 1.5): master ses, tam ekran, çözünürlük.
// Overlay olarak açılır: hem MainMenu'den hem PauseMenu'den aynı panel kullanılır,
// sahne değişimi yapılmaz (oyun durumu korunur).
// Değerler SettingsSystem üzerinden uygulanır ve user://settings.json'a kaydedilir;
// açılışta SettingsSystem.Load() ile yüklenen değerler panele yansıtılır.
// Animasyon: backdrop fade, panel slide+fade (UITheme helpers).
public partial class SettingsPanel : Control
{
    private HSlider _volumeSlider;
    private Label _volumeValueLabel;
    private CheckBox _fullscreenCheck;
    private OptionButton _resolutionOption;

    // M13.4: yeni ayar kontrolleri
    private CheckBox _vsyncCheck;
    private OptionButton _defaultSpeedOption;
    private OptionButton _difficultyOption;
    private CheckBox _ironmanCheck;

    // Animasyon için referanslar
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    // Paneli verilen parent'ın altına overlay olarak açar (zaten açıksa dokunmaz).
    private const string ScenePath = "res://Scenes/settings_panel.tscn";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[SettingsPanel] OpenUnder çağrıldı — parent={parent?.GetType().Name ?? "null"}");

        SettingsPanel panel = null;

        // 1) Sahne dosyası projede var mı?
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    panel = scene.Instantiate<SettingsPanel>();
                    GD.Print($"[SettingsPanel] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[SettingsPanel] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                GD.PrintErr($"[SettingsPanel] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[SettingsPanel] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: koddan oluştur
        panel ??= new SettingsPanel();

        parent.AddChild(panel);
        GD.Print($"[SettingsPanel] Sahneye eklendi: Visible={panel.Visible}, Size={panel.Size}, Pos={panel.Position}, parent={panel.GetParent()?.GetType().Name ?? "null"}");
    }

    public override void _Ready()
    {
        // Pause menüden açıldığında ağaç paused olur -> panel her zaman canlı çalışsın.
        ProcessMode = ProcessModeEnum.Always;

        // Ortak koyu tema
        UITheme.ApplyTheme(this);

        BuildUi();

        // Önce mevcut ayarları kontrollere yansıt, SONRA sinyalleri bağla
        // (yoksa açılışta yanlışlıkla kaydet tetiklenir).
        SyncFromSettings();
        ConnectSignals();

        IsOpen = true;

        // Açılış animasyonu: backdrop fade + panel pop-in (fade+scale)
        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        GD.Print("SettingsPanel: hazır");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // ESC: paneli kapat. SetInputAsHandled -> PauseMenu'nün ESC'sini de engeller.
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void BuildUi()
    {
        // Arkaplan: arkadaki menü butonlarına tıklamayı engeller
        _backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.75f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(520, 320) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 40);
        margin.AddThemeConstantOverride("margin_right", 40);
        margin.AddThemeConstantOverride("margin_top", 30);
        margin.AddThemeConstantOverride("margin_bottom", 30);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 18);
        margin.AddChild(vbox);

        // Başlık
        var title = new Label
        {
            Text = "SETTINGS",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        // --- Master ses satırı: etiket + slider + yüzde ---
        var volumeRow = new HBoxContainer();
        volumeRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(volumeRow);

        var volumeLabel = new Label { Text = "Master Volume", CustomMinimumSize = new Vector2(150, 0) };
        volumeRow.AddChild(volumeLabel);

        _volumeSlider = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        volumeRow.AddChild(_volumeSlider);

        _volumeValueLabel = new Label { CustomMinimumSize = new Vector2(56, 0) };
        _volumeValueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        volumeRow.AddChild(_volumeValueLabel);

        // --- Tam ekran onay kutusu ---
        _fullscreenCheck = new CheckBox { Text = "Fullscreen" };
        vbox.AddChild(_fullscreenCheck);

        // --- Çözünürlük satırı: etiket + seçim listesi ---
        var resolutionRow = new HBoxContainer();
        resolutionRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(resolutionRow);

        var resolutionLabel = new Label { Text = "Resolution", CustomMinimumSize = new Vector2(150, 0) };
        resolutionRow.AddChild(resolutionLabel);

        _resolutionOption = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        for (int i = 0; i < SettingsSystem.Resolutions.Length; i++)
        {
            var r = SettingsSystem.Resolutions[i];
            _resolutionOption.AddItem($"{r.X} x {r.Y}");
        }
        resolutionRow.AddChild(_resolutionOption);

        // --- VSync onay kutusu ---
        _vsyncCheck = new CheckBox { Text = "VSync" };
        vbox.AddChild(_vsyncCheck);

        // --- Varsayılan oyun hızı satırı: etiket + seçim listesi (0=Pause .. 5x) ---
        var speedRow = new HBoxContainer();
        speedRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(speedRow);

        var speedLabel = new Label { Text = "Default Speed", CustomMinimumSize = new Vector2(150, 0) };
        speedRow.AddChild(speedLabel);

        _defaultSpeedOption = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _defaultSpeedOption.AddItem("0 = Pause");
        _defaultSpeedOption.AddItem("1x");
        _defaultSpeedOption.AddItem("2x");
        _defaultSpeedOption.AddItem("3x");
        _defaultSpeedOption.AddItem("4x");
        _defaultSpeedOption.AddItem("5x");
        speedRow.AddChild(_defaultSpeedOption);

        // --- Zorluk seviyesi satırı: etiket + seçim listesi (Easy/Normal/Hard) ---
        var difficultyRow = new HBoxContainer();
        difficultyRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(difficultyRow);

        var difficultyLabel = new Label { Text = "Difficulty", CustomMinimumSize = new Vector2(150, 0) };
        difficultyRow.AddChild(difficultyLabel);

        _difficultyOption = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        foreach (var d in Enum.GetValues<SettingsSystem.GameDifficulty>())
            _difficultyOption.AddItem(d.ToString());
        difficultyRow.AddChild(_difficultyOption);

        // --- Ironman onay kutusu ---
        _ironmanCheck = new CheckBox { Text = "Ironman" };
        vbox.AddChild(_ironmanCheck);

        // --- GERİ butonu (sağa hizalı) ---
        var backRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        vbox.AddChild(backRow);

        var backButton = new Button
        {
            Text = "BACK",
            CustomMinimumSize = new Vector2(160, 44),
            FocusMode = Control.FocusModeEnum.None
        };
        backButton.Pressed += Close;
        backRow.AddChild(backButton);

        // Hover scale efekti (GERİ butonu)
        UITheme.AddHoverScale(backButton);
    }

    // Açılıştaki (SettingsSystem'den gelen) değerleri kontrollere yansıt.
    private void SyncFromSettings()
    {
        int pct = Mathf.RoundToInt(SettingsSystem.MasterVolume * 100f);
        _volumeSlider.Value = pct;
        _volumeValueLabel.Text = $"{pct}%";

        _fullscreenCheck.ButtonPressed = SettingsSystem.Fullscreen;

        _resolutionOption.Select(SettingsSystem.ResolutionIndex);
        // Çözünürlük yalnızca pencere modunda uygulanır -> tam ekranda seçimi kapat.
        _resolutionOption.Disabled = SettingsSystem.Fullscreen;

        // M13.4: yeni ayarları kontrollere yansıt.
        _vsyncCheck.ButtonPressed = SettingsSystem.VSync;
        _defaultSpeedOption.Select(SettingsSystem.DefaultSpeed); // 0=pause .. 5 -> item index 0..5
        _difficultyOption.Select((int)SettingsSystem.Difficulty); // Easy=0, Normal=1, Hard=2
        _ironmanCheck.ButtonPressed = SettingsSystem.Ironman;
    }

    private void ConnectSignals()
    {
        _volumeSlider.ValueChanged += OnVolumeChanged;
        _fullscreenCheck.Toggled += OnFullscreenToggled;
        _resolutionOption.ItemSelected += OnResolutionSelected;

        // M13.4: yeni ayar kontrolleri sinyalleri
        _vsyncCheck.Toggled += OnVSyncToggled;
        _defaultSpeedOption.ItemSelected += OnDefaultSpeedSelected;
        _difficultyOption.ItemSelected += OnDifficultySelected;
        _ironmanCheck.Toggled += OnIronmanToggled;
    }

    private void OnVolumeChanged(double value)
    {
        int pct = (int)value;
        _volumeValueLabel.Text = $"{pct}%";
        SettingsSystem.SetMasterVolume(pct / 100f); // uygular + kaydeder
        GD.Print($"SettingsPanel: Master Volume = {pct}%");
    }

    private void OnFullscreenToggled(bool pressed)
    {
        _resolutionOption.Disabled = pressed;
        SettingsSystem.SetFullscreen(pressed); // uygular + kaydeder
        GD.Print($"SettingsPanel: Fullscreen = {pressed}");
    }

    private void OnResolutionSelected(long index)
    {
        int idx = (int)index;
        SettingsSystem.SetResolutionIndex(idx); // uygular + kaydeder
        var r = SettingsSystem.Resolutions[idx];
        GD.Print($"SettingsPanel: Resolution = {r.X} x {r.Y}");
    }

    // M13.4: yeni ayar olay işleyicileri — her biri SettingsSystem üzerinden uygular ve kaydeder.
    private void OnVSyncToggled(bool pressed)
    {
        SettingsSystem.SetVSync(pressed);
        GD.Print($"SettingsPanel: VSync = {pressed}");
    }

    private void OnDefaultSpeedSelected(long index)
    {
        SettingsSystem.SetDefaultSpeed((int)index);
        GD.Print($"SettingsPanel: Default Speed = {index}x");
    }

    private void OnDifficultySelected(long index)
    {
        SettingsSystem.SetDifficulty((SettingsSystem.GameDifficulty)index);
        GD.Print($"SettingsPanel: Difficulty = {index}");
    }

    private void OnIronmanToggled(bool pressed)
    {
        SettingsSystem.SetIronman(pressed);
        GD.Print($"SettingsPanel: Ironman = {pressed}");
    }

    private void Close()
    {
        GD.Print("SettingsPanel: kapatılıyor (animasyonla)");
        // Kapanış animasyonu: backdrop fade out + panel pop-out, sonra QueueFree
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}