using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Core.Services
{
    public class SecurityValidator
    {
        private readonly HashSet<string> _allowedExtensions = new() { ".sln", ".csproj" };

        // Windows path: C:\path\to\file or D:/path/to/file
        private readonly Regex _windowsPath = new(@"^[a-zA-Z]:[\\/][^<>:|?*]+$", RegexOptions.Compiled);

        // Unix path: /path/to/file (absolute paths only)
        private readonly Regex _unixPath = new(@"^/[^<>:|?*\x00]+$", RegexOptions.Compiled);

        private readonly ILogger<SecurityValidator> _logger;
        private readonly bool _isWindows;

        public SecurityValidator(ILogger<SecurityValidator> logger)
        {
            _logger = logger;
            _isWindows = OperatingSystem.IsWindows();
        }

        public bool ValidateSolutionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogDebug("Path validation failed: path is null or whitespace");
                return false;
            }

            // Check for path traversal attempts
            if (ContainsPathTraversal(path))
            {
                _logger.LogWarning("Path validation failed: path traversal attempt detected in {Path}", path);
                return false;
            }

            // Validate path format based on platform
            if (!IsValidPathFormat(path))
            {
                _logger.LogDebug("Path validation failed: invalid path format for {Path}", path);
                return false;
            }

            // Check file extension
            var extension = Path.GetExtension(path);
            if (!_allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Path validation failed: extension {Extension} not allowed", extension);
                return false;
            }

            // Verify file exists and is accessible
            try
            {
                var exists = File.Exists(path);
                if (!exists)
                {
                    _logger.LogDebug("Path validation failed: file does not exist {Path}", path);
                }
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error validating path: {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// Validates a C# source file path (.cs files)
        /// </summary>
        public bool ValidateSourceFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (ContainsPathTraversal(path))
            {
                _logger.LogWarning("Source file path validation failed: path traversal attempt detected in {Path}", path);
                return false;
            }

            if (!IsValidPathFormat(path))
                return false;

            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                return File.Exists(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error validating source file path: {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// Checks for path traversal attempts in a platform-aware manner
        /// </summary>
        private bool ContainsPathTraversal(string path)
        {
            // Check for parent directory traversal
            if (path.Contains(".."))
                return true;

            // Check for home directory expansion (Unix)
            if (path.StartsWith('~'))
                return true;

            // Check for null bytes (can be used to bypass validation)
            if (path.Contains('\0'))
                return true;

            // Normalize and check for encoded traversal attempts
            var normalized = path.Replace('\\', '/');

            // Check for URL-encoded traversal patterns
            if (normalized.Contains("%2e%2e", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("%252e", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Validates path format based on current operating system
        /// </summary>
        private bool IsValidPathFormat(string path)
        {
            if (_isWindows)
            {
                // On Windows, primarily validate Windows paths but also accept Unix-style for WSL compatibility
                return _windowsPath.IsMatch(path) || _unixPath.IsMatch(path);
            }
            else
            {
                // On Unix/macOS, validate Unix paths
                return _unixPath.IsMatch(path);
            }
        }

        public string SanitizeSearchPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "*";

            // Remove potentially dangerous characters
            // Allow: word characters, wildcards (* ?), dots, and hyphens
            return Regex.Replace(pattern, @"[^\w*?.\-]", "");
        }
    }
}