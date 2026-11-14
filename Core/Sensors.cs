using LibreHardwareMonitor.Hardware;

namespace Pulse.Core;

public sealed class Snapshot
{
    public string CpuName = "CPU", GpuName = "GPU";
    public double? CpuLoad, CpuTemp, CpuFan, CpuClock;
    public double? GpuLoad, GpuTemp, GpuHotspot, GpuFan, GpuFanPct, GpuClock, GpuPower, VramUsed, VramTotal;
    public double? RamUsed, RamTotal;
}

public sealed class FanItem : Obs
{
    readonly IControl _ctl;
    float _value;
    public string Name { get; }
    public float Min { get; }
    public float Max { get; }

    public FanItem(string name, IControl ctl)
    {
        Name = name;
        _ctl = ctl;
        Min = ctl.MinSoftwareValue;
        Max = ctl.MaxSoftwareValue;
        // Show the firmware's current duty; some SuperIO chips report none, so start mid-range
        // instead of pretending the fan is stopped.
        _value = ctl.Sensor?.Value is float f && f > 0 ? f : (ctl.MinSoftwareValue + ctl.MaxSoftwareValue) / 2;
    }

    /// <summary>True once the user moved this slider; only touched fans are handed back on exit.</summary>
    public bool Touched { get; private set; }

    public float Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, Min, Max);
            if (!Set(ref _value, v)) return;
            Touched = true;
            try { _ctl.SetSoftware(v); } catch { /* hardware refused, keep UI value */ }
        }
    }

    public void Reset()
    {
        if (!Touched) return;
        try { _ctl.SetDefault(); } catch { }
        Touched = false;
    }
}

/// <summary>LibreHardwareMonitor host: one poll per second feeds every metric.</summary>
public sealed class Sensors : IDisposable
{
    readonly Computer _pc = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true,
    };

    IHardware? _cpu, _gpu, _mem;
    public string Error { get; private set; } = "";
    public bool Ready { get; private set; }
    public List<FanItem> Fans { get; } = new();

    public void Open()
    {
        try
        {
            _pc.Open();
            foreach (var hw in _pc.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu) _cpu ??= hw;
                else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel) _gpu ??= hw;
                else if (hw.HardwareType == HardwareType.Memory && hw.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) _mem = hw;
            }
            Ready = _cpu != null || _gpu != null;
            if (!Ready) Error = "Donanım sensörleri açılamadı — yönetici olarak çalıştırın.";
            RefreshFans();
        }
        catch (Exception e)
        {
            Error = e.Message;
        }
    }

    public Snapshot Poll()
    {
        var s = new Snapshot { CpuName = _cpu?.Name ?? "CPU", GpuName = _gpu?.Name ?? "GPU" };
        if (!Ready) return s;
        try
        {
            foreach (var hw in _pc.Hardware)
            {
                hw.Update();
                foreach (var sub in hw.SubHardware) sub.Update();
            }

            s.CpuLoad = Val(Pick(_cpu, SensorType.Load, "CPU Total", "Total"));
            s.CpuTemp = Val(Pick(_cpu, SensorType.Temperature, "Tctl", "Package", "Core Average", "Core Max"));
            s.CpuClock = Val(Pick(_cpu, SensorType.Clock, "Cores (Average)", "Core #1"));
            s.CpuFan = Val(Pick(_cpu?.Parent ?? _cpu, SensorType.Fan, "CPU Fan", "CPU_OPT", "Fan #1"));

            s.GpuLoad = Val(Pick(_gpu, SensorType.Load, "GPU Core", "D3D 3D"));
            s.GpuTemp = Val(Pick(_gpu, SensorType.Temperature, "GPU Core", "GPU"));
            s.GpuHotspot = Val(Pick(_gpu, SensorType.Temperature, "Hot Spot"));
            s.GpuFan = Val(Pick(_gpu, SensorType.Fan, "GPU Fan 1", "GPU Fan", "Fan"));
            s.GpuFanPct = Val(Pick(_gpu, SensorType.Control, "GPU Fan 1", "GPU Fan", "Fan"));
            s.GpuClock = Val(Pick(_gpu, SensorType.Clock, "GPU Core"));
            s.GpuPower = Val(Pick(_gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Board Power"));
            s.VramTotal = Val(Pick(_gpu, SensorType.SmallData, "GPU Memory Total"));
            s.VramUsed = Val(Pick(_gpu, SensorType.SmallData, "GPU Memory Used")) ?? Val(Pick(_gpu, SensorType.SmallData, "D3D Dedicated Memory Used"));
            // LHM: SmallData is MB, Data is GB. A vram total over 512 can only be MB.
            if (s.VramTotal > 512 && s.VramUsed is double vramMb) s.VramUsed = Math.Round(vramMb / 1024, 1);
            if (s.VramTotal > 512) s.VramTotal = Math.Round(s.VramTotal.Value / 1024, 0);

            var ramUsed = Val(Pick(_mem, SensorType.Data, "Memory Used"));
            var ramFree = Val(Pick(_mem, SensorType.Data, "Memory Available"));
            s.RamUsed = ramUsed is null ? null : Math.Round(ramUsed.Value, 1);
            s.RamTotal = ramUsed is null ? null : Math.Round(ramUsed.Value + (ramFree ?? 0), 1);
        }
        catch (Exception e)
        {
            Error = e.Message;
        }
        return s;
    }

    public void RefreshFans()
    {
        Fans.Clear();
        var seen = new HashSet<string>();
        foreach (var hw in _pc.Hardware)
            foreach (var leaf in Tree(hw))
                foreach (var sensor in leaf.Sensors)
                {
                    if (sensor.Control == null) continue;
                    var name = $"{leaf.Name} · {sensor.Name}";
                    if (!seen.Add(name)) continue;
                    Fans.Add(new FanItem(name, sensor.Control));
                }
    }

    /// <summary>Hands every fan back to firmware control — safe to call twice.</summary>
    public void RestoreFans()
    {
        foreach (var f in Fans) f.Reset();
    }

    public void Dispose()
    {
        RestoreFans();
        try { _pc.Close(); } catch { }
    }

    static IEnumerable<IHardware> Tree(IHardware hw)
    {
        yield return hw;
        foreach (var sub in hw.SubHardware)
            foreach (var x in Tree(sub)) yield return x;
    }

    static ISensor? Pick(IHardware? hw, SensorType type, params string[] names)
    {
        if (hw == null) return null;
        var all = Tree(hw).SelectMany(h => h.Sensors).Where(s => s.SensorType == type && s.Value != null).ToArray();
        foreach (var n in names)
        {
            var hit = all.FirstOrDefault(s => s.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return all.FirstOrDefault();
    }

    static double? Val(ISensor? s)
    {
        if (s?.Value is not float f) return null;
        // An unreadable sensor reports 0; show nothing instead of a fake 0 °C / 0 MHz.
        if ((s.SensorType is SensorType.Temperature or SensorType.Clock) && f <= 0) return null;
        return Math.Round(f, 1);
    }
}
