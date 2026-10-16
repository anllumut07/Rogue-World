using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Kütüphane/Ansiklopedi metinleri: Aktif senaryonun Data/library/*.json dosyalarından yüklenir.
/// Sayfalar senaryodan bağımsız olgusal metinler (coğrafya, kültür, dil).
/// Metni olmayan ülkeler için panel otomatik genel sayfa üretir.
/// </summary>
public static class LibrarySystem
{
    // M17.1: yol aktif senaryodan; senaryo değişince ResetForScenario cache'i temizler
    private static string CurrentDir => ScenarioSystem.ActiveLibraryDir;

    public class LibraryEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Capital { get; set; }
        public string Region { get; set; }
        public string Languages { get; set; }
        public string Intro { get; set; }
    }

    private static Dictionary<string, LibraryEntry> _byId = new();
    private static bool _loaded = false;

    public static IReadOnlyList<LibraryEntry> AllEntries
    {
        get
        {
            EnsureLoaded();
            // Id sırasına göre kararlı sıralama
            return _byId.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        }
    }

    public static int TextEntryCount
    {
        get
        {
            EnsureLoaded();
            return _byId.Count;
        }
    }

    public static LibraryEntry GetEntry(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return null;
        // Takma adları (GER→DEU vb.) normalize et
        id = CountryDatabase.NormalizeId(id);
        return _byId.TryGetValue(id, out var entry) ? entry : null;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var dir = DirAccess.Open(CurrentDir);
            if (dir == null)
            {
                GD.PrintErr($"[LibrarySystem] Dizin bulunamadı: {CurrentDir}");
                return;
            }

            dir.ListDirBegin();
            string fileName;
            int totalFiles = 0;
            while ((fileName = dir.GetNext()) != "")
            {
                if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                {
                    LoadFile($"{CurrentDir}/{fileName}");
                    totalFiles++;
                }
            }
            dir.ListDirEnd();

            GD.Print($"LibrarySystem hazır: {totalFiles} dosyadan {_byId.Count} ülke metni yüklendi (senaryo: {ScenarioSystem.ActiveId}).");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[LibrarySystem] Yükleme hatası: {e.Message}");
        }
    }

    // M17.1: Senaryo değişince cache'i temizle
    public static void ResetForScenario()
    {
        _byId.Clear();
        _loaded = false;
    }

    // M17.1: Senaryo verisi yükle (ülke gerekmez) — LogLoadCountsForAll için
    public static void LoadScenarioData()
    {
        EnsureLoaded();
    }

    private static void LoadFile(string path)
    {
        try
        {
            if (!Godot.FileAccess.FileExists(path)) return;

            var options = new JsonSerializerOptions { IncludeFields = true };
            var entries = JsonSerializer.Deserialize<List<LibraryEntry>>(Godot.FileAccess.GetFileAsString(path), options);
            if (entries == null) return;

            foreach (var e in entries)
            {
                if (e?.Id != null)
                {
                    // ID'yi normalize et (GER→DEU gibi takma adlar için)
                    string normId = CountryDatabase.NormalizeId(e.Id);
                    _byId[normId] = e;
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[LibrarySystem] Dosya okunamadı: {path} — {e.Message}");
        }
    }
}