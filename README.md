# Pulse — Windows oyun performans overlay'i + kontrol paneli

Tek uygulama, iki parça: şık bir **oyun içi overlay** ve bir **kontrol paneli** (FPS/frametime, CPU, GPU, RAM, sıcaklıklar, fan).
Windows 10/11 x64. .NET 10 WPF + WPF-UI (Fluent), LibreHardwareMonitor (sensörler),
Intel PresentMon (kare/FPS), nvidia-smi (güç/saat limitleri).

## Ekran görüntüleri

Kontrol paneli:

![Pulse panel](docs/panel.png)

Sekiz hazır overlay görünümü:

![Overlay temaları](docs/themes.png)

## Kurulum

**Installer (önerilen):** `PulseSetup.msi` → çift tık. `C:\Program Files\Pulse` altına kurar,
Başlangıç menüsüne kısayol ekler, Denetim Masası'ndan kaldırılabilir. Kurulum her makine içindir (admin).

Yeni MSI üretmek: `powershell -File installeruild.ps1`
(WiX 5 gerekir: `dotnet tool install --global wix --version 5.0.2`. WiX 7 lisans ücreti istediği için 5 kullanılıyor.)

**Taşınabilir:** 
```
dist\Pulse.exe
```

`dist\` klasörü taşınabilir; `Pulse.exe` + `PresentMon.exe` yan yana durmalı.
Uygulama **yönetici olarak** açılır (UAC): CPU sıcaklığı, anakart fanları ve ETW kare yakalama
ancak yükseltilmiş yetkiyle çalışır.

Geliştirme: `dotnet build` → `dotnet publish -c Release -o dist`

## Arayüz (Xbox PC uygulaması düzeni)

Logo `Assets/logo.png` (yeniden üretmek için `python Assets/make_logo.py`), pencere simgesi `Assets/pulse.ico`.
**Hakkında** sayfası: geliştirici kartı (Oğuzhan Mercan · Mercan Search and Development · Netherlands),
GitHub / X bağlantıları ve teknoloji lisans notları.

- **Sol kenar çubuğu** (244 px): logo, Giriş · Kütüphane · Overlay · Hız aşırtma; altta canlı SİSTEM kutusu
  (CPU/GPU sıcaklığı, FPS) ve overlay aç/kapa.
- **Üst bar**: ortada yuvarlak arama kutusu, sağda durum satırı.
- **Giriş**: "SON OYNANAN" hero banner'ı (Steam `library_hero.jpg`), oyun adı + istatistik + **Oyna**;
  altında 4 metrik kartı ve kare geçmişi.
- **Kütüphane**: 16:9 geniş kapak döşemeleri (4 sütun), altında oyun adı + mağaza/süre/FPS satırı;
  sıralama (A→Z, oynama süresi, son oynanan, mağaza), arama, alt çubukta **Başlat / Hedef FPS / Kaldır**.

## Overlay

- Sürükle: **Ctrl+Alt+L** (tıkla-geç kapanır) → sol üstten tut, köşeden boyutlandır.
- Göster/gizle: **Ctrl+Alt+O**.
- Kilitliyken (`WS_EX_TRANSPARENT`) fare oyuna geçer; overlay hiçbir tıklamayı yemez.
- Konum, boyut, tema, saydamlık `%LOCALAPPDATA%\Pulse\settings.json` içinde saklanır.
- Sekiz hazır görünüm: `minimal`, `cards`, `graph`, `bars`, `neon` (parlayan FPS + sparkline), `halo` (tek satır premium pill), `ticker` (monospace şerit), `dual` (FPS + dolu çubuklar).

### Overlay editörü (kodlanabilir tema)

Overlay'in görünümü sadece bir **XAML `DataTemplate`** dosyasıdır: `%LOCALAPPDATA%\Pulse\themes\*.xaml`.
Panel → **Overlay** sekmesinde kodu düzenle, **Uygula (canlı)** ile anında gör, **Kaydet** ile sakla,
**Varsayılana döndür** ile shipped sürüme dön.

Bağlanabilir değerler (`{Binding ...}`):

| Grup | Alanlar |
|------|---------|
| FPS | `Fps`, `Frametime`, `Spark` (64x40 grafik geometrisi, `<Path Data="{Binding Spark}" Stretch="Fill"/>`) |
| CPU | `CpuName`, `CpuLoad`, `CpuTemp`, `CpuFan`, `CpuClock` |
| GPU | `GpuName`, `GpuLoad`, `GpuTemp`, `GpuHotspot`, `GpuFan`, `GpuFanPct`, `GpuClock`, `GpuPower`, `VramUsed`, `VramTotal` |
| RAM | `RamUsed`, `RamTotal` |
| Diğer | `Target` (izlenen süreç), `Clock` |

Örnek satır:

```xml
<TextBlock Text="{Binding CpuTemp, StringFormat='{}{0:F0}°C', TargetNullValue='--'}" />
```

Sensör yoksa değer `null` gelir → `TargetNullValue` ile `--` göster.

## Kütüphane

Steam (kütüphane klasörleri dahil), Epic, GOG, EA App, Ubisoft Connect, Battle.net otomatik taranır;
**EXE ekle** ile kendi oyununu ekleyebilirsin (liste `settings.json` → `Custom`).

- Başlatma: Steam/Epic launcher URI'si, diğerleri doğrudan exe.
- Oturum takibi: oyunun süreci açıkken süre sayılır, kapanınca kaydedilir (`%LOCALAPPDATA%\Pulse\sessions.json`).
  20 saniyeden kısa oturumlar kaydedilmez.
- Oyun başına: toplam süre, oturum sayısı, ortalama FPS, ortalama/en yüksek GPU-CPU sıcaklığı.
- Ortalama FPS hesabı ilk %10'luk yükleme dilimini atar.
- **Hedef FPS**: oyun başına saklanır (`settings.json` → `Targets`). Oturum ortalaması hedefin
  %97'sinin altındaysa listede "hedef altı" görünür — bir kare hızı *sınırı* değil, ölçüm referansıdır.

### Kare sınırlayıcı (RTSS entegrasyonu)

Sınırlama, **RivaTuner Statistics Server** üzerinden yapılır — RTSS "Front-Edge Sync" ile hassas
frame pacing sağlar (framepacer'ın kendisi de karşılaştırma tablosunda RTSS'i eşdeğer gösteriyor).
Pulse **enjeksiyon yapmaz**; sadece RTSS profillerini yönetir:

- Kütüphanede oyun seç → **FPS sınırı** kutusuna değer yaz → **Sınırı uygula**
  (`Profiles\<exe>.cfg` içine `[Framerate] Limit=N`, `LimitDenominator=1`).
- Hız aşırtma sayfasında **Global sınır** (tüm oyunlar) ve kapatma.
- RTSS çalışmıyorsa Pulse başlatır; kurulu değilse bölüm pasif kalır ve durumu yazar.
- RTSS'in `Profiles` klasörü `Program Files` altında olduğu için yazma **yönetici** ister (uygulama zaten yönetici).

Sınırı 0 yapmak o oyunun sınırını kaldırır (`Limit=0`). Değişiklik oyun yeniden başlatılmadan
yeni karelerde etkili olur; RTSS yalnızca kendi hook'ladığı süreçlerde çalışır (WPF/DWM pencereleri kapsam dışı).

**Doğrulandı (2026-11-14):** Forager (Steam) Pulse'tan 60 fps sınırıyla başlatıldı;
`Profiles\Forager.exe.cfg` = `[Framerate] Limit=60 / LimitDenominator=1` (Pulse yazdı),
RTSS OSD oyunda sabit 60 fps gösterdi. Yani sınır + pacing RTSS tarafından uygulanıyor.
Not: sentetik D3D11 test sürecimde RTSS hook devreye girmedi (gerçek oyunlarda giriyor) — bu yüzden
doğrulama oyunla yapıldı.

> Uyarı: RTSS enjeksiyonu, framepacer gibi, agresif kernel anti-cheat'li (Vanguard, EAC, BattlEye)
> rekabetçi oyunlarda risklidir. Kullanmadan önce oyunun politikasını kontrol et.

### framepacer.com'daki hangi özellikler yok

- DLSS/FSR/XeSS **Frame Generation** ve Reflex **FG-aware** limiter (bizim sınırımız düz FPS).
- Anti-cheat tespiti/blacklist ve otomatik oyun tanıma sezgileri.
- Kendi enjeksiyon motoru, ReShade entegrasyonu, 30 dil, mağaza (Steam) sürümü.

### Kapak görselleri

Kapaklar Steam CDN'inden indirilip `%LOCALAPPDATA%\Pulse\covers\` altında saklanır:

- Steam oyunları: `steam:<appid>` → `steam/apps/<appid>/library_600x900.jpg` (yoksa `header.jpg`).
- Diğer mağazalar: isim, Steam mağaza aramasıyla appid'e eşlenir (`covers.json` içinde saklanır).
- JPEG doğrulaması yapılır (boş/HTML yanıtı yazılmaz), kapak yoksa döşemede harf monogramı görünür.
- Yeniden indirmek için `covers` klasörünü sil ve **Yenile**'ye bas.

## Hız aşırtma / undervolt (NVIDIA, isteğe bağlı)

Panel → **Hız aşırtma**:

- **Undervolt profilleri** (tek tık): `Dengeli −10%`, `Sessiz −20% + 1800 MHz`, `Maks verim −30% + 1600 MHz`.
  Güç limiti (`-pl`) + çekirdek saat tavanı (`-lgc 0,<MHz>`) birlikte uygulanır; NVAPI olmadan
  yapılabilen güvenli undervolt budur (gerçek voltaj/frekans eğrisi NVAPI Pstates20 ister — bilinçli olarak yok).
- **Güç limiti** (`nvidia-smi -pl`), **çekirdek saat kilidi** (`-lgc min,max`), **bellek kilidi** (`-lmc`).
- Boş bırakılan alan uygulanmaz; `0` = kilidi kaldır.
- **Güvenlik:** uygulama kapanırken, çökerse veya `AppDomain.ProcessExit` tetiklenirse `-rgc`, `-rmc`
  ve varsayılan güç limiti geri yüklenir. Uygulama öldürülürse bir sonraki açılışta
  `%LOCALAPPDATA%\Pulse\tuning-active` işareti görülüp temizlenir.
- Gerçek çekirdek voltaj/frekans **ofseti** NVAPI gerektirir (Afterburner gibi); burada bilinçli olarak yok.

## Fan kontrolü

Sürücüsü yazılabilir fanlar (GPU fanları + ITE SuperIO anakart fanları) kaydırıcıyla %0-100 ayarlanır.
Kapatınca veya **Fanları varsayılana döndür** ile firmware kontrolüne bırakılır (`IControl.SetDefault`).
Hangi fanın hangisi olduğunu (%100 verip dinleyerek) doğrulamadan anakart fanlarına dokunma.

## Bilinen sınırlar

- **FPS yalnızca yönetici + PresentMon ile.** PresentMon 2.x CSV'yi sadece çıkışta paylaşıma açtığı için
  1.2 s'lik dilimler hâlinde çalıştırılır: FPS ~1.5 s'de bir güncellenir. (Uzun vadeli çözüm: Paylaşımlı
  bellek API'si / kendi ETW tüketicimiz.)
- **Özel tam ekran (exclusive fullscreen)** oyunlarda hiçbir harici overlay görünmez; **borderless/windowed**
  kullan (Windows'un kendi kısıtı).
- GPU hız aşırtma yalnızca NVIDIA. AMD/Intel kartlarda sensörler çalışır, tuning bölümü pasif kalır.
- İlk açılışta sensör taraması 1-3 s sürebilir; CPU sıcaklığı yönetici değilse `--` görünür.
- Elle eklenen/mağaza dışı oyunlarda oturum tespiti exe adına bağlıdır (kurulum klasöründe bulunan en büyük exe).

## Dosyalar

```
%LOCALAPPDATA%\Pulse\
  settings.json      tema, overlay konumu, tuning değerleri, elle eklenen oyunlar
  sessions.json      oyun oturumları ve istatistikler
  themes\*.xaml      düzenlenebilir overlay temaları
  tuning-active      varsa: son kapanış düzgün olmadı, açılışta saatler sıfırlanır
  selftest.log       `Pulse.exe --selftest [exeAdı]` çıktısı (tanı için)
```

## Kaynaklar

- `Core/Sensors.cs` — LibreHardwareMonitor sarmalayıcı, sensör seçimi, fan kontrolleri
- `Core/FpsTracker.cs` — PresentMon dilimleme + FPS/frametime
- `Core/GpuTuner.cs` — nvidia-smi, güvenli geri alma
- `Core/Games.cs` — mağaza tarayıcıları, exe bulma, başlatma
- `Core/Sessions.cs` — oturum/istatistik
- `MainWindow.*` / `OverlayWindow.*` — panel ve overlay
- `themes/*.xaml` — hazır temalar
- `tools/PresentMon.exe` — Intel PresentMon 2.6.0 (MIT)
