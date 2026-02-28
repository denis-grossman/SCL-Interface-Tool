using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SCL_Interface_Tool.Models;

namespace SCL_Interface_Tool.Simulation
{
    public class MemoryTag
    {
        public string Name { get; }
        public string DataType { get; }
        public ElementDirection Direction { get; }
        public object CurrentValue { get; set; }

        public MemoryTag(string name, string dataType, ElementDirection direction, object initialValue)
        {
            Name = name;
            DataType = dataType;
            Direction = direction;
            CurrentValue = initialValue;
        }
    }

    public class ExecutionContext
    {
        public string BlockName { get; }
        public string BlockType { get; }
        public Dictionary<string, MemoryTag> Memory { get; }

        public Dictionary<string, Dictionary<string, int>> EnumDefinitions { get; }
        public Dictionary<string, Dictionary<string, string>> StructDefinitions { get; }

        // Added fullSclText parameter to parse TYPE definitions BEFORE creating memory
        public ExecutionContext(SclBlock parsedBlock, string fullSclText)
        {
            BlockName = parsedBlock.Name;
            BlockType = parsedBlock.BlockType;
            Memory = new Dictionary<string, MemoryTag>(StringComparer.OrdinalIgnoreCase);
            EnumDefinitions = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            StructDefinitions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            ParseTypeDefinitions(fullSclText);

            foreach (var el in parsedBlock.Elements)
            {
                object initValue = CreateDefaultValue(el.DataType, el.InitialValue);
                Memory[el.Name] = new MemoryTag(el.Name, el.DataType, el.Direction, initValue);
            }
        }

        private void ParseTypeDefinitions(string fullText)
        {
            var enumMatches = Regex.Matches(fullText, @"\bTYPE\b\s+""?(\w+)""?\s*:\s*\(([^)]+)\)\s*;?\s*\bEND_TYPE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in enumMatches)
            {
                string enumName = m.Groups[1].Value;
                var members = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int autoIndex = 0;

                foreach (string part in m.Groups[2].Value.Split(','))
                {
                    string trimmed = part.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var assignMatch = Regex.Match(trimmed, @"(\w+)\s*:=\s*(\d+)");
                    if (assignMatch.Success)
                    {
                        members[assignMatch.Groups[1].Value] = int.Parse(assignMatch.Groups[2].Value);
                        autoIndex = int.Parse(assignMatch.Groups[2].Value) + 1;
                    }
                    else
                    {
                        var nameMatch = Regex.Match(trimmed, @"(\w+)");
                        if (nameMatch.Success) members[nameMatch.Groups[1].Value] = autoIndex++;
                    }
                }
                if (members.Count > 0) EnumDefinitions[enumName] = members;
            }

            var structMatches = Regex.Matches(fullText, @"\bTYPE\b\s+""?(\w+)""?\s*:\s*STRUCT\b(.*?)\bEND_STRUCT\b\s*;?\s*\bEND_TYPE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in structMatches)
            {
                string structName = m.Groups[1].Value;
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match f in Regex.Matches(m.Groups[2].Value, @"(\w+)\s*:\s*(\w+(?:\s*\[.*?\]\s*OF\s+\w+)?)\s*;"))
                    fields[f.Groups[1].Value] = f.Groups[2].Value;
                if (fields.Count > 0) StructDefinitions[structName] = fields;
            }
        }

        public void PrepareForNextScanCycle()
        {
            if (BlockType.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var tag in Memory.Values)
                {
                    if (tag.Direction == ElementDirection.Temp || tag.Direction == ElementDirection.Output)
                        tag.CurrentValue = CreateDefaultValue(tag.DataType, string.Empty);
                }
            }
        }

        public object CreateDefaultValue(string dataType, string initialValue)
        {
            string dt = dataType.ToUpper().Trim();

            if (dt == "TON") return new SclStandardLib.TON();
            if (dt == "TOF") return new SclStandardLib.TOF();
            if (dt == "TP") return new SclStandardLib.TP();
            if (dt == "TONR") return new SclStandardLib.TONR();
            if (dt == "R_TRIG") return new SclStandardLib.R_TRIG();
            if (dt == "F_TRIG") return new SclStandardLib.F_TRIG();
            if (dt == "CTU") return new SclStandardLib.CTU();
            if (dt == "CTD") return new SclStandardLib.CTD();
            if (dt == "CTUD") return new SclStandardLib.CTUD();
            if (dt == "SR") return new SclStandardLib.SR();
            if (dt == "RS") return new SclStandardLib.RS();

