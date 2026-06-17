# WzComparerR2 프로젝트 구조 리서치

작성일: 2026-05-26

## 한 줄 요약

WzComparerR2는 MapleStory WZ/MS 파일을 열고 검색, 비교, 이미지/사운드/애니메이션 추출, 장비/캐릭터/맵 시뮬레이션을 제공하는 C# WinForms 데스크톱 앱입니다. 핵심 구조는 `WzComparerR2` 메인 프로그램, `WzComparerR2.WzLib` 파일 파서, `WzComparerR2.Common` 공용 모델/렌더링/UI, `WzComparerR2.PluginBase` 플러그인 계약, 그리고 선택 플러그인들로 나뉩니다.

## 먼저 보면 좋은 파일

| 목적 | 파일 |
| --- | --- |
| 솔루션 전체 프로젝트 목록 | `WzComparerR2.sln` |
| 앱 시작점, 플러그인 로딩, DLL 경로 설정 | `WzComparerR2/Program.cs` |
| 메인 WinForms 화면과 주요 사용자 액션 | `WzComparerR2/MainForm.cs` |
| 메인 앱 빌드 설정과 참조 관계 | `WzComparerR2/WzComparerR2.csproj` |
| WZ/MS 파일 로딩, 노드 트리, 이미지 추출 | `WzComparerR2.WzLib/` |
| 공용 캐릭터/아이템 모델, 애니메이션, 렌더링 유틸 | `WzComparerR2.Common/` |
| 플러그인 발견, 로딩, 컨텍스트 API | `WzComparerR2.PluginBase/` |
| 공통 MSBuild 설정 | `Build/Common.props` |
| 플러그인 빌드 산출물 복사 규칙 | `Build/WcR2Plugin.targets` |
| CI 빌드/패키징 흐름 | `azure-pipelines.yml` |

## 솔루션 구성

`WzComparerR2.sln`에는 다음 프로젝트가 들어 있습니다.

| 프로젝트 | 역할 |
| --- | --- |
| `WzComparerR2` | 메인 WinForms 실행 파일 |
| `WzComparerR2.WzLib` | WZ/MS 파일 파싱, 암호화/버전 처리, 노드 모델 |
| `WzComparerR2.Common` | 공용 도메인 모델, 렌더링, 애니메이션, 설정, 컨트롤 |
| `WzComparerR2.PluginBase` | 플러그인 로딩/컨텍스트/FindWz API |
| `WzComparerR2.Updater` | 업데이트 실행 파일 |
| `WzComparerR2.LuaConsole` | 선택 플러그인: Lua 콘솔 |
| `WzComparerR2.MapRender` | 선택 플러그인: 맵 렌더러/시뮬레이터 |
| `WzComparerR2.Avatar` | 선택 플러그인: 아바타/종이인형 |
| `WzComparerR2.Network` | 선택 플러그인: 온라인 채팅/네트워크 |
| `CharaSimResource` | 장비/캐릭터 시뮬레이션 리소스 서브모듈 |

근거:

- `README.md:16-25`가 공식 모듈 설명을 제공합니다.
- `WzComparerR2.sln:6-33`이 위 프로젝트들을 솔루션에 등록합니다.
- `git submodule status` 기준 `CharaSimResource`는 서브모듈이며, 현재 작업트리에서는 디렉터리가 비어 있습니다. 빌드 전 `git submodule update --init --recursive`가 필요할 가능성이 높습니다.

## 빌드와 런타임 대상

대부분의 프로젝트는 `net462`, `net6.0-windows`, `net8.0-windows`를 동시에 타깃합니다. 예외적으로 `WzComparerR2.WzLib`은 UI 없는 라이브러리라 `net462`, `net6.0`, `net8.0`을 타깃합니다.

중요한 빌드 특징:

- 메인 앱은 WinExe + WinForms입니다. 근거: `WzComparerR2/WzComparerR2.csproj:3-5`
- 공통 빌드 설정은 `Build/Common.props`에서 관리합니다.
- `net8`은 MonoGame `3.8.2.1105`, `net6`은 `3.8.1.303`, `net4`는 `3.8.0.1641`을 씁니다. 근거: `Build/Common.props:17-40`
- nullable은 꺼져 있고, analyzer도 빌드 중 비활성화되어 있습니다. 근거: `Build/Common.props:3-15`
- 플러그인 프로젝트는 `<WcR2Plugin>true</WcR2Plugin>`를 켜고, `Build/WcR2Plugin.targets`를 import해서 메인 앱 출력 폴더의 `Plugin/<ProjectName>` 아래로 복사됩니다. 근거: `Build/Common.props:48-51`, `Build/WcR2Plugin.targets:1-15`
- CI는 Windows 최신 이미지에서 restore, AnyCPU build, x86 build, net462/net6/net8 zip 패키징을 수행합니다. 근거: `azure-pipelines.yml:9-23`, `azure-pipelines.yml:41-107`

