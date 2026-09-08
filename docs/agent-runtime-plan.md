# WzComparerR2 Agent Runtime Plan

## 목적

현재 `WzComparerR2.Cli`는 에이전트가 WZ 데이터를 headless로 조회/추출하기 위한 인터페이스다. 기능이 늘어나면서 단일 CLI 명령과 문자열 조립만으로는 복합 작업을 안정적으로 처리하기 어려워졌다.

이 계획의 목표는 기존 WzComparerR2 엔진/GUI를 보존하면서, 에이전트가 더 빠르고 재현 가능하게 이미지 검색, 스킬 추출, 아이템/맵 리소스 추출을 실행할 수 있는 agent runtime 계층을 추가하는 것이다.

## 핵심 원칙

- 기존 WzComparerR2 엔진과 GUI는 수정하지 않는다.
- 기본 수정 범위는 새 headless/agent 계층과 기존 `WzComparerR2.Cli` wrapper로 제한한다.
- `WzComparerR2.WzLib`, WinForms GUI, 기존 플러그인/렌더링 프로젝트는 upstream 통합을 위해 가능한 한 건드리지 않는다.
- 기존 CLI 명령은 하위 호환 wrapper로 유지한다.
- 새 인터페이스는 사람이 쓰기 쉬운 CLI보다 에이전트가 구조화된 입력/출력을 다루기 쉬운 형태를 우선한다.
- 모든 복합 작업은 JSON job과 manifest로 재현 가능해야 한다.

## 최종 목표 구조

```text
WzComparerR2.Headless/
  Loading/
  Indexing/
  Search/
  Extraction/
  Skills/
  Items/
  Maps/
  Media/
  Recipes/

WzComparerR2.AgentHost/
  Program.cs
  agent run --job job.json
  agent serve --stdio

WzComparerR2.Cli/
  기존 명령 유지
  Headless 서비스 호출 wrapper로 축소

WzComparerR2.Cli.Tests/
  기존 CLI contract test 유지
  agent job smoke test 추가
```

## 새 인터페이스

### 1. Agent Job

우선 구현할 기본 인터페이스다.

```bash
wcr2-agent run --job .test/jobs/image-search.json --json
```

