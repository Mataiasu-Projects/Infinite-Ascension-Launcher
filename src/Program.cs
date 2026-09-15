using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

namespace InfiniteAscensionLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}

internal sealed class LauncherForm : Form
{
    private const string WorkerBaseUrl = "https://infinite-ascension-updater.matthprizee55.workers.dev";
    private const string ManifestUrl = WorkerBaseUrl + "/update/manifest";
    private const string GameExe = "InfiniteAscension.exe";
    private const string GitHubUrl = "https://github.com/Mataiasu-Projects/Infinite-Ascension";

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly Label serverLabel = new(), buildLabel = new(), statusLabel = new(), progressLabel = new();
    private readonly ProgressBar progress = new();
    private readonly Button playButton = new(), updateButton = new(), stopButton = new(), folderButton = new(), repairButton = new(), githubButton = new();
    private readonly Panel content = new(), nav = new(), home = new();
    private readonly System.Windows.Forms.Timer gameTimer = new() { Interval = 1000 };

    private Process? gameProcess;
    private bool busy;
    private bool autoUpdate = true;
    private int localBuild, remoteBuild;
    private string remoteCommit = "";

    private string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InfiniteAscension");
    private string GameRoot => Path.Combine(InstallRoot, "Game");
    private string GamePath => Path.Combine(GameRoot, GameExe);