## 실행 흐름

앱 시작 순서는 대략 다음과 같습니다.

1. `Program.Main()`이 WinForms 설정, 예외 처리, assembly resolve, 네이티브 DLL 경로 설정을 수행합니다.
2. .NET 6 이상에서는 코드페이지 인코딩 provider 등록과 `Dotnet6Patch.Patch()`가 실행됩니다.
3. `StartMainForm()`이 `MainForm`을 생성합니다.
4. `LoadPlugins(frm)`가 `Plugin` 폴더의 DLL을 찾아 로딩합니다.
5. 설정 파일을 로드하고 `MainForm.PluginOnLoad()`, `PluginManager.PluginOnLoad()`를 호출합니다.
6. `Application.Run(frm)`으로 메인 UI가 시작됩니다.

근거:

- `WzComparerR2/Program.cs:20-33`이 앱 부트스트랩입니다.
- `WzComparerR2/Program.cs:43-55`가 메인 폼 생성, 플러그인 로딩, 설정 초기화, Application.Run 흐름입니다.
- `WzComparerR2/Program.cs:57-83`이 플러그인 assembly 로딩 후 `PluginManager.LoadPlugin()`으로 넘깁니다.
- `WzComparerR2/Program.cs:130-149`가 `Lib/<arch>` 네이티브 DLL 경로를 잡습니다.

## 핵심 데이터 흐름

가장 중요한 데이터 모델은 `Wz_Node` 트리입니다.

1. 사용자가 WZ/MS 파일을 열면 `WzComparerR2.WzLib`이 파일을 읽어 `Wz_Structure`, `Wz_File`, `Wz_Node`, `Wz_Image` 같은 객체 트리로 만듭니다.
2. 메인 UI의 트리 컨트롤은 이 노드를 보여주고, 선택된 노드의 값 타입에 따라 이미지, 사운드, raw data, vector, UOL 등을 다르게 처리합니다.
3. 이미지 노드는 lazy extraction 성격이 강합니다. `Wz_Node`에서 이미지 접근 시 `Wz_Image.TryExtract()`가 호출되는 코드가 보입니다.
4. 플러그인과 공용 렌더링 코드는 `PluginManager.FindWz(...)`를 통해 메인 앱에 열린 WZ 노드를 다시 찾아옵니다.

근거:

- `WzComparerR2.WzLib/Wz_Structure.cs`에는 `Load`, `LoadFile`, `LoadImg`, `LoadWzFolder`, `LoadMsFile` 진입점이 있습니다.
- `WzComparerR2.WzLib/Wz_Node.cs`는 핵심 트리 노드 타입입니다.
- `WzComparerR2.WzLib/Wz_Image.cs`는 `TryExtract()`와 `ExtractImg(...)`를 통해 이미지 내용을 노드로 풉니다.
- `WzComparerR2.PluginBase/PluginManager.cs:27-75`는 타입 또는 경로 기반 `FindWz` API를 제공합니다.

## 메인 앱 디렉터리 읽는 법

`WzComparerR2/`는 메인 프로그램이라 파일이 많습니다. 처음부터 `MainForm.cs` 전체를 읽기보다 관심사별로 보면 좋습니다.

| 디렉터리/파일 | 읽는 관점 |
| --- | --- |
| `Program.cs` | 앱 시작, 플러그인 로딩, DLL resolution |
| `MainForm.cs` / `MainForm.Designer.cs` | UI 이벤트, WZ 열기/검색/보기, 플러그인 컨텍스트 제공 |
| `Comparer/` | WZ 파일 비교, PNG 비교, 가상 노드 |
| `Patcher/` | 패치 파일 생성/적용 관련 로직 |
| `CharaSim/` | 캐릭터 시뮬레이션 런타임 모델 |
| `CharaSimControl/` | 장비/아이템/스킬/퀘스트 툴팁 렌더링 UI |
| `AvatarCommon/` | 아바타 파트, 액션, 프레임, 캔버스 구성 |
| `Config/` | 메인 앱별 설정 섹션 |
| `SoundPlayer/` | Bass 기반 사운드 재생 추상화 |
| `Frm*.cs` | 기능별 WinForms 화면 |

