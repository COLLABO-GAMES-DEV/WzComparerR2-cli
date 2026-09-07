# WzComparerR2 CLI

`WzComparerR2.Cli`는 `WzComparerR2.WzLib`를 재사용하는 콘솔 도구입니다.
실행 파일 이름은 `wcr2`입니다.

에이전트 전용 자동화는 `WzComparerR2.AgentHost`가 담당합니다.
실행 파일 이름은 `wcr2-agent`입니다.

## Build

```bash
dotnet restore WzComparerR2.Cli/WzComparerR2.Cli.csproj --ignore-failed-sources
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug --no-restore
dotnet restore WzComparerR2.AgentHost/WzComparerR2.AgentHost.csproj --ignore-failed-sources
dotnet build WzComparerR2.AgentHost/WzComparerR2.AgentHost.csproj -c Debug --no-restore
```

현재 CLI는 `net8.0`을 대상으로 합니다. 로컬에 .NET 8 런타임이 없고 더 높은 런타임만 있을 때는 다음처럼 실행할 수 있습니다.

```bash
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll --help
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.AgentHost/bin/Debug/net8.0/wcr2-agent.dll --help
```

## Agent Runtime

`wcr2-agent`는 사람이 직접 옵션을 조합하는 CLI보다, 에이전트가 JSON job을 실행하고 manifest를 남기는 용도에 맞춘 인터페이스입니다. 현재는 job 파싱, 빈 job, `noop`, `image.search`, `image.export-related`, `skill.export`, `skill.export-batch` step을 지원합니다.

```bash
wcr2-agent run --job job.json --cli WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll --json
```

최소 job 예시:

```json
{
  "outputDir": ".test/agent-runs/noop",
  "steps": [
    {
      "id": "probe",
      "type": "noop"
    }
  ]
}
```

`outputDir`가 있으면 `agent-result.json` manifest를 생성합니다.

이미지 검색 job 예시:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/agent-runs/image-search",
  "steps": [
    {
      "id": "find-image",
      "type": "image.search",
      "query": "/path/to/query.png",
      "scope": ["ui"],
      "maxResults": 10,
      "trustCache": true,
      "refine": true,
      "exportTopResults": true
    },
    {
      "id": "export-related",
      "type": "image.export-related",
      "fromStep": "find-image",
      "parentDepth": 1,
      "sourceLimit": 1,
      "maxFiles": 100
    }
  ]
}
```

`image.search` step은 CLI `wcr2 image search`와 같은 Headless 서비스를 사용합니다. `image.export-related` step은 이전 검색 결과의 `ParentPath` 기준으로 같은 그룹의 PNG를 재귀 추출합니다. 상대 경로는 현재 작업 디렉터리 기준으로 해석합니다.

스킬 단건 export job 예시:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/agent-runs/skills",
  "cliPath": "WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll",
  "steps": [
    {
      "id": "firecracker",
      "type": "skill.export",
      "skillId": "5241503",
      "videoFormat": "png",
      "branch": "auto"
    }
  ]
}
```

스킬 배치 export job 예시:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/agent-runs/skill-batch",
  "steps": [
    {
      "id": "batch",
      "type": "skill.export-batch",
      "idsFile": "skill-requests.json",
      "outRoot": ".test/agent-runs/skill-batch/exports",
      "manifest": ".test/agent-runs/skill-batch/manifest.json",
      "continueOnError": true,
      "skipExisting": true,
      "videoFormat": "png"
    }
  ]
}
```

`skill.export`와 `skill.export-batch`는 Phase 4A 기준으로 기존 검증된 `wcr2 skill export/export-batch` 명령을 내부 CLI 브리지로 호출합니다. agent는 `--cli`, job `cliPath`, step `cliPath`, `WCR2_CLI_PATH`, 또는 같은 output 폴더의 `wcr2`/`wcr2.exe`/`wcr2.dll` 순서로 CLI를 찾습니다. 각 step은 CLI stdout을 `agent-skill-export-result.json` 또는 `agent-skill-batch-result.json`에 저장하고, 기존 `skill-info.json`, `resources.json`, batch `manifest.json`은 그대로 유지합니다. 이후 단계에서 구현을 Headless 서비스로 옮겨도 job 형식은 유지합니다.

## 처음 사용하는 순서

MapleStory 설치 폴더의 `Data` 경로를 먼저 확인합니다. Windows 기본 예시는 `C:\Nexon\Maple\Data`, macOS/CrossOver 예시는 `~/Library/Application Support/MapleStory/Bottles/maplestory/drive_c/Nexon/Maple/Data`입니다.

스킬을 뽑을 때는 아래 순서로 확인하면 실수를 줄일 수 있습니다.

```bash
# 1. 이름으로 후보 확인
wcr2 skill search-name --data-dir Data --name "파이어크래커" --json

# 2. 직업 코드까지 넣어 단일 skill id로 확정
wcr2 skill resolve-name --data-dir Data --name "파이어크래커" --job-code 524 --json

# 3. 확정된 id로 이미지/사운드/비디오/관련 리소스 추출
wcr2 skill export --data-dir Data --id 5241503 --out out/firecracker --video-format png --json

