# WzComparerR2 Agent Quickstart

이 문서는 새 에이전트가 이 저장소에서 바로 추출/검증/개발을 이어가기 위한 최소 절차다. 재현 가능한 명령, 산출물 위치, 판단 기준을 우선한다.

## 1. 작업 경계

기본 수정 허용 범위:

```text
WzComparerR2.Headless/**
WzComparerR2.AgentHost/**
WzComparerR2.Cli/**
WzComparerR2.Cli.Tests/**
docs/**
todo.md
research.md
AGENTS.md
CLAUDE.md
```

기본 수정 금지 범위:

```text
WzComparerR2.WzLib/**
WzComparerR2/**
WzComparerR2.PluginBase/**
WzComparerR2.MapRender/**
기존 GUI/렌더링/플러그인 프로젝트
```

금지 범위를 건드리기 전에 `Headless`/`AgentHost`/`Cli` 조합으로 우회 가능한지 먼저 확인한다. 그래도 필요하면 이유, 영향 범위, 검증 방법을 정리하고 사용자 승인을 받는다.

## 2. 실행 파일

단발 조회나 CLI 호환성 확인은 `wcr2`를 쓴다.

```bash
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll --help
```

에이전트 자동화는 `wcr2-agent`를 우선 쓴다.

```bash
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll run --job .test/job.json --json
```

macOS에서 .NET 8 런타임이 없고 더 높은 런타임만 있으면 `DOTNET_ROLL_FORWARD=Major`를 붙인다.

## 3. Data 경로

Windows 기본:

```text
C:\Nexon\Maple\Data
```

macOS/CrossOver 예시:

```text
/Users/ijun17/Library/Application Support/MapleStory/Bottles/maplestory/drive_c/Nexon/Maple/Data
```

명령에는 가능하면 root WZ가 아니라 `--data-dir <Data>`를 넘긴다. 최신 Maple 클라이언트는 split WZ/MS layout이라 `Data/Skill`, `Data/Packs/Skill_*.ms`, `Data/Skill/_Canvas`, `Data/Sound`를 함께 봐야 하는 경우가 많다.

## 4. 빌드와 검증

코드 변경 후 기본 검증:

```bash
DOTNET_ROLL_FORWARD=Major dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
DOTNET_ROLL_FORWARD=Major dotnet build WzComparerR2.AgentHost/WzComparerR2.AgentHost.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
DOTNET_ROLL_FORWARD=Major dotnet build WzComparerR2.Cli.Tests/WzComparerR2.Cli.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
```

테스트:

```bash
DATA_DIR="/path/to/Maple/Data"
WCR2_TEST_DATA_DIR="$DATA_DIR" DOTNET_ROLL_FORWARD=Major dotnet \
  WzComparerR2.Cli.Tests/bin/Release/net8.0/wcr2-tests.dll \
  --cli WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll \
  --agent WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll
```

문서만 바꾼 경우 빌드는 생략 가능하다. 최종 보고에 문서-only라 빌드하지 않았다고 명시한다.

## 5. Agent job 기본형

`.test/<purpose>-<yyyymmdd>/job.json` 형태로 job을 만든다. `.test`는 검증 산출물용이며 보통 git에 포함하지 않는다.

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/agent-run/out",
  "steps": [
    {
      "id": "probe",
      "type": "noop"
    }
  ]
}
```

실행:

```bash
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll run --job .test/agent-run/job.json --json
```

항상 `agent-result.json`과 step별 sidecar를 확인한다.

## 6. 스킬 추출

스킬명이 주어졌으면 먼저 검색한다.

```bash
wcr2 skill search-name --data-dir Data --name "파이어크래커" --json
wcr2 skill resolve-name --data-dir Data --name "파이어크래커" --job-code 524 --json
```

단건 agent job:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/skill-export/out",
  "steps": [
    {
      "id": "firecracker",
      "type": "skill.export",
      "skillId": "5241503",
      "branch": "auto",
      "videoFormat": "png"
    }
  ]
}
```

