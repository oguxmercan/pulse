using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pulse.Core;

public sealed class Game
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string LaunchCmd { get; set; } = "";   // launcher URI or exe path
    public string InstallDir { get; set; } = "";
    public string ExeName { get; set; } = "";     // process name without .exe

    public bool Installed => LaunchCmd.Contains("://") ? true : File.Exists(LaunchCmd);

    public void Launch()
    {
        if (LaunchCmd.Contains("://"))
            Process.Start(new ProcessStartInfo(LaunchCmd) { UseShellExecute = true });
        else if (File.Exists(LaunchCmd))
            Process.Start(new ProcessStartInfo(LaunchCmd)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(LaunchCmd) ?? "",
            });
    }
}

/// <summary>Reads installed games straight from each launcher's own metadata.</summary>
public static class Games
{
    static readonly HashSet<string> NotAGame = new(StringComparer.OrdinalIgnoreCase)
    {
        "Wallpaper Engine", "Lossless Scaling", "DSX", "SteamVR",
        "Steamworks Common Redistributables", "Proton EasyAntiCheat Runtime",
    };

    public static List<Game> Scan()
    {
        var all = new List<Game>();
        foreach (var scan in new Func<List<Game>>[] { Steam, Epic, Gog, Ea, Ubisoft, BattleNet })
        {
            try { all.AddRange(scan()); }
            catch (Exception e) { Debug.WriteLine($"scan failed: {e.Message}"); }
        }

        return all
            .Where(g => g.Name.Length > 0 && !NotAGame.Contains(g.Name))
            .GroupBy(g => g.Id)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static Game FromExe(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        return new Game
        {
            Id = "exe:" + exe,
            Name = name,
            Store = "Manuel",
            LaunchCmd = exe,
            InstallDir = Path.GetDirectoryName(exe) ?? "",
            ExeName = name,
        };
    }

    static List<Game> Steam()
    {
        var games = new List<Game>();
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steam)) return games;

