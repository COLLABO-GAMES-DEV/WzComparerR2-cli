# Repository Agent Guidance

이 저장소에서 작업하는 에이전트는 아래 원칙을 따른다.

## 기본 방향

- 이 프로젝트는 기존 WzComparerR2 엔진과 GUI를 보존하면서 `WzComparerR2.Headless`, `WzComparerR2.AgentHost`, `WzComparerR2.Cli`로 headless 추출/조회 기능을 확장하는 방향으로 작업한다.
- 사용자가 "뽑아줘", "추출해줘", "확인해줘"라고 요청하면 먼저 현재 CLI로 실제 추출을 시도한다.
- 현재 CLI로 추출되지 않는 경우에는 실패 원인을 확인하고, 필요한 기능을 새로 개발한다.
- 새 기능 개발은 기본적으로 `WzComparerR2.Headless`, `WzComparerR2.AgentHost`, `WzComparerR2.Cli` 영역에서만 수행한다.
- 에이전트 자동화가 목적이면 단발 CLI 옵션 조합보다 JSON job 기반 `wcr2-agent`를 우선 고려한다.

## 먼저 읽을 문서

새 세션을 시작한 에이전트는 아래 순서로 확인한다.

1. `AGENTS.md`: 수정 경계, 검증 기준, 금지 영역
2. `docs/agent-quickstart.md`: 에이전트용 실행 순서와 대표 job
3. `docs/cli.md`: 전체 명령 reference
4. `docs/agent-runtime-plan.md`: agent runtime 설계와 남은 단계
5. `todo.md`: 현재 완료/보류 상태
6. `research.md`: 이미 확인한 WZ/MS 구조와 이슈 근거

## 엔진 수정 제한

- 위컴알 기존 엔진은 가능한 한 건드리지 않는다.
- 기본적으로 수정 금지 영역:
  - `WzComparerR2.WzLib`
  - 기존 WinForms 앱인 `WzComparerR2`
  - 기존 GUI 플러그인/렌더링 프로젝트
- 위 영역 수정이 필요해 보이면 먼저 CLI 계층에서 우회 또는 조합으로 해결 가능한지 확인한다.
- 엔진 수정이 정말 필요하면 이유, 영향 범위, 검증 방법을 명확히 정리한 뒤 별도 승인받고 진행한다.

## CLI 개발 원칙

- CLI 명령은 실제 MapleStory 클라이언트 데이터의 split WZ/MS layout을 고려한다.
- `--data-dir`, `--skill-wz`, `--string-wz`, `--canvas-wz`, `--sound-wz` 같은 명시 입력을 우선 지원한다.
- 이미지/사운드/스킬/아이템 추출은 먼저 기존 CLI 명령으로 재현하고, 부족한 부분만 좁게 보강한다.
- 경로를 알 수 없는 경우 `tree`, `list`, `search`, `image list`, `sound list`, `dump`로 실제 노드 구조를 확인한 뒤 구현한다.
- 새 명령은 자동화에 쓰기 쉽도록 `--json`, 명확한 exit code, 출력 manifest를 고려한다.
- 복합 작업은 `wcr2-agent run --job <job.json> --json`으로 재현 가능한 job을 먼저 만든다.
- 대량 스킬 추출은 단건 반복보다 `skill.export-batch` 또는 agent `skill.export-xlsx`를 우선 사용한다.

## 자주 쓰는 에이전트 작업 흐름

- 스킬명만 주어진 경우:
  1. `wcr2 skill search-name --data-dir <Data> --name "<name>" --json`
  2. 필요하면 `wcr2 skill resolve-name --data-dir <Data> --name "<name>" --job-code <code> --json`
  3. 확정 후 `wcr2-agent`의 `skill.export` 또는 `skill.export-batch`로 추출
- 엑셀 스킬 목록이 주어진 경우:
  - agent `skill.export-xlsx`를 사용하고 기본 출력 pattern `{jobCode}_{jobName}/{id}_{name}`을 유지한다.
- 경로를 모르는 이미지가 주어진 경우:
  - `image.search` agent step으로 검색하고 필요하면 `image.export-related`로 같은 parent group을 추출한다.
- 아이템 icon만 필요하면:
  - agent `item.icon`을 사용한다.
- 아이템 정보와 icon을 함께 남기려면:
  - agent `item.export`를 사용한다.
- 맵은 현재 실제 렌더 이미지가 아니라 metadata export가 안정 범위다.
  - agent `map.export`로 `map-info.json`과 `map-metadata.json`을 만든다.

## 산출물 규칙

- 실제 추출 검증 산출물은 `.test/<purpose>-<yyyymmdd>` 아래에 둔다.
- 기존 사용자가 남긴 `.test` 폴더를 임의 삭제하지 않는다.
- 새 검증은 기존 폴더를 덮어쓰기보다 `rerun`, `rerun2`처럼 새 하위 폴더를 만든다.
- 결과 판단은 stdout만 보지 말고 `manifest.json`, `agent-result.json`, `resources.json`, `skill-info.json` 같은 sidecar를 같이 확인한다.

## 문서와 테스트

- 기능을 개발하거나 명령 사용법이 바뀌면 관련 문서를 같이 최신화한다.
- 우선 확인할 문서:
  - `docs/cli.md`
  - `docs/cli-migration.md`
  - `docs/windows-cli-test-checklist.md`
  - `todo.md`
  - `research.md`
- 새 추출 기능을 추가하면 가능한 범위에서 `WzComparerR2.Cli.Tests`를 업데이트한다.
- 실제 클라이언트 데이터로 검증한 결과물은 필요할 때 `.test/` 아래에 재현 가능한 폴더명으로 남긴다.

## 작업 검증

- 코드 변경 후 최소 검증:
  - `dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Release --no-restore`
  - `dotnet build WzComparerR2.AgentHost/WzComparerR2.AgentHost.csproj -c Release --no-restore`
  - `dotnet build WzComparerR2.Cli.Tests/WzComparerR2.Cli.Tests.csproj -c Release --no-restore`
  - 가능하면 `DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli.Tests/bin/Release/net8.0/wcr2-tests.dll --cli WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll --agent WzComparerR2.AgentHost/bin/Release/net8.0/wcr2-agent.dll`
- 문서만 변경한 경우에는 빌드를 생략할 수 있으며, 최종 보고에 그 사실을 명시한다.
- 커밋 전에는 `git status --short --branch`로 의도하지 않은 변경이 섞이지 않았는지 확인한다.
