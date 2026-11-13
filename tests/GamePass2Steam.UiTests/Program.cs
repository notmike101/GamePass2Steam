using System.Net;
using System.Reflection;
using GamePass2Steam;
using GamePass2Steam.Core;

internal static class Program
{
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Calls++;
            await Task.Delay(20, cancellation);
            return new(status);
        }
    }

    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        IdentityChecks.Run();
        CheckFilterErrors();
        var choose = typeof(MainForm).Assembly.GetType("GamePass2Steam.ArtworkUi")!.GetMethod("Choose", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        foreach (string query in new[] { "Another title", "123" })
        {
            using var handler = new Handler(status);
            using var http = new HttpClient(handler);
            var api = new SteamGridDb(http, "test-key-not-a-real-secret");
            using var owner = new Form { ShowInTaskbar = false, Opacity = 0 };
            using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            bool searched = false, propagated = false;
            Exception? failure = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            timer.Tick += (_, _) => {
                if (DateTime.UtcNow > deadline) { Console.Error.WriteLine("FAIL artwork chooser did not propagate its stop failure"); Environment.Exit(1); }
                if (searched) return;
                var dialog = Application.OpenForms.Cast<Form>().SingleOrDefault(f => f.Text.StartsWith("Choose artwork match"));
                if (dialog == null) return;
                var controls = Descendants(dialog).ToList();
                controls.OfType<TextBox>().Single().Text = query;
                searched = true;
                controls.OfType<Button>().Single(b => b.Text == "Search").PerformClick();
            };
            owner.Shown += async (_, _) => {
                try {
                    var task = (Task<ArtworkGame?>)choose.Invoke(null, [owner, api, "Unmatched", new List<ArtworkGame>()])!;
                    await task;
                    throw new Exception("Chooser returned normally after a terminal HTTP failure");
                } catch (ArtworkUnavailableException e) when (e.Stop) { propagated = true; }
                catch (Exception e) { failure = e; }
                finally { owner.Close(); }
            };
            timer.Start();
            Application.Run(owner);
            if (failure != null || !propagated || handler.Calls != 1 || Application.OpenForms.Count != 0)
            { Console.Error.WriteLine(failure?.ToString() ?? "FAIL chooser did not stop cleanly after one request"); return 1; }
            Console.WriteLine($"PASS chooser propagates HTTP {(int)status} from {(query == "123" ? "ID" : "title")} search and closes");
        }
        Console.WriteLine("8 Windows UI checks passed");
        return 0;
    }

    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static void CheckFilterErrors()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gp2s-filter-" + Guid.NewGuid());
        string config = Path.Combine(directory, "userdata", "1", "config");
        Directory.CreateDirectory(config);
        try
        {
            using var form = new MainForm();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainForm).GetField("steam", flags)!.SetValue(form, directory);
            var profile = Descendants(form).OfType<ComboBox>().Single();
            profile.Items.Add(new SteamProfile("1", null)); profile.SelectedIndex = 0;
            var search = Descendants(form).OfType<TextBox>().Single();
            var allApps = Descendants(form).OfType<CheckBox>().Single(c => c.Text == "Include other registered apps");
            var status = (Label)typeof(MainForm).GetField("status", flags)!.GetValue(form)!;
            void WaitForError()
            {
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (status.Text.Length == 0 && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
                if (status.Text.Length == 0) throw new Exception("Missing shortcut-file filter error");
            }
            string path = Path.Combine(config, "shortcuts.vdf");
            File.WriteAllBytes(path, BinaryVdf.Write([VdfNode.Object("shortcuts")]));
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                search.Text = "test";
                WaitForError();
                status.Text = ""; allApps.Checked = true;
                WaitForError();
            }
            File.WriteAllBytes(path, [0, 1]);
            status.Text = ""; search.Clear();
            WaitForError();
            File.WriteAllBytes(path, BinaryVdf.Write([VdfNode.Object("shortcuts")]));
            search.Text = "recovered";
            Console.WriteLine("PASS filters contain locked and malformed shortcut errors and remain usable");
        }
        finally { Directory.Delete(directory, true); }
    }
}
