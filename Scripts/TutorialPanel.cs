using Godot;
using System;

/// <summary>
/// Tutorial / First-Run Guide Panel (M36.2 + Stage 2 UI Upgrade).
/// - Overlay modal panel using GamePanel base (backdrop blocks map input, ESC/CLOSE closes).
/// - Panel fits within 1152x648 (content ~860x520).
/// - "TutorialSeen" flag persisted in user://settings.json via SettingsSystem.
/// - UI text English, comments Turkish.
/// </summary>
public partial class TutorialPanel : GamePanel
{
    public static bool IsOpen { get; private set; } = false;

    private VBoxContainer _content;
    private RichTextLabel _tipsLabel;
    private CheckBox _dontShowCheck;

    // ===== Test / UI inspection accessors =====

    /// <summary>Panel tasarım boyutu — 1152x648 ekrana sığma kontrolü için.</summary>
    public Vector2 DesignedSize => new Vector2(860, 520);

    /// <summary>Başlık metni.</summary>
    public string TitleText => Title;

    /// <summary>İpucu metni (ham BBCode).</summary>
    public string TipsText => _tipsLabel?.Text ?? "";

    /// <summary>"Don't show again" checkbox durumu.</summary>
    public bool DontShowChecked => _dontShowCheck?.ButtonPressed ?? false;

    /// <summary>ESC ile kapatma istendi mi?</summary>
    public new bool CloseRequested { get; private set; }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "TUTORIAL";
        MinSize = new Vector2(860, 520);
        MaxSize = new Vector2(1000, 700);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always; // paused iken de kapanış/input çalışsın
        BuildUi();
        IsOpen = true;

        GD.Print("[TutorialPanel] hazır — ilk açılış rehberi (Stage 2 UI)");
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
            CloseRequested = true;
            Close();
        }
    }

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        var panel = new TutorialPanel();
        parent.AddChild(panel);
    }

    private void BuildUi()
    {
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // Title is handled by GamePanel (Title property)

        // Tutorial tips text (RichTextLabel: colored, bold, list format)
        _tipsLabel = new RichTextLabel();
        _tipsLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        _tipsLabel.AddThemeColorOverride("font_color", GameTheme.TextPrimary);
        _tipsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _tipsLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _tipsLabel.Text = GetTutorialText();
        content.AddChild(_tipsLabel);

        content.AddChild(new HSeparator());

        // "Don't show again" checkbox row
        var dontShowRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        dontShowRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _dontShowCheck = new CheckBox { Text = "Don't show this again" };
        _dontShowCheck.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _dontShowCheck.ButtonPressed = SettingsSystem.TutorialSeen;
        dontShowRow.AddChild(_dontShowCheck);
        dontShowRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        content.AddChild(dontShowRow);

        content.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
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

    public override void Close()
    {
        // "Don't show again" işaretliyse bayrağı kalıcılaştır
        if (_dontShowCheck?.ButtonPressed == true)
            SettingsSystem.SetTutorialSeen(true);

        GD.Print("TutorialPanel: kapatılıyor");
        base.Close();
    }
}