`MainForm`은 `PluginContextProvider`를 구현합니다. 따라서 플러그인은 메인 폼을 직접 알기보다 `PluginContext`를 통해 선택 노드, 탭, ribbon, 이벤트, 문자열 linker 등에 접근합니다. 근거: `WzComparerR2/MainForm.cs:36`, `WzComparerR2/MainForm.cs:4220-4291`, `WzComparerR2.PluginBase/PluginContext.cs`

## 라이브러리별 역할

### `WzComparerR2.WzLib`

WZ/MS 파일 포맷을 다루는 가장 낮은 레벨입니다.

- `Wz_File`, `Wz_Directory`, `Wz_Image`, `Wz_Node`, `Wz_Structure`가 전통 WZ 파일과 노드 구조의 중심입니다.
- `Ms_File`, `Ms_FileV2`, `Ms_Image`는 newer MS 파일 형식 쪽을 다룹니다.
- `Compatibility/`는 WZ 버전/패키지 포맷 감지, offset 계산, pre-read를 담당합니다.
- `Cryptography/`와 `Utilities/IWzDecrypter.cs`는 Snow2, ChaCha20 등 암호화/복호화 주변 코드입니다.
- `Wz_Png`, `Wz_Sound`, `Wz_Video`, `Wz_RawData`, `Wz_Uol`, `Wz_Vector`가 실제 WZ 값 타입입니다.

### `WzComparerR2.Common`

메인 앱과 플러그인들이 같이 쓰는 중간층입니다.

- `CharaSim/`: 아이템, 장비, 스킬, 몬스터, NPC, 퀘스트 등 MapleStory 도메인 모델
- `Animation/`: 프레임 애니메이션, Spine 애니메이션, 캔버스 비디오 로더
- `Rendering/`: Direct2D, MonoGame, SpriteBatch, PNG effect, 텍스트 렌더링
- `Controls/`: animation control, progress dialog, graphics device control
- `Config/`: 설정 파일 등록/저장 공통 인프라
- `Encoders/`: GIF/APNG/FFmpeg encoder 추상화
- `OpenAPI/`: MapleStory OpenAPI 관련 모델/클라이언트

### `WzComparerR2.PluginBase`

플러그인 계약 레이어입니다.

- `PluginEntry`를 상속한 타입이 플러그인 entry point입니다.
- `PluginManager.GetPluginFiles()`는 실행 파일 옆 `Plugin` 디렉터리 아래에서 `WzComparerR2.*.dll`을 찾습니다. 근거: `WzComparerR2.PluginBase/PluginManager.cs:119-135`
- `PluginManager.LoadPlugin()`은 assembly의 exported type 중 `PluginEntry` 하위 타입을 찾아 생성합니다. 근거: `WzComparerR2.PluginBase/PluginManager.cs:138-161`
- `PluginContext`는 메인 폼, DotNetBar manager, 선택 노드, WZ open/closing 이벤트, tab/ribbon 추가 API를 제공합니다.

## 플러그인 구조

현재 플러그인 프로젝트는 모두 `WcR2Plugin` 속성을 사용합니다.

| 플러그인 | 주요 내용 |
| --- | --- |
| `WzComparerR2.LuaConsole` | NLua/KeraLua 기반 콘솔, Lua 예제 포함 |
| `WzComparerR2.MapRender` | MonoGame, EmptyKeys UI, SharpDX RawInput, Bass를 쓰는 맵 시뮬레이터 |
| `WzComparerR2.Avatar` | 아바타 UI와 avatar code/API form |
| `WzComparerR2.Network` | Newtonsoft.Json 기반 패킷 계약, 채팅/네트워크 UI |

공통 패턴:

- 각 프로젝트에 `Entry.cs`가 있고 `PluginEntry`를 상속합니다.
- 빌드 후 `Build/WcR2Plugin.targets`가 DLL과 `.deps.json`/`.runtimeconfig.json`을 메인 출력의 `Plugin/<ProjectName>`으로 복사합니다.
- 플러그인은 `PluginManager.FindWz(...)`로 메인 앱에 열린 WZ 노드를 조회합니다.

## 외부/네이티브 의존성

`References/`에는 NuGet이 아닌 직접 참조 DLL과 architecture별 네이티브 DLL이 있습니다.

