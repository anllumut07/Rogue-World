using Godot;
using System;

/// <summary>
/// Reusable search box with placeholder, clear button, result count label.
/// </summary>
[Tool]
public partial class SearchBox : HBoxContainer
{
    // === Signals ===
    [Signal] public delegate void TextChangedEventHandler(string text);
    [Signal] public delegate void SearchSubmittedEventHandler(string text);
    [Signal] public delegate void ClearedEventHandler();

    // === Exported properties ===
    [Export] public string PlaceholderText { get; set; } = "Search...";
    [Export] public bool ShowResultCount { get; set; } = true;
    [Export] public bool ShowClearButton { get; set; } = true;
    [Export] public int MaxLength { get; set; } = 100;

    // === Internal ===
    private LineEdit _lineEdit;
    private Label _resultCountLabel;
    private Button _clearButton;
    private int _totalCount = 0;
    private int _filteredCount = 0;

    public override void _Ready()
    {
        GameTheme.Apply(this);
        BuildUi();
    }

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", GameTheme.SpaceS);

        // Search label (optional icon)
        var searchIcon = new Label
        {
            Text = "🔍",
            CustomMinimumSize = new Vector2(28, 0)
        };
        searchIcon.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        searchIcon.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(searchIcon);

        // Line edit
        _lineEdit = new LineEdit
        {
            PlaceholderText = PlaceholderText,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 32),
            MaxLength = MaxLength
        };
        _lineEdit.TextChanged += OnTextChanged;
        _lineEdit.TextSubmitted += OnTextSubmitted;
        // Theme styles are applied automatically via GameTheme.Apply(this)
        AddChild(_lineEdit);

        // Clear button
        if (ShowClearButton)
        {
            _clearButton = new Button
            {
                Text = "✕",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(28, 28),
                TooltipText = "Clear search",
                Visible = false
            };
            _clearButton.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            GameTheme.StyleButton(_clearButton, false, 1.1f);
            _clearButton.Pressed += OnClearPressed;
            AddChild(_clearButton);
        }

        // Result count label
        if (ShowResultCount)
        {
            _resultCountLabel = new Label
            {
                Text = "",
                CustomMinimumSize = new Vector2(140, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _resultCountLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            _resultCountLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            AddChild(_resultCountLabel);
        }
    }

    private void OnTextChanged(string text)
    {
        if (_clearButton != null)
        {
            _clearButton.Visible = !string.IsNullOrEmpty(text);
        }
        EmitSignal(SignalName.TextChanged, text);
    }

    private void OnTextSubmitted(string text)
    {
        EmitSignal(SignalName.SearchSubmitted, text);
    }

    private void OnClearPressed()
    {
        _lineEdit.Text = "";
        _clearButton.Visible = false;
        EmitSignal(SignalName.Cleared);
    }

    /// <summary>
    /// Update the result count display.
    /// </summary>
    public void UpdateResultCount(int filtered, int total)
    {
        _filteredCount = filtered;
        _totalCount = total;

        if (_resultCountLabel != null)
        {
            if (total > 0)
            {
                _resultCountLabel.Text = $"Showing {filtered} / {total}";
            }
            else
            {
                _resultCountLabel.Text = "No results";
            }
        }
    }

    /// <summary>
    /// Get current search text.
    /// </summary>
    public string GetText() => _lineEdit?.Text ?? "";

    /// <summary>
    /// Set search text programmatically.
    /// </summary>
    public void SetText(string text)
    {
        if (_lineEdit != null)
        {
            _lineEdit.Text = text;
        }
    }

    /// <summary>
    /// Clear search.
    /// </summary>
    public void Clear()
    {
        OnClearPressed();
    }

    /// <summary>
    /// Focus the search box.
    /// </summary>
    public void Focus()
    {
        _lineEdit?.GrabFocus();
    }
}