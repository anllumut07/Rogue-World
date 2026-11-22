using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Yerelleştirme Sistemi (Localization System) - TR/EN Desteği.
/// JSON dosyalarından çevirileri yükler, dil değiştirme, fallback mekanizması,
/// parametreli metinler, plural formasyonları destekler.
/// </summary>
public static class LocalizationSystem
{
    // ===== Dil Tanımları =====
    public enum Language
    {
        Turkish = 0,
        English = 1
    }

    // ===== Veri Modelleri =====
    private class LocalizationData
    {
        public Dictionary<string, string> Strings = new();
        public Dictionary<string, Dictionary<string, string>> Plurals = new();
    }

    // ===== Sabitler =====
    private const string DataPath = "res://Data/localization/";
    private const string SettingsKey = "language";

    // ===== State =====
    private static Language _currentLanguage = Language.Turkish;
    private static readonly Dictionary<Language, LocalizationData> _data = new();
    private static bool _initialized;
    private static readonly List<Action<Language>> _languageChangeListeners = new();

    // ===== Public API =====

    public static Language CurrentLanguage => _currentLanguage;
    public static string CurrentLanguageCode => _currentLanguage == Language.Turkish ? "tr" : "en";
    public static string CurrentLanguageName => _currentLanguage == Language.Turkish ? "Türkçe" : "English";
    public static bool IsInitialized => _initialized;

    /// <summary>Sistemi başlatır (dil dosyalarını yükler).</summary>
    public static void Initialize()
    {
        if (_initialized) return;

        LoadAllLanguages();
        LoadLanguagePreference();

        _initialized = true;
        GD.Print($"[LocalizationSystem] Initialized — Current: {CurrentLanguageName} ({CurrentLanguageCode})");
    }

    /// <summary>Yeni oyun için dil tercihini varsayılana sıfırlar.</summary>
    public static void ResetForNewGame()
    {
        // Dil tercihi kullanıcı ayarlarından gelir, oyun içi sıfırlanmaz
        // Sadece sistem state'i sıfırlanır
        GD.Print("[LocalizationSystem] ResetForNewGame — language preference preserved");
    }

    /// <summary>Senaryo değişince yeniden başlatır.</summary>
    public static void ResetForScenario()
    {
        // Dil dosyaları senaryoya bağlı değil, global
        GD.Print("[LocalizationSystem] ResetForScenario — languages are global");
    }

    /// <summary>Tüm dil dosyalarını yükler.</summary>
    private static void LoadAllLanguages()
    {
        var languages = Enum.GetValues<Language>();
        foreach (var lang in languages)
        {
            LoadLanguage(lang);
        }
    }

