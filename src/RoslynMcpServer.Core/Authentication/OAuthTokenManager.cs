using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynMcpServer.Core.Authentication
{
    /// <summary>
    /// Manages OAuth 2.0 tokens for MCP authentication.
    /// Supports token acquisition, refresh, validation, and secure storage.
    /// </summary>
    public class OAuthTokenManager : IDisposable
    {
        private readonly OAuthConfiguration _config;
        private readonly ILogger<OAuthTokenManager> _logger;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);
        private readonly ITokenStorage _tokenStorage;

        private TokenInfo? _currentToken;
        private bool _disposed;

        public OAuthTokenManager(
            OAuthConfiguration config,
            ILogger<OAuthTokenManager> logger,
            ITokenStorage? tokenStorage = null,
            HttpClient? httpClient = null)
        {
            _config = config;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient();
            _tokenStorage = tokenStorage ?? new InMemoryTokenStorage();
        }

        /// <summary>
        /// Gets a valid access token, refreshing if necessary.
        /// </summary>
        public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            if (!_config.Enabled)
                return null;

            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                // Try to get cached token
                _currentToken ??= await _tokenStorage.GetTokenAsync();

                // Check if token is valid
                if (_currentToken != null && !_currentToken.IsExpired())
                {
                    return _currentToken.AccessToken;
                }

                // Try to refresh if we have a refresh token
                if (_currentToken?.RefreshToken != null)
                {
                    var refreshed = await RefreshTokenAsync(_currentToken.RefreshToken, cancellationToken);
                    if (refreshed != null)
                    {
                        _currentToken = refreshed;
                        await _tokenStorage.SaveTokenAsync(refreshed);
                        return refreshed.AccessToken;
                    }
                }

                _logger.LogWarning("No valid token available. Authorization required.");
                return null;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        /// <summary>
        /// Initiates the OAuth authorization code flow with PKCE.
        /// Returns the authorization URL for the user to visit.
        /// </summary>
        public AuthorizationRequest CreateAuthorizationRequest(string? state = null)
        {
            if (string.IsNullOrWhiteSpace(_config.AuthorizationEndpoint))
                throw new InvalidOperationException("Authorization endpoint not configured");

            // Generate PKCE code verifier and challenge
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);

            // Generate state if not provided
            state ??= GenerateState();

            var queryParams = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = _config.ClientId ?? throw new InvalidOperationException("ClientId not configured"),
                ["scope"] = string.Join(" ", _config.Scopes),
                ["state"] = state,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = _config.Pkce.CodeChallengeMethod
            };

            // Add resource indicator (RFC 8707)
            if (!string.IsNullOrWhiteSpace(_config.ResourceIdentifier))
            {
                queryParams["resource"] = _config.ResourceIdentifier;
            }

            var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var authorizationUrl = $"{_config.AuthorizationEndpoint}?{queryString}";

            return new AuthorizationRequest
            {
                AuthorizationUrl = authorizationUrl,
                CodeVerifier = codeVerifier,
                State = state
            };
        }

        /// <summary>
        /// Exchanges an authorization code for tokens.
        /// </summary>
        public async Task<TokenInfo?> ExchangeCodeAsync(
            string code,
            string codeVerifier,
            string? redirectUri = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_config.TokenEndpoint))
                throw new InvalidOperationException("Token endpoint not configured");

            var requestParams = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = _config.ClientId!,
                ["code_verifier"] = codeVerifier
            };

            if (!string.IsNullOrWhiteSpace(redirectUri))
            {
                requestParams["redirect_uri"] = redirectUri;
            }

            // Add resource indicator (RFC 8707)
            if (!string.IsNullOrWhiteSpace(_config.ResourceIdentifier))
            {
                requestParams["resource"] = _config.ResourceIdentifier;
            }

            // Add client secret if configured (confidential client)
            if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                requestParams["client_secret"] = _config.ClientSecret;
            }

            return await RequestTokenAsync(requestParams, cancellationToken);
        }

        /// <summary>
        /// Refreshes an access token using a refresh token.
        /// </summary>
        public async Task<TokenInfo?> RefreshTokenAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_config.TokenEndpoint))
                throw new InvalidOperationException("Token endpoint not configured");

            var requestParams = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _config.ClientId!
            };

            // Add resource indicator (RFC 8707)
            if (!string.IsNullOrWhiteSpace(_config.ResourceIdentifier))
            {
                requestParams["resource"] = _config.ResourceIdentifier;
            }

            // Add client secret if configured
            if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                requestParams["client_secret"] = _config.ClientSecret;
            }

            try
            {
                return await RequestTokenAsync(requestParams, cancellationToken);
            }
            catch (OAuthException ex) when (ex.Error == "invalid_grant")
            {
                _logger.LogWarning("Refresh token is invalid or expired");
                return null;
            }
        }

        /// <summary>
        /// Revokes a token.
        /// </summary>
        public async Task<bool> RevokeTokenAsync(
            string token,
            string tokenTypeHint = "access_token",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_config.RevocationEndpoint))
            {
                _logger.LogWarning("Revocation endpoint not configured");
                return false;
            }

            var requestParams = new Dictionary<string, string>
            {
                ["token"] = token,
                ["token_type_hint"] = tokenTypeHint,
                ["client_id"] = _config.ClientId!
            };

            if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                requestParams["client_secret"] = _config.ClientSecret;
            }

            try
            {
                var content = new FormUrlEncodedContent(requestParams);
                var response = await _httpClient.PostAsync(_config.RevocationEndpoint, content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to revoke token");
                return false;
            }
        }

        /// <summary>
        /// Validates an access token and returns the claims.
        /// </summary>
        public TokenValidationResult ValidateToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();

                if (!handler.CanReadToken(token))
                {
                    return TokenValidationResult.Invalid("Token is not a valid JWT");
                }

                var jwt = handler.ReadJwtToken(token);

                // Validate issuer
                if (_config.TokenValidation.ValidateIssuer &&
                    !string.IsNullOrWhiteSpace(_config.Issuer) &&
                    jwt.Issuer != _config.Issuer)
                {
                    return TokenValidationResult.Invalid($"Invalid issuer: {jwt.Issuer}");
                }

                // Validate audience
                if (_config.TokenValidation.ValidateAudience &&
                    !string.IsNullOrWhiteSpace(_config.Audience) &&
                    !jwt.Audiences.Contains(_config.Audience))
                {
                    return TokenValidationResult.Invalid("Invalid audience");
                }

                // Validate lifetime
                if (_config.TokenValidation.ValidateLifetime)
                {
                    var clockSkew = TimeSpan.FromSeconds(_config.TokenValidation.ClockSkewSeconds);
                    var now = DateTime.UtcNow;

                    if (jwt.ValidTo.Add(clockSkew) < now)
                    {
                        return TokenValidationResult.Invalid("Token has expired");
                    }

                    if (jwt.ValidFrom.Subtract(clockSkew) > now)
                    {
                        return TokenValidationResult.Invalid("Token is not yet valid");
                    }
                }

                // Validate algorithm
                if (_config.TokenValidation.RequireSignedTokens &&
                    !_config.TokenValidation.AllowedAlgorithms.Contains(jwt.Header.Alg))
                {
                    return TokenValidationResult.Invalid($"Invalid signing algorithm: {jwt.Header.Alg}");
                }

                return TokenValidationResult.Valid(jwt.Claims);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token validation failed");
                return TokenValidationResult.Invalid($"Validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the cached token.
        /// </summary>
        public async Task ClearTokenAsync()
        {
            await _tokenLock.WaitAsync();
            try
            {
                _currentToken = null;
                await _tokenStorage.ClearTokenAsync();
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        /// <summary>
        /// Stores a token received from an external authorization flow.
        /// </summary>
        public async Task StoreTokenAsync(TokenInfo token)
        {
            await _tokenLock.WaitAsync();
            try
            {
                _currentToken = token;
                await _tokenStorage.SaveTokenAsync(token);
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private async Task<TokenInfo?> RequestTokenAsync(
            Dictionary<string, string> requestParams,
            CancellationToken cancellationToken)
        {
            var content = new FormUrlEncodedContent(requestParams);

            var response = await _httpClient.PostAsync(_config.TokenEndpoint, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<OAuthErrorResponse>(responseBody);
                _logger.LogError("Token request failed: {Error} - {Description}",
                    error?.Error, error?.ErrorDescription);
                throw new OAuthException(error?.Error ?? "unknown", error?.ErrorDescription);
            }

            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseBody);
            if (tokenResponse == null)
            {
                throw new OAuthException("invalid_response", "Failed to parse token response");
            }

            var tokenInfo = new TokenInfo
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                TokenType = tokenResponse.TokenType,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                Scopes = tokenResponse.Scope?.Split(' ').ToList() ?? new List<string>()
            };

            _logger.LogInformation("Token acquired successfully. Expires at {ExpiresAt}", tokenInfo.ExpiresAt);
            return tokenInfo;
        }

        private string GenerateCodeVerifier()
        {
            var bytes = new byte[_config.Pkce.CodeVerifierLength];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        private string GenerateCodeChallenge(string codeVerifier)
        {
            if (_config.Pkce.CodeChallengeMethod == "plain")
            {
                return codeVerifier;
            }

            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(bytes);
        }

        private static string GenerateState()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tokenLock.Dispose();
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Information about an authorization request.
    /// </summary>
    public class AuthorizationRequest
    {
        /// <summary>
        /// The URL to redirect the user to for authorization.
        /// </summary>
        public string AuthorizationUrl { get; set; } = string.Empty;

        /// <summary>
        /// The PKCE code verifier (must be stored securely for token exchange).
        /// </summary>
        public string CodeVerifier { get; set; } = string.Empty;

        /// <summary>
        /// The state parameter for CSRF protection.
        /// </summary>
        public string State { get; set; } = string.Empty;
    }

    /// <summary>
    /// Token information.
    /// </summary>
    public class TokenInfo
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public string TokenType { get; set; } = "Bearer";
        public DateTime ExpiresAt { get; set; }
        public List<string> Scopes { get; set; } = new();

        /// <summary>
        /// Checks if the token is expired (with 30 second buffer).
        /// </summary>
        public bool IsExpired() => DateTime.UtcNow.AddSeconds(30) >= ExpiresAt;
    }

    /// <summary>
    /// Result of token validation.
    /// </summary>
    public class TokenValidationResult
    {
        public bool IsValid { get; private set; }
        public string? Error { get; private set; }
        public IEnumerable<Claim> Claims { get; private set; } = Enumerable.Empty<Claim>();

        public static TokenValidationResult Valid(IEnumerable<Claim> claims)
            => new() { IsValid = true, Claims = claims };

        public static TokenValidationResult Invalid(string error)
            => new() { IsValid = false, Error = error };
    }

    /// <summary>
    /// OAuth error response.
    /// </summary>
    public class OAuthErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }

        [JsonPropertyName("error_uri")]
        public string? ErrorUri { get; set; }
    }

    /// <summary>
    /// OAuth token response.
    /// </summary>
    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    /// <summary>
    /// OAuth exception.
    /// </summary>
    public class OAuthException : Exception
    {
        public string Error { get; }
        public string? ErrorDescription { get; }

        public OAuthException(string error, string? errorDescription = null)
            : base(errorDescription ?? error)
        {
            Error = error;
            ErrorDescription = errorDescription;
        }
    }
}
