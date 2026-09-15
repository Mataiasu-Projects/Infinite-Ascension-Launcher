using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InfiniteAscensionLauncher;

internal static class LauncherSelfUpdater
{
    private const string ManifestUrl = "https://infinite-ascension-updater.matthprizee55.workers.dev/update/manifest";
    private const string LauncherExe = "Infinite-Ascension-Launcher.exe";
    private const string BuildFile = "launcher_build.json";
    private const string BackupExe = "Infinite-Ascension-Launcher.rollback.exe";
    private const string BackupBuildFile = "launcher_build.rollback.json";
    private const long MinimumLauncherBytes = 10L * 1024L * 1024L;
    private const long MaximumArchiveBytes = 512L * 1024L * 1024L;
    private const int StartupValidationSeconds = 8;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("INFINITE_ASCENSION_SKIP_SELF_UPDATE") == "1") return;
            if (Environment.GetEnvironmentVariable("INFINITE_ASCENSION_UPDATING") == "1") return;
            SelfUpdate();
        }
        catch (Exception ex) { TryLog(ex.ToString()); }
    }

    private static void SelfUpdate()
    {
        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return;
        string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int localBuild = ReadLocalBuild(Path.Combine(installDir, BuildFile));
        TryLog($"Self-update check: localBuild={localBuild}, exe={exe}");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var manifest = DownloadManifest(http, ManifestUrl + "?nocache=" + Guid.NewGuid().ToString("N"));
        TryLog("Manifest source: updater worker");

        var root = manifest.RootElement;
        if (!root.TryGetProperty("build", out var buildElement) || !buildElement.TryGetInt32(out int remoteBuild))
            throw new InvalidOperationException("Launcher manifest has no valid build number.");
        string remoteCommit = root.TryGetProperty("commit", out var commitElement) ? commitElement.GetString() ?? "" : "";
        if (remoteBuild <= localBuild) { TryLog($"No launcher update required: remoteBuild={remoteBuild}"); return; }

        string url = "";
        string expectedSha = "";
        if (root.TryGetProperty("assets", out var assets) && assets.TryGetProperty("launcher_windows", out var asset))
        {
            expectedSha = asset.TryGetProperty("sha256", out var shaElement) ? shaElement.GetString() ?? "" : "";
            if (asset.TryGetProperty("url", out var urlElement))
            {
                string candidate = urlElement.GetString() ?? "";
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri) && candidateUri.Scheme == Uri.UriSchemeHttps)
                    url = candidate;
            }
        }
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Launcher manifest has no HTTPS update URL.");
        if (!IsSha256(expectedSha)) throw new InvalidOperationException("Launcher manifest has no valid SHA-256.");
        TryLog($"Launcher update available: local={localBuild}, remote={remoteBuild}");
        ApplyUpdate(http, exe, installDir, remoteBuild, remoteCommit, url, expectedSha);
    }

    private static JsonDocument DownloadManifest(HttpClient http, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Infinite-Ascension-Launcher-SelfUpdater/5.2");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(response.Content.ReadAsStream());
    }

    private static void ApplyUpdate(HttpClient http, string exe, string installDir, int remoteBuild, string remoteCommit, string updateUrl, string expectedSha)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "InfiniteAscensionLauncherUpdate", Guid.NewGuid().ToString("N"));
        string archive = Path.Combine(tempRoot, "launcher.zip");
        string unpack = Path.Combine(tempRoot, "unpack");
        Directory.CreateDirectory(unpack);
        try
        {
            using (var downloadResponse = http.GetAsync(updateUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                downloadResponse.EnsureSuccessStatusCode();
                long? declaredLength = downloadResponse.Content.Headers.ContentLength;
                if (declaredLength is > MaximumArchiveBytes) throw new InvalidOperationException("Launcher update archive is too large.");
                using var source = downloadResponse.Content.ReadAsStream();
                using var target = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                CopyBounded(source, target, MaximumArchiveBytes);
            }
            var archiveInfo = new FileInfo(archive);
            if (archiveInfo.Length < 5L * 1024L * 1024L) throw new InvalidOperationException("Launcher update archive is unexpectedly small.");
            using (var shaStream = File.OpenRead(archive))
            {
                string actualSha = Convert.ToHexString(SHA256.HashData(shaStream)).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualSha), Convert.FromHexString(expectedSha.ToLowerInvariant())))
                    throw new InvalidOperationException("Launcher update SHA-256 mismatch.");
            }
            ExtractSafe(archive, unpack);
            string newExe = Directory.GetFiles(unpack, LauncherExe, SearchOption.AllDirectories).FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(newExe) || !File.Exists(newExe)) throw new InvalidOperationException("Updated launcher executable is missing.");
            if (new FileInfo(newExe).Length < MinimumLauncherBytes) throw new InvalidOperationException("Updated launcher executable is unexpectedly small.");
            using (var pe = new FileStream(newExe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (pe.ReadByte() != 0x4D || pe.ReadByte() != 0x5A) throw new InvalidOperationException("Updated launcher is not a Windows PE executable.");
            }

            string script = Path.Combine(tempRoot, "apply-update.ps1");
            string escapedInstall = installDir.Replace("'", "''");
            string escapedExe = exe.Replace("'", "''");
            string escapedNewExe = newExe.Replace("'", "''");
            string escapedTemp = tempRoot.Replace("'", "''");
            string escapedCommit = remoteCommit.Replace("'", "''");
            string escapedBuildFile = BuildFile.Replace("'", "''");
            string escapedBackupExe = BackupExe.Replace("'", "''");
            string escapedBackupBuild = BackupBuildFile.Replace("'", "''");
            string scriptText = $@"
$ErrorActionPreference='Stop'
$launcherPid={Environment.ProcessId}
while(Get-Process -Id $launcherPid -ErrorAction SilentlyContinue){{Start-Sleep -Milliseconds 250}}
Start-Sleep -Milliseconds 500
$install='{escapedInstall}'
$exe='{escapedExe}'
$newExe='{escapedNewExe}'
$backup=Join-Path $install '{escapedBackupExe}'
$buildFile=Join-Path $install '{escapedBuildFile}'
$backupBuild=Join-Path $install '{escapedBackupBuild}'
$temp='{escapedTemp}'
try {{
  if(Test-Path $backup){{Remove-Item -LiteralPath $backup -Force}}
  if(Test-Path $backupBuild){{Remove-Item -LiteralPath $backupBuild -Force}}
  Copy-Item -LiteralPath $exe -Destination $backup -Force
  if(Test-Path $buildFile){{Copy-Item -LiteralPath $buildFile -Destination $backupBuild -Force}}
  Copy-Item -LiteralPath $newExe -Destination $exe -Force
  Set-Content -LiteralPath $buildFile -Value '{{""build"":{remoteBuild},""commit"":""{escapedCommit}"",""platform"":""windows""}}' -Encoding UTF8
  $env:INFINITE_ASCENSION_UPDATING='1'
  $p=Start-Process -FilePath $exe -WorkingDirectory $install -PassThru
  $deadline=(Get-Date).AddSeconds({StartupValidationSeconds})
  while((Get-Date) -lt $deadline){{
    Start-Sleep -Milliseconds 250
    if($p.HasExited){{ throw ""New launcher exited during startup validation: $($p.ExitCode)"" }}
  }}
  Remove-Item -LiteralPath $backup -Force
  if(Test-Path $backupBuild){{Remove-Item -LiteralPath $backupBuild -Force}}
  Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}} catch {{
  try {{
    if(Test-Path $backup){{Copy-Item -LiteralPath $backup -Destination $exe -Force}}
    if(Test-Path $backupBuild){{Copy-Item -LiteralPath $backupBuild -Destination $buildFile -Force}}
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $backupBuild -Force -ErrorAction SilentlyContinue
  }} catch {{}}
  $env:INFINITE_ASCENSION_SKIP_SELF_UPDATE='1'
  Start-Process -FilePath $exe -WorkingDirectory $install
  Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
  exit 20
}}
";
            File.WriteAllText(script, scriptText, Encoding.UTF8);
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = installDir });
            TryLog($"Self-update transaction handed off: targetBuild={remoteBuild}");
            Environment.Exit(0);
        }
        catch
        {
            try { Directory.Delete(tempRoot, true); } catch { }
            throw;
        }
    }

    private static void ExtractSafe(string archive, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(destination, relative));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Launcher update archive contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(full); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, true);
        }
    }

    private static void CopyBounded(Stream source, Stream target, long maximumBytes)
    {
        byte[] buffer = new byte[1024 * 1024];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidOperationException("Launcher update archive exceeds the size limit.");
            target.Write(buffer, 0, read);
        }
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static int ReadLocalBuild(string path)
    {
        try { if (!File.Exists(path)) return 0; using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.GetProperty("build").GetInt32(); }
        catch { return 0; }
    }

    private static void TryLog(string message)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InfiniteAscension", "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "launcher-self-update.log"), $"[{DateTime.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }
}
