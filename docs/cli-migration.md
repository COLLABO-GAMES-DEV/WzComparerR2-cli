# WzComparerR2 GUI to CLI Migration

This guide maps common GUI workflows to `wcr2` commands.
The CLI is focused on repeatable extraction, inspection, comparison, and metadata export.
GUI-only rendering features are called out explicitly.

## Setup

Build the CLI:

```bash
dotnet restore WzComparerR2.Cli/WzComparerR2.Cli.csproj --ignore-failed-sources
dotnet build WzComparerR2.Cli/WzComparerR2.Cli.csproj -c Debug --no-restore
```

Run the built tool:

```bash
dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll --help
```

Optionally save a default WZ input:

```bash
dotnet WzComparerR2.Cli/bin/Debug/net8.0/wcr2.dll config set default-wz /path/to/Base.wz
```

## Open WZ and Browse Tree

GUI workflow: open a WZ file, browse the left tree, expand nodes.

CLI equivalent:

```bash
wcr2 info /path/to/Base.wz
wcr2 tree /path/to/Base.wz --depth 2
wcr2 list /path/to/Base.wz --path Character
```

Use JSON for scripts:

```bash
wcr2 tree /path/to/Base.wz --depth 3 --limit 1000 --json
```

## Search Nodes

GUI workflow: search by node name or value.

CLI equivalent:

```bash
wcr2 search /path/to/String.wz --name Maple --json
wcr2 search /path/to/Base.wz --value "sword" --json
wcr2 search /path/to/Base.wz --match-path "*/Canvas" --type png
```

Use `--regex` when `--match-path` should be treated as a regular expression.

## Dump or Extract Data

GUI workflow: right-click or select a node and export data.

CLI equivalent:

```bash
wcr2 dump /path/to/Base.wz --path String --format json --out out/string.json
wcr2 dump /path/to/Base.wz --path String --format xml --out out/string.xml
wcr2 extract /path/to/Base.wz --path String --out out/string --recursive --manifest out/string/manifest.json
```

`extract` writes PNG, sound, raw data, video blobs, and scalar values when the selected nodes contain those values.

## Compare Clients

GUI workflow: open two client versions and compare changed nodes.

CLI equivalent:

```bash
wcr2 compare old/Base.wz new/Base.wz --json --out out/compare.json
wcr2 compare old/Base.wz new/Base.wz --format markdown --out out/compare.md
wcr2 compare old/Base.wz new/Base.wz --path Character --type changed --max-results 200 --json
```

Current comparison is metadata-oriented: node presence, node type, and scalar value changes.
Pixel-level image comparison is not implemented yet.

## Patch Inspection and Application

GUI workflow: inspect or apply a MapleStory patch.

CLI equivalent:

```bash
wcr2 patch inspect MaplePatch.patch --json
wcr2 patch dry-run MaplePatch.patch --target MapleStory --json
wcr2 patch apply MaplePatch.patch --target MapleStory --out MapleStory.patched --log patch.log
```

`patch apply` does not modify the target directory directly.
It copies `--target` to `--out` and patches the copy.

## Skill, Item, Gear, and Map Metadata

GUI workflow: select a skill/item/gear/map node and inspect common properties.

CLI equivalent:

```bash
wcr2 skill info Skill.wz --id 1001004 --string-wz String.wz --json
wcr2 item info Item.wz --id 2000000 --string-wz String.wz --json
wcr2 gear info Character.wz --id 1002140 --string-wz String.wz --json
wcr2 map info Map.wz --id 100000000 --string-wz String.wz --json
```

Map sections:

```bash
wcr2 map portals Map.wz --id 100000000 --json
wcr2 map life Map.wz --id 100000000 --json
wcr2 map objects Map.wz --id 100000000 --json
wcr2 map reactors Map.wz --id 100000000 --json
```

Tooltip image rendering and full map screenshots remain GUI-only for now.

## Animation and Avatar Data

GUI workflow: preview animation frames or inspect avatar code.

CLI equivalent:

```bash
wcr2 animate frames Mob.wz --path 0100100.img/stand --out out/stand --json
wcr2 avatar inspect --code "1002140,1040036,1060026" --json
wcr2 avatar unpack --code "1002140,1040036,1060026" --json
```

`animate frames` exports frame node contents and a `frames.json` manifest.
GIF/APNG encoding and avatar image rendering are not implemented yet.

## Update and Config

GUI workflow: check release and maintain local settings.

CLI equivalent:

```bash
wcr2 update check --asset net8 --json
wcr2 update download --asset net8 --out downloads
wcr2 update apply --asset net8 --json
wcr2 config list
wcr2 config set default-wz /path/to/Base.wz
```

`update apply` is dry-run unless `--execute --updater <path>` is provided.
The CLI config file is separate from GUI `Setting.config`.

## Plugins

GUI workflow: load WinForms plugins from `Plugin/`.

CLI equivalent:

```bash
wcr2 plugin list --plugin-dir CliPlugin --json
wcr2 plugin commands --plugin-dir CliPlugin
wcr2 plugin run my-command arg1 arg2 --plugin-dir CliPlugin
```

CLI plugins use `WzComparerR2.Cli.ICliCommandProvider`.
GUI plugins are not loaded by default; use `--include-gui-plugin-dir` only when you want to inspect them.
