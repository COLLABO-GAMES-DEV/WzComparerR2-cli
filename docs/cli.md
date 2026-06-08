# WzComparerR2 CLI

`WzComparerR2.Cli`는 `WzComparerR2.WzLib`를 재사용하는 콘솔 도구입니다.
실행 파일 이름은 `wcr2`입니다.

## Build

```bash
dotnet restore WzComparerR2.Cli/WzComparerR2.Cli.csproj --ignore-failed-sources
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug --no-restore
```

현재 CLI는 `net8.0`을 대상으로 합니다. 로컬에 .NET 8 런타임이 없고 더 높은 런타임만 있을 때는 다음처럼 실행할 수 있습니다.

```bash
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll --help
```

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
wcr2 skill info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]
wcr2 skill full <skill-wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--level <n|max>] [--format json|xml|text] [--out <path>]
wcr2 item info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]
wcr2 gear info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]
wcr2 map info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]
wcr2 map objects <map-wz-file-or-dir> --id <map-id> [--json]
wcr2 map portals <map-wz-file-or-dir> --id <map-id> [--json]
wcr2 map life <map-wz-file-or-dir> --id <map-id> [--json]
wcr2 map reactors <map-wz-file-or-dir> --id <map-id> [--json]
wcr2 animate frames <wz-file-or-dir> --path <wz-path> --out <dir> [--json]
wcr2 animate gif <wz-file-or-dir> --path <wz-path> --out <file.gif> [--background transparent|#RRGGBB] [--min-alpha <0-255>] [--json]
wcr2 avatar inspect --code <code> [--json]
wcr2 avatar unpack --code <code>
wcr2 lua run <script.lua> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]
wcr2 network server-info [--host <host>] [--port <port>] [--connect] [--json]
wcr2 network chat [--host <host>] [--port <port>] [--json]
wcr2 network send --message <text> [--host <host>] [--port <port>] [--json]
wcr2 update check [--asset net8|net10|net6|net462|zip] [--json]
wcr2 update download --out <dir> [--asset net8|net10|net6|net462|zip] [--force] [--json]
wcr2 update apply [--asset net8|net10|net6|net462|zip] [--updater <path>] [--download <zip>] [--execute] [--json]
wcr2 config path [--config <path>] [--json]
wcr2 config list [--config <path>] [--json]
wcr2 config get <key> [--config <path>] [--json]
wcr2 config set <key> <value> [--config <path>] [--json]
wcr2 config unset <key> [--config <path>] [--json]
wcr2 plugin list [--plugin-dir <dir>] [--include-gui-plugin-dir] [--json]
wcr2 plugin inspect <assembly.dll> [--json]
wcr2 plugin commands [--plugin-dir <dir>] [--json]
wcr2 plugin run <command> [args...] [--plugin-dir <dir>]
wcr2 patch inspect <patch-file> [--json]
wcr2 patch dry-run <patch-file> --target <dir> [--json]
wcr2 patch apply <patch-file> --target <dir> --out <dir> [--log <file>] [--json]
```

공통 옵션:

```bash
--use-base-wz
--fallback <path>
--extract-images
--json
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
wcr2 skill info Skill.wz --id 1001004 --string-wz String.wz --json
wcr2 skill full Data/Skill --id 11001025 --string-wz Data/String --format json --out out/skill-11001025.json
wcr2 skill full Data/Skill --id 1001004 --string-wz Data/String --allow-string-only --format xml --out out/power-strike.xml
wcr2 item info Item.wz --id 2000000 --string-wz String.wz
wcr2 gear info Character.wz --id 1002140 --string-wz String.wz --json
wcr2 map info Map.wz --id 100000000 --string-wz String.wz
wcr2 map portals Map.wz --id 100000000 --json
wcr2 map objects Map.wz --id 100000000 --json
wcr2 animate frames Mob.wz --path 0100100.img/stand --out out/stand --json
wcr2 animate gif Mob.wz --path 0100100.img/stand --out out/stand.gif --json
wcr2 avatar inspect --code "1002140,1040036,1060026"
wcr2 avatar unpack --code "1002140,1040036,1060026" --json
wcr2 lua run WzComparerR2.LuaConsole/Examples/DumpXml.lua --dry-run --json
wcr2 network server-info --json
wcr2 network server-info --connect --timeout 5
wcr2 network send --message "hello" --json
wcr2 update check --asset net8 --json
wcr2 update download --asset net8 --out downloads
wcr2 update apply --asset net8 --json
wcr2 config path
wcr2 config set default-wz /path/to/Base.wz
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

`--json` uses indented JSON with stable, PascalCase property names from the command result DTOs.
For automation, prefer checking high-level fields rather than relying on text output.

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

Real Maple clients can use a split `Data` layout.
If `String.wz`, `Map.wz`, `Skill.wz`, or similar root files load but do not contain the expected path/id, try the data-bearing folder or shard instead, for example `Data\String`, `Data\Skill`, `Data\Character\Cap`, `Data\Map\Map\Map1\Map1_000.wz`, or `Data\Mob_Canvas`.

