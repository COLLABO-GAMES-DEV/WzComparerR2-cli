using System;
using System.Collections.Generic;
using System.IO;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless
{
    internal static class ItemIconExporter
    {
        public static ItemIconExportResultDto Export(ParsedArgs args, string outputDirectory)
        {
            return Export(args, outputDirectory, (input, options) => new ItemIconLoadLease(WzLoadContext.Load(input, options), true));
        }

        internal static ItemIconExportResultDto Export(
            ParsedArgs args,
            string outputDirectory,
            Func<string, WzLoadOptions, ItemIconLoadLease> loadContext)
        {
            if (loadContext == null)
            {
                throw new ArgumentNullException(nameof(loadContext));
            }

            string id = args.GetValue("id");
            string name = args.GetValue("name");
            string dataDir = args.GetValue("data-dir");
            string itemInput = ItemIconPaths.ResolveItemInput(args, dataDir);
            string stringInput = ItemIconPaths.ResolveStringInput(args, dataDir, itemInput);
            string category = ItemIconPaths.NormalizeCategory(args.GetValue("category"));
            WzLoadOptions loadOptions = WzLoadOptions.FromArgs(args);

            ItemStringMatch selectedString = null;
            var diagnostics = new List<string>();

            if (!string.IsNullOrEmpty(name))
            {
                if (string.IsNullOrEmpty(stringInput))
                {
                    throw new UsageException("item icon --name requires --string-wz <file-or-dir>, --data-dir <dir>, or an input path that can infer a sibling String folder.");
                }

                using (ItemIconLoadLease stringContext = loadContext(stringInput, loadOptions))
                {
                    selectedString = ItemStringResolver.ResolveByName(stringContext.Context, name);
                    id = selectedString.Id;
                    category = category ?? selectedString.Category;
                }
            }

            if (selectedString == null && !string.IsNullOrEmpty(stringInput))
            {
                using (ItemIconLoadLease stringContext = loadContext(stringInput, loadOptions))
                {
                    selectedString = ItemStringResolver.TryResolveById(stringContext.Context, id);
                    if (selectedString != null)
                    {
                        category = category ?? selectedString.Category;
                    }
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

            using (ItemIconLoadLease context = loadContext(canvasInput, loadOptions))
            {
                Wz_Node iconNode = null;
                string matchedPath = null;
                foreach (string iconPath in iconPaths)
                {
                    iconNode = NodePath.Resolve(context.Context.Root, iconPath, true);
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
                    CanvasInputPath = context.Context.InputPath,
                    IconPath = iconNode.FullPath,
                    RequestedIconPath = matchedPath,
                    OutputDirectory = Path.GetFullPath(outputDirectory),
                    Files = files,
                    Diagnostics = diagnostics
                };
            }
        }

        internal sealed class ItemIconLoadLease : IDisposable
        {
            private readonly bool ownsContext;

            public ItemIconLoadLease(WzLoadContext context, bool ownsContext)
            {
                if (context == null)
                {
                    throw new ArgumentNullException(nameof(context));
                }

                this.Context = context;
                this.ownsContext = ownsContext;
            }

            public WzLoadContext Context { get; private set; }

            public void Dispose()
            {
                if (ownsContext && Context != null)
                {
                    Context.Dispose();
                }
                Context = null;
            }
        }
    }
}