        var roots = new List<string> { steam.Replace('/', '\\') };
        foreach (var vdf in new[] { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") })
        {
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (!roots.Contains(p, StringComparer.OrdinalIgnoreCase)) roots.Add(p);
            }
        }

        foreach (var root in roots)
        {
            var apps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var text = File.ReadAllText(acf);
                var appid = Rx(text, "appid");
                var name = Rx(text, "name");
                var dir = Rx(text, "installdir");
                if (appid == null || name == null) continue;
                var install = dir == null ? Path.Combine(apps, "common") : Path.Combine(apps, "common", dir);
                games.Add(new Game
                {
                    Id = "steam:" + appid,
                    Name = name,
                    Store = "Steam",
                    LaunchCmd = $"steam://rungameid/{appid}",
                    InstallDir = install,
                    ExeName = MainExeName(install),
                });
            }
        }
        return games;
    }

    static List<Game> Epic()
    {
        var games = new List<Game>();
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
        if (!File.Exists(file)) return games;

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (!doc.RootElement.TryGetProperty("InstallationList", out var list)) return games;
        foreach (var item in list.EnumerateArray())
        {
            var app = item.TryGetProperty("AppName", out var a) ? a.GetString() : null;
            var loc = item.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
            if (string.IsNullOrEmpty(app) || string.IsNullOrEmpty(loc)) continue;
            games.Add(new Game
            {
                Id = "epic:" + app,
                Name = Title(app),
                Store = "Epic",
                LaunchCmd = $"com.epicgames.launcher://apps/{app}?action=launch&silent=true",
                InstallDir = loc,
                ExeName = MainExeName(loc),
            });
        }
        return games;
    }

    static List<Game> Gog()
    {
        var games = new List<Game>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(@"SOFTWARE\GOG.com\Games");
            if (key == null) continue;
            foreach (var id in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(id);
                var name = sub?.GetValue("gameName") as string;
                var dir = sub?.GetValue("path") as string;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dir)) continue;
                var exe = FindMainExe(dir) ?? "";
                games.Add(new Game
                {
                    Id = "gog:" + id,
                    Name = name,
                    Store = "GOG",
                    LaunchCmd = exe,
                    InstallDir = dir,
                    ExeName = Path.GetFileNameWithoutExtension(exe),
                });
            }
        }
        return games;
    }

    static List<Game> Ea()
    {
        var games = new List<Game>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = root.OpenSubKey(@"SOFTWARE\EA Games");
        if (key == null) return games;
        foreach (var title in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(title);
            if (sub?.GetValue("Install Dir") is not string dir || !Directory.Exists(dir)) continue;
            var name = sub.GetValue("DisplayName") as string ?? title;
            var exe = FindMainExe(dir) ?? "";
            games.Add(new Game
            {
                Id = "ea:" + title,
                Name = name,
                Store = "EA App",
                LaunchCmd = exe,
                InstallDir = dir,
                ExeName = Path.GetFileNameWithoutExtension(exe),
            });
        }
        return games;
    }

    static List<Game> Ubisoft()
    {
        var games = new List<Game>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
            if (key == null) continue;
            foreach (var id in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(id);
                if (sub?.GetValue("InstallDir") is not string dir || !Directory.Exists(dir)) continue;
                var exe = FindMainExe(dir) ?? "";
                games.Add(new Game
                {
                    Id = "ubi:" + id,
                    Name = Path.GetFileName(dir.TrimEnd('\\')),
                    Store = "Ubisoft",
                    LaunchCmd = exe,
                    InstallDir = dir,
                    ExeName = Path.GetFileNameWithoutExtension(exe),
                });
            }
        }
        return games;
    }

    static List<Game> BattleNet()
    {
        var games = new List<Game>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = root.OpenSubKey(@"SOFTWARE\Blizzard Entertainment");
        if (key == null) return games;
        foreach (var title in key.GetSubKeyNames())
        {
            if (title is "Battle.net" or "Agent" or "Blizzard Entertainment") continue;
            using var sub = key.OpenSubKey(title);
            if (sub?.GetValue("InstallPath") is not string dir || !Directory.Exists(dir)) continue;
            var exe = FindMainExe(dir) ?? "";
            games.Add(new Game
            {
                Id = "bnet:" + title,
                Name = title,
                Store = "Battle.net",
                LaunchCmd = exe,
                InstallDir = dir,
                ExeName = Path.GetFileNameWithoutExtension(exe),
            });
        }
        return games;
    }

    /// <summary>Biggest exe in the install root, then in the usual engine subfolders, then one level down.</summary>
    public static string? FindMainExe(string dir)
    {
        var dirs = new List<string> { dir };
        foreach (var sub in new[] { "bin", "bin64", "bin\\x64", "Binaries", "Binaries\\Win64", "game", "Game", "x64", "Retail" })
            dirs.Add(Path.Combine(dir, sub));

        try
        {
            dirs.AddRange(Directory.EnumerateDirectories(dir)
                .Where(d => !Path.GetFileName(d).StartsWith("_CommonRedist", StringComparison.OrdinalIgnoreCase))
                .Take(12));
        }
        catch { }

        foreach (var d in dirs)
        {
            if (!Directory.Exists(d)) continue;
            var best = Directory.EnumerateFiles(d, "*.exe")
                .Where(f =>
                {
                    var n = Path.GetFileName(f);
                    return !n.Contains("unins", StringComparison.OrdinalIgnoreCase)
                        && !n.Contains("vcredist", StringComparison.OrdinalIgnoreCase)
                        && !n.Contains("dxsetup", StringComparison.OrdinalIgnoreCase)
                        && !n.Contains("crash", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
            if (best != null) return best;
        }
        return null;
    }

    static string MainExeName(string dir)
    {
        var exe = FindMainExe(dir);
        return exe == null ? "" : Path.GetFileNameWithoutExtension(exe);
    }

    static string? Rx(string text, string key)
    {
        var m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>"com.epicgames.launcher://…/Fortnite?…" → "Fortnite".</summary>
    static string Title(string appName)
    {
        var parts = appName.Split(':');
        var raw = parts.Length > 1 ? parts[1] : appName;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(raw.Replace('_', ' ').ToLowerInvariant());
    }
}
