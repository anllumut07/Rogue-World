using Godot;
using System;

// M13.1: Ses sistemi — menü müziği, oyun müziği, UI tık, savaş/barış/olay sesleri.
// Ses dosyası yok → AudioStreamGenerator ile prosedürel sinüs tonu üretilir.
// Volume SettingsSystem.MasterVolume üzerinden kontrol edilir (zaten kaydediliyor).
// Godot 4 notları:
//  - AudioStreamPlayer'da "Looping" özelliği yok; döngü akış kaynağında tanımlanır.
//  - GetStreamPlayback() yalnızca player oynatılırken (Play sonrası) geçerlidir.
//  - ClearBuffer() oynatım sırasında hata verir; her oynatma için yeni (boş) generator kullanılır.
public static class AudioSystem
{
    private static AudioStreamPlayer _musicPlayer;
    private static AudioStreamPlayer _sfxPlayer;
    private static bool _initialized;

    public static void Initialize(Node parent)
    {
        if (_initialized) return;

        _musicPlayer = new AudioStreamPlayer { Name = "AudioMusic", Bus = "Master" };
        _sfxPlayer = new AudioStreamPlayer { Name = "AudioSfx", Bus = "Master" };
        parent.AddChild(_musicPlayer);
        parent.AddChild(_sfxPlayer);

        SetVolume(SettingsSystem.MasterVolume);
        _initialized = true;
        GD.Print("AudioSystem: başlatıldı — prosedürel ses (menü/oyun müziği, UI tık, savaş/barış/olay)");
    }

    public static void PlayMenuMusic()
    {
        if (!_initialized || _musicPlayer == null) return;
        PlayToneBuffer(_musicPlayer, 220f, 0.06f, 10f); // derin ambient, 10 sn
        GD.Print("[Audio] Menü müziği çalıyor");
    }

    public static void PlayGameMusic()
    {
        if (!_initialized || _musicPlayer == null) return;
        PlayToneBuffer(_musicPlayer, 330f, 0.05f, 10f); // hafifçe daha yüksek, 10 sn
        GD.Print("[Audio] Oyun müziği çalıyor");
    }

    public static void StopMusic()
    {
        if (!_initialized || _musicPlayer == null) return;
        _musicPlayer.Stop();
        GD.Print("[Audio] Müzik durduruldu");
    }

    public static void PlayUIClick()
    {
        if (!_initialized || _sfxPlayer == null) return;
        PlayToneBuffer(_sfxPlayer, 880f, 0.12f, 0.15f); // kısa tepe ton
    }

    public static void PlayWarSound()
    {
        if (!_initialized || _sfxPlayer == null) return;
        PlayToneBuffer(_sfxPlayer, 220f, 0.15f, 0.5f); // düşük uyarı tonu
    }

    public static void PlayPeaceSound()
    {
        if (!_initialized || _sfxPlayer == null) return;
        PlayToneBuffer(_sfxPlayer, 660f, 0.10f, 0.3f); // hafif çözüm tonu
    }

    public static void PlayEventSound()
    {
        if (!_initialized || _sfxPlayer == null) return;
        PlayToneBuffer(_sfxPlayer, 440f, 0.08f, 0.3f); // olay bildirim tonu
    }

    public static void SetVolume(float v01)
    {
        if (!_initialized) return;
        float db = v01 <= 0.001f ? -80f : Mathf.LinearToDb(v01);
        AudioServer.SetBusVolumeDb(0, db);
        GD.Print($"[Audio] Ses seviyesi: {v01:P0}");
    }

    // Tek seferlik sinüs tonu oynatır: yeni (boş) generator ile, buffer'ı doldurup çalar.
    private static void PlayToneBuffer(AudioStreamPlayer player, float freqHz, float vol, float durationSec)
    {
        player.Stop();
        var gen = new AudioStreamGenerator();
        player.Stream = gen;
        player.Play(); // GetStreamPlayback() yalnızca oynatılan player'da geçerlidir
        var playback = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
        int sampleRate = 44100;
        int frames = (int)(sampleRate * durationSec);
        var pcm = new Vector2[frames];
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)sampleRate;
            float env = 1f - i / (float)frames; // fade-out
            float s = Mathf.Sin(t * freqHz * 2f * Mathf.Pi) * vol * env;
            pcm[i] = new Vector2(s, s);
        }
        playback.PushBuffer(pcm);
    }

    public static void ResetForNewGame()
    {
        if (!_initialized) return;
        StopMusic();
        GD.Print("[Audio] Yeni oyun için sıfırlandı");
    }
}
