using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Tests.Helpers;

/// <summary>
/// Helper class to build test solutions and projects for testing
/// </summary>
public class TestSolutionBuilder
{
    private readonly AdhocWorkspace _workspace;
    private Solution _solution;

    public TestSolutionBuilder()
    {
        _workspace = new AdhocWorkspace();
        _solution = _workspace.CurrentSolution;
    }

    public Solution Solution => _solution;

    /// <summary>
    /// Adds a project to the solution
    /// </summary>
    public TestSolutionBuilder AddProject(string projectName, params string[] projectReferences)
    {
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            projectName,
            projectName,
            LanguageNames.CSharp);

        _solution = _solution.AddProject(projectInfo);

        // Add project references
        foreach (var refProjectName in projectReferences)
        {
            var refProject = _solution.Projects.FirstOrDefault(p => p.Name == refProjectName);
            if (refProject != null)
            {
                _solution = _solution.AddProjectReference(
                    projectId,
                    new ProjectReference(refProject.Id));
            }
        }

        return this;
    }

    /// <summary>
    /// Adds a document with code to a project
    /// </summary>
    public TestSolutionBuilder AddDocument(string projectName, string documentName, string code)
    {
        var project = _solution.Projects.FirstOrDefault(p => p.Name == projectName);
        if (project == null)
        {
            throw new InvalidOperationException($"Project '{projectName}' not found");
        }

        var documentId = DocumentId.CreateNewId(project.Id);
        _solution = _solution.AddDocument(documentId, documentName, SourceText.From(code));

        return this;
    }

    /// <summary>
    /// Adds a class to a project
    /// </summary>
    public TestSolutionBuilder AddClass(string projectName, string className, string code)
    {
        return AddDocument(projectName, $"{className}.cs", code);
    }

    /// <summary>
    /// Creates a simple class with a method
    /// </summary>
    public static string CreateSimpleClass(string namespaceName, string className, string methodName = "DoSomething")
    {
        return $@"
namespace {namespaceName}
{{
    public class {className}
    {{
        public void {methodName}()
        {{
            // Simple method
        }}
    }}
}}";
    }

    /// <summary>
    /// Creates a class with high cyclomatic complexity
    /// </summary>
    public static string CreateComplexMethod(string namespaceName, string className, int decisionPoints = 10)
    {
        var ifStatements = string.Join("\n", Enumerable.Range(1, decisionPoints).Select(i =>
            $"            if (value > {i}) {{ result += {i}; }}"));

        return $@"
namespace {namespaceName}
{{
    public class {className}
    {{
        public int ComplexMethod(int value)
        {{
            int result = 0;
{ifStatements}
            return result;
        }}
    }}
}}";
    }

    /// <summary>
    /// Creates a class with nested if statements (high cognitive complexity)
    /// </summary>
    public static string CreateNestedMethod(string namespaceName, string className, int nestingDepth = 3)
    {
        string GenerateNesting(int depth, int current = 0)
        {
            var indent = new string(' ', (current + 3) * 4);
            if (current >= depth)
            {
                return $"{indent}result++;";
            }

            return $@"{indent}if (value > {current})
{indent}{{
{GenerateNesting(depth, current + 1)}
{indent}}}";
        }

        return $@"
namespace {namespaceName}
{{
    public class {className}
    {{
        public int NestedMethod(int value)
        {{
            int result = 0;
{GenerateNesting(nestingDepth)}
            return result;
        }}
    }}
}}";
    }

    /// <summary>
    /// Creates a solution with circular dependencies
    /// </summary>
    public static TestSolutionBuilder CreateCircularDependencySolution()
    {
        var builder = new TestSolutionBuilder();

        builder.AddProject("ProjectA", "ProjectB")
               .AddClass("ProjectA", "ClassA", CreateSimpleClass("ProjectA", "ClassA"));

        builder.AddProject("ProjectB", "ProjectA")
               .AddClass("ProjectB", "ClassB", CreateSimpleClass("ProjectB", "ClassB"));

        return builder;
    }

    /// <summary>
    /// Creates a solution with a dependency chain (no circular dependencies)
    /// </summary>
    public static TestSolutionBuilder CreateLinearDependencySolution()
    {
        var builder = new TestSolutionBuilder();

        builder.AddProject("ProjectA")
               .AddClass("ProjectA", "ClassA", CreateSimpleClass("ProjectA", "ClassA"));

        builder.AddProject("ProjectB", "ProjectA")
               .AddClass("ProjectB", "ClassB", CreateSimpleClass("ProjectB", "ClassB"));

        builder.AddProject("ProjectC", "ProjectB")
               .AddClass("ProjectC", "ClassC", CreateSimpleClass("ProjectC", "ClassC"));

        return builder;
    }
}
