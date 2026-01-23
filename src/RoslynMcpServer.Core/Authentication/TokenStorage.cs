using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RoslynMcpServer.Core.Authentication
{
    /// <summary>
    /// Interface for secure token storage.
    /// </summary>
    public interface ITokenStorage
    {
        /// <summary>
        /// Retrieves the stored token.
        /// </summary>
        Task<TokenInfo?> GetTokenAsync();

        /// <summary>
        /// Saves a token to storage.
        /// </summary>
        Task SaveTokenAsync(TokenInfo token);

        /// <summary>
        /// Clears the stored token.
        /// </summary>
        Task ClearTokenAsync();
    }

    /// <summary>
    /// In-memory token storage (not persistent across restarts).
    /// Use for development/testing only.
    /// </summary>
    public class InMemoryTokenStorage : ITokenStorage
    {
        private TokenInfo? _token;
        private readonly object _lock = new();

        public Task<TokenInfo?> GetTokenAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(_token);
            }
        }

        public Task SaveTokenAsync(TokenInfo token)
        {
            lock (_lock)
            {
                _token = token;
            }
            return Task.CompletedTask;
        }

        public Task ClearTokenAsync()
        {
            lock (_lock)
            {
                _token = null;
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Encrypted file-based token storage.
    /// Uses DPAPI on Windows, or a key file on other platforms.
    /// </summary>
    public class EncryptedFileTokenStorage : ITokenStorage
    {
        private readonly string _filePath;
        private readonly byte[]? _encryptionKey;
        private readonly ILogger<EncryptedFileTokenStorage>? _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public EncryptedFileTokenStorage(
            string? storagePath = null,
            byte[]? encryptionKey = null,
            ILogger<EncryptedFileTokenStorage>? logger = null)
        {
            _logger = logger;
            _encryptionKey = encryptionKey;

            // Default storage path
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appFolder = Path.Combine(appDataPath, "RoslynMcpServer", "auth");
                Directory.CreateDirectory(appFolder);
                storagePath = Path.Combine(appFolder, "tokens.dat");
            }

            _filePath = storagePath;
        }

        public async Task<TokenInfo?> GetTokenAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_filePath))
                    return null;

                var encryptedData = await File.ReadAllBytesAsync(_filePath);
                var decryptedData = Decrypt(encryptedData);
                var json = Encoding.UTF8.GetString(decryptedData);

                return JsonSerializer.Deserialize<TokenInfo>(json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to read token from storage");
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveTokenAsync(TokenInfo token)
        {
            await _lock.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(token);
                var data = Encoding.UTF8.GetBytes(json);
                var encryptedData = Encrypt(data);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(_filePath, encryptedData);
                _logger?.LogDebug("Token saved to storage");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save token to storage");
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task ClearTokenAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                    _logger?.LogDebug("Token cleared from storage");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to clear token from storage");
            }
            finally
            {
                _lock.Release();
            }
        }

        private byte[] Encrypt(byte[] data)
        {
            if (OperatingSystem.IsWindows() && _encryptionKey == null)
            {
                // Use DPAPI on Windows
                return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            }

            // Use AES encryption with provided key or generated key
            var key = _encryptionKey ?? GetOrCreateEncryptionKey();
            return EncryptAes(data, key);
        }

        private byte[] Decrypt(byte[] data)
        {
            if (OperatingSystem.IsWindows() && _encryptionKey == null)
            {
                // Use DPAPI on Windows
                return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            }

            // Use AES decryption
            var key = _encryptionKey ?? GetOrCreateEncryptionKey();
            return DecryptAes(data, key);
        }

        private byte[] GetOrCreateEncryptionKey()
        {
            var keyPath = Path.Combine(
                Path.GetDirectoryName(_filePath) ?? ".",
                ".encryption_key");

            if (File.Exists(keyPath))
            {
                return File.ReadAllBytes(keyPath);
            }

            // Generate new key
            var key = new byte[32]; // 256-bit key
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(key);

            // Save key with restricted permissions
            File.WriteAllBytes(keyPath, key);

            // Try to set restrictive permissions (Unix-like systems)
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch
            {
                // Ignore permission errors on unsupported platforms
            }

            return key;
        }

        private static byte[] EncryptAes(byte[] data, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);

            // Prepend IV to encrypted data
            var result = new byte[aes.IV.Length + encrypted.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

            return result;
        }

        private static byte[] DecryptAes(byte[] data, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;

            // Extract IV from data
            var iv = new byte[aes.BlockSize / 8];
            Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
            aes.IV = iv;

            // Extract encrypted data
            var encrypted = new byte[data.Length - iv.Length];
            Buffer.BlockCopy(data, iv.Length, encrypted, 0, encrypted.Length);

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        }
    }

    /// <summary>
    /// Environment variable-based token storage (for containerized environments).
    /// Tokens are stored in environment variables (not recommended for production).
    /// </summary>
    public class EnvironmentTokenStorage : ITokenStorage
    {
        private const string AccessTokenVar = "MCP_ACCESS_TOKEN";
        private const string RefreshTokenVar = "MCP_REFRESH_TOKEN";
        private const string ExpiresAtVar = "MCP_TOKEN_EXPIRES_AT";

        public Task<TokenInfo?> GetTokenAsync()
        {
            var accessToken = Environment.GetEnvironmentVariable(AccessTokenVar);
            if (string.IsNullOrWhiteSpace(accessToken))
                return Task.FromResult<TokenInfo?>(null);

            var expiresAtStr = Environment.GetEnvironmentVariable(ExpiresAtVar);
            var expiresAt = DateTime.TryParse(expiresAtStr, out var dt)
                ? dt
                : DateTime.UtcNow.AddHours(1);

            return Task.FromResult<TokenInfo?>(new TokenInfo
            {
                AccessToken = accessToken,
                RefreshToken = Environment.GetEnvironmentVariable(RefreshTokenVar),
                ExpiresAt = expiresAt
            });
        }

        public Task SaveTokenAsync(TokenInfo token)
        {
            Environment.SetEnvironmentVariable(AccessTokenVar, token.AccessToken);
            Environment.SetEnvironmentVariable(RefreshTokenVar, token.RefreshToken);
            Environment.SetEnvironmentVariable(ExpiresAtVar, token.ExpiresAt.ToString("O"));
            return Task.CompletedTask;
        }

        public Task ClearTokenAsync()
        {
            Environment.SetEnvironmentVariable(AccessTokenVar, null);
            Environment.SetEnvironmentVariable(RefreshTokenVar, null);
            Environment.SetEnvironmentVariable(ExpiresAtVar, null);
            return Task.CompletedTask;
        }
    }
}
