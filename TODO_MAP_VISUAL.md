# HOI4-Style World Map Visual Implementation — TODO

**Hedef:** Hearts of Iron 4 tarzında stratejik harita (WorldMapV4) — reference/ klasöründeki görsellerle eşleşen görsel kalite.

**Kabul Kriteri:** `map-visual-review` skill kontrol listesinde **ağırlıklı ortalama ≥ 8.0/10** ve tüm alt kriterler **≥ 7/10**.

---

## Aşama 1: Asset Pipeline ve Temel Renderer (Hafta 1-2)

### 1.1 Natural Earth Raster İndirme ve İşleme
- [ ] SR_HR.tif (shaded relief, grayscale) → `Assets/map_textures/shaded_relief.png`
- [ ] NE1_HR_LC_SR_W_DR.tif (cross-blended hypsometric + shaded relief) → `Assets/map_textures/natural_earth_1.png`
- [ ] NE2_HR_LC_SR_W_DR.tif (idealized) → `Assets/map_textures/natural_earth_2.png` (opsiyonel)
- [ ] OB_HR.tif (ocean bathymetry) → `Assets/map_textures/ocean_bathymetry.png`
- [ ] GDAL ile GeoTIFF → PNG dönüşüm, mipmaps etkin

**Kabul:** 4 doku dosyası `Assets/map_textures/` altında, Godot import edebiliyor, mipmaps çalışıyor.

### 1.2 Vektör Verisi İşleme
- [ ] `ne_10m_populated_places.shp` → `Data/map/cities_processed.json` (şehirler: başkent/major/minor, ISO3 ataması)
- [ ] `ne_10m_rivers_lake_centerlines.shp` → `Data/map/rivers.json` (nehir çizgileri)
- [ ] `ne_10m_coastline.shp` → `Data/map/coastline.json` (kıyı çizgisi)
- [ ] GeoNames `cities15000.txt` ile birleştirme ve deduplikasyon

**Kabul:** `cities_processed.json` ≥ 5000 şehir, her şehir: `Id, Name, Country, X, Y, Pop, Type, Cap, Pri, Lanchor`. `rivers.json` LineString listesi.

### 1.3 Font Yükleme
- [ ] Cinzel (Country labels) → `Assets/fonts/Cinzel-Variable.ttf`
- [ ] Oswald (Province/City/UI labels) → `Assets/fonts/Oswald-Variable.ttf`
- [ ] Cormorant (Sea labels) → `Assets/fonts/Cormorant-Variable.ttf`
- [ ] Godot FontFile import ve LoadFonts() çalışıyor

**Kabul:** 3 font yükleme hatası yok, WorldMapV4.LoadFonts() `GD.PrintErr` yazmıyor.

### 1.4 WorldMapV4 Temel Renderer
- [ ] CanvasLayer katmanları: Ocean(-2), Terrain(-1), Countries(0), Provinces(1), Rivers(2), Cities(3), Labels(4), UI(5)
- [ ] Ocean shader: batimetri + gradient + vignette
- [ ] Terrain shader: NE1 + SR_HR multiply blend (strength 0.35), saturation/contrast kontrolü
- [ ] Country fill: %55 alpha, hash tabanlı muted palet (24 renk)
- [ ] Sınır çizgileri: zoom-adaptif (1.1/zoom, clamp 0.35-3px), koyu `#0E141B`
- [ ] Kıyı glow: iç turkuaz `0.3,0.8,0.9,0.25`, 3.5px
- [ ] Kamera: pan (sağ tık/WASD), zoom (tekerlek, clamp 0.1-5.0), smooth lerp

**Kabul:** Oyun açıldığında harita render oluyor, 173 ülke yükleniyor, FPS ≥ 55 @ 1152x648.

---

## Aşama 2: Etiket Sistemi ve Çakışma Önleme (Hafta 3)

### 2.1 Ülke Etiketleri
- [ ] Label anchor = en büyük ring'in bbox merkezi
- [ ] Font: Cinzel, size = bbox genişliğine göre (min 10, max 24)
- [ ] Zoom threshold: Strategic (≥0.5) görünür
- [ ] Gölge: siyah %90, 1px offset
- [ ] Beyaz renk, outline efekti

**Kabul:** Tüm büyük ülkelerde (USA, RUS, CHN, CAN, BRA, AUS) etiket görünür, çakışma yok, zoom < 0.5'te gizlenir.

