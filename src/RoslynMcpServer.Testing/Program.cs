using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Build.Locator;
using RoslynMcpServer.Core.Services;
using Serilog;
using Serilog.Events;

namespace RoslynMcpServer.Testing;

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
                path: Path.Combine(logDirectory, "testing-startup-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting RoslynMcpServer.Testing...");
            Log.Information("This MCP server provides 2 testing analysis tools (~350 tokens)");

            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                Log.Information("MSBuild registered successfully");
            }

            var builder = Host.CreateApplicationBuilder(args);

            var environment = builder.Environment.EnvironmentName;
            var logFileName = environment == "Development" ? "testing-debug-.log" : "testing-.log";
            var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Warning;

            builder.Services.AddSerilog((services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("McpServer", "Testing");

                loggerConfiguration
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, logFileName),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: environment == "Development" ? 7 : 30,
                        restrictedToMinimumLevel: minLevel);
            });

            // Register services needed for testing tools
            builder.Services.AddSingleton<CodeAnalysisService>();
            builder.Services.AddSingleton<TestDiscoveryService>();
            builder.Services.AddSingleton<TestCoverageAnalyzer>();
            builder.Services.AddSingleton<SecurityValidator>();
            builder.Services.AddSingleton<DiagnosticLogger>();
            builder.Services.AddSingleton<IncrementalAnalyzer>();
            builder.Services.AddSingleton<IPersistentCache, FilePersistentCache>();
            builder.Services.AddSingleton<MultiLevelCacheManager>();
            builder.Services.AddSingleton<McpErrorHandler>();        // MCP error handling service
            builder.Services.AddSingleton<CancellationManager>();    // Request cancellation tracking
            builder.Services.AddSingleton<CancellableOperation>();   // Cancellable operation helper
            builder.Services.AddMemoryCache();

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("RoslynMcpServer.Testing started successfully");
            logger.LogInformation("Available tools: FindTestsForType, GetTestCoverage");

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
