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

반복 검색/추출이 많아진 뒤 추가할 장기 실행 인터페이스다.

```bash
wcr2-agent serve --stdio
```

에이전트는 JSON request를 보내고 JSON response를 받는다.

```json
{
  "id": "req-1",
  "method": "image.search",
  "params": {
    "dataDir": "/path/to/Maple/Data",
    "query": "/path/to/query.png",
    "scope": ["ui"],
    "maxResults": 10
  }
}
```

`serve`는 WZ context, source registry, index cache를 프로세스 안에서 재사용할 수 있어 대량 작업에 유리하다. 단, lifecycle과 디버깅 복잡도가 늘어나므로 1차 구현 대상은 아니다.

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

- [ ] `image.export-related` job step 추가
- [ ] `fromStep` 결과의 `ParentPath` 기준 sibling image export
- [ ] parent depth 옵션 지원
- [ ] 합성 UI 화면 대응을 위해 관련 group manifest 생성

예시:

```json
{
  "type": "image.export-related",
  "fromStep": "find-image",
  "parentDepth": 1,
  "maxFiles": 100
}
```

### Phase 4. Skill Export Recipe

- [ ] `skill.export` job step 추가
- [ ] `skill.export-batch` job step 추가
- [ ] 기존 skill sprite/export logic을 Headless 서비스로 점진 이동
- [ ] origin/delay/lt/rb/z/source metadata manifest 유지
- [ ] related effect/screen/video/sound export 유지
- [ ] xlsx 기반 batch recipe는 별도 step으로 분리

### Phase 5. Item/Map Recipe

- [ ] `item.icon` job step 추가
- [ ] `item.export` job step 추가
- [ ] `map.export` job step 추가
- [ ] 기존 CLI는 Headless wrapper로 유지

### Phase 6. Agent Serve

- [ ] `wcr2-agent serve --stdio` 추가
- [ ] line-delimited JSON request/response 프로토콜 정의
- [ ] session-level source registry/cache 재사용
- [ ] graceful shutdown 지원
- [ ] request id 기반 응답 보장

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
- stdio server session cache
- Web/UI 인터페이스
- WzLib 내부 구조 변경

이 항목들은 `agent run --job`이 안정화된 뒤 필요성에 따라 진행한다.
