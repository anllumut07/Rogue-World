using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Ulusal odak paneli (M9): ülkeye özel odak ağacını grid üzerinde gösterir.
/// Önkoşul bağlantı çizgileri (FocusTreeCanvas._Draw), durum renkleri
/// (tamamlanan/aktif/müsait/mutex kilitli/kilitli), sol tık = başlat,
/// sağ tık aktif odak = iptal, header'da aktif odak progress bar.
/// HUD FOCUS butonundan açılır; 1060x560 panel 1152x648'de tamamen sığar.
/// </summary>
public partial class FocusPanel : Control
{
    public static bool IsOpen { get; private set; }

    private const string ScenePath = "res://Scenes/focus_panel.tscn";

    public static void OpenUnder(Node parent)
    {
        if (IsOpen) return;

        GD.Print($"[FocusPanel] OpenUnder çağrıldı — parent={parent?.GetType().Name ?? "null"}");

        FocusPanel panel = null;

        // 1) Sahne dosyası projede var mı?
        if (ResourceLoader.Exists(ScenePath))
        {
            var scene = GD.Load<PackedScene>(ScenePath);
            if (scene != null)
            {
                try
                {
                    panel = scene.Instantiate<FocusPanel>();
                    GD.Print($"[FocusPanel] Sahne yüklendi ve instantiate edildi: {ScenePath}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[FocusPanel] Sahne instantiate hatası ({ScenePath}): {e.Message} — KODDAN oluşturuluyor");
                }
            }
            else
            {
                GD.PrintErr($"[FocusPanel] GD.Load null döndürdü: {ScenePath} — dosya bozuk/parse edilemiyor olabilir. KODDAN oluşturuluyor.");
            }
        }
        else
        {
            GD.PrintErr($"[FocusPanel] Sahne YOK: {ScenePath} (ResourceLoader.Exists=false) — KODDAN oluşturuluyor.");
        }

        // 2) Yedek yol: koddan oluştur
        panel ??= new FocusPanel();

        ModalHost.AddUnder(parent, panel);
        GD.Print($"[FocusPanel] Sahneye eklendi: Visible={panel.Visible}, Size={panel.Size}, Pos={panel.Position}, parent={panel.GetParent()?.GetType().Name ?? "null"}");
    }

    // UI
    private Label _treeLabel;          // ağaç adı + tamamlanan sayısı
    private Label _progressLabel;      // aktif odak adı + kalan gün
    private ProgressBar _progressBar;  // aktif odak ilerlemesi
    private FocusTreeCanvas _canvas;
    private Label _noPlayerLabel;
    private ScrollContainer _scroll;

    // Animasyon
    private ColorRect _backdrop;
    private PanelContainer _panel;

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

        GD.Print("FocusPanel: hazır");
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
        // Backdrop: Stop — alttaki harita girişini tamamen engeller
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

