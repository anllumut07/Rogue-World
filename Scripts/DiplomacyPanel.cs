using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Diplomasi paneli (M8): world tension, faksiyonlar, ilişkiler matrisi,
/// diplomatik aksiyonlar (garanti, erişim, savaş ilanı, faksiyon), barış konferansı.
/// HUD DIPLOMACY butonundan açılır.
/// </summary>
public partial class DiplomacyPanel : Control
{
    // UI
    private Label _tensionLabel;
    private ProgressBar _tensionBar;
    private VBoxContainer _factionsBox;
    private VBoxContainer _relationsBox;      // ülke listesi + butonlar
    private VBoxContainer _guaranteesBox;
    private VBoxContainer _actionsBox;        // aksiyon butonları
    private OptionButton _targetCountry;      // hedef ülke seçici
    private Label _targetRelationLabel;       // seçili ülkeyle ilişki/görüş

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    private const string ScenePath = "res://Scenes/diplomacy_panel.tscn";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[DiplomacyPanel] OpenUnder çağrıldı — parent={parent?.GetType().Name ?? "null"}");

        DiplomacyPanel panel = null;

        // 1) Sahne dosyası projede var mı?
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    panel = scene.Instantiate<DiplomacyPanel>();
                    GD.Print($"[DiplomacyPanel] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[DiplomacyPanel] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                GD.PrintErr($"[DiplomacyPanel] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[DiplomacyPanel] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: koddan oluştur
        panel ??= new DiplomacyPanel();

        parent.AddChild(panel);
        GD.Print($"[DiplomacyPanel] Sahneye eklendi: Visible={panel.Visible}, Size={panel.Size}, Pos={panel.Position}, parent={panel.GetParent()?.GetType().Name ?? "null"}");
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        UITheme.ApplyTheme(this);

        BuildUi();
        ConnectSignals();
        RefreshAll();

        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        GD.Print("DiplomacyPanel: hazır");
    }

    public override void _ExitTree()
    {
        IsOpen = false;
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;
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
        _backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.75f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(900, 620) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);

        // ===== BAŞLIK + TENSION + CLOSE =====
        var headerRow = new HBoxContainer();
        vbox.AddChild(headerRow);

        var title = new Label { Text = "DIPLOMACY" };
        title.AddThemeFontSizeOverride("font_size", 30);
        headerRow.AddChild(title);

        headerRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        // World Tension
        _tensionBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = DiplomacySystem.WorldTension,
            CustomMinimumSize = new Vector2(200, 20)
        };
        _tensionBar.AddThemeColorOverride("fill_color", Colors.OrangeRed);
        headerRow.AddChild(_tensionBar);

        _tensionLabel = new Label { Text = $"World Tension: {DiplomacySystem.WorldTension:F1}%" };
        _tensionLabel.AddThemeFontSizeOverride("font_size", 14);
        _tensionLabel.AddThemeColorOverride("font_color", Colors.OrangeRed);
        headerRow.AddChild(_tensionLabel);

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        headerRow.AddChild(closeBtn);

        vbox.AddChild(new HSeparator());

        // ===== HEDEF ÜLKE SEÇİCİ + İLİŞKİ BİLGİSİ =====
        var targetRow = new HBoxContainer();
        targetRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(targetRow);

        var targetLabel = new Label { Text = "Target:", CustomMinimumSize = new Vector2(60, 0) };
        targetRow.AddChild(targetLabel);

        _targetCountry = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None
        };
        _targetCountry.ItemSelected += OnTargetCountryChanged;
        targetRow.AddChild(_targetCountry);

        _targetRelationLabel = new Label();
        _targetRelationLabel.AddThemeFontSizeOverride("font_size", 13);
        _targetRelationLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        targetRow.AddChild(_targetRelationLabel);

        vbox.AddChild(new HSeparator());

        // Ana scroll
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        var content = new VBoxContainer();
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);

        // ===== 1. FAKSİYONLAR =====
        var facTitle = new Label { Text = "FACTIONS" };
        facTitle.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(facTitle);

        _factionsBox = new VBoxContainer();
        _factionsBox.AddThemeConstantOverride("separation", 6);
        content.AddChild(_factionsBox);

        content.AddChild(new HSeparator());

        // ===== 2. İLİŞKİLER (Hedef ülkeye göre aksiyonlar) =====
        var relTitleRow = new HBoxContainer();
        relTitleRow.AddThemeConstantOverride("separation", 12);
        content.AddChild(relTitleRow);

        var relTitle = new Label { Text = "DIPLOMATIC ACTIONS" };
        relTitle.AddThemeFontSizeOverride("font_size", 18);
        relTitleRow.AddChild(relTitle);

        content.AddChild(new HSeparator());

        _actionsBox = new VBoxContainer();
        _actionsBox.AddThemeConstantOverride("separation", 8);
        content.AddChild(_actionsBox);

        content.AddChild(new HSeparator());

        // ===== 3. GARANTİLER =====
        var guarTitle = new Label { Text = "GUARANTEES GIVEN" };
        guarTitle.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(guarTitle);

        _guaranteesBox = new VBoxContainer();
        _guaranteesBox.AddThemeConstantOverride("separation", 4);
        content.AddChild(_guaranteesBox);

        content.AddChild(new HSeparator());

        // ===== 4. TÜM İLİŞKİLER (kısaltılmış) =====
        var allRelTitle = new Label { Text = "RELATIONS (Player → Others)" };
        allRelTitle.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(allRelTitle);

        _relationsBox = new VBoxContainer();
        _relationsBox.AddThemeConstantOverride("separation", 2);
        content.AddChild(_relationsBox);
    }

    private void ConnectSignals()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += OnDataChanged;
    }

    private void OnDataChanged() => RefreshAll();

    private void RefreshAll()
    {
        RefreshTension();
        RefreshTargetCountries();
        RefreshTargetRelation();
        RefreshFactions();
        RefreshActions();
        RefreshGuarantees();
        RefreshRelations();
    }

    private void RefreshTension()
    {
        if (_tensionBar == null || !GodotObject.IsInstanceValid(_tensionBar)) return;
        _tensionBar.Value = DiplomacySystem.WorldTension;
        _tensionLabel.Text = $"World Tension: {DiplomacySystem.WorldTension:F1}%";
        // Renk: <25 yeşil, 25-50 sarı, 50-75 turuncu, >75 kırmızı
        Color c = DiplomacySystem.WorldTension < 25 ? Colors.Green
                    : DiplomacySystem.WorldTension < 50 ? Colors.Yellow
                    : DiplomacySystem.WorldTension < 75 ? new Color(1f, 0.5f, 0f)
                    : Colors.Red;
        _tensionBar.AddThemeColorOverride("fill_color", c);
        _tensionLabel.AddThemeColorOverride("font_color", c);
    }

    private void RefreshTargetCountries()
    {
        // Seçili ülkeyi koru (günlük tazelemede sıfırlanmasın)
        string prevTarget = null;
        if (_targetCountry.GetItemCount() > 0)
        {
            int sel = _targetCountry.Selected;
            if (sel >= 0 && sel < _targetCountry.GetItemCount())
                prevTarget = _targetCountry.GetItemMetadata(sel).AsString();
        }

        _targetCountry.Clear();
        if (!GameManager.Instance.HasPlayer) return;

        var playerId = GameManager.Instance.PlayerCountryId;
        var all = CountryDatabase.All().Where(c => c.Id != playerId && !c.IsAnnexed).ToList();

        int restoreIdx = 0;
        foreach (var c in all)
        {
            string relText = "";
            var rel = DiplomacySystem.GetRelationData(playerId, c.Id);
            if (rel != null) relText = $" (Rel: {rel.Value}, Op: {rel.Opinion})";
            _targetCountry.AddItem($"{c.Name} [{c.Id}]{relText}");
            _targetCountry.SetItemMetadata(_targetCountry.GetItemCount() - 1, c.Id);
            if (c.Id == prevTarget) restoreIdx = _targetCountry.GetItemCount() - 1;
        }
        if (_targetCountry.GetItemCount() > 0)
            _targetCountry.Selected = Mathf.Clamp(restoreIdx, 0, _targetCountry.GetItemCount() - 1);
    }

    private void OnTargetCountryChanged(long idx) => RefreshTargetRelation();

    private void RefreshTargetRelation()
    {
        if (_targetRelationLabel == null || !GodotObject.IsInstanceValid(_targetRelationLabel)) return;

        if (_targetCountry.GetItemCount() == 0)
        {
            _targetRelationLabel.Text = "";
            return;
        }

        string targetId = _targetCountry.GetItemMetadata(_targetCountry.Selected).AsString();
        if (string.IsNullOrEmpty(targetId)) { _targetRelationLabel.Text = ""; return; }

        var playerId = GameManager.Instance.PlayerCountryId;
        var rel = DiplomacySystem.GetRelationData(playerId, targetId);
        var target = CountryDatabase.Get(targetId);

        string war = WarSystem.IsAtWarWith(playerId, targetId) ? " ⚔ AT WAR" : "";
        string faction = DiplomacySystem.GetFactionOf(targetId) != null ? $" Faction: {DiplomacySystem.GetFactionOf(targetId).Name}" : "";
        string guarantee = DiplomacySystem.HasGuarantee(playerId, targetId) ? " ★ GUARANTEE" : "";
        string milAccess = DiplomacySystem.HasMilitaryAccess(playerId, targetId) ? " 🛡 MIL ACCESS" : "";
        string navAccess = DiplomacySystem.HasNavalAccess(playerId, targetId) ? " ⚓ NAVAL ACCESS" : "";

        if (rel != null)
            _targetRelationLabel.Text = $"Relation: {rel.Value}  |  Opinion: {rel.Opinion}{war}{faction}{guarantee}{milAccess}{navAccess}";
        else
            _targetRelationLabel.Text = $"Relation: 0  |  Opinion: 0{war}{faction}{guarantee}{milAccess}{navAccess}";
    }

    private void RefreshFactions()
    {
        ClearChildren(_factionsBox);

        foreach (var kv in DiplomacySystem.Factions)
        {
            var f = kv.Value;
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            _factionsBox.AddChild(box);

            var header = new HBoxContainer();
            header.AddThemeConstantOverride("separation", 10);
            box.AddChild(header);

            var nameLabel = new Label { Text = f.Name };
            nameLabel.AddThemeFontSizeOverride("font_size", 15);
            nameLabel.AddThemeColorOverride("font_color", new Color(f.Color));
            header.AddChild(nameLabel);

            var leader = CountryDatabase.Get(f.LeaderId);
            var leaderLabel = new Label { Text = $"Leader: {leader?.Name ?? f.LeaderId}" };
            leaderLabel.AddThemeFontSizeOverride("font_size", 12);
            leaderLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            header.AddChild(leaderLabel);

            var memberLabel = new Label { Text = $"Members: {string.Join(", ", f.MemberIds.Select(m => CountryDatabase.Get(m)?.Name ?? m))}" };
            memberLabel.AddThemeFontSizeOverride("font_size", 11);
            memberLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            box.AddChild(memberLabel);

            // Oyuncu bu faksiyonda mı?
            bool playerInFaction = f.IsMember(GameManager.Instance?.PlayerCountryId ?? "");
            if (playerInFaction)
            {
                var leaveBtn = new Button { Text = "LEAVE FACTION", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 26) };
                leaveBtn.Pressed += () => { DiplomacySystem.LeaveFaction(); RefreshAll(); };
                UITheme.AddHoverScale(leaveBtn);
                header.AddChild(leaveBtn);
            }
            else if (f.MemberIds.Count > 0)
            {
                var joinBtn = new Button { Text = "JOIN FACTION", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 26) };
                joinBtn.Pressed += () =>
                {
                    if (DiplomacySystem.RequestJoinFaction(f.Id)) RefreshAll();
                };
                UITheme.AddHoverScale(joinBtn);
                header.AddChild(joinBtn);
            }
        }

        // Faksiyon oluştur butonu (oyuncu faksiyonda değilse)
        if (GameManager.Instance.HasPlayer && DiplomacySystem.GetFactionOf(GameManager.Instance.PlayerCountryId) == null)
        {
            var createBtn = new Button { Text = "CREATE FACTION", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 28) };
            createBtn.Pressed += OnCreateFactionPressed;
            UITheme.AddHoverScale(createBtn);
            _factionsBox.AddChild(createBtn);
        }
    }

    private void OnCreateFactionPressed()
    {
        // Basit: ideology = player's ideology
        var player = GameManager.Instance.PlayerCountryData;
        string name = $"{player.Name} Faction";
        if (DiplomacySystem.CreateFaction(name, player.Ideology))
            RefreshAll();
    }

    private void RefreshActions()
    {
        ClearChildren(_actionsBox);

        if (!GameManager.Instance.HasPlayer)
        {
            AddMuted(_actionsBox, "No player country selected");
            return;
        }

        if (_targetCountry.GetItemCount() == 0)
        {
            AddMuted(_actionsBox, "Select a target country above");
            return;
        }

        string targetId = _targetCountry.GetItemMetadata(_targetCountry.Selected).AsString();
        var target = CountryDatabase.Get(targetId);
        if (target == null || target.IsAnnexed)
        {
            AddMuted(_actionsBox, "Invalid target");
            return;
        }

        var playerId = GameManager.Instance.PlayerCountryId;
        bool atWar = WarSystem.IsAtWarWith(playerId, targetId);
        bool hasGuarantee = DiplomacySystem.HasGuarantee(playerId, targetId);
        bool hasMilAccess = DiplomacySystem.HasMilitaryAccess(playerId, targetId);
        bool hasNavAccess = DiplomacySystem.HasNavalAccess(playerId, targetId);
        var myFaction = DiplomacySystem.GetFactionOf(playerId);
        var targetFaction = DiplomacySystem.GetFactionOf(targetId);
        bool sameFaction = myFaction != null && myFaction == targetFaction;

        // M17A: WAR JUSTIFICATION bölümü — casus belli checklist + BUILD CASE
        if (!atWar)
        {
            var check = CasusBelliSystem.CheckWarPreconditions(playerId, targetId);

            var justTitle = new Label { Text = "WAR JUSTIFICATION" };
            justTitle.AddThemeFontSizeOverride("font_size", 14);
            justTitle.AddThemeColorOverride("font_color", UITheme.Gold);
            _actionsBox.AddChild(justTitle);

            // Checklist: her koşul ✓ (yeşil) / ✗ (kırmızı)
            foreach (var item in check.Items)
            {
                var l = new Label { Text = $"  {(item.Met ? "✓" : "✗")} {item.Label}", TooltipText = item.Detail };
                l.AddThemeFontSizeOverride("font_size", 12);
                l.AddThemeColorOverride("font_color", item.Met
                    ? new Color(0.45f, 0.85f, 0.45f)
                    : new Color(0.9f, 0.4f, 0.35f));
                _actionsBox.AddChild(l);
            }

            // Devam eden gerekçe inşaatı / ültimatom
            var inProgress = CasusBelliSystem.Justifications
                .Where(j => j.CountryId == playerId && j.TargetId == targetId
                         && !j.IsConsumed && (j.BuildDaysLeft > 0 || j.UltimatumDaysLeft > 0))
                .ToList();
            foreach (var j in inProgress)
            {
                var t = CasusBelliSystem.GetType(j.TypeId);
                string status = j.BuildDaysLeft > 0
                    ? $"  ⏳ BUILDING CASE: {t?.Name ?? j.TypeId} — {j.BuildDaysLeft} days left"
                    : $"  ⚠ CASE READY — ultimatum: {j.UltimatumDaysLeft} days left";
                var l = new Label { Text = status };
                l.AddThemeFontSizeOverride("font_size", 12);
                l.AddThemeColorOverride("font_color", UITheme.GoldBright);
                _actionsBox.AddChild(l);
            }

            // BUILD CASE butonları (başlatılabilir gerekçe türleri)
            foreach (var type in CasusBelliSystem.GetStartableTypes(playerId, targetId))
            {
                var buildBtn = new Button
                {
                    Text = $"BUILD CASE: {type.Name} ({type.BuildDays}d, {type.PPCost} PP)",
                    FocusMode = Control.FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(220, 26),
                    TooltipText = type.Description
                };
                buildBtn.AddThemeFontSizeOverride("font_size", 12);
                buildBtn.Pressed += () =>
                {
                    if (CasusBelliSystem.StartBuildCase(playerId, targetId, type.Id))
                        RefreshAll();
                };
                UITheme.AddHoverScale(buildBtn);
                _actionsBox.AddChild(buildBtn);
            }

            // Savaş ilanı — buton yalnız tüm koşullar sağlanınca aktif; tooltip eksikleri listeler
            int warCost = WarSystem.CalculateWarGoalCost(targetId);
            var warBtn = new Button
            {
                Text = $"DECLARE WAR ({warCost} PP)",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(220, 30),
                Disabled = !check.CanDeclare
            };
            if (check.CanDeclare)
            {
                warBtn.TooltipText = "Declare war on this country (justified)";
            }
            else
            {
                var missing = check.Items.Where(i => !i.Met).Select(i => $"✗ {i.Label}: {i.Detail}");
                warBtn.TooltipText = "War declaration blocked:\n" + string.Join("\n", missing);

                // Görünür durum etiketi: neden bloklu
                var blockedLabel = new Label { Text = $"WAR DECLARATION BLOCKED — {check.Items.First(i => !i.Met).Label}" };
                blockedLabel.AddThemeFontSizeOverride("font_size", 12);
                blockedLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.35f));
                _actionsBox.AddChild(blockedLabel);
            }
            warBtn.AddThemeColorOverride("font_disabled_color", UITheme.TextMuted);
            warBtn.Pressed += () =>
            {
                // Tıklamada her koşulu logla (test edilebilirlik)
                CasusBelliSystem.LogWarDeclarationCheck(playerId, targetId);
                if (DiplomacySystem.DeclareWar(targetId)) RefreshAll();
            };
            UITheme.AddHoverScale(warBtn);
            _actionsBox.AddChild(warBtn);
        }
        else
        {
            // Savaşta: bilgi + barış seçenekleri
            var war = WarSystem.GetWarWith(targetId);
            if (war != null)
            {
                var warInfo = new Label { Text = $"⚔ AT WAR — Enemy div: {war.EnemyDivisions} | Progress: {war.WarProgress:F0}% | Losses: {war.PlayerCasualties:N0}/{war.EnemyCasualties:N0}" };
                warInfo.AddThemeFontSizeOverride("font_size", 12);
                warInfo.AddThemeColorOverride("font_color", Colors.Red);
                _actionsBox.AddChild(warInfo);

                // Zafer barışı (ilerleme %50+): puppet/annex teklifi
                if (war.WarProgress >= 50)
                {
                    var peaceBtn = new Button { Text = "PROPOSE PEACE (VICTORY — PUPPET/ANNEX)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 30) };
                    peaceBtn.Pressed += () =>
                    {
                        var deal = DiplomacySystem.ProposePeace(playerId, targetId);
                        DiplomacySystem.ApplyPeaceDeal(deal);
                        RefreshAll();
                    };
                    UITheme.AddHoverScale(peaceBtn);
                    _actionsBox.AddChild(peaceBtn);
                }

                // Beyaz barış: her zaman sunulur
                var whiteBtn = new Button { Text = "WHITE PEACE (STATUS QUO)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 30) };
                whiteBtn.Pressed += () =>
                {
                    if (DiplomacySystem.WhitePeace(targetId)) RefreshAll();
                };
                UITheme.AddHoverScale(whiteBtn);
                _actionsBox.AddChild(whiteBtn);
            }
        }

        // Garanti
        if (!atWar && !hasGuarantee)
        {
            var guarBtn = new Button { Text = "GUARANTEE INDEPENDENCE (25 PP)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 30) };
            guarBtn.Pressed += () => { if (DiplomacySystem.GuaranteeIndependence(targetId)) RefreshAll(); };
            UITheme.AddHoverScale(guarBtn);
            _actionsBox.AddChild(guarBtn);
        }
        else if (hasGuarantee)
        {
            var revokeBtn = new Button { Text = "REVOKE GUARANTEE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 30) };
            revokeBtn.Pressed += () => { DiplomacySystem.RevokeGuarantee(targetId); RefreshAll(); };
            UITheme.AddHoverScale(revokeBtn);
            _actionsBox.AddChild(revokeBtn);
        }

        // Askeri erişim
        if (!atWar && !hasMilAccess)
        {
            var milBtn = new Button { Text = "REQUEST MILITARY ACCESS (10 PP)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 30) };
            milBtn.Pressed += () => { if (DiplomacySystem.RequestMilitaryAccess(targetId)) RefreshAll(); };
            UITheme.AddHoverScale(milBtn);
            _actionsBox.AddChild(milBtn);
        }

        // Deniz erişimi
        if (!atWar && !hasNavAccess)
        {
            var navBtn = new Button { Text = "REQUEST NAVAL ACCESS (10 PP)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 30) };
            navBtn.Pressed += () => { if (DiplomacySystem.RequestNavalAccess(targetId)) RefreshAll(); };
            UITheme.AddHoverScale(navBtn);
            _actionsBox.AddChild(navBtn);
        }

        // İlişki geliştir
        if (!atWar)
        {
            var improveBtn = new Button { Text = "IMPROVE RELATIONS (10 PP/day)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 30) };
            improveBtn.Pressed += () => { if (DiplomacySystem.ImproveRelations(targetId)) RefreshAll(); };
            UITheme.AddHoverScale(improveBtn);
            _actionsBox.AddChild(improveBtn);
        }
    }

    private void RefreshGuarantees()
    {
        ClearChildren(_guaranteesBox);

        if (!GameManager.Instance.HasPlayer)
        {
            AddMuted(_guaranteesBox, "No player country");
            return;
        }

        var guarantees = DiplomacySystem.GetGuaranteesBy(GameManager.Instance.PlayerCountryId);
        if (guarantees.Count == 0)
        {
            AddMuted(_guaranteesBox, "No guarantees given");
            return;
        }

        foreach (var g in guarantees)
        {
            var target = CountryDatabase.Get(g.TargetId);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            _guaranteesBox.AddChild(row);

            // Bayrak (16x10)
            var flagBox = new FlagBox { CustomMinimumSize = new Vector2(16, 10) };
            flagBox.SetCountry(g.TargetId);
            row.AddChild(flagBox);

            var nameLabel = new Label { Text = $"★ {target?.Name ?? g.TargetId}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            nameLabel.AddThemeFontSizeOverride("font_size", 13);
            nameLabel.AddThemeColorOverride("font_color", Colors.Gold);
            row.AddChild(nameLabel);

            var revokeBtn = new Button { Text = "REVOKE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(80, 24) };
            revokeBtn.Pressed += () => { DiplomacySystem.RevokeGuarantee(g.TargetId); RefreshAll(); };
            UITheme.AddHoverScale(revokeBtn);
            row.AddChild(revokeBtn);
        }

        // Alınan garantiler
        var received = DiplomacySystem.GetGuaranteesFor(GameManager.Instance.PlayerCountryId);
        if (received.Count > 0)
        {
            AddMuted(_guaranteesBox, "Guarantees received from:");
            foreach (var gId in received)
            {
                var g = CountryDatabase.Get(gId);
                AddMuted(_guaranteesBox, $"  ★ {g?.Name ?? gId}");
            }
        }
    }

    private void RefreshRelations()
    {
        ClearChildren(_relationsBox);

        if (!GameManager.Instance.HasPlayer) return;

        var playerId = GameManager.Instance.PlayerCountryId;
        var all = CountryDatabase.All().Where(c => c.Id != playerId && !c.IsAnnexed)
            .OrderByDescending(c => DiplomacySystem.GetRelation(playerId, c.Id))
            .Take(30) // İlk 30
            .ToList();

        foreach (var c in all)
        {
            int rel = DiplomacySystem.GetRelation(playerId, c.Id);
            int op = DiplomacySystem.GetOpinion(playerId, c.Id);
            bool atWar = WarSystem.IsAtWarWith(playerId, c.Id);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _relationsBox.AddChild(row);

            // Bayrak (16x10)
            var flagBox = new FlagBox { CustomMinimumSize = new Vector2(16, 10) };
            flagBox.SetCountry(c.Id);
            row.AddChild(flagBox);

            var nameLabel = new Label { Text = $"{c.Name} [{c.Id}]", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            nameLabel.AddThemeFontSizeOverride("font_size", 12);
            if (atWar) nameLabel.AddThemeColorOverride("font_color", Colors.Red);
            else if (rel > 50) nameLabel.AddThemeColorOverride("font_color", Colors.Green);
            else if (rel < -50) nameLabel.AddThemeColorOverride("font_color", Colors.Red);
            row.AddChild(nameLabel);

            var relLabel = new Label { Text = $"Rel: {rel}  Op: {op}" };
            relLabel.AddThemeFontSizeOverride("font_size", 11);
            relLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            row.AddChild(relLabel);

            if (atWar)
            {
                var warLabel = new Label { Text = "⚔ WAR" };
                warLabel.AddThemeFontSizeOverride("font_size", 11);
                warLabel.AddThemeColorOverride("font_color", Colors.Red);
                row.AddChild(warLabel);
            }
        }
    }

    private static void ClearChildren(Node box)
    {
        foreach (Node child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void AddMuted(Node box, string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", UITheme.TextMuted);
        box.AddChild(l);
    }

    private void Close()
    {
        GD.Print("DiplomacyPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}