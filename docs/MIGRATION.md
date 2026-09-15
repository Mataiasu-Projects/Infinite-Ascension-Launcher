# Launcher extraction migration

## Status

**Phase 1 — repository bootstrap**

The standalone repository now exists and contains the migration contract and a reproducible source extraction script.

## Source

Current launcher implementation:

`Mataiasu-Projects/Infinite-Ascension/launcher/windows/`

The source contains the newer `Program.cs` updater implementation as well as legacy launcher files. The previous build pipeline compiled `LauncherV3.cs` explicitly and therefore published the legacy updater instead of `Program.cs`.

## Target

The standalone project is intentionally configured to use:

- `Program.cs` as the executable entry point
- `LauncherSelfUpdater.cs` as the module-initialized self-update component
- explicit compile items, avoiding accidental source selection

## Migration sequence

1. Extract the current launcher source.
2. Normalize the standalone project file.
3. Build Windows self-contained single-file launcher.
4. Assert that the binary contains the intended updater implementation and does not contain the legacy entry point.
5. Verify launcher self-update and game-update transactions.
6. Move launcher CI/CD and release publication to this repository.
7. Update the Infinite Ascension repository to consume launcher releases instead of building launcher code.
8. Remove the old launcher source only after standalone releases are verified.
9. Add Ed25519 release signing and anti-rollback.

## Safety rule

Do not delete `launcher/windows/` from Infinite Ascension until a standalone release has passed CI and has been tested from the actual published artifact.
