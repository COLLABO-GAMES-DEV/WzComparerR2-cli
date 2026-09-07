using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal sealed class ImageSearchOptions
    {
        public string DataDirectory { get; private set; }
        public string QueryPath { get; private set; }
        public string OutputDirectory { get; private set; }
        public string ManifestPath { get; private set; }
        public string Scope { get; private set; }
        public string CacheDirectory { get; private set; }
        public bool NoCache { get; private set; }
        public bool RebuildCache { get; private set; }
        public bool TrustCache { get; private set; }
        public bool NoRefine { get; private set; }
        public bool NoSizePrefilter { get; private set; }
        public double MinSizeRatio { get; private set; }
        public int RefineLimit { get; private set; }
        public int MaxResults { get; private set; }
        public double MinScore { get; private set; }
        public int MinAlpha { get; private set; }

        public static ImageSearchOptions FromArgs(ParsedArgs args)
        {
            bool noCache = args.HasFlag("no-cache");
            string cacheDirectory = args.GetValue("cache-dir");
            if (string.IsNullOrWhiteSpace(cacheDirectory) && !noCache)
            {
                cacheDirectory = ImageSearchCacheStore.GetDefaultCacheDirectory();
            }

            return new ImageSearchOptions
            {
                DataDirectory = args.GetValue("data-dir"),
                QueryPath = args.GetValue("query") ?? args.GetValue("image"),
                OutputDirectory = args.GetValue("out") ?? args.GetValue("output"),
                ManifestPath = args.GetValue("manifest"),
                Scope = args.GetValue("scope") ?? "all",
                CacheDirectory = cacheDirectory,
                NoCache = noCache,
                RebuildCache = args.HasFlag("rebuild-cache"),
                TrustCache = args.HasFlag("trust-cache"),
                NoRefine = args.HasFlag("no-refine"),
                NoSizePrefilter = args.HasFlag("no-size-prefilter"),
                MinSizeRatio = ParseScore(args.GetValue("min-size-ratio"), 0.25, "--min-size-ratio"),
                MaxResults = Math.Max(1, args.GetInt("max-results", 20)),
                RefineLimit = Math.Max(0, args.GetInt("refine-limit", 0)),
                MinScore = ParseScore(args.GetValue("min-score"), 0.0, "--min-score"),
                MinAlpha = Math.Max(0, Math.Min(255, args.GetInt("min-alpha", 16)))
            };
        }

        private static double ParseScore(string value, double defaultValue, string optionName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            double parsed;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                throw new UsageException(optionName + " must be a number between 0 and 1.");
            }

            if (parsed < 0 || parsed > 1)
            {
                throw new UsageException(optionName + " must be a number between 0 and 1.");
            }

            return parsed;
        }
    }

    internal sealed class ImageSearchResultDto
    {
        public string InputPath { get; set; }
        public string DataDirectory { get; set; }
        public string RootPath { get; set; }
        public string QueryPath { get; set; }
        public int QueryWidth { get; set; }
        public int QueryHeight { get; set; }
        public string Method { get; set; }
        public string Scope { get; set; }
        public string CacheDirectory { get; set; }
        public bool UsedCache { get; set; }
        public int CacheHitCount { get; set; }
        public int CacheMissCount { get; set; }
        public int FailedRootCount { get; set; }
        public bool Refined { get; set; }
        public int MaxResults { get; set; }
        public int CandidatePoolLimit { get; set; }
        public int RefinedImageCount { get; set; }
        public double MinScore { get; set; }
        public int MinAlpha { get; set; }
        public int IndexedImageCount { get; set; }
        public int ScannedImageCount { get; set; }
        public int SkippedImageCount { get; set; }
        public int PrefilteredImageCount { get; set; }
        public int Count { get { return this.Results == null ? 0 : this.Results.Count; } }
        public string OutputDirectory { get; set; }
        public string ManifestPath { get; set; }
        public List<ImageSearchRootDto> Roots { get; set; }
        public List<ImageSearchMatchDto> Results { get; set; }
    }

    internal sealed class ImageSearchRootDto
    {
        public string InputPath { get; set; }
        public string RootPath { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
        public bool CacheHit { get; set; }
        public string CachePath { get; set; }
        public int IndexedImageCount { get; set; }
        public int ScannedImageCount { get; set; }
        public int SkippedImageCount { get; set; }
        public int PrefilteredImageCount { get; set; }
        public string Error { get; set; }
    }

    internal sealed class ImageSearchMatchDto
    {
        public int Rank { get; set; }
        public string ScoreMethod { get; set; }
        public double Score { get; set; }
        public double PerceptualScore { get; set; }
        public double PixelScore { get; set; }
        public double ColorScore { get; set; }
        public double ShapeScore { get; set; }
        public int HashDistance { get; set; }
        public string QueryRegion { get; set; }
        public string MatchedRegion { get; set; }
        public string ParentPath { get; set; }
        public string SourceInputPath { get; set; }
        public string SourceRootPath { get; set; }
        public string SourceScope { get; set; }
        public bool FromCache { get; set; }
        public bool Refined { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public int? Page { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; }
        public int Pages { get; set; }
        public string OutputPath { get; set; }
        public string OutputError { get; set; }
        public long? Bytes { get; set; }
        public string Sha256 { get; set; }
    }

    internal static class ImageSimilaritySearcher
    {
        private const string FullScoreMethod = "phash-alpha-crop-region-pixel-color-v2";
        private const string CachedScoreMethod = "phash-alpha-crop-region-color-cache-v2";
        private const int HashSize = 32;
        private const int LowFrequencySize = 8;
        private static readonly double[,] CosineTable = CreateCosineTable();

        public static ImageSearchResultDto Search(string inputPath, Wz_Node root, ImageSearchOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            RgbaImage queryImage = PngFileReader.Load(options.QueryPath);
            List<ImageFingerprint> queryVariants = ImageFingerprint.CreateSearchVariants(queryImage, options.MinAlpha).ToList();
            var candidates = new List<ImageSearchCandidate>();
            var result = new ImageSearchResultDto
            {
                InputPath = Path.GetFullPath(inputPath),
                RootPath = root.FullPath,
                Scope = "explicit",
                QueryPath = Path.GetFullPath(options.QueryPath),
                QueryWidth = queryImage.Width,
                QueryHeight = queryImage.Height,
                Method = FullScoreMethod,
                MaxResults = options.MaxResults,
                CandidatePoolLimit = options.MaxResults,
                MinScore = options.MinScore,
                MinAlpha = options.MinAlpha,
                CacheDirectory = options.NoCache ? null : options.CacheDirectory,
                OutputDirectory = string.IsNullOrEmpty(options.OutputDirectory) ? null : Path.GetFullPath(options.OutputDirectory),
                Roots = new List<ImageSearchRootDto>(),
                Results = new List<ImageSearchMatchDto>()
            };
            var rootDto = new ImageSearchRootDto
            {
                InputPath = result.InputPath,
                RootPath = result.RootPath,
                Scope = result.Scope,
                Status = "scanned"
            };
            result.Roots.Add(rootDto);

            foreach (Wz_Node node in Traverse(root))
            {
                Wz_Node imageNode = NodePath.ExtractImageNode(node, true);
                var png = imageNode == null ? null : imageNode.Value as Wz_Png;
                if (png == null)
                {
                    continue;
                }

                int pages = Math.Max(1, png.ActualPages);
                for (int page = 0; page < pages; page++)
                {
                    if (ShouldPrefilterBySize(queryImage.Width, queryImage.Height, png.Width, png.Height, options))
                    {
                        result.PrefilteredImageCount++;
                        continue;
                    }

                    result.ScannedImageCount++;
                    try
                    {
                        RgbaImage candidateImage = DecodeWzPng(png, page);
                        ImageSearchScore score = Score(queryVariants, candidateImage, options.MinAlpha);
                        if (score.Score < options.MinScore)
                        {
                            continue;
                        }

                        AddTopCandidate(candidates, new ImageSearchCandidate
                        {
                            Node = imageNode,
                            SourceInputPath = result.InputPath,
                            SourceRootPath = result.RootPath,
                            SourceScope = result.Scope,
                            Page = page,
                            Match = CreateMatch(imageNode, png, page, pages, score, FullScoreMethod, result.InputPath, result.RootPath, result.Scope, false)
                        }, options.MaxResults);
                    }
                    catch (NotSupportedException)
                    {
                        result.SkippedImageCount++;
                    }
                    catch (InvalidOperationException)
                    {
                        result.SkippedImageCount++;
                    }
                }
            }
            rootDto.ScannedImageCount = result.ScannedImageCount;
            rootDto.SkippedImageCount = result.SkippedImageCount;
            rootDto.PrefilteredImageCount = result.PrefilteredImageCount;

            candidates.Sort(CompareCandidates);
            if (!string.IsNullOrEmpty(options.OutputDirectory))
            {
                ExportMatches(candidates, options.OutputDirectory, null);
            }

            int rank = 1;
            foreach (ImageSearchCandidate candidate in candidates)
            {
                candidate.Match.Rank = rank++;
                result.Results.Add(candidate.Match);
            }

            return result;
        }

        public static ImageSearchResultDto SearchInputs(IReadOnlyList<ImageSearchScanRoot> inputs, string nodePath, ImageSearchOptions options, WzLoadOptions loadOptions)
        {
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            RgbaImage queryImage = PngFileReader.Load(options.QueryPath);
            List<ImageFingerprint> queryVariants = ImageFingerprint.CreateSearchVariants(queryImage, options.MinAlpha).ToList();
            var candidates = new List<ImageSearchCandidate>();
            int candidatePoolLimit = GetCandidatePoolLimit(options);
            var result = new ImageSearchResultDto
            {
                InputPath = inputs.Count == 1 ? Path.GetFullPath(inputs[0].InputPath) : null,
                DataDirectory = string.IsNullOrEmpty(options.DataDirectory) ? null : Path.GetFullPath(options.DataDirectory),
                QueryPath = Path.GetFullPath(options.QueryPath),
                QueryWidth = queryImage.Width,
                QueryHeight = queryImage.Height,
                Method = FullScoreMethod,
                Scope = options.Scope,
                MaxResults = options.MaxResults,
                CandidatePoolLimit = candidatePoolLimit,
                MinScore = options.MinScore,
                MinAlpha = options.MinAlpha,
                CacheDirectory = options.NoCache ? null : options.CacheDirectory,
                OutputDirectory = string.IsNullOrEmpty(options.OutputDirectory) ? null : Path.GetFullPath(options.OutputDirectory),
                Roots = new List<ImageSearchRootDto>(),
                Results = new List<ImageSearchMatchDto>()
            };

            bool canUseCache = !options.NoCache && string.IsNullOrEmpty(nodePath);
            foreach (ImageSearchScanRoot input in inputs)
            {
                var rootDto = new ImageSearchRootDto
                {
                    InputPath = Path.GetFullPath(input.InputPath),
                    Scope = input.Scope
                };
                result.Roots.Add(rootDto);

                ImageSearchCacheFileDto cache;
                string cachePath;
                if (canUseCache && !options.RebuildCache && ImageSearchCacheStore.TryReadValid(options.CacheDirectory, input.InputPath, options, out cache, out cachePath))
                {
                    rootDto.CacheHit = true;
                    rootDto.CachePath = cachePath;
                    rootDto.RootPath = cache.RootPath;
                    rootDto.Status = "cache-hit";
                    result.CacheHitCount++;
                    result.UsedCache = true;
                    ScoreCachedItems(cache, queryVariants, queryImage, candidates, options, result, rootDto, candidatePoolLimit);
                    continue;
                }

                result.CacheMissCount++;
                if (canUseCache)
                {
                    rootDto.CachePath = ImageSearchCacheStore.GetCachePath(options.CacheDirectory, input.InputPath, options);
                }

                try
                {
                    using (var context = WzLoadContext.Load(input.InputPath, loadOptions ?? new WzLoadOptions()))
                    {
                        Wz_Node root = NodePath.Resolve(context.Root, nodePath, true);
                        if (root == null)
                        {
                            rootDto.Status = "path-not-found";
                            rootDto.Error = "WZ path not found: " + nodePath;
                            result.FailedRootCount++;
                            continue;
                        }

                        rootDto.RootPath = root.FullPath;
                        if (canUseCache)
                        {
                            var cacheFile = new ImageSearchCacheFileDto
                            {
                                Version = ImageSearchCacheStore.CurrentVersion,
                                Method = ImageSearchCacheStore.Method,
                                MinAlpha = options.MinAlpha,
                                InputPath = Path.GetFullPath(input.InputPath),
                                RootPath = root.FullPath,
                                Scope = input.Scope,
                                CreatedUtc = DateTime.UtcNow,
                                InputStamp = ImageSearchCacheStore.CreateStamp(input.InputPath),
                                Items = BuildIndex(input.InputPath, input.Scope, root, options, result, rootDto)
                            };
                            rootDto.CachePath = ImageSearchCacheStore.Write(options.CacheDirectory, cacheFile);
                            rootDto.Status = "indexed";
                            result.UsedCache = true;
                            ScoreCachedItems(cacheFile, queryVariants, queryImage, candidates, options, result, rootDto, candidatePoolLimit);
                        }
                        else
                        {
                            ScanLoadedRoot(input.InputPath, input.Scope, root, options, queryImage, queryVariants, candidates, result, rootDto, candidatePoolLimit);
                            rootDto.Status = "scanned";
                        }
                    }
                }
                catch (Exception ex)
                {
                    rootDto.Status = "failed";
                    rootDto.Error = GetInnermostMessage(ex);
                    result.FailedRootCount++;
                }
            }

            candidates.Sort(CompareCandidates);
            if (!options.NoRefine && candidates.Any(candidate => candidate.Match.FromCache))
            {
                RefineCandidates(candidates, queryVariants, options, loadOptions, result);
                candidates.Sort(CompareCandidates);
            }
            TrimCandidates(candidates, options.MaxResults);
            if (!string.IsNullOrEmpty(options.OutputDirectory))
            {
                ExportMatches(candidates, options.OutputDirectory, loadOptions);
            }

            int rank = 1;
            foreach (ImageSearchCandidate candidate in candidates)
            {
                candidate.Match.Rank = rank++;
                result.Results.Add(candidate.Match);
            }

            if (result.Refined)
            {
                result.Method = FullScoreMethod + "+" + CachedScoreMethod;
            }
            else if (result.UsedCache && result.CacheMissCount == 0)
            {
                result.Method = CachedScoreMethod;
            }
            else if (result.UsedCache)
            {
                result.Method = FullScoreMethod + "+" + CachedScoreMethod;
            }

            return result;
        }

        public static string WriteManifest(ImageSearchResultDto result, string manifestPath)
        {
            string fullPath = Path.GetFullPath(manifestPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return fullPath;
        }

        private static void ScanLoadedRoot(
            string inputPath,
            string scope,
            Wz_Node root,
            ImageSearchOptions options,
            RgbaImage queryImage,
            IReadOnlyList<ImageFingerprint> queryVariants,
            List<ImageSearchCandidate> candidates,
            ImageSearchResultDto result,
            ImageSearchRootDto rootDto,
            int candidatePoolLimit)
        {
            string sourceInputPath = Path.GetFullPath(inputPath);
            string sourceRootPath = root.FullPath;
            foreach (Wz_Node node in Traverse(root))
            {
                Wz_Node imageNode = NodePath.ExtractImageNode(node, true);
                var png = imageNode == null ? null : imageNode.Value as Wz_Png;
                if (png == null)
                {
                    continue;
                }

                int pages = Math.Max(1, png.ActualPages);
                for (int page = 0; page < pages; page++)
                {
                    if (ShouldPrefilterBySize(queryImage.Width, queryImage.Height, png.Width, png.Height, options))
                    {
                        result.PrefilteredImageCount++;
                        rootDto.PrefilteredImageCount++;
                        continue;
                    }

                    result.ScannedImageCount++;
                    rootDto.ScannedImageCount++;
                    try
                    {
                        RgbaImage candidateImage = DecodeWzPng(png, page);
                        List<ImageFingerprint> variants = ImageFingerprint.CreateCandidateVariants(candidateImage, options.MinAlpha).ToList();
                        ImageSearchScore score = Score(queryVariants, variants);

                        if (score.Score < options.MinScore)
                        {
                            continue;
                        }

                        AddTopCandidate(candidates, new ImageSearchCandidate
                        {
                            SourceInputPath = sourceInputPath,
                            SourceRootPath = sourceRootPath,
                            SourceScope = scope,
                            Page = page,
                            Match = CreateMatch(imageNode, png, page, pages, score, FullScoreMethod, sourceInputPath, sourceRootPath, scope, false)
                        }, candidatePoolLimit);
                    }
                    catch (NotSupportedException)
                    {
                        result.SkippedImageCount++;
                        rootDto.SkippedImageCount++;
                    }
                    catch (InvalidOperationException)
                    {
                        result.SkippedImageCount++;
                        rootDto.SkippedImageCount++;
                    }
                }
            }
        }

        private static List<ImageSearchIndexItemDto> BuildIndex(
            string inputPath,
            string scope,
            Wz_Node root,
            ImageSearchOptions options,
            ImageSearchResultDto result,
            ImageSearchRootDto rootDto)
        {
            var indexItems = new List<ImageSearchIndexItemDto>();
            foreach (Wz_Node node in Traverse(root))
            {
                Wz_Node imageNode = NodePath.ExtractImageNode(node, true);
                var png = imageNode == null ? null : imageNode.Value as Wz_Png;
                if (png == null)
                {
                    continue;
                }

                int pages = Math.Max(1, png.ActualPages);
                for (int page = 0; page < pages; page++)
                {
                    try
                    {
                        RgbaImage candidateImage = DecodeWzPng(png, page);
                        List<ImageFingerprint> variants = ImageFingerprint.CreateCandidateVariants(candidateImage, options.MinAlpha).ToList();
                        indexItems.Add(CreateIndexItem(imageNode, png, page, pages, variants));
                        result.IndexedImageCount++;
                        rootDto.IndexedImageCount++;
                    }
                    catch (NotSupportedException)
                    {
                        result.SkippedImageCount++;
                        rootDto.SkippedImageCount++;
                    }
                    catch (InvalidOperationException)
                    {
                        result.SkippedImageCount++;
                        rootDto.SkippedImageCount++;
                    }
                }
            }

            return indexItems;
        }

        private static void ScoreCachedItems(
            ImageSearchCacheFileDto cache,
            IReadOnlyList<ImageFingerprint> queryVariants,
            RgbaImage queryImage,
            List<ImageSearchCandidate> candidates,
            ImageSearchOptions options,
            ImageSearchResultDto result,
            ImageSearchRootDto rootDto,
            int candidatePoolLimit)
        {
            if (cache.Items == null)
            {
                return;
            }

            foreach (ImageSearchIndexItemDto item in cache.Items)
            {
                if (ShouldPrefilterBySize(queryImage.Width, queryImage.Height, item.Width, item.Height, options))
                {
                    result.PrefilteredImageCount++;
                    rootDto.PrefilteredImageCount++;
                    continue;
                }

                ImageSearchScore score = ScoreCached(queryVariants, item.Fingerprints);
                result.ScannedImageCount++;
                rootDto.ScannedImageCount++;
                if (score.Score < options.MinScore)
                {
                    continue;
                }

                AddTopCandidate(candidates, new ImageSearchCandidate
                {
                    SourceInputPath = cache.InputPath,
                    SourceRootPath = cache.RootPath,
                    SourceScope = cache.Scope,
                    Page = item.Page ?? 0,
                    Match = CreateMatchFromIndexItem(item, score, cache)
                }, candidatePoolLimit);
            }
        }

        private static IEnumerable<Wz_Node> Traverse(Wz_Node root)
        {
            if (root == null)
            {
                yield break;
            }

            var stack = new Stack<Wz_Node>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                Wz_Node node = NodePath.ExtractImageNode(stack.Pop(), true);
                if (node == null)
                {
                    continue;
                }

                yield return node;

                for (int i = node.Nodes.Count - 1; i >= 0; i--)
                {
                    stack.Push(node.Nodes[i]);
                }
            }
        }

        private static RgbaImage DecodeWzPng(Wz_Png png, int page)
        {
            CrossPlatformPngWriter.DecodedPngPixels decoded = CrossPlatformPngWriter.DecodeToBgra32(png, page);
            byte[] rgba = new byte[decoded.Width * decoded.Height * 4];
            for (int src = 0, dst = 0; src < decoded.Bgra.Length; src += 4, dst += 4)
            {
                rgba[dst] = decoded.Bgra[src + 2];
                rgba[dst + 1] = decoded.Bgra[src + 1];
                rgba[dst + 2] = decoded.Bgra[src];
                rgba[dst + 3] = decoded.Bgra[src + 3];
            }

            return new RgbaImage(decoded.Width, decoded.Height, rgba);
        }

        private static ImageSearchMatchDto CreateMatch(Wz_Node node, Wz_Png png, int page, int pages, ImageSearchScore score, string scoreMethod, string sourceInputPath, string sourceRootPath, string scope, bool fromCache)
        {
            return new ImageSearchMatchDto
            {
                ScoreMethod = scoreMethod,
                Score = Round(score.Score),
                PerceptualScore = Round(score.PerceptualScore),
                PixelScore = Round(score.PixelScore),
                ColorScore = Round(score.ColorScore),
                ShapeScore = Round(score.ShapeScore),
                HashDistance = score.HashDistance,
                QueryRegion = score.QueryRegion,
                MatchedRegion = score.Region,
                ParentPath = GetParentPath(node.FullPath),
                SourceInputPath = sourceInputPath,
                SourceRootPath = sourceRootPath,
                SourceScope = scope,
                FromCache = fromCache,
                Refined = string.Equals(scoreMethod, FullScoreMethod, StringComparison.Ordinal),
                Name = node.Text,
                Path = node.FullPath,
                Type = "png",
                Page = pages > 1 ? page : (int?)null,
                Width = png.Width,
                Height = png.Height,
                Format = png.Format.ToString(),
                Pages = pages
            };
        }

        private static ImageSearchMatchDto CreateMatchFromIndexItem(ImageSearchIndexItemDto item, ImageSearchScore score, ImageSearchCacheFileDto cache)
        {
            return new ImageSearchMatchDto
            {
                ScoreMethod = CachedScoreMethod,
                Score = Round(score.Score),
                PerceptualScore = Round(score.PerceptualScore),
                PixelScore = Round(score.PixelScore),
                ColorScore = Round(score.ColorScore),
                ShapeScore = Round(score.ShapeScore),
                HashDistance = score.HashDistance,
                QueryRegion = score.QueryRegion,
                MatchedRegion = score.Region,
                ParentPath = GetParentPath(item.Path),
                SourceInputPath = cache.InputPath,
                SourceRootPath = cache.RootPath,
                SourceScope = cache.Scope,
                FromCache = true,
                Refined = false,
                Name = item.Name,
                Path = item.Path,
                Type = item.Type,
                Page = item.Page,
                Width = item.Width,
                Height = item.Height,
                Format = item.Format,
                Pages = item.Pages
            };
        }

        private static ImageSearchIndexItemDto CreateIndexItem(Wz_Node node, Wz_Png png, int page, int pages, IEnumerable<ImageFingerprint> fingerprints)
        {
            return new ImageSearchIndexItemDto
            {
                Name = node.Text,
                Path = node.FullPath,
                Type = "png",
                Page = pages > 1 ? page : (int?)null,
                Width = png.Width,
                Height = png.Height,
                Format = png.Format.ToString(),
                Pages = pages,
                Fingerprints = fingerprints.Select(ToDto).ToList()
            };
        }

        private static ImageSearchFingerprintDto ToDto(ImageFingerprint fingerprint)
        {
            return new ImageSearchFingerprintDto
            {
                Region = fingerprint.Region,
                Hash = fingerprint.Hash,
                AverageR = fingerprint.AverageR,
                AverageG = fingerprint.AverageG,
                AverageB = fingerprint.AverageB,
                AspectRatio = fingerprint.AspectRatio,
                AlphaCoverage = fingerprint.AlphaCoverage
            };
        }

        private static bool ShouldPrefilterBySize(int queryWidth, int queryHeight, int candidateWidth, int candidateHeight, ImageSearchOptions options)
        {
            if (options.NoSizePrefilter)
            {
                return false;
            }
            if (queryWidth < 128 && queryHeight < 128)
            {
                return false;
            }

            double ratio = options.MinSizeRatio;
            return candidateWidth < queryWidth * ratio || candidateHeight < queryHeight * ratio;
        }

        private static double Round(double value)
        {
            return Math.Round(value, 6, MidpointRounding.AwayFromZero);
        }

        private static int GetCandidatePoolLimit(ImageSearchOptions options)
        {
            if (options.RefineLimit > 0)
            {
                return Math.Max(options.MaxResults, options.RefineLimit);
            }

            return Math.Max(options.MaxResults, Math.Min(250, Math.Max(50, options.MaxResults * 8)));
        }

        private static void TrimCandidates(List<ImageSearchCandidate> candidates, int maxResults)
        {
            if (candidates.Count <= maxResults)
            {
                return;
            }

            candidates.RemoveRange(maxResults, candidates.Count - maxResults);
        }

        private static void AddTopCandidate(List<ImageSearchCandidate> candidates, ImageSearchCandidate candidate, int maxResults)
        {
            candidates.Add(candidate);
            candidates.Sort(CompareCandidates);
            if (candidates.Count > maxResults)
            {
                candidates.RemoveAt(candidates.Count - 1);
            }
        }

        private static int CompareCandidates(ImageSearchCandidate left, ImageSearchCandidate right)
        {
            int score = right.Match.Score.CompareTo(left.Match.Score);
            if (score != 0)
            {
                return score;
            }

            int hash = left.Match.HashDistance.CompareTo(right.Match.HashDistance);
            if (hash != 0)
            {
                return hash;
            }

            return string.Compare(left.Match.Path, right.Match.Path, StringComparison.OrdinalIgnoreCase);
        }

        private static void RefineCandidates(
            List<ImageSearchCandidate> candidates,
            IReadOnlyList<ImageFingerprint> queryVariants,
            ImageSearchOptions options,
            WzLoadOptions loadOptions,
            ImageSearchResultDto result)
        {
            var loadedContexts = new Dictionary<string, WzLoadContext>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ImageSearchCandidate candidate in candidates)
                {
                    Wz_Node node = candidate.Node ?? ResolveCandidateNode(candidate, loadedContexts, loadOptions);
                    if (node == null || !(node.Value is Wz_Png))
                    {
                        candidate.Match.OutputError = "Could not resolve cached match for refine.";
                        continue;
                    }

                    try
                    {
                        var png = (Wz_Png)node.Value;
                        RgbaImage candidateImage = DecodeWzPng(png, candidate.Page);
                        ImageSearchScore score = Score(queryVariants, candidateImage, options.MinAlpha);
                        UpdateMatchScore(candidate.Match, score, FullScoreMethod, true);
                        result.Refined = true;
                        result.RefinedImageCount++;
                    }
                    catch (NotSupportedException ex)
                    {
                        candidate.Match.OutputError = "Refine skipped: " + ex.Message;
                    }
                    catch (InvalidOperationException ex)
                    {
                        candidate.Match.OutputError = "Refine skipped: " + ex.Message;
                    }
                }
            }
            finally
            {
                foreach (WzLoadContext context in loadedContexts.Values)
                {
                    context.Dispose();
                }
            }
        }

        private static void UpdateMatchScore(ImageSearchMatchDto match, ImageSearchScore score, string scoreMethod, bool refined)
        {
            match.ScoreMethod = scoreMethod;
            match.Score = Round(score.Score);
            match.PerceptualScore = Round(score.PerceptualScore);
            match.PixelScore = Round(score.PixelScore);
            match.ColorScore = Round(score.ColorScore);
            match.ShapeScore = Round(score.ShapeScore);
            match.HashDistance = score.HashDistance;
            match.QueryRegion = score.QueryRegion;
            match.MatchedRegion = score.Region;
            match.Refined = refined;
        }

        private static void ExportMatches(IReadOnlyList<ImageSearchCandidate> candidates, string outputDirectory, WzLoadOptions loadOptions)
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);
            var loadedContexts = new Dictionary<string, WzLoadContext>(StringComparer.OrdinalIgnoreCase);

            try
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    ImageSearchCandidate candidate = candidates[i];
                    Wz_Node node = candidate.Node;
                    if (node == null)
                    {
                        node = ResolveCandidateNode(candidate, loadedContexts, loadOptions);
                    }
                    if (node == null || !(node.Value is Wz_Png))
                    {
                        candidate.Match.OutputError = "Could not resolve cached match for export.";
                        continue;
                    }

                    var png = (Wz_Png)node.Value;
                    string filename = (i + 1).ToString("D2", CultureInfo.InvariantCulture)
                        + "_score-" + candidate.Match.Score.ToString("0.000", CultureInfo.InvariantCulture)
                        + "_" + SanitizeFileName(candidate.Match.Path);
                    if (candidate.Page > 0)
                    {
                        filename += ".p" + (candidate.Page + 1).ToString(CultureInfo.InvariantCulture);
                    }
                    filename += ".png";

                    string path = Path.Combine(fullOutputDirectory, filename);
                    CrossPlatformPngWriter.Save(png, candidate.Page, path);
                    var info = new FileInfo(path);
                    candidate.Match.OutputPath = path;
                    candidate.Match.Bytes = info.Length;
                    candidate.Match.Sha256 = ComputeSha256(path);
                }
            }
            finally
            {
                foreach (WzLoadContext context in loadedContexts.Values)
                {
                    context.Dispose();
                }
            }
        }

        private static Wz_Node ResolveCandidateNode(ImageSearchCandidate candidate, Dictionary<string, WzLoadContext> loadedContexts, WzLoadOptions loadOptions)
        {
            if (string.IsNullOrEmpty(candidate.SourceInputPath) || string.IsNullOrEmpty(candidate.Match.Path))
            {
                return null;
            }

            WzLoadContext context;
            if (!loadedContexts.TryGetValue(candidate.SourceInputPath, out context))
            {
                context = WzLoadContext.Load(candidate.SourceInputPath, loadOptions ?? new WzLoadOptions());
                loadedContexts.Add(candidate.SourceInputPath, context);
            }

            return NodePath.Resolve(context.Root, candidate.Match.Path, true);
        }

        private static string SanitizeFileName(string text)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var result = new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                result[i] = invalid.Contains(ch) || ch == '\\' || ch == '/' || ch == ':' ? '_' : ch;
            }

            string sanitized = new string(result);
            return sanitized.Length > 180 ? sanitized.Substring(sanitized.Length - 180) : sanitized;
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            int index = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return index <= 0 ? null : path.Substring(0, index);
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static ImageSearchScore Score(IReadOnlyList<ImageFingerprint> queryVariants, RgbaImage candidate, int minAlpha)
        {
            return Score(queryVariants, ImageFingerprint.CreateCandidateVariants(candidate, minAlpha));
        }

        private static ImageSearchScore Score(IReadOnlyList<ImageFingerprint> queryVariants, IEnumerable<ImageFingerprint> variants)
        {
            ImageSearchScore best = null;
            IReadOnlyList<ImageFingerprint> candidateVariants = variants as IReadOnlyList<ImageFingerprint> ?? variants.ToList();
            foreach (ImageFingerprint query in queryVariants)
            {
                foreach (ImageFingerprint variant in candidateVariants)
                {
                    int distance = HammingDistance(query.Hash ^ variant.Hash);
                    double perceptualScore = 1.0 - distance / 63.0;
                    double pixelScore = PixelSimilarity(query, variant);
                    double colorScore = ColorSimilarity(query, variant);
                    double shapeScore = ShapeSimilarity(query, variant);
                    double score = perceptualScore * 0.40 + pixelScore * 0.34 + colorScore * 0.18 + shapeScore * 0.08;

                    var current = new ImageSearchScore
                    {
                        Score = Clamp01(score),
                        PerceptualScore = Clamp01(perceptualScore),
                        PixelScore = Clamp01(pixelScore),
                        ColorScore = Clamp01(colorScore),
                        ShapeScore = Clamp01(shapeScore),
                        HashDistance = distance,
                        QueryRegion = query.Region,
                        Region = variant.Region
                    };

                    if (best == null || current.Score > best.Score)
                    {
                        best = current;
                    }
                }
            }

            return best ?? new ImageSearchScore { Region = "none", HashDistance = 64 };
        }

        private static ImageSearchScore ScoreCached(IReadOnlyList<ImageFingerprint> queryVariants, IReadOnlyList<ImageSearchFingerprintDto> variants)
        {
            ImageSearchScore best = null;
            if (variants == null)
            {
                return new ImageSearchScore { Region = "none", HashDistance = 64 };
            }

            foreach (ImageFingerprint query in queryVariants)
            {
                foreach (ImageSearchFingerprintDto variant in variants)
                {
                    int distance = HammingDistance(query.Hash ^ variant.Hash);
                    double perceptualScore = 1.0 - distance / 63.0;
                    double colorScore = ColorSimilarity(query.AverageR, query.AverageG, query.AverageB, variant.AverageR, variant.AverageG, variant.AverageB);
                    double shapeScore = ShapeSimilarity(query.AspectRatio, query.AlphaCoverage, variant.AspectRatio, variant.AlphaCoverage);
                    double score = perceptualScore * 0.72 + colorScore * 0.20 + shapeScore * 0.08;

                    var current = new ImageSearchScore
                    {
                        Score = Clamp01(score),
                        PerceptualScore = Clamp01(perceptualScore),
                        PixelScore = 0,
                        ColorScore = Clamp01(colorScore),
                        ShapeScore = Clamp01(shapeScore),
                        HashDistance = distance,
                        QueryRegion = query.Region,
                        Region = variant.Region
                    };

                    if (best == null || current.Score > best.Score)
                    {
                        best = current;
                    }
                }
            }

            return best ?? new ImageSearchScore { Region = "none", HashDistance = 64 };
        }

        private static double ColorSimilarity(ImageFingerprint left, ImageFingerprint right)
        {
            return ColorSimilarity(left.AverageR, left.AverageG, left.AverageB, right.AverageR, right.AverageG, right.AverageB);
        }

        private static double ColorSimilarity(double leftR, double leftG, double leftB, double rightR, double rightG, double rightB)
        {
            double dr = leftR - rightR;
            double dg = leftG - rightG;
            double db = leftB - rightB;
            return Clamp01(1.0 - Math.Sqrt(dr * dr + dg * dg + db * db) / 441.67295593);
        }

        private static double PixelSimilarity(ImageFingerprint left, ImageFingerprint right)
        {
            double sum = 0;
            for (int i = 0; i < left.Grayscale.Length; i++)
            {
                double value = left.Grayscale[i] - right.Grayscale[i];
                sum += value * value;
            }

            return Clamp01(1.0 - Math.Sqrt(sum / left.Grayscale.Length) / 255.0);
        }

        private static double ShapeSimilarity(ImageFingerprint left, ImageFingerprint right)
        {
            return ShapeSimilarity(left.AspectRatio, left.AlphaCoverage, right.AspectRatio, right.AlphaCoverage);
        }

        private static double ShapeSimilarity(double leftAspectRatio, double leftAlphaCoverage, double rightAspectRatio, double rightAlphaCoverage)
        {
            double aspectSimilarity = 1.0 - Math.Abs(leftAspectRatio - rightAspectRatio) / Math.Max(leftAspectRatio, rightAspectRatio);
            double coverageSimilarity = 1.0 - Math.Abs(leftAlphaCoverage - rightAlphaCoverage);
            return Clamp01(aspectSimilarity * 0.75 + coverageSimilarity * 0.25);
        }

        private static string GetInnermostMessage(Exception ex)
        {
            Exception current = ex;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }
            return current.Message;
        }

        private static double Clamp01(double value)
        {
            if (value < 0)
            {
                return 0;
            }
            if (value > 1)
            {
                return 1;
            }
            return value;
        }

        private static int HammingDistance(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
        }

        private static ulong ComputePHash(double[] grayscale)
        {
            double[] coefficients = new double[LowFrequencySize * LowFrequencySize];
            int index = 0;
            for (int v = 0; v < LowFrequencySize; v++)
            {
                for (int u = 0; u < LowFrequencySize; u++)
                {
                    double sum = 0;
                    for (int y = 0; y < HashSize; y++)
                    {
                        int row = y * HashSize;
                        for (int x = 0; x < HashSize; x++)
                        {
                            sum += grayscale[row + x] * CosineTable[u, x] * CosineTable[v, y];
                        }
                    }

                    coefficients[index++] = sum * DctScale(u) * DctScale(v);
                }
            }

            double[] withoutDc = coefficients.Skip(1).ToArray();
            Array.Sort(withoutDc);
            double median = withoutDc[withoutDc.Length / 2];
            ulong hash = 0;
            for (int i = 1; i < coefficients.Length; i++)
            {
                if (coefficients[i] > median)
                {
                    hash |= 1UL << (i - 1);
                }
            }

            return hash;
        }

        private static double DctScale(int value)
        {
            return value == 0 ? Math.Sqrt(1.0 / HashSize) : Math.Sqrt(2.0 / HashSize);
        }

        private static double[,] CreateCosineTable()
        {
            var table = new double[LowFrequencySize, HashSize];
            for (int frequency = 0; frequency < LowFrequencySize; frequency++)
            {
                for (int sample = 0; sample < HashSize; sample++)
                {
                    table[frequency, sample] = Math.Cos(((2 * sample + 1) * frequency * Math.PI) / (2 * HashSize));
                }
            }

            return table;
        }

        private sealed class ImageSearchCandidate
        {
            public Wz_Node Node { get; set; }
            public string SourceInputPath { get; set; }
            public string SourceRootPath { get; set; }
            public string SourceScope { get; set; }
            public int Page { get; set; }
            public ImageSearchMatchDto Match { get; set; }
        }

        private sealed class ImageSearchScore
        {
            public double Score { get; set; }
            public double PerceptualScore { get; set; }
            public double PixelScore { get; set; }
            public double ColorScore { get; set; }
            public double ShapeScore { get; set; }
            public int HashDistance { get; set; }
            public string QueryRegion { get; set; }
            public string Region { get; set; }
        }

        private sealed class ImageFingerprint
        {
            private ImageFingerprint()
            {
            }

            public ulong Hash { get; private set; }
            public double AverageR { get; private set; }
            public double AverageG { get; private set; }
            public double AverageB { get; private set; }
            public double AspectRatio { get; private set; }
            public double AlphaCoverage { get; private set; }
            public double[] Grayscale { get; private set; }
            public string Region { get; private set; }

            public static ImageFingerprint Create(RgbaImage image, int minAlpha)
            {
                ImageRect full = new ImageRect(0, 0, image.Width, image.Height);
                ImageRect bounds = FindAlphaBounds(image, full, minAlpha) ?? full;
                return Create(image, bounds, minAlpha, "full");
            }

            public static IEnumerable<ImageFingerprint> CreateSearchVariants(RgbaImage image, int minAlpha)
            {
                return CreateCandidateVariants(image, minAlpha);
            }

            public static IEnumerable<ImageFingerprint> CreateCandidateVariants(RgbaImage image, int minAlpha)
            {
                ImageRect full = new ImageRect(0, 0, image.Width, image.Height);
                ImageRect bounds = FindAlphaBounds(image, full, minAlpha) ?? full;
                foreach (var item in CreateVariantRects(bounds))
                {
                    ImageRect visible = FindAlphaBounds(image, item.Rect, minAlpha) ?? item.Rect;
                    if (visible.Width <= 0 || visible.Height <= 0)
                    {
                        continue;
                    }

                    yield return Create(image, visible, minAlpha, item.Name);
                }
            }

            private static IEnumerable<NamedRect> CreateVariantRects(ImageRect bounds)
            {
                yield return new NamedRect("full", bounds);
                int halfWidth = Math.Max(1, bounds.Width / 2);
                int halfHeight = Math.Max(1, bounds.Height / 2);
                yield return new NamedRect("left-half", new ImageRect(bounds.X, bounds.Y, halfWidth, bounds.Height));
                yield return new NamedRect("right-half", new ImageRect(bounds.X + bounds.Width - halfWidth, bounds.Y, halfWidth, bounds.Height));
                yield return new NamedRect("top-half", new ImageRect(bounds.X, bounds.Y, bounds.Width, halfHeight));
                yield return new NamedRect("bottom-half", new ImageRect(bounds.X, bounds.Y + bounds.Height - halfHeight, bounds.Width, halfHeight));
                int centerWidth = Math.Max(1, (int)Math.Round(bounds.Width * 0.65));
                int centerHeight = Math.Max(1, (int)Math.Round(bounds.Height * 0.65));
                yield return new NamedRect("center", new ImageRect(
                    bounds.X + (bounds.Width - centerWidth) / 2,
                    bounds.Y + (bounds.Height - centerHeight) / 2,
                    centerWidth,
                    centerHeight));
            }

            private static ImageFingerprint Create(RgbaImage image, ImageRect rect, int minAlpha, string region)
            {
                double[] grayscale = ResizeToGrayscale(image, rect);
                var color = AverageVisibleColor(image, rect, minAlpha);
                return new ImageFingerprint
                {
                    Hash = ComputePHash(grayscale),
                    Grayscale = grayscale,
                    AverageR = color.R,
                    AverageG = color.G,
                    AverageB = color.B,
                    AspectRatio = rect.Height == 0 ? 1.0 : (double)rect.Width / rect.Height,
                    AlphaCoverage = ComputeAlphaCoverage(image, rect, minAlpha),
                    Region = region
                };
            }

            private static ImageRect? FindAlphaBounds(RgbaImage image, ImageRect rect, int minAlpha)
            {
                int minX = rect.X + rect.Width;
                int minY = rect.Y + rect.Height;
                int maxX = rect.X - 1;
                int maxY = rect.Y - 1;

                for (int y = rect.Y; y < rect.Y + rect.Height; y++)
                {
                    for (int x = rect.X; x < rect.X + rect.Width; x++)
                    {
                        if (image.GetAlpha(x, y) <= minAlpha)
                        {
                            continue;
                        }

                        if (x < minX)
                        {
                            minX = x;
                        }
                        if (x > maxX)
                        {
                            maxX = x;
                        }
                        if (y < minY)
                        {
                            minY = y;
                        }
                        if (y > maxY)
                        {
                            maxY = y;
                        }
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    return null;
                }

                return new ImageRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }

            private static double[] ResizeToGrayscale(RgbaImage image, ImageRect rect)
            {
                var result = new double[HashSize * HashSize];
                for (int y = 0; y < HashSize; y++)
                {
                    double sourceY = rect.Y + (y + 0.5) * rect.Height / HashSize - 0.5;
                    for (int x = 0; x < HashSize; x++)
                    {
                        double sourceX = rect.X + (x + 0.5) * rect.Width / HashSize - 0.5;
                        result[y * HashSize + x] = image.SampleLumaOnWhite(sourceX, sourceY);
                    }
                }

                return result;
            }

            private static AverageColor AverageVisibleColor(RgbaImage image, ImageRect rect, int minAlpha)
            {
                long r = 0;
                long g = 0;
                long b = 0;
                long count = 0;
                for (int y = rect.Y; y < rect.Y + rect.Height; y++)
                {
                    for (int x = rect.X; x < rect.X + rect.Width; x++)
                    {
                        int offset = image.GetOffset(x, y);
                        int alpha = image.Pixels[offset + 3];
                        if (alpha <= minAlpha)
                        {
                            continue;
                        }

                        r += image.Pixels[offset];
                        g += image.Pixels[offset + 1];
                        b += image.Pixels[offset + 2];
                        count++;
                    }
                }

                if (count == 0)
                {
                    return new AverageColor(255, 255, 255);
                }

                return new AverageColor((double)r / count, (double)g / count, (double)b / count);
            }

            private static double ComputeAlphaCoverage(RgbaImage image, ImageRect rect, int minAlpha)
            {
                int visible = 0;
                int total = rect.Width * rect.Height;
                if (total == 0)
                {
                    return 0;
                }

                for (int y = rect.Y; y < rect.Y + rect.Height; y++)
                {
                    for (int x = rect.X; x < rect.X + rect.Width; x++)
                    {
                        if (image.GetAlpha(x, y) > minAlpha)
                        {
                            visible++;
                        }
                    }
                }

                return (double)visible / total;
            }
        }

        private sealed class RgbaImage
        {
            public RgbaImage(int width, int height, byte[] pixels)
            {
                this.Width = width;
                this.Height = height;
                this.Pixels = pixels;
            }

            public int Width { get; private set; }
            public int Height { get; private set; }
            public byte[] Pixels { get; private set; }

            public int GetOffset(int x, int y)
            {
                return (y * this.Width + x) * 4;
            }

            public int GetAlpha(int x, int y)
            {
                return this.Pixels[GetOffset(x, y) + 3];
            }

            public double SampleLumaOnWhite(double x, double y)
            {
                int x0 = Clamp((int)Math.Floor(x), 0, this.Width - 1);
                int y0 = Clamp((int)Math.Floor(y), 0, this.Height - 1);
                int x1 = Clamp(x0 + 1, 0, this.Width - 1);
                int y1 = Clamp(y0 + 1, 0, this.Height - 1);
                double tx = x - Math.Floor(x);
                double ty = y - Math.Floor(y);

                double a = SampleLumaNearestOnWhite(x0, y0);
                double b = SampleLumaNearestOnWhite(x1, y0);
                double c = SampleLumaNearestOnWhite(x0, y1);
                double d = SampleLumaNearestOnWhite(x1, y1);
                return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
            }

            private double SampleLumaNearestOnWhite(int x, int y)
            {
                int offset = GetOffset(x, y);
                double alpha = this.Pixels[offset + 3] / 255.0;
                double r = this.Pixels[offset] * alpha + 255 * (1 - alpha);
                double g = this.Pixels[offset + 1] * alpha + 255 * (1 - alpha);
                double b = this.Pixels[offset + 2] * alpha + 255 * (1 - alpha);
                return 0.299 * r + 0.587 * g + 0.114 * b;
            }

            private static double Lerp(double a, double b, double t)
            {
                return a + (b - a) * t;
            }

            private static int Clamp(int value, int min, int max)
            {
                if (value < min)
                {
                    return min;
                }
                if (value > max)
                {
                    return max;
                }
                return value;
            }
        }

        private readonly struct ImageRect
        {
            public ImageRect(int x, int y, int width, int height)
            {
                this.X = x;
                this.Y = y;
                this.Width = width;
                this.Height = height;
            }

            public int X { get; }
            public int Y { get; }
            public int Width { get; }
            public int Height { get; }
        }

        private readonly struct NamedRect
        {
            public NamedRect(string name, ImageRect rect)
            {
                this.Name = name;
                this.Rect = rect;
            }

            public string Name { get; }
            public ImageRect Rect { get; }
        }

        private readonly struct AverageColor
        {
            public AverageColor(double r, double g, double b)
            {
                this.R = r;
                this.G = g;
                this.B = b;
            }

            public double R { get; }
            public double G { get; }
            public double B { get; }
        }

        private static class PngFileReader
        {
            private static readonly byte[] Signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

            public static RgbaImage Load(string path)
            {
                string fullPath = Path.GetFullPath(path);
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes.Length < Signature.Length || !Signature.SequenceEqual(bytes.Take(Signature.Length)))
                {
                    throw new UsageException("Query image must be a PNG file: " + path);
                }

                int width = 0;
                int height = 0;
                int bitDepth = 0;
                int colorType = 0;
                int interlace = 0;
                byte[] palette = null;
                byte[] transparency = null;
                using (var idat = new MemoryStream())
                {
                    int offset = Signature.Length;
                    while (offset + 12 <= bytes.Length)
                    {
                        int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
                        offset += 4;
                        string type = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                        offset += 4;
                        if (length < 0 || offset + length + 4 > bytes.Length)
                        {
                            throw new UsageException("Invalid PNG chunk length in query image.");
                        }

                        ReadOnlySpan<byte> data = bytes.AsSpan(offset, length);
                        offset += length + 4;

                        if (type == "IHDR")
                        {
                            width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0, 4));
                            height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                            bitDepth = data[8];
                            colorType = data[9];
                            interlace = data[12];
                        }
                        else if (type == "PLTE")
                        {
                            palette = data.ToArray();
                        }
                        else if (type == "tRNS")
                        {
                            transparency = data.ToArray();
                        }
                        else if (type == "IDAT")
                        {
                            idat.Write(data);
                        }
                        else if (type == "IEND")
                        {
                            break;
                        }
                    }

                    return DecodeImage(width, height, bitDepth, colorType, interlace, palette, transparency, idat.ToArray());
                }
            }

            private static RgbaImage DecodeImage(int width, int height, int bitDepth, int colorType, int interlace, byte[] palette, byte[] transparency, byte[] compressed)
            {
                if (width <= 0 || height <= 0)
                {
                    throw new UsageException("Query PNG has invalid dimensions.");
                }
                if (bitDepth != 8)
                {
                    throw new UsageException("Query PNG must use 8-bit channels.");
                }
                if (interlace != 0)
                {
                    throw new UsageException("Interlaced query PNG is not supported.");
                }

                int bytesPerPixel = BytesPerPixel(colorType);
                byte[] raw = Inflate(compressed);
                byte[] rgba = new byte[width * height * 4];
                byte[] previous = new byte[width * bytesPerPixel];
                byte[] current = new byte[width * bytesPerPixel];
                int source = 0;
                for (int y = 0; y < height; y++)
                {
                    if (source >= raw.Length)
                    {
                        throw new UsageException("Query PNG pixel data is truncated.");
                    }

                    byte filter = raw[source++];
                    if (source + current.Length > raw.Length)
                    {
                        throw new UsageException("Query PNG scanline is truncated.");
                    }

                    Buffer.BlockCopy(raw, source, current, 0, current.Length);
                    source += current.Length;
                    Unfilter(current, previous, bytesPerPixel, filter);
                    CopyScanlineToRgba(current, rgba, y * width * 4, width, colorType, palette, transparency);

                    byte[] tmp = previous;
                    previous = current;
                    current = tmp;
                }

                return new RgbaImage(width, height, rgba);
            }

            private static int BytesPerPixel(int colorType)
            {
                switch (colorType)
                {
                    case 0:
                    case 3:
                        return 1;
                    case 2:
                        return 3;
                    case 4:
                        return 2;
                    case 6:
                        return 4;
                    default:
                        throw new UsageException("Unsupported query PNG color type: " + colorType);
                }
            }

            private static byte[] Inflate(byte[] compressed)
            {
                using (var input = new MemoryStream(compressed))
                using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    zlib.CopyTo(output);
                    return output.ToArray();
                }
            }

            private static void Unfilter(byte[] current, byte[] previous, int bytesPerPixel, byte filter)
            {
                for (int i = 0; i < current.Length; i++)
                {
                    int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                    int up = previous[i];
                    int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                    int value;
                    switch (filter)
                    {
                        case 0:
                            value = current[i];
                            break;
                        case 1:
                            value = current[i] + left;
                            break;
                        case 2:
                            value = current[i] + up;
                            break;
                        case 3:
                            value = current[i] + ((left + up) >> 1);
                            break;
                        case 4:
                            value = current[i] + Paeth(left, up, upLeft);
                            break;
                        default:
                            throw new UsageException("Unsupported query PNG filter type: " + filter);
                    }

                    current[i] = (byte)(value & 0xff);
                }
            }

            private static int Paeth(int a, int b, int c)
            {
                int p = a + b - c;
                int pa = Math.Abs(p - a);
                int pb = Math.Abs(p - b);
                int pc = Math.Abs(p - c);
                if (pa <= pb && pa <= pc)
                {
                    return a;
                }
                return pb <= pc ? b : c;
            }

            private static void CopyScanlineToRgba(byte[] row, byte[] rgba, int destination, int width, int colorType, byte[] palette, byte[] transparency)
            {
                int source = 0;
                for (int x = 0; x < width; x++)
                {
                    switch (colorType)
                    {
                        case 0:
                            rgba[destination] = row[source];
                            rgba[destination + 1] = row[source];
                            rgba[destination + 2] = row[source];
                            rgba[destination + 3] = 255;
                            source++;
                            break;
                        case 2:
                            rgba[destination] = row[source];
                            rgba[destination + 1] = row[source + 1];
                            rgba[destination + 2] = row[source + 2];
                            rgba[destination + 3] = 255;
                            source += 3;
                            break;
                        case 3:
                            int index = row[source++];
                            if (palette == null || index * 3 + 2 >= palette.Length)
                            {
                                throw new UsageException("Query PNG palette is missing or invalid.");
                            }

                            rgba[destination] = palette[index * 3];
                            rgba[destination + 1] = palette[index * 3 + 1];
                            rgba[destination + 2] = palette[index * 3 + 2];
                            rgba[destination + 3] = transparency != null && index < transparency.Length ? transparency[index] : (byte)255;
                            break;
                        case 4:
                            rgba[destination] = row[source];
                            rgba[destination + 1] = row[source];
                            rgba[destination + 2] = row[source];
                            rgba[destination + 3] = row[source + 1];
                            source += 2;
                            break;
                        case 6:
                            rgba[destination] = row[source];
                            rgba[destination + 1] = row[source + 1];
                            rgba[destination + 2] = row[source + 2];
                            rgba[destination + 3] = row[source + 3];
                            source += 4;
                            break;
                    }

                    destination += 4;
                }
            }
        }
    }
}
