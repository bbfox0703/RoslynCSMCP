using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Build.Locator;
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

                // Configure Serilog from appsettings.json
                builder.Services.AddSerilog((services, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .ReadFrom.Configuration(builder.Configuration)
                        .Enrich.FromLogContext();

                    // Override file path with platform-specific log directory
                    var environment = builder.Environment.EnvironmentName;
                    var logFileName = environment == "Development" ? "debug-.log" : "roslyn-mcp-.log";
                    var retainedFiles = environment == "Development" ? 7 : 30;
                    var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Warning;

                    loggerConfiguration
                        .WriteTo.Console(
                            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                            standardErrorFromLevel: LogEventLevel.Verbose)
                        .WriteTo.File(
                            path: Path.Combine(logDirectory, logFileName),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: retainedFiles,
                            restrictedToMinimumLevel: minLevel,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Properties:j}{NewLine}{Exception}");
                });

                // Register services
                builder.Services.AddSingleton<CodeAnalysisService>();
                builder.Services.AddSingleton<SymbolSearchService>();
                builder.Services.AddSingleton<TypeSignatureService>();
                builder.Services.AddSingleton<ProjectStructureService>();
                builder.Services.AddSingleton<CodeMetricsService>();
                builder.Services.AddSingleton<DependencyGraphService>();
                builder.Services.AddSingleton<CallHierarchyService>();
                builder.Services.AddSingleton<BatchQueryService>();
                builder.Services.AddSingleton<SecurityValidator>();
                builder.Services.AddSingleton<DiagnosticLogger>();
                builder.Services.AddSingleton<IncrementalAnalyzer>();
                builder.Services.AddSingleton<IPersistentCache, FilePersistentCache>();
                builder.Services.AddSingleton<MultiLevelCacheManager>();
                builder.Services.AddMemoryCache();

                // Configure MCP server
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