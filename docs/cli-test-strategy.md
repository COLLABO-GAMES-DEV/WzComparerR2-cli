# WzComparerR2 CLI Test Strategy

This document defines how CLI output should be verified without committing private MapleStory client data.

## Test Layers

1. Fixture-free contract tests
   - Run from `WzComparerR2.Cli.Tests`.
   - Cover help/version, usage errors, config, profile fallback, avatar dry-run, map dry-run, Lua dry-run/eval, network dry-run, update validation, and plugin loading.
   - These tests must run on macOS, Linux, and Windows.

2. Local real-client smoke tests
   - Run against a locally installed MapleStory client.
   - Do not commit WZ/MS files or extracted copyrighted assets.
   - Store generated evidence under `.test/` or an ignored external folder.
   - Prefer split-layout inputs such as `Data/String`, `Data/Skill`, `Data/Item`, `Data/Map/Map/Map1/Map1_000.wz`, and `Data/Mob_Canvas`.

3. Golden JSON tests
   - Use only sanitized JSON outputs.
   - Normalize machine-specific fields before comparison:
     - absolute paths
     - elapsed time
     - temporary output directories
     - file ordering when the source format does not guarantee order
   - Compare stable semantic fields first, then full JSON when the command contract is mature.

4. Binary export checks
   - Do not store exported Maple assets in git.
   - Verify metadata instead:
     - file exists
     - byte length is greater than zero
     - image dimensions
     - frame count
     - manifest entry count
     - SHA-256 only for private/local evidence, not committed fixtures

## Minimum Real-Client Fixture Matrix

Use a private local MapleStory install or copied fixture folder with:

- `Data/String`
- `Data/Skill`
- `Data/Item`
- `Data/Character/Cap`
- `Data/Map/Map/Map1/Map1_000.wz`
- `Data/Mob_Canvas`
- one patch file and copied target folder for `patch dry-run/apply`

## Golden Candidates

When real-client fixture access is available, add golden checks in this order:

1. `tree Data/String --depth 1 --json`
2. `search Data/String --name Skill --json`
3. `dump Data/String --path CashItemSearch.img --format json`
4. `skill full Data/Skill --id <known-skill> --string-wz Data/String --format json`
5. `map portals Data/Map/Map/Map1/Map1_000.wz --id 100000000 --json`
6. `compare <old-fixture> <new-fixture> --json`

## Acceptance Rules

- A command is considered automation-ready when it has:
  - one fixture-free contract test, or
  - one documented real-client smoke command, and
  - an exit-code assertion.
- A WZ export path is considered verified only when output metadata is checked, not merely when the command exits zero.
- Rendering commands with `CanRender = false` are dry-run contracts, not render verification.
- Windows-only graphics or native dependency checks must be labeled Windows-only in the test name or script.