- 관리 DLL: `DevComponents.DotNetBar2.dll`, `EmptyKeys.*`, `IMEHelper.dll`, `ImageManipulation.dll`, `spine-monogame.dll`
- 네이티브 DLL: `bass.dll`, `libapng.dll`, `libgif.dll`, `libvpx.dll`, `libyuv.dll`
- 아키텍처 폴더: `References/x86`, `References/x64`, `References/ARM64`

메인 앱 post-build는 이 네이티브 DLL들을 출력의 `Lib/x86`, `Lib/x64`, `Lib/ARM64`로 복사합니다. 근거: `WzComparerR2/WzComparerR2.csproj:115-118`

## 설정 시스템

설정은 `WzComparerR2.Common/Config`의 `ConfigManager`와 `ConfigSectionBase<T>`가 공통 기반입니다.

- `ConfigManager.ConfigFile`이 설정 파일을 엽니다.
- 각 기능은 `ConfigSectionBase<T>`를 상속한 섹션으로 설정을 정의합니다.
- 메인 앱의 구체 설정은 `WzComparerR2/Config/`에, 플러그인별 설정은 각 플러그인의 `Config/`에 있습니다.

근거:

- `WzComparerR2.Common/Config/ConfigManager.cs`의 `ConfigFile`, `Save`, `RegisterSection` 흐름
- `WzComparerR2.Common/Config/ConfigSectionBase.cs`
- `WzComparerR2/Config/*.cs`, `WzComparerR2.MapRender/Config/MapRenderConfig.cs`, `WzComparerR2.LuaConsole/Config/LuaConsoleConfig.cs`, `WzComparerR2.Network/NetworkConfig.cs`

## 테스트/검증 상태

저장소 내에서 별도 테스트 프로젝트나 `*Test*` 디렉터리는 찾지 못했습니다. CI도 restore/build/package 중심이며 테스트 실행 단계는 보이지 않습니다. 따라서 변경 작업을 할 때는 보통 다음 순서가 현실적입니다.

1. 영향 범위의 프로젝트를 좁혀 읽기
2. 가능하면 작은 단위의 수동 재현 경로 확보
3. Windows 환경에서 `dotnet restore WzComparerR2.sln`
4. Windows 환경에서 `dotnet build WzComparerR2.sln -c Release /p:Platform="Any CPU"`
5. 플러그인 관련 변경이면 출력 폴더의 `Plugin/<ProjectName>`과 `Lib/<arch>` 복사 결과 확인

## CLI 전환 구현 후 재검증 메모

작성일: 2026-05-27

현재 CLI 프로젝트는 `WzComparerR2.Cli`이며, `WzComparerR2.WzLib`의 `net8.0` 타깃을 참조합니다. 실행 파일 이름은 `wcr2`로 설정되어 있습니다.

구현된 CLI 표면:

| 기능군 | 명령 | 현재 상태 |
| --- | --- | --- |
| 기본 탐색 | `info`, `tree`, `list` | 구현됨. 실제 WZ/MS 샘플 검증은 아직 필요 |
| 검색 | `search --name`, `--value`, `--match-path`, `--type`, `--regex` | 구현됨. 잘못된 regex 오류 경로 확인 |
| 덤프/추출 | `dump --format json|xml|raw`, `extract --recursive`, `--manifest` | 구현됨. PNG/sound/raw/video blob export 경로는 실제 샘플 검증 필요 |
| 비교 | `compare <old> <new>`, `--type`, `--format json|markdown`, `--out` | 노드 타입/값 메타데이터 기준으로 구현됨. 기존 GUI comparer의 픽셀 비교와 HTML report는 미이식 |
| 패치 | `patch inspect`, `patch dry-run`, `patch apply` | patcher 코드를 CLI 프로젝트에 링크해 구현. 실제 patch 파일 기반 검증 필요 |
| 도메인 조회 | `skill/item/gear/map info` | id 노드 탐색과 `--string-wz` 이름/설명 보강 구현. CharaSim tooltip renderer는 미이식 |
| 애니메이션 | `animate frames` | 숫자 프레임 노드 추출과 `frames.json` manifest 구현. GIF/APNG encoder는 미이식 |
| 아바타 | `avatar inspect`, `avatar unpack` | avatar code 안의 item id 추출과 prefix 기반 슬롯 추정 구현. 실제 avatar renderer는 미이식 |
| 맵 metadata | `map objects`, `map portals`, `map life`, `map reactors` | map `.img` 섹션의 scalar property JSON export 구현. MonoGame screenshot render는 미이식 |
| Lua | `lua run` | 외부 `lua` 실행기 브릿지와 `--dry-run` 구현. NLua 기반 `LuaSandbox`/`env` API는 CLI에 직접 이식하지 않음 |
| Network | `network server-info`, `network chat`, `network send` | dry-run과 `server-info --connect` TCP probe만 구현. protocol handshake/login/chat은 미이식 |
| Updater | `update check`, `update download`, `update apply` | GitHub latest release 조회와 asset 다운로드 구현. `apply`는 기본 dry-run이며 `--execute --updater <path>`일 때만 외부 updater 실행 |
| Config | `config path/list/get/set/unset` | GUI `Setting.config`와 분리된 CLI 전용 JSON key/value 저장소 구현. `--config`와 `WCR2_CLI_CONFIG` override 및 `default-wz` 입력 fallback 지원 |

