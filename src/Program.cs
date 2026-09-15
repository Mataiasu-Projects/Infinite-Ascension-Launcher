using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly Label serverLabel = new(), buildLabel = new(), statusLabel = new(), progressLabel = new();
    private readonly ProgressBar progress = new();
    private readonly Button playButton = new(), updateButton = new(), stopButton = new(), folderButton = new(), repairButton = new(), githubButton = new();
    private readonly Panel content = new(), nav = new(), home = new();
    private readonly System.Windows.Forms.Timer gameTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 300000 };

    private Process? gameProcess;
    private bool busy;
    private bool autoUpdate = true;
    private int localBuild, remoteBuild;
    private string remoteCommit = "";

    private string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InfiniteAscension");
    private string GameRoot => Path.Combine(InstallRoot, "Game");
    private string GamePath => Path.Combine(GameRoot, GameExe);
    private string LogPath => Path.Combine(InstallRoot, "launcher.log");

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
        updateTimer.Tick += async (_, _) => await BackgroundRefreshAsync();
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += (_, _) => { gameTimer.Stop(); updateTimer.Stop(); http.Dispose(); };
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
        Directory.CreateDirectory(InstallRoot); Directory.CreateDirectory(GameRoot);
        Log("Launcher started.");
        gameTimer.Start();
        updateTimer.Start();
        await RefreshManifestAsync();
        if (autoUpdate && localBuild > 0 && remoteBuild > localBuild) await UpdateAsync(false);
    }

    private async Task BackgroundRefreshAsync()
    {
        if (busy || gameProcess is { HasExited: false }) return;
        await RefreshManifestAsync();
        if (autoUpdate && localBuild > 0 && remoteBuild > localBuild) await UpdateAsync(false);
    }

    private async Task RefreshManifestAsync()
    {
        try
        {
            using var response = await http.GetAsync(ManifestUrl + "?nocache=" + Guid.NewGuid().ToString("N"));
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Manifest HTTP {(int)response.StatusCode}: {TrimForUi(body)}");
            using var doc = JsonDocument.Parse(body); var root = doc.RootElement;
            remoteBuild = root.GetProperty("build").GetInt32();
            remoteCommit = root.GetProperty("commit").GetString() ?? "";
            localBuild = ReadLocalBuild();
            serverLabel.Text = "Updater: online"; buildLabel.Text = $"Build {localBuild} → {remoteBuild}";
            statusLabel.Text = remoteBuild > localBuild ? "A game update is available." : "Game is up to date.";
            Log($"Manifest OK: local={localBuild}, remote={remoteBuild}, commit={remoteCommit}");
        }
        catch (Exception ex)
        {
            serverLabel.Text = "Updater: offline"; statusLabel.Text = "Updater unavailable: " + TrimForUi(ex.Message);
            Log("Manifest failed: " + ex);
        }
    }

    private int ReadLocalBuild()
    {
        try
        {
            string path = Path.Combine(GameRoot, "build.json");
            if (!File.Exists(path)) return 0;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("build", out var b) && b.TryGetInt32(out int value) ? value : 0;
        }
        catch (Exception ex) { Log("Read local build failed: " + ex.Message); return 0; }
    }

    private async Task PlayAsync()
    {
        if (busy) return;
        if (!File.Exists(GamePath))
        {
            Log("Play requested but game executable is missing; installing game.");
            await UpdateAsync(false);
            if (!File.Exists(GamePath)) return;
        }
        try
        {
            gameProcess = Process.Start(new ProcessStartInfo(GamePath) { WorkingDirectory = GameRoot, UseShellExecute = true });
            playButton.Enabled = false;
            statusLabel.Text = "Game running.";
            Log($"Game started: {GamePath}");
        }
        catch (Exception ex) { statusLabel.Text = "Failed to start game: " + ex.Message; Log("Game start failed: " + ex); }
    }

    private async Task UpdateAsync(bool manual)
    {
        if (busy) return;
        busy = true;
        SetActionButtons(false);
        string work = Path.Combine(Path.GetTempPath(), "InfiniteAscensionUpdate", Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(InstallRoot, "Game.next");
        string rollback = Path.Combine(InstallRoot, "Game.rollback");
        try
        {
            if (gameProcess is { HasExited: false })
            {
                Log("Stopping running game before update.");
                gameProcess.Kill(true); gameProcess.Dispose(); gameProcess = null;
            }

            await RefreshManifestAsync();
            using var manifestResponse = await http.GetAsync(ManifestUrl + "?nocache=" + Guid.NewGuid().ToString("N"));
            string manifestBody = await manifestResponse.Content.ReadAsStringAsync();
            if (!manifestResponse.IsSuccessStatusCode) throw new HttpRequestException($"Manifest HTTP {(int)manifestResponse.StatusCode}: {TrimForUi(manifestBody)}");
            using var manifest = JsonDocument.Parse(manifestBody); var root = manifest.RootElement;

            var windows = root.GetProperty("assets").GetProperty("windows");
            var gameUrl = windows.GetProperty("url").GetString() ?? throw new InvalidOperationException("Manifest missing Windows game URL.");
            var expectedSha = windows.GetProperty("sha256").GetString()?.Trim().ToLowerInvariant() ?? throw new InvalidOperationException("Manifest missing Windows game SHA-256.");
            var targetBuild = root.GetProperty("build").GetInt32();
            var targetCommit = root.GetProperty("commit").GetString() ?? "";
            if (targetBuild < 1 || targetCommit.Length != 40 || !targetCommit.All(Uri.IsHexDigit)) throw new InvalidOperationException("Manifest contains an invalid game build or commit.");
            if (expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit)) throw new InvalidOperationException("Manifest contains an invalid game SHA-256.");
            if (!Uri.TryCreate(gameUrl, UriKind.Absolute, out var gameUri) || gameUri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Manifest game URL must use HTTPS.");

            if (!manual && localBuild > 0 && targetBuild <= localBuild)
            {
                statusLabel.Text = "Game is up to date.";
                return;
            }

            string zipPath = Path.Combine(work, "game.zip");
            Directory.CreateDirectory(work);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            statusLabel.Text = $"Downloading game build {targetBuild}...";
            Log($"Game update download: build={targetBuild}, url={gameUrl}");
            await DownloadAsync(gameUri, zipPath);

            var actualSha = await ComputeSha256Async(zipPath);
            Log($"Game SHA-256: expected={expectedSha}, actual={actualSha}");
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualSha), Convert.FromHexString(expectedSha))) throw new InvalidOperationException("Game package SHA-256 mismatch.");

            progressLabel.Text = "Extracting update...";
            ZipFile.ExtractToDirectory(zipPath, staging, true);
            EnsureGamePayload(staging);

            if (Directory.Exists(rollback)) Directory.Delete(rollback, true);
            if (Directory.Exists(GameRoot)) Directory.Move(GameRoot, rollback);
            Directory.Move(staging, GameRoot);
            File.WriteAllText(Path.Combine(GameRoot, "build.json"), JsonSerializer.Serialize(new { build = targetBuild, commit = targetCommit, platform = "windows" }, new JsonSerializerOptions { WriteIndented = true }));
            if (Directory.Exists(rollback)) Directory.Delete(rollback, true);

            localBuild = targetBuild; remoteBuild = targetBuild; remoteCommit = targetCommit; progress.Value = 100;
            buildLabel.Text = $"Build {localBuild} → {remoteBuild}";
            statusLabel.Text = manual ? "Update complete. Game ready." : "Update complete.";
            progressLabel.Text = "Update complete.";
            Log($"Game update committed: build={targetBuild}");
            if (manual) await PlayAsync();
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Update failed: " + TrimForUi(ex.Message);
            progressLabel.Text = ex.ToString();
            Log("Game update failed: " + ex);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            SetActionButtons(true);
            busy = false;
        }
    }

    private async Task RepairAsync()
    {
        if (busy) return;
        Log("Repair requested: forcing a fresh verified game package install.");
        await UpdateAsync(true);
    }

    private static void EnsureGamePayload(string root)
    {
        var direct = Path.Combine(root, GameExe); if (File.Exists(direct)) return;
        var found = Directory.EnumerateFiles(root, GameExe, SearchOption.AllDirectories).FirstOrDefault();
        if (found == null) throw new InvalidOperationException($"Package does not contain {GameExe}.");
        var sourceRoot = Path.GetDirectoryName(found)!;
        foreach (var path in Directory.EnumerateFileSystemEntries(sourceRoot))
        {
            var dest = Path.Combine(root, Path.GetFileName(path));
            if (Directory.Exists(path)) Directory.Move(path, dest); else File.Move(path, dest, true);
        }
        if (!File.Exists(Path.Combine(root, GameExe))) throw new InvalidOperationException($"Unable to normalize {GameExe} payload.");
    }

    private async Task DownloadAsync(Uri uri, string target)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Game download HTTP {(int)response.StatusCode}: {TrimForUi(await response.Content.ReadAsStringAsync())}");
        var total = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = File.Create(target);
        var buffer = new byte[1024 * 1024]; long readTotal = 0; int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read)); readTotal += read;
            if (total > 0)
            {
                var percent = (int)Math.Clamp(readTotal * 100L / total, 0, 100);
                progress.Value = percent; progressLabel.Text = $"Downloading update… {percent}%";
            }
        }
        if (new FileInfo(target).Length == 0) throw new InvalidOperationException("Downloaded game package is empty.");
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private void StopGame()
    {
        try
        {
            if (gameProcess is { HasExited: false }) gameProcess.Kill(true);
            gameProcess?.Dispose(); gameProcess = null; playButton.Enabled = true; statusLabel.Text = "Game stopped."; Log("Game stopped.");
        }
        catch (Exception ex) { statusLabel.Text = "Stop failed: " + ex.Message; Log("Game stop failed: " + ex); }
    }

    private void RefreshGameState()
    {
        try
        {
            if (gameProcess?.HasExited == true)
            {
                int code = gameProcess.ExitCode;
                gameProcess.Dispose(); gameProcess = null; playButton.Enabled = true; statusLabel.Text = code == 0 ? "Game exited." : $"Game exited with code {code}."; Log($"Game exited: code={code}");
            }
        }
        catch (Exception ex) { Log("Game state check failed: " + ex.Message); }
    }

    private void OpenGameFolder()
    {
        Directory.CreateDirectory(GameRoot); Process.Start(new ProcessStartInfo("explorer.exe", GameRoot) { UseShellExecute = true });
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(InstallRoot);
        if (!File.Exists(LogPath)) File.WriteAllText(LogPath, "Launcher log initialized.\n");
        Process.Start(new ProcessStartInfo("notepad.exe", LogPath) { UseShellExecute = true });
    }

    private void SetActionButtons(bool enabled)
    {
        playButton.Enabled = enabled && gameProcess is null;
        updateButton.Enabled = enabled;
        repairButton.Enabled = enabled;
        folderButton.Enabled = enabled;
        githubButton.Enabled = enabled;
        stopButton.Enabled = enabled && gameProcess is { HasExited: false };
    }

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(InstallRoot);
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    private static string TrimForUi(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
