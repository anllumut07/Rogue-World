using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Sea Zone Panel (M43.5 + Stage 2 UI Upgrade):
/// Shows fleet supremacy by sea zone and convoy escort management.
/// Uses GameTheme + reusable UI components (GamePanel, ProgressBar, OptionButton, Chip, EmptyState).
/// Preserves: backdrop blocking map input, ESC to close, zone list, escort assignment.
/// Test properties preserved for --sea-zone-panel-test compatibility.
/// </summary>
public partial class SeaZonePanel : GamePanel
{
    public static bool IsOpen { get; private set; } = false;

    private string _playerId = "";
    private List<string> _protectorIds = new();
    private List<float> _rowSupremacy = new();
    private List<float> _rowRisk = new();

    private Label _headerLabel;
    private Label _summaryLabel;
    private Label _feedbackLabel;
    private OptionButton _protectorSelector;
    private Button _assignBtn;
    private Button _revokeBtn;
    private VBoxContainer _zoneList;
    private EmptyState _emptyState;

    // ===== Test / UI inspection accessors =====

    /// <summary>Panelin çalıştığı ülke (oyuncu).</summary>
    public string PlayerId => _playerId;

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için (test/UI denetim).</summary>
    public Vector2 DesignedSize => new Vector2(940, 560);

    /// <summary>Bölge satırı sayısı (test/UI denetim).</summary>
    public int ZoneRowCount
    {
        get
        {
            if (_zoneList == null) return 0;
            int count = 0;
            foreach (var child in _zoneList.GetChildren())
                if (child != _emptyState) count++;
            return count;
        }
    }

    /// <summary>Satırların hakimiyet barlarına yazılan değerler [0,100] (test/UI denetim).</summary>
    public IReadOnlyList<float> RowSupremacy => _rowSupremacy;

    /// <summary>Satırların risk gösterge değerleri [0,100] (test/UI denetim).</summary>
    public IReadOnlyList<float> RowRisk => _rowRisk;

    /// <summary>Koruyucu adayı sayısı (test/UI denetim).</summary>
    public int ProtectorOptionCount => _protectorIds.Count;

    /// <summary>Atama butonu basılabiliyor mu? (test/UI denetim).</summary>
    public bool AssignDisabled => _assignBtn?.Disabled ?? true;

    /// <summary>Geri alma butonu basılabiliyor mu? (test/UI denetim).</summary>
    public bool RevokeDisabled => _revokeBtn?.Disabled ?? true;

    /// <summary>Son işlem mesajı (test/UI denetim).</summary>
    public string FeedbackText => _feedbackLabel?.Text ?? "";

    /// <summary>ESC ile kapatma istendi mi? (test/UI denetim).</summary>
    public new bool CloseRequested { get; private set; }

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        if (parent == null) return;

        var gm = GameManager.Instance;
        if (gm == null || !gm.HasPlayer) return;

        GD.Print($"[SeaZonePanel] Açılıyor — ülke={gm.PlayerCountryId}, bölge={SeaZoneSystem.ZoneCount}");

        var panel = new SeaZonePanel { _playerId = gm.PlayerCountryId ?? "" };
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        // Configure GamePanel properties
        Title = "SEA ZONES";
        MinSize = new Vector2(940, 560);
        MaxSize = new Vector2(1200, 800);
        Draggable = false;
        Closable = true;
        Modal = true;
        AutoCenter = true;

        base._Ready();

        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        RefreshAll();
        IsOpen = true;

