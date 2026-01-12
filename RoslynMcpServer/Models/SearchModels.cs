using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Models
{
    /// <summary>
    /// Represents a warning or partial failure during an operation
    /// </summary>
    public class OperationWarning
    {
        public string Context { get; set; } = string.Empty;  // What operation failed
        public string Message { get; set; } = string.Empty;  // Error message
        public string? Details { get; set; }                 // Additional details (optional)
    }
    public class SymbolSearchResult
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string Accessibility { get; set; } = string.Empty;
        public SymbolKind SymbolKind { get; set; }
        public string Namespace { get; set; } = string.Empty;
    }

    public class ReferenceResult
    {
        public string SymbolName { get; set; } = string.Empty;
        public string DocumentPath { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public string LineText { get; set; } = string.Empty;
        public List<string> Context { get; set; } = new();
        public bool IsDefinition { get; set; }
        public string ReferenceKind { get; set; } = string.Empty;
    }

    public class SymbolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Accessibility { get; set; } = string.Empty;
        public string DeclaringType { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Assembly { get; set; } = string.Empty;
        public string Documentation { get; set; } = string.Empty;
        public List<string> Parameters { get; set; } = new();
        public string ReturnType { get; set; } = string.Empty;
        public List<string> Attributes { get; set; } = new();
        public string SourceLocation { get; set; } = string.Empty;
    }

    public class DependencyAnalysis
    {
        public string ProjectName { get; set; } = string.Empty;
        public List<ProjectDependency> Dependencies { get; set; } = new();
        public List<NamespaceUsage> NamespaceUsages { get; set; } = new();
        public int TotalSymbols { get; set; }
        public int PublicSymbols { get; set; }
        public int InternalSymbols { get; set; }

        // Circular dependency detection
        public List<CircularDependency> CircularDependencies { get; set; } = new();
        public int CircularDependencyCount => CircularDependencies.Count;
        public bool HasCircularDependencies => CircularDependencies.Any();

        // Partial failure tracking
        public int AnalyzedProjects { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();
    }

    public class ProjectDependency
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // ProjectReference, PackageReference, etc.
        public int UsageCount { get; set; }
    }

    public class NamespaceUsage
    {
        public string Namespace { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public List<string> UsedTypes { get; set; } = new();
    }

    public class CircularDependency
    {
        public List<string> ProjectChain { get; set; } = new();
        public string Description { get; set; } = string.Empty;
        public int ChainLength => ProjectChain.Count;
        public string CycleType { get; set; } = string.Empty; // "Direct" or "Indirect"
    }

    public class ComplexityResult
    {
        public string MethodName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int Complexity { get; set; }
        public int CognitiveComplexity { get; set; }
        public int MaxNestingDepth { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
    }

    public class AnalysisResult
    {
        public int ProcessedDocuments { get; set; }
        public List<SymbolSearchResult> Symbols { get; set; } = new();
        public List<ComplexityResult> ComplexityIssues { get; set; } = new();
        public DateTime AnalysisStartTime { get; set; }
        public DateTime AnalysisEndTime { get; set; }
        public TimeSpan Duration => AnalysisEndTime - AnalysisStartTime;
    }

    // Phase 4: Diagnostics
    public class CompilationError
    {
        public string Id { get; set; } = string.Empty;           // Error code (e.g., "CS0103")
        public string Severity { get; set; } = string.Empty;     // Error, Warning, Info
        public string Message { get; set; } = string.Empty;      // Error message
        public string FilePath { get; set; } = string.Empty;     // Full file path
        public string FileName { get; set; } = string.Empty;     // File name only
        public string ProjectName { get; set; } = string.Empty;  // Project name
        public int LineNumber { get; set; }                      // Line number (1-based)
        public int ColumnNumber { get; set; }                    // Column number (1-based)
        public string LineText { get; set; } = string.Empty;     // Source code line
    }

    // Phase 4: File Analysis
    public class FileOutlineResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int TotalLines { get; set; }
        public int CodeLines { get; set; }
        public int CommentLines { get; set; }
        public int BlankLines { get; set; }
        public List<string> Namespaces { get; set; } = new();
        public List<string> UsingStatements { get; set; } = new();
        public List<TypeOutline> Types { get; set; } = new();

        // Partial failure tracking
        public int FailedTypes { get; set; }
        public int FailedMembers { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();
    }

    public class TypeOutline
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;          // Class, Interface, Struct, Enum, Record
        public string Accessibility { get; set; } = string.Empty; // Public, Internal, Private, Protected
        public string Namespace { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public List<string> BaseTypes { get; set; } = new();      // Inheritance and interfaces
        public List<string> Attributes { get; set; } = new();     // Applied attributes
        public string Documentation { get; set; } = string.Empty; // XML doc summary
        public List<MemberOutline> Members { get; set; } = new();
    }

    public class MemberOutline
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;          // Method, Property, Field, Event, Constructor
        public string Type { get; set; } = string.Empty;          // Return type or field type
        public string Accessibility { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;     // Full signature
        public int LineNumber { get; set; }
        public bool IsStatic { get; set; }
        public bool IsAsync { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsOverride { get; set; }
        public List<string> Attributes { get; set; } = new();
        public string Documentation { get; set; } = string.Empty;
    }

    // Phase 4 Week 2: FindImplementations
    public class ImplementationResult
    {
        public string ImplementingTypeName { get; set; } = string.Empty;
        public string ImplementingTypeFullName { get; set; } = string.Empty;
        public string InterfaceOrBaseTypeName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Accessibility { get; set; } = string.Empty;
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public string Namespace { get; set; } = string.Empty;
        public string Documentation { get; set; } = string.Empty;
        public List<string> ImplementedInterfaces { get; set; } = new();
        public string BaseClass { get; set; } = string.Empty;
    }

    // Phase 4 Week 2: FindTestsForType
    public class TestClassResult
    {
        public string TestClassName { get; set; } = string.Empty;
        public string TestClassFullName { get; set; } = string.Empty;
        public string TestedTypeName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string TestFramework { get; set; } = string.Empty;  // xUnit, NUnit, MSTest, Unknown
        public List<TestMethodResult> TestMethods { get; set; } = new();
        public int TestCount => TestMethods.Count;
        public string Documentation { get; set; } = string.Empty;
    }

    public class TestMethodResult
    {
        public string MethodName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public List<string> TestAttributes { get; set; } = new();  // Fact, Theory, Test, TestMethod, etc.
        public string DisplayName { get; set; } = string.Empty;    // Custom test display name if specified
    }

    // Phase 4 Week 3: GetClassHierarchy
    public class ClassHierarchyResult
    {
        public string TypeName { get; set; } = string.Empty;
        public string TypeFullName { get; set; } = string.Empty;
        public string TypeKind { get; set; } = string.Empty;  // Class, Interface, Struct, Record
        public string Accessibility { get; set; } = string.Empty;
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public string Namespace { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public List<HierarchyNode> Ancestors { get; set; } = new();      // Base classes and interfaces
        public List<HierarchyNode> Descendants { get; set; } = new();    // Derived classes
        public string Documentation { get; set; } = string.Empty;
    }

    public class HierarchyNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string TypeKind { get; set; } = string.Empty;
        public bool IsAbstract { get; set; }
        public bool IsInterface { get; set; }
        public string Namespace { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int Depth { get; set; }  // Distance from root type
        public List<HierarchyNode> Children { get; set; } = new();  // Nested hierarchy
    }

    // Phase 4 Week 3: FindAttributeUsages
    public class AttributeUsageResult
    {
        public string AttributeName { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string TargetFullName { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;  // Class, Method, Property, Field, Parameter, etc.
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Namespace { get; set; } = string.Empty;
        public string DeclaringType { get; set; } = string.Empty;  // For members, the containing type
        public List<string> AttributeArguments { get; set; } = new();  // Constructor arguments
        public Dictionary<string, string> NamedArguments { get; set; } = new();  // Named arguments
        public string Signature { get; set; } = string.Empty;  // Full signature of the target
    }

    /// <summary>
    /// Wrapper for attribute search results with failure tracking
    /// </summary>
    public class AttributeSearchResults
    {
        public List<AttributeUsageResult> Usages { get; set; } = new();
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Wrapper for compilation errors with failure tracking
    /// </summary>
    public class CompilationErrorResults
    {
        public List<CompilationError> Errors { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int FailedProjects { get; set; }
        public int FailedDiagnostics { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Represents an unused code item (type, method, property, field)
    /// </summary>
    public class UnusedItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;  // Class, Method, Property, Field, Event
        public string Accessibility { get; set; } = string.Empty;  // Private, Internal, Public
        public string DeclaringType { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Signature { get; set; } = string.Empty;
        public bool IsTestMember { get; set; }  // Member in test project
        public string Reason { get; set; } = string.Empty;  // Why it's considered unused
    }

    /// <summary>
    /// Wrapper for unused code analysis results
    /// </summary>
    public class UnusedCodeResults
    {
        public List<UnusedItem> UnusedItems { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedSymbols { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by category
        public int PrivateCount { get; set; }
        public int InternalCount { get; set; }
        public int PublicCount { get; set; }

        // Statistics by kind
        public int ClassCount { get; set; }
        public int MethodCount { get; set; }
        public int PropertyCount { get; set; }
        public int FieldCount { get; set; }
        public int EventCount { get; set; }
    }

    /// <summary>
    /// Represents an unused dependency (NuGet package or project reference)
    /// </summary>
    public class UnusedDependency
    {
        public string Name { get; set; } = string.Empty;  // Package or project name
        public string Version { get; set; } = string.Empty;  // Package version (empty for project refs)
        public string Type { get; set; } = string.Empty;  // "NuGetPackage" or "ProjectReference"
        public string ProjectName { get; set; } = string.Empty;  // Project that has this dependency
        public string ProjectPath { get; set; } = string.Empty;  // Full path to project file
        public string Reason { get; set; } = string.Empty;  // Why it's considered unused
        public List<string> ExpectedNamespaces { get; set; } = new();  // Namespaces that should be used
    }

    /// <summary>
    /// Wrapper for unused dependency analysis results
    /// </summary>
    public class UnusedDependencyResults
    {
        public List<UnusedDependency> UnusedDependencies { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by type
        public int UnusedNuGetPackages { get; set; }
        public int UnusedProjectReferences { get; set; }

        // Potential savings
        public int TotalUnusedDependencies => UnusedNuGetPackages + UnusedProjectReferences;
    }

    /// <summary>
    /// Represents a security issue detected in code
    /// </summary>
    public class SecurityIssue
    {
        public string Category { get; set; } = string.Empty;  // sql-injection, secrets, crypto, etc.
        public string Severity { get; set; } = string.Empty;  // Critical, High, Medium, Low
        public string Title { get; set; } = string.Empty;  // Short title
        public string Description { get; set; } = string.Empty;  // Detailed description
        public string Recommendation { get; set; } = string.Empty;  // How to fix
        public string MethodName { get; set; } = string.Empty;  // Method containing the issue
        public string FileName { get; set; } = string.Empty;  // File name
        public string FilePath { get; set; } = string.Empty;  // Full path
        public int LineNumber { get; set; }  // Line number
        public string CodeSnippet { get; set; } = string.Empty;  // Problematic code
        public string ProjectName { get; set; } = string.Empty;  // Project name
    }

    /// <summary>
    /// Wrapper for security issue analysis results
    /// </summary>
    public class SecurityIssueResults
    {
        public List<SecurityIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by severity
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }

        // Statistics by category
        public int SqlInjectionCount { get; set; }
        public int HardcodedSecretsCount { get; set; }
        public int WeakCryptoCount { get; set; }
        public int PathTraversalCount { get; set; }
        public int DeserializationCount { get; set; }
        public int OtherCount { get; set; }

        public int TotalIssues => Issues.Count;
    }

    /// <summary>
    /// Represents a code block instance in a duplicate set
    /// </summary>
    public class CodeBlockInstance
    {
        public string MethodName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int LineCount { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;  // First few lines for preview
    }

    /// <summary>
    /// Represents a set of duplicate code blocks
    /// </summary>
    public class DuplicateCodeBlock
    {
        public int GroupId { get; set; }
        public List<CodeBlockInstance> Instances { get; set; } = new();
        public int SimilarityPercentage { get; set; }
        public int LineCount { get; set; }
        public string Hash { get; set; } = string.Empty;  // Hash of the normalized code
    }

    /// <summary>
    /// Wrapper for duplicate code analysis results
    /// </summary>
    public class DuplicateCodeResults
    {
        public List<DuplicateCodeBlock> DuplicateBlocks { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int AnalyzedMethods { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics
        public int TotalDuplicateBlocks => DuplicateBlocks.Count;
        public int TotalDuplicateInstances => DuplicateBlocks.Sum(b => b.Instances.Count);
        public int HighSimilarityCount { get; set; }  // 95%+
        public int MediumSimilarityCount { get; set; }  // 85-94%
        public int LowSimilarityCount { get; set; }  // Below 85%
    }

    /// <summary>
    /// Represents an undocumented symbol (type, method, property, etc.)
    /// </summary>
    public class UndocumentedSymbol
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;  // Class, Method, Property, etc.
        public string Accessibility { get; set; } = string.Empty;  // Public, Internal, etc.
        public string ContainingType { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Signature { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string SuggestedDocumentation { get; set; } = string.Empty;  // AI-generated suggestion
        public List<string> Parameters { get; set; } = new();  // For methods
        public string ReturnType { get; set; } = string.Empty;  // For methods
    }

    /// <summary>
    /// Wrapper for documentation coverage analysis results
    /// </summary>
    public class DocumentationCoverageResults
    {
        public List<UndocumentedSymbol> UndocumentedSymbols { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int TotalSymbols { get; set; }
        public int DocumentedSymbols { get; set; }
        public int UndocumentedCount { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by kind
        public int UndocumentedClasses { get; set; }
        public int UndocumentedMethods { get; set; }
        public int UndocumentedProperties { get; set; }
        public int UndocumentedFields { get; set; }
        public int UndocumentedEvents { get; set; }

        // Coverage percentage
        public double CoveragePercentage => TotalSymbols > 0 ? (DocumentedSymbols * 100.0 / TotalSymbols) : 0;
    }

    /// <summary>
    /// Represents a TODO/FIXME/HACK comment found in code
    /// </summary>
    public class TODOComment
    {
        public string Type { get; set; } = string.Empty;  // TODO, FIXME, HACK, NOTE, BUG
        public string Message { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;  // If present in comment
        public string CodeContext { get; set; } = string.Empty;  // Surrounding code
    }

    /// <summary>
    /// Results from TODO comment analysis
    /// </summary>
    public class TODOCommentResults
    {
        public List<TODOComment> Comments { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by type
        public int TODOCount { get; set; }
        public int FIXMECount { get; set; }
        public int HACKCount { get; set; }
        public int NOTECount { get; set; }
        public int BUGCount { get; set; }
        public int OtherCount { get; set; }

        public int TotalComments => Comments.Count;
    }

    /// <summary>
    /// Represents a large source file
    /// </summary>
    public class LargeFile
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public long SizeInBytes { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int TypeCount { get; set; }  // Number of classes/interfaces/structs
        public int MethodCount { get; set; }  // Number of methods
    }

    /// <summary>
    /// Results from large file analysis
    /// </summary>
    public class LargeFileResults
    {
        public List<LargeFile> LargeFiles { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalLargeFiles => LargeFiles.Count;
        public int AverageLineCount => LargeFiles.Any() ? (int)LargeFiles.Average(f => f.LineCount) : 0;
        public int MaxLineCount => LargeFiles.Any() ? LargeFiles.Max(f => f.LineCount) : 0;
        public long TotalSizeInBytes => LargeFiles.Sum(f => f.SizeInBytes);
    }

    /// <summary>
    /// Represents a usage of a deprecated/obsolete API
    /// </summary>
    public class DeprecatedAPIUsage
    {
        public string APIName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string ObsoleteMessage { get; set; } = string.Empty;
        public bool IsError { get; set; }  // ObsoleteAttribute with IsError=true
        public string CodeContext { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a deprecated API with all its usages
    /// </summary>
    public class DeprecatedAPI
    {
        public string APIName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string ObsoleteMessage { get; set; } = string.Empty;
        public bool IsError { get; set; }
        public List<DeprecatedAPIUsage> Usages { get; set; } = new();
        public string Suggestion { get; set; } = string.Empty;  // Migration suggestion
    }

    /// <summary>
    /// Results from deprecated API analysis
    /// </summary>
    public class DeprecatedAPIResults
    {
        public List<DeprecatedAPI> DeprecatedAPIs { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalDeprecatedAPIs => DeprecatedAPIs.Count;
        public int TotalUsages => DeprecatedAPIs.Sum(api => api.Usages.Count);
        public int ErrorAPIs => DeprecatedAPIs.Count(api => api.IsError);
        public int WarningAPIs => DeprecatedAPIs.Count(api => !api.IsError);
    }

    /// <summary>
    /// Statistics for a single file
    /// </summary>
    public class FileStatistics
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;

        // Line counts
        public int TotalLines { get; set; }
        public int CodeLines { get; set; }
        public int CommentLines { get; set; }
        public int BlankLines { get; set; }

        // File info
        public long SizeInBytes { get; set; }

        // Code elements
        public int ClassCount { get; set; }
        public int InterfaceCount { get; set; }
        public int StructCount { get; set; }
        public int EnumCount { get; set; }
        public int MethodCount { get; set; }
        public int PropertyCount { get; set; }
        public int FieldCount { get; set; }

        // Complexity
        public int CyclomaticComplexity { get; set; }
        public int MaxMethodComplexity { get; set; }
        public string MostComplexMethod { get; set; } = string.Empty;

        // Dependencies
        public int UsingDirectivesCount { get; set; }
        public List<string> Namespaces { get; set; } = new();

        // Documentation
        public int DocumentedMembers { get; set; }
        public int UndocumentedMembers { get; set; }
        public double DocumentationCoverage => (DocumentedMembers + UndocumentedMembers) > 0
            ? (DocumentedMembers * 100.0 / (DocumentedMembers + UndocumentedMembers))
            : 0;
    }

    /// <summary>
    /// Results from file statistics analysis
    /// </summary>
    public class FileStatisticsResults
    {
        public FileStatistics? Statistics { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();
    }
}