재검증 결과:

- `dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug --no-restore -p:UseSharedCompilation=false -p:UseAppHost=false -v:minimal` 통과. 경고 0개, 오류 0개.
- `DOTNET_ROLL_FORWARD=Major dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll --help`로 도움말 표면 확인.
- `avatar inspect --code "1002140,1040036,1060026" --json` 정상 JSON 출력 확인.
- `lua run WzComparerR2.LuaConsole/Examples/DumpXml.lua --dry-run --json` 정상 dry-run 출력 확인.
- `network send --message hi --json` 정상 dry-run 출력 확인.
- `update check --asset net8 --json`으로 GitHub latest release 조회 정상 확인. 2026-05-20 생성된 `m26052000` 릴리스와 통합 zip asset을 확인.
- `update apply --asset net8 --json`은 dry-run으로 외부 updater 실행 계획만 출력하는 것을 확인.
- `update download --asset bad --out <dir>`는 잘못된 asset kind로 exit code `1`을 반환하는 것을 확인.
- `config path/set/get/list/unset`을 임시 JSON config 파일로 검증. 누락 key 조회는 exit code `2` 반환 확인. `default-wz` 설정 후 입력 경로를 생략한 `info --config <tmp>`가 설정 경로를 사용해 없는 파일 exit code `2`를 반환하는 fallback 경로도 확인.
- 오류 경로 확인: invalid regex는 exit code `1`, 없는 WZ/map/lua 파일은 exit code `2`, `network send` 메시지 누락은 exit code `1`.
- `git diff --check` 기준 공백 오류 없음.

현재 검증 한계:

- 이 저장소 안에서 `.wz`, `.img`, `.ms`, `.patch` 샘플 파일을 찾지 못했습니다. 따라서 WZ/MS happy path, patch happy path, animation frame 실제 PNG export, map metadata 실제 JSON 출력은 아직 샘플 기반 검증이 필요합니다.
- 현재 macOS 환경에는 .NET 10 런타임만 있어 CLI 실행 시 `DOTNET_ROLL_FORWARD=Major`가 필요합니다.
- 현재 `PATH`에서 `lua`, `lua5.4`, `lua5.3`, `luajit` 실행기를 찾지 못했습니다. `lua run`의 실제 실행 경로는 외부 lua 설치 후 검증해야 합니다.
- 전체 Windows GUI solution build는 아직 이 환경에서 검증하지 않았습니다. WinForms, MonoGame, 네이티브 DLL, `CharaSimResource` 서브모듈 때문에 Windows 기준 검증이 별도로 필요합니다.

설계상 새로 확인한 제약:

