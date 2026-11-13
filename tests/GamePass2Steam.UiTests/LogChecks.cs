using System.Reflection;
using GamePass2Steam;

internal static class LogChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gp2s-logs-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var prune = typeof(Log).GetMethod("Prune", BindingFlags.Static | BindingFlags.NonPublic)!;
        void Seed(string name, DateTime modified) { string path = Path.Combine(directory, name); File.WriteAllText(path, "test"); File.SetLastWriteTimeUtc(path, modified); }
        try
        {
            Seed("20200101T000000-1.log", cutoff.AddDays(-1));
            Seed("20200101T000000-2.log", cutoff.AddDays(1));
            Seed("20200101T000000-3.log", cutoff);
            Seed("unrelated.log", cutoff.AddDays(-1));
            Seed("20200101T000000-4.log.bak", cutoff.AddDays(-1));
            Seed("20200101T000000-5.log", cutoff.AddDays(-1));
            using (var locked = new FileStream(Path.Combine(directory, "20200101T000000-5.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                prune.Invoke(null, [directory, cutoff]);
            if (File.Exists(Path.Combine(directory, "20200101T000000-1.log")) || Directory.GetFiles(directory).Length != 5)
                throw new Exception("Log retention removed the wrong files");
            prune.Invoke(null, [Path.Combine(directory, "missing"), cutoff]);
            Console.WriteLine("PASS log retention preserves recent, boundary, unrelated and locked files");
        }
        finally { Directory.Delete(directory, true); }
    }
}
