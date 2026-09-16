# Infinite Ascension Launcher

Standalone Windows launcher for **Infinite Ascension**.

## What it does

- Installs and updates the Windows game client.
- Verifies game packages with SHA-256 before installation.
- Supports transactional game updates with rollback protection.
- Automatically checks for launcher updates.
- Provides PLAY, UPDATE, REPAIR and game-folder actions.
- Keeps launcher updates separate from game updates.

## Distribution

The launcher receives its update manifest from the Infinite Ascension distribution service and downloads verified release packages from the configured distribution endpoint.

## Build

Requirements:

- Windows
- .NET 8 SDK

Build locally with:

```powershell
dotnet publish src/InfiniteAscensionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/launcher-windows
```

GitHub Actions builds the release artifact, performs binary validation and a startup smoke test, generates SHA-256 checksums, and publishes a versioned release.

## Security

The launcher does not contain repository access credentials. Update packages are accepted only after HTTPS and SHA-256 validation.

Release signing is planned as a future hardening step.

## License

See `LICENSE` for the project license.
