// --- File: AutoTestRunner.cs ---
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
    public class TimingData
    {
        public List<double> Scans { get; set; } = new List<double>();
        public Dictionary<string, List<double>> Signals { get; set; } = new Dictionary<string, List<double>>();
        public Dictionary<string, bool> IsDigital { get; set; } = new Dictionary<string, bool>();
    }

    public class AutoTestRunner
    {
        private ExecutionContext _context;
        private SimulationEngine _engine;
        private Action<string, Color, bool> _log;

        public TimingData RecordedData { get; private set; }
        private int _currentScan;

        public AutoTestRunner(ExecutionContext context, SimulationEngine engine, Action<string, Color, bool> logCallback)
        {
            _context = context;
            _engine = engine;
            _log = logCallback;
        }

        public async Task RunScriptAsync(string script)
        {
            // =====================================================================
            // FIX: Properly initialize virtual time for the entire test session.
            // We start from a known base so all TON/TOF/TP timers see consistent
            // monotonically increasing time values across all RUN commands.
            // =====================================================================
            SclStandardLib.UseVirtualTime = true;
            SclStandardLib.VirtualTickCount = Environment.TickCount64;

            RecordedData = new TimingData();
            _currentScan = 0;
            RecordAnalyzerState();

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
                                // ==========================================================
                                // FIX: Use StepTime which runs multiple scans at 10ms
                                // intervals, properly advancing virtual time per scan.
                                // This allows TON/TOF/TP timers to accumulate ET correctly
                                // and trigger intermediate state changes during the window.
                                // ==========================================================
                                int scanIntervalMs = 10;
                                int totalScans = Math.Max(1, count / scanIntervalMs);

                                // We manually step and record per-scan for the chart
                                lock (_engine.MemoryLock)
                                {
                                    for (int i = 0; i < totalScans; i++)
                                    {
                                        SclStandardLib.VirtualTickCount += scanIntervalMs;
                                        _engine.StepScans(1);
                                        _currentScan++;

                                        // Record every Nth scan to keep chart data manageable
                                        // For large time windows (>1s), sample every 10 scans
                                        if (totalScans <= 100 || i % 10 == 0 || i == totalScans - 1)
                                        {
                                            RecordAnalyzerState();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // RUN X SCANS — StepScans already advances virtual time
                                // when UseVirtualTime is true (10ms per scan)
                                for (int i = 0; i < count; i++)
                                {
                                    _engine.StepScans(1);
                                    _currentScan++;
                                    RecordAnalyzerState();
                                }
                            }

                            var postState = TakeMemorySnapshot();
                            LogDelta(preState, postState);
                        }
                        else if (line.StartsWith("ASSERT ", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(line, @"ASSERT\s+([a-zA-Z0-9_\[\]\.]+)\s*==\s*(.+)");
                            if (!match.Success) throw new Exception("Invalid ASSERT syntax.");

                            string path = match.Groups[1].Value;
                            string expected = match.Groups[2].Value.Trim();
                            string actual = GetMemoryValue(path);

                            if (CompareValues(actual, expected))
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
                        break;
                    }
                }
            });

            RecordAnalyzerState();

            _log($"\n=== TEST COMPLETED: {passCount} PASSED, {failCount} FAILED ===\n", failCount == 0 ? Color.LimeGreen : Color.Red, true);

            // =====================================================================
            // FIX: Reset virtual time after test completes so that live simulation
            // (▶ Start button) uses real wall-clock time and timers work correctly.
            // This also prevents stale virtual time from leaking into manual Step
            // or into a second SimulationForm window for a different block.
            // =====================================================================
            SclStandardLib.UseVirtualTime = false;
            SclStandardLib.VirtualTickCount = 0;
        }

        /// <summary>
        /// Robust value comparison that handles case-insensitive booleans,
        /// integer formatting differences, and float tolerance.
        /// </summary>
        private bool CompareValues(string actual, string expected)
        {
            if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return true;

            // Handle boolean synonyms: "True"/"1", "False"/"0"
            if ((actual.Equals("True", StringComparison.OrdinalIgnoreCase) && expected == "1") ||
                (actual.Equals("False", StringComparison.OrdinalIgnoreCase) && expected == "0") ||
                (actual == "1" && expected.Equals("True", StringComparison.OrdinalIgnoreCase)) ||
                (actual == "0" && expected.Equals("False", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Handle float comparison with tolerance (e.g., "3.14" vs "3.14")
            if (float.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out float fActual) &&
                float.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out float fExpected))
            {
                return Math.Abs(fActual - fExpected) < 0.01f;
            }

            return false;
        }

        private void RecordAnalyzerState()
        {
            lock (_engine.MemoryLock)
            {
                if (RecordedData.Scans.Count > 0 && RecordedData.Scans.Last() == _currentScan)
                    return;

                RecordedData.Scans.Add(_currentScan);

                Action<string, object> recordValue = (name, val) =>
                {
                    if (val is bool b)
                    {
                        if (!RecordedData.Signals.ContainsKey(name)) { RecordedData.Signals[name] = new List<double>(); RecordedData.IsDigital[name] = true; }
                        RecordedData.Signals[name].Add(b ? 1.0 : 0.0);
                    }
                    else if (val is IConvertible conv)
                    {
                        try
                        {
                            double d = conv.ToDouble(CultureInfo.InvariantCulture);
                            if (!RecordedData.Signals.ContainsKey(name)) { RecordedData.Signals[name] = new List<double>(); RecordedData.IsDigital[name] = false; }
                            RecordedData.Signals[name].Add(d);
                        }
                        catch { }
                    }
                };

                foreach (var m in _context.Memory.Values)
                {
                    if (m.Direction == ElementDirection.Member) continue;

                    if (m.CurrentValue is Array arr)
                    {
                        for (int i = 0; i < arr.Length; i++) recordValue($"{m.Name}[{i}]", arr.GetValue(i));
                    }
                    else if (m.CurrentValue is Dictionary<string, object> dict)
                    {
                        foreach (var kvp in dict) recordValue($"{m.Name}.{kvp.Key}", kvp.Value);
                    }
                    else
                    {
                        recordValue(m.Name, m.CurrentValue);
                    }
                }
            }
        }

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
                    if (m.Direction == ElementDirection.Member || m.Direction == ElementDirection.Input) continue;

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
