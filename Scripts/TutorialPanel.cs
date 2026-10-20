using Godot;
using System;
using System.Collections.Generic;

// M36.2: İlk açılış rehberi (tutorial) paneli.
// Overlay (Control), backdrop MouseFilter.Stop harita girişini bloke eder, ESC kapatır.
// Panel boyutu 1152x648'e sığar (860x520 içerik). UI metinleri İngilizce, yorumlar Türkçe.
// "TutorialSeen" bayrağı SettingsSystem üzerinden user://settings.json'da kalıcıdır;
public partial class TutorialPanel : Control
{
    public static bool IsOpen { get; private set; } = false;

    private PanelContainer _root;
    private VBoxContainer _content;
    private Label _titleLabel;
    private RichTextLabel _tipsLabel;
    private CheckBox _dontShowCheck;
    private Button _closeButton;

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        var panel = new TutorialPanel();
        ModalHost.AddUnder(parent, panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // paused iken de kapanış/input çalışsın
        UITheme.ApplyTheme(this);
        BuildUi();
        IsOpen = true;
        UITheme.PopIn(this, 0.15f);

        GD.Print("[TutorialPanel] hazır — ilk açılış rehberi");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void BuildUi()
    {
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        MouseFilter = Control.MouseFilterEnum.Stop;

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(backdrop);

        _root = new PanelContainer
        {
            CustomMinimumSize = new Vector2(860, 520),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _root.SetAnchorsPreset(Control.LayoutPreset.Center);
        UITheme.ApplyTheme(_root);
        AddChild(_root);

        _content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 14);
        _content.AddThemeConstantOverride("margin_top", 20);
        _content.AddThemeConstantOverride("margin_bottom", 20);
        _content.AddThemeConstantOverride("margin_left", 28);
        _content.AddThemeConstantOverride("margin_right", 28);
        _root.AddChild(_content);

        // Başlık
        _titleLabel = new Label { Text = "WELCOME TO ROGUE WORLD", HorizontalAlignment = HorizontalAlignment.Center };
        _titleLabel.AddThemeFontSizeOverride("font_size", 26);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _content.AddChild(_titleLabel);

        var subtitle = new Label { Text = "Grand Strategy — Country Management", HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        subtitle.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _content.AddChild(subtitle);

        _content.AddChild(new HSeparator());

        // İpuçları metni (RichTextLabel: renkli, kalın, liste biçimi)
        _tipsLabel = new RichTextLabel();
        _tipsLabel.AddThemeFontSizeOverride("font_size", 15);
        _tipsLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        _tipsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _tipsLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _tipsLabel.Text = GetTutorialText();
        _content.AddChild(_tipsLabel);

        // "Gösterilmesin" onay kutusu
        var dontShowRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        dontShowRow.AddThemeConstantOverride("separation", 8);
        _dontShowCheck = new CheckBox { Text = "Don't show this again" };
        _dontShowCheck.AddThemeFontSizeOverride("font_size", 14);
        _dontShowCheck.ButtonPressed = SettingsSystem.TutorialSeen;
        dontShowRow.AddChild(_dontShowCheck);
        dontShowRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _content.AddChild(dontShowRow);

        _content.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // Kapat butonu
        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _closeButton = new Button { Text = "GOT IT", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(140, 44) };
        _closeButton.Pressed += OnClosePressed;
        UITheme.AddHoverScale(_closeButton);
        btnRow.AddChild(_closeButton);
        _content.AddChild(btnRow);
    }

    private static string GetTutorialText()
    {
        // İngilizce UI metni — proje kuralı: UI metinleri İngilizce, yorumlar Türkçe.
        return
            "[b]Welcome to Rogue World[/b] This short guide covers the basics.\n\n" +
            "• [b]Choose a country[/b]: Click a country on the map to lead it. Your country choice stays locked for the rest of the game.\n\n" +
            "• [b]You play a character[/b]: You are not a country — you play a character. Your traits (Diplomacy, Administration, Intelligence, etc.) affect the game.\n\n" +
            "• [b]Speed[/b]: Use the speed buttons in the top HUD to pause or run at 1x–5x.\n\n" +
            "• [b]Panels[/b]: HUD buttons (Economy, National Agenda, Diplomacy, etc.) open their panels. Panels block map input; press ESC to close.\n\n" +
            "• [b]Agenda[/b]: You have a limited number of action slots per week (meeting, rally, press conference, etc.). Wait until slots free up.\n\n" +
            "• [b]Save/Load[/b]: Save and load your game from the main menu or the ESC menu.\n\n" +
            "Enjoy!";
    }

    private void OnClosePressed()
    {
        // "Don't show again" işaretliyse bayrağı kalıcılaştır
        if (_dontShowCheck.ButtonPressed)
            SettingsSystem.SetTutorialSeen(true);

        Close();
    }

    private void Close()
    {
        GD.Print("TutorialPanel: kapatılıyor");
        UITheme.FadeOut(_root, 0.08f, () => QueueFree());
    }
}
