using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynMcpServer.Configuration;

/// <summary>
/// Configuration for tool profiles that control which MCP tools are loaded.
/// This helps reduce token consumption by loading only needed tools.
/// </summary>
public class ToolProfileConfig
{
    /// <summary>
    /// Available profile definitions. Key is profile name, value is list of tool names.
    /// Use "*" as a tool name to include all tools.
    /// </summary>
    public Dictionary<string, List<string>> Profiles { get; set; } = new();

    /// <summary>
    /// The currently active profile name.
    /// </summary>
    public string ActiveProfile { get; set; } = "full";

    /// <summary>
    /// Tool categories for easier profile management.
    /// Key is category name, value is list of tool names.
    /// Profiles can reference categories using "@CategoryName" syntax.
    /// </summary>
    public Dictionary<string, List<string>> Categories { get; set; } = new();

    /// <summary>
    /// Load configuration from file and environment variables.
    /// Environment variable ROSLYN_MCP_PROFILE overrides the config file setting.
    /// </summary>
    public static ToolProfileConfig Load(string? configPath = null)
    {
        var path = configPath ?? GetDefaultConfigPath();
        ToolProfileConfig config;

        if (!File.Exists(path))
        {
            config = CreateDefault();
        }
        else
        {
            try
            {
                var json = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<ToolProfileConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) ?? CreateDefault();
            }
            catch
            {
                config = CreateDefault();
            }
        }

        // Environment variable override
        var envProfile = Environment.GetEnvironmentVariable("ROSLYN_MCP_PROFILE");
        if (!string.IsNullOrEmpty(envProfile) && config.Profiles.ContainsKey(envProfile))
        {
            config.ActiveProfile = envProfile;
        }

        return config;
    }

    /// <summary>
    /// Get the list of enabled tool names for the active profile.
    /// </summary>
    public HashSet<string> GetEnabledTools()
    {
        var enabledTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Profiles.TryGetValue(ActiveProfile, out var profileTools))
        {
            // If profile not found, enable all tools
            return new HashSet<string> { "*" };
        }

        foreach (var tool in profileTools)
        {
            if (tool == "*")
            {
                // Wildcard - enable all
                return new HashSet<string> { "*" };
            }
            else if (tool.StartsWith("@"))
            {
                // Category reference - expand it
                var categoryName = tool.Substring(1);
                if (Categories.TryGetValue(categoryName, out var categoryTools))
                {
                    foreach (var categoryTool in categoryTools)
                    {
                        enabledTools.Add(categoryTool);
                    }
                }
            }
            else
            {
                enabledTools.Add(tool);
            }
        }

        return enabledTools;
    }

    /// <summary>
    /// Check if a specific tool is enabled.
    /// </summary>
    public bool IsToolEnabled(string toolName)
    {
        var enabledTools = GetEnabledTools();
        return enabledTools.Contains("*") || enabledTools.Contains(toolName);
    }

    /// <summary>
    /// Get the default config file path.
    /// </summary>
    public static string GetDefaultConfigPath()
    {
        // Look in order: working directory, exe directory, user profile
        var locations = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "tool-profiles.json"),
            Path.Combine(AppContext.BaseDirectory, "tool-profiles.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".roslyn-mcp", "tool-profiles.json")
        };

        foreach (var location in locations)
        {
            if (File.Exists(location))
            {
                return location;
            }
        }

        // Return first location as default
        return locations[0];
    }

    /// <summary>
    /// Create default configuration with predefined profiles.
    /// </summary>
    public static ToolProfileConfig CreateDefault()
    {
        return new ToolProfileConfig
        {
            ActiveProfile = "standard",
            Categories = new Dictionary<string, List<string>>
            {
                ["Navigation"] = new List<string>
                {
                    "SearchSymbols",
                    "FindReferences",
                    "GetSymbolInfo",
                    "GetProjectStructure",
                    "GetFileOutline",
                    "FindImplementations"
                },
                ["CodeQuality"] = new List<string>
                {
                    "AnalyzeCodeComplexity",
                    "FindCodeSmells",
                    "FindUnusedCode",
                    "FindDuplicateCode",
                    "FindMagicNumbers",
                    "AnalyzeNamingConventions"
                },
                ["Security"] = new List<string>
                {
                    "FindSecurityIssues",
                    "FindThreadSafetyIssues",
                    "AnalyzeExceptionHandling"
                },
                ["Dependencies"] = new List<string>
                {
                    "AnalyzeDependencies",
                    "GetDependencyGraph",
                    "FindUnusedDependencies",
                    "AnalyzePackages",
                    "AnalyzeDIContainer"
                },
                ["Refactoring"] = new List<string>
                {
                    "RenameSymbolSafely",
                    "ExtractInterface",
                    "GetChangeImpact",
                    "AnalyzeLayerViolations"
                },
                ["Testing"] = new List<string>
                {
                    "FindTestsForType",
                    "GetTestCoverage"
                },
                ["Metrics"] = new List<string>
                {
                    "GetCodeMetrics",
                    "GetFileStatistics",
                    "AnalyzeDocumentationCoverage"
                },
                ["Advanced"] = new List<string>
                {
                    "BatchQuery",
                    "FindReferencesFiltered",
                    "FindReferencesAcrossSolutions",
                    "GetCompilationErrors",
                    "GetCallHierarchy",
                    "GetClassHierarchy",
                    "GetTypeSignature",
                    "FindAttributeUsages",
                    "FindDeprecatedAPIs",
                    "FindTODOComments",
                    "FindLargeFiles",
                    "AnalyzeAPIChanges",
                    "FindPerformanceIssues"
                }
            },
            Profiles = new Dictionary<string, List<string>>
            {
                // Minimal profile - just core navigation (6 tools, ~600-1200 tokens)
                ["minimal"] = new List<string>
                {
                    "@Navigation"
                },
                // Standard profile - navigation + quality + security (19 tools, ~1900-3800 tokens)
                ["standard"] = new List<string>
                {
                    "@Navigation",
                    "@CodeQuality",
                    "@Security"
                },
                // Extended profile - standard + dependencies + refactoring (28 tools, ~2800-5600 tokens)
                ["extended"] = new List<string>
                {
                    "@Navigation",
                    "@CodeQuality",
                    "@Security",
                    "@Dependencies",
                    "@Refactoring"
                },
                // Full profile - all tools (42 tools, ~4200-8400 tokens)
                ["full"] = new List<string>
                {
                    "*"
                },
                // Custom profile example - user can modify
                ["custom"] = new List<string>
                {
                    "SearchSymbols",
                    "FindReferences",
                    "AnalyzeCodeComplexity"
                }
            }
        };
    }

    /// <summary>
    /// Save configuration to file.
    /// </summary>
    public void Save(string? configPath = null)
    {
        var path = configPath ?? GetDefaultConfigPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(path, json);
    }
}
