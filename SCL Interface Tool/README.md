# 🏭 SCL Interface Extractor, Editor, Simulator & FBD Generator

![.NET](https://img.shields.io/badge/.NET-WinForms-512BD4?style=flat&logo=.net)
![Siemens SCL](https://img.shields.io/badge/Language-SCL%20%2F%20IEC--61131--3-blue)
![Roslyn](https://img.shields.io/badge/Engine-Roslyn_C%23_Scripting-purple)
![License](https://img.shields.io/badge/License-MIT-green)

A powerful, standalone Windows desktop application designed for Siemens PLC Programmers (TIA Portal / STEP 7). 

This tool provides a high-performance **SCL code editor**, automatically extracts block interfaces, generates pixel-perfect **Function Block Diagram (FBD)** images, features a built-in **AI/LLM prompt generator**, and now includes a **Live Local Simulation Engine** to test your logic instantly without needing TIA Portal or PLCSIM.

![App Screenshot](docs/screenshot.png) *(Note: Add the new simulation screenshot here)*

## ✨ Key Features

### ▶️ Live Local SCL Simulation Engine (New!)
* **No PLC Required**: Instantly transpile, compile, and execute your Siemens SCL logic locally using a built-in Roslyn C# scripting engine.
* **Interactive Watch Table**: Monitor and force variables in real-time. Automatically flattens complex `STRUCT` and `ARRAY` data types into individual, easily editable rows.
* **Animated FBD View**: Watch your block execute live! Dynamic tags are overlaid directly onto the generated FBD image (Green for `TRUE`, Grey for `FALSE`, plus live numeric/string values).
* **Extensive SCL Support**: Fully supports `IF`, `CASE`, `FOR`, `WHILE`, `REPEAT...UNTIL`, standard math/string/bitwise functions, type conversions, edge triggers (`R_TRIG`, `F_TRIG`), timers (`TON`, `TOF`, `TP`), and counters (`CTU`, `CTD`).
* **Real-Time Diagnostics**: Track code execution cycle times down to the microsecond (µs), including Min/Max cycle tracking and total scan counts.
* **Hot Reload**: Modify your SCL code on the fly and click **"Apply Code Changes"** to seamlessly recompile and resume testing without losing your forced memory states.

### 📝 High-Performance SCL Code Editor
* **Flicker-Free Text Engine**: Built on `FastColoredTextBox` for a true Notepad++ style editing experience capable of handling massive SCL files effortlessly.
* **Real-time Syntax Highlighting**: Custom highlighting rules optimized for SCL (Keywords, Timers, Math functions, Strings, Comments).
* **Smart Recursive Folding**: Accurately collapses nested `IF`, `CASE`, `FOR`, `REGION`, and `FUNCTION_BLOCK` sections deep-to-surface. Intelligently ignores keywords hidden inside strings or comments.
* **Persistent Workspace**: The app automatically saves your code, window layouts, splitter positions, and hidden grid columns upon closing and restores them on your next launch.
* **File I/O**: Quickly import from or export to `.scl` or `.txt` files.

### 🔍 Interface Extraction & Parsing
* **Multi-Block Support**: Paste a file containing dozens of `FUNCTION_BLOCK`, `FUNCTION`, `DATA_BLOCK`, and `TYPE` (UDT/ENUM) definitions. The tool handles them all simultaneously.
* **Intelligent Parsing**: Accurately separates **Data Types**, **Initial Values** (`:=`), **System Attributes/Pragmas** (`{...}`), and **Comments** (`//` and `(* *)`).
* **Interactive Data Grid**: 
  * Auto-sizing columns that adapt to your variable names.
  * Right-click header to easily toggle column visibility (saved automatically).
  * Copy selections to the clipboard (with or without headers) for easy pasting into Excel or TIA Portal.

### 🖼️ TIA-Style FBD Image Generation
* **Pixel-Perfect Rendering**: Automatically generates an FBD representation of your block that looks exactly like Siemens TIA Portal.
* **Dynamic Layout Engine**: Automatically measures text length and calculates block heights and widths so long variable names *never* overlap.
* **Instance DB Headers**: Automatically renders the dashed `"iDB_<BlockName>"` Instance Data Block header for Function Blocks.
* **Smart Comment Wrapping**: Toggle on "Show Comments" to render multi-line comments directly on the FBD pins.
* **Export**: Copy the generated block diagram directly to your clipboard or save it as a high-resolution `.png` file.

### 🤖 Built-In AI / LLM Integrations
Streamline your workflow by instantly formatting your SCL code into highly optimized prompts ready to be pasted into ChatGPT, Claude, or DeepSeek. 
* Includes customizable, built-in engineering prompts:
  * **Code Review:** Identify bugs, race conditions, and best practice violations.
  * **Explain Logic:** Break down complex state machines for junior engineers.
  * **Generate Test Cases:** Create Markdown QA test matrices (nominal and edge cases).
  * **Create Documentation:** Generate external manual-ready documentation for your blocks.
  * **Soft Refactoring:** Add regions and clean up comments without changing execution flow.
  * **Deep Refactoring:** Architecturally optimize execution speed and simplify complex nested conditions.
* Manage, edit, and save your own custom prompts via the Settings menu.

## 🚀 Use Cases
* **Logic Commissioning & Pre-Testing**: Write and fully simulate complex math, array sorting, or state machine logic entirely offline before ever opening TIA Portal.
* **AI-Assisted Development**: Rapidly generate test cases or refactor legacy SCL code using the built-in LLM prompt generator.
* **Documentation**: Instantly generate visual FBD representations of your SCL logic blocks for user manuals, functional specifications, or handover documents.
* **Code Review**: Quickly extract and review the interface of massive, undocumented SCL files.
* **Reverse Engineering**: Analyze exported SCL source files from older projects to understand data structures and block inputs/outputs.

## 🛠️ Built With
* **C# / .NET** (Windows Forms)
* **[Microsoft.CodeAnalysis.CSharp.Scripting](https://github.com/dotnet/roslyn)** - Roslyn compiler for on-the-fly SCL-to-C# transpilation and execution.
* **[FastColoredTextBox (FCTB)](https://github.com/PavelTorgashov/FastColoredTextBox)** - High-performance text editor component.
* **System.Drawing (GDI+)** - Custom rendering engine for FBD graphics and live simulation animation.
* **System.Text.Json** - Settings and prompt serialization.

## 💻 Getting Started

### Prerequisites
* Visual Studio 2022 (or newer).
* .NET SDK (8.0 or newer recommended).

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/YourUsername/SCL-Interface-Extractor.git
   ```
2. Open the `.sln` file in Visual Studio.
3. Restore NuGet packages (Ensure `FastColoredTextBox` and `Microsoft.CodeAnalysis.CSharp.Scripting` are downloaded).
4. Build and run the application (`F5`).

## 📖 How to Use
1. **Paste or Import** your SCL code into the editor on the left. Include any `TYPE` or `DATA_BLOCK` dependencies above your main block.
2. Click **Parse SCL**. The tool will analyze the syntax and extract the blocks.
3. Select a specific block from the **"Select Block"** dropdown menu.
4. Click **Simulate Block** to open the live environment.
5. In the Simulation Window, click **▶ Start**, then double-click values in the grid to test your logic live!
6. Click **Generate FBD Image** to open the static, interactive image preview window for documentation export.
7. Use the **🤖 Copy for LLM** dropdown on the top toolbar to instantly format your code for AI analysis.

## 🤝 Contributing
Contributions are welcome! If you have suggestions for improving the parser, expanding the simulated standard library, or adding new FBD styling options, please fork the repository and create a pull request.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License
Distributed under the MIT License. See `LICENSE` for more information.

***
*Disclaimer: This tool is an independent open-source project and is not affiliated with, endorsed by, or sponsored by Siemens AG. TIA Portal and STEP 7 are trademarks of Siemens.*