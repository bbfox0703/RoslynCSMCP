using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Syntax-level classification of a single reference location, used by the
    /// FindReferencesFiltered writesOnly and publicOnly filters.
    /// </summary>
    internal static class ReferenceSyntaxClassifier
    {
        /// <summary>
        /// Returns true when the name at <paramref name="node"/> is the storage being written:
        /// the target of a simple, compound, or ??= assignment (including object initializers and
        /// deconstruction targets), the operand of ++/--, or an out/ref argument.
        /// A read on the right-hand side of an assignment is not a write.
        /// </summary>
        public static bool IsWrittenTo(SyntaxNode? node)
        {
            if (node is not ExpressionSyntax expression)
                return false;

            // Climb from the referenced name to the expression that denotes the storage:
            // obj.Name, obj?.Name, (Name). Only climb when the node is the accessed member itself,
            // so the 'x' in 'x.Name = 1' is not treated as written.
            while (true)
            {
                var parent = expression.Parent;
                if (parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == expression)
                    expression = memberAccess;
                else if (parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == expression)
                    expression = memberBinding;
                else if (parent is ParenthesizedExpressionSyntax parenthesized)
                    expression = parenthesized;
                else
                    break;
            }

            switch (expression.Parent)
            {
                case AssignmentExpressionSyntax assignment:
                    return assignment.Left == expression;

                case PrefixUnaryExpressionSyntax prefix:
                    return prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                           prefix.IsKind(SyntaxKind.PreDecrementExpression);

                case PostfixUnaryExpressionSyntax postfix:
                    return postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                           postfix.IsKind(SyntaxKind.PostDecrementExpression);

                case ArgumentSyntax argument:
                    return argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) ||
                           argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                           IsDeconstructionTarget(argument);

                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the argument is an element of a tuple on the left of a deconstructing
        /// assignment, e.g. '(a, b) = pair' or 'foreach ((a, b) in pairs)'.
        /// </summary>
        private static bool IsDeconstructionTarget(ArgumentSyntax argument)
        {
            var current = argument;
            while (current.Parent is TupleExpressionSyntax tuple)
            {
                if (tuple.Parent is AssignmentExpressionSyntax assignment)
                    return assignment.Left == tuple;
                if (tuple.Parent is ForEachVariableStatementSyntax forEach)
                    return forEach.Variable == tuple;
                if (tuple.Parent is ArgumentSyntax outer)
                {
                    current = outer;
                    continue;
                }
                return false;
            }
            return false;
        }

        /// <summary>
        /// Returns true when <paramref name="node"/> lies inside a type or member declaration that is
        /// visible outside its assembly (see <see cref="IsExternallyVisible"/>). Code in top-level
        /// statements, using directives, and namespace-level positions is not a public API context.
        /// Inside a property, the accessor's own accessibility counts (e.g. a private setter).
        /// </summary>
        public static bool IsInPublicApiContext(SyntaxNode node, SemanticModel semanticModel)
        {
            foreach (var ancestor in node.AncestorsAndSelf())
            {
                switch (ancestor)
                {
                    case GlobalStatementSyntax:
                    case BaseNamespaceDeclarationSyntax:
                    case CompilationUnitSyntax:
                        return false;

                    case AccessorDeclarationSyntax:
                    case MemberDeclarationSyntax:
                        var declared = GetDeclaredSymbol(ancestor, semanticModel);
                        return declared != null && IsExternallyVisible(declared);
                }
            }

            return false;
        }

        private static ISymbol? GetDeclaredSymbol(SyntaxNode declaration, SemanticModel semanticModel)
        {
            if (declaration is BaseFieldDeclarationSyntax field)
            {
                var firstVariable = field.Declaration.Variables.FirstOrDefault();
                return firstVariable == null ? null : semanticModel.GetDeclaredSymbol(firstVariable);
            }

            return semanticModel.GetDeclaredSymbol(declaration);
        }

        /// <summary>
        /// True when the symbol and every containing type are public, protected, or protected internal.
        /// </summary>
        public static bool IsExternallyVisible(ISymbol symbol)
        {
            for (var current = symbol; current != null && current is not INamespaceSymbol; current = current.ContainingSymbol)
            {
                switch (current.DeclaredAccessibility)
                {
                    case Accessibility.Public:
                    case Accessibility.Protected:
                    case Accessibility.ProtectedOrInternal:
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }
    }
}
