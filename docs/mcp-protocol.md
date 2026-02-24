# MCP Protocol Features

## JSON-RPC Error Handling

Standardized error codes defined in `Core/Models/McpErrorModels.cs`:

| Code | Constant | Description |
|------|----------|-------------|
| -32700 | `ParseError` | Invalid JSON |
| -32600 | `InvalidRequest` | Invalid request object |
| -32601 | `MethodNotFound` | Method not found |
| -32602 | `InvalidParams` | Invalid parameters |
| -32603 | `InternalError` | Internal error |
| -32001 | `SolutionNotFound` | Solution file not found |
| -32002 | `InvalidPath` | Path validation failed |
| -32003 | `SymbolNotFound` | Symbol not found |
| -32004 | `OperationTimeout` | Operation timed out |
| -32005 | `AccessDenied` | Access denied |
| -32006 | `ServiceUnavailable` | Service unavailable |
| -32010 | `OperationCancelled` | Cancelled by client |

**Error response format**:
```
Error [-32003]: Symbol not found: MyClass
Reason: No matching symbol in solution
Path: C:\Projects\MySolution.sln
```

---

## Cursor-based Pagination

Tools support pagination via `pageSize` and `cursor` parameters (`Core/Models/PaginationModels.cs`):

```csharp
// First page
SearchSymbols(pattern: "*Service", solutionPath: "...", pageSize: 20)
// → "Showing 20 of 150 total items (Page 1/8)\nnextCursor: eyJPZmZzZXQiOjIwLC..."

// Next page
SearchSymbols(pattern: "*Service", solutionPath: "...", cursor: "eyJPZmZzZXQiOjIwLC...")
```

**Cursor properties**:
- Base64-encoded JSON containing offset and query hash
- 24-hour expiration
- Query hash validation prevents reuse with different parameters

---

## Request Cancellation

Operations support cancellation via `CancellationManager` (`Core/Services/CancellationManager.cs`):

- Tracks in-progress requests with unique IDs
- Supports MCP `notifications/cancelled` pattern
- Automatic timeout (default: 5 minutes)
- Proper cleanup of stale entries

**Request lifecycle**: `Running` → `Completed` | `Cancelled` | `Failed`

---

## OAuth 2.0 Authentication (Optional)

For remote deployments. Configuration in `appsettings.json`:

```json
{
  "OAuth": {
    "Enabled": true,
    "Issuer": "https://auth.example.com",
    "AuthorizationEndpoint": "https://auth.example.com/authorize",
    "TokenEndpoint": "https://auth.example.com/token",
    "ClientId": "roslyn-mcp-server",
    "ResourceIdentifier": "mcp://roslyn-mcp-server",
    "Scopes": ["mcp.read", "mcp.write"],
    "Pkce": {
      "Required": true,
      "CodeChallengeMethod": "S256"
    }
  }
}
```

**Features**:
- OAuth 2.1 Authorization Code Flow
- PKCE (mandatory per MCP spec)
- Resource Indicators (RFC 8707)
- Protected Resource Metadata (RFC 9728)
- Secure token storage (DPAPI on Windows, AES elsewhere)
- Automatic token refresh

**Token storage options**:

| Class | Use Case |
|-------|----------|
| `InMemoryTokenStorage` | Development / testing |
| `EncryptedFileTokenStorage` | Production (default) |
| `EnvironmentTokenStorage` | Container environments |
