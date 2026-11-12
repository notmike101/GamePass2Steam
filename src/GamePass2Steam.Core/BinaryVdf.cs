using System.Text;

namespace GamePass2Steam.Core;

public sealed record VdfNode(byte Type, string Name, byte[]? Data = null, List<VdfNode>? Children = null)
{
    public static VdfNode Object(string name) => new(0, name, Children: []);
    public static VdfNode String(string name, string value) => new(1, name, Encoding.UTF8.GetBytes(value));
    public static VdfNode Integer(string name, uint value) => new(2, name, BitConverter.GetBytes(value));
    public string? Text(string name) => Children?.SingleOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Type: 1, Data: { } data } ? Encoding.UTF8.GetString(data) : null;
}

public static class BinaryVdf
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static List<VdfNode> Read(byte[] bytes)
    {
        if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("Shortcut file exceeds 32 MB safety limit.");
        using var reader = new BinaryReader(new MemoryStream(bytes), Utf8);
        try
        {
            var result = ReadObject(reader, 0);
            if (reader.BaseStream.Position != bytes.Length) throw new InvalidDataException("Trailing shortcut data.");
            return result;
        }
        catch (Exception e) when (e is EndOfStreamException or DecoderFallbackException)
        { throw new InvalidDataException("Truncated or invalid shortcut data; original file was not changed.", e); }
    }

    private static List<VdfNode> ReadObject(BinaryReader reader, int depth)
    {
        if (depth > 32) throw new InvalidDataException("Shortcut nesting exceeds safety limit.");
        List<VdfNode> result = [];
        while (true)
        {
            byte type = reader.ReadByte();
            if (type == 8) return result;
            string name = Utf8.GetString(ReadTerminated(reader));
            if (type == 0) { result.Add(new(0, name, Children: ReadObject(reader, depth + 1))); continue; }
            byte[] data;
            if (type == 1) data = ReadTerminated(reader);
            else
            {
                int length = type switch { 2 or 3 or 4 or 6 => 4, 7 or 10 => 8, _ => throw new InvalidDataException($"Unsupported binary VDF type {type}; file was not changed.") };
                data = reader.ReadBytes(length);
                if (data.Length != length) throw new EndOfStreamException();
            }
            result.Add(new(type, name, data));
        }
    }

    private static byte[] ReadTerminated(BinaryReader reader)
    {
        using var data = new MemoryStream();
        byte b;
        while ((b = reader.ReadByte()) != 0) data.WriteByte(b);
        return data.ToArray();
    }

    public static byte[] Write(List<VdfNode> nodes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Utf8);
        WriteObject(writer, nodes);
        return stream.ToArray();
    }

    private static void WriteObject(BinaryWriter writer, List<VdfNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Name.Contains('\0')) throw new InvalidDataException("NUL in VDF key.");
            writer.Write(node.Type); writer.Write(Utf8.GetBytes(node.Name)); writer.Write((byte)0);
            if (node.Type == 0) WriteObject(writer, node.Children!);
            else
            {
                writer.Write(node.Data!);
                if (node.Type == 1) writer.Write((byte)0);
            }
        }
        writer.Write((byte)8);
    }
}
