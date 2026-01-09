using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;

namespace RoslynMcpServer.Services
{
    public class FileAnalysisService
    {
        private readonly ILogger<FileAnalysisService> _logger;
        private readonly SecurityValidator _securityValidator;

        public FileAnalysisService(ILogger<FileAnalysisService> logger, SecurityValidator securityValidator)
        {
            _logger = logger;
            _securityValidator = securityValidator;
        }

        /// <summary>
        /// Get structural outline of a C# file without reading full implementation details
        /// </summary>
        /// <param name="filePath">Path to .cs file</param>
        /// <returns>File outline with types and members</returns>
        public async Task<FileOutlineResult> GetFileOutlineAsync(string filePath)
        {
            _logger.LogInformation("Getting file outline for: {FilePath}", filePath);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("File must be a C# source file (.cs)", nameof(filePath));
            }

            // Read file content
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var lines = sourceCode.Split('\n');

            // Parse syntax tree
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = await syntaxTree.GetRootAsync();

            // Calculate line statistics
            var (codeLines, commentLines, blankLines) = CalculateLineStatistics(lines);

            // Extract namespaces
            var namespaces = root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Select(ns => ns.Name.ToString())
                .Distinct()
                .ToList();

            // Extract using statements
            var usingStatements = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.Name?.ToString() ?? u.ToString().Replace("using ", "").TrimEnd(';'))
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            // Extract types
            var types = new List<TypeOutline>();
            var typeDeclarations = root.DescendantNodes()
                .Where(n => n is ClassDeclarationSyntax ||
                           n is InterfaceDeclarationSyntax ||
                           n is StructDeclarationSyntax ||
                           n is EnumDeclarationSyntax ||
                           n is RecordDeclarationSyntax);

            foreach (var typeDecl in typeDeclarations)
            {
                var typeOutline = ExtractTypeOutline(typeDecl);
                if (typeOutline != null)
                {
                    types.Add(typeOutline);
                }
            }

