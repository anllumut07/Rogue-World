# M32.1 — Arayüz Stil Rehberi (UI Style Guide)

## Genel Kapsam
Bu rehber, Rogue World UI'sının tek temasını tanımlar. Tüm paneller (HUD, CountryInfoPanel, EconomyPanel, ResearchPanel, FocusPanel, EventPopup, DecisionPanel, AgendaPanel, LoadGamePanel, CabinetPanel, vb.) bu temayı kullanır. Telifli görsel kullanılmaz; tüm görünüm kod ile (Godot `Theme` + `StyleBoxFlat` + Tween animasyonları) üretilir.

Kaynak kod: `Scripts/UITheme.cs` — tek `Theme` kaynağı, `ApplyTheme(Control)` ile her sahnenin kök `Control`'üne uygulanır ve ağaç boyunca çocuklara yayılır.

## Görsel Yön (HOI4 + CK3 + AoH3 karışımı)
- **HOI4**: koyu strateji havası, ciddi tonlar, ince çerçeveler, bilgi yoğun paneller.
- **CK3**: sıcak/altın detaylar, vurgu rengi olarak altın, pulse ve kenarlık vurgusu.
- **Age of History 3**: temiz harita, sade paneller, gereksiz dekorasyon yok.

## Renk Paleti
| Değer | Hex | Kullanım |
|-------|-----|----------|
| `PanelBg` | `#0B1018` | Panel arka planı (ana) |
| `PanelBgLight` | `#121A26` | Hover arka planı |
| `PanelBgPress` | `#1A2636` | Basılmış arka planı |
| `Gold` | `#C9A227` | Vurgu / çerçeve (normal) |
| `GoldBright` | `#FFD700` | Seçili / hover (canlı altın) |
| `TextMain` | `#E8E0D0` | Ana metin |
| `TextMuted` | `#9AA3AC` | İkincil metin |
| `TextDisabled` | `#5A636D` | Devre dışı metin |
| `BorderDark` | `#1E2A3A` | Ince kenarlık |
| `InputBg` | `#0A121A` | Input arka planı |

## Yazı Tipleri
- Tüm stiller tek bir varsayılan Godot fontunu kullanır (ek dosya gerekmez).
- Lisans uygun, OFL (Open Font License) bir font tercih edilmelidir (ör. DejaVu Sans, Carlito ya da benzeri). Harici font dosyası eklenmez; proje fontu varsayılan kalır.
- Boyut: `16` (Label, Button, LineEdit, TextEdit, CheckBox, OptionButton için aynı).

## Stil Kutuları (StyleBoxFlat)
### Button (ana buton)
- `normal`: `PanelBg` + `BorderDark` kenarlık (1px, köşe 4)
- `hover`: `PanelBgLight` + `Gold` kenarlık (1px, köşe 4)
- `pressed`: `PanelBgPress` + `GoldBright` kenarlık (1px, köşe 4)
- `disabled`: koyu gri arka plan + `#2A3542` kenarlık
- `focus`: şeffaf arka plan + `GoldBright` kenarlık (0px)

### ToolbarButton (küçük toggle, HUD hız butonları gibi)
- Köşe yarıçapı 3, dar iç boşluk (8px yatay / 4px dikey)
- Renkleri Button ile aynı, border 1px

### PanelContainer
- `panel`: `PanelBg` + `Gold` kenarlık (1px, köşe 6), gölge `#000000` alpha 0.4, gölge büyüklüğü 4

### InnerPanel (çerçevesiz, iç içe paneller)
- `PanelBg` + `BorderDark` kenarlık 0px, köşe 4, gölge yok

### Separator (HSeparator / VSeparator)
- `BorderDark` arka plan ve kenarlık, 0px

### LineEdit / TextEdit
- `normal`: `InputBg` + `BorderDark`
- `focus`: `InputBg` + `Gold`
- `read_only` (LineEdit): `InputBg` + `BorderDark`
- Yazı: `TextMain`, placeholder: `TextMuted`, seçili: `GoldBright`

