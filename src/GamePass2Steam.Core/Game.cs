using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace GamePass2Steam.Core;

public sealed record Game(string Id, string Name, string Family, string InstallPath, List<string> Executables, string Kind, bool LikelyGame);

public static partial class GameMetadata
{
    [GeneratedRegex(@"^[A-Za-z0-9.\-]+_[A-Za-z0-9]+![A-Za-z0-9.\-]+$")]
    private static partial Regex IdentityPattern();
    public static void ValidateIdentity(string id)
    {
        if (id.Length > 512 || !IdentityPattern().IsMatch(id)) throw new InvalidDataException("Invalid registered application identity.");
    }
    public static XDocument Parse(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
            return XDocument.Load(reader);
        }
        catch (XmlException e) { throw new InvalidDataException("Invalid game manifest.", e); }
    }
    public static string ContainedPath(string root, string relative)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (Path.IsPathRooted(relative) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Executable escapes the installation folder.");
        return path;
    }
    public static List<string> Executables(string xml, string applicationId, string root) => Parse(xml).Descendants()
        .Where(e => e.Name.LocalName == "Executable" && (string?)e.Attribute("Id") == applicationId && !string.Equals((string?)e.Attribute("IsDevOnly"), "true", StringComparison.OrdinalIgnoreCase))
        .Select(e => ContainedPath(root, (string?)e.Attribute("Name") ?? throw new InvalidDataException("Missing executable name."))).ToList();
}
