using System.Diagnostics;

namespace Pulse.Core;

/// <summary>
/// Frame limiting through RivaTuner Statistics Server, which owns the injection and does
/// Front-Edge-Sync pacing. Pulse only manages profiles (per-game and global) and keeps RTSS running.
/// No injection, no driver hacks — if RTSS is missing the feature reports that instead of pretending.
/// </summary>
public static class Limiter
{
    public static string? ExePath { get; private set; }
    public static string? ProfilesDir { get; private set; }
    public static string Status { get; private set; } = "RTSS bulunamadı";
    public static bool Available => ExePath != null && ProfilesDir != null;

    public static void Probe()
    {
        var roots = new[]
        {
            @"C:\Program Files (x86)\RivaTuner Statistics Server",
            @"C:\Program Files\RivaTuner Statistics Server",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server"),
        };

        foreach (var root in roots.Distinct())
        {
            var exe = Path.Combine(root, "RTSS.exe");
            var profiles = Path.Combine(root, "Profiles");
            if (!File.Exists(exe) || !Directory.Exists(profiles)) continue;
            ExePath = exe;
            ProfilesDir = profiles;
            Status = Running() ? "RTSS çalışıyor" : Loc.S("RTSS hazır (başlatılacak)");
            return;
        }
        Status = Loc.S("RTSS kurulu değil — sınırlama için RTSS gerekir");
    }

    public static bool Running() => Process.GetProcessesByName("RTSS").Length > 0;

    /// <summary>RTSS enforces limits only while it runs.</summary>
    public static bool EnsureRunning()
    {
        if (!Available) return false;
        if (Running()) return true;
        try
        {
            Process.Start(new ProcessStartInfo(ExePath!) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(150);
                if (Running()) { Status = "RTSS çalışıyor"; return true; }
            }
        }
        catch (Exception e) { Status = Loc.S("RTSS başlatılamadı: ") + e.Message; }
        return Running();
    }

    public static string ProfilePath(string exeName) =>
        Path.Combine(ProfilesDir!, exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName + ".cfg" : exeName + ".exe.cfg");

    /// <summary>Current Limit value of a profile, or null when the profile/keys are absent.</summary>
    public static int? ReadLimit(string exeName)
    {
        if (!Available) return null;
        var path = ProfilePath(exeName);
        if (!File.Exists(path)) return null;
        var section = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { section = line; continue; }
            if (section.Equals("[Framerate]", StringComparison.OrdinalIgnoreCase) &&
                line.StartsWith("Limit=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line["Limit=".Length..], out var fps))
                return fps;
        }
        return null;
    }

    /// <summary>Writes Limit/LimitDenominator into the game's profile, preserving every other key.</summary>
    public static (bool ok, string message) SetLimit(string exeName, int fps)
    {
        if (!Available) return (false, "RTSS bulunamadı");

        var path = ProfilePath(exeName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var section = "";
        int limitIndex = -1, denomIndex = -1, framerateIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('['))
            {
                section = line;
                if (section.Equals("[Framerate]", StringComparison.OrdinalIgnoreCase)) framerateIndex = i;
                continue;
            }
            if (!section.Equals("[Framerate]", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("Limit=", StringComparison.OrdinalIgnoreCase)) limitIndex = i;
            else if (line.StartsWith("LimitDenominator=", StringComparison.OrdinalIgnoreCase)) denomIndex = i;
        }

        if (limitIndex >= 0) lines[limitIndex] = $"Limit={fps}";
        if (denomIndex >= 0) lines[denomIndex] = "LimitDenominator=1";

        if (limitIndex < 0 || denomIndex < 0)
        {
            var insert = new List<string>();
            if (limitIndex < 0) insert.Add($"Limit={fps}");
            if (denomIndex < 0) insert.Add("LimitDenominator=1");

            if (framerateIndex >= 0) lines.InsertRange(framerateIndex + 1, insert);
            else
            {
                if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
                lines.Add("[Framerate]");
                lines.AddRange(insert);
            }
        }

        try
        {
            Directory.CreateDirectory(ProfilesDir!);
            File.WriteAllLines(path, lines);
            var running = EnsureRunning();
            return (true, $"{(fps > 0 ? $"sınır {fps} fps" : "sınır kaldırıldı")} · {Path.GetFileName(path)}" +
                          (running ? "" : " · RTSS başlatılamadı"));
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "RTSS klasörüne yazılamadı (yönetici gerekli)");
        }
        catch (Exception e) { return (false, e.Message); }
    }

    public static (bool ok, string message) SetGlobalLimit(int fps)
    {
        if (!Available) return (false, "RTSS bulunamadı");
        var path = Path.Combine(ProfilesDir!, "Global");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var section = "";
        int limitIndex = -1, denomIndex = -1, framerateIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('['))
            {
                section = line;
                if (section.Equals("[Framerate]", StringComparison.OrdinalIgnoreCase)) framerateIndex = i;
                continue;
            }
            if (!section.Equals("[Framerate]", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("Limit=", StringComparison.OrdinalIgnoreCase)) limitIndex = i;
            else if (line.StartsWith("LimitDenominator=", StringComparison.OrdinalIgnoreCase)) denomIndex = i;
        }

        if (limitIndex >= 0) lines[limitIndex] = $"Limit={fps}";
        if (denomIndex >= 0) lines[denomIndex] = "LimitDenominator=1";
        if (limitIndex < 0 || denomIndex < 0)
        {
            var insert = new List<string>();
            if (limitIndex < 0) insert.Add($"Limit={fps}");
            if (denomIndex < 0) insert.Add("LimitDenominator=1");
            if (framerateIndex >= 0) lines.InsertRange(framerateIndex + 1, insert);
            else { if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add(""); lines.Add("[Framerate]"); lines.AddRange(insert); }
        }

        try
        {
            File.WriteAllLines(path, lines);
            var running = EnsureRunning();
            return (true, $"global sınır {fps} fps" + (running ? "" : " · RTSS başlatılamadı"));
        }
        catch (UnauthorizedAccessException) { return (false, "RTSS klasörüne yazılamadı (yönetici gerekli)"); }
        catch (Exception e) { return (false, e.Message); }
    }
}
