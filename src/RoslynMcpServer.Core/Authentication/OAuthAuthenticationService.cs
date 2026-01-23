using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;

namespace RoslynMcpServer.Core.Authentication
{
    /// <summary>
    /// Provides OAuth authentication services for MCP tools.
    /// Supports token validation, scope checking, and authorization.
    /// </summary>
    public class OAuthAuthenticationService
    {
        private readonly OAuthConfiguration _config;
        private readonly OAuthTokenManager _tokenManager;
        private readonly ILogger<OAuthAuthenticationService> _logger;

        /// <summary>
        /// Gets the current authentication state.
        /// </summary>
        public AuthenticationState CurrentState { get; private set; } = AuthenticationState.NotAuthenticated;

        /// <summary>
        /// Gets the current user's claims (if authenticated).
        /// </summary>
        public ClaimsPrincipal? CurrentUser { get; private set; }

        /// <summary>
        /// Event raised when authentication state changes.
        /// </summary>
        public event EventHandler<AuthenticationStateChangedEventArgs>? AuthenticationStateChanged;

        public OAuthAuthenticationService(
            OAuthConfiguration config,
            OAuthTokenManager tokenManager,
            ILogger<OAuthAuthenticationService> logger)
        {
            _config = config;
            _tokenManager = tokenManager;
            _logger = logger;
        }

        /// <summary>
        /// Checks if OAuth authentication is required.
        /// </summary>
        public bool IsAuthenticationRequired => _config.Enabled;

        /// <summary>
        /// Checks if the current session is authenticated.
        /// </summary>
        public bool IsAuthenticated => CurrentState == AuthenticationState.Authenticated;

        /// <summary>
        /// Authenticates using an existing access token.
        /// </summary>
        public async Task<AuthenticationResult> AuthenticateWithTokenAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = _tokenManager.ValidateToken(accessToken);

                if (!validationResult.IsValid)
                {
                    SetAuthenticationState(AuthenticationState.Failed);
                    return AuthenticationResult.Failed(validationResult.Error ?? "Token validation failed");
                }

                // Create claims principal
                var identity = new ClaimsIdentity(validationResult.Claims, "OAuth");
                CurrentUser = new ClaimsPrincipal(identity);

                // Store the token
                await _tokenManager.StoreTokenAsync(new TokenInfo
                {
                    AccessToken = accessToken,
                    ExpiresAt = DateTime.UtcNow.AddHours(1) // Default if not specified in token
                });

                SetAuthenticationState(AuthenticationState.Authenticated);
                _logger.LogInformation("Authentication successful");

