using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// M42.5 — Barış Konferansı paneli (oyuncu tarafı).
/// Aktif savaştaki rakibe karşı talepleri der, harcanabilir savaş puanını
/// ve her talebin maliyetini gösterir; onaylanınca <see cref="PeaceConferenceSystem.ApplyDeal"/>
/// ile dünya durumuna uygulanır. Beyaz barış ayrı bir butondadır.
///
/// Panel tamamen görsel/etkileşim katmanıdır: durum tutan sistem zaten
/// M42.1–M42.4'te yazıldı (WarScoreSystem, PeaceConferenceSystem, CountryAI).
/// Bu yüzden Save/Load'a veri yazmaz — kalıcı durum savaş ve ülke verisinde
/// zaten saklanıyor (save-load A==B testi bundan etkilenmez).
///
/// Ölçü sınırı: 1152x648 ekrana sığacak şekilde 900x520 tasarlandı.
/// </summary>
public partial class PeaceConferencePanel : Control
{
    // ===== Durum =====

    private WarSystem.War _war;                 // bu panelin çalıştığı savaş
    private string _winnerId = "";              // puanı harcayan taraf
    private string _loserId = "";

    private Label _opponentLabel;
    private Label _scoreLabel;                  // harcanabilir / başlangıç puanı
    private Label _termsCountLabel;             // deal başına talep sınırı
    private Label _feedbackLabel;               // reddedilen / başarılı mesaj

    private VBoxContainer _demandList;          // talep satırları
    private Button _confirmBtn;
    private Button _whiteBtn;

    // Seçili talepler (sıra korunur) ve birim sayıları
    private readonly List<PeaceConferenceSystem.Demand> _selected = new();
    private readonly Dictionary<string, int> _units = new(StringComparer.OrdinalIgnoreCase);

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

    public static bool IsOpen { get; private set; }

    /// <summary>Panelin tasarım alt sınırı — 1152x648 ekrana sığma kontrolü için (test/UI denetim).</summary>
    public Vector2 DesignedSize => _panel?.CustomMinimumSize ?? Vector2.Zero;

    /// <summary>Talep listesindeki satır sayısı (test/UI denetim).</summary>
    public int DemandRowCount => _demandList?.GetChildCount() ?? 0;

    /// <summary>Seçili talep sayısı (test/UI denetim).</summary>
    public int SelectedTermCount => _selected.Count;

    /// <summary>
    /// ESC ile kapatma istendi mi? `_UnhandledInput` senkron set eder; `_ExitTree`
    /// ise animasyon (PopOut) tamamlandıktan sonra çalıştığı için test bunu bekler.
    /// </summary>
    public bool CloseRequested { get; private set; }

    private const string ScenePath = "res://Scenes/peace_conference_panel.tscn";

    // ===== Açılış =====

    /// <summary>Paneli parent altında açılacak şekilde kurar (ModalHost Layer=100).</summary>
    public static void OpenUnder(Node parent, WarSystem.War war, string perspectiveId)
    {
        if (IsOpen) return;
        if (parent == null || war == null || string.IsNullOrEmpty(perspectiveId)) return;

        // Perspektif: puanı hangi taraf harcıyor?
        string winnerId = perspectiveId;
        string loserId = string.Equals(war.AttackerId, perspectiveId, StringComparison.OrdinalIgnoreCase)
            ? war.DefenderId
            : war.AttackerId;
        if (string.Equals(winnerId, loserId, StringComparison.OrdinalIgnoreCase)) return;

        PeaceConferenceSystem.LoadData();

        GD.Print($"[PeaceConferencePanel] Açılıyor — savaş {war.AttackerId}↔{war.DefenderId}, " +
                 $"perspektif={perspectiveId}, puan={WarScoreSystem.ScoreForSide(war, perspectiveId):F1}");

        PeaceConferencePanel panel = null;

        // 1) Sahne dosyası projede var mı? (yoksa koddan oluşturulur)
        if (ResourceLoader.Exists(ScenePath))
        {
            try
            {
                var scene = GD.Load<PackedScene>(ScenePath);
                panel = scene?.Instantiate<PeaceConferencePanel>();
                if (panel == null)
                    GD.PrintErr($"[PeaceConferencePanel] Sahne instantiate edilemedi: {ScenePath} — koddan oluşturuluyor.");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[PeaceConferencePanel] Sahne hatası ({ScenePath}): {e.Message} — koddan oluşturuluyor.");
                panel = null;
            }
        }
        else
        {
            GD.PrintErr($"[PeaceConferencePanel] Sahne YOK: {ScenePath} — koddan oluşturuluyor.");
        }

        // 2) Yedek yol: koddan oluştur
        panel ??= new PeaceConferencePanel();
        panel._war = war;
        panel._winnerId = winnerId;
        panel._loserId = loserId;

        ModalHost.AddUnder(parent, panel);
    }

