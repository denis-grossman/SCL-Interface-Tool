// --- File: AppSettings.cs ---
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms; // Required for FormWindowState

namespace SCL_Interface_Tool.Models
{
    public class LlmPrompt
    {
        public string Name { get; set; }
        public string Text { get; set; }
    }

    public class AppSettings
    {
        public string LastCode { get; set; } = "";
        public int MainSplitterDistance { get; set; } = 480; // Kept for legacy compatibility
        public int RightSplitterDistance { get; set; } = -1; // Kept for legacy compatibility
        public List<string> HiddenColumns { get; set; } = new List<string>();

        // --- NEW: Window State & Dimensions ---
        public int MainFormWidth { get; set; } = 1300;
        public int MainFormHeight { get; set; } = 800;
        public FormWindowState MainFormState { get; set; } = FormWindowState.Normal;

        public int SimFormWidth { get; set; } = 1400;
        public int SimFormHeight { get; set; } = 850;
        public FormWindowState SimFormState { get; set; } = FormWindowState.Normal;

        // --- NEW OLLAMA SETTINGS ---
        public string OllamaApiUrl { get; set; } = "http://localhost:11434";
        public string OllamaModelName { get; set; } = "deepseek-coder";

        public List<LlmPrompt> Prompts { get; set; } = new List<LlmPrompt>();
        public string ActivePromptName { get; set; } = "Generate Unit Test Script";

