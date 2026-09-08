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
- [x] `extract video` 구현 가능성 조사
  - `Wz_Video`
  - `VpxVideoDecoder`
  - native `libvpx`, `libyuv` 필요성 확인
- [x] `Wz_Video` 원본 blob `.mcv` 추출 구현
- [x] `video list/export/export-all` 전용 명령을 추가한다.
  - `video list`는 MCV 헤더의 FourCC, 해상도, frame count, alpha map 여부를 출력한다.
  - `video export --format mcv`는 원본 `.mcv`를 저장한다.
  - `video export --format frames|png|gif|both`는 ffmpeg로 MCV VP9 base/alpha stream을 디코딩한다.
- [x] export 결과 manifest 생성 옵션 추가
  - `--manifest export.json`

### Phase 5A. 이미지/사운드 전용 추출 UX 보강

- [x] `sound list` 또는 `asset list --type sound`로 사운드 노드 경로/길이/채널/주파수를 먼저 찾을 수 있게 한다.
- [x] `sound export` / `sound export-all` 전용 명령을 추가한다.
  - 기존 `Wz_Sound.ExtractSound()`와 `ExtractExporter` 경로를 재사용한다.
  - MP3/PCM WAV/raw fallback, manifest, hash/bytes 검증 값을 출력한다.
- [x] `image list` 또는 `asset list --type png`로 PNG 노드 경로/크기/format/pages를 먼저 찾을 수 있게 한다.
- [x] `image export` 전용 명령을 추가한다.
  - Windows에서는 기존 `Wz_Png.ExtractPng()` 기반 PNG 저장 경로를 재사용한다.
  - macOS/Linux에서는 CLI cross-platform PNG writer로 common texture format을 직접 저장하고, 미지원 포맷은 명확한 진단을 반환한다.
- [x] `image search` 유사 이미지 검색 명령을 추가한다.
  - query PNG를 alpha crop한 뒤 pHash/색상/부분 영역 비교로 WZ PNG 후보를 찾는다.
  - `--out` 지정 시 상위 후보 PNG와 manifest를 같이 남길 수 있게 한다.
  - `--data-dir <Data>`만으로 `UI/Item/Skill/Effect/Character/...`의 `_Canvas` root를 자동 스캔할 수 있게 한다.
  - `--scope ui,item,skill`로 agent가 검색 범위를 줄일 수 있게 한다.
  - OS 사용자 cache에 root별 fingerprint index를 gzip JSON으로 저장하고, `--cache-dir`, `--no-cache`, `--rebuild-cache`, `--trust-cache`를 지원한다.
  - 큰 query는 기본 size prefilter로 작은 아이콘 후보를 제외한다. cache key는 query 크기와 독립되어 같은 root의 다른 query에도 재사용된다.
  - cache/index 점수로 넓은 후보 pool을 잡은 뒤, 기본적으로 top 후보를 WZ에서 다시 열어 pixel-level score로 refine한다. 빠른 agent probe에는 `--probe` 또는 `--trust-cache --no-refine`을 쓸 수 있다.
  - [x] 2026-09-08 image search Phase A: top-K 후보 유지 로직을 반복 full sort에서 bounded replacement로 변경해 cache hit scoring 비용을 줄였다.
  - [x] 2026-09-08 image search probe mode: `--probe` CLI flag와 agent/MCP `probe` option 추가. 정확도 보존 모드가 아니라 빠른 1차 후보 탐색용으로 문서화했다.
  - [x] 2026-09-08 image search agent memory cache: `wcr2-agent serve`/MCP 프로세스 안에서 root image index를 LRU로 보관하고 `cache.stats`/`wcr2.cache_stats`에 `imageSearchIndex*` 통계를 노출한다.
  - [x] 2026-09-08 image search scoring pruning: 현재 top-K floor를 넘을 수 없는 cache fingerprint pair의 color/shape 계산을 생략한다. mob query 기준 기존 top 30 diff 없음, probe 약 3.0초, refine 약 6.95초.
  - [x] 2026-09-08 image search binary sidecar cache: 기존 `.json.gz` cache를 유지하면서 같은 hash의 `.bin.gz`를 생성/우선 읽기 한다. mob query 기준 binary hit 후 probe 약 1.81초, refine 약 5.72초, 기존 top 30 diff 없음.
  - [x] 2026-09-08 image search background trim: `--trim-background`/agent `trimBackground`로 모서리 기반 배경 제거 query를 사용한다. 흰 배경을 붙인 mob query에서 기본 검색 1순위 `8610004.img\attack2\info\hit\3`, trim 검색 1순위 `8880725.img\stand\8`로 목표 계열 복구 확인.
  - [x] 2026-09-08 image search internal video frame target: query는 PNG로 유지하고, `--include-video`/agent `includeVideo`가 내부 MCV/Wz_Video frame을 검색 대상으로 포함한다. 직접 `5241503 screen2/video` smoke에서 `Type=video-frame`, `FrameIndex=0`, score 1.0, `--out` PNG 추출 확인.
  - 2026-09-07 검증: `1788776940228-jmgl86.png`는 `--data-dir ... --scope ui`로 `UIWindowEvent5.img\2606UltimaStory\enterUI\back`를 1순위로 찾았다. 단, query는 여러 레이어가 합성된 화면이라 단일 PNG는 배경 계층만 일치한다. root index 생성은 약 2분 23초, cache hit+refine은 약 3.6초, `--trust-cache --no-refine`은 약 1.5초였다.
