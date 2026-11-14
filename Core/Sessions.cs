namespace Pulse.Core;

public sealed class SessionRecord
{
    public string GameId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public DateTime Start { get; set; }
    public double Seconds { get; set; }
    public double AvgFps { get; set; }
    public double AvgCpuTemp { get; set; }
    public double AvgGpuTemp { get; set; }
    public double MaxCpuTemp { get; set; }
    public double MaxGpuTemp { get; set; }
}

public sealed class GameStat
{
    public string Name = "";
    public string Store = "";
    public TimeSpan Played;
    public int Count;
    public DateTime Last;
    public double AvgFps, AvgGpuTemp, MaxGpuTemp, MaxCpuTemp;
}

/// <summary>Times each game session and accumulates its FPS/temperature samples.</summary>
public sealed class Sessions
{
    readonly List<SessionRecord> _all = Store.Load("sessions.json", () => new List<SessionRecord>());
    readonly List<double> _fps = new(), _cpu = new(), _gpu = new();
    SessionRecord? _cur;

    public bool Active => _cur != null;
    public string CurrentName => _cur?.Name ?? "";
    public TimeSpan Elapsed => _cur == null ? TimeSpan.Zero : DateTime.Now - _cur.Start;
    public int Count => _all.Count;

    public void Start(Game g)
    {
        End();
        _cur = new SessionRecord { GameId = g.Id, Name = g.Name, Store = g.Store, Start = DateTime.Now };
        _fps.Clear(); _cpu.Clear(); _gpu.Clear();
    }

    public void Sample(double? fps, double? cpuTemp, double? gpuTemp)
    {
        if (_cur == null) return;
        if (fps is > 0 && fps < 1000) _fps.Add(fps.Value);
        if (cpuTemp is > 0) _cpu.Add(cpuTemp.Value);
        if (gpuTemp is > 0) _gpu.Add(gpuTemp.Value);
    }

    /// <summary>Average of a sample set ignores the first samples so loading spikes don't skew it.</summary>
    static double Avg(List<double> v) => v.Count == 0 ? 0 : Math.Round(v.Skip(v.Count / 10).DefaultIfEmpty(0).Average(), 0);

    public void End(bool force = false)
    {
        if (_cur == null) return;
        _cur.Seconds = Math.Round((DateTime.Now - _cur.Start).TotalSeconds);
        _cur.AvgFps = Avg(_fps);
        _cur.AvgCpuTemp = Avg(_cpu);
        _cur.AvgGpuTemp = Avg(_gpu);
        _cur.MaxCpuTemp = _cpu.Count == 0 ? 0 : Math.Round(_cpu.Max());
        _cur.MaxGpuTemp = _gpu.Count == 0 ? 0 : Math.Round(_gpu.Max());
        if (force || _cur.Seconds >= 20) _all.Add(_cur);
        if (_all.Count > 500) _all.RemoveRange(0, _all.Count - 500);
        Store.Save("sessions.json", _all);
        _cur = null;
    }

    public Dictionary<string, GameStat> Stats() => _all
        .GroupBy(r => r.GameId)
        .ToDictionary(g => g.Key, g =>
        {
            var withFps = g.Where(r => r.AvgFps > 0).ToList();
            var withGpu = g.Where(r => r.AvgGpuTemp > 0).ToList();
            return new GameStat
            {
                Name = g.First().Name,
                Store = g.First().Store,
                Played = TimeSpan.FromSeconds(g.Sum(r => r.Seconds)),
                Count = g.Count(),
                Last = g.Max(r => r.Start),
                AvgFps = withFps.Count == 0 ? 0 : Math.Round(withFps.Average(r => r.AvgFps)),
                AvgGpuTemp = withGpu.Count == 0 ? 0 : Math.Round(withGpu.Average(r => r.AvgGpuTemp)),
                MaxGpuTemp = g.Max(r => r.MaxGpuTemp),
                MaxCpuTemp = g.Max(r => r.MaxCpuTemp),
            };
        });
}
