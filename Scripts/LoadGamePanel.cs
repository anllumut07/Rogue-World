using Godot;
using System;

/// <summary>
/// Kayıt / Yükleme paneli (M15.3).
/// - MainMenu ve PauseMenu'den aynı bileşen açılır (OpenUnder).
/// - MainMenu: SAVE devre dışı, LOAD + DELETE çalışır.
/// - PauseMenu: SAVE + LOAD + DELETE çalışır ("mevcut slotun üzerine kaydet" = CurrentSlot önseçili).
/// - Slot adı (LineEdit), üzerine yazma/silme onay pencereleri (ConfirmationDialog).
/// - 1152x648'e sığacak boyut (~560x560), ScrollContainer, backdrop MouseFilter.Stop.
/// - AUTO SAVE + slot 1-10 listesi: slot adı, tarih, ülke, hız, kayıt zamanı.
/// - ESC/CLOSE kapatır; PauseMenu ESC dışlama listesine eklenecek.
/// </summary>
public partial class LoadGamePanel : CanvasLayer
{
    private const string AutoLabel = "AUTO";

    public static bool IsOpen { get; private set; }

    private ColorRect _backdrop;
    private PanelContainer _panel;
    private ScrollContainer _scroll;
    private VBoxContainer _slotList;
    private Button _loadBtn;
    private Button _saveBtn;
    private Button _deleteBtn;
    private Button _backBtn;
    private LineEdit _nameEdit;

    private ConfirmationDialog _confirmDialog;
    private string _pendingAction = ""; // "save" | "delete"
    private int _pendingSlot = -1;

    private int _selectedSlot = -1; // -1 = yok, 0 = auto, 1-10 = slot
    private bool _fromMainMenu;     // MainMenu'den açıldıysa true (sahne değiştirir), PauseMenu'den false (aynı oyunda)
    private Action _afterLoad;      // Yükleme başarılıysa çağrılır (PauseMenu kapanması için)
    private Label _errorLabel;       // M15.4: kullanıcıya görünen hata mesajı

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 100;

        BuildUi();

        IsOpen = true;

        // Açılış animasyonu: backdrop fade + panel pop-in
        UITheme.FadeIn(_backdrop, 0.1f);
        UITheme.PopIn(_panel, 0.15f);

