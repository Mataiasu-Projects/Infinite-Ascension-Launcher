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

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly Label serverLabel = new(), buildLabel = new(), statusLabel = new(), progressLabel = new();
    private readonly ProgressBar progress = new();
    private readonly Button playButton = new(), updateButton = new(), stopButton = new(), folderButton = new(), repairButton = new();
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
        BackColor = Color.FromArgb(10, 9, 16);
        ForeColor = Color.FromArgb(232, 230, 240);
        Font = new Font("Segoe UI", 10F);
        BuildUi();
        gameTimer.Tick += (_, _) => RefreshGameState();
        updateTimer.Tick += async (_, _) => await BackgroundRefreshAsync();
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += (_, _) => { gameTimer.Stop(); updateTimer.Stop(); http.Dispose(); };
    }

    private void BuildUi()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Color.FromArgb(20, 18, 30), Padding = new Padding(28, 0, 28, 0) };
        var title = new Label { Text = "INFINITE ASCENSION", AutoSize = true, Font = new Font("Segoe UI Semibold", 19F), ForeColor = Color.FromArgb(244, 242, 250), Location = new Point(28, 17) };
        var subtitle = new Label { Text = "GAME LAUNCHER", AutoSize = true, Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(150, 143, 174), Location = new Point(30, 49) };
        serverLabel.AutoSize = true; serverLabel.Font = new Font("Segoe UI Semibold", 9F); serverLabel.ForeColor = Color.FromArgb(180, 174, 198); serverLabel.Location = new Point(760, 18);
        buildLabel.AutoSize = true; buildLabel.Font = new Font("Segoe UI", 9F); buildLabel.ForeColor = Color.FromArgb(130, 124, 150); buildLabel.Location = new Point(760, 43);
        top.Controls.AddRange(new Control[] { title, subtitle, serverLabel, buildLabel });

        nav.Dock = DockStyle.Left; nav.Width = 184; nav.BackColor = Color.FromArgb(15, 14, 23); nav.Padding = new Padding(12, 18, 12, 0);
        AddNavButton("HOME", 18); AddNavButton("SETTINGS", 68); AddNavButton("LOGS", 118);

        content.Dock = DockStyle.Fill; content.Padding = new Padding(30, 28, 30, 30);
        home.Dock = DockStyle.Fill; home.BackColor = Color.FromArgb(10, 9, 16); content.Controls.Add(home);

        var hero = new Panel { Dock = DockStyle.Top, Height = 166, BackColor = Color.FromArgb(27, 23, 42), Padding = new Padding(26, 22, 26, 18) };
        hero.Controls.Add(new Label { Text = "READY FOR ASCENSION", AutoSize = true, Font = new Font("Segoe UI Semibold", 23F), ForeColor = Color.FromArgb(246, 244, 252), Location = new Point(24, 20) });
        statusLabel.Text = "Checking update service…"; statusLabel.AutoSize = false; statusLabel.Width = 820; statusLabel.Height = 46; statusLabel.Location = new Point(26, 70); statusLabel.Font = new Font("Segoe UI", 10F); statusLabel.ForeColor = Color.FromArgb(184, 178, 202); statusLabel.AutoEllipsis = true;
        hero.Controls.Add(statusLabel);
        home.Controls.Add(hero);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(0, 20, 0, 0), WrapContents = false, BackColor = Color.Transparent };
        ConfigureButton(playButton, "PLAY", 150, true); ConfigureButton(updateButton, "UPDATE", 135, false); ConfigureButton(repairButton, "REPAIR", 135, false);
        ConfigureButton(stopButton, "STOP", 110, false); ConfigureButton(folderButton, "GAME FOLDER", 150, false);
        actions.Controls.AddRange(new Control[] { playButton, updateButton, repairButton, stopButton, folderButton }); home.Controls.Add(actions);

        var info = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = Color.FromArgb(19, 17, 29), Padding = new Padding(20, 16, 20, 14) };
        var infoTitle = new Label { Text = "INSTALLATION", AutoSize = true, Font = new Font("Segoe UI Semibold", 9F), ForeColor = Color.FromArgb(158, 151, 181), Location = new Point(20, 14) };
        var infoText = new Label { AutoSize = false, Width = 850, Height = 30, Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(211, 207, 221), Location = new Point(20, 40), Text = "Local build 0  •  Remote build —  •  Verified packages" };
        info.Controls.AddRange(new Control[] { infoTitle, infoText });
        home.Controls.Add(info);

        progress.Dock = DockStyle.Top; progress.Height = 18; progress.Style = ProgressBarStyle.Continuous; progress.Margin = new Padding(0, 16, 0, 0); home.Controls.Add(progress);
        progressLabel.Dock = DockStyle.Top; progressLabel.Height = 34; progressLabel.Padding = new Padding(2, 7, 0, 0); progressLabel.ForeColor = Color.FromArgb(139, 133, 158); home.Controls.Add(progressLabel);

        playButton.Click += async (_, _) => await PlayAsync();
        updateButton.Click += async (_, _) => await UpdateAsync(true);
        repairButton.Click += async (_, _) => await RepairAsync();
        stopButton.Click += (_, _) => StopGame();
        folderButton.Click += (_, _) => OpenGameFolder();

        Controls.Add(content); Controls.Add(nav); Controls.Add(top);
    }

    private void AddNavButton(string text, int top)
    {
        var button = new Button { Text = text, Width = 160, Height = 40, Left = 12, Top = top, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(26, 23, 38), ForeColor = Color.FromArgb(207, 202, 219), FlatAppearance = { BorderColor = Color.FromArgb(53, 48, 71), BorderSize = 1 } };
        button.Click += (_, _) => ShowSection(text); nav.Controls.Add(button);
    }

    private static void ConfigureButton(Button button, string text, int width, bool primary)
    {
        button.Text = text; button.Width = width; button.Height = 46; button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Color.FromArgb(91, 67, 145) : Color.FromArgb(37, 33, 54);
        button.ForeColor = Color.White; button.Margin = new Padding(0, 0, 10, 0);
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(132, 102, 197) : Color.FromArgb(62, 56, 82);
        button.FlatAppearance.BorderSize = 1;
    }

    private void ShowSection(string section)
    {
        if (section == "HOME") { content.Controls.Clear(); content.Controls.Add(home); return; }
        if (section == "LOGS") { OpenLogs(); return; }
        using var form = new Form { Text = "Infinite Ascension — Settings", Width = 620, Height = 360, StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor };
        var auto = new CheckBox { Text = "Automatic game updates", Checked = autoUpdate, AutoSize = true, Left = 26, Top = 28 };
        var close = new Button { Text = "Close", Width = 100, Left = 26, Top = 80 };
        close.Click += (_, _) => { autoUpdate = auto.Checked; form.Close(); }; form.Controls.Add(auto); form.Controls.Add(close); form.ShowDialog(this);
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(InstallRoot); Directory.CreateDirectory(GameRoot);
        Log("Launcher started."); gameTimer.Start(); updateTimer.Start(); await RefreshManifestAsync();
        if (autoUpdate && localBuild > 0 && remoteBuild > localBuild) await UpdateAsync(false);
    }

    private async Task BackgroundRefreshAsync()
    {
        if (busy || gameProcess is { HasExited: false }) return;
        await RefreshManifestAsync(); if (autoUpdate && localBuild > 0 && remoteBuild > localBuild) await UpdateAsync(false);
    }

    private async Task RefreshManifestAsync()
    {
        try
        {
            using var response = await http.GetAsync(ManifestUrl + "?nocache=" + Guid.NewGuid().ToString("N"));
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Manifest HTTP {(int)response.StatusCode}: {TrimForUi(body)}");
            using var doc = JsonDocument.Parse(body); var root = doc.RootElement;
            remoteBuild = root.GetProperty("build").GetInt32(); remoteCommit = root.GetProperty("commit").GetString() ?? ""; localBuild = ReadLocalBuild();
            serverLabel.Text = "UPDATER  •  ONLINE"; serverLabel.ForeColor = Color.FromArgb(126, 211, 171); buildLabel.Text = $"Build {localBuild}  →  {remoteBuild}";
            statusLabel.Text = remoteBuild > localBuild ? "A game update is available. Select UPDATE to install the verified package." : "Game is up to date. Select PLAY to launch.";
            Log($"Manifest OK: local={localBuild}, remote={remoteBuild}, commit={remoteCommit}");
        }
        catch (Exception ex)
        {
            serverLabel.Text = "UPDATER  •  OFFLINE"; serverLabel.ForeColor = Color.FromArgb(220, 120, 132); statusLabel.Text = "Update service unavailable: " + TrimForUi(ex.Message); Log("Manifest failed: " + ex);
        }
    }

    private int ReadLocalBuild()
    {
        try
        {
            string path = Path.Combine(GameRoot, "build.json"); if (!File.Exists(path)) return 0;
            using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty("build", out var b) && b.TryGetInt32(out int value) ? value : 0;
        }
        catch (Exception ex) { Log("Read local build failed: " + ex.Message); return 0; }
    }

    private async Task PlayAsync()
    {
        if (busy) return;
        if (!File.Exists(GamePath))
        {
            Log("Play requested but game executable is missing; installing game."); await UpdateAsync(false); if (!File.Exists(GamePath)) return;
        }
        try
        {
            gameProcess = Process.Start(new ProcessStartInfo(GamePath) { WorkingDirectory = GameRoot, UseShellExecute = true });
            playButton.Enabled = false; statusLabel.Text = "Game running."; Log($"Game started: {GamePath}");
        }
        catch (Exception ex) { statusLabel.Text = "Failed to start game: " + ex.Message; Log("Game start failed: " + ex); }
    }

    private async Task UpdateAsync(bool manual)
    {
        if (busy) return; busy = true; SetActionButtons(false);
        string work = Path.Combine(Path.GetTempPath(), "InfiniteAscensionUpdate", Guid.NewGuid().ToString("N")); string staging = Path.Combine(InstallRoot, "Game.next"); string rollback = Path.Combine(InstallRoot, "Game.rollback");
        try
        {
            if (gameProcess is { HasExited: false }) { Log("Stopping running game before update."); gameProcess.Kill(true); gameProcess.Dispose(); gameProcess = null; }
            await RefreshManifestAsync();
            using var manifestResponse = await http.GetAsync(ManifestUrl + "?nocache=" + Guid.NewGuid().ToString("N")); string manifestBody = await manifestResponse.Content.ReadAsStringAsync();
            if (!manifestResponse.IsSuccessStatusCode) throw new HttpRequestException($"Manifest HTTP {(int)manifestResponse.StatusCode}: {TrimForUi(manifestBody)}");
            using var manifest = JsonDocument.Parse(manifestBody); var root = manifest.RootElement;
            var windows = root.GetProperty("assets").GetProperty("windows"); var gameUrl = windows.GetProperty("url").GetString() ?? throw new InvalidOperationException("Manifest missing Windows game URL.");
            var expectedSha = windows.GetProperty("sha256").GetString()?.Trim().ToLowerInvariant() ?? throw new InvalidOperationException("Manifest missing Windows game SHA-256.");
            var targetBuild = root.GetProperty("build").GetInt32(); var targetCommit = root.GetProperty("commit").GetString() ?? "";
            if (targetBuild < 1 || targetCommit.Length != 40 || !targetCommit.All(Uri.IsHexDigit)) throw new InvalidOperationException("Manifest contains an invalid game build or commit.");
            if (expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit)) throw new InvalidOperationException("Manifest contains an invalid game SHA-256.");
            if (!Uri.TryCreate(gameUrl, UriKind.Absolute, out var gameUri) || gameUri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Manifest game URL must use HTTPS.");
            if (!manual && localBuild > 0 && targetBuild <= localBuild) { statusLabel.Text = "Game is up to date."; return; }

            string zipPath = Path.Combine(work, "game.zip"); Directory.CreateDirectory(work); if (Directory.Exists(staging)) Directory.Delete(staging, true); Directory.CreateDirectory(staging);
            statusLabel.Text = $"Downloading game build {targetBuild}…"; Log($"Game update download: build={targetBuild}, url={gameUrl}"); await DownloadAsync(gameUri, zipPath);
            var actualSha = await ComputeSha256Async(zipPath); Log($"Game SHA-256: expected={expectedSha}, actual={actualSha}");
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualSha), Convert.FromHexString(expectedSha))) throw new InvalidOperationException("Game package SHA-256 mismatch.");
            progressLabel.Text = "Extracting verified update…"; ZipFile.ExtractToDirectory(zipPath, staging, true); EnsureGamePayload(staging);
            if (Directory.Exists(rollback)) Directory.Delete(rollback, true); if (Directory.Exists(GameRoot)) Directory.Move(GameRoot, rollback); Directory.Move(staging, GameRoot);
            File.WriteAllText(Path.Combine(GameRoot, "build.json"), JsonSerializer.Serialize(new { build = targetBuild, commit = targetCommit, platform = "windows" }, new JsonSerializerOptions { WriteIndented = true })); if (Directory.Exists(rollback)) Directory.Delete(rollback, true);
            localBuild = targetBuild; remoteBuild = targetBuild; remoteCommit = targetCommit; progress.Value = 100; buildLabel.Text = $"Build {localBuild}  →  {remoteBuild}";
            statusLabel.Text = manual ? "Update complete. Game ready." : "Update installed automatically. Game ready.";
        }
        catch (Exception ex) { statusLabel.Text = "Update failed: " + TrimForUi(ex.Message); Log("Update failed: " + ex); }
        finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } busy = false; SetActionButtons(true); RefreshGameState(); }
    }

    private async Task RepairAsync() => await UpdateAsync(true);

    private async Task DownloadAsync(Uri uri, string destination)
    {
        progress.Value = 0; progressLabel.Text = "Downloading verified package…";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(); await using var output = File.Create(destination);
        byte[] buffer = new byte[1024 * 128]; long readTotal = 0; int read;
        while ((read = await input.ReadAsync(buffer)) > 0) { await output.WriteAsync(buffer.AsMemory(0, read)); readTotal += read; if (total is > 0) progress.Value = Math.Min(100, (int)(readTotal * 100 / total.Value)); }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static void EnsureGamePayload(string root)
    {
        if (!File.Exists(Path.Combine(root, GameExe))) throw new InvalidOperationException("Verified package does not contain the game executable.");
    }

    private void SetActionButtons(bool enabled)
    {
        playButton.Enabled = enabled && gameProcess is not { HasExited: false }; updateButton.Enabled = enabled; repairButton.Enabled = enabled; stopButton.Enabled = gameProcess is { HasExited: false }; folderButton.Enabled = enabled;
    }

    private void RefreshGameState()
    {
        if (gameProcess is { HasExited: true }) { gameProcess.Dispose(); gameProcess = null; playButton.Enabled = !busy; statusLabel.Text = "Game stopped. Ready to launch."; }
        stopButton.Enabled = gameProcess is { HasExited: false };
    }

    private void StopGame()
    {
        try { if (gameProcess is { HasExited: false }) gameProcess.Kill(true); } catch (Exception ex) { Log("Stop failed: " + ex.Message); }
        RefreshGameState();
    }

    private void OpenGameFolder()
    {
        Directory.CreateDirectory(GameRoot); Process.Start(new ProcessStartInfo("explorer.exe", GameRoot) { UseShellExecute = true });
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(InstallRoot); if (!File.Exists(LogPath)) File.WriteAllText(LogPath, "");
        Process.Start(new ProcessStartInfo("notepad.exe", LogPath) { UseShellExecute = true });
    }

    private void Log(string message)
    {
        try { Directory.CreateDirectory(InstallRoot); File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}"); } catch { }
    }

    private static string TrimForUi(string value) => value.Length <= 500 ? value : value[..500];
}