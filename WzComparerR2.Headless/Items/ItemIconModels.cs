using System.Collections.Generic;

namespace WzComparerR2.Headless
{
    internal sealed class ItemIconExportResultDto
    {
        public string QueryName { get; set; }
        public string Id { get; set; }
        public string PaddedId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string StringPath { get; set; }
        public string StringInputPath { get; set; }
        public string CanvasInputPath { get; set; }
        public string RequestedIconPath { get; set; }
        public string IconPath { get; set; }
        public string OutputDirectory { get; set; }
        public string ManifestPath { get; set; }
        public List<ExtractedFileDto> Files { get; set; }
        public List<string> Diagnostics { get; set; }

        public ExtractResultDto ToExtractResult()
        {
            return new ExtractResultDto
            {
                InputPath = this.CanvasInputPath,
                SourcePath = this.IconPath,
                ManifestPath = this.ManifestPath,
                Files = this.Files ?? new List<ExtractedFileDto>()
            };
        }
    }

    internal sealed class ItemStringMatch
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Path { get; set; }
        public string InputPath { get; set; }
    }
}
