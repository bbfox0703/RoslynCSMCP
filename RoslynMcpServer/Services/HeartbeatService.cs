using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace RoslynMcpServer.Services
{
    /// <summary>
    /// Background service that logs periodic heartbeat messages to confirm the MCP server is running
    /// </summary>
    public class HeartbeatService : BackgroundService
    {
        private readonly ILogger<HeartbeatService> _logger;
        private readonly TimeSpan _heartbeatInterval;
        private readonly DateTime _startTime;
        private int _heartbeatCount = 0;

        public HeartbeatService(ILogger<HeartbeatService> logger)
        {
            _logger = logger;
            _startTime = DateTime.UtcNow;

            // Default interval: 20 minutes (between 15-30 minutes as requested)
            // Can be configured via environment variable HEARTBEAT_INTERVAL_MINUTES
            var intervalMinutes = GetHeartbeatIntervalMinutes();
            _heartbeatInterval = TimeSpan.FromMinutes(intervalMinutes);

            _logger.LogInformation(
                "HeartbeatService initialized with interval: {Interval} minutes",
                intervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("HeartbeatService started at {StartTime:yyyy-MM-dd HH:mm:ss} UTC", _startTime);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(_heartbeatInterval, stoppingToken);

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        LogHeartbeat();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                _logger.LogInformation("HeartbeatService stopping gracefully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HeartbeatService encountered an error");
            }
        }

        private void LogHeartbeat()
        {
            _heartbeatCount++;
            var uptime = DateTime.UtcNow - _startTime;
            var process = Process.GetCurrentProcess();

            _logger.LogInformation(
                "💓 HEARTBEAT #{Count} | " +
                "Uptime: {Uptime:d'd 'h'h 'm'm'} | " +
                "Memory: {Memory:N0} MB | " +
                "Threads: {Threads} | " +
                "Time: {Time:yyyy-MM-dd HH:mm:ss} UTC",
                _heartbeatCount,
                uptime,
                process.WorkingSet64 / 1024.0 / 1024.0,
                process.Threads.Count,
                DateTime.UtcNow);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            var totalUptime = DateTime.UtcNow - _startTime;
            _logger.LogInformation(
                "HeartbeatService stopped. Total uptime: {Uptime:d'd 'h'h 'm'm 's's'} | Total heartbeats: {Count}",
                totalUptime,
                _heartbeatCount);

            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Gets the heartbeat interval from environment variable or uses default
        /// </summary>
        private static int GetHeartbeatIntervalMinutes()
        {
            var envVar = Environment.GetEnvironmentVariable("HEARTBEAT_INTERVAL_MINUTES");

            if (int.TryParse(envVar, out int minutes) && minutes >= 5 && minutes <= 60)
            {
                return minutes;
            }

            // Default: 20 minutes (middle of 15-30 range)
            return 20;
        }
    }
}
