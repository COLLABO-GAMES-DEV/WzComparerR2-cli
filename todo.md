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
- [ ] CLI 출력 스냅샷 검증 방식을 정한다.
  - JSON 출력은 golden file 비교
  - binary export는 hash/크기/metadata 비교

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
- [ ] `--quiet`, `--verbose`, `--no-color` 옵션을 추가한다.
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
- [ ] `search --path <glob-or-regex>` 구현
- [x] `search --value <text>` 구현
- [ ] 검색 범위 옵션 추가
  - `--path <wz-path>`
  - `--max-results <n>`
  - [ ] `--type image|sound|string|vector|uol|raw`
- [ ] lazy image extraction이 필요한 검색과 필요 없는 검색을 분리한다.
- [ ] 검색 성능 측정용 샘플 케이스를 만든다.

완료 기준:

- [ ] String.wz 또는 folder WZ에서 이름/값 검색 가능
- [ ] 큰 파일에서 진행률 또는 제한 옵션으로 제어 가능

## Phase 5. 덤프/export 기본 기능

- [ ] `dump --format json` 구현
- [x] `dump --format xml` 구현 여부 검토
- [ ] `dump --format raw` 구현
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
- [ ] export 결과 manifest 생성 옵션 추가
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
- [ ] `compare <old> <new>` 명령을 구현한다.
- [ ] 출력 포맷을 정의한다.
  - JSON diff
  - human summary
  - optional HTML/markdown report
- [ ] 이미지 비교 결과 export를 지원한다.
- [ ] filter 옵션 추가
  - `--type added|removed|changed`
  - `--path`
  - `--ignore-image-binary`

완료 기준:

- [ ] 두 WZ 파일 또는 두 WZ 폴더의 차이를 CLI에서 확인 가능
- [ ] diff 결과가 자동화 가능한 JSON으로 저장됨

## Phase 7. 패처 기능 CLI화

- [ ] `WzComparerR2/Patcher/` 구조를 분석한다.
  - `WzPatcher`
  - `ReversePatcherBuilder`
  - `PatcherSetting`
  - `Builder/*`
- [ ] 읽기 전용 dry-run 기능을 먼저 만든다.
  - `patch inspect`
  - `patch dry-run`
- [ ] 실제 적용 명령을 만든다.
  - `patch apply <patch-file> --target <dir> --out <dir>`
- [ ] reverse patch 생성 명령을 만든다.
  - `patch reverse-build <old> <new> --out <patch>`
- [ ] 파일 overwrite 정책을 명확히 한다.
  - 기본값은 원본 수정 금지
  - `--in-place`는 별도 확인 옵션 필요
- [ ] checksum 검증과 로그 파일을 추가한다.

완료 기준:

- [ ] dry-run으로 변경 예정 파일 목록 확인 가능
- [ ] out directory 방식으로 안전하게 patch 적용 가능
- [ ] checksum 실패가 명확히 보고됨

## Phase 8. CharaSim/Tooltip 계열 CLI화

- [ ] `WzComparerR2.Common/CharaSim/` 모델 로딩 방식을 정리한다.
- [ ] `WzComparerR2/CharaSim/CharaSimLoader.cs`의 UI 의존성을 분리한다.
- [ ] 아이템/장비 조회 명령 구현
  - `item info --id <id>`
  - `gear info --id <id>`
  - `skill info --id <id>`
  - `mob info --id <id>`
  - `npc info --id <id>`
  - `quest info --id <id>`
- [ ] 출력 형식
  - text
  - JSON
  - optional tooltip image
- [ ] 툴팁 렌더러의 WinForms/GDI 의존을 CLI에서 호출 가능한 렌더링 서비스로 감싼다.
- [ ] string linker 초기화 옵션을 제공한다.
  - `--string-wz`
  - `--item-wz`
  - `--etc-wz`
  - `--quest-wz`

완료 기준:

- [ ] 아이템/스킬/몬스터/NPC/퀘스트 정보를 CLI에서 조회 가능
- [ ] 최소 하나 이상의 툴팁 이미지 export 가능

## Phase 9. 애니메이션/GIF 생성 CLI화

- [ ] `WzComparerR2.Common/Gif*`, `Animation/`, `Encoders/` 구조를 분석한다.
- [ ] animation frame 추출 명령 구현
  - `animate frames --path <wz-path> --out <dir>`
