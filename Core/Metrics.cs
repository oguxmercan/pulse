using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Pulse.Core;

/// <summary>Everything an overlay theme can bind to. Updated once per second on the UI thread.</summary>
public sealed class Metrics : Obs
{
    const int HistLen = 60;
    const double HistWidth = 160, HistHeight = 40, FpsCeiling = 240;

    readonly Queue<double> _hist = new();
    double? _fps, _frametime;
    double? _cpuLoad, _cpuTemp, _cpuFan, _cpuClock;
    double? _gpuLoad, _gpuTemp, _gpuHotspot, _gpuFan, _gpuFanPct, _gpuClock, _gpuPower, _vramUsed, _vramTotal;
    double? _ramUsed, _ramTotal;
    string _cpuName = "CPU", _gpuName = "GPU", _target = "—", _clock = "";
    Geometry _spark = Geometry.Empty;

    public Geometry Spark { get => _spark; private set => Set(ref _spark, value); }

    public string CpuName { get => _cpuName; set { if (Set(ref _cpuName, value)) Raise(nameof(CpuShort)); } }
    public string GpuName { get => _gpuName; set { if (Set(ref _gpuName, value)) Raise(nameof(GpuShort)); } }

    /// <summary>Model names without vendor/suffix noise so they fit a small overlay line.</summary>
    public string CpuShort => Short(CpuName);
    public string GpuShort => Short(GpuName);

    public string Target { get => _target; set => Set(ref _target, value); }
    public string Clock { get => _clock; set => Set(ref _clock, value); }
    public double? Fps { get => _fps; private set => Set(ref _fps, value); }
    public double? Frametime { get => _frametime; private set => Set(ref _frametime, value); }
    public double? CpuLoad { get => _cpuLoad; set => Set(ref _cpuLoad, value); }
    public double? CpuTemp { get => _cpuTemp; set => Set(ref _cpuTemp, value); }
    public double? CpuFan { get => _cpuFan; set => Set(ref _cpuFan, value); }
    public double? CpuClock { get => _cpuClock; set => Set(ref _cpuClock, value); }
    public double? GpuLoad { get => _gpuLoad; set => Set(ref _gpuLoad, value); }
    public double? GpuTemp { get => _gpuTemp; set => Set(ref _gpuTemp, value); }
    public double? GpuHotspot { get => _gpuHotspot; set => Set(ref _gpuHotspot, value); }
    public double? GpuFan { get => _gpuFan; set => Set(ref _gpuFan, value); }
    public double? GpuFanPct { get => _gpuFanPct; set => Set(ref _gpuFanPct, value); }
    public double? GpuClock { get => _gpuClock; set => Set(ref _gpuClock, value); }
    public double? GpuPower { get => _gpuPower; set => Set(ref _gpuPower, value); }
    public double? VramUsed { get => _vramUsed; set => Set(ref _vramUsed, value); }
    public double? VramTotal { get => _vramTotal; set => Set(ref _vramTotal, value); }
    public double? RamUsed { get => _ramUsed; set => Set(ref _ramUsed, value); }
    public double? RamTotal { get => _ramTotal; set => Set(ref _ramTotal, value); }

    public void Apply(Snapshot s)
    {
        CpuName = s.CpuName;
        GpuName = s.GpuName;
        CpuLoad = s.CpuLoad; CpuTemp = s.CpuTemp; CpuFan = s.CpuFan; CpuClock = s.CpuClock;
        GpuLoad = s.GpuLoad; GpuTemp = s.GpuTemp; GpuHotspot = s.GpuHotspot; GpuFan = s.GpuFan;
        GpuFanPct = s.GpuFanPct; GpuClock = s.GpuClock; GpuPower = s.GpuPower;
        VramUsed = s.VramUsed; VramTotal = s.VramTotal;
        RamUsed = s.RamUsed; RamTotal = s.RamTotal;
    }

    public void PushFps(double fps) => PushFps(fps, null);

    public void PushFps(double fps, double? frametime)
    {
        Fps = Math.Round(fps, 0);
        Frametime = frametime is double ft ? Math.Round(ft, 1) : (fps > 0.5 ? Math.Round(1000 / fps, 1) : null);
        _hist.Enqueue(Math.Clamp(fps, 0, FpsCeiling));
        while (_hist.Count > HistLen) _hist.Dequeue();
        Spark = BuildSpark();
    }

    public void ClearFps()
    {
        Fps = null; Frametime = null; _hist.Clear(); Spark = Geometry.Empty;
    }

    Geometry BuildSpark()
    {
        var pts = _hist.ToArray();
        if (pts.Length < 2) return Geometry.Empty;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, HistHeight - pts[0] / FpsCeiling * HistHeight), false, false);
            for (int i = 1; i < pts.Length; i++)
            {
                double x = i / (double)(HistLen - 1) * HistWidth;
                ctx.LineTo(new Point(x, HistHeight - pts[i] / FpsCeiling * HistHeight), true, false);
            }
        }
        geo.Freeze();
        return geo;
    }

    static string Short(string s)
    {
        // suffix first: "Ryzen 5 5600X 6-Core Processor" must lose " 6-Core Processor" before "Core " is stripped
        s = Regex.Replace(s, @"\s+(\d+-Core.*|\d+-Thread.*|Processor.*|CPU.*|with .*)$", "", RegexOptions.IgnoreCase);
        foreach (var p in new[] { "NVIDIA GeForce ", "NVIDIA ", "AMD ", "Intel(R) ", "Intel ", "Radeon ", "Core(TM) ", "Core " })
            s = s.Replace(p, "", StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }
}