        // --- DIRECTORY HELPERS ---
        public static string GetAppFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "SclInterfaceTool");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return folder;
        }

        private static string GetSettingsFilePath() => Path.Combine(GetAppFolder(), "settings.json");

        // NEW: Layout File Paths for DockPanel Suite
        public static string GetMainLayoutFilePath() => Path.Combine(GetAppFolder(), "main_layout.xml");
        public static string GetSimLayoutFilePath() => Path.Combine(GetAppFolder(), "sim_layout.xml");

        public static AppSettings Load()
        {
            string path = GetSettingsFilePath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings?.Prompts == null || settings.Prompts.Count == 0)
                        settings?.RestoreDefaultPrompts();
                    return settings ?? new AppSettings();
                }
                catch (JsonException)
                {
                    File.Move(path, path + ".corrupted", overwrite: true);
                }
            }

            var defaultSettings = new AppSettings();
            defaultSettings.RestoreDefaultPrompts();
            return defaultSettings;
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetSettingsFilePath(), json);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Failed to save settings: {ex.Message}");
            }
        }

        public void RestoreDefaultPrompts()
        {
            Prompts = new List<LlmPrompt>
            {
                new LlmPrompt
                {
                    Name = "Generate Unit Test Script",
                    Text = @"You are a strict, Hostile QA Automation Engineer specializing in Siemens PLC logic. Your goal is to write a test script that CATCHES bugs, commissioning bypasses, and commented-out logic. 

CRITICAL RULES FOR BLACK-BOX TESTING:
1. Test the Specification, NOT the Broken Code: Assume the provided SCL code body is full of bugs and bypasses (e.g., `///#fault := TRUE`). Do NOT write assertions designed to ""pass"" broken code! Your assertions MUST be based entirely on the intended specification derived from the variable comments (VAR_INPUT / VAR_OUTPUT) and `REGION` headers.
   - Example: If a comment says `Fault: 1=Latched Fault`, and you drop a healthy permissive input, you MUST `ASSERT Fault == TRUE`. If the actual code is broken and doesn't latch the fault, the simulator will correctly flag it as a FAIL.
2. Isolated Environment (No Magic): The simulator executes this block in a vacuum. External hardware does NOT exist. If the logic expects a pulsing clock, a rising edge, or a mechanical feedback (like a contactor or VFD run signal), you MUST explicitly toggle those inputs manually using `SET` commands.
3. Scan Cycle & Timers: PLCs evaluate timers per scan. To test a timer, you MUST execute `RUN 1 SCANS` to register the input BEFORE advancing time! 
   - CORRECT: SET Start = TRUE -> RUN 1 SCANS -> RUN 1000 MS -> ASSERT Out == TRUE
   - INCORRECT: SET Start = TRUE -> RUN 1000 MS
4. State Management: If you inject a fault, you MUST explicitly restore the input to its healthy state and test the recovery sequence.
5. Counters: If a variable increments during the test, ensure your assertions check for the NEW incremented value.

DSL SYNTAX:
- SET [VarName] = [Value]
- RUN [X] SCANS
- RUN [X] MS
- ASSERT [VarName] == [Value]

Provide ONLY the raw script code. No markdown formatting (```) or conversational text.
                Source SCL Code to be tested: "
            },
            new LlmPrompt { Name = "Code Review", Text = "You are a Senior Siemens PLC Automation Engineer. Please perform a rigorous code review of the following SCL (Structured Control Language) code. Focus on:\n1. Potential logical bugs, race conditions, or unhandled edge cases.\n2. Safety considerations (e.g., missing interlocks).\n3. Siemens TIA Portal best practices (e.g., optimized block access, data type alignment, timer usage).\n4. Code readability and maintainability.\nProvide constructive feedback and code snippets for suggested improvements.\n\nCode:\n" },
            new LlmPrompt { Name = "Explain Logic", Text = "You are an expert control systems engineer mentoring a junior automation engineer. Explain the exact functionality of the following Siemens SCL logic in a clear and educational manner. Structure your explanation as follows:\n1. High-Level Purpose: What does this block do?\n2. Key Interface Elements: Explain the critical inputs and outputs.\n3. Step-by-Step Logic Breakdown: Explain the state machines, timers, or mathematical operations occurring inside.\n4. Real-world physical analogy (if applicable).\n\nCode:\n" },
            new LlmPrompt { Name = "Generate Test Cases", Text = "As a Software QA Engineer for industrial automation, generate a comprehensive Test Plan for the following Siemens SCL Function Block. Include:\n1. Nominal/Happy Path scenarios.\n2. Edge cases (e.g., simultaneous conflicting inputs, zero values).\n3. Timeout, fault, and recovery scenarios.\n4. Initialization/First-scan behavior.\nFormat the output as a Markdown test matrix with columns for 'Test ID', 'Initial State', 'Action/Inputs', and 'Expected Output'.\n\nCode:\n" },
            new LlmPrompt { Name = "Create Documentation", Text = "Generate a professional, ready-to-use Markdown documentation file for the following Siemens SCL block so it can be added to an external software manual. Structure the document as follows:\n# [Block Name]\n## 📖 Description\n(Detailed explanation of what the block does)\n## 🎯 Use Cases\n(Where and why an engineer should use this block in a machine)\n## 🔌 Interface\n(Markdown tables for Inputs, Outputs, InOuts, and Statics with their data types and descriptions)\n## ⚠️ Special Cases & Considerations\n(What developers must take into account when calling this block, e.g., required pre-conditions or hardware setup).\n\nCode:\n" },
            new LlmPrompt { Name = "Soft Refactoring", Text = "Please perform a 'Soft Refactoring' on the following Siemens SCL code to improve readability and maintainability without altering its core execution logic. Apply the following rules strictly:\n1. Split the logic into logical sections using Siemens `REGION` and `END_REGION` pragmas.\n2. Add a standard block header comment at the top containing a short description, special cases, and usage notes.\n3. Add concise inline comments explaining the *why* behind complex conditions (not just the *what*).\n4. Ensure the output is valid TIA Portal SCL code ready to be pasted directly back into the editor.\n\nCode:\n" },
            new LlmPrompt { Name = "Deep Refactoring", Text = "You are an Expert Siemens Automation Architect. Please perform a 'Deep Refactoring' of the following SCL code. Your goal is to optimize the architecture, execution speed, and logic complexity while maintaining 100% functional equivalence. Please apply the following:\n1. Simplify complex nested IF/ELSE conditions (e.g., using Boolean algebra, CASE statements, or early RETURNs).\n2. Optimize variable usage (e.g., reducing redundant reads/writes, appropriate use of Temp vs Static variables).\n3. Eliminate dead code, redundant evaluations, and anti-patterns.\n4. Modernize the logic to follow the latest IEC 61131-3 and TIA Portal best practices.\nProvide a summary bullet list of the architectural changes you made and the reasoning behind them, followed by the complete, refactored SCL code block.\n\nCode:\n" },
            new LlmPrompt { Name = "Add New Feature", Text = "You are an Expert Siemens PLC Automation Engineer. Please add a new feature to the following Siemens SCL code based strictly on the instructions found within the inline comments (e.g., '// TODO:', '// FEATURE:', or general descriptive comments). \n\nIMPORTANT RULES:\n1. Context: Analyze the existing block interface and architecture to ensure your new logic blends seamlessly.\n2. Interface Updates: If the new feature requires new parameters, add them to the correct section (VAR_INPUT, VAR_OUTPUT, VAR, etc.) and provide appropriate data types and comments.\n3. No Code Deletion: Do not remove or refactor existing logic unless the feature instructions explicitly tell you to do so.\n4. Output: Provide ONLY the raw, complete, updated SCL code without markdown formatting (```) so I can paste it directly back into my editor.\n\nCode:\n" }
        };
            ActivePromptName = "Generate Unit Test Script";
        }
    }
}