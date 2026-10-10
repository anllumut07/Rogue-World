using Godot;
using System;
using System.Globalization;

// Merkezi oyun döngüsü: tarih, hız, pause, sinyaller.
// Autoload olarak eklenir (project.godot -> [autoload] GameManager).
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    // Stale singleton koruması: sahne değişiminde Instance ölü node olabilir.
    public static bool IsAvailable =>
        Instance != null && GodotObject.IsInstanceValid(Instance);

    // --- Sinyaller ---
    [Signal] public delegate void DateChangedEventHandler(string dateText);
    [Signal] public delegate void SpeedChangedEventHandler(int newSpeed);
    [Signal] public delegate void DayChangedEventHandler(long totalDays);
    [Signal] public delegate void PlayerCountryChangedEventHandler(string countryId);
    [Signal] public delegate void PlayerCountryDataChangedEventHandler();

    // --- Yapılandırma ---
    [Export] public double SecondsPerDay { get; set; } = 1.0; // 1x hız = 1 saniyede 1 gün

    // --- Durum ---
    public DateTime CurrentDate { get; private set; } = new DateTime(1936, 1, 1); // HOI4 başlangıcı
    public int TotalDays { get; private set; } = 0;
    [Export] public double BasePPDailyGain { get; set; } = 2.0; // HOI4 temel: 2 PP/gün

    public int Speed { get; private set; } = 0; // 0=pause, 1-5 hız
    public bool IsPaused => Speed == 0;

    public string PlayerCountryId { get; private set; } = "";
    public bool HasPlayer => PlayerCountryId != "";

    // Oyuncu ülkesinin verisi (CountryDatabase üzerinden)
    private CountryData _playerCountryData;
    public CountryData PlayerCountryData
    {
        get
        {
            if (_playerCountryData == null && HasPlayer)
                _playerCountryData = CountryDatabase.GetOrCreate(PlayerCountryId, "");
            return _playerCountryData;
        }
    }

    private double _accumulator;
    private int _speedBeforePause = 1;

    public override void _Ready()
    {
        Instance = this;

        // Ayarları yükle ve uygula
        SettingsSystem.Load();
        SettingsSystem.Apply();

        // Geliştirici/test argümanı: "--speed=N" ile başlangıç hızı (headless test için).
        // Örnek: godot --headless --quit-after 600 -- --speed=2 res://Scenes/game.tscn
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--speed=") && int.TryParse(arg.Substring(8), out var s))
                SetSpeed(Mathf.Clamp(s, 0, 5));

        GD.Print($"GameManager hazır. Başlangıç tarihi: {GetDateText()} | Hız: {Speed}");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Process(double delta)
    {
        if (IsPaused) return;

        _accumulator += delta * Speed;
        while (_accumulator >= SecondsPerDay)
        {
            _accumulator -= SecondsPerDay;
            AdvanceDay();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        switch (key.Keycode)
        {
            case Key.Space:
                TogglePause();
                break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key3: SetSpeed(3); break;
            case Key.Key4: SetSpeed(4); break;
            case Key.Key5: SetSpeed(5); break;
        }
    }

    private void AdvanceDay()
    {
        CurrentDate = CurrentDate.AddDays(1);
        TotalDays++;

        // Gunluk tick: oyuncu ulkesinin PP'si, manpower'i artsin
        DailyTick();

        EmitSignal(SignalName.DayChanged, (long)TotalDays);
        EmitSignal(SignalName.DateChanged, GetDateText());
    }

    private void DailyTick()
    {
        if (!HasPlayer) return;
        var data = PlayerCountryData;
        if (data == null) return;

        data.PoliticalPower += BasePPDailyGain;
        data.ManpowerPool += data.ManpowerPerDay;

        EmitSignal(SignalName.PlayerCountryDataChanged);
    }

    public string GetDateText()
    {
        // HOI4 stili: "1 Jan 1936"
        return CurrentDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    public void TogglePause()
    {
        if (IsPaused) SetSpeed(_speedBeforePause);
        else SetSpeed(0);
    }

    public void SetSpeed(int speed)
    {
        speed = Mathf.Clamp(speed, 0, 5);
        if (Speed == speed) return;
        Speed = speed;
        if (speed > 0) _speedBeforePause = speed;
        EmitSignal(SignalName.SpeedChanged, Speed);
    }

    public void SetPlayerCountry(string id)
    {
        if (PlayerCountryId == id) return;
        PlayerCountryId = id ?? "";
        _playerCountryData = null; // cache temizle
        var data = PlayerCountryData;
        GD.Print($"GameManager: Oyuncu ülkesi = {PlayerCountryId} ({data?.Name ?? "—"})");
        EmitSignal(SignalName.PlayerCountryChanged, PlayerCountryId);
        EmitSignal(SignalName.PlayerCountryDataChanged);
    }

    // Save/Load desteği
    public void ApplyLoadedState(DateTime date, int totalDays, string playerCountryId)
    {
        CurrentDate = date;
        TotalDays = totalDays;
        PlayerCountryId = playerCountryId ?? "";
        _playerCountryData = null;

        EmitSignal(SignalName.DateChanged, GetDateText());
        EmitSignal(SignalName.PlayerCountryChanged, PlayerCountryId);
        EmitSignal(SignalName.DayChanged, (long)TotalDays);
        EmitSignal(SignalName.PlayerCountryDataChanged);
    }
}