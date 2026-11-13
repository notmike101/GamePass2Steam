using System.Diagnostics;
using GamePass2Steam.Core;

namespace GamePass2Steam;

public static class Launcher
{
    public static async Task Run(string id)
    {
        GameMetadata.ValidateIdentity(id);
        string name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..24];
        using var mutex = new Mutex(false, @"Local\GamePass2Steam-" + name);
        bool owned;
        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) throw new IOException("This game already has a GamePass2Steam launcher session.");
        // Mutex ownership is thread-affine; keep the owner alive until the process exits, without releasing on an async continuation.
        Log.Write("Resolving " + id);
        var result = await Discovery.Scan(id);
        var game = result.Games.SingleOrDefault(g => g.Id == id) ?? throw new IOException("Game is no longer registered for this Windows user. Repair it in Xbox, then refresh and re-import.");
        if (Native.GameProcesses(game).Count != 0) throw new IOException("The game is already running. Exit it before launching from Steam so Steam can track this session.");
        uint bootstrap = Native.Activate(id);
        Log.Write($"Activated {id}; activation PID {bootstrap}; targets {string.Join("; ", game.Executables)}");
        var watch = Stopwatch.StartNew(); var tracker = new SessionTracker(); string previous = "";
        while (true)
        {
            var pids = Native.GameProcesses(game);
            string current = string.Join(",", pids);
            if (current != previous) { Log.Write("Game PIDs: " + (current.Length == 0 ? "none" : current)); previous = current; }
            var state = tracker.Observe(watch.Elapsed, pids.Count > 0);
            if (state == SessionState.Failed) throw new IOException("No matching game process appeared within 120 seconds. Open the game from Xbox, complete sign-in or updates, then retry. The activation PID alone is not proof of success.");
            if (state == SessionState.Exited) { Log.Write("Game session exited; launcher finishing."); return; }
            await Task.Delay(500);
        }
    }
}

public static class Log
{
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamePass2Steam", "logs");
    private static readonly string FilePath = Path.Combine(DirectoryPath, DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Environment.ProcessId + ".log");
    private static bool pruned;
    public static void Write(string message)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!pruned) { Prune(DirectoryPath, DateTime.UtcNow.AddDays(-30)); pruned = true; }
        File.AppendAllText(FilePath, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
    }
    private static void Prune(string directory, DateTime cutoff)
    {
        try
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (string file in Directory.EnumerateFiles(directory, "*.log"))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(file), @"\A[0-9]{8}T[0-9]{6}-[0-9]+\.log\z")) continue;
                try
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
