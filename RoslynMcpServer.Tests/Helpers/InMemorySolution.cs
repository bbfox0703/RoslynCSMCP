using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Helpers;

/// <summary>
/// Builds compilable in-memory solutions (projects reference the running .NET runtime), so service
/// behavior can be tested precisely without writing .sln files or loading MSBuild
/// </summary>
public sealed class InMemorySolution
{
    // Managed assemblies of the shared framework the tests run on
    private static readonly Lazy<IReadOnlyList<MetadataReference>> RuntimeReferences = new(() =>
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    });

    private readonly AdhocWorkspace _workspace = new();
    private readonly Dictionary<string, ProjectId> _projectIds = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoslynMcpInMemory", Guid.NewGuid().ToString("N"));
    private Solution _solution;

    public InMemorySolution()
    {
        _solution = _workspace.CurrentSolution;
    }

    public Solution Solution => _solution;

    public InMemorySolution AddProject(string projectName, params string[] projectReferences)
    {
        var projectId = ProjectId.CreateNewId(projectName);
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            projectName,
            projectName,
            LanguageNames.CSharp,
            filePath: Path.Combine(_root, projectName, $"{projectName}.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: RuntimeReferences.Value,
            projectReferences: projectReferences.Select(name => new ProjectReference(_projectIds[name])));

        _solution = _solution.AddProject(projectInfo);
        _projectIds[projectName] = projectId;
        return this;
    }

    public InMemorySolution AddDocument(string projectName, string fileName, string code)
    {
        var documentId = DocumentId.CreateNewId(_projectIds[projectName]);
        _solution = _solution.AddDocument(
            documentId,
            fileName,
            SourceText.From(code),
            filePath: Path.Combine(_root, projectName, fileName));
        return this;
    }

    /// <summary>
    /// Fails the test when any project has compile errors, so tests never pass on broken fixtures
    /// </summary>
    public async Task<InMemorySolution> AssertCompilesAsync()
    {
        foreach (var project in _solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            var errors = compilation!.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            errors.Should().BeEmpty($"fixture project '{project.Name}' must compile");
        }

        return this;
    }

    /// <summary>
    /// Creates a CodeAnalysisService that serves this solution for the given path
    /// </summary>
    public InMemoryCodeAnalysisService CreateCodeAnalysisService(string solutionPath) =>
        new(solutionPath, _solution);
}

/// <summary>
/// CodeAnalysisService that returns prebuilt solutions instead of loading them with MSBuild
/// </summary>
public sealed class InMemoryCodeAnalysisService : CodeAnalysisService
{
    private readonly Dictionary<string, Solution> _solutions = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryCodeAnalysisService()
        : base(NullLogger<CodeAnalysisService>.Instance)
    {
    }

    public InMemoryCodeAnalysisService(string solutionPath, Solution solution)
        : this()
    {
        Register(solutionPath, solution);
    }

    public InMemoryCodeAnalysisService Register(string solutionPath, Solution solution)
    {
        _solutions[solutionPath] = solution;
        return this;
    }

    public override Task<Solution> GetSolutionAsync(string solutionPath)
    {
        if (!_solutions.TryGetValue(solutionPath, out var solution))
            throw new FileNotFoundException($"No in-memory solution registered for '{solutionPath}'");

        return Task.FromResult(solution);
    }
}

/// <summary>
/// An empty, existing .sln file, for tools that validate the solution path before calling services
/// </summary>
public sealed class PlaceholderSolutionFile : IDisposable
{
    private readonly string _directory;

    public PlaceholderSolutionFile()
    {
        _directory = Path.Combine(Path.GetTempPath(), "RoslynMcpInMemory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        FilePath = Path.Combine(_directory, "InMemory.sln");
        File.WriteAllText(FilePath, string.Empty);
    }

    public string FilePath { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
