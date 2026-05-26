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
