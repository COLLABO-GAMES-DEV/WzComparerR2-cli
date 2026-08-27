# Repository Agent Guidance

이 저장소에서 작업하는 에이전트는 아래 원칙을 따른다.

## 기본 방향

- 이 프로젝트는 기존 WzComparerR2 엔진과 GUI를 보존하면서 `WzComparerR2.Cli`로 headless 추출/조회 기능을 확장하는 방향으로 작업한다.
- 사용자가 "뽑아줘", "추출해줘", "확인해줘"라고 요청하면 먼저 현재 CLI로 실제 추출을 시도한다.
- 현재 CLI로 추출되지 않는 경우에는 실패 원인을 확인하고, 필요한 기능을 새로 개발한다.
- 새 기능 개발은 기본적으로 `WzComparerR2.Cli` 영역에서만 수행한다.

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
  - 가능하면 `dotnet WzComparerR2.Cli.Tests/bin/Release/net8.0/wcr2-tests.dll --cli WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll`
- 문서만 변경한 경우에는 빌드를 생략할 수 있으며, 최종 보고에 그 사실을 명시한다.
- 커밋 전에는 `git status --short --branch`로 의도하지 않은 변경이 섞이지 않았는지 확인한다.
