using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// M37.5: Uluslararası örgütler oyuncu arayüzü paneli.
// HUD ORGANIZATIONS butonundan overlay olarak açılır; ESC kapatır, backdrop harita girdisini bloke eder.
// Tüm içerik kodla oluşturulur (.tscn yok), CabinetPanel kalıbını takip eder.
public partial class OrganizationPanel : Control
{
    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _content;
    private ScrollContainer _scroll;

    private const string Title = "ORGANIZATIONS";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[OrganizationPanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new OrganizationPanel();
        parent.AddChild(panel);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // paused iken de kapanış/input çalışsın
        UITheme.ApplyTheme(this);
        BuildUi();
        RefreshContent();
        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);
        GD.Print("OrganizationPanel: hazır");
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
        _backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.75f), MouseFilter = Control.MouseFilterEnum.Stop };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(860, 520) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        margin.AddChild(vbox);

        // --- Başlık ---
        var titleRow = new HBoxContainer();
        var title = new Label { Text = Title };
        title.AddThemeFontSizeOverride("font_size", 30);
        titleRow.AddChild(title);
        titleRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        titleRow.AddChild(closeBtn);
        vbox.AddChild(titleRow);
        vbox.AddChild(new HSeparator());

        // --- Oyuncu ülke özeti ---
        _countryLabel = new Label { Text = "Ülke seçili değil." };
        _countryLabel.AddThemeFontSizeOverride("font_size", 20);
        _countryLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        vbox.AddChild(_countryLabel);

        // --- İçerik (kaydırılabilir) ---
        _scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(_content);
        vbox.AddChild(_scroll);

        RefreshContent();
    }

    private Label _countryLabel;

    private void RefreshContent()
    {
        if (_content == null) return;

        var children = new List<Node>();
        foreach (Node child in _content.GetChildren()) children.Add(child);
        foreach (var child in children)
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        var gm = GameManager.Instance;
        string playerId = gm?.HasPlayer == true && gm.PlayerCountryData != null ? gm.PlayerCountryData.Id : null;

        if (playerId == null)
        {
            var empty = new Label { Text = "Ülke seçili değil." };
            empty.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _content.AddChild(empty);
            _countryLabel.Text = "Ülke seçili değil.";
            return;
        }

        var playerOrgs = OrganizationSystem.CountryOrgs(playerId);
        _countryLabel.Text = $"{gm.PlayerCountryData.Name} ({playerId}) — Üyelikler: " +
            (playerOrgs.Count > 0 ? string.Join(", ", playerOrgs) : "Yok");

        foreach (var orgId in OrganizationSystem.OrganizationIds)
        {
            var def = OrganizationSystem.Get(orgId);
            if (def == null) continue;

            bool isMember = OrganizationSystem.IsMember(orgId, playerId);
            var card = BuildOrgCard(def, playerId, isMember);
            _content.AddChild(card);
        }

        var note = new Label { Text = "JOIN/LEAVE: üyelik değişimi (test kanca kodu, M37.4). Oy: üye ağırlığı (AIVoteWeight). Çarpanlar üyeliklere göre toplanır (M37.3)." };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _content.AddChild(note);
    }

    private VBoxContainer BuildOrgCard(OrganizationSystem.OrganizationDef def, string playerId, bool isMember)
    {
        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 6);

        // Üst satır: isim + tür
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        var name = new Label { Text = def.Name };
        name.AddThemeFontSizeOverride("font_size", 16);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.TooltipText = $"ID: {def.Id} | Kurulma: {def.FoundedDate} | Merkez: {def.HeadquartersCountryId}";
        row.AddChild(name);

        var typeBadge = new Label { Text = $"[{def.Type}]" };
        typeBadge.AddThemeFontSizeOverride("font_size", 12);
        typeBadge.AddThemeColorOverride("font_color", UITheme.TextMuted);
        typeBadge.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(typeBadge);

        card.AddChild(row);

        // Detaylar: üyeler, kurallar, çarpanlar
        var details = new Label
        {
            Text = $"Üyeler ({OrganizationSystem.Members(def.Id).Count}): {string.Join(", ", OrganizationSystem.Members(def.Id).Take(8))}" +
                   (OrganizationSystem.Members(def.Id).Count > 8 ? " ..." : "")
        };
        details.AddThemeFontSizeOverride("font_size", 12);
        details.AddThemeColorOverride("font_color", UITheme.TextMuted);
        card.AddChild(details);

        if (def.SpecialRules != null && def.SpecialRules.Length > 0)
        {
            var rules = new Label { Text = "Kurallar: " + string.Join("; ", def.SpecialRules) };
            rules.AddThemeFontSizeOverride("font_size", 11);
            rules.AddThemeColorOverride("font_color", UITheme.TextMuted);
            card.AddChild(rules);
        }

        // Çarpanlar (M37.3)
        var multParts = new List<string>();
        if (def.MilitaryEfficiencyBonus != 0) multParts.Add($"Askeri verimlilik {def.MilitaryEfficiencyBonus * 100:+0;-0}%");
        if (def.TradeBonus != 0) multParts.Add($"Ticaret {def.TradeBonus * 100:+0;-0}%");
        if (def.LegitimacyBonus != 0) multParts.Add($"Meşruiyet {def.LegitimacyBonus * 100:+0;-0}%");
        if (multParts.Count > 0)
        {
            var mult = new Label { Text = "Çarpanlar: " + string.Join(" | ", multParts) };
            mult.AddThemeFontSizeOverride("font_size", 12);
            mult.AddThemeColorOverride("font_color", UITheme.Gold);
            card.AddChild(mult);
        }

        // Buton satırı: üyelik + oy
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);

        var actionBtn = new Button
        {
            Text = isMember ? "ÜYELİKTEN ÇIK" : "ÜYE OL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(120, 28)
        };
        string orgId = def.Id; // closure için kopya
        actionBtn.Pressed += () =>
        {
            if (isMember)
                OrganizationSystem.RemoveMember(orgId, playerId);
            else
                OrganizationSystem.AddMember(orgId, playerId);
            GD.Print($"[OrganizationPanel] {playerId} {(isMember ? "çıkardı" : "ekledi")} → {def.Name} ({orgId})");
            RefreshContent();
        };
        UITheme.AddHoverScale(actionBtn);
        btnRow.AddChild(actionBtn);

        var voteBtn = new Button
        {
            Text = "OY",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(40, 28),
            TooltipText = isMember
                ? $"AI oy ağırlığı: {OrganizationSystem.AIVoteWeight(orgId, playerId, "agenda"):F2} (üye)"
                : "Üye değilseniz oy kullanamazsınız."
        };
        voteBtn.Disabled = !isMember;
        voteBtn.Pressed += () =>
        {
            GD.Print($"[OrganizationPanel] Oy → {def.Name} ({orgId}) ağırlık={OrganizationSystem.AIVoteWeight(orgId, playerId, "agenda"):F2}");
        };
        UITheme.AddHoverScale(voteBtn);
        btnRow.AddChild(voteBtn);

        card.AddChild(btnRow);

        // Üye olduğunda mevcut çarpan özeti (kullanıcının ülkesine özel)
        if (isMember)
        {
            var summ = new Label
            {
                Text = $"Sizin çarpanlarınız (üye olduğunuz org'lar): Askeri {OrganizationSystem.GetMilitaryEfficiencyMultiplier(playerId):F2} | " +
                       $"Ticaret {OrganizationSystem.GetTradeMultiplier(playerId):F2} | Meşruiyet {OrganizationSystem.GetLegitimacyMultiplier(playerId):F2}"
            };
            summ.AddThemeFontSizeOverride("font_size", 11);
            summ.AddThemeColorOverride("font_color", UITheme.TextMuted);
            card.AddChild(summ);
        }

        return card;
    }

    private void Close()
    {
        GD.Print("OrganizationPanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}
