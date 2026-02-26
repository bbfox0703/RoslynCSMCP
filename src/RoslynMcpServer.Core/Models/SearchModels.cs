using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Core.Models
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

    /// <summary>
    /// Represents a NuGet package in a project
    /// </summary>
    public class PackageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectPath { get; set; } = string.Empty;
        public bool IsUsed { get; set; } = true;  // Whether the package is actually used
        public List<string> UsedNamespaces { get; set; } = new();  // Namespaces from this package that are used
        public List<string> ExpectedNamespaces { get; set; } = new();  // Common namespaces for this package
    }

    /// <summary>
    /// Represents a security vulnerability in a package
    /// </summary>
    public class PackageVulnerability
    {
        public string PackageName { get; set; } = string.Empty;
        public string AffectedVersion { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;  // Critical, High, Medium, Low
        public string VulnerabilityId { get; set; } = string.Empty;  // CVE ID or similar
        public string Description { get; set; } = string.Empty;
        public string RecommendedVersion { get; set; } = string.Empty;  // Minimum safe version
        public List<string> AffectedProjects { get; set; } = new();  // Projects using this vulnerable version
    }

    /// <summary>
    /// Represents an available package update
    /// </summary>
    public class PackageUpdate
    {
        public string PackageName { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public int MajorVersionsAhead { get; set; }
        public int MinorVersionsAhead { get; set; }
        public int PatchVersionsAhead { get; set; }
        public List<string> AffectedProjects { get; set; } = new();
        public bool IsBreakingChange { get; set; }  // Major version change
    }

    /// <summary>
    /// Represents a version conflict between projects
    /// </summary>
    public class PackageConflict
    {
        public string PackageName { get; set; } = string.Empty;
        public List<PackageVersionUsage> VersionUsages { get; set; } = new();
        public string RecommendedVersion { get; set; } = string.Empty;  // Version to standardize to
    }

    /// <summary>
    /// Represents usage of a specific package version in a project
    /// </summary>
    public class PackageVersionUsage
    {
        public string ProjectName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    /// <summary>
    /// Wrapper for package analysis results
    /// </summary>
    public class PackageAnalysisResults
    {
        public List<PackageInfo> AllPackages { get; set; } = new();
        public List<PackageVulnerability> Vulnerabilities { get; set; } = new();
        public List<PackageUpdate> AvailableUpdates { get; set; } = new();
        public List<PackageConflict> VersionConflicts { get; set; } = new();
        public List<PackageInfo> UnusedPackages { get; set; } = new();

        public int AnalyzedProjects { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics
        public int TotalPackages => AllPackages.Count;
        public int UniquePackages => AllPackages.Select(p => p.Name).Distinct().Count();
        public int UpToDatePackages => TotalPackages - AvailableUpdates.Sum(u => u.AffectedProjects.Count);
        public int VulnerablePackages => Vulnerabilities.Count;
        public int UnusedPackagesCount => UnusedPackages.Count;
        public int ConflictingPackages => VersionConflicts.Count;

        // Severity counts
        public int CriticalVulnerabilities => Vulnerabilities.Count(v => v.Severity == "Critical");
        public int HighVulnerabilities => Vulnerabilities.Count(v => v.Severity == "High");
        public int MediumVulnerabilities => Vulnerabilities.Count(v => v.Severity == "Medium");
        public int LowVulnerabilities => Vulnerabilities.Count(v => v.Severity == "Low");
    }

    /// <summary>
    /// Represents a type (class/interface) and its test coverage
    /// </summary>
    public class TypeCoverage
    {
        public string TypeName { get; set; } = string.Empty;
        public string FullTypeName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Accessibility { get; set; } = string.Empty;
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }

        // Test information
        public bool HasTests { get; set; }
        public List<string> TestClasses { get; set; } = new();
        public int TestCount { get; set; }

        // Member coverage
        public int TotalPublicMembers { get; set; }
        public int TestedPublicMembers { get; set; }
        public List<MemberCoverage> UncoveredMembers { get; set; } = new();

        // Complexity information
        public int CyclomaticComplexity { get; set; }
        public int MaxMethodComplexity { get; set; }

        // Coverage percentage
        public double CoveragePercentage => TotalPublicMembers > 0
            ? (TestedPublicMembers * 100.0 / TotalPublicMembers)
            : 0;

        // Risk assessment
        public string RiskLevel { get; set; } = string.Empty; // Critical, High, Medium, Low
    }

    /// <summary>
    /// Represents a member (method/property) and its test coverage
    /// </summary>
    public class MemberCoverage
    {
        public string MemberName { get; set; } = string.Empty;
        public string MemberKind { get; set; } = string.Empty; // Method, Property, Event
        public string Signature { get; set; } = string.Empty;
        public string DeclaringType { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public bool HasTest { get; set; }
        public List<string> TestMethods { get; set; } = new();
        public int CyclomaticComplexity { get; set; }
        public bool IsPublic { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsAbstract { get; set; }
    }

    /// <summary>
    /// Represents coverage statistics for a project or namespace
    /// </summary>
    public class CoverageStatistics
    {
        public string Name { get; set; } = string.Empty; // Project or namespace name
        public int TotalTypes { get; set; }
        public int TestedTypes { get; set; }
        public int UncoveredTypes { get; set; }
        public int TotalPublicMembers { get; set; }
        public int TestedPublicMembers { get; set; }
        public int UncoveredPublicMembers { get; set; }
        public double TypeCoveragePercentage => TotalTypes > 0 ? (TestedTypes * 100.0 / TotalTypes) : 0;
        public double MemberCoveragePercentage => TotalPublicMembers > 0 ? (TestedPublicMembers * 100.0 / TotalPublicMembers) : 0;
    }

    /// <summary>
    /// Wrapper for test coverage analysis results
    /// </summary>
    public class TestCoverageResults
    {
        public List<TypeCoverage> TypeCoverages { get; set; } = new();
        public Dictionary<string, CoverageStatistics> ProjectStatistics { get; set; } = new();
        public Dictionary<string, CoverageStatistics> NamespaceStatistics { get; set; } = new();

        public int AnalyzedProjects { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Overall statistics
        public int TotalTypes { get; set; }
        public int TestedTypes { get; set; }
        public int UncoveredTypes => TotalTypes - TestedTypes;
        public int TotalPublicMembers { get; set; }
        public int TestedPublicMembers { get; set; }
        public int UncoveredPublicMembers => TotalPublicMembers - TestedPublicMembers;

        // Coverage percentages
        public double OverallTypeCoverage => TotalTypes > 0 ? (TestedTypes * 100.0 / TotalTypes) : 0;
        public double OverallMemberCoverage => TotalPublicMembers > 0 ? (TestedPublicMembers * 100.0 / TotalPublicMembers) : 0;

        // Risk analysis
        public int CriticalRiskTypes { get; set; } // High complexity, no tests
        public int HighRiskTypes { get; set; }     // Medium complexity, no tests
        public int MediumRiskTypes { get; set; }   // Low complexity, no tests or High complexity, partial tests
        public int LowRiskTypes { get; set; }      // Fully tested
    }

    /// <summary>
    /// Represents a symbol that is impacted by a change
    /// </summary>
    public class ImpactedSymbol
    {
        public string SymbolName { get; set; } = string.Empty;
        public string FullSymbolName { get; set; } = string.Empty;
        public string SymbolKind { get; set; } = string.Empty; // Class, Method, Property, etc.
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string ReferenceKind { get; set; } = string.Empty; // Direct, Indirect
        public int Distance { get; set; } // Distance from changed symbol (0 = direct reference)
        public string ImpactType { get; set; } = string.Empty; // Usage, Inheritance, Implementation
        public string CodeContext { get; set; } = string.Empty; // Code snippet showing usage
    }

    /// <summary>
    /// Represents a dependency chain from the changed symbol to an impacted symbol
    /// </summary>
    public class DependencyChain
    {
        public List<string> Chain { get; set; } = new(); // Symbol names in the chain
        public int ChainLength => Chain.Count;
        public string StartSymbol => Chain.FirstOrDefault() ?? string.Empty;
        public string EndSymbol => Chain.LastOrDefault() ?? string.Empty;
        public List<string> ProjectsInvolved { get; set; } = new();
        public bool CrossesProjectBoundary => ProjectsInvolved.Distinct().Count() > 1;
    }

    /// <summary>
    /// Represents the impact of changing a symbol
    /// </summary>
    public class ChangeImpactResults
    {
        public string TargetSymbol { get; set; } = string.Empty;
        public string TargetSymbolFullName { get; set; } = string.Empty;
        public string SymbolKind { get; set; } = string.Empty;
        public string Accessibility { get; set; } = string.Empty;
        public string DeclaringType { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }

        // Impact statistics
        public int TotalImpactedSymbols { get; set; }
        public int DirectReferences { get; set; }
        public int IndirectReferences { get; set; }
        public int ImpactedProjects { get; set; }
        public int ImpactedFiles { get; set; }
        public List<string> ImpactedProjectNames { get; set; } = new();

        // Impacted symbols
        public List<ImpactedSymbol> ImpactedSymbols { get; set; } = new();

        // Dependency chains
        public List<DependencyChain> DependencyChains { get; set; } = new();

        // Risk assessment
        public string RiskLevel { get; set; } = string.Empty; // Critical, High, Medium, Low
        public string RiskReason { get; set; } = string.Empty;
        public bool IsPublicAPI { get; set; }
        public bool IsBreakingChange { get; set; }
        public List<string> BreakingChangeReasons { get; set; } = new();

        // Recommendations
        public List<string> Recommendations { get; set; } = new();

        // Warnings
        public List<OperationWarning> Warnings { get; set; } = new();

        // Impact by project
        public Dictionary<string, int> ImpactByProject { get; set; } = new();

        // Impact by type
        public Dictionary<string, int> ImpactByKind { get; set; } = new();
    }

    /// <summary>
    /// Represents a performance issue found in code
    /// </summary>
    public class PerformanceIssue
    {
        public string IssueType { get; set; } = string.Empty; // LinqMisuse, StringConcatenation, Boxing, etc.
        public string Severity { get; set; } = string.Empty; // Critical, High, Medium, Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string MethodName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FixExample { get; set; } = string.Empty; // Example of how to fix
        public double EstimatedImpact { get; set; } // Performance impact score (1-10)
    }

    /// <summary>
    /// Wrapper for performance issue analysis results
    /// </summary>
    public class PerformanceIssueResults
    {
        public List<PerformanceIssue> Issues { get; set; } = new();

        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics
        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == "Critical");
        public int HighIssues => Issues.Count(i => i.Severity == "High");
        public int MediumIssues => Issues.Count(i => i.Severity == "Medium");
        public int LowIssues => Issues.Count(i => i.Severity == "Low");

        // Issues by type
        public Dictionary<string, int> IssuesByType { get; set; } = new();

        // Issues by project
        public Dictionary<string, int> IssuesByProject { get; set; } = new();

        // Issues by file
        public Dictionary<string, int> IssuesByFile { get; set; } = new();
    }

    /// <summary>
    /// Represents a naming convention violation
    /// </summary>
    public class NamingViolation
    {
        public string SymbolName { get; set; } = string.Empty;
        public string SymbolKind { get; set; } = string.Empty; // Class, Interface, Method, Field, etc.
        public string ViolationType { get; set; } = string.Empty; // InterfaceNaming, Casing, PrivateFieldNaming, etc.
        public string Severity { get; set; } = string.Empty; // High, Medium, Low
        public string CurrentName { get; set; } = string.Empty;
        public string SuggestedName { get; set; } = string.Empty;
        public string ExpectedConvention { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Accessibility { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string DeclaringType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Results from naming convention analysis
    /// </summary>
    public class NamingConventionResults
    {
        public List<NamingViolation> Violations { get; set; } = new();

        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int AnalyzedSymbols { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics
        public int TotalViolations => Violations.Count;
        public int HighSeverityViolations => Violations.Count(v => v.Severity == "High");
        public int MediumSeverityViolations => Violations.Count(v => v.Severity == "Medium");
        public int LowSeverityViolations => Violations.Count(v => v.Severity == "Low");

        // Violations by type
        public Dictionary<string, int> ViolationsByType { get; set; } = new();

        // Violations by symbol kind
        public Dictionary<string, int> ViolationsBySymbolKind { get; set; } = new();

        // Violations by project
        public Dictionary<string, int> ViolationsByProject { get; set; } = new();

        // Violations by file
        public Dictionary<string, int> ViolationsByFile { get; set; } = new();

        // Compliance score (percentage of symbols following conventions)
        public double ComplianceScore => AnalyzedSymbols > 0
            ? ((AnalyzedSymbols - TotalViolations) * 100.0 / AnalyzedSymbols)
            : 100.0;
    }

    /// <summary>
    /// Represents an API change between two versions
    /// </summary>
    public class APIChange
    {
        public string SymbolName { get; set; } = string.Empty;
        public string FullSymbolName { get; set; } = string.Empty;
        public string SymbolKind { get; set; } = string.Empty; // Type, Method, Property, Field, Event
        public string ChangeType { get; set; } = string.Empty; // Added, Removed, Modified, AccessibilityChanged, SignatureChanged
        public string ImpactLevel { get; set; } = string.Empty; // Breaking, NonBreaking, Internal
        public string Severity { get; set; } = string.Empty; // Critical, High, Medium, Low
        public string Description { get; set; } = string.Empty;
        public string OldVersion { get; set; } = string.Empty;
        public string NewVersion { get; set; } = string.Empty;
        public string OldSignature { get; set; } = string.Empty;
        public string NewSignature { get; set; } = string.Empty;
        public string OldAccessibility { get; set; } = string.Empty;
        public string NewAccessibility { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string DeclaringType { get; set; } = string.Empty;
        public string MigrationGuidance { get; set; } = string.Empty;
        public List<string> AffectedAreas { get; set; } = new();
    }

    /// <summary>
    /// Results from API change analysis
    /// </summary>
    public class APIChangeResults
    {
        public List<APIChange> Changes { get; set; } = new();

        public string OldVersionPath { get; set; } = string.Empty;
        public string NewVersionPath { get; set; } = string.Empty;
        public string OldVersionLabel { get; set; } = string.Empty;
        public string NewVersionLabel { get; set; } = string.Empty;

        public int AnalyzedProjects { get; set; }
        public int AnalyzedOldSymbols { get; set; }
        public int AnalyzedNewSymbols { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics
        public int TotalChanges => Changes.Count;
        public int BreakingChanges => Changes.Count(c => c.ImpactLevel == "Breaking");
        public int NonBreakingChanges => Changes.Count(c => c.ImpactLevel == "NonBreaking");
        public int InternalChanges => Changes.Count(c => c.ImpactLevel == "Internal");

        public int AddedSymbols => Changes.Count(c => c.ChangeType == "Added");
        public int RemovedSymbols => Changes.Count(c => c.ChangeType == "Removed");
        public int ModifiedSymbols => Changes.Count(c => c.ChangeType == "Modified" ||
                                                          c.ChangeType == "AccessibilityChanged" ||
                                                          c.ChangeType == "SignatureChanged");

        public int CriticalChanges => Changes.Count(c => c.Severity == "Critical");
        public int HighSeverityChanges => Changes.Count(c => c.Severity == "High");
        public int MediumSeverityChanges => Changes.Count(c => c.Severity == "Medium");
        public int LowSeverityChanges => Changes.Count(c => c.Severity == "Low");

        // Changes by type
        public Dictionary<string, int> ChangesByType { get; set; } = new();

        // Changes by symbol kind
        public Dictionary<string, int> ChangesBySymbolKind { get; set; } = new();

        // Changes by namespace
        public Dictionary<string, int> ChangesByNamespace { get; set; } = new();

        // Semantic versioning recommendation
        public string RecommendedVersionBump { get; set; } = string.Empty; // Major, Minor, Patch
        public string VersioningReason { get; set; } = string.Empty;
    }

    // Phase 1 Tools Models (New)

    /// <summary>
    /// Represents a magic number or hardcoded literal in code
    /// </summary>
    public class MagicNumber
    {
        public string Value { get; set; } = string.Empty;  // The literal value
        public string Type { get; set; } = string.Empty;  // Number, String, Boolean
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string ContainingMember { get; set; } = string.Empty;  // Method or property name
        public string ContainingType { get; set; } = string.Empty;  // Class name
        public string CodeContext { get; set; } = string.Empty;  // Line of code
        public string SuggestedConstantName { get; set; } = string.Empty;  // Suggested name
        public string Reason { get; set; } = string.Empty;  // Why it's considered magic
    }

    /// <summary>
    /// Results from magic number analysis
    /// </summary>
    public class MagicNumberResults
    {
        public List<MagicNumber> MagicNumbers { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by type
        public int NumericLiterals { get; set; }
        public int StringLiterals { get; set; }
        public int BooleanLiterals { get; set; }

        // Statistics by severity
        public int HighPriority { get; set; }  // Used in multiple places
        public int MediumPriority { get; set; }  // Used once but in important logic
        public int LowPriority { get; set; }  // Simple cases

        public int TotalMagicNumbers => MagicNumbers.Count;
    }

    /// <summary>
    /// Represents a code smell detected in the codebase
    /// </summary>
    public class CodeSmell
    {
        public string SmellType { get; set; } = string.Empty;  // LongMethod, LargeClass, etc.
        public string Severity { get; set; } = string.Empty;  // High, Medium, Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;  // Method, class, etc.
        public string SymbolKind { get; set; } = string.Empty;  // Method, Class, Property
        public Dictionary<string, object> Metrics { get; set; } = new();  // Specific metrics
        public string CodeSnippet { get; set; } = string.Empty;
    }

    /// <summary>
    /// Results from code smell analysis
    /// </summary>
    public class CodeSmellResults
    {
        public List<CodeSmell> Smells { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int AnalyzedSymbols { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by severity
        public int HighSeverity { get; set; }
        public int MediumSeverity { get; set; }
        public int LowSeverity { get; set; }

        // Statistics by smell type
        public Dictionary<string, int> SmellsByType { get; set; } = new();

        public int TotalSmells => Smells.Count;
    }

    /// <summary>
    /// Represents an architecture layer definition
    /// </summary>
    public class LayerDefinition
    {
        public string Name { get; set; } = string.Empty;  // Presentation, Application, etc.
        public List<string> ProjectPatterns { get; set; } = new();  // *.Web, *.API
        public List<string> MatchedProjects { get; set; } = new();  // Actual project names
    }

    /// <summary>
    /// Represents an architecture dependency rule
    /// </summary>
    public class LayerRule
    {
        public string FromLayer { get; set; } = string.Empty;
        public string ToLayer { get; set; } = string.Empty;
        public bool Allowed { get; set; }
    }

    /// <summary>
    /// Represents a violation of architecture rules
    /// </summary>
    public class LayerViolation
    {
        public string ViolationType { get; set; } = string.Empty;  // DirectDependency, CircularDependency
        public string Severity { get; set; } = string.Empty;  // Critical, High, Medium
        public string FromLayer { get; set; } = string.Empty;
        public string ToLayer { get; set; } = string.Empty;
        public string FromProject { get; set; } = string.Empty;
        public string ToProject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> ViolatingReferences { get; set; } = new();  // Specific type/namespace references
        public string Recommendation { get; set; } = string.Empty;
    }

    /// <summary>
    /// Results from layer violation analysis
    /// </summary>
    public class LayerViolationResults
    {
        public List<LayerViolation> Violations { get; set; } = new();
        public List<LayerDefinition> Layers { get; set; } = new();
        public List<LayerRule> Rules { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics
        public int TotalViolations => Violations.Count;
        public int CriticalViolations { get; set; }
        public int HighSeverityViolations { get; set; }
        public int MediumSeverityViolations { get; set; }

        // Violations by type
        public Dictionary<string, int> ViolationsByType { get; set; } = new();

        // Compliance score
        public double ComplianceScore { get; set; }  // Percentage of rules followed
    }

    /// <summary>
    /// Represents a symbol that will be renamed
    /// </summary>
    public class RenameTarget
    {
        public string CurrentName { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string SymbolKind { get; set; } = string.Empty;  // Class, Method, Property, etc.
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string ProjectName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a location where a symbol needs to be renamed
    /// </summary>
    public class RenameLocation
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public string OldText { get; set; } = string.Empty;
        public string NewText { get; set; } = string.Empty;
        public string LineContext { get; set; } = string.Empty;  // The full line
        public bool IsDefinition { get; set; }
    }

    /// <summary>
    /// Represents a file change needed for rename
    /// </summary>
    public class FileRenameChange
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int ChangeCount { get; set; }
        public List<RenameLocation> Locations { get; set; } = new();
    }

    /// <summary>
    /// Results from rename symbol operation
    /// </summary>
    public class RenameSymbolResults
    {
        public RenameTarget Target { get; set; } = new();
        public List<FileRenameChange> FileChanges { get; set; } = new();
        public bool IsPreview { get; set; }  // True if preview mode, false if executed
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        // Statistics
        public int TotalLocations { get; set; }
        public int FilesAffected { get; set; }
        public int ProjectsAffected { get; set; }

        // Validation
        public List<string> Conflicts { get; set; } = new();  // Name conflicts detected
        public List<string> Warnings { get; set; } = new();  // Warnings about the rename
        public bool HasConflicts => Conflicts.Any();

        // Risk assessment
        public string RiskLevel { get; set; } = string.Empty;  // Low, Medium, High, Critical
        public string RiskReason { get; set; } = string.Empty;
    }

    // ============================================================================
    // Phase 2 Models - Advanced Refactoring and .NET-Specific Analysis
    // ============================================================================

    #region ExtractInterface

    /// <summary>
    /// Represents a member to be extracted into an interface
    /// </summary>
    public class ExtractableMember
    {
        public string Name { get; set; } = string.Empty;
        public string MemberType { get; set; } = string.Empty;  // Method, Property, Event
        public string ReturnType { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;  // Full signature
        public string Documentation { get; set; } = string.Empty;  // XML doc comments
        public bool IsSelected { get; set; } = true;  // Include in interface by default
    }

    /// <summary>
    /// Results from interface extraction operation
    /// </summary>
    public class InterfaceExtractionResult
    {
        public string ClassName { get; set; } = string.Empty;
        public string InterfaceName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string InterfaceCode { get; set; } = string.Empty;  // Generated interface code
        public List<ExtractableMember> Members { get; set; } = new();
        public string SuggestedFilePath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new();

        // Statistics
        public int TotalMembers => Members.Count;
        public int SelectedMembers => Members.Count(m => m.IsSelected);
    }

    #endregion

    #region AnalyzeDIContainer

    /// <summary>
    /// Represents a dependency injection issue
    /// </summary>
    public class DIContainerIssue
    {
        public string IssueType { get; set; } = string.Empty;  // Unregistered, LifetimeMismatch, Circular, Captive
        public string Severity { get; set; } = string.Empty;  // Critical, High, Medium, Low
        public string ServiceType { get; set; } = string.Empty;
        public string ImplementationType { get; set; } = string.Empty;
        public string ServiceLifetime { get; set; } = string.Empty;  // Singleton, Scoped, Transient
        public string Description { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public List<string> DependencyChain { get; set; } = new();  // For circular dependencies
    }

    /// <summary>
    /// Results from DI container analysis
    /// </summary>
    public class DIContainerResults
    {
        public List<DIContainerIssue> Issues { get; set; } = new();
        public int AnalyzedServices { get; set; }
        public int AnalyzedConstructors { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by issue type
        public int UnregisteredCount { get; set; }
        public int LifetimeMismatchCount { get; set; }
        public int CircularDependencyCount { get; set; }
        public int CaptiveDependencyCount { get; set; }

        // Statistics by severity
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }

        public int TotalIssues => Issues.Count;
    }

    #endregion

    #region AnalyzeExceptionHandling

    /// <summary>
    /// Represents an exception handling anti-pattern or issue
    /// </summary>
    public class ExceptionHandlingIssue
    {
        public string IssueType { get; set; } = string.Empty;  // EmptyCatch, SwallowedException, GenericCatch, MissingUsing
        public string Severity { get; set; } = string.Empty;  // High, Medium, Low
        public string Description { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string CodeSnippet { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;  // Exception type being caught
        public string ProjectName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Results from exception handling analysis
    /// </summary>
    public class ExceptionHandlingResults
    {
        public List<ExceptionHandlingIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedTryBlocks { get; set; }
        public int AnalyzedMethods { get; set; }
        public int TotalTryBlocks { get; set; }
        public int TotalCatchBlocks { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by issue type
        public int EmptyCatchCount { get; set; }
        public int SwallowedExceptionCount { get; set; }
        public int GenericCatchCount { get; set; }
        public int GenericExceptionCount { get; set; }  // Alias for GenericCatchCount
        public int MissingUsingCount { get; set; }

        // Statistics by severity
        public int HighSeverityCount { get; set; }
        public int MediumSeverityCount { get; set; }
        public int LowSeverityCount { get; set; }
        public int HighCount { get; set; }    // Alias for HighSeverityCount
        public int MediumCount { get; set; }  // Alias for MediumSeverityCount
        public int LowCount { get; set; }     // Alias for LowSeverityCount

        public int TotalIssues => Issues.Count;
    }

    #endregion

    #region FindThreadSafetyIssues

    /// <summary>
    /// Represents a thread safety issue or potential race condition
    /// </summary>
    public class ThreadSafetyIssue
    {
        public string IssueType { get; set; } = string.Empty;  // MutableStatic, UnsynchronizedAccess, UnsafeCollection, DoubleCheckLocking
        public string Severity { get; set; } = string.Empty;  // Critical, High, Medium, Low
        public string Description { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;  // Field, property, or method name
        public string MemberType { get; set; } = string.Empty;  // Field, Property, Method
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string CodeSnippet { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public bool IsStaticMember { get; set; }
    }

    /// <summary>
    /// Results from thread safety analysis
    /// </summary>
    public class ThreadSafetyResults
    {
        public List<ThreadSafetyIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int AnalyzedTypes { get; set; }
        public int AnalyzedMembers { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        // Statistics by issue type
        public int MutableStaticCount { get; set; }
        public int UnsynchronizedAccessCount { get; set; }
        public int UnsafeCollectionCount { get; set; }
        public int DoubleCheckLockingCount { get; set; }
        public int AsyncDeadlockCount { get; set; }
        public int SharedStateCount { get; set; }
        public int DoubleLockingCount { get; set; }

        // Statistics by severity
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }

        public int TotalIssues => Issues.Count;
    }

    #endregion

    #region AOT Compatibility Analysis

    /// <summary>
    /// Represents a single Native AOT or trimming compatibility issue.
    /// </summary>
    public class AotIssue
    {
        public string Category { get; set; } = string.Empty;     // Reflection|JsonSerialization|GeneratedRegex|TrimAnnotation
        public string Severity { get; set; } = string.Empty;     // Critical|High|Medium|Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;   // API name (e.g. Type.GetType)
        public string CodeSnippet { get; set; } = string.Empty;  // Max 100 chars
        public string Recommendation { get; set; } = string.Empty;
        public string FixExample { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated results from AnalyzeAotCompatibility.
    /// </summary>
    public class AotCompatibilityResults
    {
        public List<AotIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == "Critical");
        public int HighIssues => Issues.Count(i => i.Severity == "High");
        public int MediumIssues => Issues.Count(i => i.Severity == "Medium");
        public int LowIssues => Issues.Count(i => i.Severity == "Low");

        public Dictionary<string, int> IssuesByCategory { get; set; } = new();
        public Dictionary<string, int> IssuesByProject { get; set; } = new();
    }

    #endregion

    #region PInvoke Compatibility Analysis

    /// <summary>
    /// Represents a single P/Invoke or native interop compatibility issue.
    /// </summary>
    public class PInvokeIssue
    {
        public string IssueType { get; set; } = string.Empty;   // DllImportMigration|MarshalUnsafety|MissingMarshalAs|SetLastError|CallingConvention|ResourceLeak|ModernizeTypes|MagicOffset|NativeLibrary
        public string Severity { get; set; } = string.Empty;    // Critical|High|Medium|Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FixExample { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated results from AnalyzePInvoke.
    /// </summary>
    public class PInvokeAnalysisResults
    {
        public List<PInvokeIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == "Critical");
        public int HighIssues => Issues.Count(i => i.Severity == "High");
        public int MediumIssues => Issues.Count(i => i.Severity == "Medium");
        public int LowIssues => Issues.Count(i => i.Severity == "Low");

        public Dictionary<string, int> IssuesByType { get; set; } = new();
        public Dictionary<string, int> IssuesByProject { get; set; } = new();
    }

    #endregion

    #region Source Generator Opportunity Analysis

    /// <summary>
    /// Represents a single source generator migration opportunity.
    /// </summary>
    public class SourceGeneratorOpportunity
    {
        public string Category { get; set; } = string.Empty;    // GeneratedRegex|JsonSerializable|ObservableProperty|RelayCommand|LoggerMessage
        public string Severity { get; set; } = string.Empty;    // High|Medium|Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FixExample { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated results from FindSourceGeneratorOpportunities.
    /// </summary>
    public class SourceGeneratorAnalysisResults
    {
        public List<SourceGeneratorOpportunity> Opportunities { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalOpportunities => Opportunities.Count;
        public int HighOpportunities => Opportunities.Count(o => o.Severity == "High");
        public int MediumOpportunities => Opportunities.Count(o => o.Severity == "Medium");
        public int LowOpportunities => Opportunities.Count(o => o.Severity == "Low");

        public Dictionary<string, int> OpportunitiesByCategory { get; set; } = new();
        public Dictionary<string, int> OpportunitiesByProject { get; set; } = new();
    }

    #endregion

    #region Unsafe Code Analysis

    /// <summary>
    /// Represents a single unsafe code risk finding.
    /// </summary>
    public class UnsafeCodeIssue
    {
        public string IssueType { get; set; } = string.Empty;   // StackAllocSize|StackAllocNoSpan|StackAllocInLoop|PointerArithmetic|VoidPointerCast|CrossThreadPointer|OversizedFixed|UnsafeAsync
        public string Severity { get; set; } = string.Empty;    // Critical|High|Medium|Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FixExample { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated results from AnalyzeUnsafeCode.
    /// </summary>
    public class UnsafeCodeAnalysisResults
    {
        public List<UnsafeCodeIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == "Critical");
        public int HighIssues => Issues.Count(i => i.Severity == "High");
        public int MediumIssues => Issues.Count(i => i.Severity == "Medium");
        public int LowIssues => Issues.Count(i => i.Severity == "Low");

        public Dictionary<string, int> IssuesByType { get; set; } = new();
        public Dictionary<string, int> IssuesByProject { get; set; } = new();
    }

    #endregion

    #region Concurrency Pattern Analysis

    /// <summary>
    /// Represents a single concurrency anti-pattern finding.
    /// </summary>
    public class ConcurrencyIssue
    {
        public string IssueType { get; set; } = string.Empty;   // ConfigureAwait|TimerDispose|UnboundedConcurrency|MissingCancellationToken|CancellationNotPropagated|NonThreadSafeCollection|StaticFieldLock|AwaitInLock
        public string Severity { get; set; } = string.Empty;    // Critical|High|Medium|Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FixExample { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated results from AnalyzeConcurrencyPatterns.
    /// </summary>
    public class ConcurrencyAnalysisResults
    {
        public List<ConcurrencyIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == "Critical");
        public int HighIssues => Issues.Count(i => i.Severity == "High");
        public int MediumIssues => Issues.Count(i => i.Severity == "Medium");
        public int LowIssues => Issues.Count(i => i.Severity == "Low");

        public Dictionary<string, int> IssuesByType { get; set; } = new();
        public Dictionary<string, int> IssuesByProject { get; set; } = new();
    }

    #endregion

    #region Integrity Pattern Analysis

    /// <summary>
    /// Represents a single integrity verification or protection pattern finding.
    /// </summary>
    public class IntegrityPatternIssue
    {
        public string IssueType { get; set; } = string.Empty;   // Sha256Sentinel|XorStringProtection|HardcodedChecksum|AntiDebugPattern|SentinelMagicBytes
        public string Severity { get; set; } = string.Empty;    // Critical|High|Medium|Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated results from AnalyzeIntegrityPatterns.
    /// </summary>
    public class IntegrityAnalysisResults
    {
        public List<IntegrityPatternIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == "Critical");
        public int HighIssues => Issues.Count(i => i.Severity == "High");
        public int MediumIssues => Issues.Count(i => i.Severity == "Medium");
        public int LowIssues => Issues.Count(i => i.Severity == "Low");

        public Dictionary<string, int> IssuesByType { get; set; } = new();
        public Dictionary<string, int> IssuesByProject { get; set; } = new();
    }

    #endregion

    #region Memory Allocation Analysis

    /// <summary>
    /// Represents a single memory allocation hotspot or inefficiency.
    /// </summary>
    public class MemoryAllocationIssue
    {
        public string IssueType { get; set; } = string.Empty;   // ArrayPoolOpportunity|Boxing|SubstringSpan|ReadOnlySpan|StringBuilderOpportunity
        public string Severity { get; set; } = string.Empty;    // Critical|High|Medium|Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FixExample { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated results from AnalyzeMemoryAllocation.
    /// </summary>
    public class MemoryAllocationResults
    {
        public List<MemoryAllocationIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == "Critical");
        public int HighIssues => Issues.Count(i => i.Severity == "High");
        public int MediumIssues => Issues.Count(i => i.Severity == "Medium");
        public int LowIssues => Issues.Count(i => i.Severity == "Low");

        public Dictionary<string, int> IssuesByType { get; set; } = new();
        public Dictionary<string, int> IssuesByProject { get; set; } = new();
    }

    #endregion

    #region IPC Pattern Analysis

    /// <summary>
    /// Represents a single IPC (Inter-Process Communication) pattern finding.
    /// </summary>
    public class IpcPatternIssue
    {
        public string IssueType { get; set; } = string.Empty;   // NamedPipeUsage|JsonRpcPattern|IpcErrorHandling|UnbufferedPipe|SynchronousPipeIo|MissingPipeTimeout|JsonRpcMissingErrorHandling|HardcodedPipeName
        public string Severity { get; set; } = string.Empty;    // Critical|High|Medium|Low
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string SymbolName { get; set; } = string.Empty;
        public string CodeSnippet { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string FixExample { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated results from AnalyzeIpcPatterns.
    /// </summary>
    public class IpcAnalysisResults
    {
        public List<IpcPatternIssue> Issues { get; set; } = new();
        public int AnalyzedProjects { get; set; }
        public int AnalyzedFiles { get; set; }
        public int FailedProjects { get; set; }
        public List<OperationWarning> Warnings { get; set; } = new();

        public int TotalIssues => Issues.Count;
        public int CriticalIssues => Issues.Count(i => i.Severity == "Critical");
        public int HighIssues => Issues.Count(i => i.Severity == "High");
        public int MediumIssues => Issues.Count(i => i.Severity == "Medium");
        public int LowIssues => Issues.Count(i => i.Severity == "Low");

        public Dictionary<string, int> IssuesByType { get; set; } = new();
        public Dictionary<string, int> IssuesByProject { get; set; } = new();
    }

    #endregion
}