using System;
using System.IO;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless.Wz
{
    public sealed class HeadlessWzLoadOptions
    {
        public bool UseBaseWz { get; set; }
        public string FallbackPath { get; set; }
    }

    internal sealed class HeadlessWzLoadContext : IDisposable
    {
        private HeadlessWzLoadContext(string inputPath, Wz_Structure structure)
        {
            this.InputPath = inputPath;
            this.Structure = structure;
        }

        public string InputPath { get; private set; }
        public Wz_Structure Structure { get; private set; }
        public Wz_Node Root { get { return this.Structure.WzNode; } }

        public static HeadlessWzLoadContext Load(string inputPath, HeadlessWzLoadOptions options)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("Input path is required.", nameof(inputPath));
            }

            options = options ?? new HeadlessWzLoadOptions();
            string fullPath = Path.GetFullPath(inputPath);
            var structure = new Wz_Structure();

            try
            {
                if (Directory.Exists(fullPath))
                {
                    Wz_Node node = null;
                    structure.LoadWzFolder(fullPath, ref node, options.UseBaseWz, options.FallbackPath);
                    structure.WzNode = node;
                    structure.calculate_img_count();
                }
                else if (File.Exists(fullPath))
                {
                    string ext = Path.GetExtension(fullPath);
                    if (string.Equals(ext, ".img", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.LoadImg(fullPath);
                    }
                    else if (string.Equals(ext, ".ms", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.LoadMsFile(fullPath);
                    }
                    else if (!string.IsNullOrEmpty(options.FallbackPath)
                        && !string.Equals(Path.GetFileName(fullPath), "list.wz", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.WzNode = new Wz_Node(Path.GetFileName(fullPath));
                        structure.LoadFile(fullPath, structure.WzNode, options.UseBaseWz, false, options.FallbackPath);
                        structure.calculate_img_count();
                    }
                    else
                    {
                        structure.Load(fullPath, options.UseBaseWz);
                    }
                }
                else
                {
                    throw new FileNotFoundException("Input path not found: " + inputPath);
                }

                if (structure.WzNode == null)
                {
                    throw new InvalidOperationException("No root node was loaded from: " + inputPath);
                }

                return new HeadlessWzLoadContext(fullPath, structure);
            }
            catch
            {
                structure.Clear();
                throw;
            }
        }

        public void Dispose()
        {
            this.Structure.Clear();
        }
    }

    internal static class HeadlessNodePath
    {
        public static Wz_Node Resolve(Wz_Node root, string path, bool extractImages)
        {
            if (root == null)
            {
                return null;
            }

            Wz_Node current = ExtractImageNode(root, extractImages);
            if (string.IsNullOrWhiteSpace(path))
            {
                return current;
            }

            string[] parts = path
                .Replace('/', '\\')
                .Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            int start = 0;
            if (parts.Length > 0 && string.Equals(parts[0], current.Text, StringComparison.OrdinalIgnoreCase))
            {
                start = 1;
            }

            for (int i = start; i < parts.Length; i++)
            {
                current = ExtractImageNode(current, extractImages);
                if (current == null)
                {
                    return null;
                }

                Wz_Node child = current.Nodes[parts[i]];
                if (child == null)
                {
                    foreach (Wz_Node candidate in current.Nodes)
                    {
                        if (string.Equals(candidate.Text, parts[i], StringComparison.OrdinalIgnoreCase))
                        {
                            child = candidate;
                            break;
                        }
                    }
                }

                if (child == null)
                {
                    return null;
                }

                current = child;
            }

            return ExtractImageNode(current, extractImages);
        }

        public static Wz_Node ExtractImageNode(Wz_Node node, bool extractImages)
        {
            if (!extractImages || node == null)
            {
                return node;
            }

            Wz_Image image = node.GetValue<Wz_Image>();
            if (image != null && image.TryExtract())
            {
                return image.Node;
            }

            return node;
        }
    }
}
