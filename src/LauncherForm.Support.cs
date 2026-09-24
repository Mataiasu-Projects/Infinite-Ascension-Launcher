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
        private int ReadLocalBuild()
        {
            try
            {
                string path = Path.Combine(GameRoot, "build.json"); if (!File.Exists(path)) return 0;
                using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.TryGetProperty("build", out var b) && b.TryGetInt32(out int value) ? value : 0;
            }
            catch (Exception ex) { Log("Read local build failed: " + ex.Message); return 0; }
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
