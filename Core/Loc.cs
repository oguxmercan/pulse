using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Pulse.Core;

/// <summary>
/// Turkish is the source language: every UI string is written in Turkish and this table maps it to
/// English. XAML text is translated once when the window loads (bound text is left alone), so strings
/// added later must also get an entry here or they stay Turkish in English mode.
/// </summary>
public static class Loc
{
    public static string Lang { get; private set; } = "tr";
    public static bool En => Lang == "en";

    public static void Apply(string? lang)
    {
        Lang = lang == "en" ? "en" : "tr";
        try
        {
            var c = CultureInfo.GetCultureInfo(En ? "en-US" : "tr-TR");
            CultureInfo.DefaultThreadCurrentCulture = c;
            CultureInfo.DefaultThreadCurrentUICulture = c;
        }
        catch { }
    }

    /// <summary>Translate a whole Turkish string.</summary>
    public static string S(string tr) => En && Map.TryGetValue(tr, out var en) ? en : tr;

    /// <summary>Translate a Turkish format string, then format it.</summary>
    public static string T(string tr, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, S(tr), args);

    /// <summary>
    /// Translate literal text in a loaded tree. The logical tree is used because collapsed pages have
    /// no visuals yet; template-generated visuals are walked too. Bound text is never touched.
    /// </summary>
    public static void Walk(DependencyObject root) => Walk(root, new HashSet<DependencyObject>());

    static void Walk(DependencyObject node, HashSet<DependencyObject> seen)
    {
        if (!En || node is not Visual || !seen.Add(node)) return;   // Grid row/column definitions are not visuals
        Translate(node);

        // snapshot first: translating swaps ContentControl.Content, which mutates the logical children
        foreach (var child in LogicalTreeHelper.GetChildren(node).Cast<object>().ToList())
            if (child is DependencyObject d) Walk(d, seen);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (VisualTreeHelper.GetChild(node, i) is Visual child) Walk(child, seen);
    }

    static void Translate(DependencyObject node)
    {
        if (node is TextBlock tb && Map.ContainsKey(tb.Text)
            && !BindingOperations.IsDataBound(tb, TextBlock.TextProperty))
            tb.Text = S(tb.Text);
        else if (node is ContentControl cc && cc.Content is string s && Map.ContainsKey(s)
                 && !BindingOperations.IsDataBound(cc, ContentControl.ContentProperty))
            cc.Content = S(s);
        else if (node is HeaderedContentControl hc && hc.Header is string h && Map.ContainsKey(h))
            hc.Header = S(h);

        if (node is FrameworkElement fe && fe.ToolTip is string tip && Map.ContainsKey(tip))
            fe.ToolTip = S(tip);
    }