# 4. 여러 개는 한 프로세스에서 배치 추출
wcr2 skill export-batch --data-dir Data --names-file skill-names.tsv --out-root out/skills --manifest out/skills/manifest.json --json
```

`resolve-name` 결과가 `ambiguous`이면 같은 이름 또는 비슷한 이름의 skill id가 여러 개 남아 있다는 뜻입니다. 이 경우 `Candidates`의 `Id`, `JobCode`, `FoundData`, `SourceProfile`, `VisualBranches`를 보고 `--job-code`를 추가하거나 ID를 직접 지정합니다.

## Commands

```bash
wcr2 info <file-or-dir> [--json]
wcr2 tree <file-or-dir> [--path <wz-path>] [--depth <n>] [--limit <n>] [--json]
wcr2 list <file-or-dir> [--path <wz-path>] [--json]
wcr2 search <file-or-dir> --name <text> [--path <wz-path>] [--json]
wcr2 search <file-or-dir> --value <text> [--path <wz-path>] [--json]
wcr2 search <file-or-dir> --match-path <glob-or-regex> [--type <type>] [--json]
wcr2 compare <old-file-or-dir> <new-file-or-dir> [--path <wz-path>] [--type added|removed|changed] [--format json|markdown] [--out <path>] [--json]
wcr2 dump <file-or-dir> --path <wz-path> [--format json|xml|raw] [--out <path>]
wcr2 extract <file-or-dir> --path <wz-path> --out <output-dir> [--recursive] [--manifest <json>] [--json]
wcr2 sound list <file-or-dir> [--path <wz-path>] [--max-results <n>] [--json]
wcr2 sound export <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]
wcr2 sound export-all <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]
wcr2 image list <file-or-dir> [--path <wz-path>] [--max-results <n>] [--json]
wcr2 image search [<file-or-dir>] --query <png> [--data-dir <Data>] [--scope ui,item,skill,...] [--path <wz-path>] [--out <output-dir>] [--max-results <n>] [--min-score <0..1>] [--cache-dir <dir>] [--trust-cache] [--no-refine] [--json]
wcr2 image export <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]
wcr2 image export-all <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]
wcr2 video list <file-or-dir> [--path <wz-path>] [--max-results <n>] [--json]
wcr2 video export <file-or-dir> --path <wz-path> --out <output-dir> [--format mcv|frames|png|gif|both] [--ffmpeg <path>] [--manifest <json>] [--json]
wcr2 video export-all <file-or-dir> --path <wz-path> --out <output-dir> [--format mcv|frames|png|gif|both] [--ffmpeg <path>] [--manifest <json>] [--json]
wcr2 skill info [<wz-file-or-dir>] --id <id> [--skill-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]
wcr2 skill full [<skill-wz-file-or-dir>] --id <id> [--skill-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--level <n|max>] [--format json|xml|text] [--out <path>]
wcr2 skill search-name [<skill-wz-file-or-dir>] --name <text> [--job-code <code>] [--data-dir <dir>] [--max-results <n>] [--json]
wcr2 skill resolve-name [<skill-wz-file-or-dir>] --name <text> [--job-code <code>] [--data-dir <dir>] [--json]
wcr2 skill sprite [<skill-wz-file-or-dir>] --id <id> --out <dir> [--skill-wz <file-or-dir>] [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--branch icon,effect,hit] [--json]
wcr2 skill export [<skill-wz-file-or-dir>] --id <id> --out <dir> [--skill-wz <file-or-dir>] [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--sound-wz <file-or-dir>] [--related-key <name>] [--video-format mcv|frames|png|gif|both] [--branch auto|icon,effect,hit] [--json]
wcr2 skill export-batch [<skill-wz-file-or-dir>] --ids <id,id>|--ids-file <path>|--names-file <path> --out-root <dir> [--skill-wz <file-or-dir>] [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--sound-wz <file-or-dir>] [--related-key <name>] [--video-format mcv|frames|png|gif|both] [--branch auto|icon,effect,hit] [--skip-existing] [--continue-on-error] [--manifest <json>] [--json]
wcr2 item info [<wz-file-or-dir>] --id <id> [--item-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]
wcr2 item icon [<item-or-data-dir>] --name <exact-name>|--id <id> --out <dir> [--data-dir <dir>] [--string-wz <file-or-dir>] [--canvas-wz <file-or-dir>] [--category cash|consume|install|etc|pet] [--json]
wcr2 gear info [<wz-file-or-dir>] --id <id> [--character-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]
wcr2 mob info [<wz-file-or-dir>] --id <id> [--mob-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]
wcr2 npc info [<wz-file-or-dir>] --id <id> [--npc-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]
wcr2 quest info [<wz-file-or-dir>] --id <id> [--quest-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]
wcr2 map info [<wz-file-or-dir>] --id <id> [--map-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]
wcr2 map objects <map-wz-file-or-dir> --id <map-id> [--json]
wcr2 map portals <map-wz-file-or-dir> --id <map-id> [--json]
wcr2 map life <map-wz-file-or-dir> --id <map-id> [--json]
wcr2 map reactors <map-wz-file-or-dir> --id <map-id> [--json]
wcr2 map render [<map-wz-file-or-dir>] --id <map-id> --out <map.png> --dry-run [--layer <n|all>] [--include-life] [--include-reactor] [--include-tooltip] [--json]
wcr2 animate frames <wz-file-or-dir> --path <wz-path> --out <dir> [--json]
wcr2 animate gif <wz-file-or-dir> --path <wz-path> --out <file.gif> [--background transparent|#RRGGBB] [--min-alpha <0-255>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]
wcr2 animate apng <wz-file-or-dir> --path <wz-path> --out <file.png> [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--optimize] [--json]
wcr2 animate ffmpeg <wz-file-or-dir> --path <wz-path> --out <file> [--ffmpeg <path>] [--ffmpeg-args <format>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]
wcr2 avatar inspect --code <code> [--json]
wcr2 avatar unpack --code <code>
wcr2 avatar render --code <code> --out <avatar.png> --dry-run [--action <action>] [--emotion <emotion>] [--offline] [--api-key <key>] [--json]
wcr2 avatar render --items <ids> --out <avatar.png> --dry-run [--action <action>] [--emotion <emotion>] [--json]
wcr2 lua run <script.lua> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]
wcr2 lua eval <code> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]
wcr2 lua eval --code <code> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]
wcr2 network server-info [--host <host>] [--port <port>] [--connect] [--json]
wcr2 network chat [--host <host>] [--port <port>] [--json]
wcr2 network send --message <text> [--host <host>] [--port <port>] [--json]
wcr2 update check [--asset net8|net10|net6|net462|zip] [--json]
wcr2 update download --out <dir> [--asset net8|net10|net6|net462|zip] [--force] [--json]
wcr2 update apply [--asset net8|net10|net6|net462|zip] [--updater <path>] [--download <zip>] [--execute] [--json]
wcr2 config path [--config <path>] [--json]
wcr2 config list [--config <path>] [--profile <name>] [--json]
wcr2 config get <key> [--config <path>] [--profile <name>] [--json]
wcr2 config set <key> <value> [--config <path>] [--profile <name>] [--json]
wcr2 config unset <key> [--config <path>] [--profile <name>] [--json]
wcr2 plugin list [--plugin-dir <dir>] [--include-gui-plugin-dir] [--json]
wcr2 plugin inspect <assembly.dll> [--json]
wcr2 plugin commands [--plugin-dir <dir>] [--json]
wcr2 plugin run <command> [args...] [--plugin-dir <dir>]
wcr2 patch inspect <patch-file> [--json]
wcr2 patch dry-run <patch-file> --target <dir> [--json]
wcr2 patch apply <patch-file> --target <dir> --out <dir> [--log <file>] [--json]
wcr2-agent run --job <job.json> [--out <dir>] [--json]
wcr2-agent serve --stdio
```

공통 옵션:

```bash
--use-base-wz
--fallback <path>
--extract-images
--json
--quiet
--verbose
--no-color
--format xml
--regex
--ignore-image-binary
```

## Examples

```bash
wcr2 info Base.wz
wcr2 tree Base.wz --depth 2
wcr2 list Base.wz --path Character
wcr2 search String.wz --name Maple --json
wcr2 search Base.wz --match-path "*/Canvas" --type png
wcr2 compare old/Base.wz new/Base.wz --json
wcr2 compare old/Base.wz new/Base.wz --path Character --type changed --max-results 200 --out out/character-diff.json
wcr2 compare old/Base.wz new/Base.wz --format markdown --out out/compare.md
wcr2 dump Base.wz --path String --format json --out out/string.json
wcr2 dump Base.wz --path String --format xml --out out/string.xml
wcr2 extract Base.wz --path String --out out/string --recursive
wcr2 extract Base.wz --path String --out out/string --recursive --manifest out/string/manifest.json
wcr2 extract Base.wz --path String --out out/string.xml --format xml
wcr2 sound list Data/Sound --max-results 20 --json
wcr2 sound export Data/Sound --path AchievementEff.img/GradeUp --out out/sound --manifest out/sound/manifest.json --json
wcr2 image list Data/Mob_Canvas --path 0100100.img --max-results 20 --json
wcr2 image search --data-dir Data --scope ui --query query.png --out out/image-search --manifest out/image-search/manifest.json --json
wcr2 image export Data/Mob_Canvas --path 0100100.img/stand/0 --out out/image --manifest out/image/manifest.json --json
wcr2 skill info Skill.wz --id 1001004 --string-wz String.wz --json
wcr2 skill full Data/Skill --id 11001025 --string-wz Data/String --format json --out out/skill-11001025.json
wcr2 skill full --data-dir Data --id 1001008 --format json --out out/skill-1001008.json
wcr2 skill full Data/Skill --id 1001004 --string-wz Data/String --allow-string-only --format xml --out out/power-strike.xml
wcr2 skill search-name --data-dir Data --name "파이어크래커" --json
wcr2 skill resolve-name --data-dir Data --name "파이어크래커" --job-code 524 --json
wcr2 skill sprite --data-dir Data --id 1121008 --branch effect,hit --out out/skill-1121008 --json
wcr2 skill export --data-dir Data --id 1121008 --out out/skill-1121008 --json
wcr2 skill export-batch --data-dir Data --ids-file skills.tsv --out-root out/skills --skip-existing --manifest out/skills/manifest.json --json
wcr2 skill export-batch --data-dir Data --names-file skill-names.tsv --out-root out/skills-by-name --skip-existing --manifest out/skills-by-name/manifest.json --json
wcr2 item info Item.wz --id 2000000 --string-wz String.wz
wcr2 item icon --data-dir Data --name "미라클 큐브" --out out/icons/miracle-cube --json
wcr2 item icon --data-dir Data --name "보따리상인 묘묘(7일)" --out out/icons/myomyo-7day --json
wcr2 item icon --data-dir Data --id 5072000 --out out/icons/megaphone --json
wcr2 gear info Character.wz --id 1002140 --string-wz String.wz --json
wcr2 mob info Mob.wz --id 100100 --string-wz String.wz --json
wcr2 npc info Npc.wz --id 9000000 --string-wz String.wz --json
wcr2 quest info Quest.wz --id 1000 --string-wz String.wz --json
wcr2 map info Map.wz --id 100000000 --string-wz String.wz
wcr2 map portals Map.wz --id 100000000 --json
wcr2 map objects Map.wz --id 100000000 --json
wcr2 map render --id 100000000 --out out/map.png --dry-run --include-life --json
wcr2 animate frames Mob.wz --path 0100100.img/stand --out out/stand --json
wcr2 animate gif Mob.wz --path 0100100.img/stand --out out/stand.gif --json
wcr2 animate gif Mob.wz --path 0100100.img/stand --out out/stand-fast.gif --start-frame 1 --end-frame 4 --delay 80 --scale 2 --origin 0,0 --background '#ffffff'
wcr2 animate apng Mob.wz --path 0100100.img/stand --out out/stand.png --json
wcr2 animate ffmpeg Mob.wz --path 0100100.img/stand --out out/stand.mp4 --ffmpeg /usr/local/bin/ffmpeg --json
wcr2 avatar inspect --code "1002140,1040036,1060026"
wcr2 avatar unpack --code "1002140,1040036,1060026" --json
wcr2 avatar render --items "00002000,00012000,00020000,00030000,1002140" --out out/avatar.png --dry-run --json
wcr2 lua run WzComparerR2.LuaConsole/Examples/DumpXml.lua --dry-run --json
wcr2 lua eval --code "print('ok')" --dry-run --json
wcr2 network server-info --json
wcr2 network server-info --connect --timeout 5
wcr2 network send --message "hello" --json
wcr2 update check --asset net8 --json
wcr2 update download --asset net8 --out downloads
wcr2 update apply --asset net8 --json
wcr2 config path
wcr2 config set default-wz /path/to/Base.wz
wcr2 config set default-wz /path/to/KMS/Base.wz --profile kms
wcr2 config get default-wz
wcr2 config unset default-wz
wcr2 plugin list --plugin-dir CliPlugin --json
wcr2 plugin inspect CliPlugin/MyPlugin.dll
wcr2 plugin commands
wcr2 plugin run my-command arg1 arg2 --plugin-dir CliPlugin
wcr2 patch inspect MaplePatch.patch --json
wcr2 patch dry-run MaplePatch.patch --target MapleStory --json
wcr2 patch apply MaplePatch.patch --target MapleStory --out MapleStory.patched --log patch.log
```

More workflow examples are available in:

- `docs/cli-migration.md`: GUI workflow to CLI command mapping.
- `samples/cli/batch-extract.sh`: batch extract multiple WZ paths.
- `samples/cli/compare-report.sh`: generate JSON and Markdown compare reports.
- `samples/cli/avatar-metadata.sh`: export avatar-code metadata.
- `samples/cli/map-metadata.sh`: export map portal/life/object/reactor metadata.
- `samples/cli/windows-maple-smoke.ps1`: Windows smoke test script for a real MapleStory client folder.
- `docs/windows-cli-test-checklist.md`: Windows manual checklist and report template.

## First Run

For a new CLI checkout, the shortest verification path is:

```bash
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug --no-restore
dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll info /path/to/Base.wz
dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll tree /path/to/Base.wz --depth 2 --json
dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll extract /path/to/Base.wz --path String --out out/string --recursive --manifest out/string/manifest.json
dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll compare old/Base.wz new/Base.wz --format markdown --out out/compare.md
```

If the machine only has a newer .NET runtime, prefix the command with `DOTNET_ROLL_FORWARD=Major`.

## JSON Output Contract

`--json` uses indented UTF-8 JSON with stable, PascalCase property names from the command result DTOs.
Korean and other non-ASCII WZ strings are emitted as readable text instead of `\uXXXX` escape sequences.
For automation, prefer checking high-level fields rather than relying on text output.
`--quiet` suppresses stdout for successful commands, while errors still go to stderr.
`--verbose` adds exception details to stderr on failure.
`--no-color` is accepted for script compatibility; current CLI output does not emit color codes.

Common result shapes:

```json
{
  "InputPath": "/path/to/Base.wz",
  "RootName": "Base.wz",
  "WzFileCount": 1,
  "Files": []
}
```

```json
{
  "OldInputPath": "old/Base.wz",
  "NewInputPath": "new/Base.wz",
  "AddedCount": 0,
  "RemovedCount": 0,
  "ChangedCount": 0,
  "Differences": []
}
```

```json
{
  "InputPath": "Base.wz",
  "Path": "String",
  "OutputDirectory": "out/string",
  "Files": []
}
```

Exit codes are part of the automation contract; text messages may change as help text improves.

## Testing

The CLI test harness intentionally avoids external test framework packages.
It builds as a console project and runs the compiled CLI as a child process.

```bash
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug --no-restore
dotnet build WzComparerR2.Cli.Tests/WzComparerR2.Cli.Tests.csproj -c Debug --no-restore
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli.Tests/bin/Debug/net8.0/wcr2-tests.dll --cli WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll
```

The current automated tests cover CLI help/version, usage errors, config, avatar metadata, Lua dry-run, network dry-run, update validation, and CLI plugin discovery/execution.
Full WZ happy-path golden tests still require sample `.wz`/`.img` fixtures.
Snapshot and real-client verification rules are documented in [`docs/cli-test-strategy.md`](cli-test-strategy.md).

Real Maple clients can use a split `Data` layout.
If `String.wz`, `Map.wz`, `Skill.wz`, or similar root files load but do not contain the expected path/id, try the data-bearing folder or shard instead, for example `Data\String`, `Data\Skill`, `Data\Character\Cap`, `Data\Map\Map\Map1\Map1_000.wz`, or `Data\Mob_Canvas`.
On macOS/CrossOver installs, the same canvas data may appear under split folders such as `Data/Mob/_Canvas`.
Current KMS-style macOS/CrossOver shards such as `Data/String/String_000.wz`, `Data/Item/Cash/Cash_000.wz`, and `Data/Item/Cash/_Canvas/_Canvas_000.wz` can use the KMST1202 150-byte randomized PKG2 header. The CLI/WzLib reader recognizes this layout and reports `WzVersion: 1202` for those files.
When loading still fails with `--json`, the CLI reports exit code `3` and writes a structured error JSON to stderr with the first header bytes, file size, and PKG2 random-header probe results including `CurrentPkg2RandomHeader64DataSizeMatches`.
For searches inside `.img` nodes, pass `--extract-images`, for example `wcr2 search Data/String/String_000.wz --value "미라클 큐브" --extract-images --json`.
Cash icons may be split across `_Canvas` shards; for example item `5062000` uses `Data/Item/Cash/_Canvas/_Canvas_001.wz` path `0506.img/05062000/info/icon`.
For item icons, prefer `item icon --data-dir Data --name "<item name>" --out <dir> --json`. It resolves the item id from `String`, chooses the matching `Item/<category>/_Canvas` folder, exports `info/icon` as PNG, and reports the source path/hash. Duration labels such as `[7일]name` and `name(7일)` are normalized for lookup; if a duration-specific id has no separate icon, the command tries representative id fallbacks such as `5450007 -> 5450000` and records that in `Diagnostics`.

`extract`는 PNG, sound, raw data, video blob, scalar 값을 자동으로 파일로 내보냅니다.
컨테이너 노드를 선택하면 `--recursive`가 필요합니다.
`sound list`와 `image list`는 실제 export 전에 경로와 메타데이터를 찾는 전용 명령입니다.
`sound export`는 기존 `Wz_Sound.ExtractSound()` 경로를 재사용해 MP3, PCM WAV, raw fallback 파일을 씁니다.
`image export`는 Windows에서 기존 `Wz_Png.ExtractPng()`/`System.Drawing` PNG 저장 경로를 재사용합니다.
macOS/Linux에서는 CLI의 cross-platform PNG writer가 `ARGB4444`, `ARGB8888`, `ARGB1555`, `RGB565`, `DXT3`, `DXT5`, `A8`, `RGBA1010102`, `BC7` 같은 일반 WZ texture format을 직접 저장합니다.
아직 지원하지 않는 texture format은 명확한 진단을 반환하며, 이 경우 `image list` 메타데이터 조회는 계속 사용할 수 있습니다.
`image search`는 로컬 PNG를 query로 받아 WZ PNG 노드와 유사도를 비교합니다. 명시 input 없이 `--data-dir <Data>`를 주면 `UI`, `Item`, `Skill`, `Effect`, `Character`, `Mob`, `Npc`, `Map`, `Etc`, `Quest`, `Reactor`, `Morph` 아래의 `_Canvas` root를 자동으로 찾습니다. 범위를 줄이려면 `--scope ui,item,skill`처럼 comma-separated scope를 지정합니다.
검색은 투명 영역 crop, pHash 기반 perceptual hash, 색 평균, query/candidate 부분 영역(`left-half`, `right-half`, `top-half`, `bottom-half`, `center`) 비교를 함께 사용합니다. 큰 query에서는 작은 후보를 먼저 제외하는 size prefilter가 기본으로 켜져 있으며, 기준은 `--min-size-ratio 0.25`입니다. query가 강하게 확대/축소된 이미지라면 `--no-size-prefilter`로 끌 수 있습니다.
`--out`을 지정하면 상위 후보 PNG를 함께 추출하고, `--manifest`를 지정하면 검색 결과 JSON을 저장합니다. `--data-dir` 검색은 OS 사용자 cache에 root별 fingerprint index를 gzip JSON으로 저장합니다. 기본 위치는 macOS `~/Library/Caches/wcr2/image-search`, Windows `%LOCALAPPDATA%\wcr2\image-search`, Linux `${XDG_CACHE_HOME:-~/.cache}/wcr2/image-search`입니다. cache key에는 input path와 alpha 설정만 포함되므로 같은 root의 다른 query에도 재사용됩니다. 다시 만들려면 `--rebuild-cache`, 완전히 끄려면 `--no-cache`를 사용합니다. cache hit 후에는 기본적으로 상위 후보 pool을 WZ에서 다시 열어 pixel-level score로 refine합니다. 속도만 우선할 때는 `--trust-cache --no-refine`을 사용할 수 있습니다.
`video list`는 `Wz_Video`/MCV 노드의 `FourCC`, width/height, frame count, alpha map flag를 조회합니다.
`video export`는 기본적으로 원본 `.mcv`를 저장합니다. `--format frames`는 alpha map을 적용한 PNG 프레임을 만들고, `--format gif`는 확인용 GIF를 만듭니다. `frames`, `gif`, `both`는 ffmpeg가 필요하며, 실행 파일이 PATH에 없으면 `--ffmpeg <path>`를 지정합니다.
export manifest의 각 파일 항목에는 `Bytes`와 `Sha256`이 포함됩니다.

`skill/item/gear/map info`는 먼저 데이터 WZ에서 id 노드를 찾고, `--string-wz`가 있으면 String.wz의 이름/설명 값을 추가합니다.
`--data-dir <Data>`를 주면 `Skill`, `Item`, `Character`, `Map`, `Mob`, `Npc`, `Quest`와 sibling `String` 후보를 자동으로 사용합니다. JSON에는 `DataInputPath`, `StringInputPath`, 후보 목록이 포함됩니다.
현재는 텍스트/JSON 메타데이터 조회가 중심이며, tooltip image 렌더링은 아직 포함하지 않습니다.

`skill full`은 렌더링 없이 CharaSim 스타일의 headless 스킬 해석 결과를 내보냅니다.
출력에는 `SourceProfile`, `LinkerStatus`, `UnresolvedPlaceholders`, `DataInputPath`, `StringInputPath`, 입력 후보 목록, `Common`, `PvpCommon`, `LevelProperties`, 요구 스킬, 액션, 플래그, 아이콘 메타데이터, 원문 요약, `ResolvedSummary`, 가능한 경우 `NextResolvedSummary`, 미해결 placeholder 진단이 포함됩니다.
`ResolvedSummary`는 `String/Skill.img`의 `h` 템플릿을 기준으로 계산하며, `#c10...#` 같은 색상 태그는 표시용 markup으로 보고 제거합니다. 현재 skill id의 문자열 노드에 `desc`/`h`가 없으면 이름이 같은 다른 skill id에 설명이 있어도 자동으로 덮어쓰지 않으므로 `Description` 또는 `RawSummary`가 `null`일 수 있습니다.
`LinkerStatus`는 CLI headless resolver가 데이터 노드, String metadata, scalar stat, visual branch, summary template를 각각 찾았는지 보여줍니다. `GuiStringLinkerLoaded`는 아직 `false`이며, 이는 GUI의 전체 `StringLinker.Load(...)`를 직접 붙인 상태가 아니라 CLI 안전 범위의 headless 해석이라는 뜻입니다.
`SourceProfile`이 `visual-only`이면 입력 WZ 노드에 아이콘/이펙트 같은 canvas 계열 데이터는 있지만 `common`/`level` 수치 property가 없다는 뜻입니다. 이 경우 `MaxLevel`, `LevelCount`, placeholder 치환 값이 `null` 또는 미해결로 남을 수 있습니다.
`--data-dir <Data>`를 주면 먼저 `Data/Skill`과 `Data/String`을 자동 후보로 사용합니다. `Data/Skill`에서 skill id를 못 찾거나 찾은 노드가 canvas/visual-only에 가까우면 `Data/Packs/Skill_*.ms`를 lazy fallback으로 순회해 더 풍부한 metadata 노드를 찾습니다. pack 내부에서는 `psdSkill/<id>` 같은 파생 marker보다 `.../skill/<id>` 실제 스킬 노드를 우선하고, 첫 약한 매치에서 멈추지 않고 더 높은 metadata 점수의 후보를 선택합니다. positional `Data/Skill`만 줘도 sibling `Data/String`이 있으면 문자열 후보로 자동 추가합니다.
스킬 ID를 모르는 상태에서는 먼저 `skill search-name --data-dir Data --name "<스킬명>" --json`을 실행합니다. 이 명령은 `String/Skill.img`를 순회해 이름 후보를 찾고, 각 후보가 실제 `Data/Skill` 또는 `Data/Packs/Skill_*.ms`에 있는지 확인해 `FoundData`, `SourceProfile`, `StatPropertyCount`, `VisualBranches`, `DataPath`, `StringPath`를 함께 출력합니다.
자동화 전에 단일 ID로 결정 가능한지 확인하려면 `skill resolve-name --data-dir Data --name "<스킬명>" --job-code <직업코드> --json`을 사용합니다. 이름의 공백/기호 차이는 정규화하고 짧은 오타는 fuzzy 후보로 보여주지만, 동명이인이나 파생 ID가 남으면 `Status: ambiguous`로 실패합니다. `--job-code`는 `floor(skillId / 10000)`로 계산되는 Maple job code와 정확히 비교합니다. 예를 들어 `1111010`은 `111`, `11111004`는 `1111`입니다. `--job-code`를 주면 해당 직업 코드에 맞는 후보만 resolved 대상이 되므로 엑셀 스킬명을 잘못 매칭하는 실수를 줄일 수 있습니다.
`--format json|xml|text`와 `--out <path>`를 지원합니다.
실제 skill node가 없고 `String/Skill.img` 문자열만 있는 ID는 기본적으로 실패하지만, `--allow-string-only`를 주면 `Mode: string-only` 결과로 이름/설명/문자열 속성을 확인할 수 있습니다.
툴팁 PNG 렌더링은 아직 포함하지 않으며 Windows-only 후속 단계로 분리되어 있습니다.

