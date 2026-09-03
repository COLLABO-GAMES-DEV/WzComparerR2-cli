using System;
using System.Collections.Generic;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static class DomainInfoFinder
    {
        public static Wz_Node FindDataNode(Wz_Node root, string kind, string id)
        {
            var candidates = BuildIdCandidates(kind, id);
            if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                return FindSkillDataNode(root, candidates);
            }

            foreach (Wz_Node node in Traverse(root, true))
            {
                if (MatchesAny(node.Text, candidates))
                {
                    if (IsPreferredDomainPath(node, kind))
                    {
                        return node;
                    }
                }
            }

            foreach (Wz_Node node in Traverse(root, true))
            {
                if (MatchesAny(node.Text, candidates))
                {
                    return node;
                }
            }

            return null;
        }

        private static Wz_Node FindSkillDataNode(Wz_Node root, List<string> candidates)
        {
            Wz_Node preferred = null;
            Wz_Node fallback = null;
            foreach (Wz_Node node in Traverse(root, true))
            {
                if (!MatchesAny(node.Text, candidates))
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = node;
                }

                if (IsDirectSkillDataPath(node, candidates))
                {
                    return node;
                }

                if (preferred == null && IsPreferredDomainPath(node, "skill"))
                {
                    preferred = node;
                }
            }

            return preferred ?? fallback;
        }

        private static bool IsDirectSkillDataPath(Wz_Node node, List<string> candidates)
        {
            string path = NormalizePath(node.FullPath).Trim('/');
            foreach (string candidate in candidates)
            {
                if (path.EndsWith("/skill/" + candidate, StringComparison.OrdinalIgnoreCase)
                    || path.Equals("skill/" + candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static DomainStringInfo FindStringInfo(Wz_Node root, string kind, string id)
        {
            var candidates = BuildIdCandidates(kind, id);
            foreach (Wz_Node node in Traverse(root, true))
            {
                if (!MatchesAny(node.Text, candidates))
                {
                    continue;
                }

                var info = DomainStringInfo.FromNode(node);
                if (info.HasValues && IsPreferredStringPath(node, kind))
                {
                    return info;
                }
            }

            foreach (Wz_Node node in Traverse(root, true))
            {
                if (!MatchesAny(node.Text, candidates))
                {
                    continue;
                }

                var info = DomainStringInfo.FromNode(node);
                if (info.HasValues)
                {
                    return info;
                }
            }

            return null;
        }

        public static List<DomainStringEntry> EnumerateStringInfos(Wz_Node root, string kind)
        {
            var results = new List<DomainStringEntry>();
            foreach (Wz_Node node in Traverse(root, true))
            {
                string id = TryReadStringInfoId(node.Text);
                if (string.IsNullOrEmpty(id) || !IsPreferredStringPath(node, kind))
                {
                    continue;
                }

                var info = DomainStringInfo.FromNode(node);
                if (!info.HasValues)
                {
                    continue;
                }

                results.Add(new DomainStringEntry
                {
                    Id = id,
                    Path = node.FullPath,
                    StringInfo = info
                });
            }

            return results;
        }

        private static string TryReadStringInfoId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string id = text.Trim();
            if (id.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
            {
                id = id.Substring(0, id.Length - 4);
            }

            long numericId;
            return long.TryParse(id, out numericId) && numericId >= 0
                ? numericId.ToString()
                : null;
        }

        private static List<string> BuildIdCandidates(string kind, string id)
        {
            var candidates = new List<string>();
            AddUnique(candidates, id);
            AddUnique(candidates, id + ".img");

            int numericId;
            if (int.TryParse(id, out numericId))
            {
                if (string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(candidates, numericId.ToString("d9"));
                    AddUnique(candidates, numericId.ToString("d9") + ".img");
                }
                else if (string.Equals(kind, "mob", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(candidates, numericId.ToString("d7"));
                    AddUnique(candidates, numericId.ToString("d7") + ".img");
                    AddUnique(candidates, numericId.ToString("d8"));
                    AddUnique(candidates, numericId.ToString("d8") + ".img");
                }
                else
                {
                    AddUnique(candidates, numericId.ToString("d8"));
                    AddUnique(candidates, numericId.ToString("d8") + ".img");
                }
            }

            return candidates;
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (!string.IsNullOrEmpty(value) && !list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(value);
            }
        }

        private static bool MatchesAny(string text, List<string> candidates)
        {
            return candidates.Any(candidate => string.Equals(text, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPreferredDomainPath(Wz_Node node, string kind)
        {
            string path = NormalizePath(node.FullPath);
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                kind = "item";
            }

            if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/skill", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/item", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("/character", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/map", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "mob", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/mob", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/npc", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "quest", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/quest", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return true;
        }

        private static bool IsPreferredStringPath(Wz_Node node, string kind)
        {
            string path = NormalizePath(node.FullPath);
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                kind = "item";
            }

            if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("skill.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.EndsWith("/skill.img", StringComparison.OrdinalIgnoreCase);
            }
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("cash.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("consume.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("eqp.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("etc.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("ins.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("pet.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("map.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "mob", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("mob.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("npc.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "quest", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("quest.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return true;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static IEnumerable<Wz_Node> Traverse(Wz_Node root, bool extractImages)
        {
            if (root == null)
            {
                yield break;
            }

            var stack = new Stack<Wz_Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Wz_Node node = NodePath.ExtractImageNode(stack.Pop(), extractImages);
                if (node == null)
                {
                    continue;
                }

                yield return node;

                var children = node.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
        }
    }

    internal sealed class DomainStringEntry
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public DomainStringInfo StringInfo { get; set; }
    }

    internal sealed class DomainStringInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Values { get; set; }

        public bool HasValues
        {
            get
            {
                return !string.IsNullOrEmpty(this.Name)
                    || !string.IsNullOrEmpty(this.Description)
                    || (this.Values != null && this.Values.Count > 0);
            }
        }

        public static DomainStringInfo FromNode(Wz_Node node)
        {
            var info = new DomainStringInfo
            {
                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
            info.Values["__path"] = node.FullPath;

            foreach (Wz_Node child in node.Nodes)
            {
                string value = NodeDto.FormatValue(child.Value);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                info.Values[child.Text] = value;
                if (string.Equals(child.Text, "name", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(child.Text, "mapName", StringComparison.OrdinalIgnoreCase))
                {
                    info.Name = value;
                }
                else if (string.Equals(child.Text, "desc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(child.Text, "h", StringComparison.OrdinalIgnoreCase))
                {
                    info.Description = value;
                }
            }

            if (string.IsNullOrEmpty(info.Name))
            {
                string streetName;
                string mapName;
                if (info.Values.TryGetValue("streetName", out streetName)
                    && info.Values.TryGetValue("mapName", out mapName))
                {
                    info.Name = streetName + " - " + mapName;
                }
            }

            return info;
        }
    }

    internal sealed class DomainInfoDto
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public string DataInputPath { get; set; }
        public string StringInputPath { get; set; }
        public List<string> DataInputCandidates { get; set; }
        public List<string> StringInputCandidates { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int ChildrenCount { get; set; }
        public int? LevelCount { get; set; }
        public int? MaxLevel { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public Dictionary<string, string> StringProperties { get; set; }
        public List<string> IconPaths { get; set; }

        public static DomainInfoDto FromNode(
            string kind,
            string id,
            Wz_Node node,
            DomainStringInfo stringInfo,
            string dataInputPath,
            string stringInputPath,
            IReadOnlyList<string> dataInputCandidates,
            IReadOnlyList<string> stringInputCandidates)
        {
            var dto = new DomainInfoDto
            {
                Kind = kind,
                Id = id,
                DataInputPath = dataInputPath,
                StringInputPath = stringInputPath,
                DataInputCandidates = dataInputCandidates == null ? new List<string>() : dataInputCandidates.ToList(),
                StringInputCandidates = stringInputCandidates == null ? new List<string>() : stringInputCandidates.ToList(),
                Path = node.FullPath,
                Name = stringInfo == null ? null : stringInfo.Name,
                Description = stringInfo == null ? null : stringInfo.Description,
                ChildrenCount = node.Nodes.Count,
                Properties = CollectImmediateProperties(node),
                StringProperties = stringInfo == null ? new Dictionary<string, string>() : stringInfo.Values,
                IconPaths = CollectIconPaths(node)
            };

            ApplyLevelInfo(dto, node);
            return dto;
        }

        private static Dictionary<string, string> CollectImmediateProperties(Wz_Node node)
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
            return properties;
        }

        private static List<string> CollectIconPaths(Wz_Node node)
        {
            var paths = new List<string>();
            var stack = new Stack<Wz_Node>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                Wz_Node current = NodePath.ExtractImageNode(stack.Pop(), true);
                if (current == null)
                {
                    continue;
                }

                if (current.Value is Wz_Png
                    && current.Text != null
                    && current.Text.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    paths.Add(current.FullPath);
                }

                var children = current.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
            return paths;
        }

        private static void ApplyLevelInfo(DomainInfoDto dto, Wz_Node node)
        {
            Wz_Node level = FindChild(node, "level");
            if (level == null)
            {
                return;
            }

            dto.LevelCount = level.Nodes.Count;
            int maxLevel = 0;
            foreach (Wz_Node child in level.Nodes)
            {
                int parsed;
                if (int.TryParse(child.Text, out parsed) && parsed > maxLevel)
                {
                    maxLevel = parsed;
                }
            }
            if (maxLevel > 0)
            {
                dto.MaxLevel = maxLevel;
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
}
