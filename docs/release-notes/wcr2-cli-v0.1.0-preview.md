# WzComparerR2 CLI v0.1.0 Preview

This is the first preview release of the experimental WzComparerR2 command line tool.
It is intended for Windows users who want to test WZ inspection, extraction, comparison, and automation workflows without opening the WinForms GUI.

## Download

Use:

```text
WzComparerR2.Cli-win-x64-self-contained.zip
```

Unzip it and run:

```powershell
.\wcr2.exe --help
.\wcr2.exe info C:\Path\To\Maple\Base.wz
```

This package is Windows x64 self-contained, so a separate .NET runtime should not be required.

## Highlights

- Added `wcr2` CLI for WZ/MS inspection and automation.
- Added core commands:
  - `info`
  - `tree`
  - `list`
  - `search`
  - `dump`
  - `extract`
  - `compare`
- Added patch commands:
  - `patch inspect`
  - `patch dry-run`
  - `patch apply`
- Added metadata commands:
  - `skill info`
  - `item info`
  - `gear info`
  - `map info`
  - `map portals`
  - `map life`
  - `map objects`
  - `map reactors`
- Added automation/helper commands:
  - `animate frames`
  - `avatar inspect`
  - `avatar unpack`
  - `lua run`
  - `network server-info`
  - `update check/download/apply`
  - `config`
  - `plugin list/inspect/commands/run`
- Added JSON output support for script-friendly workflows.
- Added CLI config file support, including `default-wz` fallback input.
- Added Windows Maple client smoke test checklist and PowerShell smoke script.
- Added CLI process-based test harness with 13 smoke/error tests.

## Example Commands

```powershell
.\wcr2.exe info C:\Nexon\Maple\Base.wz --json
.\wcr2.exe tree C:\Nexon\Maple\Base.wz --depth 2 --json
.\wcr2.exe search C:\Nexon\Maple\Data\String --name Skill --json
.\wcr2.exe extract C:\Nexon\Maple\Data\String --path CashItemSearch.img --out out\string --recursive --manifest out\string\manifest.json --json
.\wcr2.exe compare old\Base.wz new\Base.wz --format markdown --out out\compare.md
.\wcr2.exe skill info C:\Nexon\Maple\Data\Skill --id 3001004 --string-wz C:\Nexon\Maple\Data\String --json
.\wcr2.exe map portals C:\Nexon\Maple\Data\Map\Map\Map1\Map1_000.wz --id 100000000 --json
.\wcr2.exe avatar inspect --code "1002140,1040036,1060026" --json
```

Modern Maple clients may store real data in split `Data\...` folders and shards instead of the root `.wz` files.
If a root file returns `WZ path not found` or `id not found`, retry with the matching `Data` directory or shard.

## Windows Real Client Testing

For full real-client validation, follow:

```text
docs/windows-cli-test-checklist.md
```

Or run the smoke script from a source checkout:

```powershell
.\samples\cli\windows-maple-smoke.ps1 `
  -Wcr2 "C:\Wcr2CliTest\wcr2-win-x64-self-contained\wcr2.exe" `
  -Maple "C:\Nexon\Maple" `
  -Out "C:\Wcr2CliTest\out"
```

## Known Limitations

- This is a preview CLI release.
- The current package was cross-published for Windows x64; final execution validation must be done on Windows.
- Windows smoke testing confirmed the CLI works against a split Maple client layout when pointed at data-bearing inputs such as `Data\String`, `Data\Skill`, `Data\Character\Cap`, `Data\Map\Map\Map1\Map1_000.wz`, and `Data\Mob_Canvas`.
- Avatar PNG rendering is not implemented yet.
- Full map screenshot rendering is not implemented yet.
- `network chat` and `network send` do not replace the GUI network plugin yet.
- WZ happy-path golden tests require real `.wz` fixtures and are still pending.
- Patch application should only be tested against a copied client folder, not a live MapleStory install directory.

## Artifact Integrity

SHA-256:

```text
f19329f66a5941b85de62cf9e7b23825533056d4263364ad81e28a95561f2257
```

## Build Notes

Built from the CLI project using:

```bash
dotnet publish WzComparerR2.Cli/WzComparerR2.Cli.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false \
  -p:UseAppHost=true
```
