using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Bayrak kütüphanesi (M-Flags).
/// - res://Assets/flags/{ISO3}.png dosyalarını önbellekli yükler.
/// - Dosya yoksa prosedürel yedek bayrak üretir: 3:2 dikdörtgen, ülke rengi,
///   ortada 3 harfli kod, ince kenarlık. Hiçbir koşulda null dönmez.
/// - PNG'ler 160px genişliktir; gösterim FlagBox üzerinden max 160px'e sığar.
/// - Not: PNG'ler editör import'u olmadan da yüklenebilsin diye önce
///   ResourceLoader, olmazsa Image.LoadFromFile (doğrudan disk) denenir.
/// </summary>
public static class FlagLibrary
{
    private const int MaxFlagWidth = 160;   // gösterim üst sınırı (FlagBox uygular)

    private static readonly Dictionary<string, Texture2D> _cache = new();
    private static readonly HashSet<string> _pngLoaded = new();   // PNG'den yüklenenler
    private static readonly HashSet<string> _fallbackUsed = new(); // yedek bayrak kullananlar
    private static bool _reportLogged = false;

    /// <summary>
    /// Ülke için bayrak Texture2D döner. Önbellekli, asla null değil.
    /// </summary>
    public static Texture2D GetFlag(string countryId)
    {
        string id = NormalizeId(countryId);

        if (_cache.TryGetValue(id, out var cached))
            return cached;

        Texture2D tex = TryLoadPng(id);
        if (tex != null)
        {
            _pngLoaded.Add(id);
        }
        else
        {
            tex = GenerateFallbackFlag(id);
            _fallbackUsed.Add(id);
        }

        _cache[id] = tex;
        return tex;
    }

    /// <summary>
    /// id'yi ISO A3'e normalize eder: 3 karakter, büyük harf.
    /// "tur" / "TUR" / "TUR " → "TUR"; bozuk/gereksiz → "???".
    /// </summary>
    public static string NormalizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "???";
        string upper = id.Trim().ToUpperInvariant();

        // ISO A3 bilinen kısaltmalar: HOI4 kodları (GER, SOV...) ISO A3'e çevrilir
        upper = MapHoi4ToIso(upper);

