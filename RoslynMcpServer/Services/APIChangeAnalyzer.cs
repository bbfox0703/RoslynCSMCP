using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;

namespace RoslynMcpServer.Services
{
    /// <summary>
    /// Service for analyzing API changes between two versions
    /// </summary>
    public class APIChangeAnalyzer
    {
        private readonly ILogger<APIChangeAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public APIChangeAnalyzer(
            ILogger<APIChangeAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes API changes between two solution versions
        /// </summary>
        public async Task<APIChangeResults> AnalyzeAPIChangesAsync(
            string oldSolutionPath,
            string newSolutionPath,
            string oldVersionLabel = "Old",
            string newVersionLabel = "New",
            bool includeInternal = false)
        {
            var results = new APIChangeResults
            {
                OldVersionPath = oldSolutionPath,
                NewVersionPath = newSolutionPath,
                OldVersionLabel = oldVersionLabel,
                NewVersionLabel = newVersionLabel
            };

            try
            {
                // Load both solutions
                var oldSolution = await _codeAnalysis.GetSolutionAsync(oldSolutionPath);
                var newSolution = await _codeAnalysis.GetSolutionAsync(newSolutionPath);

                // Extract public API symbols from both versions
                var oldSymbols = await ExtractPublicAPISymbolsAsync(oldSolution, includeInternal);
                var newSymbols = await ExtractPublicAPISymbolsAsync(newSolution, includeInternal);

                results.AnalyzedOldSymbols = oldSymbols.Count;
                results.AnalyzedNewSymbols = newSymbols.Count;

                // Compare and detect changes
                DetectChanges(oldSymbols, newSymbols, results, oldVersionLabel, newVersionLabel);

                // Calculate statistics
                CalculateStatistics(results);

                // Determine semantic versioning recommendation
                DetermineVersioningRecommendation(results);

                _logger.LogInformation(
                    "API change analysis complete: {ChangeCount} changes detected ({Breaking} breaking, {NonBreaking} non-breaking)",
                    results.TotalChanges,
                    results.BreakingChanges,
                    results.NonBreakingChanges);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing API changes");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Extracts public API symbols from a solution
        /// </summary>
        private async Task<Dictionary<string, ISymbol>> ExtractPublicAPISymbolsAsync(
            Solution solution,
            bool includeInternal)
        {
            var symbols = new Dictionary<string, ISymbol>();

            var projects = solution.Projects
                .Where(p => p.SupportsCompilation)
                .ToList();

            foreach (var project in projects)
            {
                try
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null)
                        continue;

                    // Get all types from the assembly
                    var visitor = new PublicAPISymbolVisitor(includeInternal);
                    visitor.Visit(compilation.Assembly.GlobalNamespace);

                    foreach (var symbol in visitor.Symbols)
                    {
                        var key = GetSymbolKey(symbol);
                        if (!symbols.ContainsKey(key))
                        {
                            symbols[key] = symbol;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract symbols from project: {ProjectName}", project.Name);
                }
            }

            return symbols;
        }

        /// <summary>
        /// Detects changes between old and new symbol sets
        /// </summary>
        private void DetectChanges(
            Dictionary<string, ISymbol> oldSymbols,
            Dictionary<string, ISymbol> newSymbols,
            APIChangeResults results,
            string oldVersion,
            string newVersion)
        {
            var changes = new List<APIChange>();

            // Find removed symbols
            foreach (var kvp in oldSymbols)
            {
                if (!newSymbols.ContainsKey(kvp.Key))
                {
                    changes.Add(CreateRemovedChange(kvp.Value, oldVersion, newVersion));
                }
            }

            // Find added symbols
            foreach (var kvp in newSymbols)
            {
                if (!oldSymbols.ContainsKey(kvp.Key))
                {
                    changes.Add(CreateAddedChange(kvp.Value, oldVersion, newVersion));
                }
            }

            // Find modified symbols
            foreach (var kvp in newSymbols)
            {
                if (oldSymbols.TryGetValue(kvp.Key, out var oldSymbol))
                {
                    var modificationChanges = DetectModifications(oldSymbol, kvp.Value, oldVersion, newVersion);
                    changes.AddRange(modificationChanges);
                }
            }

            results.Changes = changes;
        }

        /// <summary>
        /// Detects modifications to a symbol
        /// </summary>
        private List<APIChange> DetectModifications(
            ISymbol oldSymbol,
            ISymbol newSymbol,
            string oldVersion,
            string newVersion)
        {
            var changes = new List<APIChange>();

            // Check accessibility changes
            if (oldSymbol.DeclaredAccessibility != newSymbol.DeclaredAccessibility)
            {
                changes.Add(CreateAccessibilityChange(oldSymbol, newSymbol, oldVersion, newVersion));
            }

            // Check signature changes for methods
            if (oldSymbol is IMethodSymbol oldMethod && newSymbol is IMethodSymbol newMethod)
            {
                if (!SignaturesMatch(oldMethod, newMethod))
                {
                    changes.Add(CreateSignatureChange(oldMethod, newMethod, oldVersion, newVersion));
                }
            }

            // Check type changes
            if (oldSymbol is INamedTypeSymbol oldType && newSymbol is INamedTypeSymbol newType)
            {
                // Check if class became abstract or sealed
                if (oldType.IsAbstract != newType.IsAbstract)
                {
                    changes.Add(CreateTypeModifierChange(oldType, newType, oldVersion, newVersion, "abstract"));
                }
                if (oldType.IsSealed != newType.IsSealed)
                {
                    changes.Add(CreateTypeModifierChange(oldType, newType, oldVersion, newVersion, "sealed"));
                }

                // Check base type changes
                if (!BaseTypesMatch(oldType, newType))
                {
                    changes.Add(CreateBaseTypeChange(oldType, newType, oldVersion, newVersion));
                }
            }

            // Check property changes
            if (oldSymbol is IPropertySymbol oldProp && newSymbol is IPropertySymbol newProp)
            {
                if (oldProp.Type.ToDisplayString() != newProp.Type.ToDisplayString())
                {
                    changes.Add(CreatePropertyTypeChange(oldProp, newProp, oldVersion, newVersion));
                }
            }

            return changes;
        }

        /// <summary>
        /// Checks if method signatures match
        /// </summary>
        private bool SignaturesMatch(IMethodSymbol oldMethod, IMethodSymbol newMethod)
        {
            // Check return type
            if (oldMethod.ReturnType.ToDisplayString() != newMethod.ReturnType.ToDisplayString())
                return false;

            // Check parameter count
            if (oldMethod.Parameters.Length != newMethod.Parameters.Length)
                return false;

            // Check each parameter
            for (int i = 0; i < oldMethod.Parameters.Length; i++)
            {
                var oldParam = oldMethod.Parameters[i];
                var newParam = newMethod.Parameters[i];

                if (oldParam.Type.ToDisplayString() != newParam.Type.ToDisplayString())
                    return false;

                if (oldParam.RefKind != newParam.RefKind)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if base types match
        /// </summary>
        private bool BaseTypesMatch(INamedTypeSymbol oldType, INamedTypeSymbol newType)
        {
            var oldBase = oldType.BaseType?.ToDisplayString() ?? "";
            var newBase = newType.BaseType?.ToDisplayString() ?? "";
            return oldBase == newBase;
        }

        /// <summary>
        /// Creates a change entry for a removed symbol
        /// </summary>
        private APIChange CreateRemovedChange(ISymbol symbol, string oldVersion, string newVersion)
        {
            return new APIChange
            {
                SymbolName = symbol.Name,
                FullSymbolName = symbol.ToDisplayString(),
                SymbolKind = symbol.Kind.ToString(),
                ChangeType = "Removed",
                ImpactLevel = "Breaking",
                Severity = "Critical",
                Description = $"{symbol.Kind} '{symbol.Name}' was removed",
                OldVersion = oldVersion,
                NewVersion = newVersion,
                OldSignature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                NewSignature = "",
                OldAccessibility = symbol.DeclaredAccessibility.ToString(),
                NewAccessibility = "",
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                DeclaringType = symbol.ContainingType?.Name ?? "",
                MigrationGuidance = $"The {symbol.Kind.ToString().ToLower()} '{symbol.Name}' has been removed. Find an alternative API or retain the old version.",
                AffectedAreas = new List<string> { "All consumers of this symbol" }
            };
        }

        /// <summary>
        /// Creates a change entry for an added symbol
        /// </summary>
        private APIChange CreateAddedChange(ISymbol symbol, string oldVersion, string newVersion)
        {
            return new APIChange
            {
                SymbolName = symbol.Name,
                FullSymbolName = symbol.ToDisplayString(),
                SymbolKind = symbol.Kind.ToString(),
                ChangeType = "Added",
                ImpactLevel = "NonBreaking",
                Severity = "Low",
                Description = $"New {symbol.Kind.ToString().ToLower()} '{symbol.Name}' was added",
                OldVersion = oldVersion,
                NewVersion = newVersion,
                OldSignature = "",
                NewSignature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                OldAccessibility = "",
                NewAccessibility = symbol.DeclaredAccessibility.ToString(),
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                DeclaringType = symbol.ContainingType?.Name ?? "",
                MigrationGuidance = $"New {symbol.Kind.ToString().ToLower()} '{symbol.Name}' is now available for use.",
                AffectedAreas = new List<string> { "New functionality" }
            };
        }

        /// <summary>
        /// Creates a change entry for accessibility changes
        /// </summary>
        private APIChange CreateAccessibilityChange(
            ISymbol oldSymbol,
            ISymbol newSymbol,
            string oldVersion,
            string newVersion)
        {
            var oldAccess = oldSymbol.DeclaredAccessibility;
            var newAccess = newSymbol.DeclaredAccessibility;

            bool isBreaking = IsAccessibilityChangeBreaking(oldAccess, newAccess);

            return new APIChange
            {
                SymbolName = newSymbol.Name,
                FullSymbolName = newSymbol.ToDisplayString(),
                SymbolKind = newSymbol.Kind.ToString(),
                ChangeType = "AccessibilityChanged",
                ImpactLevel = isBreaking ? "Breaking" : "NonBreaking",
                Severity = isBreaking ? "High" : "Medium",
                Description = $"Accessibility changed from {oldAccess} to {newAccess}",
                OldVersion = oldVersion,
                NewVersion = newVersion,
                OldSignature = oldSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                NewSignature = newSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                OldAccessibility = oldAccess.ToString(),
                NewAccessibility = newAccess.ToString(),
                Namespace = newSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                DeclaringType = newSymbol.ContainingType?.Name ?? "",
                MigrationGuidance = isBreaking
                    ? $"The {newSymbol.Kind.ToString().ToLower()} '{newSymbol.Name}' is now less accessible. Update consuming code or use reflection."
                    : $"The {newSymbol.Kind.ToString().ToLower()} '{newSymbol.Name}' is now more accessible.",
                AffectedAreas = isBreaking
                    ? new List<string> { "Code accessing this symbol from restricted contexts" }
                    : new List<string> { "No breaking impact" }
            };
        }

        /// <summary>
        /// Creates a change entry for signature changes
        /// </summary>
        private APIChange CreateSignatureChange(
            IMethodSymbol oldMethod,
            IMethodSymbol newMethod,
            string oldVersion,
            string newVersion)
        {
            return new APIChange
            {
                SymbolName = newMethod.Name,
                FullSymbolName = newMethod.ToDisplayString(),
                SymbolKind = "Method",
                ChangeType = "SignatureChanged",
                ImpactLevel = "Breaking",
                Severity = "Critical",
                Description = "Method signature was changed",
                OldVersion = oldVersion,
                NewVersion = newVersion,
                OldSignature = oldMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                NewSignature = newMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                OldAccessibility = oldMethod.DeclaredAccessibility.ToString(),
                NewAccessibility = newMethod.DeclaredAccessibility.ToString(),
                Namespace = newMethod.ContainingNamespace?.ToDisplayString() ?? "",
                DeclaringType = newMethod.ContainingType?.Name ?? "",
                MigrationGuidance = $"Update all call sites to match the new signature: {newMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
                AffectedAreas = new List<string> { "All callers of this method" }
            };
        }

        /// <summary>
        /// Creates a change entry for type modifier changes
        /// </summary>
        private APIChange CreateTypeModifierChange(
            INamedTypeSymbol oldType,
            INamedTypeSymbol newType,
            string oldVersion,
            string newVersion,
            string modifier)
        {
            bool wasModified = modifier == "abstract" ? oldType.IsAbstract : oldType.IsSealed;
            bool isModified = modifier == "abstract" ? newType.IsAbstract : newType.IsSealed;

            return new APIChange
            {
                SymbolName = newType.Name,
                FullSymbolName = newType.ToDisplayString(),
                SymbolKind = "Type",
                ChangeType = "Modified",
                ImpactLevel = "Breaking",
                Severity = "High",
                Description = isModified
                    ? $"Type became {modifier}"
                    : $"Type is no longer {modifier}",
                OldVersion = oldVersion,
                NewVersion = newVersion,
                OldSignature = oldType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                NewSignature = newType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                OldAccessibility = oldType.DeclaredAccessibility.ToString(),
                NewAccessibility = newType.DeclaredAccessibility.ToString(),
                Namespace = newType.ContainingNamespace?.ToDisplayString() ?? "",
                DeclaringType = "",
                MigrationGuidance = isModified
                    ? $"Type '{newType.Name}' is now {modifier}. Update code that {(modifier == "abstract" ? "instantiates" : "inherits from")} this type."
                    : $"Type '{newType.Name}' is no longer {modifier}.",
                AffectedAreas = new List<string>
                {
                    modifier == "abstract" ? "Direct instantiation" : "Inheritance"
                }
            };
        }

        /// <summary>
        /// Creates a change entry for base type changes
        /// </summary>
        private APIChange CreateBaseTypeChange(
            INamedTypeSymbol oldType,
            INamedTypeSymbol newType,
            string oldVersion,
            string newVersion)
        {
            return new APIChange
            {
                SymbolName = newType.Name,
                FullSymbolName = newType.ToDisplayString(),
                SymbolKind = "Type",
                ChangeType = "Modified",
                ImpactLevel = "Breaking",
                Severity = "High",
                Description = "Base type was changed",
                OldVersion = oldVersion,
                NewVersion = newVersion,
                OldSignature = $"{oldType.Name} : {oldType.BaseType?.Name ?? "object"}",
                NewSignature = $"{newType.Name} : {newType.BaseType?.Name ?? "object"}",
                OldAccessibility = oldType.DeclaredAccessibility.ToString(),
                NewAccessibility = newType.DeclaredAccessibility.ToString(),
                Namespace = newType.ContainingNamespace?.ToDisplayString() ?? "",
                DeclaringType = "",
                MigrationGuidance = $"Base type changed from '{oldType.BaseType?.Name ?? "object"}' to '{newType.BaseType?.Name ?? "object"}'. Review inheritance hierarchies.",
                AffectedAreas = new List<string> { "Inheritance hierarchy", "Polymorphic code" }
            };
        }

        /// <summary>
        /// Creates a change entry for property type changes
        /// </summary>
        private APIChange CreatePropertyTypeChange(
            IPropertySymbol oldProp,
            IPropertySymbol newProp,
            string oldVersion,
            string newVersion)
        {
            return new APIChange
            {
                SymbolName = newProp.Name,
                FullSymbolName = newProp.ToDisplayString(),
                SymbolKind = "Property",
                ChangeType = "Modified",
                ImpactLevel = "Breaking",
                Severity = "Critical",
                Description = "Property type was changed",
                OldVersion = oldVersion,
                NewVersion = newVersion,
                OldSignature = $"{oldProp.Type.ToDisplayString()} {oldProp.Name}",
                NewSignature = $"{newProp.Type.ToDisplayString()} {newProp.Name}",
                OldAccessibility = oldProp.DeclaredAccessibility.ToString(),
                NewAccessibility = newProp.DeclaredAccessibility.ToString(),
                Namespace = newProp.ContainingNamespace?.ToDisplayString() ?? "",
                DeclaringType = newProp.ContainingType?.Name ?? "",
                MigrationGuidance = $"Property type changed from '{oldProp.Type.ToDisplayString()}' to '{newProp.Type.ToDisplayString()}'. Update consuming code.",
                AffectedAreas = new List<string> { "All code accessing this property" }
            };
        }

        /// <summary>
        /// Checks if an accessibility change is breaking
        /// </summary>
        private bool IsAccessibilityChangeBreaking(Accessibility oldAccess, Accessibility newAccess)
        {
            // Accessibility levels (most to least permissive): Public > Protected > Internal > Private
            var accessLevels = new Dictionary<Accessibility, int>
            {
                { Accessibility.Public, 4 },
                { Accessibility.Protected, 3 },
                { Accessibility.Internal, 2 },
                { Accessibility.Private, 1 }
            };

            var oldLevel = accessLevels.GetValueOrDefault(oldAccess, 0);
            var newLevel = accessLevels.GetValueOrDefault(newAccess, 0);

            // Breaking if accessibility decreased
            return newLevel < oldLevel;
        }

        /// <summary>
        /// Generates a unique key for a symbol
        /// </summary>
        private string GetSymbolKey(ISymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        /// <summary>
        /// Calculates statistics
        /// </summary>
        private void CalculateStatistics(APIChangeResults results)
        {
            // Changes by type
            results.ChangesByType = results.Changes
                .GroupBy(c => c.ChangeType)
                .ToDictionary(g => g.Key, g => g.Count());

            // Changes by symbol kind
            results.ChangesBySymbolKind = results.Changes
                .GroupBy(c => c.SymbolKind)
                .ToDictionary(g => g.Key, g => g.Count());

            // Changes by namespace
            results.ChangesByNamespace = results.Changes
                .Where(c => !string.IsNullOrEmpty(c.Namespace))
                .GroupBy(c => c.Namespace)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Determines semantic versioning recommendation
        /// </summary>
        private void DetermineVersioningRecommendation(APIChangeResults results)
        {
            if (results.BreakingChanges > 0)
            {
                results.RecommendedVersionBump = "Major";
                results.VersioningReason = $"Found {results.BreakingChanges} breaking change(s). Requires major version bump (e.g., 1.0.0 → 2.0.0).";
            }
            else if (results.AddedSymbols > 0)
            {
                results.RecommendedVersionBump = "Minor";
                results.VersioningReason = $"Added {results.AddedSymbols} new symbol(s) without breaking changes. Requires minor version bump (e.g., 1.0.0 → 1.1.0).";
            }
            else if (results.TotalChanges > 0)
            {
                results.RecommendedVersionBump = "Patch";
                results.VersioningReason = "Only internal changes detected. Requires patch version bump (e.g., 1.0.0 → 1.0.1).";
            }
            else
            {
                results.RecommendedVersionBump = "None";
                results.VersioningReason = "No API changes detected.";
            }
        }

        /// <summary>
        /// Visitor to collect public API symbols
        /// </summary>
        private class PublicAPISymbolVisitor : SymbolVisitor
        {
            private readonly bool _includeInternal;
            public List<ISymbol> Symbols { get; } = new();

            public PublicAPISymbolVisitor(bool includeInternal)
            {
                _includeInternal = includeInternal;
            }

            public override void VisitNamespace(INamespaceSymbol symbol)
            {
                foreach (var member in symbol.GetMembers())
                {
                    member.Accept(this);
                }
            }

            public override void VisitNamedType(INamedTypeSymbol symbol)
            {
                if (ShouldIncludeSymbol(symbol))
                {
                    Symbols.Add(symbol);

                    // Visit members
                    foreach (var member in symbol.GetMembers())
                    {
                        member.Accept(this);
                    }
                }
            }

            public override void VisitMethod(IMethodSymbol symbol)
            {
                if (ShouldIncludeSymbol(symbol) && !symbol.IsImplicitlyDeclared)
                {
                    Symbols.Add(symbol);
                }
            }

            public override void VisitProperty(IPropertySymbol symbol)
            {
                if (ShouldIncludeSymbol(symbol))
                {
                    Symbols.Add(symbol);
                }
            }

            public override void VisitField(IFieldSymbol symbol)
            {
                if (ShouldIncludeSymbol(symbol))
                {
                    Symbols.Add(symbol);
                }
            }

            public override void VisitEvent(IEventSymbol symbol)
            {
                if (ShouldIncludeSymbol(symbol))
                {
                    Symbols.Add(symbol);
                }
            }

            private bool ShouldIncludeSymbol(ISymbol symbol)
            {
                var accessibility = symbol.DeclaredAccessibility;

                if (accessibility == Accessibility.Public)
                    return true;

                if (_includeInternal && accessibility == Accessibility.Internal)
                    return true;

                return false;
            }
        }
    }
}
