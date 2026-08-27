using System;
using System.Collections.Generic;
using System.IO;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static class ItemIconExporter
    {
        public static ItemIconExportResultDto Export(ParsedArgs args, string outputDirectory)
        {
            string id = args.GetValue("id");
            string name = args.GetValue("name");
            string dataDir = args.GetValue("data-dir");
            string itemInput = ItemIconPaths.ResolveItemInput(args, dataDir);
            string stringInput = ItemIconPaths.ResolveStringInput(args, dataDir, itemInput);
            string category = ItemIconPaths.NormalizeCategory(args.GetValue("category"));

            ItemStringMatch selectedString = null;
            var diagnostics = new List<string>();

            if (!string.IsNullOrEmpty(name))
            {
                selectedString = ItemStringResolver.ResolveByName(stringInput, name, args);
                id = selectedString.Id;
                category = category ?? selectedString.Category;
            }

            if (selectedString == null && !string.IsNullOrEmpty(stringInput))
            {
                selectedString = ItemStringResolver.TryResolveById(stringInput, id, args);
                if (selectedString != null)
                {
                    category = category ?? selectedString.Category;
                }
            }

            category = category ?? ItemIconPaths.InferCategoryFromId(id);
            if (string.Equals(category, "eqp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "equip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "character", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException("item icon currently supports Item/_Canvas categories, not equipment Character icons. Provide a Cash/Consume/Install/Etc/Pet item id.");
            }

            string canvasInput = ItemIconPaths.ResolveCanvasInput(args, dataDir, itemInput, category);
            if (string.IsNullOrEmpty(canvasInput))
            {
                throw new UsageException("item icon could not find an Item/" + ItemIconPaths.ToFolderName(category) + "/_Canvas input. Provide --canvas-wz <file-or-dir>, --item-wz <path>, or --data-dir <dir>.");
            }

            string paddedId = ItemIconPaths.PadItemId(id);
            List<string> paddedIdCandidates = ItemIconPaths.BuildPaddedIdCandidates(id);
            List<string> iconPaths = ItemIconPaths.BuildIconPathCandidates(paddedIdCandidates);

            using (var context = WzLoadContext.Load(canvasInput, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node iconNode = null;
                string matchedPath = null;
                foreach (string iconPath in iconPaths)
                {
                    iconNode = NodePath.Resolve(context.Root, iconPath, true);
                    if (iconNode != null && iconNode.Value is Wz_Png)
                    {
                        matchedPath = iconPath;
                        break;
                    }
                }

                if (iconNode == null)
                {
                    throw new UsageException("item icon not found for id " + id + " under " + canvasInput + ". Tried: " + string.Join(", ", iconPaths));
                }
                if (!matchedPath.Contains(paddedId, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add("Exact icon id " + paddedId + " was not found; exported fallback icon path " + matchedPath + ".");
                }

                var files = ExtractExporter.ExportMedia(iconNode, outputDirectory, false, "image");
                if (files.Count == 0)
                {
                    throw new UsageException("item icon node was found but no PNG was exported: " + iconNode.FullPath);
                }

                return new ItemIconExportResultDto
                {
                    QueryName = name,
                    Id = id,
                    PaddedId = paddedId,
                    Name = selectedString == null ? null : selectedString.Name,
                    Category = category,
                    StringPath = selectedString == null ? null : selectedString.Path,
                    StringInputPath = selectedString == null ? null : selectedString.InputPath,
                    CanvasInputPath = context.InputPath,
                    IconPath = iconNode.FullPath,
                    RequestedIconPath = matchedPath,
                    OutputDirectory = Path.GetFullPath(outputDirectory),
                    Files = files,
                    Diagnostics = diagnostics
                };
            }
        }
    }
}
