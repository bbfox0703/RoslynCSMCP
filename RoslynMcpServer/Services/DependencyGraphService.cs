using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Text;

namespace RoslynMcpServer.Services
{
    public class DependencyGraphService
    {
        private readonly CodeAnalysisService _codeAnalysisService;
        private readonly ILogger<DependencyGraphService> _logger;

        public DependencyGraphService(
            CodeAnalysisService codeAnalysisService,
            ILogger<DependencyGraphService> logger)
        {
            _codeAnalysisService = codeAnalysisService;
            _logger = logger;
        }

        public async Task<string> GetDependencyGraphAsync(
            string solutionPath,
            string format = "text",
            bool includePackages = false)
        {
            try
            {
                var solution = await _codeAnalysisService.GetSolutionAsync(solutionPath);
                var dependencies = await AnalyzeDependencies(solution, includePackages);

                return format.ToLower() switch
                {
                    "dot" => FormatAsDot(dependencies, Path.GetFileNameWithoutExtension(solutionPath)),
                    "mermaid" => FormatAsMermaid(dependencies),
                    _ => FormatAsText(dependencies, includePackages)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dependency graph");
                throw;
            }
        }

        private async Task<Dictionary<string, List<string>>> AnalyzeDependencies(
            Solution solution,
            bool includePackages)
        {
            var dependencies = new Dictionary<string, List<string>>();

            foreach (var project in solution.Projects)
            {
                var projectDeps = new List<string>();

                // Project references
                foreach (var projectRef in project.ProjectReferences)
                {
                    var referencedProject = solution.GetProject(projectRef.ProjectId);
                    if (referencedProject != null)
                    {
                        projectDeps.Add(referencedProject.Name);
                    }
                }

                // Package references (if requested)
                if (includePackages)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation != null)
                    {
                        foreach (var reference in compilation.References)
                        {
                            var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                            if (assemblySymbol != null)
                            {
                                var name = assemblySymbol.Identity.Name;
                                // Filter out system and Windows assemblies
                                if (!string.IsNullOrEmpty(name) &&
                                    !name.StartsWith("System.") &&
                                    !name.StartsWith("System") &&
                                    !name.StartsWith("Microsoft.") &&
                                    !name.StartsWith("netstandard") &&
                                    !name.StartsWith("mscorlib") &&
                                    !name.StartsWith("Windows."))
                                {
                                    projectDeps.Add($"[{name}]");
                                }
                            }
                        }
                    }
                }

                dependencies[project.Name] = projectDeps;
            }

            return dependencies;
        }

        private string FormatAsText(Dictionary<string, List<string>> dependencies, bool includePackages)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Dependency Graph\n");

            var projectsWithDeps = dependencies.Where(kvp => kvp.Value.Any()).OrderBy(kvp => kvp.Key);
            var projectsWithoutDeps = dependencies.Where(kvp => !kvp.Value.Any()).OrderBy(kvp => kvp.Key);

            foreach (var kvp in projectsWithDeps)
            {
                builder.AppendLine($"📁 {kvp.Key}");

                var projectDeps = kvp.Value.Where(d => !d.StartsWith("[")).ToList();
                var packageDeps = kvp.Value.Where(d => d.StartsWith("[")).ToList();

                if (projectDeps.Any())
                {
                    foreach (var dep in projectDeps)
                    {
                        builder.AppendLine($"  ├─> {dep}");
                    }
                }

                if (includePackages && packageDeps.Any())
                {
                    builder.AppendLine($"  📦 Packages:");
                    foreach (var dep in packageDeps)
                    {
                        var pkgName = dep.Trim('[', ']');
                        builder.AppendLine($"    └─> {pkgName}");
                    }
                }

                builder.AppendLine();
            }

            if (projectsWithoutDeps.Any())
            {
                foreach (var kvp in projectsWithoutDeps)
                {
                    builder.AppendLine($"📁 {kvp.Key}");
                    builder.AppendLine("  (no dependencies)");
                    builder.AppendLine();
                }
            }

            // Summary
            var totalProjects = dependencies.Count;
            var projectsWithDepsCount = projectsWithDeps.Count();
            var totalDependencies = dependencies.Sum(kvp => kvp.Value.Count(d => !d.StartsWith("[")));

            builder.AppendLine("Summary:");
            builder.AppendLine($"  Total Projects: {totalProjects}");
            builder.AppendLine($"  Projects with Dependencies: {projectsWithDepsCount}");
            builder.AppendLine($"  Total Project Dependencies: {totalDependencies}");

            return builder.ToString();
        }

        private string FormatAsDot(Dictionary<string, List<string>> dependencies, string graphName)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"digraph \"{graphName}\" {{");
            builder.AppendLine("  rankdir=LR;");
            builder.AppendLine("  node [shape=box, style=filled, fillcolor=lightblue];");
            builder.AppendLine();

            // Define nodes
            foreach (var project in dependencies.Keys.OrderBy(k => k))
            {
                var safeNodeName = MakeSafeDotName(project);
                builder.AppendLine($"  \"{safeNodeName}\" [label=\"{project}\"];");
            }

            builder.AppendLine();

            // Define edges (only project dependencies, not packages)
            foreach (var kvp in dependencies.OrderBy(k => k.Key))
            {
                var fromNode = MakeSafeDotName(kvp.Key);
                foreach (var dep in kvp.Value.Where(d => !d.StartsWith("[")).OrderBy(d => d))
                {
                    var toNode = MakeSafeDotName(dep);
                    builder.AppendLine($"  \"{fromNode}\" -> \"{toNode}\";");
                }
            }

            builder.AppendLine("}");
            return builder.ToString();
        }

        private string FormatAsMermaid(Dictionary<string, List<string>> dependencies)
        {
            var builder = new StringBuilder();
            builder.AppendLine("```mermaid");
            builder.AppendLine("graph LR");

            // Define nodes with safer IDs
            var nodeIds = new Dictionary<string, string>();
            int nodeCounter = 1;
            foreach (var project in dependencies.Keys.OrderBy(k => k))
            {
                var nodeId = $"N{nodeCounter}";
                nodeIds[project] = nodeId;
                builder.AppendLine($"    {nodeId}[\"{project}\"]");
                nodeCounter++;
            }

            builder.AppendLine();

            // Define edges (only project dependencies, not packages)
            foreach (var kvp in dependencies.OrderBy(k => k.Key))
            {
                var fromId = nodeIds[kvp.Key];
                foreach (var dep in kvp.Value.Where(d => !d.StartsWith("[") && nodeIds.ContainsKey(d)).OrderBy(d => d))
                {
                    var toId = nodeIds[dep];
                    builder.AppendLine($"    {fromId} --> {toId}");
                }
            }

            // Styling
            builder.AppendLine();
            builder.AppendLine("    classDef default fill:#4A90E2,stroke:#2E5C8A,stroke-width:2px,color:#fff;");

            builder.AppendLine("```");
            return builder.ToString();
        }

        private string MakeSafeDotName(string name)
        {
            // Replace characters that might cause issues in DOT format
            return name.Replace(".", "_").Replace("-", "_");
        }
    }
}
