using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal sealed class AvatarCodeDto
    {
        public string RawCode { get; set; }
        public bool IsValid { get; set; }
        public List<AvatarItemDto> Items { get; set; }
        public List<string> Warnings { get; set; }

        public static AvatarCodeDto Parse(string code)
        {
            var dto = new AvatarCodeDto
            {
                RawCode = code,
                Items = new List<AvatarItemDto>(),
                Warnings = new List<string>()
            };

            foreach (Match match in Regex.Matches(code ?? string.Empty, @"\d{4,}"))
            {
                if (dto.Items.Any(item => item.Id == match.Value))
                {
                    continue;
                }

                dto.Items.Add(AvatarItemDto.FromId(match.Value));
            }

            dto.IsValid = dto.Items.Count > 0;
            if (!dto.IsValid)
            {
                dto.Warnings.Add("No item ids were found in the avatar code.");
            }
            if (dto.Items.Any(item => item.Category == "unknown"))
            {
                dto.Warnings.Add("Some ids could not be classified by MapleStory item id prefix.");
            }

            return dto;
        }
    }

    internal sealed class AvatarRenderPlanDto
    {
        public string Mode { get; set; }
        public string OutputPath { get; set; }
        public string Action { get; set; }
        public string Emotion { get; set; }
        public bool Offline { get; set; }
        public bool ApiKeyProvided { get; set; }
        public bool CanRender { get; set; }
        public List<AvatarItemDto> Items { get; set; }
        public List<AvatarRenderCandidateDto> Candidates { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> Blockers { get; set; }

        public static AvatarRenderPlanDto Create(AvatarCodeDto code, string outputPath, string action, string emotion, bool offline, bool apiKeyProvided)
        {
            var warnings = new List<string>(code.Warnings ?? new List<string>());
            if (!offline && !apiKeyProvided)
            {
                warnings.Add("No MapleStory OpenAPI key was provided; dry-run will not attempt remote code expansion.");
            }

            return new AvatarRenderPlanDto
            {
                Mode = "dry-run",
                OutputPath = outputPath,
                Action = action,
                Emotion = emotion,
                Offline = offline,
                ApiKeyProvided = apiKeyProvided,
                CanRender = false,
                Items = code.Items,
                Candidates = code.Items.Select(AvatarRenderCandidateDto.FromItem).ToList(),
                Warnings = warnings,
                Blockers = new List<string>
                {
                    "AvatarCommon.AvatarCanvas and AvatarCanvasManager resolve WZ data through PluginManager.FindWz.",
                    "CLI must inject a headless WZ repository before AvatarCommon can render without the WinForms plugin host.",
                    "PNG rendering uses System.Drawing/GDI+ paths and still needs Windows verification with a real Maple client."
                }
            };
        }
    }

    internal sealed class AvatarRenderCandidateDto
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string SlotGuess { get; set; }
        public List<string> CandidatePaths { get; set; }

        public static AvatarRenderCandidateDto FromItem(AvatarItemDto item)
        {
            return new AvatarRenderCandidateDto
            {
                Id = item.Id,
                Category = item.Category,
                SlotGuess = item.SlotGuess,
                CandidatePaths = GuessCandidatePaths(item)
            };
        }

        private static List<string> GuessCandidatePaths(AvatarItemDto item)
        {
            string id = NormalizeItemId(item.Id);
            var paths = new List<string>();
            switch (item.Category)
            {
                case "body":
                case "head":
                    paths.Add("Character/" + id + ".img");
                    break;
                case "face":
                    paths.Add("Character/Face/" + id + ".img");
                    break;
                case "hair":
                    paths.Add("Character/Hair/" + id + ".img");
                    break;
                case "equipment":
                    string folder = EquipmentFolder(item.SlotGuess);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        paths.Add("Character/" + folder + "/" + id + ".img");
                    }
                    paths.Add("Character/" + id + ".img");
                    break;
                default:
                    paths.Add("Character/" + id + ".img");
                    break;
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeItemId(string id)
        {
            int numericId;
            if (int.TryParse(id, out numericId) && numericId >= 0)
            {
                return numericId.ToString("D8", CultureInfo.InvariantCulture);
            }

            return id;
        }

        private static string EquipmentFolder(string slot)
        {
            switch (slot)
            {
                case "cap":
                    return "Cap";
                case "face-accessory":
                case "eye-accessory":
                case "earrings":
                case "pendant":
                case "belt":
                case "shoulder":
                case "pocket":
                case "badge":
                case "emblem":
                case "totem":
                    return "Accessory";
                case "coat":
                    return "Coat";
                case "longcoat":
                    return "Longcoat";
                case "pants":
                    return "Pants";
                case "shoes":
                    return "Shoes";
                case "glove":
                    return "Glove";
                case "shield":
                    return "Shield";
                case "cape":
                    return "Cape";
                case "ring":
                    return "Ring";
                case "medal":
                    return "Medal";
                case "weapon":
                    return "Weapon";
                default:
                    return null;
            }
        }
    }

    internal sealed class AvatarItemDto
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string SlotGuess { get; set; }

        public static AvatarItemDto FromId(string id)
        {
            int numericId;
            int.TryParse(id, out numericId);
            int prefix = numericId / 10000;

            string slot = GuessEquipmentSlot(prefix);
            return new AvatarItemDto
            {
                Id = id,
                Category = slot == "unknown" ? GuessGeneralCategory(prefix, numericId) : "equipment",
                SlotGuess = slot
            };
        }

        private static string GuessEquipmentSlot(int prefix)
        {
            switch (prefix)
            {
                case 100:
                    return "cap";
                case 101:
                    return "face-accessory";
                case 102:
                    return "eye-accessory";
                case 103:
                    return "earrings";
                case 104:
                    return "coat";
                case 105:
                    return "longcoat";
                case 106:
                    return "pants";
                case 107:
                    return "shoes";
                case 108:
                    return "glove";
                case 109:
                    return "shield";
                case 110:
                    return "cape";
                case 111:
                    return "ring";
                case 112:
                    return "pendant";
                case 113:
                    return "belt";
                case 114:
                    return "medal";
                case 115:
                    return "shoulder";
                case 116:
                    return "pocket";
                case 118:
                    return "badge";
                case 119:
                    return "emblem";
                case 120:
                    return "totem";
            }

            if (prefix >= 121 && prefix <= 170)
            {
                return "weapon";
            }

            return "unknown";
        }

        private static string GuessGeneralCategory(int prefix, int numericId)
        {
            if (numericId >= 2000 && numericId < 3000)
            {
                return "body";
            }
            if (numericId >= 12000 && numericId < 13000)
            {
                return "head";
            }
            if (numericId >= 20000 && numericId < 30000)
            {
                return "face";
            }
            if (numericId >= 30000 && numericId < 50000)
            {
                return "hair";
            }
            if (prefix >= 200 && prefix <= 245)
            {
                return "use";
            }
            if (prefix >= 300 && prefix <= 399)
            {
                return "install";
            }
            if (prefix >= 400 && prefix <= 499)
            {
                return "etc";
            }
            if (prefix >= 500 && prefix <= 599)
            {
                return "cash";
            }
            return "unknown";
        }
    }

    internal sealed class MapRenderPlanDto
    {
        public string Mode { get; set; }
        public string Id { get; set; }
        public string WzInputPath { get; set; }
        public string OutputPath { get; set; }
        public string Layer { get; set; }
        public bool IncludeLife { get; set; }
        public bool IncludeReactor { get; set; }
        public bool IncludeTooltip { get; set; }
        public bool CanRender { get; set; }
        public List<string> CandidatePaths { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> Blockers { get; set; }

        public static MapRenderPlanDto Create(string id, string wzInputPath, string outputPath, string layer, bool includeLife, bool includeReactor, bool includeTooltip)
        {
            return new MapRenderPlanDto
            {
                Mode = "dry-run",
                Id = id,
                WzInputPath = wzInputPath,
                OutputPath = outputPath,
                Layer = layer,
                IncludeLife = includeLife,
                IncludeReactor = includeReactor,
                IncludeTooltip = includeTooltip,
                CanRender = false,
                CandidatePaths = BuildCandidatePaths(id),
                Warnings = BuildWarnings(wzInputPath),
                Blockers = new List<string>
                {
                    "WzComparerR2.MapRender is built around MonoGame Game and GraphicsDevice lifecycle.",
                    "CLI needs an offscreen render target and deterministic viewport before screenshot export can run headless.",
                    "MapRender also depends on EmptyKeys UI, Bass/native runtime files, and real-client asset layout verification."
                }
            };
        }

        private static List<string> BuildCandidatePaths(string id)
        {
            var paths = new List<string>();
            int numericId;
            if (int.TryParse(id, out numericId) && numericId >= 0)
            {
                int group = numericId / 100000000;
                int shard = (numericId / 100000) % 1000;
                paths.Add("Map/Map/Map" + group + "/" + numericId.ToString("D9", CultureInfo.InvariantCulture) + ".img");
                paths.Add("Data/Map/Map/Map" + group + "/Map" + group + "_" + shard.ToString("D3", CultureInfo.InvariantCulture) + ".wz");
            }
            else
            {
                paths.Add("Map/Map/<group>/" + id + ".img");
            }

            return paths;
        }

        private static List<string> BuildWarnings(string wzInputPath)
        {
            var warnings = new List<string>();
            if (string.IsNullOrEmpty(wzInputPath))
            {
                warnings.Add("No map WZ input path was provided; dry-run only reports candidate paths.");
            }
            return warnings;
        }
    }

    internal sealed class MapMetadataDto
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public List<MapSectionItemDto> Portals { get; set; }
        public List<MapSectionItemDto> Life { get; set; }
        public List<MapSectionItemDto> Reactors { get; set; }
        public List<MapSectionItemDto> Objects { get; set; }
        public List<MapSectionItemDto> SelectedItems { get; set; }

        public static MapMetadataDto FromMapNode(string id, Wz_Node mapNode, string selectedSection)
        {
            var dto = new MapMetadataDto
            {
                Id = id,
                Path = mapNode.FullPath,
                Portals = CollectSection(mapNode, "portal", "portal"),
                Life = CollectSection(mapNode, "life", "life"),
                Reactors = CollectSection(mapNode, "reactor", "reactor"),
                Objects = CollectObjects(mapNode)
            };

            switch (selectedSection)
            {
                case "portals":
                    dto.SelectedItems = dto.Portals;
                    break;
                case "life":
                    dto.SelectedItems = dto.Life;
                    break;
                case "reactors":
                    dto.SelectedItems = dto.Reactors;
                    break;
                default:
                    dto.SelectedItems = dto.Objects;
                    break;
            }

            return dto;
        }

        private static List<MapSectionItemDto> CollectSection(Wz_Node mapNode, string sectionName, string kind)
        {
            var result = new List<MapSectionItemDto>();
            Wz_Node section = FindChild(mapNode, sectionName);
            if (section == null)
            {
                return result;
            }

            foreach (Wz_Node item in section.Nodes)
            {
                result.Add(MapSectionItemDto.FromNode(kind, null, item));
            }
            return result;
        }

        private static List<MapSectionItemDto> CollectObjects(Wz_Node mapNode)
        {
            var result = new List<MapSectionItemDto>();
            foreach (Wz_Node layer in mapNode.Nodes)
            {
                int layerNo;
                if (!int.TryParse(layer.Text, out layerNo))
                {
                    continue;
                }

                AddLayerItems(result, layer, layerNo, "obj", "object");
                AddLayerItems(result, layer, layerNo, "tile", "tile");
            }
            return result;
        }

        private static void AddLayerItems(List<MapSectionItemDto> result, Wz_Node layer, int layerNo, string sectionName, string kind)
        {
            Wz_Node section = FindChild(layer, sectionName);
            if (section == null)
            {
                return;
            }

            foreach (Wz_Node item in section.Nodes)
            {
                result.Add(MapSectionItemDto.FromNode(kind, layerNo, item));
            }
        }

        private static Wz_Node FindChild(Wz_Node node, string name)
        {
            foreach (Wz_Node child in node.Nodes)
            {
                if (string.Equals(child.Text, name, StringComparison.OrdinalIgnoreCase))
                {
                    return NodePath.ExtractImageNode(child, true);
                }
            }
            return null;
        }
    }

    internal sealed class MapSectionItemDto
    {
        public string Kind { get; set; }
        public string Index { get; set; }
        public int? Layer { get; set; }
        public string Path { get; set; }
        public string Summary { get; set; }
        public Dictionary<string, string> Properties { get; set; }

        public static MapSectionItemDto FromNode(string kind, int? layer, Wz_Node node)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Wz_Node child in node.Nodes)
            {
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    properties[child.Text] = value;
                }
            }

            return new MapSectionItemDto
            {
                Kind = kind,
                Index = node.Text,
                Layer = layer,
                Path = node.FullPath,
                Properties = properties,
                Summary = BuildSummary(properties)
            };
        }

        private static string BuildSummary(Dictionary<string, string> properties)
        {
            string[] keys = { "id", "type", "x", "y", "tm", "tn", "pn", "rx0", "rx1", "mobTime", "reactorTime" };
            var parts = new List<string>();
            foreach (string key in keys)
            {
                string value;
                if (properties.TryGetValue(key, out value))
                {
                    parts.Add(key + "=" + value);
                }
            }
            return string.Join(" ", parts);
        }
    }
}
