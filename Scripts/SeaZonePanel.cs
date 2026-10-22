using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// M43.5 — Deniz bölgesi paneli (oyuncu tarafı).
///
/// M43.1–M43.4'ün ürettiği veriyi tek ekranda toplar: her deniz bölgesinde
/// oyuncunun hakimiyeti (filo ağırlığından saf hesap), bölgenin baskına uğrama
/// riski, bölgeyi domine eden ülke ve müttefik korumasının etkisi.
///
/// Etkileşim yalnızca iki eyleme bağlıdır:
///   • ASSIGN ESCORT — seçili müttefik filoyu oyuncunun konvoylarına atar
///     (<see cref="ConvoyRaidSystem.AssignEscort"/>).
///   • REVOKE ESCORT — korumayı geri çeker (<see cref="ConvoyRaidSystem.RevokeEscort"/>).
///
/// Panel tamamen görsel/etkileşim katmanıdır: durum tutan sistemler zaten
/// M43.1–M43.4'te yazıldı (SeaZoneSystem, NavalCombatSystem, ConvoyRaidSystem).
/// Bu yüzden Save/Load'a veri yazmaz — kalıcı durum o sistemlerde ve ülke
/// verisinde zaten saklanıyor (save-load A==B testi bundan etkilenmez).
///
/// Ölçü sınırı: 1152x648 ekrana sığacak şekilde 940x560 tasarlandı.
/// </summary>
public partial class SeaZonePanel : Control
{
    // ===== Durum =====

    private string _playerId = "";

    private Label _headerLabel;      // ülke + filo özeti
    private Label _summaryLabel;     // ortalama hakimiyet / konvoy riski / koruma
    private Label _feedbackLabel;    // atama/geri alma mesajı
    private OptionButton _protectorSelector;
    private Button _assignBtn;
    private Button _revokeBtn;

    private VBoxContainer _zoneList;

    // Koruyucu adayları (dropdown sırasıyla) — test ve seçim doğrulaması için.
    private readonly List<string> _protectorIds = new();

    // Satır başına çizilen değerler (test/UI denetim anlık görüntüsü).
    private readonly List<float> _rowSupremacy = new();
    private readonly List<float> _rowRisk = new();

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için (test/UI denetim).</summary>
    public Vector2 DesignedSize => _panel?.CustomMinimumSize ?? Vector2.Zero;

    /// <summary>Bölge satırı sayısı (test/UI denetim).</summary>
    public int ZoneRowCount => _zoneList?.GetChildCount() ?? 0;

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

    /// <summary>Panelin çalıştığı ülke (oyuncu).</summary>
    public string PlayerId => _playerId;

    /// <summary>
    /// ESC ile kapatma istendi mi? `_UnhandledInput` senkron set eder; `_ExitTree`
    /// ise animasyon (PopOut) tamamlandıktan sonra çalıştığı için test bunu bekler.
    /// </summary>
    public bool CloseRequested { get; private set; }

    // ===== Açılış =====

    /// <summary>Paneli parent altında açılacak şekilde kurar (ModalHost Layer=100).</summary>
    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;
        if (parent == null) return;

        var gm = GameManager.Instance;
        if (gm == null || !gm.HasPlayer) return;

        GD.Print($"[SeaZonePanel] Açılıyor — ülke={gm.PlayerCountryId}, " +
                 $"bölge={SeaZoneSystem.ZoneCount}");