        // 1152x648 dahil küçük çözünürlükte tam sığan panel boyutu
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(1060, 560) };
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

        // --- Header: başlık + ağaç bilgisi + aktif odak + CLOSE ---
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(headerRow);

        var title = new Label { Text = "NATIONAL FOCUS" };
        title.AddThemeFontSizeOverride("font_size", 26);
        headerRow.AddChild(title);

        _treeLabel = new Label();
        _treeLabel.AddThemeFontSizeOverride("font_size", 15);
        _treeLabel.AddThemeColorOverride("font_color", UITheme.Gold);
        _treeLabel.TooltipText = "Loaded focus tree for the player country";
        headerRow.AddChild(_treeLabel);

        headerRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _progressLabel = new Label();
        _progressLabel.AddThemeFontSizeOverride("font_size", 14);
        _progressLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        headerRow.AddChild(_progressLabel);

        _progressBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 0,
            CustomMinimumSize = new Vector2(140, 18),
            Visible = false
        };
        _progressBar.AddThemeColorOverride("fill_color", Colors.Orange);
        headerRow.AddChild(_progressBar);

        var closeBtn = new Button { Text = "CLOSE", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(100, 36) };
        closeBtn.Pressed += Close;
        UITheme.AddHoverScale(closeBtn);
        headerRow.AddChild(closeBtn);

        vbox.AddChild(new HSeparator());

        // --- Ağaç alanı: kaydırılabilir çizim düzlemi ---
        _scroll = new ScrollContainer();
        _scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddChild(_scroll);

        // Çizim düzlemi: Ignore — kendisi tık yemez, çocuk butonlar ve
        // ScrollContainer sürüklemesi çalışır (harita girişine karışmaz)
        _canvas = new FocusTreeCanvas
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OnStateChanged = () => UpdateActiveInfo()
        };
        _scroll.AddChild(_canvas);

        // Oyuncu seçili değilse bilgi mesajı
        _noPlayerLabel = new Label
        {
            Text = "No player country selected.\nDouble-click a country on the map first.",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _noPlayerLabel.AddThemeFontSizeOverride("font_size", 18);
        _noPlayerLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        _noPlayerLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _noPlayerLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _noPlayerLabel.Visible = false;
        vbox.AddChild(_noPlayerLabel);
    }

    private void ConnectSignals()
    {
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged += OnDataChanged;
    }

    // Günlük tick / odak değişimi: durumları güncelle (butonları yeniden KURMA)
    private void OnDataChanged()
    {
        var st = FocusSystem.PlayerState;

        // Ağaç değişti mi? (yeni oyun / ülke) → tam yeniden kurulum
        if (st == null || _canvas.CurrentTree != st.Tree)
        {
            RefreshAll();
            return;
        }

        _canvas.UpdateStates();
        RefreshTreeLabel(st);
        UpdateActiveInfo();
    }

    private void RefreshAll()
    {
        var st = FocusSystem.PlayerState;

        if (st == null)
        {
            _noPlayerLabel.Visible = true;
            _canvas.Visible = false;
            _scroll.Visible = false;
            _treeLabel.Text = "—";
            _progressLabel.Text = "";
            _progressBar.Visible = false;
            return;
        }

        _noPlayerLabel.Visible = false;
        _canvas.Visible = true;
        _scroll.Visible = true;
        RefreshTreeLabel(st);
        _canvas.SetTree(st.Tree, st);
        UpdateActiveInfo();
    }

    private void RefreshTreeLabel(FocusSystem.CountryFocusState st)
    {
        // M17.1: senaryo odak ağacı içermeyebilir (modern_world placeholder) — null güvenliği
        if (st?.Tree == null)
        {
            _treeLabel.Text = "No focus tree for this scenario";
            return;
        }
        _treeLabel.Text = $"{st.Tree.Name} — {st.Completed.Count}/{st.Tree.Focuses.Count} completed";
    }

    private void UpdateActiveInfo()
    {
        var st = FocusSystem.PlayerState;

        if (st?.Active == null)
        {
            _progressLabel.Text = "No active focus";
            _progressBar.Visible = false;
            _progressBar.Value = 0;
            return;
        }

        int daysLeft = st.Active.Cost - st.ActiveDays;
        float pct = st.Active.Cost > 0 ? 100f * st.ActiveDays / st.Active.Cost : 0f;
        _progressLabel.Text = $"{st.Active.Name} — {daysLeft} days left";
        _progressBar.Visible = true;
        _progressBar.Value = pct;
    }

    private void Close()
    {
        GD.Print("FocusPanel: kapatılıyor");
        if (GameManager.IsAvailable)
            GameManager.Instance.PlayerCountryDataChanged -= OnDataChanged;

        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () => QueueFree());
    }
}

/// <summary>
/// Odak ağacı çizim düzlemi: önkoşul bağlantı çizgileri (_Draw) + mutlak
/// konumlu odak butonları. Butonlar bir kez kurulur; UpdateStates yalnızca
/// stil/progress tazeler (günlük tick'te yeniden kurulum maliyeti yok).
/// </summary>
public partial class FocusTreeCanvas : Control
{
    // Grid ölçüleri: hücre 165x96, düğüm 152x62 (6 sütun = 990px → 1060 panelde yatay kaydırma yok)
    private const float CellW = 165f;
    private const float CellH = 96f;
    private const float NodeW = 152f;
    private const float NodeH = 62f;
    private const float Pad = 7f;

    // Panelin aktif bilgi satırını tazelemesi için geri çağrı
    public Action OnStateChanged;

    public FocusSystem.FocusTree CurrentTree { get; private set; }
    private FocusSystem.CountryFocusState _state;

    private readonly Dictionary<string, Button> _buttons = new();
    private readonly Dictionary<string, ProgressBar> _bars = new();

    // Durum bazlı stil kutuları (her tazelemede yeniden tahsis edilmesin)
    private static StyleBoxFlat _sbCompleted, _sbInProgress, _sbAvailable, _sbMutex, _sbLocked;