    static readonly Dictionary<string, string> Map = new()
    {
        // navigation / shell
        ["GEZİN"] = "BROWSE",
        ["Giriş"] = "Home",
        ["Kütüphane"] = "Library",
        ["Hız aşırtma"] = "Overclocking",
        ["Hakkında"] = "About",
        ["SİSTEM"] = "SYSTEM",
        ["Kütüphanem"] = "My library",
        ["SON OYNANAN"] = "LAST PLAYED",
        ["Oynama süresi"] = "Playtime",
        ["Kare geçmişi"] = "Frame history",
        ["Oyun ara"] = "Search games",
        ["Oyna"] = "Play",
        ["Kütüphanede gör"] = "Show in library",
        ["Yenile"] = "Refresh",
        ["EXE ekle"] = "Add EXE",
        ["Başlat"] = "Launch",
        ["Kaldır"] = "Remove",
        ["Kapat"] = "Close",
        ["Kaydet"] = "Save",
        ["Uygula"] = "Apply",
        ["Görünüm"] = "Theme",
        ["Saydamlık"] = "Opacity",
        ["Sınırlayıcı…"] = "Limiter…",

        // overlay page
        ["Taşıma modu"] = "Move mode",
        ["Taşıma modunu aç / kilitle"] = "Toggle move mode / lock",
        ["Tıkla-geç (fareyi oyuna bırakır)"] = "Click-through (lets the mouse reach the game)",
        ["Kısayollar"] = "Shortcuts",
        ["Ctrl+Alt+O göster/gizle · Ctrl+Alt+L taşıma"] = "Ctrl+Alt+O show/hide · Ctrl+Alt+L move",
        ["Ctrl+Alt+O — overlay göster/gizle"] = "Ctrl+Alt+O — show/hide overlay",
        ["Ctrl+Alt+L — tıkla-geç aç/kapat"] = "Ctrl+Alt+L — toggle click-through",
        ["Kilit açıkken sürükle, köşeden boyutlandır."] = "While unlocked, drag to move and pull a corner to resize.",
        ["Bağlanabilir değerler"] = "Bindable values",
        ["Tema kodu (XAML DataTemplate) — düzenle, uygula, kaydet"] = "Theme markup (XAML DataTemplate) — edit, apply, save",
        ["Uygula (canlı)"] = "Apply (live)",
        ["Varsayılana döndür"] = "Reset to default",
        ["Klasörü aç"] = "Open folder",
        ["TAŞI · boyut için köşeden çek"] = "DRAG · pull a corner to resize",

        // library / limiter
        ["Oyun seç"] = "Select game",
        ["FPS sınırı"] = "FPS limit",
        ["Sınırı uygula"] = "Apply limit",
        ["Bu oyun için RTSS kare sınırı (0 = kapalı). İstatistiklerde hedef olarak da kullanılır."] =
            "RTSS frame limit for this game (0 = off). Also used as the target in the stats.",
        ["Kare sınırlayıcı (RTSS)"] = "Frame limiter (RTSS)",
        ["Global sınır"] = "Global limit",
        ["Tüm oyunlar için fps sınırı (0 = kapalı)"] = "FPS limit for every game (0 = off)",
        ["Sınır RTSS'e yazılır (Front-Edge-Sync). Kütüphanede oyun seçip 'Sınırı uygula' ile oyun bazlı sınır konur. RTSS enjeksiyon kullanır: Vanguard/EAC/BattlEye'li oyunlarda risk alma."] =
            "The limit is written into RTSS (Front-Edge Sync). Choose a game in the library and press 'Apply limit' for a per-game cap. RTSS injects into the game: do not use it with Vanguard/EAC/BattlEye titles.",
        ["Sınırlayıcı: RTSS hazır"] = "Limiter: RTSS ready",
        ["Sınırlayıcı: RTSS çalışıyor"] = "Limiter: RTSS running",
        ["Sınırlayıcı: RTSS kapalı"] = "Limiter: RTSS stopped",
        ["Sınırlayıcı: RTSS yok"] = "Limiter: RTSS missing",
        ["RTSS bulunamadı"] = "RTSS not found",
        ["RTSS kurulu değil. Kare sınırı için RTSS kur (rivatuner) — Pulse sınırı profil dosyasına yazar, enjeksiyonu RTSS yapar."] =
            "RTSS is not installed. Install RivaTuner Statistics Server for frame limiting — Pulse only writes the profile, RTSS does the injection.",
        ["Sınır için exe adı bilinmiyor."] = "No exe name known for this game.",
        ["Global sınır kapatıldı."] = "Global limit removed.",
        ["Sadece elle eklenen oyunlar kaldırılabilir."] = "Only manually added games can be removed.",
        ["Oyun exe dosyasını seç"] = "Pick the game executable",
        ["Uygulama (*.exe)|*.exe"] = "Application (*.exe)|*.exe",
        ["Kütüphane yeniden tarandı."] = "Library scanned again.",
        ["Oyun bekleniyor"] = "Waiting for a game",

        // overclocking
        ["Undervolt / verim profilleri"] = "Undervolt / efficiency presets",
        ["Dengeli −10%"] = "Balanced −10%",
        ["Sessiz −20% + 1800 MHz"] = "Quiet −20% + 1800 MHz",
        ["Maks verim −30% + 1600 MHz"] = "Max efficiency −30% + 1600 MHz",
        ["Güç limiti (W)"] = "Power limit (W)",
        ["Çekirdek alt limit (MHz)"] = "Core clock min (MHz)",
        ["Çekirdek üst limit (MHz)"] = "Core clock max (MHz)",
        ["Bellek alt limit (MHz)"] = "Memory clock min (MHz)",
        ["Bellek üst limit (MHz)"] = "Memory clock max (MHz)",
        ["Güç limiti + çekirdek saat tavanı birlikte uygulanır — NVAPI olmadan yapılabilen güvenli undervolt. Kapatınca geri alınır."] =
            "Applies a power limit plus a core clock ceiling — the safe undervolt you can do without NVAPI. Rolled back when you close it.",
        ["Ayarlar nvidia-smi ile uygulanır; uygulama kapanınca/çökünce -rgc, -rmc ve varsayılan güç limiti geri yüklenir. Boş alan uygulanmaz, 0 = kilidi kaldır. Gerçek voltaj ofseti (curve) NVAPI gerektirir; burada yok."] =
            "Settings are applied through nvidia-smi; on exit or crash -rgc, -rmc and the default power limit are restored. Empty fields are skipped, 0 removes a lock. A real voltage offset (curve) needs NVAPI and is not implemented.",
        ["Uygulandı."] = "Applied.",
        ["Uygulandı. Kapatınca geri alınır."] = "Applied. Rolled back when the app closes.",
        ["Uygulanıyor…"] = "Applying…",
        ["Varsayılan saat ve güç limiti geri yüklendi."] = "Default clocks and power limit restored.",
        ["nvidia-smi bulunamadı (AMD/Intel kartlar desteklenmiyor)."] = "nvidia-smi not found (AMD/Intel cards are not supported).",

        // fans / sensors
        ["Fan kontrolü"] = "Fan control",
        ["Sadece yazılabilir fanlar."] = "Writable fans only.",
        ["Sensörler yükleniyor…"] = "Loading sensors…",
        ["Kapatınca firmware kontrolüne döner."] = "Returns to firmware control when closed.",
        ["Yazılabilir fan bulunamadı (anakart/GPU sürücüsü izin vermiyor)."] = "No writable fan found (board/GPU driver refuses).",
        ["Fanları varsayılana döndür"] = "Restore fans to default",
        ["Fanlar firmware kontrolüne bırakıldı."] = "Fans handed back to the firmware.",
        ["Donanım sensörleri açılamadı — yönetici olarak çalıştırın."] =
            "Hardware sensors could not be opened — run the app as administrator.",
        ["CPU fan"] = "CPU fan",
        ["GPU fan"] = "GPU fan",
        ["GPU hotspot"] = "GPU hotspot",
        ["VRAM"] = "VRAM",

        // about
        ["Teknolojiler ve lisans"] = "Tech stack and licences",
        ["Oyun içi performans overlay'i · kare sınırlayıcı · sensör paneli"] =
            "In-game performance overlay · frame limiter · sensor panel",
        [".NET 10 · WPF + WPF-UI · LibreHardwareMonitor · Intel PresentMon 2.6 (MIT) · RivaTuner Statistics Server (kare sınırı / pacing) · nvidia-smi · WiX 5 (MSI)"] =
            ".NET 10 · WPF + WPF-UI · LibreHardwareMonitor · Intel PresentMon 2.6 (MIT) · RivaTuner Statistics Server (frame limit / pacing) · nvidia-smi · WiX 5 (MSI)",
        ["Uyarı: RTSS enjeksiyonu kullanan rekabetçi oyunlarda (Vanguard / EAC / BattlEye) ban riski vardır."] =
            "Warning: RTSS injection carries a ban risk in competitive games with kernel anti-cheat (Vanguard / EAC / BattlEye).",
        ["Dil"] = "Language",

        // runtime messages (code-behind)
        ["Overlay'i gizle"] = "Hide overlay",
        ["Overlay'i göster"] = "Show overlay",
        ["Overlay kilitli — fare oyuna geçiyor."] = "Overlay locked — the mouse goes to the game.",
        ["Taşıma modu açık — overlay'i sürükleyip köşeden boyutlandır."] =
            "Move mode is on — drag the overlay and pull a corner to resize.",
        ["Varsayılan tema bulunamadı."] = "Default theme not found.",
        ["Henüz oturum kaydı yok."] = "No sessions recorded yet.",
        ["Yeniden tara"] = "Rescan",
        ["Sensörler açılamadı: "] = "Sensors unavailable: ",
        ["Başlatılamadı: "] = "Could not start: ",
        ["Eklendi: "] = "Added: ",
        ["Hata: "] = "Error: ",
        ["Kapatılamadı: "] = "Could not remove: ",
        ["Sınır uygulanamadı: "] = "Could not apply the limit: ",
        ["Varsayılana döndürüldü."] = "Reset to default.",
        ["hedefte"] = "on target",
        ["hedef altı"] = "below target",
        ["yeni"] = "new",

        // format strings
        ["Hedef: {0} fps"] = "Target: {0} fps",
        ["Toplam oynama: {0}"] = "Total playtime: {0}",
        ["Oturum sayısı: {0}"] = "Sessions: {0}",
        ["Ortalama FPS: {0}"] = "Average FPS: {0}",
        ["Ortalama GPU: {0}"] = "Avg GPU: {0}",
        ["En yüksek GPU: {0}"] = "Peak GPU: {0}",
        ["En yüksek CPU: {0}"] = "Peak CPU: {0}",
        ["Ortalama CPU: {0}"] = "Avg CPU: {0}",
        ["Son oynama: {0}"] = "Last played: {0}",
        ["Mağaza: {0}"] = "Store: {0}",
        ["Süreç: {0} (oturum bunun açık olmasıyla sayılır)"] = "Process: {0} (tracked while it runs)",
        ["Başlatma: {0}"] = "Launch: {0}",
        ["{0} başlatıldı."] = "{0} launched.",
        ["{0} sa {1} dk"] = "{0} h {1} min",
        ["{0} dk"] = "{0} min",
        ["{0} sn"] = "{0} s",
        ["{0} oyun"] = "{0} games",
        ["Son oynanan"] = "Last played",
        ["Mağaza"] = "Store",
        ["{0} · yeni"] = "{0} · new",
        ["{0} başlatıldı — oturum süreç açılınca başlar."] = "{0} launched — the session starts once its process is up.",
        ["{0} uygulanıyor…"] = "Applying {0}…",
        ["{0} · güç {1}–{2} W (varsayılan {3} W)"] = "{0} · power {1}–{2} W (default {3} W)",
        ["{0} W (varsayılan {1} W)"] = "{0} W (default {1} W)",
        ["   Hedef: {0} fps"] = "   Target: {0} fps",
        ["   En yüksek GPU: {0}"] = "   Peak GPU: {0}",
        ["   En yüksek CPU: {0}"] = "   Peak CPU: {0}",
        ["güç: "] = "power: ",
        ["çekirdek: "] = "core: ",
        ["bellek: "] = "memory: ",
        ["Sıfırlanıyor…"] = "Resetting…",
        ["Oturum izleme: "] = "Session tracking: ",
        ["RTSS hazır (başlatılacak)"] = "RTSS ready (will start)",
        ["RTSS kurulu değil — sınırlama için RTSS gerekir"] = "RTSS is not installed — frame limiting needs it",
        ["RTSS başlatılamadı: "] = "RTSS could not start: ",
        ["PresentMon.exe eksik (uygulama klasöründe olmalı)."] = "PresentMon.exe is missing (it belongs next to the app).",
        ["ETW oturumu açılamadı (logman temizliği gerek)"] = "ETW session could not open (clean up with logman)",
    };
}