    public LauncherForm()
    {
        Text = "Infinite Ascension";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(12, 12, 20);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10F);
        BuildUi();
        gameTimer.Tick += (_, _) => RefreshGameState();
        Shown += async (_, _) => await InitializeAsync();
    }

    private void BuildUi()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.FromArgb(22, 20, 34) };
        top.Controls.Add(new Label { Text = "INFINITE ASCENSION", AutoSize = true, Font = new Font("Segoe UI Semibold", 18F), Location = new Point(28, 19) });
        serverLabel.AutoSize = true; serverLabel.Location = new Point(360, 26);
        buildLabel.AutoSize = true; buildLabel.Location = new Point(360, 47);
        top.Controls.Add(serverLabel); top.Controls.Add(buildLabel);

        nav.Dock = DockStyle.Left; nav.Width = 190; nav.BackColor = Color.FromArgb(18, 17, 27);
        AddNavButton("Home", 22); AddNavButton("Settings", 70); AddNavButton("Logs", 118);

        content.Dock = DockStyle.Fill; content.Padding = new Padding(26);
        home.Dock = DockStyle.Fill; content.Controls.Add(home);
        var hero = new Panel { Dock = DockStyle.Top, Height = 150, BackColor = Color.FromArgb(28, 25, 43) };
        hero.Controls.Add(new Label { Text = "Welcome back", AutoSize = true, Font = new Font("Segoe UI Semibold", 24F), Location = new Point(24, 20) });
        statusLabel.Text = "Checking launcher status..."; statusLabel.AutoSize = true; statusLabel.Location = new Point(26, 68);
        hero.Controls.Add(statusLabel); home.Controls.Add(hero);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 105, Padding = new Padding(0, 18, 0, 0), WrapContents = false };
        ConfigureButton(playButton, "PLAY", 140); ConfigureButton(updateButton, "UPDATE", 140); ConfigureButton(repairButton, "REPAIR", 140);
        ConfigureButton(stopButton, "STOP", 110); ConfigureButton(folderButton, "GAME FOLDER", 140); ConfigureButton(githubButton, "GITHUB", 120);
        actions.Controls.AddRange(new Control[] { playButton, updateButton, repairButton, stopButton, folderButton, githubButton }); home.Controls.Add(actions);
        progress.Dock = DockStyle.Top; progress.Height = 22; home.Controls.Add(progress);
        progressLabel.Dock = DockStyle.Top; progressLabel.Height = 34; progressLabel.Padding = new Padding(4, 8, 0, 0); home.Controls.Add(progressLabel);

        playButton.Click += async (_, _) => await PlayAsync();
        updateButton.Click += async (_, _) => await UpdateAsync(true);
        repairButton.Click += async (_, _) => await RepairAsync();
        stopButton.Click += (_, _) => StopGame();
        folderButton.Click += (_, _) => OpenGameFolder();
        githubButton.Click += (_, _) => Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });

        Controls.Add(content); Controls.Add(nav); Controls.Add(top);
    }

    private void AddNavButton(string text, int top)
    {
        var button = new Button { Text = text, Width = 160, Height = 38, Left = 14, Top = top, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 28, 44), ForeColor = Color.Gainsboro };
        button.Click += (_, _) => ShowSection(text); nav.Controls.Add(button);
    }

    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text; button.Width = width; button.Height = 46; button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(42, 37, 65); button.ForeColor = Color.White; button.Margin = new Padding(0, 0, 10, 0);
    }

    private void ShowSection(string section)
    {
        if (section == "Home") { content.Controls.Clear(); content.Controls.Add(home); return; }
        if (section == "Logs") { OpenLogs(); return; }
        using var form = new Form { Text = "Infinite Ascension — Settings", Width = 620, Height = 360, StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor };
        var auto = new CheckBox { Text = "Automatic game updates", Checked = autoUpdate, AutoSize = true, Left = 26, Top = 28 };
        var close = new Button { Text = "Close", Width = 100, Left = 26, Top = 80 };
        close.Click += (_, _) => { autoUpdate = auto.Checked; form.Close(); }; form.Controls.Add(auto); form.Controls.Add(close); form.ShowDialog(this);
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(InstallRoot); Directory.CreateDirectory(GameRoot); gameTimer.Start();
        await RefreshManifestAsync();
        if (autoUpdate && localBuild > 0 && remoteBuild > localBuild) await UpdateAsync(false);
    }

    private async Task RefreshManifestAsync()
    {
        try
        {
            using var response = await http.GetAsync(ManifestUrl); response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = doc.RootElement;
            remoteBuild = root.GetProperty("build").GetInt32(); remoteCommit = root.GetProperty("commit").GetString() ?? ""; localBuild = ReadLocalBuild();
            serverLabel.Text = "Updater: online"; buildLabel.Text = $"Build {localBuild} → {remoteBuild}";
            statusLabel.Text = remoteBuild > localBuild ? "A game update is available." : "Game is up to date.";
        }
        catch (Exception ex) { serverLabel.Text = "Updater: offline"; statusLabel.Text = "Updater unavailable: " + ex.Message; }
    }

    private int ReadLocalBuild()
    {
        foreach (var path in new[] { Path.Combine(GameRoot, "build.json") })
        {
            try { if (!File.Exists(path)) continue; using var doc = JsonDocument.Parse(File.ReadAllText(path)); if (doc.RootElement.TryGetProperty("build", out var b)) return b.GetInt32(); } catch { }
        }
        return 0;
    }

    private async Task PlayAsync()
    {
        if (busy) return;
        if (!File.Exists(GamePath)) { await UpdateAsync(false); if (!File.Exists(GamePath)) return; }
        try { gameProcess = Process.Start(new ProcessStartInfo(GamePath) { WorkingDirectory = GameRoot, UseShellExecute = true }); playButton.Enabled = false; statusLabel.Text = "Game running."; }
        catch (Exception ex) { statusLabel.Text = "Failed to start game: " + ex.Message; }
    }

    private async Task UpdateAsync(bool manual)
    {
        if (busy) return; busy = true;
        try
        {
            if (gameProcess is { HasExited: false }) { gameProcess.Kill(true); gameProcess.Dispose(); gameProcess = null; }
            await RefreshManifestAsync();
            if (!manual && remoteBuild <= localBuild) { statusLabel.Text = "Game is up to date."; return; }

            using var manifestResponse = await http.GetAsync(ManifestUrl); manifestResponse.EnsureSuccessStatusCode();
            using var manifest = JsonDocument.Parse(await manifestResponse.Content.ReadAsStringAsync()); var root = manifest.RootElement;
            var windows = root.GetProperty("assets").GetProperty("windows");
            var gameUrl = windows.GetProperty("url").GetString() ?? throw new InvalidOperationException("Manifest missing Windows game URL.");
            var expectedSha = windows.GetProperty("sha256").GetString()?.Trim().ToLowerInvariant() ?? throw new InvalidOperationException("Manifest missing Windows game SHA-256.");
            var targetBuild = root.GetProperty("build").GetInt32(); var targetCommit = root.GetProperty("commit").GetString() ?? "";
            if (expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit)) throw new InvalidOperationException("Manifest contains an invalid game SHA-256.");

            string work = Path.Combine(Path.GetTempPath(), "InfiniteAscensionUpdate", Guid.NewGuid().ToString("N"));
            string zipPath = Path.Combine(work, "game.zip"); string staging = Path.Combine(InstallRoot, "Game.next"); string rollback = Path.Combine(InstallRoot, "Game.rollback");
            Directory.CreateDirectory(work); statusLabel.Text = "Downloading game update...";
            await DownloadAsync(gameUrl, zipPath);
            var actualSha = await ComputeSha256Async(zipPath);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualSha), Convert.FromHexString(expectedSha))) throw new InvalidOperationException("Game package SHA-256 mismatch.");

            if (Directory.Exists(staging)) Directory.Delete(staging, true); Directory.CreateDirectory(staging); progressLabel.Text = "Extracting update...";
            ZipFile.ExtractToDirectory(zipPath, staging, true); EnsureGamePayload(staging);
            if (Directory.Exists(rollback)) Directory.Delete(rollback, true); if (Directory.Exists(GameRoot)) Directory.Move(GameRoot, rollback); Directory.Move(staging, GameRoot);
            File.WriteAllText(Path.Combine(GameRoot, "build.json"), JsonSerializer.Serialize(new { build = targetBuild, commit = targetCommit, platform = "windows" }));
            if (Directory.Exists(rollback)) Directory.Delete(rollback, true); localBuild = targetBuild; remoteCommit = targetCommit; progress.Value = 100;
            buildLabel.Text = $"Build {localBuild} → {remoteBuild}"; statusLabel.Text = manual ? "Update complete. Game ready." : "Update complete."; progressLabel.Text = "Update complete.";
            try { Directory.Delete(work, true); } catch { }
            if (manual) await PlayAsync();
        }
        catch (Exception ex) { statusLabel.Text = "Update failed: " + ex.Message; progressLabel.Text = ex.ToString(); }
        finally { busy = false; }
    }

    private async Task RepairAsync()
    {
        if (busy) return; await UpdateAsync(true);
    }

    private static void EnsureGamePayload(string root)
    {
        var direct = Path.Combine(root, GameExe); if (File.Exists(direct)) return;
        var found = Directory.EnumerateFiles(root, GameExe, SearchOption.AllDirectories).FirstOrDefault();
        if (found == null) throw new InvalidOperationException($"Package does not contain {GameExe}.");
        var sourceRoot = Path.GetDirectoryName(found)!;
        foreach (var path in Directory.EnumerateFileSystemEntries(sourceRoot))
        {
            var dest = Path.Combine(root, Path.GetFileName(path)); if (Directory.Exists(path)) Directory.Move(path, dest); else File.Move(path, dest, true);
        }
    }

    private async Task DownloadAsync(string url, string target)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Distribution URL must use HTTPS.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1; await using var source = await response.Content.ReadAsStreamAsync(); await using var destination = File.Create(target);
        var buffer = new byte[1024 * 1024]; long readTotal = 0; int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read)); readTotal += read;
            if (total > 0) { var percent = (int)Math.Clamp(readTotal * 100L / total, 0, 100); progress.Value = percent; progressLabel.Text = $"Downloading update… {percent}%"; }
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private void StopGame()
    {
        try { if (gameProcess is { HasExited: false }) gameProcess.Kill(true); gameProcess = null; playButton.Enabled = true; statusLabel.Text = "Game stopped."; } catch (Exception ex) { statusLabel.Text = "Stop failed: " + ex.Message; }
    }

    private void RefreshGameState()
    {
        try { if (gameProcess?.HasExited == true) { gameProcess.Dispose(); gameProcess = null; playButton.Enabled = true; statusLabel.Text = "Game exited."; } } catch { }
    }

    private void OpenGameFolder()
    {
        Directory.CreateDirectory(GameRoot); Process.Start(new ProcessStartInfo("explorer.exe", GameRoot) { UseShellExecute = true });
    }

    private void OpenLogs()
    {
        var path = Path.Combine(InstallRoot, "launcher.log"); if (!File.Exists(path)) File.WriteAllText(path, "Launcher log initialized.\n");
        Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
    }
}