- [x] macOS PNG 직접 저장 가능성을 별도 조사한다.
  - 코어 로직 변경 최소화를 우선하고, CLI 전용 PNG writer로 `ARGB4444`, `ARGB8888`, `ARGB1555`, `RGB565`, `DXT3`, `DXT5`, `A8`, `RGBA1010102`, `BC7`를 우선 지원한다.
- [x] split `Data` layout 경로 차이를 문서화한다.
  - Windows 예: `Data\Mob_Canvas`
  - macOS/CrossOver 예: `Data/Mob/_Canvas`
- [x] 실제 WZ 샘플 기반 media golden 검증을 추가한다.
  - `WCR2_TEST_DATA_DIR`가 있는 환경에서만 실행되는 optional test로 추가한다.
  - PNG golden: `Mob/_Canvas` 또는 `Mob_Canvas`의 `0100100.img/stand/0`, dimensions/hash/manifest DTO와 실제 파일 bytes/hash 검증.
  - Sound golden: `Sound/AchievementEff.img/GradeUp`, MP3 bytes/hash/ms/channels/frequency 검증.
  - Video golden: `Packs/Skill_00006.ms`의 `5241503 screen2/video`, ffmpeg가 있을 때 첫 frame PNG dimensions/hash/frame metadata 검증.

완료 기준:

- [x] 이미지 노드를 PNG 파일로 추출 가능
- [x] 사운드 노드를 파일로 추출 가능
- [ ] export 실패 시 실패 노드 목록을 JSON으로 받을 수 있음

### Phase 5B. macOS/CrossOver Item/String 패키지 로딩 지원

배경:

- macOS/CrossOver 설치의 `Data/Item`, `Data/Item/Cash`, `Data/Item/Cash/_Canvas`, `Data/Item/Consume`, `Data/Item/Pet`, `Data/String`은 현재 `WzLoadContext.Load(...)`에서 `The file is not a valid wz file.`로 실패한다.
- 같은 설치에서도 `Data/Mob/_Canvas`와 `Data/Character/Cap`은 정상 로딩된다.
- 헤더 증거:
  - 정상 로딩 예: `Data/Mob/_Canvas/_Canvas_000.wz` = `PKG1...`
  - 정상 로딩 예: `Data/Character/Cap/Cap_000.wz` = `PKG1...`
  - 실패 예: `Data/Item/Cash/Cash.wz` = `a3 f3 00 f9 ...`
  - 실패 예: `Data/Item/Cash/Cash_000.wz` = `1c 15 1f aa ...`
  - 실패 예: `Data/Item/Cash/_Canvas/_Canvas_000.wz` = `c3 08 31 86 ...`
  - 실패 예: `Data/String/String_000.wz` = `07 79 dd be ...`
  - 실패 예: `Data/Skill/_Canvas/_Canvas_000.wz` = `cc 05 e4 3e ...`
- 현재 `Wz_File.GetHeader`는 `PKG1`, `PKG2`, 그리고 KMST1201 random header 후보만 유효 헤더로 받아들인다. 위 실패 샘플들은 첫 4바이트가 `PKG1/PKG2`가 아니며, 현재 KMST1201 dataSize gather 오프셋도 파일 크기-68과 일치하지 않았다.
- `Data/Item/*.ini`와 `Data/String/String.ini`는 현재 확인한 범위에서 `LastWzIndex|0` 정도만 담고 있어 복호화/컨테이너 메타데이터로 쓰기 어렵다.
- 이 문제 때문에 `미라클 큐브`, `호신부적`, `운명의 수레바퀴`, `보따리상인 묘묘(7일)`, `고성능 순간이동의 돌`, `MSW 아바타 코디 이용권(30일)`, `펫`, `고성능 확성기`, `아이템 확성기` 같은 Cash/Item 아이콘의 `info/icon` PNG를 macOS 로컬 데이터에서 추출하지 못한다.

작업:

- [x] 실패 파일의 컨테이너/암호화/압축 포맷을 식별한다.
  - `Item/Cash/Cash_000.wz`
  - `Item/Cash/_Canvas/_Canvas_000.wz`
  - `Item/Consume/Consume_000.wz`
  - `Item/Pet/Pet_000.wz`
  - `String/String_000.wz`
  - 2026-06-25 확인: Kagamia upstream의 KMST1202 150-byte randomized PKG2 header와 일치한다.
