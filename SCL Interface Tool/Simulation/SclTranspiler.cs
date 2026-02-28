using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SCL_Interface_Tool.Models;
using ExecutionContext = SCL_Interface_Tool.Simulation.ExecutionContext;

namespace SCL_Interface_Tool.Simulation
{
    public class SimulationGlobals { public Dictionary<string, MemoryTag> Memory { get; set; } }

    public static class SclTranspiler
    {
        private static readonly HashSet<string> UnsupportedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TCON", "TDISCON", "TSEND", "TRCV", "RUNTIME", "RD_SYS_T", "WR_SYS_T",
            "DPRD_DAT", "DPWR_DAT", "RDREC", "WRREC", "RALRM", "QRY_DINT",
            "DeviceStates", "ModuleStates", "LED", "GET", "PUT"
        };

        public static string Transpile(string fullSclText, ExecutionContext context)
        {
            string bodyText = ExtractBlockBody(fullSclText, context.BlockName, context.BlockType);

            foreach (var func in UnsupportedFunctions)
            {
                if (Regex.IsMatch(bodyText, $@"\b{func}\b", RegexOptions.IgnoreCase))
                    throw new Exception($"Unsupported HW function '{func}' detected. Cannot simulate locally.");
            }

            string csharpLogic = ConvertSclToCSharp(bodyText, context);
            StringBuilder script = new StringBuilder();

            script.AppendLine("using System;");
            script.AppendLine("using System.Collections.Generic;");
            script.AppendLine("using SCL_Interface_Tool.Simulation;");
            script.AppendLine("using static SCL_Interface_Tool.Simulation.SclStandardLib;\n");

            script.AppendLine("// --- MAP IN ---");
            foreach (var tag in context.Memory.Values)
            {
                string cType = GetCSharpType(tag.DataType, context);
                script.AppendLine($"{cType} {tag.Name} = ({cType})Memory[\"{tag.Name}\"].CurrentValue;");
            }

            script.AppendLine("\n// --- LOGIC ---");
            script.AppendLine(csharpLogic);

            script.AppendLine("\n// --- MAP OUT ---");
            foreach (var tag in context.Memory.Values)
                script.AppendLine($"Memory[\"{tag.Name}\"].CurrentValue = {tag.Name};");

            return script.ToString();
        }

        private static string ExtractBlockBody(string full, string name, string type)
        {
            string endKeyword = type.Equals("PROGRAM", StringComparison.OrdinalIgnoreCase) ? "END_PROGRAM" : $"END_{type}";
            var m = Regex.Match(full, $@"(?i)\b{type}\b\s+""?{Regex.Escape(name)}""?\s*(.*?)\b{endKeyword}\b", RegexOptions.Singleline);
            if (!m.Success) return "";
            string content = m.Groups[1].Value;

            int beginIdx = content.IndexOf("BEGIN", StringComparison.OrdinalIgnoreCase);
            if (beginIdx >= 0) return content.Substring(beginIdx + 5).Trim();

            int lastEndVar = content.LastIndexOf("END_VAR", StringComparison.OrdinalIgnoreCase);
            if (lastEndVar >= 0) return content.Substring(lastEndVar + 7).Trim();

            return content.Trim();
        }

