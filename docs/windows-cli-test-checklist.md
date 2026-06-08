# Windows Maple Client CLI Test Checklist

This checklist is for validating a Windows `wcr2.exe` build against a real MapleStory client folder.
It is written so another agent can run it step by step and report exact evidence.

## Prepared Artifact

From the current macOS workspace, a Windows x64 self-contained CLI publish was created at:

```text
artifacts/wcr2-win-x64-self-contained/
artifacts/wcr2-win-x64-self-contained.zip
```

The published executable is:

```text
artifacts/wcr2-win-x64-self-contained/wcr2.exe
```

Local verification performed on macOS:

- `dotnet publish WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:UseAppHost=true`
- `file artifacts/wcr2-win-x64-self-contained/wcr2.exe` reports `PE32+ executable (console) x86-64, for MS Windows`
- The artifact is self-contained, so a target Windows machine should not need a separate .NET runtime for normal CLI use.

Important limitation: this macOS environment cannot execute `wcr2.exe`, so the final executable smoke test must happen on Windows.

## Scope

Validate:

- The published `wcr2.exe` starts on Windows.
- The CLI can read a real MapleStory client WZ folder.
- Core commands work with actual client files: `info`, `tree`, `list`, `search`, `dump`, `extract`, `compare`.
- Domain commands work where the IDs exist: `skill`, `item`, `gear`, `map`.
- Metadata-only commands still work: `avatar`, `config`, `plugin`.

Do not validate as completed unless command output files exist and exit codes are recorded.

## Safety Rules

- Treat the MapleStory install folder as read-only.
- Do not run `patch apply` against the live client folder.
- If patch testing is needed, copy the target client folder to a temporary directory first.
- Put all CLI output under a separate output folder, for example `C:\Wcr2CliTest\out`.
- If Windows Defender blocks a downloaded zip/exe, unblock the zip in file properties or run:

```powershell
Unblock-File C:\Wcr2CliTest\wcr2-win-x64-self-contained.zip
```

## Prerequisites

On Windows:

- Windows 10/11 x64.
- PowerShell 5+.
- A MapleStory client folder that contains either classic root WZ files or modern split data folders under `Data\`.
- Optional: a second MapleStory client folder for real old/new compare testing.

Common Maple install locations to check:

```powershell
Test-Path "C:\Nexon\Maple"
Test-Path "C:\Program Files (x86)\Wizet\MapleStory"
Get-ChildItem C:\ -Directory -Recurse -ErrorAction SilentlyContinue -Filter MapleStory | Select-Object -First 10 FullName
```

## Setup

Extract the artifact:

```powershell
$Root = "C:\Wcr2CliTest"
$CliDir = "$Root\wcr2"
$Out = "$Root\out"
New-Item -ItemType Directory -Force $Root, $CliDir, $Out | Out-Null

Expand-Archive "$Root\wcr2-win-x64-self-contained.zip" -DestinationPath $Root -Force
$Wcr2 = "$Root\wcr2-win-x64-self-contained\wcr2.exe"
```

Set the Maple client path:

```powershell
$Maple = "C:\Nexon\Maple"
$Data = Join-Path $Maple "Data"
$Base = Join-Path $Maple "Base.wz"

Get-ChildItem $Maple -Filter *.wz | Select-Object Name, Length | Format-Table
Get-ChildItem $Data -Directory -ErrorAction SilentlyContinue | Select-Object Name | Format-Table
```

Stop if `Base.wz` is missing.

## Input Layout Detection

Modern Maple clients can have tiny root/link WZ files at paths like `String.wz`, while the real data lives in `Data\String`, `Data\Skill`, `Data\Item`, `Data\Character`, `Data\Mob_Canvas`, and sharded map files such as `Data\Map\Map\Map1\Map1_000.wz`.

If a command fails with `WZ path not found` or `<kind> id not found` against a root WZ file, retry with the data-bearing directory or shard.

Use this detection block:

```powershell
function FirstExisting($name, [string[]] $candidates) {
  foreach ($candidate in $candidates) {
    if (Test-Path $candidate) {
      Write-Host "$name = $candidate"
      return $candidate
    }
  }
  throw "No usable $name input found. Tried: $($candidates -join ', ')"
}

