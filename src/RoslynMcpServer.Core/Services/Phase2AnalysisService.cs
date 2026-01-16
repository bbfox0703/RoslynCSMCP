using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Text;

namespace RoslynMcpServer.Core.Services;

/// <summary>
/// Phase 2 Analysis Service - Advanced Refactoring and .NET-Specific Analysis
/// Provides tools for interface extraction, DI analysis, exception handling, and thread safety
/// </summary>
public class Phase2AnalysisService
{
    private readonly ILogger<Phase2AnalysisService> _logger;

    public Phase2AnalysisService(ILogger<Phase2AnalysisService> logger)
    {
        _logger = logger;
    }

    #region ExtractInterface (Fully Implemented)

    /// <summary>
    /// Extract an interface from a class
    /// </summary>
    public async Task<InterfaceExtractionResult> ExtractInterfaceAsync(
        string solutionPath,
        string typeName,
        string? interfaceName = null,
        string? targetNamespace = null)
    {
        var result = new InterfaceExtractionResult
        {
            Success = false
        };

        try
        {
            // Load solution
            using var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Find the target type
            var targetType = await FindTypeByNameAsync(solution, typeName);
            if (targetType == null)
            {
                result.ErrorMessage = $"Type '{typeName}' not found in solution.";
                return result;
            }

            // Verify it's a class (not already an interface)
            if (targetType.TypeKind == TypeKind.Interface)
            {
                result.ErrorMessage = $"'{typeName}' is already an interface.";
                return result;
            }

            // Set basic info
            result.ClassName = targetType.Name;
            result.Namespace = targetNamespace ?? targetType.ContainingNamespace?.ToDisplayString() ?? "YourNamespace";
            result.InterfaceName = interfaceName ?? $"I{targetType.Name}";

            // Check if interface name already exists
            var conflictingType = await FindTypeByNameAsync(solution, result.InterfaceName);
            if (conflictingType != null)
            {
                result.Warnings.Add($"Interface name '{result.InterfaceName}' already exists. Consider using a different name.");
            }

            // Extract public members
            var extractableMembers = new List<ExtractableMember>();

            // Extract public methods (excluding constructors, operators, etc.)
            foreach (var method in targetType.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (method.MethodKind != MethodKind.Ordinary)
                    continue;

                extractableMembers.Add(new ExtractableMember
                {
                    Name = method.Name,
                    MemberType = "Method",
                    ReturnType = method.ReturnType.ToDisplayString(),
                    Signature = GetMethodSignature(method),
                    Documentation = GetDocumentationComment(method),
                    IsSelected = true
                });
            }

            // Extract public properties
            foreach (var property in targetType.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.DeclaredAccessibility != Accessibility.Public)
                    continue;

                extractableMembers.Add(new ExtractableMember
                {
                    Name = property.Name,
                    MemberType = "Property",
                    ReturnType = property.Type.ToDisplayString(),
                    Signature = GetPropertySignature(property),
                    Documentation = GetDocumentationComment(property),
                    IsSelected = true
                });
            }

            // Extract public events
            foreach (var evt in targetType.GetMembers().OfType<IEventSymbol>())
            {
                if (evt.DeclaredAccessibility != Accessibility.Public)
                    continue;

                extractableMembers.Add(new ExtractableMember
                {
                    Name = evt.Name,
                    MemberType = "Event",
                    ReturnType = evt.Type.ToDisplayString(),
                    Signature = $"event {evt.Type.ToDisplayString()} {evt.Name};",
                    Documentation = GetDocumentationComment(evt),
                    IsSelected = true
                });
            }

            result.Members = extractableMembers;

            if (!extractableMembers.Any())
            {
                result.ErrorMessage = $"No public members found in '{typeName}' to extract.";
                return result;
            }

            // Generate interface code
            result.InterfaceCode = GenerateInterfaceCode(result);

            // Suggest file path
            var originalLocation = targetType.Locations.FirstOrDefault();
            if (originalLocation?.SourceTree?.FilePath != null)
            {
                var directory = Path.GetDirectoryName(originalLocation.SourceTree.FilePath);
                result.SuggestedFilePath = Path.Combine(directory!, $"{result.InterfaceName}.cs");
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in ExtractInterfaceAsync: {ex}");
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<INamedTypeSymbol?> FindTypeByNameAsync(Solution solution, string typeName)
    {
        foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();

                var typeDeclarations = root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(t => t.Identifier.Text == typeName);

                foreach (var typeDecl in typeDeclarations)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
                    if (symbol != null) return symbol;
                }
            }
        }

