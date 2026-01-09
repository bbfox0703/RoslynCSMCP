using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;
using System.Text.RegularExpressions;
using MsSymbolInfo = Microsoft.CodeAnalysis.SymbolInfo;
using SymbolInfo = RoslynMcpServer.Models.SymbolInfo;

namespace RoslynMcpServer.Services
{
    public class SymbolSearchService
    {
        private readonly CodeAnalysisService _codeAnalysis;
        private readonly ILogger<SymbolSearchService> _logger;
        private readonly IMemoryCache _cache;

        public SymbolSearchService(CodeAnalysisService codeAnalysis, 
            ILogger<SymbolSearchService> logger, IMemoryCache cache)
        {
            _codeAnalysis = codeAnalysis;
            _logger = logger;
            _cache = cache;
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
            // Convert wildcard pattern to regex
            var regexPattern = Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            
            var options = RegexOptions.Compiled;
            if (ignoreCase) options |= RegexOptions.IgnoreCase;
            
            return new Regex($"^{regexPattern}$", options);
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
            catch
            {
                // If we can't determine, assume it's not a write
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
    }
}