`skill sprite`는 스킬 ID 기준으로 `icon`, `effect`, `hit` 같은 스프라이트 branch만 PNG로 내보냅니다. 소리까지 같이 뽑으려면 `skill export`를 쓰거나 `skill sprite --include-sound`를 추가합니다.
스킬 메타데이터 안의 PNG가 `_outlink`가 달린 1x1 stub이면, CLI는 `_outlink` 값을 읽고 `--canvas-wz` 또는 `--data-dir <Data>`에서 찾은 `Data/Skill/_Canvas`와 `Data/Packs/Skill*.ms` 후보를 순회해 실제 Canvas 노드를 해석합니다.
`skill export`는 실제 스킬 노드에서 감지한 visual branch를 자동 추출한 뒤 `Sound/Skill.img/<skillId>` 아래의 `Use`, `Hit` 같은 사운드를 함께 추출합니다. `--data-dir <Data>`만 준 경우에도 `Data/Skill`에서 못 찾은 스킬은 `Data/Packs/Skill_*.ms` metadata fallback으로 찾습니다. 자동 추출은 `screen`, `screen2`, `tile`, `special`, `special1`, `effect2`처럼 스킬마다 다른 branch 이름을 포함합니다.
`skill sprite`와 `skill export`는 `--out` 폴더에 `skill-info.json`과 `resources.json`도 함께 저장합니다. `skill-info.json`에는 스킬 이름, 설명, 원본/치환된 설명 템플릿, 선택 레벨, unresolved placeholder, data/string 경로가 들어갑니다. `resources.json`에는 `icon`, `effect`, `hit`, `screen*`, `sound`, `video`, `related` 같은 리소스 branch 이름, metadata `InputPath`/`MetadataSourcePath`, `_outlink`/resolved 경로, 실제 canvas `ResolvedInputPath`, 상태, 파일 수, 추출 파일 목록이 들어갑니다. 각 파일 항목에는 `Bytes`, `Sha256`, `RelativePath`, `FrameIndex`, PNG의 `Width`/`Height`/`Format`/`Pages`, 사운드의 `DataLength`/`Ms`/`Channels`/`Frequency`/`SoundType`, MCV의 `Width`/`Height`/`Format`/`FrameCount`/`VideoFlags`가 기록됩니다. 스킬 metadata stub에 `origin`, `lt`, `rb`, `z`, `delay`, `_outlink`, `_inlink` 또는 기타 scalar/vector child가 있으면 `Origin`, `Lt`, `Rb`, `Z`, `Delay`, `OutlinkPath`, `InlinkPath`, `Metadata`로 같이 남깁니다. branch 자체에 `action`, `time`, `repeat` 같은 scalar child가 있으면 resource-level `Metadata`로 기록합니다. CLI가 branch 이름을 해석해 만든 임의 용도 설명은 넣지 않습니다.
`skill export`는 같은 skill id의 노드 아래에 있는 `Wz_Video`도 자동으로 포함합니다. 먼저 대표 `Data/Skill` 노드를 확인하고, 없으면 `Data/Packs/Skill*.ms`에서 같은 skill id의 메타데이터 노드를 찾아 `screen/video`, `screen2/video` 같은 MCV 컷신을 `video/` 아래에 저장합니다. 기본은 PNG 프레임 추출이며, `--video-format mcv`를 주면 원본 `.mcv`를 저장합니다. `--video-format frames|png|gif|both`를 주면 ffmpeg로 PNG 프레임 또는 GIF까지 만듭니다. 필요 없으면 `--skip-video`를 사용합니다.
비디오가 없는 스킬은 `resources.json`/`--json`에 `Status: no-video-assets`로만 기록하고 빈 `video/` 폴더는 만들지 않습니다.
`--sound-wz <path>`로 Sound 입력을 직접 지정할 수 있고, `--data-dir <Data>`를 주면 `Data/Sound`를 자동 후보로 사용합니다.

