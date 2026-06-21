using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal sealed class SkillFullDto
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public string Mode { get; set; }
        public bool FoundData { get; set; }
        public string SourceProfile { get; set; }
        public string DataInputPath { get; set; }
        public string StringInputPath { get; set; }
        public List<string> DataInputCandidates { get; set; }
        public List<string> StringInputCandidates { get; set; }
        public string DataPath { get; set; }
        public string StringPath { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string PassiveDescription { get; set; }
        public int? Level { get; set; }
        public int? MaxLevel { get; set; }
        public int? MasterLevel { get; set; }
        public int? LevelCount { get; set; }
        public bool PreBigBangSkill { get; set; }
        public string RawSummary { get; set; }
        public string ResolvedSummary { get; set; }
        public int? NextLevel { get; set; }
        public string NextRawSummary { get; set; }
        public string NextResolvedSummary { get; set; }
        public Dictionary<string, string> Common { get; set; }
        public Dictionary<string, string> EffectiveProperties { get; set; }
        public List<SkillLevelPropertiesDto> LevelProperties { get; set; }
        public Dictionary<string, string> PvpCommon { get; set; }
        public Dictionary<string, string> StringProperties { get; set; }
        public Dictionary<string, bool> Flags { get; set; }
        public Dictionary<string, string> SpecialProperties { get; set; }
        public Dictionary<string, int> RequiredSkills { get; set; }
        public int? RequiredLevel { get; set; }
        public int? RequiredAmount { get; set; }
        public int StatPropertyCount { get; set; }
        public int VisualBranchCount { get; set; }
        public List<string> VisualBranches { get; set; }
        public SkillLinkerStatusDto LinkerStatus { get; set; }
        public List<string> Actions { get; set; }
        public List<SkillIconDto> Icons { get; set; }
        public List<SkillVectorDto> Vectors { get; set; }
        public Dictionary<string, List<SkillExtraPropertyDto>> AttackInfo { get; set; }
        public List<SkillSummaryVariantDto> SummaryVariants { get; set; }
        public List<string> UnresolvedPlaceholders { get; set; }
        public List<string> Diagnostics { get; set; }

        public static SkillFullDto FromStringOnly(
            string id,
            DomainStringInfo stringInfo,
            string stringInputPath,
            IReadOnlyList<string> dataInputCandidates,
            IReadOnlyList<string> stringInputCandidates)
        {
            var skillString = SkillStringInfo.FromDomainStringInfo(stringInfo);
            return new SkillFullDto
            {
                Kind = "skill",
                Id = id,
                Mode = "string-only",
                FoundData = false,
                SourceProfile = "string-only",
                StringInputPath = stringInputPath,
                DataInputCandidates = CopyList(dataInputCandidates),
                StringInputCandidates = CopyList(stringInputCandidates),
                StringPath = stringInfo == null ? null : stringInfo.Values.GetValueOrDefault("__path"),
                Name = skillString.Name,
                Description = skillString.Description,
                PassiveDescription = skillString.PassiveDescription,
                StringProperties = skillString.Values,
                Common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                EffectiveProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                LevelProperties = new List<SkillLevelPropertiesDto>(),
                PvpCommon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                SpecialProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                RequiredSkills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                VisualBranches = new List<string>(),
                LinkerStatus = SkillLinkerStatusDto.ForStringOnly(stringInputPath, dataInputCandidates, stringInputCandidates, skillString),
                Actions = new List<string>(),
                Icons = new List<SkillIconDto>(),
                Vectors = new List<SkillVectorDto>(),
                AttackInfo = new Dictionary<string, List<SkillExtraPropertyDto>>(StringComparer.OrdinalIgnoreCase),
                SummaryVariants = skillString.BuildVariants(),
                UnresolvedPlaceholders = CollectPlaceholders(skillString.BuildVariants().Select(item => item.Text)),
                Diagnostics = new List<string> { "No matching skill data node was found; output contains String.wz metadata only." }
            };
        }

        public static SkillFullDto FromNode(
            string id,
            Wz_Node node,
            DomainStringInfo stringInfo,
            int? requestedLevel,
            string dataInputPath,
            string stringInputPath,
            IReadOnlyList<string> dataInputCandidates,
            IReadOnlyList<string> stringInputCandidates)
        {
            var model = HeadlessSkillModel.FromNode(node);
            var skillString = SkillStringInfo.FromDomainStringInfo(stringInfo);
            int selectedLevel = ResolveLevel(model, requestedLevel);
            var effective = model.GetEffectiveProperties(selectedLevel);
            var diagnostics = new List<string>();
            var unresolvedPlaceholders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string rawSummary = skillString.SelectSummary(model.PreBigBangSkill, selectedLevel);
            string resolvedSummary = CliSkillSummaryResolver.Resolve(rawSummary, selectedLevel, effective, diagnostics, unresolvedPlaceholders);
            int? nextLevel = ResolveNextLevel(model, selectedLevel);
            string nextRawSummary = null;
            string nextResolvedSummary = null;
            if (nextLevel.HasValue)
            {
                nextRawSummary = skillString.SelectSummary(model.PreBigBangSkill, nextLevel.Value);
                nextResolvedSummary = CliSkillSummaryResolver.Resolve(nextRawSummary, nextLevel.Value, model.GetEffectiveProperties(nextLevel.Value), diagnostics, unresolvedPlaceholders);
            }

            if (stringInfo == null)
            {
                diagnostics.Add("No --string-wz was provided or no String.wz skill entry was found.");
            }
            if (string.IsNullOrEmpty(rawSummary))
            {
                diagnostics.Add("No skill summary template was found in string metadata.");
            }
            if (model.StatPropertyCount == 0 && (model.Icons.Count > 0 || model.VisualBranches.Count > 0))
            {
                diagnostics.Add("Skill data node contains visual/canvas data but no common/level scalar stat properties; MaxLevel and LevelCount may remain null for this input.");
            }

            return new SkillFullDto
            {
                Kind = "skill",
                Id = id,
                Mode = "charasim-headless",
                FoundData = true,
                SourceProfile = model.GetSourceProfile(),
                DataInputPath = dataInputPath,
                StringInputPath = stringInputPath,
                DataInputCandidates = CopyList(dataInputCandidates),
                StringInputCandidates = CopyList(stringInputCandidates),
                DataPath = node.FullPath,
                StringPath = stringInfo == null ? null : stringInfo.Values.GetValueOrDefault("__path"),
                Name = skillString.Name,
                Description = skillString.Description,
                PassiveDescription = skillString.PassiveDescription,
                Level = selectedLevel,
                MaxLevel = model.MaxLevel > 0 ? (int?)model.MaxLevel : null,
                MasterLevel = model.MasterLevel > 0 ? (int?)model.MasterLevel : null,
                LevelCount = model.LevelProperties.Count > 0 ? (int?)model.LevelProperties.Count : null,
                PreBigBangSkill = model.PreBigBangSkill,
                RawSummary = rawSummary,
                ResolvedSummary = resolvedSummary,
                NextLevel = nextLevel,
                NextRawSummary = nextRawSummary,
                NextResolvedSummary = nextResolvedSummary,
                Common = model.Common,
                EffectiveProperties = effective,
                LevelProperties = model.LevelProperties
                    .Select(item => new SkillLevelPropertiesDto { Level = item.Key, Properties = item.Value })
                    .ToList(),
                PvpCommon = model.PvpCommon,
                StringProperties = skillString.Values,
                Flags = model.Flags,
                SpecialProperties = model.SpecialProperties,
                RequiredSkills = model.RequiredSkills,
                RequiredLevel = model.RequiredLevel,
                RequiredAmount = model.RequiredAmount,
                StatPropertyCount = model.StatPropertyCount,
                VisualBranchCount = model.VisualBranches.Count,
                VisualBranches = model.VisualBranches,
                LinkerStatus = SkillLinkerStatusDto.ForNode(
                    dataInputPath,
                    stringInputPath,
                    dataInputCandidates,
                    stringInputCandidates,
                    model,
                    skillString,
                    rawSummary,
                    unresolvedPlaceholders.Count),
                Actions = model.Actions,
                Icons = model.Icons,
                Vectors = model.Vectors,
                AttackInfo = model.AttackInfo,
                SummaryVariants = skillString.BuildVariants(),
                UnresolvedPlaceholders = unresolvedPlaceholders.ToList(),
                Diagnostics = diagnostics
            };
        }

        private static List<string> CopyList(IReadOnlyList<string> values)
        {
            return values == null ? new List<string>() : values.ToList();
        }

        private static List<string> CollectPlaceholders(IEnumerable<string> texts)
        {
            var placeholders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in texts ?? Enumerable.Empty<string>())
            {
                CliSkillSummaryResolver.CollectPlaceholders(text, placeholders);
            }
            return placeholders.ToList();
        }

        private static int ResolveLevel(HeadlessSkillModel model, int? requestedLevel)
        {
            if (requestedLevel.HasValue)
            {
                return requestedLevel.Value;
            }
            if (model.MaxLevel > 0)
            {
                return model.MaxLevel;
            }
            if (model.LevelProperties.Count > 0)
            {
                return model.LevelProperties.Keys.Max();
            }
            return 1;
        }

        private static int? ResolveNextLevel(HeadlessSkillModel model, int selectedLevel)
        {
            bool disableNextLevelInfo;
            if (model.Flags.TryGetValue("disableNextLevelInfo", out disableNextLevelInfo) && disableNextLevelInfo)
            {
                return null;
            }

            if (model.MaxLevel > selectedLevel)
            {
                return selectedLevel + 1;
            }

            if (model.LevelProperties.Count > 0)
            {
                foreach (int level in model.LevelProperties.Keys)
                {
                    if (level > selectedLevel)
                    {
                        return level;
                    }
                }
            }

            return null;
        }
    }

    internal sealed class SkillLinkerStatusDto
    {
        public string Resolver { get; set; }
        public string Status { get; set; }
        public bool FoundData { get; set; }
        public bool FoundString { get; set; }
        public bool FoundStats { get; set; }
        public bool FoundVisuals { get; set; }
        public bool FoundSummaryTemplate { get; set; }
        public bool SummaryFullyResolved { get; set; }
        public int UnresolvedPlaceholderCount { get; set; }
        public bool GuiStringLinkerLoaded { get; set; }
        public string DataInputPath { get; set; }
        public string StringInputPath { get; set; }
        public int DataInputCandidateCount { get; set; }
        public int StringInputCandidateCount { get; set; }
        public List<string> Notes { get; set; }

        public static SkillLinkerStatusDto ForStringOnly(
            string stringInputPath,
            IReadOnlyList<string> dataInputCandidates,
            IReadOnlyList<string> stringInputCandidates,
            SkillStringInfo skillString)
        {
            var variants = skillString == null
                ? new List<SkillSummaryVariantDto>()
                : skillString.BuildVariants();
            var unresolved = SkillFullDtoPlaceholderCounter.Count(variants.Select(item => item.Text));
            return new SkillLinkerStatusDto
            {
                Resolver = "cli-headless",
                Status = "string-only",
                FoundData = false,
                FoundString = skillString != null && skillString.Values.Count > 0,
                FoundStats = false,
                FoundVisuals = false,
                FoundSummaryTemplate = variants.Count > 0,
                SummaryFullyResolved = unresolved == 0,
                UnresolvedPlaceholderCount = unresolved,
                GuiStringLinkerLoaded = false,
                StringInputPath = stringInputPath,
                DataInputCandidateCount = dataInputCandidates == null ? 0 : dataInputCandidates.Count,
                StringInputCandidateCount = stringInputCandidates == null ? 0 : stringInputCandidates.Count,
                Notes = new List<string> { "String metadata was found, but no matching skill data node was found." }
            };
        }

        public static SkillLinkerStatusDto ForNode(
            string dataInputPath,
            string stringInputPath,
            IReadOnlyList<string> dataInputCandidates,
            IReadOnlyList<string> stringInputCandidates,
            HeadlessSkillModel model,
            SkillStringInfo skillString,
            string rawSummary,
            int unresolvedPlaceholderCount)
        {
            bool foundStats = model != null && model.StatPropertyCount > 0;
            bool foundVisuals = model != null && (model.Icons.Count > 0 || model.VisualBranches.Count > 0);
            bool foundString = skillString != null && skillString.Values.Count > 0;
            bool foundSummary = !string.IsNullOrEmpty(rawSummary);
            var notes = new List<string>();
            if (!foundString)
            {
                notes.Add("No String.wz metadata was linked for this skill id.");
            }
            if (!foundStats && foundVisuals)
            {
                notes.Add("Skill data contains visual/canvas branches but no scalar common/level stats.");
            }
            if (foundSummary && unresolvedPlaceholderCount > 0)
            {
                notes.Add("Summary template still contains placeholders because matching scalar stats were not available.");
            }

            return new SkillLinkerStatusDto
            {
                Resolver = "cli-headless",
                Status = DetermineStatus(foundString, foundStats, foundVisuals, foundSummary, unresolvedPlaceholderCount),
                FoundData = model != null,
                FoundString = foundString,
                FoundStats = foundStats,
                FoundVisuals = foundVisuals,
                FoundSummaryTemplate = foundSummary,
                SummaryFullyResolved = foundSummary && unresolvedPlaceholderCount == 0,
                UnresolvedPlaceholderCount = unresolvedPlaceholderCount,
                GuiStringLinkerLoaded = false,
                DataInputPath = dataInputPath,
                StringInputPath = stringInputPath,
                DataInputCandidateCount = dataInputCandidates == null ? 0 : dataInputCandidates.Count,
                StringInputCandidateCount = stringInputCandidates == null ? 0 : stringInputCandidates.Count,
                Notes = notes
            };
        }

        private static string DetermineStatus(bool foundString, bool foundStats, bool foundVisuals, bool foundSummary, int unresolvedPlaceholderCount)
        {
            if (!foundString)
            {
                return "missing-string";
            }
            if (!foundStats && foundVisuals)
            {
                return "visual-only";
            }
            if (!foundSummary)
            {
                return "missing-summary";
            }
            if (unresolvedPlaceholderCount > 0)
            {
                return "partial";
            }
            return "resolved";
        }
    }

    internal static class SkillFullDtoPlaceholderCounter
    {
        public static int Count(IEnumerable<string> texts)
        {
            var placeholders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in texts ?? Enumerable.Empty<string>())
            {
                CliSkillSummaryResolver.CollectPlaceholders(text, placeholders);
            }
            return placeholders.Count;
        }
    }

    internal sealed class HeadlessSkillModel
    {
        public Dictionary<string, string> Common { get; private set; }
        public SortedDictionary<int, Dictionary<string, string>> LevelProperties { get; private set; }
        public Dictionary<string, string> PvpCommon { get; private set; }
        public Dictionary<string, bool> Flags { get; private set; }
        public Dictionary<string, string> SpecialProperties { get; private set; }
        public Dictionary<string, int> RequiredSkills { get; private set; }
        public int? RequiredLevel { get; private set; }
        public int? RequiredAmount { get; private set; }
        public int StatPropertyCount { get; private set; }
        public int MasterLevel { get; private set; }
        public int MaxLevel { get; private set; }
        public bool PreBigBangSkill { get; private set; }
        public List<string> VisualBranches { get; private set; }
        public List<string> Actions { get; private set; }
        public List<SkillIconDto> Icons { get; private set; }
        public List<SkillVectorDto> Vectors { get; private set; }
        public Dictionary<string, List<SkillExtraPropertyDto>> AttackInfo { get; private set; }

        private HeadlessSkillModel()
        {
            Common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LevelProperties = new SortedDictionary<int, Dictionary<string, string>>();
            PvpCommon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            SpecialProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RequiredSkills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            VisualBranches = new List<string>();
            Actions = new List<string>();
            Icons = new List<SkillIconDto>();
            Vectors = new List<SkillVectorDto>();
            AttackInfo = new Dictionary<string, List<SkillExtraPropertyDto>>(StringComparer.OrdinalIgnoreCase);
        }

        public static HeadlessSkillModel FromNode(Wz_Node node)
        {
            var model = new HeadlessSkillModel();
            foreach (Wz_Node child in node.Nodes)
            {
                string name = child.Text;
                switch (name)
                {
                    case "icon":
                    case "iconMouseOver":
                    case "iconDisabled":
                        model.Icons.Add(SkillIconDto.FromNode(name, child));
                        break;
                    case "common":
                        model.ReadCommon(child);
                        break;
                    case "PVPcommon":
                        model.PvpCommon = CollectScalarProperties(child);
                        break;
                    case "level":
                        model.ReadLevels(child);
                        break;
                    case "req":
                        model.ReadRequirements(child);
                        break;
                    case "action":
                        model.ReadActions(child);
                        break;
                    case "masterLevel":
                        model.MasterLevel = child.GetValue<int>();
                        model.SpecialProperties[name] = model.MasterLevel.ToString();
                        break;
                    case "reqLev":
                        model.RequiredLevel = child.GetValue<int>();
                        model.SpecialProperties[name] = model.RequiredLevel.ToString();
                        break;
                    case "hyper":
                    case "vSkill":
                    case "vehicleID":
                        model.SpecialProperties[name] = NodeDto.FormatValue(child.Value);
                        break;
                    case "hyperStat":
                    case "invisible":
                    case "combatOrders":
                    case "notRemoved":
                    case "origin":
                    case "ascent":
                    case "timeLimited":
                    case "isPetAutoBuff":
                    case "isSequenceOn":
                    case "disableNextLevelInfo":
                        model.Flags[name] = child.GetValue<int>() != 0;
                        break;
                    case "relationSkill":
                    case "addAttack":
                    case "assistSkillLink":
                        model.SpecialProperties[name] = SummarizeChildValues(child);
                        break;
                    default:
                        string value = NodeDto.FormatValue(child.Value);
                        if (!string.IsNullOrEmpty(value))
                        {
                            model.SpecialProperties[name] = value;
                        }
                        else if (child.Nodes.Count > 0)
                        {
                            model.VisualBranches.Add(name);
                        }
                        break;
                }
            }

            model.MaxLevel = model.ResolveMaxLevel();
            model.StatPropertyCount = model.Common.Count
                + model.PvpCommon.Count
                + model.LevelProperties.Values.Sum(item => item.Count);
            model.PreBigBangSkill = model.LevelProperties.Count > 0
                && (model.Common.Count == 0 || model.Common.ContainsKey("maxLevel"));
            return model;
        }

        public string GetSourceProfile()
        {
            bool hasStats = StatPropertyCount > 0 || MasterLevel > 0 || RequiredLevel.HasValue || RequiredAmount.HasValue || RequiredSkills.Count > 0;
            bool hasVisuals = Icons.Count > 0 || VisualBranches.Count > 0;
            if (hasStats && hasVisuals)
            {
                return "mixed";
            }
            if (hasStats)
            {
                return "scalar";
            }
            if (hasVisuals)
            {
                return "visual-only";
            }
            return "metadata-only";
        }

        public Dictionary<string, string> GetEffectiveProperties(int level)
        {
            if (PreBigBangSkill && level > 0)
            {
                Dictionary<string, string> props;
                if (LevelProperties.TryGetValue(level, out props))
                {
                    return new Dictionary<string, string>(props, StringComparer.OrdinalIgnoreCase);
                }
            }
            return new Dictionary<string, string>(Common, StringComparer.OrdinalIgnoreCase);
        }

        private void ReadCommon(Wz_Node commonNode)
        {
            foreach (Wz_Node prop in commonNode.Nodes)
            {
                if (string.Equals(prop.Text, "attackInfo", StringComparison.OrdinalIgnoreCase))
                {
                    ReadAttackInfo(prop);
                    continue;
                }

                var vector = prop.Value as Wz_Vector;
                if (vector != null)
                {
                    Vectors.Add(new SkillVectorDto { Name = prop.Text, X = vector.X, Y = vector.Y, Path = prop.FullPath });
                    continue;
                }

                string value = NodeDto.FormatValue(prop.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    Common[prop.Text] = value;
                }
            }
        }

        private void ReadLevels(Wz_Node levelNode)
        {
            foreach (Wz_Node child in levelNode.Nodes)
            {
                int level;
                if (!int.TryParse(child.Text, out level))
                {
                    continue;
                }
                LevelProperties[level] = CollectScalarProperties(child);
            }
        }

        private void ReadRequirements(Wz_Node reqNode)
        {
            foreach (Wz_Node child in reqNode.Nodes)
            {
                if (string.Equals(child.Text, "level", StringComparison.OrdinalIgnoreCase))
                {
                    RequiredLevel = child.GetValue<int>();
                }
                else if (string.Equals(child.Text, "reqAmount", StringComparison.OrdinalIgnoreCase))
                {
                    RequiredAmount = child.GetValue<int>();
                }
                else
                {
                    int skillId;
                    if (int.TryParse(child.Text, out skillId))
                    {
                        RequiredSkills[child.Text] = child.GetValue<int>();
                    }
                }
            }
        }

        private void ReadActions(Wz_Node actionNode)
        {
            foreach (Wz_Node child in actionNode.Nodes.OrderBy(item => ParseIntOrMax(item.Text)))
            {
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    Actions.Add(value);
                }
            }
        }

        private void ReadAttackInfo(Wz_Node attackInfoNode)
        {
            foreach (Wz_Node jobNode in attackInfoNode.Nodes)
            {
                var props = new List<SkillExtraPropertyDto>();
                foreach (Wz_Node prop in jobNode.Nodes)
                {
                    props.Add(new SkillExtraPropertyDto
                    {
                        Name = prop.Text,
                        Value = NodeDto.FormatValue(prop.Value)
                    });
                }
                AttackInfo[jobNode.Text] = props;
            }
        }

        private int ResolveMaxLevel()
        {
            string maxLevel;
            if (Common.TryGetValue("maxLevel", out maxLevel))
            {
                int parsed;
                if (int.TryParse(maxLevel, out parsed))
                {
                    return parsed;
                }
            }
            return LevelProperties.Count > 0 ? LevelProperties.Keys.Max() : 0;
        }

        private static Dictionary<string, string> CollectScalarProperties(Wz_Node node)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Wz_Node child in node.Nodes)
            {
                if (child.Value is Wz_Vector)
                {
                    continue;
                }
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    props[child.Text] = value;
                }
            }
            return props;
        }

        private static string SummarizeChildValues(Wz_Node node)
        {
            var parts = new List<string>();
            foreach (Wz_Node child in node.Nodes)
            {
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(child.Text + "=" + value);
                }
            }
            return string.Join(" ", parts);
        }

        private static int ParseIntOrMax(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : int.MaxValue;
        }
    }

    internal sealed class SkillStringInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string PassiveDescription { get; set; }
        public Dictionary<string, string> Values { get; set; }
        public List<string> SkillH { get; private set; }
        public List<string> PassiveH { get; private set; }
        public List<string> HyperChangedH { get; private set; }
        public SortedDictionary<int, string> ExtraH { get; private set; }

        private SkillStringInfo()
        {
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SkillH = new List<string>();
            PassiveH = new List<string>();
            HyperChangedH = new List<string>();
            ExtraH = new SortedDictionary<int, string>();
        }

        public static SkillStringInfo FromDomainStringInfo(DomainStringInfo info)
        {
            var result = new SkillStringInfo();
            if (info == null)
            {
                return result;
            }

            result.Values = new Dictionary<string, string>(info.Values ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            result.Name = Get(result.Values, "name") ?? info.Name;
            result.Description = Get(result.Values, "desc") ?? info.Description;
            result.PassiveDescription = Get(result.Values, "pdesc");

            string h = Get(result.Values, "h");
            if (!string.IsNullOrEmpty(h))
            {
                result.SkillH.Add(h);
            }
            else
            {
                for (int i = 1; ; i++)
                {
                    h = Get(result.Values, "h" + i);
                    if (string.IsNullOrEmpty(h))
                    {
                        break;
                    }
                    result.SkillH.Add(h);
                }
            }

            AddIfNotNull(result.PassiveH, Get(result.Values, "ph"));
            AddIfNotNull(result.HyperChangedH, Get(result.Values, "hch"));
            foreach (var item in result.Values)
            {
                if (item.Key.StartsWith("h_", StringComparison.OrdinalIgnoreCase))
                {
                    int level;
                    if (int.TryParse(item.Key.Substring(2), out level) && level > 0)
                    {
                        result.ExtraH[level] = item.Value;
                    }
                }
            }

            return result;
        }

        public string SelectSummary(bool preBigBangSkill, int level)
        {
            if (preBigBangSkill)
            {
                if (SkillH.Count >= level && level > 0)
                {
                    return SkillH[level - 1];
                }
                if (SkillH.Count == 1)
                {
                    return SkillH[0];
                }
                return null;
            }

            string h = SkillH.Count > 0 ? SkillH[0] : null;
            foreach (var item in ExtraH)
            {
                if (level < item.Key)
                {
                    break;
                }
                h = item.Value;
            }
            return h;
        }

        public List<SkillSummaryVariantDto> BuildVariants()
        {
            var variants = new List<SkillSummaryVariantDto>();
            for (int i = 0; i < SkillH.Count; i++)
            {
                variants.Add(new SkillSummaryVariantDto { Kind = "h", Level = SkillH.Count == 1 ? (int?)null : i + 1, Text = SkillH[i] });
            }
            foreach (string text in PassiveH)
            {
                variants.Add(new SkillSummaryVariantDto { Kind = "ph", Text = text });
            }
            foreach (string text in HyperChangedH)
            {
                variants.Add(new SkillSummaryVariantDto { Kind = "hch", Text = text });
            }
            foreach (var item in ExtraH)
            {
                variants.Add(new SkillSummaryVariantDto { Kind = "h_", Level = item.Key, Text = item.Value });
            }
            return variants;
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            return values != null && values.TryGetValue(key, out value) ? value : null;
        }

        private static void AddIfNotNull(List<string> values, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }
    }

    internal static class CliSkillSummaryResolver
    {
        private static readonly Dictionary<string, string> GlobalVariableMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "comboConAran", "aranComboCon" }
        };

        public static string Resolve(string template, int level, Dictionary<string, string> properties, List<string> diagnostics)
        {
            return Resolve(template, level, properties, diagnostics, null);
        }

        public static string Resolve(string template, int level, Dictionary<string, string> properties, List<string> diagnostics, ISet<string> unresolvedPlaceholders)
        {
            if (template == null)
            {
                return null;
            }

            var output = new StringBuilder();
            int index = 0;
            while (index < template.Length)
            {
                if (template[index] == '#')
                {
                    int length = ReadPlaceholderLength(template, index + 1);
                    if (index + 1 < template.Length && template[index + 1] == 'c')
                    {
                        index += 2;
                        continue;
                    }
                    if (length > 0)
                    {
                        string key = template.Substring(index + 1, length);
                        string value;
                        if (TryGetValue(properties, key, out value)
                            || TryGetMappedValue(properties, key, out value))
                        {
                            output.Append(EvaluateValue(key, value, level, diagnostics));
                            index += length + 1;
                            continue;
                        }

                        diagnostics.Add("Unresolved summary placeholder: #" + key);
                        if (unresolvedPlaceholders != null)
                        {
                            unresolvedPlaceholders.Add("#" + key);
                        }
                        output.Append("#").Append(key);
                        index += length + 1;
                        continue;
                    }

                    index++;
                    continue;
                }

                if (template[index] == '\\' && index + 1 < template.Length)
                {
                    switch (template[index + 1])
                    {
                        case 'r':
                            output.Append('\r');
                            break;
                        case 'n':
                            output.Append('\n');
                            break;
                        case '\\':
                            output.Append('\\');
                            break;
                        default:
                            output.Append(template[index + 1]);
                            break;
                    }
                    index += 2;
                    continue;
                }

                output.Append(template[index]);
                index++;
            }

            return output.ToString().Replace("\t", string.Empty).TrimEnd('\r', '\n');
        }

        public static void CollectPlaceholders(string template, ISet<string> placeholders)
        {
            if (string.IsNullOrEmpty(template) || placeholders == null)
            {
                return;
            }

            int index = 0;
            while (index < template.Length)
            {
                if (template[index] == '#')
                {
                    int length = ReadPlaceholderLength(template, index + 1);
                    if (index + 1 < template.Length && template[index + 1] == 'c')
                    {
                        index += 2;
                        continue;
                    }
                    if (length > 0)
                    {
                        placeholders.Add("#" + template.Substring(index + 1, length));
                        index += length + 1;
                        continue;
                    }
                }
                index++;
            }
        }

        private static string EvaluateValue(string key, string value, int level, List<string> diagnostics)
        {
            try
            {
                decimal parsed = WzComparerR2.Calculator.Parse(value.ToLowerInvariant(), level);
                if (string.Equals(key, "cooltimeMS", StringComparison.Ordinal))
                {
                    return (parsed / 1000).ToString("0.##");
                }
                if (key.EndsWith("PerM", StringComparison.Ordinal))
                {
                    return (parsed / 100).ToString("0.#");
                }
                return parsed.ToString();
            }
            catch (Exception ex)
            {
                diagnostics.Add("Failed to evaluate #" + key + "='" + value + "': " + ex.Message);
                return value;
            }
        }

        private static bool TryGetMappedValue(Dictionary<string, string> properties, string key, out string value)
        {
            string mapped;
            if (GlobalVariableMapping.TryGetValue(key, out mapped) && !string.IsNullOrEmpty(mapped))
            {
                return TryGetValue(properties, mapped, out value);
            }
            value = null;
            return false;
        }

        private static bool TryGetValue(Dictionary<string, string> properties, string key, out string value)
        {
            if (properties != null)
            {
                foreach (var item in properties)
                {
                    if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = item.Value;
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        private static int ReadPlaceholderLength(string text, int start)
        {
            int length = 0;
            while (start + length < text.Length)
            {
                char ch = text[start + length];
                if (ch == '_'
                    || (ch >= 'a' && ch <= 'z')
                    || (ch >= 'A' && ch <= 'Z')
                    || (length > 0 && ch >= '0' && ch <= '9'))
                {
                    length++;
                    continue;
                }
                break;
            }
            return length;
        }
    }

    internal sealed class SkillFullXmlWriter
    {
        public static string ToXml(SkillFullDto dto)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            };
            using (var stringWriter = new StringWriter())
            {
                using (var writer = XmlWriter.Create(stringWriter, settings))
                {
                    writer.WriteStartElement("skill");
                    writer.WriteAttributeString("id", dto.Id);
                    writer.WriteAttributeString("mode", dto.Mode);
                    writer.WriteAttributeString("foundData", dto.FoundData.ToString().ToLowerInvariant());
                    WriteElement(writer, "sourceProfile", dto.SourceProfile);
                    WriteElement(writer, "dataInputPath", dto.DataInputPath);
                    WriteElement(writer, "stringInputPath", dto.StringInputPath);
                    WriteElement(writer, "name", dto.Name);
                    WriteElement(writer, "description", dto.Description);
                    WriteElement(writer, "passiveDescription", dto.PassiveDescription);
                    if (dto.LinkerStatus != null)
                    {
                        writer.WriteStartElement("linkerStatus");
                        writer.WriteAttributeString("resolver", dto.LinkerStatus.Resolver);
                        writer.WriteAttributeString("status", dto.LinkerStatus.Status);
                        writer.WriteAttributeString("foundData", dto.LinkerStatus.FoundData.ToString().ToLowerInvariant());
                        writer.WriteAttributeString("foundString", dto.LinkerStatus.FoundString.ToString().ToLowerInvariant());
                        writer.WriteAttributeString("foundStats", dto.LinkerStatus.FoundStats.ToString().ToLowerInvariant());
                        writer.WriteAttributeString("foundVisuals", dto.LinkerStatus.FoundVisuals.ToString().ToLowerInvariant());
                        writer.WriteAttributeString("foundSummaryTemplate", dto.LinkerStatus.FoundSummaryTemplate.ToString().ToLowerInvariant());
                        writer.WriteAttributeString("summaryFullyResolved", dto.LinkerStatus.SummaryFullyResolved.ToString().ToLowerInvariant());
                        writer.WriteAttributeString("unresolvedPlaceholderCount", dto.LinkerStatus.UnresolvedPlaceholderCount.ToString());
                        writer.WriteAttributeString("guiStringLinkerLoaded", dto.LinkerStatus.GuiStringLinkerLoaded.ToString().ToLowerInvariant());
                        if (dto.LinkerStatus.Notes != null)
                        {
                            foreach (string note in dto.LinkerStatus.Notes)
                            {
                                WriteElement(writer, "note", note);
                            }
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteStartElement("summary");
                    if (dto.Level.HasValue)
                    {
                        writer.WriteAttributeString("level", dto.Level.Value.ToString());
                    }
                    WriteElement(writer, "raw", dto.RawSummary);
                    WriteElement(writer, "resolved", dto.ResolvedSummary);
                    writer.WriteEndElement();
                    if (dto.NextLevel.HasValue)
                    {
                        writer.WriteStartElement("nextSummary");
                        writer.WriteAttributeString("level", dto.NextLevel.Value.ToString());
                        WriteElement(writer, "raw", dto.NextRawSummary);
                        WriteElement(writer, "resolved", dto.NextResolvedSummary);
                        writer.WriteEndElement();
                    }
                    WriteDictionary(writer, "common", "property", dto.Common);
                    WriteDictionary(writer, "effectiveProperties", "property", dto.EffectiveProperties);
                    WriteDictionary(writer, "pvpCommon", "property", dto.PvpCommon);
                    WriteDictionary(writer, "strings", "property", dto.StringProperties);
                    WriteDictionary(writer, "flags", "flag", dto.Flags.ToDictionary(item => item.Key, item => item.Value.ToString().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase));
                    WriteDictionary(writer, "specialProperties", "property", dto.SpecialProperties);
                    WriteDictionary(writer, "requiredSkills", "skill", dto.RequiredSkills.ToDictionary(item => item.Key, item => item.Value.ToString(), StringComparer.OrdinalIgnoreCase));
                    writer.WriteStartElement("levels");
                    foreach (var level in dto.LevelProperties)
                    {
                        writer.WriteStartElement("level");
                        writer.WriteAttributeString("value", level.Level.ToString());
                        WriteDictionaryItems(writer, "property", level.Properties);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("actions");
                    foreach (string action in dto.Actions)
                    {
                        WriteElement(writer, "action", action);
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("visualBranches");
                    if (dto.VisualBranches != null)
                    {
                        foreach (string branch in dto.VisualBranches)
                        {
                            WriteElement(writer, "branch", branch);
                        }
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("icons");
                    foreach (var icon in dto.Icons)
                    {
                        writer.WriteStartElement("icon");
                        writer.WriteAttributeString("name", icon.Name);
                        writer.WriteAttributeString("path", icon.Path);
                        writer.WriteAttributeString("type", icon.Type);
                        if (icon.Width.HasValue) writer.WriteAttributeString("width", icon.Width.Value.ToString());
                        if (icon.Height.HasValue) writer.WriteAttributeString("height", icon.Height.Value.ToString());
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("unresolvedPlaceholders");
                    if (dto.UnresolvedPlaceholders != null)
                    {
                        foreach (string placeholder in dto.UnresolvedPlaceholders)
                        {
                            WriteElement(writer, "placeholder", placeholder);
                        }
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("diagnostics");
                    foreach (string diagnostic in dto.Diagnostics)
                    {
                        WriteElement(writer, "diagnostic", diagnostic);
                    }
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                return stringWriter.ToString();
            }
        }

        private static void WriteDictionary(XmlWriter writer, string rootName, string itemName, Dictionary<string, string> values)
        {
            writer.WriteStartElement(rootName);
            WriteDictionaryItems(writer, itemName, values);
            writer.WriteEndElement();
        }

        private static void WriteDictionaryItems(XmlWriter writer, string itemName, Dictionary<string, string> values)
        {
            foreach (var item in values ?? new Dictionary<string, string>())
            {
                writer.WriteStartElement(itemName);
                writer.WriteAttributeString("name", item.Key);
                writer.WriteAttributeString("value", item.Value);
                writer.WriteEndElement();
            }
        }

        private static void WriteElement(XmlWriter writer, string name, string value)
        {
            if (value == null)
            {
                return;
            }
            writer.WriteStartElement(name);
            writer.WriteString(value);
            writer.WriteEndElement();
        }
    }

    internal sealed class SkillLevelPropertiesDto
    {
        public int Level { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }

    internal sealed class SkillIconDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Format { get; set; }
        public int? Pages { get; set; }

        public static SkillIconDto FromNode(string name, Wz_Node node)
        {
            Wz_Node extracted = NodePath.ExtractImageNode(node, true) ?? node;
            var png = extracted.Value as Wz_Png;
            return new SkillIconDto
            {
                Name = name,
                Path = extracted.FullPath,
                Type = NodeDto.GetTypeName(extracted.Value),
                Width = png == null ? null : (int?)png.Width,
                Height = png == null ? null : (int?)png.Height,
                Format = png == null ? null : png.Format.ToString(),
                Pages = png == null ? null : (int?)png.ActualPages
            };
        }
    }

    internal sealed class SkillVectorDto
    {
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Path { get; set; }
    }

    internal sealed class SkillExtraPropertyDto
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    internal sealed class SkillSummaryVariantDto
    {
        public string Kind { get; set; }
        public int? Level { get; set; }
        public string Text { get; set; }
    }
}
