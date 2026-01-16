using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace RoslynMcpServer.Configuration;

/// <summary>
/// Extension methods for MCP server builder that support tool profile filtering.
/// </summary>
public static class McpToolFilterExtensions
{
    /// <summary>
    /// Register MCP tools from assembly with profile-based filtering.
    /// Only tools enabled in the active profile will be registered.
    /// </summary>
    /// <param name="builder">The MCP server builder</param>
    /// <param name="config">Tool profile configuration (null = load from file or use defaults)</param>
    /// <returns>The builder for chaining</returns>
    public static IMcpServerBuilder WithFilteredTools(
        this IMcpServerBuilder builder,
        ToolProfileConfig? config = null)
    {
        config ??= ToolProfileConfig.Load();

        var enabledTools = config.GetEnabledTools();
        var isFullProfile = enabledTools.Contains("*");

        // Log profile information
        Console.Error.WriteLine($"[Tool Profile] Active profile: {config.ActiveProfile}");

        if (isFullProfile)
        {
            Console.Error.WriteLine("[Tool Profile] Loading ALL tools (full profile)");
            // Use standard registration for full profile
            return builder.WithToolsFromAssembly();
        }

        Console.Error.WriteLine($"[Tool Profile] Enabled tools: {string.Join(", ", enabledTools.OrderBy(t => t))}");

        // Get all tool types from the assembly
        var assembly = Assembly.GetExecutingAssembly();
        var toolTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .ToList();

        var registeredCount = 0;
        var skippedCount = 0;

        foreach (var toolType in toolTypes)
        {
            var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null);

            foreach (var method in methods)
            {
                var toolName = method.Name;

                if (enabledTools.Contains(toolName))
                {
                    // Register this tool
                    builder.WithTools(toolType);
                    registeredCount++;
                    Console.Error.WriteLine($"[Tool Profile] Registered: {toolName}");
                }
                else
                {
                    skippedCount++;
                }
            }
        }

        Console.Error.WriteLine($"[Tool Profile] Summary: {registeredCount} tools registered, {skippedCount} skipped");
        Console.Error.WriteLine($"[Tool Profile] Estimated token savings: ~{skippedCount * 150}-{skippedCount * 200} tokens");

        return builder;
    }

    /// <summary>
    /// Register MCP tools using a specific profile name.
    /// </summary>
    public static IMcpServerBuilder WithToolProfile(
        this IMcpServerBuilder builder,
        string profileName)
    {
        var config = ToolProfileConfig.Load();
        config.ActiveProfile = profileName;
        return builder.WithFilteredTools(config);
    }

    /// <summary>
    /// Get statistics about available tool profiles.
    /// </summary>
    public static ToolProfileStatistics GetToolStatistics(ToolProfileConfig? config = null)
    {
        config ??= ToolProfileConfig.Load();
        var assembly = Assembly.GetExecutingAssembly();

        // Count total tools
        var totalTools = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null))
            .Count();

        var enabledTools = config.GetEnabledTools();
        var enabledCount = enabledTools.Contains("*") ? totalTools : enabledTools.Count;

        return new ToolProfileStatistics
        {
            TotalTools = totalTools,
            EnabledTools = enabledCount,
            ActiveProfile = config.ActiveProfile,
            EstimatedTokensAll = totalTools * 175, // Average estimate
            EstimatedTokensEnabled = enabledCount * 175,
            TokenSavings = (totalTools - enabledCount) * 175
        };
    }
}

/// <summary>
/// Statistics about tool profile configuration.
/// </summary>
public class ToolProfileStatistics
{
    public int TotalTools { get; set; }
    public int EnabledTools { get; set; }
    public string ActiveProfile { get; set; } = "";
    public int EstimatedTokensAll { get; set; }
    public int EstimatedTokensEnabled { get; set; }
    public int TokenSavings { get; set; }

    public override string ToString()
    {
        return $"""
            Tool Profile Statistics:
              Active Profile: {ActiveProfile}
              Total Tools: {TotalTools}
              Enabled Tools: {EnabledTools}
              Estimated Tokens (all): ~{EstimatedTokensAll}
              Estimated Tokens (enabled): ~{EstimatedTokensEnabled}
              Token Savings: ~{TokenSavings}
            """;
    }
}
