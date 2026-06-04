using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Single source of truth for complexity metrics.
    ///
    /// Historically the codebase had six slightly different cyclomatic-complexity
    /// implementations (in CodeMetricsService, FileStatisticsAnalyzer,
    /// TestCoverageAnalyzer, IncrementalAnalyzer, QualityTools and CodeNavigationTools),
    /// each missing a different set of decision points, so the same method could report
    /// different numbers depending on which tool was asked. All of them now delegate here.
    /// </summary>
    public static class ComplexityCalculator
    {
        /// <summary>
        /// Computes McCabe cyclomatic complexity for a member (method, accessor,
        /// local function, constructor, …). The result is 1 + the number of independent
        /// decision points.
        ///
        /// Counted decision points (+1 each):
        ///   if / while / do / for / foreach (incl. tuple deconstruction)
        ///   catch clause, case label (incl. pattern labels; default excluded)
        ///   switch-expression arm (discard arm excluded), case/catch 'when' guard
        ///   ternary ?:, null-coalescing ??, null-conditional ?. / ?[]
        ///   logical &amp;&amp; and ||, pattern 'and' / 'or' combinators
        /// </summary>
        public static int CalculateCyclomatic(SyntaxNode? node)
        {
            if (node is null)
                return 1;

            int complexity = 1;

            foreach (var n in node.DescendantNodes())
            {
                switch (n)
                {
                    case IfStatementSyntax:
                    case WhileStatementSyntax:
                    case DoStatementSyntax:
                    case ForStatementSyntax:
                    case ForEachStatementSyntax:
                    case ForEachVariableStatementSyntax:
                    case CatchClauseSyntax:
                    case CaseSwitchLabelSyntax:          // case 1:
                    case CasePatternSwitchLabelSyntax:   // case Foo f:
                    case WhenClauseSyntax:               // ... when (cond)
                    case ConditionalExpressionSyntax:    // a ? b : c
                    case ConditionalAccessExpressionSyntax: // a?.b / a?[i]
                        complexity++;
                        break;

                    case SwitchExpressionArmSyntax arm:
                        // Each arm is a branch; the catch-all discard arm (_ => …) is the
                        // switch-expression equivalent of 'default' and is not counted.
                        if (arm.Pattern is not DiscardPatternSyntax)
                            complexity++;
                        break;

                    case BinaryExpressionSyntax bin when
                        bin.IsKind(SyntaxKind.LogicalAndExpression) ||
                        bin.IsKind(SyntaxKind.LogicalOrExpression) ||
                        bin.IsKind(SyntaxKind.CoalesceExpression):
                        complexity++;
                        break;

                    case BinaryPatternSyntax bp when
                        bp.IsKind(SyntaxKind.AndPattern) ||
                        bp.IsKind(SyntaxKind.OrPattern):
                        complexity++;
                        break;
                }
            }

            return complexity;
        }

        /// <summary>
        /// Computes cognitive complexity (SonarSource specification): like cyclomatic
        /// complexity but weighted by nesting depth, so deeply nested control flow costs
        /// more than flat control flow. Boolean-operator sequences are counted once per
        /// run, and break/continue/goto each add 1.
        /// </summary>
        public static int CalculateCognitive(SyntaxNode? memberNode)
        {
            if (memberNode is null)
                return 0;

            int cognitiveComplexity = 0;

            void AnalyzeNode(SyntaxNode node, int nestingLevel)
            {
                // Structural decision points: +1 plus the current nesting level.
                if (node.IsKind(SyntaxKind.IfStatement) ||
                    node.IsKind(SyntaxKind.WhileStatement) ||
                    node.IsKind(SyntaxKind.ForStatement) ||
                    node.IsKind(SyntaxKind.ForEachStatement) ||
                    node.IsKind(SyntaxKind.ForEachVariableStatement) ||
                    node.IsKind(SyntaxKind.DoStatement) ||
                    node.IsKind(SyntaxKind.SwitchStatement) ||
                    node.IsKind(SyntaxKind.CatchClause) ||
                    node.IsKind(SyntaxKind.ConditionalExpression) ||
                    node.IsKind(SyntaxKind.CoalesceExpression))
                {
                    cognitiveComplexity += 1 + nestingLevel;

                    foreach (var child in node.ChildNodes())
                    {
                        AnalyzeNode(child, nestingLevel + 1);
                    }
                    return; // children already visited with deeper nesting
                }

                // Switch expressions: each arm is a branch weighted by nesting; a 'when'
                // guard on an arm adds one more.
                if (node is SwitchExpressionSyntax switchExpr)
                {
                    foreach (var arm in switchExpr.Arms)
                    {
                        cognitiveComplexity += 1 + nestingLevel;
                        if (arm.WhenClause is not null)
                            cognitiveComplexity += 1;
                    }

                    foreach (var child in node.ChildNodes())
                    {
                        AnalyzeNode(child, nestingLevel + 1);
                    }
                    return;
                }

                // Logical operators: +1 only when a new boolean run starts (the topmost
                // node of a chain of the same operator), so 'a && b && c' counts once.
                if (node.IsKind(SyntaxKind.LogicalAndExpression) ||
                    node.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    var parent = node.Parent;
                    if (parent is null ||
                        (!parent.IsKind(SyntaxKind.LogicalAndExpression) &&
                         !parent.IsKind(SyntaxKind.LogicalOrExpression)))
                    {
                        cognitiveComplexity += 1;
                    }
                }

                // Flow-breaking jumps: +1, not affected by nesting.
                if (node.IsKind(SyntaxKind.BreakStatement) ||
                    node.IsKind(SyntaxKind.ContinueStatement) ||
                    node.IsKind(SyntaxKind.GotoStatement))
                {
                    cognitiveComplexity += 1;
                }

                foreach (var child in node.ChildNodes())
                {
                    AnalyzeNode(child, nestingLevel);
                }
            }

            foreach (var child in memberNode.ChildNodes())
            {
                AnalyzeNode(child, 0);
            }

            return cognitiveComplexity;
        }
    }
}
