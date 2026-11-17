# Performans Profilering – Stage 3 Spec

## Test Senaryoları
1. **Her panel** açılıp kapatılıp, temel etkileşim (tıklama, kaydırma, dropdown) yapılır.  
2. **5 dakikalık** `dotnet‑trace` (veya `dotnet‑counters`) izlemesi yapılır.  
3. Aynı senaryo **her bir panel** için tekrarlanır (toplam 18 panel).

## Ölçümler
- **FPS** (body‑rate)  
- **CPU kullanımı** (%)
- **Bellek kullanımı** (MB)  
- **GC (Garbage Collection) durasyonları** (ms)

## Optimizasyon Hedefleri
- **FPS ≥ 60** modern sistem, ≥ 30 düşük konfigürasyon
- **CPU kullanımı** tek panelde < 70%
- **Bellek kullanımı** artış < 50 MB/panel
- **GC durasyonu** < 50 ms, sıklık < 1/s

## Raporlama
Her panel için sonuçlar `perf-report.md` dosyasına şu formatta eklenir:
```
Panel: <Ad>
FPS: <değer> | CPU: <%> | Mem: <MB> | GC: <ms>
Notlar: ...
```
Toplu test sonrası `dotnet build` temiz, save-load testleri PASS olmalı.