### OptionButton
- `normal`: `InputBg` + `BorderDark`
- `hover`: `PanelBgLight` + `Gold`
- `pressed`: `PanelBgPress` + `GoldBright`
- `focus`: `InputBg` + `GoldBright` (0px kenarlık)

### PopupMenu (OptionButton açılır listesi)
- `panel`: `PanelBg` + `Gold` kenarlık (1px, köşe 4)
- `hover`: `PanelBgLight` + `Gold` (0px kenarlık, köşe 2)
- Yazı: `TextMain`, hover: `GoldBright`, disabled: `TextDisabled`

### CheckBox
- Stil kutuları `Button`'dan miras alır; `check_vadjust` = 0

### HSlider
- `slider` (ray): `#1A2A3A` + `BorderDark` (0px, köşe 4)
- `grabber_area` (dolu kısım): `Gold` + `Gold` (0px, köşe 4)
- `grabber_area_highlight`: `GoldBright` + `GoldBright`

### HScrollBar / VScrollBar
- `scroll` (arkaplan): şeffaf + `BorderDark` (0px)
- `grabber` / `grabber_highlight` / `grabber_pressed`: `Gold` / `GoldBright`

### Window (temel)
- `panel`: `PanelBg` + `Gold` kenarlık (1px, köşe 6)

## Font Boyutları (Tema sabitleri)
Tüm kontroller için `font_size = 16`. Farklı boyut gereken özel etiketler (başlık, başlık etiketi) harici `Label` ayarıyla üzerine yazılır.

## Animasyonlar (`UITheme.cs` yardımcı metodları)
| Metod | Açıklama |
|-------|----------|
| `AddHoverScale(control, 1.05, 0.08s)` | Hover'da hafif büyütme (1.05×) |
| `FadeIn(panel, 0.12s)` | Modulate alpha 0→1 fade-in |
| `FadeOut(panel, 0.1s, onComplete)` | Modulate alpha 1→0 fade-out, sonrada callback |
| `PopIn(panel, 0.15s)` | Fade + 0.96→1.0 ölçeklendirme (Back ease) |
| `PopOut(panel, 0.1s, onComplete)` | Fade + 1.0→0.97 ölçeklendirme, sonrada callback |

## Panel Boyutları (1152×648 kısıtı)
Tüm overlay paneller 1152×648 çözünürlükte tam sığmalıdır:
- FocusPanel: 1060×560
- DecisionPanel: 900×500
- AgendaPanel: 860×560
- MonthlyReportPanel: 560×420
- LibraryPanel: 900×560
- LoadGamePanel: 520×560
- CabinetPanel: 860×520
- EventPopup: 780×460
- ScenarioSelectPanel: 1152×648'e sığar

## Mouse Davranışı (bozulmamalı)
- Panel açıkken: backdrop `MouseFilter.Stop` → harita girdisi (sağ tık sürükleme, tekerlek, WASD) engellenir.
- Panel kapalıyken: RMB sürükleme, tekerlek zoom, WASD pan, ülke seçimi aynen çalışır.
- Dekoratif kontrol bayrakları (ör. bayrak): `MouseFilter = Ignore`.

## Kural: Tek Tema
Tüm paneller aynı `Theme` kaynağını kullanır. Yeni bir panel eklendiğinde `UITheme.ApplyTheme(root)` çağrılır; özel varyant gerekirse `SetTypeVariation` ile alt stil eklenir (ör. `ToolbarButton`, `InnerPanel`).

## Uygulama Durumu
- `Scripts/UITheme.cs`: mevcut, derleme 0 hata / 0 uyarı, `GD.Print("UITheme: hazır")` logu var.
- Tüm mevcut paneller temayı kullanıyor; `GameHud`, `MainMenu`, `PauseMenu` gibi kök kontroller `ApplyTheme` ile bağlanmış.