- [x] 기존 `Wz_Structure.LoadFile`이 지원하는 헤더 경로와 새 패키지 포맷의 차이를 정리한다.
- [x] GUI 또는 upstream WzComparerR2 계열에서 같은 포맷을 읽는 코드가 있는지 조사한다.
  - Kagamia master의 KMST1202 64-bit PKG2 header/read path를 기준으로 최소 이식했다.
- [x] 코어 로직 변경 범위를 정한다.
  - 최소안: CLI 전용 pre-decode/adapter로 기존 WzLib 입력에 맞춘다.
  - 중간안: WzLib에 새 package reader를 추가하되 기존 `PKG1` 경로는 건드리지 않는다. 선택됨.
  - 보류안: Windows에서 추출 가능한 데이터만 공식 지원하고 macOS Item/String은 제한으로 문서화한다.
- [x] `info/tree/search/image list/image export`가 새 Item/String 패키지에서 동작하게 한다.
  - `String_000.wz` info/search, `Cash_000.wz` tree, `Cash/_Canvas` image list/export 검증.
- [x] 이름 기반 아이템 아이콘 추출 플로우를 추가한다.
  - `item icon --name <exact-name>|--id <id> --out <dir>` 구현.
  - `String`에서 item name -> item id/category 조회.
  - `Item/<category>/_Canvas`에서 `<group>.img/<id>/info/icon` 또는 `iconRaw` 조회.
  - `[7일]보따리상인 묘묘`처럼 기간 표기가 앞/뒤나 괄호 형태로 다른 경우 이름을 정규화해 조회.
  - 기간제 string id에 개별 icon이 없으면 `5450007 -> 5450000`처럼 10/100/1000 단위 대표 icon fallback을 진단과 함께 출력.
- [ ] 요청 샘플 9개 아이템의 실제 `info/icon` PNG를 `.test` 아래에 생성한다.
  - 확인 가능한 7개 생성: `.test/wcr2-requested-item-icons-kmst1202-20260625`
  - `item icon` 명령으로 재생성한 결과: `.test/wcr2-item-icon-command-20260625`
  - `MSW 아바타 코디 이용권(30일)`은 현재 `String_000.wz` 검색 결과 없음.
  - `펫`은 단일 exact item name이 아니라 카테고리/묶음명으로 보여 ID 확정 필요.
- [x] 실패 시 `not valid wz` 대신 “지원되지 않는 macOS package format” 진단과 대상 파일 헤더를 JSON으로 보고한다.

완료 기준:

- [x] macOS/CrossOver `Data/Item/Cash/_Canvas`를 CLI에서 로딩할 수 있다.
- [x] macOS/CrossOver `Data/String`에서 한국어 아이템 이름 검색이 가능하다.
- [x] 위 9개 아이템 중 확인 가능한 항목의 실제 WZ icon PNG가 추출된다.
- [ ] Windows 기존 `Data\Item`/`Data\String` 동작이 깨지지 않는다.

### Phase 5C. CLI Program.cs 행동 유지형 리팩토링

배경:

- `WzComparerR2.Cli/Program.cs`가 8천 줄을 넘어 CLI 라우팅, 인자 파싱, WZ 로딩, repository, media export, DTO, plugin/update/patch 로직이 한 파일에 누적되어 있다.
- Phase 5B의 Item/String 패키지 로딩은 `WzLoadContext`, `CliWzRepository`, `MediaAssetFinder`, `ExtractExporter`를 함께 건드릴 가능성이 높아, 먼저 파일 경계를 정리해야 변경 리스크와 토큰 사용량을 줄일 수 있다.
- 현재 목표는 기능 변경이 아니라 동작을 유지한 채 읽기 쉬운 파일 단위로 이동하는 것이다.

작업:

