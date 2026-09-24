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

internal sealed partial class LauncherForm : Form
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


}