            var arrayMatch = Regex.Match(dt, @"ARRAY\s*\[\s*(\d+)\s*\.\.\s*(\d+)\s*\]\s*OF\s+(\w+)", RegexOptions.IgnoreCase);
            if (arrayMatch.Success)
            {
                int hi = int.Parse(arrayMatch.Groups[2].Value);
                string elemType = arrayMatch.Groups[3].Value.ToUpper();
                int size = hi + 1;

                return elemType switch
                {
                    "BOOL" => new bool[size],
                    "REAL" or "LREAL" => new float[size],
                    "STRING" => new string[size],
                    _ => new int[size]
                };
            }

            if (StructDefinitions.ContainsKey(dt))
            {
                var structDef = StructDefinitions[dt];
                var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in structDef) obj[kvp.Key] = CreateDefaultValue(kvp.Value, "");
                return obj;
            }

            if (EnumDefinitions.ContainsKey(dt))
            {
                if (!string.IsNullOrWhiteSpace(initialValue) && EnumDefinitions[dt].TryGetValue(initialValue.Trim(), out int enumVal))
                    return enumVal;
                return 0;
            }

            return dt switch
            {
                "BOOL" => TryParseBool(initialValue),
                "REAL" or "LREAL" => TryParseFloat(initialValue),
                "STRING" => TryParseString(initialValue),
                "DATE" => TryParseDate(initialValue),
                "TIME_OF_DAY" or "TOD" => TryParseTod(initialValue),
                "DATE_AND_TIME" or "DT" => TryParseDt(initialValue),
                _ => TryParseInt(initialValue) // INT, DINT, WORD, TIME, etc.
            };
        }

        private bool TryParseBool(string val) => !string.IsNullOrWhiteSpace(val) && (val.Trim().ToUpper() == "TRUE" || val.Trim() == "1");
        private float TryParseFloat(string val) => float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float res) ? res : 0.0f;
        private string TryParseString(string val) => string.IsNullOrWhiteSpace(val) ? "" : val.Trim().Trim('\'', '"');

        private int TryParseInt(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            val = val.Trim();
            if (val.StartsWith("T#", StringComparison.OrdinalIgnoreCase) || val.StartsWith("TIME#", StringComparison.OrdinalIgnoreCase)) return ParseSiemensTime(val);
            if (val.StartsWith("16#", StringComparison.OrdinalIgnoreCase)) return int.TryParse(val.Substring(3), NumberStyles.HexNumber, null, out int h) ? h : 0;
            if (val.StartsWith("2#", StringComparison.OrdinalIgnoreCase)) { try { return Convert.ToInt32(val.Substring(2), 2); } catch { return 0; } }
            if (int.TryParse(val, out int res)) return res;
            return 0;
        }

        private long TryParseDate(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            val = val.Trim();
            if (val.StartsWith("D#", StringComparison.OrdinalIgnoreCase) || val.StartsWith("DATE#", StringComparison.OrdinalIgnoreCase))
            {
                string dateStr = Regex.Replace(val, @"^(?:DATE#|D#)", "", RegexOptions.IgnoreCase);
                if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt)) return dt.Ticks;
            }
            return 0;
        }

        private long TryParseTod(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            val = val.Trim();
            if (val.StartsWith("TOD#", StringComparison.OrdinalIgnoreCase) || val.StartsWith("TIME_OF_DAY#", StringComparison.OrdinalIgnoreCase))
            {
                string todStr = Regex.Replace(val, @"^(?:TIME_OF_DAY#|TOD#)", "", RegexOptions.IgnoreCase);
                if (TimeSpan.TryParse(todStr, CultureInfo.InvariantCulture, out TimeSpan ts)) return ts.Ticks;
            }
            return 0;
        }

        private long TryParseDt(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            val = val.Trim();
            if (val.StartsWith("DT#", StringComparison.OrdinalIgnoreCase) || val.StartsWith("DATE_AND_TIME#", StringComparison.OrdinalIgnoreCase))
            {
                string dtStr = Regex.Replace(val, @"^(?:DATE_AND_TIME#|DT#)", "", RegexOptions.IgnoreCase).Replace("-", " ").Trim();
                string[] formats = { "yyyy MM dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
                if (DateTime.TryParseExact(dtStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt)) return dt.Ticks;
                if (DateTime.TryParse(dtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt.Ticks;
            }
            return 0;
        }

        private int ParseSiemensTime(string t)
        {
            t = t.StartsWith("TIME#", StringComparison.OrdinalIgnoreCase) ? t.Substring(5).ToLower() : t.Substring(2).ToLower();
            if (t.EndsWith("ms") && int.TryParse(t.Replace("ms", ""), out int ms)) return ms;
            if (t.EndsWith("s") && float.TryParse(t.Replace("s", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float s)) return (int)(s * 1000);
            if (t.EndsWith("m") && float.TryParse(t.Replace("m", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float m)) return (int)(m * 60000);
            if (t.EndsWith("h") && float.TryParse(t.Replace("h", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float h)) return (int)(h * 3600000);
            return 0;
        }
    }
}
