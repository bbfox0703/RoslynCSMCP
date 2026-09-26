using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Build.Locator;
using RoslynMcpServer.Core.Services;
using Serilog;
using Serilog.Events;

namespace RoslynMcpServer.Advanced;

class Program
{
    static async Task Main(string[] args)
    {
        var logDirectory = GetLogDirectory();
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                standardErrorFromLevel: LogEventLevel.Verbose)
            .WriteTo.File(
                path: Path.Combine(logDirectory, "advanced-startup-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting RoslynMcpServer.Advanced...");
            Log.Information("This MCP server provides 15 advanced analysis tools (~2,625 tokens)");

            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                Log.Information("MSBuild registered successfully");
            }

            var builder = Host.CreateApplicationBuilder(args);

            var environment = builder.Environment.EnvironmentName;
            var logFileName = environment == "Development" ? "advanced-debug-.log" : "advanced-.log";
            var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Warning;

            builder.Services.AddSerilog((services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("McpServer", "Advanced");

                loggerConfiguration
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, logFileName),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: environment == "Development" ? 7 : 30,
                        restrictedToMinimumLevel: minLevel);
            });

            // Register all services needed for advanced tools
            ConfigureServices(builder.Services);

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("RoslynMcpServer.Advanced started successfully");
            logger.LogInformation("Available tools: BatchQuery, FindReferencesFiltered, FindReferencesAcrossSolutions, GetCompilationErrors, GetCallHierarchy, GetClassHierarchy, GetTypeSignature, FindAttributeUsages, FindDeprecatedAPIs, FindTODOComments, FindLargeFiles, AnalyzeAPIChanges, FindPerformanceIssues, AnalyzeIntegrityPatterns, AnalyzeIpcPatterns");

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            Environment.Exit(1);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Registers the services this server's tools depend on. Kept separate from Main so tests
    /// can verify every tool-injected service resolves without starting the host.
    /// </summary>
    internal static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<SymbolSearchService>();
        services.AddSingleton<CodeMetricsService>();       // Required by BatchQueryService
        services.AddSingleton<DependencyGraphService>();   // Required by BatchQueryService
        services.AddSingleton<BatchQueryService>();
        services.AddSingleton<CallHierarchyService>();
        services.AddSingleton<TypeSignatureService>();
        services.AddSingleton<AttributeSearchService>();
        services.AddSingleton<DeprecatedAPIAnalyzer>();
        services.AddSingleton<TODOCommentAnalyzer>();
        services.AddSingleton<LargeFileAnalyzer>();
        services.AddSingleton<APIChangeAnalyzer>();
        services.AddSingleton<PerformanceIssueAnalyzer>();
        services.AddSingleton<IntegrityPatternAnalyzer>();
        services.AddSingleton<IpcPatternAnalyzer>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();
        services.AddSingleton<IncrementalAnalyzer>();
        services.AddSingleton<IPersistentCache, FilePersistentCache>();
        services.AddSingleton<MultiLevelCacheManager>();
        services.AddSingleton<McpErrorHandler>();        // MCP error handling service
        services.AddSingleton<CancellationManager>();    // Request cancellation tracking
        services.AddSingleton<CancellableOperation>();   // Cancellable operation helper
        services.AddMemoryCache();
    }

    private static string GetLogDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Path.GetTempPath(), "RoslynCSMCP", "logs");
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return Path.Combine("/tmp", "RoslynCSMCP", "logs");
        else
            return Path.Combine(Directory.GetCurrentDirectory(), "logs");
    }
}