### 2.2 Deniz/Okyaanus Etiketleri
- [ ] Font: Cormorant Italic
- [ ] Renk: Açık mavi `#A6D1FF` (okyanus %85, deniz %75)
- [ ] Rotation: Deniz şeklinin uzun eksenine göre (basit: bbox aspect ratio'dan hesapla)
- [ ] Letter-spacing: 2-4px (büyük denizlerde)
- [ ] Zoom threshold: Ocean ≥ 0.3, Sea ≥ 0.7

**Kabul:** "ATLANTIC OCEAN", "PACIFIC OCEAN", "MEDITERRANEAN SEA" etiketleri okunaklı, italic, döndürülmüş.

### 2.3 Çakışma Önleme (Label Collision Avoidance)
- [ ] Force-directed veya simulated annealing algoritması
- [ ] Ülke etiketleri birbirini itir, deniz etiketlerinden kaçınır
- [ ] Max 50 iterasyon, frame'ye yayılır (anlık değil)
- [ ] Priority: Büyük ülke > Küçük ülke > Deniz

**Kabul:** `map-visual-review` Etiketler kategorisi ≥ 7/10. Hiçbir etiket üst üste binmiyor.

---

## Aşama 3: Şehir Sistemi (Hafta 4)

### 3.1 Şehir Verisi ve Sınıflandırma
- [ ] `cities_processed.json` → `Data/map/cities_runtime.json` (WorldMapV4 formatı)
- [ ] Türler: `capital` (yıldız, altın), `megacity` >10M (turuncu), `major` >1M (amber), `large` >500K (beyaz), `medium` >100K (gri), `minor` >15K (koyu gri)
- [ ] Label anchor: Şehir pozisyonundan küçük offset
- [ ] Priority: Capital=100, Megacity=80, Major=60, Large=40, Medium=20, Minor=10

**Kabul:** 173 ülkede en az 1 başkent, toplam ≥ 3000 şehir yükleniyor.

### 3.2 Şehir İşaretleyicileri (Markers)
- [ ] Procedural texture: Beyaz daire + siyah outline (64x64)
- [ ] Capital: Yıldız şekli (ayrı texture veya shader)
- [ ] Scale: Tip bazlı (capital 0.25, megacity 0.22, major 0.18, large 0.15, medium 0.12, minor 0.10)
- [ ] Zoom clamp: 0.08 - 0.5
- [ ] Visibility: Zoom ≥ 1.5 (Tactical)

**Kabul:** `map-visual-review` Şehirler kategorisi ≥ 7/10. Başkentler altın yıldız, büyükşehirler turuncu daire.

### 3.3 Şehir Etiketleri
- [ ] Font: Oswald, size: capital 12, megacity 11, major 10, large 9, medium 8, minor 7
- [ ] Visibility: Zoom ≥ 3.0 (Detail)
- [ ] Çakışma önleme: Ülke/deniz etiketlerinden kaçınma

**Kabul:** Zoom 3.0+'da başkent isimleri (Paris, London, Berlin, Moscow, Beijing, Tokyo, Washington) görünür.

---

## Aşama 4: LOD Sistemi ve Performans (Hafta 5)

### 4.1 LOD Threshold Uygulaması
| Zoom | Görünen Özellikler |
|------|-------------------|
| 0.1 - 0.5 (Strategic) | Ülke fill/border, kıyı glow, büyük ülke etiketleri, okyanus etiketleri |
| 0.5 - 1.5 (Operational) | + İl sınırları, ana nehirler, tüm ülke etiketleri, deniz etiketleri, başkentler |
| 1.5 - 3.0 (Tactical) | + Küçük nehirler, kentsel alanlar, tüm şehir işaretleri, il etiketleri |
| 3.0+ (Detail) | + Tüm şehir isimleri, tooltip, frontline/plan çizgileri |

- [ ] `_Process()` içinde zoom'a göre layer Visibility toggle
- [ ] Smooth fade (alpha lerp) threshold geçişlerinde
- [ ] Province grid spatial index hit-test için

**Kabul:** Zoom seviyeleri arasında geçiş smooth, hiçbir frame'de "pop-in" yok. `map-visual-review` LOD kategorisi ≥ 8/10.

### 4.2 Performans Optimizasyonu
- [ ] MultiMeshInstance2D für şehir işaretleri (tek draw call)
- [ ] Province borders: tek Line2D çok noktalı (batching)
- [ ] Texture mipmaps etkin, max size 4096px (tiles'e bölünmüş)
- [ ] Frame budget: < 8ms GPU, < 5ms CPU @ 1152x648

**Kabul:** `GD.Print("[PERF] FPS=...")` logunda 60 FPS stabil. Bellek < 500MB.

---

## Aşama 5: Harita Modları ve Görsel Polish (Hafta 6)

### 5.1 Harita Modları (7 mod)
| Mod | Ülke Rengi | Notlar |
|-----|-----------|--------|
| Political | Hash palet + %55 alpha | Seçili=altın, hover=açık, player=altın border |
| Terrain | Transparent | Arazi tam görünür |
| Factories | `#333333` → `#FFD700` gradient | Max fabrikaya göre normalize |
| Resources | Kaynak rengi (Oil=kahve, Steel=gri, vb.) | Yoksa transparent |
| Supply | Mavi `%45 alpha` | Supply map overlay |
| AirZones | Mor `%35 alpha` | Air zone overlay |
| Culture | Transparent | Kültür mismatch konturu (mevcut) |

- [ ] `SetMapMode()` switch, `RefreshOwnershipColors()` güncelleme
- [ ] Mod geçişinde color lerp (0.2s)

**Kabul:** 7 mod çalışıyor, `map-visual-review` Harita Modları ≥ 8/10.

### 5.2 Era/Time Efektleri
- [ ] Era tone: 1936-1938 normal, 1939-1941 kırmızımsı %4, 1942+ koyu kırmızı %7
- [ ] Vignette: Kenarlarda yumuşak karartma (radial gradient)
- [ ] Paper texture: Çok hafif kağıt dokusu overlay (opsiyonel, toggle)
- [ ] Day/night terminator: Güneş pozisyonuna göre (opsiyonel)

**Kabul:** Yıl 1940'da harita kırmızımsı ton alıyor, vignette görünür.

### 5.3 Shader Geliştirmeleri
- [ ] Coastal wave shader: Kıyıda hafif animasyonlu çizgiler
- [ ] Terrain directional light: Sol üstten hafif ışık (normal map yoksa height-based)
- [ ] Terrain saturation/contrast: UI slider ile ayarlanabilir (debug)

**Kabul:** `map-visual-review` Genel Hiss ≥ 8/10. "HoI4 oynamış gibi" hissi.

---

## Aşama 6: Entegrasyon, Test ve Doğrulama (Hafta 7)

### 6.1 Mevcut Sistemlerle Entegrasyon
- [ ] `Game.cs` --map-v4 flag ile WorldMapV4 instantiate
- [ ] Sinyaller: CountrySelected, CountryDeselected, ProvinceSelectionChanged, CitySelected
- [ ] CountryInfoPanel, MiniMapPanel, GameHud çalışıyor
- [ ] Save/Load: WorldMapV4 state (kamera pos, zoom, mod) kaydediliyor
- [ ] A==B determinizm testi geçiyor (`--save-load-test`)

**Kabul:** `dotnet build` 0 hata, `--save-load-test` PASS, tüm UI panelleri açılıyor.

### 6.2 Görsel Doğrulama (map-visual-review)
- [ ] `--screenshot-test=map` → `test-logs/screens/map_1152x648.png`
- [ ] Reference görsellerle yan yana karşılaştırma
- [ ] Skill kontrol listesi dolduruluyor
- [ ] Ağırlıklı ortalama ≥ 8.0/10
- [ ] Tüm kategoriler ≥ 7/10

**Kabul:** Review raporu `GECE-RAPOR.md`'ye ekleniyor. Skor ≥ 8.0.

### 6.3 Regresyon Testleri
- [ ] `--scenario-test` (historical_1936 + modern_world)
- [ ] `--soak-test` (10 yıl, 5x hız)
- [ ] `--balance-test` (savaş dağılımı)
- [ ] Mevcut testler bozulmamış

**Kabul:** Tüm testler exit code 0.

---

## Özet: Kabul Kriterleri Tablosu

| Aşama | Teslimat | Ölçülebilir Kriter | Doğrulama Yöntemi |
|-------|----------|-------------------|-------------------|
| 1 | Asset pipeline + temel renderer | 4 doku yüklendi, 173 ülke render, FPS≥55 | `GD.Print`, screenshot |
| 2 | Etiket sistemi | Çakışma yok, font doğru, zoom threshold | `map-visual-review` Etiketler ≥7 |
| 3 | Şehir sistemi | ≥3000 şehir, tip ayrımı, marker/label | `map-visual-review` Şehirler ≥7 |
| 4 | LOD + Performans | 4 seviye smooth, 60 FPS, <500MB | `[PERF]` log, `map-visual-review` LOD ≥8 |
| 5 | Harita modları + polish | 7 mod, era tone, vignette, shader | `map-visual-review` Modlar ≥8, Genel ≥8 |
| 6 | Entegrasyon + doğrulama | Save/Load A==B, tüm testler PASS, skor≥8.0 | `--save-load-test`, review raporu |

---

## Notlar

- **Reference görseller:** `reference/HoI4_DD_1_map.jpg`, `reference/hoi4 worldmap.jpg`, `reference/OWB_Full_Map_4.0.jpeg`
- **Her aşama sonunda:** `dotnet build` → 0 hata, screenshot al → `map-visual-review` skill ile skorla
- **Blok eden sorun:** Font yükleme hatası (Variable font desteği), texture import hatası, shader compile hatası
- **Performans hedefi:** 60 FPS @ 1152x648, RTX 3050 Laptop GPU referans
- **Telif:** HoI4 dosyası KULLANILMAZ, sadece tarz taklit edilir. Tüm asset Natural Earth (Public Domain) + GeoNames (CC BY 4.0) + Google Fonts (OFL).