        private static string ConvertSclToCSharp(string scl, ExecutionContext context)
        {
            scl = Regex.Replace(scl, @"\(\*[\s\S]*?\*\)", "");
            scl = Regex.Replace(scl, @"//.*", "");

            scl = Regex.Replace(scl, @"\b(\w+)\s*\(\s*IN\s*:=\s*(.+?)\s*,\s*PT\s*:=\s*(.+?)\s*\)\s*;", "$1.Execute($2, $3);", RegexOptions.IgnoreCase);

            scl = Regex.Replace(scl, @"\b(\w+)\s*\((\s*\w+\s*:=\s*.+?(?:\s*,\s*\w+\s*:=\s*.+?)*)\s*\)\s*;", m =>
            {
                string instance = m.Groups[1].Value;
                var values = Regex.Matches(m.Groups[2].Value, @":=\s*(.+?)(?:\s*,|$)");
                var args = new System.Collections.Generic.List<string>();
                foreach (Match v in values) args.Add(v.Groups[1].Value.Trim());
                return $"{instance}.Execute({string.Join(", ", args)});";
            }, RegexOptions.IgnoreCase);

            scl = Regex.Replace(scl, @"(?i)(?:TIME#|T#)([0-9\.]+)(ms|s|m|h)\b", m =>
            {
                float v = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                switch (m.Groups[2].Value.ToLower()) { case "h": v *= 3600000; break; case "m": v *= 60000; break; case "s": v *= 1000; break; }
                return ((int)v).ToString();
            });

            scl = Regex.Replace(scl, @"(?i)(?:DATE_AND_TIME#|DT#)(\d{4})-(\d{1,2})-(\d{1,2})-(\d{1,2}):(\d{2}):(\d{2})\b", "new DateTime($1, $2, $3, $4, $5, $6).Ticks");
            scl = Regex.Replace(scl, @"(?i)(?:TIME_OF_DAY#|TOD#)(\d{1,2}):(\d{2}):(\d{2})\b", "new TimeSpan($1, $2, $3).Ticks");
            scl = Regex.Replace(scl, @"(?i)(?:DATE#|D#)(\d{4})-(\d{1,2})-(\d{1,2})\b", "new DateTime($1, $2, $3).Ticks");

            scl = Regex.Replace(scl, @"\b16#([0-9A-Fa-f]+)\b", "0x$1");
            scl = Regex.Replace(scl, @"\b2#([01]+)\b", "0b$1");

            foreach (var enumDef in context.EnumDefinitions)
                foreach (var member in enumDef.Value)
                    scl = Regex.Replace(scl, $@"\b{Regex.Escape(enumDef.Key)}#{Regex.Escape(member.Key)}\b", member.Value.ToString(), RegexOptions.IgnoreCase);

            scl = Regex.Replace(scl, @"#(?=\w)", "");
            scl = Regex.Replace(scl, @"(\w+(?:\.\w+)?)\s*\*\*\s*(\w+(?:\.\w+)?)", "EXPT($1, $2)");
            scl = Regex.Replace(scl, @"'([^']*)'", "\"$1\"");

            foreach (var tag in context.Memory.Values)
            {
                string dtUpper = tag.DataType.ToUpper();
                if (context.StructDefinitions.ContainsKey(dtUpper))
                {
                    foreach (var field in context.StructDefinitions[dtUpper].Keys)
                    {
                        scl = Regex.Replace(scl, $@"\b{Regex.Escape(tag.Name)}\b\.{Regex.Escape(field)}\s*:=\s*(.+?);", $"STRUCT_SET({tag.Name}, \"{field}\", $1);", RegexOptions.IgnoreCase);
                        string castType = GetCSharpType(context.StructDefinitions[dtUpper][field], context);
                        scl = Regex.Replace(scl, $@"\b{Regex.Escape(tag.Name)}\b\.{Regex.Escape(field)}\b", $"(({castType})STRUCT_GET({tag.Name}, \"{field}\"))", RegexOptions.IgnoreCase);
                    }
                }
            }

            scl = Regex.Replace(scl, @"(?<!\w)(\d+\.\d+)(?![fFdDmM\w])", "${1}f");

            scl = Regex.Replace(scl, @"(?<![:<>!+\-*/])=(?!=)", "==");
            scl = scl.Replace(":=", "=");
            scl = scl.Replace("<>", "!=");
            scl = Regex.Replace(scl, @"\bAND\b", "&&", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bOR\b", "||", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bXOR\b", "^", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bNOT\b", "!", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bMOD\b", "%", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bTRUE\b", "true", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bFALSE\b", "false", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bRETURN\b\s*;", "return;", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bEXIT\b\s*;", "break;", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bCONTINUE\b\s*;", "continue;", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bREGION\b(.*)", "", RegexOptions.IgnoreCase);
            scl = Regex.Replace(scl, @"\bEND_REGION\b", "", RegexOptions.IgnoreCase);

            string[] lines = scl.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            StringBuilder result = new StringBuilder();
            bool insideCase = false, firstCaseLabel = true, repeatClosedByUntil = false;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) { result.AppendLine(); continue; }

                if (Regex.IsMatch(line, @"^\s*END_IF\s*;?\s*$", RegexOptions.IgnoreCase)) { result.AppendLine("}"); continue; }
                if (Regex.IsMatch(line, @"^\s*END_WHILE\s*;?\s*$", RegexOptions.IgnoreCase)) { result.AppendLine("}"); continue; }
                if (Regex.IsMatch(line, @"^\s*END_FOR\s*;?\s*$", RegexOptions.IgnoreCase)) { result.AppendLine("}"); continue; }
                if (Regex.IsMatch(line, @"^\s*END_REPEAT\s*;?\s*$", RegexOptions.IgnoreCase)) { if (!repeatClosedByUntil) result.AppendLine("} while (true);"); repeatClosedByUntil = false; continue; }
                if (Regex.IsMatch(line, @"^\s*END_CASE\s*;?\s*$", RegexOptions.IgnoreCase)) { if (insideCase) result.AppendLine("    break;"); result.AppendLine("}"); insideCase = false; firstCaseLabel = true; continue; }

                var elsifMatch = Regex.Match(line, @"^\s*ELSIF\b(.+)\bTHEN\s*$", RegexOptions.IgnoreCase);
                if (elsifMatch.Success) { result.AppendLine($"}} else if ({elsifMatch.Groups[1].Value.Trim()}) {{"); continue; }

                if (Regex.IsMatch(line, @"^\s*ELSE\s*$", RegexOptions.IgnoreCase))
                {
                    if (insideCase) { result.AppendLine("    break;"); result.AppendLine("default:"); } else { result.AppendLine("} else {"); }
                    continue;
                }