- [x] 리팩토링 전 `WzComparerR2.Cli.Tests` Release harness를 통과시켜 현재 동작을 잠근다.
- [x] `ParsedArgs`를 별도 파일로 분리한다.
- [x] 공통 CLI context/plugin 타입을 별도 파일로 분리한다.
- [x] `WzLoadContext`, `WzLoadOptions`, `CliWzRepository` 계층을 별도 파일로 분리한다.
- [x] media/image/sound 탐색과 export 계층을 별도 파일로 분리한다.
- [x] domain info 탐색/DTO 계층을 별도 파일로 분리한다.
- [x] 공통 WZ node/search DTO와 path helper를 별도 파일로 분리한다.
- [x] compare/patch DTO와 helper 타입을 별도 파일로 분리한다.
- [x] config store/DTO 계층을 별도 파일로 분리한다.
- [x] update DTO/client 계층을 별도 파일로 분리한다.
- [x] lua/network DTO와 실행 helper를 별도 파일로 분리한다.
- [x] animation frame/GIF export DTO와 helper를 별도 파일로 분리한다.
- [x] skill DTO와 writer/helper 타입을 기능별 파일로 분리한다.
- [x] map/avatar DTO와 render plan 타입을 기능별 파일로 분리한다.
- [x] help/plugin option helper를 `Program.Help.cs` partial 파일로 분리한다.
- [x] patch 명령 실행 흐름을 `Program.Patch.cs` partial 파일로 분리한다.
- [x] compare 명령 실행 흐름과 node diff helper를 `Program.Compare.cs` partial 파일로 분리한다.
- [x] dump/extract/media 명령 실행 흐름을 `Program.Media.cs` partial 파일로 분리한다.
- [x] domain/skill/animate/avatar/map/lua/network 명령 실행 흐름을 `Program.DomainCommands.cs` partial 파일로 분리한다.
- [x] update/config/plugin 명령 실행 흐름을 `Program.AppCommands.cs` partial 파일로 분리한다.
- [x] `Program.cs`는 `Main`, command routing, 공통 helper 중심으로 축소한다.
- [x] 커밋 전 CLI 생성 파일을 `Commands/`, `Infrastructure/`, `Wz/`, `Domain/`, `Media/`, `Models/` 폴더로 정리한다.
- [x] 추가된 `item icon` 구현을 작은 파일로 분리한다.
  - `ItemIconExporter`: 실행 흐름
  - `ItemIconPaths`: 입력/카테고리/icon path 계산
  - `ItemStringResolver`: String name/id 조회
  - `ItemIconModels`: 출력 DTO
- [x] `Program.DomainCommands.cs`의 대형 명령 블록을 기능별 partial 파일로 분리한다.
  - `Program.Skill.cs`
  - `Program.Animate.cs`
  - `Program.Avatar.cs`
  - `Program.Map.cs`
  - `Program.LuaNetwork.cs`
- [x] 각 분리 단계 후 build/test를 실행하고 `git diff --check`를 확인한다.

완료 기준:

- [x] `Program.cs`가 command routing 중심으로 축소된다.
- [x] `Program.DomainCommands.cs`가 domain info/item icon 라우팅 중심으로 축소된다.
- [x] 현재 분리 범위에서 CLI 공개 동작과 JSON 계약이 변경되지 않는다.
- [x] `dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Release --no-restore` 통과
- [x] `dotnet run --project WzComparerR2.Cli.Tests/WzComparerR2.Cli.Tests.csproj -c Release --no-restore -- --cli WzComparerR2.Cli/bin/Release/net8.0/wcr2.dll` 통과
- [x] `git diff --check` 통과

### Phase 5D. 스킬 스프라이트 `_outlink` 추출 인터페이스

배경:

- `Skill/112.img/skill/<id>` 같은 스킬 메타데이터 노드는 `icon`, `effect`, `hit`에 1x1 stub PNG를 들고, 실제 픽셀은 `_outlink = Skill/_Canvas/...`로 분리될 수 있다.
- 일반 `image export`는 선택한 노드의 현재 PNG만 추출하므로, 사용자가 스킬 ID 기준으로 실제 스프라이트를 뽑으려면 `_outlink` 대상 Canvas 입력을 직접 찾아야 했다.
- 기존 WzComparer/WzLib 엔진은 유지하고 CLI 레이어에서만 outlink 해석과 Canvas 후보 순회를 제공하는 것이 가장 안전하다.

작업:

- [x] `skill sprite [<skill-wz>] --id <id> --out <dir>` 명령을 추가한다.
- [x] `skill export [<skill-wz>] --id <id> --out <dir>` 명령을 추가해 스프라이트와 사운드를 같이 추출한다.
- [x] `--branch icon,effect,hit` 또는 `--branch effect,hit/0`처럼 스킬 하위 branch를 선택할 수 있게 한다.
- [x] 스킬 branch의 첫 `_outlink`를 기준으로 branch 대상 경로를 계산한다.
- [x] `--canvas-wz <path>` 명시 입력과 `--data-dir <Data>` 기반 `Data/Skill/_Canvas`, `Data/Packs/Skill*.ms` 후보를 순회한다.
- [x] `--sound-wz <path>` 명시 입력과 `--data-dir <Data>` 기반 `Data/Sound` 후보에서 `Sound/Skill.img/<skillId>` 사운드를 추출한다.
- [x] `skill export` 기본 branch를 자동 감지로 바꿔 `screen2`, `screen3`, `tile`, `special*`, `effect2` 같은 실제 visual branch를 누락하지 않게 한다.
- [x] `skill export`가 action/delay 기반 related asset key를 자동 수집하게 한다.
  - 스킬 대표 노드에 action 값이 없으면 `Data/Packs/Skill*.ms` 메타데이터에서 `6thFireCracker` 같은 seed를 보강한다.
  - `--related-key <name>`, `--related-wz <path>`, `--include-related`, `--skip-related` 옵션을 제공한다.
  - related 검색 대상은 `Skill/_Canvas`, `Effect/_Canvas`, `Character/_Canvas`, `Character/Afterimage`로 제한한다.
