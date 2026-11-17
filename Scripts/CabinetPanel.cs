using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Cabinet Panel (M21.1 + Stage 2 UI Upgrade):
/// Shows country's ministries and assigned ministers.
/// HUD CABINET button opens this as modal overlay.
/// Uses GameTheme + reusable UI components (Chip, TooltipBuilder, GamePanel).
/// Preserves: backdrop blocking map input, ESC to close, fade animations.
/// </summary>
public partial class CabinetPanel : GamePanel
{
    public static bool IsOpen { get; private set; }

    private VBoxContainer _contentContainer;
    private ScrollContainer _contentScroll;
    private EmptyState _emptyState;

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[CabinetPanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}");
        var panel = new CabinetPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "CABINET";
        MinSize = new Vector2(860, 520);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        RefreshContent();
        IsOpen = true;

        GD.Print("CabinetPanel: hazır (Stage 2 UI)");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void BuildUi()
    {
        // Content goes into GamePanel's ContentContainer
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // Header with country name
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(headerRow);

        var countryLabel = new Label { Text = "No Country Selected" };
        countryLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        countryLabel.AddThemeColorOverride("font_color", GameTheme.Gold);
        countryLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _countryNameLabel = countryLabel;
        headerRow.AddChild(countryLabel);

        content.AddChild(new HSeparator());

        // Content area
        _contentScroll = new ScrollContainer();
        _contentScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _contentScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(_contentScroll);

        _contentContainer = new VBoxContainer();
        _contentContainer.AddThemeConstantOverride("separation", GameTheme.SpaceS);
        _contentContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _contentScroll.AddChild(_contentContainer);

        _emptyState = EmptyState.CreateGeneric("No Data", "Select a country to view cabinet.");
        _emptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _emptyState.Visible = false;
        _contentContainer.AddChild(_emptyState);
    }

    private Label _countryNameLabel;

    private void RefreshContent()
    {
        foreach (var child in _contentContainer.GetChildren())
        {
            if (child == _emptyState) continue;
            _contentContainer.RemoveChild(child);
            child.QueueFree();
        }

        var gm = GameManager.Instance;
        if (!gm?.HasPlayer ?? true)
        {
            _emptyState.SetContent(
                "No Player Country",
                "Select a country to play first."
            );
            _emptyState.Visible = true;

            _countryNameLabel.Text = "No Country Selected";
            return;
        }

        _emptyState.Visible = false;

        string countryId = gm.PlayerCountryData.Id;
        string countryName = gm.PlayerCountryData.Name;

        _countryNameLabel.Text = $"{countryName} ({countryId}) — MINISTERS";

        foreach (var ministry in CabinetSystem.Ministries)
        {
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
            card.AddThemeStyleboxOverride("panel", GameTheme.CardBox());
            _contentContainer.AddChild(card);

            // --- Top row: ministry name + status (minister name or "Unassigned") ---
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", GameTheme.SpaceM);
            card.AddChild(row);

            var name = new Label { Text = ministry.Name };
            name.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.TooltipText = ministry.Description;
            row.AddChild(name);

            string ministerId = CabinetSystem.GetMinister(countryId, ministry.Id);
            var minister = string.IsNullOrEmpty(ministerId)
                ? null
                : CabinetSystem.Ministers.FirstOrDefault(m => m.Id == ministerId);
            string ministerName = minister?.Name ?? ministerId ?? "Unassigned";
            var status = new Label
            {
                Text = string.IsNullOrEmpty(ministerId) ? "Unassigned" : ministerName
            };
            status.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            status.AddThemeColorOverride("font_color",
                string.IsNullOrEmpty(ministerId) ? GameTheme.TextMuted : GameTheme.Gold);
            status.CustomMinimumSize = new Vector2(210, 0);
            status.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(status);

            if (string.IsNullOrEmpty(ministerId))
            {
                // --- Unassigned: available candidates, each with ASSIGN button ---
                var candidates = CabinetSystem.GetAvailableMinisters(countryId, ministry.Id);
                if (candidates.Count > 0)
                {
                    foreach (var m in candidates)
                    {
                        var candRow = new HBoxContainer();
                        candRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
                        card.AddChild(candRow);

                        var info = new Label
                        {
                            Text = $"{m.Name} — skill {m.Skill}, honesty {m.Honesty}, loyalty {m.Loyalty}, pop {m.Popularity} [{string.Join(",", m.Traits)}]"
                        };
                        info.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                        info.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                        candRow.AddChild(info);

                        var assignBtn = new Button
                        {
                            Text = "ASSIGN",
                            FocusMode = Control.FocusModeEnum.None,
                            CustomMinimumSize = new Vector2(80, 28),
                            TooltipText = $"Appoint {m.Name} as {ministry.Name}"
                        };
                        string candId = m.Id; // closure copy
                        string candName = m.Name;
                        assignBtn.Pressed += () =>
                        {
                            CabinetSystem.AssignMinister(countryId, ministry.Id, candId);
                            GD.Print($"[CabinetPanel] {candName} assigned → {ministry.Name}");
                            RefreshContent();
                        };
                        GameTheme.StyleButton(assignBtn);
                        candRow.AddChild(assignBtn);
                    }
                }
                else
                {
                    var none = new Label { Text = "No available ministers for this ministry." };
                    none.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                    none.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                    card.AddChild(none);
                }
            }
            else
            {
                // --- Assigned: minister stats + DISMISS button ---
                var stats = new Label
                {
                    Text = $"skill {minister.Skill} | honesty {minister.Honesty} | loyalty {minister.Loyalty} | pop {minister.Popularity}"
                };
                stats.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
                stats.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                card.AddChild(stats);

                var traits = minister.Traits != null && minister.Traits.Count > 0
                    ? $"Traits: {string.Join(", ", minister.Traits)}"
                    : "";
                if (!string.IsNullOrEmpty(traits))
                {
                    var tLabel = new Label { Text = traits };
                    tLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
                    tLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
                    card.AddChild(tLabel);
                }

                var scandal = CabinetSystem.GetScandalStatus(minister.Id);
                if (!string.IsNullOrEmpty(scandal))
                {
                    var sLabel = new Label { Text = scandal };
                    sLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeTiny);
                    sLabel.AddThemeColorOverride("font_color", GameTheme.Red);
                    card.AddChild(sLabel);
                }

                var dismissRow = new HBoxContainer();
                dismissRow.AddThemeConstantOverride("separation", GameTheme.SpaceS);
                card.AddChild(dismissRow);

                var dismissBtn = new Button
                {
                    Text = "DISMISS",
                    FocusMode = Control.FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(80, 28),
                    TooltipText = $"Remove {ministerName} from {ministry.Name}"
                };
                string minId = ministerId; // closure copy
                string minName = ministerName;
                dismissBtn.Pressed += () =>
                {
                    CabinetSystem.AssignMinister(countryId, ministry.Id, "");
                    GD.Print($"[CabinetPanel] {minName} dismissed — {ministry.Name} now vacant");
                    RefreshContent();
                };
                GameTheme.StyleButton(dismissBtn);
                dismissRow.AddChild(dismissBtn);
            }
        }

        var note = new Label { Text = "Minister pool and appointment/dismissal (M21.2/M21.3): Honest minister → lower crime & corruption." };
        note.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        note.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _contentContainer.AddChild(note);
    }

    public override void Close()
    {
        GD.Print("CabinetPanel: kapatılıyor");
        base.Close();
    }
}