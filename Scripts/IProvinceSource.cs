using System.Collections.Generic;

// M18: İl kaynağı arayüzü. Şimdilik prosedürel uygulama (ülke poligonlarını bölme),
// sonradan Natural Earth admin-1 gibi harici veri kaynağıyla değiştirilebilir.
// Tüm üretim DeterministicRNG + sabit seed ile yapılır → aynı seed her zaman aynı iller üretir.
public interface IProvinceSource
{
    /// <summary>Tüm illeri döndürür (ilk çağrıda yüklenir, sonra önbellekten).</summary>
    IReadOnlyList<ProvinceData> LoadProvinces();

    /// <summary>İl sayısı (log ve test için).</summary>
    int ProvinceCount { get; }

    /// <summary>Belirli ülkenin illerini döndürür.</summary>
    IReadOnlyList<ProvinceData> GetProvincesForCountry(string countryId);

    /// <summary>Yeni oyun / senaryo değişikliğinde state'i sıfırlar.</summary>
    void ResetForNewGame();
}