엑셀 기반 batch:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/xlsx-skills/out",
  "steps": [
    {
      "id": "xlsx-skills",
      "type": "skill.export-xlsx",
      "xlsx": "skills.xlsx",
      "sheet": "skills",
      "outRoot": ".test/xlsx-skills/out/exports",
      "branch": "auto",
      "videoFormat": "png",
      "continueOnError": true,
      "skipExisting": true
    }
  ]
}
```

`skill.export-xlsx`는 기본적으로 `직업`, `직업 코드`, `스킬`/`skill` header를 찾고, 출력 폴더는 `{jobCode}_{jobName}/{id}_{name}` pattern을 쓴다. 표 구조가 다르면 `jobNameColumn`, `jobCodeColumn`, `skillColumns`, `headerRow`, `firstDataRow`를 명시한다.

스킬 결과에서 확인할 파일:

```text
skill-info.json
resources.json
export-result.json
manifest.json
```

`resources.json`의 `Origin`, `Delay`, `Z`, `OutlinkPath`, `ResolvedInputPath`, `MetadataSourcePath`를 확인해야 MSW/MGH 쪽 animation import 문제를 판단할 수 있다.

## 7. 이미지 검색

경로를 모르는 PNG가 있으면 image search를 먼저 쓴다.

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/image-search/out",
  "steps": [
    {
      "id": "find-image",
      "type": "image.search",
      "query": "/path/to/query.png",
      "scope": ["ui", "item", "skill", "effect"],
      "maxResults": 20,
      "refine": true,
      "trimBackground": false,
      "backgroundTolerance": 24,
      "includeVideo": false,
      "maxVideoFrames": 0,
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

검색 cache 기본 위치:

```text
macOS: ~/Library/Caches/wcr2/image-search
Windows: %LOCALAPPDATA%\wcr2\image-search
Linux: ${XDG_CACHE_HOME:-~/.cache}/wcr2/image-search
```

cache는 호환용 `.json.gz`와 속도용 `.bin.gz` sidecar를 같은 디렉터리에 둔다. 기존 JSON cache만 있어도 첫 검색에서 binary sidecar를 만든다. sidecar는 파생 파일이므로 삭제해도 되고, `--rebuild-cache`/`rebuildCache`는 JSON과 sidecar를 다시 만든다.

MCV/Wz_Video 내부 프레임도 검색 대상에 넣어야 하면 `"includeVideo": true`를 켠다. query는 여전히 PNG 파일이어야 하며, 이 옵션은 `Data/Packs/Skill*.ms` 안의 video frame을 디코드해 image index에 포함한다. 결과는 `Type: "video-frame"`, `VideoPath`, `FrameIndex`, `FrameCount`, `FrameDelayMs`, `FrameStartMs`를 가진다. `maxVideoFrames`는 비디오당 앞 n프레임만 디코드하며, `0`은 전체 프레임이다. `ffmpeg`가 PATH에 없으면 `"ffmpeg": "/path/to/ffmpeg"`를 지정한다.

검색이 느리면 `--scope` 또는 job `scope`를 좁힌다. 확대/축소된 reference 이미지라면 `noSizePrefilter`를 고려한다. 배경이 붙은 인터넷 reference 이미지라면 `"trimBackground": true`를 사용하고, 경계가 과하게 잘리거나 덜 잘리면 `"backgroundTolerance": 16..48` 범위에서 조정한다. 빠른 1차 후보 탐색만 필요하면 job에 `"probe": true`를 넣는다. `probe`는 cache를 신뢰하고 pixel refine를 생략하므로 최종 확정에는 `"refine": true`인 기본 검색을 다시 실행한다.

cache hit 검색은 cache v3 파일을 그대로 쓰고, 실행 중 메모리에서 width/height/aspect/alpha bucket index를 만든다. manifest의 `BucketPrefilteredImageCount`, `ScoringBucketCount`, `CoarsePrefilteredImageCount`를 보면 size/shape bucket pruning이 얼마나 적용됐는지 확인할 수 있다.

반복 검색은 `wcr2-agent serve --stdio` 또는 MCP 서버에서 실행하는 편이 더 빠르다. agent 프로세스가 디스크 image search cache에서 읽은 root index를 메모리에 유지하므로, 같은 scope/root의 두 번째 검색부터 JSON gzip cache를 다시 역직렬화하는 비용을 줄인다. 현재 상태는 `cache.stats`/`wcr2.cache_stats`의 `imageSearchIndexCount`, `imageSearchIndexHits`, `imageSearchIndexMisses`, `imageSearchIndexes`에서 확인한다.

## 8. 아이템과 맵

아이템 icon:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/item-icon/out",
  "steps": [
    {
      "id": "miracle-cube",
      "type": "item.icon",
      "name": "미라클 큐브",
      "category": "cash"
    }
  ]
}
```

