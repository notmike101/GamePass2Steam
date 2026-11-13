using System.Diagnostics;
using System.Reflection;
using GamePass2Steam;
using GamePass2Steam.Core;

internal static class ProcessChecks
{
    public static void Run()
    {
        var native = typeof(Launcher).Assembly.GetType("GamePass2Steam.Native")!;
        var find = native.GetMethod("GameProcesses", BindingFlags.Static | BindingFlags.Public)!;
        List<int> Find(params string[] paths) => (List<int>)find.Invoke(null,
            [new Game("Example_abcd!Play", "Test", "Example_abcd", "", paths.ToList(), "GDK / packaged Win32", true)])!;
        string executable = Environment.ProcessPath!;
        if (!Find(executable.ToUpperInvariant(), executable.ToLowerInvariant()).SequenceEqual(new[] {Environment.ProcessId}))
            throw new Exception("Exact process match or duplicate executable filtering failed");
        string wrongDirectory = Path.Combine(Path.GetTempPath(), "gp2s-not-installed", Path.GetFileName(executable));
        if (Find(wrongDirectory).Count != 0 || Find().Count != 0)
            throw new Exception("Process names alone must not establish a game match");
        using var child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
        })!;
        try
        {
            string childPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            if (!Find(childPath).Contains(child.Id)) throw new Exception("Child process was not detected");
            child.StandardInput.WriteLine("exit"); child.StandardInput.Flush();
            if (!child.WaitForExit(5000)) throw new Exception("Child did not exit normally");
            if (Find(childPath).Contains(child.Id)) throw new Exception("Exited child remains tracked");
        }
        finally { if (!child.HasExited) child.Kill(); }
        Console.WriteLine("PASS process matching preserves case-insensitive paths, deduplication, path validation and normal exit");
    }
}