`skill export`는 추가로 action/delay 기반 related asset lookup을 수행합니다. 대표 스킬 노드가 visual-only shard라 action 값이 비어 있으면 `Data/Packs/Skill*.ms` 메타데이터에서 `action/0 = 6thFireCracker` 같은 문자열 seed를 찾아 `Data/Skill/_Canvas`, `Data/Effect/_Canvas`, `Data/Character/_Canvas`, `Data/Character/Afterimage`를 검색합니다. 직접 seed를 줄 때는 `--related-key 6thFireCracker`를 사용합니다. 내부 `screen*/video`는 `Videos`로 붙고, 외부 action key 검색 결과는 JSON의 `RelatedAssets`와 `RelatedFileCount`에 기록됩니다.
`skill export-batch`는 여러 스킬을 한 프로세스에서 연속 추출합니다. 단건 `skill export`와 같은 스프라이트/사운드/비디오/related 옵션을 사용하지만, Skill/String repository와 Canvas/Sound/Skill*.ms 입력 context를 세션 동안 캐시해서 다량 추출 시 반복 로딩 비용을 줄입니다. 기본은 첫 실패에서 중단하고 exit code 1을 반환합니다. 전체를 끝까지 돌리고 실패 항목만 manifest로 확인하려면 `--continue-on-error`를 사용합니다. 이미 뽑은 항목은 `--skip-existing`을 주면 `export-result.json`이 있는 폴더를 건너뜁니다.
`--ids`는 `1121008,5241503`처럼 쉼표나 세미콜론으로 나열합니다. `--ids-file` 텍스트 파일은 한 줄에 `id` 또는 `id<TAB>relative/output`을 받습니다. 같은 스킬 ID를 여러 직업/역할 폴더에 넣어야 하면 두 번째 칸에 상대 출력 경로를 적습니다. JSON 파일도 가능하며 배열 원소는 문자열 ID 또는 `{ "id": "5241503", "relativeOutput": "522_캡틴/5241503_파이어크래커" }` 객체입니다. 배치 명령은 각 항목 폴더에 `skill-info.json`, `resources.json`, `export-result.json`을 쓰고, `--manifest <json>`을 주면 전체 요약을 별도 파일로 저장합니다.
`--names-file`도 지원합니다. 텍스트 파일은 한 줄에 `name`, `jobCode<TAB>name`, `jobCode<TAB>name<TAB>relative/output`, 또는 `jobName<TAB>jobCode<TAB>name<TAB>relative/output`을 받습니다. JSON 파일은 문자열 이름 배열 또는 `{ "name": "파이어크래커", "jobCode": "524", "relativeOutput": "524_캡틴/5241503_파이어크래커" }` 객체 배열을 받습니다. 이름 기반 항목은 내부적으로 `resolve-name`과 같은 규칙을 사용하고, 단일 ID로 확정되지 않으면 해당 항목을 failed로 기록합니다.
`--branch auto`, `--branch visual`, `--branch all`은 자동 감지된 visual branch를 추출합니다. `--branch effect,hit/0`처럼 쉼표로 여러 branch를 직접 지정할 수도 있고, `--branch auto,hit/0`처럼 자동 감지와 명시 branch를 합칠 수도 있습니다.
출력 JSON에는 branch별 `Status`, `OutlinkPath`, `ResolvedPath`, `TriedCanvasInputs`, 사운드 `Status`, `TriedSoundInputs`, 추출된 파일의 `Bytes`, `Sha256`, media metadata, frame metadata가 포함됩니다. 단, 특정 variant가 실제 canvas PNG만 갖고 있고 `origin`/`delay`/`z` child가 없는 경우에는 해당 값이 null로 남습니다.
최신 클라이언트의 특정 Canvas shard가 현재 WzLib에서 읽히지 않으면 명령은 엔진을 우회해 복호화하지 않고 `outlink-not-resolved`와 로딩 진단을 남깁니다. 이 경우 명시적으로 읽히는 Canvas 파일을 `--canvas-wz`로 넘기거나 WzLib 패키지 포맷 지원을 별도 단계로 확장해야 합니다.

