using System.Net.Http;
using System.Text.Json;

namespace Pulse.Core;

/// <summary>
/// Steam CDN artwork, cached on disk in four flavours: portrait capsule, wide header,
/// library hero (banner) and the transparent logo. Steam games resolve by appid; other
/// stores are matched by name through Steam's public store search (no API key).
/// </summary>
public static class Covers
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    static readonly Dictionary<string, string> Mapped = Store.Load("covers.json", () => new Dictionary<string, string>());

    public static string Dir { get; } = Path.Combine(Store.Dir, "covers");

    public static string? Portrait(Game g) => File(g, ".jpg");
    public static string? Wide(Game g) => File(g, ".wide.jpg");
    public static string? Hero(Game g) => File(g, ".hero.jpg");
    public static string? Logo(Game g) => File(g, ".logo.png");

    static string? File(Game g, string suffix)
    {
        var path = Path.Combine(Dir, Key(g) + suffix);
        return System.IO.File.Exists(path) ? path : null;
    }

    static string Key(Game g)
    {
        var raw = g.Id;
        foreach (var c in Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
        return raw.Replace(':', '_').Replace('\\', '_').Replace('/', '_');
    }

    /// <summary>Downloads whatever artwork is missing, one game at a time (polite to the CDN).</summary>
    public static async Task FetchAsync(IReadOnlyList<Game> games, Action<Game> updated, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Dir);
        foreach (var g in games)
        {
            if (ct.IsCancellationRequested) return;
            if (Portrait(g) != null && Wide(g) != null && Hero(g) != null) { updated(g); continue; }

            var id = await SteamIdAsync(g, ct);
            if (id == null) continue;

            var baseKey = Path.Combine(Dir, Key(g));
            await Get($"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_600x900.jpg", baseKey + ".jpg", ct);
            await Get($"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/header.jpg", baseKey + ".wide.jpg", ct);
            await Get($"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_hero.jpg", baseKey + ".hero.jpg", ct);
            updated(g);
            await Task.Delay(100, ct);
        }
    }

    static async Task<string?> SteamIdAsync(Game g, CancellationToken ct)
    {
        if (g.Id.StartsWith("steam:", StringComparison.Ordinal)) return g.Id["steam:".Length..];
        if (Mapped.TryGetValue(g.Id, out var known)) return known;

        try
        {
            var term = Uri.EscapeDataString(Clean(g.Name));
            var json = await Http.GetStringAsync($"https://store.steampowered.com/api/storesearch/?term={term}&l=tr&cc=tr", ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0) return null;

            string? best = null, firstName = null;
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var t) && t.GetString() != "app") continue;
                var id = item.TryGetProperty("id", out var i) ? i.GetInt64().ToString() : null;
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (id == null) continue;
                firstName ??= id;
                if (Clean(name).Equals(Clean(g.Name), StringComparison.OrdinalIgnoreCase)) { best = id; break; }
            }

            var result = best ?? firstName;
            if (result != null)
            {
                Mapped[g.Id] = result;
                Store.Save("covers.json", Mapped);
            }
            return result;
        }
        catch { return null; }
    }

    static string Clean(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == ' ' ? c : ' ');
        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Replace("the", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    static async Task<string?> Get(string url, string dest, CancellationToken ct)
    {
        if (System.IO.File.Exists(dest)) return dest;
        try
        {
            using var resp = await Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length < 1500) return null;
            var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8;
            var isPng = bytes[0] == 0x89 && bytes[1] == 0x50;
            if (!isJpeg && !isPng) return null;
            var tmp = dest + ".part";
            await System.IO.File.WriteAllBytesAsync(tmp, bytes, ct);
            System.IO.File.Move(tmp, dest, true);
            return dest;
        }
        catch { return null; }
    }
}
