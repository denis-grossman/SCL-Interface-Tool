using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SCL_Interface_Tool.Models;

namespace SCL_Interface_Tool.Simulation
{
    public class AutoTestRunner
    {
        private ExecutionContext _context;
        private SimulationEngine _engine;
        private Action<string, Color, bool> _log;

        public AutoTestRunner(ExecutionContext context, SimulationEngine engine, Action<string, Color, bool> logCallback)
        {
            _context = context;
            _engine = engine;
            _log = logCallback;
        }

        public async Task RunScriptAsync(string script)
        {
            SclStandardLib.UseVirtualTime = true;
            SclStandardLib.VirtualTickCount = 0;

            string[] lines = script.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int passCount = 0;
            int failCount = 0;
            int stepNum = 1;

            _log("=== STARTING AUTOMATED TEST ===\n", Color.Cyan, true);

            await Task.Run(() =>
            {
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

                    _log($"\n[STEP {stepNum++}] {line}", Color.White, true);

                    try
                    {
                        var preState = TakeMemorySnapshot();

                        if (line.StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(line, @"SET\s+([a-zA-Z0-9_\[\]\.]+)\s*=\s*(.+)");
                            if (!match.Success) throw new Exception("Invalid SET syntax.");
                            string path = match.Groups[1].Value;
                            string val = match.Groups[2].Value;

                            SetMemoryValue(path, val);
                            _log($"  ▶ IN: {path} set to {val}", Color.LightGray, false);
                        }
                        else if (line.StartsWith("RUN ", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(line, @"RUN\s+(\d+)\s+(SCANS?|MS)", RegexOptions.IgnoreCase);
                            if (!match.Success) throw new Exception("Invalid RUN syntax.");

                            int count = int.Parse(match.Groups[1].Value);
                            string type = match.Groups[2].Value.ToUpper();

                            if (type == "MS")
                            {
                                SclStandardLib.VirtualTickCount += count;
                                _engine.StepScans(1); // Execute 1 scan so timers evaluate the new time
                            }
                            else
                            {
                                _engine.StepScans(count);
                            }

                            // Calculate and print Delta
                            var postState = TakeMemorySnapshot();
                            LogDelta(preState, postState);
                        }
                        else if (line.StartsWith("ASSERT ", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(line, @"ASSERT\s+([a-zA-Z0-9_\[\]\.]+)\s*==\s*(.+)");
                            if (!match.Success) throw new Exception("Invalid ASSERT syntax.");

                            string path = match.Groups[1].Value;
                            string expected = match.Groups[2].Value;
                            string actual = GetMemoryValue(path);

                            if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                            {
                                _log($"  ▶ PASS ({actual})", Color.LimeGreen, true);
                                passCount++;
                            }
                            else
                            {
                                _log($"  ▶ FAIL: Expected {expected}, got {actual}", Color.Red, true);
                                failCount++;
                            }
                        }
                        else
                        {
                            throw new Exception("Unknown command.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"  ▶ ERROR: {ex.Message}", Color.Red, true);
                        failCount++;
                        break; // Stop test on syntax/execution error
                    }
                }
            });

            _log($"\n=== TEST COMPLETED: {passCount} PASSED, {failCount} FAILED ===\n", failCount == 0 ? Color.LimeGreen : Color.Red, true);
            SclStandardLib.UseVirtualTime = false; // Reset clock for live simulation
        }

        // --- Memory Access Helpers ---

        private void SetMemoryValue(string path, string valStr)
        {
            lock (_engine.MemoryLock)
            {
                var parts = ParsePath(path);
                if (!_context.Memory.TryGetValue(parts.parent, out var tag)) throw new Exception($"Variable {parts.parent} not found.");

                object existing = GetActualValue(tag.CurrentValue, parts.subKey);
                object newVal = ParsePrimitive(valStr, existing);

                if (parts.subKey is int idx) ((Array)tag.CurrentValue).SetValue(newVal, idx);
                else if (parts.subKey is string key) ((Dictionary<string, object>)tag.CurrentValue)[key] = newVal;
                else tag.CurrentValue = newVal;
            }
        }

        private string GetMemoryValue(string path)
        {
            lock (_engine.MemoryLock)
            {
                var parts = ParsePath(path);
                if (!_context.Memory.TryGetValue(parts.parent, out var tag)) return "NULL";
                return FormatPrimitive(GetActualValue(tag.CurrentValue, parts.subKey));
            }
        }

        private (string parent, object subKey) ParsePath(string path)
        {
            var arrayMatch = Regex.Match(path, @"(\w+)\[(\d+)\]");
            if (arrayMatch.Success) return (arrayMatch.Groups[1].Value, int.Parse(arrayMatch.Groups[2].Value));

            var structMatch = Regex.Match(path, @"(\w+)\.(\w+)");
            if (structMatch.Success) return (structMatch.Groups[1].Value, structMatch.Groups[2].Value);

            return (path, null);
        }

        private object GetActualValue(object root, object subKey)
        {
            if (subKey is int idx) return ((Array)root).GetValue(idx);
            if (subKey is string key) return ((Dictionary<string, object>)root)[key];
            return root;
        }

        private Dictionary<string, string> TakeMemorySnapshot()
        {
            var snap = new Dictionary<string, string>();
            lock (_engine.MemoryLock)
            {
                foreach (var m in _context.Memory.Values)
                {
                    if (m.Direction == ElementDirection.Member || m.Direction == ElementDirection.Input) continue; // Only track Outputs, Statics, InOuts

                    if (m.CurrentValue is Array arr)
                    {
                        for (int i = 0; i < arr.Length; i++) snap[$"{m.Name}[{i}]"] = FormatPrimitive(arr.GetValue(i));
                    }
                    else if (m.CurrentValue is Dictionary<string, object> dict)
                    {
                        foreach (var kvp in dict) snap[$"{m.Name}.{kvp.Key}"] = FormatPrimitive(kvp.Value);
                    }
                    else if (m.CurrentValue != null && m.CurrentValue.GetType().IsPrimitive || m.CurrentValue is string)
                    {
                        snap[m.Name] = FormatPrimitive(m.CurrentValue);
                    }
                }
            }
            return snap;
        }

        private void LogDelta(Dictionary<string, string> pre, Dictionary<string, string> post)
        {
            bool changed = false;
            foreach (var kvp in post)
            {
                if (pre.TryGetValue(kvp.Key, out string preVal) && preVal != kvp.Value)
                {
                    _log($"  ▶ OUT: {kvp.Key} changed ({preVal} -> {kvp.Value})", Color.Gold, false);
                    changed = true;
                }
            }
            if (!changed) _log("  ▶ (No output changes)", Color.DimGray, false);
        }

        private string FormatPrimitive(object val) => val is bool b ? (b ? "TRUE" : "FALSE") : (val is float f ? f.ToString("F2", CultureInfo.InvariantCulture) : val?.ToString() ?? "0");

        private object ParsePrimitive(string str, object existingVal)
        {
            if (existingVal is bool) return str.Equals("true", StringComparison.OrdinalIgnoreCase) || str == "1";
            if (existingVal is float) return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0.0f;
            return int.TryParse(str, out int i) ? i : 0;
        }
    }
}
