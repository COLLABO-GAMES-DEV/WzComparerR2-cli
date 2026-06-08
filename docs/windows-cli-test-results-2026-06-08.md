# Windows CLI Smoke Result - 2026-06-08

Environment reported by Windows tester:

- Windows version: `Microsoft Windows NT 10.0.26200.0`
- Maple client path: `C:\Nexon\Maple`
- Maple data path: `C:\Nexon\Maple\Data`
- `wcr2.exe`: `C:\Users\KTH\Desktop\wcr2-win-x64-self-contained\wcr2-win-x64-self-contained\wcr2.exe`
- Version output: `wcr2 cli 0.1.0`

## Main Checklist

- Evidence folder provided locally: `wcr2-cli-smoke-out-20260608-163137`
- Summary file: `wcr2-cli-smoke-out-20260608-163137/smoke-summary.json`
- Total commands: 33
- Passed commands: 17
- Failed commands: 16

The main run used these inputs:

- `base`: `C:\Nexon\Maple\Data\Base\Base.wz`
- `string`: `C:\Nexon\Maple\Data\String\String.wz`
- `skill`: `C:\Nexon\Maple\Data\Skill\Skill.wz`
- `item`: `C:\Nexon\Maple\Data\Item\Item.wz`
- `character`: `C:\Nexon\Maple\Data\Character\Character.wz`
- `map`: `C:\Nexon\Maple\Data\Map\Map.wz`
- `mob`: `C:\Nexon\Maple\Data\Mob\Mob.wz`

The failures were concentrated around thin/root shard inputs such as `String\String.wz`, `Skill\Skill.wz`, `Item\Item.wz`, `Character\Character.wz`, `Map\Map.wz`, and `Mob\Mob.wz`.
Failures were `WZ path not found` or `<kind> id not found`.

Interpretation: this Maple client uses a split data/shard layout. The root files exist and can load, but the queried content lives under `Data\...` folders or sharded WZ files.

Failed commands:

- `dump-string-Skill.img`: `WZ path not found: Skill.img`
- `dump-string-Item.img`: `WZ path not found: Item.img`
- `dump-string-Map.img`: `WZ path not found: Map.img`
- `dump-string-Skill`: `WZ path not found: Skill`
- `dump-string-Item`: `WZ path not found: Item`
- `dump-string-Map`: `WZ path not found: Map`
- `skill-1001004`: `skill id not found: 1001004`
- `item-2000000`: `item id not found: 2000000`
- `gear-1002140`: `gear id not found: 1002140`
- `map-100000000`: `map id not found: 100000000`
- `map-100000000-portals`: `map id not found: 100000000`
- `map-100000000-life`: `map id not found: 100000000`
- `map-100000000-objects`: `map id not found: 100000000`
- `map-100000000-reactors`: `map id not found: 100000000`
- `animate-0100100-stand-backslash`: `WZ path not found: 0100100.img\stand`
- `animate-0100100-stand-slash`: `WZ path not found: 0100100.img/stand`

## Directory/Sharded Retry

- Passed commands: 14
- Failed commands: 0

Validated inputs:

- `Data\String`
- `Data\Skill`
- `Data\Item`
- `Data\Character\Cap`
- `Data\Map\Map\Map1\Map1_000.wz`
- `Data\Mob_Canvas`

Validated commands:

- `tree`
- `search`
- `dump`
- `extract`
- `skill info`
- `item info`
- `gear info`
- `map info`
- `map portals`
- `map life`
- `map objects`
- `map reactors`
- `animate frames`

Animation retry produced `frames.json` and `0000\0.png`.

## Follow-Up

- Treat split `Data` layout as the primary Windows smoke-test path.
- Keep classic root WZ paths in docs as fallback for older clients.
- Do not classify root-path `WZ path not found` failures as CLI regressions until data-bearing shard inputs have also been tried.