    /// <summary>Belirli bir dili yükler.</summary>
    private static void LoadLanguage(Language lang)
    {
        string langCode = lang == Language.Turkish ? "tr" : "en";
        string path = $"{DataPath}strings_{langCode}.json";

        var data = new LocalizationData();

        try
        {
            if (Godot.FileAccess.FileExists(path))
            {
                string json = Godot.FileAccess.GetFileAsString(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("strings", out var stringsElem))
                {
                    foreach (var prop in stringsElem.EnumerateObject())
                    {
                        data.Strings[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }

                if (root.TryGetProperty("plurals", out var pluralsElem))
                {
                    foreach (var prop in pluralsElem.EnumerateObject())
                    {
                        var forms = new Dictionary<string, string>();
                        foreach (var formProp in prop.Value.EnumerateObject())
                        {
                            forms[formProp.Name] = formProp.Value.GetString() ?? "";
                        }
                        data.Plurals[prop.Name] = forms;
                    }
                }

                GD.Print($"[Localization] Loaded {lang}: {data.Strings.Count} strings, {data.Plurals.Count} plurals");
            }
            else
            {
                GD.PrintErr($"[Localization] Missing file: {path}");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Localization] Failed to load {lang}: {e.Message}");
        }

        _data[lang] = data;
    }

    /// <summary>Kullanıcı dil tercihini yükler.</summary>
    private static void LoadLanguagePreference()
    {
        try
        {
            var config = new Godot.ConfigFile();
            string path = "user://settings.cfg";
            if (config.Load(path) == Godot.Error.Ok)
            {
                string langCode = config.GetValue("settings", SettingsKey, "tr").AsString();
                if (langCode == "en")
                    _currentLanguage = Language.English;
                else
                    _currentLanguage = Language.Turkish;
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Localization] Failed to load language preference: {e.Message}");
            _currentLanguage = Language.Turkish;
        }
    }

    /// <summary>Dil tercihini kaydeder.</summary>
    public static void SaveLanguagePreference()
    {
        try
        {
            var config = new Godot.ConfigFile();
            config.SetValue("settings", SettingsKey, CurrentLanguageCode);
            config.Save("user://settings.cfg");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Localization] Failed to save language preference: {e.Message}");
        }
    }

    /// <summary>Dili değiştirir.</summary>
    public static void SetLanguage(Language lang)
    {
        if (_currentLanguage == lang) return;

        _currentLanguage = lang;
        SaveLanguagePreference();
        NotifyLanguageChange();

        GD.Print($"[Localization] Language changed to: {lang} ({(lang == Language.Turkish ? "tr" : "en")})");
    }

    /// <summary>Dil değiştirme dinleyicisi ekler.</summary>
    public static void AddLanguageChangeListener(Action<Language> listener)
    {
        if (!_languageChangeListeners.Contains(listener))
            _languageChangeListeners.Add(listener);
    }

    /// <summary>Dil değiştirme dinleyicisi kaldırır.</summary>
    public static void RemoveLanguageChangeListener(Action<Language> listener)
    {
        _languageChangeListeners.Remove(listener);
    }

    private static void NotifyLanguageChange()
    {
        foreach (var listener in _languageChangeListeners)
        {
            try { listener(_currentLanguage); }
            catch (Exception e) { GD.PrintErr($"[Localization] Listener error: {e.Message}"); }
        }
    }

    // ===== Çeviri API =====

    /// <summary>Basit çeviri getirir (key yoksa key'i döner).</summary>
    public static string T(string key)
    {
        if (!_initialized) Initialize();

        if (_data.TryGetValue(_currentLanguage, out var data))
        {
            if (data.Strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;
        }

        // Fallback: English
        if (_currentLanguage != Language.English && _data.TryGetValue(Language.English, out var enData))
        {
            if (enData.Strings.TryGetValue(key, out var enValue) && !string.IsNullOrEmpty(enValue))
                return enValue;
        }

        // Fallback: key itself
        return key;
    }

    /// <summary>Parametreli çeviri getirir (string.Format uyumlu).</summary>
    public static string T(string key, params object[] args)
    {
        string template = T(key);
        if (args.Length == 0) return template;

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            GD.PrintErr($"[Localization] Format error for key '{key}': {template}");
            return template;
        }
    }

    /// <summary>Plural form getirir (sayıya göre doğru formu seçer).</summary>
    /// <param name="key">Plural key (örn. "days")</param>
    /// <param name="count">Sayı</param>
    /// <param name="args">Format argümanları (count {0} olarak geçer)</param>
    public static string TPlural(string key, long count, params object[] args)
    {
        if (!_initialized) Initialize();

        if (!_data.TryGetValue(_currentLanguage, out var data) || !data.Plurals.TryGetValue(key, out var forms))
        {
            // Fallback: English
            if (_currentLanguage != Language.English && _data.TryGetValue(Language.English, out var enData) && enData.Plurals.TryGetValue(key, out forms))
            {
                // Use English forms
            }
            else
            {
                // Ultimate fallback: key itself
                return T(key, count);
            }
        }

        string form = GetPluralForm(forms, count, _currentLanguage);
        if (args.Length > 0)
        {
            // Add count as first arg if not present
            var allArgs = new object[args.Length + 1];
            allArgs[0] = count;
            Array.Copy(args, 0, allArgs, 1, args.Length);
            try { return string.Format(form, allArgs); }
            catch { return form; }
        }
        return form;
    }

    /// <summary>Dile göre doğru plural formu seçer.</summary>
    private static string GetPluralForm(Dictionary<string, string> forms, long count, Language lang)
    {
        // Turkish: 2 forma (one, other)
        // English: 2 forma (one, other)
        // Diğer diller için genişletilebilir

        if (lang == Language.Turkish)
        {
            // Türkçe: 1 için "one", diğerleri için "other"
            return forms.TryGetValue(count == 1 ? "one" : "other", out var f) ? f : forms.Values.FirstOrDefault() ?? "";
        }
        else // English
        {
            // İngilizce: 1 için "one", diğerleri için "other"
            return forms.TryGetValue(count == 1 ? "one" : "other", out var f) ? f : forms.Values.FirstOrDefault() ?? "";
        }
    }

    /// <summary>Key var mı kontrol eder.</summary>
    public static bool HasKey(string key)
    {
        if (!_initialized) Initialize();
        return _data.TryGetValue(_currentLanguage, out var data) && data.Strings.ContainsKey(key);
    }

    /// <summary>Mevcut dildeki tüm key'leri getirir (debug/editor için).</summary>
    public static IEnumerable<string> GetAllKeys()
    {
        if (!_initialized) Initialize();
        if (_data.TryGetValue(_currentLanguage, out var data))
            return data.Strings.Keys;
        return Enumerable.Empty<string>();
    }

    // ===== Save / Load =====

    public class LocalizationSave
    {
        public int Language = 0;
    }

    public static LocalizationSave GetSaveData()
    {
        return new LocalizationSave { Language = (int)_currentLanguage };
    }

    public static void LoadSaveData(LocalizationSave data)
    {
        if (data == null) return;
        _currentLanguage = (Language)Math.Clamp(data.Language, 0, 1);
        GD.Print($"[Localization] Language loaded from save: {_currentLanguage}");
    }

    public static void ResetForNewGameStatic()
    {
        // Dil ayarı kullanıcı ayarıdır, oyun içinde sıfırlanmaz
        GD.Print("[LocalizationSystem] ResetForNewGameStatic — language preserved");
    }
}