- [ ] GIF/APNG export 명령 구현
  - `animate gif`
  - `animate apng`
- [ ] overlay 옵션을 CLI 인자로 설계한다.
  - origin
  - delay
  - background
  - scale
  - frame range
- [ ] ffmpeg encoder 사용 여부와 경로 옵션을 정한다.

완료 기준:

- [ ] WZ animation node에서 frame PNG export 가능
- [ ] GIF 또는 APNG 파일 생성 가능

## Phase 10. Avatar 기능 CLI화

- [ ] `WzComparerR2.Avatar/Entry.cs`와 `WzComparerR2.Avatar/UI/` 의존성을 분리한다.
- [ ] `WzComparerR2/AvatarCommon/` 재사용 범위를 확인한다.
- [ ] avatar code 파싱/검증 명령 구현
  - `avatar inspect --code <code>`
  - `avatar unpack --code <code> --json`
- [ ] avatar render 명령 구현
  - `avatar render --code <code> --out avatar.png`
  - `avatar render --items <ids...> --action <action>`
- [ ] 외부 MapleStory OpenAPI 사용 여부를 옵션화한다.
  - `--offline`
  - `--api-key`
- [ ] rendering을 headless로 실행할 수 있는지 검증한다.

완료 기준:

- [ ] 아바타 구성 정보를 JSON으로 출력 가능
- [ ] 최소 정적 avatar PNG export 가능

## Phase 11. MapRender 기능 CLI화

- [ ] `WzComparerR2.MapRender/Entry.cs`, `FrmMapRender.cs`, `FrmMapRender2.cs`, `MapData.cs` 구조를 분석한다.
- [ ] map metadata 조회 명령 구현
  - `map info --id <map-id>`
  - `map objects --id <map-id>`
  - `map portals --id <map-id>`
- [ ] headless render 가능성 조사
  - MonoGame device 생성
  - offscreen render target
  - native dependency
- [ ] map screenshot export 명령 구현
  - `map render --id <map-id> --out map.png`
  - `--layer`
  - `--include-life`
  - `--include-reactor`
  - `--include-tooltip`
- [ ] world map/minimap export 검토
  - `map minimap`
  - `map worldmap`

완료 기준:

- [ ] map metadata를 JSON으로 출력 가능
- [ ] 최소 한 개 map screenshot PNG export 가능

## Phase 12. LuaConsole 기능 CLI화

- [ ] `WzComparerR2.LuaConsole/LuaSandbox.cs`를 CLI에서 재사용할 수 있게 정리한다.
- [ ] Lua 실행 명령 구현
  - `lua run <script.lua> --wz <file-or-dir>`
  - `lua eval <code> --wz <file-or-dir>`
- [ ] Lua global API 문서화
  - find node
  - dump
  - export
- [ ] sandbox 제한과 파일 접근 정책을 정한다.
- [ ] examples를 CLI 기준으로 갱신한다.

완료 기준:

- [ ] 기존 `Examples/*.lua` 중 최소 2개가 CLI에서 실행 가능
- [ ] Lua 오류가 line/stack 정보와 함께 출력됨

## Phase 13. Network 기능 CLI화

- [ ] `WzComparerR2.Network/WcClient.cs`와 `Contracts/`를 분석한다.
- [ ] CLI에서 필요한 기능 범위를 재평가한다.
  - 채팅 접속
  - 서버 정보 조회
  - 메시지 송수신
  - custom package
- [ ] 명령 후보
  - `network server-info`
  - `network login`
  - `network chat`
  - `network send`
- [ ] interactive CLI 모드와 non-interactive 모드를 분리한다.
- [ ] credential 저장을 피하고 env var 또는 인자 입력으로 처리한다.

완료 기준:

- [ ] 서버 정보 조회 또는 dry-run 수준 명령 구현
- [ ] 실제 채팅 기능은 별도 승인 후 진행

## Phase 14. Updater 기능 CLI화

- [ ] 기존 `WzComparerR2.Updater`와 `WzComparerR2/Updater.cs` 역할을 분석한다.
- [ ] CLI update command의 책임을 정한다.
  - 최신 버전 확인
  - 다운로드 URL 출력
  - 다운로드
  - self-update 또는 외부 updater 실행