스킬 ID만 알고 있을 때의 기본 경로 규칙은 다음과 같습니다.
일반 직업 스킬은 대체로 `floor(skillId / 10000).img/skill/<skillId>`에 있습니다. 예를 들어 `1121008`은 `112.img/skill/1121008`입니다.
split client에서는 이 메타데이터가 `Data/Packs/Skill_00000.ms` 같은 pack 안에 있고, 실제 픽셀은 `_outlink`로 분리된 `Skill/_Canvas/112.img/skill/1121008/...`에 있을 수 있습니다.
따라서 직접 경로를 확인하려면 먼저 `skill full --json`의 `DataInputPath`와 `DataPath`를 보고, 실제 스프라이트 추출 결과는 `skill sprite --json`의 branch별 `OutlinkPath`와 `ResolvedPath`를 보면 됩니다.
경로를 수동으로 확인하는 예시는 아래와 같습니다.

```bash
wcr2 skill full --data-dir Data --id 1121008 --format json --out out/1121008-full.json
wcr2 image list Data/Packs/Skill_00000.ms --path 112.img/skill/1121008 --max-results 50 --json
wcr2 skill sprite --data-dir Data --id 1121008 --branch icon,effect,hit/0 --out out/1121008-sprite --json
wcr2 skill export --data-dir Data --id 1121008 --out out/1121008-assets --json
wcr2 skill export-batch --data-dir Data --ids 1121008,5241503 --out-root out/skill-batch --video-format png --json
wcr2 video list Data/Packs/Skill_00006.ms --path Skill/524.img/skill/5241503 --json
wcr2 video export Data/Packs/Skill_00006.ms --path Skill/524.img/skill/5241503/screen2/video --format frames --out out/firecracker-screen2
wcr2 skill export --data-dir Data --id 5241503 --video-format gif --out out/firecracker --json
```

