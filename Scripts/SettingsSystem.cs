using Godot;
using System.Text.Json;

// Oyun ayarları: diskte user://settings.json'da tutulur, açılışta otomatik yüklenir.
public static class SettingsSystem
{
    private const string Path = "user://settings.json";

    public static float MasterVolume { get; private set; } = 0.8f;
    public static bool Fullscreen { get; private set; } = false;
    public static int ResolutionIndex { get; private set; } = 0;

    public static readonly Vector2I[] Resolutions =
    {
        new(1280, 720),
        new(1600, 900),
        new(1920, 1080)
    };

    private class SaveBlob
    {
        public float MasterVolume { get; set; }
        public bool Fullscreen { get; set; }
        public int ResolutionIndex { get; set; }
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
            ResolutionIndex = ResolutionIndex
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
    }
}