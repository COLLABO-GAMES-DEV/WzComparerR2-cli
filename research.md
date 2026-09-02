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
- CLI repository/find service는 `skill full`과 `skill/item/gear/mob/npc/quest/map info`에서 입력 후보를 관리합니다. positional 입력, 도메인별 `--*-wz`, `--data-dir/<DomainFolder>`를 data 후보로 보고, `--string-wz`, `--data-dir/String`, sibling `Data/String`을 string 후보로 자동 추가합니다. 출력에는 실제 hit 경로 `DataInputPath`, `StringInputPath`와 전체 후보 목록이 포함됩니다.
- `skill full`은 `LinkerStatus`와 `UnresolvedPlaceholders`를 구조화해 출력합니다. `LinkerStatus`는 CLI headless resolver가 data node, String metadata, scalar stats, visual branch, summary template를 각각 찾았는지 보여주며, `GuiStringLinkerLoaded=false`로 GUI `StringLinker.Load(...)` 전체 이식이 아직 아님을 명시합니다.
- `String.wz`에서 문자열을 ID만으로 찾으면 같은 숫자 ID가 `Npc.img`, `Eqp.img`, `Skill.img`에 동시에 존재할 수 있습니다. 실제로 `3001004`는 skill data node가 있지만 string 검색은 NPC 문자열을 먼저 잡을 수 있었고, `1001004`는 장비 문자열을 먼저 잡을 수 있었습니다. 따라서 도메인 문자열 조회는 `skill`이면 `Skill.img`, `item/gear`이면 `Cash.img`/`Consume.img`/`Eqp.img` 등 도메인별 String 파일을 우선해야 합니다.
- macOS CrossOver 실클라 `Data/Skill`에서는 `11001025`, `11100027`, `1001008` 같은 일부 실제 skill node가 아이콘/이펙트 중심의 canvas 데이터로 구성되어 `common`/`maxLevel`이 비어 있었습니다. 이 경우 `skill full`은 `SourceProfile = visual-only`, `StatPropertyCount = 0`, `VisualBranches`를 출력하고, 남은 `#x`, `#mpCon`, `#indiePMdR` 같은 placeholder를 `Diagnostics`에 남깁니다. 이는 CLI 파싱 실패라기보다 현재 입력 shard에 수치 property가 없다는 신호로 보아야 합니다.
- `String/Skill.img/1001004`의 “파워 스트라이크” 문자열은 확인되지만 현재 `Data/Skill`에서는 실제 skill node가 검색되지 않았습니다. `skill full --allow-string-only`는 이 케이스를 `Mode = string-only`, `FoundData = false`로 출력합니다.
- Phase 9에서 `animate gif`와 `animate apng`를 추가했습니다. 구현은 기존 Common의 `Gif`, `GifFrame`, `BuildInGifEncoder`, `BuildInApngEncoder`를 CLI에 링크하는 방식입니다. 빌드 출력에 `ImageManipulation.dll`과 `libapng.dll`은 복사되지만, macOS 실클라 실행은 `The type initializer for 'Gdip' threw an exception`으로 실패했습니다. 이는 이전 `animate frames` PNG 추출 실패와 같은 `System.Drawing/GDI+` 제한이므로 실제 GIF/APNG 생성은 Windows 환경에서 검증해야 합니다.
- `animate gif`/`animate apng`는 CLI 레벨에서 `--start-frame`, `--end-frame`, `--delay`, `--scale`, `--origin`을 공유합니다. GIF는 추가로 `--background`와 `--min-alpha`를 받습니다. 이 옵션들은 encoder 호출 전 `GifFrame` 목록을 새로 구성하는 방식이라 기존 Common encoder 코드를 수정하지 않습니다.
- `animate ffmpeg`는 기존 `FFmpegEncoder`를 CLI에 링크해 사용합니다. 기본값은 `ffmpeg` 실행 파일과 mp4/H.264 인수 포맷이며, CLI에서는 `--ffmpeg <path>`와 `--ffmpeg-args <format>`로 override합니다. `FFmpegEncoder`도 `GifEncoder` 하위 타입이라 GIF/APNG와 같은 frame transform 옵션을 공유합니다.
- `lua eval`을 추가했습니다. `lua run`과 같은 외부 Lua 실행기 브릿지를 쓰며, 실제 실행 시에는 `lua -e <code>`로 호출하고 `--dry-run`에서는 코드/line count/WZ 환경 계약만 JSON으로 반환합니다. NLua 기반 LuaConsole `env` global API는 여전히 직접 이식하지 않았습니다.
- Network CLI는 non-interactive 모드로 명시했습니다. `server-info --connect`만 TCP probe를 수행하고, `chat`/`send`는 dry-run contract입니다. `--interactive`는 live chat 구현 전까지 예약 옵션으로 거부합니다.
- Config profile 지원을 추가했습니다. 저장소 schema는 기존 flat JSON을 유지하고 `--profile kms` 값을 `profiles.kms.<key>`로 저장합니다. WZ 입력 fallback은 `profiles.<profile>.default-wz`, `profiles.<profile>.wz`, 전역 `default-wz`, 전역 `wz` 순서입니다.
- Azure Pipeline에 CLI win-x64 self-contained publish/archive/upload 항목을 추가했습니다. GUI artifact는 기존 `WcR2_With_Plugins*.zip` 이름을 유지하고, CLI artifact는 `WzComparerR2.Cli-win-x64-self-contained_<BuildNumber>.zip`으로 분리합니다.
- 표준 출력 옵션을 추가했습니다. `--quiet`은 성공 stdout을 억제하고, `--verbose`는 실패 stderr에 예외 타입/스택을 추가합니다. `--no-color`는 현재 색상 출력이 없지만 자동화 호환 플래그로 수용합니다.
- `docs/cli-test-strategy.md`를 추가해 fixture-free contract test, private real-client smoke test, golden JSON 비교, binary export metadata 검증 기준을 분리했습니다.
- Patch CLI는 이미 `patch dry-run`에서 기존 파일 checksum을 확인하고 `ValidCount`, `ChecksumMismatchCount`, action `Status=checksum-mismatch`를 출력합니다. `patch apply --log <file>`은 복사본 적용 이벤트를 텍스트 로그로 남깁니다. 실제 patch 파일 기반 검증은 별도 fixture 확보가 필요합니다.
- `mob info`, `npc info`, `quest info`를 `item/gear/map info`와 같은 `DomainInfoFinder` 기반 얕은 metadata 조회로 추가했습니다. mob/npc는 7자리 `.img` id 후보를 우선 포함하고, String lookup은 `Mob.img`, `Npc.img`, `Quest.img` 경로를 선호합니다.
- Phase 10 Avatar 분석 결과, `WzComparerR2.Avatar/Entry.cs`와 UI form 계층은 WinForms plugin host, ribbon/menu lifecycle, OpenAPI form 흐름에 묶여 있어 CLI에 직접 붙이기 어렵습니다.
- `WzComparerR2/AvatarCommon/AvatarCanvas`에는 action/frame 합성에 필요한 핵심 로직이 있지만, WZ 노드 탐색이 `PluginManager.FindWz` 전역 호출에 묶여 있습니다. 실제 headless PNG 렌더를 하려면 먼저 `AvatarCanvas`/`AvatarCanvasManager`가 CLI의 WZ repository 또는 `Func<string, Wz_Node>` 같은 resolver를 주입받도록 분리해야 합니다.
- `avatar render --dry-run`을 추가했습니다. 현재 이 명령은 실제 PNG를 만들지 않고, 입력 avatar code/items를 파싱해 body/head/face/hair/equipment 분류, 후보 `Character/.../*.img` 경로, action/emotion, `--offline`/`--api-key` 상태, 그리고 실제 render blocker를 JSON으로 출력합니다. `CanRender = false`는 의도된 계약입니다.
- 기본 body/head/face/hair ID는 장비 prefix와 다르게 `00002000`, `00012000`, `00020000`, `00030000`처럼 8자리 문자열에 앞자리 0이 포함될 수 있습니다. CLI의 avatar parser는 이제 이 범위를 `body`, `head`, `face`, `hair`로 따로 분류합니다.
- Phase 11에서 `map render --dry-run`을 추가했습니다. 이 명령은 실제 screenshot을 만들지 않고 map id에서 `Map/Map/MapN/<id>.img`와 split layout 후보 `Data/Map/Map/MapN/MapN_###.wz`를 계산하며, `--layer`, `--include-life`, `--include-reactor`, `--include-tooltip` 옵션과 MonoGame/EmptyKeys/native dependency blocker를 JSON으로 출력합니다.
- 실제 map screenshot export는 `WzComparerR2.MapRender`의 MonoGame `Game`/`GraphicsDevice` lifecycle, offscreen render target 생성, Bass/native runtime 배치, real-client split shard layout 확인이 필요합니다. 따라서 현재 `map render`의 `CanRender = false`는 의도된 계약입니다.
- 2026-06-22 macOS MapleStory 설치 경로는 `~/Library/Application Support/MapleStory/Bottles/maplestory/drive_c/Nexon/Maple`로 확인했습니다. `Data/Mob/_Canvas/_Canvas_000.wz`와 `Data/Character/Cap/Cap_000.wz`는 `PKG1` 헤더라 CLI/WzLib에서 로딩됩니다.
- 같은 설치의 `Data/Item/Cash/Cash.wz`, `Data/Item/Cash/Cash_000.wz`, `Data/Item/Cash/_Canvas/_Canvas_000.wz`, `Data/String/String_000.wz`, `Data/Skill/_Canvas/_Canvas_000.wz`는 첫 4바이트가 `PKG1/PKG2`가 아니고 KMST1201 random-header dataSize probe도 파일 크기-68과 일치하지 않았지만, Kagamia upstream의 KMST1202 150-byte random header probe(`fileSize - 150`)와는 일치했습니다.
- Phase 5B에서 KMST1202 64-bit PKG2 random header 경로를 WzLib에 최소 이식했습니다. `Data/String/String_000.wz`는 `WzVersion = 1202`, root children 26으로 열렸고, `Data/Item/Cash/Cash_000.wz`는 45개 img, `Data/Item/Cash/_Canvas/_Canvas_000.wz`/`_Canvas_001.wz`는 Cash icon canvas를 로딩했습니다. 기존 `Data/Mob/_Canvas/_Canvas_000.wz` PKG1 샘플도 계속 정상 로딩됩니다.
- `search Data/String/String_000.wz --value "미라클 큐브" --extract-images --json`으로 `Cash.img\5062000\name`을 찾았고, `image export Data/Item/Cash/_Canvas/_Canvas_001.wz --path 0506.img/05062000/info/icon`으로 macOS에서 37x38 PNG를 생성했습니다.
- 요청된 Cash 아이콘 중 확인 가능한 7개를 `.test/wcr2-requested-item-icons-kmst1202-20260625`에 추출했습니다. `MSW 아바타 코디 이용권(30일)`은 현재 `String_000.wz` 검색 결과가 없었고, `펫`은 단일 exact item name이 아니라 카테고리/묶음명으로 보여 ID 확정이 필요합니다. `[7일]보따리상인 묘묘`는 string ID `5450007`이지만 canvas에는 기간제별 아이콘이 없어 `5450000` 대표 아이콘을 사용했습니다.
- `item icon` 명령을 추가해 위 수동 흐름을 한 번에 실행할 수 있게 했습니다. `item icon --data-dir <Data> --name "미라클 큐브" --out <dir> --json`은 `Cash.img\5062000`을 찾고 `0506.img/05062000/info/icon`을 PNG로 저장했습니다. `보따리상인 묘묘(7일)`처럼 요청명은 뒤쪽 괄호, 실제 String명은 `[7일]` prefix인 경우도 duration 표기를 정규화해 `Cash.img\5450007`을 찾고, 개별 아이콘이 없으면 `0545.img/05450000/info/icon` 대표 아이콘 fallback을 `Diagnostics`에 남깁니다. 재검증 결과는 `.test/wcr2-item-icon-command-20260625`에 있습니다.
- CLI command 파일 경계는 2026-06-26 기준으로 다시 정리했습니다. `Program.DomainCommands.cs`는 domain info와 `item icon` 라우팅만 남기고, `skill`, `animate`, `avatar`, `map`, `lua/network` 실행 흐름은 각각 `Program.Skill.cs`, `Program.Animate.cs`, `Program.Avatar.cs`, `Program.Map.cs`, `Program.LuaNetwork.cs`로 분리했습니다. `item icon` 내부도 `ItemIconExporter`, `ItemIconPaths`, `ItemStringResolver`, `ItemIconModels`로 나뉘어 있으므로 이후 아이템 media 기능은 `WzComparerR2.Cli/Media/ItemIconExporter.cs`에서 시작하면 됩니다.
- Phase 5B에서 CLI 로딩 실패 진단도 확장했습니다. `--json`이 있는 로딩 실패는 exit code `3`을 유지하고 stderr JSON에 `Diagnostic.FileName`, `First4Hex`, `HeaderHex`, `DetectedFormat`, `ExpectedPkg2RandomDataSize`, `CurrentPkg2RandomDataSizeProbe`, `ExpectedPkg2RandomHeader64DataSize`, `CurrentPkg2RandomHeader64DataSizeProbe`를 출력합니다. 성공 JSON stdout 계약은 변경하지 않았습니다.
- `skill sprite` 명령을 추가해 스킬 ID 기준 실제 스프라이트 PNG를 추출할 수 있게 했습니다. 구현은 기존 WzComparer/WzLib 엔진을 수정하지 않고 CLI `SkillSpriteExporter`에서 스킬 메타데이터의 `_outlink`를 읽어 `Skill/_Canvas/...` 대상 경로를 계산한 뒤, `--canvas-wz` 또는 `--data-dir <Data>`로 찾은 `Data/Skill/_Canvas`와 `Data/Packs/Skill*.ms` 후보를 순회합니다. macOS/CrossOver 실클라에서 `Data/Packs/Skill_00000.ms`, skill `1121008`, branches `icon,effect,hit/0`을 대상으로 27개 PNG를 추출했고, 샘플 치수는 `icon 32x32`, `effect/4 740x440`, `hit/0/0 200x156`으로 1x1 stub이 아닌 실제 Canvas였습니다. 검증 산출물은 `.test/wcr2-skill-sprite-interface-20260827-165627`에 있습니다.
- `skill sprite`는 이미지 전용이라 사운드를 포함하지 않습니다. 이 혼동을 줄이기 위해 `skill export` 명령을 추가했고, 이 명령은 스프라이트 추출 후 `Sound/Skill.img/<skillId>` 아래의 사운드를 함께 추출합니다. `1121008` 실클라 검증에서는 `icon/effect/hit_0` PNG 27개와 `sound/Use.mp3`, `sound/Hit.mp3` 2개를 함께 생성했습니다. 검증 산출물은 `.test/wcr2-skill-export-with-sound-20260827-171245`에 있습니다.
- 2026-09-01 오리진/어센트 스킬 대량 추출 검증에서 `screen` 누락은 PNG 추출 실패가 아니라 수동 branch 목록 부족이 원인이었습니다. 예를 들어 `3141502`는 `screen2`, `screen3`, `1241501`은 `tile`, `5241503`은 `special*` branch가 추가로 있었습니다. 이에 따라 `skill export`는 기본적으로 스킬 노드에서 visual branch를 자동 감지하도록 바꾸고, `--branch auto|visual|all`로 같은 동작을 명시할 수 있게 했습니다.
- `skill export`에 action/delay related asset lookup을 추가했습니다. 대표 skill node가 visual-only shard라 action 값이 없을 수 있으므로 `Data/Packs/Skill*.ms` 메타데이터도 읽어 `action/0` 같은 문자열 seed를 보강합니다. `5241503` 파이어크래커는 이 방식으로 `6thFireCracker`, `6thFireCrackerClone`을 자동 발견했습니다. 이 key는 외부 `_Canvas` 이미지 이름과 직접 매칭되지 않았지만, 같은 pack 메타데이터의 내부 `screen*/video` 노드가 실제 컷신이었습니다.
- 2026-09-01 파이어크래커 컷신 조사에서 사용자가 제공한 해적 캐릭터 애니메이션은 `Data/Packs/Skill_00006.ms :: Skill/524.img/skill/5241503/screen2/video`로 확인했습니다. 이 노드는 `Wz_Video`/MCV이며 FourCC `VP90`, 1368x768, 41 frames, alpha map 포함입니다. 같은 스킬 아래 `screen/video`는 다른 변형 컷이고, `screen3`~`screen12`는 1700x1080 배경/섬광/불꽃 합성 레이어입니다. CLI에 `video list/export/export-all`을 추가했고, `skill export --data-dir <Data> --id 5241503 --video-format gif`가 `Data/Packs/Skill*.ms` 보강 스캔을 통해 12개 `screen*/video` GIF를 자동 추출하는 것을 확인했습니다. 검증 산출물은 `.test/wcr2-skill-export-video-20260901-170733`에 있습니다.
- 2026-09-01 캐릭터 카드 스킬 대량 추출 중 `1321014` 매직 크래쉬, `3221002`/`3321022` 샤프 아이즈, `2220004`/`2320004` 인피니티는 `Data/Skill`만으로는 `skill id not found`였지만, `Data/Packs/Skill_*.ms`를 직접 입력하면 `_outlink`를 따라 PNG가 추출되었습니다. 따라서 이 케이스는 스킬 삭제가 아니라 CLI `--data-dir` repository가 skill pack metadata를 기본 데이터 후보로 잡지 못한 문제였습니다. `CliWzRepository`는 이제 `Data/Skill`에서 id를 못 찾을 때 `Data/Packs/Skill_*.ms`를 lazy fallback으로 순회합니다.
- 2026-09-01 이후 `skill sprite`와 `skill export`는 출력 폴더에 `skill-info.json`과 `resources.json`을 함께 저장합니다. `skill-info.json`은 String/Skill.img 기반 이름/설명, 원본/해석된 스킬 설명 템플릿, unresolved placeholder, data/string 경로를 보존합니다. `resources.json`은 `icon`, `effect`, `hit`, `screen*`, `sound`, `video`, `related` 같은 실제 branch 이름, `_outlink`와 resolved 경로, 추출 상태, 파일 목록만 보존합니다. branch 의미를 추정한 CLI 임의 설명은 공식 데이터처럼 보일 수 있어 포함하지 않습니다.
- 2026-09-01 대량 추출 결과의 빈 `video/` 폴더는 대부분 비디오 추출 실패가 아니라 `VideoExporter.Export()`가 비디오 노드 존재 여부를 확인하기 전에 출력 디렉터리를 먼저 만들던 부작용이었습니다. `resources.json`의 video 항목이 `Status: no-video-assets`, `ExportedFileCount: 0`이면 해당 스킬에 추출된 비디오가 없다는 뜻입니다. 이후 CLI는 실제 `Wz_Video`를 만났을 때만 비디오 출력 디렉터리를 생성합니다.
- 2026-09-02 `resources.json`의 offset/delay/z 누락 원인을 재검증했습니다. 기존 `ExtractedFileDto`는 `SourcePath`, `OutputPath`, `Type`, `Bytes`, `Sha256`만 저장했고, `skill sprite/export`가 `_outlink`를 따라 실제 Canvas PNG를 저장할 때 원래 metadata stub의 child 값(`origin`, `z`, `delay`, `_outlink`)을 파일 DTO에 다시 연결하지 않았습니다. CLI 계층에서 `ExtractedFileDto`를 확장해 PNG width/height/format/pages, sound length/ms/channels/frequency/type, MCV width/height/FourCC/frame count/alpha flag, frame `Origin`/`Lt`/`Rb`/`Z`/`Delay`/`OutlinkPath`/`InlinkPath`, 기타 direct scalar/vector child `Metadata`를 기록하게 했습니다. 검증 산출물은 `.test/wcr2-skill-resource-metadata-20260902/rerun-icon-root`이며, `400011001`의 `summon/summoned/0`은 `Origin=-22,173`, `Z=0`, `Delay=90`, `OutlinkPath=Skill/_Canvas/40001.img/skill/400011001/summon/summoned/0`로 기록됐고, `5241503`의 `special/0`은 `Origin=184,263`, `Delay=60`으로 기록됐습니다. `5241503 screen2/video` MCV는 `Width=1368`, `Height=768`, `Format=VP90`, `FrameCount=41`, `VideoFlags=AlphaMap`로 기록됐습니다.
- 2026-09-02 `--data-dir <Data>`에서 `Data/Skill`이 같은 skill id의 canvas/visual-only 노드를 먼저 찾으면 description/stat/metadata가 null로 남을 수 있음을 확인했습니다. `CliWzRepository`는 이제 skill data node가 약한 visual-only 매치이면 `Data/Packs/Skill_*.ms` lazy 후보를 추가로 확인해 `common`/`level` 등 scalar metadata가 있는 노드를 더 우선합니다. 예: `400011001`과 `5241503`은 `Data/Packs/Skill_00005.ms`, `Skill_00006.ms`의 mixed metadata 노드로 보정됐습니다. 반면 `400011002`는 현재 로컬 데이터에서 `Data/Skill`에 실제 PNG만 있고 `Data/Packs/Skill_00005.ms`에는 `400011001/psdSkill/400011002` 빈 marker만 확인되어 `origin`/`delay`/`z`가 채워지지 않습니다. 이 경우는 CLI 누락이 아니라 해당 variant 소스에 frame metadata가 없는 케이스로 기록해야 합니다.
- 2026-09-02 스킬 설명 품질 점검 결과, `.test/wcr2-card-skill-export-20260901-180337`의 기존 `skill-info.json` 232개 중 `Description` 누락 57개, `RawSummary` 누락 69개, 둘 다 누락 57개였습니다. 이 산출물은 기존 export sidecar를 backfill한 결과라 전체 232개가 `ResolvedSummary = null`이지만, 현재 CLI로 다시 `skill full/export`를 실행하면 문자열 템플릿과 수치 property가 있는 스킬은 `ResolvedSummary`를 계산합니다. 점검 리포트는 `.test/wcr2-description-quality-report-20260902`에 있습니다.
- 위 57개 name-only 스킬은 모두 같은 이름의 다른 skill id에 `Description`/`RawSummary` 후보가 있었습니다. 예: `400051049`/`400051050` 노틸러스 어썰트는 `400051040`, `5241501` 드레드노트는 `5241500`, `2141503` 인페르날 베놈은 `2141500`에 설명 후보가 있습니다. 다만 이는 같은 이름 fallback 후보이지 현재 id의 공식 문자열은 아니므로, CLI가 자동으로 덮어쓰면 데이터 출처가 흐려집니다.
- 설명 템플릿 안의 `#c10...#` 같은 색상 태그가 `#c10` placeholder로 오인되던 문제를 수정했습니다. `#cooltime`처럼 실제 placeholder가 `#c`로 시작하는 경우는 계속 치환 대상으로 처리합니다. `5241500`/`5241503` 재실행 샘플에서 `#c10`은 unresolved 목록에서 빠졌고, 남은 `#damage`, `#mpCon` 등은 visual-only 노드에 수치 property가 없어 남는 항목입니다.
- 2026-09-02 `Wz_Video`/MCV 출력 정책을 재정리했습니다. `video export`는 원본 조사 목적이 있으므로 기본 `--format mcv`를 유지하지만, `--format png`를 `frames` 별칭으로 받습니다. `skill export`는 메월드 이관에 바로 쓰기 쉽도록 기본 비디오 출력을 PNG 프레임으로 바꿨고, 원본 blob이 필요할 때만 `--video-format mcv`를 명시합니다. 기존 `.test/wcr2-card-skill-export-20260901-180337` 안의 `1141500` 스피릿 칼리버 MCV는 `117`개 `1366x768` RGBA PNG 프레임으로 풀렸고, 원본 `VideoFlags=AlphaMap`입니다.
- 2026-09-03 전체 스킬 재추출에서 기존 방식의 주요 속도 병목은 스킬마다 별도 CLI 프로세스를 띄우고 Skill/String repository, `_outlink` Canvas input, Sound input, Skill*.ms metadata input을 반복 로딩하는 구조였습니다. `skill export-batch`를 추가해 여러 스킬을 한 프로세스에서 처리하고 `SkillSpriteExportSession`이 repository 및 WZ load context를 세션 동안 캐시하도록 했습니다. 단건 `skill export`의 출력 계약은 유지하고, 배치 명령은 각 항목 폴더에 `skill-info.json`, `resources.json`, `export-result.json`을 직접 저장하며 `--manifest`로 전체 요약을 남깁니다. 실클라 대량 추출은 배치 명령으로 다시 벤치마크해야 하지만, 이전 253개 단건 재추출 로그 기준 스킬별 평균은 약 29.0초, 중앙값은 약 27.9초였습니다.
- 2026-09-03 스킬명을 손으로 검색하면서 생기는 오매칭을 줄이기 위해 CLI에 `skill search-name`/`skill resolve-name`을 추가했습니다. resolver는 `String/Skill.img`의 이름 후보를 먼저 만들고, 각 후보가 실제 `Data/Skill` 또는 `Data/Packs/Skill_*.ms`에서 data node로 열리는지 확인해 `FoundData`, `SourceProfile`, `StatPropertyCount`, `VisualBranches`, `DataPath`, `StringPath`를 함께 출력합니다. 이름 비교는 공백/기호 차이를 정규화하고 짧은 오타는 fuzzy 후보로 노출하지만, 같은 이름의 여러 ID나 파생 ID가 남으면 `ambiguous`로 실패합니다. `--job-code`는 `floor(skillId / 10000)`와 정확히 비교합니다. 다만 같은 이름 후보 중 `psdSkill` marker처럼 metadata-only이고 visual/stat이 없는 항목은, visual/stat을 가진 대표 후보가 하나뿐이면 resolve를 막지 않습니다. `skill export-batch --names-file`은 같은 규칙으로 이름을 ID로 확정한 뒤 추출하며, 배치 안에서는 String 후보 목록과 data profile을 캐시하고 실패 항목은 manifest에 후보 목록을 남깁니다.

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
