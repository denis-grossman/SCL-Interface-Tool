using SCL_Interface_Tool.Interfaces;
using SCL_Interface_Tool.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SCL_Interface_Tool.Parsers
{
    public class RegexSclParser : ISclParser
    {
        // Matches: Name {Attributes} : DataType := InitialValue; // Comment
        // Tolerates missing attributes, values, and comments.
        private readonly Regex _varRegex = new Regex(
            @"^\s*(?<name>[a-zA-Z0-9_]+)\s*(?:\{(?<attr>[^}]+)\})?\s*:\s*(?<type>[a-zA-Z0-9_\[\]\.]+)\s*(?::=\s*(?<val>[^;]+))?\s*;?\s*(?://(?<comment>.*))?$",
            RegexOptions.Compiled);

        private readonly Regex _blockStartRegex = new Regex(
            @"^\s*(FUNCTION_BLOCK|FUNCTION)\s+""?([^""\s]+)""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public List<SclBlock> Parse(string sclText, out List<string> errors)
        {
            var blocks = new List<SclBlock>();
            errors = new List<string>();

            SclBlock currentBlock = null;
            ElementDirection currentDirection = ElementDirection.None;

            var lines = sclText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
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
                        continue;
                    }

                    if (currentBlock == null) continue; // Skip lines outside blocks

                    // 2. Track Variable Sections
                    var upperLine = line.Trim().ToUpper();
                    if (upperLine.StartsWith("VAR_INPUT")) { currentDirection = ElementDirection.Input; continue; }
                    if (upperLine.StartsWith("VAR_OUTPUT")) { currentDirection = ElementDirection.Output; continue; }
                    if (upperLine.StartsWith("VAR_IN_OUT")) { currentDirection = ElementDirection.InOut; continue; }
                    if (upperLine.StartsWith("VAR_TEMP")) { currentDirection = ElementDirection.Temp; continue; }
                    if (upperLine.StartsWith("VAR CONSTANT")) { currentDirection = ElementDirection.Constant; continue; }
                    if (upperLine == "VAR") { currentDirection = ElementDirection.Static; continue; }
                    if (upperLine == "END_VAR" || upperLine.StartsWith("END_FUNCTION")) { currentDirection = ElementDirection.None; continue; }
                    if (upperLine.StartsWith("TITLE =")) { currentBlock.Title = line.Substring(line.IndexOf('=') + 1).Trim(); continue; }

                    // 3. Parse Variable inside a section
                    if (currentDirection != ElementDirection.None)
                    {
                        var varMatch = _varRegex.Match(line);
                        if (varMatch.Success)
                        {
                            currentBlock.Elements.Add(new InterfaceElement
                            {
                                Name = varMatch.Groups["name"].Value,
                                Attributes = varMatch.Groups["attr"].Value.Trim(),
                                DataType = varMatch.Groups["type"].Value.Trim(),
                                InitialValue = varMatch.Groups["val"].Value.Trim(),
                                Comment = varMatch.Groups["comment"].Value.Trim(),
                                Direction = currentDirection
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
