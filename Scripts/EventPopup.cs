using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Olay popup penceresi (M10.1): ekran ortasında açılır, olay metni ve
/// A/B/C seçenek butonları gösterir. Açıkken oyun otomatik duraklar
/// (auto-pause), backdrop harita girişini engeller. ESC ile kapatılamaz
/// (seçim zorunlu — HOI4 kuralı). CanvasLayer (Layer=20) → HUD'un üstünde.
/// </summary>
public partial class EventPopup : CanvasLayer
{
    public static bool IsOpen { get; private set; }

    // Açık popup instance'ı — SaveSystem load sırasında zorla kapatmak için
    private static EventPopup _current;

    /// <summary>
    /// M12: Açık popup'ı callback ÇAĞIRMADAN kapatır (kaynak yükleme öncesi).
    /// Seçim yapılmadan kapatıldığı için olay seçenekleri uygulanmaz; EventSystem
    /// state'i zaten yüklenecek olan kayıtla değiştirilir.
    /// </summary>
    public static void ForceCloseForLoad()
    {
        if (_current == null || !GodotObject.IsInstanceValid(_current)) { _current = null; return; }
        GD.Print("[EventPopup] Kayıt yükleme — popup callback olmadan kapatılıyor");
        _current._onOptionChosen = null; // efekt uygulanmasın
        _current.QueueFree();
        _current = null;
        IsOpen = false;
    }

    // Callback: (eventId, optionIndex) → EventSystem.OnEventOptionChosen
    private Action<string, int> _onOptionChosen;

    // Gösterilecek olay (AddChild'tan önce set edilir, _Ready'de UI'ya yazılır)
    private EventSystem.GameEvent _event;

    // UI
    private Control _root;
    private Label _eventNameLabel;
    private FlagBox _eventFlagBox;
    private Label _eventDescLabel;
    private VBoxContainer _optionsContainer;
    private ColorRect _backdrop;
    private PanelContainer _panel;

    // Animasyon
    private const float FADE_DURATION = 0.12f;
    private const float POP_DURATION = 0.18f;

    private const string ScenePath = "res://Scenes/event_popup.tscn";

    public static void OpenAndShow(EventSystem.GameEvent evt, Action<string, int> onOptionChosen)
    {
        if (IsOpen) return; // zaten açık — EventSystem kuyruğu bekletir

        EventPopup popup = null;

        // 1) Sahne dosyasından yükle — hata olursa GD.PrintErr + koddan fallback
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    popup = scene.Instantiate<EventPopup>();
                    GD.Print($"[EventPopup] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[EventPopup] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                GD.PrintErr($"[EventPopup] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[EventPopup] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: koddan yeni popup oluştur (UI zaten BuildUi'de kuruluyor)
        popup ??= new EventPopup();

        // AddChild'tan ÖNCE set edilir — _Ready'de UI'ya yazılır
        popup._event = evt;
        popup._onOptionChosen = onOptionChosen;

        // Root sahneye ekle (GameManager autoload her zaman hayatta)
        if (GameManager.IsAvailable)
            GameManager.Instance.GetTree().Root.CallDeferred(Node.MethodName.AddChild, popup);
        else
            GD.PrintErr("[EventPopup] GameManager yok — popup açılamadı");
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // pause edilse bile çalışsın
        Layer = 20; // HUD (CanvasLayer 10) üzeri

        BuildUi();
        IsOpen = true;
        _current = this; // M12: instance takibi

        UITheme.FadeIn(_backdrop, FADE_DURATION);
        UITheme.PopIn(_panel, POP_DURATION);

        if (_event != null) SetEvent(_event);

        GD.Print($"EventPopup: açıldı — {_event?.Name ?? "?"}");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
        if (_current == this) _current = null; // M12
        GD.Print("EventPopup: kapatıldı");
    }

    // ESC yoksayılır — seçenek seçilene kadar kapatılamaz
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
            GetViewport().SetInputAsHandled();
    }

    private void BuildUi()
    {
        // Kök Control: viewport kadar (CanvasLayer çocuğu olarak FullRect)
        _root = new Control();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore; // çocuklar tıklasın
        AddChild(_root);

        // Backdrop: Stop — alttaki harita/HUD girişini tamamen engeller
        _backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.85f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        // Panel boyutu: 1152x648'de rahat sığar
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(780, 460) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 16);
        margin.AddChild(vbox);

        // --- Header: bayrak + olay adı ---
        var headerRow = new HBoxContainer();
        headerRow.Alignment = BoxContainer.AlignmentMode.Center;
        headerRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(headerRow);

        _eventFlagBox = new FlagBox { CustomMinimumSize = new Vector2(48, 32) };
        headerRow.AddChild(_eventFlagBox);

        _eventNameLabel = new Label();
        _eventNameLabel.AddThemeFontSizeOverride("font_size", 28);
        _eventNameLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _eventNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _eventNameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        headerRow.AddChild(_eventNameLabel);

        vbox.AddChild(new HSeparator());

        // --- Açıklama (kaydırılabilir) ---
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        _eventDescLabel = new Label();
        _eventDescLabel.AddThemeFontSizeOverride("font_size", 16);
        _eventDescLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _eventDescLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _eventDescLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_eventDescLabel);