`extract`는 PNG, sound, raw data, video blob, scalar 값을 자동으로 파일로 내보냅니다.
컨테이너 노드를 선택하면 `--recursive`가 필요합니다.

`skill/item/gear/map info`는 먼저 데이터 WZ에서 id 노드를 찾고, `--string-wz`가 있으면 String.wz의 이름/설명 값을 추가합니다.
현재는 텍스트/JSON 메타데이터 조회가 중심이며, tooltip image 렌더링은 아직 포함하지 않습니다.

`skill full`은 렌더링 없이 CharaSim 스타일의 headless 스킬 해석 결과를 내보냅니다.
출력에는 `common`, `PVPcommon`, `level`, 요구 스킬, 액션, 플래그, 아이콘 메타데이터, 원문 요약, `resolvedSummary`, 가능한 경우 `nextResolvedSummary`, 미해결 placeholder 진단이 포함됩니다.
`--format json|xml|text`와 `--out <path>`를 지원합니다.
실제 skill node가 없고 `String/Skill.img` 문자열만 있는 ID는 기본적으로 실패하지만, `--allow-string-only`를 주면 `Mode: string-only` 결과로 이름/설명/문자열 속성을 확인할 수 있습니다.
툴팁 PNG 렌더링은 아직 포함하지 않으며 Windows-only 후속 단계로 분리되어 있습니다.

`map objects/portals/life/reactors`는 map `.img` 안의 해당 섹션을 읽어 좌표, id, 이동 대상 같은 scalar property를 JSON으로 내보냅니다.
MonoGame 기반 screenshot 렌더링은 아직 CLI에 포함하지 않습니다.

`animate frames`는 숫자 자식 노드를 프레임으로 보고 각 프레임의 PNG/사운드/스칼라 값을 하위 폴더로 추출한 뒤 `frames.json` manifest를 생성합니다.
`animate gif`는 기존 built-in GIF encoder를 사용해 직접 PNG 프레임이 있는 애니메이션 노드를 GIF로 저장합니다.
UOL이 다른 WZ 파일을 참조하는 복잡한 애니메이션이나 APNG 인코딩은 아직 포함하지 않습니다.
macOS에서는 WZ PNG 추출이 `System.Drawing/GDI+`에서 실패할 수 있으므로 Windows 실클라 검증을 우선하세요.

`avatar inspect`와 `avatar unpack`은 avatar code 안의 item id를 추출하고 MapleStory item id prefix로 장비 슬롯을 추정합니다.
실제 캐릭터 렌더링은 아직 포함하지 않습니다.

`lua run`은 새 의존성을 추가하지 않기 위해 현재 시스템 `PATH`의 `lua`, `lua5.4`, `lua5.3`, `luajit` 실행기를 사용합니다.
`--wz`를 지정하면 WZ 로딩 가능 여부를 먼저 확인하고 `WCR2_WZ_INPUT`, `WCR2_WZ_ROOT` 환경 변수를 전달합니다.
기존 LuaConsole의 `env`/NLua 통합 API는 아직 headless CLI로 이식하지 않았으므로 기존 예제 검증은 `--dry-run`부터 사용하세요.

`network` 명령은 기본적으로 dry-run입니다.
`server-info --connect`는 프로토콜 로그인 없이 TCP 접속 가능 여부만 확인하며, `chat`과 `send`는 아직 GUI 플러그인의 실제 채팅 세션을 대체하지 않습니다.

`update check`는 GitHub latest release API에서 릴리스 정보와 다운로드 URL을 조회합니다.
현재 릴리스가 `net8` 같은 개별 asset 대신 통합 zip만 제공하면 `--asset net8`은 통합 zip을 fallback으로 선택합니다.
`update download`는 선택된 asset을 지정 폴더에 저장하고, 이미 파일이 있으면 `--force` 없이는 중단합니다.
`update apply`는 기본적으로 dry-run이며, 실제 적용을 시작하려면 `--execute --updater <path>`가 필요합니다.
기존 `WzComparerR2.Updater`는 Windows GUI 앱 배포 폴더 기준으로 동작하므로 CLI는 직접 파일을 교체하지 않고 외부 updater 실행 계약만 제공합니다.

`config` 명령은 GUI의 `Setting.config`와 분리된 CLI 전용 JSON 파일을 읽고 씁니다.
기본 위치는 Windows에서 `%APPDATA%/WzComparerR2/wcr2.config.json`, Unix 계열에서 `$XDG_CONFIG_HOME/wzcomparerr2/wcr2.config.json` 또는 `~/.config/wzcomparerr2/wcr2.config.json`입니다.
`--config <path>`나 `WCR2_CLI_CONFIG` 환경 변수로 위치를 바꿀 수 있습니다.
`default-wz` 또는 `wz` 값을 저장해두면 단일 WZ 입력을 받는 명령에서 입력 경로를 생략했을 때 fallback으로 사용합니다.
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