- `WzComparerR2.LuaConsole`은 NLua/KeraLua를 쓰지만 `System.Windows.Forms.Application.StartupPath`와 plugin `Entry` assembly 위치를 전제로 합니다. 그래서 CLI에서 프로젝트를 직접 참조하기보다, 당장은 외부 lua 프로세스 방식이 더 작고 안전합니다.
- `WzComparerR2.Network`는 `WcClient`와 `Contracts`가 분리되어 있지만 plugin `Entry`가 UI logger, config, auto reconnect, login handshake를 함께 들고 있습니다. CLI에서 실제 채팅을 하려면 protocol handshake와 session state를 별도 서비스로 분리해야 합니다.
- `WzComparerR2/Updater.cs`는 GitHub latest release 조회와 asset 선택을 담당하고, `WzComparerR2.Updater` 실행 파일은 zip 압축 해제와 GUI 앱 파일 교체를 담당합니다. CLI는 직접 self-update를 수행하지 않고 `update check/download`와 외부 updater 실행 계약으로 분리하는 편이 GUI 업데이트와 충돌하지 않습니다.
- 현재 GitHub latest release는 `net8` 개별 asset 대신 통합 zip 하나를 제공합니다. CLI의 `--asset net8`은 통합 zip으로 fallback하되, 외부 updater 실행 시에는 기존 updater가 요구하는 버전 인자 `8`을 별도로 전달해야 합니다.
- `WzComparerR2.Common/Config/ConfigManager.cs`는 `System.Windows.Forms.Application.StartupPath`와 `Setting.config`를 사용합니다. CLI에서 그대로 재사용하면 GUI 설정 파일과 lifecycle이 섞이므로, 현재는 OS별 user config 경로에 JSON 파일을 두는 별도 저장소가 더 안전합니다.
- 기존 `WzComparerR2.PluginBase.PluginEntry`는 생성자부터 `PluginContext`를 요구하고, `PluginContext`는 `Form`, `DotNetBarManager`, ribbon/tab 추가 API, 선택 노드 이벤트에 묶여 있습니다. 따라서 CLI 플러그인은 GUI `PluginEntry`를 재사용하지 않고 `WzComparerR2.Cli.ICliCommandProvider` public contract로 분리했습니다.
- CLI plugin discovery는 GUI `Plugin/`과 충돌하지 않도록 기본 경로를 `CliPlugin/`로 정했습니다. 명시 경로는 `--plugin-dir`, config key `plugin-dir`, `WCR2_CLI_PLUGIN_DIR`가 우선이고, GUI `Plugin/`은 `--include-gui-plugin-dir`가 있을 때만 inspect 대상으로 스캔합니다.
- CLI plugin load는 `AssemblyDependencyResolver` 기반의 별도 `AssemblyLoadContext`를 사용하고, `WzComparerR2.Cli` assembly만 현재 실행 assembly로 되돌려 provider interface identity를 맞춥니다. 깨진 DLL이나 GUI 전용 DLL은 CLI 전체를 죽이지 않고 plugin 결과의 `Error`로 남깁니다.
- `WzComparerR2.Cli.Tests`는 외부 test framework 없이 CLI process를 직접 실행하는 console harness입니다. 현재 저장소에 WZ 샘플이 없으므로 help/version/error/config/avatar/lua dry-run/network dry-run/update validation/plugin discovery 같은 fixture-free 계약을 먼저 고정하고, 실제 WZ golden/export 테스트는 sample fixture 확보 후 추가해야 합니다.
- Windows 실클라 검증에서 최신 Maple client는 root `String.wz`/`Skill.wz`/`Map.wz` 등이 실제 데이터가 아닌 얇은 root/link 파일처럼 동작했고, 실제 조회는 `Data\String`, `Data\Skill`, `Data\Item`, `Data\Character\Cap`, `Data\Map\Map\Map1\Map1_000.wz`, `Data\Mob_Canvas` 같은 데이터 폴더/샤드 입력에서 성공했습니다. 따라서 Windows smoke 문서는 split `Data` layout을 기본 후보로 포함해야 합니다.
- `WzComparerR2.MapRender`는 MonoGame `Game`, graphics device, EmptyKeys UI, Bass/Native dependency와 강하게 연결되어 있습니다. CLI에서는 screenshot render보다 WZ metadata export를 먼저 제공하는 것이 현실적입니다.
- `Avatar`와 `CharaSim` 계열은 실제 렌더링/툴팁으로 갈수록 WinForms/GDI/Common renderer 의존이 커집니다. 현재 CLI의 id/prefix 기반 정보 출력은 “탐색용 metadata” 수준이며 GUI와 동일한 결과물은 아닙니다.
- Phase 8A에서 `skill full`을 추가했습니다. 이 명령은 `Skill.CreateFromNode` 전체를 직접 참조하지 않고 CLI 내부 headless DTO로 `common`, `PVPcommon`, `level`, `req`, `action`, 플래그, 아이콘 메타데이터, 원문/해석 요약, 진단을 JSON/XML/text로 출력합니다. 수식 계산은 기존 `WzComparerR2.Common/Calculator.cs`를 CLI 프로젝트에 링크해 재사용합니다.
- `String.wz`에서 문자열을 ID만으로 찾으면 같은 숫자 ID가 `Npc.img`, `Eqp.img`, `Skill.img`에 동시에 존재할 수 있습니다. 실제로 `3001004`는 skill data node가 있지만 string 검색은 NPC 문자열을 먼저 잡을 수 있었고, `1001004`는 장비 문자열을 먼저 잡을 수 있었습니다. 따라서 도메인 문자열 조회는 `skill`이면 `Skill.img`, `item/gear`이면 `Cash.img`/`Consume.img`/`Eqp.img` 등 도메인별 String 파일을 우선해야 합니다.
- macOS CrossOver 실클라 `Data/Skill`에서는 `11001025`, `11100027` 같은 일부 실제 skill node가 아이콘과 문자열 중심으로 구성되어 `common`/`maxLevel`이 비어 있었습니다. 이 경우 `skill full`은 `resolvedSummary`에 치환 가능한 값만 반영하고, 남은 `#x`, `#indiePMdR` 같은 placeholder를 `Diagnostics`에 남깁니다. 이는 CLI 파싱 실패라기보다 현재 입력 shard에 수치 property가 없다는 신호로 보아야 합니다.
- `String/Skill.img/1001004`의 “파워 스트라이크” 문자열은 확인되지만 현재 `Data/Skill`에서는 실제 skill node가 검색되지 않았습니다. `skill full --allow-string-only`는 이 케이스를 `Mode = string-only`, `FoundData = false`로 출력합니다.
- Phase 9에서 `animate gif`와 `animate apng`를 추가했습니다. 구현은 기존 Common의 `Gif`, `GifFrame`, `BuildInGifEncoder`, `BuildInApngEncoder`를 CLI에 링크하는 방식입니다. 빌드 출력에 `ImageManipulation.dll`과 `libapng.dll`은 복사되지만, macOS 실클라 실행은 `The type initializer for 'Gdip' threw an exception`으로 실패했습니다. 이는 이전 `animate frames` PNG 추출 실패와 같은 `System.Drawing/GDI+` 제한이므로 실제 GIF/APNG 생성은 Windows 환경에서 검증해야 합니다.
- `animate gif`/`animate apng`는 CLI 레벨에서 `--start-frame`, `--end-frame`, `--delay`, `--scale`, `--origin`을 공유합니다. GIF는 추가로 `--background`와 `--min-alpha`를 받습니다. 이 옵션들은 encoder 호출 전 `GifFrame` 목록을 새로 구성하는 방식이라 기존 Common encoder 코드를 수정하지 않습니다.
- `animate ffmpeg`는 기존 `FFmpegEncoder`를 CLI에 링크해 사용합니다. 기본값은 `ffmpeg` 실행 파일과 mp4/H.264 인수 포맷이며, CLI에서는 `--ffmpeg <path>`와 `--ffmpeg-args <format>`로 override합니다. `FFmpegEncoder`도 `GifEncoder` 하위 타입이라 GIF/APNG와 같은 frame transform 옵션을 공유합니다.
- `lua eval`을 추가했습니다. `lua run`과 같은 외부 Lua 실행기 브릿지를 쓰며, 실제 실행 시에는 `lua -e <code>`로 호출하고 `--dry-run`에서는 코드/line count/WZ 환경 계약만 JSON으로 반환합니다. NLua 기반 LuaConsole `env` global API는 여전히 직접 이식하지 않았습니다.
- Network CLI는 non-interactive 모드로 명시했습니다. `server-info --connect`만 TCP probe를 수행하고, `chat`/`send`는 dry-run contract입니다. `--interactive`는 live chat 구현 전까지 예약 옵션으로 거부합니다.
- Config profile 지원을 추가했습니다. 저장소 schema는 기존 flat JSON을 유지하고 `--profile kms` 값을 `profiles.kms.<key>`로 저장합니다. WZ 입력 fallback은 `profiles.<profile>.default-wz`, `profiles.<profile>.wz`, 전역 `default-wz`, 전역 `wz` 순서입니다.
- Phase 10 Avatar 분석 결과, `WzComparerR2.Avatar/Entry.cs`와 UI form 계층은 WinForms plugin host, ribbon/menu lifecycle, OpenAPI form 흐름에 묶여 있어 CLI에 직접 붙이기 어렵습니다.
- `WzComparerR2/AvatarCommon/AvatarCanvas`에는 action/frame 합성에 필요한 핵심 로직이 있지만, WZ 노드 탐색이 `PluginManager.FindWz` 전역 호출에 묶여 있습니다. 실제 headless PNG 렌더를 하려면 먼저 `AvatarCanvas`/`AvatarCanvasManager`가 CLI의 WZ repository 또는 `Func<string, Wz_Node>` 같은 resolver를 주입받도록 분리해야 합니다.
- `avatar render --dry-run`을 추가했습니다. 현재 이 명령은 실제 PNG를 만들지 않고, 입력 avatar code/items를 파싱해 body/head/face/hair/equipment 분류, 후보 `Character/.../*.img` 경로, action/emotion, `--offline`/`--api-key` 상태, 그리고 실제 render blocker를 JSON으로 출력합니다. `CanRender = false`는 의도된 계약입니다.
- 기본 body/head/face/hair ID는 장비 prefix와 다르게 `00002000`, `00012000`, `00020000`, `00030000`처럼 8자리 문자열에 앞자리 0이 포함될 수 있습니다. CLI의 avatar parser는 이제 이 범위를 `body`, `head`, `face`, `hair`로 따로 분류합니다.
- Phase 11에서 `map render --dry-run`을 추가했습니다. 이 명령은 실제 screenshot을 만들지 않고 map id에서 `Map/Map/MapN/<id>.img`와 split layout 후보 `Data/Map/Map/MapN/MapN_###.wz`를 계산하며, `--layer`, `--include-life`, `--include-reactor`, `--include-tooltip` 옵션과 MonoGame/EmptyKeys/native dependency blocker를 JSON으로 출력합니다.
- 실제 map screenshot export는 `WzComparerR2.MapRender`의 MonoGame `Game`/`GraphicsDevice` lifecycle, offscreen render target 생성, Bass/native runtime 배치, real-client split shard layout 확인이 필요합니다. 따라서 현재 `map render`의 `CanRender = false`는 의도된 계약입니다.

