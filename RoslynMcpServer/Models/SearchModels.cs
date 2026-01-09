using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Models
{
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

    public class ComplexityResult
    {
        public string MethodName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int Complexity { get; set; }
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
}