# QA Spec – Stage 3

## Amaç
Stage 3 UI Polish ve Performans iyileştirmelerinin regresyon yaratmadığını doğrulamak.

## Test Kapsamı
- 18 panelin tamamı GameTheme bileşenleriyle doğru render ediliyor mu?
- Responsive layout: 100%, 150%, 200% DPI ve 16:9 / 16:10 / 21:9 ekran oranları
- Save/Load test: historical_1936 ve modern_world senaryoları
- Etkileşim testleri: tıklama, kaydırma, dropdown, tooltip, empty state

## Test Senaryoları
1. **Build Test**
   - `dotnet build` → 0 error / 0 warning
2. **Save-Load Test**
   - `--save-load-test historical_1936` → PASS A==B
   - `--save-load-test --scenario=modern_world` → PASS A==B
3. **Regression Test Suite**
   - --climate-test, --migration-test, --espionage-mission-test
   - --recognition-test, --occupation-test, --civil-war-test
   - --scenario-test, --decisions-test, --advisor-test
4. **UI Smoke Test**
   - Her panel açılır, temel akış çalışır, hata/overlap yok

## Kabul Kriterleri
- Tüm testler PASS
- GECE-RAPOR.md ve PROJECT_STATE.md güncellenmiş
- Git commit ve push yapılmış

## Raporlama
Sonuçlar `QA_REPORT.md` dosyasına panel bazında eklenir.