- [x] `skill export`가 같은 skill id의 내부 `Wz_Video` 컷신을 자동 포함한다.
  - 대표 `Data/Skill` 노드에 비디오가 없으면 `Data/Packs/Skill*.ms`에서 같은 skill id의 메타데이터 노드를 찾아 `screen*/video`를 추출한다.
  - 확인 사례: `5241503` 파이어크래커는 `Data/Packs/Skill_00006.ms :: Skill/524.img/skill/5241503/screen2/video`를 포함해 12개 MCV 컷신 레이어를 추출한다.
  - `--video-format mcv|frames|png|gif|both`, `--skip-video`, `--ffmpeg <path>` 옵션을 제공한다.
  - `skill export`의 기본 비디오 출력은 PNG 프레임이며, 원본 `.mcv`가 필요할 때만 `--video-format mcv`를 사용한다.
- [ ] action key와 실제 외부 컷신 이미지 경로가 이름으로 직접 매칭되지 않는 스킬을 위한 추가 매핑/역추적을 확장한다.
  - 내부 `screen*/video`는 해결됨. 외부 Effect/Character 전용 컷신이 action key 이름과 다른 경로에 있을 경우는 추가 조사 대상이다.
- [x] outlink 대상 context가 살아있는 동안 PNG export를 수행해 closed stream 문제를 피한다.
- [x] JSON에 `Status`, `OutlinkPath`, `ResolvedPath`, `TriedCanvasInputs`, `TriedSoundInputs`, 파일 `Bytes/Sha256`을 출력한다.
- [x] `skill sprite/export` 출력 폴더에 `skill-info.json`, `resources.json` sidecar를 추가해 실제 스킬 설명과 리소스 branch 경로/상태/파일 목록을 같이 저장한다.
- [x] `resources.json` 파일 항목에 PNG/sound/MCV intrinsic metadata와 frame `origin`/`z`/`delay`/`_outlink` direct metadata를 포함한다.
- [x] `_outlink`를 따라 Canvas PNG를 저장한 경우 원래 skill metadata stub의 같은 relative frame 값을 파일 DTO에 다시 연결한다.
- [x] `--data-dir` 스킬 조회가 canvas/visual-only 노드를 먼저 잡으면 `Data/Packs/Skill_*.ms`에서 더 풍부한 metadata 노드를 추가 확인한다.
- [x] `Data/Packs/Skill_*.ms` 내부 검색에서 `psdSkill/<id>` marker보다 `.../skill/<id>` 실제 스킬 노드를 우선하고, 첫 약한 lazy match 대신 metadata 점수가 높은 후보를 선택한다.
- [x] `resources.json` image resource에 metadata 입력/branch source, resolved canvas 입력, branch-level `action`/`time`/`repeat` metadata를 기록한다.
- [ ] `psdSkill` 같은 파생/모드 marker가 빈 노드이고 실제 variant PNG에도 `origin`/`delay`/`z`가 없는 경우, 대표 스킬과 variant 리소스의 결합 export 정책을 설계한다.
- [x] 대량 추출 `skill-info.json`의 설명 누락을 점검하고, name-only 스킬과 같은 이름 설명 후보를 분리한 리포트를 남긴다.
- [x] 스킬 설명 resolver가 `#c10...#` 색상 태그를 unresolved placeholder로 오인하지 않게 보정한다.
- [ ] 같은 이름 설명 후보를 공식 필드에 덮어쓰지 않고 별도 fallback candidate 필드 또는 명시 옵션으로 노출하는 방식을 설계한다.
- [x] 비디오가 없는 스킬에서 빈 `video/` 폴더가 생기지 않게 `VideoExporter` 출력 디렉터리 생성을 실제 비디오 발견 이후로 늦춘다.
- [x] `skill export-batch`를 추가해 여러 스킬을 한 CLI 프로세스에서 추출하고 Skill/String repository 및 Canvas/Sound/Skill*.ms context를 재사용한다.
  - `--ids`, `--ids-file`, `--out-root`, `--manifest`, `--skip-existing`, `--continue-on-error`를 지원한다.
  - 텍스트 `--ids-file`은 `id` 또는 `id<TAB>relative/output` 형식을 지원해 같은 skill id를 여러 직업 폴더에 배치할 수 있다.
  - 각 항목 폴더에 `skill-info.json`, `resources.json`, `export-result.json`을 직접 쓴다.
- [x] 스킬 이름 기반 검색/해석 CLI를 추가한다.
  - `skill search-name --name <text>`는 `String/Skill.img`에서 이름 후보를 찾고 data node 존재 여부, `SourceProfile`, visual branch 정보를 같이 보여준다.
  - `skill resolve-name --name <text> --job-code <code>`는 단일 skill id로 확정 가능한지 확인하고, 동명이인/파생 ID가 남으면 `ambiguous`로 실패한다.
  - `--job-code`는 `floor(skillId / 10000)` 기준으로 정확 매칭한다.
  - 이름 비교는 공백/기호 차이를 정규화하고 짧은 오타는 fuzzy 후보로 노출한다.
