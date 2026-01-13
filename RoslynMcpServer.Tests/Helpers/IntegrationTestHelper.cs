using System.Text;

namespace RoslynMcpServer.Tests.Helpers;

/// <summary>
/// Helper class to create real solution files for integration testing
/// </summary>
public class IntegrationTestHelper : IDisposable
{
    private readonly string _testRootPath;
    private readonly List<string> _createdDirectories = new();
    private readonly List<string> _createdFiles = new();

    public IntegrationTestHelper(string testName)
    {
        // Create a unique test directory in the temp folder
        _testRootPath = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootPath);
        _createdDirectories.Add(_testRootPath);
    }

    public string TestRootPath => _testRootPath;

    /// <summary>
    /// Creates a solution file with projects
    /// </summary>
    public string CreateSolution(string solutionName, params ProjectDefinition[] projects)
    {
        var solutionPath = Path.Combine(_testRootPath, $"{solutionName}.sln");

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");
        sb.AppendLine("VisualStudioVersion = 17.0.31903.59");
        sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

        var projectGuids = new Dictionary<string, string>();

        // Add projects
        foreach (var project in projects)
        {
            var projectGuid = Guid.NewGuid().ToString("B").ToUpper();
            projectGuids[project.Name] = projectGuid;
            var projectPath = $"{project.Name}\\{project.Name}.csproj";

            sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{project.Name}\", \"{projectPath}\", \"{projectGuid}\"");
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
        sb.AppendLine("\tEndGlobalSection");

        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var project in projects)
        {
            var guid = projectGuids[project.Name];
            sb.AppendLine($"\t\t{guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            sb.AppendLine($"\t\t{guid}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            sb.AppendLine($"\t\t{guid}.Release|Any CPU.ActiveCfg = Release|Any CPU");
            sb.AppendLine($"\t\t{guid}.Release|Any CPU.Build.0 = Release|Any CPU");
        }
        sb.AppendLine("\tEndGlobalSection");

        sb.AppendLine("EndGlobal");

        File.WriteAllText(solutionPath, sb.ToString());
        _createdFiles.Add(solutionPath);

        // Create projects
        foreach (var project in projects)
        {
            CreateProject(project);
        }

        // Try to restore the solution to make it loadable by MSBuildWorkspace
        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"restore \"{solutionPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _testRootPath
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            process?.WaitForExit(30000); // 30 second timeout
        }
        catch
        {
            // Best effort - if restore fails, tests may still work
        }

        return solutionPath;
    }

    /// <summary>
    /// Creates a project with source files
    /// </summary>
    private void CreateProject(ProjectDefinition project)
    {
        var projectDir = Path.Combine(_testRootPath, project.Name);
        Directory.CreateDirectory(projectDir);
        _createdDirectories.Add(projectDir);

        // Create .csproj file
        var projectPath = Path.Combine(projectDir, $"{project.Name}.csproj");
        var csprojContent = GenerateProjectFile(project);
        File.WriteAllText(projectPath, csprojContent);
        _createdFiles.Add(projectPath);

        // Create source files
        foreach (var sourceFile in project.SourceFiles)
        {
            var sourceFilePath = Path.Combine(projectDir, sourceFile.FileName);
            File.WriteAllText(sourceFilePath, sourceFile.Content);
            _createdFiles.Add(sourceFilePath);
        }
    }

    /// <summary>
    /// Generates a .csproj file content
    /// </summary>
    private string GenerateProjectFile(ProjectDefinition project)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("  </PropertyGroup>");

        // Add project references
        if (project.ProjectReferences.Any())
        {
            sb.AppendLine();
            sb.AppendLine("  <ItemGroup>");
            foreach (var reference in project.ProjectReferences)
            {
                sb.AppendLine($"    <ProjectReference Include=\"..\\{reference}\\{reference}.csproj\" />");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine();
        sb.AppendLine("</Project>");

        return sb.ToString();
    }

    /// <summary>
    /// Cleans up all created files and directories
    /// </summary>
    public void Dispose()
    {
        try
        {
            // Delete files first
            foreach (var file in _createdFiles.Where(File.Exists))
            {
                File.Delete(file);
            }

            // Delete directories in reverse order (deepest first)
            foreach (var dir in _createdDirectories.OrderByDescending(d => d.Length).Where(Directory.Exists))
            {
                Directory.Delete(dir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}

/// <summary>
/// Defines a project to be created in a test solution
/// </summary>
public class ProjectDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<SourceFile> SourceFiles { get; set; } = new();
    public List<string> ProjectReferences { get; set; } = new();

    public ProjectDefinition(string name)
    {
        Name = name;
    }

    public ProjectDefinition AddSourceFile(string fileName, string content)
    {
        SourceFiles.Add(new SourceFile { FileName = fileName, Content = content });
        return this;
    }

    public ProjectDefinition AddReference(string projectName)
    {
        ProjectReferences.Add(projectName);
        return this;
    }
}

/// <summary>
/// Represents a source code file
/// </summary>
public class SourceFile
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Provides common code templates for test files
/// </summary>
public static class CodeTemplates
{
    public static string SimpleClass(string namespaceName, string className, string? baseClass = null, params string[] interfaces)
    {
        var inheritance = new List<string>();
        if (!string.IsNullOrEmpty(baseClass))
            inheritance.Add(baseClass);
        inheritance.AddRange(interfaces);

        var inheritanceClause = inheritance.Any() ? $" : {string.Join(", ", inheritance)}" : "";

        return $@"namespace {namespaceName};

public class {className}{inheritanceClause}
{{
    public void DoSomething()
    {{
        // Implementation
    }}

    public string GetValue()
    {{
        return ""value"";
    }}
}}
";
    }

    public static string Interface(string namespaceName, string interfaceName)
    {
        return $@"namespace {namespaceName};

public interface {interfaceName}
{{
    void DoSomething();
    string GetValue();
}}
";
    }

    public static string ClassWithMethod(string namespaceName, string className, string methodName, string returnType = "void")
    {
        var returnStatement = returnType != "void" ? $"\n        return default({returnType});" : "";

        return $@"namespace {namespaceName};

public class {className}
{{
    public {returnType} {methodName}()
    {{{returnStatement}
    }}
}}
";
    }

    public static string ComplexMethod(string namespaceName, string className, int cyclomaticComplexity = 10)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");
        sb.AppendLine("    public int ComplexMethod(int value)");
        sb.AppendLine("    {");
        sb.AppendLine("        int result = 0;");

        // Add if statements to increase complexity
        for (int i = 1; i <= cyclomaticComplexity; i++)
        {
            sb.AppendLine($"        if (value > {i}) {{ result += {i}; }}");
        }

        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    public static string ClassWithReferences(string namespaceName, string className, params string[] referencedClasses)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");

        foreach (var refClass in referencedClasses)
        {
            sb.AppendLine($"    private {refClass}? _{refClass.ToLower()};");
            sb.AppendLine();
            sb.AppendLine($"    public void Use{refClass}()");
            sb.AppendLine("    {");
            sb.AppendLine($"        _{refClass.ToLower()} = new {refClass}();");
            sb.AppendLine($"        _{refClass.ToLower()}.DoSomething();");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    public static string AttributeClass(string namespaceName, string attributeName)
    {
        return $@"namespace {namespaceName};

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public class {attributeName}Attribute : Attribute
{{
    public string? Message {{ get; set; }}

    public {attributeName}Attribute() {{ }}

    public {attributeName}Attribute(string message)
    {{
        Message = message;
    }}
}}
";
    }

    public static string ClassWithAttribute(string namespaceName, string className, string attributeName, string? attributeArg = null)
    {
        var attrUsage = string.IsNullOrEmpty(attributeArg)
            ? $"[{attributeName}]"
            : $"[{attributeName}(\"{attributeArg}\")]";

        return $@"namespace {namespaceName};

{attrUsage}
public class {className}
{{
    {attrUsage}
    public void AnnotatedMethod()
    {{
        // Implementation
    }}
}}
";
    }
}
