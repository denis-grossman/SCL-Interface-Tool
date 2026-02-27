# 🏭 SCL Interface Extractor, Editor & FBD Generator

![.NET](https://img.shields.io/badge/.NET-WinForms-512BD4?style=flat&logo=.net)
![Siemens SCL](https://img.shields.io/badge/Language-SCL%20%2F%20IEC--61131--3-blue)
![License](https://img.shields.io/badge/License-MIT-green)

A powerful, standalone Windows desktop application designed for Siemens PLC Programmers (TIA Portal / STEP 7). 

This tool provides a high-performance **SCL code editor**, automatically extracts block interfaces, generates pixel-perfect **Function Block Diagram (FBD)** images, and features built-in **AI/LLM prompt generation** to help you refactor, document, and review your code instantly.

![App Screenshot](docs/screenshot.png)

## ✨ Key Features

### 📝 High-Performance SCL Code Editor
* **Flicker-Free Text Engine**: Built on `FastColoredTextBox` for a true Notepad++ style editing experience capable of handling massive SCL files effortlessly.
* **Real-time Syntax Highlighting**: Custom highlighting rules optimized for SCL (Keywords, Timers, Math functions, Strings, Comments).
* **Smart Recursive Folding**: Accurately collapses nested `IF`, `CASE`, `FOR`, `REGION`, and `FUNCTION_BLOCK` sections deep-to-surface. Intelligently ignores keywords hidden inside strings or comments.
* **Persistent Workspace**: The app automatically saves your code, window layouts, splitter positions, and hidden grid columns upon closing and restores them on your next launch.
* **File I/O**: Quickly import from or export to `.scl` or `.txt` files.

### 🤖 Built-In AI / LLM Integrations
Streamline your workflow by instantly formatting your SCL code into highly optimized prompts ready to be pasted into ChatGPT, Claude, or DeepSeek. 
* Includes customizable, built-in engineering prompts:
  * **Code Review:** Identify bugs, race conditions, and TIA Portal best practice violations.
  * **Explain Logic:** Break down complex state machines for junior engineers.
  * **Generate Test Cases:** Create Markdown QA test matrices (nominal and edge cases).
  * **Create Documentation:** Generate external manual-ready documentation for your blocks.
  * **Soft Refactoring:** Add regions, clean up comments, and organize logic without changing execution flow.
  * **Deep Refactoring:** Architecturally optimize execution speed and simplify complex nested conditions.
* Manage, edit, and save your own custom prompts via the Settings menu.

### 🔍 Interface Extraction
* **Multi-Block Support**: Paste a file containing dozens of `FUNCTION_BLOCK`, `FUNCTION`, `DATA_BLOCK`, and `TYPE` (UDT) definitions. The tool handles them all simultaneously.
* **Intelligent Parsing**: Accurately separates **Data Types**, **Initial Values** (`:=`), **System Attributes/Pragmas** (`{...}`), and **Comments** (`//`).
* **Interactive Data Grid**: 
  * Auto-sizing columns that adapt to your variable names.
  * Right-click header to easily toggle column visibility (saved automatically).
  * Copy selections to the clipboard (with or without headers) for easy pasting into Excel or TIA Portal.

### 🖼️ TIA-Style FBD Image Generation
* **Pixel-Perfect Rendering**: Automatically generates an FBD representation of your block that looks exactly like Siemens TIA Portal.
* **Dynamic Layout Engine**: Automatically measures text length and calculates block heights and widths so long variable names *never* overlap.
* **Instance DB Headers**: Automatically renders the dashed `"iDB_<BlockName>"` Instance Data Block header for Function Blocks.
* **Smart Comment Wrapping**: Toggle on "Show Comments" to render multi-line comments directly on the FBD pins.
* **Interactive Preview**: Zoom in/out (50% to 200%), auto-centering, and hover over pins to reveal TIA-style yellow tooltips.
* **Export**: Copy the generated block diagram directly to your clipboard or save it as a high-resolution `.png` file.

## 🚀 Use Cases
* **AI-Assisted Development**: Rapidly generate test cases or refactor legacy SCL code using the built-in LLM prompt generator.
* **Documentation**: Instantly generate visual FBD representations of your SCL logic blocks for user manuals, functional specifications, or handover documents.
* **Code Review**: Quickly extract and review the interface of massive, undocumented SCL files without needing to open heavy TIA Portal instances.
* **Reverse Engineering**: Analyze exported SCL source files from older projects to understand data structures and block inputs/outputs.

## 🛠️ Built With
* **C# / .NET** (Windows Forms)
* **[FastColoredTextBox (FCTB)](https://github.com/PavelTorgashov/FastColoredTextBox)** - High-performance text editor component.
* **System.Drawing (GDI+)** - Custom rendering engine for FBD graphics.
* **System.Text.Json** - Settings and prompt serialization.

## 💻 Getting Started

### Prerequisites
* Visual Studio 2022 (or newer).
* .NET SDK (10.0 recommended).

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/YourUsername/SCL-Interface-Extractor.git
   ```
2. Open the `.sln` file in Visual Studio.
3. Restore NuGet packages (Ensure `FastColoredTextBox` or `FastColoredTextBox.Net5` is downloaded).
4. Build and run the application (`F5`).

## 📖 How to Use
1. **Paste or Import** your SCL code into the editor on the left.
2. Click **Parse SCL**. The tool will analyze the syntax and log any errors in the bottom panel.
3. Select a specific block from the **"Select Block"** dropdown menu.
4. View the parsed variables in the Data Grid.
5. Click **Generate FBD Image** (available for FBs and FCs) to open the interactive image preview window. You can copy the image to your clipboard from there.
6. Use the **🤖 Copy for LLM** dropdown on the top toolbar to instantly format your code for AI analysis.

## 🤝 Contributing
Contributions are welcome! If you have suggestions for improving the parser (e.g., handling deeply nested `STRUCT` arrays better) or adding new FBD styling options, please fork the repository and create a pull request.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License
Distributed under the MIT License. See `LICENSE` for more information.

***
*Disclaimer: This tool is an independent open-source project and is not affiliated with, endorsed by, or sponsored by Siemens AG. TIA Portal and STEP 7 are trademarks of Siemens.*