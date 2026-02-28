using SCL_Interface_Tool.Interfaces;
using SCL_Interface_Tool.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SCL_Interface_Tool.Parsers
{
    public class RegexSclParser : ISclParser
    {
        // Improved Regex: Accurately isolates DataType and Initial Value regardless of spaces/arrays
        private readonly Regex _varRegex = new Regex(
            @"^\s*(?<name>[a-zA-Z0-9_]+)\s*(?:\{(?<attr>[^}]+)\})?\s*:\s*(?<type>[^:=;]+?)(?:\s*:=\s*(?<val>[^;]+))?\s*;?\s*(?://\s*(?<comment>.*))?$",
            RegexOptions.Compiled);

        // Added DATA_BLOCK and TYPE support
        private readonly Regex _blockStartRegex = new Regex(
            @"^\s*(FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE)\s+""?([^""\s]+)""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public List<SclBlock> Parse(string sclText, out List<string> errors)
        {
            var blocks = new List<SclBlock>();
            errors = new List<string>();

            // Convert inline (* comment *) to // comment so the variable regex can extract them
            // This handles: VarName : Int; (* This is a comment *)
            sclText = Regex.Replace(sclText, @"\(\*\s*(.*?)\s*\*\)", m =>
            {
                string content = m.Groups[1].Value.Trim();
                // If the comment is on a line with other code, convert to // comment
                // If it spans multiple lines (contains newline), just remove it
                if (content.Contains("\n") || content.Contains("\r"))
                    return ""; // Remove multi-line block comments
                else
                    return "// " + content; // Convert single-line block comments to //
            });

            SclBlock currentBlock = null;
            ElementDirection currentDirection = ElementDirection.None;
            int elementIndex = 1;

            var lines = sclText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                // ... rest of the method stays exactly the same

                try
                {
                    // 1. Check for block start
                    var blockMatch = _blockStartRegex.Match(line);
                    if (blockMatch.Success)
                    {
                        currentBlock = new SclBlock
                        {
                            BlockType = blockMatch.Groups[1].Value.ToUpper(),
                            Name = blockMatch.Groups[2].Value
                        };
                        blocks.Add(currentBlock);
                        currentDirection = ElementDirection.None;
                        elementIndex = 1; // Reset index for new block
                        continue;
                    }

                    if (currentBlock == null) continue;

                    var upperLine = line.Trim().ToUpper();

                    // Skip struct/begin declarations inside DBs to avoid confusion
                    if (upperLine == "STRUCT" || upperLine == "END_STRUCT" || upperLine == "BEGIN") continue;

                    // 2. Track Variable Sections (for FB/FC)
                    if (upperLine.StartsWith("VAR_INPUT")) { currentDirection = ElementDirection.Input; continue; }
                    if (upperLine.StartsWith("VAR_OUTPUT")) { currentDirection = ElementDirection.Output; continue; }
                    if (upperLine.StartsWith("VAR_IN_OUT")) { currentDirection = ElementDirection.InOut; continue; }
                    if (upperLine.StartsWith("VAR_TEMP")) { currentDirection = ElementDirection.Temp; continue; }
                    if (upperLine.StartsWith("VAR CONSTANT")) { currentDirection = ElementDirection.Constant; continue; }
                    if (upperLine == "VAR") { currentDirection = ElementDirection.Static; continue; }
                    if (upperLine == "END_VAR" || upperLine.StartsWith("END_FUNCTION") || upperLine.StartsWith("END_DATA_BLOCK") || upperLine.StartsWith("END_TYPE"))
                    {
                        currentDirection = ElementDirection.None;
                        continue;
                    }
                    if (upperLine.StartsWith("TITLE =")) { currentBlock.Title = line.Substring(line.IndexOf('=') + 1).Trim(); continue; }

                    // 3. Evaluate Direction for DBs and UDTs (which often lack VAR sections)
                    ElementDirection activeDirection = currentDirection;
                    if (activeDirection == ElementDirection.None && (currentBlock.BlockType == "DATA_BLOCK" || currentBlock.BlockType == "TYPE"))
                    {
                        activeDirection = ElementDirection.Member;
                    }

                    // 4. Parse Variable
                    if (activeDirection != ElementDirection.None)
                    {
                        var varMatch = _varRegex.Match(line);
                        if (varMatch.Success)
                        {
                            currentBlock.Elements.Add(new InterfaceElement
                            {
                                Index = elementIndex++,
                                Name = varMatch.Groups["name"].Value.Trim(),
                                Attributes = varMatch.Groups["attr"].Value.Trim(),
                                DataType = varMatch.Groups["type"].Value.Trim(),
                                InitialValue = varMatch.Groups["val"].Value.Trim(),
                                Comment = varMatch.Groups["comment"].Value.Trim(),
                                Direction = activeDirection
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error parsing line: '{line}'. Exception: {ex.Message}");
                }
            }
            return blocks;
        }
    }
}
