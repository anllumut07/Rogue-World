using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// M53: Danışman ekibi oyuncu arayüzü paneli.
/// HUD ADVISORS butonundan overlay olarak açılır; ESC kapatır, backdrop harita girdisini bloke eder.
/// Tüm içerik kodla oluşturulur (.tscn yok), OrganizationPanel kalıbını takip eder.
/// </summary>
public partial class AdvisorPanel : Control
{
    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private VBoxContainer _content;
    private ScrollContainer _scroll;

    private const string Title = "ADVISORS";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print("[AdvisorPanel] OpenUnder — parent=" + (parent?.GetType().Name ?? "null"));
        var panel = new AdvisorPanel();
        ModalHost.AddUnder(parent, panel);
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
        GD.Print("AdvisorPanel: hazır");
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

        var activeAdvisors = AdvisorSystem.GetAllActiveAdvisors();
        _countryLabel.Text = $"{gm.PlayerCountryData.Name} ({playerId}) — Aktif danışman: {activeAdvisors.Count}/3";

        // Mevcut aktif danışmanları göster
        foreach (var type in new[] { AdvisorSystem.AdvisorType.Economy, AdvisorSystem.AdvisorType.Security, AdvisorSystem.AdvisorType.Media })
        {
            var active = AdvisorSystem.GetActiveAdvisor(type);
            var card = BuildAdvisorCard(type, active, playerId);
            _content.AddChild(card);
        }

        // Müsait profiller listesi (işe almak için)
        var availableLabel = new Label { Text = "MÜSAİT DANIŞMANLAR (işe almak için tıklayın)" };
        availableLabel.AddThemeFontSizeOverride("font_size", 16);
        availableLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _content.AddChild(availableLabel);
        _content.AddChild(new HSeparator());

        foreach (var type in new[] { AdvisorSystem.AdvisorType.Economy, AdvisorSystem.AdvisorType.Security, AdvisorSystem.AdvisorType.Media })
        {
            var active = AdvisorSystem.GetActiveAdvisor(type);
            if (active != null) continue; // Zaten bu tipte danışman var

            var profiles = AdvisorSystem.GetProfilesForType(type);
            foreach (var profile in profiles)
            {
                var card = BuildAvailableAdvisorCard(profile, type, playerId);
                _content.AddChild(card);
            }
        }

        // Tavsiye geçmişi
        var historyLabel = new Label { Text = "TAVSİYE GEÇMİŞİ (son 10)" };
        historyLabel.AddThemeFontSizeOverride("font_size", 16);
        historyLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _content.AddChild(historyLabel);
        _content.AddChild(new HSeparator());

