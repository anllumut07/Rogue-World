using System;
using Godot;

/// <summary>
/// M46.4: Harita görünüm durumu (mod + kültür vurgusu).
/// WorldMap düğümünden bağımsız yaşar: ana menüde bir kayıt yüklenip oyun
/// sahnesi kurulduğunda görünümün doğru modda açılması için hafızada bekler.
/// WorldMap her değişiklikte buraya yazar, kayıt buradan okunur.
/// </summary>
public static class MapViewSystem
{
    private static WorldMap.MapMode _mode = WorldMap.MapMode.Political;
    private static string _cultureHighlight = "";

    public static WorldMap.MapMode Mode => _mode;
    public static string CultureHighlight => _cultureHighlight;

    /// <summary>Harita modunu kaydeder. Tanımsız bir enum değeri dışarıda bırakılır.</summary>
    public static void SetMode(WorldMap.MapMode mode)
    {
        if (!Enum.IsDefined(typeof(WorldMap.MapMode), mode)) return;
        if (_mode == mode) return;
        _mode = mode;
    }

    /// <summary>Vurgulanan kültürü kaydeder (null/boş = vurgu yok).</summary>
    public static void SetCultureHighlight(string culture)
    {
        string next = culture ?? "";
        if (_cultureHighlight == next) return;
        _cultureHighlight = next;
    }

    public static SaveSystem.MapViewSave GetSaveData()
    {
        return new SaveSystem.MapViewSave
        {
            Mode = (int)_mode,
            CultureHighlight = _cultureHighlight,
        };
    }

    public static void LoadSaveData(SaveSystem.MapViewSave data)
    {
        if (data == null) return;
        _mode = Enum.IsDefined(typeof(WorldMap.MapMode), data.Mode)
            ? (WorldMap.MapMode)data.Mode
            : WorldMap.MapMode.Political;
        _cultureHighlight = data.CultureHighlight ?? "";
        GD.Print($"[MapView] Harita görünümü yüklendi: mod={_mode}, " +
                 $"vurgu={(_cultureHighlight.Length > 0 ? _cultureHighlight : "(yok)")}");
    }

    /// <summary>Yeni oyun/ana menü: görünümü başlangıç değerine döner.</summary>
    public static void Reset()
    {
        _mode = WorldMap.MapMode.Political;
        _cultureHighlight = "";
    }
}
