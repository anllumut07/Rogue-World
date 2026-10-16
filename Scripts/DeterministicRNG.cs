using System;
using Godot;

/// <summary>
/// Deterministik xorshift64* RNG — tek ulong state, hızlı, tamamen yeniden üretilebilir.
/// System.Random, GD.Randf, RandomNumberGenerator yerine kullanılacak.
/// State save/load için tek bir ulong yeterli.
/// </summary>
public static class DeterministicRNG
{
    private static ulong _state;
    private static bool _initialized;

    /// <summary>RNG başlatılmış mı? (GameManager: headless test akışında lazy-init için)</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>Yeni oyun başında çağrılır: seed = tarih hash + sabit offset (her oyun farklı, ama tekrar edilebilir).</summary>
    public static void InitializeForNewGame(ulong? forcedSeed = null)
    {
        if (forcedSeed.HasValue)
        {
            _state = forcedSeed.Value;
        }
        else
        {
            // Seed: Unix epoch'tan gün sayısı + sabit — her yeni oyunda farklı, ama bilinçli
            long daysSinceEpoch = DateTime.UtcNow.Date.Subtract(new DateTime(1970, 1, 1)).Days;
            _state = (ulong)daysSinceEpoch * 0x9E3779B97F4A7C15UL + 0xBF58476D1CE4E5B9UL;
        }
        _initialized = true;
        GD.Print($"[RNG] Yeni oyun için başlatıldı: seed={_state}");
    }

    /// <summary>Kaydedilmiş state'i yükler (LoadGame içinde çağrılır).</summary>
    public static void LoadState(ulong state)
    {
        _state = state;
        _initialized = true;
        GD.Print($"[RNG] State yüklendi: {_state}");
    }

    /// <summary>Mevcut state'i döndürür (SaveGame içinde çağrılır).</summary>
    public static ulong GetState() => _state;

    /// <summary>RNG henüz başlatılmamışsa varsayılan bir seed ile başlat (testler için).</summary>
    private static void EnsureInitialized()
    {
        if (!_initialized)
        {
            _state = 0x9E3779B97F4A7C15UL; // sabit default seed
            _initialized = true;
        }
    }

    // ---- Core xorshift64* ----
    // http://xorshift.di.unimi.it/xorshift64star.c
    private static ulong NextUlong()
    {
        EnsureInitialized();
        ulong x = _state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _state = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    // ---- Public API (System.Random benzeri) ----

    /// <returns>[0, uint.MaxValue]</returns>
    public static uint NextUInt()
    {
        return (uint)(NextUlong() >> 32);
    }

    /// <returns>[0, int.MaxValue]</returns>
    public static int NextInt()
    {
        return (int)(NextUInt() & 0x7FFFFFFF);
    }

    /// <returns>[min, max) — max dahil değil</returns>
    public static int NextInt(int min, int max)
    {
        if (min >= max) return min;
        return min + NextInt() % (max - min);
    }

    /// <returns>[0.0, 1.0)</returns>
    public static float NextFloat()
    {
        // 24 bit mantissa precision yeterli
        return (NextUInt() >> 8) * (1.0f / 16777216.0f);
    }

    /// <returns>[0.0, 1.0) — double hassasiyet</returns>
    public static double NextDouble()
    {
        return (NextUlong() >> 11) * (1.0 / 9007199254740992.0); // 2^53
    }

    /// <returns>[min, max)</returns>
    public static float NextFloat(float min, float max)
    {
        return min + NextFloat() * (max - min);
    }

    /// <returns>[min, max)</returns>
    public static double NextDouble(double min, double max)
    {
        return min + NextDouble() * (max - min);
    }

    /// <summary>Beronoulli: verilen olasılıkla true döner (p in [0,1]).</summary>
    public static bool NextBool(double probability)
    {
        return NextDouble() < probability;
    }

    /// <summary>Diziden rastgele eleman seç (boşsa default(T)).</summary>
    public static T NextChoice<T>(T[] array)
    {
        if (array == null || array.Length == 0) return default;
        return array[NextInt(0, array.Length)];
    }

    /// <summary>Listeden rastgele eleman seç (boşsa default(T)).</summary>
    public static T NextChoice<T>(System.Collections.Generic.IList<T> list)
    {
        if (list == null || list.Count == 0) return default;
        return list[NextInt(0, list.Count)];
    }

    /// <summary>Weighted random: weights toplamı 1 olmak zorunda değil.</summary>
    public static int NextWeightedIndex(float[] weights)
    {
        if (weights == null || weights.Length == 0) return -1;
        float sum = 0;
        foreach (var w in weights) sum += w;
        if (sum <= 0) return 0;
        float r = NextFloat() * sum;
        float acc = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (r < acc) return i;
        }
        return weights.Length - 1;
    }

    /// <summary>Shuffle (Fisher-Yates) — diziyi yerinde karıştırır.</summary>
    public static void Shuffle<T>(T[] array)
    {
        if (array == null) return;
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = NextInt(0, i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    /// <summary>List shuffle.</summary>
    public static void Shuffle<T>(System.Collections.Generic.IList<T> list)
    {
        if (list == null) return;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = NextInt(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}