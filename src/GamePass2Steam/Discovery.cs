using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GamePass2Steam.Core;
using Microsoft.Win32;

namespace GamePass2Steam;

public sealed record DiscoveryResult(List<Game> Games, List<string> Warnings);
internal sealed record RegisteredApp(string Id, string Name, string Family, string InstallPath, string Manifest, string? Config);
internal sealed record RegistrationResult(List<RegisteredApp> Items, List<string> Warnings);
public static class Discovery
{
    public static async Task<DiscoveryResult> Scan(string? id = null)
    {
        if (id != null) GameMetadata.ValidateIdentity(id);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("GamePass2Steam.Discover.ps1")!;
        using var reader = new StreamReader(resource);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.Environment["GP2S_DISCOVERY_ID"] = id ?? "";
        foreach (string arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(await reader.ReadToEndAsync())) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Windows package discovery could not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new IOException("Windows package discovery timed out. Try opening the Xbox app and retrying."); }
        if (process.ExitCode != 0) throw new IOException("Windows package discovery failed: " + await errors);
        var registrations = JsonSerializer.Deserialize<RegistrationResult>(await output) ?? throw new InvalidDataException("No package discovery response.");
        List<Game> games = [];
        foreach (var app in registrations.Items)
        {
            try
            {
                GameMetadata.ValidateIdentity(app.Id);
                var manifest = GameMetadata.Parse(app.Manifest);
                string appId = app.Id.Split('!')[1];
                var application = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application" && string.Equals((string?)e.Attribute("Id"), appId, StringComparison.OrdinalIgnoreCase));
                if (application == null) continue;
                bool gdk = app.Config != null;
                List<string> executables = gdk ? GameMetadata.Executables(app.Config!, appId, app.InstallPath) : [];
                if (gdk && executables.Count == 0) continue;
                if (!gdk && (string?)application.Attribute("Executable") is { Length: > 0 } executable)
                    executables.Add(GameMetadata.ContainedPath(app.InstallPath, executable));
                if (executables.Count == 0) { registrations.Warnings.Add(app.Name + ": no trackable executable in manifest."); continue; }
                bool xbox = gdk || manifest.Descendants().Any(e => e.Attributes().Any(a => a.Value.StartsWith("ms-xbl-", StringComparison.OrdinalIgnoreCase) || a.Value.Contains("XboxLive", StringComparison.OrdinalIgnoreCase)));
                string kind = gdk ? "GDK / packaged Win32" : (string?)application.Attribute("EntryPoint") == "Windows.FullTrustApplication" ? "Packaged Win32" : "UWP";
                games.Add(new(app.Id, app.Name, app.Family, app.InstallPath, executables.Select(Native.CanonicalPath).ToList(), kind, xbox));
            }
            catch (Exception e) when (e is IOException or ArgumentException) { registrations.Warnings.Add(app.Name + ": " + e.Message); }
        }
        return new(games.DistinctBy(g => g.Id, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Name).ToList(), registrations.Warnings);
    }

    public static List<Game> MatchFolder(string folder, IEnumerable<Game> games)
    {
        string content = File.Exists(Path.Combine(folder, "MicrosoftGame.config")) || File.Exists(Path.Combine(folder, "AppxManifest.xml")) ? folder : Path.Combine(folder, "Content");
        string file = Path.Combine(content, "MicrosoftGame.config");
        if (!File.Exists(file)) file = Path.Combine(content, "AppxManifest.xml");
        if (!File.Exists(file)) throw new IOException("Choose a game folder containing MicrosoftGame.config or AppxManifest.xml (or its parent). External-launcher and unregistered games are not supported.");
        string name = (string?)GameMetadata.Parse(File.ReadAllText(file)).Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Name") ?? throw new InvalidDataException("No package identity in selected folder.");
        var matches = games.Where(g => g.Family.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) throw new IOException("This installation is not registered for this Windows user. Install or repair it through Xbox, then refresh.");
        return matches;
    }

    public static string SteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string? path = key?.GetValue("SteamPath") as string;
        if (path == null || !Directory.Exists(Path.Combine(path, "userdata"))) throw new IOException("Steam installation/userdata not found. Sign in to Steam once, then retry.");
        return Path.GetFullPath(path);
    }
    public static bool SteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }
    public static string ProfilePath(string steam, string profile)
    {
        if (!uint.TryParse(profile, out _)) throw new InvalidDataException("Select a numeric local Steam profile.");
        string directory = Path.Combine(steam, "userdata", profile);
        if (!Directory.Exists(directory)) throw new IOException("Steam profile does not exist. Sign in to Steam first.");
        return Path.Combine(directory, "config", "shortcuts.vdf");
    }
}
