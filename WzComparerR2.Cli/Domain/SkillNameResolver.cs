using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WzComparerR2.Cli
{
    internal sealed class SkillNameSearchOptions
    {
        public string Name { get; set; }
        public string JobCode { get; set; }
        public int MaxResults { get; set; }
        public bool RequireData { get; set; }

        public static SkillNameSearchOptions FromArgs(ParsedArgs args, bool requireData)
        {
            string name = args.GetValue("name") ?? args.GetValue("query");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new UsageException("skill name lookup requires --name <text>.");
            }

            int maxResults = args.GetInt("max-results", 25);
            if (maxResults <= 0)
            {
                throw new UsageException("skill name lookup --max-results must be a positive integer.");
            }

            return new SkillNameSearchOptions
            {
                Name = name.Trim(),
                JobCode = NormalizeJobCode(args.GetValue("job-code") ?? args.GetValue("job")),
                MaxResults = maxResults,
                RequireData = requireData
            };
        }

        internal static string NormalizeJobCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();
            long numeric;
            return long.TryParse(trimmed, out numeric) && numeric >= 0
                ? numeric.ToString()
                : trimmed;
        }
    }

    internal static class SkillNameResolver
    {
        public static SkillNameResolveSession OpenSession(CliWzRepository repository)
        {
            return new SkillNameResolveSession(repository);
        }

        public static SkillNameSearchResultDto Search(CliWzRepository repository, SkillNameSearchOptions options)
        {
            using (var session = OpenSession(repository))
            {
                return session.Search(options);
            }
        }

        public static SkillNameSearchResultDto Resolve(CliWzRepository repository, SkillNameSearchOptions options)
        {
            using (var session = OpenSession(repository))
            {
                return session.Resolve(options);
            }
        }

        internal static SkillNameSearchResultDto Search(
            CliWzRepository repository,
            SkillNameSearchOptions options,
            IReadOnlyList<CliWzStringEntryResult> stringEntries,
            Dictionary<string, SkillNameDataProfileDto> dataProfileCache)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.Name))
            {
                throw new UsageException("skill name lookup requires --name <text>.");
            }

            var result = new SkillNameSearchResultDto
            {
                Query = options.Name,
                NormalizedQuery = NormalizeName(options.Name),
                LooseQuery = NormalizeLoose(options.Name),
                JobCode = SkillNameSearchOptions.NormalizeJobCode(options.JobCode),
                Status = "searched",
                Diagnostics = new List<string>(),
                Candidates = new List<SkillNameCandidateDto>()
            };

            var candidates = BuildBaseCandidates(stringEntries, result);
            result.StringCandidateCount = candidates.Count;
            if (candidates.Count == 0)
            {
                result.Status = "not-found";
                result.Diagnostics.Add("No String/Skill.img entries matched the requested name.");
                return result;
            }

            var ranked = candidates
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(options.MaxResults * 4, 50))
                .ToList();

            foreach (var candidate in ranked)
            {
                EnrichWithData(repository, candidate, result.Diagnostics, dataProfileCache);
            }

            result.Candidates = ranked
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.FoundData)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Take(options.MaxResults)
                .ToList();
            result.ReturnedCount = result.Candidates.Count;
            ApplyResolution(result, options.RequireData);
            return result;
        }

        internal static SkillNameSearchResultDto Resolve(
            CliWzRepository repository,
            SkillNameSearchOptions options,
            IReadOnlyList<CliWzStringEntryResult> stringEntries,
            Dictionary<string, SkillNameDataProfileDto> dataProfileCache)
        {
            var result = Search(repository, options, stringEntries, dataProfileCache);
            if (result.Status == "searched")
            {
                ApplyResolution(result, options.RequireData);
            }
            return result;
        }

        private static List<SkillNameCandidateDto> BuildBaseCandidates(IReadOnlyList<CliWzStringEntryResult> stringEntries, SkillNameSearchResultDto result)
        {
            var dedupe = new Dictionary<string, SkillNameCandidateDto>(StringComparer.OrdinalIgnoreCase);
            foreach (CliWzStringEntryResult entry in stringEntries ?? Array.Empty<CliWzStringEntryResult>())
            {
                var skillString = SkillStringInfo.FromDomainStringInfo(entry.StringInfo);
                string displayName = skillString.Name ?? entry.StringInfo.Name;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                string matchType;
                int score = ScoreName(result.Query, result.NormalizedQuery, result.LooseQuery, displayName, out matchType);
                if (score <= 0)
                {
                    continue;
                }

                string inferredJobCode = InferJobCode(entry.Id);
                bool jobMatches = string.IsNullOrEmpty(result.JobCode) || JobCodeMatches(entry.Id, inferredJobCode, result.JobCode);
                if (!string.IsNullOrEmpty(result.JobCode))
                {
                    score += jobMatches ? 250 : -200;
                }

                string key = entry.Id + "|" + entry.InputPath + "|" + entry.Path;
                dedupe[key] = new SkillNameCandidateDto
                {
                    Id = entry.Id,
                    JobCode = inferredJobCode,
                    JobCodeMatched = jobMatches,
                    Name = displayName,
                    Description = skillString.Description,
                    StringPath = entry.Path,
                    StringInputPath = entry.InputPath,
                    MatchType = matchType,
                    Score = score,
                    VisualBranches = new List<string>()
                };
            }

            return dedupe.Values
                .Where(item => string.IsNullOrEmpty(result.JobCode) || item.Score > 0)
                .ToList();
        }

        private static void EnrichWithData(
            CliWzRepository repository,
            SkillNameCandidateDto candidate,
            List<string> diagnostics,
            Dictionary<string, SkillNameDataProfileDto> dataProfileCache)
        {
            try
            {
                SkillNameDataProfileDto profile;
                if (dataProfileCache == null || !dataProfileCache.TryGetValue(candidate.Id, out profile))
                {
                    profile = BuildDataProfile(repository, candidate.Id);
                    if (dataProfileCache != null)
                    {
                        dataProfileCache[candidate.Id] = profile;
                    }
                }

                ApplyDataProfile(candidate, profile);
                if (!candidate.FoundData)
                {
                    return;
                }

                candidate.Score += 80;
                if (candidate.StatPropertyCount > 0)
                {
                    candidate.Score += 20;
                }
                if (candidate.HasVisuals)
                {
                    candidate.Score += 20;
                }
            }
            catch (Exception ex) when (ex is UsageException || ex is WzLoadException || ex is System.IO.FileNotFoundException || ex is System.IO.DirectoryNotFoundException)
            {
                if (diagnostics != null)
                {
                    diagnostics.Add("Failed to inspect data node for skill " + candidate.Id + ": " + ex.Message);
                }
            }
        }

        private static SkillNameDataProfileDto BuildDataProfile(CliWzRepository repository, string id)
        {
            var profile = new SkillNameDataProfileDto
            {
                VisualBranches = new List<string>()
            };
            var dataResult = repository.FindDataNode("skill", id);
            if (dataResult == null)
            {
                return profile;
            }

            var model = HeadlessSkillModel.FromNode(dataResult.Node);
            profile.FoundData = true;
            profile.DataPath = dataResult.Node.FullPath;
            profile.DataInputPath = dataResult.InputPath;
            profile.SourceProfile = model.GetSourceProfile();
            profile.StatPropertyCount = model.StatPropertyCount;
            profile.VisualBranchCount = model.VisualBranches.Count;
            profile.VisualBranches = model.VisualBranches.ToList();
            profile.HasIcon = model.Icons.Count > 0;
            profile.HasVisuals = model.Icons.Count > 0 || model.VisualBranches.Count > 0;
            return profile;
        }

        private static void ApplyDataProfile(SkillNameCandidateDto candidate, SkillNameDataProfileDto profile)
        {
            if (candidate == null || profile == null)
            {
                return;
            }

            candidate.FoundData = profile.FoundData;
            candidate.DataPath = profile.DataPath;
            candidate.DataInputPath = profile.DataInputPath;
            candidate.SourceProfile = profile.SourceProfile;
            candidate.StatPropertyCount = profile.StatPropertyCount;
            candidate.VisualBranchCount = profile.VisualBranchCount;
            candidate.VisualBranches = profile.VisualBranches == null ? new List<string>() : profile.VisualBranches.ToList();
            candidate.HasIcon = profile.HasIcon;
            candidate.HasVisuals = profile.HasVisuals;
        }

        private static void ApplyResolution(SkillNameSearchResultDto result, bool requireData)
        {
            if (result.Candidates == null || result.Candidates.Count == 0)
            {
                result.Status = "not-found";
                return;
            }

            var viable = result.Candidates
                .Where(item => (!requireData || item.FoundData)
                    && (string.IsNullOrEmpty(result.JobCode) || item.JobCodeMatched))
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (viable.Count == 0)
            {
                result.Status = requireData ? "no-data-match" : "not-found";
                result.Diagnostics.Add(requireData
                    ? "Matching string entries were found, but none had a usable skill data node for export."
                    : "No candidate satisfied the requested filters.");
                return;
            }

            SkillNameCandidateDto top = viable[0];
            SkillNameCandidateDto second = viable.Count > 1 ? viable[1] : null;
            bool topIsStrong = top.MatchType == "exact"
                || top.MatchType == "normalized-exact"
                || (top.MatchType == "fuzzy" && (second == null || top.Score - second.Score >= 120));

            if (topIsStrong && second == null)
            {
                MarkResolved(result, top);
                return;
            }

            if (topIsStrong && second != null)
            {
                bool blockedByUsablePeer = viable.Skip(1).Any(candidate => BlocksResolution(top, candidate));
                if (!blockedByUsablePeer)
                {
                    MarkResolved(result, top);
                    return;
                }
            }

            result.Status = "ambiguous";
            result.Diagnostics.Add("Multiple matching skill IDs remain; pass --job-code or use the id explicitly.");
        }

        private static bool BlocksResolution(SkillNameCandidateDto top, SkillNameCandidateDto candidate)
        {
            bool topUsable = HasUsableData(top);
            bool candidateUsable = HasUsableData(candidate);
            if (topUsable && !candidateUsable)
            {
                return false;
            }

            bool sameNormalizedName = string.Equals(NormalizeLoose(top.Name), NormalizeLoose(candidate.Name), StringComparison.OrdinalIgnoreCase);
            bool closeScore = top.Score - candidate.Score < 120;
            return sameNormalizedName || closeScore;
        }

        private static bool HasUsableData(SkillNameCandidateDto candidate)
        {
            return candidate != null
                && candidate.FoundData
                && (candidate.HasVisuals || candidate.StatPropertyCount > 0);
        }

        private static void MarkResolved(SkillNameSearchResultDto result, SkillNameCandidateDto top)
        {
            result.Status = "resolved";
            result.ResolvedId = top.Id;
            result.ResolvedName = top.Name;
            result.ResolvedJobCode = top.JobCode;
        }

        private static int ScoreName(string query, string normalizedQuery, string looseQuery, string candidateName, out string matchType)
        {
            string normalizedCandidate = NormalizeName(candidateName);
            string looseCandidate = NormalizeLoose(candidateName);
            if (string.Equals(normalizedQuery, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                matchType = "exact";
                return 1000;
            }
            if (string.Equals(looseQuery, looseCandidate, StringComparison.OrdinalIgnoreCase))
            {
                matchType = "normalized-exact";
                return 950;
            }
            if (normalizedCandidate.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || looseCandidate.StartsWith(looseQuery, StringComparison.OrdinalIgnoreCase))
            {
                matchType = "prefix";
                return 720;
            }
            if (normalizedCandidate.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                || looseCandidate.IndexOf(looseQuery, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matchType = "contains";
                return 620;
            }

            int distance = ComputeFuzzyDistance(looseQuery, looseCandidate);
            if (distance >= 0)
            {
                matchType = "fuzzy";
                return 520 - (distance * 50);
            }

            matchType = null;
            return 0;
        }

        private static int ComputeFuzzyDistance(string query, string candidate)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(candidate))
            {
                return -1;
            }

            int maxDistance = Math.Max(1, Math.Min(3, query.Length / 4));
            if (Math.Abs(query.Length - candidate.Length) > maxDistance)
            {
                return -1;
            }

            int distance = LevenshteinDistance(query, candidate, maxDistance);
            return distance <= maxDistance ? distance : -1;
        }

        private static int LevenshteinDistance(string left, string right, int maxDistance)
        {
            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++)
            {
                previous[j] = j;
            }

            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                int rowMin = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                    int insertion = current[j - 1] + 1;
                    int deletion = previous[j] + 1;
                    int value = Math.Min(substitution, Math.Min(insertion, deletion));
                    current[j] = value;
                    rowMin = Math.Min(rowMin, value);
                }

                if (rowMin > maxDistance)
                {
                    return maxDistance + 1;
                }

                int[] temp = previous;
                previous = current;
                current = temp;
            }

            return previous[right.Length];
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Normalize(NormalizationForm.FormKC).Trim();
            var builder = new StringBuilder(normalized.Length);
            bool previousWasSpace = false;
            foreach (char ch in normalized)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasSpace)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                    continue;
                }

                builder.Append(char.ToLowerInvariant(ch));
                previousWasSpace = false;
            }
            return builder.ToString().Trim();
        }

        private static string NormalizeLoose(string value)
        {
            string normalized = NormalizeName(value);
            var builder = new StringBuilder(normalized.Length);
            foreach (char ch in normalized)
            {
                if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
                {
                    continue;
                }
                builder.Append(ch);
            }
            return builder.ToString();
        }

        private static bool JobCodeMatches(string id, string inferredJobCode, string requestedJobCode)
        {
            if (string.IsNullOrEmpty(requestedJobCode))
            {
                return true;
            }
            return string.Equals(inferredJobCode, requestedJobCode, StringComparison.OrdinalIgnoreCase);
        }

        private static string InferJobCode(string id)
        {
            long numericId;
            if (!long.TryParse(id, out numericId) || numericId < 0)
            {
                return null;
            }
            return (numericId / 10000).ToString();
        }
    }

    internal sealed class SkillNameResolveSession : IDisposable
    {
        private readonly CliWzRepository repository;
        private readonly Dictionary<string, SkillNameDataProfileDto> dataProfileCache;
        private List<CliWzStringEntryResult> stringEntries;
        private bool disposed;

        public SkillNameResolveSession(CliWzRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            this.repository = repository;
            this.dataProfileCache = new Dictionary<string, SkillNameDataProfileDto>(StringComparer.OrdinalIgnoreCase);
        }

        public SkillNameSearchResultDto Search(SkillNameSearchOptions options)
        {
            ThrowIfDisposed();
            return SkillNameResolver.Search(repository, options, GetStringEntries(), dataProfileCache);
        }

        public SkillNameSearchResultDto Resolve(SkillNameSearchOptions options)
        {
            ThrowIfDisposed();
            return SkillNameResolver.Resolve(repository, options, GetStringEntries(), dataProfileCache);
        }

        public void Dispose()
        {
            disposed = true;
        }

        private IReadOnlyList<CliWzStringEntryResult> GetStringEntries()
        {
            if (stringEntries == null)
            {
                stringEntries = repository.EnumerateStringInfos("skill");
            }
            return stringEntries;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SkillNameResolveSession));
            }
        }
    }

    internal sealed class SkillNameDataProfileDto
    {
        public bool FoundData { get; set; }
        public string SourceProfile { get; set; }
        public int StatPropertyCount { get; set; }
        public bool HasIcon { get; set; }
        public bool HasVisuals { get; set; }
        public int VisualBranchCount { get; set; }
        public List<string> VisualBranches { get; set; }
        public string DataPath { get; set; }
        public string DataInputPath { get; set; }
    }

    internal sealed class SkillNameSearchResultDto
    {
        public string Query { get; set; }
        public string NormalizedQuery { get; set; }
        public string LooseQuery { get; set; }
        public string JobCode { get; set; }
        public string Status { get; set; }
        public string ResolvedId { get; set; }
        public string ResolvedName { get; set; }
        public string ResolvedJobCode { get; set; }
        public int StringCandidateCount { get; set; }
        public int ReturnedCount { get; set; }
        public List<string> Diagnostics { get; set; }
        public List<SkillNameCandidateDto> Candidates { get; set; }
    }

    internal sealed class SkillNameCandidateDto
    {
        public string Id { get; set; }
        public string JobCode { get; set; }
        public bool JobCodeMatched { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string MatchType { get; set; }
        public int Score { get; set; }
        public bool FoundData { get; set; }
        public string SourceProfile { get; set; }
        public int StatPropertyCount { get; set; }
        public bool HasIcon { get; set; }
        public bool HasVisuals { get; set; }
        public int VisualBranchCount { get; set; }
        public List<string> VisualBranches { get; set; }
        public string DataPath { get; set; }
        public string DataInputPath { get; set; }
        public string StringPath { get; set; }
        public string StringInputPath { get; set; }
    }
}
