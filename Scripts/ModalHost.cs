using Godot;

// Modal paneller (karakter oluşturma, kabine, ekonomi, olay popup'ları vb.)
// HUD (GameHud: CanvasLayer Layer=10) ve diğer overlay'lerin ÜSTÜNDE çizilmeli ve
// fare olayını doğru almalı. Paneller doğrudan Game köküne (canvas katman 0) veya
// HUD'ya (CanvasLayer 10) eklendiğinde z-order/mouse sorunları oluşuyordu
// (kullanıcı raporu: "Culture kontrolüne tıklanamıyor").
// OpenUnder çağrıları bu yardımcı ile paneli kendi CanvasLayer'ına (Layer=100) sarar.
// Normal oyun akışına etkisi yoktur: yalnızca canvas katman/input sırasını düzeltir.
public static class ModalHost
{
    public const int LayerId = 100;

    /// Paneli parent altına, kendi CanvasLayer'ı (Layer=100) içinde ekler.
    public static void AddUnder(Node parent, Node panel)
    {
        if (panel == null) return;
        if (parent == null)
        {
            panel.QueueFree();
            return;
        }
        var layer = new CanvasLayer { Layer = LayerId };
        layer.AddChild(panel);
        parent.AddChild(layer);
        // Panel kapandığında kendisini sarmalayan katmanı da temizle
        panel.TreeExiting += layer.QueueFree;
    }
}
