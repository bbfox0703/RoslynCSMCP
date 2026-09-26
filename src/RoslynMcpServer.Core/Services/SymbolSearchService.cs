using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using MsSymbolInfo = Microsoft.CodeAnalysis.SymbolInfo;
using SymbolInfo = RoslynMcpServer.Core.Models.SymbolInfo;

namespace RoslynMcpServer.Core.Services
{
    public class SymbolSearchService
    {
        private readonly CodeAnalysisService _codeAnalysis;
        private readonly ILogger<SymbolSearchService> _logger;
        private readonly IMemoryCache _cache;
        private readonly ConcurrentDictionary<string, Regex> _regexCache;

        public SymbolSearchService(CodeAnalysisService codeAnalysis,
            ILogger<SymbolSearchService> logger, IMemoryCache cache)
        {
            _codeAnalysis = codeAnalysis;
            _logger = logger;
            _cache = cache;
            _regexCache = new ConcurrentDictionary<string, Regex>();
        }

        public async Task<IEnumerable<SymbolSearchResult>> SearchSymbolsAsync(
            string pattern, string solutionPath, string symbolTypes, bool ignoreCase)
        {
            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var typeFilter = ParseSymbolTypes(symbolTypes);
            var regex = CreateWildcardRegex(pattern, ignoreCase);
            
            var results = new List<SymbolSearchResult>();
            
            // Search across all projects in parallel
            var searchTasks = solution.Projects
                .Where(p => p.SupportsCompilation)
                .Select(project => SearchProjectSymbolsAsync(project, regex, typeFilter));
            
            var projectResults = await Task.WhenAll(searchTasks);
            
            foreach (var projectResult in projectResults)
                results.AddRange(projectResult);
            
            // Sort by relevance score
            return results
                .OrderByDescending(r => CalculateRelevanceScore(r, pattern))
                .ThenBy(r => r.Name);
        }

        private async Task<IEnumerable<SymbolSearchResult>> SearchProjectSymbolsAsync(
            Project project, Regex pattern, HashSet<SymbolKind> typeFilter)
        {
            var results = new List<SymbolSearchResult>();
            
            try
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) return results;
                
                var symbols = GetFilteredSymbols(compilation, typeFilter);
                
                foreach (var symbol in symbols)
                {
                    if (pattern.IsMatch(symbol.Name) || pattern.IsMatch(symbol.ToDisplayString()))
                    {
                        results.Add(CreateSearchResult(symbol, project));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching project: {ProjectName}", project.Name);
            }
            
            return results;
        }

        private Regex CreateWildcardRegex(string pattern, bool ignoreCase)
        {
            // Create cache key combining pattern and case sensitivity
            var cacheKey = $"{pattern}|{ignoreCase}";

            // Try to get from cache, or create and cache if not exists
            return _regexCache.GetOrAdd(cacheKey, _ =>
            {
                // Convert wildcard pattern to regex
                var regexPattern = Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".");

                var options = RegexOptions.Compiled;
                if (ignoreCase) options |= RegexOptions.IgnoreCase;

                // Add timeout to prevent ReDoS attacks
                return new Regex($"^{regexPattern}$", options, TimeSpan.FromSeconds(1));
            });
        }

        private HashSet<SymbolKind> ParseSymbolTypes(string symbolTypes)
        {
            var types = new HashSet<SymbolKind>();
            
            foreach (var type in symbolTypes.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (type.Trim().ToLower())
                {
                    case "class": 
                    case "interface": 
                    case "struct": 
                    case "enum": 
                        types.Add(SymbolKind.NamedType); 
                        break;
                    case "method": types.Add(SymbolKind.Method); break;
                    case "property": types.Add(SymbolKind.Property); break;
                    case "field": types.Add(SymbolKind.Field); break;
                    case "event": types.Add(SymbolKind.Event); break;
                    case "namespace": types.Add(SymbolKind.Namespace); break;
                }
            }
            
            return types;
        }

        private IEnumerable<ISymbol> GetFilteredSymbols(Compilation compilation, HashSet<SymbolKind> typeFilter)
        {
            return GetAllSymbolsRecursive(compilation.GlobalNamespace)
                .Where(s => typeFilter.Contains(s.Kind));
        }