    /// <summary>
    /// Aktif savaşlardan oyuncuyu içeren ilkini bulur (test ve UI kısayolu için).
    /// </summary>
    public static WarSystem.War FindWarFor(string countryId)
    {
        if (string.IsNullOrEmpty(countryId)) return null;
        return WarSystem.Wars.FirstOrDefault(w => !w.IsEnded &&
            (string.Equals(w.AttackerId, countryId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(w.DefenderId, countryId, StringComparison.OrdinalIgnoreCase)));
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        UITheme.ApplyTheme(this);

        BuildUi();
        RefreshAll();

        IsOpen = true;

        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        GD.Print("PeaceConferencePanel: hazır");
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
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(900, 520) };
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        // ===== BAŞLIK =====
        var header = new HBoxContainer();
        vbox.AddChild(header);

        var title = new Label { Text = "PEACE CONFERENCE" };
        title.AddThemeFontSizeOverride("font_size", 26);
        header.AddChild(title);

        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _opponentLabel = new Label { Text = "" };
        _opponentLabel.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(_opponentLabel);

        var closeBtn = new Button { Text = "CANCEL", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 34) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        header.AddChild(closeBtn);

        vbox.AddChild(new HSeparator());

        // ===== PUAN ÖZETİ =====
        var scoreRow = new HBoxContainer();
        scoreRow.AddThemeConstantOverride("separation", 18);
        vbox.AddChild(scoreRow);

        _scoreLabel = new Label { Text = "" };
        _scoreLabel.AddThemeFontSizeOverride("font_size", 18);
        _scoreLabel.AddThemeColorOverride("font_color", Colors.Gold);
        scoreRow.AddChild(_scoreLabel);

        _termsCountLabel = new Label { Text = "" };
        _termsCountLabel.AddThemeFontSizeOverride("font_size", 14);
        scoreRow.AddChild(_termsCountLabel);

        _feedbackLabel = new Label { Text = "" };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 14);
        _feedbackLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Right;
        scoreRow.AddChild(_feedbackLabel);