        RefreshSlots();
    }

    public override void _ExitTree()
    {
        IsOpen = false;
    }

    // ESC: paneli kapat. PauseMenu Layer=100 önce alır ama IsOpen true iken ESC'yi bize bırakır.
    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            ClosePanel();
        }
    }

    // Overlay olarak açar (SettingsPanel.OpenUnder ile aynı desen)
    public static void OpenUnder(Node parent, bool fromMainMenu = false, Action afterLoad = null)
    {
        if (IsOpen) return;

        GD.Print($"[LoadGamePanel] OpenUnder — parent={parent?.GetType().Name ?? "null"}, fromMainMenu={fromMainMenu}");

        var panel = new LoadGamePanel();
        panel._fromMainMenu = fromMainMenu;
        panel._afterLoad = afterLoad;

        ModalHost.AddUnder(parent, panel);
    }

    private void BuildUi()
    {
        // Backdrop: ekranı kaplar, fare girdilerini yakalar (harita girdisi bloklansın)
        _backdrop = new ColorRect();
        _backdrop.Color = new Color(0f, 0f, 0f, 0.75f);
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.MouseFilter = Control.MouseFilterEnum.Stop; // KRİTİK: arka plan girdisi bloklansın
        AddChild(_backdrop);

        UITheme.ApplyTheme(_backdrop);

        // CenterContainer -> Panel
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.AddChild(center);

        _panel = new PanelContainer();
        _panel.CustomMinimumSize = new Vector2(560, 560); // 1152x648'e sığar
        center.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 30);
        margin.AddThemeConstantOverride("margin_right", 30);
        margin.AddThemeConstantOverride("margin_top", 25);
        margin.AddThemeConstantOverride("margin_bottom", 25);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 16);
        margin.AddChild(vbox);

        // Başlık
        var title = new Label();
        title.Text = _fromMainMenu ? "LOAD GAME" : "SAVE / LOAD GAME";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        // Açıklama
        var hint = new Label();
        hint.Text = _fromMainMenu
            ? "Select a save slot to load or delete."
            : "Select a save slot. Enter a name and press SAVE to overwrite current slot.";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeColorOverride("font_color", UITheme.TextMuted);
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hint);

        // M15.4: Kullanıcıya görünen hata mesajı ("Kayıt yüklenemedi")
        _errorLabel = new Label();
        _errorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _errorLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.3f));
        _errorLabel.AddThemeFontSizeOverride("font_size", 14);
        _errorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _errorLabel.Hide();
        vbox.AddChild(_errorLabel);

        // ScrollContainer + Slot listesi
        _scroll = new ScrollContainer();
        _scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _scroll.MouseFilter = Control.MouseFilterEnum.Stop;
        vbox.AddChild(_scroll);

        _slotList = new VBoxContainer();
        _slotList.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_slotList);

        // Kayıt adı girişi (sadece oyun içinde)
        if (!_fromMainMenu)
        {
            var nameRow = new HBoxContainer();
            nameRow.AddThemeConstantOverride("separation", 10);
            vbox.AddChild(nameRow);

            var nameLabel = new Label { Text = "Save Name:" };
            nameLabel.AddThemeFontSizeOverride("font_size", 16);
            nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            nameRow.AddChild(nameLabel);

            _nameEdit = new LineEdit();
            _nameEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _nameEdit.PlaceholderText = "Save name (optional, auto: Country — Date)";
            _nameEdit.AddThemeFontSizeOverride("font_size", 16);
            nameRow.AddChild(_nameEdit);
        }

        // Buton satırı: SAVE / LOAD / DELETE / BACK
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(btnRow);

        _saveBtn = new Button { Text = "SAVE", FocusMode = Control.FocusModeEnum.None, Disabled = true };
        _saveBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _saveBtn.Pressed += OnSavePressed;
        btnRow.AddChild(_saveBtn);
        UITheme.AddHoverScale(_saveBtn);

        _loadBtn = new Button { Text = "LOAD", FocusMode = Control.FocusModeEnum.None, Disabled = true };
        _loadBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _loadBtn.Pressed += OnLoadPressed;
        btnRow.AddChild(_loadBtn);
        UITheme.AddHoverScale(_loadBtn);

        _deleteBtn = new Button { Text = "DELETE", FocusMode = Control.FocusModeEnum.None, Disabled = true };
        _deleteBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _deleteBtn.Pressed += OnDeletePressed;
        btnRow.AddChild(_deleteBtn);
        UITheme.AddHoverScale(_deleteBtn);

        _backBtn = new Button { Text = "BACK", FocusMode = Control.FocusModeEnum.None };
        _backBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _backBtn.Pressed += OnBackPressed;
        btnRow.AddChild(_backBtn);
        UITheme.AddHoverScale(_backBtn);

        // Onay penceresi (ConfirmationDialog) — sistemin kendisi, ProcessMode.Always miras alır
        _confirmDialog = new ConfirmationDialog();
        _confirmDialog.DialogText = "";
        _confirmDialog.Confirmed += OnConfirmAction;
        _confirmDialog.Canceled += () => GD.Print("[LoadGamePanel] Onay iptal edildi");
        AddChild(_confirmDialog);

        // SAVE butonu: MainMenu'den açıldıysa gizli/devre dışı
        if (_fromMainMenu)
        {
            _saveBtn.Hide();
        }
        else
        {
            // Varsayılan kayıt adını doldur (oyuncu ülkesi + tarih)
            var gm = GameManager.Instance;
            if (gm?.HasPlayer == true)
            {
                var dateText = gm.CurrentDate.ToString("d MMM yyyy");
                _nameEdit.Text = $"{gm.PlayerCountryData?.Name ?? gm.PlayerCountryId} — {dateText}";
            }
        }

        // CurrentSlot önseçili (M15.3: "mevcut slotun üzerine kaydet")
        if (!_fromMainMenu && SaveSystem.CurrentSlot >= 0 && SaveSystem.CurrentSlot <= SaveSystem.MaxSlots)
        {
            // Seçimi RefreshSlots sonrası ayarlamak için deferred çağır
            CallDeferred(nameof(SelectCurrentSlotDeferred));
        }
    }

    private void SelectCurrentSlotDeferred()
    {
        int slot = SaveSystem.CurrentSlot;
        var info = slot == 0 ? SaveSystem.GetAutoSaveInfo() : SaveSystem.GetSlotInfo(slot);
        if (info.Exists)
        {
            SelectSlot(slot, null); // butonu refresh sonrası bul
        }
    }

    private void RefreshSlots()
    {
        // Eski butonları temizle
        foreach (var child in _slotList.GetChildren())
            child.QueueFree();

        // AUTO SAVE (slot 0)
        var autoInfo = SaveSystem.GetAutoSaveInfo();
        AddSlotButton(0, autoInfo);

        // Slots 1-10
        for (int i = 1; i <= SaveSystem.MaxSlots; i++)
        {
            var info = SaveSystem.GetSlotInfo(i);
            AddSlotButton(i, info);
        }
    }

    private void AddSlotButton(int slot, SaveSystem.SlotInfo info)
    {
        var btn = new Button { FocusMode = Control.FocusModeEnum.None };
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btn.CustomMinimumSize = new Vector2(0, 80); // yükseklik
        btn.Alignment = HorizontalAlignment.Left;

        // İçerik: dikey kutu — üstte slot adı/tarih, altta ülke + kayıt zamanı
        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 2);
        btn.AddChild(vbox);

        var topLabel = new Label();
        topLabel.AddThemeFontSizeOverride("font_size", 16);
        topLabel.HorizontalAlignment = HorizontalAlignment.Left;
        vbox.AddChild(topLabel);

        var bottomLabel = new Label();
        bottomLabel.AddThemeFontSizeOverride("font_size", 13);
        bottomLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        bottomLabel.HorizontalAlignment = HorizontalAlignment.Left;
        vbox.AddChild(bottomLabel);

        // Veriyi doldur
        string slotDisplay = slot == 0 ? "AUTO" : $"SLOT {slot}";
        if (info.Exists)
        {
            string nameLine = !string.IsNullOrEmpty(info.SlotName)
                ? $"{slotDisplay} — {info.SlotName}"
                : $"{slotDisplay} — {info.DateText}";
            topLabel.Text = nameLine;

            string bottomLine = $"{info.CountryName} ({info.CountryId})";
            if (!string.IsNullOrEmpty(info.ScenarioName))
                bottomLine += $"  •  {info.ScenarioName}";
            if (!string.IsNullOrEmpty(info.SavedAtText))
                bottomLine += $"  •  saved: {info.SavedAtText}";
            bottomLine += $"  •  speed: {info.Speed}x";
            bottomLabel.Text = bottomLine;
        }
        else
        {
            topLabel.Text = $"{slotDisplay} — EMPTY";
            topLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
            bottomLabel.Text = "—";
            bottomLabel.AddThemeColorOverride("font_color", UITheme.TextMuted);
        }

        // Tıklama: seçimi güncelle
        btn.Pressed += () => SelectSlot(slot, btn);
        _slotList.AddChild(btn);
    }

    private void SelectSlot(int slot, Button sender)
    {
        // Önceki seçimi resetle
        foreach (var child in _slotList.GetChildren())
        {
            if (child is Button b)
                b.SelfModulate = Colors.White;
        }

        // Hata mesajını temizle (yeni seçim = temiz başlangıç)
        ClearError();

        // Yeni seçim
        _selectedSlot = slot;
        // Seçili butonu vurgula
        if (sender != null)
            sender.SelfModulate = UITheme.Gold;

        // Butonları etkinleştir/devre dışı bırak
        var info = slot == 0 ? SaveSystem.GetAutoSaveInfo() : SaveSystem.GetSlotInfo(slot);

        // LOAD: kayıt varsa
        _loadBtn.Disabled = !info.Exists;

        // SAVE: oyun içinde + slot 1-10 + oyuncu var
        bool canSave = !_fromMainMenu && slot >= 1 && slot <= SaveSystem.MaxSlots &&
                       GameManager.IsAvailable && GameManager.Instance.HasPlayer;
        _saveBtn.Disabled = !canSave;

        // DELETE: kayıt varsa (AUTO veya 1-10)
        _deleteBtn.Disabled = !info.Exists;

        GD.Print($"[LoadGamePanel] Seçili slot: {(slot == 0 ? "AUTO" : $"SLOT {slot}")}, kayıt var: {info.Exists}");
    }

    private void OnSavePressed()
    {
        if (_selectedSlot < 1 || _selectedSlot > SaveSystem.MaxSlots) return;

        var info = SaveSystem.GetSlotInfo(_selectedSlot);
        string name = string.IsNullOrWhiteSpace(_nameEdit?.Text) ? null : _nameEdit.Text.Trim();

        // Eğer slot doluysa onay iste
        if (info.Exists)
        {
            _pendingAction = "save";
            _pendingSlot = _selectedSlot;
            _confirmDialog.DialogText = $"Overwrite save in SLOT {_selectedSlot}?\n\nExisting: {info.SlotName ?? info.DateText}";
            _confirmDialog.OkButtonText = "OVERWRITE";
            _confirmDialog.Show();
            return;
        }

        // Boşsa doğrudan kaydet
        DoSave(_selectedSlot, name);
    }

    private void OnLoadPressed()
    {
        if (_selectedSlot < 0) return;

        GD.Print($"[LoadGamePanel] LOAD basıldı — slot {_selectedSlot}");

        // Önceki hata mesajını temizle
        ClearError();

        if (_fromMainMenu)
        {
            // Ana menüden: sahne değiştir, pending load set et
            SaveSystem.PendingLoadSlot = _selectedSlot;
            ClosePanel();
            GetTree().ChangeSceneToFile("res://Scenes/game.tscn");
        }
        else
        {
            // PauseMenu'den: mevcut oyunda yükle
            bool ok = SaveSystem.LoadGame(_selectedSlot);
            if (ok)
            {
                GD.Print($"[LoadGamePanel] Yükleme başarılı — oyun pause'lu devam edecek");
                var after = _afterLoad;
                ClosePanel();
                after?.Invoke();
            }
            else
            {
                ShowError("Kayıt yüklenemedi. Dosya bozuk, eksik veya sürüm uyumsuz olabilir.");
                GD.PrintErr("[LoadGamePanel] Yükleme BAŞARISIZ — hata gösterildi");
            }
        }
    }

    private void OnDeletePressed()
    {
        if (_selectedSlot < 0) return;

        var info = _selectedSlot == 0 ? SaveSystem.GetAutoSaveInfo() : SaveSystem.GetSlotInfo(_selectedSlot);
        if (!info.Exists) return;

        string slotDisplay = _selectedSlot == 0 ? "AUTO" : $"SLOT {_selectedSlot}";
        _pendingAction = "delete";
        _pendingSlot = _selectedSlot;
        _confirmDialog.DialogText = $"Delete save in {slotDisplay}?\n\n{info.SlotName ?? info.DateText} — {info.CountryName}";
        _confirmDialog.OkButtonText = "DELETE";
        _confirmDialog.Show();
    }

    private void OnConfirmAction()
    {
        if (_pendingAction == "save" && _pendingSlot >= 1)
        {
            string name = string.IsNullOrWhiteSpace(_nameEdit?.Text) ? null : _nameEdit.Text.Trim();
            DoSave(_pendingSlot, name);
        }
        else if (_pendingAction == "delete" && _pendingSlot >= 0)
        {
            DoDelete(_pendingSlot);
        }
        _pendingAction = "";
        _pendingSlot = -1;
    }

    private void DoSave(int slot, string name)
    {
        GD.Print($"[LoadGamePanel] SAVE — slot {slot}, name={name ?? "(auto)"}");
        SaveSystem.SaveGame(slot, name);
        RefreshSlots(); // Liste yenile (slot adı/güncellenmiş tarih gösterilsin)
        // Seçimi koru
        SelectSlot(slot, null);
    }

    private void DoDelete(int slot)
    {
        GD.Print($"[LoadGamePanel] DELETE — slot {slot}");
        SaveSystem.DeleteSave(slot);
        RefreshSlots();
        _selectedSlot = -1;
        _loadBtn.Disabled = true;
        _saveBtn.Disabled = true;
        _deleteBtn.Disabled = true;
    }

    private void OnBackPressed()
    {
        GD.Print("[LoadGamePanel] BACK basıldı — kapanıyor");
        ClosePanel();
    }

    // M15.4: Kullanıcıya görünen hata mesajı göster / temizle
    private void ShowError(string message)
    {
        if (_errorLabel == null) return;
        _errorLabel.Text = message;
        _errorLabel.Show();
    }

    private void ClearError()
    {
        if (_errorLabel == null) return;
        _errorLabel.Text = "";
        _errorLabel.Hide();
    }

    private void ClosePanel()
    {
        // Kapanış animasyonu
        UITheme.FadeOut(_backdrop, 0.08f);
        UITheme.PopOut(_panel, 0.1f, () =>
        {
            _backdrop.Hide();
            _panel.Hide();
            IsOpen = false;
            QueueFree();
        });
    }
}