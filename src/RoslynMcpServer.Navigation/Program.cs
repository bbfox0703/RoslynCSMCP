using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Build.Locator;
using RoslynMcpServer.Core.Services;
using Serilog;
using Serilog.Events;

namespace RoslynMcpServer.Navigation;

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
                path: Path.Combine(logDirectory, "navigation-startup-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting RoslynMcpServer.Navigation...");
            Log.Information("This MCP server provides 6 navigation tools (~1,050 tokens)");

            // Register MSBuild before any workspace operations
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

            var environment = builder.Environment.EnvironmentName;
            var logFileName = environment == "Development" ? "navigation-debug-.log" : "navigation-.log";
            var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Warning;

            // Configure Serilog
            builder.Services.AddSerilog((services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("McpServer", "Navigation");

                var retainedFiles = environment == "Development" ? 7 : 30;
                loggerConfiguration
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, logFileName),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: retainedFiles,
                        restrictedToMinimumLevel: minLevel);
            });

            // Register only the services needed for navigation tools
            ConfigureServices(builder.Services);

            // Configure MCP server with Navigation tools only
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("RoslynMcpServer.Navigation started successfully");
            logger.LogInformation("Available tools: SearchSymbols, FindReferences, GetSymbolInfo, GetProjectStructure, GetFileOutline, FindImplementations");

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

    /// <summary>
    /// Registers the services this server's tools depend on. Kept separate from Main so tests
    /// can verify every tool-injected service resolves without starting the host.
    /// </summary>
    internal static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<SymbolSearchService>();
        services.AddSingleton<ProjectStructureService>();
        services.AddSingleton<FileAnalysisService>();
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
            baseDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        }

        return baseDir;
    }
}