            var result = new FileOutlineResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                TotalLines = lines.Length,
                CodeLines = codeLines,
                CommentLines = commentLines,
                BlankLines = blankLines,
                Namespaces = namespaces,
                UsingStatements = usingStatements,
                Types = types
            };

            _logger.LogInformation("File outline extracted: {TypeCount} types, {TotalLines} lines",
                types.Count, lines.Length);

            return result;
        }

        /// <summary>
        /// Calculate line statistics (code, comments, blank)
        /// </summary>
        private (int codeLines, int commentLines, int blankLines) CalculateLineStatistics(string[] lines)
        {
            int codeLines = 0;
            int commentLines = 0;
            int blankLines = 0;
            bool inMultiLineComment = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    blankLines++;
                }
                else if (inMultiLineComment)
                {
                    commentLines++;
                    if (trimmed.Contains("*/"))
                    {
                        inMultiLineComment = false;
                    }
                }
                else if (trimmed.StartsWith("//"))
                {
                    commentLines++;
                }
                else if (trimmed.StartsWith("/*"))
                {
                    commentLines++;
                    if (!trimmed.Contains("*/"))
                    {
                        inMultiLineComment = true;
                    }
                }
                else
                {
                    codeLines++;
                }
            }

            return (codeLines, commentLines, blankLines);
        }

        /// <summary>
        /// Extract type outline from type declaration syntax
        /// </summary>
        private TypeOutline? ExtractTypeOutline(SyntaxNode typeDecl)
        {
            try
            {
                string name = string.Empty;
                string kind = string.Empty;
                string accessibility = string.Empty;
                BaseListSyntax? baseList = null;
                SyntaxList<AttributeListSyntax> attributeLists = default;
                SyntaxNode? parent = typeDecl.Parent;

                // Extract common properties based on type
                switch (typeDecl)
                {
                    case ClassDeclarationSyntax classDecl:
                        name = classDecl.Identifier.Text;
                        kind = "Class";
                        accessibility = GetAccessibility(classDecl.Modifiers);
                        baseList = classDecl.BaseList;
                        attributeLists = classDecl.AttributeLists;
                        break;
                    case InterfaceDeclarationSyntax interfaceDecl:
                        name = interfaceDecl.Identifier.Text;
                        kind = "Interface";
                        accessibility = GetAccessibility(interfaceDecl.Modifiers);
                        baseList = interfaceDecl.BaseList;
                        attributeLists = interfaceDecl.AttributeLists;
                        break;
                    case StructDeclarationSyntax structDecl:
                        name = structDecl.Identifier.Text;
                        kind = "Struct";
                        accessibility = GetAccessibility(structDecl.Modifiers);
                        baseList = structDecl.BaseList;
                        attributeLists = structDecl.AttributeLists;
                        break;
                    case EnumDeclarationSyntax enumDecl:
                        name = enumDecl.Identifier.Text;
                        kind = "Enum";
                        accessibility = GetAccessibility(enumDecl.Modifiers);
                        baseList = enumDecl.BaseList;
                        attributeLists = enumDecl.AttributeLists;
                        break;
                    case RecordDeclarationSyntax recordDecl:
                        name = recordDecl.Identifier.Text;
                        kind = recordDecl.ClassOrStructKeyword.Text == "struct" ? "Record Struct" : "Record";
                        accessibility = GetAccessibility(recordDecl.Modifiers);
                        baseList = recordDecl.BaseList;
                        attributeLists = recordDecl.AttributeLists;
                        break;
                }

                // Get namespace
                var namespaceName = GetContainingNamespace(typeDecl);

                // Get line number
                var lineSpan = typeDecl.GetLocation().GetLineSpan();
                var lineNumber = lineSpan.StartLinePosition.Line + 1;

                // Get base types and interfaces
                var baseTypes = new List<string>();
                if (baseList != null)
                {
                    baseTypes = baseList.Types
                        .Select(t => t.Type.ToString())
                        .ToList();
                }

                // Get attributes
                var attributes = attributeLists
                    .SelectMany(al => al.Attributes)
                    .Select(a => a.Name.ToString())
                    .ToList();

                // Get documentation
                var documentation = ExtractDocumentation(typeDecl);

                // Extract members
                var members = ExtractMembers(typeDecl);

                return new TypeOutline
                {
                    Name = name,
                    FullName = string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}",
                    Kind = kind,
                    Accessibility = accessibility,
                    Namespace = namespaceName,
                    LineNumber = lineNumber,
                    BaseTypes = baseTypes,
                    Attributes = attributes,
                    Documentation = documentation,
                    Members = members
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting type outline");
                return null;
            }
        }

        /// <summary>
        /// Extract members from type declaration
        /// </summary>
        private List<MemberOutline> ExtractMembers(SyntaxNode typeDecl)
        {
            var members = new List<MemberOutline>();

            var memberNodes = typeDecl.ChildNodes()
                .Where(n => n is MethodDeclarationSyntax ||
                           n is PropertyDeclarationSyntax ||
                           n is FieldDeclarationSyntax ||
                           n is EventDeclarationSyntax ||
                           n is ConstructorDeclarationSyntax);

            foreach (var memberNode in memberNodes)
            {
                var memberOutline = ExtractMemberOutline(memberNode);
                if (memberOutline != null)
                {
                    members.Add(memberOutline);
                }
            }

            return members;
        }

        /// <summary>
        /// Extract member outline from member declaration syntax
        /// </summary>
        private MemberOutline? ExtractMemberOutline(SyntaxNode memberNode)
        {
            try
            {
                string name = string.Empty;
                string kind = string.Empty;
                string type = string.Empty;
                string accessibility = string.Empty;
                string signature = string.Empty;
                bool isStatic = false;
                bool isAsync = false;
                bool isAbstract = false;
                bool isVirtual = false;
                bool isOverride = false;
                SyntaxList<AttributeListSyntax> attributeLists = default;

                switch (memberNode)
                {
                    case MethodDeclarationSyntax method:
                        name = method.Identifier.Text;
                        kind = "Method";
                        type = method.ReturnType.ToString();
                        accessibility = GetAccessibility(method.Modifiers);
                        signature = GetMethodSignature(method);
                        isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);
                        isAsync = method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                        isAbstract = method.Modifiers.Any(SyntaxKind.AbstractKeyword);
                        isVirtual = method.Modifiers.Any(SyntaxKind.VirtualKeyword);
                        isOverride = method.Modifiers.Any(SyntaxKind.OverrideKeyword);
                        attributeLists = method.AttributeLists;
                        break;

                    case PropertyDeclarationSyntax property:
                        name = property.Identifier.Text;
                        kind = "Property";
                        type = property.Type.ToString();
                        accessibility = GetAccessibility(property.Modifiers);
                        signature = $"{type} {name} {{ {GetPropertyAccessors(property)} }}";
                        isStatic = property.Modifiers.Any(SyntaxKind.StaticKeyword);
                        isAbstract = property.Modifiers.Any(SyntaxKind.AbstractKeyword);
                        isVirtual = property.Modifiers.Any(SyntaxKind.VirtualKeyword);
                        isOverride = property.Modifiers.Any(SyntaxKind.OverrideKeyword);
                        attributeLists = property.AttributeLists;
                        break;

                    case FieldDeclarationSyntax field:
                        var variable = field.Declaration.Variables.FirstOrDefault();
                        if (variable != null)
                        {
                            name = variable.Identifier.Text;
                            kind = "Field";
                            type = field.Declaration.Type.ToString();
                            accessibility = GetAccessibility(field.Modifiers);
                            signature = $"{type} {name}";
                            isStatic = field.Modifiers.Any(SyntaxKind.StaticKeyword);
                            attributeLists = field.AttributeLists;
                        }
                        break;

                    case EventDeclarationSyntax eventDecl:
                        name = eventDecl.Identifier.Text;
                        kind = "Event";
                        type = eventDecl.Type.ToString();
                        accessibility = GetAccessibility(eventDecl.Modifiers);
                        signature = $"event {type} {name}";
                        isStatic = eventDecl.Modifiers.Any(SyntaxKind.StaticKeyword);
                        isAbstract = eventDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
                        isVirtual = eventDecl.Modifiers.Any(SyntaxKind.VirtualKeyword);
                        isOverride = eventDecl.Modifiers.Any(SyntaxKind.OverrideKeyword);
                        attributeLists = eventDecl.AttributeLists;
                        break;

                    case ConstructorDeclarationSyntax constructor:
                        name = constructor.Identifier.Text;
                        kind = "Constructor";
                        type = "void";
                        accessibility = GetAccessibility(constructor.Modifiers);
                        signature = GetConstructorSignature(constructor);
                        isStatic = constructor.Modifiers.Any(SyntaxKind.StaticKeyword);
                        attributeLists = constructor.AttributeLists;
                        break;
                }

                if (string.IsNullOrEmpty(name))
                {
                    return null;
                }

                var lineSpan = memberNode.GetLocation().GetLineSpan();
                var lineNumber = lineSpan.StartLinePosition.Line + 1;

                var attributes = attributeLists
                    .SelectMany(al => al.Attributes)
                    .Select(a => a.Name.ToString())
                    .ToList();

                var documentation = ExtractDocumentation(memberNode);

                return new MemberOutline
                {
                    Name = name,
                    Kind = kind,
                    Type = type,
                    Accessibility = accessibility,
                    Signature = signature,
                    LineNumber = lineNumber,
                    IsStatic = isStatic,
                    IsAsync = isAsync,
                    IsAbstract = isAbstract,
                    IsVirtual = isVirtual,
                    IsOverride = isOverride,
                    Attributes = attributes,
                    Documentation = documentation
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting member outline");
                return null;
            }
        }

        /// <summary>
        /// Get accessibility modifier as string
        /// </summary>
        private string GetAccessibility(SyntaxTokenList modifiers)
        {
            if (modifiers.Any(SyntaxKind.PublicKeyword))
                return "Public";
            if (modifiers.Any(SyntaxKind.PrivateKeyword))
                return "Private";
            if (modifiers.Any(SyntaxKind.ProtectedKeyword) && modifiers.Any(SyntaxKind.InternalKeyword))
                return "Protected Internal";
            if (modifiers.Any(SyntaxKind.ProtectedKeyword))
                return "Protected";
            if (modifiers.Any(SyntaxKind.InternalKeyword))
                return "Internal";
            return "Private"; // Default accessibility
        }

        /// <summary>
        /// Get containing namespace for a syntax node
        /// </summary>
        private string GetContainingNamespace(SyntaxNode node)
        {
            var namespaceDecl = node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();

            return namespaceDecl?.Name.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Extract XML documentation comment
        /// </summary>
        private string ExtractDocumentation(SyntaxNode node)
        {
            var trivia = node.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                           t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

            if (!trivia.Any())
            {
                return string.Empty;
            }

            var docComments = trivia.Select(t => t.ToString()).ToList();
            var fullDoc = string.Join("\n", docComments);

            // Extract summary from XML doc
            var summaryMatch = System.Text.RegularExpressions.Regex.Match(
                fullDoc, @"<summary>\s*(.*?)\s*</summary>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (summaryMatch.Success)
            {
                return summaryMatch.Groups[1].Value.Trim()
                    .Replace("///", "")
                    .Replace("///", "")
                    .Trim();
            }

            return string.Empty;
        }

        /// <summary>
        /// Get method signature with parameters
        /// </summary>
        private string GetMethodSignature(MethodDeclarationSyntax method)
        {
            var parameters = string.Join(", ", method.ParameterList.Parameters
                .Select(p => $"{p.Type} {p.Identifier.Text}"));

            return $"{method.ReturnType} {method.Identifier.Text}({parameters})";
        }

        /// <summary>
        /// Get constructor signature with parameters
        /// </summary>
        private string GetConstructorSignature(ConstructorDeclarationSyntax constructor)
        {
            var parameters = string.Join(", ", constructor.ParameterList.Parameters
                .Select(p => $"{p.Type} {p.Identifier.Text}"));

            return $"{constructor.Identifier.Text}({parameters})";
        }

        /// <summary>
        /// Get property accessors (get/set/init)
        /// </summary>
        private string GetPropertyAccessors(PropertyDeclarationSyntax property)
        {
            if (property.AccessorList == null)
            {
                return "get;"; // Expression-bodied property
            }

            var accessors = property.AccessorList.Accessors
                .Select(a => a.Keyword.Text)
                .ToList();

            return string.Join(" ", accessors) + ";";
        }
    }
}