$String = FirstExisting "String" @("$Data\String", "$Maple\String.wz")
$Skill = FirstExisting "Skill" @("$Data\Skill", "$Maple\Skill.wz")
$Item = FirstExisting "Item" @("$Data\Item", "$Maple\Item.wz")
$Gear = FirstExisting "Gear" @("$Data\Character\Cap", "$Data\Character", "$Maple\Character.wz")
$Map = FirstExisting "Map" @("$Data\Map\Map\Map1\Map1_000.wz", "$Data\Map", "$Maple\Map.wz")
$Mob = FirstExisting "Mob animation" @("$Data\Mob_Canvas", "$Data\Mob", "$Maple\Mob.wz")
```

Validated split-layout examples from Windows testing:

- `Data\String` with `CashItemSearch.img`
- `Data\Skill` with skill id `3001004`
- `Data\Item` with item id `2000000`
- `Data\Character\Cap` with gear id `1002140`
- `Data\Map\Map\Map1\Map1_000.wz` with map id `100000000`
- `Data\Mob_Canvas` with animation path `0100100.img\stand`

## Required Smoke Commands

Run each command and record `$LASTEXITCODE`.

### 1. Executable Starts

```powershell
& $Wcr2 --help
if ($LASTEXITCODE -ne 0) { throw "wcr2 --help failed" }

& $Wcr2 version
if ($LASTEXITCODE -ne 0) { throw "wcr2 version failed" }
```

Expected: help text and `wcr2 cli 0.1.0`.

### 2. Basic WZ Loading

```powershell
& $Wcr2 info $Base --json | Tee-Object "$Out\info-base.json"
if ($LASTEXITCODE -ne 0) { throw "info Base.wz failed" }

& $Wcr2 tree $Base --depth 2 --limit 200 --json | Tee-Object "$Out\tree-base.json"
if ($LASTEXITCODE -ne 0) { throw "tree Base.wz failed" }

& $Wcr2 list $Base --json | Tee-Object "$Out\list-base.json"
if ($LASTEXITCODE -ne 0) { throw "list Base.wz failed" }
```

Expected files:

- `$Out\info-base.json`
- `$Out\tree-base.json`
- `$Out\list-base.json`

### 3. Search

```powershell
& $Wcr2 search $Base --match-path "*" --max-results 20 --json | Tee-Object "$Out\search-base-path.json"
if ($LASTEXITCODE -ne 0) { throw "search Base.wz by path failed" }

& $Wcr2 search $String --name "Skill" --max-results 20 --json | Tee-Object "$Out\search-string-skill.json"
if ($LASTEXITCODE -ne 0) { throw "search String.wz by name failed" }
```

If the second command returns no useful rows but exit code is `0`, keep the output and try another known Korean/English node name from `tree $String --depth 2`.

### 4. Dump and Extract

First inspect `String.wz`:

```powershell
& $Wcr2 tree $String --depth 2 --limit 100 --json | Tee-Object "$Out\tree-string.json"
if ($LASTEXITCODE -ne 0) { throw "tree String.wz failed" }
```

Then choose an image path that exists.
For modern split `Data\String`, `CashItemSearch.img` is a validated candidate.

```powershell
$StringNode = "CashItemSearch.img"
& $Wcr2 dump $String --path $StringNode --format json --out "$Out\dump-string-skill.json"
if ($LASTEXITCODE -ne 0) { throw "dump String.wz failed; check `$StringNode" }

& $Wcr2 extract $String --path $StringNode --out "$Out\extract-string-skill" --recursive --manifest "$Out\extract-string-skill\manifest.json" --json | Tee-Object "$Out\extract-string-skill.json"
if ($LASTEXITCODE -ne 0) { throw "extract String.wz failed; check `$StringNode" }
```

Expected:

- `$Out\dump-string-skill.json`
- `$Out\extract-string-skill\manifest.json`
- one or more extracted files under `$Out\extract-string-skill`

### 5. Compare

Same-file compare should succeed and produce zero or low-level metadata-only differences:

```powershell
& $Wcr2 compare $Base $Base --json --out "$Out\compare-base-self.json"
if ($LASTEXITCODE -ne 0) { throw "same-file compare failed" }

& $Wcr2 compare $Base $Base --format markdown --out "$Out\compare-base-self.md"
if ($LASTEXITCODE -ne 0) { throw "same-file markdown compare failed" }
```

If a second client folder is available:

```powershell
$OldMaple = "D:\MapleOld"
$NewMaple = "D:\MapleNew"
& $Wcr2 compare "$OldMaple\Base.wz" "$NewMaple\Base.wz" --json --out "$Out\compare-base-old-new.json"
if ($LASTEXITCODE -ne 0) { throw "old/new Base.wz compare failed" }
```

### 6. Domain Metadata

These IDs are smoke-test examples for a modern split client.
If an ID is missing in the tested client version, record the failure and retry with an ID found in that WZ.

```powershell
& $Wcr2 skill info $Skill --id 3001004 --string-wz $String --json | Tee-Object "$Out\skill-3001004.json"
if ($LASTEXITCODE -ne 0) { Write-Warning "skill 3001004 failed; try another known skill id" }

