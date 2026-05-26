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
wcr2 extract <file-or-dir> --path <wz-path> --out <output-dir> [--recursive] [--json]
```

공통 옵션:

```bash
--use-base-wz
--fallback <path>
--extract-images
--json
--format xml
```

## Examples

```bash
wcr2 info Base.wz
wcr2 tree Base.wz --depth 2
wcr2 list Base.wz --path Character
wcr2 search String.wz --name Maple --json
wcr2 extract Base.wz --path String --out out/string --recursive
wcr2 extract Base.wz --path String --out out/string.xml --format xml
```

`extract`는 PNG, sound, raw data, video blob, scalar 값을 자동으로 파일로 내보냅니다.
컨테이너 노드를 선택하면 `--recursive`가 필요합니다.

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
