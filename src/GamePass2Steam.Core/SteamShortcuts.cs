using System.Text;

namespace GamePass2Steam.Core;

public static class SteamShortcuts
{
    private const string Marker = "GamePass2Steam:";
    public static List<VdfNode> Entries(List<VdfNode> root)
    {
        if (root.Count != 1 || root[0].Name != "shortcuts" || root[0].Type != 0) throw new InvalidDataException("Expected one shortcuts object.");
        var entries = root[0].Children!;
        foreach (var entry in entries)
        {
            if (entry.Type != 0 || !uint.TryParse(entry.Name, out _) || entry.Children!.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Malformed or ambiguous shortcut entry.");
            if (entry.Children!.Any(n => n.Name.Equals("tags", StringComparison.OrdinalIgnoreCase) && n.Type != 0)) throw new InvalidDataException("Invalid shortcut tags.");
        }
        return entries;
    }
    public static string? ManagedId(VdfNode entry) => entry.Children!.FirstOrDefault(n => n.Name.Equals("tags", StringComparison.OrdinalIgnoreCase))?.Children?
        .Where(n => n.Type == 1).Select(n => Encoding.UTF8.GetString(n.Data!)).FirstOrDefault(s => s.StartsWith(Marker, StringComparison.Ordinal))?[Marker.Length..];

    public static void Update(List<VdfNode> root, IEnumerable<Game> games, string launcher, bool remove)
    {
        if (!Path.IsPathFullyQualified(launcher) || launcher.IndexOfAny(['"', '\0', '\r', '\n']) >= 0) throw new InvalidDataException("Launcher must be an absolute executable path without quotes.");
        var entries = Entries(root);
        foreach (var game in games.DistinctBy(g => g.Id))
        {
            GameMetadata.ValidateIdentity(game.Id);
            var matches = entries.Where(e => ManagedId(e) == game.Id).ToList();
            if (matches.Count > 1) throw new InvalidDataException("Multiple managed shortcuts have the same identity. Remove duplicates in Steam before retrying.");
            if (remove) { foreach (var match in matches) entries.Remove(match); continue; }
            VdfNode entry;
            if (matches.Count == 0)
            {
                entry = VdfNode.Object(entries.Count.ToString());
                uint appId = Crc32(Encoding.UTF8.GetBytes(Marker + game.Id)) | 0x80000000;
                if (entries.Any(e => e.Children!.Any(n => n.Name.Equals("appid", StringComparison.OrdinalIgnoreCase) && n.Type == 2 && BitConverter.ToUInt32(n.Data!) == appId))) throw new InvalidDataException("Steam shortcut ID collision; no files changed.");
                entry.Children!.AddRange([VdfNode.Integer("appid", appId), VdfNode.String("appname", game.Name), VdfNode.String("icon", ""), VdfNode.String("ShortcutPath", ""), VdfNode.Integer("IsHidden", 0), VdfNode.Integer("AllowDesktopConfig", 1), VdfNode.Integer("AllowOverlay", 1), VdfNode.Integer("OpenVR", 0), VdfNode.Integer("Devkit", 0), VdfNode.String("DevkitGameID", ""), VdfNode.Integer("LastPlayTime", 0)]);
                var tags = VdfNode.Object("tags"); tags.Children!.Add(VdfNode.String("0", Marker + game.Id)); entry.Children.Add(tags); entries.Add(entry);
            }
            else entry = matches[0];
            Set(entry, "Exe", '"' + launcher + '"');
            Set(entry, "StartDir", '"' + Path.GetDirectoryName(launcher)! + '"');
            Set(entry, "LaunchOptions", "--launch \"" + game.Id + "\"");
        }
        for (int i = 0; i < entries.Count; i++) entries[i] = entries[i] with { Name = i.ToString() };
    }
    private static void Set(VdfNode entry, string key, string value)
    {
        int index = entry.Children!.FindIndex(n => n.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        var node = VdfNode.String(index < 0 ? key : entry.Children[index].Name, value);
        if (index < 0) entry.Children.Add(node); else entry.Children[index] = node;
    }
    private static uint Crc32(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in bytes) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); }
        return ~crc;
    }
}

public sealed class ShortcutStore(string path, Func<bool> steamRunning)
{
    public List<VdfNode> Read() => File.Exists(path) ? BinaryVdf.Read(File.ReadAllBytes(path)) : [VdfNode.Object("shortcuts")];
    public string? Apply(IEnumerable<Game> games, string launcher, bool remove) => Change(root => SteamShortcuts.Update(root, games, launcher, remove));
    public string? Restore(string backup)
    {
        var restored = BinaryVdf.Read(File.ReadAllBytes(backup)); SteamShortcuts.Entries(restored);
        return Change(root => { root.Clear(); root.AddRange(restored); });
    }
    private string? Change(Action<List<VdfNode>> transform)
    {
        if (steamRunning()) throw new IOException("Exit Steam using Steam > Exit, then retry. GamePass2Steam will not force-close it.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var guard = new FileStream(path + ".gp2s.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        bool existed = File.Exists(path);
        byte[] original = existed ? File.ReadAllBytes(path) : [];
        var root = existed ? BinaryVdf.Read(original) : new List<VdfNode> { VdfNode.Object("shortcuts") };
        SteamShortcuts.Entries(root); transform(root);
        byte[] output = BinaryVdf.Write(root); SteamShortcuts.Entries(BinaryVdf.Read(output));
        if (output.SequenceEqual(original)) return null;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string backup = path + ".gp2s-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".bak";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(output); file.Flush(true); }
            if (steamRunning()) throw new IOException("Steam started during the update; nothing was replaced. Exit Steam and retry.");
            if (existed != File.Exists(path) || (existed && !File.ReadAllBytes(path).SequenceEqual(original))) throw new IOException("Shortcuts changed during the update; retry after closing other editors.");
            if (existed) File.Replace(temp, path, backup); else File.Move(temp, path);
            return existed ? backup : "Created new shortcuts.vdf (no previous file to back up).";
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