- [x] `skill export-batch --names-file <path>`를 추가한다.
  - 텍스트 `--names-file`은 `name`, `jobCode<TAB>name`, `jobCode<TAB>name<TAB>relative/output`, `jobName<TAB>jobCode<TAB>name<TAB>relative/output` 형식을 지원한다.
  - JSON `--names-file`은 문자열 이름 배열 또는 `{ name, jobCode, relativeOutput }` 객체 배열을 지원한다.
  - 배치 내부에서 String 후보 목록과 data profile을 캐시한다.
  - resolve 실패 항목은 manifest에 `ResolveStatus`와 후보 목록을 남긴다.
- [x] fixture-free CLI 테스트에 도움말/필수 인자 검증을 추가한다.
- [x] macOS/CrossOver 실클라에서 `1121008`의 `icon`, `effect`, `hit/0` 실제 PNG와 `Use/Hit.mp3` 추출을 검증한다.

완료 기준:

- [x] 기존 WzLib/GUI 엔진 수정 없이 CLI 계층만으로 스킬 스프라이트 export 인터페이스가 생긴다.
- [x] `_outlink` stub이 실제 Canvas PNG로 해석되면 1x1이 아닌 실제 치수의 PNG가 생성된다.
- [x] `skill export`가 스킬 사운드까지 함께 추출한다.
- [x] Canvas 입력을 읽을 수 없거나 경로가 없으면 branch별 진단을 JSON으로 받을 수 있다.

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
- [x] CLI용 WZ repository/find service를 만든다.
  - `PluginManager.FindWz` 이벤트/WinForms 의존 없이 `FindWz("Skill/1100.img/skill/11001025")` 같은 경로 조회를 제공한다.
  - 입력 후보: `--skill-wz`, `--item-wz`, `--character-wz`, `--map-wz`, `--mob-wz`, `--npc-wz`, `--quest-wz`, `--string-wz`, `--data-dir`.
  - 현재 적용 범위: `skill full`과 `skill/item/gear/mob/npc/quest/map info`의 data/string repository. `etc/base` 후보 확장은 후속 단계.
  - split layout 후보: `Data/Skill`, `Data/String`, `Data/Item`, `Data/Etc`, `Data/Quest`.
- [ ] `StringLinker` 초기화를 CLI에서 수행한다.
  - [x] 현재 `--string-wz` 단일 보강은 구현됨.
  - [ ] `StringLinker.Load(stringNode, itemNode, etcNode, questNode)`와 호환되는 입력 로딩을 제공한다.
  - [x] `skill full --allow-string-only`에서 string-only 상태를 출력에 명확히 표시한다.
  - [x] CLI headless full-linker 상태를 출력에 명확히 표시한다.
    - `LinkerStatus`로 data/string/stats/visuals/summary 해석 여부와 `GuiStringLinkerLoaded=false` 제한을 JSON/XML/text에 노출한다.
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
- [x] canvas 중심 스킬 데이터 진단을 추가한다.
  - `skill full` 출력에 `SourceProfile`, `StatPropertyCount`, `VisualBranches`를 추가해 `common`/`level`이 비는 이유를 구분한다.
  - macOS CrossOver `Data/Skill`의 `1001008`은 `SourceProfile = visual-only`, `StatPropertyCount = 0`으로 확인됨.
- [x] 미해결 summary placeholder를 구조화한다.
  - `Diagnostics` 문자열 외에 `UnresolvedPlaceholders` 배열과 `LinkerStatus.UnresolvedPlaceholderCount`를 출력한다.
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
- [x] Agent Runtime 계획 문서 작성: `docs/agent-runtime-plan.md`
- [x] Agent Runtime Phase 1 뼈대 구현
  - `WzComparerR2.Headless` class library 추가
  - `WzComparerR2.AgentHost` exe 추가
  - `wcr2-agent run --job <job.json> [--json]` 추가
  - 빈 job, `noop`, 알 수 없는 step, 잘못된 JSON 계약 테스트 추가
- [x] Agent Runtime Phase 2: `image.search` Headless 서비스 분리 및 job step 연결
  - CLI `wcr2 image search`와 `wcr2-agent` `image.search` step이 같은 Headless 구현을 사용
  - 2026-09-07 Sunday Maple 배너 query로 CLI/AgentHost 양쪽 검증 통과
- [x] Agent Runtime Phase 3: `image.export-related` job step 추가
  - `fromStep`으로 `image.search` 결과를 참조
  - `ParentPath` 기준 group PNG export 및 manifest 생성
  - 2026-09-07 Sunday Maple 그룹 6개 PNG 추출 검증 통과
- [x] Agent Runtime Phase 4A: `skill.export` / `skill.export-batch` job step 추가
  - 초기 구현은 기존 `wcr2 skill export/export-batch`를 호출하는 CLI bridge 방식
