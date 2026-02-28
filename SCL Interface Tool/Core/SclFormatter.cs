// --- File: Parsers/SclFormatter.cs ---
using System;
using System.Text;

namespace SCL_Interface_Tool.Core
{
    public static class SclFormatter
    {
        public static string Format(string rawScl)
        {
            var lines = rawScl.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            int indentLevel = 0;
            string indentString = "    "; // 4 spaces per indent

            foreach (var originalLine in lines)
            {
                string line = originalLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    sb.AppendLine();
                    continue;
                }

                string upperLine = line.ToUpper();

                // 1. PRE-OUTDENT (Decrease indent BEFORE writing the line)
                if (upperLine.StartsWith("END_") ||
                    upperLine.StartsWith("ELSE") ||
                    upperLine.StartsWith("ELSIF") ||
                    upperLine.StartsWith("UNTIL"))
                {
                    indentLevel = Math.Max(0, indentLevel - 1);
                }

                // 2. WRITE LINE
                sb.AppendLine(new string(' ', indentLevel * 4) + line);

                // 3. POST-INDENT (Increase indent AFTER writing the line)
                // Only increase if it opens a block AND doesn't close it on the same line
                bool opensBlock =
                    (upperLine.StartsWith("IF") && upperLine.Contains("THEN")) ||
                    (upperLine.StartsWith("ELSIF") && upperLine.Contains("THEN")) ||
                    upperLine.Equals("ELSE") ||
                    upperLine.StartsWith("REGION") ||
                    (upperLine.StartsWith("CASE") && upperLine.Contains("OF")) ||
                    (upperLine.StartsWith("FOR") && upperLine.Contains("DO")) ||
                    (upperLine.StartsWith("WHILE") && upperLine.Contains("DO")) ||
                    upperLine.Equals("REPEAT") ||
                    upperLine.StartsWith("STRUCT") ||
                    upperLine.StartsWith("TYPE") ||
                    upperLine.Equals("VAR_INPUT") ||
                    upperLine.Equals("VAR_OUTPUT") ||
                    upperLine.Equals("VAR_IN_OUT") ||
                    upperLine.Equals("VAR_TEMP") ||
                    upperLine.Equals("VAR") ||
                    upperLine.StartsWith("VAR CONSTANT") ||
                    upperLine.StartsWith("ORGANIZATION_BLOCK") ||
                    upperLine.StartsWith("DATA_BLOCK") ||
                    upperLine.StartsWith("FUNCTION_BLOCK") ||
                    (upperLine.StartsWith("FUNCTION") && !upperLine.StartsWith("FUNCTION_BLOCK"));

                bool closesBlockInline = upperLine.Contains("END_IF") || upperLine.Contains("END_CASE") ||
                                         upperLine.Contains("END_FOR") || upperLine.Contains("END_WHILE") ||
                                         upperLine.Contains("END_REGION");

                if (opensBlock && !closesBlockInline)
                {
                    indentLevel++;
                }
            }

            return sb.ToString();
        }
    }
}