        return null;
    }

    private string GetMethodSignature(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p =>
            $"{p.Type.ToDisplayString()} {p.Name}"));

        var typeParameters = method.TypeParameters.Any()
            ? $"<{string.Join(", ", method.TypeParameters.Select(tp => tp.Name))}>"
            : "";

        return $"{method.ReturnType.ToDisplayString()} {method.Name}{typeParameters}({parameters});";
    }

    private string GetPropertySignature(IPropertySymbol property)
    {
        var accessors = new List<string>();
        if (property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
            accessors.Add("get;");
        if (property.SetMethod?.DeclaredAccessibility == Accessibility.Public)
            accessors.Add("set;");

        return $"{property.Type.ToDisplayString()} {property.Name} {{ {string.Join(" ", accessors)} }}";
    }

    private string GetDocumentationComment(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
            return string.Empty;

        // Extract summary from XML
        var summaryStart = xml.IndexOf("<summary>");
        var summaryEnd = xml.IndexOf("</summary>");
        if (summaryStart >= 0 && summaryEnd > summaryStart)
        {
            var summary = xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9).Trim();
            return summary;
        }

        return string.Empty;
    }

    private string GenerateInterfaceCode(InterfaceExtractionResult result)
    {
        var code = new StringBuilder();

        // Add namespace
        code.AppendLine($"namespace {result.Namespace};");
        code.AppendLine();

        // Add interface declaration
        code.AppendLine($"public interface {result.InterfaceName}");
        code.AppendLine("{");

        // Add members
        foreach (var member in result.Members.Where(m => m.IsSelected))
        {
            // Add documentation if available
            if (!string.IsNullOrWhiteSpace(member.Documentation))
            {
                code.AppendLine($"    /// <summary>");
                code.AppendLine($"    /// {member.Documentation}");
                code.AppendLine($"    /// </summary>");
            }

            // Add member
            code.AppendLine($"    {member.Signature}");
            code.AppendLine();
        }

        code.AppendLine("}");

        return code.ToString();
    }

    #endregion

    #region AnalyzeExceptionHandling (Framework - To be implemented)

    /// <summary>
    /// Analyze exception handling patterns and detect anti-patterns
    /// </summary>
    public async Task<ExceptionHandlingResults> AnalyzeExceptionHandlingAsync(
        string solutionPath,
        bool checkEmptyCatch = true,
        bool checkSwallowedExceptions = true,
        bool checkMissingUsing = true)
    {
        var results = new ExceptionHandlingResults();

        try
        {
            // Load solution
            using var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Analyze each project
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                results.AnalyzedProjects++;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync();

                    // 1. Find all try-catch-finally blocks
                    var tryStatements = root.DescendantNodes().OfType<TryStatementSyntax>();
                    results.TotalTryBlocks += tryStatements.Count();

                    foreach (var tryStatement in tryStatements)
                    {
                        var filePath = tree.FilePath ?? "Unknown";
                        var lineNumber = tryStatement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                        // Analyze each catch clause
                        foreach (var catchClause in tryStatement.Catches)
                        {
                            results.TotalCatchBlocks++;

                            // 2. Check for empty catch blocks
                            if (checkEmptyCatch && IsEmptyCatchBlock(catchClause))
                            {
                                results.Issues.Add(new ExceptionHandlingIssue
                                {
                                    IssueType = "EmptyCatch",
                                    Severity = "High",
                                    Description = "Empty catch block found. This silently swallows exceptions and makes debugging difficult.",
                                    Recommendation = "Add logging, rethrowing, or appropriate error handling. Consider using specific exception types.",
                                    FilePath = filePath,
                                    LineNumber = lineNumber,
                                    CodeSnippet = catchClause.ToString()
                                });
                                results.EmptyCatchCount++;
                            }

                            // 3. Check for swallowed exceptions (no logging, no rethrowing)
                            if (checkSwallowedExceptions && IsSwallowedException(catchClause, semanticModel))
                            {
                                results.Issues.Add(new ExceptionHandlingIssue
                                {
                                    IssueType = "SwallowedException",
                                    Severity = "Medium",
                                    Description = "Exception is caught but not logged or rethrown. This hides errors and makes debugging difficult.",
                                    Recommendation = "Add logging (e.g., _logger.LogError) or rethrow the exception if it cannot be handled.",
                                    FilePath = filePath,
                                    LineNumber = lineNumber,
                                    CodeSnippet = catchClause.ToString()
                                });
                                results.SwallowedExceptionCount++;
                            }

                            // 4. Check for generic Exception catches
                            if (IsGenericExceptionCatch(catchClause, semanticModel))
                            {
                                results.Issues.Add(new ExceptionHandlingIssue
                                {
                                    IssueType = "GenericException",
                                    Severity = "Low",
                                    Description = "Catching generic 'Exception' type. This can catch unexpected exceptions and hide programming errors.",
                                    Recommendation = "Catch specific exception types (e.g., IOException, ArgumentException) when possible.",
                                    FilePath = filePath,
                                    LineNumber = lineNumber,
                                    CodeSnippet = catchClause.Declaration?.Type.ToString() ?? "catch"
                                });
                                results.GenericExceptionCount++;
                            }
                        }
                    }

                    // 5. Check for missing using statements with IDisposable
                    if (checkMissingUsing)
                    {
                        var missingUsingIssues = FindMissingUsingStatements(root, semanticModel, tree.FilePath ?? "Unknown");
                        results.Issues.AddRange(missingUsingIssues);
                        results.MissingUsingCount += missingUsingIssues.Count;
                    }
                }
            }

            // Calculate severity counts
            results.HighCount = results.Issues.Count(i => i.Severity == "High");
            results.MediumCount = results.Issues.Count(i => i.Severity == "Medium");
            results.LowCount = results.Issues.Count(i => i.Severity == "Low");

            _logger.LogInformation($"AnalyzeExceptionHandling completed: {results.TotalIssues} issues found in {results.AnalyzedProjects} projects");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in AnalyzeExceptionHandlingAsync: {ex}");
            results.Warnings.Add(new OperationWarning
            {
                Context = "Exception Handling Analysis",
                Message = $"Analysis failed: {ex.Message}"
            });
        }

        return results;
    }

    private bool IsEmptyCatchBlock(CatchClauseSyntax catchClause)
    {
        // A catch block is empty if it has no statements
        return !catchClause.Block.Statements.Any();
    }

    private bool IsSwallowedException(CatchClauseSyntax catchClause, SemanticModel semanticModel)
    {
        // If it's empty, it's already flagged by IsEmptyCatchBlock
        if (!catchClause.Block.Statements.Any())
            return false;

        // Check if there's a throw statement (rethrowing is good)
        var hasThrow = catchClause.Block.DescendantNodes().OfType<ThrowStatementSyntax>().Any();
        if (hasThrow)
            return false;

        // Check if there's any logging-like invocation
        // Look for method invocations that might be logging
        var invocations = catchClause.Block.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var invocation in invocations)
        {
            var methodName = GetInvocationMethodName(invocation);

            // Common logging method patterns
            if (methodName != null && (
                methodName.Contains("Log") ||
                methodName.Contains("Write") ||
                methodName.Contains("Trace") ||
                methodName.Contains("Debug") ||
                methodName.Contains("Error") ||
                methodName.Contains("Warn") ||
                methodName.Contains("Info")))
            {
                return false; // Has logging, not swallowed
            }
        }

        // No throw and no logging = swallowed
        return true;
    }

    private string? GetInvocationMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null
        };
    }

    private bool IsGenericExceptionCatch(CatchClauseSyntax catchClause, SemanticModel semanticModel)
    {
        if (catchClause.Declaration == null)
            return false; // catch without type (catch-all)

        var exceptionType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
        if (exceptionType == null)
            return false;

        // Check if it's exactly System.Exception (not a derived type)
        return exceptionType.ToDisplayString() == "System.Exception";
    }

    private List<ExceptionHandlingIssue> FindMissingUsingStatements(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath)
    {
        var issues = new List<ExceptionHandlingIssue>();

        // Find all local variable declarations
        var localDeclarations = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>();

        foreach (var declaration in localDeclarations)
        {
            // Skip if it's already in a using statement
            if (declaration.Parent is UsingStatementSyntax)
                continue;

            // Check each variable declarator
            foreach (var variable in declaration.Declaration.Variables)
            {
                var variableSymbol = semanticModel.GetDeclaredSymbol(variable);
                if (variableSymbol is not ILocalSymbol localSymbol)
                    continue;

                // Check if the type implements IDisposable
                var type = localSymbol.Type;
                if (ImplementsIDisposable(type))
                {
                    var lineNumber = declaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    issues.Add(new ExceptionHandlingIssue
                    {
                        IssueType = "MissingUsing",
                        Severity = "Medium",
                        Description = $"Variable '{variable.Identifier.Text}' of type '{type.Name}' implements IDisposable but is not wrapped in a using statement.",
                        Recommendation = "Wrap in a using statement or using declaration to ensure proper resource disposal.",
                        FilePath = filePath,
                        LineNumber = lineNumber,
                        CodeSnippet = declaration.ToString()
                    });
                }
            }
        }

        return issues;
    }

    private bool ImplementsIDisposable(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        // Check if the type or any of its base types/interfaces implement IDisposable
        return type.AllInterfaces.Any(i => i.ToDisplayString() == "System.IDisposable");
    }

    #endregion

    #region AnalyzeDIContainer (Framework - To be implemented)

    /// <summary>
    /// Analyze dependency injection configuration for common issues
    /// </summary>
    public async Task<DIContainerResults> AnalyzeDIContainerAsync(
        string solutionPath,
        bool checkLifetimes = true,
        bool checkCircular = true,
        bool checkCaptive = true)
    {
        var results = new DIContainerResults();

        try
        {
            // Load solution
            using var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // Step 1: Build service registration map
            var serviceRegistrations = new Dictionary<string, ServiceRegistration>();

            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync();

                    // Find DI registration calls
                    var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

                    foreach (var invocation in invocations)
                    {
                        var registration = ParseDIRegistration(invocation, semanticModel, tree.FilePath ?? "Unknown");
                        if (registration != null)
                        {
                            results.AnalyzedServices++;
                            var key = registration.ServiceType;

                            // Check for multiple registrations
                            if (serviceRegistrations.ContainsKey(key))
                            {
                                results.Issues.Add(new DIContainerIssue
                                {
                                    IssueType = "MultipleRegistration",
                                    Severity = "Low",
                                    ServiceType = registration.ServiceType,
                                    ImplementationType = registration.ImplementationType,
                                    ServiceLifetime = registration.Lifetime,
                                    Description = $"Service '{registration.ServiceType}' is registered multiple times. Last registration wins.",
                                    Recommendation = "Review if multiple registrations are intentional. Consider using TryAdd* methods or removing duplicate registrations.",
                                    FilePath = registration.FilePath,
                                    LineNumber = registration.LineNumber
                                });
                            }

                            serviceRegistrations[key] = registration;
                        }
                    }
                }
            }

            // Step 2: Find constructor injection points and validate
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync();

                    // Find all class constructors
                    var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();

                    foreach (var constructor in constructors)
                    {
                        results.AnalyzedConstructors++;

                        var containingClass = constructor.Parent as ClassDeclarationSyntax;
                        if (containingClass == null) continue;

                        var classSymbol = semanticModel.GetDeclaredSymbol(containingClass);
                        if (classSymbol == null) continue;

                        var className = classSymbol.ToDisplayString();

                        // Analyze constructor parameters (dependencies)
                        foreach (var parameter in constructor.ParameterList.Parameters)
                        {
                            var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
                            if (parameterSymbol == null) continue;

                            var dependencyType = parameterSymbol.Type.ToDisplayString();

                            // Check if dependency is registered
                            if (!serviceRegistrations.ContainsKey(dependencyType))
                            {
                                // Check if it's a framework type (skip these)
                                if (IsFrameworkType(dependencyType))
                                    continue;

                                results.Issues.Add(new DIContainerIssue
                                {
                                    IssueType = "UnregisteredDependency",
                                    Severity = "High",
                                    ServiceType = dependencyType,
                                    ImplementationType = className,
                                    Description = $"Constructor of '{className}' depends on '{dependencyType}', which is not registered in the DI container.",
                                    Recommendation = $"Register '{dependencyType}' in the DI container using AddScoped, AddSingleton, or AddTransient.",
                                    FilePath = tree.FilePath ?? "Unknown",
                                    LineNumber = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                                });
                                results.UnregisteredCount++;
                            }
                            else if (checkCaptive && serviceRegistrations.ContainsKey(className))
                            {
                                // Check for captive dependencies
                                var consumerLifetime = serviceRegistrations[className].Lifetime;
                                var dependencyLifetime = serviceRegistrations[dependencyType].Lifetime;

                                if (IsCaptiveDependency(consumerLifetime, dependencyLifetime))
                                {
                                    results.Issues.Add(new DIContainerIssue
                                    {
                                        IssueType = "CaptiveDependency",
                                        Severity = "High",
                                        ServiceType = className,
                                        ImplementationType = dependencyType,
                                        ServiceLifetime = consumerLifetime,
                                        Description = $"Captive dependency detected: {consumerLifetime} service '{className}' depends on {dependencyLifetime} service '{dependencyType}'.",
                                        Recommendation = $"Change '{className}' to {dependencyLifetime} or '{dependencyType}' to {consumerLifetime}. A longer-lived service should not depend on a shorter-lived service.",
                                        FilePath = tree.FilePath ?? "Unknown",
                                        LineNumber = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                        DependencyChain = new List<string> { className, dependencyType }
                                    });
                                    results.CaptiveDependencyCount++;
                                }
                            }
                        }
                    }
                }
            }

            // Step 3: Detect circular dependencies
            if (checkCircular)
            {
                var circularDeps = DetectCircularDependencies(serviceRegistrations, solution);
                foreach (var cycle in circularDeps)
                {
                    results.Issues.Add(new DIContainerIssue
                    {
                        IssueType = "CircularDependency",
                        Severity = "Critical",
                        ServiceType = cycle.First(),
                        Description = $"Circular dependency detected: {string.Join(" → ", cycle)} → {cycle.First()}",
                        Recommendation = "Break the circular dependency by introducing an interface, using a factory pattern, or refactoring the design.",
                        FilePath = "Multiple Files",
                        LineNumber = 0,
                        DependencyChain = cycle
                    });
                    results.CircularDependencyCount++;
                }
            }

            // Calculate severity counts
            results.CriticalCount = results.Issues.Count(i => i.Severity == "Critical");
            results.HighCount = results.Issues.Count(i => i.Severity == "High");
            results.MediumCount = results.Issues.Count(i => i.Severity == "Medium");
            results.LowCount = results.Issues.Count(i => i.Severity == "Low");

            _logger.LogInformation($"AnalyzeDIContainer completed: {results.TotalIssues} issues found, {results.AnalyzedServices} services, {results.AnalyzedConstructors} constructors");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in AnalyzeDIContainerAsync: {ex}");
            results.Warnings.Add(new OperationWarning
            {
                Context = "DI Container Analysis",
                Message = $"Analysis failed: {ex.Message}"
            });
        }

        return results;
    }

    private class ServiceRegistration
    {
        public string ServiceType { get; set; } = string.Empty;
        public string ImplementationType { get; set; } = string.Empty;
        public string Lifetime { get; set; } = string.Empty;  // Singleton, Scoped, Transient
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
    }

    private ServiceRegistration? ParseDIRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string filePath)
    {
        var methodName = GetInvocationMethodName(invocation);
        if (methodName == null) return null;

        // Check if it's a DI registration method
        string? lifetime = methodName switch
        {
            "AddSingleton" => "Singleton",
            "AddScoped" => "Scoped",
            "AddTransient" => "Transient",
            "TryAddSingleton" => "Singleton",
            "TryAddScoped" => "Scoped",
            "TryAddTransient" => "Transient",
            _ => null
        };

        if (lifetime == null) return null;

        // Extract type arguments
        string serviceType = "";
        string implementationType = "";

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is GenericNameSyntax genericName)
        {
            var typeArgs = genericName.TypeArgumentList.Arguments;

            if (typeArgs.Count == 1)
            {
                // AddScoped<Service>() - service is implementation
                var typeInfo = semanticModel.GetTypeInfo(typeArgs[0]);
                serviceType = typeInfo.Type?.ToDisplayString() ?? typeArgs[0].ToString();
                implementationType = serviceType;
            }
            else if (typeArgs.Count == 2)
            {
                // AddScoped<IService, Service>()
                var serviceTypeInfo = semanticModel.GetTypeInfo(typeArgs[0]);
                var implTypeInfo = semanticModel.GetTypeInfo(typeArgs[1]);
                serviceType = serviceTypeInfo.Type?.ToDisplayString() ?? typeArgs[0].ToString();
                implementationType = implTypeInfo.Type?.ToDisplayString() ?? typeArgs[1].ToString();
            }
        }

        if (string.IsNullOrEmpty(serviceType))
            return null;

        return new ServiceRegistration
        {
            ServiceType = serviceType,
            ImplementationType = implementationType,
            Lifetime = lifetime,
            FilePath = filePath,
            LineNumber = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1
        };
    }

    private bool IsCaptiveDependency(string consumerLifetime, string dependencyLifetime)
    {
        // Singleton can depend on Singleton only
        // Scoped can depend on Singleton or Scoped
        // Transient can depend on anything

        if (consumerLifetime == "Singleton")
        {
            return dependencyLifetime != "Singleton";
        }

        if (consumerLifetime == "Scoped")
        {
            return dependencyLifetime == "Transient";
        }

        return false; // Transient can depend on anything
    }

    private bool IsFrameworkType(string typeName)
    {
        // Skip common framework types that don't need DI registration
        return typeName.StartsWith("Microsoft.Extensions.Logging.ILogger") ||
               typeName.StartsWith("Microsoft.Extensions.Configuration.IConfiguration") ||
               typeName.StartsWith("Microsoft.Extensions.Options.IOptions") ||
               typeName.StartsWith("System.") ||
               typeName == "string" ||
               typeName == "int" ||
               typeName == "bool";
    }

    private List<List<string>> DetectCircularDependencies(
        Dictionary<string, ServiceRegistration> registrations,
        Solution solution)
    {
        var cycles = new List<List<string>>();
        var dependencyGraph = BuildDependencyGraph(registrations, solution).Result;

        // Use DFS to detect cycles
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var currentPath = new List<string>();

        foreach (var service in dependencyGraph.Keys)
        {
            if (!visited.Contains(service))
            {
                FindCycles(service, dependencyGraph, visited, recursionStack, currentPath, cycles);
            }
        }

        return cycles;
    }

    private async Task<Dictionary<string, List<string>>> BuildDependencyGraph(
        Dictionary<string, ServiceRegistration> registrations,
        Solution solution)
    {
        var graph = new Dictionary<string, List<string>>();

        // Initialize graph with registered services
        foreach (var service in registrations.Keys)
        {
            graph[service] = new List<string>();
        }

        // Build dependency edges
        foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();

                var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();

                foreach (var constructor in constructors)
                {
                    var containingClass = constructor.Parent as ClassDeclarationSyntax;
                    if (containingClass == null) continue;

                    var classSymbol = semanticModel.GetDeclaredSymbol(containingClass);
                    if (classSymbol == null) continue;

                    var className = classSymbol.ToDisplayString();

                    // Only track if this class is registered as a service
                    if (!registrations.ContainsKey(className))
                        continue;

                    if (!graph.ContainsKey(className))
                        graph[className] = new List<string>();

                    foreach (var parameter in constructor.ParameterList.Parameters)
                    {
                        var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
                        if (parameterSymbol == null) continue;

                        var dependencyType = parameterSymbol.Type.ToDisplayString();

                        // Only track dependencies that are registered services
                        if (registrations.ContainsKey(dependencyType))
                        {
                            graph[className].Add(dependencyType);
                        }
                    }
                }
            }
        }

        return graph;
    }

    private bool FindCycles(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> currentPath,
        List<List<string>> cycles)
    {
        visited.Add(node);
        recursionStack.Add(node);
        currentPath.Add(node);

        if (graph.ContainsKey(node))
        {
            foreach (var neighbor in graph[node])
            {
                if (!visited.Contains(neighbor))
                {
                    if (FindCycles(neighbor, graph, visited, recursionStack, currentPath, cycles))
                    {
                        recursionStack.Remove(node);
                        currentPath.RemoveAt(currentPath.Count - 1);
                        return true;
                    }
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle
                    var cycleStartIndex = currentPath.IndexOf(neighbor);
                    var cycle = currentPath.Skip(cycleStartIndex).ToList();
                    cycles.Add(cycle);
                }
            }
        }

        recursionStack.Remove(node);
        currentPath.RemoveAt(currentPath.Count - 1);
        return false;
    }

    #endregion

    #region FindThreadSafetyIssues (Framework - To be implemented)

    /// <summary>
    /// Detect common thread safety issues and race conditions
    /// </summary>
    public async Task<ThreadSafetyResults> FindThreadSafetyIssuesAsync(
        string solutionPath,
        bool checkStaticFields = true,
        bool checkSharedState = true,
        bool checkCollections = true)
    {
        var results = new ThreadSafetyResults();

        try
        {
            // Load solution
            using var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                results.AnalyzedProjects++;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync();
                    var filePath = tree.FilePath ?? "Unknown";

                    results.AnalyzedFiles++;

                    // 1. Check for mutable static fields
                    if (checkStaticFields)
                    {
                        var staticFields = root.DescendantNodes()
                            .OfType<FieldDeclarationSyntax>()
                            .Where(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)));

                        foreach (var field in staticFields)
                        {
                            var isReadOnly = field.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));
                            var isConst = field.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));

                            if (!isReadOnly && !isConst)
                            {
                                foreach (var variable in field.Declaration.Variables)
                                {
                                    var fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                                    if (fieldSymbol == null) continue;

                                    var fieldType = fieldSymbol.Type.ToDisplayString();

                                    // Check if it's a potentially problematic mutable type
                                    if (IsMutableType(fieldType))
                                    {
                                        results.Issues.Add(new ThreadSafetyIssue
                                        {
                                            IssueType = "MutableStaticField",
                                            Severity = "High",
                                            Description = $"Mutable static field '{variable.Identifier.Text}' of type '{fieldType}' can cause race conditions in multi-threaded scenarios.",
                                            Recommendation = "Make field 'readonly' if possible, or use thread-safe alternatives like ConcurrentDictionary, or add lock-based synchronization.",
                                            FilePath = filePath,
                                            LineNumber = variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                            MemberName = variable.Identifier.Text,
                                            CodeSnippet = field.ToString()
                                        });
                                        results.MutableStaticCount++;
                                    }
                                }
                            }
                        }
                    }

                    // 2. Check for non-thread-safe collection usage in static/field contexts
                    if (checkCollections)
                    {
                        var fields = root.DescendantNodes().OfType<FieldDeclarationSyntax>();

                        foreach (var field in fields)
                        {
                            var isStatic = field.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                            var isReadOnly = field.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));

                            foreach (var variable in field.Declaration.Variables)
                            {
                                var fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                                if (fieldSymbol == null) continue;

                                var fieldType = fieldSymbol.Type.ToDisplayString();

                                if (IsNonThreadSafeCollection(fieldType) && (isStatic || !isReadOnly))
                                {
                                    results.Issues.Add(new ThreadSafetyIssue
                                    {
                                        IssueType = "UnsafeCollection",
                                        Severity = isStatic ? "High" : "Medium",
                                        Description = $"Field '{variable.Identifier.Text}' uses non-thread-safe collection type '{fieldType}'. This can cause race conditions when accessed by multiple threads.",
                                        Recommendation = $"Use thread-safe alternatives: ConcurrentDictionary, ConcurrentBag, ConcurrentQueue, or ImmutableList. Or protect access with locks.",
                                        FilePath = filePath,
                                        LineNumber = variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                        MemberName = variable.Identifier.Text,
                                        CodeSnippet = field.ToString()
                                    });
                                    results.UnsafeCollectionCount++;
                                }
                            }
                        }
                    }

                    // 3. Detect double-checked locking patterns
                    var ifStatements = root.DescendantNodes().OfType<IfStatementSyntax>();
                    foreach (var ifStatement in ifStatements)
                    {
                        var lockStatements = ifStatement.Statement.DescendantNodesAndSelf().OfType<LockStatementSyntax>();
                        foreach (var lockStatement in lockStatements)
                        {
                            var innerIfStatements = lockStatement.Statement.DescendantNodes().OfType<IfStatementSyntax>();
                            if (innerIfStatements.Any())
                            {
                                // Potential double-checked locking
                                results.Issues.Add(new ThreadSafetyIssue
                                {
                                    IssueType = "DoubleCheckLocking",
                                    Severity = "Medium",
                                    Description = "Possible double-checked locking pattern detected. This pattern can be unsafe without proper memory barriers (volatile keyword).",
                                    Recommendation = "Ensure the checked field is marked 'volatile', or use Lazy<T> for thread-safe lazy initialization.",
                                    FilePath = filePath,
                                    LineNumber = ifStatement.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                    CodeSnippet = ifStatement.ToString().Substring(0, Math.Min(100, ifStatement.ToString().Length))
                                });
                                results.DoubleLockingCount++;
                            }
                        }
                    }

                    // 4. Detect async/await patterns that may cause deadlocks
                    var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                    foreach (var method in methods)
                    {
                        var isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));

                        // Look for .Result or .Wait() calls in async methods
                        var memberAccesses = method.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
                        foreach (var memberAccess in memberAccesses)
                        {
                            var memberName = memberAccess.Name.Identifier.Text;

                            if (memberName == "Result" || memberName == "Wait")
                            {
                                var expressionType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
                                if (expressionType != null && IsTaskType(expressionType.ToDisplayString()))
                                {
                                    results.Issues.Add(new ThreadSafetyIssue
                                    {
                                        IssueType = "AsyncDeadlock",
                                        Severity = "High",
                                        Description = $"Synchronous blocking call '.{memberName}' on Task in {(isAsync ? "async" : "")} method '{method.Identifier.Text}'. This can cause deadlocks in UI or ASP.NET contexts.",
                                        Recommendation = "Use 'await' instead of '.Result' or '.Wait()'. If you must block, use '.GetAwaiter().GetResult()' or '.ConfigureAwait(false)'.",
                                        FilePath = filePath,
                                        LineNumber = memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                        MemberName = method.Identifier.Text,
                                        CodeSnippet = memberAccess.ToString()
                                    });
                                    results.AsyncDeadlockCount++;
                                }
                            }
                        }

                        // Look for .GetAwaiter().GetResult() in async methods (less problematic but worth noting)
                        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
                        foreach (var invocation in invocations)
                        {
                            if (invocation.Expression is MemberAccessExpressionSyntax getResultAccess &&
                                getResultAccess.Name.Identifier.Text == "GetResult")
                            {
                                if (getResultAccess.Expression is InvocationExpressionSyntax getAwaiterInvocation &&
                                    getAwaiterInvocation.Expression is MemberAccessExpressionSyntax getAwaiterAccess &&
                                    getAwaiterAccess.Name.Identifier.Text == "GetAwaiter")
                                {
                                    // This pattern is better but still worth flagging
                                    if (isAsync)
                                    {
                                        results.Issues.Add(new ThreadSafetyIssue
                                        {
                                            IssueType = "AsyncBlockingPattern",
                                            Severity = "Low",
                                            Description = $"Using '.GetAwaiter().GetResult()' in async method '{method.Identifier.Text}'. While safer than .Result, this still blocks.",
                                            Recommendation = "Prefer 'await' in async methods. Use .GetAwaiter().GetResult() only when absolutely necessary.",
                                            FilePath = filePath,
                                            LineNumber = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                            MemberName = method.Identifier.Text,
                                            CodeSnippet = invocation.ToString()
                                        });
                                    }
                                }
                            }
                        }
                    }

                    // 5. Detect shared state without synchronization (instance fields accessed from multiple async methods)
                    if (checkSharedState)
                    {
                        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
                        foreach (var classDecl in classes)
                        {
                            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                            if (classSymbol == null) continue;

                            // Find instance fields
                            var instanceFields = classDecl.DescendantNodes()
                                .OfType<FieldDeclarationSyntax>()
                                .Where(f => !f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                                .SelectMany(f => f.Declaration.Variables.Select(v => v.Identifier.Text))
                                .ToHashSet();

                            // Find async methods in this class
                            var asyncMethods = classDecl.DescendantNodes()
                                .OfType<MethodDeclarationSyntax>()
                                .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.AsyncKeyword)))
                                .ToList();

                            // If there are multiple async methods accessing instance fields, flag it
                            if (asyncMethods.Count > 1 && instanceFields.Any())
                            {
                                foreach (var field in instanceFields)
                                {
                                    var accessCount = asyncMethods.Count(m =>
                                        m.DescendantNodes()
                                        .OfType<IdentifierNameSyntax>()
                                        .Any(id => id.Identifier.Text == field));

                                    if (accessCount > 1)
                                    {
                                        results.Issues.Add(new ThreadSafetyIssue
                                        {
                                            IssueType = "SharedStateAccess",
                                            Severity = "Medium",
                                            Description = $"Instance field '{field}' in class '{classDecl.Identifier.Text}' is accessed by multiple async methods. This may cause race conditions if methods run concurrently.",
                                            Recommendation = "Consider using locks, SemaphoreSlim for async coordination, or making the field immutable. Review if concurrent access is possible.",
                                            FilePath = filePath,
                                            LineNumber = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                            MemberName = field
                                        });
                                        results.SharedStateCount++;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Calculate severity counts
            results.CriticalCount = results.Issues.Count(i => i.Severity == "Critical");
            results.HighCount = results.Issues.Count(i => i.Severity == "High");
            results.MediumCount = results.Issues.Count(i => i.Severity == "Medium");
            results.LowCount = results.Issues.Count(i => i.Severity == "Low");

            _logger.LogInformation($"FindThreadSafetyIssues completed: {results.TotalIssues} issues found in {results.AnalyzedProjects} projects");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in FindThreadSafetyIssuesAsync: {ex}");
            results.Warnings.Add(new OperationWarning
            {
                Context = "Thread Safety Analysis",
                Message = $"Analysis failed: {ex.Message}"
            });
        }

        return results;
    }

    private bool IsMutableType(string typeName)
    {
        // Check for common mutable types
        return typeName.StartsWith("System.Collections.Generic.List") ||
               typeName.StartsWith("System.Collections.Generic.Dictionary") ||
               typeName.StartsWith("System.Collections.Generic.HashSet") ||
               typeName.StartsWith("System.Collections.Generic.Queue") ||
               typeName.StartsWith("System.Collections.Generic.Stack") ||
               typeName.StartsWith("System.Text.StringBuilder") ||
               (!typeName.Contains("ReadOnly") && !typeName.Contains("Immutable") &&
                (typeName.Contains("[]") || typeName.Contains("List") || typeName.Contains("Dictionary")));
    }

    private bool IsNonThreadSafeCollection(string typeName)
    {
        // Non-thread-safe collection types
        return typeName.StartsWith("System.Collections.Generic.List<") ||
               typeName.StartsWith("System.Collections.Generic.Dictionary<") ||
               typeName.StartsWith("System.Collections.Generic.HashSet<") ||
               typeName.StartsWith("System.Collections.Generic.Queue<") ||
               typeName.StartsWith("System.Collections.Generic.Stack<") ||
               typeName.StartsWith("System.Collections.ArrayList") ||
               typeName.StartsWith("System.Collections.Hashtable");
    }

    private bool IsTaskType(string typeName)
    {
        return typeName.StartsWith("System.Threading.Tasks.Task") ||
               typeName == "System.Threading.Tasks.Task";
    }

    #endregion
}
