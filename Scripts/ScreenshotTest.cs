using Godot;

// --screenshot-test: PENCERE modunda (headless DEĞİL) belirtilen sahneyi/paneli açar,
// 30 kare bekler, test-logs/screens/<ad>_<çözünürlük>.png kaydeder ve çıkış yapar.
// Kullanım: godot --path . res://Scenes/game.tscn -- --screenshot-test=map --resolution=1152x648 --country=TUR
// Bu bayrak yalnızca komut satırında çalışır; normal oyunu etkilemez.
public static class ScreenshotTest
{
    public enum ShotTarget
    {
        Map,               // ana harita (+ minimap her zaman açık)
        CharacterCreation, // karakter oluşturma paneli (M24.1 akışı)
        StartPath,         // başlangıç yolu seçimi (M24.3)
        Cabinet,           // kabine paneli (M21)
        Statistics,        // istatistik + bütçe paneli (M20)
        Election,          // seçim/parlamento paneli (M23)
        Organizations,     // uluslararası örgütler paneli (M37)
    }

    public static bool Enabled { get; private set; } = false;
    public static ShotTarget Target { get; private set; } = ShotTarget.Map;
    public static string ResolutionText { get; private set; } = "1152x648";
    public static Vector2I Resolution { get; private set; } = new Vector2I(1152, 648);

    /// Komut satırı argümanlarını ayrıştır: --screenshot-test=<hedef>, --resolution=WxH
    public static void Parse(string[] userArgs)
    {
        foreach (var arg in userArgs)
        {
            if (arg == "--screenshot-test")
            {
                Enabled = true;
            }
            else if (arg.StartsWith("--screenshot-test="))
            {
                Enabled = true;
                Target = ParseTarget(arg.Substring("--screenshot-test=".Length));
            }
            else if (arg.StartsWith("--resolution="))
            {
                ResolutionText = arg.Substring("--resolution=".Length);
                var parts = ResolutionText.Split('x', 'X');
                if (parts.Length == 2 && int.TryParse(parts[0], out var rw) && int.TryParse(parts[1], out var rh))
                    Resolution = new Vector2I(rw, rh);
                else
                    GD.PrintErr($"[ScreenshotTest] Çözünürlük ayrıştırılamadı: {ResolutionText} (varsayılan 1152x648)");
            }
        }
        if (Enabled)
        {
            GD.Print($"[ScreenshotTest] Aktif — hedef={Target}, çözünürlük={Resolution.X}x{Resolution.Y}");
        }
    }

    private static ShotTarget ParseTarget(string raw)
    {
        var key = raw.Trim().ToLowerInvariant();
        return key switch
        {
            "map" or "harita" => ShotTarget.Map,
            "character" or "character_creation" or "karakter" => ShotTarget.CharacterCreation,
            "start_path" or "startpath" => ShotTarget.StartPath,
            "cabinet" or "kabine" => ShotTarget.Cabinet,
            "statistics" or "budget" or "butce" or "istatistik" => ShotTarget.Statistics,
            "election" or "parliament" or "secim" => ShotTarget.Election,
            "organizations" or "org" or "orgutler" => ShotTarget.Organizations,
            _ => ShotTarget.Map,
        };
    }

    /// Pencereyi hedef çözünürlüğe getir (kayıtlı ayarların tam ekranını ez).
    public static void ApplyWindow()
    {
        if (!Enabled) return;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(Resolution);
        DisplayServer.WindowSetPosition(new Vector2I(80, 60));
        GD.Print($"[ScreenshotTest] Pencere: {Resolution.X}x{Resolution.Y} (windowed)");
    }

    /// PNG dosya yolu: test-logs/screens/<hedef>_<W>x<H>.png
    public static string OutputPath()
    {
        return $"test-logs/screens/{Target.ToString().ToLowerInvariant()}_{Resolution.X}x{Resolution.Y}.png";
    }
}
