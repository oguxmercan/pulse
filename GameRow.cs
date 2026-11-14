using System.Globalization;

namespace Pulse.Core;

public sealed class GameRow : Obs
{
    public Game Game { get; }
    public GameRow(Game g) { Game = g; RefreshArt(); }

    public string Name => Game.Name;
    public string Store => Game.Store;
    public string Initial => Game.Name.Length > 0 ? Game.Name[..1].ToUpperInvariant() : "?";

    string _portrait = "", _wide = "", _hero = "", _logo = "";
    public string Portrait { get => _portrait; private set => Set(ref _portrait, value); }
    public string Wide { get => _wide; private set => Set(ref _wide, value); }
    public string Hero { get => _hero; private set => Set(ref _hero, value); }
    public string Logo { get => _logo; private set => Set(ref _logo, value); }

    /// <summary>Re-reads the cached artwork paths (cheap; call after a download batch).</summary>
    public void RefreshArt()
    {
        Portrait = Covers.Portrait(Game) ?? "";
        Wide = Covers.Wide(Game) ?? "";
        Hero = Covers.Hero(Game) ?? "";
        Logo = Covers.Logo(Game) ?? "";
    }

    public string Diagonal => Wide.Length > 0 ? "" : "#1A2434";

    int _target;
    public int TargetFps { get => _target; set => Set(ref _target, value); }

    string _sub = "";
    public string Sub { get => _sub; set => Set(ref _sub, value); }

    GameStat? _stat;
    public GameStat? Stat => _stat;

    public void SetStat(GameStat? s)
    {
        _stat = s;
        if (s == null) { Sub = Loc.T("{0} · yeni", Store); Raise(nameof(Details)); return; }
        var parts = new List<string> { Store, Dur(s.Played) };
        if (s.AvgFps > 0) parts.Add($"{s.AvgFps:F0} fps");
        if (_target > 0 && s.AvgFps > 0) parts.Add(s.AvgFps >= _target * 0.97 ? Loc.S("hedefte") : Loc.S("hedef altı"));
        if (s.AvgGpuTemp > 0) parts.Add($"{s.AvgGpuTemp:F0}°C");
        Sub = string.Join(" · ", parts);
        Raise(nameof(Details));
    }

    public string Details
    {
        get
        {
            var d = new List<string> { Loc.T("Mağaza: {0}", Game.Store) };
            if (_stat != null)
            {
                d.Add(Loc.T("Toplam oynama: {0}", Dur(_stat.Played)));
                d.Add(Loc.T("Oturum sayısı: {0}", _stat.Count));
                d.Add(Loc.T("Ortalama FPS: {0}", _stat.AvgFps > 0 ? _stat.AvgFps.ToString("F0", CultureInfo.CurrentCulture) : "—") +
                      (_target > 0 ? Loc.T("   Hedef: {0} fps", _target) : ""));
                d.Add(Loc.T("Ortalama GPU: {0}", _stat.AvgGpuTemp > 0 ? _stat.AvgGpuTemp.ToString("F0", CultureInfo.CurrentCulture) + "°C" : "—") +
                      Loc.T("   En yüksek GPU: {0}", _stat.MaxGpuTemp > 0 ? _stat.MaxGpuTemp.ToString("F0", CultureInfo.CurrentCulture) + "°C" : "—") +
                      Loc.T("   En yüksek CPU: {0}", _stat.MaxCpuTemp > 0 ? _stat.MaxCpuTemp.ToString("F0", CultureInfo.CurrentCulture) + "°C" : "—"));
                d.Add(Loc.T("Son oynama: {0}", _stat.Last.ToString("dd.MM.yyyy HH:mm")));
            }
            else d.Add(Loc.S("Henüz oturum kaydı yok."));
            d.Add("");
            d.Add(Loc.T("Başlatma: {0}", Game.LaunchCmd));
            if (Game.ExeName.Length > 0) d.Add(Loc.T("Süreç: {0}", Game.ExeName + ".exe (oturum bunun açık olmasıyla sayılır)"));
            return string.Join(Environment.NewLine, d);
        }
    }

    static string Dur(TimeSpan t) => t.TotalMinutes < 1
        ? Loc.T("{0} sn", t.Seconds)
        : t.TotalHours >= 1
            ? Loc.T("{0} sa {1} dk", (int)t.TotalHours, t.Minutes)
            : Loc.T("{0} dk", t.Minutes);
}
