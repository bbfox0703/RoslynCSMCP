using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Build.Locator;
using RoslynMcpServer.Core.Services;
using Serilog;
using Serilog.Events;

namespace RoslynMcpServer.Security;

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
                path: Path.Combine(logDirectory, "security-startup-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting RoslynMcpServer.Security...");
            Log.Information("This MCP server provides 3 security analysis tools (~525 tokens)");

            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                Log.Information("MSBuild registered successfully");
            }

            var builder = Host.CreateApplicationBuilder(args);

            var environment = builder.Environment.EnvironmentName;
            var logFileName = environment == "Development" ? "security-debug-.log" : "security-.log";
            var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Warning;

            builder.Services.AddSerilog((services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("McpServer", "Security");

                loggerConfiguration
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, logFileName),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: environment == "Development" ? 7 : 30,
                        restrictedToMinimumLevel: minLevel);
            });

            // Register services needed for security tools
            ConfigureServices(builder.Services);

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("RoslynMcpServer.Security started successfully");
            logger.LogInformation("Available tools: FindSecurityIssues, FindThreadSafetyIssues, AnalyzeExceptionHandling");

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
        services.AddSingleton<SecurityIssueAnalyzer>();
        services.AddSingleton<Phase2AnalysisService>();
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
