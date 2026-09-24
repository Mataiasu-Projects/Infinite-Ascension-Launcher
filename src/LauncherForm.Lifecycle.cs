using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace InfiniteAscensionLauncher;

internal sealed partial class LauncherForm
{
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
}