`map objects/portals/life/reactors`는 map `.img` 안의 해당 섹션을 읽어 좌표, id, 이동 대상 같은 scalar property를 JSON으로 내보냅니다.
`map render --dry-run`은 실제 PNG를 만들지 않고, map id 기반 후보 shard/path, layer/include 옵션, MonoGame headless 렌더링 blocker를 JSON으로 출력합니다.
MonoGame 기반 실제 screenshot 렌더링은 아직 CLI에 포함하지 않습니다.

`animate frames`는 숫자 자식 노드를 프레임으로 보고 각 프레임의 PNG/사운드/스칼라 값을 하위 폴더로 추출한 뒤 `frames.json` manifest를 생성합니다.
`animate gif`는 기존 built-in GIF encoder를 사용해 직접 PNG 프레임이 있는 애니메이션 노드를 GIF로 저장합니다.
`animate apng`는 기존 native `libapng.dll` 기반 encoder를 사용하며 Windows x64 배포에 DLL을 함께 복사합니다.
`animate ffmpeg`는 `ffmpeg` 실행 파일로 raw BGRA 프레임을 pipe 전송해 mp4 등 외부 encoder 출력을 생성합니다.
`--ffmpeg <path>`로 실행 파일 경로를 지정할 수 있고, `--ffmpeg-args <format>`은 `%i` 입력 pipe, `%w` 폭, `%h` 높이, `%t` 프레임 delay, `%o` 출력 파일 자리표시자를 지원합니다.
세 encoder 명령은 `--start-frame`, `--end-frame`, `--delay`, `--scale`, `--origin`을 공유합니다.
GIF는 추가로 `--background transparent|#RRGGBB`, `--min-alpha <0-255>`를 지원합니다.
UOL이 다른 WZ 파일을 참조하는 복잡한 애니메이션은 아직 제한적입니다.
macOS에서는 WZ PNG 추출이 `System.Drawing/GDI+`에서 실패할 수 있으므로 Windows 실클라 검증을 우선하세요.

