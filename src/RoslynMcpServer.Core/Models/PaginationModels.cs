using System.Text;
using System.Text.Json;

namespace RoslynMcpServer.Core.Models
{
    /// <summary>
    /// Represents a cursor for pagination, encoding the current position in the result set.
    /// MCP specification recommends cursor-based pagination for large result sets.
    /// </summary>
    public class PaginationCursor
    {
        /// <summary>
        /// The offset (number of items to skip)
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// Optional: A unique identifier for the last item (for keyset pagination)
        /// </summary>
        public string? LastItemId { get; set; }

        /// <summary>
        /// Optional: Sort key value of the last item
        /// </summary>
        public string? LastSortValue { get; set; }

        /// <summary>
        /// Timestamp when the cursor was created (for cache validation)
        /// </summary>
        public long CreatedAt { get; set; }

        /// <summary>
        /// Hash of the query parameters (to detect if query changed)
        /// </summary>
        public string? QueryHash { get; set; }

        /// <summary>
        /// Encodes the cursor to a Base64 string for transmission
        /// </summary>
        public string Encode()
        {
            var json = JsonSerializer.Serialize(this);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Decodes a cursor from a Base64 string
        /// </summary>
        public static PaginationCursor? Decode(string? encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded))
                return null;

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                return JsonSerializer.Deserialize<PaginationCursor>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a new cursor for the next page
        /// </summary>
        public static PaginationCursor CreateNext(int currentOffset, int pageSize, string? lastItemId = null, string? queryHash = null)
        {
            return new PaginationCursor
            {
                Offset = currentOffset + pageSize,
                LastItemId = lastItemId,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                QueryHash = queryHash
            };
        }
    }

    /// <summary>
    /// Generic paginated response wrapper following MCP conventions
    /// </summary>
    /// <typeparam name="T">The type of items in the result set</typeparam>
    public class PaginatedResult<T>
    {
        /// <summary>
        /// The items for the current page
        /// </summary>
        public List<T> Items { get; set; } = new();

        /// <summary>
        /// Total number of items across all pages (if known)
        /// </summary>
        public int? TotalCount { get; set; }

        /// <summary>
        /// Number of items in the current page
        /// </summary>
        public int Count => Items.Count;

        /// <summary>
        /// The current page number (1-based)
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// Number of items per page
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Whether there are more items after this page
        /// </summary>
        public bool HasMore { get; set; }

        /// <summary>
        /// Cursor for fetching the next page (null if no more pages)
        /// </summary>
        public string? NextCursor { get; set; }

        /// <summary>
        /// Cursor for fetching the previous page (null if on first page)
        /// </summary>
        public string? PreviousCursor { get; set; }

        /// <summary>
        /// Total number of pages (if TotalCount is known)
        /// </summary>
        public int? TotalPages => TotalCount.HasValue && PageSize > 0
            ? (int)Math.Ceiling((double)TotalCount.Value / PageSize)
            : null;

        /// <summary>
        /// Creates an empty paginated result
        /// </summary>
        public static PaginatedResult<T> Empty(int pageSize = 20)
        {
            return new PaginatedResult<T>
            {
                Items = new List<T>(),
                TotalCount = 0,
                Page = 1,
                PageSize = pageSize,
                HasMore = false
            };
        }

        /// <summary>
        /// Creates a paginated result from a full list of items
        /// </summary>
        public static PaginatedResult<T> FromList(
            IEnumerable<T> allItems,
            int page = 1,
            int pageSize = 20,
            string? queryHash = null)
        {
            var itemList = allItems.ToList();
            var totalCount = itemList.Count;
            var offset = (page - 1) * pageSize;
            var pageItems = itemList.Skip(offset).Take(pageSize).ToList();
            var hasMore = offset + pageItems.Count < totalCount;

            var result = new PaginatedResult<T>
            {
                Items = pageItems,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasMore = hasMore
            };

            if (hasMore)
            {
                var nextCursor = PaginationCursor.CreateNext(offset, pageSize, queryHash: queryHash);
                result.NextCursor = nextCursor.Encode();
            }

            if (page > 1)
            {
                var prevCursor = new PaginationCursor
                {
                    Offset = Math.Max(0, offset - pageSize),
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    QueryHash = queryHash
                };
                result.PreviousCursor = prevCursor.Encode();
            }

            return result;
        }

