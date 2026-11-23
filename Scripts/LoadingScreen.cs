using Godot;
using System;

/// <summary>
/// Loading Screen - Shows progress while heavy map systems initialize.
/// CanvasLayer at high layer to render above everything during loading.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    public static LoadingScreen Instance { get; private set; }

    private Label _titleLabel;
    private Label _statusLabel;
    private ProgressBar _progressBar;
    private ColorRect _backdrop;
    private Timer _pulseTimer;
    private Label _stepLabel;

    private int _totalSteps = 0;
    private int _currentStep = 0;
    private string _currentStatus = "";
    private bool _pulseDirection = true;

    public override void _Ready()
    {
        Instance = this;
        Layer = 200; // Above all UI layers
        Visible = false;

        BuildUI();
    }

    private void BuildUI()
    {
        // Full-screen backdrop
        _backdrop = new ColorRect
        {
            Color = new Color(0.05f, 0.08f, 0.12f, 1f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        // Center container
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(500, 300),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.1f, 0.14f, 0.95f),
            BorderColor = UITheme.Gold,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 30,
            ContentMarginRight = 30,
            ContentMarginTop = 30,
            ContentMarginBottom = 30,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 20);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.AddChild(vbox);

        // Title
        _titleLabel = new Label
        {
            Text = "ROGUE WORLD",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 36);
        _titleLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        vbox.AddChild(_titleLabel);

        // Subtitle
        var subtitle = new Label
        {
            Text = "Loading World Map Systems...",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        subtitle.AddThemeFontSizeOverride("font_size", 16);
        subtitle.AddThemeColorOverride("font_color", UITheme.TextMuted);
        vbox.AddChild(subtitle);

        // Status label
        _statusLabel = new Label
        {
            Text = "Initializing...",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        _statusLabel.AddThemeColorOverride("font_color", UITheme.TextMain);
        vbox.AddChild(_statusLabel);

        // Progress bar
        _progressBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 24)
        };
        var barStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.15f, 0.2f, 1f),
            BorderColor = new Color(0.2f, 0.25f, 0.35f, 1f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4
        };
        var fillStyle = new StyleBoxFlat
        {
            BgColor = UITheme.Gold,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4
        };
        _progressBar.AddThemeStyleboxOverride("background", barStyle);
        _progressBar.AddThemeStyleboxOverride("fill", fillStyle);
        vbox.AddChild(_progressBar);

        // Step counter
        _stepLabel = new Label
        {
            Text = "Step 0 / 0",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _stepLabel.AddThemeFontSizeOverride("font_size", 12);
        _stepLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        vbox.AddChild(_stepLabel);

        // Use a simple timer for pulse effect instead of AnimationPlayer
        _pulseTimer = new Timer
        {
            WaitTime = 1.0f,
            OneShot = false,
            Autostart = false
        };
        AddChild(_pulseTimer);
        _pulseTimer.Timeout += OnPulseTimer;
    }

    private void OnPulseTimer()
    {
        if (!Visible) return;

        var tween = CreateTween();
        if (_pulseDirection)
        {
            tween.TweenProperty(_statusLabel, "modulate:a", 0.5f, 1.0f);
            _pulseDirection = false;
        }
        else
        {
            tween.TweenProperty(_statusLabel, "modulate:a", 1.0f, 1.0f);
            _pulseDirection = true;
        }
    }

    /// <summary>
    /// Show the loading screen with total number of steps
    /// </summary>
    public void Show(int totalSteps)
    {
        _totalSteps = totalSteps;
        _currentStep = 0;
        _progressBar.Value = 0;
        _statusLabel.Text = "Initializing...";
        _stepLabel.Text = $"Step 0 / {totalSteps}";
        Visible = true;
        _pulseTimer.Start();
        GD.Print($"[LoadingScreen] Showing - {totalSteps} steps");
    }

    /// <summary>
    /// Update progress with a status message
    /// </summary>
    public void UpdateProgress(string status, int step = -1)
    {
        if (step >= 0)
            _currentStep = step;
        else
            _currentStep++;

        _currentStatus = status;
        _statusLabel.Text = status;
        _stepLabel.Text = $"Step {_currentStep} / {_totalSteps}";

        float progress = _totalSteps > 0 ? (_currentStep / (float)_totalSteps) * 100f : 0f;
        _progressBar.Value = progress;

        GD.Print($"[LoadingScreen] [{_currentStep}/{_totalSteps}] {status} ({progress:F1}%)");
    }

    /// <summary>
    /// Hide the loading screen with fade animation
    /// </summary>
    public async void HideScreen()
    {
        _pulseTimer.Stop();

        // Quick fade out
        var tween = CreateTween();
        tween.TweenProperty(_backdrop, "modulate:a", 0f, 0.15f);
        tween.TweenProperty(_statusLabel, "modulate:a", 0f, 0.15f);
        tween.TweenProperty(_progressBar, "modulate:a", 0f, 0.15f);
        tween.TweenProperty(_titleLabel, "modulate:a", 0f, 0.15f);
        tween.TweenProperty(_stepLabel, "modulate:a", 0f, 0.15f);

        await ToSignal(tween, Tween.SignalName.Finished);

        Visible = false;
        _backdrop.Modulate = Colors.White; // Reset for next use
        GD.Print("[LoadingScreen] Hidden");
    }
}