        // ===== TALEP LİSTESİ =====
        var listHeader = new Label { Text = "DEMANDS  (score cost — budget is your war score)" };
        listHeader.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(listHeader);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 300),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        vbox.AddChild(scroll);

        _demandList = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _demandList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_demandList);

        vbox.AddChild(new HSeparator());

        // ===== ONAY BUTONLARI =====
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(btnRow);

        _whiteBtn = new Button
        {
            Text = "WHITE PEACE (STATUS QUO)",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(260, 40),
            TooltipText = "No territory changes, no capitulation record"
        };
        _whiteBtn.Pressed += OnWhitePeacePressed;
        UITheme.AddHoverScale(_whiteBtn);
        btnRow.AddChild(_whiteBtn);

        btnRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _confirmBtn = new Button
        {
            Text = "CONFIRM DEAL",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(200, 40),
            TooltipText = "Apply the listed demands to the world state"
        };
        _confirmBtn.Pressed += OnConfirmPressed;
        UITheme.AddHoverScale(_confirmBtn);
        btnRow.AddChild(_confirmBtn);
    }

    // ===== Yenileme =====

    private float MyScore()
    {
        if (_war == null) return 0f;
        return WarScoreSystem.ScoreForSide(_war, _winnerId);
    }

    private void RefreshAll()
    {
        RefreshHeader();
        RefreshScore();
        BuildDemandList();
    }

    private void RefreshHeader()
    {
        var loser = CountryDatabase.Get(_loserId);
        _opponentLabel.Text = $"vs {loser?.Name ?? _loserId}";
    }

    private void RefreshScore()
    {
        float score = MyScore();
        double spent = _selected.Sum(d => PeaceConferenceSystem.CostFor(d.TermId, d.Units));
        double left = Math.Max(0, score - spent);

        _scoreLabel.Text = $"Spendable score: {left:F1}  /  {score:F1}";
        _termsCountLabel.Text = $"Terms: {_selected.Count} / {PeaceConferenceSystem.MaxTermsPerDeal}";
        _confirmBtn.Disabled = _selected.Count == 0;
    }

    private void BuildDemandList()
    {
        foreach (var child in _demandList.GetChildren().ToList())
            child.QueueFree();

        float score = MyScore();

        foreach (var def in PeaceConferenceSystem.All.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _demandList.AddChild(row);

            var label = new Label
            {
                Text = def.Label,
                CustomMinimumSize = new Vector2(230, 24),
                TooltipText = def.Description
            };
            row.AddChild(label);

            // Kullanılabilirlik: puan eşiği
            bool available = PeaceConferenceSystem.IsAvailable(def.Id, score);
            if (!available)
            {
                var why = new Label
                {
                    Text = $"— requires ≥ {def.RequiresScoreAtLeast:F0} score (have {score:F1})",
                    CustomMinimumSize = new Vector2(260, 24)
                };
                why.AddThemeColorOverride("font_color", Colors.IndianRed);
                why.AddThemeFontSizeOverride("font_size", 13);
                row.AddChild(why);
                continue;
            }

            int maxUnits = Math.Max(1, def.MaxUnits);
            int current = _units.GetValueOrDefault(def.Id, def.MaxUnits == 1 ? 1 : maxUnits);

            var cost = PeaceConferenceSystem.CostFor(def.Id, current);
            var costLabel = new Label
            {
                Text = $"cost {cost:F1}",
                CustomMinimumSize = new Vector2(90, 24)
            };
            row.AddChild(costLabel);

            // Birim seçici (maks 1 birimli terimlerde sabit etiket)
            if (def.MaxUnits > 1)
            {
                var unitLabel = new Label { Text = def.UnitLabel, CustomMinimumSize = new Vector2(40, 24) };
                row.AddChild(unitLabel);

                var spin = new SpinBox
                {
                    MinValue = 1,
                    MaxValue = maxUnits,
                    Step = 1,
                    Value = current,
                    CustomMinimumSize = new Vector2(90, 24),
                    FocusMode = Control.FocusModeEnum.None
                };
                spin.ValueChanged += v =>
                {
                    _units[def.Id] = (int)v;
                    RefreshScore();
                };
                row.AddChild(spin);
            }
            else
            {
                var unitLabel = new Label { Text = "fixed", CustomMinimumSize = new Vector2(50, 24) };
                row.AddChild(unitLabel);
            }

            row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

            bool chosen = _selected.Any(d => d.TermId == def.Id);
            var toggle = new Button
            {
                Text = chosen ? "REMOVE" : "ADD",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(90, 26),
                Disabled = !chosen && _selected.Count >= PeaceConferenceSystem.MaxTermsPerDeal
            };
            int capturedUnits = current;
            toggle.Pressed += () => ToggleTerm(def.Id, capturedUnits);
            UITheme.AddHoverScale(toggle);
            row.AddChild(toggle);
        }
    }

    private void ToggleTerm(string termId, int units)
    {
        int idx = _selected.FindIndex(d => string.Equals(d.TermId, termId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            _selected.RemoveAt(idx);
            _feedbackLabel.Text = "";
        }
        else
        {
            if (_selected.Count >= PeaceConferenceSystem.MaxTermsPerDeal)
            {
                _feedbackLabel.Text = $"Deal already holds max {PeaceConferenceSystem.MaxTermsPerDeal} terms";
                _feedbackLabel.AddThemeColorOverride("font_color", Colors.IndianRed);
                return;
            }
            _selected.Add(new PeaceConferenceSystem.Demand(termId, Math.Max(1, units)));
            _feedbackLabel.Text = "";
        }
        BuildDemandList();
        RefreshScore();
    }

    // ===== Eylemler =====

    private void OnConfirmPressed()
    {
        if (_war == null || _selected.Count == 0) return;

        var demands = new List<PeaceConferenceSystem.Demand>(_selected);
        var res = PeaceConferenceSystem.ApplyDeal(_war, demands, _winnerId, _loserId);

        if (res.Rejected.Count > 0)
        {
            _feedbackLabel.Text = $"Rejected: {string.Join(", ", res.Rejected)}";
            _feedbackLabel.AddThemeColorOverride("font_color", Colors.IndianRed);
            GD.Print($"[PeaceConferencePanel] Reddedilen talepler: {string.Join(", ", res.Rejected)}");
        }
        else
        {
            _feedbackLabel.Text = "Deal applied.";
            _feedbackLabel.AddThemeColorOverride("font_color", Colors.LightGreen);
        }

        GD.Print($"[PeaceConferencePanel] Anlaşma uygulandı — harcanan puan {res.ScoreSpent:F1}, " +
                 $"il devri {res.ProvincesTransferred}, fabrika {res.FactoriesTaken}, " +
                 $"sepet {res.FactoriesScrapped}, PP {res.PoliticalPowerTransferred:F0}");

        _selected.Clear();
        RefreshAll();
    }

    private void OnWhitePeacePressed()
    {
        if (_war == null) return;

        bool ok = PeaceConferenceSystem.WhitePeaceDeal(_war);
        _feedbackLabel.Text = ok ? "White peace signed — war ended." : "White peace failed.";
        _feedbackLabel.AddThemeColorOverride("font_color", ok ? Colors.LightGreen : Colors.IndianRed);
        GD.Print($"[PeaceConferencePanel] Beyaz barış {(ok ? "imzalandı" : "başarısız")}");

        _selected.Clear();
        RefreshAll();
    }

    private void Close()
    {
        GD.Print("PeaceConferencePanel: kapatılıyor");
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}
