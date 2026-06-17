# WzComparerR2 전체 기능 CLI 구현 계획

작성일: 2026-05-26

## 목표

WzComparerR2의 기존 WinForms 기능을 가능한 한 CLI 명령으로 사용할 수 있게 만든다. 1차 목표는 기존 파서와 도메인 로직을 최대한 재사용하면서 `wcr2` 단일 실행 파일로 WZ/MS 파일 탐색, 검색, 추출, 비교, 패치, 캐릭터/아이템 정보 출력, 맵/아바타 렌더링 export를 제공하는 것이다.

## 기본 원칙

- 기존 `WzComparerR2.WzLib`를 핵심 파서로 재사용한다.
- WinForms UI 코드에 직접 의존하지 않는 CLI용 서비스 레이어를 새로 만든다.
- 처음부터 모든 기능을 한 번에 옮기지 않고, 기능군별로 CLI 명령을 추가한다.
- 각 단계는 독립적으로 빌드/검증 가능한 상태로 끝낸다.
- GUI 전용 기능은 CLI에서 동일한 화면 조작을 제공하기보다 headless export, JSON 출력, batch 작업으로 재해석한다.
- 새 의존성은 가능하면 피하고, 필요하면 명시적으로 검토한다.

## 제안 프로젝트 구조

- `WzComparerR2.Cli/`
  - 새 콘솔 실행 프로젝트
  - 최종 실행 명령: `wcr2`
- `WzComparerR2.Cli.Core/` 또는 `WzComparerR2.Common` 내부 하위 namespace
  - CLI와 GUI가 같이 쓸 수 있는 서비스 레이어
  - 파일 로딩, 노드 탐색, export, compare, patch orchestration 담당
- 기존 유지
  - `WzComparerR2.WzLib`: WZ/MS 파서
  - `WzComparerR2.Common`: 공용 모델/렌더링/설정
  - `WzComparerR2`: 기존 WinForms 앱
  - 플러그인 프로젝트들: 기능별로 headless 진입점 추가 검토

## MVP 명령어 설계

```bash
wcr2 info <file-or-dir>
wcr2 tree <file-or-dir> [--path <wz-path>] [--depth <n>] [--json]
wcr2 list <file-or-dir> --path <wz-path> [--json]
wcr2 search <file-or-dir> --name <text>
wcr2 search <file-or-dir> --value <text>
wcr2 extract <file-or-dir> --path <wz-path> --out <dir> [--recursive] [--format xml]
wcr2 compare <old-file-or-dir> <new-file-or-dir> --out <json-or-dir>
```

## 전체 기능군 매핑

| 기존 기능 | CLI 명령 후보 | 우선순위 | 비고 |
| --- | --- | --- | --- |
| WZ/MS 파일 열기 | `info`, `tree`, `list` | P0 | 모든 기능의 기반 |
| 노드 검색 | `search` | P0 | 이름/경로/값 검색 |
| 노드 덤프 | `dump` | P0 | JSON/XML/raw |
| 이미지 추출 | `extract image` | P0 | PNG/APNG/GIF 후보 |
| 사운드 추출 | `extract sound` | P1 | mp3/wav/raw |
| 비디오 추출 | `extract video` | P1 | mcv/libvpx 연동 확인 필요 |
| 클라이언트 비교 | `compare` | P1 | `Comparer/` 재사용 |
| 패치 적용/생성 | `patch apply`, `patch build`, `patch reverse` | P2 | 위험도가 있어 별도 검증 필요 |
| 장비/아이템/스킬 툴팁 | `item`, `skill`, `mob`, `npc`, `quest` | P2 | 텍스트/JSON/이미지 export |
| GIF/애니메이션 생성 | `animate export` | P2 | frame export부터 시작 |
| 아바타 렌더링 | `avatar render`, `avatar inspect` | P3 | GUI 의존 제거 필요 |
| 맵 렌더링 | `map render`, `map info` | P3 | headless screenshot/export 중심 |
| Lua 콘솔 | `lua run` | P3 | batch script 실행으로 재해석 |
| Network 채팅 | `network chat`, `network send` | P4 | CLI 필요성 재검토 |
| Updater | `update check`, `update apply` | P4 | 기존 updater와 책임 분리 필요 |
| WinForms 설정 화면 | `config get/set/list` | P4 | CLI 설정 파일 별도 설계 가능 |

## Phase 0. 사전 정리와 기준선 확보

- [x] `research.md` 기준으로 CLI 전환 범위를 확정한다.
- [ ] Windows 빌드 가능 환경을 준비한다.
- [ ] `CharaSimResource` 서브모듈을 초기화한다.
  - 명령 후보: `git submodule update --init --recursive`
- [ ] 현재 솔루션 restore/build 기준선을 기록한다.
  - `dotnet restore WzComparerR2.sln`
  - `dotnet build WzComparerR2.sln -c Release /p:Platform="Any CPU"`
- [ ] 샘플 WZ/MS 파일 세트를 정한다.
  - 공개 가능 샘플 또는 로컬 비공개 fixture
  - 최소: Base/String/Item/Map/Sound/Mob/Npc/Skill 계열
- [x] CLI 출력 스냅샷 검증 방식을 정한다.
  - JSON 출력은 golden file 비교
  - binary export는 hash/크기/metadata 비교
  - 세부 기준: `docs/cli-test-strategy.md`

완료 기준:

- [ ] 기존 앱이 깨지지 않고 빌드된다.
- [ ] CLI 검증에 사용할 최소 샘플 데이터와 expected output 전략이 있다.

## Phase 1. CLI 프로젝트 골격 추가

- [x] `WzComparerR2.Cli` 콘솔 프로젝트를 추가한다.
- [x] 타깃 프레임워크를 결정한다.
  - 1안: `net8.0` 우선
  - 2안: 기존 호환성을 위해 `net462;net8.0` 다중 타깃
