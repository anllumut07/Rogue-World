using Godot;

// HOI4 tarzı ülke verisi: 1936 başlangıç değerleri.
public class CountryData
{
    public string Id = "?";              // ISO A3 (GeoJSON id ile eşleşir): "TUR", "DEU"...
    public string Name = "?";
    public string Capital = "—";
    public string Ideology = "Neutral";   // Democracy / Fascism / Communism / Neutral
    public string RegimeType = "Republic"; // Republic, Monarchy, Theocracy, Military Junta, Single Party, Federal Republic

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
    public double Legitimacy = 70;       // meşruiyet/yasallık (0-100)
    public double TrustInGovernment = 50; // hükümete güven (0-100)
    public double Polarization = 30;     // toplumsal kutuplaşma (0-100)
    public double CorruptionLevel = 20;  // yolsuzluk seviyesi (0-100)
    public double InflationRate = 2;     // enflasyon % (yıllık)
    public double UnemploymentRate = 5;  // işsizlik %

    // Sosyal
    public double HealthcareCapacity = 70; // sağlık sistemi kapasitesi (0-100)
    public double EducationLevel = 60;    // eğitim seviyesi (0-100)

    // Askeri (başlangıç ordusu — Milestone 7'de bölüklere dönüşecek)
    public int StartingDivisions = 10;   // yaklaşık tümen sayısı

    // M17.2: Teknoloji seviyesi (1-4) — kişi başı GSYİH ve savunma bütçesinden türetilir
    public int TechLevel = 1;
    public int IntelligenceLevel = 50;    // istihbarat yeteneği (0-100)
    public int CyberLevel = 30;           // siber yeteneği (0-100)
    public int CyberDefense = 40;         // siber savunma (0-100)
    public int CounterIntelLevel = 30;    // karşı istihbarat (0-100)

    // Nükleer
    public int NuclearProgramProgress = 0; // nükleer program ilerlemesi (0-100)
    public int NuclearSafetyIncidents = 0; // nükleer güvenlik olayları

    // Seçim/İç politika
    public int ElectionInterferenceLevel = 0; // seçim müdahalesi seviyesi (0-100)
    public string LeaderName = "";         // lider adı (LeaderSystem'den)

    // Altyapı
    public int InfrastructureLevel = 50;   // altyapı seviyesi (0-100)

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