        /// <summary>
        /// Creates a paginated result using cursor-based pagination
        /// </summary>
        public static PaginatedResult<T> FromCursor(
            IEnumerable<T> allItems,
            string? cursor,
            int pageSize = 20,
            string? queryHash = null)
        {
            var itemList = allItems.ToList();
            var totalCount = itemList.Count;

            var decodedCursor = PaginationCursor.Decode(cursor);
            var offset = decodedCursor?.Offset ?? 0;

            // Validate cursor hasn't expired (24 hours) or query changed
            if (decodedCursor != null)
            {
                var cursorAge = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - decodedCursor.CreatedAt;
                if (cursorAge > 86400) // 24 hours
                {
                    offset = 0; // Reset to beginning if cursor expired
                }

                if (queryHash != null && decodedCursor.QueryHash != queryHash)
                {
                    offset = 0; // Reset if query parameters changed
                }
            }

            var page = (offset / pageSize) + 1;
            var pageItems = itemList.Skip(offset).Take(pageSize).ToList();
            var hasMore = offset + pageItems.Count < totalCount;

            var result = new PaginatedResult<T>
            {
                Items = pageItems,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasMore = hasMore
            };

            if (hasMore)
            {
                var nextCursor = PaginationCursor.CreateNext(offset, pageSize, queryHash: queryHash);
                result.NextCursor = nextCursor.Encode();
            }

            if (offset > 0)
            {
                var prevCursor = new PaginationCursor
                {
                    Offset = Math.Max(0, offset - pageSize),
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    QueryHash = queryHash
                };
                result.PreviousCursor = prevCursor.Encode();
            }

            return result;
        }
    }

    /// <summary>
    /// Request parameters for paginated operations
    /// </summary>
    public class PaginationRequest
    {
        /// <summary>
        /// Cursor for continuation (takes precedence over Page)
        /// </summary>
        public string? Cursor { get; set; }

        /// <summary>
        /// Page number (1-based, used if Cursor is not provided)
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// Number of items per page (default: 20, max: 100)
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// Validates and normalizes the pagination request
        /// </summary>
        public void Validate()
        {
            if (Page < 1) Page = 1;
            if (PageSize < 1) PageSize = 20;
            if (PageSize > 100) PageSize = 100;
        }

        /// <summary>
        /// Computes a hash of the request parameters for cursor validation
        /// </summary>
        public string ComputeQueryHash(params string[] additionalParams)
        {
            var combined = string.Join("|", additionalParams.Where(p => p != null));
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(bytes).Substring(0, 16);
        }
    }

    /// <summary>
    /// Extension methods for formatting paginated results
    /// </summary>
    public static class PaginationFormatExtensions
    {
        /// <summary>
        /// Formats pagination metadata as a string header
        /// </summary>
        public static string FormatPaginationHeader<T>(this PaginatedResult<T> result)
        {
            var sb = new StringBuilder();

            if (result.TotalCount.HasValue)
            {
                sb.AppendLine($"Showing {result.Count} of {result.TotalCount} total items (Page {result.Page}/{result.TotalPages})");
            }
            else
            {
                sb.AppendLine($"Showing {result.Count} items (Page {result.Page})");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats pagination footer with cursor information
        /// </summary>
        public static string FormatPaginationFooter<T>(this PaginatedResult<T> result)
        {
            if (!result.HasMore)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"More results available. Use cursor to fetch next page:");
            sb.AppendLine($"  nextCursor: {result.NextCursor}");

            return sb.ToString();
        }

        /// <summary>
        /// Formats a complete paginated response with header and footer
        /// </summary>
        public static string FormatPaginatedResponse<T>(
            this PaginatedResult<T> result,
            Func<T, string> itemFormatter,
            string title = "Results")
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine($"# {title}");
            sb.AppendLine();
            sb.Append(result.FormatPaginationHeader());
            sb.AppendLine();

            // Items
            foreach (var item in result.Items)
            {
                sb.AppendLine(itemFormatter(item));
            }

            // Footer
            sb.Append(result.FormatPaginationFooter());

            return sb.ToString();
        }
    }
}
