using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
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
                        foreach (var typeMember in GetTypeMembersRecursive(namedType))
                            yield return typeMember;
                        break;
                }
            }
        }

        /// <summary>
        /// Get the members of a type, descending into nested types at any depth
        /// </summary>
        private IEnumerable<ISymbol> GetTypeMembersRecursive(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                yield return member;

                if (member is INamedTypeSymbol nestedType)
                {
                    foreach (var nestedMember in GetTypeMembersRecursive(nestedType))
                        yield return nestedMember;
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

        public async Task<IEnumerable<ReferenceResult>> FindReferencesAsync(
            string symbolName, string solutionPath, bool includeDefinition)
        {
            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var targetSymbols = await FindSymbolsByNameAsync(solution, symbolName);

            var allReferences = new List<ReferenceResult>();

            foreach (var symbol in targetSymbols)
            {
                var references = await SymbolFinder.FindReferencesAsync(symbol, solution);

                foreach (var referencedSymbol in references)
                {
                    foreach (var location in referencedSymbol.Locations)
                    {
                        // Check if this location is a definition by comparing with the symbol's definition locations
                        var isDefinition = referencedSymbol.Definition.Locations.Any(defLoc =>
                            defLoc.SourceTree == location.Location.SourceTree &&
                            defLoc.SourceSpan == location.Location.SourceSpan);

                        if (!includeDefinition && isDefinition)
                            continue;

                        var reference = await CreateReferenceResultAsync(location, symbol, isDefinition);
                        if (reference != null)
                            allReferences.Add(reference);
                    }
                }
            }

            return allReferences
                .GroupBy(r => $"{r.DocumentPath}:{r.LineNumber}")
                .Select(g => g.First()) // Remove duplicates
                .OrderBy(r => r.DocumentPath)
                .ThenBy(r => r.LineNumber);
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
            // Get all references first
            var allReferences = await FindReferencesAsync(symbolName, solutionPath, includeDefinition);
            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var targetSymbols = await FindSymbolsByNameAsync(solution, symbolName);
            var targetSymbol = targetSymbols.FirstOrDefault();

            // Apply filters
            var filteredReferences = allReferences.AsEnumerable();

            // Filter: Exclude test projects
            if (excludeTests)
            {
                filteredReferences = filteredReferences.Where(r =>
                    !IsTestProject(r.ProjectName));
            }

            // Filter: Cross-project only
            if (crossProjectOnly && targetSymbol != null)
            {
                var definitionProjectName = targetSymbol.ContainingAssembly?.Name ?? "";
                filteredReferences = filteredReferences.Where(r =>
                    !r.ProjectName.Equals(definitionProjectName, StringComparison.OrdinalIgnoreCase) &&
                    !r.IsDefinition);
            }

            // Filter: Public API only
            if (publicOnly && targetSymbol != null)
            {
                // Only show references where the symbol being accessed is public
                var isPublicSymbol = targetSymbol.DeclaredAccessibility == Accessibility.Public;
                if (!isPublicSymbol)
                {
                    // If the symbol itself is not public, return empty
                    return Enumerable.Empty<ReferenceResult>();
                }
            }

            // Filter: Project name pattern
            if (!string.IsNullOrWhiteSpace(projectFilter))
            {
                var regex = CreateWildcardRegex(projectFilter, ignoreCase: true);
                filteredReferences = filteredReferences.Where(r =>
                    regex.IsMatch(r.ProjectName));
            }

            // Filter: Writes only (requires syntax analysis)
            if (writesOnly)
            {
                var writeReferences = new List<ReferenceResult>();
                foreach (var reference in filteredReferences)
                {
                    if (await IsWriteOperationAsync(reference, solution))
                    {
                        writeReferences.Add(reference);
                    }
                }
                filteredReferences = writeReferences;
            }

            return filteredReferences;
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

            var solutionResults = await Task.WhenAll(searchTasks);

            // Merge and deduplicate results
            var allReferences = solutionResults
                .SelectMany(r => r)
                .GroupBy(r => $"{r.DocumentPath}:{r.LineNumber}:{r.ColumnNumber}")
                .Select(g => g.First()) // Deduplicate by location
                .OrderBy(r => r.DocumentPath)
                .ThenBy(r => r.LineNumber)
                .ThenBy(r => r.ColumnNumber)
                .ToList();

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

        private async Task<bool> IsWriteOperationAsync(ReferenceResult reference, Solution solution)
        {
            try
            {
                // Find the document
                var document = solution.Projects
                    .SelectMany(p => p.Documents)
                    .FirstOrDefault(d => d.FilePath == reference.DocumentPath);

                if (document == null)
                    return false;

                var syntaxTree = await document.GetSyntaxTreeAsync();
                if (syntaxTree == null)
                    return false;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null)
                    return false;

                // Get the syntax node at the reference location
                var position = syntaxTree.GetText().Lines[reference.LineNumber - 1].Start + reference.ColumnNumber - 1;
                var node = syntaxTree.GetRoot().FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(position, 1));

                // Check if this is an assignment operation
                // Simple heuristic: check if the node or its parent is an assignment expression
                var currentNode = node;
                while (currentNode != null)
                {
                    var kind = currentNode.Kind().ToString();
                    if (kind.Contains("Assignment") ||
                        kind.Contains("PostIncrement") ||
                        kind.Contains("PostDecrement") ||
                        kind.Contains("PreIncrement") ||
                        kind.Contains("PreDecrement"))
                    {
                        return true;
                    }

                    currentNode = currentNode.Parent;

                    // Don't traverse too far up
                    if (currentNode?.Kind().ToString().Contains("Statement") == true)
                        break;
                }

                return false;
            }
            catch (Exception ex)
            {
                // If we can't determine, assume it's not a write
                _logger.LogDebug(ex, "Could not determine if reference is write access, assuming read");
                return false;
            }
        }

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

        private async Task<ReferenceResult?> CreateReferenceResultAsync(
            ReferenceLocation location, ISymbol symbol, bool isDefinition)
        {
            if (location.Document == null) return null;
            
            var document = location.Document;
            var sourceText = await document.GetTextAsync();
            var lineSpan = location.Location.GetLineSpan();
            
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
            
            return new ReferenceResult
            {
                SymbolName = symbol.Name,
                DocumentPath = document.FilePath ?? "",
                ProjectName = document.Project.Name,
                LineNumber = lineNumber + 1,
                ColumnNumber = lineSpan.StartLinePosition.Character + 1,
                LineText = line.ToString(),
                Context = context,
                IsDefinition = isDefinition,
                ReferenceKind = DetermineReferenceKind(location.Location, symbol)
            };
        }

        private string DetermineReferenceKind(Location location, ISymbol symbol)
        {
            // This is a simplified implementation
            // A more sophisticated version would analyze the syntax context
            return symbol.Kind switch
            {
                SymbolKind.Method => "Method Call",
                SymbolKind.Property => "Property Access",
                SymbolKind.Field => "Field Access",
                SymbolKind.NamedType => "Type Reference",
                _ => "Reference"
            };
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
            var targetType = await FindTypeByNameAsync(solution, typeName);

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

            foreach (var (type, project) in await GetSolutionSourceTypesAsync(solution))
            {
                // Skip the target type itself
                if (IsSameTypeDefinition(type, targetType))
                    continue;

                // Compare definitions so that constructed generics (IRepo<int>, Base<string>) match
                bool isImplementation = isInterface
                    ? type.AllInterfaces.Any(i => IsSameTypeDefinition(i, targetType))
                    : InheritsFrom(type, targetType);

                if (!isImplementation)
                    continue;

                // Skip abstract implementations if not requested
                if (type.IsAbstract && !includeAbstractImplementations)
                    continue;

                var implementation = CreateImplementationResult(type, targetType, project);
                if (implementation != null)
                {
                    results.Add(implementation);
                }
            }

            _logger.LogInformation("Found {Count} implementations", results.Count);
            return results.OrderBy(r => r.ImplementingTypeName).ToList();
        }

        /// <summary>
        /// Find a named type by simple name (case-insensitive). Types declared in the solution's source,
        /// including nested types at any depth, are preferred; referenced assemblies such as the framework
        /// are searched only when no source type matches.
        /// </summary>
        private async Task<INamedTypeSymbol?> FindTypeByNameAsync(Solution solution, string typeName)
        {
            var compilations = new List<Compilation>();

            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                compilations.Add(compilation);

                var sourceMatch = GetAllTypesInNamespace(compilation.Assembly.GlobalNamespace)
                    .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (sourceMatch != null)
                    return sourceMatch;
            }

            foreach (var compilation in compilations)
            {
                var referencedMatch = GetAllTypesInNamespace(compilation.GlobalNamespace)
                    .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (referencedMatch != null)
                    return referencedMatch;
            }

            return null;
        }

        /// <summary>
        /// Collect every type declared in the solution's source (nested types included), each paired with
        /// the project that declares it. Only each compilation's own assembly is walked, so referenced
        /// projects and the framework are not re-scanned, and a type is listed once even when a
        /// multi-targeted project contributes one compilation per target framework.
        /// </summary>
        private static async Task<List<(INamedTypeSymbol Type, Project Project)>> GetSolutionSourceTypesAsync(Solution solution)
        {
            var types = new List<(INamedTypeSymbol Type, Project Project)>();
            var seen = new HashSet<string>();

            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var type in GetAllTypesInNamespace(compilation.Assembly.GlobalNamespace))
                {
                    if (seen.Add(GetTypeDefinitionKey(type)))
                        types.Add((type, project));
                }
            }

            return types;
        }

        /// <summary>
        /// Identity of a type definition that is stable across compilations: the declaring assembly plus the
        /// documentation comment ID of the original (unconstructed) definition.
        /// </summary>
        private static string GetTypeDefinitionKey(INamedTypeSymbol type)
        {
            var definition = type.OriginalDefinition;
            return $"{definition.ContainingAssembly?.Name}|{definition.GetDocumentationCommentId() ?? definition.ToDisplayString()}";
        }

        /// <summary>
        /// Whether two types share the same definition, so that Repo&lt;int&gt; matches Repo&lt;T&gt;
        /// </summary>
        private static bool IsSameTypeDefinition(INamedTypeSymbol candidate, INamedTypeSymbol target)
        {
            var candidateDefinition = candidate.OriginalDefinition;
            var targetDefinition = target.OriginalDefinition;

            if (SymbolEqualityComparer.Default.Equals(candidateDefinition, targetDefinition))
                return true;

            // The same declaration can surface as distinct symbols in different compilations
            // (for example a retargeted project reference), so fall back to the definition key
            return candidateDefinition.Name == targetDefinition.Name
                && candidateDefinition.Arity == targetDefinition.Arity
                && GetTypeDefinitionKey(candidateDefinition) == GetTypeDefinitionKey(targetDefinition);
        }

        /// <summary>
        /// Whether a type derives from the target class at any depth
        /// </summary>
        private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol targetType)
        {
            for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (IsSameTypeDefinition(baseType, targetType))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get all types in a namespace recursively
        /// </summary>
        private static IEnumerable<INamedTypeSymbol> GetAllTypesInNamespace(INamespaceSymbol namespaceSymbol)
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
        private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
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
        private static ImplementationResult? CreateImplementationResult(
            INamedTypeSymbol type,
            INamedTypeSymbol targetType,
            Project project)
        {
            var location = type.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
                return null;

            var lineSpan = location.GetLineSpan();

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
        /// <param name="direction">both, ancestors, or descendants (case-insensitive)</param>
        /// <exception cref="ArgumentException">direction is not one of the supported values</exception>
        public async Task<ClassHierarchyResult?> GetClassHierarchyAsync(
            string typeName,
            string solutionPath,
            string direction = "both",
            int maxDepth = 10)
        {
            var normalizedDirection = NormalizeHierarchyDirection(direction)
                ?? throw new ArgumentException(
                    $"Invalid direction '{direction}'. Use both, ancestors, or descendants.", nameof(direction));

            _logger.LogInformation("Getting class hierarchy for: {TypeName}, Direction: {Direction}", typeName, normalizedDirection);

            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);

            // Find the target type
            var targetType = await FindTypeByNameAsync(solution, typeName);

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
            if (normalizedDirection == "ancestors" || normalizedDirection == "both")
            {
                result.Ancestors = GetAncestors(targetType, solution, maxDepth);
            }

            // Get descendants (derived classes)
            if (normalizedDirection == "descendants" || normalizedDirection == "both")
            {
                // Scan the solution once and index every source type by its direct base class and interfaces
                var descendantMap = BuildDirectDescendantMap(await GetSolutionSourceTypesAsync(solution));
                result.Descendants = GetDescendants(targetType, descendantMap, maxDepth, 0, new HashSet<string>());
            }

            return result;
        }

        /// <summary>
        /// Normalize a class hierarchy direction; returns null when the value is not supported
        /// </summary>
        public static string? NormalizeHierarchyDirection(string? direction)
        {
            var normalized = string.IsNullOrWhiteSpace(direction) ? "both" : direction.Trim().ToLowerInvariant();
            return normalized is "both" or "ancestors" or "descendants" ? normalized : null;
        }

        /// <summary>
        /// Get ancestor types (base classes and interfaces) recursively
        /// </summary>
        private static List<HierarchyNode> GetAncestors(INamedTypeSymbol type, Solution solution, int maxDepth, int currentDepth = 0)
        {
            var ancestors = new List<HierarchyNode>();

            if (currentDepth >= maxDepth)
                return ancestors;

            // Add base class
            if (type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object)
            {
                var baseNode = CreateHierarchyNode(type.BaseType, currentDepth + 1, GetDeclaringProjectName(type.BaseType, solution));
                // Recursively get ancestors of base class
                baseNode.Children = GetAncestors(type.BaseType, solution, maxDepth, currentDepth + 1);
                ancestors.Add(baseNode);
            }

            // Add interfaces
            foreach (var iface in type.Interfaces)
            {
                var ifaceNode = CreateHierarchyNode(iface, currentDepth + 1, GetDeclaringProjectName(iface, solution));
                // Recursively get base interfaces
                ifaceNode.Children = GetAncestors(iface, solution, maxDepth, currentDepth + 1);
                ancestors.Add(ifaceNode);
            }

            return ancestors;
        }

        /// <summary>
        /// Index source types by the definition of their direct base class and each directly implemented
        /// interface (or, for interfaces, each directly extended interface)
        /// </summary>
        private static Dictionary<string, List<(INamedTypeSymbol Type, Project Project)>> BuildDirectDescendantMap(
            List<(INamedTypeSymbol Type, Project Project)> sourceTypes)
        {
            var map = new Dictionary<string, List<(INamedTypeSymbol Type, Project Project)>>();

            foreach (var entry in sourceTypes)
            {
                var directBases = entry.Type.Interfaces.AsEnumerable();
                if (entry.Type.BaseType != null)
                    directBases = directBases.Prepend(entry.Type.BaseType);

                // Distinct: implementing IFoo<int> and IFoo<string> is still one direct descendant of IFoo<T>
                foreach (var baseKey in directBases.Select(GetTypeDefinitionKey).Distinct())
                {
                    if (!map.TryGetValue(baseKey, out var derivedTypes))
                    {
                        derivedTypes = new List<(INamedTypeSymbol Type, Project Project)>();
                        map[baseKey] = derivedTypes;
                    }

                    derivedTypes.Add(entry);
                }
            }

            return map;
        }

        /// <summary>
        /// Get descendant types (derived classes and implementers) recursively from the prebuilt index
        /// </summary>
        private static List<HierarchyNode> GetDescendants(
            INamedTypeSymbol type,
            Dictionary<string, List<(INamedTypeSymbol Type, Project Project)>> descendantMap,
            int maxDepth,
            int currentDepth,
            HashSet<string> path)
        {
            var descendants = new List<HierarchyNode>();

            if (currentDepth >= maxDepth)
                return descendants;

            if (!descendantMap.TryGetValue(GetTypeDefinitionKey(type), out var derivedTypes))
                return descendants;

            foreach (var (derivedType, project) in derivedTypes)
            {
                // Guard against inheritance cycles, which only occur in code that does not compile
                var derivedKey = GetTypeDefinitionKey(derivedType);
                if (!path.Add(derivedKey))
                    continue;

                var descendantNode = CreateHierarchyNode(derivedType, currentDepth + 1, project.Name);
                descendantNode.Children = GetDescendants(derivedType, descendantMap, maxDepth, currentDepth + 1, path);
                descendants.Add(descendantNode);

                path.Remove(derivedKey);
            }

            return descendants.OrderBy(d => d.Name).ToList();
        }

        /// <summary>
        /// Name of the project that declares a source type, or the containing assembly name for
        /// types from referenced assemblies
        /// </summary>
        private static string GetDeclaringProjectName(INamedTypeSymbol type, Solution solution)
        {
            var sourceTree = type.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree;
            var project = sourceTree != null ? solution.GetDocument(sourceTree)?.Project : null;
            return project?.Name ?? type.ContainingAssembly?.Name ?? "";
        }

        /// <summary>
        /// Create HierarchyNode from a type symbol
        /// </summary>
        private static HierarchyNode CreateHierarchyNode(
            INamedTypeSymbol type,
            int depth,
            string projectName)
        {
            var location = type.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();

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