# 🏭 SCL Interface Extractor & FBD Generator

![.NET](https://img.shields.io/badge/.NET-WinForms-512BD4?style=flat&logo=.net)
![Siemens SCL](https://img.shields.io/badge/Language-SCL%20%2F%20IEC--61131--3-blue)
![License](https://img.shields.io/badge/License-MIT-green)

A powerful, standalone Windows desktop application designed for Siemens PLC Programmers (TIA Portal / STEP 7). 

This tool automatically parses multi-block SCL (Structured Control Language) source code, extracts interface definitions (Inputs, Outputs, Statics, Constants, etc.), and generates pixel-perfect, TIA Portal-style **Function Block Diagram (FBD)** images.

![App Screenshot](docs/screenshot.png)

## ✨ Key Features

### 📝 Smart SCL Code Editor
* **Real-time Syntax Highlighting**: Custom highlighting rules optimized for SCL (Keywords, Timers, Math functions, Strings, Comments).
* **Smart Code Folding**: Accurately collapses `FUNCTION_BLOCK`, `IF`, `CASE`, `FOR`, and `REGION` blocks (intelligently ignores keywords hidden inside strings or comments).
* **Notepad++ Style Interface**: Includes line numbers, Undo/Redo history, and a live status bar tracking Lines, Length, and Cursor Position.
* **File I/O**: Quickly import from or export to `.scl` or `.txt` files.

### 🔍 Interface Extraction
* **Multi-Block Support**: Paste a file containing dozens of `FUNCTION_BLOCK`, `FUNCTION`, `DATA_BLOCK`, and `TYPE` (UDT) definitions. The tool handles them all.
* **Intelligent Parsing**: Accurately separates **Data Types**, **Initial Values** (`:=`), **System Attributes/Pragmas** (`{...}`), and **Comments** (`//`).
* **Interactive Data Grid**: 
  * Sort and filter interface elements.
  * Right-click header to easily toggle column visibility.
  * Copy selections to the clipboard (with or without headers) for easy pasting into Excel or documentation.
  * Hover over rows to view full comments.

### 🖼️ TIA-Style FBD Image Generation
* **Pixel-Perfect Rendering**: Automatically generates an FBD representation of your block that looks exactly like Siemens TIA Portal*.
* **Dynamic Layout**: Automatically calculates block heights, draws `EN/ENO` pins, and maps colored connection dots (Blue for Inputs, Green for Outputs).
* **Instance DB Headers**: Automatically renders the dashed `iDB_<BlockName>` Instance Data Block header for Function Blocks.
* **Smart Comment Wrapping**: Toggle on "Show Comments" to render multi-line comments directly on the FBD pins.
* **Interactive Preview**: Zoom in/out (50% to 200%) and hover over pins to reveal TIA-style yellow tooltips.
* **Export**: Save the generated block diagram as a high-resolution `.png` file.

## 🚀 Use Cases
* **Documentation**: Instantly generate visual FBD representations of your SCL logic blocks for user manuals, functional specifications, or handover documents.
* **Code Review**: Quickly extract and review the interface of massive, undocumented SCL files without needing to open heavy TIA Portal instances.
* **Reverse Engineering**: Analyze exported SCL source files from older projects to understand data structures and block inputs/outputs.

## 🛠️ Built With
* **C# / .NET** (Windows Forms)
* **[FastColoredTextBox (FCTB)](https://github.com/PavelTorgashov/FastColoredTextBox)** - High-performance text editor component.
* **System.Drawing (GDI+)** - Custom rendering engine for FBD graphics.

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
3. Restore NuGet packages (Ensure `FastColoredTextBox` is downloaded).
4. Build and run the application (`F5`).

## 📖 How to Use
1. **Paste or Import** your SCL code into the editor on the left.
2. Click **Parse SCL**. The tool will analyze the syntax and log any errors in the bottom panel.
3. Select a specific block from the **"Select Block"** dropdown menu.
4. View the parsed variables in the Data Grid.
5. Click **Generate FBD Image** (available for FBs and FCs) to open the interactive image preview window.
6. Toggle **Show Comments** or use the **Save as PNG** button to export your FBD block.

## 🧩 Example Supported SCL Code

The parser easily handles complex definitions, including mixed pragmas and initial values:

```pascal
FUNCTION_BLOCK "FB_ValveControl"
TITLE = Valve Control
{ S7_Optimized_Access := 'FALSE' }
VERSION : 0.1

   VAR_INPUT 
      cmdOpen : Bool;   // 1=Command to open valve
      cmdClose : Bool;  // 1=Command to close valve
   END_VAR

   VAR_OUTPUT 
      stsOpen { ExternalWritable := 'False'} : Bool := FALSE; // Valve is open
   END_VAR

   VAR 
      TmrTimeout {InstructionName := 'TON'; LibVersion := '1.0'} : TON;
   END_VAR

BEGIN
    // SCL Logic goes here...
    IF #cmdOpen THEN
        #stsOpen := TRUE;
    END_IF;
END_FUNCTION_BLOCK
```

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