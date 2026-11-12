using System.Diagnostics;
using GamePass2Steam.Core;

namespace GamePass2Steam;

internal static class ArtworkUi
{
    public static string? Configure(Form owner, string? currentKey)
    {
        using var dialog = new Form { Text = "SteamGridDB artwork", Size = new Size(650, 310), MinimumSize = new Size(550, 310), StartPosition = FormStartPosition.CenterParent, Font = owner.Font };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { AutoSize = true, Text = "Optional artwork uses your own SteamGridDB API key. The key stays in memory for this app session only and is never saved or logged. Game names are sent to SteamGridDB when you enable artwork for an import." });
        var link = new LinkLabel { Text = "Get your key from SteamGridDB preferences", AutoSize = true };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://www.steamgriddb.com/profile/preferences/api") { UseShellExecute = true });
        layout.Controls.Add(link);
        var key = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Top, Text = currentKey ?? "", AccessibleName = "SteamGridDB API key (session only)" };
        layout.Controls.Add(key);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var save = new Button { Text = "Use key", AutoSize = true }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => {
            string value = key.Text.Trim();
            if (value.Length is < 16 or > 256 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            { MessageBox.Show(dialog, "Enter the API key from your SteamGridDB preferences."); return; }
            dialog.DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons); dialog.Controls.Add(layout); dialog.AcceptButton = save; dialog.CancelButton = cancel;
        return dialog.ShowDialog(owner) == DialogResult.OK ? key.Text.Trim() : currentKey;
    }

    public static async Task<string> Fill(Form owner, List<Game> games, ShortcutStore store, string key)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GamePass2Steam/0.1");
        var api = new SteamGridDb(http, key);
        var installer = new ArtworkInstaller(api, Discovery.SteamRunning, bytes => {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (image.RawFormat.Guid != System.Drawing.Imaging.ImageFormat.Png.Guid) throw new InvalidDataException("Unsupported artwork format.");
            using var decoded = new Bitmap(image); // Force decoding before any file is installed.
        });
        int installed = 0; List<string> warnings = [];
        foreach (var game in games)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                if (!ArtworkInstaller.Missing(store, game.Id)) continue;
                var match = ArtworkInstaller.RememberedMatch(store, game.Id);
                if (match == null)
                {
                    var candidates = await api.Search(game.Name, cancellation.Token);
                    match = SteamGridDb.ExactMatch(game.Name, candidates);
                    if (match == null)
                    {
                        cancellation.CancelAfter(Timeout.InfiniteTimeSpan); // Time spent choosing a game is not a network timeout.
                        match = await Choose(owner, api, game.Name, candidates);
                        cancellation.CancelAfter(TimeSpan.FromMinutes(3));
                    }
                }
                if (match == null) { warnings.Add(game.Name + ": artwork skipped; no match selected."); continue; }
                var result = await installer.Fill(store, game.Id, match, cancellation.Token);
                installed += result.Installed;
                warnings.AddRange(result.Warnings.Select(w => game.Name + ": " + w));
            }
            catch (Exception e)
            {
                warnings.Add(game.Name + ": " + ArtworkInstaller.SafeError(e));
                if (e is ArtworkUnavailableException { Stop: true }) break;
            }
        }
        if (warnings.Count > 0) MessageBox.Show(owner, "Shortcut import succeeded. Artwork is optional; existing images were kept.\n\n" + string.Join("\n", warnings.Take(20)), "Artwork results", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return $" Artwork: {installed} files installed; {warnings.Count} notices.";
    }

    private static async Task<ArtworkGame?> Choose(Form owner, SteamGridDb api, string title, List<ArtworkGame> candidates)
    {
        using var dialog = new Form { Text = "Choose artwork match — " + title, Size = new Size(720, 450), MinimumSize = new Size(600, 350), StartPosition = FormStartPosition.CenterParent, Font = owner.Font };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Text = "No unambiguous exact match. Select the correct game, search another title, or enter a SteamGridDB game ID. Skip leaves its artwork unchanged." });
        var searchRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var query = new TextBox { Text = title, Width = 400, AccessibleName = "Artwork game title or SteamGridDB ID" }; var search = new Button { Text = "Search", AutoSize = true };
        searchRow.Controls.Add(query); searchRow.Controls.Add(search); layout.Controls.Add(searchRow);
        var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = "SteamGridDB matching games" };
        list.Items.AddRange(candidates.Cast<object>().ToArray()); list.SelectedIndex = -1; layout.Controls.Add(list);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var use = new Button { Text = "Use selected game", AutoSize = true, Enabled = false };
        var skip = new Button { Text = "Skip artwork", AutoSize = true, DialogResult = DialogResult.Cancel };
        list.SelectedIndexChanged += (_, _) => use.Enabled = list.SelectedItem is ArtworkGame;
        use.Click += (_, _) => { if (list.SelectedItem is ArtworkGame) dialog.DialogResult = DialogResult.OK; };
        using var cancellation = new CancellationTokenSource();
        ArtworkUnavailableException? stopFailure = null;
        Task searchTask = Task.CompletedTask;
        dialog.FormClosing += (_, _) => cancellation.Cancel();
        async Task Search()
        {
            search.Enabled = use.Enabled = false;
            try {
                var results = long.TryParse(query.Text.Trim(), out long id) ? new List<ArtworkGame> { await api.Game(id, cancellation.Token) } : await api.Search(query.Text.Trim(), cancellation.Token);
                if (!dialog.IsDisposed) { list.Items.Clear(); list.Items.AddRange(results.Cast<object>().ToArray()); list.SelectedIndex = -1; }
            } catch (ArtworkUnavailableException e) when (e.Stop) {
                stopFailure = e;
                if (dialog.Visible) dialog.Close();
            } catch (Exception e) { if (!dialog.IsDisposed && !cancellation.IsCancellationRequested) MessageBox.Show(dialog, ArtworkInstaller.SafeError(e), "Artwork search"); }
            finally { if (!dialog.IsDisposed && stopFailure == null) search.Enabled = true; }
        }
        search.Click += (_, _) => searchTask = Search();
        buttons.Controls.Add(use); buttons.Controls.Add(skip); layout.Controls.Add(buttons); dialog.Controls.Add(layout); dialog.CancelButton = skip;
        var result = dialog.ShowDialog(owner);
        await searchTask;
        if (stopFailure != null) throw stopFailure;
        return result == DialogResult.OK ? list.SelectedItem as ArtworkGame : null;
    }
}
