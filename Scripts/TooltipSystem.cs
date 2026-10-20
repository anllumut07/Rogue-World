using Godot;
using System.Collections.Generic;

// M13.2: Detaylı tooltip sistemi. Kontrolere hover ile stilize açıklama paneli gösterir.
//
// Kullanım:
//   TooltipSystem.Set(button, "Bu butonun detaylı açıklaması");
//   TooltipSystem.Unset(button);   // bağlantıyı kaldır
//
// Built-in Control.TooltipText'i devre dışı bırakır (iki tooltip'in aynı anda
// çıkmasını engeller). TooltipPanel, en üst CanvasLayer (Layer=200) üzerinde
// fareyle birlikte hareket eder ve fare olaylarını engellemez.
public static class TooltipSystem
{
    private static CanvasLayer _layer;
    private static TooltipPanel _tip;
    private static readonly Dictionary<Control, string> _texts = new();

    // Kontrolü tooltip ile bağla. Aynı kontrol tekrar bağlanırsa metin güncellenir.
    public static void Set(Control control, string text)
    {
        if (control == null) return;
        Ensure(control);
        if (_texts.ContainsKey(control))
        {
            _texts[control] = text;
            return;
        }
        _texts[control] = text;
        control.TooltipText = ""; // built-in'ı kapat (çakışma önleme)

        Control c = control; // lambda yakalaması için
        control.MouseEntered += () => OnEntered(c);
        control.MouseExited += () => OnExited(c);
    }

    // Bağlantıyı kaldır ve built-in tooltip'i geri yüklemeye bırak.
    public static void Unset(Control control)
    {
        if (control == null) return;
        _texts.Remove(control);
        control.TooltipText = "";
    }

    private static void Ensure(Control control)
    {
        if (_layer != null) return;

        var tree = control.GetTree();
        if (tree == null)
        {
            // Kontrol henüz ağaca dahil değil; ağaca girdiğinde tekrar dene.
            control.TreeEntered += () => Ensure(control);
            return;
        }

        var root = tree.Root;
        _layer = new CanvasLayer { Layer = 200 }; // tüm panellerin üstünde
        _tip = new TooltipPanel();
        _layer.AddChild(_tip);

        // "_Ready" sırasında "Parent node is busy" oluşabilir — ertelenmiş ekleme.
        root.CallDeferred(Node.MethodName.AddChild, _layer);
    }

    private static void OnEntered(Control c)
    {
        if (_texts.TryGetValue(c, out var text))
            _tip?.Show(text);
    }

    private static void OnExited(Control c)
    {
        _tip?.Hide();
    }
}