                var ifMatch = Regex.Match(line, @"^\s*IF\b(.+)\bTHEN\s*$", RegexOptions.IgnoreCase);
                if (ifMatch.Success) { result.AppendLine($"if ({ifMatch.Groups[1].Value.Trim()}) {{"); continue; }

                var whileMatch = Regex.Match(line, @"^\s*WHILE\b(.+)\bDO\s*$", RegexOptions.IgnoreCase);
                if (whileMatch.Success) { result.AppendLine($"while ({whileMatch.Groups[1].Value.Trim()}) {{"); continue; }

                if (Regex.IsMatch(line, @"^\s*REPEAT\s*$", RegexOptions.IgnoreCase)) { repeatClosedByUntil = false; result.AppendLine("do {"); continue; }

                var untilMatch = Regex.Match(line, @"^\s*UNTIL\b(.+?)(?:\bEND_REPEAT\b)?\s*;?\s*$", RegexOptions.IgnoreCase);
                if (untilMatch.Success) { string condition = untilMatch.Groups[1].Value.Trim().TrimEnd(';'); result.AppendLine($"}} while (!({condition}));"); repeatClosedByUntil = true; continue; }

                var forMatch = Regex.Match(line, @"^\s*FOR\b\s+(\w+)\s*:?=\s*(.+?)\s+TO\s+(.+?)(?:\s+BY\s+(.+?))?\s+DO\s*$", RegexOptions.IgnoreCase);
                if (forMatch.Success) { string v = forMatch.Groups[1].Value, from = forMatch.Groups[2].Value, to = forMatch.Groups[3].Value, by = forMatch.Groups[4].Success ? forMatch.Groups[4].Value : "1"; result.AppendLine($"for ({v} = {from}; {v} <= {to}; {v} += {by}) {{"); continue; }

                var caseMatch = Regex.Match(line, @"^\s*CASE\b(.+)\bOF\s*$", RegexOptions.IgnoreCase);
                if (caseMatch.Success) { result.AppendLine($"switch ({caseMatch.Groups[1].Value.Trim()}) {{"); insideCase = true; firstCaseLabel = true; continue; }

                if (insideCase)
                {
                    var caseLabelMatch = Regex.Match(line, @"^\s*([\d]+(?:\s*(?:,|\.\.)\s*[\d]+)*)\s*:\s*$");
                    if (caseLabelMatch.Success) { if (!firstCaseLabel) result.AppendLine("    break;"); firstCaseLabel = false; EmitCaseLabels(result, caseLabelMatch.Groups[1].Value); continue; }

                    var caseInlineMatch = Regex.Match(line, @"^\s*([\d]+(?:\s*(?:,|\.\.)\s*[\d]+)*)\s*:\s*(.+)$");
                    if (caseInlineMatch.Success) { if (!firstCaseLabel) result.AppendLine("    break;"); firstCaseLabel = false; EmitCaseLabels(result, caseInlineMatch.Groups[1].Value); result.AppendLine($"    {caseInlineMatch.Groups[2].Value.Trim()}"); continue; }
                }
                result.AppendLine(line);
            }
            return result.ToString();
        }

        private static void EmitCaseLabels(StringBuilder result, string labelText)
        {
            foreach (string part in Regex.Split(labelText, @"\s*,\s*"))
            {
                var rangeMatch = Regex.Match(part.Trim(), @"^(\d+)\.\.(\d+)$");
                if (rangeMatch.Success)
                {
                    for (int n = int.Parse(rangeMatch.Groups[1].Value); n <= int.Parse(rangeMatch.Groups[2].Value); n++) result.AppendLine($"case {n}:");
                }
                else
                {
                    result.AppendLine($"case {part.Trim()}:");
                }
            }
        }

        private static string GetCSharpType(string dt, ExecutionContext context)
        {
            string upper = dt.ToUpper().Trim();
            if (upper == "BOOL") return "bool";
            if (upper == "REAL" || upper == "LREAL") return "float";
            if (upper == "STRING") return "string";
            if (upper == "DATE" || upper == "TIME_OF_DAY" || upper == "TOD" || upper == "DATE_AND_TIME" || upper == "DT") return "long";
            if (upper == "TON" || upper == "TOF" || upper == "TP" || upper == "TONR" || upper == "R_TRIG" || upper == "F_TRIG" || upper == "CTU" || upper == "CTD" || upper == "CTUD" || upper == "SR" || upper == "RS") return $"SclStandardLib.{upper}";

            if (Regex.IsMatch(upper, @"ARRAY\s*\[.*\]\s*OF\s+\w+"))
            {
                string elemUpper = Regex.Match(upper, @"OF\s+(\w+)").Groups[1].Value;
                if (elemUpper == "BOOL") return "bool[]";
                if (elemUpper == "REAL" || elemUpper == "LREAL") return "float[]";
                if (elemUpper == "STRING") return "string[]";
                return "int[]";
            }
            if (context.StructDefinitions.ContainsKey(upper)) return "Dictionary<string, object>";
            return "int"; // Fallback for INT, DINT, WORD, ENUM etc.
        }
    }
}
