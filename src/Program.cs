using System.Diagnostics;
using System.Drawing;
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
    private const string HealthUrl = WorkerBaseUrl + "/health";
    private const string GameExe = "InfiniteAscension.exe";
    private const string GitHubUrl = "https://github.com/Mataiasu-Projects/Infinite-Ascension";

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly Label serverLabel = new(), buildLabel = new(), statusLabel = new(), progressLabel = new();
    private readonly ProgressBar progress = new();
    private readonly Button playButton = new(), updateButton = new(), logsButton = new(), stopButton = new(), folderButton = new(), repairButton = new(), serverButton = new(), githubButton = new();
    private readonly Panel content = new(), nav = new(), home = new();
    private readonly Dictionary<string, Button> navButtons = new();
    private readonly System.Windows.Forms.Timer gameTimer = new() { Interval = 1000 };

    private Process? gameProcess;
    private bool busy, serverCheckRunning, autoUpdate = true;
    private int localBuild, remoteBuild;
    private string remoteCommit = "";
    private string? remoteGameUrl, remoteGameSha;

    private string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InfiniteAscension");
    private string GameDir => Path.Combine(RootDir, "Game");
    private string GamePath => Path.Combine(GameDir, GameExe);
    private string LogsDir => Path.Combine(RootDir, "Logs");
    private string LauncherLog => Path.Combine(LogsDir, "launcher.log");
    private string GameLog => Path.Combine(LogsDir, "game.log");
    private string StatePath => Path.Combine(RootDir, "launcher_state.json");
    private string SettingsPath => Path.Combine(RootDir, "launcher_settings.json");

    public LauncherForm()
    {
        Text = "Infinite Ascension Launcher";
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(5, 7, 17);
        ForeColor = Color.White;
        DoubleBuffered = true;
        LoadSettings();
        localBuild = ReadLocalBuild();
        Directory.CreateDirectory(LogsDir);
        BuildChrome(); BuildHome(); BuildPages(); SelectPage("home");
        Resize += (_, _) => LayoutEverything();
        Shown += async (_, _) => await StartupAsync();
        FormClosing += (_, e) =>
        {
            if (!busy) return;
            var answer = MessageBox.Show("Une opération est en cours. Fermer le launcher maintenant ?", "Infinite Ascension", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) e.Cancel = true;
        };
        gameTimer.Tick += (_, _) => RefreshGameState();
        gameTimer.Start();
        LayoutEverything();
    }

    private void BuildChrome()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.FromArgb(8, 10, 22) };
        top.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(103, 61, 220)); e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1); };
        Controls.Add(top);
        top.Controls.Add(MakeLabel("✦  INFINITE ASCENSION", 18, 12, 300, 32, 16, Color.White, true));
        top.Controls.Add(MakeLabel("LAUNCHER  ·  v2.2", 315, 19, 160, 22, 9, Color.FromArgb(177, 132, 255), true));
        serverLabel.Text = "●  Distribution : vérification…"; serverLabel.AutoSize = true; serverLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold); top.Controls.Add(serverLabel);
        var minimize = ChromeButton("—", 40, 38, 11); var maximize = ChromeButton("□", 40, 38, 10); var close = ChromeButton("×", 42, 38, 17);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maximize.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        close.Click += (_, _) => Close();
        top.Controls.Add(minimize); top.Controls.Add(maximize); top.Controls.Add(close);
        top.Resize += (_, _) => { close.Location = new Point(top.Width - 48, 9); maximize.Location = new Point(top.Width - 90, 9); minimize.Location = new Point(top.Width - 132, 9); serverLabel.Location = new Point(Math.Max(500, top.Width - 335), 19); };

        nav.Dock = DockStyle.Left; nav.Width = 205; nav.BackColor = Color.FromArgb(7, 9, 19);
        nav.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(47, 40, 79)); e.Graphics.DrawLine(pen, nav.Width - 1, 0, nav.Width - 1, nav.Height); };
        Controls.Add(nav);
        AddNav("home", "⌂   ACCUEIL", 70); AddNavAction("play", "▶   JOUER", 122, PlayGame); AddNavAction("update", "↻   MISE À JOUR", 174, () => _ = CheckAndUpdateAsync(true));
        AddNav("craft", "✦   IA CRAFT", 226); AddNav("news", "▣   ACTUALITÉS", 278); AddNav("ranking", "♜   CLASSEMENTS", 330); AddNav("community", "♟   COMMUNAUTÉ", 382);
        AddNavAction("logs", "▤   JOURNAUX", 434, OpenLogs); AddNavAction("folder", "▰   DOSSIER DU JEU", 486, OpenGameFolder); AddNavAction("stop", "■   ARRÊTER LE JEU", 538, StopGame); AddNav("settings", "⚙   PARAMÈTRES", 590);
        var footer = MakeLabel("MATAIASU  ·  PLAY BEYOND LIMITS", 12, 0, 181, 44, 8, Color.FromArgb(95, 100, 128), false); footer.TextAlign = ContentAlignment.MiddleCenter; footer.Dock = DockStyle.Bottom; nav.Controls.Add(footer);
        content.Dock = DockStyle.Fill; content.BackColor = Color.FromArgb(5, 7, 17); content.Padding = new Padding(28, 22, 28, 22); content.AutoScroll = true; Controls.Add(content); content.BringToFront();
    }

    private void AddNav(string key, string text, int top) { var b = CreateNavButton(text, top); b.Click += (_, _) => SelectPage(key); nav.Controls.Add(b); navButtons[key] = b; }
    private void AddNavAction(string key, string text, int top, Action action) { var b = CreateNavButton(text, top); b.Click += (_, _) => action(); nav.Controls.Add(b); navButtons[key] = b; }
    private static Button CreateNavButton(string text, int top) => new() { Text = text, Location = new Point(0, top), Size = new Size(204, 50), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = Color.Transparent, ForeColor = Color.FromArgb(190, 187, 212), Font = new Font("Segoe UI", 9.2f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(22, 0, 0, 0), Cursor = Cursors.Hand };

    private void BuildHome()
    {
        home.Dock = DockStyle.Fill; content.Controls.Add(home);
        home.Controls.Add(MakeLabel("INFINITE ASCENSION", 0, 0, 700, 48, 29, Color.White, true));
        home.Controls.Add(MakeLabel("L'AVENTURE SANS FIN", 2, 48, 500, 25, 10, Color.FromArgb(181, 129, 255), true));
        var hero = Card(0, 84, 650, 170); home.Controls.Add(hero);
        statusLabel.Text = "Initialisation…"; statusLabel.Font = new Font("Segoe UI", 12, FontStyle.Bold); statusLabel.AutoSize = true; statusLabel.Location = new Point(22, 22); hero.Controls.Add(statusLabel);
        buildLabel.Text = "Build locale : —   ·   Disponible : —"; buildLabel.Font = new Font("Segoe UI", 10); buildLabel.ForeColor = Color.FromArgb(192, 195, 218); buildLabel.AutoSize = true; buildLabel.Location = new Point(22, 58); hero.Controls.Add(buildLabel);
        progress.Minimum = 0; progress.Maximum = 100; progress.Size = new Size(600, 12); progress.Location = new Point(22, 94); hero.Controls.Add(progress);
        progressLabel.Font = new Font("Segoe UI", 8.5f); progressLabel.ForeColor = Color.FromArgb(139, 144, 172); progressLabel.AutoSize = true; progressLabel.Location = new Point(22, 118); hero.Controls.Add(progressLabel);

        ConfigureAction(playButton, "▶   JOUER", Color.FromArgb(104, 57, 218), new Point(0, 404), new Size(200, 48)); playButton.Font = new Font("Segoe UI", 12, FontStyle.Bold); playButton.Click += (_, _) => PlayGame(); home.Controls.Add(playButton);
        ConfigureAction(updateButton, "↻   MISE À JOUR", Color.FromArgb(22, 25, 46), new Point(210, 404), new Size(200, 48)); updateButton.Click += async (_, _) => await CheckAndUpdateAsync(true); home.Controls.Add(updateButton);
        ConfigureAction(repairButton, "⟳   RÉPARER LE JEU", Color.FromArgb(34, 28, 51), new Point(420, 404), new Size(200, 48)); repairButton.Click += async (_, _) => await CheckAndUpdateAsync(true, true); home.Controls.Add(repairButton);
        ConfigureAction(logsButton, "▤   JOURNAUX", Color.FromArgb(22, 25, 46), new Point(0, 462), new Size(200, 44)); logsButton.Click += (_, _) => OpenLogs(); home.Controls.Add(logsButton);
        ConfigureAction(folderButton, "▰   DOSSIER DU JEU", Color.FromArgb(22, 25, 46), new Point(210, 462), new Size(200, 44)); folderButton.Click += (_, _) => OpenGameFolder(); home.Controls.Add(folderButton);
        ConfigureAction(serverButton, "●   TESTER LA DISTRIBUTION", Color.FromArgb(22, 25, 46), new Point(420, 462), new Size(200, 44)); serverButton.Click += async (_, _) => await CheckServerAsync(); home.Controls.Add(serverButton);
        ConfigureAction(stopButton, "■   ARRÊTER LE JEU", Color.FromArgb(55, 25, 42), new Point(0, 520), new Size(620, 44)); stopButton.Click += (_, _) => StopGame(); home.Controls.Add(stopButton);

        var install = Card(0, 274, 650, 110); home.Controls.Add(install); AddCardTitle(install, "INSTALLATION", 18);
        var installText = MakeLabel(GameDir, 18, 48, 610, 24, 9, Color.FromArgb(170, 174, 201), false); installText.AutoEllipsis = true; install.Controls.Add(installText);
        install.Controls.Add(MakeLabel("Les mises à jour sont téléchargées depuis la distribution publique et vérifiées par SHA-256.", 18, 76, 610, 22, 8.5f, Color.FromArgb(111, 117, 148), false));
        var info = Card(675, 84, 350, 480); home.Controls.Add(info); AddCardTitle(info, "ÉTAT", 18); AddInfoRow(info, "Jeu", "Local", 60); AddInfoRow(info, "Distribution", "—", 94); AddInfoRow(info, "Build", "—", 128); AddInfoRow(info, "Commit", "—", 162); AddInfoRow(info, "Installation", "—", 196); AddInfoRow(info, "Processus", "Arrêté", 230);
        info.Controls.Add(MakeLabel("Une mise à jour manuelle ferme le jeu, installe la nouvelle version puis relance automatiquement le jeu.", 18, 274, 314, 55, 8.5f, Color.FromArgb(118, 123, 151), false));
        ConfigureAction(githubButton, "◆   GITHUB DU PROJET", Color.FromArgb(22, 25, 46), new Point(18, 338), new Size(314, 46)); githubButton.Click += (_, _) => OpenUrl(GitHubUrl); info.Controls.Add(githubButton);
    }

    private void BuildPages()
    {
        BuildSimplePage("craft", "IA CRAFT", "Outils de création pour Infinite Ascension.", "Interface IA Craft isolée du système de mise à jour.");
        BuildSimplePage("news", "ACTUALITÉS", "Informations du projet.", "Les actualités dynamiques seront affichées lorsqu'une source officielle sera branchée.");
        BuildSimplePage("ranking", "CLASSEMENTS", "Classements serveur.", "Aucune donnée serveur fiable n'est actuellement exposée au launcher.");
        BuildSimplePage("community", "COMMUNAUTÉ", "Espaces communautaires.", "Les liens officiels seront ajoutés depuis la configuration du projet.");
        BuildSettingsPage();
    }

    private void BuildSimplePage(string key, string title, string subtitle, string body)
    {
        var p = new Panel { Name = "Page_" + key, Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        p.Controls.Add(MakeLabel(title, 22, 18, 700, 44, 28, Color.White, true)); p.Controls.Add(MakeLabel(subtitle, 24, 62, 800, 28, 10, Color.FromArgb(181, 129, 255), true));
        var card = Card(22, 116, 0, 180); card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; p.Controls.Add(card); AddCardTitle(card, title, 18);
        var bodyLabel = MakeLabel(body, 18, 54, 0, 100, 10, Color.FromArgb(216, 215, 231), false); bodyLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; card.Controls.Add(bodyLabel); content.Controls.Add(p);
    }

    private void BuildSettingsPage()
    {
        var p = new Panel { Name = "Page_settings", Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        p.Controls.Add(MakeLabel("PARAMÈTRES", 22, 18, 700, 44, 28, Color.White, true)); p.Controls.Add(MakeLabel("Configuration locale du launcher.", 24, 62, 800, 28, 10, Color.FromArgb(181, 129, 255), true));
        var card = Card(22, 116, 0, 230); card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; p.Controls.Add(card); AddCardTitle(card, "MISES À JOUR", 18);
        var auto = new CheckBox { Text = "Vérifier automatiquement les mises à jour au démarrage", Checked = autoUpdate, Location = new Point(18, 58), AutoSize = true, ForeColor = Color.White, BackColor = Color.Transparent };
        auto.CheckedChanged += (_, _) => { autoUpdate = auto.Checked; SaveSettings(); Log($"Auto-update={(autoUpdate ? "on" : "off")}"); }; card.Controls.Add(auto);
        card.Controls.Add(MakeLabel("Le manifeste et le SHA-256 sont vérifiés avant installation.", 18, 98, 700, 42, 9, Color.FromArgb(135, 140, 168), false));
        var manual = new Button { Text = "↻  VÉRIFIER MAINTENANT", Location = new Point(18, 152), Size = new Size(220, 42), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = Color.FromArgb(104, 57, 218), ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
        manual.Click += async (_, _) => await CheckAndUpdateAsync(true); card.Controls.Add(manual); content.Controls.Add(p);
    }

    private async Task StartupAsync()
    {
        UpdateUiState(); await CheckServerAsync();
        if (autoUpdate) await CheckAndUpdateAsync(false);
        else SetStatus(File.Exists(GamePath) ? "Prêt à jouer. Mise à jour automatique désactivée." : "Jeu non installé. Lancez une mise à jour.");
    }

    private async Task CheckServerAsync()
    {
        if (serverCheckRunning) return; serverCheckRunning = true;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{HealthUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"); req.Headers.UserAgent.ParseAdd("Infinite-Ascension-Launcher/12.0");
            using var response = await http.SendAsync(req); if (response.IsSuccessStatusCode) SetServer("●  Distribution : en ligne", true); else SetServer($"●  Distribution : indisponible ({(int)response.StatusCode})", false);
        }
        catch (Exception ex) { Log($"Health check: {ex.Message}"); SetServer("●  Distribution : hors ligne", false); }
        finally { serverCheckRunning = false; }
    }

    private async Task CheckAndUpdateAsync(bool manual, bool forceRepair = false)
    {
        if (busy) return;
        bool restartGameAfterUpdate = manual && IsGameRunning();
        if (restartGameAfterUpdate)
        {
            SetStatus("Fermeture du jeu avant la mise à jour…");
            Log("Mise à jour manuelle demandée pendant que le jeu tournait : fermeture automatique.");
            StopGame();
            await Task.Delay(300);
            if (IsGameRunning()) { SetStatus("Impossible de fermer le jeu automatiquement."); return; }
        }
        if (gameProcess is { HasExited: false }) { SetStatus("Fermez le jeu avant de le mettre à jour."); return; }
        busy = true; SetBusy(true);
        try
        {
            Directory.CreateDirectory(GameDir); SetStatus(manual ? (forceRepair ? "Réparation du jeu…" : "Recherche des mises à jour…") : "Connexion à la distribution…"); progress.Value = 0; progressLabel.Text = "Lecture du manifeste…";
            using var manifest = await GetManifestAsync(); var root = manifest.RootElement;
            remoteBuild = root.GetProperty("build").GetInt32(); remoteCommit = root.TryGetProperty("commit", out var c) ? c.GetString() ?? "" : "";
            if (!root.TryGetProperty("assets", out var assets) || !assets.TryGetProperty("windows", out var asset)) throw new InvalidOperationException("Le manifeste ne contient pas d'asset Windows.");
            remoteGameUrl = asset.GetProperty("url").GetString(); remoteGameSha = asset.GetProperty("sha256").GetString();
            if (string.IsNullOrWhiteSpace(remoteGameUrl) || string.IsNullOrWhiteSpace(remoteGameSha) || !IsSha256(remoteGameSha)) throw new InvalidOperationException("Asset Windows invalide : URL ou SHA-256 absent.");
            buildLabel.Text = $"Build locale : #{localBuild}   ·   Disponible : #{remoteBuild}";
            if (forceRepair || !File.Exists(GamePath) || remoteBuild > localBuild) { await InstallGameAsync(remoteGameUrl!, remoteGameSha!, remoteBuild, remoteCommit); SetStatus($"Jeu prêt — build #{remoteBuild}."); }
            else { progress.Value = 100; progressLabel.Text = "Aucune mise à jour nécessaire."; SetStatus("Jeu à jour. Prêt à jouer."); }
        }
        catch (Exception ex)
        {
            Log($"UPDATE ERROR: {ex}"); progress.Value = 0; progressLabel.Text = "Consultez JOURNAUX pour le détail.";
            SetStatus(File.Exists(GamePath) ? "Mise à jour indisponible. Le jeu local reste utilisable." : "Installation impossible. Consultez JOURNAUX.");
        }
        finally
        {
            busy = false; SetBusy(false); UpdateUiState();
            if (restartGameAfterUpdate && File.Exists(GamePath))
            {
                Log("Fin de mise à jour : relance automatique du jeu demandée.");
                await Task.Delay(500);
                PlayGame();
            }
        }
    }

    private async Task<JsonDocument> GetManifestAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ManifestUrl}?nocache={Guid.NewGuid():N}"); request.Headers.UserAgent.ParseAdd("Infinite-Ascension-Launcher/12.0"); request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode(); return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private async Task InstallGameAsync(string url, string expectedSha, int build, string commit)
    {
        const string transactionVersion = "GAME_UPDATE_TRANSACTION_V2";
        if (IsGameRunning()) throw new InvalidOperationException("Le jeu est encore lancé.");

        string tempRoot = Path.Combine(Path.GetTempPath(), "InfiniteAscensionUpdate", Guid.NewGuid().ToString("N"));
        string archive = Path.Combine(tempRoot, "game.zip"); string unpack = Path.Combine(tempRoot, "game");
        string staging = Path.Combine(RootDir, "Game.next"); string rollback = Path.Combine(RootDir, "Game.rollback"); string stateBackup = StatePath + ".rollback";
        bool swapped = false, oldGameMoved = false, oldStateBackedUp = false; Directory.CreateDirectory(tempRoot); Directory.CreateDirectory(unpack); Log($"{transactionVersion} BEGIN build={build}");
        try
        {
            SetStatus("Téléchargement de la mise à jour…"); progress.Value = 0; progressLabel.Text = "Téléchargement : 0%";
            using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd("Infinite-Ascension-Launcher/13.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0, done = 0; using var sha = SHA256.Create();
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[1024 * 1024]; int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) { await target.WriteAsync(buffer.AsMemory(0, read)); sha.TransformBlock(buffer, 0, read, null, 0); done += read; if (total > 0) { int pct = (int)Math.Clamp(done * 100L / total, 0, 100); progress.Value = pct; progressLabel.Text = $"Téléchargement : {pct}%"; } }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0); await target.FlushAsync();
            }
            string actualSha = Convert.ToHexString(sha.Hash!).ToLowerInvariant(); string normalizedExpected = expectedSha.Trim().ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actualSha), Encoding.ASCII.GetBytes(normalizedExpected))) throw new InvalidOperationException($"SHA-256 invalide : attendu {normalizedExpected}, reçu {actualSha}.");
            Log($"UPDATE DOWNLOAD COMPLETE build={build} sha256={actualSha}");
            SetStatus("Extraction du jeu…"); progressLabel.Text = "Ouverture de l'archive…";
            IOException? lastArchiveError = null; bool extractedOk = false;
            for (int attempt = 1; attempt <= 12 && !extractedOk; attempt++)
            {
                try { ZipFile.ExtractToDirectory(archive, unpack, true); extractedOk = true; }
                catch (IOException ex) when (attempt < 12) { lastArchiveError = ex; Log($"Archive encore verrouillée, nouvelle tentative {attempt + 1}/12 : {ex.Message}"); await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt)); }
            }
            if (!extractedOk) throw new IOException("Impossible d'ouvrir l'archive après plusieurs tentatives.", lastArchiveError);
            string extracted = Directory.GetFiles(unpack, GameExe, SearchOption.AllDirectories).FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(extracted)) throw new InvalidOperationException("InfiniteAscension.exe absent du package.");
            if (new FileInfo(extracted).Length < 20L * 1024L * 1024L) throw new InvalidOperationException("InfiniteAscension.exe est anormalement petit.");
            SetStatus("Préparation de l'installation transactionnelle…"); if (Directory.Exists(staging)) Directory.Delete(staging, true); Directory.CreateDirectory(staging);
            foreach (string file in Directory.GetFiles(unpack, "*", SearchOption.AllDirectories)) { string relative = Path.GetRelativePath(unpack, file); string destination = Path.Combine(staging, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, true); }
            string stagedExe = Path.Combine(staging, GameExe); if (!File.Exists(stagedExe) || new FileInfo(stagedExe).Length < 20L * 1024L * 1024L) throw new InvalidOperationException("Installation staging invalide.");
            if (Directory.Exists(rollback)) Directory.Delete(rollback, true); if (File.Exists(StatePath)) { File.Copy(StatePath, stateBackup, true); oldStateBackedUp = true; }
            if (Directory.Exists(GameDir)) { Directory.Move(GameDir, rollback); oldGameMoved = true; }
            Directory.Move(staging, GameDir); swapped = true; if (!File.Exists(GamePath) || new FileInfo(GamePath).Length < 20L * 1024L * 1024L) throw new InvalidOperationException("Jeu installé invalide après permutation.");
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new { build, commit }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8); localBuild = build; progress.Value = 100; progressLabel.Text = $"Installation terminée — build #{build}."; Log($"{transactionVersion} SWAP_OK build={build}");
            if (Directory.Exists(rollback)) Directory.Delete(rollback, true); if (File.Exists(stateBackup)) File.Delete(stateBackup); oldGameMoved = false; oldStateBackedUp = false; Log($"{transactionVersion} SUCCESS build={build} commit={commit} sha256={actualSha}");
        }
        catch
        {
            Log($"{transactionVersion} ROLLBACK build={build}");
            try { if (swapped && Directory.Exists(GameDir)) Directory.Delete(GameDir, true); if (oldGameMoved && Directory.Exists(rollback)) Directory.Move(rollback, GameDir); if (oldStateBackedUp && File.Exists(stateBackup)) File.Copy(stateBackup, StatePath, true); else if (swapped && !oldStateBackedUp && File.Exists(StatePath)) File.Delete(StatePath); Log($"{transactionVersion} ROLLBACK_OK build={build}"); }
            catch (Exception rollbackError) { Log($"{transactionVersion} ROLLBACK_ERROR: {rollbackError}"); }
            throw;
        }
        finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (Exception ex) { Log($"Staging cleanup: {ex.Message}"); } try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch (Exception ex) { Log($"Temp cleanup: {ex.Message}"); } }
    }

    private bool IsGameRunning() { try { if (gameProcess is { HasExited: false }) return true; return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe)).Any(); } catch { return false; } }

    private void PlayGame()
    {
        if (busy || IsGameRunning()) return; if (!File.Exists(GamePath)) { SetStatus("Jeu non installé. Lancez une vérification des mises à jour."); return; }
        try
        {
            Directory.CreateDirectory(LogsDir); File.WriteAllText(GameLog, $"=== Infinite Ascension {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}", Encoding.UTF8);
            var psi = new ProcessStartInfo { FileName = GamePath, WorkingDirectory = GameDir, UseShellExecute = false, CreateNoWindow = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            gameProcess = new Process { StartInfo = psi, EnableRaisingEvents = true }; gameProcess.OutputDataReceived += (_, e) => { if (e.Data != null) LogGame("STDOUT", e.Data); }; gameProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) LogGame("STDERR", e.Data); }; gameProcess.Exited += (_, _) => { try { BeginInvoke(UpdateUiState); } catch { } };
            if (!gameProcess.Start()) throw new InvalidOperationException("Impossible de lancer le jeu."); gameProcess.BeginOutputReadLine(); gameProcess.BeginErrorReadLine(); SetStatus("Jeu lancé."); UpdateUiState(); Log($"Jeu lancé : {GamePath}");
        }
        catch (Exception ex) { Log($"PLAY ERROR: {ex}"); SetStatus("Impossible de lancer le jeu. Consultez JOURNAUX."); }
    }

    private void StopGame()
    {
        try
        {
            if (gameProcess is { HasExited: false }) { gameProcess.Kill(true); try { gameProcess.WaitForExit(5000); } catch { } gameProcess.Dispose(); gameProcess = null; Log("Jeu arrêté par le launcher."); }
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe))) { try { if (!process.HasExited) process.Kill(true); } catch { } finally { process.Dispose(); } }
            SetStatus("Jeu arrêté.");
        }
        catch (Exception ex) { Log($"STOP ERROR: {ex}"); }
        finally { UpdateUiState(); }
    }

    private void RefreshGameState() { if (gameProcess is { HasExited: true }) { Log($"Jeu terminé : code={gameProcess.ExitCode}"); gameProcess.Dispose(); gameProcess = null; UpdateUiState(); } }
    private void OpenLogs() { try { Directory.CreateDirectory(LogsDir); Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{LogsDir}\"", UseShellExecute = true }); } catch (Exception ex) { Log($"LOG OPEN ERROR: {ex.Message}"); } }
    private void OpenGameFolder() { try { Directory.CreateDirectory(GameDir); Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{GameDir}\"", UseShellExecute = true }); } catch (Exception ex) { Log($"FOLDER OPEN ERROR: {ex.Message}"); } }
    private static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { } }

    private void SelectPage(string page)
    {
        home.Visible = page == "home"; foreach (var pair in navButtons) pair.Value.BackColor = pair.Key == page ? Color.FromArgb(42, 22, 72) : Color.Transparent;
        foreach (Control c in content.Controls) if (c is Panel p && p.Name.StartsWith("Page_", StringComparison.Ordinal)) p.Visible = p.Name == "Page_" + page;
        if (page == "home") home.BringToFront(); else content.Controls["Page_" + page]?.BringToFront();
    }

    private void LayoutEverything()
    {
        foreach (Control c in content.Controls)
        {
            if (c is not Panel p || !p.Name.StartsWith("Page_", StringComparison.Ordinal)) continue;
            foreach (Control child in p.Controls) if (child is Panel card && card.Width == 0) card.Width = Math.Max(420, p.ClientSize.Width - 44);
        }
    }

    private void UpdateUiState()
    {
        bool running = IsGameRunning(); playButton.Enabled = !busy && File.Exists(GamePath) && !running; stopButton.Enabled = running; updateButton.Enabled = !busy; repairButton.Enabled = !busy; logsButton.Enabled = !busy; folderButton.Enabled = !busy; serverButton.Enabled = !busy;
        if (!busy && running) SetStatus("Jeu en cours d'exécution."); buildLabel.Text = remoteBuild > 0 ? $"Build locale : #{localBuild}   ·   Disponible : #{remoteBuild}" : $"Build locale : #{localBuild}   ·   Disponible : —";
    }
    private void SetBusy(bool value) { playButton.Enabled = !value && File.Exists(GamePath) && !IsGameRunning(); updateButton.Enabled = !value; repairButton.Enabled = !value; logsButton.Enabled = !value; folderButton.Enabled = !value; serverButton.Enabled = !value; stopButton.Enabled = !value && IsGameRunning(); Cursor = value ? Cursors.WaitCursor : Cursors.Default; }
    private void SetStatus(string text) => statusLabel.Text = text;
    private void SetServer(string text, bool online) { serverLabel.Text = text; serverLabel.ForeColor = online ? Color.FromArgb(91, 235, 111) : Color.FromArgb(244, 154, 90); }
    private int ReadLocalBuild() { try { if (!File.Exists(StatePath)) return 0; using var doc = JsonDocument.Parse(File.ReadAllText(StatePath)); return doc.RootElement.GetProperty("build").GetInt32(); } catch { return 0; } }
    private void LoadSettings() { try { if (!File.Exists(SettingsPath)) return; using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath)); autoUpdate = !doc.RootElement.TryGetProperty("autoUpdate", out var value) || value.GetBoolean(); } catch { autoUpdate = true; } }
    private void SaveSettings() { try { Directory.CreateDirectory(RootDir); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { autoUpdate, uploadLogs = true, confirmClose = true }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8); } catch (Exception ex) { Log($"SETTINGS SAVE ERROR: {ex.Message}"); } }
    private void Log(string message) { try { Directory.CreateDirectory(LogsDir); File.AppendAllText(LauncherLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8); } catch { } }
    private void LogGame(string stream, string message) { try { File.AppendAllText(GameLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stream}] {message}{Environment.NewLine}", Encoding.UTF8); } catch { } }
    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static Panel Card(int x, int y, int width, int height) { var p = new Panel { Location = new Point(x, y), Size = new Size(width, height), BackColor = Color.FromArgb(10, 13, 30) }; p.Paint += (_, e) => { using var border = new Pen(Color.FromArgb(50, 53, 82)); using var accent = new Pen(Color.FromArgb(103, 61, 220), 2); e.Graphics.DrawRectangle(border, 0, 0, p.Width - 1, p.Height - 1); e.Graphics.DrawLine(accent, 0, 0, Math.Min(80, p.Width), 0); }; return p; }
    private static void AddCardTitle(Panel card, string text, int top) => card.Controls.Add(MakeLabel(text, 18, top, Math.Max(250, card.Width - 36), 25, 11, Color.FromArgb(191, 143, 255), true));
    private static void AddInfoRow(Panel card, string key, string value, int top) { card.Controls.Add(MakeLabel(key, 18, top, 120, 22, 9, Color.FromArgb(130, 135, 163), false)); var v = MakeLabel(value, 145, top, 180, 22, 9, Color.White, true); v.AutoEllipsis = true; card.Controls.Add(v); }
    private static Label MakeLabel(string text, int x, int y, int width, int height, float size, Color color, bool bold) => new() { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, AutoSize = false };
    private static Button ChromeButton(string text, int width, int height, float fontSize) => new() { Text = text, Size = new Size(width, height), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = Color.Transparent, ForeColor = Color.White, Font = new Font("Segoe UI", fontSize), TabStop = false };
    private static void ConfigureAction(Button b, string text, Color back, Point location, Size size) { b.Text = text; b.Location = location; b.Size = size; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.BackColor = back; b.ForeColor = Color.White; b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold); b.Cursor = Cursors.Hand; b.Anchor = AnchorStyles.Top | AnchorStyles.Left; }
}
