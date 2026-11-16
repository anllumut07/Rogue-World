using Godot;
using System;

/// <summary>
/// Standard empty state message component for when lists/containers have no items.
/// </summary>
[Tool]
public partial class EmptyState : VBoxContainer
{
    // === Exported properties ===
    [Export] public string Title { get; set; } = "No Data";
    [Export] public string Message { get; set; } = "No items to display.";
    [Export] public string Icon { get; set; } = "📭";
    [Export] public string ActionText { get; set; } = "";
    [Export] public Color TitleColor { get; set; } = GameTheme.TextSecondary;
    [Export] public Color MessageColor { get; set; } = GameTheme.TextMuted;
    [Export] public int IconSize { get; set; } = 48;
    [Export] public int TitleFontSize { get; set; } = GameTheme.FontSizeLarge;
    [Export] public int MessageFontSize { get; set; } = GameTheme.FontSizeNormal;

    // === Internal ===
    private Label _iconLabel;
    private Label _titleLabel;
    private Label _messageLabel;
    private Button _actionButton;

    // === Signals ===
    [Signal] public delegate void ActionPressedEventHandler();

    public override void _Ready()
    {
        GameTheme.Apply(this);
        BuildUi();
    }

    private void BuildUi()
    {
        Alignment = BoxContainer.AlignmentMode.Center;
        AddThemeConstantOverride("separation", GameTheme.SpaceM);
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // Icon
        _iconLabel = new Label
        {
            Text = Icon,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _iconLabel.AddThemeFontSizeOverride("font_size", IconSize);
        AddChild(_iconLabel);

        // Title
        _titleLabel = new Label
        {
            Text = Title,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", TitleFontSize);
        _titleLabel.AddThemeColorOverride("font_color", TitleColor);
        AddChild(_titleLabel);

        // Message
        _messageLabel = new Label
        {
            Text = Message,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _messageLabel.AddThemeFontSizeOverride("font_size", MessageFontSize);
        _messageLabel.AddThemeColorOverride("font_color", MessageColor);
        _messageLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddChild(_messageLabel);

        // Action button (optional)
        if (!string.IsNullOrEmpty(ActionText))
        {
            var spacer = new Control();
            spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            AddChild(spacer);

            _actionButton = new Button
            {
                Text = ActionText,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(160, 40)
            };
            _actionButton.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            GameTheme.StyleButton(_actionButton);
            _actionButton.Pressed += () => EmitSignal(SignalName.ActionPressed);
            AddChild(_actionButton);
        }
    }

    /// <summary>
    /// Update the empty state content at runtime.
    /// </summary>
    public void SetContent(string title, string message, string icon = "", string actionText = "")
    {
        Title = title;
        Message = message;
        Icon = icon;
        ActionText = actionText;

        if (_titleLabel != null) _titleLabel.Text = title;
        if (_messageLabel != null) _messageLabel.Text = message;
        if (_iconLabel != null) _iconLabel.Text = string.IsNullOrEmpty(icon) ? "📭" : icon;

        if (_actionButton != null)
        {
            _actionButton.Text = actionText;
            _actionButton.Visible = !string.IsNullOrEmpty(actionText);
        }
    }

    /// <summary>
    /// Create a preset empty state for decisions.
    /// </summary>
    public static EmptyState CreateNoDecisions()
    {
        return new EmptyState
        {
            Title = "No Decisions Available",
            Message = "Requirements not met, on cooldown, or filtered out.",
            Icon = "📋",
        };
    }

    /// <summary>
    /// Create a preset empty state for active decisions.
    /// </summary>
    public static EmptyState CreateNoActiveDecisions()
    {
        return new EmptyState
        {
            Title = "No Active Decisions",
            Message = "Start a decision from the list to see progress here.",
            Icon = "⏳",
        };
    }

    /// <summary>
    /// Create a preset empty state for search results.
    /// </summary>
    public static EmptyState CreateNoSearchResults(string query)
    {
        return new EmptyState
        {
            Title = "No Results Found",
            Message = $"No items match \"{query}\". Try a different search term.",
            Icon = "🔍",
        };
    }

    /// <summary>
    /// Create a preset empty state for generic lists.
    /// </summary>
    public static EmptyState CreateGeneric(string title, string message)
    {
        return new EmptyState
        {
            Title = title,
            Message = message,
            Icon = "📭",
        };
    }
}