        var history = AdvisorSystem.GetAdviceHistory(10);
        if (history.Count == 0)
        {
            var noHist = new Label { Text = "Henüz tavsiye yok." };
            noHist.AddThemeColorOverride("font_color", UITheme.TextMuted);
            _content.AddChild(noHist);
        }
        else
        {
            foreach (var advice in history.OrderByDescending(a => a.GeneratedDay))
            {
                var card = BuildAdviceHistoryCard(advice);
                _content.AddChild(card);
            }
        }
    }

    private VBoxContainer BuildAdvisorCard(AdvisorSystem.AdvisorType type, AdvisorSystem.ActiveAdvisor active, string playerId)
    {
        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 6);

        // Üst satır: tip + isim + durum
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        var typeBadge = new Label { Text = $"[{type}]" };
        typeBadge.AddThemeFontSizeOverride("font_size", 14);
        typeBadge.AddThemeColorOverride("font_color", UITheme.TextMuted);
        typeBadge.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(typeBadge);

        var profile = AdvisorSystem.GetAllProfiles().FirstOrDefault(p => p.Id == active?.ProfileId);
        string name = profile?.Name ?? active?.ProfileId ?? "—";
        var nameLabel = new Label { Text = name };
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        var statusLabel = new Label { Text = "AKTİF" };
        statusLabel.AddThemeFontSizeOverride("font_size", 12);
        statusLabel.AddThemeColorOverride("font_color", Colors.LightGreen);
        statusLabel.CustomMinimumSize = new Vector2(60, 0);
        row.AddChild(statusLabel);

        var fireBtn = new Button
        {
            Text = "GÖREVDEN AL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(120, 28)
        };
        var t = type; // closure için kopya
        fireBtn.Pressed += () =>
        {
            AdvisorSystem.FireAdvisor(t, GameManager.Instance.TotalDays);
            GD.Print($"[AdvisorPanel] Danışman görevden alındı: {t}");
            RefreshContent();
        };
        UITheme.AddHoverScale(fireBtn);
        row.AddChild(fireBtn);

        var adviceBtn = new Button
        {
            Text = "TAVSİYE İSTE",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(120, 28)
        };
        adviceBtn.Pressed += () =>
        {
            var advice = AdvisorSystem.RequestAdvice(t, GameManager.Instance.TotalDays, playerId);
            if (advice != null)
            {
                GD.Print($"[AdvisorPanel] Tavsiye alındı: {advice.Recommendation} (conf={advice.Confidence})");
            }
            else
            {
                GD.Print($"[AdvisorPanel] Tavsiye üretilemedi (cooldown aktif veya danışman yok)");
            }
            RefreshContent();
        };
        UITheme.AddHoverScale(adviceBtn);
        row.AddChild(adviceBtn);

        card.AddChild(row);

        // Detaylar: trait, loyalty, popularity
        if (profile != null)
        {
            var details = new Label
            {
                Text = $"Trait: {profile.Trait} | Loyalty: {profile.Loyalty} | Popularity: {profile.Popularity} | Uzmanlık: {string.Join(", ", profile.ExpertiseTags)}"
            };
            details.AddThemeFontSizeOverride("font_size", 12);
            details.AddThemeColorOverride("font_color", UITheme.TextMuted);
            card.AddChild(details);
        }

        // İstatistikler
        if (active != null)
        {
            var stats = new Label
            {
                Text = $"İşe alım: gün {active.HiredDay} | Tavsiye sayısı: {active.AdviceCount} | Son tavsiye: {(active.LastAdviceDay >= 0 ? active.LastAdviceDay.ToString() : "yok")}"
            };
            stats.AddThemeFontSizeOverride("font_size", 11);
            stats.AddThemeColorOverride("font_color", UITheme.TextMuted);
            card.AddChild(stats);
        }

        // Cooldown
        if (AdvisorSystem.GetAllActiveAdvisors().TryGetValue(type, out var a) && a != null)
        {
            // Cooldown bilgisi için internal erişim yok, sadece UI'da göster
        }

        return card;
    }

    private VBoxContainer BuildAvailableAdvisorCard(AdvisorSystem.AdvisorProfile profile, AdvisorSystem.AdvisorType type, string playerId)
    {
        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 6);
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.2f, 0.5f), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });

        // Üst satır: tip + isim
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        var typeBadge = new Label { Text = $"[{type}]" };
        typeBadge.AddThemeFontSizeOverride("font_size", 14);
        typeBadge.AddThemeColorOverride("font_color", UITheme.TextMuted);
        typeBadge.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(typeBadge);

        var nameLabel = new Label { Text = profile.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 16);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        var cultureLabel = new Label { Text = $"({profile.CultureGroup})" };
        cultureLabel.AddThemeFontSizeOverride("font_size", 12);
        cultureLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        cultureLabel.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(cultureLabel);

        var hireBtn = new Button
        {
            Text = "İŞE AL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(100, 28)
        };
        var pId = profile.Id; // closure için kopya
        var t = type;
        hireBtn.Pressed += () =>
        {
            bool ok = AdvisorSystem.HireAdvisor(pId, t, GameManager.Instance.TotalDays);
            if (ok)
                GD.Print($"[AdvisorPanel] Danışman işe alındı: {profile.Name} ({t})");
            else
                GD.Print($"[AdvisorPanel] Danışman işe alınamadı: {profile.Name}");
            RefreshContent();
        };
        UITheme.AddHoverScale(hireBtn);
        row.AddChild(hireBtn);

        card.AddChild(row);

        // Detaylar
        var details = new Label
        {
            Text = $"Trait: {profile.Trait} | Loyalty: {profile.Loyalty} | Popularity: {profile.Popularity} | Uzmanlık: {string.Join(", ", profile.ExpertiseTags)}"
        };
        details.AddThemeFontSizeOverride("font_size", 12);
        details.AddThemeColorOverride("font_color", UITheme.TextMuted);
        card.AddChild(details);

        return card;
    }

    private VBoxContainer BuildAdviceHistoryCard(AdvisorSystem.AdvisorAdvice advice)
    {
        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 4);
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.15f, 0.5f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });

        // Başlık satırı
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        var typeBadge = new Label { Text = $"[{advice.Type}]" };
        typeBadge.AddThemeFontSizeOverride("font_size", 12);
        typeBadge.AddThemeColorOverride("font_color", UITheme.TextMuted);
        typeBadge.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(typeBadge);

        var confLabel = new Label { Text = $"Conf: {advice.Confidence}%" };
        confLabel.AddThemeFontSizeOverride("font_size", 12);
        confLabel.AddThemeColorOverride("font_color", advice.IsUrgent ? Colors.Orange : UITheme.Gold);
        confLabel.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(confLabel);

        var dayLabel = new Label { Text = $"Gün {advice.GeneratedDay}" };
        dayLabel.AddThemeFontSizeOverride("font_size", 12);
        dayLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        dayLabel.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(dayLabel);

        if (advice.IsUrgent)
        {
            var urgentBadge = new Label { Text = "⚠ ACİL" };
            urgentBadge.AddThemeFontSizeOverride("font_size", 12);
            urgentBadge.AddThemeColorOverride("font_color", Colors.OrangeRed);
            row.AddChild(urgentBadge);
        }

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        card.AddChild(row);

        // Öneri
        var recLabel = new Label { Text = advice.Recommendation };
        recLabel.AddThemeFontSizeOverride("font_size", 14);
        recLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        card.AddChild(recLabel);

        // Gerekçe
        var ratLabel = new Label { Text = advice.Rationale };
        ratLabel.AddThemeFontSizeOverride("font_size", 12);
        ratLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        ratLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        card.AddChild(ratLabel);

        return card;
    }

    private void Close()
    {
        GD.Print("AdvisorPanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}