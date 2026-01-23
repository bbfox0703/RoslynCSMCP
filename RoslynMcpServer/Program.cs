using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Build.Locator;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Core.Configuration;
using RoslynMcpServer.Configuration;
using RoslynMcpServer.Services;
using Serilog;
using Serilog.Events;

namespace RoslynMcpServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Determine log directory based on platform
            var logDirectory = GetLogDirectory();
            Directory.CreateDirectory(logDirectory);

            // Configure Serilog early for startup logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    standardErrorFromLevel: LogEventLevel.Verbose)
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "startup-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting RoslynCSMCP Server...");
                Log.Information("Log directory: {LogDirectory}", logDirectory);

                // Register MSBuild before any workspace operations
                // This is required for Roslyn to find MSBuild
                if (!MSBuildLocator.IsRegistered)
                {
                    try
                    {
                        MSBuildLocator.RegisterDefaults();
                        Log.Information("MSBuild registered successfully");
                    }
                    catch (Exception ex)
                    {
                        Log.Fatal(ex, "Failed to register MSBuild: {Message}", ex.Message);
                        Environment.Exit(1);
                    }
                }

                var builder = Host.CreateApplicationBuilder(args);

                // Store environment info for logging after Serilog configuration
                var environment = builder.Environment.EnvironmentName;
                var logFileName = environment == "Development" ? "debug-.log" : "roslyn-mcp-.log";
                var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Warning;

                // Configure Serilog from appsettings.json
                // Configuration is environment-aware (reads appsettings.Development.json in Development mode)
                builder.Services.AddSerilog((services, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .ReadFrom.Configuration(builder.Configuration)
                        .Enrich.FromLogContext()
                        .Enrich.WithProperty("Environment", environment);

                    // Override file paths to use platform-specific log directory
                    var retainedFiles = environment == "Development" ? 7 : 30;
                    var outputTemplate = environment == "Development"
                        ? "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Properties:j}{NewLine}{Exception}"
                        : "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

                    // Add File sink with platform-specific path (overrides appsettings.json path)
                    loggerConfiguration
                        .WriteTo.File(
                            path: Path.Combine(logDirectory, logFileName),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: retainedFiles,
                            restrictedToMinimumLevel: minLevel,
                            outputTemplate: outputTemplate);
                });

                // Log configuration info AFTER Serilog is fully configured
                Log.Information("Configured logging for environment: {Environment}, MinLevel: {MinLevel}, LogFile: {LogFile}",
                    environment, minLevel, Path.Combine(logDirectory, logFileName));

                // Show PowerShell tail command for debug logs in Development mode
                if (environment == "Development")
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Log.Information("To tail debug log (PowerShell): Get-Content \"{LogPath}\" -Wait -Tail 20",
                            $"$env:TEMP\\RoslynCSMCP\\logs\\debug-$(Get-Date -Format yyyyMMdd).log");
                    }
                    else
                    {
                        Log.Information("To tail debug log (PowerShell): Get-Content \"{LogPath}\" -Wait -Tail 20",
                            "/tmp/RoslynCSMCP/logs/debug-$(date +%Y%m%d).log");
                    }
                }

                // Register services
                builder.Services.AddSingleton<CodeAnalysisService>();
                builder.Services.AddSingleton<SymbolSearchService>();
                builder.Services.AddSingleton<TypeSignatureService>();
                builder.Services.AddSingleton<ProjectStructureService>();
                builder.Services.AddSingleton<CodeMetricsService>();
                builder.Services.AddSingleton<DependencyGraphService>();
                builder.Services.AddSingleton<CallHierarchyService>();
                builder.Services.AddSingleton<BatchQueryService>();
                builder.Services.AddSingleton<DiagnosticsService>();
                builder.Services.AddSingleton<FileAnalysisService>();
                builder.Services.AddSingleton<TestDiscoveryService>();
                builder.Services.AddSingleton<AttributeSearchService>();
                builder.Services.AddSingleton<UnusedCodeAnalyzer>();
                builder.Services.AddSingleton<UnusedDependencyAnalyzer>();
                builder.Services.AddSingleton<SecurityIssueAnalyzer>();
                builder.Services.AddSingleton<DuplicateCodeAnalyzer>();
                builder.Services.AddSingleton<DocumentationAnalyzer>();
                builder.Services.AddSingleton<TODOCommentAnalyzer>();
                builder.Services.AddSingleton<LargeFileAnalyzer>();
                builder.Services.AddSingleton<DeprecatedAPIAnalyzer>();
                builder.Services.AddSingleton<FileStatisticsAnalyzer>();
                builder.Services.AddSingleton<PackageAnalysisService>();
                builder.Services.AddSingleton<TestCoverageAnalyzer>();
                builder.Services.AddSingleton<ChangeImpactAnalyzer>();
                builder.Services.AddSingleton<PerformanceIssueAnalyzer>();
                builder.Services.AddSingleton<NamingConventionAnalyzer>();
                builder.Services.AddSingleton<APIChangeAnalyzer>();
                builder.Services.AddSingleton<Phase1AnalysisService>();  // Phase 1 Tools
                builder.Services.AddSingleton<Phase2AnalysisService>();  // Phase 2 Tools
                builder.Services.AddSingleton<SecurityValidator>();
                builder.Services.AddSingleton<DiagnosticLogger>();
                builder.Services.AddSingleton<IncrementalAnalyzer>();
                builder.Services.AddSingleton<IPersistentCache, FilePersistentCache>();
                builder.Services.AddSingleton<MultiLevelCacheManager>();
                builder.Services.AddSingleton<McpErrorHandler>();  // MCP JSON-RPC error handling
                builder.Services.AddMemoryCache();

                // Register HeartbeatService as a background service
                builder.Services.AddHostedService<HeartbeatService>();

                // Load and log tool profile configuration
                var toolProfileConfig = ToolProfileConfig.Load();
                var toolStats = McpToolFilterExtensions.GetToolStatistics(toolProfileConfig);
                Log.Information("Tool Profile: {Profile}", toolProfileConfig.ActiveProfile);
                Log.Information("Tool Statistics: {EnabledTools}/{TotalTools} tools enabled",
                    toolStats.EnabledTools, toolStats.TotalTools);
                Log.Information("Estimated token usage: ~{Tokens} tokens (savings: ~{Savings})",
                    toolStats.EstimatedTokensEnabled, toolStats.TokenSavings);

                // Configure MCP server
                // NOTE: Current MCP SDK doesn't support per-tool filtering.
                // All tools are registered; profile config is for documentation/planning.
                // To reduce tokens, see docs/TOKEN_OPTIMIZATION.md for manual tool reduction.
                builder.Services
                    .AddMcpServer()
                    .WithStdioServerTransport()
                    .WithToolsFromAssembly();

                var host = builder.Build();

                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Roslyn MCP Server started successfully");
                logger.LogInformation("Environment: {Environment}", builder.Environment.EnvironmentName);
                logger.LogInformation("Logs are being written to: {LogDirectory}", logDirectory);

                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly: {Message}", ex.Message);
                Environment.Exit(1);
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }

        private static string GetLogDirectory()
        {
            string baseDir;

            if (OperatingSystem.IsWindows())
            {
                baseDir = Path.Combine(Path.GetTempPath(), "RoslynCSMCP", "logs");
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                baseDir = Path.Combine("/tmp", "RoslynCSMCP", "logs");
            }
            else
            {
                // Fallback to current directory for unknown platforms
                baseDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            }

            return baseDir;
        }
    }
}