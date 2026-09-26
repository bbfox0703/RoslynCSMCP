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

    #region AnalyzeExceptionHandling

    /// <summary>
    /// Analyze exception handling patterns and detect anti-patterns
    /// </summary>
    public async Task<ExceptionHandlingResults> AnalyzeExceptionHandlingAsync(
        string solutionPath,
        bool checkEmptyCatch = true,
        bool checkSwallowedExceptions = true,
        bool checkMissingUsing = true,
        bool checkGenericCatch = true)
    {
        var results = new ExceptionHandlingResults();
        var options = new ExceptionHandlingOptions(checkEmptyCatch, checkSwallowedExceptions, checkMissingUsing, checkGenericCatch);

        try
        {
            // Load solution
            using var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // A file compiled into several projects (multi-targeting, linked files) is analyzed once.
            var analyzedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Analyze each project
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                results.AnalyzedProjects++;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    if (!string.IsNullOrEmpty(tree.FilePath) && !analyzedFiles.Add(tree.FilePath))
                        continue;

                    AnalyzeExceptionHandlingInTree(tree, compilation.GetSemanticModel(tree), project.Name, options, results);
                }
            }

            FinalizeExceptionHandlingCounts(results);

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

    internal readonly record struct ExceptionHandlingOptions(
        bool CheckEmptyCatch,
        bool CheckSwallowedExceptions,
        bool CheckMissingUsing,
        bool CheckGenericCatch);

    /// <summary>
    /// Runs the exception-handling checks over one syntax tree and appends issues and raw counts to results.
    /// Call <see cref="FinalizeExceptionHandlingCounts"/> once all trees are analyzed.
    /// </summary>
    internal static void AnalyzeExceptionHandlingInTree(
        SyntaxTree tree,
        SemanticModel semanticModel,
        string projectName,
        ExceptionHandlingOptions options,
        ExceptionHandlingResults results)
    {
        var root = tree.GetRoot();
        var filePath = string.IsNullOrEmpty(tree.FilePath) ? "Unknown" : tree.FilePath;
        var fileName = Path.GetFileName(filePath);

        ExceptionHandlingIssue CreateIssue(string issueType, string severity, SyntaxNode node, string description, string recommendation, string codeSnippet, string exceptionType = "") =>
            new()
            {
                IssueType = issueType,
                Severity = severity,
                Description = description,
                Recommendation = recommendation,
                MethodName = DisposableUsageAnalysis.GetEnclosingMemberName(node),
                FilePath = filePath,
                FileName = fileName,
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                CodeSnippet = codeSnippet,
                ExceptionType = exceptionType,
                ProjectName = projectName
            };

        // 1. Find all try-catch-finally blocks
        var tryStatements = root.DescendantNodes().OfType<TryStatementSyntax>().ToList();
        results.TotalTryBlocks += tryStatements.Count;
        results.AnalyzedTryBlocks += tryStatements.Count;

        foreach (var tryStatement in tryStatements)
        {
            // Analyze each catch clause
            foreach (var catchClause in tryStatement.Catches)
            {
                results.TotalCatchBlocks++;
                var exceptionType = GetCaughtExceptionType(catchClause, semanticModel);

                // 2. Check for empty catch blocks
                if (options.CheckEmptyCatch && IsEmptyCatchBlock(catchClause))
                {
                    results.Issues.Add(CreateIssue(
                        "EmptyCatch",
                        "High",
                        catchClause,
                        "Empty catch block found. This silently swallows exceptions and makes debugging difficult.",
                        "Add logging, rethrowing, or appropriate error handling. Consider using specific exception types.",
                        catchClause.ToString(),
                        exceptionType));
                    results.EmptyCatchCount++;
                }

                // 3. Check for swallowed exceptions (no logging, no rethrowing)
                if (options.CheckSwallowedExceptions && IsSwallowedException(catchClause))
                {
                    results.Issues.Add(CreateIssue(
                        "SwallowedException",
                        "Medium",
                        catchClause,
                        "Exception is caught but not logged or rethrown. This hides errors and makes debugging difficult.",
                        "Add logging (e.g., _logger.LogError) or rethrow the exception if it cannot be handled.",
                        catchClause.ToString(),
                        exceptionType));
                    results.SwallowedExceptionCount++;
                }

                // 4. Check for catch-all clauses: catch (Exception) or a bare catch, without an exception filter
                if (options.CheckGenericCatch && IsGenericExceptionCatch(catchClause, semanticModel))
                {
                    results.Issues.Add(CreateIssue(
                        "GenericException",
                        "Low",
                        catchClause,
                        catchClause.Declaration == null
                            ? "Bare 'catch' clause catches every exception. This can catch unexpected exceptions and hide programming errors."
                            : "Catching generic 'Exception' type. This can catch unexpected exceptions and hide programming errors.",
                        "Catch specific exception types (e.g., IOException, ArgumentException) when possible, or add an exception filter.",
                        catchClause.Declaration?.Type.ToString() ?? "catch",
                        exceptionType));
                    results.GenericExceptionCount++;
                }
            }
        }

        // 5. Check for IDisposable locals that are created but never disposed or handed off
        if (options.CheckMissingUsing)
        {
            foreach (var declaration in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                foreach (var (variable, local) in DisposableUsageAnalysis.FindUndisposedLocals(declaration, semanticModel))
                {
                    results.Issues.Add(CreateIssue(
                        "MissingUsing",
                        "Medium",
                        declaration,
                        $"Variable '{variable.Identifier.Text}' of type '{local.Type.Name}' implements IDisposable but is never disposed, returned, stored, or passed on.",
                        "Wrap in a using statement or using declaration to ensure proper resource disposal.",
                        declaration.ToString()));
                    results.MissingUsingCount++;
                }
            }
        }
    }

    /// <summary>
    /// Fills the alias and severity counters from the collected issues.
    /// </summary>
    internal static void FinalizeExceptionHandlingCounts(ExceptionHandlingResults results)
    {
        results.GenericCatchCount = results.GenericExceptionCount;

        results.HighCount = results.HighSeverityCount = results.Issues.Count(i => i.Severity == "High");
        results.MediumCount = results.MediumSeverityCount = results.Issues.Count(i => i.Severity == "Medium");
        results.LowCount = results.LowSeverityCount = results.Issues.Count(i => i.Severity == "Low");
    }

    private static string GetCaughtExceptionType(CatchClauseSyntax catchClause, SemanticModel semanticModel)
    {
        if (catchClause.Declaration == null)
            return "(all exceptions)";

        return semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type?.ToDisplayString()
            ?? catchClause.Declaration.Type.ToString();
    }

    private static bool IsEmptyCatchBlock(CatchClauseSyntax catchClause)
    {
        // A catch block is empty if it has no statements
        return !catchClause.Block.Statements.Any();
    }

    private static bool IsSwallowedException(CatchClauseSyntax catchClause)
    {
        // If it's empty, it's already flagged by IsEmptyCatchBlock
        if (!catchClause.Block.Statements.Any())
            return false;

        // Check if there's a throw statement or throw expression (rethrowing is good)
        var hasThrow = catchClause.Block.DescendantNodes().Any(n => n is ThrowStatementSyntax or ThrowExpressionSyntax);
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

    private static string? GetInvocationMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null
        };
    }

    private static bool IsGenericExceptionCatch(CatchClauseSyntax catchClause, SemanticModel semanticModel)
    {
        // An exception filter narrows a catch-all clause to what it can handle.
        if (catchClause.Filter != null)
            return false;

        // A bare catch catches everything
        if (catchClause.Declaration == null)
            return true;

        var exceptionType = semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type;
        if (exceptionType == null)
            return false;

        // Check if it's exactly System.Exception (not a derived type)
        return exceptionType.ToDisplayString() == "System.Exception";
    }

    #endregion

    #region AnalyzeDIContainer

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

            var compilations = new List<Compilation>();
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                    compilations.Add(compilation);
            }

            AnalyzeDIContainer(compilations, checkLifetimes, checkCircular, checkCaptive, results);

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

    /// <summary>
    /// Core DI analysis over already-built compilations.
    /// Registrations are keyed by service type; the implementation type of each registration
    /// (AddScoped&lt;IService, Impl&gt;) is what the captive and cycle checks follow.
    /// </summary>
    internal static void AnalyzeDIContainer(
        IReadOnlyList<Compilation> compilations,
        bool checkLifetimes,
        bool checkCircular,
        bool checkCaptive,
        DIContainerResults results)
    {
        // A file compiled into several projects (multi-targeting, linked files) is analyzed once.
        var trees = new List<(SyntaxTree Tree, SemanticModel Model)>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var compilation in compilations)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!string.IsNullOrEmpty(tree.FilePath) && !seenFiles.Add(tree.FilePath))
                    continue;
                trees.Add((tree, compilation.GetSemanticModel(tree)));
            }
        }

        // Step 1: Build service registration map (service type -> registrations in source order)
        var registrationsByService = new Dictionary<string, List<ServiceRegistration>>();

        foreach (var (tree, semanticModel) in trees)
        {
            var root = tree.GetRoot();
            var filePath = string.IsNullOrEmpty(tree.FilePath) ? "Unknown" : tree.FilePath;

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var registration = ParseDIRegistration(invocation, semanticModel, filePath);
                if (registration == null)
                    continue;

                results.AnalyzedServices++;

                if (!registrationsByService.TryGetValue(registration.ServiceType, out var previous))
                {
                    registrationsByService[registration.ServiceType] = new List<ServiceRegistration> { registration };
                    continue;
                }

                var conflicting = previous.FirstOrDefault(p => p.Lifetime != registration.Lifetime);
                if (checkLifetimes && conflicting != null)
                {
                    results.Issues.Add(new DIContainerIssue
                    {
                        IssueType = "LifetimeMismatch",
                        Severity = "Medium",
                        ServiceType = registration.ServiceType,
                        ImplementationType = registration.ImplementationType,
                        ServiceLifetime = registration.Lifetime,
                        Description = $"Service '{registration.ServiceType}' is registered as {conflicting.Lifetime} ({conflicting.FilePath}:{conflicting.LineNumber}) and again as {registration.Lifetime}. The last registration wins, so the effective lifetime depends on registration order.",
                        Recommendation = "Register the service once with a single lifetime, or use TryAdd* so the first registration is kept.",
                        FilePath = registration.FilePath,
                        LineNumber = registration.LineNumber
                    });
                    results.LifetimeMismatchCount++;
                }
                else
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

                previous.Add(registration);
            }
        }

        // The registration the container resolves for each service type (last one wins).
        var effectiveRegistrations = registrationsByService.ToDictionary(kv => kv.Key, kv => kv.Value[^1]);

        // Implementation type -> the effective registrations that construct it.
        var registrationsByImplementation = effectiveRegistrations.Values
            .GroupBy(r => r.ImplementationType)
            .ToDictionary(g => g.Key, g => g.ToList());

        // One implementation registered under several services with different lifetimes gets one
        // instance per registration, each living by a different rule.
        if (checkLifetimes)
        {
            foreach (var (implementation, registrations) in registrationsByImplementation)
            {
                var lifetimes = registrations.Select(r => r.Lifetime).Distinct().ToList();
                if (lifetimes.Count < 2)
                    continue;

                var first = registrations.OrderBy(r => r.FilePath).ThenBy(r => r.LineNumber).First();
                results.Issues.Add(new DIContainerIssue
                {
                    IssueType = "LifetimeMismatch",
                    Severity = "Medium",
                    ServiceType = string.Join(", ", registrations.Select(r => r.ServiceType)),
                    ImplementationType = implementation,
                    ServiceLifetime = string.Join("/", lifetimes),
                    Description = $"Implementation '{implementation}' is registered with different lifetimes: {string.Join(", ", registrations.Select(r => $"{r.ServiceType} as {r.Lifetime}"))}. Each registration creates its own instance, so the services do not share state as they may appear to.",
                    Recommendation = $"Register '{implementation}' once with one lifetime and forward the other services to it, e.g. services.AddSingleton<IB>(sp => (Impl)sp.GetRequiredService<IA>()).",
                    FilePath = first.FilePath,
                    LineNumber = first.LineNumber
                });
                results.LifetimeMismatchCount++;
            }
        }

        // Step 2: Find constructor injection points and validate
        var constructorDependencies = new Dictionary<string, HashSet<string>>();
        var reportedCaptives = new HashSet<(string Service, string Dependency)>();

        foreach (var (tree, semanticModel) in trees)
        {
            var root = tree.GetRoot();
            var filePath = string.IsNullOrEmpty(tree.FilePath) ? "Unknown" : tree.FilePath;

            foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                results.AnalyzedConstructors++;

                if (constructor.Parent is not TypeDeclarationSyntax containingType)
                    continue;

                if (semanticModel.GetDeclaredSymbol(containingType) is not INamedTypeSymbol classSymbol)
                    continue;

                var className = classSymbol.ToDisplayString();
                registrationsByImplementation.TryGetValue(className, out var consumerRegistrations);

                if (!constructorDependencies.TryGetValue(className, out var dependencies))
                    constructorDependencies[className] = dependencies = new HashSet<string>();

                // Analyze constructor parameters (dependencies)
                foreach (var parameter in constructor.ParameterList.Parameters)
                {
                    var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
                    if (parameterSymbol == null) continue;

                    var dependencyType = parameterSymbol.Type.ToDisplayString();
                    var lineNumber = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                    // Check if dependency is registered
                    if (!effectiveRegistrations.TryGetValue(dependencyType, out var dependencyRegistration))
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
                            FilePath = filePath,
                            LineNumber = lineNumber
                        });
                        results.UnregisteredCount++;
                        continue;
                    }

                    dependencies.Add(dependencyType);

                    if (!checkCaptive || consumerRegistrations == null)
                        continue;

                    // Check for captive dependencies, once per registration that constructs this class
                    foreach (var consumer in consumerRegistrations)
                    {
                        if (!IsCaptiveDependency(consumer.Lifetime, dependencyRegistration.Lifetime))
                            continue;

                        if (!reportedCaptives.Add((consumer.ServiceType, dependencyType)))
                            continue;

                        var consumerName = consumer.ServiceType == className ? className : $"{consumer.ServiceType} ({className})";
                        var dependencyName = dependencyRegistration.ImplementationType == dependencyType
                            ? dependencyType
                            : $"{dependencyType} ({dependencyRegistration.ImplementationType})";

                        results.Issues.Add(new DIContainerIssue
                        {
                            IssueType = "CaptiveDependency",
                            Severity = "High",
                            ServiceType = consumer.ServiceType,
                            ImplementationType = className,
                            ServiceLifetime = consumer.Lifetime,
                            Description = $"Captive dependency detected: {consumer.Lifetime} service '{consumerName}' depends on {dependencyRegistration.Lifetime} service '{dependencyName}'.",
                            Recommendation = $"Change '{consumer.ServiceType}' to {dependencyRegistration.Lifetime} or '{dependencyType}' to {consumer.Lifetime}. A longer-lived service should not depend on a shorter-lived service.",
                            FilePath = filePath,
                            LineNumber = lineNumber,
                            DependencyChain = new List<string> { consumer.ServiceType, dependencyType }
                        });
                        results.CaptiveDependencyCount++;
                    }
                }
            }
        }

        // Step 3: Detect circular dependencies
        if (checkCircular)
        {
            foreach (var cycle in DetectCircularDependencies(effectiveRegistrations, constructorDependencies))
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
    }

    private class ServiceRegistration
    {
        public string ServiceType { get; set; } = string.Empty;
        public string ImplementationType { get; set; } = string.Empty;
        public string Lifetime { get; set; } = string.Empty;  // Singleton, Scoped, Transient
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
    }

    private static ServiceRegistration? ParseDIRegistration(
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

    private static bool IsCaptiveDependency(string consumerLifetime, string dependencyLifetime)
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

    private static bool IsFrameworkType(string typeName)
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

    /// <summary>
    /// Finds cycles in the service graph. Each service points to the services its registered
    /// implementation's constructors depend on, so IA → Impl(IB) → Impl(IA) is a cycle IA → IB.
    /// Each cycle is reported once, starting from its alphabetically first service.
    /// </summary>
    private static List<List<string>> DetectCircularDependencies(
        Dictionary<string, ServiceRegistration> effectiveRegistrations,
        Dictionary<string, HashSet<string>> constructorDependencies)
    {
        var graph = new Dictionary<string, List<string>>();
        foreach (var (service, registration) in effectiveRegistrations)
        {
            graph[service] = constructorDependencies.TryGetValue(registration.ImplementationType, out var dependencies)
                ? dependencies.Where(effectiveRegistrations.ContainsKey).OrderBy(d => d, StringComparer.Ordinal).ToList()
                : new List<string>();
        }

        var cycles = new List<List<string>>();
        var seenCycles = new HashSet<string>();
        var visited = new HashSet<string>();
        var onPath = new HashSet<string>();
        var path = new List<string>();

        void Visit(string node)
        {
            visited.Add(node);
            onPath.Add(node);
            path.Add(node);

            foreach (var neighbor in graph[node])
            {
                if (onPath.Contains(neighbor))
                {
                    var cycle = path.Skip(path.IndexOf(neighbor)).ToList();

                    // Rotate so the cycle starts at its smallest member, then dedupe.
                    var start = cycle.IndexOf(cycle.Min(StringComparer.Ordinal)!);
                    cycle = cycle.Skip(start).Concat(cycle.Take(start)).ToList();
                    if (seenCycles.Add(string.Join("\u0000", cycle)))
                        cycles.Add(cycle);
                }
                else if (!visited.Contains(neighbor))
                {
                    Visit(neighbor);
                }
            }

            onPath.Remove(node);
            path.RemoveAt(path.Count - 1);
        }

        foreach (var service in graph.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!visited.Contains(service))
                Visit(service);
        }

        return cycles;
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
