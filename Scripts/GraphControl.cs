using Godot;
using System;
using System.Linq;

// Tek göstergenin son 60 günlük eğilim çizgisi (M20.5). İstatistik panelinde gösterilir.
public partial class GraphControl : Control
{
    private string _countryId;
    private string _indicatorId;

    private readonly Label _minLabel = new() { Text = "0" };
    private readonly Label _midLabel = new() { Text = "0" };
    private readonly Label _maxLabel = new() { Text = "0" };
    private readonly Label _valueLabel = new() { Text = "-" };

    public void SetSeries(string countryId, string indicatorId)
    {
        _countryId = countryId;
        _indicatorId = indicatorId;
        QueueRedraw();
    }

    public override void _Ready()
    {
        foreach (var l in new Label[] { _minLabel, _midLabel, _maxLabel, _valueLabel })
        {
            l.AddThemeColorOverride("font_color", UITheme.TextMuted);
            l.AddThemeFontSizeOverride("font_size", 11);
            AddChild(l);
        }
        _valueLabel.AddThemeColorOverride("font_color", UITheme.GoldBright);
    }

    public override void _Process(double delta)
    {
        // Panel açıkken değerler günlük değişir → her karede yeniden çiz (canlı grafik)
        QueueRedraw();
        var rect = GetRect();
        _minLabel.Position = new Vector2(4, rect.Size.Y - 15);
        _midLabel.Position = new Vector2(rect.Size.X / 2 - 14, rect.Size.Y - 15);
        _maxLabel.Position = new Vector2(rect.Size.X - 42, rect.Size.Y - 15);
        _valueLabel.Position = new Vector2(rect.Size.X - 110, 4);
    }

    public override void _Draw()
    {
        if (string.IsNullOrEmpty(_countryId) || string.IsNullOrEmpty(_indicatorId)) return;
        var def = StatisticsSystem.Definitions.FirstOrDefault(d => d.Id == _indicatorId);
        if (def == null) return;

        var history = StatisticsSystem.GetHistory(_countryId, _indicatorId);
        if (history == null || history.Count == 0) return;

        _minLabel.Text = $"{def.Min:N0}";
        _midLabel.Text = $"{(def.Min + def.Max) / 2:N0}";
        _maxLabel.Text = $"{def.Max:N0}";
        if (history.Count > 0)
            _valueLabel.Text = $"{def.Name}: {history[^1]:N1}";

        var rect = GetRect();
        var padX = 36f;
        var padY = 24f;
        var x0 = padX;
        var x1 = rect.Size.X - padX;
        var yTop = padY;
        var yBottom = rect.Size.Y - padY - 20f;

        // Arka plan
        DrawRect(rect, UITheme.PanelBgLight, true);

        // Çizgi: her gün bir nokta, eşit aralıklı
        int n = history.Count;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float x = n > 1 ? x0 + (float)i / (n - 1) * (x1 - x0) : (x0 + x1) / 2;
            double v = history[i];
            double t = def.Max > def.Min ? Math.Clamp((v - def.Min) / (def.Max - def.Min), 0, 1) : 0.5;
            float y = yBottom - (float)t * (yBottom - yTop);
            pts[i] = new Vector2(x, y);
        }
        DrawPolyline(pts, UITheme.Gold, 2f);

        // Son nokta vurgusu
        if (n > 0)
            DrawCircle(pts[^1], 3f, UITheme.GoldBright);
    }
}
