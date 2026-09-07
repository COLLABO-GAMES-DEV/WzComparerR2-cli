using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace WzComparerR2.Headless.Agent
{
    internal sealed class XlsxSkillRecipeOptions
    {
        public string SheetName { get; set; }
        public string JobNameColumn { get; set; }
        public string JobCodeColumn { get; set; }
        public List<string> SkillColumns { get; set; }
        public int HeaderRow { get; set; }
        public int FirstDataRow { get; set; }
        public int MaxRows { get; set; }
    }

    internal sealed class XlsxSkillRecipeResultDto
    {
        public string WorkbookPath { get; set; }
        public string SheetName { get; set; }
        public int HeaderRow { get; set; }
        public int FirstDataRow { get; set; }
        public string JobNameColumn { get; set; }
        public string JobCodeColumn { get; set; }
        public List<string> SkillColumns { get; set; }
        public int RequestCount { get; set; }
        public List<string> Diagnostics { get; set; }
        public List<XlsxSkillRecipeRequestDto> Requests { get; set; }
    }

    internal sealed class XlsxSkillRecipeRequestDto
    {
        public string JobName { get; set; }
        public string JobCode { get; set; }
        public string Name { get; set; }
        public string SourceCell { get; set; }
    }

    internal static class XlsxSkillRecipeReader
    {
        private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace OfficeRelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static XlsxSkillRecipeResultDto Read(string workbookPath, XlsxSkillRecipeOptions options)
        {
            if (string.IsNullOrWhiteSpace(workbookPath))
            {
                throw new UsageException("skill.export-xlsx requires xlsx, xlsxFile, or workbook.");
            }
            if (!File.Exists(workbookPath))
            {
                throw new FileNotFoundException("XLSX workbook not found: " + workbookPath);
            }

            options = options ?? new XlsxSkillRecipeOptions();
            using (ZipArchive archive = ZipFile.OpenRead(workbookPath))
            {
                var sharedStrings = LoadSharedStrings(archive);
                var worksheet = ResolveWorksheet(archive, options.SheetName);
                var rows = LoadRows(worksheet.Entry, sharedStrings);
                if (rows.Count == 0)
                {
                    throw new UsageException("skill.export-xlsx worksheet has no rows: " + worksheet.Name);
                }

                var diagnostics = new List<string>();
                XlsxRow header = ResolveHeaderRow(rows, options);
                string jobNameColumn = NormalizeColumn(options.JobNameColumn);
                string jobCodeColumn = NormalizeColumn(options.JobCodeColumn);
                var skillColumns = NormalizeColumns(options.SkillColumns);

                if (header != null)
                {
                    if (string.IsNullOrWhiteSpace(jobNameColumn))
                    {
                        jobNameColumn = DetectColumn(header, IsJobNameHeader);
                    }
                    if (string.IsNullOrWhiteSpace(jobCodeColumn))
                    {
                        jobCodeColumn = DetectColumn(header, IsJobCodeHeader);
                    }
                    if (skillColumns.Count == 0)
                    {
                        skillColumns = DetectColumns(header, IsSkillHeader);
                    }
                }

                if (skillColumns.Count == 0)
                {
                    throw new UsageException("skill.export-xlsx could not detect skill columns; set skillColumns.");
                }

                int headerRowNumber = header == null ? 0 : header.RowNumber;
                int firstDataRow = options.FirstDataRow > 0
                    ? options.FirstDataRow
                    : (headerRowNumber > 0 ? headerRowNumber + 1 : 1);

                var result = new XlsxSkillRecipeResultDto
                {
                    WorkbookPath = Path.GetFullPath(workbookPath),
                    SheetName = worksheet.Name,
                    HeaderRow = headerRowNumber,
                    FirstDataRow = firstDataRow,
                    JobNameColumn = jobNameColumn,
                    JobCodeColumn = jobCodeColumn,
                    SkillColumns = skillColumns,
                    Diagnostics = diagnostics,
                    Requests = new List<XlsxSkillRecipeRequestDto>()
                };

                diagnostics.Add("XLSX sheet: " + worksheet.Name);
                diagnostics.Add("XLSX columns: jobName=" + (jobNameColumn ?? string.Empty)
                    + " jobCode=" + (jobCodeColumn ?? string.Empty)
                    + " skill=" + string.Join(",", skillColumns));

                string currentJobName = null;
                string currentJobCode = null;
                int visitedRows = 0;
                foreach (XlsxRow row in rows.Where(item => item.RowNumber >= firstDataRow).OrderBy(item => item.RowNumber))
                {
                    visitedRows++;
                    if (options.MaxRows > 0 && visitedRows > options.MaxRows)
                    {
                        diagnostics.Add("XLSX row scan truncated to " + options.MaxRows + " data rows.");
                        break;
                    }

                    string rowJobName = ReadCell(row, jobNameColumn);
                    string rowJobCode = ReadCell(row, jobCodeColumn);
                    if (!string.IsNullOrWhiteSpace(rowJobName))
                    {
                        currentJobName = rowJobName;
                    }
                    if (!string.IsNullOrWhiteSpace(rowJobCode))
                    {
                        currentJobCode = rowJobCode;
                    }

                    foreach (string column in skillColumns)
                    {
                        foreach (string skillName in SplitSkillNames(ReadCell(row, column)))
                        {
                            if (IsIgnorableSkillValue(skillName))
                            {
                                continue;
                            }

                            result.Requests.Add(new XlsxSkillRecipeRequestDto
                            {
                                JobName = currentJobName,
                                JobCode = currentJobCode,
                                Name = skillName,
                                SourceCell = column + row.RowNumber.ToString()
                            });
                        }
                    }
                }

                result.RequestCount = result.Requests.Count;
                if (result.RequestCount == 0)
                {
                    throw new UsageException("skill.export-xlsx produced no skill requests from worksheet: " + worksheet.Name);
                }

                return result;
            }
        }

        public static void WriteNamesFile(XlsxSkillRecipeResultDto recipe, string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var lines = new List<string>();
            lines.Add("# jobName\tjobCode\tskillName\trelativeOutput");
            foreach (XlsxSkillRecipeRequestDto request in recipe.Requests)
            {
                lines.Add(CleanTsv(request.JobName) + "\t"
                    + CleanTsv(request.JobCode) + "\t"
                    + CleanTsv(request.Name) + "\t");
            }
            File.WriteAllLines(path, lines);
        }

        private static List<string> LoadSharedStrings(ZipArchive archive)
        {
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return new List<string>();
            }

            XDocument document = LoadXml(entry);
            return document.Descendants(SpreadsheetNs + "si")
                .Select(si => string.Concat(si.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
                .ToList();
        }

        private static XlsxWorksheet ResolveWorksheet(ZipArchive archive, string sheetName)
        {
            XDocument workbook = TryLoadXml(archive.GetEntry("xl/workbook.xml"));
            if (workbook != null)
            {
                XElement selected = null;
                foreach (XElement sheet in workbook.Descendants(SpreadsheetNs + "sheet"))
                {
                    if (selected == null || string.Equals((string)sheet.Attribute("name"), sheetName, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = sheet;
                    }
                    if (selected == sheet && !string.IsNullOrWhiteSpace(sheetName))
                    {
                        break;
                    }
                }

                if (selected != null)
                {
                    string resolvedName = (string)selected.Attribute("name") ?? "sheet";
                    string relationshipId = (string)selected.Attribute(OfficeRelationshipNs + "id");
                    ZipArchiveEntry entry = ResolveWorksheetEntryFromRelationship(archive, relationshipId);
                    if (entry != null)
                    {
                        return new XlsxWorksheet(resolvedName, entry);
                    }
                }
            }

            ZipArchiveEntry fallback = archive.GetEntry("xl/worksheets/sheet1.xml");
            if (fallback == null)
            {
                throw new UsageException("skill.export-xlsx could not find xl/worksheets/sheet1.xml.");
            }
            return new XlsxWorksheet("sheet1", fallback);
        }

        private static ZipArchiveEntry ResolveWorksheetEntryFromRelationship(ZipArchive archive, string relationshipId)
        {
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                return null;
            }

            XDocument relationships = TryLoadXml(archive.GetEntry("xl/_rels/workbook.xml.rels"));
            if (relationships == null)
            {
                return null;
            }

            XElement relationship = relationships.Descendants(PackageRelationshipNs + "Relationship")
                .FirstOrDefault(item => string.Equals((string)item.Attribute("Id"), relationshipId, StringComparison.Ordinal));
            string target = relationship == null ? null : (string)relationship.Attribute("Target");
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            string path = target.StartsWith("/", StringComparison.Ordinal)
                ? target.TrimStart('/')
                : "xl/" + target.TrimStart('/');
            return archive.GetEntry(path.Replace('\\', '/'));
        }

        private static List<XlsxRow> LoadRows(ZipArchiveEntry worksheetEntry, IReadOnlyList<string> sharedStrings)
        {
            XDocument document = LoadXml(worksheetEntry);
            var rows = new List<XlsxRow>();
            int nextRowNumber = 1;
            foreach (XElement rowElement in document.Descendants(SpreadsheetNs + "row"))
            {
                int rowNumber;
                if (!int.TryParse((string)rowElement.Attribute("r"), out rowNumber))
                {
                    rowNumber = nextRowNumber;
                }
                nextRowNumber = rowNumber + 1;

                var row = new XlsxRow(rowNumber);
                foreach (XElement cell in rowElement.Elements(SpreadsheetNs + "c"))
                {
                    string reference = (string)cell.Attribute("r");
                    string column = ExtractColumnName(reference);
                    if (string.IsNullOrWhiteSpace(column))
                    {
                        continue;
                    }

                    string value = ReadCellValue(cell, sharedStrings);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        row.Cells[column] = value.Trim();
                    }
                }
                rows.Add(row);
            }
            return rows;
        }

        private static XlsxRow ResolveHeaderRow(IReadOnlyList<XlsxRow> rows, XlsxSkillRecipeOptions options)
        {
            if (options.HeaderRow > 0)
            {
                return rows.FirstOrDefault(row => row.RowNumber == options.HeaderRow);
            }

            if (!string.IsNullOrWhiteSpace(options.JobNameColumn)
                && !string.IsNullOrWhiteSpace(options.JobCodeColumn)
                && options.SkillColumns != null
                && options.SkillColumns.Count > 0)
            {
                return null;
            }

            return rows.Where(row => row.RowNumber <= 30)
                .FirstOrDefault(row => row.Cells.Values.Any(IsSkillHeader)
                    && row.Cells.Values.Any(value => IsJobNameHeader(value) || IsJobCodeHeader(value)));
        }

        private static string ReadCell(XlsxRow row, string column)
        {
            if (row == null || string.IsNullOrWhiteSpace(column))
            {
                return null;
            }

            string value;
            return row.Cells.TryGetValue(NormalizeColumn(column), out value) ? value : null;
        }

        private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
        {
            string type = (string)cell.Attribute("t");
            if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase))
            {
                string rawIndex = (string)cell.Element(SpreadsheetNs + "v");
                int index;
                if (int.TryParse(rawIndex, out index) && index >= 0 && index < sharedStrings.Count)
                {
                    return sharedStrings[index];
                }
                return rawIndex;
            }
            if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
            {
                XElement inline = cell.Element(SpreadsheetNs + "is");
                return inline == null ? null : string.Concat(inline.Descendants(SpreadsheetNs + "t").Select(t => t.Value));
            }

            return (string)cell.Element(SpreadsheetNs + "v");
        }

        private static string DetectColumn(XlsxRow header, Func<string, bool> predicate)
        {
            return header.Cells
                .Where(item => predicate(item.Value))
                .Select(item => item.Key)
                .FirstOrDefault();
        }

        private static List<string> DetectColumns(XlsxRow header, Func<string, bool> predicate)
        {
            return header.Cells
                .Where(item => predicate(item.Value))
                .Select(item => item.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsJobNameHeader(string value)
        {
            string normalized = NormalizeHeader(value);
            return (normalized.Contains("직업") || normalized.Contains("job"))
                && !normalized.Contains("코드")
                && !normalized.Contains("code")
                && !normalized.Contains("id");
        }

        private static bool IsJobCodeHeader(string value)
        {
            string normalized = NormalizeHeader(value);
            return normalized.Contains("직업코드")
                || normalized.Contains("jobcode")
                || normalized.Contains("jobid")
                || normalized == "code";
        }

        private static bool IsSkillHeader(string value)
        {
            string normalized = NormalizeHeader(value);
            return (normalized.Contains("스킬") || normalized.Contains("skill"))
                && !normalized.Contains("코드")
                && !normalized.Contains("아이디")
                && !normalized.Contains("id");
        }

        private static bool IsIgnorableSkillValue(string value)
        {
            string normalized = NormalizeHeader(value);
            if (normalized.Length == 0)
            {
                return true;
            }
            if (normalized == "-" || normalized == "없음" || normalized == "n/a" || normalized == "na")
            {
                return true;
            }
            if (normalized.Contains("원") || normalized.Contains("%"))
            {
                return true;
            }
            return decimal.TryParse(normalized, out _);
        }

        private static IEnumerable<string> SplitSkillNames(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            foreach (string part in value.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }

        private static string NormalizeHeader(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Trim().ToLowerInvariant().Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-').ToArray());
        }

        private static string NormalizeColumn(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return new string(value.Trim().Where(char.IsLetter).Select(char.ToUpperInvariant).ToArray());
        }

        private static List<string> NormalizeColumns(IReadOnlyList<string> values)
        {
            var result = new List<string>();
            if (values == null)
            {
                return result;
            }

            foreach (string raw in values)
            {
                foreach (string part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string column = NormalizeColumn(part);
                    if (!string.IsNullOrWhiteSpace(column)
                        && !result.Any(item => string.Equals(item, column, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(column);
                    }
                }
            }
            return result;
        }

        private static string ExtractColumnName(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            return new string(reference.TakeWhile(char.IsLetter).Select(char.ToUpperInvariant).ToArray());
        }

        private static string CleanTsv(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static XDocument LoadXml(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream);
            }
        }

        private static XDocument TryLoadXml(ZipArchiveEntry entry)
        {
            return entry == null ? null : LoadXml(entry);
        }

        private sealed class XlsxWorksheet
        {
            public XlsxWorksheet(string name, ZipArchiveEntry entry)
            {
                this.Name = name;
                this.Entry = entry;
            }

            public string Name { get; private set; }
            public ZipArchiveEntry Entry { get; private set; }
        }

        private sealed class XlsxRow
        {
            public XlsxRow(int rowNumber)
            {
                this.RowNumber = rowNumber;
                this.Cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public int RowNumber { get; private set; }
            public Dictionary<string, string> Cells { get; private set; }
        }
    }
}