        var chars = new char[3];
        int j = 0;
        foreach (char c in upper)
        {
            if (char.IsLetterOrDigit(c) && j < 3)
                chars[j++] = c;
        }
        while (j < 3) chars[j++] = '?';
        return new string(chars);
    }

    // HOI4 3 harfli kodları → ISO A3 (Assets/flags dosya adlarıyla eşleşir)
    private static readonly Dictionary<string, string> Hoi4ToIso = new()
    {
        ["GER"] = "DEU", ["SOV"] = "RUS", ["USA"] = "USA", ["ENG"] = "GBR",
        ["FRA"] = "FRA", ["TUR"] = "TUR", ["ITA"] = "ITA", ["JAP"] = "JPN",
        ["CHI"] = "CHN", ["SAU"] = "SAU", ["EGY"] = "EGY", ["IND"] = "IND",
        ["IRN"] = "IRN", ["ISR"] = "ISR", ["BRA"] = "BRA"
    };

    private static string MapHoi4ToIso(string upper)
    {
        return Hoi4ToIso.TryGetValue(upper, out var iso) ? iso : upper;
    }

    /// <summary>PNG'den bayrak yükler: önce ResourceLoader (import edilmişse), sonra doğrudan disk.</summary>
    private static Texture2D TryLoadPng(string id)
    {
        string resPath = $"res://Assets/flags/{id.ToLowerInvariant()}.png";

        // 1) Editör import'u varsa ResourceLoader çalışır
        if (ResourceLoader.Exists(resPath))
        {
            try
            {
                var tex = ResourceLoader.Load<Texture2D>(resPath);
                if (tex != null) return tex;
            }
            catch (Exception e)
            {
                GD.PrintErr($"[FlagLibrary] ResourceLoader hatası ({id}): {e.Message}");
            }
        }

        // 2) Import yoksa doğrudan diskten oku (headless test + editör taramadan önce)
        try
        {
            string absPath = ProjectSettings.GlobalizePath(resPath);
            if (!string.IsNullOrEmpty(absPath) && System.IO.File.Exists(absPath))
            {
                var img = Image.LoadFromFile(absPath);
                if (img != null && img.GetWidth() > 0)
                {
                    return ImageTexture.CreateFromImage(img);
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[FlagLibrary] Disk okuma hatası ({id}): {e.Message}");
        }

        return null;
    }

    /// <summary>
    /// Prosedürel yedek bayrak: 3:2 (120x80) dikdörtgen, ülke rengi zemin,
    /// ortada 3 harfli kod (5x7 bitmap font, 2x ölçek), 1px koyu kenarlık.
    /// </summary>
    private static Texture2D GenerateFallbackFlag(string id)
    {
        const int w = 120, h = 80; // 3:2

        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(GetCountryColor(id));

        // 1px koyu kenarlık
        var border = new Color(0.15f, 0.15f, 0.15f, 1f);
        for (int x = 0; x < w; x++) { img.SetPixel(x, 0, border); img.SetPixel(x, h - 1, border); }
        for (int y = 0; y < h; y++) { img.SetPixel(0, y, border); img.SetPixel(w - 1, y, border); }

        DrawCode(img, id, w / 2, h / 2);

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Ülke rengi: CountryData ideoloji rengi, yoksa nötr gri.</summary>
    private static Color GetCountryColor(string id)
    {
        var data = CountryDatabase.Get(id);
        if (data != null) return data.GetFlagColor();
        // Detaylı verisi olmayan ülke: id'den deterministik ton üret
        int hash = id.GetHashCode();
        float r = 0.35f + ((hash & 0xFF) / 255f) * 0.4f;
        float g = 0.35f + (((hash >> 8) & 0xFF) / 255f) * 0.4f;
        float b = 0.35f + (((hash >> 16) & 0xFF) / 255f) * 0.4f;
        return new Color(r, g, b);
    }

    // ===== 5x7 bitmap font (string pattern'lar — doğrulanabilir) =====

    private static readonly Dictionary<char, string[]> Font5x7 = new()
    {
        ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
        ['C'] = new[] { "01110", "10001", "10000", "10000", "10000", "10001", "01110" },
        ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
        ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
        ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
        ['G'] = new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01110" },
        ['H'] = new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['I'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" },
        ['J'] = new[] { "00111", "00010", "00010", "00010", "00010", "10010", "01100" },
        ['K'] = new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
        ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
        ['M'] = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
        ['N'] = new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" },
        ['O'] = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
        ['Q'] = new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" },
        ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
        ['S'] = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
        ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
        ['U'] = new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['V'] = new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
        ['W'] = new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" },
        ['X'] = new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
        ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
        ['Z'] = new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" },
        ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
        ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
        ['2'] = new[] { "01110", "10001", "00001", "00110", "01000", "10000", "11111" },
        ['3'] = new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
        ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
        ['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
        ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
        ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
        ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
        ['?'] = new[] { "01110", "10001", "00010", "00100", "00100", "00000", "00100" },
    };

    /// <summary>3 harfli kodu görüntünün ortasına çizer (2x ölçek, gölge efekti).</summary>
    private static void DrawCode(Image img, string code, int cx, int cy)
    {
        if (string.IsNullOrEmpty(code)) return;

        const int scale = 2;
        const int glyphW = 5, glyphH = 7;
        int spacing = 1;
        int totalW = code.Length * glyphW * scale + (code.Length - 1) * spacing * scale;
        int startX = cx - totalW / 2;
        int startY = cy - glyphH * scale / 2;

        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (!Font5x7.TryGetValue(c, out var pattern)) continue;

            int gx = startX + i * (glyphW * scale + spacing * scale);

            // Gölge (1px sağ-alta siyah), sonra beyaz glif
            DrawGlyph(img, pattern, gx + scale, startY + scale, scale, new Color(0f, 0f, 0f, 0.75f));
            DrawGlyph(img, pattern, gx, startY, scale, Colors.White);
        }
    }

    private static void DrawGlyph(Image img, string[] pattern, int x, int y, int scale, Color color)
    {
        for (int row = 0; row < pattern.Length; row++)
        {
            string line = pattern[row];
            for (int col = 0; col < line.Length; col++)
            {
                if (line[col] != '1') continue;
                // scale*scale blok doldur
                for (int dy = 0; dy < scale; dy++)
                {
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int px = x + col * scale + dx;
                        int py = y + row * scale + dy;
                        if (px >= 0 && px < img.GetWidth() && py >= 0 && py < img.GetHeight())
                            img.SetPixel(px, py, color);
                    }
                }
            }
        }
    }

    // ===== Rapor / ömür yönetimi =====

    /// <summary>
    /// Oyun başında çağrılır: tüm ülkeleri tarar, PNG bulunanları ve
    /// yedek bayrak kullanacakları tek satır loglar. Bir kez çalışır.
    /// </summary>
    public static void LogReportOnce()
    {
        if (_reportLogged) return;
        _reportLogged = true;

        var all = CountryDatabase.All().ToList();
        foreach (var c in all) GetFlag(c.Id); // önbelleği ısıt

        var png = all.Where(c => _pngLoaded.Contains(NormalizeId(c.Id))).Select(c => c.Id).ToList();
        var fb = all.Where(c => _fallbackUsed.Contains(NormalizeId(c.Id))).Select(c => c.Id).ToList();

        GD.Print($"[FlagLibrary] Bayrak raporu: {png.Count}/{all.Count} PNG bulundu → {string.Join(", ", png)}");
        GD.Print($"[FlagLibrary] Yedek bayrak (prosedürel): {fb.Count} ülke → {string.Join(", ", fb)}");
    }

    /// <summary>Yeni oyun: önbellek temizlenir (rapor logu tekrar çalışabilir).</summary>
    public static void ResetForNewGame()
    {
        _cache.Clear();
        _pngLoaded.Clear();
        _fallbackUsed.Clear();
        _reportLogged = false;
    }

    /// <summary>İstatistik: (PNG sayısı, yedek sayısı).</summary>
    public static (int PngCount, int FallbackCount) GetStats()
        => (_pngLoaded.Count, _fallbackUsed.Count);
}