- [ ] 명령 후보
  - `update check`
  - `update download --out <dir>`
  - `update apply`
- [ ] 기존 GUI 앱 업데이트와 CLI 업데이트가 충돌하지 않도록 분리한다.

완료 기준:

- [ ] 최신 릴리스 정보를 CLI에서 확인 가능
- [ ] 자동 적용은 별도 안전 설계 후 진행

## Phase 15. Config CLI

- [ ] CLI 전용 config 위치를 정한다.
  - Windows: `%APPDATA%/WzComparerR2.Cli`
  - portable: 실행 파일 옆 config
- [ ] 기존 `ConfigManager` 재사용 가능성을 확인한다.
- [ ] 명령 구현
  - `config list`
  - `config get <key>`
  - `config set <key> <value>`
  - `config unset <key>`
- [ ] profile 지원 검토
  - `--profile kms`
  - `--profile gms`
  - `--profile custom`
- [ ] CLI 기본값 문서화

완료 기준:

- [ ] 반복 작업에서 매번 WZ 경로를 입력하지 않아도 됨
- [ ] config 오류가 명확히 보고됨

## Phase 16. 플러그인/확장 모델 재설계

- [ ] 기존 `PluginEntry`는 WinForms 컨텍스트 중심이므로 CLI용 plugin contract를 별도 설계한다.
- [ ] 후보 인터페이스
  - `ICliCommandProvider`
  - `ICliExportProvider`
  - `ICliNodeAction`
- [ ] 기존 GUI 플러그인과 CLI 플러그인을 동시에 지원할지 결정한다.
- [ ] plugin discovery 경로를 정한다.
  - `Plugin/`
  - `CliPlugin/`
- [ ] version compatibility 정책을 만든다.
- [ ] 실패한 plugin load가 CLI 전체를 죽이지 않게 한다.

완료 기준:

- [ ] 외부 CLI 명령을 plugin assembly에서 등록 가능
- [ ] 기존 GUI plugin loading과 충돌하지 않음

## Phase 17. 문서화

- [x] `docs/cli.md` 작성
  - 설치
  - 명령어
  - 예제
  - exit code
  - JSON schema
- [ ] `README.md`에 CLI 섹션 추가
- [x] 각 명령의 `--help` 텍스트 작성
- [ ] 마이그레이션 가이드 작성
  - GUI에서 하던 작업을 CLI로 하는 예시
- [ ] sample scripts 작성
  - batch extract
  - compare report
  - avatar render
  - map screenshot

완료 기준:

- [ ] 신규 사용자가 문서만 보고 `info/tree/extract/compare`를 실행할 수 있음

## Phase 18. 테스트 전략

- [ ] CLI 단위 테스트 프로젝트 추가
  - `WzComparerR2.Cli.Tests`
- [ ] parser 자체를 검증하기보다 CLI service와 output contract를 검증한다.
- [ ] golden output 테스트 추가
  - tree JSON
  - search JSON
  - compare JSON
- [ ] export 검증 추가
  - PNG dimensions
  - sound file exists/hash
  - manifest
- [x] error case 테스트 추가
  - 파일 없음
  - path 없음
  - unsupported value type
  - corrupt WZ
- [ ] Windows CI에 CLI test 단계 추가
- [ ] macOS/Linux에서 가능한 pure library test와 Windows-only test를 분리한다.

완료 기준:

- [ ] CLI 명령별 최소 happy path와 error path 테스트가 있음
- [ ] CI에서 CLI 빌드와 테스트가 실행됨

## Phase 19. 패키징/배포

- [ ] CLI binary artifact 이름 결정
  - `wcr2.exe`
  - `wcr2-net8-win-x64.zip`
- [ ] self-contained publish 여부 결정
- [ ] native DLL 포함 규칙 정리
  - `Lib/x86`
  - `Lib/x64`
  - `Lib/ARM64`
- [ ] Azure pipeline에 CLI artifact 추가
- [ ] release note에 CLI 사용 예시 추가
- [ ] 기존 GUI artifact와 CLI artifact를 분리한다.

완료 기준:

- [ ] CI 산출물에 CLI zip이 포함됨
- [ ] zip만 풀어서 `wcr2 --help` 실행 가능

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
- [ ] 실제 WZ/MS 샘플 기반 `info/tree/list/search/extract` 검증 필요

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
