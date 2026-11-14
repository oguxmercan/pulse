using LibreHardwareMonitor.Hardware;

namespace Pulse.Core;

/// <summary>`Pulse.exe --selftest` — headless check of every subsystem, writes %LOCALAPPDATA%\Pulse\selftest.log.</summary>
public static class SelfTest
{

    static bool FpsInspector_Admin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static async Task RunAsync(string? fpsTarget = null)
    {
        var log = new List<string>();
        void L(string s) => log.Add(s);

        try
        {
            L($"admin={FpsInspector_Admin()} presentmon={File.Exists(Path.Combine(AppContext.BaseDirectory, "PresentMon.exe"))}");

            using var sensors = new Sensors();
            sensors.Open();
            var snap = sensors.Poll();
            L($"cpu={snap.CpuName} gpu={snap.GpuName} err=[{sensors.Error}]");
            L($"load={snap.CpuLoad} temp={snap.CpuTemp} clock={snap.CpuClock} fan={snap.CpuFan}");
            L($"gpuLoad={snap.GpuLoad} gpuTemp={snap.GpuTemp} hotspot={snap.GpuHotspot} gpuFan={snap.GpuFan}/{snap.GpuFanPct} clock={snap.GpuClock} power={snap.GpuPower}");
            L($"vram={snap.VramUsed}/{snap.VramTotal} ram={snap.RamUsed}/{snap.RamTotal}");
            L($"fans_controllable={sensors.Fans.Count}: " + string.Join(" | ", sensors.Fans.Select(f => $"{f.Name} {f.Min}-{f.Max}")));

            // Live probe: attach to the biggest foreign window and watch real frames for 5 s.
            var target = fpsTarget != null
                ? (System.Diagnostics.Process.GetProcessesByName(fpsTarget).FirstOrDefault(p => p.MainWindowHandle != 0)
                   ?? System.Diagnostics.Process.GetProcessesByName(fpsTarget).FirstOrDefault())
                : System.Diagnostics.Process.GetProcesses()
                    .Where(p => p.MainWindowHandle != 0 && p.ProcessName != "Pulse")
                    .OrderByDescending(p => p.WorkingSet64)
                    .FirstOrDefault();
            if (target != null)
            {
                using var fps = new FpsTracker();
                var got = new List<string>();
                fps.Sample += (v, ft) => { lock (got) got.Add($"{v:F1}@{ft:F1}ms"); };
                fps.Track((uint)target.Id, target.ProcessName);
                Thread.Sleep(6000);
                fps.Stop();
                lock (got) L($"fps_probe target={target.ProcessName} samples={got.Count} vals={string.Join(",", got)} err=[{fps.Error}]");
            }

            var metrics = new Metrics();
            metrics.Apply(snap);
            metrics.PushFps(144.2);
            metrics.PushFps(138);
            L($"metrics_fps={metrics.Fps} ft={metrics.Frametime} spark={!metrics.Spark.IsEmpty()}");

            GpuTuner.Probe();
            L($"clocks=" + GpuTuner.CurrentClocks().Replace('\n', ' '));

            var games = Games.Scan();
            L($"games={games.Count} stores=" + string.Join(",", games.GroupBy(g => g.Store).Select(g => $"{g.Key}:{g.Count()}")));
            foreach (var g in games.Take(8)) L($"  {g.Store,-10} {g.Name,-34} exe={g.ExeName} launch={g.LaunchCmd}");

            var cfg = Store.Load("settings.json", () => new Settings());
            L($"settings_theme={cfg.Theme} custom={cfg.Custom.Count} ids={string.Join(";", cfg.Custom.Select(c => c.Id))}");

            Store.SeedThemes();
            foreach (var f in Directory.EnumerateFiles(Store.ThemesDir, "*.xaml").OrderBy(x => x))
            {
                try
                {
                    var obj = System.Windows.Markup.XamlReader.Parse(File.ReadAllText(f));
                    L($"theme {Path.GetFileName(f)} -> {obj.GetType().Name}");
                }
                catch (Exception e) { L($"theme {Path.GetFileName(f)} -> FAIL {e.Message}"); }
            }

            var sessions = new Sessions();
            L($"sessions_loaded={sessions.Count}");
            L($"result=OK");
        }
        catch (Exception e)
        {
            L($"result=FAIL {e}");
        }

        Directory.CreateDirectory(Store.Dir);
        File.WriteAllLines(Path.Combine(Store.Dir, "selftest.log"), log);
        Environment.Exit(0);
    }

}
