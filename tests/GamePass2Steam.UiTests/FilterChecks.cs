using System.Diagnostics;
using System.Reflection;
using GamePass2Steam;
using GamePass2Steam.Core;

internal static class FilterChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gp2s-debounce-" + Guid.NewGuid());
        string config = Path.Combine(directory, "userdata", "1", "config");
        Directory.CreateDirectory(config);
        try
        {
            using var form = new MainForm();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            T Field<T>(string name) => (T)typeof(MainForm).GetField(name, flags)!.GetValue(form)!;
            void Populate() => typeof(MainForm).GetMethod("Populate", flags)!.Invoke(form, null);
            void WaitFor(Func<bool> ready)
            {
                var clock = Stopwatch.StartNew();
                while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(10); }
                if (!ready()) throw new Exception("Filter did not settle");
            }
            var game = new Game("Example_abcd!Play", "Game", "Example_abcd", "", [], "GDK", true);
            Field<List<Game>>("games").AddRange([game, game with {Id = "Other_abcd!Play", Name = "Other"}]);
            typeof(MainForm).GetField("steam", flags)!.SetValue(form, directory);
            var profiles = Field<ComboBox>("profiles"); profiles.Items.Add(new SteamProfile("1", null)); profiles.SelectedIndex = 0;
            var grid = Field<DataGridView>("grid"); var search = Field<TextBox>("search");
            int added = 0; grid.RowsAdded += (_, _) => added++;
            search.Text = "G"; search.Text = "Ga"; search.Text = "Game";
            if (grid.Rows.Count != 2 || added != 0) throw new Exception("Filter ran before typing settled");
            WaitFor(() => grid.Rows.Count == 1);
            if (added != 1 || (string)grid.Rows[0].Cells[1].Value! != "Game") throw new Exception("Typing did not coalesce into one population");
            string path = Path.Combine(config, "shortcuts.vdf");
            File.WriteAllBytes(path, BinaryVdf.Write([VdfNode.Object("shortcuts")]));
            var store = new ShortcutStore(path, () => false);
            string backup = store.Apply([game], Environment.ProcessPath!, false)!;
            Populate();
            if ((string)grid.Rows[0].Cells[3].Value! != "Managed") throw new Exception("Import did not refresh immediately");
            store.Restore(backup); Populate();
            if ((string)grid.Rows[0].Cells[3].Value! != "Not added") throw new Exception("Restore did not refresh immediately");
            store.Apply([game], Environment.ProcessPath!, false);
            search.Text = "Gam";
            WaitFor(() => (string)grid.Rows[0].Cells[3].Value! == "Managed");
            Console.WriteLine("PASS typing coalesces and filtering observes imports, restore and external file changes");
        }
        finally { Directory.Delete(directory, true); }
    }
}
