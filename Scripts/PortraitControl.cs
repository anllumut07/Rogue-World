using Godot;

// M24.1: Katmanlı prosedürel portre önizleme — yüz, saç, göz, kıyafet.
// Tüm renkler ve boyutlar Character verisinden deterministik türetilir (external asset yok).
public partial class PortraitControl : Control
{
    private Character _character;

    public Character Character
    {
        get => _character;
        set
        {
            _character = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_character == null) return;

        var rect = GetGlobalRect();
        float cx = rect.Size.X / 2f;
        float cy = rect.Size.Y / 2f;
        float faceRadius = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.32f;

        // Katman sırası (en alttan en üste): gövde/kıyafet → omuz → boyun → yüz → saç → göz

        // 1) Kıyafet (gövde)
        var clothingColor = HtmlToColor(_character.ClothingStyle, new Color("#4A5A6A"));
        DrawRect(new Rect2(cx - faceRadius * 1.5f, cy + faceRadius * 0.5f, faceRadius * 3f, faceRadius * 1.6f),
            clothingColor, true);

        // 2) Boyun
        DrawRect(new Rect2(cx - faceRadius * 0.22f, cy - faceRadius * 0.15f, faceRadius * 0.44f, faceRadius * 0.4f),
            HtmlToColor(_character.SkinTone, new Color("#E0AC69")), true);

        // 3) Yüz (oval)
        DrawCircle(new Vector2(cx, cy), faceRadius, HtmlToColor(_character.SkinTone, new Color("#E0AC69")));

        // 4) Saç (yüzü kapanacak şekilde üstteki kısım)
        var hairColor = HtmlToColor(_character.HairColor, new Color("#4A3A2A"));
        float hairTop = cy - faceRadius;
        float hairBottom = cy + faceRadius * 0.35f;
        DrawRect(new Rect2(cx - faceRadius, hairTop, faceRadius * 2f, hairBottom - hairTop), hairColor, true);
        // Saçın alt kısmını yüz rengiyle maskele (yüz ovalinin altı görünür gibi)
        DrawCircle(new Vector2(cx, cy + faceRadius * 0.1f), faceRadius * 0.95f,
            HtmlToColor(_character.SkinTone, new Color("#E0AC69")));

        // 5) Gözler (iki küçük oval)
        float eyeY = cy - faceRadius * 0.2f;
        float eyeX = faceRadius * 0.35f;
        float eyeSize = faceRadius * 0.12f;
        DrawCircle(new Vector2(cx - eyeX, eyeY), eyeSize, Colors.Black);
        DrawCircle(new Vector2(cx + eyeX, eyeY), eyeSize, Colors.Black);

        // 6) Burnu (üçgen basit nokta)
        DrawCircle(new Vector2(cx, cy + faceRadius * 0.05f), faceRadius * 0.06f,
            HtmlToColor(_character.SkinTone, new Color("#C18F63")));

        // 7) Ağız
        DrawRect(new Rect2(cx - faceRadius * 0.1f, cy + faceRadius * 0.22f, faceRadius * 0.2f, faceRadius * 0.05f),
            HtmlToColor(_character.SkinTone, new Color("#8A5A4A")), true);
    }

    private static Color HtmlToColor(string style, Color fallback)
    {
        return style switch
        {
            "Formal" => new Color("#2A3A4A"),
            "Casual" => new Color("#5A4A3A"),
            "Uniform" => new Color("#1A2A3A"),
            "Academic" => new Color("#3A3A4A"),
            "Activist" => new Color("#4A3A2A"),
            _ => fallback
        };
    }
}
