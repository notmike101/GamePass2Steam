using System.Reflection;
using GamePass2Steam;
using GamePass2Steam.Core;

internal static class IdentityChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gp2s-identity-" + Guid.NewGuid());
        string config = Path.Combine(directory, "userdata", "1", "config");
        Directory.CreateDirectory(config);
        try
        {
            var game = new Game("Example_abcd!Play", "Game", "Example_abcd", "", [], "GDK", true);
            new ShortcutStore(Path.Combine(config, "shortcuts.vdf"), () => false).Apply([game], Environment.ProcessPath!, false);
            using var form = new MainForm();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            T Field<T>(string name) => (T)typeof(MainForm).GetField(name, flags)!.GetValue(form)!;
            typeof(MainForm).GetField("steam", flags)!.SetValue(form, directory);
            var games = Field<List<Game>>("games"); games.Add(game with {Id = game.Id.ToLowerInvariant()});
            var profiles = Field<ComboBox>("profiles"); profiles.Items.Add(new SteamProfile("1", null)); profiles.SelectedIndex = 0;
            var grid = Field<DataGridView>("grid");
            if (grid.Rows.Count != 1 || (string)grid.Rows[0].Cells[3].Value! != "Managed") throw new Exception("Identity casing created a missing-registration row");
            grid.Rows[0].Cells[0].Value = true;
            games[0] = game with {Id = game.Id.ToUpperInvariant()};
            typeof(MainForm).GetMethod("Populate", flags)!.Invoke(form, null);
            if (grid.Rows.Count != 1 || grid.Rows[0].Cells[0].Value is not true) throw new Exception("Identity casing lost selection");
            Console.WriteLine("PASS identity casing preserves managed UI rows and selection");
        }
        finally { Directory.Delete(directory, true); }
    }
}