- [x] Agent Runtime Phase 4B: 기존 skill sprite/export logic을 Headless 서비스로 직접 이동
  - `ParsedArgs`, `UsageException`, WZ 로더, domain finder/name resolver, media exporter, skill sprite/batch exporter를 `WzComparerR2.Headless`로 이동
  - `wcr2-agent` `skill.export` / `skill.export-batch`는 프로세스 생성 없이 in-process exporter 호출
  - 단건 result sidecar: `agent-skill-export-result.json`
  - 배치 result sidecar: `agent-skill-batch-result.json`
  - `3141000` direct agent smoke: 단건/batch 각각 32개 추출, `Origin`/`Delay` metadata 유지 확인
  - `5241503` direct agent smoke: image 150개, sound 8개, video frame 356개 추출 확인
- [x] Agent Runtime Phase 4C: skill related cross-root effect export direct 검증
  - `.test/wcr2-agent-skill-related-20260907/job.json`에서 `skill.export` direct step으로 explicit `relatedWz`/`relatedKey`를 검증
  - `3141000` `branch=icon`, `relatedWz=Data/Effect/_Canvas/_Canvas_001.wz`, `relatedKey=nodepoint`, `maxRelatedInputs=1`, `maxRelatedMatches=1`
  - 결과: sprite 1개, sound 4개, related PNG 3개, video 0개, 총 8개 추출
  - `agent-result.json`에 `cliPath` 없이 in-process `command`만 남는 것 확인
- [x] Agent Runtime Phase 4D: xlsx 기반 skill batch recipe step 추가
  - `skill.export-xlsx` agent step 추가
  - `.xlsx` workbook을 `_agent/<stepId>-names.tsv`로 변환한 뒤 기존 `skill.export-batch` Headless 경로 재사용
  - 기본 output pattern: `{jobCode}_{jobName}/{id}_{name}`
  - 자동 header: `직업`, `직업 코드`, `스킬`/`skill`; 필요 시 `jobNameColumn`, `jobCodeColumn`, `skillColumns`, `headerRow`, `firstDataRow` 명시
  - `.test/wcr2-agent-xlsx-skill-export-20260907/rerun4`: `보우마스터/314/폭풍의 시 VI` 1건을 `314_보우마스터/3141000_폭풍의 시 VI`로 추출, 총 5개 파일
- [x] Agent Runtime Phase 5: item/map recipe step 추가
  - `item.icon`: item id/name 기반 icon PNG 추출을 agent에서 직접 호출
  - `item.export`: `item-info.json`, `item-icon-result.json`, `agent-item-export-result.json` 저장
  - `map.export`: `map-info.json`, `map-metadata.json`, `agent-map-export-result.json` 저장
  - `itemId`/`mapId`를 selector로 사용; 숫자-only step id는 selector fallback으로 허용
  - `.test/wcr2-agent-item-map-20260907`: `미라클 큐브` icon 1개, `빨간 포션` item export icon 1개, `헤네시스` portals 37/life 35/objects 1603/reactors 0 확인
- [x] Agent Runtime Phase 6A: `serve --stdio` 기본 protocol/lifecycle 추가
  - newline-delimited JSON request/response
  - `ping`, `run`, `shutdown` method 지원
  - `run`은 `jobPath` 또는 inline `job` object 지원
  - `id`/`requestId`를 response `id`로 보존
  - response는 한 줄 compact JSON으로 출력
  - `WzComparerR2.Cli.Tests`: `agent serve stdio handles ping run shutdown` 추가
- [x] Agent 전용 quickstart 문서 추가
  - `docs/agent-quickstart.md`
  - 새 에이전트 read order, 수정 경계, 대표 job, 검증 명령, 현재 한계 정리
  - `AGENTS.md`, `docs/cli.md`, `docs/agent-runtime-plan.md`의 stale agent 설명 보정
- [x] Agent Runtime Phase 6B: serve process 안에서 skill export WZ session cache 재사용
  - `skill.export`, `skill.export-batch`, `skill.export-xlsx`가 동일한 스킬 입력/옵션 조합이면 같은 serve process 안에서 WZ repository/session 재사용
  - `run` result의 `cacheStats`, skill step의 `cacheStatus`, serve `cache.stats`/`cache.clear`로 상태 확인 및 초기화
  - cache size는 현재 skill session 4개, 초과 시 least-recently-used 세션 dispose
  - `.test/wcr2-agent-serve-cache-20260908`: `3141000` `keydown` 두 번 연속 추출에서 첫 요청 `cache-miss`, 두 번째 요청 `cache-hit`, 각 20개 파일 확인
