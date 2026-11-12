using System.Globalization;
using System.Text;

namespace GamePass2Steam.Core;

public sealed record SteamProfile(string Id, string? Name)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";
}

public static class SteamProfiles
{
    public static List<SteamProfile> Load(string steam)
    {
        var ids = Directory.GetDirectories(Path.Combine(steam, "userdata"))
            .Select(Path.GetFileName).OfType<string>().Where(id => uint.TryParse(id, out _)).ToList();
        string? metadata = null;
        try
        {
            string path = Path.Combine(steam, "config", "loginusers.vdf");
            using var file = File.OpenRead(path);
            if (file.Length <= 1024 * 1024)
            {
                using var reader = new StreamReader(file, new UTF8Encoding(false, true));
                metadata = reader.ReadToEnd();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException) { }
        return Resolve(ids, metadata);
    }

    public static List<SteamProfile> Resolve(IEnumerable<string> ids, string? metadata)
    {
        Dictionary<string, string?> names = [];
        try
        {
            if (metadata != null)
            {
                var root = new Reader(metadata).Read();
                var users = root.SingleOrDefault(n => n.Key.Equals("users", StringComparison.OrdinalIgnoreCase))?.Children;
                if (users != null)
                {
                    foreach (var group in users.GroupBy(n => n.Key))
                    {
                        // Public individual SteamID64s contain the userdata account ID in their low 32 bits.
                        const ulong individualBase = 76561197960265728;
                        if (group.Count() != 1 || !ulong.TryParse(group.Key, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
                            || id <= individualBase || id > individualBase + uint.MaxValue) continue;
                        var fields = group.Single().Children;
                        string? Field(string key) => fields?.SingleOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
                        string? name = Field("PersonaName");
                        if (string.IsNullOrWhiteSpace(name)) name = Field("AccountName");
                        names[(id - individualBase).ToString(CultureInfo.InvariantCulture)] = name;
                    }
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { names.Clear(); }
        return ids.Select(id => new SteamProfile(id, names.GetValueOrDefault(id))).ToList();
    }

    private sealed record Entry(string Key, string? Value, List<Entry>? Children);
    private sealed class Reader(string text)
    {
        private int position;
        public List<Entry> Read(int depth = 0)
        {
            if (depth > 16 || text.Length > 1024 * 1024) throw new InvalidDataException("Profile metadata exceeds limits.");
            List<Entry> entries = [];
            while (true)
            {
                SkipSpace();
                if (position == text.Length)
                {
                    if (depth != 0) throw new InvalidDataException("Unclosed profile object.");
                    return entries;
                }
                if (text[position] == '}')
                {
                    if (depth == 0) throw new InvalidDataException("Unexpected closing brace.");
                    position++; return entries;
                }
                string key = Token(); SkipSpace();
                if (position < text.Length && text[position] == '{')
                { position++; entries.Add(new(key, null, Read(depth + 1))); }
                else entries.Add(new(key, Token(), null));
            }
        }
        private void SkipSpace()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position])) { position++; continue; }
                if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
                { while (position < text.Length && text[position] != '\n') position++; continue; }
                break;
            }
        }
        private string Token()
        {
            SkipSpace();
            if (position == text.Length || text[position] is '{' or '}') throw new InvalidDataException("Expected profile field.");
            if (text[position] != '"')
            {
                int start = position;
                while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not '{' and not '}') position++;
                return text[start..position];
            }
            position++; StringBuilder value = new();
            while (position < text.Length)
            {
                char c = text[position++];
                if (c == '"') return value.ToString();
                if (c == '\\')
                {
                    if (position == text.Length) break;
                    char escaped = text[position++];
                    if (escaped is not '"' and not '\\') value.Append('\\');
                    c = escaped;
                }
                value.Append(c);
            }
            throw new InvalidDataException("Unclosed profile string.");
        }
    }
}
