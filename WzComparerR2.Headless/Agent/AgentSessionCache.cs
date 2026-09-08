using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless.Agent
{
    internal sealed class AgentSessionCache : IDisposable
    {
        private const int DefaultMaxSkillSessions = 4;
        private const int DefaultMaxDomainRepositories = 8;
        private const int DefaultMaxWzContexts = 16;
        private readonly Dictionary<string, SkillSessionEntry> skillSessions =
            new Dictionary<string, SkillSessionEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, DomainRepositoryEntry> domainRepositories =
            new Dictionary<string, DomainRepositoryEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, WzContextEntry> wzContexts =
            new Dictionary<string, WzContextEntry>(StringComparer.Ordinal);
        private long skillSessionHits;
        private long skillSessionMisses;
        private long skillSessionEvictions;
        private long domainRepositoryHits;
        private long domainRepositoryMisses;
        private long domainRepositoryEvictions;
        private long wzContextHits;
        private long wzContextMisses;
        private long wzContextEvictions;
        private int nextSkillSessionId;
        private int nextDomainRepositoryId;
        private int nextWzContextId;
        private bool disposed;

        public AgentSkillSessionLease GetSkillSession(
            string skillInput,
            ParsedArgs args,
            SkillSpriteExportOptions options)
        {
            ThrowIfDisposed();

            string key = BuildSkillSessionKey(skillInput, args, options);
            SkillSessionEntry entry;
            if (skillSessions.TryGetValue(key, out entry))
            {
                skillSessionHits++;
                entry.UseCount++;
                entry.LastUsedAt = DateTimeOffset.Now;
                return new AgentSkillSessionLease(entry.Session, "cache-hit", entry.Id);
            }

            skillSessionMisses++;
            if (skillSessions.Count >= DefaultMaxSkillSessions)
            {
                EvictLeastRecentlyUsed();
            }

            var session = SkillSpriteExporter.OpenSession(skillInput, args, options);
            entry = new SkillSessionEntry
            {
                Id = "skill-" + (++nextSkillSessionId),
                Key = key,
                SkillInputPath = NormalizePathValue(skillInput),
                DataDirectory = NormalizeOptionalPath(args.GetValue("data-dir")),
                Session = session,
                UseCount = 1,
                CreatedAt = DateTimeOffset.Now,
                LastUsedAt = DateTimeOffset.Now
            };
            skillSessions.Add(key, entry);
            return new AgentSkillSessionLease(entry.Session, "cache-miss", entry.Id);
        }

        public AgentDomainRepositoryLease GetDomainRepository(
            string kind,
            string input,
            ParsedArgs args)
        {
            ThrowIfDisposed();

            string key = BuildDomainRepositoryKey(kind, input, args);
            DomainRepositoryEntry entry;
            if (domainRepositories.TryGetValue(key, out entry))
            {
                domainRepositoryHits++;
                entry.UseCount++;
                entry.LastUsedAt = DateTimeOffset.Now;
                return new AgentDomainRepositoryLease(entry.Repository, "cache-hit", entry.Id);
            }

            domainRepositoryMisses++;
            if (domainRepositories.Count >= DefaultMaxDomainRepositories)
            {
                EvictLeastRecentlyUsedDomainRepository();
            }

            var repository = CliWzRepository.ForDomain(kind, input, args);
            entry = new DomainRepositoryEntry
            {
                Id = "domain-" + (++nextDomainRepositoryId),
                Key = key,
                Kind = kind,
                InputPath = NormalizePathValue(input),
                DataDirectory = NormalizeOptionalPath(args.GetValue("data-dir")),
                Repository = repository,
                UseCount = 1,
                CreatedAt = DateTimeOffset.Now,
                LastUsedAt = DateTimeOffset.Now
            };
            domainRepositories.Add(key, entry);
            return new AgentDomainRepositoryLease(entry.Repository, "cache-miss", entry.Id);
        }

        public AgentWzContextLease GetWzContext(string input, WzLoadOptions options)
        {
            ThrowIfDisposed();

            string key = BuildWzContextKey(input, options);
            WzContextEntry entry;
            if (wzContexts.TryGetValue(key, out entry))
            {
                wzContextHits++;
                entry.UseCount++;
                entry.LastUsedAt = DateTimeOffset.Now;
                return new AgentWzContextLease(entry.Context, "cache-hit", entry.Id);
            }

            wzContextMisses++;
            if (wzContexts.Count >= DefaultMaxWzContexts)
            {
                EvictLeastRecentlyUsedWzContext();
            }

            WzLoadContext context = WzLoadContext.Load(input, options);
            entry = new WzContextEntry
            {
                Id = "wz-" + (++nextWzContextId),
                Key = key,
                InputPath = context.InputPath,
                Context = context,
                UseCount = 1,
                CreatedAt = DateTimeOffset.Now,
                LastUsedAt = DateTimeOffset.Now
            };
            wzContexts.Add(key, entry);
            return new AgentWzContextLease(entry.Context, "cache-miss", entry.Id);
        }

        public AgentSessionCacheStatsDto GetStats()
        {
            return new AgentSessionCacheStatsDto
            {
                SkillSessionCount = skillSessions.Count,
                MaxSkillSessions = DefaultMaxSkillSessions,
                SkillSessionHits = skillSessionHits,
                SkillSessionMisses = skillSessionMisses,
                SkillSessionEvictions = skillSessionEvictions,
                SkillSessions = skillSessions.Values
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new AgentSkillSessionCacheEntryDto
                    {
                        Id = item.Id,
                        SkillInputPath = item.SkillInputPath,
                        DataDirectory = item.DataDirectory,
                        UseCount = item.UseCount,
                        CreatedAt = item.CreatedAt.ToString("o"),
                        LastUsedAt = item.LastUsedAt.ToString("o")
                    })
                    .ToList(),
                DomainRepositoryCount = domainRepositories.Count,
                MaxDomainRepositories = DefaultMaxDomainRepositories,
                DomainRepositoryHits = domainRepositoryHits,
                DomainRepositoryMisses = domainRepositoryMisses,
                DomainRepositoryEvictions = domainRepositoryEvictions,
                DomainRepositories = domainRepositories.Values
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new AgentDomainRepositoryCacheEntryDto
                    {
                        Id = item.Id,
                        Kind = item.Kind,
                        InputPath = item.InputPath,
                        DataDirectory = item.DataDirectory,
                        UseCount = item.UseCount,
                        CreatedAt = item.CreatedAt.ToString("o"),
                        LastUsedAt = item.LastUsedAt.ToString("o")
                    })
                    .ToList(),
                WzContextCount = wzContexts.Count,
                MaxWzContexts = DefaultMaxWzContexts,
                WzContextHits = wzContextHits,
                WzContextMisses = wzContextMisses,
                WzContextEvictions = wzContextEvictions,
                WzContexts = wzContexts.Values
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new AgentWzContextCacheEntryDto
                    {
                        Id = item.Id,
                        InputPath = item.InputPath,
                        UseCount = item.UseCount,
                        CreatedAt = item.CreatedAt.ToString("o"),
                        LastUsedAt = item.LastUsedAt.ToString("o")
                    })
                    .ToList()
            };
        }

        public void Clear()
        {
            foreach (SkillSessionEntry entry in skillSessions.Values)
            {
                entry.Session.Dispose();
            }
            skillSessions.Clear();

            foreach (DomainRepositoryEntry entry in domainRepositories.Values)
            {
                entry.Repository.Dispose();
            }
            domainRepositories.Clear();

            foreach (WzContextEntry entry in wzContexts.Values)
            {
                entry.Context.Dispose();
            }
            wzContexts.Clear();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Clear();
            disposed = true;
        }

        private void EvictLeastRecentlyUsed()
        {
            SkillSessionEntry oldest = skillSessions.Values
                .OrderBy(item => item.LastUsedAt)
                .FirstOrDefault();
            if (oldest == null)
            {
                return;
            }

            skillSessions.Remove(oldest.Key);
            oldest.Session.Dispose();
            skillSessionEvictions++;
        }

        private void EvictLeastRecentlyUsedDomainRepository()
        {
            DomainRepositoryEntry oldest = domainRepositories.Values
                .OrderBy(item => item.LastUsedAt)
                .FirstOrDefault();
            if (oldest == null)
            {
                return;
            }

            domainRepositories.Remove(oldest.Key);
            oldest.Repository.Dispose();
            domainRepositoryEvictions++;
        }

        private void EvictLeastRecentlyUsedWzContext()
        {
            WzContextEntry oldest = wzContexts.Values
                .OrderBy(item => item.LastUsedAt)
                .FirstOrDefault();
            if (oldest == null)
            {
                return;
            }

            wzContexts.Remove(oldest.Key);
            oldest.Context.Dispose();
            wzContextEvictions++;
        }

        private static string BuildSkillSessionKey(string skillInput, ParsedArgs args, SkillSpriteExportOptions options)
        {
            var parts = new List<string>();
            AddPart(parts, "skillInput", NormalizePathValue(skillInput));
            AddArgPath(parts, args, "data-dir");
            AddArgPath(parts, args, "skill-wz");
            AddArgPath(parts, args, "string-wz");
            AddArgPath(parts, args, "fallback");
            AddArgValue(parts, args, "video-format");
            AddArgValue(parts, args, "format");
            AddArgFlag(parts, args, "use-base-wz");

            AddPart(parts, "branches", JoinValues(options.Branches));
            AddPart(parts, "canvas", JoinValues(options.CanvasInputs.Select(NormalizePathValue)));
            AddPart(parts, "sound", JoinValues(options.SoundInputs.Select(NormalizePathValue)));
            AddPart(parts, "related", JoinValues(options.RelatedInputs.Select(NormalizePathValue)));
            AddPart(parts, "relatedKeys", JoinValues(options.RelatedKeys));
            AddPart(parts, "maxCanvas", options.MaxCanvasInputs.ToString());
            AddPart(parts, "maxSound", options.MaxSoundInputs.ToString());
            AddPart(parts, "maxRelatedInputs", options.MaxRelatedInputs.ToString());
            AddPart(parts, "maxRelatedMatches", options.MaxRelatedMatches.ToString());
            AddPart(parts, "directOnly", options.DirectOnly ? "1" : "0");
            AddPart(parts, "includeSounds", options.IncludeSounds ? "1" : "0");
            AddPart(parts, "includeVideos", options.IncludeVideos ? "1" : "0");
            AddPart(parts, "includeRelated", options.IncludeRelatedAssets ? "1" : "0");
            AddPart(parts, "autoVisual", options.AutoVisualBranches ? "1" : "0");
            return string.Join("\u001f", parts);
        }

        private static string BuildDomainRepositoryKey(string kind, string input, ParsedArgs args)
        {
            var parts = new List<string>();
            string normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            AddPart(parts, "kind", normalizedKind);
            AddPart(parts, "input", NormalizePathValue(input));
            AddArgPath(parts, args, "data-dir");
            AddArgPath(parts, args, normalizedKind + "-wz");
            AddArgPath(parts, args, "string-wz");
            AddArgPath(parts, args, "item-wz");
            AddArgPath(parts, args, "character-wz");
            AddArgPath(parts, args, "fallback");
            AddArgFlag(parts, args, "use-base-wz");
            return string.Join("\u001f", parts);
        }

        private static string BuildWzContextKey(string input, WzLoadOptions options)
        {
            var parts = new List<string>();
            AddPart(parts, "input", NormalizePathValue(input));
            AddPart(parts, "useBaseWz", options != null && options.UseBaseWz ? "1" : "0");
            AddPart(parts, "fallback", NormalizeOptionalPath(options == null ? null : options.FallbackPath));
            return string.Join("\u001f", parts);
        }

        private static void AddArgPath(List<string> parts, ParsedArgs args, string name)
        {
            AddPart(parts, name, NormalizeOptionalPath(args.GetValue(name)));
        }

        private static void AddArgValue(List<string> parts, ParsedArgs args, string name)
        {
            AddPart(parts, name, args.GetValue(name) ?? string.Empty);
        }

        private static void AddArgFlag(List<string> parts, ParsedArgs args, string name)
        {
            AddPart(parts, name, args.HasFlag(name) ? "1" : "0");
        }

        private static void AddPart(List<string> parts, string name, string value)
        {
            parts.Add(name + "=" + (value ?? string.Empty));
        }

        private static string JoinValues(IEnumerable<string> values)
        {
            return string.Join("\u001e", values ?? Array.Empty<string>());
        }

        private static string NormalizeOptionalPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizePathValue(value);
        }

        private static string NormalizePathValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AgentSessionCache));
            }
        }

        private sealed class SkillSessionEntry
        {
            public string Id { get; set; }
            public string Key { get; set; }
            public string SkillInputPath { get; set; }
            public string DataDirectory { get; set; }
            public SkillSpriteExporter.SkillSpriteExportSession Session { get; set; }
            public int UseCount { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset LastUsedAt { get; set; }
        }

        private sealed class DomainRepositoryEntry
        {
            public string Id { get; set; }
            public string Key { get; set; }
            public string Kind { get; set; }
            public string InputPath { get; set; }
            public string DataDirectory { get; set; }
            public CliWzRepository Repository { get; set; }
            public int UseCount { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset LastUsedAt { get; set; }
        }

        private sealed class WzContextEntry
        {
            public string Id { get; set; }
            public string Key { get; set; }
            public string InputPath { get; set; }
            public WzLoadContext Context { get; set; }
            public int UseCount { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset LastUsedAt { get; set; }
        }
    }

    internal sealed class AgentSkillSessionLease
    {
        public AgentSkillSessionLease(
            SkillSpriteExporter.SkillSpriteExportSession session,
            string cacheStatus,
            string sessionId)
        {
            this.Session = session;
            this.CacheStatus = cacheStatus;
            this.SessionId = sessionId;
        }

        public SkillSpriteExporter.SkillSpriteExportSession Session { get; private set; }
        public string CacheStatus { get; private set; }
        public string SessionId { get; private set; }
    }

    internal sealed class AgentDomainRepositoryLease
    {
        public AgentDomainRepositoryLease(
            CliWzRepository repository,
            string cacheStatus,
            string sessionId)
        {
            this.Repository = repository;
            this.CacheStatus = cacheStatus;
            this.SessionId = sessionId;
        }

        public CliWzRepository Repository { get; private set; }
        public string CacheStatus { get; private set; }
        public string SessionId { get; private set; }
    }

    internal sealed class AgentWzContextLease
    {
        public AgentWzContextLease(
            WzLoadContext context,
            string cacheStatus,
            string sessionId)
        {
            this.Context = context;
            this.CacheStatus = cacheStatus;
            this.SessionId = sessionId;
        }

        public WzLoadContext Context { get; private set; }
        public string CacheStatus { get; private set; }
        public string SessionId { get; private set; }
    }
}
