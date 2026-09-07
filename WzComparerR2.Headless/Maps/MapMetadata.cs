using System;
using System.Collections.Generic;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless
{
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
