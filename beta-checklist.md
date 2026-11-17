# Beta Checklist – Stage 3 Release

## Kod Kalitesi
- [ ] `dotnet build` temiz, 0 error / 0 warning
- [ ] Tüm 18 panel GameTheme bileşenleriyle güncellenmiş
- [ ] Kod inceleme tamam, ADR’ler dokümante edilmiş

## Testler
- [ ] Save-Load testler PASS (historical_1936, modern_world)
- [ ] Tüm regression testleri PASS
- [ ] Performans profili: FPS ≥ 60 / CPU < 70% / Mem artışı kabul edilebilir
- [ ] UI responsive testleri geçti

## Dokümantasyon
- [ ] GECE-RAPOR.md güncellendi
- [ ] PROJECT_STATE.md Stage 3 bölümü tamamlandı
- [ ] ui-polish-spec.md, perf-spec.md, qa-spec.md güncellendi

## Git
- [ ] Branch `gece-calisma` üzerinde değişiklikler commitlendi
- [ ] Push yapıldı, CI yeşil
- [ ] Release tag oluşturuldu: `v3.0-beta-stage3`

## Dağıtım
- [ ] Beta kullanıcılarına bildirim gönderildi
- [ ] Geri bildirim toplama kanalı açıldı

Not: Tüm maddeler işaretlenmeden beta yayını yapılmaz.
