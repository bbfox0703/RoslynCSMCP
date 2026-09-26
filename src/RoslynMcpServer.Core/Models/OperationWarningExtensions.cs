using System.Text;

namespace RoslynMcpServer.Core.Models
{
    /// <summary>
    /// Formatting helpers that keep load and analysis failures visible in tool output, so a run that
    /// failed or only partly succeeded is never mistaken for a clean result.
    /// </summary>
    public static class OperationWarningExtensions
    {
        private const int DefaultMaxShown = 10;

        /// <summary>
        /// Appends a warnings block listing the context and message of each warning (up to
        /// <paramref name="maxShown"/>), separated from preceding text by a blank line.
        /// Does nothing when there are no warnings.
        /// </summary>
        public static StringBuilder AppendWarnings(
            this StringBuilder output,
            IReadOnlyCollection<OperationWarning> warnings,
            int maxShown = DefaultMaxShown)
        {
            if (warnings.Count == 0)
                return output;

            EnsureBlankLine(output);
            output.AppendLine($"⚠️ Warnings ({warnings.Count}); results may be incomplete:");
            foreach (var warning in warnings.Take(maxShown))
            {
                output.AppendLine(string.IsNullOrWhiteSpace(warning.Context)
                    ? $"  - {warning.Message}"
                    : $"  - {warning.Context}: {warning.Message}");
            }
            if (warnings.Count > maxShown)
                output.AppendLine($"  ... and {warnings.Count - maxShown} more");

            return output;
        }

        /// <summary>
        /// Returns <paramref name="text"/> followed by a warnings block, or <paramref name="text"/>
        /// unchanged when there are no warnings. Intended for early-return branches such as
        /// "No issues found." that would otherwise hide a load failure.
        /// </summary>
        public static string WithWarnings(
            this string text,
            IReadOnlyCollection<OperationWarning> warnings,
            int maxShown = DefaultMaxShown)
        {
            if (warnings.Count == 0)
                return text;

            return new StringBuilder(text).AppendWarnings(warnings, maxShown).ToString();
        }

        private static void EnsureBlankLine(StringBuilder output)
        {
            if (output.Length == 0)
                return;

            var trailingNewlines = 0;
            for (var i = output.Length - 1; i >= 0 && trailingNewlines < 2; i--)
            {
                var c = output[i];
                if (c == '\n')
                    trailingNewlines++;
                else if (c != '\r')
                    break;
            }

            for (; trailingNewlines < 2; trailingNewlines++)
                output.AppendLine();
        }
    }
}
