Her zaman Türkçe cevap ver.
Ekran görüntüsü (PNG) okuma, modelin görüntü desteği yok.

Proje: Rogue World, HOI4 benzeri grand strateji oyunu.
Motor/dil: Godot 4 + C# (.NET).
Plan: TODO.md dosyasındaki milestone'lar, sırayla ilerle.
Kural: Bir milestone %100 çalışmadan sonrakine geçme.
Derleme kontrolü: dotnet build
Kod stili: Türkçe yorumlar, İngilizce UI metinleri, GD.Print debug logları.
Her alt görev bitince TODO.md'deki ilgili kutuyu [x] yap.
Büyük değişiklikten önce ne yapacağını kısaca açıkla.
Aynı anda tek alt görev üzerinde çalış.

Çalışma kuralları:
- Mouse davranışını bozma (sağ tık sürükleme, tekerlek, WASD, ülke seçimi). Yeni kontrollerde MouseFilter'ı bilinçli seç.
- Her alt görevden sonra dotnet build çalıştır.
- Test kodu (--focus-test vb.) normal oyunda çalışmasın.
- dotnet run ve editörü açma, git kullanma.
- PROGRESS.md ve TODO.md'yi güncel tut.
- Veri yükleyen sistemler başlangıçta kaç kayıt yüklediğini GD.Print ile logla. JSON okurken IncludeFields kontrol et.
- Paneller 1152x648'e sığsın, açıkken harita girdisini bloklasın, kapanınca geri gelsin.
- Aynı hatayı 3 kez düzeltemezsen "takıldı" yaz ve sonrakine geç.
- Her görev sonunda cevabının sonuna 10 satırı geçmeyen "ÖZET" ekle: neyi değiştirdin (dosya adlarıyla), neyi çözemedin, bilinen sorunlar, benim test etmem gerekenler.
- **Faz 2 kararları: gerçek kişi adı kullanma, rastgelelik yalnızca DeterministicRNG, her yeni sistem Save/Load'a eklenir ve doğrulama testine girer.**