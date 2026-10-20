using Godot;

// HOI4 tarzı ülke verisi: 1936 başlangıç değerleri.
public class CountryData
{
    public string Id = "?";              // ISO A3 (GeoJSON id ile eşleşir): "TUR", "DEU"...
    public string Name = "?";
    public string Capital = "—";
    public string Ideology = "Neutral";   // Democracy / Fascism / Communism / Neutral

    // Demografi
    public long Population;              // 1936 nüfus
    public long ManpowerPool;             // kullanılabilir asker havuzu

    // Ekonomi
    public int CivFactories;             // sivil fabrika
    public int MilFactories;             // askeri fabrika
    public int Dockyards;                // tersane

    // Kaynaklar (HOI4: günlük üretim birimi)
    public int Oil;
    public int Rubber;
    public int Steel;
    public int Aluminum;
    public int Tungsten;
    public int Chromium;

    // Politika
    public double PoliticalPower;        // başlangıç PP
    public double Prestige;              // uluslararası prestij (uzay programı ödülleri vb.)
    public double StabilityPercent = 60; // 0-100
    public double WarSupportPercent = 30;// 0-100
    public int ResearchSlots = 3;

    // Askeri (başlangıç ordusu — Milestone 7'de bölüklere dönüşecek)
    public int StartingDivisions = 10;   // yaklaşık tümen sayısı

    // M17.2: Teknoloji seviyesi (1-4) — kişi başı GSYİH ve savunma bütçesinden türetilir
    public int TechLevel = 1;

    // Savaş durumu (çalışma anında değişir)
    public bool IsAnnexed = false;
    public string OwnerId = "";          // ilhak edildiyse yeni sahibi

    // Veri kaynağı: true = countries_data.json (detaylı), false = deterministik fallback
    public bool IsDetailed = false;

    // --- Türetmiş yardımcılar ---
    public int TotalFactories => CivFactories + MilFactories + Dockyards;

    public long ManpowerPerDay => (long)(Population / 100_000); // günlük manpower artışı (basit model)

    // Basit GDP tahmini: Nüfus × SivilFabrika × katsayı (1936 doları civarında)
    // HOI4'te GDP ayrı alan yok; factories × population proxy'si kullanılır.
    public long EstimatedGDP => (long)(Population * CivFactories * 150);

    public string GetIdeologyColor()
    {
        return Ideology switch
        {
            "Democracy" => "#4A7FB5",
            "Fascism" => "#8B5A2B",
            "Communism" => "#B03A2E",
            _ => "#8E9AA5"
        };
    }

    // Bayrak placeholder için ideolojiye dayalı arka plan rengi (prosedürel bayrak yerine)
    public Color GetFlagColor()
    {
        return new Color(GetIdeologyColor());
    }
}