아이템 정보 + icon:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/item-export/out",
  "steps": [
    {
      "id": "red-potion",
      "type": "item.export",
      "itemId": "2000000"
    }
  ]
}
```

맵 metadata:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/map-export/out",
  "steps": [
    {
      "id": "henesys",
      "type": "map.export",
      "mapId": "100000000"
    }
  ]
}
```

`map.export`는 현재 실제 렌더 이미지가 아니다. `map-info.json`과 `map-metadata.json`에 portals/life/objects/reactors를 기록한다.

## 9. Agent serve

`serve --stdio`는 한 프로세스를 유지하며 newline-delimited JSON request/response를 처리한다.

```bash
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll serve --stdio
```

지원 method:

```json
{ "id": "p1", "method": "ping" }
{ "id": "r1", "method": "run", "jobPath": ".test/job.json", "outputDir": ".test/job/out" }
{ "id": "c1", "method": "cache.stats" }
{ "id": "c2", "method": "cache.clear" }
{ "id": "s1", "method": "shutdown" }
```

`run`은 inline `job` object도 받을 수 있다.

```json
{ "id": "r2", "method": "run", "job": { "steps": [{ "id": "probe", "type": "noop" }] } }
```

같은 serve process 안에서는 `skill.export`, `skill.export-batch`, `skill.export-xlsx`가 동일한 스킬 입력/옵션 조합일 때 WZ repository/session을 재사용한다. `item.icon`, `item.export`, `map.export`도 domain repository 및 WZ context cache를 재사용한다. `run` 응답의 `result.cacheStats`로 현재 세션 수와 hit/miss를 확인하고, cache를 쓰는 step의 `cacheStatus`로 해당 step이 `cache-hit`, `cache-miss`, `cache-mixed`인지 확인한다. 캐시를 비우려면 `cache.clear`를 보낸다.

## 10. MCP wrapper

MCP를 지원하는 에이전트 호스트에서는 `wcr2-agent mcp --stdio`를 연결한다. 이 wrapper는 MCP JSON-RPC 요청을 받아 내부 `AgentJobRunner`로 전달한다.

```bash
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll mcp --stdio
```

MCP client 설정 예시:

```json
{
  "mcpServers": {
    "wcr2": {
      "command": "dotnet",
      "args": [
        "/Users/ijun17/Desktop/MSW/WzComparerR2/WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll",
        "mcp",
        "--stdio"
      ],
      "env": {
        "DOTNET_ROLL_FORWARD": "Major"
      }
    }
  }
}
```

대표 tool:

```text
wcr2.run_job
wcr2.skill_export
wcr2.skill_export_batch
wcr2.skill_export_xlsx
wcr2.item_icon
wcr2.item_export
wcr2.map_export
wcr2.image_search
wcr2.cache_stats
wcr2.cache_clear
```

경로를 모르면 `wcr2.image_search`로 후보를 찾고, 스킬/아이템/맵이 확정되어 있으면 전용 tool을 쓴다. 복합 workflow나 엑셀 기반 대량 추출은 `wcr2.run_job`에 기존 agent job을 inline 또는 `jobPath`로 넘긴다.

## 11. 흔한 판단 기준

- `skill-info.json` 이름이 깨져 보이면 먼저 viewer/editor encoding을 확인한다. JSON 자체는 UTF-8로 저장된다.
- `resources.json`의 `Origin`/`Delay`가 null이면 exporter 누락인지 원본 metadata 부재인지 `Data/Packs/Skill_*.ms`와 `_Canvas` resolved path를 같이 확인한다.
- 스킬이 안 잡히면 삭제된 스킬로 단정하지 말고 `skill search-name`, `skill resolve-name`, job code, 파생 skill id, pack metadata 후보를 확인한다.
- `map portals/life/objects/reactors`가 비어 있으면 `_Canvas/<mapId>.img` preview를 잡은 것이 아닌지 확인한다. 현재 map finder는 populated map node를 우선한다.
- macOS에서 일부 GIF/APNG/System.Drawing 경로는 제한될 수 있다. PNG 추출은 cross-platform writer가 지원하는 texture format이면 동작한다.
- patch/lua/network 실제 검증은 fixture 또는 외부 runtime/target 서버가 필요하다. dry-run 통과를 실제 handshake 통과로 보고하지 않는다.

## 12. 커밋 전 체크

```bash
git diff --check
git status --short --branch
```

코드 변경이면 build/test 결과를 최종 보고에 포함한다. 문서-only 변경이면 빌드 생략 여부를 명시한다.
