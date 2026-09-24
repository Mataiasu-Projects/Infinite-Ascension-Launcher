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
}
