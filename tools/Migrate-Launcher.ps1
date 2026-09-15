[CmdletBinding()]
param(
    [string]$SourceRepo = "https://github.com/Mataiasu-Projects/Infinite-Ascension.git",
    [string]$SourcePath = "launcher/windows",
    [string]$DestinationPath = "src"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("InfiniteAscensionLauncherMigration-" + [guid]::NewGuid().ToString("N"))

try {
    git clone --filter=blob:none --no-checkout $SourceRepo $temp
    git -C $temp sparse-checkout init --no-cone
    git -C $temp sparse-checkout set $SourcePath
    git -C $temp checkout main

    $source = Join-Path $temp $SourcePath
    $destination = Join-Path $root $DestinationPath
    New-Item -ItemType Directory -Force -Path $destination | Out-Null

    Get-ChildItem -LiteralPath $source -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($source.Length).TrimStart('\','/')
        if ($relative -eq "InfiniteAscensionLauncher.csproj") { return }
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }

    Write-Host "Launcher source migrated to $destination"
    Write-Host "Review the resulting tree before deleting launcher/windows from Infinite Ascension."
}
finally {
    if (Test-Path $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
}