## 처음 작업할 때 주의할 점

- 이 프로젝트는 Windows 데스크톱/WinForms/DirectX/네이티브 DLL에 강하게 묶여 있습니다. macOS에서 구조 분석은 가능하지만 실제 빌드/실행 검증은 Windows가 사실상 필요합니다.
- `CharaSimResource`는 서브모듈입니다. 현재 디렉터리가 비어 있으므로 로컬 빌드는 실패할 수 있습니다.
- `MainForm.cs`가 매우 크고 많은 기능을 직접 갖고 있습니다. 기능 추가/수정 전에는 관련 `Frm*.cs`, `Config`, `Common`, `PluginBase` 경계를 먼저 확인하는 편이 좋습니다.
- analyzer와 nullable이 꺼져 있습니다. 새 코드에서 nullable 경고나 analyzer 기반 품질 보장을 기대하기 어렵습니다.
- 플러그인 로딩은 런타임 폴더 구조에 의존합니다. 플러그인 DLL 이름, 출력 위치, `.deps.json` 복사 여부가 중요합니다.
- 이미지/사운드/비디오 처리는 native dependency와 format compatibility 코드가 얽혀 있어, 단순 리팩터링도 실제 WZ 샘플로 확인하는 것이 좋습니다.

## 추천 탐색 순서

