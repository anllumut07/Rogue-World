using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Reusable tab bar component with category tabs, hover/selected states.
/// </summary>
[Tool]
public partial class TabBar : HBoxContainer
{
    // === Signals ===
    [Signal] public delegate void TabChangedEventHandler(string tabId, int tabIndex);

    // === Exported properties ===
    [Export] public string[] TabIds { get; set; } = Array.Empty<string>();
    [Export] public string[] TabLabels { get; set; } = Array.Empty<string>();
    [Export] public int SelectedIndex { get; set; } = 0;
    [Export] public bool AllowDeselect { get; set; } = false;

    // === Internal ===
    private List<Button> _tabButtons = new();
    private Color _activeBg = GameTheme.BgMedium;
    private Color _activeBorder = GameTheme.Gold;
    private Color _inactiveBg = new Color("#1A1A2E");
    private Color _inactiveBorder = new Color("#3A3A5A");

    public override void _Ready()
    {
        GameTheme.Apply(this);
        BuildTabs();
    }

    private void BuildTabs()
    {
        AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        _tabButtons.Clear();

        int count = Math.Max(TabIds.Length, TabLabels.Length);
        for (int i = 0; i < count; i++)
        {
            string id = i < TabIds.Length ? TabIds[i] : $"tab_{i}";
            string label = i < TabLabels.Length ? TabLabels[i] : id;

            var btn = new Button
            {
                Text = label,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(100, 32),
                ButtonGroup = null // We'll manage selection manually
            };
            btn.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            btn.AddThemeStyleboxOverride("normal", GameTheme.MakeTabStyle(_inactiveBg, _inactiveBorder));
            btn.AddThemeStyleboxOverride("hover", GameTheme.MakeTabStyle(
                new Color(_inactiveBg.R + 0.05f, _inactiveBg.G + 0.05f, _inactiveBg.B + 0.05f),
                GameTheme.GoldDim));
            btn.AddThemeStyleboxOverride("pressed", GameTheme.MakeTabStyle(_activeBg, _activeBorder));
            btn.AddThemeStyleboxOverride("focus", GameTheme.MakeTabStyle(new Color(0,0,0,0), GameTheme.GoldBright, GameTheme.BorderThick));

            int index = i;
            btn.Pressed += () => OnTabPressed(index, id);
            _tabButtons.Add(btn);
            AddChild(btn);
        }

        UpdateVisuals();
    }

    private void OnTabPressed(int index, string id)
    {
        if (!AllowDeselect && index == SelectedIndex) return;

        SelectedIndex = index;
        UpdateVisuals();
        EmitSignal(SignalName.TabChanged, id, index);
    }

    public void UpdateVisuals()
    {
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            var btn = _tabButtons[i];
            bool active = i == SelectedIndex;

            if (active)
            {
                btn.AddThemeColorOverride("font_color", GameTheme.TextOnGold);
                btn.AddThemeStyleboxOverride("normal", GameTheme.MakeTabStyle(_activeBg, _activeBorder));
            }
            else
            {
                btn.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                btn.AddThemeStyleboxOverride("normal", GameTheme.MakeTabStyle(_inactiveBg, _inactiveBorder));
            }
        }
    }

    /// <summary>
    /// Set tabs at runtime.
    /// </summary>
    public void SetTabs(string[] ids, string[] labels, int selectedIndex = 0)
    {
        // Clear existing
        foreach (var btn in _tabButtons)
        {
            RemoveChild(btn);
            btn.QueueFree();
        }
        _tabButtons.Clear();

        TabIds = ids;
        TabLabels = labels;
        SelectedIndex = Mathf.Clamp(selectedIndex, 0, Math.Max(ids.Length, labels.Length) - 1);

        BuildTabs();
    }

    /// <summary>
    /// Get the currently selected tab ID.
    /// </summary>
    public string GetSelectedId()
    {
        return SelectedIndex >= 0 && SelectedIndex < TabIds.Length ? TabIds[SelectedIndex] : "";
    }

    /// <summary>
    /// Get the currently selected tab label.
    /// </summary>
    public string GetSelectedLabel()
    {
        return SelectedIndex >= 0 && SelectedIndex < TabLabels.Length ? TabLabels[SelectedIndex] : "";
    }

    /// <summary>
    /// Style for tab buttons.
    /// </summary>
    private StyleBoxFlat MakeTabStyle(Color bg, Color border, int borderWidth = 1, int radius = 4)
    {
        var b = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        b.SetBorderWidthAll(borderWidth);
        b.SetCornerRadiusAll(radius);
        return b;
    }
}