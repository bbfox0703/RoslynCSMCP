using System.Text.Json.Serialization;

namespace RoslynMcpServer.Core.Authentication
{
    /// <summary>
    /// OAuth 2.0 configuration for MCP server authentication.
    /// Follows MCP Authorization Specification (2025-11-25).
    ///
    /// Reference:
    /// - https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization
    /// - RFC 8707 (Resource Indicators for OAuth 2.0)
    /// - RFC 9728 (OAuth 2.0 Protected Resource Metadata)
    /// </summary>
    public class OAuthConfiguration
    {
        /// <summary>
        /// Whether OAuth authentication is enabled (default: false for local servers)
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The authorization server's issuer URL
        /// </summary>
        public string? Issuer { get; set; }

        /// <summary>
        /// Authorization endpoint URL
        /// </summary>
        public string? AuthorizationEndpoint { get; set; }

        /// <summary>
        /// Token endpoint URL
        /// </summary>
        public string? TokenEndpoint { get; set; }

        /// <summary>
        /// Token revocation endpoint URL (optional)
        /// </summary>
        public string? RevocationEndpoint { get; set; }

        /// <summary>
        /// Token introspection endpoint URL (optional)
        /// </summary>
        public string? IntrospectionEndpoint { get; set; }

        /// <summary>
        /// JWKS (JSON Web Key Set) endpoint URL for token validation
        /// </summary>
        public string? JwksUri { get; set; }

        /// <summary>
        /// Client ID for this MCP server
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Client secret (for confidential clients only)
        /// </summary>
        public string? ClientSecret { get; set; }

        /// <summary>
        /// OAuth scopes required for MCP operations
        /// </summary>
        public List<string> Scopes { get; set; } = new() { "mcp.read", "mcp.write" };

        /// <summary>
        /// Resource identifier for this MCP server (RFC 8707)
        /// </summary>
        public string? ResourceIdentifier { get; set; }

        /// <summary>
        /// Audience value expected in tokens
        /// </summary>
        public string? Audience { get; set; }

        /// <summary>
        /// Token validation settings
        /// </summary>
        public TokenValidationSettings TokenValidation { get; set; } = new();

        /// <summary>
        /// PKCE settings
        /// </summary>
        public PkceSettings Pkce { get; set; } = new();

        /// <summary>
        /// Validates the configuration
        /// </summary>
        public ValidationResult Validate()
        {
            if (!Enabled)
                return ValidationResult.Success();

            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Issuer))
                errors.Add("Issuer is required when OAuth is enabled");

            if (string.IsNullOrWhiteSpace(TokenEndpoint))
                errors.Add("TokenEndpoint is required when OAuth is enabled");

            if (string.IsNullOrWhiteSpace(ClientId))
                errors.Add("ClientId is required when OAuth is enabled");

            if (string.IsNullOrWhiteSpace(ResourceIdentifier))
                errors.Add("ResourceIdentifier is required for RFC 8707 compliance");

            return errors.Any()
                ? ValidationResult.Failed(errors)
                : ValidationResult.Success();
        }
    }

    /// <summary>
    /// Token validation settings
    /// </summary>
    public class TokenValidationSettings
    {
        /// <summary>
        /// Whether to validate the token issuer
        /// </summary>
        public bool ValidateIssuer { get; set; } = true;

        /// <summary>
        /// Whether to validate the audience claim
        /// </summary>
        public bool ValidateAudience { get; set; } = true;

        /// <summary>
        /// Whether to validate the token lifetime
        /// </summary>
        public bool ValidateLifetime { get; set; } = true;

        /// <summary>
        /// Clock skew tolerance for token expiration (in seconds)
        /// </summary>
        public int ClockSkewSeconds { get; set; } = 300;

        /// <summary>
        /// Whether to require signed tokens
        /// </summary>
        public bool RequireSignedTokens { get; set; } = true;

        /// <summary>
        /// Allowed signing algorithms
        /// </summary>
        public List<string> AllowedAlgorithms { get; set; } = new() { "RS256", "RS384", "RS512", "ES256", "ES384", "ES512" };
    }

    /// <summary>
    /// PKCE (Proof Key for Code Exchange) settings
    /// Per MCP spec: PKCE is mandatory
    /// </summary>
    public class PkceSettings
    {
        /// <summary>
        /// Whether PKCE is required (per MCP spec, should always be true)
        /// </summary>
        public bool Required { get; set; } = true;

        /// <summary>
        /// Code challenge method (S256 recommended)
        /// </summary>
        public string CodeChallengeMethod { get; set; } = "S256";

        /// <summary>
        /// Length of the code verifier (43-128 characters)
        /// </summary>
        public int CodeVerifierLength { get; set; } = 64;
    }

    /// <summary>
    /// Result of configuration validation
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; private set; }
        public List<string> Errors { get; private set; } = new();

        public static ValidationResult Success() => new() { IsValid = true };
        public static ValidationResult Failed(List<string> errors) => new() { IsValid = false, Errors = errors };
        public static ValidationResult Failed(string error) => new() { IsValid = false, Errors = new List<string> { error } };
    }

    /// <summary>
    /// OAuth 2.0 Protected Resource Metadata (RFC 9728)
    /// MCP servers MUST implement this to advertise their authorization requirements.
    /// </summary>
    public class ProtectedResourceMetadata
    {
        /// <summary>
        /// The protected resource identifier
        /// </summary>
        [JsonPropertyName("resource")]
        public string Resource { get; set; } = string.Empty;

        /// <summary>
        /// Authorization servers that can issue tokens for this resource
        /// </summary>
        [JsonPropertyName("authorization_servers")]
        public List<string> AuthorizationServers { get; set; } = new();

        /// <summary>
        /// Supported bearer token methods
        /// </summary>
        [JsonPropertyName("bearer_methods_supported")]
        public List<string> BearerMethodsSupported { get; set; } = new() { "header" };

        /// <summary>
        /// Scopes supported by this resource
        /// </summary>
        [JsonPropertyName("scopes_supported")]
        public List<string> ScopesSupported { get; set; } = new();

        /// <summary>
        /// Resource documentation URL
        /// </summary>
        [JsonPropertyName("resource_documentation")]
        public string? ResourceDocumentation { get; set; }

        /// <summary>
        /// Resource signing algorithms supported
        /// </summary>
        [JsonPropertyName("resource_signing_alg_values_supported")]
        public List<string>? ResourceSigningAlgValuesSupported { get; set; }
    }
}
