using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Build.Locator;
using RoslynMcpServer.Core.Services;
using Serilog;
using Serilog.Events;

namespace RoslynMcpServer.Quality;

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
                path: Path.Combine(logDirectory, "quality-startup-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting RoslynMcpServer.Quality...");
            Log.Information("This MCP server provides 8 quality analysis tools (~1,400 tokens)");

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
            var logFileName = environment == "Development" ? "quality-debug-.log" : "quality-.log";
            var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Warning;

            builder.Services.AddSerilog((services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("McpServer", "Quality");

                var retainedFiles = environment == "Development" ? 7 : 30;
                loggerConfiguration
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, logFileName),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: retainedFiles,
                        restrictedToMinimumLevel: minLevel);
            });

            // Register services needed for quality tools
            ConfigureServices(builder.Services);

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("RoslynMcpServer.Quality started successfully");
            logger.LogInformation("Available tools: AnalyzeCodeComplexity, FindCodeSmells, FindUnusedCode, FindDuplicateCode, FindMagicNumbers, AnalyzeNamingConventions, AnalyzeConcurrencyPatterns, AnalyzeMagicNumbers");

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
        services.AddSingleton<CodeMetricsService>();
        services.AddSingleton<Phase1AnalysisService>();
        services.AddSingleton<UnusedCodeAnalyzer>();
        services.AddSingleton<DuplicateCodeAnalyzer>();
        services.AddSingleton<NamingConventionAnalyzer>();
        services.AddSingleton<ConcurrencyPatternAnalyzer>();
        services.AddSingleton<MagicNumberAnalyzer>();
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
