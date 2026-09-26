namespace RoslynMcpServer.Core.Models
{
    /// <summary>
    /// A source declaration that a requested symbol name could refer to.
    /// </summary>
    public class SymbolCandidate
    {
        /// <summary>Qualified name that can be passed back as the symbol name, e.g. 'App.Services.UserService.Save(App.Models.User, bool)'.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>class, abstract class, interface, record, struct, enum, delegate, method, property, field, event, ...</summary>
        public string Kind { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }

        public override string ToString() =>
            $"{DisplayName} ({Kind}, {Path.GetFileName(FilePath)}:{LineNumber}, project {ProjectName})";
    }

    /// <summary>
    /// A requested symbol name did not identify exactly one declaration in the solution's source: several
    /// declarations match equally well, or declarations with that name exist but none is of the kind the
    /// tool needs or fits the requested type arguments or parameter list. The message lists the candidates
    /// and is meant to be shown to the caller as is.
    /// </summary>
    public class SymbolResolutionException : ArgumentException
    {
        public SymbolResolutionException(
            string message, string requestedName, bool isAmbiguous, IReadOnlyList<SymbolCandidate> candidates)
            : base(message)
        {
            RequestedName = requestedName;
            IsAmbiguous = isAmbiguous;
            Candidates = candidates;
        }

        public string RequestedName { get; }

        /// <summary>True when several declarations match; false when none fits (see <see cref="Candidates"/>).</summary>
        public bool IsAmbiguous { get; }

        /// <summary>The tied declarations, or the same-named declarations that were rejected.</summary>
        public IReadOnlyList<SymbolCandidate> Candidates { get; }
    }
}