& $Wcr2 item info $Item --id 2000000 --string-wz $String --json | Tee-Object "$Out\item-2000000.json"
if ($LASTEXITCODE -ne 0) { Write-Warning "item 2000000 failed; try another known item id" }

& $Wcr2 gear info $Gear --id 1002140 --string-wz $String --json | Tee-Object "$Out\gear-1002140.json"
if ($LASTEXITCODE -ne 0) { Write-Warning "gear 1002140 failed; try another known gear id" }

& $Wcr2 map info $Map --id 100000000 --string-wz $String --json | Tee-Object "$Out\map-100000000.json"
if ($LASTEXITCODE -ne 0) { Write-Warning "map 100000000 failed; try another known map id" }
```

### 7. Map Sections

```powershell
$MapId = "100000000"
& $Wcr2 map portals $Map --id $MapId --json | Tee-Object "$Out\map-$MapId-portals.json"
& $Wcr2 map life $Map --id $MapId --json | Tee-Object "$Out\map-$MapId-life.json"
& $Wcr2 map objects $Map --id $MapId --json | Tee-Object "$Out\map-$MapId-objects.json"
& $Wcr2 map reactors $Map --id $MapId --json | Tee-Object "$Out\map-$MapId-reactors.json"
```

Each command should exit `0` if the map exists.
Empty sections are acceptable for maps that do not contain that section.

### 8. Animation Frames

Find a candidate animation path first:

```powershell
& $Wcr2 tree $Mob --depth 3 --limit 200 --json | Tee-Object "$Out\tree-mob.json"
```

Try a known mob/action path from the tree output:

```powershell
$AnimPath = "0100100.img\stand"
& $Wcr2 animate frames $Mob --path $AnimPath --out "$Out\anim-0100100-stand" --json | Tee-Object "$Out\anim-0100100-stand.json"
if ($LASTEXITCODE -ne 0) { Write-Warning "animation path failed; choose another path from tree-mob.json" }
```

Expected when successful:

- `$Out\anim-0100100-stand\frames.json`
- frame subfolders/files under `$Out\anim-0100100-stand`

### 9. Config and Avatar

```powershell
$Config = "$Out\wcr2.config.json"
& $Wcr2 config set default-wz $Base --config $Config --json | Tee-Object "$Out\config-set.json"
if ($LASTEXITCODE -ne 0) { throw "config set failed" }

& $Wcr2 info --config $Config --json | Tee-Object "$Out\info-config-default.json"
if ($LASTEXITCODE -ne 0) { throw "config default-wz fallback failed" }

& $Wcr2 avatar inspect --code "1002140,1040036,1060026" --json | Tee-Object "$Out\avatar-inspect.json"
if ($LASTEXITCODE -ne 0) { throw "avatar inspect failed" }
```

### 10. Plugin Smoke

This validates default plugin discovery does not crash even when no plugins are installed:

```powershell
& $Wcr2 plugin list --json | Tee-Object "$Out\plugin-list.json"
if ($LASTEXITCODE -ne 0) { throw "plugin list failed" }
```

## Optional Source Test Harness

If the Windows agent has the source checkout and .NET SDK installed, also run:

```powershell
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Release
dotnet build WzComparerR2.Cli.Tests/WzComparerR2.Cli.Tests.csproj -c Release
dotnet run --project WzComparerR2.Cli.Tests/WzComparerR2.Cli.Tests.csproj -c Release --no-build -- --cli WzComparerR2.Cli\bin\Release\net8.0\wcr2.dll
```

Expected:

```text
Total: 13 Passed: 13 Failed: 0
```

## Report Template

After running the checklist, report:

```text
Windows version:
Maple client path:
wcr2.exe path:
wcr2 version output:

Required smoke:
- help/version:
- info/tree/list:
- search:
- dump/extract:
- compare:
- skill/item/gear/map:
- map sections:
- animate frames:
- config/avatar:
- plugin:

Output folder:
Failed commands with full stderr:
Notes about missing WZ files or IDs:
```

## Known Gaps

- This CLI does not yet render avatar PNGs.
- This CLI does not yet render full map screenshots.
- `network chat/send` is still dry-run/TCP-probe level and does not replace GUI chat.
- Actual patch application must be tested only against a copied client folder.
