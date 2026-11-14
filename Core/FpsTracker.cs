using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulse.Core;

/// <summary>
/// FPS/frametime of the tracked process from Intel PresentMon (ETW, needs admin).
/// PresentMon 2.x keeps its CSV open exclusively and never flushes to stdout, so each sample is
/// a one-second capture: run, stop, read, repeat. Shortcut: ~1.3 s update cadence — upgrade if
/// PresentMon grows a shared/streaming output mode.
/// </summary>
public sealed class FpsTracker : IDisposable
{
    const int CaptureMs = 3000;
    static readonly string ExePath = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");

    CancellationTokenSource? _cts;
    string _matchArg = "--process_name", _matchValue = "", _session = "", _csv = "";

    public string Error { get; private set; } = "";
    public bool Running => _pid != 0;
    uint _pid;

    /// <summary>Raised ~0.7x/s with (fps, average frametime ms).</summary>
    public event Action<double, double>? Sample;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public static uint ForegroundPid()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid;
    }

    public static string ProcessName(uint pid)
    {
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch { return "?"; }
    }

    /// <summary>Chrome and friends present from a child process, so match by exe name when we have one.</summary>
    public void Track(uint pid, string processName = "")
    {
        if (pid == _pid) return;   // the 1 s foreground poll must not restart an in-flight capture
        Stop();
        if (pid == 0) return;
        _pid = pid;
        // PresentMon's CLI mishandles quoted names, so a name with a space ("Schedule I.exe") is
        // matched by pid instead. Plain names still use --process_name so chrome's child presenters work.
        var byPid = processName.Length == 0 || processName.Any(char.IsWhiteSpace);
        _matchArg = byPid ? "--process_id" : "--process_name";
        _matchValue = byPid ? pid.ToString() : processName + ".exe";
        if (!File.Exists(ExePath)) { Error = Loc.S("PresentMon.exe eksik (uygulama klasöründe olmalı)."); return; }
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = Task.Run(() => LoopAsync(cts.Token));
    }

    async Task LoopAsync(CancellationToken ct)
    {
        var seq = 0;
        while (!ct.IsCancellationRequested)
        {
            // Globally unique per chunk: a re-track of the same pid used to repeat the name and
            // collide with a session that was still shutting down (PresentMon then exits 143).
            seq++;
            var token = $"{_pid}_{DateTime.UtcNow.Ticks}";
            _session = $"Pulse{token}";
            _csv = Path.Combine(Path.GetTempPath(), $"pulse-fps-{token}.csv");
            var ok = await CaptureAsync(ct);
            if (ct.IsCancellationRequested) return;
            if (!ok) await Task.Delay(1500, ct).ContinueWith(_ => { });
            else await Task.Delay(60, ct).ContinueWith(_ => { });
        }
    }

    async Task<bool> CaptureAsync(CancellationToken ct)
    {
        try { File.Delete(_csv); } catch { }
        Process? pm = null;
        try
        {
            var psi = new ProcessStartInfo(ExePath)
            {
                UseShellExecute = true,          // PresentMon is a console app; give it a real launch context
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath(),
            };
            // ArgumentList, not a built string: PresentMon mishandles quoted paths in Arguments.
            foreach (var arg in new[]
            {
                _matchArg, _matchValue,
                "--output_file", _csv,
                "--no_console_stats",
                "--session_name", _session,
                "--v1_metrics",
            })
                psi.ArgumentList.Add(arg);

            pm = new Process { StartInfo = psi };
            pm.Start();

            await Task.Delay(CaptureMs, ct);
            if (ct.IsCancellationRequested) return false;

            var exitedEarly = pm.HasExited;
            try { if (!pm.HasExited) pm.Kill(); } catch { }
            try { pm.WaitForExit(3000); } catch { }
            StopSession(_session);
            var errText = "";
            try { errText = pm.StandardError.ReadToEnd().Trim(); } catch { }

            var lines = await ReadUnlockedAsync();
            if (lines.Count == 0)
            {
                // PresentMon exits 143 and writes nothing when it cannot create its trace session
                // (leftover sessions). Surface it instead of silently showing "--".
                Error = errText.Length > 0
                    ? errText
                    : Loc.S("ETW oturumu açılamadı (logman temizliği gerek)");
                return false;
            }

            var (fps, ft) = Parse(lines);
            if (fps <= 0) return false;
            Error = "";
            Sample?.Invoke(fps, ft);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception e)
        {
            Error = e.Message;
            return false;
        }
        finally
        {
            try { if (pm is { HasExited: false }) pm.Kill(); } catch { }
            try { pm?.Dispose(); } catch { }
            try { File.Delete(_csv); } catch { }
        }
    }

    /// <summary>The file stays locked until PresentMon really exits; give it a moment.</summary>
    async Task<List<string>> ReadUnlockedAsync()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                if (!File.Exists(_csv)) return new List<string>();
                using var fs = new FileStream(_csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8, true);
                var text = await sr.ReadToEndAsync();
                return text.Split('\n').Select(l => l.Trim('\r', '\uFEFF', ' ', '\0')).Where(l => l.Length > 0).ToList();
            }
            catch (IOException) { await Task.Delay(120); }
            catch (UnauthorizedAccessException) { await Task.Delay(120); }
        }
        return new List<string>();
    }

    /// <summary>fps = frames / measured span, frametime = mean msBetweenPresents.</summary>
    static (double fps, double frametime) Parse(List<string> lines)
    {
        int msCol = 9, timeCol = 7;
        double first = double.NaN, last = double.NaN, sum = 0;
        int n = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("Application", StringComparison.Ordinal))
            {
                var head = line.Split(',');
                for (int i = 0; i < head.Length; i++)
                {
                    var name = head[i].Trim();
                    if (name.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase)) msCol = i;
                    if (name.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase)) timeCol = i;
                }
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length <= Math.Max(msCol, timeCol)) continue;
            if (!double.TryParse(parts[msCol], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms <= 0) continue;
            if (!double.TryParse(parts[timeCol], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
            if (double.IsNaN(first)) first = t;
            last = t;
            sum += ms;
            n++;
        }

        if (n < 2) return (0, 0);
        var span = last - first;
        return (span > 0 ? (n - 1) / span : 0, sum / n);
    }

    static void StopSession(string name)
    {
        try
        {
            var psi = new ProcessStartInfo("logman", $"stop \"{name}\" -ets")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }

    /// <summary>Kills orphaned PresentMon processes and the ETW sessions a hard kill leaves behind.</summary>
    public static void CleanStaleSessions()
    {
        try
        {
            foreach (var pm in Process.GetProcessesByName("PresentMon"))
            {
                try
                {
                    // Only ours: another tool's PresentMon must keep its capture.
                    if (pm.MainModule?.FileName == ExePath) { pm.Kill(); pm.WaitForExit(2000); }
                }
                catch { }
                finally { pm.Dispose(); }
            }
        }
        catch { }

        try
        {
            var psi = new ProcessStartInfo("logman", "query -ets")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            foreach (var line in text.Split('\n'))
            {
                var name = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (name == null) continue;
                if (name.StartsWith("Pulse", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("TraceEventSession", StringComparison.OrdinalIgnoreCase))
                    StopSession(name);
            }
        }
        catch { }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _pid = 0;
        if (_session.Length > 0) StopSession(_session);
        _session = "";
    }

    public void Dispose() => Stop();
}