- [x] Agent Runtime Phase 6C: item/map domain repository 및 WZ context cache 확장
  - `item.icon`, `item.export`, `map.export`가 같은 serve process 안에서 domain repository 및 WZ context cache 재사용
  - `cacheStats`에 skill session, domain repository, WZ context count/hit/miss/eviction 기록
  - step `cacheStatus`는 단일 cache hit/miss 또는 복수 cache가 섞인 `cache-mixed`로 기록
  - `.test/wcr2-agent-serve-cache-20260908/serve-output-phase6c.jsonl`: `item.icon`/`item.export`/`map.export` 반복 요청에서 WZ context 및 domain repository hit 확인
- [x] Agent Runtime Phase 7: MCP stdio wrapper 추가
  - `wcr2-agent mcp --stdio` command 추가
  - MCP JSON-RPC request 처리: `server/discover`, legacy `initialize`, `ping`, `tools/list`, `tools/call`
  - `wcr2.run_job`은 `jobPath` 또는 inline `job` object로 기존 agent job 실행
  - 대표 tool 노출: `wcr2.skill_export`, `wcr2.skill_export_batch`, `wcr2.skill_export_xlsx`, `wcr2.item_icon`, `wcr2.item_export`, `wcr2.map_export`, `wcr2.image_search`
  - cache tool 노출: `wcr2.cache_stats`, `wcr2.cache_clear`
  - `WzComparerR2.Cli.Tests`: MCP discover/tools/run stdio smoke 추가
  - `docs/agent-quickstart.md`, `docs/cli.md`, `docs/agent-runtime-plan.md`에 연결 예시와 tool 목록 반영
- [ ] Agent Runtime Phase 6D: media/search/general source registry cache 확장
  - 현재 cache 범위는 agent skill/item/map step으로 제한
  - standalone media command와 image search의 live WZ source registry는 추후 필요성에 따라 확장
- [x] 실제 WZ/MS 샘플 기반 `info/tree/list/search/compare/dump/extract` 검증
  - `.test/wcr2-core-command-smoke-20260907/`
  - `info Data/String --json`: `String.wz`, `String_000.wz` 인식
  - `tree Data/String --depth 1 --limit 30 --json`: root image 26개 확인
  - `list Data/String --json`: root child 26개 확인
  - `search Data/String --name CashItemSearch --json`: `CashItemSearch.img` 1건 확인
  - `dump Data/String --path CashItemSearch.img --format json`: child 32개 확인
  - `extract Data/String --path CashItemSearch.img --recursive`: 파일 43개 추출
  - `compare Data/Effect/_Canvas/_Canvas_001.wz` self-compare: differences 0건 확인
- [x] 실제 WZ/MS 샘플 기반 `skill/item/gear/map info`, `animate frames` 검증
  - `.test/wcr2-domain-animate-smoke-20260907/`
  - `skill info --data-dir Data --id 3141000`: `폭풍의 시 VI`, children 14
  - `item info --data-dir Data --id 2000000`: `빨간 포션`
  - `gear info --data-dir Data --id 1002140`: `위젯 무적 모자`
  - `map info --data-dir Data --id 100000000`: `헤네시스`, children 20
  - `animate frames Data/Mob/_Canvas --path 0100100.img/stand`: frame 1개 PNG 추출
- [x] 실제 WZ/MS 샘플 기반 `map objects/portals/life/reactors` 검증
  - `.test/wcr2-map-detail-smoke-20260907/`
  - `map portals Data/Map/Map/Map1 --id 100000000`: portals 37개
  - `map life Data/Map/Map/Map1 --id 100000000`: life 35개
  - `map objects Data/Map/Map/Map1 --id 100000000`: objects/tiles 1603개
  - `map reactors Data/Map/Map/Map1 --id 100000000`: 헤네시스 원본 reactor 0개 확인
  - split map directory에서 `_Canvas/<id>.img` placeholder가 실제 map metadata보다 먼저 잡히던 문제를 `DomainInfoFinder`에서 non-canvas populated map node 선호로 수정
- [ ] 외부 lua 실행기와 CLI용 Lua API 기반 실제 script 실행 검증 필요
  - 현재 macOS PATH에서 `lua`, `lua5.4`, `lua5.3`, `luajit` 미발견
  - 현재 구현은 외부 Lua 실행기 bridge + `--dry-run` 계약까지 검증됨
- [ ] Network 실제 서버 protocol handshake 검증 필요
  - 현재 CLI 구현은 `server-info --connect` TCP probe와 `chat`/`send` dry-run 계약까지 검증됨
  - 실제 Maple protocol handshake/login/chat 대상 서버 또는 fixture 필요
- [ ] 실제 patch 파일 기반 `patch inspect` 검증 필요
  - 현재 저장소에서 실제 `.patch`/patch sample fixture 미발견
- [ ] 실제 patch 파일과 target 폴더 기반 `patch dry-run` 검증 필요
  - 실제 patch fixture와 target MapleStory 폴더 조합 필요
- [ ] 실제 patch 파일과 target 폴더 기반 `patch apply` 검증 필요
  - 실제 patch fixture와 복사본 output 검증 필요

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