        var panel = new SeaZonePanel { _playerId = gm.PlayerCountryId ?? "" };
        ModalHost.AddUnder(parent, panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // duraklatılmış oyunda da kapatma çalışsın
        UITheme.ApplyTheme(this);

        BuildUi();
        RefreshAll();

        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);
        GD.Print("SeaZonePanel: hazır");
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
            CloseRequested = true; // senkron gözlemlenebilir bayrak (test/UI denetim)
            Close();
        }
    }

    // ===== Kurulum =====

    private void BuildUi()
    {
        _backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop // harita girdisini blokla
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(940, 560) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        // ===== BAŞLIK =====
        var header = new HBoxContainer();
        vbox.AddChild(header);

        var title = new Label { Text = "SEA ZONES" };
        title.AddThemeFontSizeOverride("font_size", 26);
        header.AddChild(title);

        _headerLabel = new Label { Text = "" };
        _headerLabel.AddThemeFontSizeOverride("font_size", 15);
        _headerLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
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
        UITheme.AddHoverScale(closeBtn);
        header.AddChild(closeBtn);

        vbox.AddChild(new HSeparator());

        // ===== ÖZET =====
        _summaryLabel = new Label { Text = "" };
        _summaryLabel.AddThemeFontSizeOverride("font_size", 15);
        _summaryLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        vbox.AddChild(_summaryLabel);

        // ===== KORUMA SATIRI =====
        var escortRow = new HBoxContainer();
        escortRow.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(escortRow);

        var escortLbl = new Label { Text = "CONVOY ESCORT:" };
        escortLbl.AddThemeFontSizeOverride("font_size", 14);
        escortLbl.AddThemeColorOverride("font_color", UITheme.TextMuted);
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
        UITheme.AddHoverScale(_assignBtn);
        escortRow.AddChild(_assignBtn);

        _revokeBtn = new Button
        {
            Text = "REVOKE ESCORT",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(160, 34),
            TooltipText = "Withdraw the escort from your convoy routes"
        };
        _revokeBtn.Pressed += OnRevokePressed;
        UITheme.AddHoverScale(_revokeBtn);
        escortRow.AddChild(_revokeBtn);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 13);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        escortRow.AddChild(_feedbackLabel);

        vbox.AddChild(new HSeparator());

        // ===== BÖLGE LİSTESİ =====
        var listHeader = new Label
        {
            Text = "FLEET SUPREMACY BY ZONE   (50 = contested, 100 = your control, 0 = enemy control)"
        };
        listHeader.AddThemeFontSizeOverride("font_size", 14);
        listHeader.AddThemeColorOverride("font_color", UITheme.TextMuted);
        vbox.AddChild(listHeader);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 320),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        vbox.AddChild(scroll);

        _zoneList = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _zoneList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_zoneList);

        var note = new Label
        {
            Text = "Supremacy is derived from fleet weights (dockyards + naval stock, minus battle damage). " +
                   "Raiding loss is proportional to enemy supremacy; an allied escort lowers it, but never to zero."
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", UITheme.TextMuted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(note);
    }

    // ===== Yenileme =====

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

    /// <summary>
    /// Koruyucu adayları: oyuncunun komşusu olan bölgelerden en az birine
    /// komşusu, müttefik ve filoya sahip ülkeler (kendisi hariç).
    /// </summary>
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

        var children = new List<Node>();
        foreach (Node child in _zoneList.GetChildren()) children.Add(child);
        foreach (var child in children)
        {
            _zoneList.RemoveChild(child);
            child.QueueFree();
        }

        string escortId = ConvoyRaidSystem.EscortOf(_playerId);

        foreach (var zone in SeaZoneSystem.All.OrderBy(z => z.Name, StringComparer.Ordinal))
        {
            // Hakimiyet ve risk yalnızca komşu bölgeler için anlamlıdır
            // (ConvoyRaidSystem.FindRaidedZone de yalnızca komşu bölgelere bakar).
            bool adjacent = zone.AdjacentCountryIds.Any(id =>
                string.Equals(id, _playerId, StringComparison.OrdinalIgnoreCase));

            float ownSup = SeaZoneSystem.Supremacy(zone, _playerId);
            float effSup = ConvoyRaidSystem.EffectiveSupremacy(zone, _playerId);
            float risk = adjacent ? ConvoyRaidSystem.ZoneRiskPercent(zone, _playerId) : 0f;

            // Koruyucu bu bölgeye ulaşabiliyor mu? (komşu + müttefik)
            bool escortHere = adjacent && escortId != null &&
                              zone.AdjacentCountryIds.Any(id =>
                                  string.Equals(id, escortId, StringComparison.OrdinalIgnoreCase));

            // Çizilen bar: koruma etkisini de yansıtır (UI, durumu değiştirmez).
            float shown = escortHere ? effSup : ownSup;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.TooltipText =
                $"{zone.Name} ({zone.Id}) — adjacent: {string.Join(", ", zone.AdjacentCountryIds)}. " +
                (!adjacent
                    ? "Your trade routes do not cross this zone."
                    : escortHere
                        ? $"Escorted by {escortId}: supremacy {ownSup:F1} → {effSup:F1}, risk {risk:F0}%."
                        : $"Supremacy {ownSup:F1}, raid risk {risk:F0}%.");

            var name = new Label { Text = zone.Name };
            name.AddThemeFontSizeOverride("font_size", 15);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (!adjacent) name.AddThemeColorOverride("font_color", UITheme.TextDisabled);
            row.AddChild(name);

            string dominant = SeaZoneSystem.DominantCountry(zone);
            var dominantLbl = new Label
            {
                Text = dominant == null ? "uncontrolled" : $"held: {dominant}",
                CustomMinimumSize = new Vector2(120, 0)
            };
            dominantLbl.AddThemeFontSizeOverride("font_size", 13);
            dominantLbl.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(dominantLbl);

            var bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = Mathf.Clamp(shown, 0f, 100f),
                CustomMinimumSize = new Vector2(160, 18)
            };
            bar.AddThemeColorOverride("fill_color", SupremacyColor(shown));
            if (!adjacent) bar.Modulate = new Color(0.55f, 0.55f, 0.55f);
            row.AddChild(bar);

            var value = new Label
            {
                Text = adjacent ? $"{shown:F0}" : "—",
                CustomMinimumSize = new Vector2(44, 0)
            };
            value.AddThemeFontSizeOverride("font_size", 14);
            value.AddThemeColorOverride("font_color",
                !adjacent ? UITheme.TextDisabled :
                escortHere ? UITheme.GoldBright : UITheme.TextMain);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(value);

            var riskLbl = new Label
            {
                Text = adjacent ? $"risk {risk:F0}%" : "no route",
                CustomMinimumSize = new Vector2(74, 0)
            };
            riskLbl.AddThemeFontSizeOverride("font_size", 13);
            riskLbl.AddThemeColorOverride("font_color",
                !adjacent || risk <= 0.01f ? UITheme.TextMuted : UITheme.TextMain);
            riskLbl.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(riskLbl);

            _zoneList.AddChild(row);
            _rowSupremacy.Add(shown);
            _rowRisk.Add(risk);
        }

        _revokeBtn.Disabled = escortId == null;
        RefreshSummary();
    }

    /// <summary>Hakimiyet rengi: güvenli / çekişmeli / rakip ağırlıklı.</summary>
    private static Color SupremacyColor(float supremacy)
    {
        if (supremacy >= SeaZoneSystem.SafeSupremacyThreshold) return new Color(0.30f, 0.72f, 0.40f);
        if (supremacy >= 50f) return new Color(0.80f, 0.65f, 0.18f);
        return new Color(0.78f, 0.32f, 0.32f);
    }

    // ===== Eylemler =====

    private void OnAssignPressed()
    {
        int index = _protectorSelector.Selected;
        if (index < 0 || index >= _protectorIds.Count)
        {
            _feedbackLabel.Text = "no allied fleet selected";
            _feedbackLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            return;
        }

        string protectorId = _protectorIds[index];
        var result = ConvoyRaidSystem.AssignEscort(protectorId, _playerId);

        if (!result.Accepted)
        {
            // Reddedilen tahsis durumu değiştirmez; gerekçe kullanıcıya gösterilir.
            _feedbackLabel.Text = $"cannot assign: {ReasonText(result.Reason)}";
            _feedbackLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.4f, 0.4f));
            GD.Print($"[SeaZonePanel] Koruma reddedildi — {protectorId} → {_playerId}: {result.Reason}");
            return;
        }

        _feedbackLabel.Text = $"escort assigned: {protectorId}";
        _feedbackLabel.AddThemeColorOverride("font_color", UITheme.GoldBright);
        BuildZoneList();
    }

    private void OnRevokePressed()
    {
        bool removed = ConvoyRaidSystem.RevokeEscort(_playerId);
        _feedbackLabel.Text = removed ? "escort withdrawn" : "no active escort";
        _feedbackLabel.AddThemeColorOverride("font_color",
            removed ? UITheme.GoldBright : UITheme.TextMuted);
        BuildZoneList();
    }

    private static string ReasonText(ConvoyRaidSystem.EscortReason reason)
    {
        switch (reason)
        {
            case ConvoyRaidSystem.EscortReason.Ok: return "ready";
            case ConvoyRaidSystem.EscortReason.SelfProtection: return "a country cannot escort its own convoys";
            case ConvoyRaidSystem.EscortReason.UnknownCountry: return "country not found";
            case ConvoyRaidSystem.EscortReason.NotAllies: return "not an allied fleet";
            case ConvoyRaidSystem.EscortReason.ProtectorHasNoFleet: return "that ally has no fleet";
            case ConvoyRaidSystem.EscortReason.Annexed: return "the country no longer exists";
            default: return reason.ToString();
        }
    }

    private void Close()
    {
        GD.Print("SeaZonePanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}