`avatar inspect`와 `avatar unpack`은 avatar code 안의 item id를 추출하고 MapleStory item id prefix로 장비 슬롯을 추정합니다.
`avatar render --dry-run`은 실제 PNG를 만들지 않고, 입력 아이템의 캐릭터 파츠 분류, 후보 WZ 경로, action/emotion, OpenAPI 옵션 상태, headless 렌더링 blocker를 JSON으로 출력합니다.
현재 실제 캐릭터 PNG 렌더링은 아직 포함하지 않습니다.

`lua run`과 `lua eval`은 새 의존성을 추가하지 않기 위해 현재 시스템 `PATH`의 `lua`, `lua5.4`, `lua5.3`, `luajit` 실행기를 사용합니다.
`--wz`를 지정하면 WZ 로딩 가능 여부를 먼저 확인하고 `WCR2_WZ_INPUT`, `WCR2_WZ_ROOT` 환경 변수를 전달합니다.
기존 LuaConsole의 `env`/NLua 통합 API는 아직 headless CLI로 이식하지 않았으므로 기존 예제 검증은 `--dry-run`부터 사용하세요.

`network` 명령은 기본적으로 dry-run입니다.
`server-info --connect`는 프로토콜 로그인 없이 TCP 접속 가능 여부만 확인하며, `chat`과 `send`는 아직 GUI 플러그인의 실제 채팅 세션을 대체하지 않습니다.
`--interactive`는 예약 옵션이며 현재는 명시적으로 거부됩니다.

