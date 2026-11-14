using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Pulse.Core;
using Wpf.Ui.Controls;

namespace Pulse;

public partial class MainWindow : FluentWindow, INotifyPropertyChanged
{
    static readonly Dictionary<string, (double Percent, int CoreCap)> UndervoltPresets = new()
    {
        ["Dengeli −10%"] = (10, 0),
        ["Sessiz −20% + 1800 MHz"] = (20, 1800),
        ["Maks verim −30% + 1600 MHz"] = (30, 1600),
    };

    readonly Settings _set;
    readonly Sensors _sensors = new();
    readonly FpsTracker _fps = new();
    readonly Sessions _sessions = new();
    readonly OverlayWindow _overlay;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer _geomSave = new() { Interval = TimeSpan.FromSeconds(1.2) };
    readonly List<GameRow> _rows = new();
    bool _loading = true;
    bool _suspendTarget;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Metrics Metrics => _overlay.Metrics;
    public ObservableCollection<GameRow> Games { get; } = new();
    public ObservableCollection<GameRow> Filtered { get; } = new();
    public ObservableCollection<FanItem> Fans { get; } = new();
    public List<string> ThemeNames { get; } = new();

    GameRow? _featured;
    public GameRow? Featured
    {
        get => _featured;
        private set { _featured = value; PropertyChanged?.Invoke(this, new(nameof(Featured))); }
    }

    string _sessionText = Loc.S("Oyun bekleniyor");
    public string SessionText { get => _sessionText; set { _sessionText = value; PropertyChanged?.Invoke(this, new(nameof(SessionText))); } }

    public MainWindow()
    {
        InitializeComponent();
        _set = Store.Load("settings.json", () => new Settings());
        _overlay = new OverlayWindow();
        DataContext = this;

        _fps.Sample += (fps, ft) => Dispatcher.BeginInvoke(() => Metrics.PushFps(fps, ft));

        LangBox.SelectedIndex = Loc.En ? 1 : 0;
        InitOverlay();
        InitThemes();
        LoadGames();
        Limiter.Probe();
        InitTuning();
        RefreshLimiterUi();
        StartTimers();

        _tick.Tick += OnTick;
        _watch.Tick += OnWatch;
        _geomSave.Tick += (_, _) => { _geomSave.Stop(); SaveSettings(); };

        SetVersionLabels();
        _loading = false;

        // LHM's first scan loads a kernel driver and can take ~10 s: never block the first paint on it.
        Loaded += async (_, _) =>
        {
            Loc.Walk(this);
            await Task.Run(_sensors.Open);
            foreach (var f in _sensors.Fans) Fans.Add(f);
            FanHint.Text = Fans.Count > 0
                ? Loc.S("Kapatınca firmware kontrolüne döner.")
                : Loc.S("Yazılabilir fan bulunamadı (anakart/GPU sürücüsü izin vermiyor).");
            TuningStatus.Text = _sensors.Error.Length > 0 && !_sensors.Ready
                ? Loc.S("Sensörler açılamadı: ") + _sensors.Error
                : "";
        };
    }

