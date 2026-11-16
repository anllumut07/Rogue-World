using Godot;
using System;

/// <summary>
/// Reusable window frame panel with title bar, close button, ESC to close, optional drag.
/// Designed for consistent panel UX across the game (Stage 1 UI Upgrade).
/// </summary>
[Tool]
public partial class GamePanel : PanelContainer
{
    // === Signals ===
    [Signal] public delegate void CloseRequestedEventHandler();

    // === Exported properties ===
    [Export] public string Title { get; set; } = "PANEL";
    [Export] public Vector2 MinSize { get; set; } = new Vector2(800, 500);
    [Export] public Vector2 MaxSize { get; set; } = new Vector2(1600, 1000);
    [Export] public bool Draggable { get; set; } = true;
    [Export] public bool Closable { get; set; } = true;
    [Export] public bool Modal { get; set; } = true;
    [Export] public bool AutoCenter { get; set; } = true;

    // === Internal state ===
    private VBoxContainer _rootVBox;
    private HBoxContainer _titleBar;
    private Label _titleLabel;
    private Button _closeButton;
    private Control _contentContainer;
    private ColorRect _backdrop;
    private bool _isDragging;
    private Vector2 _dragOffset;
    private Node _originalParent;

    /// <summary>
    /// Get the content container to add child controls.
    /// </summary>
    public Control ContentContainer => _contentContainer;

    /// <summary>
    /// Open the panel under a parent node (usually the game scene root).
    /// </summary>
    public static T OpenUnder<T>(Node parent) where T : GamePanel, new()
    {
        var panel = new T();
        if (parent is CanvasLayer)
        {
            parent.AddChild(panel);
        }
        else
        {
            // Find a CanvasLayer or use the viewport
            var canvasLayer = parent.GetNodeOrNull<CanvasLayer>("HUD") ?? parent.GetNodeOrNull<CanvasLayer>("UILayer");
            if (canvasLayer != null)
            {
                canvasLayer.AddChild(panel);
            }
            else
            {
                // Add directly to parent, but ensure it's on top
                parent.AddChild(panel);
                panel.SetAsTopLevel(true);
            }
        }
        return panel;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameTheme.Apply(this);
        BuildUi();
        ConnectSignals();

        if (AutoCenter)
        {
            CallDeferred(nameof(CenterOnScreen));
        }

        GameTheme.PopIn(this, 0.15f);
        if (_backdrop != null)
        {
            GameTheme.FadeIn(_backdrop, 0.1f);
        }
    }

    private void BuildUi()
    {
        // Main panel styling
        CustomMinimumSize = MinSize;
        if (MaxSize.X > 0 && MaxSize.Y > 0)
        {
            // Note: MaxSize not directly supported, use rect_max_size via script if needed
        }

        // Create backdrop if modal
        if (Modal)
        {
            _backdrop = new ColorRect
            {
                Color = new Color(0, 0, 0, 0.65f),
                MouseFilter = Control.MouseFilterEnum.Stop
            };
            _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_backdrop);
            _backdrop.GuiInput += OnBackdropInput;
        }

        // Root layout
        _rootVBox = new VBoxContainer();
        AddChild(_rootVBox);

        // Title bar
        _titleBar = new HBoxContainer();
        _titleBar.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        _titleBar.AddThemeConstantOverride("margin_left", GameTheme.SpaceL);
        _titleBar.AddThemeConstantOverride("margin_right", GameTheme.SpaceL);
        _titleBar.AddThemeConstantOverride("margin_top", GameTheme.SpaceS);
        _titleBar.AddThemeConstantOverride("margin_bottom", GameTheme.SpaceS);
        _rootVBox.AddChild(_titleBar);

        _titleLabel = new Label();
        _titleLabel.Text = Title;
        _titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTitle);
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        _titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _titleBar.AddChild(_titleLabel);

        if (Closable)
        {
            _closeButton = new Button
            {
                Text = "✕",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(36, 36),
                TooltipText = "Close (ESC)"
            };
            _closeButton.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            GameTheme.StyleButton(_closeButton, false, 1.1f);
            _closeButton.Pressed += OnClosePressed;
            _titleBar.AddChild(_closeButton);
        }

        // Separator under title
        var separator = new HSeparator();
        separator.AddThemeStyleboxOverride("separator", GameTheme.SeparatorStyle(GameTheme.BorderMedium, 1));
        _rootVBox.AddChild(separator);

        // Content container
        _contentContainer = new MarginContainer();
        _contentContainer.AddThemeConstantOverride("margin_left", GameTheme.SpaceL);
        _contentContainer.AddThemeConstantOverride("margin_right", GameTheme.SpaceL);
        _contentContainer.AddThemeConstantOverride("margin_top", GameTheme.SpaceM);
        _contentContainer.AddThemeConstantOverride("margin_bottom", GameTheme.SpaceL);
        _contentContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _contentContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rootVBox.AddChild(_contentContainer);

        // Apply panel style
        AddThemeStyleboxOverride("panel", GameTheme.PanelBox());
    }

    private void ConnectSignals()
    {
        // Title bar drag
        if (Draggable)
        {
            _titleBar.GuiInput += OnTitleBarInput;
        }
    }

    private void OnTitleBarInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                _isDragging = true;
                _dragOffset = mouseButton.Position;
                // Capture mouse for dragging
                GetViewport().SetInputAsHandled();
            }
            else
            {
                _isDragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
        {
            var globalPos = GetGlobalMousePosition();
            Position = globalPos - _dragOffset;
        }
    }

    private void OnBackdropInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
        {
            Close();
        }
    }

    private void OnClosePressed()
    {
        Close();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void CenterOnScreen()
    {
        var viewportSize = GetViewportRect().Size;
        Position = (viewportSize - Size) / 2;
    }

    public virtual void Close()
    {
        if (!IsInsideTree()) return;

        EmitSignal(SignalName.CloseRequested);

        var fadeTarget = _backdrop ?? (Control)this;
        GameTheme.FadeOut(fadeTarget, 0.08f);
        GameTheme.PopOut(this, 0.1f, () => QueueFree());
    }

    /// <summary>
    /// Set panel title at runtime.
    /// </summary>
    public void SetTitle(string title)
    {
        Title = title;
        if (_titleLabel != null)
        {
            _titleLabel.Text = title;
        }
    }

    /// <summary>
    /// Add a child to the content area.
    /// </summary>
    public void AddContent(Control child)
    {
        _contentContainer?.AddChild(child);
    }
}