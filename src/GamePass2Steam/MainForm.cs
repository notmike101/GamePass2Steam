using System.Diagnostics;
using GamePass2Steam.Core;
using Microsoft.Win32;

namespace GamePass2Steam;

public sealed class MainForm : Form
{
    private readonly ComboBox profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, DropDownWidth = 480, AccessibleName = "Steam profile" };
    private readonly TextBox search = new() { PlaceholderText = "Filter games by name…", Width = 250, AccessibleName = "Filter games" };
    private readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 200 };
    private readonly CheckBox allApps = new() { Text = "Include other registered apps", AutoSize = true };
    private readonly CheckBox artwork = new() { Text = "Fill missing SteamGridDB artwork", AutoSize = true };
    private string? artworkKey;
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Installed games" };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(1020, 0), Padding = new Padding(0, 8, 0, 0) };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private List<Game> games = [];
    private string steam = "";
    private bool busy;
    private CancellationTokenSource? importCancellation;
    private bool writingImport;

    public MainForm()
    {
        Text = "GamePass2Steam"; MinimumSize = new Size(850, 540); Size = new Size(1120, 720); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Select games below, exit Steam, then add them. Windows handles sign-in, licensing and updates as usual.", AutoSize = true, Margin = new Padding(0, 0, 0, 18) });
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 10) };
        filters.Controls.Add(new Label { Text = "Steam profile", AutoSize = true, Padding = new Padding(0, 5, 8, 0) }); filters.Controls.Add(profiles); filters.Controls.Add(search); filters.Controls.Add(allApps); layout.Controls.Add(filters);
        filters.Controls.Add(artwork);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Add", Width = 45 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Game", HeaderText = "Game", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Installation", ReadOnly = true, Width = 190 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Managed", HeaderText = "Steam entry", ReadOnly = true, Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Location", HeaderText = "Location", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 60 });
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        layout.Controls.Add(grid);
        AddButton("Add / update selected", () => Mutate(false)); AddButton("Remove managed selected", () => Mutate(true));
        AddButton("Refresh", RefreshGames); AddButton("Find game folder…", FindFolder); AddButton("Restore backup…", Restore);
        AddButton("Artwork settings…", () => { artworkKey = ArtworkUi.Configure(this, artworkKey); return Task.CompletedTask; });
        AddButton("Open logs", () => { Directory.CreateDirectory(Log.DirectoryPath); Process.Start(new ProcessStartInfo(Log.DirectoryPath) { UseShellExecute = true }); return Task.CompletedTask; });
        layout.Controls.Add(actions); layout.Controls.Add(status); Controls.Add(layout);
        search.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); }; allApps.CheckedChanged += (_, _) => TryPopulate(); profiles.SelectedIndexChanged += (_, _) => { if (!busy) TryPopulate(); };
        filterTimer.Tick += (_, _) => { filterTimer.Stop(); TryPopulate(); };
        Disposed += (_, _) => filterTimer.Dispose();
        Shown += async (_, _) => await Execute(Initialize);
        FormClosing += (_, e) => { if (importCancellation != null) { e.Cancel = true; if (!writingImport) importCancellation.Cancel(); status.Text = writingImport ? "Finishing shortcuts/artwork before reopening Steam. Please wait." : "Import canceled. Waiting for Steam shutdown recovery."; } };
    }
    private void AddButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8), Margin = new Padding(0, 12, 8, 0) };
        button.Click += async (_, _) => await Execute(action); actions.Controls.Add(button);
    }
    private async Task Execute(Func<Task> action)
    {
        if (busy) return;
        busy = true; actions.Enabled = profiles.Enabled = grid.Enabled = false; UseWaitCursor = true;
        try { await action(); }
        catch (Exception e) { Log.Write(e.ToString()); status.Text = e.Message; MessageBox.Show(this, e.Message, "GamePass2Steam", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; actions.Enabled = profiles.Enabled = grid.Enabled = true; UseWaitCursor = false; }
    }
    private async Task Initialize()
    {
        steam = Discovery.SteamPath();
        using var active = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
        string activeId = Convert.ToUInt32(active?.GetValue("ActiveUser") ?? 0).ToString();
        foreach (var profile in SteamProfiles.Load(steam)) profiles.Items.Add(profile);
        profiles.SelectedItem = profiles.Items.Cast<SteamProfile>().FirstOrDefault(p => p.Id == activeId);
        if (profiles.SelectedIndex < 0 && profiles.Items.Count == 1) profiles.SelectedIndex = 0;
        await RefreshGames();
    }
    private async Task RefreshGames()
    {
        status.Text = "Reading current Windows registrations…";
        var result = await Discovery.Scan(); games = result.Games; Populate();
        foreach (string warning in result.Warnings) Log.Write("Discovery warning: " + warning);
        status.Text = $"Found {games.Count(g => g.LikelyGame)} likely games and {games.Count} registered apps. " + (Discovery.SteamRunning() ? "Steam is running: exit Steam before making changes. " : "Steam is closed. ") + (result.Warnings.Count > 0 ? $"{result.Warnings.Count} discovery warnings; see logs. " : "") + "Keep this application in its current folder so Steam can find it.";
    }
    private string ProfileId() => (profiles.SelectedItem as SteamProfile)?.Id ?? throw new IOException("Select the intended Steam profile first.");
    private ShortcutStore Store() => new(Discovery.ProfilePath(steam, ProfileId()), Discovery.SteamRunning);
    private void TryPopulate() { try { Populate(); } catch (Exception e) { status.Text = e.Message; } }
    private void Populate()
    {
        filterTimer.Stop();
        var selected = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is true).Select(r => ((Game)r.Tag!).Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managed = profiles.SelectedItem != null ? SteamShortcuts.Entries(Store().Read()).Where(e => SteamShortcuts.ManagedId(e) != null).ToDictionary(e => SteamShortcuts.ManagedId(e)!, e => e.Text("appname") ?? "Missing game", StringComparer.OrdinalIgnoreCase) : [];
        var display = games.Concat(managed.Where(p => games.All(g => !g.Id.Equals(p.Key, StringComparison.OrdinalIgnoreCase))).Select(p => new Game(p.Key, p.Value, "", "Not installed / refresh to repair", [], "Missing registration", false)));
        grid.Rows.Clear();
        foreach (var game in display.Where(g => (allApps.Checked || g.LikelyGame || managed.ContainsKey(g.Id)) && g.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)))
        {
            int row = grid.Rows.Add(selected.Contains(game.Id), game.Name, game.Kind, managed.ContainsKey(game.Id) ? "Managed" : "Not added", game.InstallPath); grid.Rows[row].Tag = game;
        }
    }
    private async Task Mutate(bool remove)
    {
        grid.EndEdit();
        var selected = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is true).Select(r => (Game)r.Tag!).ToList();
        if (selected.Count == 0) throw new IOException("Check at least one game in the Add column.");
        if (!remove && selected.Any(g => g.Executables.Count == 0)) throw new IOException("A selected game is no longer installed. Repair it in Xbox, or remove its managed Steam entry.");
        var store = Store();
        string? backup = null;
        string artworkResult = "";
        async Task WriteSelected()
        {
            backup = store.Apply(selected, Environment.ProcessPath!, remove);
            if (!remove && artwork.Checked)
            {
                if (string.IsNullOrWhiteSpace(artworkKey)) artworkResult = " Artwork skipped: enter your API key in Artwork settings.";
                else
                {
                    try { artworkResult = await ArtworkUi.Fill(this, selected, store, artworkKey); }
                    catch (Exception) { artworkResult = " Shortcuts saved; artwork could not be completed. Retry artwork later."; }
                }
            }
        }
        bool restart = !remove && Discovery.SteamRunning();
        if (restart)
        {
            if (MessageBox.Show(this, "Steam is running. Close it normally, import these games, then reopen it?\n\nThis may interrupt games or downloads. Save your work first. This choice applies only to this import.\n\nChoose No or Cancel to leave Steam and shortcuts unchanged and exit Steam manually.", "Close and reopen Steam for this import?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            { status.Text = "Nothing was imported. Exit Steam using Steam > Exit, then retry when ready."; return; }
            using var cancellation = new CancellationTokenSource();
            importCancellation = cancellation;
            using var waiting = new Form { Text = "Waiting for Steam", Size = new Size(520, 210), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
            var message = new Label { Text = "Waiting up to 60 seconds for Steam to close normally…", Dock = DockStyle.Fill, Padding = new Padding(16) };
            var cancel = new Button { Text = "Cancel import", Dock = DockStyle.Bottom, Height = 40 };
            bool complete = false, writing = false;
            void Cancel() { if (writing) return; cancellation.Cancel(); cancel.Enabled = false; message.Text = "Import canceled. Waiting for Steam to finish closing so it can be reopened. No shortcuts will be changed."; }
            cancel.Click += (_, _) => Cancel();
            waiting.FormClosing += (_, e) => { if (!complete) { e.Cancel = true; Cancel(); } };
            waiting.Controls.Add(message); waiting.Controls.Add(cancel); waiting.Show(this);
            void StartSteam(bool shutdown)
            {
                var start = new ProcessStartInfo(Path.Combine(steam, "steam.exe")) { UseShellExecute = false };
                if (shutdown) start.ArgumentList.Add("-shutdown");
                using var process = Process.Start(start) ?? throw new IOException("Could not start the Steam client.");
            }
            try
            {
                await SteamImport.RunAsync(true, Discovery.SteamRunning, () => StartSteam(true), () => StartSteam(false),
                    async () => { writing = writingImport = true; cancel.Enabled = false; message.Text = "Updating shortcuts and optional artwork. Steam will reopen when this finishes."; await WriteSelected(); }, cancellation.Token);
            }
            catch (OperationCanceledException) { status.Text = "Import canceled. No shortcuts were changed. Steam was reopened if shutdown completed."; return; }
            finally { complete = true; writingImport = false; importCancellation = null; waiting.Close(); }
        }
        else await WriteSelected();
        Populate();
        status.Text = (remove ? "Removed selected managed shortcuts. Installed games are untouched. " : "Steam shortcuts are ready. Open Steam and select Play. ") + (backup == null ? "No file changes were needed." : "Backup: " + backup);
        status.Text += artworkResult;
    }
    private Task FindFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select an Xbox game installation or its Content folder", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var ids = Discovery.MatchFolder(dialog.SelectedPath, games).Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            allApps.Checked = true; search.Clear(); Populate();
            foreach (DataGridViewRow row in grid.Rows) if (ids.Contains(((Game)row.Tag!).Id)) { row.Cells[0].Value = true; grid.FirstDisplayedScrollingRowIndex = row.Index; }
            status.Text = "Matched the folder to its registered Windows identity. Use Add / update selected.";
        }
        return Task.CompletedTask;
    }
    private Task Restore()
    {
        using var dialog = new OpenFileDialog { Title = "Restore a Steam shortcuts backup", Filter = "GamePass2Steam backups|*.bak", InitialDirectory = Path.GetDirectoryName(Discovery.ProfilePath(steam, ProfileId())) };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (MessageBox.Show(this, "Restore this entire shortcut snapshot? Changes made since the backup will be replaced. The current file will be backed up first.", "Restore shortcuts", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return Task.CompletedTask;
            string? backup = Store().Restore(dialog.FileName); Populate(); status.Text = "Restored shortcut snapshot. Previous file: " + backup;
        }
        return Task.CompletedTask;
    }
}
