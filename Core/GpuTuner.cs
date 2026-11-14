using System.Diagnostics;
using System.Globalization;

namespace Pulse.Core;

/// <summary>
/// NVIDIA-only tuning through the documented nvidia-smi interface: power limit + clock locks.
/// Every value is volatile and restored on exit; a crash is covered by ResetAll() at next start.
/// </summary>
public static class GpuTuner
{
    static string? _smi;
    public static bool Available => _smi != null;
    public static string Name { get; private set; } = "";
    public static double PowerDefault { get; private set; }
    public static double PowerMin { get; private set; }
    public static double PowerMax { get; private set; }
    public static string LastError { get; private set; } = "";

    public static void Probe()
    {
        _smi = Locate();
        if (_smi == null) { LastError = "nvidia-smi bulunamadı (AMD/Intel kartlar desteklenmiyor)."; return; }
        var (_, o) = Run("--query-gpu=name,power.default_limit,power.min_limit,power.max_limit --format=csv,noheader,nounits");
        var parts = o.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 4) { LastError = o; _smi = null; return; }
        Name = parts[0];
        PowerDefault = D(parts[1]);
        PowerMin = D(parts[2]);
        PowerMax = D(parts[3]);
    }

    static double D(string s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static string Marker => Path.Combine(Store.Dir, "tuning-active");

    /// <summary>Marks that non-default clocks/power are applied, so a crash is repaired on next start.</summary>
    public static void MarkActive(bool on)
    {
        try
        {
            if (on) { Directory.CreateDirectory(Store.Dir); File.WriteAllText(Marker, "1"); }
            else if (File.Exists(Marker)) File.Delete(Marker);
        }
        catch { }
    }

    /// <summary>Undoes leftovers from a killed process. Returns true if anything was reset.</summary>
    public static bool ResetStale()
    {
        if (!File.Exists(Marker)) return false;
        ResetAll();
        MarkActive(false);
        return true;
    }

    public static string SetPowerLimit(double watts)
    {
        var w = Math.Clamp(Math.Round(watts), PowerMin, PowerMax);
        return Run($"-pl {w.ToString(CultureInfo.InvariantCulture)}").output;
    }

    /// <summary>Locks core clocks. min==max pins the clock; min&lt;max sets a floor band.</summary>
    public static string LockCore(int minMhz, int maxMhz)
    {
        if (minMhz <= 0 && maxMhz <= 0) return Run("-rgc").output;
        var arg = maxMhz > minMhz ? $"{minMhz},{maxMhz}" : $"{minMhz}";
        return Run($"-lgc {arg}").output;
    }

    public static string LockMemory(int minMhz, int maxMhz)
    {
        if (minMhz <= 0 && maxMhz <= 0) return Run("-rmc").output;
        var arg = maxMhz > minMhz ? $"{minMhz},{maxMhz}" : $"{minMhz}";
        return Run($"-lmc {arg}").output;
    }

    /// <summary>
    /// Undervolt without NVAPI: lower the power limit and cap the core clock ceiling.
    /// Both are documented, reversible and undone by ResetAll().
    /// </summary>
    public static string ApplyUndervolt(double watts, int coreCeiling)
    {
        var msgs = new List<string>();
        var r = SetPowerLimit(watts);
        if (r.Length > 0) msgs.Add("güç: " + r);
        r = coreCeiling > 0 ? Run($"-lgc 0,{coreCeiling}").output : Run("-rgc").output;
        if (r.Length > 0) msgs.Add("saat: " + r);
        MarkActive(true);
        return string.Join(" | ", msgs);
    }

    /// <summary>Unlocks clocks and puts the power limit back. Safe when nothing was applied.</summary>
    public static void ResetAll()
    {
        if (_smi == null) return;
        Run("-rgc");
        Run("-rmc");
        if (PowerDefault > 0) Run($"-pl {PowerDefault.ToString(CultureInfo.InvariantCulture)}");
    }

    public static string CurrentClocks()
    {
        if (_smi == null) return "";
        var (_, o) = Run("--query-gpu=clocks.gr,clocks.mem,power.limit --format=csv,noheader,nounits");
        return o;
    }

    static (int code, string output) Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo(_smi!, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd().Trim();
            var e = p.StandardError.ReadToEnd().Trim();
            p.WaitForExit(10000);
            var text = string.IsNullOrWhiteSpace(e) ? o : $"{o} {e}".Trim();
            if (p.ExitCode != 0) LastError = text;
            return (p.ExitCode, text);
        }
        catch (Exception e2)
        {
            LastError = e2.Message;
            return (-1, e2.Message);
        }
    }

    static string? Locate()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            @"C:\Windows\System32\nvidia-smi.exe",
        };
        return candidates.FirstOrDefault(File.Exists) ?? "nvidia-smi.exe";
    }
}
