using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Core.Services
{
    public class SecurityValidator
    {
        private readonly HashSet<string> _allowedExtensions = new() { ".sln", ".csproj" };
        private readonly Regex _safePath = new(@"^[a-zA-Z]:[\\/][^<>:|?*]+$");
        private readonly ILogger<SecurityValidator> _logger;

        public SecurityValidator(ILogger<SecurityValidator> logger)
        {
            _logger = logger;
        }
        
        public bool ValidateSolutionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            
            // Check for path traversal attempts
            if (path.Contains("..") || path.Contains("~"))
                return false;
            
            // Validate path format
            if (!_safePath.IsMatch(path))
                return false;
            
            // Check file extension
            var extension = Path.GetExtension(path);
            if (!_allowedExtensions.Contains(extension))
                return false;
            
            // Verify file exists and is accessible
            try
            {
                return File.Exists(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error validating path: {Path}", path);
                return false;
            }
        }
        
        public string SanitizeSearchPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "*";
            
            // Remove potentially dangerous characters
            return Regex.Replace(pattern, @"[^\w*?.]", "");
        }
    }
}