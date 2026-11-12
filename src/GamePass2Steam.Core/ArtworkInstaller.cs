using System.Text.Json;

namespace GamePass2Steam.Core;

public sealed class ArtworkInstaller(SteamGridDb api, Func<bool> steamRunning, Action<byte[]> validateImage)
{
    private sealed record SavedMatch(string Identity, ArtworkGame Game);
    public static string Stem(uint appId, ArtworkKind kind) => appId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        (kind switch { ArtworkKind.Portrait => "p", ArtworkKind.Landscape => "", ArtworkKind.Hero => "_hero", ArtworkKind.Logo => "_logo", _ => "_icon" });

    private static bool Exists(string directory, string stem) => Directory.Exists(directory) && Directory.EnumerateFiles(directory, stem + ".*")
        .Any(path => new[] { ".png", ".jpg", ".jpeg", ".webp", ".ico", ".tga" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

    private static (uint AppId, string? Icon) Shortcut(ShortcutStore store, string id)
    {
        var entries = SteamShortcuts.Entries(store.Read());
        var entry = entries.Single(e => SteamShortcuts.ManagedId(e) == id);
        var node = entry.Children!.Single(n => n.Name.Equals("appid", StringComparison.OrdinalIgnoreCase));
        if (node.Type != 2 || node.Data?.Length != 4) throw new InvalidDataException("Shortcut has no usable AppID.");
        uint appId = BitConverter.ToUInt32(node.Data);
        if (entries.Count(e => e.Children!.Any(n => n.Name.Equals("appid", StringComparison.OrdinalIgnoreCase) && n.Type == 2 && BitConverter.ToUInt32(n.Data!) == appId)) != 1)
            throw new InvalidDataException("Shortcut AppID is ambiguous.");
        var icon = entry.Children!.SingleOrDefault(n => n.Name.Equals("icon", StringComparison.OrdinalIgnoreCase));
        return (appId, icon != null && icon.Type != 1 ? "Unsupported existing icon metadata" : entry.Text("icon"));
    }

    public static bool Missing(ShortcutStore store, string id)
    {
        var shortcut = Shortcut(store, id); string directory = Path.Combine(store.ConfigDirectory, "grid");
        return Enum.GetValues<ArtworkKind>().Any(k => !(k == ArtworkKind.Icon && !string.IsNullOrWhiteSpace(shortcut.Icon))
            && (!Exists(directory, Stem(shortcut.AppId, k)) || k == ArtworkKind.Icon && File.Exists(Path.Combine(directory, Stem(shortcut.AppId, k) + ".png.gp2s-source.json"))));
    }

    public static ArtworkGame? RememberedMatch(ShortcutStore store, string id)
    {
        try
        {
            var shortcut = Shortcut(store, id);
            string path = Path.Combine(store.ConfigDirectory, "grid", shortcut.AppId + ".gp2s-match.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 8192) return null;
            var saved = JsonSerializer.Deserialize<SavedMatch>(File.ReadAllText(path));
            return saved?.Identity == id && saved.Game is { Id: > 0, Name: { Length: > 0 and <= 500 } } ? saved.Game : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public async Task<(int Installed, List<string> Warnings)> Fill(ShortcutStore store, string id, ArtworkGame match, CancellationToken cancellation)
    {
        var shortcut = Shortcut(store, id); string directory = Path.Combine(store.ConfigDirectory, "grid");
        if (steamRunning()) throw new ArtworkUnavailableException("Steam started; artwork changes stopped. Exit Steam and retry.", true);
        Directory.CreateDirectory(directory);
        string matchPath = Path.Combine(directory, shortcut.AppId + ".gp2s-match.json");
        if (!File.Exists(matchPath))
        {
            using var file = new FileStream(matchPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(file, new SavedMatch(id, match));
        }
        int installed = 0; List<string> warnings = [];
        foreach (var kind in Enum.GetValues<ArtworkKind>())
        {
            cancellation.ThrowIfCancellationRequested();
            string stem = Stem(shortcut.AppId, kind);
            if (kind == ArtworkKind.Icon && !string.IsNullOrWhiteSpace(shortcut.Icon)) continue;
            try
            {
                if (Exists(directory, stem))
                {
                    string icon = Path.Combine(directory, stem + ".png");
                    if (kind == ArtworkKind.Icon && File.Exists(icon) && File.Exists(icon + ".gp2s-source.json")) store.FillIcon(id, shortcut.AppId, icon);
                    continue;
                }
                var asset = await api.Asset(match.Id, kind, cancellation);
                if (asset == null) { warnings.Add($"{kind}: no static PNG available"); continue; }
                byte[] bytes = await api.Download(asset, cancellation); validateImage(bytes);
                if (steamRunning()) throw new ArtworkUnavailableException("Steam started; artwork changes stopped. Exit Steam and retry.", true);
                var current = Shortcut(store, id);
                if (current.AppId != shortcut.AppId) throw new IOException("Shortcut changed during artwork lookup; retry.");
                if (kind == ArtworkKind.Icon && !string.IsNullOrWhiteSpace(current.Icon) || Exists(directory, stem)) continue;
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, stem + ".png"), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
                    if (steamRunning()) throw new ArtworkUnavailableException("Steam started; artwork changes stopped. Exit Steam and retry.", true);
                    if (Exists(directory, stem)) continue;
                    File.Move(temp, path, overwrite: false);
                    installed++;
                    File.WriteAllText(path + ".gp2s-source.json", JsonSerializer.Serialize(new { Provider = "SteamGridDB", Game = match, AssetId = asset.Id, asset.Author, Source = asset.Url.AbsoluteUri }));
                    if (kind == ArtworkKind.Icon) store.FillIcon(id, shortcut.AppId, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            catch (ArtworkUnavailableException e) when (e.Stop) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException)
            { warnings.Add($"{kind}: {SafeError(e)}"); }
        }
        return (installed, warnings);
    }

    public static string SafeError(Exception error) => error is ArtworkUnavailableException ? error.Message
        : error is OperationCanceledException ? "Artwork canceled."
        : "Artwork could not be read or saved. Existing artwork and shortcuts were preserved.";
}
