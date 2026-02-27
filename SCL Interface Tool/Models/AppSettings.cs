using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
        public int MainSplitterDistance { get; set; } = 480;
        public int RightSplitterDistance { get; set; } = -1;
        public List<string> HiddenColumns { get; set; } = new List<string>();
        public List<LlmPrompt> Prompts { get; set; } = new List<LlmPrompt>();
        public string ActivePromptName { get; set; } = "Code Review";

        private static string GetSettingsFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "SclInterfaceTool");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Path.Combine(folder, "settings.json");
        }

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
                    // The file is corrupted. Rename it so the user doesn't lose it entirely, 
                    // but the app can generate a fresh one.
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
                    Name = "Code Review",
                    Text = "You are a Senior Siemens PLC Automation Engineer. Please perform a rigorous code review of the following SCL (Structured Control Language) code. Focus on:\n1. Potential logical bugs, race conditions, or unhandled edge cases.\n2. Safety considerations (e.g., missing interlocks).\n3. Siemens TIA Portal best practices (e.g., optimized block access, data type alignment, timer usage).\n4. Code readability and maintainability.\nProvide constructive feedback and code snippets for suggested improvements.\n\nCode:\n"
                },
                new LlmPrompt
                {
                    Name = "Explain Logic",
                    Text = "You are an expert control systems engineer mentoring a junior automation engineer. Explain the exact functionality of the following Siemens SCL logic in a clear and educational manner. Structure your explanation as follows:\n1. High-Level Purpose: What does this block do?\n2. Key Interface Elements: Explain the critical inputs and outputs.\n3. Step-by-Step Logic Breakdown: Explain the state machines, timers, or mathematical operations occurring inside.\n4. Real-world physical analogy (if applicable).\n\nCode:\n"
                },
                new LlmPrompt
                {
                    Name = "Generate Test Cases",
                    Text = "As a Software QA Engineer for industrial automation, generate a comprehensive Test Plan for the following Siemens SCL Function Block. Include:\n1. Nominal/Happy Path scenarios.\n2. Edge cases (e.g., simultaneous conflicting inputs, zero values).\n3. Timeout, fault, and recovery scenarios.\n4. Initialization/First-scan behavior.\nFormat the output as a Markdown test matrix with columns for 'Test ID', 'Initial State', 'Action/Inputs', and 'Expected Output'.\n\nCode:\n"
                },
                new LlmPrompt
                {
                    Name = "Create Documentation",
                    Text = "Generate a professional, ready-to-use Markdown documentation file for the following Siemens SCL block so it can be added to an external software manual. Structure the document as follows:\n# [Block Name]\n## 📖 Description\n(Detailed explanation of what the block does)\n## 🎯 Use Cases\n(Where and why an engineer should use this block in a machine)\n## 🔌 Interface\n(Markdown tables for Inputs, Outputs, InOuts, and Statics with their data types and descriptions)\n## ⚠️ Special Cases & Considerations\n(What developers must take into account when calling this block, e.g., required pre-conditions or hardware setup).\n\nCode:\n"
                },
                new LlmPrompt
                {
                    Name = "Soft Refactoring",
                    Text = "Please perform a 'Soft Refactoring' on the following Siemens SCL code to improve readability and maintainability without altering its core execution logic. Apply the following rules strictly:\n1. Split the logic into logical sections using Siemens `REGION` and `END_REGION` pragmas.\n2. Add a standard block header comment at the top containing a short description, special cases, and usage notes.\n3. Add concise inline comments explaining the *why* behind complex conditions (not just the *what*).\n4. Ensure the output is valid TIA Portal SCL code ready to be pasted directly back into the editor.\n\nCode:\n"
                },
                new LlmPrompt
                {
                    Name = "Deep Refactoring",
                    Text = "You are an Expert Siemens Automation Architect. Please perform a 'Deep Refactoring' of the following SCL code. Your goal is to optimize the architecture, execution speed, and logic complexity while maintaining 100% functional equivalence. Please apply the following:\n1. Simplify complex nested IF/ELSE conditions (e.g., using Boolean algebra, CASE statements, or early RETURNs).\n2. Optimize variable usage (e.g., reducing redundant reads/writes, appropriate use of Temp vs Static variables).\n3. Eliminate dead code, redundant evaluations, and anti-patterns.\n4. Modernize the logic to follow the latest IEC 61131-3 and TIA Portal best practices.\nProvide a summary bullet list of the architectural changes you made and the reasoning behind them, followed by the complete, refactored SCL code block.\n\nCode:\n"
                }
            };
            ActivePromptName = "Explain Logic";
        }

    }

}