    // ---------- navigation ----------

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && PageHome == null) return;
        var nav = sender as RadioButton;
        PageHome.Visibility = ReferenceEquals(nav, NavHome) ? Visibility.Visible : Visibility.Collapsed;
        PageLibrary.Visibility = ReferenceEquals(nav, NavLibrary) ? Visibility.Visible : Visibility.Collapsed;
        PageOverlay.Visibility = ReferenceEquals(nav, NavOverlay) ? Visibility.Visible : Visibility.Collapsed;
        PageTune.Visibility = ReferenceEquals(nav, NavTune) ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = ReferenceEquals(nav, NavAbout) ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = nav?.Content as string ?? "";
        Loc.Walk(this); // collapsed pages are only reached once they are shown
    }

    void Lang_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LangBox.SelectedIndex < 0) return;
        var en = LangBox.SelectedIndex == 1;
        if (en == Loc.En) return;                       // restart: the UI text is baked in at load
        _set.Lang = en ? "en" : "tr";
        Store.Save("settings.json", _set);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = true, Verb = "runas" });
        }
        catch { }
        Application.Current.Shutdown();
    }

    void SetVersionLabels()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        var text = $"v{v.Major}.{v.Minor}.{v.Build}";
        VersionText.Text = text;
        AboutVersion.Text = $"{text} · .NET 10 / WPF · LibreHardwareMonitor · PresentMon · RTSS";
    }

    void OpenLink_Click(object sender, RoutedEventArgs e) => OpenLink((sender as FrameworkElement)?.Tag as string);

    void Link_Open(object sender, MouseButtonEventArgs e) => OpenLink((sender as FrameworkElement)?.Tag as string);

    static void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    // ---------- overlay ----------

    void InitOverlay()
    {
        _overlay.Left = _set.Left;
        _overlay.Top = _set.Top;
        _overlay.Width = Math.Max(140, _set.Width);
        _overlay.Height = Math.Max(60, _set.Height);
        _overlay.Opacity = Math.Clamp(_set.Opacity, 0.3, 1);
        _overlay.LoadThemeFile(Path.Combine(Store.ThemesDir, _set.Theme + ".xaml"));
        _overlay.SetClickThrough(_set.ClickThrough);
        _overlay.Show();
        if (!_set.OverlayVisible) _overlay.Hide();

        OpacitySlider.Value = _overlay.Opacity;
        ClickThroughBox.IsChecked = _set.ClickThrough;
        OverlayButton.Content = _set.OverlayVisible ? Loc.S("Overlay'i gizle") : Loc.S("Overlay'i göster");

        _overlay.LocationChanged += (_, _) => { _geomSave.Stop(); _geomSave.Start(); };
        _overlay.SizeChanged += (_, _) => { _geomSave.Stop(); _geomSave.Start(); };
    }

    void OverlayToggle_Click(object sender, RoutedEventArgs e)
    {
        _set.OverlayVisible = !_set.OverlayVisible;
        if (_set.OverlayVisible) _overlay.Show(); else _overlay.Hide();
        OverlayButton.Content = _set.OverlayVisible ? Loc.S("Overlay'i gizle") : Loc.S("Overlay'i göster");
        SaveSettings();
    }

    void MoveMode_Click(object sender, RoutedEventArgs e)
    {
        _set.ClickThrough = !_set.ClickThrough;
        ClickThroughBox.IsChecked = _set.ClickThrough;
        _overlay.SetClickThrough(_set.ClickThrough);
        StatusLine.Text = _set.ClickThrough
            ? Loc.S("Overlay kilitli — fare oyuna geçiyor.")
            : Loc.S("Taşıma modu açık — overlay'i sürükleyip köşeden boyutlandır.");
        SaveSettings();
    }

    void ClickThrough_Toggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _set.ClickThrough = ClickThroughBox.IsChecked == true;
        _overlay.SetClickThrough(_set.ClickThrough);
        SaveSettings();
    }

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _overlay == null) return;
        _overlay.Opacity = e.NewValue;
        _set.Opacity = e.NewValue;
        SaveSettings();
    }

    // ---------- themes ----------

    void InitThemes()
    {
        ThemeNames.Clear();
        foreach (var f in Directory.EnumerateFiles(Store.ThemesDir, "*.xaml").OrderBy(x => x))
            ThemeNames.Add(Path.GetFileNameWithoutExtension(f));
        ThemeBox.ItemsSource = ThemeNames;
        ThemeBox.SelectedItem = ThemeNames.Contains(_set.Theme) ? _set.Theme : ThemeNames.FirstOrDefault();

        var path = Path.Combine(Store.ThemesDir, _set.Theme + ".xaml");
        if (File.Exists(path)) EditorBox.Text = File.ReadAllText(path);
    }

    string CurrentTheme => ThemeBox.SelectedItem as string ?? _set.Theme;
    string ThemePath => Path.Combine(Store.ThemesDir, CurrentTheme + ".xaml");

    void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not string name) return;
        var path = Path.Combine(Store.ThemesDir, name + ".xaml");
        if (!File.Exists(path)) return;
        EditorBox.Text = File.ReadAllText(path);
        _set.Theme = name;
        EditorError.Text = _overlay.LoadThemeFile(path) ? "" : _overlay.ThemeError;
        SaveSettings();
    }

    void ApplyTheme_Click(object sender, RoutedEventArgs e) =>
        EditorError.Text = _overlay.LoadThemeSource(EditorBox.Text) ? Loc.S("Uygulandı.") : _overlay.ThemeError;

    void SaveTheme_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_overlay.LoadThemeSource(EditorBox.Text)) { EditorError.Text = _overlay.ThemeError; return; }
            File.WriteAllText(ThemePath, EditorBox.Text);
            EditorError.Text = $"Kaydedildi: {Path.GetFileName(ThemePath)}";
        }
        catch (Exception ex) { EditorError.Text = ex.Message; }
    }

    void ResetTheme_Click(object sender, RoutedEventArgs e)
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "themes", CurrentTheme + ".xaml");
        if (!File.Exists(shipped)) { EditorError.Text = Loc.S("Varsayılan tema bulunamadı."); return; }
        File.Copy(shipped, ThemePath, true);
        EditorBox.Text = File.ReadAllText(shipped);
        EditorError.Text = _overlay.LoadThemeSource(EditorBox.Text) ? Loc.S("Varsayılana döndürüldü.") : _overlay.ThemeError;
    }

    void OpenThemes_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start("explorer.exe", Store.ThemesDir); } catch { }
    }

    // ---------- library ----------

    void LoadGames()
    {
        _rows.Clear();
        Games.Clear();
        foreach (var g in Games_Scan())
        {
            var row = new GameRow(g)
            {
                TargetFps = _set.Targets.TryGetValue(g.Id, out var t) ? t : 0,
            };
            _rows.Add(row);
            Games.Add(row);
        }
        RefreshStats();
        InitSort();
        ApplyFilter();
        _ = LoadCoversAsync();
    }

    async Task LoadCoversAsync()
    {
        try
        {
            await Covers.FetchAsync(_rows.Select(r => r.Game).ToList(), g =>
            {
                void Apply()
                {
                    var row = _rows.FirstOrDefault(r => r.Game.Id == g.Id);
                    if (row == null) return;
                    row.RefreshArt();
                    if (row == Featured) Featured = null;
                    PickFeatured();
                }

                if (Dispatcher.CheckAccess()) Apply();
                else Dispatcher.BeginInvoke(Apply);
            });
        }
        catch { }
    }

    /// <summary>Home hero: the most recently played game that actually has banner art.</summary>
    void PickFeatured()
    {
        var stats = _sessions.Stats();
        DateTime Last(GameRow r) => stats.TryGetValue(r.Game.Id, out var s) ? s.Last : DateTime.MinValue;

        var withHero = _rows.Where(r => r.Hero.Length > 0).OrderByDescending(Last).FirstOrDefault();
        if (Featured != null && Featured.Hero.Length > 0 && Featured != withHero && Last(Featured) >= Last(withHero ?? Featured))
            return; // keep an explicitly chosen game
        Featured = withHero ?? _rows.OrderByDescending(Last).FirstOrDefault();
    }

    void InitSort()
    {
        if (SortBox.Items.Count > 0) return;
        SortBox.ItemsSource = new[] { "A → Z", Loc.S("Oynama süresi"), Loc.S("Son oynanan"), Loc.S("Mağaza") };
        SortBox.SelectedIndex = 0;
    }

    void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        ApplyFilter();
    }

    List<Game> Games_Scan()
    {
        var all = Core.Games.Scan();
        foreach (var custom in _set.Custom)
            if (all.All(g => g.Id != custom.Id)) all.Add(custom);
        return all;
    }

    GameRow? Selected => GameList?.SelectedItem as GameRow;

    void ApplyFilter()
    {
        var q = SearchBox?.Text?.Trim() ?? "";
        var stats = _sessions.Stats();
        IEnumerable<GameRow> rows = _rows.Where(r =>
            q.Length == 0 ||
            r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            r.Store.Contains(q, StringComparison.CurrentCultureIgnoreCase));

        rows = (SortBox?.SelectedIndex ?? 0) switch
        {
            1 => rows.OrderByDescending(r => stats.TryGetValue(r.Game.Id, out var s) ? s.Played : TimeSpan.Zero),
            2 => rows.OrderByDescending(r => stats.TryGetValue(r.Game.Id, out var s) ? s.Last : DateTime.MinValue),
            3 => rows.OrderBy(r => r.Store).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        Filtered.Clear();
        foreach (var r in rows) Filtered.Add(r);
        if (LibCount != null) LibCount.Text = Loc.T("{0} oyun", Filtered.Count);
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        ApplyFilter();
    }

    void RefreshStats()
    {
        var stats = _sessions.Stats();
        foreach (var row in _rows) row.SetStat(stats.TryGetValue(row.Game.Id, out var s) ? s : null);
        if (Featured == null) PickFeatured();
    }

    void RefreshGames_Click(object sender, RoutedEventArgs e)
    {
        LoadGames();
        StatusLine.Text = Loc.S("Kütüphane yeniden tarandı.");
    }

    void AddExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.S("Uygulama (*.exe)|*.exe"), Title = Loc.S("Oyun exe dosyasını seç") };
        if (dlg.ShowDialog() != true) return;
        var game = Core.Games.FromExe(dlg.FileName);
        if (_set.Custom.All(g => g.Id != game.Id))
        {
            _set.Custom.Add(game);
            Store.Save("settings.json", _set);
        }
        var row = new GameRow(game);
        _rows.Add(row);
        Games.Add(row);
        ApplyFilter();
        GameList.SelectedItem = row;
        _ = Covers.FetchAsync(new[] { game }, _ => Dispatcher.BeginInvoke(row.RefreshArt));
        StatusLine.Text = Loc.S("Eklendi: ") + game.Name;
    }

    void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (_set.Custom.RemoveAll(g => g.Id == row.Game.Id) == 0)
        {
            StatusLine.Text = Loc.S("Sadece elle eklenen oyunlar kaldırılabilir.");
            return;
        }
        Store.Save("settings.json", _set);
        _rows.Remove(row);
        Games.Remove(row);
        ApplyFilter();
        SelName.Text = Loc.S("Oyun seç");
        SelStats.Text = "";
    }

    void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } row) return;
        SelName.Text = row.Name + (row.Game.Installed ? "" : "  · kurulu değil");
        SelStats.Text = row.Details;
        LaunchButton.IsEnabled = row.Game.Installed;
        _suspendTarget = true;
        var rtss = Limiter.Available && row.Game.ExeName.Length > 0 ? Limiter.ReadLimit(row.Game.ExeName + ".exe") : null;
        TargetBox.Text = (rtss ?? row.TargetFps) > 0 ? (rtss ?? row.TargetFps).ToString() : "";
        _suspendTarget = false;
    }

    void Target_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || _suspendTarget || Selected is not { } row) return;
        var target = int.TryParse(TargetBox.Text, out var v) && v is > 0 and < 1000 ? v : 0;
        row.TargetFps = target;
        if (target > 0) _set.Targets[row.Game.Id] = target;
        else _set.Targets.Remove(row.Game.Id);
        Store.Save("settings.json", _set);
        RefreshStats();
        SelStats.Text = row.Details;
    }

    void HeroPlay_Click(object sender, RoutedEventArgs e)
    {
        if (Featured is not { } row) return;
        try { row.Game.Launch(); StatusLine.Text = Loc.T("{0} başlatıldı.", row.Name); }
        catch (Exception ex) { StatusLine.Text = Loc.S("Başlatılamadı: ") + ex.Message; }
    }

    void HeroBrowse_Click(object sender, RoutedEventArgs e)
    {
        NavLibrary.IsChecked = true;
        if (Featured != null)
        {
            if (!Filtered.Contains(Featured)) SearchBox.Text = "";
            GameList.ScrollIntoView(Featured);
            GameList.SelectedItem = Featured;
        }
    }

    void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        try
        {
            row.Game.Launch();
            StatusLine.Text = Loc.T("{0} başlatıldı — oturum süreç açılınca başlar.", row.Name);
        }
        catch (Exception ex) { StatusLine.Text = Loc.S("Başlatılamadı: ") + ex.Message; }
    }

    // ---------- tuning ----------

    void RefreshLimiterUi()
    {
        LimiterStatusLine.Text = Limiter.Available ? Loc.S("Sınırlayıcı: RTSS hazır") : Loc.S("Sınırlayıcı: RTSS yok");
        LimiterInfo.Text = Limiter.Available
            ? $"{Limiter.Status} · {Limiter.ProfilesDir}"
            : Loc.S("RTSS kurulu değil. Kare sınırı için RTSS kur (rivatuner) — Pulse sınırı profil dosyasına yazar, enjeksiyonu RTSS yapar.");
        if (Limiter.Available && GlobalLimitBox.Text.Length == 0)
        {
            var g = Limiter.ReadLimit("Global") ?? 0;
            GlobalLimitBox.Text = g > 0 ? g.ToString() : "";
        }
    }

    void ApplyLimit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (row.Game.ExeName.Length == 0) { StatusLine.Text = Loc.S("Sınır için exe adı bilinmiyor."); return; }
        var fps = int.TryParse(TargetBox.Text, out var v) && v is > 0 and < 1000 ? v : 0;
        var (ok, msg) = Limiter.SetLimit(row.Game.ExeName + ".exe", fps);
        StatusLine.Text = ok ? $"{row.Name}: {msg}" : Loc.S("Sınır uygulanamadı: ") + msg;
        RefreshLimiterUi();
    }

    void ApplyGlobalLimit_Click(object sender, RoutedEventArgs e)
    {
        var fps = int.TryParse(GlobalLimitBox.Text, out var v) && v is > 0 and < 1000 ? v : 0;
        var (ok, msg) = Limiter.SetGlobalLimit(fps);
        StatusLine.Text = ok ? msg : Loc.S("Sınır uygulanamadı: ") + msg;
        RefreshLimiterUi();
    }

    void ClearGlobalLimit_Click(object sender, RoutedEventArgs e)
    {
        GlobalLimitBox.Text = "";
        var (ok, msg) = Limiter.SetGlobalLimit(0);
        StatusLine.Text = ok ? Loc.S("Global sınır kapatıldı.") : Loc.S("Kapatılamadı: ") + msg;
        RefreshLimiterUi();
    }

    void InitTuning()
    {
        PowerSlider.Minimum = GpuTuner.Available && GpuTuner.PowerMin > 0 ? GpuTuner.PowerMin : 50;
        PowerSlider.Maximum = GpuTuner.Available && GpuTuner.PowerMax > PowerSlider.Minimum ? GpuTuner.PowerMax : 450;
        PowerSlider.Value = Math.Clamp(_set.PowerLimit > 0 ? _set.PowerLimit : GpuTuner.PowerDefault, PowerSlider.Minimum, PowerSlider.Maximum);
        CoreMinBox.Text = _set.CoreLockMin.ToString();
        CoreMaxBox.Text = _set.CoreLockMax.ToString();
        MemMinBox.Text = _set.MemLockMin.ToString();
        MemMaxBox.Text = _set.MemLockMax.ToString();
        GpuInfo.Text = GpuTuner.Available
            ? Loc.T("{0} · güç {1}–{2} W (varsayılan {3} W)", GpuTuner.Name, GpuTuner.PowerMin.ToString("F0"), GpuTuner.PowerMax.ToString("F0"), GpuTuner.PowerDefault.ToString("F0"))
            : GpuTuner.LastError;
        TuningStatus.Text = GpuTuner.Available ? "" : "Hız aşırtma kullanılamıyor.";
    }

    void Power_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PowerLabel != null)
            PowerLabel.Text = Loc.T("{0} W (varsayılan {1} W)", e.NewValue.ToString("F0"), GpuTuner.PowerDefault.ToString("F0"));
    }

    void ApplyUndervolt(string preset)
    {
        if (!GpuTuner.Available) return;
        var (percent, cap) = UndervoltPresets[preset];
        var watts = Math.Round(GpuTuner.PowerDefault * (1 - percent / 100.0));
        TuningStatus.Text = Loc.T("{0} uygulanıyor…", preset);
        Task.Run(() => GpuTuner.ApplyUndervolt(watts, cap)).ContinueWith(t => Dispatcher.Invoke(() =>
        {
            if (t.IsFaulted)
            {
                TuningStatus.Text = Loc.S("Hata: ") + t.Exception?.GetBaseException().Message;
                return;
            }
            PowerSlider.Value = Math.Clamp(watts, PowerSlider.Minimum, PowerSlider.Maximum);
            CoreMinBox.Text = "0";
            CoreMaxBox.Text = cap.ToString();
            _set.PowerLimit = watts;
            _set.CoreLockMin = 0;
            _set.CoreLockMax = cap;
            SaveSettings();
            TuningStatus.Text = $"{preset} · {watts:F0} W" + (cap > 0 ? $" · tavan {cap} MHz" : "") + " — kapatınca geri alınır.";
        }));
    }

    void UvBalanced_Click(object sender, RoutedEventArgs e) => ApplyUndervolt("Dengeli −10%");
    void UvQuiet_Click(object sender, RoutedEventArgs e) => ApplyUndervolt("Sessiz −20% + 1800 MHz");
    void UvEco_Click(object sender, RoutedEventArgs e) => ApplyUndervolt("Maks verim −30% + 1600 MHz");

    void ApplyTuning_Click(object sender, RoutedEventArgs e)
    {
        if (!GpuTuner.Available) return;
        _set.PowerLimit = PowerSlider.Value;
        _set.CoreLockMin = Int(CoreMinBox.Text);
        _set.CoreLockMax = Int(CoreMaxBox.Text);
        _set.MemLockMin = Int(MemMinBox.Text);
        _set.MemLockMax = Int(MemMaxBox.Text);
        SaveSettings();

        var p = _set;
        TuningStatus.Text = Loc.S("Uygulanıyor…");
        Task.Run(() =>
        {
            var msgs = new List<string>();
            var r = GpuTuner.SetPowerLimit(p.PowerLimit);
            if (r.Length > 0) msgs.Add(Loc.S("güç: ") + OneLine(r));
            r = GpuTuner.LockCore(p.CoreLockMin, p.CoreLockMax);
            if (r.Length > 0) msgs.Add(Loc.S("çekirdek: ") + OneLine(r));
            r = GpuTuner.LockMemory(p.MemLockMin, p.MemLockMax);
            if (r.Length > 0) msgs.Add(Loc.S("bellek: ") + OneLine(r));
            GpuTuner.MarkActive(true);
            return msgs.Count == 0 ? Loc.S("Uygulandı. Kapatınca geri alınır.") : string.Join(" · ", msgs);
        }).ContinueWith(t => Dispatcher.Invoke(() =>
            TuningStatus.Text = t.IsFaulted ? "Hata: " + t.Exception?.GetBaseException().Message : t.Result));
    }

    void ResetTuning_Click(object sender, RoutedEventArgs e)
    {
        TuningStatus.Text = Loc.S("Sıfırlanıyor…");
        Task.Run(() =>
        {
            GpuTuner.ResetAll();
            GpuTuner.MarkActive(false);
            return Loc.S("Varsayılan saat ve güç limiti geri yüklendi.");
        }).ContinueWith(t => Dispatcher.Invoke(() =>
        {
            PowerSlider.Value = GpuTuner.PowerDefault;
            CoreMinBox.Text = "0";
            CoreMaxBox.Text = "0";
            MemMinBox.Text = "0";
            MemMaxBox.Text = "0";
            TuningStatus.Text = t.IsFaulted ? Loc.S("Hata: ") + t.Exception?.GetBaseException().Message : t.Result;
            _set.CoreLockMin = _set.CoreLockMax = _set.MemLockMin = _set.MemLockMax = 0;
            SaveSettings();
        }));
    }

    void RestoreFans_Click(object sender, RoutedEventArgs e)
    {
        _sensors.RestoreFans();
        Fans.Clear();
        _sensors.RefreshFans();
        foreach (var f in _sensors.Fans) Fans.Add(f);
        StatusLine.Text = Loc.S("Fanlar firmware kontrolüne bırakıldı.");
    }

    static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out var v) ? v : 0;

    static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ').Trim();

    // ---------- live loop ----------

    void StartTimers()
    {
        _tick.Start();
        _watch.Start();
    }

    async void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var snap = await Task.Run(_sensors.Poll);
            Metrics.Apply(snap);
            _sessions.Sample(Metrics.Fps, snap.CpuTemp, snap.GpuTemp);

            SessionText = _sessions.Active
                ? $"{_sessions.CurrentName} · {_sessions.Elapsed:hh\\:mm\\:ss}"
                : Loc.S("Oyun bekleniyor");

            TrackForeground();

            if (_fps.Error.Length > 0) StatusLine.Text = "FPS: " + OneLine(_fps.Error);

            if (Limiter.Available)
            {
                var running = Limiter.Running();
                var text = running ? Loc.S("Sınırlayıcı: RTSS çalışıyor") : Loc.S("Sınırlayıcı: RTSS kapalı");
                if (LimiterStatusLine.Text != text) LimiterStatusLine.Text = text;
            }
        }
        catch (Exception ex) { StatusLine.Text = ex.Message; }
    }

    void TrackForeground()
    {
        var pid = FpsTracker.ForegroundPid();
        if (pid == 0 || pid == Environment.ProcessId) return;
        var name = FpsTracker.ProcessName(pid);
        if (name is "explorer" or "ShellExperienceHost" or "ApplicationFrameHost" or "StartMenuExperienceHost") return;
        Metrics.Target = name;
        _fps.Track(pid, name);
    }

    void OnWatch(object? sender, EventArgs e)
    {
        try
        {
            var running = RunningNames();
            var game = _rows.FirstOrDefault(r => r.Game.ExeName.Length >= 4 && running.Contains(r.Game.ExeName));

            if (game != null && !_sessions.Active) _sessions.Start(game.Game);
            else if (game == null && _sessions.Active)
            {
                _sessions.End();
                RefreshStats();
                SessionText = Loc.S("Oyun bekleniyor");
            }
        }
        catch (Exception ex) { StatusLine.Text = Loc.S("Oturum izleme: ") + ex.Message; }
    }

    static HashSet<string> RunningNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try { set.Add(p.ProcessName); } catch { } finally { p.Dispose(); }
        }
        return set;
    }

    // ---------- hotkeys ----------

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = (HwndSource)PresentationSource.FromVisual(this);
        src.AddHook(HotkeyHook);
        RegisterHotKey(src.Handle, 1, MOD_CONTROL | MOD_ALT, 0x4F); // Ctrl+Alt+O
        RegisterHotKey(src.Handle, 2, MOD_CONTROL | MOD_ALT, 0x4C); // Ctrl+Alt+L
    }

    IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        if (wParam.ToInt32() == 1) OverlayToggle_Click(this, new RoutedEventArgs());
        else if (wParam.ToInt32() == 2) MoveMode_Click(this, new RoutedEventArgs());
        handled = true;
        return IntPtr.Zero;
    }

    // ---------- persistence ----------

    void SaveSettings()
    {
        if (_overlay == null || !_overlay.IsLoaded) return;
        _set.Left = _overlay.Left;
        _set.Top = _overlay.Top;
        _set.Width = _overlay.Width;
        _set.Height = _overlay.Height;
        _set.Opacity = _overlay.Opacity;
        _set.ClickThrough = _overlay.ClickThrough;
        _set.Theme = ThemeBox?.SelectedItem as string ?? _set.Theme;
        Store.Save("settings.json", _set);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _tick.Stop();
        _watch.Stop();
        try { SaveSettings(); } catch { }
        _sessions.End();
        _fps.Dispose();
        _sensors.Dispose();
        GpuTuner.ResetAll();
        GpuTuner.MarkActive(false);
        _overlay.Close();
        base.OnClosing(e);
    }
}