        private IEnumerable<ISymbol> GetAllSymbolsRecursive(INamespaceSymbol namespaceSymbol)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                yield return member;

                switch (member)
                {
                    case INamespaceSymbol nestedNamespace:
                        foreach (var nested in GetAllSymbolsRecursive(nestedNamespace))
                            yield return nested;
                        break;
                    
                    case INamedTypeSymbol namedType:
                        foreach (var typeMember in namedType.GetMembers())
                            yield return typeMember;
                        break;
                }
            }
        }

        private SymbolSearchResult CreateSearchResult(ISymbol symbol, Project project)
        {
            var location = symbol.Locations.FirstOrDefault();
            var lineNumber = location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
            
            return new SymbolSearchResult
            {
                Name = symbol.Name,
                FullName = symbol.ToDisplayString(),
                Category = GetSymbolCategory(symbol),
                Location = $"{project.Name}:{Path.GetFileName(location?.SourceTree?.FilePath)}:{lineNumber}",
                ProjectName = project.Name,
                FilePath = location?.SourceTree?.FilePath ?? "",
                LineNumber = lineNumber,
                Summary = GetSymbolSummary(symbol),
                Accessibility = symbol.DeclaredAccessibility.ToString().ToLower(),
                SymbolKind = symbol.Kind,
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? ""
            };
        }

        private string GetSymbolCategory(ISymbol symbol)
        {
            return symbol switch
            {
                INamedTypeSymbol namedType => namedType.TypeKind.ToString(),
                IMethodSymbol => "Method",
                IPropertySymbol => "Property",
                IFieldSymbol => "Field",
                IEventSymbol => "Event",
                INamespaceSymbol => "Namespace",
                _ => symbol.Kind.ToString()
            };
        }

        private string GetSymbolSummary(ISymbol symbol)
        {
            return symbol switch
            {
                IMethodSymbol method => $"({string.Join(", ", method.Parameters.Select(p => $"{p.Type.Name} {p.Name}"))})",
                IPropertySymbol property => $": {property.Type.Name}",
                IFieldSymbol field => $": {field.Type.Name}",
                INamedTypeSymbol type => $"{type.TypeKind} with {type.GetMembers().Length} members",
                _ => ""
            };
        }

        private double CalculateRelevanceScore(SymbolSearchResult result, string searchPattern)
        {
            double score = 0;
            
            // Exact match gets highest score
            if (result.Name.Equals(searchPattern.Replace("*", "").Replace("?", ""), 
                StringComparison.OrdinalIgnoreCase))
                score += 100;
            
            // Prefix match
            if (result.Name.StartsWith(searchPattern.Replace("*", ""), 
                StringComparison.OrdinalIgnoreCase))
                score += 50;
            
            // Length penalty (shorter names are more relevant)
            score -= result.Name.Length * 0.1;
            
            // Public accessibility bonus
            if (result.Accessibility == "public")
                score += 10;
            
            return score;
        }

        #region Find References

        // Qualified form compared against names such as 'Type.Member' or 'Ns.Type.Member':
        // namespaces and containing types, without generic arguments or parameter lists.
        private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.None,
            memberOptions: SymbolDisplayMemberOptions.IncludeContainingType);

        /// <summary>A symbol resolved from the requested name, with the projects that declare it.</summary>
        private sealed record ReferenceTarget(ISymbol Symbol, IReadOnlySet<ProjectId> DeclaringProjects);

        /// <summary>One reference or declaration location, kept with what the filters need.</summary>
        private sealed record ReferenceHit(ReferenceResult Result, Document Document, TextSpan Span, ReferenceTarget Target);

        public async Task<IEnumerable<ReferenceResult>> FindReferencesAsync(
            string symbolName, string solutionPath, bool includeDefinition)
        {
            var search = await SearchReferencesAsync(symbolName, solutionPath, includeDefinition);
            return search.References;
        }

        /// <summary>
        /// Finds references like <see cref="FindReferencesAsync"/> and also reports how many declared
        /// symbols matched the name, so callers can tell "no such symbol" from "no references".
        /// </summary>
        public async Task<ReferenceSearchResult> SearchReferencesAsync(
            string symbolName, string solutionPath, bool includeDefinition)
        {
            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var targets = await ResolveReferenceTargetsAsync(solution, symbolName);
            var hits = await CollectReferenceHitsAsync(solution, solutionPath, targets, includeDefinition);

            return new ReferenceSearchResult
            {
                MatchedSymbolCount = targets.Count,
                References = DeduplicateByLine(hits.Select(h => h.Result)).ToList()
            };
        }

        public async Task<IEnumerable<ReferenceResult>> FindReferencesFilteredAsync(
            string symbolName,
            string solutionPath,
            bool includeDefinition,
            bool publicOnly = false,
            bool excludeTests = false,
            bool crossProjectOnly = false,
            bool writesOnly = false,
            string? projectFilter = null)
        {
            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var targets = await ResolveReferenceTargetsAsync(solution, symbolName);
            IEnumerable<ReferenceHit> hits = await CollectReferenceHitsAsync(solution, solutionPath, targets, includeDefinition);

            // Filters run per location, before lines are deduplicated, so a line that both reads
            // and writes the symbol still survives writesOnly.
            if (excludeTests)
            {
                hits = hits.Where(h => !IsTestProject(h.Result.ProjectName));
            }

            if (!string.IsNullOrWhiteSpace(projectFilter))
            {
                var regex = CreateWildcardRegex(projectFilter, ignoreCase: true);
                hits = hits.Where(h => regex.IsMatch(h.Result.ProjectName));
            }

            // Declaration sites always sit in the declaring project, so they are dropped too.
            if (crossProjectOnly)
            {
                hits = hits.Where(h => !h.Result.IsDefinition &&
                    !h.Target.DeclaringProjects.Contains(h.Document.Project.Id));
            }

            // Declarations are not writes; only reference locations are classified.
            if (writesOnly)
            {
                var writes = new List<ReferenceHit>();
                foreach (var hit in hits.Where(h => !h.Result.IsDefinition))
                {
                    if (await IsWriteAsync(hit))
                        writes.Add(hit);
                }
                hits = writes;
            }

            if (publicOnly)
            {
                var publicHits = new List<ReferenceHit>();
                foreach (var hit in hits)
                {
                    if (await IsInPublicApiContextAsync(hit))
                        publicHits.Add(hit);
                }
                hits = publicHits;
            }

            return DeduplicateByLine(hits.Select(h => h.Result)).ToList();
        }

        /// <summary>
        /// Finds references across multiple solutions
        /// </summary>
        public async Task<IEnumerable<ReferenceResult>> FindReferencesAcrossSolutionsAsync(
            string symbolName,
            string[] solutionPaths,
            bool includeDefinition)
        {
            _logger.LogInformation("Finding references for '{SymbolName}' across {Count} solutions",
                symbolName, solutionPaths.Length);

            // Search all solutions in parallel
            var searchTasks = solutionPaths.Select(async solutionPath =>
            {
                try
                {
                    _logger.LogDebug("Searching solution: {SolutionPath}", solutionPath);
                    var references = await FindReferencesAsync(symbolName, solutionPath, includeDefinition);
                    _logger.LogDebug("Found {Count} references in {SolutionPath}",
                        references.Count(), Path.GetFileName(solutionPath));
                    return references;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to search solution: {SolutionPath}", solutionPath);
                    return Enumerable.Empty<ReferenceResult>();
                }
            });

            // Task.WhenAll keeps input order, so a line found in several solutions (shared files)
            // is attributed to the first listed solution that contains it.
            var solutionResults = await Task.WhenAll(searchTasks);
            var allReferences = DeduplicateByLine(solutionResults.SelectMany(r => r)).ToList();

            _logger.LogInformation("Found {TotalCount} unique references across all solutions",
                allReferences.Count);

            return allReferences;
        }

        private bool IsTestProject(string projectName)
        {
            // Common test project naming patterns
            var testPatterns = new[] { "Test", "Tests", "Testing", ".Test.", ".Tests.", "Spec", "Specs" };
            return testPatterns.Any(pattern =>
                projectName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Resolves a requested name to the symbols declared in the solution's source. Accepts a simple
        /// name ('Save') or a name qualified by containing types and/or namespaces ('UserService.Save',
        /// 'App.Services.UserService.Save'); generic arguments and parameter lists are ignored.
        /// Symbols from referenced assemblies are never matched. Exact-case matches win; when there
        /// are none, case is ignored.
        /// </summary>
        private async Task<List<ReferenceTarget>> ResolveReferenceTargetsAsync(Solution solution, string symbolName)
        {
            var requested = NormalizeRequestedName(symbolName);
            if (requested.Length == 0)
                return new List<ReferenceTarget>();

            var lastDot = requested.LastIndexOf('.');
            var isQualified = lastDot >= 0;
            var simpleName = isQualified ? requested[(lastDot + 1)..] : requested;

            var candidates = new List<(ISymbol Symbol, Project Project)>();
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // compilation.Assembly is this project's own source; referenced assemblies, including
                // other projects of the solution (searched as their own project), are not walked.
                candidates.AddRange(GetSourceSymbolsRecursive(compilation.Assembly.GlobalNamespace)
                    .Where(s => s.Name.Equals(simpleName, StringComparison.OrdinalIgnoreCase) &&
                                (!isQualified || QualifiedNameMatches(s, requested, StringComparison.OrdinalIgnoreCase)))
                    .Select(s => (s, project)));
            }

            var exactCase = candidates
                .Where(c => isQualified
                    ? QualifiedNameMatches(c.Symbol, requested, StringComparison.Ordinal)
                    : c.Symbol.Name.Equals(simpleName, StringComparison.Ordinal))
                .ToList();
            var matches = exactCase.Count > 0 ? exactCase : candidates;

            return matches
                .Select(c => new ReferenceTarget(c.Symbol, GetDeclaringProjects(solution, c.Symbol, c.Project)))
                .ToList();
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

        private static bool QualifiedNameMatches(ISymbol symbol, string requested, StringComparison comparison)
        {
            var qualifiedName = symbol.ToDisplayString(QualifiedNameFormat);
            return qualifiedName.Equals(requested, comparison) ||
                   qualifiedName.EndsWith("." + requested, comparison);
        }

        /// <summary>Namespaces, types at any nesting depth, and their members.</summary>
        private static IEnumerable<ISymbol> GetSourceSymbolsRecursive(INamespaceOrTypeSymbol container)
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

        /// <summary>
        /// The project the symbol was found in plus every project that compiles one of its declaring
        /// files (linked files, other target frameworks of a multi-targeted project).
        /// </summary>
        private static IReadOnlySet<ProjectId> GetDeclaringProjects(Solution solution, ISymbol symbol, Project foundIn)
        {
            var projects = new HashSet<ProjectId> { foundIn.Id };
            foreach (var location in symbol.Locations.Where(l => l.IsInSource))
            {
                var filePath = location.SourceTree?.FilePath;
                if (string.IsNullOrEmpty(filePath)) continue;

                foreach (var documentId in solution.GetDocumentIdsWithFilePath(filePath))
                    projects.Add(documentId.ProjectId);
            }
            return projects;
        }

        private static async Task<List<ReferenceHit>> CollectReferenceHitsAsync(
            Solution solution, string solutionPath, IReadOnlyList<ReferenceTarget> targets, bool includeDefinition)
        {
            var hits = new List<ReferenceHit>();

            foreach (var target in targets)
            {
                if (includeDefinition)
                {
                    foreach (var location in target.Symbol.Locations.Where(l => l.IsInSource))
                    {
                        var document = solution.GetDocument(location.SourceTree);
                        if (document != null)
                            hits.Add(await CreateReferenceHitAsync(document, location, target, solutionPath, isDefinition: true));
                    }
                }

                var referencedSymbols = await SymbolFinder.FindReferencesAsync(target.Symbol, solution);
                foreach (var referencedSymbol in referencedSymbols)
                {
                    foreach (var location in referencedSymbol.Locations)
                    {
                        hits.Add(await CreateReferenceHitAsync(
                            location.Document, location.Location, target, solutionPath, isDefinition: false));
                    }
                }
            }

            return hits;
        }

        /// <summary>
        /// One entry per file line, sorted by file and line. A declaration wins over a reference on
        /// the same line; otherwise the leftmost reference (then the first solution) is kept.
        /// </summary>
        private static IEnumerable<ReferenceResult> DeduplicateByLine(IEnumerable<ReferenceResult> references)
        {
            return references
                .GroupBy(r => (r.DocumentPath, r.LineNumber))
                .Select(g => g.OrderByDescending(r => r.IsDefinition).ThenBy(r => r.ColumnNumber).First())
                .OrderBy(r => r.DocumentPath)
                .ThenBy(r => r.LineNumber);
        }

        private async Task<bool> IsWriteAsync(ReferenceHit hit)
        {
            try
            {
                var root = await hit.Document.GetSyntaxRootAsync();
                if (root == null)
                    return false;

                var node = root.FindNode(hit.Span, getInnermostNodeForTie: true);
                return ReferenceSyntaxClassifier.IsWrittenTo(node);
            }
            catch (Exception ex)
            {
                // If we can't determine, assume it's not a write
                _logger.LogDebug(ex, "Could not determine if reference is write access, assuming read");
                return false;
            }
        }

        private async Task<bool> IsInPublicApiContextAsync(ReferenceHit hit)
        {
            try
            {
                var root = await hit.Document.GetSyntaxRootAsync();
                var semanticModel = await hit.Document.GetSemanticModelAsync();
                if (root == null || semanticModel == null)
                    return false;

                var node = root.FindNode(hit.Span, getInnermostNodeForTie: true);
                return ReferenceSyntaxClassifier.IsInPublicApiContext(node, semanticModel);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not determine the accessibility context of a reference, excluding it");
                return false;
            }
        }

        private static async Task<ReferenceHit> CreateReferenceHitAsync(
            Document document, Location location, ReferenceTarget target, string solutionPath, bool isDefinition)
        {
            var sourceText = await document.GetTextAsync();
            var lineSpan = location.GetLineSpan();

            // Get surrounding context
            var lineNumber = lineSpan.StartLinePosition.Line;
            var line = sourceText.Lines[lineNumber];
            var contextStart = Math.Max(0, lineNumber - 2);
            var contextEnd = Math.Min(sourceText.Lines.Count - 1, lineNumber + 2);

            var context = sourceText.Lines
                .Skip(contextStart)
                .Take(contextEnd - contextStart + 1)
                .Select((l, i) => $"{contextStart + i + 1,4}: {l}")
                .ToList();

            var result = new ReferenceResult
            {
                SymbolName = target.Symbol.Name,
                DocumentPath = document.FilePath ?? "",
                ProjectName = document.Project.Name,
                SolutionPath = solutionPath,
                LineNumber = lineNumber + 1,
                ColumnNumber = lineSpan.StartLinePosition.Character + 1,
                LineText = line.ToString(),
                Context = context,
                IsDefinition = isDefinition,
                ReferenceKind = isDefinition ? "Definition" : DetermineReferenceKind(target.Symbol)
            };

            return new ReferenceHit(result, document, location.SourceSpan, target);
        }

        private static string DetermineReferenceKind(ISymbol symbol)
        {
            // Kind of the referenced symbol, not of the individual usage
            return symbol.Kind switch
            {
                SymbolKind.Method => "Method Call",
                SymbolKind.Property => "Property Access",
                SymbolKind.Field => "Field Access",
                SymbolKind.NamedType => "Type Reference",
                _ => "Reference"
            };
        }

        #endregion

        private async Task<IEnumerable<ISymbol>> FindSymbolsByNameAsync(Solution solution, string symbolName)
        {
            var symbols = new List<ISymbol>();

            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                {
                    var projectSymbols = GetAllSymbolsRecursive(compilation.GlobalNamespace)
                        .Where(s => s.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
                    symbols.AddRange(projectSymbols);
                }
            }

            return symbols;
        }

        public async Task<SymbolInfo?> GetSymbolInfoAsync(string symbolName, string solutionPath)
        {
            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var symbols = await FindSymbolsByNameAsync(solution, symbolName);
            var symbol = symbols.FirstOrDefault();
            
            if (symbol == null) return null;
            
            var info = new SymbolInfo
            {
                Name = symbol.Name,
                FullName = symbol.ToDisplayString(),
                Kind = symbol.Kind.ToString(),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                DeclaringType = symbol.ContainingType?.Name ?? "",
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                Assembly = symbol.ContainingAssembly?.Name ?? "",
                Documentation = symbol.GetDocumentationCommentXml() ?? ""
            };
            
            // Add method-specific information
            if (symbol is IMethodSymbol method)
            {
                info.Parameters = method.Parameters.Select(p => $"{p.Type.Name} {p.Name}").ToList();
                info.ReturnType = method.ReturnType.Name;
            }
            
            // Add property-specific information
            if (symbol is IPropertySymbol property)
            {
                info.ReturnType = property.Type.Name;
            }
            
            // Add attributes
            info.Attributes = symbol.GetAttributes()
                .Select(attr => attr.AttributeClass?.Name ?? "")
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
            
            // Add source location
            var location = symbol.Locations.FirstOrDefault();
            if (location != null && location.SourceTree != null)
            {
                var lineSpan = location.GetLineSpan();
                info.SourceLocation = $"{Path.GetFileName(location.SourceTree.FilePath)}:{lineSpan.StartLinePosition.Line + 1}";
            }
            
            return info;
        }

        /// <summary>
        /// Find all implementations of an interface or abstract class
        /// </summary>
        public async Task<List<ImplementationResult>> FindImplementationsAsync(
            string typeName,
            string solutionPath,
            bool includeAbstractImplementations = false)
        {
            _logger.LogInformation("Finding implementations for: {TypeName}", typeName);

            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var results = new List<ImplementationResult>();

            // Find the target interface or abstract class
            var targetSymbols = await FindSymbolsByNameAsync(solution, typeName);
            var targetType = targetSymbols.OfType<INamedTypeSymbol>().FirstOrDefault();

            if (targetType == null)
            {
                _logger.LogWarning("Type not found: {TypeName}", typeName);
                return results;
            }

            // Check if target is interface or abstract class
            bool isInterface = targetType.TypeKind == TypeKind.Interface;
            bool isAbstractClass = targetType.IsAbstract && targetType.TypeKind == TypeKind.Class;

            if (!isInterface && !isAbstractClass)
            {
                _logger.LogWarning("Type is not an interface or abstract class: {TypeName}", typeName);
                return results;
            }

            _logger.LogInformation("Searching for implementations of {TypeKind}: {TypeName}",
                targetType.TypeKind, typeName);

            // Search all projects for implementations
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // Get all named type symbols in the project
                var allTypes = compilation.GlobalNamespace.GetNamespaceMembers()
                    .SelectMany(ns => GetAllTypesInNamespace(ns))
                    .Concat(GetAllTypesInNamespace(compilation.GlobalNamespace));

                foreach (var type in allTypes)
                {
                    // Skip the target type itself
                    if (SymbolEqualityComparer.Default.Equals(type, targetType))
                        continue;

                    // Check if type implements the interface or inherits from abstract class
                    bool isImplementation = false;

                    if (isInterface)
                    {
                        // Check if type implements the interface (directly or indirectly)
                        isImplementation = type.AllInterfaces.Any(i =>
                            SymbolEqualityComparer.Default.Equals(i, targetType));
                    }
                    else if (isAbstractClass)
                    {
                        // Check if type inherits from abstract class
                        var baseType = type.BaseType;
                        while (baseType != null)
                        {
                            if (SymbolEqualityComparer.Default.Equals(baseType, targetType))
                            {
                                isImplementation = true;
                                break;
                            }
                            baseType = baseType.BaseType;
                        }
                    }

                    if (isImplementation)
                    {
                        // Skip abstract implementations if not requested
                        if (type.IsAbstract && !includeAbstractImplementations)
                            continue;

                        var implementation = await CreateImplementationResultAsync(type, targetType, project);
                        if (implementation != null)
                        {
                            results.Add(implementation);
                        }
                    }
                }
            }

            _logger.LogInformation("Found {Count} implementations", results.Count);
            return results.OrderBy(r => r.ImplementingTypeName).ToList();
        }

        /// <summary>
        /// Get all types in a namespace recursively
        /// </summary>
        private IEnumerable<INamedTypeSymbol> GetAllTypesInNamespace(INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                yield return type;

                // Get nested types
                foreach (var nestedType in GetNestedTypes(type))
                {
                    yield return nestedType;
                }
            }

            foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypesInNamespace(childNamespace))
                {
                    yield return type;
                }
            }
        }

        /// <summary>
        /// Get nested types recursively
        /// </summary>
        private IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
        {
            foreach (var nestedType in type.GetTypeMembers())
            {
                yield return nestedType;

                foreach (var deepNestedType in GetNestedTypes(nestedType))
                {
                    yield return deepNestedType;
                }
            }
        }

        /// <summary>
        /// Create ImplementationResult from a type symbol
        /// </summary>
        private async Task<ImplementationResult?> CreateImplementationResultAsync(
            INamedTypeSymbol type,
            INamedTypeSymbol targetType,
            Project project)
        {
            var location = type.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
                return null;

            var lineSpan = location.GetLineSpan();
            var document = project.GetDocument(location.SourceTree);

            // Get documentation
            var documentation = type.GetDocumentationCommentXml() ?? "";
            var summaryMatch = Regex.Match(documentation, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
            var summary = summaryMatch.Success
                ? summaryMatch.Groups[1].Value.Replace("///", "").Trim()
                : "";

            // Get implemented interfaces
            var implementedInterfaces = type.AllInterfaces
                .Select(i => i.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                .ToList();

            // Get base class
            var baseClass = type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object
                ? type.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                : "";

            return new ImplementationResult
            {
                ImplementingTypeName = type.Name,
                ImplementingTypeFullName = type.ToDisplayString(),
                InterfaceOrBaseTypeName = targetType.Name,
                FilePath = location.SourceTree?.FilePath ?? "",
                FileName = location.SourceTree != null ? Path.GetFileName(location.SourceTree.FilePath) : "",
                ProjectName = project.Name,
                LineNumber = lineSpan.StartLinePosition.Line + 1,
                Accessibility = type.DeclaredAccessibility.ToString(),
                IsAbstract = type.IsAbstract,
                IsSealed = type.IsSealed,
                Namespace = type.ContainingNamespace?.ToDisplayString() ?? "",
                Documentation = summary,
                ImplementedInterfaces = implementedInterfaces,
                BaseClass = baseClass
            };
        }

        /// <summary>
        /// Get complete class hierarchy (ancestors and descendants) for a type
        /// </summary>
        public async Task<ClassHierarchyResult?> GetClassHierarchyAsync(
            string typeName,
            string solutionPath,
            string direction = "both",
            int maxDepth = 10)
        {
            _logger.LogInformation("Getting class hierarchy for: {TypeName}, Direction: {Direction}", typeName, direction);

            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);

            // Find the target type
            var targetSymbols = await FindSymbolsByNameAsync(solution, typeName);
            var targetType = targetSymbols.OfType<INamedTypeSymbol>().FirstOrDefault();

            if (targetType == null)
            {
                _logger.LogWarning("Type not found: {TypeName}", typeName);
                return null;
            }

            var location = targetType.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();

            // Get documentation
            var documentation = targetType.GetDocumentationCommentXml() ?? "";
            var summaryMatch = Regex.Match(documentation, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
            var summary = summaryMatch.Success
                ? summaryMatch.Groups[1].Value.Replace("///", "").Trim()
                : "";

            var result = new ClassHierarchyResult
            {
                TypeName = targetType.Name,
                TypeFullName = targetType.ToDisplayString(),
                TypeKind = targetType.TypeKind.ToString(),
                Accessibility = targetType.DeclaredAccessibility.ToString(),
                IsAbstract = targetType.IsAbstract,
                IsSealed = targetType.IsSealed,
                Namespace = targetType.ContainingNamespace?.ToDisplayString() ?? "",
                FilePath = location?.SourceTree?.FilePath ?? "",
                LineNumber = lineSpan?.StartLinePosition.Line + 1 ?? 0,
                Documentation = summary
            };

            // Get ancestors (base classes and interfaces)
            if (direction == "ancestors" || direction == "both")
            {
                result.Ancestors = await GetAncestorsAsync(targetType, maxDepth);
            }

            // Get descendants (derived classes)
            if (direction == "descendants" || direction == "both")
            {
                result.Descendants = await GetDescendantsAsync(targetType, solution, maxDepth);
            }

            return result;
        }

        /// <summary>
        /// Get ancestor types (base classes and interfaces) recursively
        /// </summary>
        private async Task<List<HierarchyNode>> GetAncestorsAsync(INamedTypeSymbol type, int maxDepth, int currentDepth = 0)
        {
            var ancestors = new List<HierarchyNode>();

            if (currentDepth >= maxDepth)
                return ancestors;

            // Add base class
            if (type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object)
            {
                var baseNode = await CreateHierarchyNodeAsync(type.BaseType, currentDepth + 1);
                if (baseNode != null)
                {
                    // Recursively get ancestors of base class
                    baseNode.Children = await GetAncestorsAsync(type.BaseType, maxDepth, currentDepth + 1);
                    ancestors.Add(baseNode);
                }
            }

            // Add interfaces
            foreach (var iface in type.Interfaces)
            {
                var ifaceNode = await CreateHierarchyNodeAsync(iface, currentDepth + 1);
                if (ifaceNode != null)
                {
                    // Recursively get base interfaces
                    ifaceNode.Children = await GetAncestorsAsync(iface, maxDepth, currentDepth + 1);
                    ancestors.Add(ifaceNode);
                }
            }

            return ancestors;
        }

        /// <summary>
        /// Get descendant types (derived classes) by searching the solution
        /// </summary>
        private async Task<List<HierarchyNode>> GetDescendantsAsync(
            INamedTypeSymbol type,
            Solution solution,
            int maxDepth,
            int currentDepth = 0)
        {
            var descendants = new List<HierarchyNode>();

            if (currentDepth >= maxDepth)
                return descendants;

            // Search all projects for types that inherit from or implement the target type
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // Get all types in the project
                var allTypes = compilation.GlobalNamespace.GetNamespaceMembers()
                    .SelectMany(ns => GetAllTypesInNamespace(ns))
                    .Concat(GetAllTypesInNamespace(compilation.GlobalNamespace));

                foreach (var candidateType in allTypes)
                {
                    // Skip the type itself
                    if (SymbolEqualityComparer.Default.Equals(candidateType, type))
                        continue;

                    bool isDirectDescendant = false;

                    // Check if candidate directly inherits from type
                    if (candidateType.BaseType != null &&
                        SymbolEqualityComparer.Default.Equals(candidateType.BaseType, type))
                    {
                        isDirectDescendant = true;
                    }

                    // Check if candidate directly implements the interface (only for interfaces)
                    if (type.TypeKind == TypeKind.Interface &&
                        candidateType.Interfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, type)))
                    {
                        isDirectDescendant = true;
                    }

                    if (isDirectDescendant)
                    {
                        var descendantNode = await CreateHierarchyNodeAsync(candidateType, currentDepth + 1, project);
                        if (descendantNode != null)
                        {
                            // Recursively get descendants of this type
                            descendantNode.Children = await GetDescendantsAsync(
                                candidateType,
                                solution,
                                maxDepth,
                                currentDepth + 1);
                            descendants.Add(descendantNode);
                        }
                    }
                }
            }

            return descendants.OrderBy(d => d.Name).ToList();
        }

        /// <summary>
        /// Create HierarchyNode from a type symbol
        /// </summary>
        private async Task<HierarchyNode?> CreateHierarchyNodeAsync(
            INamedTypeSymbol type,
            int depth,
            Project? project = null)
        {
            var location = type.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();

            // For system types or types not in source, we won't have project info
            var projectName = project?.Name ?? type.ContainingAssembly?.Name ?? "";

            return new HierarchyNode
            {
                Name = type.Name,
                FullName = type.ToDisplayString(),
                TypeKind = type.TypeKind.ToString(),
                IsAbstract = type.IsAbstract,
                IsInterface = type.TypeKind == TypeKind.Interface,
                Namespace = type.ContainingNamespace?.ToDisplayString() ?? "",
                ProjectName = projectName,
                FilePath = location?.SourceTree?.FilePath ?? "",
                LineNumber = lineSpan?.StartLinePosition.Line + 1 ?? 0,
                Depth = depth
            };
        }
    }
}