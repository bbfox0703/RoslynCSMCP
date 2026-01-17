using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Core.Services
{
    public class TestDiscoveryService
    {
        private readonly ILogger<TestDiscoveryService> _logger;
        private readonly CodeAnalysisService _codeAnalysis;
        private readonly SecurityValidator _securityValidator;

        // Test framework detection patterns
        private readonly HashSet<string> _xunitAttributes = new() { "Fact", "Theory", "FactAttribute", "TheoryAttribute" };
        private readonly HashSet<string> _nunitAttributes = new() { "Test", "TestCase", "TestCaseSource", "TestAttribute", "TestCaseAttribute" };
        private readonly HashSet<string> _mstestAttributes = new() { "TestMethod", "DataTestMethod", "TestMethodAttribute", "DataTestMethodAttribute" };

        public TestDiscoveryService(
            ILogger<TestDiscoveryService> logger,
            CodeAnalysisService codeAnalysis,
            SecurityValidator securityValidator)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
            _securityValidator = securityValidator;
        }

        /// <summary>
        /// Find all test classes and methods for a given type
        /// </summary>
        public async Task<List<TestClassResult>> FindTestsForTypeAsync(
            string typeName,
            string solutionPath,
            bool includePartialMatches = true)
        {
            _logger.LogInformation("Finding tests for type: {TypeName}", typeName);

            if (!_securityValidator.ValidateSolutionPath(solutionPath))
            {
                _logger.LogWarning("Invalid solution path: {SolutionPath}", solutionPath);
                throw new ArgumentException("Invalid solution path", nameof(solutionPath));
            }

            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var results = new List<TestClassResult>();

            // Pattern matching for test class names
            // Examples: UserServiceTests, UserServiceTest, TestUserService, UserService_Tests
            var testClassPatterns = new List<Regex>();

            // Exact matches: TypeNameTests, TypeNameTest
            testClassPatterns.Add(new Regex($"^{Regex.Escape(typeName)}Tests?$", RegexOptions.IgnoreCase));

            // Partial matches if enabled
            if (includePartialMatches)
            {
                // Contains the type name: SomethingUserServiceTests
                testClassPatterns.Add(new Regex($"{Regex.Escape(typeName)}.*Tests?", RegexOptions.IgnoreCase));
                // Starts with Test: TestUserService
                testClassPatterns.Add(new Regex($"^Test{Regex.Escape(typeName)}", RegexOptions.IgnoreCase));
                // With underscores: UserService_Tests, UserService_Should
                testClassPatterns.Add(new Regex($"^{Regex.Escape(typeName)}_", RegexOptions.IgnoreCase));
            }

            // Search test projects
            var testProjects = solution.Projects.Where(p => IsTestProject(p.Name));

            foreach (var project in testProjects)
            {
                if (!project.SupportsCompilation)
                    continue;

                _logger.LogDebug("Searching test project: {ProjectName}", project.Name);

                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    continue;

                // Detect test framework
                var testFramework = DetectTestFramework(compilation);

                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var root = await syntaxTree.GetRootAsync();
                    var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

                    foreach (var classDecl in classDeclarations)
                    {
                        var className = classDecl.Identifier.Text;

                        // Check if class name matches test patterns
                        bool isMatch = testClassPatterns.Any(pattern => pattern.IsMatch(className));

                        if (isMatch)
                        {
                            var testClass = await CreateTestClassResultAsync(
                                classDecl,
                                syntaxTree,
                                project,
                                typeName,
                                testFramework,
                                compilation);

                            if (testClass != null && testClass.TestMethods.Any())
                            {
                                results.Add(testClass);
                            }
                        }
                    }
                }
            }

            _logger.LogInformation("Found {Count} test classes for {TypeName}", results.Count, typeName);
            return results.OrderBy(r => r.TestClassName).ToList();
        }

        /// <summary>
        /// Check if project is a test project based on naming conventions
        /// </summary>
        private bool IsTestProject(string projectName)
        {
            var testPatterns = new[]
            {
                "Test", "Tests", "Testing",
                ".Test.", ".Tests.", ".Testing.",
                "Spec", "Specs",
                "UnitTest", "IntegrationTest",
                "FunctionalTest", "E2ETest"
            };

            return testPatterns.Any(pattern =>
                projectName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Detect which test framework is being used
        /// </summary>
        private string DetectTestFramework(Compilation compilation)
        {
            var references = compilation.ReferencedAssemblyNames;

            if (references.Any(r => r.Name.Contains("xunit", StringComparison.OrdinalIgnoreCase)))
                return "xUnit";

            if (references.Any(r => r.Name.Contains("nunit", StringComparison.OrdinalIgnoreCase)))
                return "NUnit";

            if (references.Any(r => r.Name.Contains("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase) ||
                                   r.Name.Contains("MSTest", StringComparison.OrdinalIgnoreCase)))
                return "MSTest";

            return "Unknown";
        }

        /// <summary>
        /// Create TestClassResult from class declaration
        /// </summary>
        private async Task<TestClassResult?> CreateTestClassResultAsync(
            ClassDeclarationSyntax classDecl,
            SyntaxTree syntaxTree,
            Project project,
            string testedTypeName,
            string testFramework,
            Compilation compilation)
        {
            try
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);

                if (classSymbol == null)
                    return null;

                var lineSpan = classDecl.GetLocation().GetLineSpan();
                var filePath = syntaxTree.FilePath;

                // Get test methods
                var testMethods = new List<TestMethodResult>();
                var methodDeclarations = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>();

                foreach (var methodDecl in methodDeclarations)
                {
                    var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
                    if (methodSymbol == null)
                        continue;

                    var testMethod = CreateTestMethodResult(methodDecl, methodSymbol, testFramework);
                    if (testMethod != null)
                    {
                        testMethods.Add(testMethod);
                    }
                }

                // Only return if class has test methods
                if (!testMethods.Any())
                    return null;

                // Get documentation
                var documentation = classSymbol.GetDocumentationCommentXml() ?? "";
                var summaryMatch = Regex.Match(documentation, @"<summary>\s*(.*?)\s*</summary>", RegexOptions.Singleline);
                var summary = summaryMatch.Success
                    ? summaryMatch.Groups[1].Value.Replace("///", "").Trim()
                    : "";

                return new TestClassResult
                {
                    TestClassName = classSymbol.Name,
                    TestClassFullName = classSymbol.ToDisplayString(),
                    TestedTypeName = testedTypeName,
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    ProjectName = project.Name,
                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                    TestFramework = testFramework,
                    TestMethods = testMethods.OrderBy(m => m.LineNumber).ToList(),
                    Documentation = summary
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test class result for {ClassName}", classDecl.Identifier.Text);
                return null;
            }
        }

        /// <summary>
        /// Create TestMethodResult from method declaration
        /// </summary>
        private TestMethodResult? CreateTestMethodResult(
            MethodDeclarationSyntax methodDecl,
            IMethodSymbol methodSymbol,
            string testFramework)
        {
            // Check if method has test attributes
            var attributes = methodSymbol.GetAttributes();
            var testAttributes = new List<string>();

            foreach (var attr in attributes)
            {
                var attrName = attr.AttributeClass?.Name ?? "";

                // Check for test framework attributes
                if (_xunitAttributes.Contains(attrName) ||
                    _nunitAttributes.Contains(attrName) ||
                    _mstestAttributes.Contains(attrName))
                {
                    testAttributes.Add(attrName.Replace("Attribute", ""));
                }
            }

            // Not a test method if no test attributes found
            if (!testAttributes.Any())
                return null;

            var lineSpan = methodDecl.GetLocation().GetLineSpan();

            // Get display name if specified
            string displayName = "";
            foreach (var attr in attributes)
            {
                // xUnit: [Fact(DisplayName = "...")]
                // NUnit: [Test(Description = "...")]
                // MSTest: [TestMethod, DisplayName("...")]
                if (attr.ConstructorArguments.Any())
                {
                    var firstArg = attr.ConstructorArguments.First();
                    if (firstArg.Kind == TypedConstantKind.Primitive && firstArg.Value is string strValue)
                    {
                        displayName = strValue;
                        break;
                    }
                }

                // Check named arguments
                foreach (var namedArg in attr.NamedArguments)
                {
                    if ((namedArg.Key == "DisplayName" || namedArg.Key == "Description") &&
                        namedArg.Value.Value is string namedValue)
                    {
                        displayName = namedValue;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(displayName))
                    break;
            }

            return new TestMethodResult
            {
                MethodName = methodSymbol.Name,
                LineNumber = lineSpan.StartLinePosition.Line + 1,
                TestAttributes = testAttributes,
                DisplayName = displayName
            };
        }
    }
}