        vbox.AddChild(new HSeparator());

        // --- Seçenekler (A/B/C butonları) ---
        _optionsContainer = new VBoxContainer();
        _optionsContainer.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(_optionsContainer);
    }

    public void SetEvent(EventSystem.GameEvent evt)
    {
        _event = evt;
        _eventNameLabel.Text = evt.Name;
        _eventDescLabel.Text = evt.Description;

        // Bayrak: olayın ülkesi (Trigger.Country) veya oyuncu ülkesi
        string flagCountry = !string.IsNullOrEmpty(evt.Trigger.Country)
            ? evt.Trigger.Country
            : GameManager.Instance?.PlayerCountryId ?? "";
        _eventFlagBox.SetCountry(flagCountry);

        // Eski butonları temizle
        foreach (var child in _optionsContainer.GetChildren())
        {
            _optionsContainer.RemoveChild(child);
            child.QueueFree();
        }

        // Seçenek butonları: A), B), C)...
        for (int i = 0; i < evt.Options.Count; i++)
        {
            var opt = evt.Options[i];
            char letter = (char)('A' + i);

            var btn = new Button
            {
                Text = $"{letter}) {opt.Label}",
                CustomMinimumSize = new Vector2(0, 52),
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = DescribeEffects(opt.Effects)
            };
            btn.AddThemeFontSizeOverride("font_size", 16);

            int idx = i; // closure capture
            btn.Pressed += () => OnOptionSelected(evt.Id, idx);
            UITheme.AddHoverScale(btn, 1.05f);

            _optionsContainer.AddChild(btn);
        }
    }

    private void OnOptionSelected(string eventId, int optionIndex)
    {
        GD.Print($"EventPopup: seçenek {optionIndex} seçildi ({eventId})");

        // Animasyonlu kapanış → efektleri uygula → serbest bırak
        UITheme.FadeOut(_backdrop, FADE_DURATION);
        UITheme.PopOut(_panel, POP_DURATION, () =>
        {
            _onOptionChosen?.Invoke(eventId, optionIndex);
            QueueFree();
        });
    }

    // Etki özeti (tooltip için, İngilizce UI)
    private static string DescribeEffects(EventSystem.EventEffects e)
    {
        var parts = new List<string>();

        if (e.PoliticalPower != 0) parts.Add($"{e.PoliticalPower:+#;-#} PP");
        if (e.Stability != 0) parts.Add($"{e.Stability:+#;-#}% Stability");
        if (e.WarSupport != 0) parts.Add($"{e.WarSupport:+#;-#}% War Support");
        if (e.CivFactories != 0) parts.Add($"{e.CivFactories:+#;-#} Civilian Factories");
        if (e.MilFactories != 0) parts.Add($"{e.MilFactories:+#;-#} Military Factories");
        if (e.Dockyards != 0) parts.Add($"{e.Dockyards:+#;-#} Dockyards");
        if (e.ResearchSlots != 0) parts.Add($"{e.ResearchSlots:+#;-#} Research Slot");
        if (e.Manpower != 0) parts.Add($"{FormatSigned(e.Manpower)} Manpower");
        if (e.Oil != 0) parts.Add($"{e.Oil:+#;-#} Oil");
        if (e.Rubber != 0) parts.Add($"{e.Rubber:+#;-#} Rubber");
        if (e.Steel != 0) parts.Add($"{e.Steel:+#;-#} Steel");
        if (e.Aluminum != 0) parts.Add($"{e.Aluminum:+#;-#} Aluminum");
        if (e.Tungsten != 0) parts.Add($"{e.Tungsten:+#;-#} Tungsten");
        if (e.Chromium != 0) parts.Add($"{e.Chromium:+#;-#} Chromium");
        if (e.WorldTension != 0) parts.Add($"{e.WorldTension:+#;-#}% World Tension");

        // Özel efektler
        if (!string.IsNullOrEmpty(e.TriggerEvent)) parts.Add($"Triggers: {e.TriggerEvent}");
        if (!string.IsNullOrEmpty(e.DeclareWar)) parts.Add($"Declares war on {e.DeclareWar}");
        if (e.DeclareWarAllies) parts.Add("...and their allies");
        if (!string.IsNullOrEmpty(e.AnnexCountry)) parts.Add($"Annexes {e.AnnexCountry}");
        if (e.WarGoalCostReduction != null)
            parts.Add($"War goal vs {e.WarGoalCostReduction.Target}: -{e.WarGoalCostReduction.ReductionPercent}% PP cost");

        return parts.Count > 0 ? string.Join("\n", parts) : "No effects";
    }

    private static string FormatSigned(long value)
    {
        string sign = value >= 0 ? "+" : "-";
        long abs = Math.Abs(value);
        if (abs >= 1000) return $"{sign}{abs / 1000}k";
        return $"{sign}{abs}";
    }
}