`update check`는 GitHub latest release API에서 릴리스 정보와 다운로드 URL을 조회합니다.
현재 릴리스가 `net8` 같은 개별 asset 대신 통합 zip만 제공하면 `--asset net8`은 통합 zip을 fallback으로 선택합니다.
`update download`는 선택된 asset을 지정 폴더에 저장하고, 이미 파일이 있으면 `--force` 없이는 중단합니다.
`update apply`는 기본적으로 dry-run이며, 실제 적용을 시작하려면 `--execute --updater <path>`가 필요합니다.
기존 `WzComparerR2.Updater`는 Windows GUI 앱 배포 폴더 기준으로 동작하므로 CLI는 직접 파일을 교체하지 않고 외부 updater 실행 계약만 제공합니다.

`config` 명령은 GUI의 `Setting.config`와 분리된 CLI 전용 JSON 파일을 읽고 씁니다.
기본 위치는 Windows에서 `%APPDATA%/WzComparerR2/wcr2.config.json`, Unix 계열에서 `$XDG_CONFIG_HOME/wzcomparerr2/wcr2.config.json` 또는 `~/.config/wzcomparerr2/wcr2.config.json`입니다.
`--config <path>`나 `WCR2_CLI_CONFIG` 환경 변수로 위치를 바꿀 수 있습니다.
`default-wz` 또는 `wz` 값을 저장해두면 단일 WZ 입력을 받는 명령에서 입력 경로를 생략했을 때 fallback으로 사용합니다.
`--profile <name>`을 사용하면 값이 `profiles.<name>.<key>` 아래 저장되며, 같은 명령에서 `--profile`을 지정했을 때 profile 값이 전역 `default-wz`/`wz`보다 먼저 사용됩니다.
현재 config 값은 CLI 작업 자동화용 key/value 저장소이며, 기존 GUI 설정 화면과 직접 동기화하지 않습니다.

`plugin` 명령은 CLI 전용 확장 DLL을 찾고 실행합니다.
기본 discovery 경로는 `--plugin-dir`, config key `plugin-dir`, 환경 변수 `WCR2_CLI_PLUGIN_DIR`, 실행 파일 옆 `CliPlugin/`, 현재 작업 디렉터리의 `CliPlugin/` 순서입니다.
기존 GUI 플러그인의 `Plugin/` 폴더는 WinForms `PluginEntry` 중심이라 기본 스캔에서 제외되며, 확인이 필요할 때만 `--include-gui-plugin-dir`로 읽습니다.
CLI 플러그인은 `WzComparerR2.Cli.ICliCommandProvider`를 구현하고 public parameterless constructor를 제공해야 합니다.
`plugin list`와 `plugin commands`는 깨진 DLL이 있어도 전체 CLI를 중단하지 않고 해당 assembly의 `Error`만 결과에 포함합니다.
`plugin run --json`은 provider가 쓴 stdout/stderr를 JSON의 `Stdout`/`Stderr` 필드에 캡처합니다.
`plugin run`에서 `--plugin-dir`, `--config`, `--json`은 CLI host 옵션으로 소비되며, 플러그인에 `--foo` 같은 자체 옵션을 그대로 넘기려면 `plugin run my-command -- --foo value`처럼 `--` 뒤에 둡니다.

`search --path`는 검색을 시작할 WZ 노드를 고르고, `search --match-path`는 결과 경로를 glob 패턴으로 필터링합니다.
정규식을 쓰려면 `--regex`를 같이 지정하세요.

`compare`는 현재 노드 타입과 값 메타데이터 기준으로 `added`, `removed`, `changed` 차이를 출력합니다.
이미지 픽셀 단위 비교는 아직 포함하지 않으며, `--ignore-image-binary`는 이후 픽셀 비교 옵션이 추가될 때도 현재 동작을 유지하기 위한 명시 옵션입니다.

`patch inspect`는 패치 파일을 읽기 전용으로 열어 part 목록, 타입, checksum, notice 정보를 출력합니다.
`patch dry-run`은 target 폴더를 기준으로 create/rebuild/delete 예정 작업과 기존 파일 checksum 상태를 점검합니다.
`patch apply`는 원본 target을 직접 수정하지 않고 target 폴더를 `--out`으로 복사한 다음 복사본에만 패치를 적용합니다.

## Exit Codes

- `0`: success
- `1`: usage error
- `2`: input file or directory not found
- `3`: WZ load failed
- `5`: unexpected error

## VS Code Notes

CLI 프로젝트는 `WzComparerR2.WzLib`의 `net8.0` 타깃을 명시적으로 참조합니다.
VS Code에서 `Program.cs`에 `System` 또는 `WzComparerR2.WzLib` 관련 빨간줄이 많이 보이면 아래 명령으로 restore/build 정보를 갱신한 뒤 C# language server를 재시작하세요.

```bash
dotnet restore WzComparerR2.Cli/WzComparerR2.Cli.csproj --ignore-failed-sources
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug --no-restore
```
