# Infinite Ascension Launcher

Standalone Windows launcher for Infinite Ascension.

## Architecture

- Launcher source and CI/CD live in this repository.
- Infinite Ascension remains the game/server repository.
- Distribution is served through the Infinite Ascension updater Worker.
- Game updates are verified with SHA-256 before installation.
- Launcher self-update uses a separate transactional path and rollback files.

## Migration

The launcher is being extracted from `Mataiasu-Projects/Infinite-Ascension` so it can have an independent release lifecycle.

### Migration rules

1. Preserve the current functional launcher behavior during extraction.
2. Make the standalone repository the canonical source of launcher code.
3. Keep game code and launcher code separated.
4. Add CI binary-content checks so the published executable is built from the intended source.
5. Add release signing after the extraction is stable.

## Current source of truth during migration

Until the extraction is completed, the launcher implementation originates from:
`launcher/windows/` in `Mataiasu-Projects/Infinite-Ascension`.

Do not delete the original launcher from Infinite Ascension until a standalone build has passed CI and has been verified from the produced artifact.
