using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal sealed class MediaListResultDto
    {
        public string InputPath { get; set; }
        public string RootPath { get; set; }
        public string Type { get; set; }
        public int Count { get { return this.Assets == null ? 0 : this.Assets.Count; } }
        public int MaxResults { get; set; }
        public bool Truncated { get; set; }
        public List<MediaAssetDto> Assets { get; set; }

        public static MediaListResultDto Create(string inputPath, Wz_Node root, string kind, int maxResults)
        {
            return new MediaListResultDto
            {
                InputPath = Path.GetFullPath(inputPath),
                RootPath = root.FullPath,
                Type = kind,
                MaxResults = maxResults,
                Assets = new List<MediaAssetDto>()
            };
        }
    }

    internal sealed class MediaAssetDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Format { get; set; }
        public int? Pages { get; set; }
        public int? DataLength { get; set; }
        public int? Ms { get; set; }
        public int? Channels { get; set; }
        public int? Frequency { get; set; }
        public string SoundType { get; set; }

        public static MediaAssetDto FromNode(Wz_Node node)
        {
            var dto = new MediaAssetDto
            {
                Name = node.Text,
                Path = node.FullPath,
                Type = NodeDto.GetTypeName(node.Value),
                Value = NodeDto.FormatValue(node.Value)
            };

            var png = node.Value as Wz_Png;
            if (png != null)
            {
                dto.Width = png.Width;
                dto.Height = png.Height;
                dto.Format = png.Format.ToString();
                dto.Pages = png.ActualPages;
                return dto;
            }

            var sound = node.Value as Wz_Sound;
            if (sound != null)
            {
                dto.DataLength = sound.DataLength;
                dto.Ms = sound.Ms;
                dto.Channels = sound.Channels;
                dto.Frequency = sound.Frequency;
                dto.SoundType = sound.SoundType.ToString();
            }

            return dto;
        }
    }

    internal static class MediaAssetFinder
    {
        public static List<MediaAssetDto> Find(Wz_Node root, string kind, int maxResults, out bool truncated)
        {
            var assets = new List<MediaAssetDto>();
            int limit = maxResults <= 0 ? int.MaxValue : maxResults;
            truncated = false;

            foreach (Wz_Node node in Traverse(root))
            {
                if (!MatchesKind(node, kind))
                {
                    continue;
                }

                if (assets.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                assets.Add(MediaAssetDto.FromNode(node));
            }

            return assets;
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

                var children = node.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
        }

        private static bool MatchesKind(Wz_Node node, string kind)
        {
            if (string.Equals(kind, "sound", StringComparison.OrdinalIgnoreCase))
            {
                return node.Value is Wz_Sound;
            }
            if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
            {
                return node.Value is Wz_Png;
            }

            return false;
        }
    }
}