- [x] `WzComparerR2.Cli`가 `WzComparerR2.WzLib`를 참조하게 한다.
- [x] 필요 시 `WzComparerR2.Common` 참조를 추가하되, WinForms 의존 기능은 피한다.
- [x] `WzComparerR2.sln`에 CLI 프로젝트를 등록한다.
- [x] 기본 명령 구조를 만든다.
  - `wcr2 --help`
  - `wcr2 --version`
  - `wcr2 info`
- [x] exit code 규칙을 정의한다.
  - `0`: 성공
  - `1`: 사용자 입력 오류
  - `2`: 파일/경로 없음
  - `3`: WZ 파싱 실패
  - `4`: export 실패
  - `5`: 내부 오류

완료 기준:

- [ ] `dotnet run --project WzComparerR2.Cli -- --help`가 동작한다.
- [ ] 기존 WinForms 프로젝트 빌드가 유지된다.

## Phase 2. WZ/MS 로딩 서비스 분리

- [x] CLI용 `WzLoadOptions`를 만든다.
  - file path
  - folder path
  - useBaseWz 여부
  - fallback path
  - encoding/region/key 옵션
- [x] `WzComparerR2.WzLib/Wz_Structure.cs`의 로딩 진입점을 감싸는 서비스를 만든다.
  - `Load(string fileName, bool useBaseWz = false)`
  - `LoadFile(...)`
  - `LoadImg(...)`
  - `LoadWzFolder(...)`
  - `LoadMsFile(...)`
