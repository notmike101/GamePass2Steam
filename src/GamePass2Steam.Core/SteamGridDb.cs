using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GamePass2Steam.Core;

public sealed record ArtworkGame(long Id, string Name)
{
    public override string ToString() => $"{Name} (SteamGridDB {Id})";
}
public sealed record ArtworkAsset(long Id, Uri Url, string Author, int Score);
public sealed class ArtworkUnavailableException(string message, bool stop = false) : IOException(message)
{
    public bool Stop { get; } = stop;
}
public enum ArtworkKind { Portrait, Landscape, Hero, Logo, Icon }

public sealed class SteamGridDb(HttpClient http, string key)
{
    public static ArtworkGame? ExactMatch(string name, IEnumerable<ArtworkGame> candidates)
    {
        static string Normalize(string text) => string.Concat(text.Replace("™", "").Replace("®", "").Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit)).ToUpperInvariant();
        var matches = candidates.Where(g => Normalize(g.Name) == Normalize(name)).DistinctBy(g => g.Id).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<List<ArtworkGame>> Search(string title, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 300) return [];
        using var data = await Api("search/autocomplete/" + Uri.EscapeDataString(title), cancellation);
        return data.RootElement.GetProperty("data").EnumerateArray().Select(ReadGame).DistinctBy(g => g.Id).ToList();
    }

    public async Task<ArtworkGame> Game(long id, CancellationToken cancellation)
    {
        if (id <= 0) throw new InvalidDataException("Enter a positive SteamGridDB game ID.");
        using var data = await Api("games/id/" + id.ToString(CultureInfo.InvariantCulture), cancellation);
        return ReadGame(data.RootElement.GetProperty("data"));
    }

    private static ArtworkGame ReadGame(JsonElement value)
    {
        // The API schema also documents an enveloped search-item representation.
        if (value.TryGetProperty("data", out var nested)) value = nested;
        long id = value.GetProperty("id").GetInt64(); string? name = value.GetProperty("name").GetString();
        if (id <= 0 || string.IsNullOrWhiteSpace(name) || name.Length > 500) throw new InvalidDataException("Invalid artwork game response.");
        return new(id, name);
    }

    public async Task<ArtworkAsset?> Asset(long id, ArtworkKind kind, CancellationToken cancellation)
    {
        string category = kind switch { ArtworkKind.Portrait or ArtworkKind.Landscape => "grids", ArtworkKind.Hero => "heroes", ArtworkKind.Logo => "logos", _ => "icons" };
        string dimensions = kind switch { ArtworkKind.Portrait => "&dimensions=600x900,660x930", ArtworkKind.Landscape => "&dimensions=920x430,460x215", _ => "" };
        using var data = await Api($"{category}/game/{id}?types=static&mimes=image/png&nsfw=false&humor=false&epilepsy=false&limit=50{dimensions}", cancellation);
        List<ArtworkAsset> assets = [];
        foreach (var item in data.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!item.TryGetProperty("url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || !AllowedImageUri(uri)) continue;
            if (item.TryGetProperty("tags", out var tags) && tags.EnumerateArray().Any(t => t.GetString() is "nsfw" or "humor" or "epilepsy" or "animated")) continue;
            string author = item.TryGetProperty("author", out var person) && person.TryGetProperty("name", out var authorName) ? authorName.GetString() ?? "Unknown" : "Unknown";
            assets.Add(new(item.GetProperty("id").GetInt64(), uri, author, item.TryGetProperty("score", out var score) ? score.GetInt32() : 0));
        }
        return assets.OrderByDescending(a => a.Score).ThenBy(a => a.Id).FirstOrDefault();
    }

    public static bool AllowedImageUri(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host.EndsWith(".steamgriddb.com", StringComparison.OrdinalIgnoreCase) && uri.Host.StartsWith("cdn", StringComparison.OrdinalIgnoreCase);

    public async Task<byte[]> Download(ArtworkAsset asset, CancellationToken cancellation)
    {
        if (!AllowedImageUri(asset.Url)) throw new InvalidDataException("Untrusted artwork download location.");
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        byte[] bytes = await Send(request, 20 * 1024 * 1024, cancellation, image: true);
        ValidatePng(bytes);
        return bytes;
    }

    private async Task<JsonDocument> Api(string path, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        JsonDocument document;
        try { document = JsonDocument.Parse(await Send(request, 2 * 1024 * 1024, cancellation, image: false)); }
        catch (JsonException) { throw new ArtworkUnavailableException("SteamGridDB returned an invalid response."); }
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        { document.Dispose(); throw new ArtworkUnavailableException("SteamGridDB returned an unsuccessful response."); }
        return document;
    }

    private async Task<byte[]> Send(HttpRequestMessage request, int limit, CancellationToken cancellation, bool image)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ArtworkUnavailableException("SteamGridDB rejected the API key. Check Artwork settings.", true);
            if ((int)response.StatusCode == 429) throw new ArtworkUnavailableException("SteamGridDB rate limit reached. Try again later.", true);
            if (!response.IsSuccessStatusCode) throw new ArtworkUnavailableException($"SteamGridDB request failed (HTTP {(int)response.StatusCode}).");
            if (image && response.Content.Headers.ContentType?.MediaType != "image/png") throw new InvalidDataException("Artwork was not a PNG image.");
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Artwork response exceeds size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (output.Length + count > limit) throw new InvalidDataException("Artwork response exceeds size limit.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        catch (HttpRequestException) { throw new ArtworkUnavailableException("Could not connect to SteamGridDB. Check your connection and try later."); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new ArtworkUnavailableException("SteamGridDB request timed out."); }
    }

    public static void ValidatePng(byte[] bytes)
    {
        if (bytes.Length < 45 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) throw new InvalidDataException("Invalid PNG artwork.");
        int position = 8; bool data = false;
        while (position <= bytes.Length - 12)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position, 4));
            if (size > bytes.Length - position - 12) throw new InvalidDataException("Truncated PNG artwork.");
            string type = Encoding.ASCII.GetString(bytes, position + 4, 4);
            if (position == 8)
            {
                if (type != "IHDR" || size != 13) throw new InvalidDataException("Missing PNG header.");
                uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 8, 4));
                uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 12, 4));
                if (width == 0 || height == 0 || width > 8192 || height > 8192 || (ulong)width * height > 32_000_000) throw new InvalidDataException("Unsupported artwork dimensions.");
            }
            if (type == "acTL") throw new InvalidDataException("Animated artwork is not supported.");
            if (type == "IDAT") data = true;
            position += checked((int)size + 12);
            if (type == "IEND")
            {
                if (!data || size != 0 || position != bytes.Length) throw new InvalidDataException("Invalid PNG end marker.");
                return;
            }
        }
        throw new InvalidDataException("Incomplete PNG artwork.");
    }
}