                return AuthenticationResult.Success(CurrentUser);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication failed");
                SetAuthenticationState(AuthenticationState.Failed);
                return AuthenticationResult.Failed($"Authentication error: {ex.Message}");
            }
        }

        /// <summary>
        /// Initiates the OAuth authorization code flow.
        /// </summary>
        public AuthorizationFlowResult InitiateAuthorizationFlow(string? redirectUri = null)
        {
            if (!_config.Enabled)
            {
                return AuthorizationFlowResult.NotRequired();
            }

            try
            {
                var authRequest = _tokenManager.CreateAuthorizationRequest();
                SetAuthenticationState(AuthenticationState.Pending);

                return AuthorizationFlowResult.Initiated(
                    authRequest.AuthorizationUrl,
                    authRequest.State,
                    authRequest.CodeVerifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate authorization flow");
                return AuthorizationFlowResult.Failed($"Failed to initiate authorization: {ex.Message}");
            }
        }

        /// <summary>
        /// Completes the authorization flow by exchanging the code for tokens.
        /// </summary>
        public async Task<AuthenticationResult> CompleteAuthorizationAsync(
            string code,
            string codeVerifier,
            string? state = null,
            string? expectedState = null,
            string? redirectUri = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate state if provided
                if (!string.IsNullOrEmpty(expectedState) && state != expectedState)
                {
                    SetAuthenticationState(AuthenticationState.Failed);
                    return AuthenticationResult.Failed("State mismatch - possible CSRF attack");
                }

                var tokenInfo = await _tokenManager.ExchangeCodeAsync(
                    code, codeVerifier, redirectUri, cancellationToken);

                if (tokenInfo == null)
                {
                    SetAuthenticationState(AuthenticationState.Failed);
                    return AuthenticationResult.Failed("Failed to exchange authorization code");
                }

                // Validate and set up authentication
                var validationResult = _tokenManager.ValidateToken(tokenInfo.AccessToken);

                if (!validationResult.IsValid)
                {
                    SetAuthenticationState(AuthenticationState.Failed);
                    return AuthenticationResult.Failed(validationResult.Error ?? "Token validation failed");
                }

                // Create claims principal
                var identity = new ClaimsIdentity(validationResult.Claims, "OAuth");
                CurrentUser = new ClaimsPrincipal(identity);

                SetAuthenticationState(AuthenticationState.Authenticated);
                _logger.LogInformation("Authorization flow completed successfully");

                return AuthenticationResult.Success(CurrentUser);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authorization flow failed");
                SetAuthenticationState(AuthenticationState.Failed);
                return AuthenticationResult.Failed($"Authorization error: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the current user has the required scope.
        /// </summary>
        public bool HasScope(string scope)
        {
            if (!IsAuthenticated || CurrentUser == null)
                return false;

            var scopeClaim = CurrentUser.FindFirst("scope") ?? CurrentUser.FindFirst("scp");
            if (scopeClaim == null)
                return false;

            var scopes = scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if the current user has all required scopes.
        /// </summary>
        public bool HasAllScopes(params string[] requiredScopes)
        {
            return requiredScopes.All(HasScope);
        }

        /// <summary>
        /// Checks if the current user has any of the required scopes.
        /// </summary>
        public bool HasAnyScope(params string[] requiredScopes)
        {
            return requiredScopes.Any(HasScope);
        }

        /// <summary>
        /// Signs out the current user.
        /// </summary>
        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var accessToken = await _tokenManager.GetAccessTokenAsync(cancellationToken);
                if (!string.IsNullOrEmpty(accessToken))
                {
                    await _tokenManager.RevokeTokenAsync(accessToken, cancellationToken: cancellationToken);
                }

                await _tokenManager.ClearTokenAsync();
                CurrentUser = null;
                SetAuthenticationState(AuthenticationState.NotAuthenticated);

                _logger.LogInformation("User signed out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sign out");
                // Still clear local state even if revocation fails
                CurrentUser = null;
                SetAuthenticationState(AuthenticationState.NotAuthenticated);
            }
        }

        /// <summary>
        /// Gets the Protected Resource Metadata for this MCP server.
        /// </summary>
        public ProtectedResourceMetadata GetResourceMetadata()
        {
            return new ProtectedResourceMetadata
            {
                Resource = _config.ResourceIdentifier ?? "mcp://roslyn-mcp-server",
                AuthorizationServers = string.IsNullOrWhiteSpace(_config.Issuer)
                    ? new List<string>()
                    : new List<string> { _config.Issuer },
                BearerMethodsSupported = new List<string> { "header" },
                ScopesSupported = _config.Scopes,
                ResourceDocumentation = "https://github.com/anthropics/roslyn-mcp"
            };
        }

        /// <summary>
        /// Validates a request's authorization header.
        /// </summary>
        public AuthorizationValidationResult ValidateAuthorizationHeader(string? authorizationHeader)
        {
            if (!_config.Enabled)
            {
                return AuthorizationValidationResult.NotRequired();
            }

            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return AuthorizationValidationResult.Missing();
            }

            if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return AuthorizationValidationResult.Invalid("Authorization header must use Bearer scheme");
            }

            var token = authorizationHeader.Substring(7);
            var validationResult = _tokenManager.ValidateToken(token);

            if (!validationResult.IsValid)
            {
                return AuthorizationValidationResult.Invalid(validationResult.Error ?? "Token validation failed");
            }

            return AuthorizationValidationResult.Valid(validationResult.Claims);
        }

        private void SetAuthenticationState(AuthenticationState newState)
        {
            var oldState = CurrentState;
            CurrentState = newState;

            if (oldState != newState)
            {
                AuthenticationStateChanged?.Invoke(this, new AuthenticationStateChangedEventArgs(oldState, newState));
            }
        }
    }

    /// <summary>
    /// Authentication state.
    /// </summary>
    public enum AuthenticationState
    {
        NotAuthenticated,
        Pending,
        Authenticated,
        Failed
    }

    /// <summary>
    /// Event args for authentication state changes.
    /// </summary>
    public class AuthenticationStateChangedEventArgs : EventArgs
    {
        public AuthenticationState OldState { get; }
        public AuthenticationState NewState { get; }

        public AuthenticationStateChangedEventArgs(AuthenticationState oldState, AuthenticationState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    /// <summary>
    /// Result of an authentication attempt.
    /// </summary>
    public class AuthenticationResult
    {
        public bool IsSuccess { get; private set; }
        public string? Error { get; private set; }
        public ClaimsPrincipal? User { get; private set; }

        public static AuthenticationResult Success(ClaimsPrincipal user)
            => new() { IsSuccess = true, User = user };

        public static AuthenticationResult Failed(string error)
            => new() { IsSuccess = false, Error = error };
    }

    /// <summary>
    /// Result of initiating an authorization flow.
    /// </summary>
    public class AuthorizationFlowResult
    {
        public bool IsRequired { get; private set; }
        public bool IsSuccess { get; private set; }
        public string? AuthorizationUrl { get; private set; }
        public string? State { get; private set; }
        public string? CodeVerifier { get; private set; }
        public string? ErrorMessage { get; private set; }

        public static AuthorizationFlowResult NotRequired()
            => new() { IsRequired = false, IsSuccess = true };

        public static AuthorizationFlowResult Initiated(string authUrl, string state, string codeVerifier)
            => new()
            {
                IsRequired = true,
                IsSuccess = true,
                AuthorizationUrl = authUrl,
                State = state,
                CodeVerifier = codeVerifier
            };

        public static AuthorizationFlowResult Failed(string error)
            => new() { IsRequired = true, IsSuccess = false, ErrorMessage = error };
    }

    /// <summary>
    /// Result of authorization header validation.
    /// </summary>
    public class AuthorizationValidationResult
    {
        public bool IsRequired { get; private set; }
        public bool IsValid { get; private set; }
        public string? Error { get; private set; }
        public IEnumerable<Claim> Claims { get; private set; } = Enumerable.Empty<Claim>();

        public static AuthorizationValidationResult NotRequired()
            => new() { IsRequired = false, IsValid = true };

        public static AuthorizationValidationResult Missing()
            => new() { IsRequired = true, IsValid = false, Error = "Authorization header required" };

        public static AuthorizationValidationResult Invalid(string error)
            => new() { IsRequired = true, IsValid = false, Error = error };

        public static AuthorizationValidationResult Valid(IEnumerable<Claim> claims)
            => new() { IsRequired = true, IsValid = true, Claims = claims };
    }
}