- [x] CLI에서 `Wz_Node`를 안정적으로 탐색하는 path resolver를 만든다.
  - `/`와 `\` 모두 지원
  - 대소문자 옵션 검토
  - full path와 relative path 구분
- [x] 파일 로딩 실패 메시지를 사람이 이해하기 쉽게 변환한다.
- [x] `info`, `tree`, `list` 명령을 구현한다.

완료 기준:

- [ ] 단일 WZ 파일에서 root 정보 출력 가능
- [ ] 폴더형 WZ 구조에서 tree 출력 가능
- [ ] MS 파일 로딩 가능 여부 확인
- [x] 잘못된 경로 입력 시 명확한 에러와 non-zero exit code 반환

## Phase 3. 표준 출력 포맷

- [x] CLI 출력 모델을 정의한다.
  - `NodeDto`
  - `FileInfoDto`
  - `SearchResultDto`
  - `ExportResultDto`
  - `CompareResultDto`
- [x] 기본 human-readable 출력과 `--json` 출력을 분리한다.
- [x] `--quiet`, `--verbose`, `--no-color` 옵션을 추가한다.
  - `--quiet`: 성공 stdout 억제
  - `--verbose`: 실패 stderr에 예외 타입/스택 추가
  - `--no-color`: 현재 색상 미사용이지만 스크립트 호환 플래그로 수용
- [x] 큰 tree 출력 제한 옵션을 추가한다.
  - `--depth`
  - `--limit`
  - `--include-values`
- [x] 오류 출력은 stderr로 통일한다.

완료 기준:

- [x] 현재 구현된 P0 명령이 `--json`을 지원한다.
- [x] JSON 출력은 자동화 스크립트에서 파싱 가능하다.

## Phase 4. 검색 기능 구현

- [x] `search --name <text>` 구현
- [x] `search --match-path <glob-or-regex>` 구현
- [x] `search --value <text>` 구현
- [x] 검색 범위 옵션 추가
  - `--path <wz-path>`
  - `--max-results <n>`
  - `--type image|sound|string|vector|uol|raw`
- [ ] lazy image extraction이 필요한 검색과 필요 없는 검색을 분리한다.
- [ ] 검색 성능 측정용 샘플 케이스를 만든다.

완료 기준:

- [ ] String.wz 또는 folder WZ에서 이름/값 검색 가능
- [ ] 큰 파일에서 진행률 또는 제한 옵션으로 제어 가능

## Phase 5. 덤프/export 기본 기능

- [x] `dump --format json` 구현
- [x] `dump --format xml` 구현
- [x] `dump --format raw` 구현
- [x] `extract` 경로에서 raw/video blob 파일 추출 구현
- [x] `extract image` 구현
  - `Wz_Png` -> PNG
  - linked source/UOL 처리
  - batch export 옵션
- [x] `extract sound` 구현
  - `Wz_Sound`
  - 확장자 자동 결정
  - raw 추출 fallback
- [ ] `extract video` 구현 가능성 조사
  - `Wz_Video`
  - `VpxVideoDecoder`
  - native `libvpx`, `libyuv` 필요성 확인
- [x] `Wz_Video` 원본 blob `.mcv` 추출 구현
- [x] export 결과 manifest 생성 옵션 추가
  - `--manifest export.json`

완료 기준:

- [ ] 이미지 노드를 PNG 파일로 추출 가능
- [ ] 사운드 노드를 파일로 추출 가능
- [ ] export 실패 시 실패 노드 목록을 JSON으로 받을 수 있음

## Phase 6. 비교 기능 CLI화

- [ ] `WzComparerR2/Comparer/` 구조를 분석한다.
  - `WzFileComparer`
  - `CompareDifference`
  - `DifferenceType`
  - `WzPngComparison`
  - `WzVirtualNode`
- [ ] WinForms 의존이 섞인 부분을 서비스로 분리한다.
- [x] `compare <old> <new>` 명령을 구현한다.
- [x] 출력 포맷을 정의한다.
  - [x] JSON diff
  - [x] human summary
  - [x] optional markdown report
  - [ ] optional HTML report
- [x] JSON diff 파일 저장 옵션 추가
  - `--out <json>`
- [ ] 이미지 비교 결과 export를 지원한다.
- [ ] filter 옵션 추가
  - [x] `--type added|removed|changed`
  - [x] `--path`
  - [x] `--ignore-image-binary`

완료 기준:

- [ ] 두 WZ 파일 또는 두 WZ 폴더의 차이를 CLI에서 확인 가능
- [ ] diff 결과가 자동화 가능한 JSON으로 저장됨

## Phase 7. 패처 기능 CLI화

- [x] `WzComparerR2/Patcher/` 구조를 분석한다.
  - `WzPatcher`
  - `ReversePatcherBuilder`
  - `PatcherSetting`
  - `Builder/*`
- [x] 읽기 전용 dry-run 기능을 먼저 만든다.
  - [x] `patch inspect`
  - [x] `patch dry-run`
- [x] 실제 적용 명령을 만든다.
  - [x] `patch apply <patch-file> --target <dir> --out <dir>`
- [ ] reverse patch 생성 명령을 만든다.
  - `patch reverse-build <old> <new> --out <patch>`
- [x] 파일 overwrite 정책을 명확히 한다.
  - 기본값은 원본 수정 금지
  - `--in-place`는 지원하지 않음
  - `--out`은 target과 분리되어야 하며 없거나 비어 있어야 함
- [x] checksum 검증과 로그 파일을 추가한다.
  - [x] `patch dry-run` 기존 파일 checksum 검증
  - [x] patch apply 로그 파일

완료 기준:

- [x] dry-run으로 변경 예정 파일 목록 확인 가능
- [x] out directory 방식으로 안전하게 patch 적용 가능
- [x] checksum 실패가 명확히 보고됨
  - `PatchDryRunResultDto.ChecksumMismatchCount`와 action `Status=checksum-mismatch`로 보고.

## Phase 8. CharaSim/Tooltip 계열 CLI화

- [ ] `WzComparerR2.Common/CharaSim/` 모델 로딩 방식을 정리한다.
- [ ] `WzComparerR2/CharaSim/CharaSimLoader.cs`의 UI 의존성을 분리한다.
- [x] 아이템/장비/기본 도메인 조회 명령 구현
  - [x] `item info --id <id>`
  - [x] `gear info --id <id>`
  - [x] `skill info --id <id>`
  - [x] `mob info --id <id>`
  - [x] `npc info --id <id>`
  - [x] `quest info --id <id>`
- [x] 출력 형식
  - [x] text
  - [x] JSON
  - [ ] optional tooltip image
- [ ] 툴팁 렌더러의 WinForms/GDI 의존을 CLI에서 호출 가능한 렌더링 서비스로 감싼다.
- [ ] string linker 초기화 옵션을 제공한다.
  - [x] `--string-wz`
  - `--item-wz`
  - `--etc-wz`
  - `--quest-wz`

완료 기준:

- [ ] 아이템/스킬/몬스터/NPC/퀘스트 정보를 CLI에서 조회 가능
  - [x] 아이템/장비/스킬 기본 데이터 조회 명령 구현
- [ ] 최소 하나 이상의 툴팁 이미지 export 가능

## Phase 8A. CharaSim headless 해석 계층 CLI 연결

분석 결과:

- 현재 `skill info`는 `WzComparerR2.Common/CharaSim`을 사용하지 않고 `DomainInfoFinder` + `DomainInfoDto`로 id 노드와 문자열을 얕게 합친다.
- 그래서 `LevelCount`, `MaxLevel`, `Description`이 `null`이거나 `#x`, `#damage`, `#indiePMdR` 같은 placeholder가 남는 것은 데이터 깨짐보다 CharaSim 해석 계층 미연결에 가깝다.
- 기존 GUI의 풍부한 스킬 해석은 `Skill.CreateFromNode`, `StringLinker`, `SummaryParser`, `Calculator` 조합으로 수행된다.
- `SkillTooltipRender2`와 툴팁 이미지 export는 `System.Drawing`, WinForms, `CharaSimResource`, `PluginManager.FindWz` 의존이 커서 첫 단계에서 바로 CLI에 붙이면 macOS/GDI+ 문제와 GUI 의존 문제가 함께 터진다.

목표:

- 렌더링 없이 스킬/아이템/장비/몹/NPC/퀘스트의 CharaSim 모델 해석 결과를 JSON/XML/text로 출력한다.
- 스킬은 우선 `common`, `PVPcommon`, `level`, `req`, `action`, `maxLevel`, `masterLevel`, flags, resolved summary를 CLI에서 볼 수 있게 한다.
- 툴팁 PNG export는 Windows-only 후속 단계로 분리한다.

작업:

- [x] Headless CharaSim core 분리 방식을 결정한다.
  - 후보 A: `WzComparerR2.Common`에 `net8.0` non-windows target을 추가하고 순수 CharaSim 파일만 조건부 빌드한다.
  - 후보 B: 새 프로젝트 `WzComparerR2.CharaSimCore`를 만들고 `Skill`, `StringResult`, `StringLinker`, `SummaryParser`, `SummaryParams`, `Calculator`를 이동/공유한다.
  - 후보 C: CLI 프로젝트에 필요한 파일만 링크한다. 빠르지만 장기 유지보수 비용이 커서 임시 방안으로만 사용한다.
  - 결정: Phase 8A에서는 후보 C를 선택했다. `Calculator.cs`만 링크하고, GUI/GDI 의존 없이 스킬 headless DTO/요약 파서를 CLI에 둔다.
- [ ] CLI용 WZ repository/find service를 만든다.
  - `PluginManager.FindWz` 이벤트/WinForms 의존 없이 `FindWz("Skill/1100.img/skill/11001025")` 같은 경로 조회를 제공한다.
  - 입력 후보: `--skill-wz`, `--string-wz`, `--item-wz`, `--etc-wz`, `--quest-wz`, `--base-wz`, `--data-dir`.
  - split layout 후보: `Data/Skill`, `Data/String`, `Data/Item`, `Data/Etc`, `Data/Quest`.
- [ ] `StringLinker` 초기화를 CLI에서 수행한다.
  - [x] 현재 `--string-wz` 단일 보강은 구현됨.
  - [ ] `StringLinker.Load(stringNode, itemNode, etcNode, questNode)`와 호환되는 입력 로딩을 제공한다.
  - [x] `skill full --allow-string-only`에서 string-only 상태를 출력에 명확히 표시한다.
  - [ ] full-linker 상태를 출력에 명확히 표시한다.
- [x] `skill full` 또는 `skill detail` 명령을 추가한다.
  - 예: `wcr2 skill full <skill-wz> --id 11001025 --string-wz <string> --level max --json`
  - 출력: raw path, name, desc, h/ph/hch, common, pvpCommon, levelCommon, reqSkill, reqLevel, actions, flags, icon paths, maxLevel, masterLevel.
  - 출력: `resolvedSummary`, `nextLevelSummary`, `unresolvedPlaceholders`.
  - 기존 `skill info`는 호환성 유지용 얕은 metadata 명령으로 남긴다.
- [x] `skill full --format json|xml|text`를 지원한다.
  - XML은 raw WZ dump가 아니라 CharaSim 해석 결과 XML로 정의한다.
  - 예: `<skill id="11001025" name="라이징 선"><common>...</common><summary level="...">...</summary></skill>`.
- [x] `1001004` 같은 string-only 스킬 상태를 명확히 처리한다.
  - `String/Skill.img/1001004`는 존재하지만 `Data/Skill` 실제 skill node가 없으면 exit 실패 대신 `Status: string-only` 옵션을 제공할지 결정한다.
  - 기본은 기존 CLI 호환을 위해 `skill full`에서만 풍부한 진단을 제공한다.
- [x] 실제 WZ 기반 golden 샘플을 추가한다.
  - macOS CrossOver 실클라에서 확인한 후보: `10000074`, `11001025`, `11100027`.
  - 검증 포인트: 이름 한글 정상 디코딩, common/level 존재 여부, summary placeholder 치환 여부, string-only 진단.
  - fixture가 없을 때는 자동 테스트를 skip/gate 처리한다.
- [ ] 렌더링 단계는 별도 Phase 8B로 분리한다.
  - `skill tooltip --out <png>`는 Windows 우선.
  - macOS는 `System.Drawing/GDI+` 오류 해결 전까지 제한으로 문서화한다.
  - 이 단계에서만 `CharaSimResource`/`SkillTooltipRender2` 직접 사용 여부를 재검토한다.

완료 기준:

- [x] `skill info` 기존 JSON/text 계약이 깨지지 않는다.
- [x] `skill full`이 실제 개별 스킬 3개 이상에서 CharaSim model fields를 출력한다.
- [ ] `SummaryParser` 기반 resolved summary가 common 값이 있는 스킬에서 placeholder를 실제 값으로 치환한다.
- [x] `1001004`처럼 실제 skill node가 없는 경우 string-only 상태 또는 명확한 진단을 제공한다.
- [x] macOS에서도 JSON/XML/text 출력은 동작한다.
- [x] PNG tooltip export는 Windows-only 또는 별도 제한으로 문서화된다.

## Phase 9. 애니메이션/GIF 생성 CLI화

- [x] `WzComparerR2.Common/Gif*`, `Animation/`, `Encoders/` 구조를 분석한다.
- [x] animation frame 추출 명령 구현
  - `animate frames --path <wz-path> --out <dir>`
- [x] GIF/APNG export 명령 구현
  - [x] `animate gif`
  - [x] `animate apng`
- [x] overlay 옵션을 CLI 인자로 설계한다.
  - [x] origin: `--origin <x,y>`
  - [x] delay: `--delay <ms>`
  - [x] background: `--background transparent|#RRGGBB`, `--min-alpha <0-255>`
  - [x] scale: `--scale <factor>`
  - [x] frame range: `--start-frame <n>`, `--end-frame <n>`
- [x] ffmpeg encoder 사용 여부와 경로 옵션을 정한다.
  - [x] `animate ffmpeg`
  - [x] `--ffmpeg <path>`
  - [x] `--ffmpeg-args <format>`

완료 기준:

- [ ] WZ animation node에서 frame PNG export 가능
  - [x] `animate frames` 구현과 manifest 생성 경로 빌드 검증 완료
  - [ ] 실제 WZ animation sample 기반 PNG export 검증 필요
- [x] GIF 또는 APNG 파일 생성 가능
  - [x] `animate gif` 구현 및 빌드 검증
  - [x] `animate apng` 구현 및 빌드 검증
  - [ ] 실제 WZ animation sample 기반 GIF export 검증 필요
  - [ ] 실제 WZ animation sample 기반 APNG export 검증 필요

## Phase 10. Avatar 기능 CLI화

- [x] `WzComparerR2.Avatar/Entry.cs`와 `WzComparerR2.Avatar/UI/` 의존성을 분석한다.
  - `Entry.cs`/UI는 WinForms plugin host, OpenAPI form, ribbon/menu lifecycle에 묶여 있어 CLI에 직접 링크하지 않는다.
- [x] `WzComparerR2/AvatarCommon/` 재사용 범위를 확인한다.
  - `AvatarCanvas`의 합성 로직은 재사용 후보지만 내부 WZ 탐색이 `PluginManager.FindWz`에 묶여 있어 repository injection 분리가 먼저 필요하다.
- [x] avatar code 파싱/검증 명령 구현
  - `avatar inspect --code <code>`
  - `avatar unpack --code <code> --json`
- [x] avatar render dry-run 명령 구현
  - `avatar render --code <code> --out avatar.png --dry-run`
  - `avatar render --items <ids...> --action <action> --dry-run`
  - 후보 WZ 경로, action/emotion, blocker manifest를 JSON으로 출력한다.
- [x] 외부 MapleStory OpenAPI 사용 여부를 옵션화한다.
  - `--offline`
  - `--api-key`
- [ ] avatar render 실제 PNG 명령 구현
  - `AvatarCommon`에서 `PluginManager.FindWz` 직접 호출을 제거하거나 주입 가능하게 만든 뒤 진행한다.
- [x] rendering을 headless로 실행할 수 있는지 1차 검증한다.
  - 현재 결론: CLI dry-run은 가능하지만 실제 render는 `PluginManager.FindWz`와 `System.Drawing/GDI+` 의존 때문에 아직 blocked.

완료 기준:

- [x] 아바타 구성 정보를 JSON으로 출력 가능
- [x] avatar render dry-run plan을 JSON으로 출력 가능
- [ ] 최소 정적 avatar PNG export 가능

## Phase 11. MapRender 기능 CLI화

- [x] `WzComparerR2.MapRender/Entry.cs`, `FrmMapRender.cs`, `FrmMapRender2.cs`, `MapData.cs` 구조를 분석한다.
- [x] map metadata 조회 명령 구현
  - [x] `map info --id <map-id>`
  - [x] `map objects --id <map-id>`
  - [x] `map portals --id <map-id>`
  - [x] `map life --id <map-id>`
  - [x] `map reactors --id <map-id>`
- [x] headless render 가능성 조사
  - MonoGame device 생성
  - offscreen render target
  - native dependency
- [x] map render dry-run 명령 구현
  - `map render --id <map-id> --out map.png --dry-run`
  - `--layer`
  - `--include-life`
  - `--include-reactor`
  - `--include-tooltip`
- [ ] map screenshot export 명령 구현
  - MonoGame `Game` 없이 offscreen `GraphicsDevice`/render target을 안정적으로 만들 수 있는지 Windows에서 검증한 뒤 진행한다.
- [ ] world map/minimap export 검토
  - `map minimap`
  - `map worldmap`

완료 기준:

- [ ] map metadata를 JSON으로 출력 가능
  - [x] map metadata JSON DTO와 명령 surface 구현 완료
  - [ ] 실제 Map.wz sample 기반 JSON 출력 검증 필요
- [x] map render dry-run plan을 JSON으로 출력 가능
- [ ] 최소 한 개 map screenshot PNG export 가능

## Phase 12. LuaConsole 기능 CLI화

- [ ] `WzComparerR2.LuaConsole/LuaSandbox.cs`를 CLI에서 재사용할 수 있게 정리한다.
  - 현재 `LuaSandbox`는 NLua + WinForms startup path 의존이 있어 CLI 직접 참조 대신 외부 lua 실행기 브릿지로 1차 구현
- [x] `WzComparerR2.LuaConsole/LuaSandbox.cs` 재사용 가능성 분석
- [x] Lua 실행 명령 구현
  - `lua run <script.lua> --wz <file-or-dir>`
  - [x] `lua eval <code> --wz <file-or-dir>`
- [x] dry-run 검증 옵션 구현
  - `--dry-run`
- [x] WZ 입력 검증과 환경 변수 전달 구현
  - `WCR2_WZ_INPUT`
  - `WCR2_WZ_ROOT`
- [ ] Lua global API 문서화
  - find node
  - dump
  - export
- [ ] sandbox 제한과 파일 접근 정책을 정한다.
- [ ] examples를 CLI 기준으로 갱신한다.

완료 기준:

- [ ] 기존 `Examples/*.lua` 중 최소 2개가 CLI에서 실행 가능
  - [x] `DumpXml.lua` dry-run 검증 가능
  - [x] `lua eval --code "print('ok')" --dry-run --json` 검증 가능
  - [ ] 현재 환경에 외부 lua 실행기가 없어 실제 script 실행 미검증
- [ ] Lua 오류가 line/stack 정보와 함께 출력됨

## Phase 13. Network 기능 CLI화

- [x] `WzComparerR2.Network/WcClient.cs`와 `Contracts/`를 분석한다.
- [x] CLI에서 필요한 기능 범위를 재평가한다.
  - 채팅 접속
  - 서버 정보 조회
  - 메시지 송수신
  - custom package
- [x] 명령 후보
  - [x] `network server-info`
  - [ ] `network login`
  - [x] `network chat`
  - [x] `network send`
- [x] interactive CLI 모드와 non-interactive 모드를 분리한다.
  - 기본 명령은 non-interactive dry-run/probe이며, `--interactive`는 예약 옵션으로 명시적으로 거부한다.
- [x] credential 저장을 피하고 env var 또는 인자 입력으로 처리한다.
  - 현재 credential을 받거나 저장하지 않는 dry-run/probe만 제공

완료 기준:

- [x] 서버 정보 조회 또는 dry-run 수준 명령 구현
- [ ] 실제 채팅 기능은 별도 승인 후 진행

## Phase 14. Updater 기능 CLI화

- [x] 기존 `WzComparerR2.Updater`와 `WzComparerR2/Updater.cs` 역할을 분석한다.
- [x] CLI update command의 책임을 정한다.
  - [x] 최신 버전 확인
  - [x] 다운로드 URL 출력
  - [x] 다운로드
  - [x] self-update 또는 외부 updater 실행 계약
- [x] 명령 후보
  - [x] `update check`
  - [x] `update download --out <dir>`
  - [x] `update apply`
- [x] 기존 GUI 앱 업데이트와 CLI 업데이트가 충돌하지 않도록 분리한다.

완료 기준:

- [x] 최신 릴리스 정보를 CLI에서 확인 가능
- [x] 자동 적용은 별도 안전 설계 후 진행

## Phase 15. Config CLI

- [x] CLI 전용 config 위치를 정한다.
  - Windows: `%APPDATA%/WzComparerR2/wcr2.config.json`
  - Unix: `$XDG_CONFIG_HOME/wzcomparerr2/wcr2.config.json` 또는 `~/.config/wzcomparerr2/wcr2.config.json`
  - override: `--config <path>` 또는 `WCR2_CLI_CONFIG`
- [x] 기존 `ConfigManager` 재사용 가능성을 확인한다.
- [x] 명령 구현
  - [x] `config path`
  - [x] `config list`
  - [x] `config get <key>`
  - [x] `config set <key> <value>`
  - [x] `config unset <key>`
- [x] profile 지원 검토
  - `--profile kms`
  - `--profile gms`
  - `--profile custom`
  - 값은 `profiles.<profile>.<key>` flat key로 저장하고, 입력 fallback은 profile 값을 전역 값보다 우선한다.
- [x] CLI 기본값 문서화

완료 기준:

- [x] 반복 작업에서 매번 WZ 경로를 입력하지 않아도 됨
- [x] config 오류가 명확히 보고됨

## Phase 16. 플러그인/확장 모델 재설계

- [x] 기존 `PluginEntry`는 WinForms 컨텍스트 중심이므로 CLI용 plugin contract를 별도 설계한다.
- [x] 후보 인터페이스
  - [x] `ICliCommandProvider`: 구현 완료
  - [ ] `ICliExportProvider`: WZ export hook가 필요해질 때 별도 추가
  - [ ] `ICliNodeAction`: WZ node context 전달 계약이 안정화된 뒤 추가
- [x] 기존 GUI 플러그인과 CLI 플러그인을 동시에 지원할지 결정한다.
  - 결정: CLI 플러그인은 `CliPlugin/`과 `ICliCommandProvider` 중심으로 분리한다. 기존 GUI `Plugin/`은 기본 스캔하지 않고 `--include-gui-plugin-dir`로 inspect만 지원한다.
- [x] plugin discovery 경로를 정한다.
  - `--plugin-dir`
  - config key `plugin-dir`
  - `WCR2_CLI_PLUGIN_DIR`
  - 실행 파일 옆 `CliPlugin/`
  - 현재 작업 디렉터리의 `CliPlugin/`
  - `Plugin/`은 `--include-gui-plugin-dir` 지정 시만 스캔
- [x] version compatibility 정책을 만든다.
  - 1차 정책: 플러그인은 `WzComparerR2.Cli.ICliCommandProvider` public contract를 참조한다. `plugin inspect`가 assembly name/version, provider type, command descriptor를 표시한다.
- [x] 실패한 plugin load가 CLI 전체를 죽이지 않게 한다.
  - 실패 assembly는 `Status=failed`, `Error=<message>`로 보고하고 `plugin list/commands`는 계속 진행한다.

완료 기준:

- [x] 외부 CLI 명령을 plugin assembly에서 등록 가능
  - `plugin commands`가 `ICliCommandProvider.GetCommands()` 결과를 수집하고, `plugin run <command>`가 provider 실행까지 수행한다.
- [x] 기존 GUI plugin loading과 충돌하지 않음
  - CLI 기본 경로는 `CliPlugin/`이고 GUI `Plugin/`은 opt-in scan이다.

## Phase 17. 문서화

- [x] `docs/cli.md` 작성
  - 설치
  - 명령어
  - 예제
  - exit code
  - JSON schema
- [x] `README.md`에 CLI 섹션 추가
- [x] 각 명령의 `--help` 텍스트 작성
- [x] 마이그레이션 가이드 작성
  - GUI에서 하던 작업을 CLI로 하는 예시
- [x] sample scripts 작성
  - [x] batch extract: `samples/cli/batch-extract.sh`
  - [x] compare report: `samples/cli/compare-report.sh`
  - [x] avatar metadata export: `samples/cli/avatar-metadata.sh`
  - [x] map metadata export: `samples/cli/map-metadata.sh`
  - [x] avatar render dry-run: CLI image rendering blocker manifest 제공
  - [ ] avatar render PNG: `AvatarCommon` headless repository injection 후 구현
  - [x] map render dry-run: CLI screenshot rendering blocker manifest 제공
  - [ ] map screenshot PNG: MonoGame offscreen render target 검증 후 구현

완료 기준:

- [x] 신규 사용자가 문서만 보고 `info/tree/extract/compare`를 실행할 수 있음

## Phase 18. 테스트 전략

- [x] CLI 단위 테스트 프로젝트 추가
  - `WzComparerR2.Cli.Tests`: 외부 test framework 없이 실행되는 CLI process 기반 smoke/error test harness
- [x] parser 자체를 검증하기보다 CLI service와 output contract를 검증한다.
  - CLI 바이너리를 실제 process로 실행하고 exit code, stdout/stderr, JSON field를 검증한다.
- [ ] golden output 테스트 추가
  - [x] golden/snapshot 검증 전략 문서화: `docs/cli-test-strategy.md`
  - [x] 현재 샘플 없이 검증 가능한 JSON contract: `avatar`, `config`, `lua --dry-run`, `network`, `plugin`
  - [ ] 실제 WZ sample 기반 tree JSON
  - [ ] 실제 WZ sample 기반 search JSON
  - [ ] 실제 WZ sample 기반 compare JSON
- [ ] export 검증 추가
  - [ ] PNG dimensions: 실제 WZ image sample 필요
  - [ ] sound file exists/hash: 실제 WZ sound sample 필요
  - [ ] manifest: 실제 WZ extract sample 필요
- [x] error case 테스트 추가
  - 파일 없음
  - invalid regex
  - missing config key
  - invalid update asset
  - corrupt plugin assembly
  - path 없음/unsupported value type/corrupt WZ는 실제 WZ fixture 확보 후 추가
- [x] Windows CI에 CLI test 단계 추가
  - Azure pipeline `Build anycpu` 직후 Release CLI 바이너리 대상으로 `WzComparerR2.Cli.Tests` 실행
- [x] macOS/Linux에서 가능한 pure library test와 Windows-only test를 분리한다.
  - 현재 test harness는 WZ 샘플 없이 macOS/Linux/Windows에서 실행 가능한 CLI smoke/error tests에 한정한다.
  - GUI/렌더링/실제 WZ happy path는 fixture 확보 후 별도 테스트로 추가한다.

완료 기준:

- [ ] CLI 명령별 최소 happy path와 error path 테스트가 있음
  - 현재 WZ 샘플 불필요 명령의 happy/error path는 자동화 완료.
  - WZ 샘플 기반 `info/tree/list/search/compare/dump/extract` happy path는 미완료.
- [x] CI에서 CLI 빌드와 테스트가 실행됨

## Phase 19. 패키징/배포

- [x] CLI binary artifact 이름 결정
  - 실행 파일: `wcr2.exe`
  - 현재 수동 산출물: `artifacts/wcr2-win-x64-self-contained.zip`
- [x] self-contained publish 여부 결정
  - Windows 현장 테스트용은 `win-x64 --self-contained true`로 런타임 포함 publish.
- [x] native DLL 포함 규칙 정리
  - `Lib/x86`
  - `Lib/x64`
  - `Lib/ARM64`
  - CLI는 현재 WzLib 중심 기능이라 GUI native `Lib/*`를 별도로 포함하지 않는다. self-contained publish는 .NET runtime/native runtime 파일을 publish 폴더에 포함한다.
- [x] Azure pipeline에 CLI artifact 추가
  - `Publish CLI win-x64 self-contained`와 `Compress CLI win-x64 release` 단계 추가.
- [x] release note에 CLI 사용 예시 추가
- [x] 기존 GUI artifact와 CLI artifact를 분리한다.
  - GUI: `WcR2_With_Plugins*.zip`
  - CLI: `WzComparerR2.Cli-win-x64-self-contained_<BuildNumber>.zip`

완료 기준:

- [ ] CI 산출물에 CLI zip이 포함됨
  - pipeline 정의에는 추가 완료. 실제 Azure run artifact 확인 필요.
- [ ] zip만 풀어서 `wcr2 --help` 실행 가능
  - macOS에서 `wcr2.exe`가 Windows x64 PE executable인 것은 확인. 실제 실행은 Windows에서 `docs/windows-cli-test-checklist.md`로 검증 필요.

## 권장 구현 순서

1. Phase 0-3: CLI 기반과 출력 계약
2. Phase 4-5: 검색과 추출
3. Phase 6: 비교
4. Phase 7: 패치
5. Phase 8-9: CharaSim/Tooltip, 애니메이션
6. Phase 10-11: Avatar, MapRender
7. Phase 12-14: Lua, Network, Updater
8. Phase 15-19: config, plugin, docs, tests, packaging

## 첫 번째 PR 후보

첫 PR은 작고 검증 가능하게 만든다.

- [x] `WzComparerR2.Cli` 프로젝트 추가
- [x] `info`, `tree`, `list` 구현
- [x] `search --name`, `search --value` 기본 구현
- [x] `extract --path --out` 기본 구현
- [x] `dump --format json|xml|raw` 기본 구현
- [x] `compare <old> <new>` 기본 구현
- [x] `--json` 출력 지원
- [x] README 또는 `docs/cli.md` 초안 추가
- [x] 최소 테스트 또는 수동 검증 로그 추가

첫 PR 완료 기준:

- [ ] `dotnet run --project WzComparerR2.Cli -- info <sample.wz>` 동작
- [ ] `dotnet run --project WzComparerR2.Cli -- tree <sample.wz> --depth 2 --json` 동작
- [x] CLI 프로젝트 단독 빌드 통과
- [ ] 기존 GUI 앱 빌드 유지

현재 구현 메모:

- [x] `dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug` 통과
- [x] `WzComparerR2.WzLib` 프로젝트 참조를 `net8.0`으로 고정해 VS Code/MSBuild 빨간줄 원인 완화
- [x] 현재 macOS 환경은 .NET 8 런타임이 없어 `DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll --help`로 도움말 실행 확인
- [x] 없는 파일 입력 시 exit code `2`와 오류 메시지 확인
- [x] `extract` 도움말 노출 및 없는 파일 입력 시 exit code `2` 확인
- [x] `search --match-path`, `--type`, `--regex` 도움말 노출 및 잘못된 regex 입력 시 exit code `1` 확인
- [x] `dump` 도움말 노출 및 없는 파일 입력 시 exit code `2` 확인
- [x] `compare` 도움말 노출, 인자 부족 시 exit code `1`, 없는 파일 입력 시 exit code `2`, 잘못된 `--type` 입력 시 exit code `1` 확인
- [x] `compare --format markdown --out <path>` 구현
- [x] `patch inspect` 도움말 노출 및 없는 파일 입력 시 exit code `2` 확인
- [x] `patch dry-run` 도움말, 인자 부족, 없는 patch 파일, 없는 target 폴더 입력 검증
- [x] `patch apply` 도움말, 인자 부족, 없는 patch 파일, 없는 target 폴더, target과 같은 out 입력 검증
- [x] `skill`, `animate` 도움말 노출 확인
- [x] `avatar inspect --code "1002140,1040036,1060026" --json` 파싱 출력 확인
- [x] `avatar unpack --code abc --json` 경고 출력 확인
- [x] `skill info /no/such.wz --id 1001004` 없는 파일 입력 시 exit code `2` 확인
- [x] `map` 도움말 노출 확인
- [x] `lua run WzComparerR2.LuaConsole/Examples/DumpXml.lua --dry-run --json` 출력 확인
- [x] `network server-info --json` dry-run 출력 확인
- [x] `map portals /no/such.wz --id 100000000` 없는 파일 입력 시 exit code `2` 확인
- [x] `network send` 메시지 누락 시 exit code `1` 확인
- [x] `lua run /no/script.lua --dry-run --json` 없는 파일 입력 시 exit code `2` 확인
- [x] `update --help` 도움말 노출 확인
- [x] `update check --asset net8 --json`으로 GitHub latest release 조회 확인
- [x] 현재 latest release가 통합 zip asset만 제공할 때 `--asset net8`이 zip fallback을 선택하는지 확인
- [x] `update apply --asset net8 --json` dry-run 출력과 외부 updater 실행 안전장치 확인
- [x] `update download --asset bad --out <dir>` 잘못된 asset 입력 시 exit code `1` 확인
- [x] `update apply --execute --asset net10 --updater /no/updater` 기존 updater 미지원 asset 차단 확인
- [x] `config path --config <tmp> --json` 경로와 존재 여부 출력 확인
- [x] `config set/get/list/unset` 임시 JSON 파일 기반 동작 확인
- [x] `config get <missing> --json` 누락 key에서 exit code `2` 확인
- [x] `default-wz` 설정 후 `info --config <tmp>`가 positional input 없이 설정 경로를 fallback으로 쓰는지 확인
- [x] `plugin --help`, `plugin list --json`, `plugin inspect <wcr2.dll> --json` 출력 확인
- [x] 깨진 DLL이 있는 `plugin list --plugin-dir <tmp> --json`가 exit code `0`으로 계속 진행하고 `LoadFailedCount`/`Error`를 보고하는지 확인
- [x] `/tmp` 테스트 플러그인이 `ICliCommandProvider`로 `hello` 명령을 등록하고 `plugin commands --plugin-dir <tmp> --json`에서 탐지되는지 확인
- [x] `/tmp` 테스트 플러그인을 `plugin run hello Codex --plugin-dir <tmp> --json`으로 실행하고 stdout이 JSON `Stdout` 필드에 캡처되는지 확인
- [x] Phase 17 문서화: README CLI 섹션, `docs/cli-migration.md`, `samples/cli/*.sh`, `docs/cli.md` first-run/JSON contract 추가
- [x] Phase 18 테스트 프로젝트 추가: `WzComparerR2.Cli.Tests`
- [x] Debug test harness 실행: 13개 통과
- [x] Release test harness 실행: 13개 통과
- [x] Azure pipeline에 `Run CLI tests` 단계 추가
- [x] Windows x64 self-contained CLI publish 생성: `artifacts/wcr2-win-x64-self-contained/`
- [x] Windows x64 CLI zip 생성: `artifacts/wcr2-win-x64-self-contained.zip`
- [x] `wcr2.exe`가 Windows x64 PE console executable인지 확인
- [x] Windows 실클라 테스트 체크리스트 작성: `docs/windows-cli-test-checklist.md`
- [x] Windows 실클라 smoke script 작성: `samples/cli/windows-maple-smoke.ps1`
- [x] Windows 실클라 실행 결과 반영
  - 기본 root WZ 기준 체크리스트: 17 passed, 16 failed
  - split `Data` directory/shard retry: 14 passed, 0 failed
  - 확인된 성공 입력: `Data\String`, `Data\Skill`, `Data\Item`, `Data\Character\Cap`, `Data\Map\Map\Map1\Map1_000.wz`, `Data\Mob_Canvas`
  - `samples/cli/windows-maple-smoke.ps1`와 `docs/windows-cli-test-checklist.md`를 split layout 자동/수동 후보 기준으로 업데이트
- [x] `dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false -v:minimal` 재검증 통과
- [x] `git diff --check` 공백 오류 없음
- [x] 현재 저장소 안에서 `.wz`, `.img`, `.ms`, `.patch` 샘플 파일을 찾지 못함
- [x] 현재 macOS 환경 PATH에서 `lua`, `lua5.4`, `lua5.3`, `luajit` 실행기를 찾지 못함
- [ ] 실제 WZ/MS 샘플 기반 `info/tree/list/search/compare/dump/extract` 검증 필요
- [ ] 실제 WZ/MS 샘플 기반 `skill/item/gear/map info`, `animate frames` 검증 필요
- [ ] 실제 WZ/MS 샘플 기반 `map objects/portals/life/reactors` 검증 필요
- [ ] 외부 lua 실행기와 CLI용 Lua API 기반 실제 script 실행 검증 필요
- [ ] Network 실제 서버 protocol handshake 검증 필요
- [ ] 실제 patch 파일 기반 `patch inspect` 검증 필요
- [ ] 실제 patch 파일과 target 폴더 기반 `patch dry-run` 검증 필요
- [ ] 실제 patch 파일과 target 폴더 기반 `patch apply` 검증 필요

## 주요 리스크

- [ ] WinForms와 도메인 로직이 섞인 기능은 분리 비용이 큼
- [ ] MapRender/Avatar는 headless 렌더링에서 graphics device 문제가 생길 수 있음
- [ ] native DLL 배포와 architecture 선택이 CLI에서도 필요함
- [ ] 테스트 fixture로 쓸 수 있는 WZ/MS 샘플 확보가 필요함
- [ ] GUI에서 자연스러운 기능이 CLI에서는 명령/옵션으로 재설계되어야 함
- [ ] 기존 프로젝트는 analyzer/nullable이 꺼져 있어 리팩터링 안정망이 약함

## 보류 결정 사항

- [ ] CLI 타깃을 `net8.0` 단독으로 할지, `net462`까지 지원할지
- [ ] command parser를 직접 구현할지, 별도 패키지를 쓸지
- [ ] CLI 서비스 레이어를 새 프로젝트로 둘지, `WzComparerR2.Common`에 둘지
- [ ] Network 기능을 실제로 CLI 핵심 범위에 넣을지
- [ ] Updater self-update를 지원할지
- [ ] MapRender를 완전 headless render까지 할지, metadata/export 중심으로 제한할지
