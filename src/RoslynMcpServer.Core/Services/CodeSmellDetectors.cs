using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Core.Models;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Detectors for various code smells
    /// </summary>
    public static class CodeSmellDetectors
    {
        #region Long Method

        public static List<CodeSmell> DetectLongMethods(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                var lineSpan = method.GetLocation().GetLineSpan();
                var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

                // Thresholds: 50+ lines = High, 30-49 = Medium, 20-29 = Low
                string? severity = lineCount >= 50 ? "High" : lineCount >= 30 ? "Medium" : lineCount >= 20 ? "Low" : null;

                if (severity != null)
                {
                    var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

                    smells.Add(new CodeSmell
                    {
                        SmellType = "LongMethod",
                        Severity = severity,
                        Title = $"Method '{method.Identifier.Text}' is too long",
                        Description = $"Method has {lineCount} lines of code. Long methods are harder to understand, test, and maintain.",
                        Recommendation = "Consider extracting smaller methods. Break down complex logic into well-named helper methods. Aim for methods under 20 lines.",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = method.Identifier.Text,
                        SymbolKind = "Method",
                        Metrics = new Dictionary<string, object>
                        {
                            ["LineCount"] = lineCount,
                            ["ParameterCount"] = method.ParameterList.Parameters.Count,
                            ["ContainingType"] = containingType?.Identifier.Text ?? "Unknown"
                        },
                        CodeSnippet = GetMethodSignature(method)
                    });
                }
            }

            return smells;
        }

        #endregion

        #region Large Class

        public static List<CodeSmell> DetectLargeClasses(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

            foreach (var type in types)
            {
                var lineSpan = type.GetLocation().GetLineSpan();
                var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

                var members = type.Members;
                var memberCount = members.Count;
                var methods = members.OfType<MethodDeclarationSyntax>().Count();
                var properties = members.OfType<PropertyDeclarationSyntax>().Count();
                var fields = members.OfType<FieldDeclarationSyntax>().Count();

                // Thresholds: 500+ lines OR 30+ members = High, 300+ lines OR 20+ members = Medium
                bool isLarge = lineCount >= 500 || memberCount >= 30;
                bool isMedium = lineCount >= 300 || memberCount >= 20;

                string? severity = isLarge ? "High" : isMedium ? "Medium" : null;

                if (severity != null)
                {
                    smells.Add(new CodeSmell
                    {
                        SmellType = "LargeClass",
                        Severity = severity,
                        Title = $"Class '{type.Identifier.Text}' is too large",
                        Description = $"Class has {lineCount} lines and {memberCount} members. Large classes violate Single Responsibility Principle and are hard to maintain.",
                        Recommendation = "Consider splitting this class into smaller, focused classes. Use Extract Class refactoring. Group related members into separate classes.",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = type.Identifier.Text,
                        SymbolKind = type.Kind().ToString().Replace("Declaration", ""),
                        Metrics = new Dictionary<string, object>
                        {
                            ["LineCount"] = lineCount,
                            ["TotalMembers"] = memberCount,
                            ["Methods"] = methods,
                            ["Properties"] = properties,
                            ["Fields"] = fields
                        },
                        CodeSnippet = $"{type.Keyword.Text} {type.Identifier.Text}"
                    });
                }
            }

            return smells;
        }

        #endregion

        #region Long Parameter List

        public static List<CodeSmell> DetectLongParameterLists(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                var paramCount = method.ParameterList.Parameters.Count;

                // Thresholds: 6+ params = High, 5 = Medium, 4 = Low
                string? severity = paramCount >= 6 ? "High" : paramCount == 5 ? "Medium" : paramCount == 4 ? "Low" : null;

                if (severity != null)
                {
                    var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                    var lineSpan = method.GetLocation().GetLineSpan();

                    smells.Add(new CodeSmell
                    {
                        SmellType = "LongParameterList",
                        Severity = severity,
                        Title = $"Method '{method.Identifier.Text}' has too many parameters",
                        Description = $"Method has {paramCount} parameters. Long parameter lists are hard to understand and use. They often indicate poor abstraction.",
                        Recommendation = "Consider: 1) Introduce Parameter Object pattern, 2) Use Builder pattern, 3) Break method into smaller methods, 4) Use configuration objects.",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = method.Identifier.Text,
                        SymbolKind = "Method",
                        Metrics = new Dictionary<string, object>
                        {
                            ["ParameterCount"] = paramCount,
                            ["Parameters"] = string.Join(", ", method.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier}")),
                            ["ContainingType"] = containingType?.Identifier.Text ?? "Unknown"
                        },
                        CodeSnippet = GetMethodSignature(method)
                    });
                }
            }

            return smells;
        }

        #endregion

        #region Primitive Obsession

        public static List<CodeSmell> DetectPrimitiveObsession(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            // Look for methods with multiple primitive parameters of the same type
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                var parameters = method.ParameterList.Parameters;
                if (parameters.Count < 3) continue;

                // Group by primitive types
                var primitiveGroups = parameters
                    .Where(p => IsPrimitiveType(p.Type?.ToString() ?? ""))
                    .GroupBy(p => p.Type?.ToString())
                    .Where(g => g.Count() >= 3)
                    .ToList();

                if (primitiveGroups.Any())
                {
                    var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                    var lineSpan = method.GetLocation().GetLineSpan();
                    var mostCommonType = primitiveGroups.OrderByDescending(g => g.Count()).First();

                    smells.Add(new CodeSmell
                    {
                        SmellType = "PrimitiveObsession",
                        Severity = "Medium",
                        Title = $"Method '{method.Identifier.Text}' uses many primitive parameters",
                        Description = $"Method has {mostCommonType.Count()} parameters of type '{mostCommonType.Key}'. This suggests missing domain objects.",
                        Recommendation = "Create a value object or data class to represent these related primitives. This improves type safety and encapsulation.",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = method.Identifier.Text,
                        SymbolKind = "Method",
                        Metrics = new Dictionary<string, object>
                        {
                            ["PrimitiveType"] = mostCommonType.Key ?? "Unknown",
                            ["Count"] = mostCommonType.Count(),
                            ["TotalParameters"] = parameters.Count,
                            ["ContainingType"] = containingType?.Identifier.Text ?? "Unknown"
                        },
                        CodeSnippet = GetMethodSignature(method)
                    });
                }
            }

            return smells;
        }

        #endregion

        #region Switch Statements

        public static List<CodeSmell> DetectSwitchStatements(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            // Find switch statements with many cases
            var switchStatements = root.DescendantNodes().OfType<SwitchStatementSyntax>();

            foreach (var switchStmt in switchStatements)
            {
                var caseCount = switchStmt.Sections.Count;

                // Thresholds: 8+ cases = High, 5-7 = Medium
                string? severity = caseCount >= 8 ? "High" : caseCount >= 5 ? "Medium" : null;

                if (severity != null)
                {
                    var containingMethod = switchStmt.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    var containingType = switchStmt.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                    var lineSpan = switchStmt.GetLocation().GetLineSpan();

                    smells.Add(new CodeSmell
                    {
                        SmellType = "SwitchStatements",
                        Severity = severity,
                        Title = $"Large switch statement with {caseCount} cases",
                        Description = $"Switch statement has {caseCount} cases. Large switches are hard to maintain and often indicate missing polymorphism.",
                        Recommendation = "Consider using polymorphism: 1) Replace with Strategy pattern, 2) Use polymorphic methods, 3) Apply State pattern if appropriate.",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = containingMethod?.Identifier.Text ?? "Unknown",
                        SymbolKind = "SwitchStatement",
                        Metrics = new Dictionary<string, object>
                        {
                            ["CaseCount"] = caseCount,
                            ["ContainingMethod"] = containingMethod?.Identifier.Text ?? "Unknown",
                            ["ContainingType"] = containingType?.Identifier.Text ?? "Unknown"
                        },
                        CodeSnippet = $"switch ({switchStmt.Expression}) {{ {caseCount} cases }}"
                    });
                }
            }

            return smells;
        }

        #endregion

        #region Data Clumps

        public static List<CodeSmell> DetectDataClumps(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

            // Find parameter patterns that appear in multiple methods
            var parameterSignatures = new Dictionary<string, List<MethodDeclarationSyntax>>();

            foreach (var method in methods)
            {
                if (method.ParameterList.Parameters.Count < 3) continue;

                // Create signature from parameter types (ignoring names)
                var signature = string.Join(",", method.ParameterList.Parameters
                    .Select(p => p.Type?.ToString() ?? "unknown")
                    .OrderBy(t => t));

                if (!parameterSignatures.ContainsKey(signature))
                    parameterSignatures[signature] = new List<MethodDeclarationSyntax>();

                parameterSignatures[signature].Add(method);
            }

            // Report clumps (same parameter pattern in 2+ methods)
            foreach (var kvp in parameterSignatures.Where(kv => kv.Value.Count >= 2))
            {
                foreach (var method in kvp.Value)
                {
                    var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                    var lineSpan = method.GetLocation().GetLineSpan();

                    smells.Add(new CodeSmell
                    {
                        SmellType = "DataClumps",
                        Severity = "Medium",
                        Title = $"Method '{method.Identifier.Text}' has repeated parameter pattern",
                        Description = $"Parameter pattern appears in {kvp.Value.Count} methods. Data clumps suggest missing abstractions.",
                        Recommendation = "Extract these parameters into a class. Create a Parameter Object or Value Object to group related data.",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = method.Identifier.Text,
                        SymbolKind = "Method",
                        Metrics = new Dictionary<string, object>
                        {
                            ["OccurrenceCount"] = kvp.Value.Count,
                            ["ParameterPattern"] = kvp.Key,
                            ["ParameterCount"] = method.ParameterList.Parameters.Count,
                            ["RelatedMethods"] = string.Join(", ", kvp.Value.Select(m => m.Identifier.Text)),
                            ["ContainingType"] = containingType?.Identifier.Text ?? "Unknown"
                        },
                        CodeSnippet = GetMethodSignature(method)
                    });
                }
            }

            return smells;
        }

        #endregion

        #region Feature Envy

        public static List<CodeSmell> DetectFeatureEnvy(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                try
                {
                    var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                    if (containingType == null) continue;

                    // Count member accesses
                    var memberAccesses = method.DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .ToList();

                    if (memberAccesses.Count < 5) continue; // Need reasonable sample size

                    // Group by accessed type
                    var accessesByType = new Dictionary<string, int>();

                    foreach (var access in memberAccesses)
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(access);
                        var symbol = symbolInfo.Symbol;

                        if (symbol?.ContainingType != null)
                        {
                            var typeName = symbol.ContainingType.Name;
                            if (!accessesByType.ContainsKey(typeName))
                                accessesByType[typeName] = 0;
                            accessesByType[typeName]++;
                        }
                    }

                    // Find if we access another type more than our own
                    var ownTypeName = containingType.Identifier.Text;
                    var ownAccesses = accessesByType.GetValueOrDefault(ownTypeName, 0);

                    var mostAccessedOther = accessesByType
                        .Where(kv => kv.Key != ownTypeName)
                        .OrderByDescending(kv => kv.Value)
                        .FirstOrDefault();

                    // Feature envy if we access another type more than our own (and it's significant)
                    if (mostAccessedOther.Value > ownAccesses && mostAccessedOther.Value >= 3)
                    {
                        var lineSpan = method.GetLocation().GetLineSpan();

                        smells.Add(new CodeSmell
                        {
                            SmellType = "FeatureEnvy",
                            Severity = "Medium",
                            Title = $"Method '{method.Identifier.Text}' envies class '{mostAccessedOther.Key}'",
                            Description = $"Method accesses '{mostAccessedOther.Key}' {mostAccessedOther.Value} times vs own class {ownAccesses} times. This suggests the method is in the wrong class.",
                            Recommendation = "Consider moving this method to the class it uses most. Use Move Method refactoring or Extract Method to the envied class.",
                            FilePath = syntaxTree.FilePath,
                            FileName = Path.GetFileName(syntaxTree.FilePath),
                            ProjectName = projectName,
                            LineNumber = lineSpan.StartLinePosition.Line + 1,
                            SymbolName = method.Identifier.Text,
                            SymbolKind = "Method",
                            Metrics = new Dictionary<string, object>
                            {
                                ["EnviedClass"] = mostAccessedOther.Key,
                                ["EnviedAccesses"] = mostAccessedOther.Value,
                                ["OwnAccesses"] = ownAccesses,
                                ["ContainingType"] = ownTypeName
                            },
                            CodeSnippet = GetMethodSignature(method)
                        });
                    }
                }
                catch
                {
                    // Skip on errors (e.g., semantic model issues)
                    continue;
                }
            }

            return smells;
        }

        #endregion

        #region Message Chains

        public static List<CodeSmell> DetectMessageChains(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                // Find chained member accesses (a.b.c.d.e...)
                var chainedAccesses = method.DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Where(ma => HasLongChain(ma, 3)) // 3+ levels
                    .ToList();

                foreach (var chain in chainedAccesses)
                {
                    var chainLength = GetChainLength(chain);

                    if (chainLength >= 3)
                    {
                        var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                        var lineSpan = chain.GetLocation().GetLineSpan();

                        smells.Add(new CodeSmell
                        {
                            SmellType = "MessageChains",
                            Severity = chainLength >= 5 ? "High" : "Medium",
                            Title = $"Long message chain detected ({chainLength} levels)",
                            Description = $"Found {chainLength}-level method chain. Message chains create tight coupling and violate Law of Demeter.",
                            Recommendation = "Use Hide Delegate pattern. Create intermediate methods in the immediate collaborator. Consider if this reveals a missing abstraction.",
                            FilePath = syntaxTree.FilePath,
                            FileName = Path.GetFileName(syntaxTree.FilePath),
                            ProjectName = projectName,
                            LineNumber = lineSpan.StartLinePosition.Line + 1,
                            SymbolName = method.Identifier.Text,
                            SymbolKind = "Method",
                            Metrics = new Dictionary<string, object>
                            {
                                ["ChainLength"] = chainLength,
                                ["ChainExpression"] = chain.ToString(),
                                ["ContainingMethod"] = method.Identifier.Text,
                                ["ContainingType"] = containingType?.Identifier.Text ?? "Unknown"
                            },
                            CodeSnippet = chain.ToString()
                        });
                    }
                }
            }

            return smells;
        }

        #endregion

        #region Middle Man

        public static List<CodeSmell> DetectMiddleMan(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

            foreach (var type in types)
            {
                var methods = type.Members.OfType<MethodDeclarationSyntax>().ToList();
                if (methods.Count < 3) continue;

                // Count methods that just delegate
                int delegatingMethods = 0;

                foreach (var method in methods)
                {
                    if (IsSimpleDelegation(method))
                        delegatingMethods++;
                }

                // If >50% of methods just delegate, it's a middle man
                double delegationRatio = (double)delegatingMethods / methods.Count;

                if (delegationRatio > 0.5 && delegatingMethods >= 3)
                {
                    var lineSpan = type.GetLocation().GetLineSpan();

                    smells.Add(new CodeSmell
                    {
                        SmellType = "MiddleMan",
                        Severity = delegationRatio > 0.75 ? "High" : "Medium",
                        Title = $"Class '{type.Identifier.Text}' is a middle man",
                        Description = $"{delegatingMethods} out of {methods.Count} methods just delegate to another class. Middle men add unnecessary indirection.",
                        Recommendation = "Consider: 1) Remove Middle Man - let clients call the real object directly, 2) Inline the class if it adds no value, 3) Add actual behavior to justify its existence.",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = type.Identifier.Text,
                        SymbolKind = type.Kind().ToString().Replace("Declaration", ""),
                        Metrics = new Dictionary<string, object>
                        {
                            ["DelegatingMethods"] = delegatingMethods,
                            ["TotalMethods"] = methods.Count,
                            ["DelegationRatio"] = Math.Round(delegationRatio * 100, 1)
                        },
                        CodeSnippet = $"{type.Keyword.Text} {type.Identifier.Text}"
                    });
                }
            }

            return smells;
        }

        #endregion

        #region Speculative Generality

        public static List<CodeSmell> DetectSpeculativeGenerality(SyntaxTree syntaxTree, SemanticModel semanticModel, string projectName)
        {
            var smells = new List<CodeSmell>();
            var root = syntaxTree.GetRoot();

            // Look for abstract classes with only one implementation (checked in same file)
            var abstractClasses = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)))
                .ToList();

            foreach (var abstractClass in abstractClasses)
            {
                var lineSpan = abstractClass.GetLocation().GetLineSpan();

                // Check if it has abstract members
                var abstractMembers = abstractClass.Members
                    .Where(m => m is MethodDeclarationSyntax method &&
                                method.Modifiers.Any(mod => mod.IsKind(SyntaxKind.AbstractKeyword)))
                    .ToList();

                if (abstractMembers.Count > 0)
                {
                    // Simple heuristic: if it's abstract but small and in isolation, might be speculative
                    // Real check would need cross-file analysis (count implementations)
                    var methodCount = abstractClass.Members.OfType<MethodDeclarationSyntax>().Count();

                    smells.Add(new CodeSmell
                    {
                        SmellType = "SpeculativeGenerality",
                        Severity = "Low",
                        Title = $"Abstract class '{abstractClass.Identifier.Text}' may be speculative",
                        Description = $"Abstract class with {abstractMembers.Count} abstract members. May indicate premature abstraction if it has few implementations.",
                        Recommendation = "Verify this abstraction is needed. If there's only one implementation, consider removing the abstraction. Follow YAGNI (You Aren't Gonna Need It).",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = abstractClass.Identifier.Text,
                        SymbolKind = "AbstractClass",
                        Metrics = new Dictionary<string, object>
                        {
                            ["AbstractMembers"] = abstractMembers.Count,
                            ["TotalMembers"] = methodCount
                        },
                        CodeSnippet = $"abstract class {abstractClass.Identifier.Text}"
                    });
                }
            }

            // Look for interfaces with only one method (potential over-engineering)
            var interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>();

            foreach (var iface in interfaces)
            {
                var members = iface.Members;
                if (members.Count == 1)
                {
                    var lineSpan = iface.GetLocation().GetLineSpan();

                    smells.Add(new CodeSmell
                    {
                        SmellType = "SpeculativeGenerality",
                        Severity = "Low",
                        Title = $"Interface '{iface.Identifier.Text}' has only one member",
                        Description = "Single-member interfaces may indicate over-engineering. Consider if this abstraction is necessary.",
                        Recommendation = "If there's only one implementation, consider using the concrete class directly. Add interfaces when you actually need polymorphism.",
                        FilePath = syntaxTree.FilePath,
                        FileName = Path.GetFileName(syntaxTree.FilePath),
                        ProjectName = projectName,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        SymbolName = iface.Identifier.Text,
                        SymbolKind = "Interface",
                        Metrics = new Dictionary<string, object>
                        {
                            ["MemberCount"] = members.Count
                        },
                        CodeSnippet = $"interface {iface.Identifier.Text}"
                    });
                }
            }

            return smells;
        }

        #endregion

        #region Helper Methods

        private static bool IsPrimitiveType(string typeName)
        {
            return typeName switch
            {
                "int" or "Int32" or "long" or "Int64" or "short" or "Int16" or
                "byte" or "Byte" or "bool" or "Boolean" or "float" or "Single" or
                "double" or "Double" or "decimal" or "Decimal" or "char" or "Char" or
                "string" or "String" => true,
                _ => false
            };
        }

        private static string GetMethodSignature(MethodDeclarationSyntax method)
        {
            var returnType = method.ReturnType?.ToString() ?? "void";
            var parameters = string.Join(", ", method.ParameterList.Parameters.Select(p =>
                $"{p.Type} {p.Identifier}"));
            return $"{returnType} {method.Identifier}({parameters})";
        }

        private static bool HasLongChain(MemberAccessExpressionSyntax access, int minLength)
        {
            return GetChainLength(access) >= minLength;
        }

        private static int GetChainLength(MemberAccessExpressionSyntax access)
        {
            int length = 1;
            var current = access.Expression;

            while (current is MemberAccessExpressionSyntax memberAccess)
            {
                length++;
                current = memberAccess.Expression;
            }

            return length;
        }

        private static bool IsSimpleDelegation(MethodDeclarationSyntax method)
        {
            // Simple delegation: method body has single statement that's a method call or return
            var body = method.Body;
            if (body == null || body.Statements.Count != 1) return false;

            var statement = body.Statements[0];

            // Check for return delegation or expression statement
            return statement is ReturnStatementSyntax ||
                   statement is ExpressionStatementSyntax expr &&
                   expr.Expression is InvocationExpressionSyntax;
        }

        #endregion
    }
}