새로운 기능이나 버그를 볼 때는 아래 순서가 이해하기 쉽습니다.

1. `README.md`와 `WzComparerR2.sln`으로 모듈 경계 확인
2. `WzComparerR2/Program.cs`로 앱 시작과 플러그인 로딩 이해
3. `WzComparerR2/MainForm.cs`에서 해당 메뉴/버튼/이벤트 핸들러 검색
4. 데이터가 WZ 노드 관련이면 `WzComparerR2.WzLib/Wz_Structure.cs`, `Wz_Node.cs`, `Wz_Image.cs` 확인
5. 공용 모델/렌더링이면 `WzComparerR2.Common/` 확인
6. 플러그인 기능이면 해당 플러그인의 `Entry.cs`에서 UI 등록과 이벤트 구독 확인
7. 빌드/배포 문제면 `Build/Common.props`, `Build/WcR2Plugin.targets`, `azure-pipelines.yml` 확인

## 분석 확신도

- 높은 확신: 프로젝트/모듈 구성, 타깃 프레임워크, 플러그인 로딩 방식, CI 빌드 흐름
- 중간 확신: 각 기능별 내부 흐름. 파일명과 주요 코드 경로로 추론했지만 모든 UI 이벤트를 끝까지 추적하지는 않았습니다.
- 낮은 확신: 실제 런타임 동작과 포맷별 호환성. WZ/MS 샘플 파일과 Windows 실행 검증 없이는 코드 구조 이상의 판단은 제한됩니다.
