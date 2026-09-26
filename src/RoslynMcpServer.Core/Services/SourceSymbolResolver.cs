using Microsoft.CodeAnalysis;
using RoslynMcpServer.Core.Models;
using System.Text;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// The kinds of declaration a tool accepts for a requested name, with the wording used in messages.
    /// </summary>
    internal sealed class SymbolTarget
    {
        private readonly Func<ISymbol, bool> _accepts;

        private SymbolTarget(string description, Func<ISymbol, bool> accepts)
        {
            Description = description;
            _accepts = accepts;
        }

        public static SymbolTarget TypeOrMember { get; } = new("type or member", s => s is not INamespaceSymbol);

        public static SymbolTarget Type { get; } = new("type", s => s is INamedTypeSymbol);

        public static SymbolTarget InterfaceOrAbstractClass { get; } = new("interface or abstract class", s =>
            s is INamedTypeSymbol { TypeKind: TypeKind.Interface } or INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: true });

        /// <summary>Used in messages such as "No type in the solution's source matches ...".</summary>
        public string Description { get; }

        public bool Accepts(ISymbol symbol) => _accepts(symbol);
    }

    /// <summary>
    /// A requested symbol name split into the parts used for matching. Accepted forms: 'Save',
    /// 'UserService.Save', 'App.Services.UserService.Save', optionally with type arguments on the last
    /// segment ('Result&lt;T&gt;', 'Result`1') and a parameter list ('Save(User, bool)').
    /// </summary>
    internal sealed class SymbolNameRequest
    {
        private static readonly string[] ParameterModifiers = { "this", "params", "scoped", "ref", "out", "in", "readonly" };

        private SymbolNameRequest(string original, string qualifiedName, int? arity, IReadOnlyList<string>? parameterTypes)
        {
            Original = original;
            QualifiedName = qualifiedName;
            var lastDot = qualifiedName.LastIndexOf('.');
            IsQualified = lastDot >= 0;
            SimpleName = IsQualified ? qualifiedName[(lastDot + 1)..] : qualifiedName;
            Arity = arity;
            ParameterTypes = parameterTypes;
        }

        public string Original { get; }

        /// <summary>The name without type arguments or parameter list, e.g. 'App.Services.UserService.Save'.</summary>
        public string QualifiedName { get; }

        public string SimpleName { get; }

        public bool IsQualified { get; }

        /// <summary>Number of type arguments given on the last segment; null when none were given.</summary>
        public int? Arity { get; }

        /// <summary>Normalized parameter types (modifiers, names and whitespace removed); null when no parameter list was given.</summary>
        public IReadOnlyList<string>? ParameterTypes { get; }

        public static SymbolNameRequest Parse(string symbolName)
        {
            var name = symbolName.Trim();
            if (name.StartsWith("global::", StringComparison.Ordinal))
                name = name["global::".Length..];

            IReadOnlyList<string>? parameterTypes = null;
            var parameterListStart = name.IndexOf('(');
            var namePart = parameterListStart >= 0 ? name[..parameterListStart] : name;
            if (parameterListStart >= 0)
            {
                var parameterListEnd = name.LastIndexOf(')');
                var parameterList = parameterListEnd > parameterListStart
                    ? name[(parameterListStart + 1)..parameterListEnd]
                    : name[(parameterListStart + 1)..];
                parameterTypes = string.IsNullOrWhiteSpace(parameterList)
                    ? Array.Empty<string>()
                    : SplitTopLevel(parameterList, ',').Select(NormalizeParameterType).ToList();
            }

            return new SymbolNameRequest(symbolName, NormalizeRequestedName(namePart), ParseArity(namePart), parameterTypes);
        }

        /// <summary>
        /// Normalizes a requested symbol name: drops 'global::', a parameter list, generic arguments
        /// and arity suffixes, and whitespace, and turns nested-type '+' separators into '.'.
        /// </summary>
        internal static string NormalizeRequestedName(string symbolName)
        {
            var name = symbolName.Trim();
            if (name.StartsWith("global::", StringComparison.Ordinal))
                name = name["global::".Length..];

            var parameterListStart = name.IndexOf('(');
            if (parameterListStart >= 0)
                name = name[..parameterListStart];

            var builder = new StringBuilder(name.Length);
            var genericDepth = 0;
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '<') { genericDepth++; continue; }
                if (c == '>') { genericDepth = Math.Max(0, genericDepth - 1); continue; }
                if (genericDepth > 0 || char.IsWhiteSpace(c)) continue;
                if (c == '`')
                {
                    while (i + 1 < name.Length && char.IsDigit(name[i + 1])) i++;
                    continue;
                }
                builder.Append(c == '+' ? '.' : c);
            }

            return builder.ToString().Trim('.');
        }

        /// <summary>Type arguments on the last name segment: 'Result&lt;T&gt;' and 'Dictionary&lt;,&gt;' give 1 and 2, 'Result`1' gives 1.</summary>
        private static int? ParseArity(string namePart)
        {
            var depth = 0;
            var segmentStart = 0;
            for (var i = 0; i < namePart.Length; i++)
            {
                var c = namePart[i];
                if (c == '<') depth++;
                else if (c == '>') depth = Math.Max(0, depth - 1);
                else if (depth == 0 && (c == '.' || c == '+')) segmentStart = i + 1;
            }

            var segment = namePart[segmentStart..].Trim();
            var typeArgumentsStart = segment.IndexOf('<');
            if (typeArgumentsStart >= 0)
                return SplitTopLevel(segment[(typeArgumentsStart + 1)..].TrimEnd('>'), ',').Count;

            var backtick = segment.IndexOf('`');
            if (backtick >= 0 && int.TryParse(segment[(backtick + 1)..], out var arity))
                return arity;

            return null;
        }

        /// <summary>'ref List&lt;int&gt; items = null' becomes 'List&lt;int&gt;'.</summary>
        private static string NormalizeParameterType(string parameter)
        {
            var text = parameter.Trim();

            var defaultValue = SplitTopLevel(text, '=');
            text = defaultValue[0].Trim();

            bool stripped;
            do
            {
                stripped = false;
                foreach (var modifier in ParameterModifiers)
                {
                    if (text.Length > modifier.Length && text.StartsWith(modifier, StringComparison.Ordinal) &&
                        char.IsWhiteSpace(text[modifier.Length]))
                    {
                        text = text[modifier.Length..].TrimStart();
                        stripped = true;
                    }
                }
            } while (stripped);

            // A trailing parameter name: 'User user' -> 'User'
            var words = SplitTopLevel(text, ' ').Where(w => w.Length > 0).ToList();
            if (words.Count > 1 && IsIdentifier(words[^1]))
                text = string.Join(" ", words.Take(words.Count - 1));

            return RemoveWhitespace(text).Replace("global::", "", StringComparison.Ordinal);
        }

        private static bool IsIdentifier(string text) =>
            text.Length > 0 &&
            (char.IsLetter(text[0]) || text[0] == '_' || text[0] == '@') &&
            text.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');

        internal static string RemoveWhitespace(string text) =>
            string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

        /// <summary>Splits on a separator that is not inside &lt;&gt;, () or [].</summary>
        private static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c is '<' or '(' or '[') depth++;
                else if (c is '>' or ')' or ']') depth = Math.Max(0, depth - 1);
                else if (depth == 0 && c == separator)
                {
                    parts.Add(text[start..i]);
                    start = i + 1;
                }
            }
            parts.Add(text[start..]);
            return parts;
        }
    }

    /// <summary>
    /// Resolves a requested name to symbols declared in the solution's source. Symbols from referenced
    /// assemblies (framework, NuGet packages) are never matched; nested types and their members are.
    /// </summary>
    internal static class SourceSymbolResolver
    {
        private const int MaxListedCandidates = 20;

        // Qualified form compared against names such as 'Type.Member' or 'Ns.Type.Member':
        // namespaces and containing types, without generic arguments or parameter lists.
        private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.None,
            memberOptions: SymbolDisplayMemberOptions.IncludeContainingType);

        // Forms a requested parameter type is compared against: 'List<int>', 'System.Collections.Generic.List<int>',
        // 'System.Collections.Generic.List<System.Int32>'. Nullable reference annotations are left out.
        private static readonly SymbolDisplayFormat[] ParameterTypeFormats =
        {
            new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes),
            new(globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes),
            new(globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters),
        };

        /// <summary>
        /// Resolves the name to the one declaration it identifies.
        /// <list type="number">
        /// <item>Source declarations whose simple name matches, ignoring case, and (for a qualified request)
        /// whose namespace and containing types end with the requested qualifier, ignoring case.</item>
        /// <item>Only kinds accepted by <paramref name="target"/>; with type arguments, only that arity;
        /// with a parameter list, only methods and indexers whose parameter types match.</item>
        /// <item>Exact-case matches win over case-insensitive ones.</item>
        /// <item>Without type arguments, a non-generic type wins over generic types of the same name, as in C#.</item>
        /// </list>
        /// </summary>
        /// <returns>The symbol, or null when nothing in the source has that name.</returns>
        /// <exception cref="SymbolResolutionException">
        /// Several declarations remain, or declarations with that name exist but none passes step 2.
        /// </exception>
        public static async Task<ISymbol?> ResolveSingleAsync(Solution solution, string symbolName, SymbolTarget target)
        {
            var request = SymbolNameRequest.Parse(symbolName);
            var nameMatches = await FindNameMatchesAsync(solution, request);
            var selected = Select(request, nameMatches, target);

            if (selected.Count == 1)
                return selected[0].Symbol;

            if (selected.Count > 1)
            {
                throw CreateException(request, selected, isAmbiguous: true,
                    $"'{request.Original}' matches {selected.Count} declarations in the solution's source. " +
                    "Call again with one of these names:");
            }

            var rejected = nameMatches.Where(m => m.Symbol is not INamespaceSymbol).ToList();
            if (rejected.Count == 0)
                return null;

            throw CreateException(request, rejected, isAmbiguous: false,
                $"No {target.Description} in the solution's source matches '{request.Original}'. " +
                "Declarations with that name:");
        }

        /// <summary>For callers whose empty result would otherwise also mean "nothing found".</summary>
        public static SymbolResolutionException NotFound(string symbolName, SymbolTarget target) => new(
            $"No {target.Description} named '{symbolName}' is declared in the solution's source. " +
            "Types and members of referenced assemblies (framework, NuGet packages) are not searched.",
            symbolName, isAmbiguous: false, Array.Empty<SymbolCandidate>());

        /// <summary>
        /// Source declarations whose name matches the request ignoring case, one entry per declaration even
        /// when several projects compile it (linked files, multi-targeting). Kind, arity, parameter list and
        /// case preference are not applied.
        /// </summary>
        public static async Task<List<(ISymbol Symbol, Project Project)>> FindNameMatchesAsync(
            Solution solution, SymbolNameRequest request)
        {
            var matches = new List<(ISymbol Symbol, Project Project)>();
            if (request.SimpleName.Length == 0)
                return matches;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // compilation.Assembly is this project's own source; referenced assemblies, including
                // other projects of the solution (walked as their own project), are not walked.
                foreach (var symbol in GetSourceSymbolsRecursive(compilation.Assembly.GlobalNamespace))
                {
                    if (!symbol.Name.Equals(request.SimpleName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (request.IsQualified && !QualifiedNameMatches(symbol, request.QualifiedName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (seen.Add(GetDeclarationKey(symbol)))
                        matches.Add((symbol, project));
                }
            }

            return matches;
        }

        /// <summary>Applies the kind, arity and parameter filters, then the exact-case and non-generic preferences.</summary>
        internal static List<(ISymbol Symbol, Project Project)> Select(
            SymbolNameRequest request, IReadOnlyList<(ISymbol Symbol, Project Project)> nameMatches, SymbolTarget target)
        {
            var eligible = nameMatches
                .Where(m => target.Accepts(m.Symbol))
                .Where(m => request.Arity == null || GetArity(m.Symbol) == request.Arity)
                .Where(m => request.ParameterTypes == null || ParametersMatch(m.Symbol, request.ParameterTypes))
                .ToList();

            var exactCase = eligible.Where(m => request.IsQualified
                    ? QualifiedNameMatches(m.Symbol, request.QualifiedName, StringComparison.Ordinal)
                    : m.Symbol.Name.Equals(request.SimpleName, StringComparison.Ordinal))
                .ToList();
            if (exactCase.Count > 0)
                eligible = exactCase;

            if (request.Arity == null && eligible.Any(m => m.Symbol is INamedTypeSymbol { Arity: 0 }))
                eligible = eligible.Where(m => m.Symbol is not INamedTypeSymbol { Arity: > 0 }).ToList();

            return eligible;
        }

        internal static bool QualifiedNameMatches(ISymbol symbol, string requested, StringComparison comparison)
        {
            var qualifiedName = symbol.ToDisplayString(QualifiedNameFormat);
            return qualifiedName.Equals(requested, comparison) ||
                   qualifiedName.EndsWith("." + requested, comparison);
        }

        /// <summary>Namespaces, types at any nesting depth, and their members.</summary>
        internal static IEnumerable<ISymbol> GetSourceSymbolsRecursive(INamespaceOrTypeSymbol container)
        {
            foreach (var member in container.GetMembers())
            {
                yield return member;

                if (member is INamespaceOrTypeSymbol nested)
                {
                    foreach (var inner in GetSourceSymbolsRecursive(nested))
                        yield return inner;
                }
            }
        }

        /// <summary>Kind, signature and declaring source spans: equal for one declaration compiled into several projects.</summary>
        private static string GetDeclarationKey(ISymbol symbol)
        {
            var locations = symbol.Locations
                .Where(l => l.IsInSource)
                .Select(l => $"{l.SourceTree?.FilePath}:{l.SourceSpan.Start}")
                .OrderBy(l => l, StringComparer.Ordinal);
            return $"{symbol.Kind}|{symbol.ToDisplayString()}|{string.Join(";", locations)}";
        }

        private static int GetArity(ISymbol symbol) => symbol switch
        {
            INamedTypeSymbol type => type.Arity,
            IMethodSymbol method => method.Arity,
            _ => 0
        };

        private static bool ParametersMatch(ISymbol symbol, IReadOnlyList<string> requestedTypes)
        {
            var parameters = symbol switch
            {
                IMethodSymbol method => method.Parameters,
                IPropertySymbol { IsIndexer: true } indexer => indexer.Parameters,
                _ => default
            };

            if (parameters.IsDefault || parameters.Length != requestedTypes.Count)
                return false;

            return parameters.Zip(requestedTypes).All(pair => ParameterTypeMatches(pair.Second, pair.First.Type));
        }

        /// <summary>
        /// 'int', 'Int32' and 'System.Int32' all match int; 'List&lt;User&gt;' and 'System.Collections.Generic.List&lt;App.User&gt;'
        /// match List&lt;User&gt;. Case is ignored.
        /// </summary>
        private static bool ParameterTypeMatches(string requested, ITypeSymbol type)
        {
            if (Matches(requested))
                return true;

            // A nullable reference annotation is not part of the signature: 'string?' matches string
            return type.IsReferenceType && requested.Contains('?') && Matches(requested.Replace("?", ""));

            bool Matches(string text) => ParameterTypeFormats.Any(format =>
            {
                var shown = SymbolNameRequest.RemoveWhitespace(type.ToDisplayString(format));
                return shown.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                       shown.EndsWith("." + text, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static SymbolResolutionException CreateException(
            SymbolNameRequest request, IEnumerable<(ISymbol Symbol, Project Project)> matches, bool isAmbiguous, string heading)
        {
            var candidates = matches
                .Select(m => CreateCandidate(m.Symbol, m.Project))
                .OrderBy(c => c.DisplayName, StringComparer.Ordinal)
                .ToList();

            var message = new StringBuilder(heading);
            foreach (var candidate in candidates.Take(MaxListedCandidates))
                message.Append("\n  - ").Append(candidate);
            if (candidates.Count > MaxListedCandidates)
                message.Append($"\n  ... and {candidates.Count - MaxListedCandidates} more");

            return new SymbolResolutionException(message.ToString(), request.Original, isAmbiguous, candidates);
        }

        private static SymbolCandidate CreateCandidate(ISymbol symbol, Project project)
        {
            var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            return new SymbolCandidate
            {
                DisplayName = symbol.ToDisplayString(),
                Kind = DescribeKind(symbol),
                ProjectName = project.Name,
                FilePath = location?.SourceTree?.FilePath ?? "",
                LineNumber = location != null ? location.GetLineSpan().StartLinePosition.Line + 1 : 0
            };
        }

        private static string DescribeKind(ISymbol symbol) => symbol switch
        {
            INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
            INamedTypeSymbol { IsRecord: true, IsAbstract: true } => "abstract record",
            INamedTypeSymbol { IsRecord: true } => "record",
            INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: true } => "static class",
            INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: true } => "abstract class",
            INamedTypeSymbol type => type.TypeKind.ToString().ToLowerInvariant(),
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "constructor",
            IPropertySymbol { IsIndexer: true } => "indexer",
            _ => symbol.Kind.ToString().ToLowerInvariant()
        };
    }
}
