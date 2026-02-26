using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for detecting IPC (Inter-Process Communication) patterns in C# code.
    /// Detects: Named pipe usage and configuration issues, JSON-RPC protocol patterns,
    /// IPC error handling gaps, synchronous pipe I/O, missing timeouts,
    /// hardcoded pipe names, and unbuffered pipe streams.
    /// </summary>
    public class IpcPatternAnalyzer
    {
        private readonly ILogger<IpcPatternAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public IpcPatternAnalyzer(
            ILogger<IpcPatternAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for IPC patterns and issues.
        /// </summary>
        public async Task<IpcAnalysisResults> AnalyzeAsync(
            string solutionPath,
            string[] issueTypes)
        {
            var results = new IpcAnalysisResults();
            var allIssues = new ConcurrentBag<IpcPatternIssue>();

            try
            {
                var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);

                var projects = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .ToList();

                results.AnalyzedProjects = projects.Count;

                var projectTasks = projects.Select(async project =>
                {
                    try
                    {
                        var issues = await AnalyzeProjectAsync(project, issueTypes);
                        foreach (var issue in issues)
                            allIssues.Add(issue);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                        results.FailedProjects++;
                    }
                });

                await Task.WhenAll(projectTasks);

                results.Issues = allIssues.ToList();
                results.AnalyzedFiles = results.Issues.Select(i => i.FilePath).Distinct().Count();

                results.IssuesByType = results.Issues
                    .GroupBy(i => i.IssueType)
                    .ToDictionary(g => g.Key, g => g.Count());

                results.IssuesByProject = results.Issues
                    .GroupBy(i => i.ProjectName)
                    .ToDictionary(g => g.Key, g => g.Count());

                _logger.LogInformation(
                    "IPC pattern analysis complete: {IssueCount} issues found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing IPC patterns");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<IpcPatternIssue>> AnalyzeProjectAsync(
            Project project, string[] issueTypes)
        {
            var issues = new List<IpcPatternIssue>();

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return issues;

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                // Skip generated files
                if (syntaxTree.FilePath.Contains(".g.cs") ||
                    syntaxTree.FilePath.Contains("\\obj\\") ||
                    syntaxTree.FilePath.Contains("/obj/"))
                    continue;

                try
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    if (ShouldCheck(issueTypes, "NamedPipeUsage"))
                        issues.AddRange(CheckNamedPipeUsage(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "JsonRpcPattern"))
                        issues.AddRange(CheckJsonRpcPattern(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "IpcErrorHandling"))
                        issues.AddRange(CheckIpcErrorHandling(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "SynchronousPipeIo"))
                        issues.AddRange(CheckSynchronousPipeIo(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "MissingPipeTimeout"))
                        issues.AddRange(CheckMissingPipeTimeout(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "HardcodedPipeName"))
                        issues.AddRange(CheckHardcodedPipeName(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "UnbufferedPipe"))
                        issues.AddRange(CheckUnbufferedPipe(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "JsonRpcMissingErrorHandling"))
                        issues.AddRange(CheckJsonRpcMissingErrorHandling(root, semanticModel, syntaxTree, project.Name));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze syntax tree: {FilePath}", syntaxTree.FilePath);
                }
            }

            return issues;
        }

        private static bool ShouldCheck(string[] issueTypes, string issueType) =>
            issueTypes.Length == 0 ||
            issueTypes.Any(t => t.Equals("all", StringComparison.OrdinalIgnoreCase)) ||
            issueTypes.Any(t => t.Equals(issueType, StringComparison.OrdinalIgnoreCase));

        // ──────────────────────────────────────────────────────────────────────
        // CheckNamedPipeUsage
        // Detects NamedPipeClientStream / NamedPipeServerStream construction
        // ──────────────────────────────────────────────────────────────────────

        private List<IpcPatternIssue> CheckNamedPipeUsage(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IpcPatternIssue>();

            var objectCreations = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>();

            foreach (var creation in objectCreations)
            {
                try
                {
                    var typeSymbol = model.GetTypeInfo(creation).Type;
                    if (typeSymbol == null) continue;

                    var fullName = typeSymbol.ToDisplayString();
                    if (fullName != "System.IO.Pipes.NamedPipeClientStream" &&
                        fullName != "System.IO.Pipes.NamedPipeServerStream")
                        continue;

                    var isClient = fullName.Contains("Client");
                    var kind = isClient ? "Client" : "Server";
                    var snippet = creation.ToString();
                    if (snippet.Length > 100) snippet = snippet[..100] + "…";

                    issues.Add(CreateIssue(
                        "NamedPipeUsage", "Low",
                        $"NamedPipe{kind}Stream detected — IPC via named pipe",
                        $"A NamedPipe{kind}Stream is created here. Named pipes provide reliable IPC but require careful " +
                        "attention to security descriptors, timeouts, and error handling.",
                        tree, creation, project, snippet,
                        $"Verify PipeDirection, pipe security (PipeSecurity/ACL), and that the stream is disposed in a using block.",
                        $"using var pipe = new NamedPipe{kind}Stream(pipeName, PipeDirection.InOut,\n" +
                        "    maxInstances: 1, transmissionMode: PipeTransmissionMode.Message);\n" +
                        $"pipe.WaitForConnectionAsync(cancellationToken); // server side"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking named pipe usage in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckJsonRpcPattern
        // Detects JSON-RPC method invocation patterns (StreamJsonRpc / manual JSON-RPC)
        // ──────────────────────────────────────────────────────────────────────

        private List<IpcPatternIssue> CheckJsonRpcPattern(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IpcPatternIssue>();

            var objectCreations = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>();

            foreach (var creation in objectCreations)
            {
                try
                {
                    var typeSymbol = model.GetTypeInfo(creation).Type;
                    if (typeSymbol == null) continue;

                    var fullName = typeSymbol.ToDisplayString();

                    // StreamJsonRpc (Microsoft's JSON-RPC library)
                    if (fullName.Contains("JsonRpc") || fullName.Contains("StreamJsonRpc"))
                    {
                        var snippet = creation.ToString();
                        if (snippet.Length > 100) snippet = snippet[..100] + "…";

                        issues.Add(CreateIssue(
                            "JsonRpcPattern", "Low",
                            $"JSON-RPC object '{typeSymbol.Name}' detected — IPC via JSON-RPC",
                            $"A {typeSymbol.Name} instance is created. JSON-RPC over named pipes or streams is a common " +
                            "IPC pattern in language servers (LSP), dev tools, and plugin architectures.",
                            tree, creation, project, snippet,
                            "Ensure the JsonRpc instance is disposed, error notifications are handled, and the connection " +
                            "is protected against untrusted input.",
                            "var rpc = JsonRpc.Attach(stream, target);\nawait rpc.StartListeningAsync();\n" +
                            "// Dispose when done: await rpc.DisposeAsync();"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking JSON-RPC pattern in {File}", tree.FilePath);
                }
            }

            // Also detect manual JSON-RPC method dispatch: looking for "method" + "id" + "params" pattern
            // in string literal assignments that match the JSON-RPC 2.0 spec
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var typeName = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    // StreamJsonRpc: JsonRpc.InvokeAsync / NotifyAsync / InvokeWithCancellationAsync
                    if ((typeName.Contains("JsonRpc") || typeName.Contains("StreamJsonRpc")) &&
                        (methodName is "InvokeAsync" or "NotifyAsync" or "InvokeWithCancellationAsync" or
                                       "InvokeWithProgressReportingAsync" or "InvokeAsync"))
                    {
                        var snippet = invocation.ToString();
                        if (snippet.Length > 100) snippet = snippet[..100] + "…";

                        issues.Add(CreateIssue(
                            "JsonRpcPattern", "Low",
                            $"JSON-RPC {methodName} call — remote procedure invocation over IPC",
                            $"A JSON-RPC remote call ({methodName}) is made. Ensure the target method name is not " +
                            "constructed from user input to prevent method injection attacks.",
                            tree, invocation, project, snippet,
                            "Use compile-time method name constants rather than string literals for RPC method names.",
                            "// Prefer typed proxies over string-based invocation:\n" +
                            "var proxy = rpc.Attach<IMyService>();\nawait proxy.DoWorkAsync();"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking JSON-RPC invocation in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckIpcErrorHandling
        // Detects pipe Read/Write calls without try/catch for IOException/PipeException
        // ──────────────────────────────────────────────────────────────────────

        private List<IpcPatternIssue> CheckIpcErrorHandling(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IpcPatternIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var typeName = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    bool isPipeOperation =
                        (typeName.Contains("NamedPipeClientStream") ||
                         typeName.Contains("NamedPipeServerStream") ||
                         typeName.Contains("AnonymousPipeClientStream") ||
                         typeName.Contains("AnonymousPipeServerStream")) &&
                        (methodName is "Read" or "Write" or "ReadAsync" or "WriteAsync" or
                                       "Connect" or "ConnectAsync" or "WaitForConnection" or "WaitForConnectionAsync");

                    if (!isPipeOperation)
                        continue;

                    // Check if inside a try block that catches IOException or PipeException
                    var enclosingTry = invocation.Ancestors()
                        .OfType<TryStatementSyntax>()
                        .FirstOrDefault();

                    if (enclosingTry == null)
                    {
                        var snippet = invocation.ToString();
                        if (snippet.Length > 80) snippet = snippet[..80] + "…";

                        issues.Add(CreateIssue(
                            "IpcErrorHandling", "High",
                            $"Pipe {methodName}() without exception handling — broken pipe risk",
                            $"Calling {methodName}() on a named pipe without a try/catch means the process will " +
                            "crash on pipe disconnection (IOException/PipeException). Pipes can break at any time " +
                            "if the remote process exits or the connection is reset.",
                            tree, invocation, project, snippet,
                            "Wrap pipe I/O in try/catch (IOException, ObjectDisposedException) and handle graceful reconnect.",
                            "try\n{\n    await pipe.ReadAsync(buffer, cancellationToken);\n}\n" +
                            "catch (IOException ex) when (ex.Message.Contains(\"pipe\"))\n{\n" +
                            "    // Handle broken pipe — reconnect or exit\n}"));
                    }
                    else
                    {
                        // Try block exists — verify it catches relevant exceptions
                        bool catchesIoException = enclosingTry.Catches.Any(c =>
                        {
                            if (c.Declaration == null) return true; // catch-all
                            var catchTypeName = c.Declaration.Type.ToString();
                            return catchTypeName.Contains("IOException") ||
                                   catchTypeName.Contains("PipeException") ||
                                   catchTypeName.Contains("Exception");
                        });

                        if (!catchesIoException)
                        {
                            var snippet = invocation.ToString();
                            if (snippet.Length > 80) snippet = snippet[..80] + "…";

                            issues.Add(CreateIssue(
                                "IpcErrorHandling", "Medium",
                                $"Pipe {methodName}() — try block present but may not catch IOException",
                                "The pipe operation is inside a try block, but the catch clauses may not handle " +
                                "IOException or PipeException which are the expected failures for named pipes.",
                                tree, invocation, project, snippet,
                                "Ensure the catch block handles IOException and ObjectDisposedException.",
                                "catch (IOException) { /* broken pipe */ }\ncatch (ObjectDisposedException) { /* pipe closed */ }"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking IPC error handling in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckSynchronousPipeIo
        // Detects synchronous Read/Write on named pipes (blocking thread pool)
        // ──────────────────────────────────────────────────────────────────────

        private List<IpcPatternIssue> CheckSynchronousPipeIo(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IpcPatternIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var typeName = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    bool isSyncPipeIo =
                        (typeName.Contains("NamedPipeClientStream") ||
                         typeName.Contains("NamedPipeServerStream")) &&
                        (methodName is "Read" or "Write" or
                                       "Connect" or "WaitForConnection");

                    if (!isSyncPipeIo)
                        continue;

                    // Check if we are inside an async method
                    bool inAsyncMethod = invocation.Ancestors()
                        .OfType<MethodDeclarationSyntax>()
                        .Any(m => m.Modifiers.Any(SyntaxKind.AsyncKeyword));

                    if (!inAsyncMethod)
                        continue; // Sync in sync method is expected

                    var snippet = invocation.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    issues.Add(CreateIssue(
                        "SynchronousPipeIo", "Medium",
                        $"Synchronous pipe {methodName}() in async method — thread pool blocking",
                        $"Calling synchronous {methodName}() on a named pipe inside an async method blocks the " +
                        "thread pool thread for the duration of the I/O operation. Use the async overload instead.",
                        tree, invocation, project, snippet,
                        $"Replace {methodName}() with {methodName}Async(cancellationToken).",
                        $"// Before: pipe.{methodName}(buffer, 0, count);\n" +
                        $"// After:  await pipe.{methodName}Async(buffer, 0, count, cancellationToken);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking synchronous pipe I/O in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckMissingPipeTimeout
        // Detects Connect() / WaitForConnection() without timeout/cancellation
        // ──────────────────────────────────────────────────────────────────────

        private List<IpcPatternIssue> CheckMissingPipeTimeout(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IpcPatternIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var typeName = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    bool isConnectOrWait =
                        (typeName.Contains("NamedPipeClientStream") ||
                         typeName.Contains("NamedPipeServerStream")) &&
                        (methodName is "Connect" or "WaitForConnection" or
                                       "ConnectAsync" or "WaitForConnectionAsync");

                    if (!isConnectOrWait)
                        continue;

                    var args = invocation.ArgumentList.Arguments;

                    // Connect(int timeout) or ConnectAsync(CancellationToken) — check for no-arg versions
                    bool hasTimeout = args.Any(a =>
                    {
                        var argTypeInfo = model.GetTypeInfo(a.Expression);
                        var argType = argTypeInfo.Type?.SpecialType;
                        return argType == SpecialType.System_Int32 || // int timeout
                               argTypeInfo.Type?.ToDisplayString().Contains("CancellationToken") == true;
                    });

                    if (hasTimeout)
                        continue;

                    // Check if it's a zero-arg call (no timeout, no cancellation)
                    if (args.Count != 0)
                        continue;

                    var snippet = invocation.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    issues.Add(CreateIssue(
                        "MissingPipeTimeout", "High",
                        $"Pipe {methodName}() without timeout — potential indefinite hang",
                        $"Calling {methodName}() with no timeout or CancellationToken means the call can block " +
                        "indefinitely if the other end never connects or responds. This causes thread exhaustion " +
                        "in server scenarios.",
                        tree, invocation, project, snippet,
                        $"Pass a timeout (milliseconds) or a CancellationToken to {methodName}.",
                        $"// With timeout:\npipe.Connect(timeoutMs: 5000);\n\n" +
                        $"// With cancellation (preferred):\nawait pipe.ConnectAsync(cancellationToken);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking pipe timeout in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckHardcodedPipeName
        // Detects hardcoded string literals used as pipe names in constructors
        // ──────────────────────────────────────────────────────────────────────

        private List<IpcPatternIssue> CheckHardcodedPipeName(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IpcPatternIssue>();

            var objectCreations = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>();

            foreach (var creation in objectCreations)
            {
                try
                {
                    var typeSymbol = model.GetTypeInfo(creation).Type;
                    if (typeSymbol == null) continue;

                    var fullName = typeSymbol.ToDisplayString();
                    if (!fullName.Contains("NamedPipeClientStream") &&
                        !fullName.Contains("NamedPipeServerStream"))
                        continue;

                    if (creation.ArgumentList == null || creation.ArgumentList.Arguments.Count == 0)
                        continue;

                    // The pipe name argument (for ClientStream: arg[1] if server is arg[0], arg[0] for ServerStream)
                    // NamedPipeClientStream(string serverName, string pipeName, ...)
                    // NamedPipeServerStream(string pipeName, ...)
                    int pipeNameArgIdx = fullName.Contains("Client") ? 1 : 0;
                    var args = creation.ArgumentList.Arguments;

                    if (args.Count <= pipeNameArgIdx)
                        continue;

                    var pipeNameArg = args[pipeNameArgIdx].Expression;

                    if (!pipeNameArg.IsKind(SyntaxKind.StringLiteralExpression))
                        continue;

                    var literalValue = ((LiteralExpressionSyntax)pipeNameArg).Token.ValueText;
                    var snippet = creation.ToString();
                    if (snippet.Length > 100) snippet = snippet[..100] + "…";

                    issues.Add(CreateIssue(
                        "HardcodedPipeName", "Medium",
                        $"Hardcoded pipe name \"{literalValue}\" — use a configurable constant",
                        $"The named pipe name \"{literalValue}\" is hardcoded as a string literal. " +
                        "Hardcoded pipe names make it harder to run multiple instances and can clash with other processes.",
                        tree, creation, project, snippet,
                        "Extract the pipe name to a configuration constant or app setting to allow per-instance customization.",
                        "// Before: new NamedPipeClientStream(\".\", \"my-pipe\", ...);\n" +
                        "// After:  new NamedPipeClientStream(\".\", PipeNames.MainPipe, ...);\n" +
                        "// Where:  public static class PipeNames { public const string MainPipe = \"my-pipe\"; }"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking hardcoded pipe name in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckUnbufferedPipe
        // Detects pipes created without StreamWriter/StreamReader buffering
        // ──────────────────────────────────────────────────────────────────────

        private List<IpcPatternIssue> CheckUnbufferedPipe(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IpcPatternIssue>();

            // Find pipes that are directly used for Read/Write without wrapping in StreamReader/StreamWriter
            var localDecls = root.DescendantNodes()
                .OfType<LocalDeclarationStatementSyntax>();

            foreach (var decl in localDecls)
            {
                try
                {
                    foreach (var variable in decl.Declaration.Variables)
                    {
                        if (variable.Initializer == null) continue;

                        var initTypeInfo = model.GetTypeInfo(variable.Initializer.Value);
                        var initTypeName = initTypeInfo.Type?.ToDisplayString() ?? string.Empty;

                        if (!initTypeName.Contains("NamedPipeClientStream") &&
                            !initTypeName.Contains("NamedPipeServerStream"))
                            continue;

                        // Get the variable name and check if it's ever wrapped in StreamReader/StreamWriter
                        var varName = variable.Identifier.Text;
                        var enclosingMethod = decl.Ancestors()
                            .OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault();

                        if (enclosingMethod == null) continue;

                        var methodText = enclosingMethod.ToString();

                        bool isWrapped = methodText.Contains("StreamReader") ||
                                         methodText.Contains("StreamWriter") ||
                                         methodText.Contains("BinaryReader") ||
                                         methodText.Contains("BinaryWriter") ||
                                         methodText.Contains("BufferedStream");

                        if (isWrapped) continue;

                        var snippet = variable.ToString();
                        if (snippet.Length > 80) snippet = snippet[..80] + "…";

                        issues.Add(CreateIssue(
                            "UnbufferedPipe", "Low",
                            $"Pipe '{varName}' used without buffered reader/writer",
                            $"The named pipe '{varName}' is not wrapped in a StreamReader/StreamWriter or BinaryReader/Writer. " +
                            "Unbuffered pipe I/O with small reads/writes is inefficient — each call is a system call.",
                            tree, variable, project, snippet,
                            "Wrap the pipe in a StreamReader/StreamWriter (with AutoFlush=true) or BinaryReader/Writer for efficient I/O.",
                            "using var writer = new StreamWriter(pipe) { AutoFlush = true };\n" +
                            "using var reader = new StreamReader(pipe);\nawait writer.WriteLineAsync(message);"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking unbuffered pipe in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckJsonRpcMissingErrorHandling
        // Detects JSON-RPC invocations without handling RemoteInvocationException
        // ──────────────────────────────────────────────────────────────────────

        private List<IpcPatternIssue> CheckJsonRpcMissingErrorHandling(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IpcPatternIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var typeName = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    bool isJsonRpcCall =
                        (typeName.Contains("JsonRpc") || typeName.Contains("StreamJsonRpc")) &&
                        (methodName is "InvokeAsync" or "InvokeWithCancellationAsync" or
                                       "InvokeWithProgressReportingAsync");

                    if (!isJsonRpcCall)
                        continue;

                    // Check for enclosing try/catch that handles RemoteInvocationException
                    var enclosingTry = invocation.Ancestors()
                        .OfType<TryStatementSyntax>()
                        .FirstOrDefault();

                    if (enclosingTry == null)
                    {
                        var snippet = invocation.ToString();
                        if (snippet.Length > 80) snippet = snippet[..80] + "…";

                        issues.Add(CreateIssue(
                            "JsonRpcMissingErrorHandling", "High",
                            $"JSON-RPC {methodName} without error handling — unhandled RemoteInvocationException",
                            $"JSON-RPC {methodName} can throw RemoteInvocationException when the remote method fails. " +
                            "Without a try/catch, unhandled RPC errors will propagate as unhandled exceptions.",
                            tree, invocation, project, snippet,
                            "Wrap JSON-RPC calls in try/catch to handle RemoteInvocationException and ConnectionLostException.",
                            "try\n{\n    await rpc.InvokeAsync(\"method\", args);\n}\n" +
                            "catch (RemoteInvocationException ex)\n{\n    // Handle RPC error\n}\n" +
                            "catch (ConnectionLostException)\n{\n    // Handle disconnected\n}"));
                    }
                    else
                    {
                        bool handlesRpcException = enclosingTry.Catches.Any(c =>
                        {
                            if (c.Declaration == null) return true; // catch-all
                            var catchType = c.Declaration.Type.ToString();
                            return catchType.Contains("RemoteInvocationException") ||
                                   catchType.Contains("ConnectionLostException") ||
                                   catchType.Contains("Exception");
                        });

                        if (!handlesRpcException)
                        {
                            var snippet = invocation.ToString();
                            if (snippet.Length > 80) snippet = snippet[..80] + "…";

                            issues.Add(CreateIssue(
                                "JsonRpcMissingErrorHandling", "Medium",
                                $"JSON-RPC {methodName} — catch block may miss RemoteInvocationException",
                                "The JSON-RPC call is in a try block, but the catch clauses may not handle " +
                                "RemoteInvocationException or ConnectionLostException.",
                                tree, invocation, project, snippet,
                                "Add explicit catch for RemoteInvocationException and ConnectionLostException.",
                                "catch (RemoteInvocationException ex) { /* RPC error */ }\n" +
                                "catch (ConnectionLostException) { /* pipe broken */ }"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking JSON-RPC error handling in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CreateIssue helper
        // ──────────────────────────────────────────────────────────────────────

        private static IpcPatternIssue CreateIssue(
            string issueType, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string snippet, string recommendation, string fixExample = "")
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;

            return new IpcPatternIssue
            {
                IssueType = issueType,
                Severity = severity,
                Title = title,
                Description = description,
                FilePath = tree.FilePath,
                FileName = Path.GetFileName(tree.FilePath),
                ProjectName = project,
                LineNumber = line,
                SymbolName = string.Empty,
                CodeSnippet = snippet.Trim(),
                Recommendation = recommendation,
                FixExample = fixExample
            };
        }
    }
}
