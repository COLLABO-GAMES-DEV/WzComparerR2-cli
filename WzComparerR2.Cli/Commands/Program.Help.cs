using System;
using System.Collections.Generic;
using System.Linq;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static bool IsHelp(string arg)
        {
            return string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUpdateAssetKind(string value)
        {
            switch ((value ?? string.Empty).ToLowerInvariant())
            {
                case "net8":
                case "net10":
                case "net6":
                case "net462":
                case "zip":
                    return true;
                default:
                    return false;
            }
        }

        private static IReadOnlyList<string> GetPluginCommandArguments(ParsedArgs args)
        {
            var result = new List<string>();
            var tail = args.GetTailArguments(2);
            for (int i = 0; i < tail.Count; i++)
            {
                string arg = tail[i];
                if (string.Equals(arg, "--", StringComparison.Ordinal))
                {
                    result.AddRange(tail.Skip(i + 1));
                    break;
                }

                if (IsPluginHostOptionWithValue(arg))
                {
                    i++;
                    continue;
                }

                if (IsPluginHostFlag(arg))
                {
                    continue;
                }

                result.Add(arg);
            }
            return result;
        }

        private static bool IsPluginHostOptionWithValue(string arg)
        {
            return string.Equals(arg, "--plugin-dir", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPluginHostFlag(string arg)
        {
            return string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--include-gui-plugin-dir", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("wcr2 - WzComparerR2 command line tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 info <file-or-dir> [--json]");
            Console.WriteLine("  wcr2 tree <file-or-dir> [--path <wz-path>] [--depth <n>] [--limit <n>] [--json]");
            Console.WriteLine("  wcr2 list <file-or-dir> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 search <file-or-dir> --name <text> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 search <file-or-dir> --value <text> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 search <file-or-dir> --match-path <glob-or-regex> [--type <type>] [--json]");
            Console.WriteLine("  wcr2 compare <old-file-or-dir> <new-file-or-dir> [--path <wz-path>] [--type added|removed|changed] [--format json|markdown] [--out <path>] [--json]");
            Console.WriteLine("  wcr2 dump <file-or-dir> --path <wz-path> [--format json|xml|raw] [--out <path>]");
            Console.WriteLine("  wcr2 extract <file-or-dir> --path <wz-path> --out <output-dir> [--recursive] [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 sound list <file-or-dir> [--path <wz-path>] [--max-results <n>] [--json]");
            Console.WriteLine("  wcr2 sound export <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 sound export-all <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 image list <file-or-dir> [--path <wz-path>] [--max-results <n>] [--json]");
            Console.WriteLine("  wcr2 image search [<file-or-dir>] --query <png> [--data-dir <Data>] [--scope ui,item,skill,...] [--path <wz-path>] [--out <output-dir>] [--max-results <n>] [--min-score <0..1>] [--cache-dir <dir>] [--trust-cache] [--no-refine] [--json]");
            Console.WriteLine("  wcr2 image export <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 image export-all <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 video list <file-or-dir> [--path <wz-path>] [--max-results <n>] [--json]");
            Console.WriteLine("  wcr2 video export <file-or-dir> --path <wz-path> --out <output-dir> [--format mcv|frames|png|gif|both] [--ffmpeg <path>] [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 video export-all <file-or-dir> --path <wz-path> --out <output-dir> [--format mcv|frames|png|gif|both] [--ffmpeg <path>] [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 skill info [<wz-file-or-dir>] --id <id> [--skill-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 skill full [<skill-wz-file-or-dir>] --id <id> [--skill-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--level <n>] [--format json|xml|text] [--out <path>]");
            Console.WriteLine("  wcr2 skill search-name [<skill-wz-file-or-dir>] --name <text> [--job-code <code>] [--data-dir <dir>] [--max-results <n>] [--json]");
            Console.WriteLine("  wcr2 skill resolve-name [<skill-wz-file-or-dir>] --name <text> [--job-code <code>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 skill sprite [<skill-wz-file-or-dir>] --id <id> --out <dir> [--skill-wz <file-or-dir>] [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--branch icon,effect,hit] [--json]");
            Console.WriteLine("  wcr2 skill export [<skill-wz-file-or-dir>] --id <id> --out <dir> [--skill-wz <file-or-dir>] [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--sound-wz <file-or-dir>] [--related-key <name>] [--video-format mcv|frames|png|gif|both] [--branch auto|icon,effect,hit] [--json]");
            Console.WriteLine("  wcr2 skill export-batch [<skill-wz-file-or-dir>] --ids <id,id>|--ids-file <path>|--names-file <path> --out-root <dir> [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--sound-wz <file-or-dir>] [--output-pattern <pattern>] [--skip-existing] [--continue-on-error] [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 item info [<wz-file-or-dir>] --id <id> [--item-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 item icon [<item-or-data-dir>] --name <exact-name>|--id <id> --out <dir> [--data-dir <dir>] [--string-wz <file-or-dir>] [--canvas-wz <file-or-dir>] [--category cash|consume|install|etc|pet] [--json]");
            Console.WriteLine("  wcr2 gear info [<wz-file-or-dir>] --id <id> [--character-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 mob info [<wz-file-or-dir>] --id <id> [--mob-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 npc info [<wz-file-or-dir>] --id <id> [--npc-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 quest info [<wz-file-or-dir>] --id <id> [--quest-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 map info [<wz-file-or-dir>] --id <id> [--map-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 map objects <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map portals <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 animate frames <wz-file-or-dir> --path <wz-path> --out <dir> [--json]");
            Console.WriteLine("  wcr2 animate gif <wz-file-or-dir> --path <wz-path> --out <file.gif> [--background transparent|#RRGGBB] [--min-alpha <0-255>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            Console.WriteLine("  wcr2 animate apng <wz-file-or-dir> --path <wz-path> --out <file.png> [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--optimize] [--json]");
            Console.WriteLine("  wcr2 animate ffmpeg <wz-file-or-dir> --path <wz-path> --out <file> [--ffmpeg <path>] [--ffmpeg-args <format>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            Console.WriteLine("  wcr2 avatar inspect --code <code> [--json]");
            Console.WriteLine("  wcr2 avatar unpack --code <code>");
            Console.WriteLine("  wcr2 lua run <script.lua> [--wz <file-or-dir>] [--dry-run] [--json]");
            Console.WriteLine("  wcr2 lua eval <code> [--wz <file-or-dir>] [--dry-run] [--json]");
            Console.WriteLine("  wcr2 network server-info [--host <host>] [--port <port>] [--connect] [--json]");
            Console.WriteLine("  wcr2 network send --message <text> [--host <host>] [--port <port>] [--json]");
            Console.WriteLine("  wcr2 update check [--asset net8|net10|net6|net462|zip] [--json]");
            Console.WriteLine("  wcr2 update download --out <dir> [--asset net8|net10|net6|net462|zip] [--json]");
            Console.WriteLine("  wcr2 update apply [--asset net8|net10|net6|net462|zip] [--updater <path>] [--execute] [--json]");
            Console.WriteLine("  wcr2 config list|get|set|unset|path [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine("  wcr2 plugin list|commands [--plugin-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 plugin inspect <assembly.dll> [--json]");
            Console.WriteLine("  wcr2 plugin run <command> [args...] [--plugin-dir <dir>]");
            Console.WriteLine("  wcr2 patch inspect <patch-file> [--json]");
            Console.WriteLine("  wcr2 patch dry-run <patch-file> --target <dir> [--json]");
            Console.WriteLine("  wcr2 patch apply <patch-file> --target <dir> --out <dir> [--log <file>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Common options:");
            Console.WriteLine("  --use-base-wz       Load with Base.wz link behavior where supported.");
            Console.WriteLine("  --fallback <path>   Fallback WZ file or folder.");
            Console.WriteLine("  --extract-images    Extract image nodes while traversing.");
            Console.WriteLine("  --json              Emit JSON output.");
            Console.WriteLine("  --quiet             Suppress stdout for successful commands.");
            Console.WriteLine("  --verbose           Include exception details on stderr when a command fails.");
            Console.WriteLine("  --no-color          Disable colored output; accepted for script compatibility.");
            Console.WriteLine("  --format xml        Export selected node as XML instead of loose files.");
            Console.WriteLine("  --regex             Treat --match-path as a regular expression.");
            Console.WriteLine("  --ignore-image-binary  Skip pixel-level image comparison where supported.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  wcr2 info Base.wz");
            Console.WriteLine("  wcr2 tree Base.wz --depth 2");
            Console.WriteLine("  wcr2 list Base.wz --path Character");
            Console.WriteLine("  wcr2 search String.wz --name Maple --json");
            Console.WriteLine("  wcr2 search Base.wz --match-path \"*/Canvas\" --type png");
            Console.WriteLine("  wcr2 compare old/Base.wz new/Base.wz --json");
            Console.WriteLine("  wcr2 extract Base.wz --path String --out out/string --recursive");
            Console.WriteLine("  wcr2 sound list Data/Sound --max-results 20");
            Console.WriteLine("  wcr2 sound export Data/Sound --path AchievementEff.img/GradeUp --out out/sound");
            Console.WriteLine("  wcr2 image list Data/Mob_Canvas --path 0100100.img --max-results 20");
            Console.WriteLine("  wcr2 image search --data-dir Data --query query.png --scope ui --out out/image-search --json");
            Console.WriteLine("  wcr2 image export Data/Mob_Canvas --path 0100100.img/stand/0 --out out/image");
            Console.WriteLine("  wcr2 video export Data/Packs/Skill_00006.ms --path Skill/524.img/skill/5241503/screen2/video --format gif --out out/firecracker-video");
            Console.WriteLine("  wcr2 skill info Skill.wz --id 1001004 --string-wz String.wz --json");
            Console.WriteLine("  wcr2 skill full Data/Skill --id 3001004 --string-wz Data/String --format json");
            Console.WriteLine("  wcr2 skill full --data-dir Data --id 1001008 --format json");
            Console.WriteLine("  wcr2 skill search-name --data-dir Data --name \"파이어크래커\" --json");
            Console.WriteLine("  wcr2 skill resolve-name --data-dir Data --name \"파이어크래커\" --job-code 524 --json");
            Console.WriteLine("  wcr2 skill sprite --data-dir Data --id 1121008 --branch effect,hit --out out/skill-1121008 --json");
            Console.WriteLine("  wcr2 skill export --data-dir Data --id 5241503 --video-format gif --out out/firecracker --json");
            Console.WriteLine("  wcr2 skill export-batch --data-dir Data --names-file skill-names.tsv --out-root out/skills --skip-existing --manifest out/skills/manifest.json --json");
            Console.WriteLine("  wcr2 item icon --data-dir Data --name \"미라클 큐브\" --out out/icons --json");
            Console.WriteLine("  wcr2 map portals Map.wz --id 100000000 --json");
            Console.WriteLine("  wcr2 animate frames Mob.wz --path 0100100.img/stand --out out/stand");
            Console.WriteLine("  wcr2 animate gif Mob.wz --path 0100100.img/stand --out out/stand.gif");
            Console.WriteLine("  wcr2 animate apng Mob.wz --path 0100100.img/stand --out out/stand.png");
            Console.WriteLine("  wcr2 animate ffmpeg Mob.wz --path 0100100.img/stand --out out/stand.mp4");
            Console.WriteLine("  wcr2 avatar inspect --code \"1002140,1040036,1060026\"");
            Console.WriteLine("  wcr2 lua run WzComparerR2.LuaConsole/Examples/DumpXml.lua --dry-run --json");
            Console.WriteLine("  wcr2 lua eval --code \"print('ok')\" --dry-run --json");
            Console.WriteLine("  wcr2 network server-info --json");
            Console.WriteLine("  wcr2 update check --asset net8 --json");
            Console.WriteLine("  wcr2 update download --asset net8 --out downloads");
            Console.WriteLine("  wcr2 config set default-wz /path/to/Base.wz");
            Console.WriteLine("  wcr2 config list");
            Console.WriteLine("  wcr2 plugin list --plugin-dir CliPlugin --json");
            Console.WriteLine("  wcr2 plugin commands");
            Console.WriteLine("  wcr2 patch inspect MaplePatch.patch --json");
            Console.WriteLine("  wcr2 patch dry-run MaplePatch.patch --target MapleStory --json");
            Console.WriteLine("  wcr2 patch apply MaplePatch.patch --target MapleStory --out MapleStory.patched --log patch.log");
        }

        private static void PrintMapHelp()
        {
            Console.WriteLine("wcr2 map - map metadata tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 map info <map-wz-file-or-dir> --id <map-id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 map objects <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map portals <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map life <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map reactors <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map render [<map-wz-file-or-dir>] --id <map-id> --out <map.png> --dry-run [--layer <n|all>] [--include-life] [--include-reactor] [--include-tooltip] [--json]");
        }

        private static void PrintSkillHelp()
        {
            Console.WriteLine("wcr2 skill - skill lookup tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 skill info <skill-wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 skill full [<skill-wz-file-or-dir>] --id <id> [--skill-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--level <n>] [--format json|xml|text] [--out <path>]");
            Console.WriteLine("  wcr2 skill search-name [<skill-wz-file-or-dir>] --name <text> [--job-code <code>] [--data-dir <dir>] [--max-results <n>] [--json]");
            Console.WriteLine("  wcr2 skill resolve-name [<skill-wz-file-or-dir>] --name <text> [--job-code <code>] [--data-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 skill sprite [<skill-wz-file-or-dir>] --id <id> --out <dir> [--skill-wz <file-or-dir>] [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--branch icon,effect,hit] [--json]");
            Console.WriteLine("  wcr2 skill export [<skill-wz-file-or-dir>] --id <id> --out <dir> [--skill-wz <file-or-dir>] [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--sound-wz <file-or-dir>] [--video-format mcv|frames|png|gif|both] [--branch auto|icon,effect,hit] [--json]");
            Console.WriteLine("  wcr2 skill export-batch [<skill-wz-file-or-dir>] --ids <id,id>|--ids-file <path>|--names-file <path> --out-root <dir> [--skill-wz <file-or-dir>] [--data-dir <dir>] [--canvas-wz <file-or-dir>] [--sound-wz <file-or-dir>] [--output-pattern <pattern>] [--video-format mcv|frames|png|gif|both] [--branch auto|icon,effect,hit] [--skip-existing] [--continue-on-error] [--manifest <json>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --allow-string-only  Emit string metadata when the skill id exists only in String.wz.");
            Console.WriteLine("  --data-dir <dir>     Add split Data candidates such as <dir>/Skill, <dir>/String, and lazy <dir>/Packs/Skill_*.ms fallback.");
            Console.WriteLine("  --skill-wz <path>    Add or replace the skill WZ file/folder input candidate.");
            Console.WriteLine("  --name <text>        Search/resolve a skill id from String/Skill.img by Korean or localized skill name.");
            Console.WriteLine("  --job-code <code>    Prefer or require a class/job code during skill name resolution.");
            Console.WriteLine("  --max-results <n>    Limit skill name search candidates.");
            Console.WriteLine("  --canvas-wz <path>   Override or add Skill/_Canvas input used to resolve _outlink sprite pixels.");
            Console.WriteLine("  --sound-wz <path>    Override or add Sound input used by skill export.");
            Console.WriteLine("  --include-sound      Also export Sound/Skill.img/<id> when using skill sprite.");
            Console.WriteLine("  --include-video      Also export Wz_Video screen nodes when using skill sprite; skill export includes them by default.");
            Console.WriteLine("  --skip-video         Disable Wz_Video export for skill export.");
            Console.WriteLine("  --video-format <fmt> Export skill videos as mcv, frames/png, gif, or both; default PNG frames.");
            Console.WriteLine("  --ffmpeg <path>      ffmpeg executable used when --video-format is frames/png/gif/both.");
            Console.WriteLine("  --branch <list>      Export auto/all/visual detected branches, or explicit paths such as effect,hit/0.");
            Console.WriteLine("  --include-related    Search related Skill/Effect/Character inputs for action/delay keys.");
            Console.WriteLine("  --related-key <name> Add a manual related key such as 6thFireCracker.");
            Console.WriteLine("  --related-wz <path>  Add an input for related action/screen sprite lookup.");
            Console.WriteLine("  --skip-related       Disable related action/delay lookup for skill export.");
            Console.WriteLine("  --direct-only        Export only pixels present in the skill node and do not follow _outlink.");
            Console.WriteLine("  --ids <list>         Export multiple skill ids in one process. Use with skill export-batch.");
            Console.WriteLine("  --ids-file <path>    Text lines are id or id<TAB>relative/output; JSON arrays may contain strings or { id, relativeOutput } objects.");
            Console.WriteLine("  --names <list>       Export multiple skill names in one process after name resolution.");
            Console.WriteLine("  --names-file <path>  Text lines are name, jobCode<TAB>name, jobCode<TAB>name<TAB>relative/output, jobName<TAB>jobCode<TAB>name, or jobName<TAB>jobCode<TAB>name<TAB>relative/output.");
            Console.WriteLine("  --out-root <dir>     Batch output root. Entries without a relative output write under <out-root>/<id>.");
            Console.WriteLine("  --output-pattern <p> Batch relative output pattern when an entry has no explicit relative output. Tokens: {id}, {name}, {jobCode}, {jobName}.");
            Console.WriteLine("  --skip-existing      Skip batch entries whose output already has export-result.json.");
            Console.WriteLine("  --continue-on-error  Keep processing later batch entries after a failed skill.");
            Console.WriteLine("  --manifest <json>    Write the batch summary JSON to a file.");
            Console.WriteLine("  JSON/XML/text output includes SourceProfile, LinkerStatus, and UnresolvedPlaceholders.");
            Console.WriteLine("  skill sprite/export writes skill-info.json and resources.json sidecars under --out.");
            Console.WriteLine("  resources.json file entries include size/format/hash and source frame metadata such as Origin, Z, Delay, and _outlink when present.");
        }

        private static void PrintDomainHelp(string kind)
        {
            string wzOption = string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase) ? "character" : kind;
            Console.WriteLine("wcr2 " + kind + " - " + kind + " lookup tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 " + kind + " info [<wz-file-or-dir>] --id <id> [--" + wzOption + "-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--json]");
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  wcr2 item icon [<item-or-data-dir>] --name <exact-name>|--id <id> --out <dir> [--data-dir <dir>] [--string-wz <file-or-dir>] [--canvas-wz <file-or-dir>] [--category cash|consume|install|etc|pet] [--json]");
            }
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --data-dir <dir>   Add split Data layout candidates such as <dir>/" + CliWzRepository.GetDefaultDataFolderName(kind) + " and <dir>/String.");
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  --name <text>      Resolve an exact String.wz item name before exporting its info/icon PNG.");
                Console.WriteLine("  --canvas-wz <path> Override the Item/<category>/_Canvas input used for icon PNG export.");
                Console.WriteLine("  --category <kind>  Use when an id cannot be mapped through String.wz.");
            }
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  --item-wz <path>   Accepted alias for Character/Item-style gear data inputs.");
            }
            if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  wcr2 skill full [<wz-file-or-dir>] --id <id> [--skill-wz <file-or-dir>] [--string-wz <file-or-dir>] [--data-dir <dir>] [--level <n>] [--format json|xml|text]");
                Console.WriteLine("  wcr2 skill sprite [<wz-file-or-dir>] --id <id> --out <dir> [--canvas-wz <file-or-dir>] [--branch icon,effect,hit] [--json]");
                Console.WriteLine("  wcr2 skill export [<wz-file-or-dir>] --id <id> --out <dir> [--sound-wz <file-or-dir>] [--branch auto|icon,effect,hit] [--json]");
            }
        }

        private static void PrintAnimateHelp()
        {
            Console.WriteLine("wcr2 animate - animation export tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 animate frames <wz-file-or-dir> --path <wz-path> --out <dir> [--json]");
            Console.WriteLine("  wcr2 animate gif <wz-file-or-dir> --path <wz-path> --out <file.gif> [--background transparent|#RRGGBB] [--min-alpha <0-255>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            Console.WriteLine("  wcr2 animate apng <wz-file-or-dir> --path <wz-path> --out <file.png> [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--optimize] [--json]");
            Console.WriteLine("  wcr2 animate ffmpeg <wz-file-or-dir> --path <wz-path> --out <file> [--ffmpeg <path>] [--ffmpeg-args <format>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
        }

        private static void PrintAvatarHelp()
        {
            Console.WriteLine("wcr2 avatar - avatar code tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 avatar inspect --code <code> [--json]");
            Console.WriteLine("  wcr2 avatar unpack --code <code>");
            Console.WriteLine("  wcr2 avatar render --code <code> --out <avatar.png> --dry-run [--action <action>] [--emotion <emotion>] [--offline] [--api-key <key>] [--json]");
            Console.WriteLine("  wcr2 avatar render --items <ids> --out <avatar.png> --dry-run [--action <action>] [--emotion <emotion>] [--json]");
        }

        private static void PrintLuaHelp()
        {
            Console.WriteLine("wcr2 lua - Lua script tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 lua run <script.lua> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]");
            Console.WriteLine("  wcr2 lua eval <code> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]");
            Console.WriteLine("  wcr2 lua eval --code <code> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]");
        }

        private static void PrintNetworkHelp()
        {
            Console.WriteLine("wcr2 network - network command tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 network server-info [--host <host>] [--port <port>] [--connect] [--json]");
            Console.WriteLine("  wcr2 network chat [--host <host>] [--port <port>] [--json]");
            Console.WriteLine("  wcr2 network send --message <text> [--host <host>] [--port <port>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Notes:");
            Console.WriteLine("  network commands are non-interactive dry-runs unless server-info --connect is used for a TCP probe.");
            Console.WriteLine("  --interactive is reserved and currently rejected.");
        }

        private static void PrintUpdateHelp()
        {
            Console.WriteLine("wcr2 update - release update tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 update check [--repo <owner/name>] [--current-version <version>] [--asset net8|net10|net6|net462|zip] [--json]");
            Console.WriteLine("  wcr2 update download --out <dir> [--repo <owner/name>] [--asset net8|net10|net6|net462|zip] [--force] [--json]");
            Console.WriteLine("  wcr2 update apply [--repo <owner/name>] [--asset net8|net10|net6|net462|zip] [--updater <path>] [--download <zip>] [--execute] [--json]");
            Console.WriteLine();
            Console.WriteLine("Notes:");
            Console.WriteLine("  update apply is dry-run unless --execute is provided.");
            Console.WriteLine("  --execute requires an external WzComparerR2.Updater executable.");
        }

        private static void PrintConfigHelp()
        {
            Console.WriteLine("wcr2 config - CLI configuration tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 config path [--config <path>] [--json]");
            Console.WriteLine("  wcr2 config list [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine("  wcr2 config get <key> [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine("  wcr2 config set <key> <value> [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine("  wcr2 config unset <key> [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Default path:");
            Console.WriteLine("  Windows: %APPDATA%/WzComparerR2/wcr2.config.json");
            Console.WriteLine("  Unix:    $XDG_CONFIG_HOME/wzcomparerr2/wcr2.config.json or ~/.config/wzcomparerr2/wcr2.config.json");
            Console.WriteLine();
            Console.WriteLine("Profiles:");
            Console.WriteLine("  --profile <name> stores values under profiles.<name>.<key> and profile values override global fallback keys.");
        }

        private static void PrintPluginHelp()
        {
            Console.WriteLine("wcr2 plugin - CLI plugin tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 plugin list [--plugin-dir <dir>] [--include-gui-plugin-dir] [--json]");
            Console.WriteLine("  wcr2 plugin inspect <assembly.dll> [--json]");
            Console.WriteLine("  wcr2 plugin commands [--plugin-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 plugin run <command> [args...] [--plugin-dir <dir>]");
            Console.WriteLine();
            Console.WriteLine("Discovery:");
            Console.WriteLine("  Explicit --plugin-dir, config key plugin-dir, WCR2_CLI_PLUGIN_DIR, then CliPlugin beside the executable/current directory.");
            Console.WriteLine("  GUI Plugin directories are scanned only with --include-gui-plugin-dir.");
        }

        private static void PrintPatchHelp()
        {
            Console.WriteLine("wcr2 patch - patch inspection tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 patch inspect <patch-file> [--json]");
            Console.WriteLine("  wcr2 patch dry-run <patch-file> --target <dir> [--json]");
            Console.WriteLine("  wcr2 patch apply <patch-file> --target <dir> --out <dir> [--log <file>] [--json]");
        }

        private static void PrintMediaHelp(string kind)
        {
            Console.WriteLine("wcr2 " + kind + " - list and export " + kind + " assets");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 " + kind + " list <file-or-dir> [--path <wz-path>] [--max-results <n>] [--json]");
            if (string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  wcr2 video export <file-or-dir> --path <wz-path> --out <output-dir> [--format mcv|frames|png|gif|both] [--ffmpeg <path>] [--manifest <json>] [--json]");
                Console.WriteLine("  wcr2 video export-all <file-or-dir> --path <wz-path> --out <output-dir> [--format mcv|frames|png|gif|both] [--ffmpeg <path>] [--manifest <json>] [--json]");
            }
            else if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  wcr2 image search [<file-or-dir>] --query <png> [--data-dir <Data>] [--scope ui,item,skill,...] [--path <wz-path>] [--out <output-dir>] [--max-results <n>] [--min-score <0..1>] [--cache-dir <dir>] [--trust-cache] [--no-refine] [--json]");
                Console.WriteLine("  wcr2 image export <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]");
                Console.WriteLine("  wcr2 image export-all <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]");
            }
            else
            {
                Console.WriteLine("  wcr2 " + kind + " export <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]");
                Console.WriteLine("  wcr2 " + kind + " export-all <file-or-dir> --path <wz-path> --out <output-dir> [--manifest <json>] [--json]");
            }
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --path <wz-path>       WZ node path to inspect or export.");
            if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  --query <png>          Query PNG for perceptual image search.");
                Console.WriteLine("  --data-dir <Data>      Auto-search canvas roots under a Maple Data directory when no explicit input is provided.");
                Console.WriteLine("  --scope <list>         Comma-separated data domains for --data-dir search; default all.");
                Console.WriteLine("  --cache-dir <dir>      Fingerprint cache directory; default OS user cache.");
                Console.WriteLine("  --no-cache             Disable image-search fingerprint cache.");
                Console.WriteLine("  --rebuild-cache        Ignore existing cache and rebuild fingerprints.");
                Console.WriteLine("  --trust-cache          Skip source timestamp validation for faster cache reads.");
                Console.WriteLine("  --no-refine            Return cache/index scores without reopening top candidates for pixel scoring.");
                Console.WriteLine("  --refine-limit <n>     Number of cache/index candidates to refine; default max(50, max-results*8), capped at 250.");
                Console.WriteLine("  --min-score <0..1>     Keep matches at or above this score; default 0.");
                Console.WriteLine("  --min-alpha <0-255>    Alpha threshold used for transparent crop; default 16.");
            }
            Console.WriteLine("  --out <output-dir>     Directory for exported files.");
            Console.WriteLine("  --manifest <json>      Write export manifest JSON.");
            Console.WriteLine("  --max-results <n>      Limit list/search results; list default 100, image search default 20.");
            if (string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  --format <format>      Video export format: mcv, frames/png, gif, or both; default mcv.");
                Console.WriteLine("  --decode [format]      Alias for --format frames, or for the supplied frames/png/gif/both value.");
                Console.WriteLine("  --ffmpeg <path>        ffmpeg executable used for frames/png/gif/both decode; default ffmpeg.");
                Console.WriteLine("  --max-frames <n>       Decode only first n frames when format is frames/png/gif/both.");
                Console.WriteLine("  --keep-video-work      Keep generated IVF work files next to decoded output.");
            }
            Console.WriteLine("  --json                 Emit JSON output.");
            Console.WriteLine();
            if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Note:");
                Console.WriteLine("  image export uses WzComparerR2's existing System.Drawing PNG path on Windows.");
                Console.WriteLine("  On macOS/Linux, image export uses the CLI cross-platform PNG writer for common WZ texture formats.");
                Console.WriteLine("  image search can auto-scan Data/*/_Canvas roots with --data-dir and stores compact fingerprints in the user cache.");
            }
        }

        private static string FormatNullableBool(bool? value)
        {
            return value.HasValue ? value.Value.ToString().ToLowerInvariant() : "unknown";
        }
    }

}