        GD.Print("SeaZonePanel: hazır (Stage 2 UI)");
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
            CloseRequested = true;
            Close();
        }
    }

    private void BuildUi()
    {
        var content = ContentContainer;
        content.AddThemeConstantOverride("separation", GameTheme.SpaceM);

        // Header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(header);

        var title = new Label { Text = "SEA ZONES" };
        title.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeXLarge);
        title.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        header.AddChild(title);

        _headerLabel = new Label { Text = "" };
        _headerLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        _headerLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        _headerLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _headerLabel.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(_headerLabel);

        var closeBtn = new Button
        {
            Text = "CLOSE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(96, 34)
        };
        closeBtn.Pressed += Close;
        GameTheme.StyleButton(closeBtn);
        header.AddChild(closeBtn);

        content.AddChild(new HSeparator());

        // Summary
        _summaryLabel = new Label { Text = "" };
        _summaryLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeMedium);
        _summaryLabel.AddThemeColorOverride("font_color", GameTheme.Gold);
        content.AddChild(_summaryLabel);

        // Escort row
        var escortRow = new HBoxContainer();
        escortRow.AddThemeConstantOverride("separation", GameTheme.SpaceM);
        content.AddChild(escortRow);

        var escortLbl = new Label { Text = "CONVOY ESCORT:" };
        escortLbl.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        escortLbl.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        escortRow.AddChild(escortLbl);

        _protectorSelector = new OptionButton
        {
            CustomMinimumSize = new Vector2(200, 30),
            TooltipText = "Allied fleet that protects your convoys"
        };
        escortRow.AddChild(_protectorSelector);

        _assignBtn = new Button
        {
            Text = "ASSIGN ESCORT",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(160, 34),
            TooltipText = "Assign the selected allied fleet to your convoy routes"
        };
        _assignBtn.Pressed += OnAssignPressed;
        GameTheme.StyleButton(_assignBtn);
        escortRow.AddChild(_assignBtn);

        _revokeBtn = new Button
        {
            Text = "REVOKE ESCORT",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(160, 34),
            TooltipText = "Withdraw the escort from your convoy routes"
        };
        _revokeBtn.Pressed += OnRevokePressed;
        GameTheme.StyleButton(_revokeBtn);
        escortRow.AddChild(_revokeBtn);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        escortRow.AddChild(_feedbackLabel);

        content.AddChild(new HSeparator());

        // Zone list
        var listHeader = new Label
        {
            Text = "FLEET SUPREMACY BY ZONE   (50 = contested, 100 = your control, 0 = enemy control)"
        };
        listHeader.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
        listHeader.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        content.AddChild(listHeader);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 320),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        content.AddChild(scroll);

        _zoneList = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _zoneList.AddThemeConstantOverride("separation", GameTheme.SpaceXS);
        scroll.AddChild(_zoneList);

        _emptyState = EmptyState.CreateGeneric("No Sea Zones", "Sea zone data not available.");
        _emptyState.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _emptyState.Visible = false;
        _zoneList.AddChild(_emptyState);

        var note = new Label
        {
            Text = "Supremacy is derived from fleet weights (dockyards + naval stock, minus battle damage). " +
                   "Raiding loss is proportional to enemy supremacy; an allied escort lowers it, but never to zero."
        };
        note.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
        note.AddThemeColorOverride("font_color", GameTheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(note);
    }

    private void RefreshAll()
    {
        RefreshHeader();
        BuildProtectorOptions();
        BuildZoneList();
    }

    private void RefreshHeader()
    {
        var country = CountryDatabase.Get(_playerId);
        float fleet = SeaZoneSystem.FleetWeight(_playerId);
        _headerLabel.Text = $"{country?.Name ?? _playerId} ({_playerId})   •   FLEET WEIGHT {fleet:F0}";
    }

    private void RefreshSummary()
    {
        float avg = SeaZoneSystem.AverageSupremacy(_playerId);
        float risk = ConvoyRaidSystem.TotalRiskPercent(_playerId);
        string escort = ConvoyRaidSystem.EscortOf(_playerId);

        string escortText;
        if (escort == null)
        {
            escortText = "no escort";
        }
        else
        {
            var protector = CountryDatabase.Get(escort);
            float reduction = ConvoyRaidSystem.EscortReductionPercent(_playerId);
            escortText = $"escort {protector?.Name ?? escort} (-{reduction:F0}% losses)";
        }

        _summaryLabel.Text = $"AVG SUPREMACY {avg:F1}   •   CONVOY RISK {risk:F1}%   •   {escortText}";
    }

    private void BuildProtectorOptions()
    {
        _protectorIds.Clear();
        _protectorSelector.Clear();

        var adjacent = new HashSet<string>(
            SeaZoneSystem.All
                .Where(z => z.AdjacentCountryIds.Any(id =>
                    string.Equals(id, _playerId, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(z => z.AdjacentCountryIds),
            StringComparer.OrdinalIgnoreCase);

        foreach (var c in CountryDatabase.All()
                     .Where(c => c != null && !c.IsAnnexed &&
                                 !string.Equals(c.Id, _playerId, StringComparison.OrdinalIgnoreCase) &&
                                 adjacent.Contains(c.Id) &&
                                 ConvoyRaidSystem.IsAllied(c.Id, _playerId) &&
                                 SeaZoneSystem.FleetWeight(c.Id) > 0f)
                     .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
        {
            _protectorIds.Add(c.Id);
            _protectorSelector.AddItem($"{c.Name} ({c.Id}) — fleet {SeaZoneSystem.FleetWeight(c.Id):F0}");
        }

        if (_protectorIds.Count > 0) _protectorSelector.Select(0);

        _assignBtn.Disabled = _protectorIds.Count == 0;
        _assignBtn.TooltipText = _protectorIds.Count == 0
            ? "No allied fleet is available near your sea zones"
            : "Assign the selected allied fleet to your convoy routes";

        RefreshSummary();
    }

    private void BuildZoneList()
    {
        _rowSupremacy.Clear();
        _rowRisk.Clear();

        foreach (var child in _zoneList.GetChildren())
        {
            if (child == _emptyState) continue;
            _zoneList.RemoveChild(child);
            child.QueueFree();
        }

        string escortId = ConvoyRaidSystem.EscortOf(_playerId);

        foreach (var zone in SeaZoneSystem.All.OrderBy(z => z.Name, StringComparer.Ordinal))
        {
            bool adjacent = zone.AdjacentCountryIds.Any(id =>
                string.Equals(id, _playerId, StringComparison.OrdinalIgnoreCase));

            float ownSup = SeaZoneSystem.Supremacy(zone, _playerId);
            float effSup = ConvoyRaidSystem.EffectiveSupremacy(zone, _playerId);
            float risk = adjacent ? ConvoyRaidSystem.ZoneRiskPercent(zone, _playerId) : 0f;

            bool escortHere = adjacent && escortId != null &&
                              zone.AdjacentCountryIds.Any(id =>
                                  string.Equals(id, escortId, StringComparison.OrdinalIgnoreCase));

            float shown = escortHere ? effSup : ownSup;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", GameTheme.SpaceM);
            row.TooltipText =
                $"{zone.Name} ({zone.Id}) — adjacent: {string.Join(", ", zone.AdjacentCountryIds)}. " +
                (!adjacent
                    ? "Your trade routes do not cross this zone."
                    : escortHere
                        ? $"Escorted by {escortId}: supremacy {ownSup:F1} → {effSup:F1}, risk {risk:F0}%."
                        : $"Supremacy {ownSup:F1}, raid risk {risk:F0}%.");

            var name = new Label { Text = zone.Name };
            name.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (!adjacent) name.AddThemeColorOverride("font_color", GameTheme.TextDisabled);
            row.AddChild(name);

            string dominant = SeaZoneSystem.DominantCountry(zone);
            var dominantLbl = new Label
            {
                Text = dominant == null ? "uncontrolled" : $"held: {dominant}",
                CustomMinimumSize = new Vector2(120, 0)
            };
            dominantLbl.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            dominantLbl.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            row.AddChild(dominantLbl);

            var bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = Mathf.Clamp(shown, 0f, 100f),
                CustomMinimumSize = new Vector2(160, 18)
            };
            var (bg, fill) = GameTheme.ProgressBarStyles(SupremacyColor(shown));
            bar.AddThemeStyleboxOverride("background", bg);
            bar.AddThemeStyleboxOverride("fill", fill);
            if (!adjacent) bar.Modulate = new Color(0.55f, 0.55f, 0.55f);
            row.AddChild(bar);

            var value = new Label
            {
                Text = adjacent ? $"{shown:F0}" : "—",
                CustomMinimumSize = new Vector2(44, 0)
            };
            value.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeNormal);
            value.AddThemeColorOverride("font_color",
                !adjacent ? GameTheme.TextDisabled :
                escortHere ? GameTheme.GoldBright : GameTheme.TextPrimary);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(value);

            var riskLbl = new Label
            {
                Text = adjacent ? $"risk {risk:F0}%" : "no route",
                CustomMinimumSize = new Vector2(74, 0)
            };
            riskLbl.AddThemeFontSizeOverride("font_size", GameTheme.FontSizeSmall);
            riskLbl.AddThemeColorOverride("font_color",
                !adjacent || risk <= 0.01f ? GameTheme.TextMuted : GameTheme.TextPrimary);
            riskLbl.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(riskLbl);

            _zoneList.AddChild(row);
            _rowSupremacy.Add(shown);
            _rowRisk.Add(risk);
        }

        if (_zoneList.GetChildCount() <= 1) // only empty state
        {
            _emptyState.Visible = true;
        }
        else
        {
            _emptyState.Visible = false;
        }

        _revokeBtn.Disabled = escortId == null;
        RefreshSummary();
    }

    private static Color SupremacyColor(float supremacy)
    {
        if (supremacy >= SeaZoneSystem.SafeSupremacyThreshold) return GameTheme.Green;
        if (supremacy >= 50f) return GameTheme.Yellow;
        return GameTheme.Red;
    }

    private void OnAssignPressed()
    {
        int index = _protectorSelector.Selected;
        if (index < 0 || index >= _protectorIds.Count)
        {
            _feedbackLabel.Text = "no allied fleet selected";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.TextMuted);
            return;
        }

        string protectorId = _protectorIds[index];
        var result = ConvoyRaidSystem.AssignEscort(protectorId, _playerId);

        if (!result.Accepted)
        {
            _feedbackLabel.Text = $"cannot assign: {ReasonText(result.Reason)}";
            _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.Red);
            GD.Print($"[SeaZonePanel] Koruma reddedildi — {protectorId} → {_playerId}: {result.Reason}");
            return;
        }

        _feedbackLabel.Text = $"escort assigned: {protectorId}";
        _feedbackLabel.AddThemeColorOverride("font_color", GameTheme.GoldBright);
        BuildZoneList();
    }

    private void OnRevokePressed()
    {
        bool removed = ConvoyRaidSystem.RevokeEscort(_playerId);
        _feedbackLabel.Text = removed ? "escort withdrawn" : "no active escort";
        _feedbackLabel.AddThemeColorOverride("font_color",
            removed ? GameTheme.GoldBright : GameTheme.TextMuted);
        BuildZoneList();
    }

    private static string ReasonText(ConvoyRaidSystem.EscortReason reason)
    {
        return reason switch
        {
            ConvoyRaidSystem.EscortReason.Ok => "ready",
            ConvoyRaidSystem.EscortReason.SelfProtection => "a country cannot escort its own convoys",
            ConvoyRaidSystem.EscortReason.UnknownCountry => "country not found",
            ConvoyRaidSystem.EscortReason.NotAllies => "not an allied fleet",
            ConvoyRaidSystem.EscortReason.ProtectorHasNoFleet => "that ally has no fleet",
            ConvoyRaidSystem.EscortReason.Annexed => "the country no longer exists",
            _ => reason.ToString()
        };
    }

    public override void Close()
    {
        GD.Print("SeaZonePanel: kapatılıyor");
        base.Close();
    }
}