    public FocusTreeCanvas()
    {
        BuildStyleboxes();
    }

    private static void BuildStyleboxes()
    {
        if (_sbCompleted != null) return;

        _sbCompleted = MakeBox(new Color("#14231A"), new Color("#4C9A4C"));
        _sbInProgress = MakeBox(new Color("#26201A"), new Color("#D07A2A"));
        _sbAvailable = MakeBox(new Color("#121A26"), UITheme.Gold);
        _sbMutex = MakeBox(new Color("#221416"), new Color("#8B3A3A"));
        _sbLocked = MakeBox(new Color("#0B1018"), new Color("#1E2A3A"));
    }

    private static StyleBoxFlat MakeBox(Color bg, Color border)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        b.SetBorderWidthAll(2);
        b.SetCornerRadiusAll(4);
        b.ContentMarginLeft = 4;
        b.ContentMarginRight = 4;
        b.ContentMarginTop = 2;
        b.ContentMarginBottom = 2;
        return b;
    }

    // Ağacı kur (yeni oyun / panel ilk açılış): butonları bir kez oluştur
    public void SetTree(FocusSystem.FocusTree tree, FocusSystem.CountryFocusState state)
    {
        CurrentTree = tree;
        _state = state;

        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _buttons.Clear();
        _bars.Clear();

        if (tree == null) return;

        int maxX = 0, maxY = 0;
        foreach (var f in tree.Focuses)
        {
            maxX = Math.Max(maxX, f.X);
            maxY = Math.Max(maxY, f.Y);
        }
        CustomMinimumSize = new Vector2((maxX + 1) * CellW + Pad * 2, (maxY + 1) * CellH + Pad * 2);

        foreach (var f in tree.Focuses)
        {
            var pos = NodePos(f);

            var btn = new Button
            {
                Text = f.Name,
                Position = pos,
                CustomMinimumSize = new Vector2(NodeW, NodeH),
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = $"{f.Name}\n{f.Description}\n\nEffects: {FocusSystem.DescribeEffects(f)}\nCost: {f.Cost} days\n\nLeft-click: start | Right-click active: cancel"
            };
            btn.AddThemeFontSizeOverride("font_size", 12);

            string id = f.Id;
            btn.Pressed += () => OnFocusClicked(id);
            btn.GuiInput += (InputEvent e) => OnFocusGuiInput(e, id);

            AddChild(btn);
            _buttons[f.Id] = btn;

            var bar = new ProgressBar
            {
                MinValue = 0, MaxValue = 100, Value = 0,
                Position = new Vector2(pos.X, pos.Y + NodeH + 3),
                CustomMinimumSize = new Vector2(NodeW, 8),
                Visible = false
            };
            bar.AddThemeColorOverride("fill_color", Colors.Orange);
            AddChild(bar);
            _bars[f.Id] = bar;
        }

        UpdateStates();
    }

    // Durumları tazele: stil kutuları + font renkleri + progress + çizgi yeniden çizim
    public void UpdateStates()
    {
        if (CurrentTree == null || _state == null) return;

        foreach (var f in CurrentTree.Focuses)
        {
            var status = FocusSystem.GetStatusFor(_state, f.Id);
            if (!_buttons.TryGetValue(f.Id, out var btn)) continue;

            string prefix = status switch
            {
                FocusSystem.FocusStatus.Completed => "✓ ",
                FocusSystem.FocusStatus.InProgress => "⟳ ",
                FocusSystem.FocusStatus.MutexBlocked => "⊗ ",
                _ => ""
            };
            btn.Text = prefix + f.Name;

            switch (status)
            {
                case FocusSystem.FocusStatus.Completed:
                    btn.AddThemeStyleboxOverride("normal", _sbCompleted);
                    btn.AddThemeColorOverride("font_color", new Color("#7BC97B"));
                    break;
                case FocusSystem.FocusStatus.InProgress:
                    btn.AddThemeStyleboxOverride("normal", _sbInProgress);
                    btn.AddThemeColorOverride("font_color", new Color("#E8A85C"));
                    break;
                case FocusSystem.FocusStatus.Available:
                    btn.AddThemeStyleboxOverride("normal", _sbAvailable);
                    btn.AddThemeColorOverride("font_color", UITheme.TextMain);
                    break;
                case FocusSystem.FocusStatus.MutexBlocked:
                    btn.AddThemeStyleboxOverride("normal", _sbMutex);
                    btn.AddThemeColorOverride("font_color", new Color("#B06060"));
                    break;
                default: // Locked
                    btn.AddThemeStyleboxOverride("normal", _sbLocked);
                    btn.AddThemeColorOverride("font_color", UITheme.TextMuted);
                    break;
            }

            // Progress bar yalnızca aktif odakta görünür
            if (_bars.TryGetValue(f.Id, out var bar))
            {
                bool active = status == FocusSystem.FocusStatus.InProgress;
                bar.Visible = active;
                if (active && f.Cost > 0)
                    bar.Value = 100f * _state.ActiveDays / f.Cost;
            }
        }

        QueueRedraw(); // bağlantı çizgileri durum renkleri tazelensin
    }

    // Sol tık: müsait odak başlat, diğerleri bilgilendirme logu
    private void OnFocusClicked(string focusId)
    {
        var status = FocusSystem.GetStatusFor(_state, focusId);
        switch (status)
        {
            case FocusSystem.FocusStatus.Available:
                FocusSystem.StartFocus(focusId);
                break;
            case FocusSystem.FocusStatus.InProgress:
                GD.Print("[FocusPanel] Bu odak zaten aktif — iptal için SAĞ tıkla");
                break;
            case FocusSystem.FocusStatus.MutexBlocked:
                GD.Print("[FocusPanel] Bu odak kilitli: mutually exclusive kardeşi tamamlanmış");
                break;
            case FocusSystem.FocusStatus.Completed:
                GD.Print("[FocusPanel] Bu odak zaten tamamlanmış");
                break;
            default:
                GD.Print("[FocusPanel] Bu odak kilitli: önkoşullar eksik");
                break;
        }

        UpdateStates();
        OnStateChanged?.Invoke();
    }

    // Sağ tık: aktif odağı iptal et (RMB haritaya gitmez — backdrop Stop)
    private void OnFocusGuiInput(InputEvent e, string focusId)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
        {
            if (FocusSystem.GetStatusFor(_state, focusId) == FocusSystem.FocusStatus.InProgress)
            {
                FocusSystem.CancelActiveFocus();
                UpdateStates();
                OnStateChanged?.Invoke();
            }
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        if (CurrentTree == null || _state == null) return;

        var linkOpen = new Color(UITheme.Gold, 0.8f);       // önkoşul tamam — yol açık
        var linkDone = new Color("#4C9A4C", 0.9f);          // iki uç da tamam
        var linkClosed = new Color("#2A3542", 0.9f);        // önkoşul eksik
        var mutexLine = new Color("#7A3030", 0.45f);        // mutex ilişkisi

        foreach (var f in CurrentTree.Focuses)
        {
            // Önkoşul bağlantıları
            foreach (var pId in f.Prerequisites)
            {
                var pf = CurrentTree.ById.GetValueOrDefault(pId);
                if (pf == null) continue;

                bool prereqDone = _state.Completed.Contains(pId);
                bool bothDone = prereqDone && _state.Completed.Contains(f.Id);

                DrawLine(BottomAnchor(pf), TopAnchor(f),
                    bothDone ? linkDone : prereqDone ? linkOpen : linkClosed, 2f, true);
            }

            // Mutex ilişkisi: ince koyu kırmızı çizgi
            foreach (var mId in f.MutuallyExclusive)
            {
                var mf = CurrentTree.ById.GetValueOrDefault(mId);
                if (mf == null || string.CompareOrdinal(f.Id, mId) > 0) continue; // çift çizim engelle
                DrawLine(CenterOf(f), CenterOf(mf), mutexLine, 1f, true);
            }
        }
    }

    private static Vector2 NodePos(FocusSystem.Focus f) => new(Pad + f.X * CellW, Pad + f.Y * CellH);

    private static Vector2 TopAnchor(FocusSystem.Focus f)
    {
        var p = NodePos(f);
        return new Vector2(p.X + NodeW / 2f, p.Y);
    }

    private static Vector2 BottomAnchor(FocusSystem.Focus f)
    {
        var p = NodePos(f);
        return new Vector2(p.X + NodeW / 2f, p.Y + NodeH);
    }

    private static Vector2 CenterOf(FocusSystem.Focus f)
    {
        var p = NodePos(f);
        return new Vector2(p.X + NodeW / 2f, p.Y + NodeH / 2f);
    }
}
