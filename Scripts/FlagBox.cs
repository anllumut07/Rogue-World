using Godot;

/// <summary>
/// Yeniden kullanılabilir bayrak kutusu (M-Flags).
/// - FlagLibrary'den önbellekli Texture2D alır.
/// - Oranı koruyarak kutuya sığdırır (letterbox), ince (1px) koyu çerçeve çizer.
/// - 160px genişlikten büyük çizmez.
/// - MouseFilter.Ignore: dekoratif, harita tıklamalarını yutmaz.
/// - Kullanım: new FlagBox { CustomMinimumSize = (42,28) } + SetCountry("TUR")
/// </summary>
public partial class FlagBox : Control
{
    private string _countryId = "";
    private Texture2D _texture;

    public FlagBox()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore; // dekoratif: tıklamayı yutmasın
        // Bayraklar küçük gösterilir: linear filter yumuşatma, mipmap yok
        TextureFilter = CanvasItem.TextureFilterEnum.Linear;
    }

    /// <summary>Ülke kodunu ayarlar, bayrağı önbellekten yükler ve yeniden çizer.</summary>
    public void SetCountry(string countryId)
    {
        _countryId = countryId;
        _texture = FlagLibrary.GetFlag(_countryId);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_texture == null) return;

        var box = Size;
        if (box.X < 1 || box.Y < 1) return;

        // Orana göre sığdır: flag'in en uzun kenarı kutu içine sığar
        float texW = _texture.GetWidth();
        float texH = _texture.GetHeight();
        if (texW < 1 || texH < 1) return;

        float fitScale = System.Math.Min(box.X / texW, box.Y / texH);
        float drawW = texW * fitScale;
        float drawH = texH * fitScale;

        // 160px üst sınırı: en fazla 160 genişlikte çiz
        const float maxW = 160f;
        if (drawW > maxW)
        {
            float shrink = maxW / drawW;
            drawW *= shrink;
            drawH *= shrink;
        }

        // Ortala
        var pos = new Vector2((box.X - drawW) / 2f, (box.Y - drawH) / 2f);
        var dest = new Rect2(pos, new Vector2(drawW, drawH));

        DrawTextureRect(_texture, dest, tile: false);

        // İnce çerçeve (1px koyu)
        DrawRect(dest, new Color(0.1f, 0.1f, 0.1f, 0.9f), filled: false, width: 1f);
    }
}