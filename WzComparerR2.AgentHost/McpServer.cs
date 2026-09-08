using System;
using System.Collections.Generic;
using System.Text.Json;
using WzComparerR2.Headless.Agent;

namespace WzComparerR2.AgentHost
{
    internal sealed class McpServer
    {
        private const string ModernProtocolVersion = "2026-07-28";
        private const string LegacyProtocolVersion = "2025-11-25";
        private readonly string version;
        private readonly JsonSerializerOptions jsonOptions;

        public McpServer(string version)
        {
            this.version = version;
            this.jsonOptions = new JsonSerializerOptions(AgentJobRunner.JsonOptions)
            {
                WriteIndented = false
            };
        }

        public int RunStdio()
        {
            using (var runner = new AgentJobRunner())
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    object response = HandleLine(runner, line);
                    if (response == null)
                    {
                        continue;
                    }

                    Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
                    Console.Out.Flush();
                }
            }

            return 0;
        }

        private object HandleLine(AgentJobRunner runner, string line)
        {
            JsonDocument document = null;
            try
            {
                document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Error(null, -32600, "Invalid Request", "JSON-RPC message must be an object.");
                }

                JsonElement idElement;
                bool hasId = root.TryGetProperty("id", out idElement);
                object id = hasId ? (object)idElement.Clone() : null;

                string method = GetStringProperty(root, "method");
                if (string.IsNullOrWhiteSpace(method))
                {
                    return hasId
                        ? Error(id, -32600, "Invalid Request", "JSON-RPC request requires method.")
                        : null;
                }

                if (!hasId)
                {
                    return null;
                }

                switch (method)
                {
                    case "server/discover":
                        return Result(id, DiscoverResult());
                    case "initialize":
                        return Result(id, InitializeResult(root));
                    case "ping":
                        return Result(id, CompleteResult(new Dictionary<string, object>()));
                    case "tools/list":
                        return Result(id, ListToolsResult());
                    case "tools/call":
                        return Result(id, CallTool(runner, root));
                    case "resources/list":
                        return Result(id, EmptyListResult("resources"));
                    case "resources/templates/list":
                        return Result(id, EmptyListResult("resourceTemplates"));
                    case "prompts/list":
                        return Result(id, EmptyListResult("prompts"));
                    default:
                        return Error(id, -32601, "Method not found", "Unknown MCP method: " + method);
                }
            }
            catch (JsonException ex)
            {
                return Error(null, -32700, "Parse error", ex.Message);
            }
            catch (Exception ex)
            {
                return Error(null, -32603, "Internal error", ex.Message);
            }
            finally
            {
                if (document != null)
                {
                    document.Dispose();
                }
            }
        }

        private Dictionary<string, object> DiscoverResult()
        {
            return new Dictionary<string, object>
            {
                { "resultType", "complete" },
                { "supportedVersions", new[] { ModernProtocolVersion, LegacyProtocolVersion, "2025-06-18" } },
                { "capabilities", ServerCapabilities() },
                { "instructions", "Use wcr2.run_job for arbitrary WzComparerR2 agent jobs. Prefer specific tools for common skill, item, map, and image-search tasks." },
                { "ttlMs", 300000 },
                { "cacheScope", "public" },
                { "_meta", ServerMeta() }
            };
        }

        private Dictionary<string, object> InitializeResult(JsonElement root)
        {
            string requested = null;
            JsonElement paramsElement;
            if (root.TryGetProperty("params", out paramsElement) && paramsElement.ValueKind == JsonValueKind.Object)
            {
                requested = GetStringProperty(paramsElement, "protocolVersion");
            }

            string selected = string.Equals(requested, "2025-06-18", StringComparison.OrdinalIgnoreCase)
                ? "2025-06-18"
                : LegacyProtocolVersion;

            return new Dictionary<string, object>
            {
                { "protocolVersion", selected },
                { "capabilities", ServerCapabilities() },
                { "serverInfo", ServerInfo() },
                { "instructions", "Use tools/call with wcr2.run_job for arbitrary agent jobs, or use the narrower wcr2.skill_export, wcr2.item_icon, wcr2.item_export, wcr2.map_export, and wcr2.image_search tools." }
            };
        }

        private Dictionary<string, object> ListToolsResult()
        {
            return new Dictionary<string, object>
            {
                { "resultType", "complete" },
                { "tools", GetTools() },
                { "ttlMs", 300000 },
                { "cacheScope", "public" },
                { "_meta", ServerMeta() }
            };
        }

        private object CallTool(AgentJobRunner runner, JsonElement root)
        {
            JsonElement paramsElement;
            if (!root.TryGetProperty("params", out paramsElement) || paramsElement.ValueKind != JsonValueKind.Object)
            {
                return ToolExecutionError("invalid-params", "tools/call requires params.");
            }

            string name = GetStringProperty(paramsElement, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return ToolExecutionError("invalid-params", "tools/call requires params.name.");
            }

            JsonElement arguments;
            if (!paramsElement.TryGetProperty("arguments", out arguments) || arguments.ValueKind == JsonValueKind.Undefined)
            {
                arguments = EmptyObject();
            }
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                return ToolExecutionError("invalid-params", "tools/call params.arguments must be an object.");
            }

            try
            {
                switch (name)
                {
                    case "wcr2.run_job":
                        return ToolResult(runner.Run(BuildRunRequest(arguments)), false);
                    case "wcr2.skill_export":
                        return AgentRunToolResult(runner.Run(BuildSingleStepRequest(arguments, "skill.export", "skill-export")));
                    case "wcr2.skill_export_batch":
                        return AgentRunToolResult(runner.Run(BuildSingleStepRequest(arguments, "skill.export-batch", "skill-export-batch")));
                    case "wcr2.skill_export_xlsx":
                        return AgentRunToolResult(runner.Run(BuildSingleStepRequest(arguments, "skill.export-xlsx", "skill-export-xlsx")));
                    case "wcr2.item_icon":
                        return AgentRunToolResult(runner.Run(BuildSingleStepRequest(arguments, "item.icon", "item-icon")));
                    case "wcr2.item_export":
                        return AgentRunToolResult(runner.Run(BuildSingleStepRequest(arguments, "item.export", "item-export")));
                    case "wcr2.map_export":
                        return AgentRunToolResult(runner.Run(BuildSingleStepRequest(arguments, "map.export", "map-export")));
                    case "wcr2.image_search":
                        return AgentRunToolResult(runner.Run(BuildSingleStepRequest(arguments, "image.search", "image-search")));
                    case "wcr2.cache_stats":
                        return ToolResult(runner.GetCacheStats(), false);
                    case "wcr2.cache_clear":
                        return ToolResult(runner.ClearCache(), false);
                    default:
                        return ToolExecutionError("unknown-tool", "Unknown tool: " + name);
                }
            }
            catch (Exception ex)
            {
                return ToolExecutionError("tool-exception", ex.Message);
            }
        }

        private object AgentRunToolResult(AgentRunResult result)
        {
            return ToolResult(result, result == null || !result.IsSuccess);
        }

        private AgentRunRequest BuildRunRequest(JsonElement arguments)
        {
            AgentJob job = null;
            JsonElement jobElement;
            if (arguments.TryGetProperty("job", out jobElement) && jobElement.ValueKind == JsonValueKind.Object)
            {
                job = JsonSerializer.Deserialize<AgentJob>(jobElement.GetRawText(), AgentJobRunner.JsonOptions);
            }

            return new AgentRunRequest
            {
                JobPath = FirstNonEmpty(GetStringProperty(arguments, "jobPath"), GetStringProperty(arguments, "job")),
                OutputDirectoryOverride = FirstNonEmpty(
                    GetStringProperty(arguments, "outputDir"),
                    GetStringProperty(arguments, "outputDirectory"),
                    GetStringProperty(arguments, "out")),
                BaseDirectory = GetStringProperty(arguments, "baseDirectory"),
                Job = job
            };
        }

        private AgentRunRequest BuildSingleStepRequest(JsonElement arguments, string stepType, string defaultStepId)
        {
            string output = FirstNonEmpty(
                GetStringProperty(arguments, "outputDir"),
                GetStringProperty(arguments, "outputDirectory"),
                GetStringProperty(arguments, "outputRoot"),
                GetStringProperty(arguments, "outRoot"),
                GetStringProperty(arguments, "out"));
            string stepId = FirstNonEmpty(GetStringProperty(arguments, "stepId"), defaultStepId);

            var step = new AgentJobStep
            {
                Id = stepId,
                Type = stepType,
                ExtensionData = CopyExtensionData(arguments)
            };

            if (!step.ExtensionData.ContainsKey("outputDir") && !string.IsNullOrWhiteSpace(output))
            {
                step.ExtensionData["outputDir"] = JsonSerializer.SerializeToElement(output);
            }

            var job = new AgentJob
            {
                DataDir = GetStringProperty(arguments, "dataDir"),
                OutputDir = output,
                Steps = new List<AgentJobStep> { step }
            };

            return new AgentRunRequest
            {
                BaseDirectory = GetStringProperty(arguments, "baseDirectory"),
                Job = job
            };
        }

        private static Dictionary<string, JsonElement> CopyExtensionData(JsonElement arguments)
        {
            var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "job",
                "jobPath",
                "baseDirectory",
                "stepId"
            };
            var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in arguments.EnumerateObject())
            {
                if (skipped.Contains(property.Name))
                {
                    continue;
                }
                result[property.Name] = property.Value.Clone();
            }
            return result;
        }

        private Dictionary<string, object> ToolResult(object structuredContent, bool isError)
        {
            object content = structuredContent ?? new Dictionary<string, object>();
            string text = JsonSerializer.Serialize(content, jsonOptions);
            return new Dictionary<string, object>
            {
                { "resultType", "complete" },
                { "content", new[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } },
                { "structuredContent", content },
                { "isError", isError },
                { "_meta", ServerMeta() }
            };
        }

        private object ToolExecutionError(string error, string message)
        {
            var structured = new Dictionary<string, object>
            {
                { "status", "failed" },
                { "error", error },
                { "message", message }
            };
            return ToolResult(structured, true);
        }

        private Dictionary<string, object> EmptyListResult(string propertyName)
        {
            return new Dictionary<string, object>
            {
                { "resultType", "complete" },
                { propertyName, Array.Empty<object>() },
                { "ttlMs", 300000 },
                { "cacheScope", "public" },
                { "_meta", ServerMeta() }
            };
        }

        private List<Dictionary<string, object>> GetTools()
        {
            return new List<Dictionary<string, object>>
            {
                Tool("wcr2.run_job", "Run WzComparerR2 Agent Job", "Run an arbitrary wcr2-agent JSON job from jobPath or inline job.", RunJobSchema()),
                Tool("wcr2.skill_export", "Export Skill Assets", "Export one MapleStory skill by id using the existing agent skill.export step.", SingleStepSchema("skillId", "Skill id to export.")),
                Tool("wcr2.skill_export_batch", "Export Skill Batch", "Export multiple skills using ids, idsFile, names, or namesFile.", SingleStepSchema("ids", "Comma-separated skill ids. idsFile, names, and namesFile are also accepted.")),
                Tool("wcr2.skill_export_xlsx", "Export Skills From XLSX", "Read an XLSX skill sheet and export matching skills with job/skill folder naming.", SingleStepSchema("xlsx", "Path to the XLSX workbook.")),
                Tool("wcr2.item_icon", "Export Item Icon", "Export one item icon by itemId or name.", SingleStepSchema("itemId", "Item id to export. name is also accepted.")),
                Tool("wcr2.item_export", "Export Item Info And Icon", "Export item-info.json and item icon by itemId or name.", SingleStepSchema("itemId", "Item id to export. name is also accepted.")),
                Tool("wcr2.map_export", "Export Map Metadata", "Export map-info.json and map-metadata.json for one map id.", SingleStepSchema("mapId", "Map id to export.")),
                Tool("wcr2.image_search", "Search WZ By Image", "Search Maple Data for a PNG query image and return candidate WZ paths.", SingleStepSchema("query", "PNG query image path.")),
                Tool("wcr2.cache_stats", "Read Agent Cache Stats", "Return current serve-process cache counts, hits, misses, and entries.", EmptyObjectSchema()),
                Tool("wcr2.cache_clear", "Clear Agent Cache", "Dispose all cached WZ repositories and contexts in this MCP process.", EmptyObjectSchema())
            };
        }

        private static Dictionary<string, object> Tool(string name, string title, string description, Dictionary<string, object> inputSchema)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "title", title },
                { "description", description },
                { "inputSchema", inputSchema }
            };
        }

        private static Dictionary<string, object> RunJobSchema()
        {
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "additionalProperties", false },
                {
                    "properties",
                    new Dictionary<string, object>
                    {
                        { "jobPath", StringSchema("Path to a wcr2-agent job JSON file.") },
                        { "job", new Dictionary<string, object> { { "type", "object" }, { "description", "Inline wcr2-agent job object." }, { "additionalProperties", true } } },
                        { "outputDir", StringSchema("Optional output directory override.") },
                        { "baseDirectory", StringSchema("Base directory for relative job paths.") }
                    }
                }
            };
        }

        private static Dictionary<string, object> SingleStepSchema(string selectorName, string selectorDescription)
        {
            var properties = new Dictionary<string, object>
            {
                { "dataDir", StringSchema("MapleStory Data directory. Use input or domain-specific WZ path instead when needed.") },
                { "input", StringSchema("Optional direct WZ file or directory input.") },
                { selectorName, StringSchema(selectorDescription) },
                { "name", StringSchema("Name selector for item or skill lookup where supported.") },
                { "outputDir", StringSchema("Output directory for this single-step export or search.") },
                { "outputRoot", StringSchema("Output root for batch-style tools.") },
                { "branch", StringSchema("Skill branch list such as auto, icon,effect,hit, keydown, prepare.") },
                { "category", StringSchema("Item category such as cash, consume, install, etc, or pet.") },
                { "scope", StringSchema("Image search scope such as ui, item, skill, map, mob, or all.") },
                { "probe", BoolSchema("For image search, run a faster first-pass query by trusting cache and skipping pixel refine.") },
                { "trustCache", BoolSchema("For image search, skip source timestamp validation for faster cache reads.") },
                { "noRefine", BoolSchema("For image search, return cache/index scores without reopening top candidates for pixel scoring.") },
                { "refine", BoolSchema("For image search, enable or disable pixel-level refinement.") },
                { "trimBackground", BoolSchema("For image search, trim a solid-ish border background from the query before scoring.") },
                { "backgroundTolerance", NumberSchema("For image search, RGB distance threshold for trimBackground; default 24.") },
                { "includeVideo", BoolSchema("For image search, include internal MCV/Wz_Video frames in the searched index. Query remains a PNG.") },
                { "ffmpeg", StringSchema("For image/video operations, ffmpeg executable path; default ffmpeg.") },
                { "maxVideoFrames", NumberSchema("For image search includeVideo, decode only first n frames per video; default 0 means all frames.") },
                { "stepId", StringSchema("Optional step id to use inside the generated agent job.") },
                { "baseDirectory", StringSchema("Base directory for relative paths.") }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "additionalProperties", true },
                { "properties", properties }
            };
        }

        private static Dictionary<string, object> EmptyObjectSchema()
        {
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "additionalProperties", false }
            };
        }

        private static Dictionary<string, object> StringSchema(string description)
        {
            return new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", description }
            };
        }

        private static Dictionary<string, object> BoolSchema(string description)
        {
            return new Dictionary<string, object>
            {
                { "type", "boolean" },
                { "description", description }
            };
        }

        private static Dictionary<string, object> NumberSchema(string description)
        {
            return new Dictionary<string, object>
            {
                { "type", "number" },
                { "description", description }
            };
        }

        private Dictionary<string, object> CompleteResult(Dictionary<string, object> values)
        {
            var result = new Dictionary<string, object>(values ?? new Dictionary<string, object>())
            {
                { "resultType", "complete" },
                { "_meta", ServerMeta() }
            };
            return result;
        }

        private static Dictionary<string, object> ServerCapabilities()
        {
            return new Dictionary<string, object>
            {
                { "tools", new Dictionary<string, object> { { "listChanged", false } } }
            };
        }

        private Dictionary<string, object> ServerMeta()
        {
            return new Dictionary<string, object>
            {
                { "io.modelcontextprotocol/serverInfo", ServerInfo() }
            };
        }

        private Dictionary<string, object> ServerInfo()
        {
            return new Dictionary<string, object>
            {
                { "name", "wcr2-agent" },
                { "title", "WzComparerR2 Agent Host" },
                { "version", version },
                { "description", "MCP wrapper for WzComparerR2 headless MapleStory asset extraction." }
            };
        }

        private static object Result(object id, object result)
        {
            return new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", id },
                { "result", result }
            };
        }

        private static object Error(object id, int code, string message, object data)
        {
            return new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", id },
                {
                    "error",
                    new Dictionary<string, object>
                    {
                        { "code", code },
                        { "message", message },
                        { "data", data }
                    }
                }
            };
        }

        private static JsonElement EmptyObject()
        {
            using (JsonDocument document = JsonDocument.Parse("{}"))
            {
                return document.RootElement.Clone();
            }
        }

        private static string GetStringProperty(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            return value.GetString();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            return null;
        }
    }
}
