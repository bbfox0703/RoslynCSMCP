using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Shared ownership heuristics for IDisposable values, used by FindPerformanceIssues
    /// (DisposableNotDisposed) and AnalyzeExceptionHandling (MissingUsing).
    /// A value is "owned" when the declaring code creates it (object creation, a static factory,
    /// or a Create*/Open*/Begin* call). An owned value is considered handled when it is disposed
    /// (Dispose/DisposeAsync/Close, a using statement) or handed off (returned, stored, passed
    /// as an argument, or placed in an initializer).
    /// </summary>
    internal static class DisposableUsageAnalysis
    {
        private static readonly string[] DisposeMethodNames = { "Dispose", "DisposeAsync", "Close" };
        private static readonly string[] OwningMethodPrefixes = { "Create", "Open", "Begin" };

        /// <summary>
        /// True if the type is, or implements, System.IDisposable or System.IAsyncDisposable.
        /// Task and ValueTask are excluded: disposing them is never required.
        /// </summary>
        internal static bool IsDisposableType(ITypeSymbol? type)
        {
            if (type == null || type.TypeKind == TypeKind.Error)
                return false;

            if (IsTaskType(type))
                return false;

            if (IsDisposableInterface(type))
                return true;

            return type.AllInterfaces.Any(IsDisposableInterface);
        }

        private static bool IsDisposableInterface(ITypeSymbol type)
        {
            var name = type.ToDisplayString();
            return name == "System.IDisposable" || name == "System.IAsyncDisposable";
        }

        private static bool IsTaskType(ITypeSymbol type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks" &&
                    (current.Name == "Task" || current.Name == "ValueTask"))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True if the expression creates a value its caller owns.
        /// </summary>
        internal static bool IsOwnedCreation(ExpressionSyntax? expression, SemanticModel model)
        {
            expression = Unwrap(expression);

            switch (expression)
            {
                case ObjectCreationExpressionSyntax:
                case ImplicitObjectCreationExpressionSyntax:
                    return true;

                case AwaitExpressionSyntax awaitExpression:
                    return IsOwnedCreation(awaitExpression.Expression, model);

                case InvocationExpressionSyntax invocation:
                    if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
                        return false;
                    if (method.IsStatic && method.MethodKind != MethodKind.ReducedExtension)
                        return true;
                    return OwningMethodPrefixes.Any(p => method.Name.StartsWith(p, StringComparison.Ordinal));

                default:
                    return false;
            }
        }

        private static ExpressionSyntax? Unwrap(ExpressionSyntax? expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;
                    case CastExpressionSyntax cast:
                        expression = cast.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                        expression = postfix.Operand;
                        continue;
                    default:
                        return expression;
                }
            }
        }

        /// <summary>
        /// Returns the locals declared by this statement that are created here, have a disposable
        /// type, and are never disposed or handed off within their scope.
        /// Using declarations (using var / await using var) are never returned.
        /// </summary>
        internal static IEnumerable<(VariableDeclaratorSyntax Variable, ILocalSymbol Local)> FindUndisposedLocals(
            LocalDeclarationStatementSyntax declaration,
            SemanticModel model)
        {
            if (declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
                yield break;

            // A local can only be referenced inside the block (or top-level program) that declares it.
            SyntaxNode? scope = declaration.Parent is GlobalStatementSyntax global ? global.Parent : declaration.Parent;
            if (scope == null)
                yield break;

            foreach (var variable in declaration.Declaration.Variables)
            {
                if (model.GetDeclaredSymbol(variable) is not ILocalSymbol local)
                    continue;

                var initializer = variable.Initializer?.Value;
                if (initializer == null || !IsOwnedCreation(initializer, model))
                    continue;

                if (!IsDisposableType(local.Type) && !IsDisposableType(model.GetTypeInfo(initializer).Type))
                    continue;

                if (IsDisposedOrHandedOff(local, new[] { scope }, model.Compilation))
                    continue;

                yield return (variable, local);
            }
        }

        /// <summary>
        /// True if any reference to the symbol inside the given nodes disposes it or hands it off.
        /// </summary>
        internal static bool IsDisposedOrHandedOff(ISymbol symbol, IEnumerable<SyntaxNode> scopes, Compilation compilation)
        {
            foreach (var scope in scopes)
            {
                SemanticModel? model = null;

                foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (identifier.Identifier.ValueText != symbol.Name)
                        continue;

                    model ??= compilation.GetSemanticModel(scope.SyntaxTree);
                    var referenced = model.GetSymbolInfo(identifier).Symbol;
                    if (!SymbolEqualityComparer.Default.Equals(referenced?.OriginalDefinition, symbol.OriginalDefinition))
                        continue;

                    if (IsDisposingOrHandOffReference(identifier))
                        return true;
                }
            }

            return false;
        }

        private static bool IsDisposingOrHandOffReference(IdentifierNameSyntax identifier)
        {
            // Treat this.field and field identically.
            ExpressionSyntax expression = identifier;
            if (identifier.Parent is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisAccess &&
                thisAccess.Name == identifier)
            {
                expression = thisAccess;
            }

            // Climb through wrappers that pass the value through unchanged.
            while (true)
            {
                var parent = expression.Parent;
                if (parent is ParenthesizedExpressionSyntax or CastExpressionSyntax ||
                    parent is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) ||
                    parent is BinaryExpressionSyntax binary && (binary.IsKind(SyntaxKind.AsExpression) || binary.IsKind(SyntaxKind.CoalesceExpression)) ||
                    parent is ConditionalExpressionSyntax conditional && conditional.Condition != expression)
                {
                    expression = (ExpressionSyntax)parent!;
                    continue;
                }

                if (parent is SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax switchExpression } arm &&
                    arm.Expression == expression)
                {
                    expression = switchExpression;
                    continue;
                }

                break;
            }

            switch (expression.Parent)
            {
                // x.Dispose() / x.DisposeAsync() / x.Close()
                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.Expression == expression &&
                         memberAccess.Parent is InvocationExpressionSyntax &&
                         DisposeMethodNames.Contains(memberAccess.Name.Identifier.ValueText):
                    return true;

                // x?.Dispose()
                case ConditionalAccessExpressionSyntax conditionalAccess
                    when conditionalAccess.Expression == expression &&
                         conditionalAccess.WhenNotNull is InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } &&
                         DisposeMethodNames.Contains(binding.Name.Identifier.ValueText):
                    return true;

                // using (x) { ... }
                case UsingStatementSyntax usingStatement when usingStatement.Expression == expression:
                    return true;

                // return x; => x; yield return x;
                case ReturnStatementSyntax:
                case ArrowExpressionClauseSyntax:
                case YieldStatementSyntax:
                    return true;

                // other = x; (including object initializers: new Holder { Resource = x })
                case AssignmentExpressionSyntax assignment when assignment.Right == expression:
                    return true;

                // var other = x; / using var other = x;
                case EqualsValueClauseSyntax:
                    return true;

                // new Wrapper(x), Register(x), list.Add(x), (x, y)
                case ArgumentSyntax:
                    return true;

                // new List<IDisposable> { x }, new[] { x }
                case InitializerExpressionSyntax:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Name of the member (method, constructor, property, accessor owner, local function, ...)
        /// that contains the node, or "(global)" for top-level code.
        /// </summary>
        internal static string GetEnclosingMemberName(SyntaxNode node)
        {
            foreach (var ancestor in node.AncestorsAndSelf())
            {
                switch (ancestor)
                {
                    case LocalFunctionStatementSyntax localFunction:
                        return localFunction.Identifier.Text;
                    case MethodDeclarationSyntax method:
                        return method.Identifier.Text;
                    case ConstructorDeclarationSyntax constructor:
                        return constructor.Identifier.Text;
                    case DestructorDeclarationSyntax destructor:
                        return "~" + destructor.Identifier.Text;
                    case OperatorDeclarationSyntax op:
                        return "operator " + op.OperatorToken.Text;
                    case ConversionOperatorDeclarationSyntax conversion:
                        return "operator " + conversion.Type;
                    case PropertyDeclarationSyntax property:
                        return property.Identifier.Text;
                    case IndexerDeclarationSyntax:
                        return "this[]";
                    case EventDeclarationSyntax eventDeclaration:
                        return eventDeclaration.Identifier.Text;
                    case BaseFieldDeclarationSyntax field:
                        return field.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "(field)";
                    case BaseTypeDeclarationSyntax type:
                        return type.Identifier.Text;
                }
            }

            return "(global)";
        }
    }
}
