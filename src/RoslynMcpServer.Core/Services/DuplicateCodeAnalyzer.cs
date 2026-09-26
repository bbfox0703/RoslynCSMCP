using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for detecting duplicate code blocks across the solution.
    ///
    /// Each method body is reduced to a token sequence: comments and whitespace disappear, names
    /// declared inside the method (parameters, locals, loop and catch variables, lambda parameters,
    /// local functions, type parameters) become a placeholder, and literals become a placeholder per
    /// kind. The method name and signature are not part of the sequence, so renamed copies match.
    /// Similarity between two methods is 2 × LCS / (|a| + |b|) over those token sequences.
    /// </summary>
    public class DuplicateCodeAnalyzer
    {
        private const string DeclaredNameToken = "$id";
        private const int ShingleLength = 5;
        private const int MaxShinglePostings = 64;
        private const long MaxLcsCells = 10_000_000;

        private readonly ILogger<DuplicateCodeAnalyzer> _logger;

        public DuplicateCodeAnalyzer(ILogger<DuplicateCodeAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes solution for duplicate code blocks
        /// </summary>
        public async Task<DuplicateCodeResults> AnalyzeDuplicateCodeAsync(
            string solutionPath,
            int minLines = 5,
            int similarityThreshold = 90)
        {
            var results = new DuplicateCodeResults();

            try
            {
                if (!File.Exists(solutionPath))
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "Invalid solution path"
                    });
                    return results;
                }

                // Validate parameters
                if (minLines < 3)
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "minLines must be at least 3, using default value 5"
                    });
                    minLines = 5;
                }

                if (similarityThreshold < 70 || similarityThreshold > 100)
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "similarity must be between 70-100, using default value 90"
                    });
                    similarityThreshold = 90;
                }

                // Load solution
                using var workspace = MSBuildWorkspace.Create();
                workspace.RegisterWorkspaceFailedHandler((e) =>
                {
                    if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    {
                        _logger.LogWarning("Workspace loading warning: {Message}", e.Diagnostic.Message);
                    }
                });

                var solution = await workspace.OpenSolutionAsync(solutionPath);
                results.AnalyzedProjects = solution.Projects.Count();

                // Collect all methods with a body
                var allMethods = new ConcurrentBag<(MethodDeclarationSyntax Method, string ProjectName)>();
                int failedProjects = 0;

                var projectTasks = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .Select(async project =>
                    {
                        try
                        {
                            var compilation = await project.GetCompilationAsync();
                            if (compilation == null) return;

                            foreach (var syntaxTree in compilation.SyntaxTrees)
                            {
                                var root = await syntaxTree.GetRootAsync();

                                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                                {
                                    if (method.Body != null || method.ExpressionBody != null)
                                        allMethods.Add((method, project.Name));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                            Interlocked.Increment(ref failedProjects);
                        }
                    });

                await Task.WhenAll(projectTasks);

                results.FailedProjects = failedProjects;

                // A file compiled into several projects (multi-targeting, linked files) must not
                // report each of its methods as a duplicate of itself: keep one copy per location.
                var distinctMethods = allMethods
                    .GroupBy(m => (m.Method.SyntaxTree.FilePath, m.Method.SpanStart))
                    .Select(g => g.OrderBy(m => m.ProjectName, StringComparer.Ordinal).First())
                    .ToList();

                results.AnalyzedFiles = distinctMethods.Select(m => m.Method.SyntaxTree.FilePath).Distinct().Count();
                results.AnalyzedMethods = distinctMethods.Count;

                var blocks = distinctMethods
                    .Select(m => CreateCodeBlock(m.Method, m.ProjectName))
                    .Where(b => b.LineCount >= minLines && b.Tokens.Length > 0)
                    .ToList();

                results.DuplicateBlocks = FindDuplicateBlocks(blocks, similarityThreshold);

                CalculateStatistics(results);

                _logger.LogInformation(
                    "Duplicate code analysis complete: {BlockCount} duplicate blocks found ({InstanceCount} instances)",
                    results.TotalDuplicateBlocks,
                    results.TotalDuplicateInstances);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing duplicate code");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Builds the comparison unit for a method: location, line span of the whole declaration,
        /// and the normalized token sequence of its body.
        /// </summary>
        internal static CodeBlockInfo CreateCodeBlock(MethodDeclarationSyntax method, string projectName)
        {
            var filePath = method.SyntaxTree.FilePath;
            var lineSpan = method.GetLocation().GetLineSpan();
            var tokens = NormalizeBody(method);

            return new CodeBlockInfo
            {
                MethodName = method.Identifier.Text,
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                LineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1,
                ProjectName = projectName,
                Tokens = tokens,
                Hash = ComputeHash(string.Join(" ", tokens)),
                OriginalNode = method
            };
        }

        /// <summary>
        /// Token sequence of the method body with comments, whitespace, declared names, and literal
        /// values normalized away. Names that are not declared in the method (types, members,
        /// called methods) are kept, so two methods only match if they do the same things.
        /// </summary>
        internal static string[] NormalizeBody(MethodDeclarationSyntax method)
        {
            SyntaxNode? body = (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression;
            if (body == null)
                return Array.Empty<string>();

            var declaredNames = CollectDeclaredNames(method);
            var tokens = new List<string>();

            foreach (var token in body.DescendantTokens())
            {
                if (token.IsMissing || token.Span.IsEmpty)
                    continue;

                switch (token.Kind())
                {
                    case SyntaxKind.IdentifierToken:
                        tokens.Add(declaredNames.Contains(token.ValueText) ? DeclaredNameToken : token.ValueText);
                        break;

                    case SyntaxKind.NumericLiteralToken:
                        tokens.Add("$num");
                        break;

                    case SyntaxKind.CharacterLiteralToken:
                        tokens.Add("$char");
                        break;

                    case SyntaxKind.StringLiteralToken:
                    case SyntaxKind.Utf8StringLiteralToken:
                    case SyntaxKind.SingleLineRawStringLiteralToken:
                    case SyntaxKind.MultiLineRawStringLiteralToken:
                    case SyntaxKind.Utf8SingleLineRawStringLiteralToken:
                    case SyntaxKind.Utf8MultiLineRawStringLiteralToken:
                    case SyntaxKind.InterpolatedStringTextToken:
                        tokens.Add("$str");
                        break;

                    default:
                        tokens.Add(token.Text);
                        break;
                }
            }

            return tokens.ToArray();
        }

        private static HashSet<string> CollectDeclaredNames(MethodDeclarationSyntax method)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in method.DescendantNodes())
            {
                var identifier = node switch
                {
                    ParameterSyntax parameter => parameter.Identifier,
                    TypeParameterSyntax typeParameter => typeParameter.Identifier,
                    VariableDeclaratorSyntax variable => variable.Identifier,
                    ForEachStatementSyntax forEach => forEach.Identifier,
                    CatchDeclarationSyntax catchDeclaration => catchDeclaration.Identifier,
                    SingleVariableDesignationSyntax designation => designation.Identifier,
                    LocalFunctionStatementSyntax localFunction => localFunction.Identifier,
                    FromClauseSyntax from => from.Identifier,
                    LetClauseSyntax let => let.Identifier,
                    JoinClauseSyntax join => join.Identifier,
                    JoinIntoClauseSyntax joinInto => joinInto.Identifier,
                    QueryContinuationSyntax continuation => continuation.Identifier,
                    _ => default
                };

                if (identifier.IsKind(SyntaxKind.IdentifierToken) && identifier.ValueText.Length > 0)
                    names.Add(identifier.ValueText);
            }

            return names;
        }

        /// <summary>
        /// Groups blocks into duplicate sets.
        /// Blocks with identical normalized bodies form one unit (100% similar). When the threshold
        /// is below 100, units are clustered around seeds, largest first: each seed collects every
        /// unassigned unit whose similarity to it reaches the threshold, and the group reports the
        /// lowest of those similarities. Candidates are found through shared 5-token shingles.
        /// </summary>
        internal static List<DuplicateCodeBlock> FindDuplicateBlocks(IReadOnlyList<CodeBlockInfo> blocks, int similarityThreshold)
        {
            // Exact units, largest first, deterministic order
            var units = blocks
                .GroupBy(b => b.Hash)
                .Select(g => g
                    .OrderBy(b => b.FilePath, StringComparer.Ordinal)
                    .ThenBy(b => b.StartLine)
                    .ToList())
                .OrderByDescending(g => g[0].Tokens.Length)
                .ThenBy(g => g[0].FilePath, StringComparer.Ordinal)
                .ThenBy(g => g[0].StartLine)
                .ToList();

            var clusters = new List<(List<List<CodeBlockInfo>> Units, int Similarity)>();

            if (similarityThreshold >= 100)
            {
                clusters.AddRange(units.Select(u => (new List<List<CodeBlockInfo>> { u }, 100)));
            }
            else
            {
                var shingles = units.Select(u => GetShingles(u[0].Tokens)).ToList();
                var postings = new Dictionary<int, List<int>>();
                for (int i = 0; i < units.Count; i++)
                {
                    foreach (var shingle in shingles[i])
                    {
                        if (!postings.TryGetValue(shingle, out var list))
                            postings[shingle] = list = new List<int>();
                        list.Add(i);
                    }
                }

                var assigned = new bool[units.Count];
                for (int seed = 0; seed < units.Count; seed++)
                {
                    if (assigned[seed])
                        continue;
                    assigned[seed] = true;

                    // Shingles shared by very many methods (boilerplate) do not narrow anything down.
                    var candidates = new SortedSet<int>();
                    foreach (var shingle in shingles[seed])
                    {
                        var list = postings[shingle];
                        if (list.Count > MaxShinglePostings)
                            continue;
                        foreach (var other in list)
                        {
                            if (!assigned[other])
                                candidates.Add(other);
                        }
                    }

                    var members = new List<List<CodeBlockInfo>> { units[seed] };
                    var groupSimilarity = 100;
                    foreach (var candidate in candidates)
                    {
                        var similarity = ComputeSimilarity(units[seed][0].Tokens, units[candidate][0].Tokens, similarityThreshold);
                        if (similarity < similarityThreshold)
                            continue;

                        members.Add(units[candidate]);
                        assigned[candidate] = true;
                        groupSimilarity = Math.Min(groupSimilarity, similarity);
                    }

                    clusters.Add((members, groupSimilarity));
                }
            }

            var duplicateBlocks = new List<DuplicateCodeBlock>();
            foreach (var (clusterUnits, similarity) in clusters)
            {
                var clusterBlocks = clusterUnits.SelectMany(u => u).ToList();
                if (clusterBlocks.Count < 2)
                    continue;

                duplicateBlocks.Add(new DuplicateCodeBlock
                {
                    Instances = clusterBlocks.Select(b => new CodeBlockInstance
                    {
                        MethodName = b.MethodName,
                        FileName = b.FileName,
                        FilePath = b.FilePath,
                        StartLine = b.StartLine,
                        EndLine = b.EndLine,
                        LineCount = b.LineCount,
                        ProjectName = b.ProjectName,
                        CodeSnippet = GetCodeSnippet(b.OriginalNode, 3)
                    }).ToList(),
                    SimilarityPercentage = similarity,
                    LineCount = clusterBlocks.Max(b => b.LineCount),
                    Hash = clusterUnits[0][0].Hash
                });
            }

            // Sort by line count (larger duplicates first)
            duplicateBlocks = duplicateBlocks
                .OrderByDescending(b => b.LineCount)
                .ThenByDescending(b => b.Instances.Count)
                .ThenByDescending(b => b.SimilarityPercentage)
                .ToList();

            for (int i = 0; i < duplicateBlocks.Count; i++)
                duplicateBlocks[i].GroupId = i + 1;

            return duplicateBlocks;
        }

        /// <summary>
        /// Similarity in percent (rounded down): 2 × LCS / (|a| + |b|) over the token sequences.
        /// Returns 0 early when the length difference alone rules out reaching the threshold.
        /// </summary>
        internal static int ComputeSimilarity(string[] a, string[] b, int threshold = 0)
        {
            if (a.Length == 0 || b.Length == 0)
                return 0;

            var total = a.Length + b.Length;
            if (200L * Math.Min(a.Length, b.Length) < (long)threshold * total)
                return 0;

            if ((long)a.Length * b.Length > MaxLcsCells)
                return a.SequenceEqual(b) ? 100 : 0;

            // Classic two-row LCS
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    current[j] = a[i - 1] == b[j - 1]
                        ? previous[j - 1] + 1
                        : Math.Max(previous[j], current[j - 1]);
                }
                (previous, current) = (current, previous);
            }

            return (int)(200L * previous[b.Length] / total);
        }

        private static HashSet<int> GetShingles(string[] tokens)
        {
            var shingles = new HashSet<int>();
            for (int i = 0; i + ShingleLength <= tokens.Length; i++)
            {
                var hash = new HashCode();
                for (int j = 0; j < ShingleLength; j++)
                    hash.Add(tokens[i + j], StringComparer.Ordinal);
                shingles.Add(hash.ToHashCode());
            }

            // Bodies shorter than one shingle still need a key to be compared at all.
            if (shingles.Count == 0 && tokens.Length > 0)
                shingles.Add(string.Join(" ", tokens).GetHashCode());

            return shingles;
        }

        /// <summary>
        /// Computes SHA256 hash of the code
        /// </summary>
        private static string ComputeHash(string code)
        {
            var bytes = Encoding.UTF8.GetBytes(code);
            return Convert.ToBase64String(SHA256.HashData(bytes));
        }

        /// <summary>
        /// Gets a code snippet (first N lines) for preview
        /// </summary>
        private static string GetCodeSnippet(SyntaxNode node, int maxLines)
        {
            var lines = node.ToString()
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Take(maxLines);

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Calculates statistics for the results
        /// </summary>
        private void CalculateStatistics(DuplicateCodeResults results)
        {
            results.HighSimilarityCount = results.DuplicateBlocks
                .Count(b => b.SimilarityPercentage >= 95);

            results.MediumSimilarityCount = results.DuplicateBlocks
                .Count(b => b.SimilarityPercentage >= 85 && b.SimilarityPercentage < 95);

            results.LowSimilarityCount = results.DuplicateBlocks
                .Count(b => b.SimilarityPercentage < 85);
        }

        /// <summary>
        /// Internal class to hold code block information during analysis
        /// </summary>
        internal class CodeBlockInfo
        {
            public string MethodName { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public int LineCount { get; set; }
            public string ProjectName { get; set; } = string.Empty;
            public string[] Tokens { get; set; } = Array.Empty<string>();
            public string Hash { get; set; } = string.Empty;
            public SyntaxNode OriginalNode { get; set; } = null!;
        }
    }
}
