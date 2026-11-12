using System.Text.Json;
using GamePass2Steam.Core;

namespace GamePass2Steam;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Length == 0) { Application.Run(new MainForm()); return 0; }
            string command = args[0];
            string Value(string key) { int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Missing " + key); }
            void Output(object value) { string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }); if (args.Contains("--output")) File.WriteAllText(Value("--output"), json); else Console.WriteLine(json); }
            switch (command)
            {
                case "--discover": Output(Discovery.Scan().GetAwaiter().GetResult()); break;
                case "--profiles": Output(Directory.GetDirectories(Path.Combine(Discovery.SteamPath(), "userdata")).Select(Path.GetFileName).Where(n => uint.TryParse(n, out _))); break;
                case "--launch": Launcher.Run(Value("--launch")).GetAwaiter().GetResult(); break;
                case "--import":
                case "--remove":
                    var games = Discovery.Scan().GetAwaiter().GetResult().Games;
                    string id = Value(command);
                    GameMetadata.ValidateIdentity(id);
                    var game = games.SingleOrDefault(g => g.Id == id) ?? (command == "--remove" ? new Game(id, id, "", "", [], "", false) : throw new IOException("Game not found; run --discover."));
                    var store = new ShortcutStore(Discovery.ProfilePath(Discovery.SteamPath(), Value("--profile")), Discovery.SteamRunning);
                    Output(new { Backup = store.Apply([game], Environment.ProcessPath!, command == "--remove") }); break;
                case "--restore":
                    Output(new { Backup = new ShortcutStore(Discovery.ProfilePath(Discovery.SteamPath(), Value("--profile")), Discovery.SteamRunning).Restore(Value("--restore")) }); break;
                default: throw new ArgumentException("Commands: --discover [--output file], --profiles [--output file], --import AUMID --profile ID, --remove AUMID --profile ID, --restore backup --profile ID, --launch AUMID.");
            }
            return 0;
        }
        catch (Exception e)
        {
            Log.Write(e.ToString());
            if (args.Length == 0) MessageBox.Show(e.Message + "\n\nLogs: " + Log.DirectoryPath, "GamePass2Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if (args[0] == "--launch")
            {
                using var error = new Form { Text = "GamePass2Steam — launch failed", Size = new Size(600, 260), StartPosition = FormStartPosition.CenterScreen };
                error.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = e.Message + "\r\n\r\nLogs: " + Log.DirectoryPath + "\r\n\r\nThis error window closes automatically in 30 seconds." });
                using var timer = new System.Windows.Forms.Timer { Interval = 30000 };
                timer.Tick += (_, _) => error.Close(); timer.Start(); Application.Run(error);
            }
            if (args.Contains("--output")) { int i = Array.IndexOf(args, "--output"); if (i + 1 < args.Length) File.WriteAllText(args[i + 1], JsonSerializer.Serialize(new { Error = e.Message })); }
            Console.Error.WriteLine(e.Message); return 1;
        }
    }
}