예시 job:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/agent-runs/20260907-image-search",
  "steps": [
    {
      "id": "find-image",
      "type": "image.search",
      "query": "/path/to/query.png",
      "scope": ["ui", "item", "skill", "effect"],
      "maxResults": 20,
      "trustCache": true,
      "refine": true,
      "exportTopResults": true
    },
    {
      "id": "export-related",
      "type": "image.export-related",
      "fromStep": "find-image",
      "parentDepth": 1
    }
  ]
}
```

예상 출력:

```json
{
  "status": "ok",
  "jobPath": ".test/jobs/image-search.json",
  "outputDir": ".test/agent-runs/20260907-image-search",
  "steps": [
    {
      "id": "find-image",
      "status": "ok",
      "results": [
        {
          "score": 0.848,
          "sourceInputPath": "Data/UI/_Canvas",
          "path": "UIWindowEvent5.img\\2606UltimaStory\\enterUI\\back",
          "parentPath": "UIWindowEvent5.img\\2606UltimaStory\\enterUI",
          "type": "png",
          "width": 772,
          "height": 556,
          "exportedPath": ".test/..."
        }
      ]
    }
  ]
}
```

### 2. Agent Serve

반복 검색/추출이 많을 때 쓸 수 있는 장기 실행 인터페이스다. 현재 구현은 request/response protocol과 lifecycle을 제공하며, 같은 serve process 안에서 스킬 export WZ repository/session, item/map domain repository, WZ context를 request 간 재사용한다.

```bash
wcr2-agent serve --stdio
```

에이전트는 JSON request를 보내고 JSON response를 받는다.

```json
{ "id": "p1", "method": "ping" }
{ "id": "r1", "method": "run", "jobPath": ".test/job.json", "outputDir": ".test/job/out" }
{ "id": "s1", "method": "shutdown" }
```

`run`은 inline `job` object도 받을 수 있다. 응답은 한 줄 compact JSON이고, request의 `id`/`requestId`는 response `id`로 보존된다.

`serve`는 `cache.stats`와 `cache.clear` method를 제공한다. 현재 캐시 범위는 `skill.export`, `skill.export-batch`, `skill.export-xlsx`의 스킬 입력/옵션별 WZ repository/session, `item.icon`/`item.export`의 String/Canvas WZ context, `item.export`/`map.export`의 domain repository이다. binary index cache와 전체 media command source registry는 이후 확장 대상이다.

## 내부 계층

### WzSourceRegistry

역할:

- `Data` 폴더의 split WZ/MS layout 스캔
- `UI/_Canvas`, `Item/*/_Canvas`, `Skill/_Canvas`, `Effect/_Canvas`, `Packs/Skill_*.ms` 같은 물리 입력 후보 정리
- scope alias 관리
- source stamp 및 cache invalidation 정보 제공

### AssetIndex

역할:

- 이미지, 사운드, 비디오, 스킬, 아이템, 맵 리소스의 경로/크기/타입 metadata index 생성
- 이미지 검색용 root별 fingerprint index 생성
- index cache 저장/로드
- agent job 간 index 재사용

### Resolver

역할:

- logical path와 physical input path 연결
- `_outlink`, `_inlink` 해석
- metadata stub과 `_Canvas` 실 pixel source 병합
- split WZ/MS shard에서 실제 리소스 위치 추적

### Extractor

역할:

- PNG 추출
- MCV/video frame 추출
- sound 추출
- XML/JSON/raw dump
- 결과 파일 hash/bytes/metadata manifest 생성

### RecipeRunner

역할:

- job JSON 파싱
- step 실행 순서 관리
- step 간 결과 참조
- 부분 실패/계속 실행 정책
- 최종 manifest 생성

## Phase Plan

### Phase 1. 프로젝트 뼈대

- [x] `WzComparerR2.Headless` class library 추가
- [x] `WzComparerR2.AgentHost` exe 추가
- [x] solution에 두 프로젝트 등록
- [x] 기존 `WzComparerR2.Cli`는 그대로 동작하게 유지
- [x] 최소 `agent run --job` 명령 추가
- [x] 빈 job / 알 수 없는 step / 잘못된 JSON validation 추가

현재 지원 범위:

- `wcr2-agent run --job <job.json> [--out <dir>] [--json]`
- 빈 job은 성공 처리한다.
- `noop` step은 성공 처리한다.
- 알 수 없는 step type, 누락된 step id/type, 중복 step id, 잘못된 JSON은 실패 JSON으로 보고한다.
- `outputDir` 또는 `--out`이 있으면 `agent-result.json` manifest를 남긴다.

검증:

```bash
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Release --no-restore
dotnet build WzComparerR2.AgentHost/WzComparerR2.AgentHost.csproj -c Release --no-restore
dotnet build WzComparerR2.Cli.Tests/WzComparerR2.Cli.Tests.csproj -c Release --no-restore
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli.Tests/bin/Release/net8.0/wcr2-tests.dll --cli WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll --agent WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll
```

### Phase 2. Image Search 서비스 분리

- [x] 현재 `WzComparerR2.Cli/Media/ImageSearch*.cs`를 Headless 서비스로 이동
- [x] CLI `image search`는 Headless API 호출 wrapper로 축소
- [x] `image.search` job step 추가
- [x] root별 image index cache 유지
- [x] query region matching, candidate pool, top-N refine 유지
- [x] `ParentPath`, `QueryRegion`, `Refined`, `SourceInputPath` manifest 유지

현재 지원 범위:

- `image.search` step은 `dataDir` + `scope` 또는 `input`을 받아 검색한다.
- `scope`는 문자열 또는 문자열 배열을 허용한다.
- `exportTopResults: true`이면 step output 폴더에 상위 PNG와 `image-search-result.json`을 남긴다.
- 상대 경로는 현재 작업 디렉터리 기준으로 해석한다.
- CLI `wcr2 image search`와 AgentHost `image.search`는 같은 Headless 구현을 사용한다.

검증:

```bash
wcr2-agent run --job .test/jobs/image-search-ui.json --json
wcr2 image search --data-dir Data --scope ui --query query.png --json
```

두 결과의 상위 후보가 동일해야 한다.

2026-09-07 검증:

- AgentHost job과 CLI wrapper 모두 `1788783245097-0nzfjm.png` query로 `UIWindowEvent.img\sundayMaple2\backgrnd`를 1순위로 찾았다.
- 점수는 `0.997541`, `--scope ui`, cache hit + refine 기준이다.

### Phase 3. Related Export

- [x] `image.export-related` job step 추가
- [x] `fromStep` 결과의 `ParentPath` 기준 sibling image export
- [x] parent depth 옵션 지원
- [x] 합성 UI 화면 대응을 위해 관련 group manifest 생성

현재 지원 범위:

- `fromStep`으로 이전 `image.search` step을 참조한다.
- `parentDepth`, `sourceLimit`, `maxFiles`를 지원한다.
- step output 폴더에 그룹 PNG와 `related-images-result.json`을 남긴다.

예시:

```json
{
  "type": "image.export-related",
  "fromStep": "find-image",
  "parentDepth": 1,
  "maxFiles": 100
}
```

2026-09-07 검증:

- `UIWindowEvent.img\sundayMaple2\backgrnd` 검색 결과에서 `parentDepth: 1`로 `UIWindowEvent.img\sundayMaple2` 그룹을 추출했다.
- close 버튼 상태, `backgrnd`, `icon` 총 6개 PNG가 export됐다.

### Phase 4. Skill Export Recipe

- [x] `skill.export` job step 추가
- [x] `skill.export-batch` job step 추가
- [x] Phase 4A: 기존 `wcr2 skill export/export-batch`를 호출하는 CLI bridge 연결
- [x] Phase 4B: 기존 skill sprite/export logic을 Headless 서비스로 직접 이동
- [x] origin/delay/lt/rb/z/source metadata manifest 유지
- [x] screen/video/sound export 유지
- [x] related cross-root effect export 유지 검증
- [x] xlsx 기반 batch recipe는 별도 step으로 분리

현재 `skill.export`와 `skill.export-batch`는 Headless로 옮긴 기존 스킬 추출 구현을 프로세스 생성 없이 직접 호출한다. agent job은 CLI 호환 옵션 이름을 유지하되 실행은 `SkillSpriteExporter`/`SkillBatchExporter` in-process 호출로 처리한다. 기존 CLI 추출 결과와 문서화된 `skill-info.json`/`resources.json`/batch `manifest.json` 형식은 그대로 유지한다.

2026-09-07 검증:

- `3141000 폭풍의 시 VI` direct agent 단건/batch export가 각각 32개 파일을 추출했다.
- `resources.json`의 `prepare`, `keydown`, `keydownend` frame metadata에 `Origin`과 `Delay`가 유지됐다.
- `5241503 파이어크래커` direct agent export가 image 150개, sound 8개, video frame 356개를 추출했다.
- `3141000 폭풍의 시 VI` direct agent related 검증에서 `Data/Effect/_Canvas/_Canvas_001.wz`와 `relatedKey=nodepoint`를 명시해 related PNG 3개를 추출했다.
- 같은 결과의 `agent-result.json`에는 `cliPath`가 없고 `command: ["skill","export",...]`만 남아, CLI bridge 없이 in-process 경로가 사용된 것을 확인했다.

단건 예시:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/agent-runs/skills",
  "steps": [
    {
      "id": "firecracker",
      "type": "skill.export",
      "skillId": "5241503",
      "videoFormat": "png"
    }
  ]
}
```

배치 예시:

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

xlsx 배치 예시:

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/agent-runs/xlsx-skills",
  "steps": [
    {
      "id": "xlsx-skills",
      "type": "skill.export-xlsx",
      "xlsx": "skills.xlsx",
      "sheet": "skills",
      "outRoot": ".test/agent-runs/xlsx-skills/exports",
      "branch": "auto",
      "videoFormat": "png",
      "continueOnError": true
    }
  ]
}
```

`skill.export-xlsx`는 `.xlsx`를 읽어 중간 `names.tsv` recipe로 변환한 뒤 기존 `skill.export-batch` Headless 경로를 호출한다. 기본 출력 pattern은 `{jobCode}_{jobName}/{id}_{name}`이다. 자동 header 감지는 `직업`, `직업 코드`, `스킬`/`skill` 계열 header를 사용하며, 표 구조가 다르면 `jobNameColumn`, `jobCodeColumn`, `skillColumns`, `headerRow`, `firstDataRow`를 명시한다.

2026-09-07 검증:

- 최소 xlsx fixture `skills` sheet에서 `보우마스터`, `314`, `폭풍의 시 VI`를 읽어 request 1개를 생성했다.
- direct agent `skill.export-xlsx`가 `314_보우마스터/3141000_폭풍의 시 VI` 폴더에 icon PNG와 sound 4개를 추출했다.
- 검증 산출물은 `.test/wcr2-agent-xlsx-skill-export-20260907/rerun4`이다.

### Phase 5. Item/Map Recipe

- [x] `item.icon` job step 추가
- [x] `item.export` job step 추가
- [x] `map.export` job step 추가
- [x] 기존 CLI는 Headless wrapper로 유지

`item.icon`은 현재 CLI의 item icon 추출기를 Headless로 이동한 뒤 agent에서 직접 호출한다. `item.export`는 `item-info.json`, `item-icon-result.json`, `agent-item-export-result.json`을 한 output folder에 저장한다. `map.export`는 렌더링이 아니라 metadata export이며 `map-info.json`, `map-metadata.json`, `agent-map-export-result.json`을 저장한다.

```json
{
  "dataDir": "/path/to/Maple/Data",
  "outputDir": ".test/agent-runs/item-map",
  "steps": [
    { "id": "cube-icon", "type": "item.icon", "name": "미라클 큐브", "category": "cash" },
    { "id": "red-potion", "type": "item.export", "itemId": "2000000" },
    { "id": "henesys", "type": "map.export", "mapId": "100000000" }
  ]
}
```

주의: agent step의 `id`는 step identifier이므로 item/map id는 `itemId`/`mapId`를 우선 사용한다. step `id`가 숫자로만 되어 있고 selector가 없으면 fallback으로 item/map id처럼 해석한다.

2026-09-07 검증:

- `.test/wcr2-agent-item-map-20260907`: `item.icon`이 `미라클 큐브` `5062000` icon PNG 1개를 추출했다.
- `item.export`가 `2000000` `빨간 포션`의 `item-info.json`과 icon PNG 1개를 추출했다.
- `map.export`가 `100000000` `헤네시스`에서 portals 37개, life 35개, objects/tiles 1603개, reactors 0개를 기록했다.

### Phase 6. Agent Serve

- [x] `wcr2-agent serve --stdio` 추가
- [x] line-delimited JSON request/response 프로토콜 정의
- [x] skill export session-level WZ repository/cache 재사용
- [x] item/map domain repository 및 WZ context cache 재사용
- [ ] media/search/general source registry cache 재사용
- [x] graceful shutdown 지원
- [x] request id 기반 응답 보장

`serve --stdio`는 stdin/stdout에서 newline-delimited JSON을 사용한다. 지원 method는 `ping`, `run`, `cache.stats`, `cache.clear`, `shutdown`이며, request의 `id` 또는 `requestId`는 response `id`로 그대로 반환한다. `run`은 기존 `AgentJobRunner`를 호출하고 `jobPath` 또는 inline `job` object를 받을 수 있다. 응답은 한 줄 compact JSON으로 출력한다.

예시:

```json
{ "id": "p1", "method": "ping" }
{ "id": "r1", "method": "run", "jobPath": "job.json", "outputDir": ".test/agent-runs/job1" }
{ "id": "c1", "method": "cache.stats" }
{ "id": "c2", "method": "cache.clear" }
{ "id": "s1", "method": "shutdown" }
```

같은 serve process 안에서 `skill.export`, `skill.export-batch`, `skill.export-xlsx`는 동일한 스킬 입력/옵션 조합일 때 WZ repository/session을 재사용한다. `item.icon`, `item.export`, `map.export`는 domain repository와 WZ context cache를 재사용한다. `run` result의 `cacheStats`, step의 `cacheStatus`, `cache.stats` response로 hit/miss와 현재 세션 수를 확인한다. 캐시 크기는 현재 skill session 4개, domain repository 8개, WZ context 16개이며, 초과 시 least-recently-used 항목을 dispose한다.

### Phase 7. MCP wrapper

`wcr2-agent mcp --stdio`는 MCP client가 AgentHost를 직접 tool server처럼 호출할 수 있게 하는 stdio JSON-RPC wrapper다. 별도 MCP SDK/package 의존성을 추가하지 않고, `AgentJobRunner`를 같은 process 안에서 유지해 `serve --stdio`와 동일한 캐시 재사용 효과를 얻는다.

지원 request:

```text
server/discover
initialize
ping
tools/list
tools/call
resources/list
resources/templates/list
prompts/list
```

노출 tool:

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

완료 기준:

- [x] `wcr2-agent mcp --stdio` command 추가
- [x] `server/discover`, legacy `initialize`, `tools/list`, `tools/call` 처리
- [x] `wcr2.run_job`으로 inline job 실행
- [x] 스킬/아이템/맵/이미지 검색 대표 agent step을 MCP tool로 노출
- [x] `cache.stats`/`cache.clear`를 MCP tool로 노출
- [x] `WzComparerR2.Cli.Tests`: MCP stdio discover/tools/run smoke 추가
- [x] `docs/agent-quickstart.md`, `docs/cli.md`에 연결 예시 추가

## Upstream 통합 전략

원본 WzComparerR2에서 새 기능을 받아오기 쉽게 하기 위해, 변경 경계를 명확히 유지한다.

허용 범위:

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

기본 금지 범위:

```text
WzComparerR2.WzLib/**
WzComparerR2/**
WzComparerR2.PluginBase/**
WzComparerR2.MapRender/**
기존 GUI/렌더링/플러그인 프로젝트
```

Upstream sync 기본 절차:

```bash
git status --short --branch
git remote -v
git fetch upstream
git rebase upstream/<원본브랜치>
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Release --no-restore
dotnet build WzComparerR2.AgentHost/WzComparerR2.AgentHost.csproj -c Release --no-restore
dotnet build WzComparerR2.Cli.Tests/WzComparerR2.Cli.Tests.csproj -c Release --no-restore
DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli.Tests/bin/Release/net8.0/wcr2-tests.dll --cli WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll --agent WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll
```

주의:

- `WzComparerR2.WzLib` 충돌이 발생하면 먼저 우리가 엔진 경계를 침범했는지 확인한다.
- 가능한 경우 Headless/CLI 계층에서 adapter를 추가해 해결한다.
- 엔진 수정이 필요하면 별도 승인과 영향 범위 정리가 필요하다.

## 성공 기준

- 기존 CLI 명령이 깨지지 않는다.
- agent job 하나로 이미지 검색부터 관련 리소스 추출까지 재현 가능하다.
- 결과는 항상 JSON manifest로 남는다.
- 실제 Maple `Data` split layout에서 동작한다.
- upstream rebase 시 충돌 범위가 새 headless/agent/cli 계층 안에 머문다.
- WzLib/GUI 변경 없이 기능 확장이 가능하다.

## 보류 항목

- 완전한 binary cache 포맷
- full Data global image index builder
- media/search/general stdio server source registry cache
- Web/UI 인터페이스
- WzLib 내부 구조 변경

이 항목들은 `agent run --job`이 안정화된 뒤 필요성에 따라 진행한다.
