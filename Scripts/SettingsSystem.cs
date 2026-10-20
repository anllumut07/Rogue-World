using Godot;
using System;
using System.Text.Json;

// Oyun ayarları: diskte user://settings.json'da tutulur, açılışta otomatik yüklenir.
public static class SettingsSystem
{
    private const string Path = "user://settings.json";

    public static float MasterVolume { get; private set; } = 0.8f;
    public static bool Fullscreen { get; private set; } = false;
    public static int ResolutionIndex { get; private set; } = 0;
    public static bool VSync { get; private set; } = true;
    public static int DefaultSpeed { get; private set; } = 0; // 0=pause, 1-5
    public static GameDifficulty Difficulty { get; private set; } = GameDifficulty.Normal;
    public static bool Ironman { get; private set; } = false;
    public static bool TutorialSeen { get; private set; } = false; // M36.2: ilk açılış rehberi görüldü mü?

    public static readonly Vector2I[] Resolutions =
    {
        new(1280, 720),
        new(1600, 900),
        new(1920, 1080)
    };

    public enum GameDifficulty
    {
        Easy,
        Normal,
        Hard
    }

    private class SaveBlob
    {
        public float MasterVolume { get; set; }
        public bool Fullscreen { get; set; }
        public int ResolutionIndex { get; set; }
        public bool VSync { get; set; }
        public int DefaultSpeed { get; set; }
        public string Difficulty { get; set; }
        public bool Ironman { get; set; }
        public bool TutorialSeen { get; set; }
    }

    public static void Load()
    {
        if (!FileAccess.FileExists(Path)) return;

        try
        {
            var blob = JsonSerializer.Deserialize<SaveBlob>(FileAccess.GetFileAsString(Path));
            if (blob == null) return;

            MasterVolume = Mathf.Clamp(blob.MasterVolume, 0f, 1f);
            Fullscreen = blob.Fullscreen;
            ResolutionIndex = Mathf.Clamp(blob.ResolutionIndex, 0, Resolutions.Length - 1);
            VSync = blob.VSync;
            DefaultSpeed = Mathf.Clamp(blob.DefaultSpeed, 0, 5);
            Difficulty = Enum.TryParse<GameDifficulty>(blob.Difficulty, out var d) ? d : GameDifficulty.Normal;
            Ironman = blob.Ironman;
            TutorialSeen = blob.TutorialSeen;
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"Settings yüklenemedi: {e.Message}");
        }
    }

    public static void Save()
    {
        var blob = new SaveBlob
        {
            MasterVolume = MasterVolume,
            Fullscreen = Fullscreen,
            ResolutionIndex = ResolutionIndex,
            VSync = VSync,
            DefaultSpeed = DefaultSpeed,
            Difficulty = Difficulty.ToString(),
            Ironman = Ironman,
            TutorialSeen = TutorialSeen
        };

        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f.StoreString(JsonSerializer.Serialize(blob));
    }

    public static void SetMasterVolume(float v01)
    {
        MasterVolume = Mathf.Clamp(v01, 0f, 1f);
        Apply();
        Save();
    }

    public static void SetFullscreen(bool on)
    {
        Fullscreen = on;
        Apply();
        Save();
    }

    public static void SetResolutionIndex(int idx)
    {
        ResolutionIndex = Mathf.Clamp(idx, 0, Resolutions.Length - 1);
        Apply();
        Save();
    }

    public static void SetVSync(bool on)
    {
        VSync = on;
        Apply();
        Save();
    }

    public static void SetDefaultSpeed(int speed)
    {
        DefaultSpeed = Mathf.Clamp(speed, 0, 5);
        Save();
    }

    public static void SetDifficulty(GameDifficulty difficulty)
    {
        Difficulty = difficulty;
        Save();
    }

    public static void SetIronman(bool on)
    {
        Ironman = on;
        Save();
    }

    public static void SetTutorialSeen(bool seen)
    {
        TutorialSeen = seen;
        Save();
        GD.Print($"SettingsSystem: TutorialSeen = {seen}");
    }

    public static void Apply()
    {
        // Ses her ortamda uygulanabilir
        float db = MasterVolume <= 0.001f ? -80f : Mathf.LinearToDb(MasterVolume);
        AudioServer.SetBusVolumeDb(0, db);

        // DÜZELTME: Godot editöründen çalıştırırken (gömülü pencere) pencereye dokunma.
        // Değerler kaydedilir; oyunu standalone açınca uygulanır.
        if (OS.HasFeature("editor"))
            return;

        var win = Engine.GetMainLoop() as SceneTree;
        if (win?.Root == null) return;
        var window = win.Root;

        if (Fullscreen)
        {
            window.Mode = Window.ModeEnum.Fullscreen;
        }
        else
        {
            window.Mode = Window.ModeEnum.Windowed;
            window.Size = Resolutions[ResolutionIndex];

            var screen = DisplayServer.ScreenGetSize();
            window.Position = (screen - Resolutions[ResolutionIndex]) / 2;
        }

        // VSync: standalone pencere için uygulanır.
        try
        {
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"VSync uygulanamadı: {e.Message}");
        }
    }

    // ===== Save/Load DTO (M13.4) =====
    public class SettingsSave
    {
        public float MasterVolume { get; set; }
        public bool Fullscreen { get; set; }
        public int ResolutionIndex { get; set; }
        public bool VSync { get; set; }
        public int DefaultSpeed { get; set; }
        public string Difficulty { get; set; }
        public bool Ironman { get; set; }
        public bool TutorialSeen { get; set; }
    }

    public static SettingsSave GetSaveData()
    {
        return new SettingsSave
        {
            MasterVolume = MasterVolume,
            Fullscreen = Fullscreen,
            ResolutionIndex = ResolutionIndex,
            VSync = VSync,
            DefaultSpeed = DefaultSpeed,
            Difficulty = Difficulty.ToString(),
            Ironman = Ironman,
            TutorialSeen = TutorialSeen
        };
    }

    public static void LoadSaveData(SettingsSave s)
    {
        if (s == null) return;

        MasterVolume = Mathf.Clamp(s.MasterVolume, 0f, 1f);
        Fullscreen = s.Fullscreen;
        ResolutionIndex = Mathf.Clamp(s.ResolutionIndex, 0, Resolutions.Length - 1);
        VSync = s.VSync;
        DefaultSpeed = Mathf.Clamp(s.DefaultSpeed, 0, 5);
        Difficulty = Enum.TryParse<GameDifficulty>(s.Difficulty, out var d) ? d : GameDifficulty.Normal;
        Ironman = s.Ironman;
        TutorialSeen = s.TutorialSeen;
    }

    public static void ResetForNewGame()
    {
        // Ayarlar kullanıcı tercihidir (ses, ekran, çözünürlük, vsync, hız, zorluk, ironman)
        // ve oyunlar arası kalır — yeni oyunda sıfırlanmaz.
        GD.Print("SettingsSystem: ResetForNewGame — kullanıcı ayarları korundu (sıfırlanmadı).");
    }
}