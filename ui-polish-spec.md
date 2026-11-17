# UI Polishing – Stage 3 Spec

## Kontroller
- [ ] Tüm 18 panel GameTheme bileşenleriyle güncellenmiş mi?
- [ ] UI responsive layout (100 %‑200 % DPI, farklı ekran oranları) test edildi mi?
- [ ] Görsel hata düzeltmeleri (misalign, overlap, eksik state) uygulanmış mı?
- [ ] `dotnet build` 0 error / 0 warning mı?
- [ ] `dotnet run --project <project> -- --scenario=modern_world` sonrası UI görsel olarak doğru görünüyor mu?

## Görev Açıklaması
1. **GameTheme** bileşenleri (GamePanel, Chip, TooltipBuilder, EmptyState, OptionButton, ProgressBar, GraphControl, vs.) kullanarak UI’yi iyileştirin.  
2. **Responsive layout**: panellerin farklı ekran boyutları ve DPI’da doğru görünmesi gerekir.  
3. **Görsel hata düzeltmeleri**: çizgisel hizalar, çakışma, eksik ikon/metin gibi sorunları giderin.  
4. **İyileştirme sonrası** her paneli **dotnet build** → **save‑load test** → **smoke UI test** ile doğrulayın.

## Not
- Yeni görseller, ikon veya animasyon dosyaları `Assets\` klasörüne eklenirse, bunları `GameTheme` stiline göre referans alın.  
- Değişiklikler yaptıktan sonra **git commit** ve **push** yapmayı unutmayın.
