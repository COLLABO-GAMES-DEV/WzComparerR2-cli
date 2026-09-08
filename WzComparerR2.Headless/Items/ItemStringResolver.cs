using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless
{
    internal static class ItemStringResolver
    {
        public static ItemStringMatch ResolveByName(string stringInput, string name, ParsedArgs args)
        {
            if (string.IsNullOrEmpty(stringInput))
            {
                throw new UsageException("item icon --name requires --string-wz <file-or-dir>, --data-dir <dir>, or an input path that can infer a sibling String folder.");
            }

            using (var context = WzLoadContext.Load(stringInput, WzLoadOptions.FromArgs(args)))
            {
                return ResolveByName(context, name);
            }
        }

        internal static ItemStringMatch ResolveByName(WzLoadContext context, string name)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var exactMatches = FindMatches(context, match =>
                string.Equals(match.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ItemIconPaths.CanonicalizeItemName(match.Name), ItemIconPaths.CanonicalizeItemName(name), StringComparison.OrdinalIgnoreCase));
            if (exactMatches.Count == 1)
            {
                return exactMatches[0];
            }
            if (exactMatches.Count > 1)
            {
                throw new UsageException("item icon --name matched multiple item ids: " + string.Join(", ", exactMatches.Select(item => item.Id + " " + item.Path)));
            }

            var containsMatches = FindMatches(context, match =>
                match.Name != null && match.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            string hint = containsMatches.Count == 0
                ? string.Empty
                : " Similar names: " + string.Join(", ", containsMatches.Take(10).Select(item => item.Id + "=" + item.Name));
            throw new UsageException("item icon --name not found in String data: " + name + "." + hint);
        }

        public static ItemStringMatch TryResolveById(string stringInput, string id, ParsedArgs args)
        {
            using (var context = WzLoadContext.Load(stringInput, WzLoadOptions.FromArgs(args)))
            {
                return TryResolveById(context, id);
            }
        }

        internal static ItemStringMatch TryResolveById(WzLoadContext context, string id)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var matches = FindMatches(context, match =>
                string.Equals(match.Id, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(match.Id, ItemIconPaths.PadItemId(id), StringComparison.OrdinalIgnoreCase));
            return matches.FirstOrDefault();
        }

        private static List<ItemStringMatch> FindMatches(WzLoadContext context, Func<ItemStringMatch, bool> predicate)
        {
            var matches = new List<ItemStringMatch>();
            foreach (Wz_Node node in Traverse(context.Root))
            {
                if (!string.Equals(node.Text, "name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = NodeDto.FormatValue(node.Value);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var itemNode = node.ParentNode;
                var imageNode = itemNode == null ? null : itemNode.ParentNode;
                if (imageNode == null || string.IsNullOrEmpty(imageNode.Text) || !imageNode.Text.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = new ItemStringMatch
                {
                    Id = itemNode.Text,
                    Name = value,
                    Category = ItemIconPaths.NormalizeCategory(Path.GetFileNameWithoutExtension(imageNode.Text)),
                    Path = itemNode.FullPath,
                    InputPath = context.InputPath
                };

                if (predicate(match))
                {
                    matches.Add(match);
                }
            }

            return matches;
        }

        private static IEnumerable<Wz_Node> Traverse(Wz_Node root)
        {
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
    }
}
