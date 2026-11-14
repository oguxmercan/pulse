using System.Text.Encodings.Web;
using System.Text.Json;

namespace Pulse.Core;

public sealed class Settings
{
    public string Lang { get; set; } = "tr";
    public string Theme { get; set; } = "minimal";
    public double Left { get; set; } = 24;
    public double Top { get; set; } = 24;
    public double Width { get; set; } = 330;
    public double Height { get; set; } = 130;
    public double Opacity { get; set; } = 1;
    public bool OverlayVisible { get; set; } = true;
    public bool ClickThrough { get; set; } = true;
    public int CoreLockMin { get; set; }
    public int CoreLockMax { get; set; }
    public int MemLockMin { get; set; }
    public int MemLockMax { get; set; }
    public double PowerLimit { get; set; }
    public List<Game> Custom { get; set; } = new();
    public Dictionary<string, int> Targets { get; set; } = new();
}

public static class Store
{
    public static string Dir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse");

    public static string ThemesDir { get; } = Path.Combine(Dir, "themes");

    static readonly JsonSerializerOptions J = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Load<T>(string file, Func<T> make)
    {
        try
        {
            var p = Path.Combine(Dir, file);
            if (File.Exists(p)) return JsonSerializer.Deserialize<T>(File.ReadAllText(p), J) ?? make();
        }
        catch { /* corrupt file: start clean rather than crash on launch */ }
        return make();
    }

    public static void Save<T>(string file, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, file), JsonSerializer.Serialize(value, J));
        }
        catch { }
    }

    /// <summary>Seed themes from the shipped folder; refresh presets the user has not edited.</summary>
    public static void SeedThemes()
    {
        Directory.CreateDirectory(ThemesDir);
        var shipped = Path.Combine(AppContext.BaseDirectory, "themes");
        if (!Directory.Exists(shipped)) return;

        var mark = Path.Combine(ThemesDir, ".seeded");
        var firstRun = !File.Exists(mark);
        var known = File.Exists(mark)
            ? File.ReadAllLines(mark).Select(l => l.Split('|')).Where(a => a.Length == 2)
                  .ToDictionary(a => a[0], a => a[1])
            : new Dictionary<string, string>();

        foreach (var f in Directory.EnumerateFiles(shipped, "*.xaml"))
        {
            var name = Path.GetFileName(f);
            var dest = Path.Combine(ThemesDir, name);
            var hash = Hash(f);
            // refresh presets we shipped before; leave files the user edited alone
            if (!firstRun && File.Exists(dest))
            {
                if (!known.TryGetValue(name, out var prev)) continue;
                if (Hash(dest) != prev) continue;
            }
            File.Copy(f, dest, overwrite: true);
            known[name] = hash;
        }

        File.WriteAllLines(mark, known.Select(kv => $"{kv.Key}|{kv.Value}"));
    }

    static string Hash(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
