# SCL Ninja

A powerful, standalone "little beast" of a desktop application designed for Siemens PLC Automation Engineers. This tool allows you to parse raw Siemens SCL (Structured Control Language) code, automatically generate TIA Portal-style FBD-like graphical views, simulate execution in real-time without physical hardware, run automated Unit Tests (TDD), and leverage Local AI (Ollama) to aggressively QA and refactor your logic securely offline.

**Author:**  DNT  
**Website:** [www.lacum.ru](http://www.lacum.ru) 

📥 **[Download the latest release here](https://github.com/MY_GITHUB_NAME/SCL_Ninja/releases)**

---

## ✨ Key Features

### 🧩 1. Advanced SCL Parsing & FBD-Like Generation
*   **Intelligent Parsing:** Instantly extracts variables (`VAR_INPUT`, `VAR_OUTPUT`, `VAR_IN_OUT`, `VAR`, `STAT`, `TEMP`) from raw SCL code.
*   **FBD-Like Graphics:** Dynamically renders a clean, pixel-perfect Function Block Diagram-style image to visualize the block's interface.
*   **Interactive View:** Zoom, pan, and hover over pins to read parameter comments. Copy to clipboard or export as PNG for machine documentation.

### ⚙️ 2. Real-Time Simulation Engine
*   **Roslyn C# Transpilation:** Converts SCL logic to C# and executes it in a high-speed background loop (OB1-style continuous scan).
*   **Standard Library Support:** Accurately simulates IEC Timers (`TON`, `TOF`, `TP`, `TONR`), Counters (`CTU`, `CTD`), Edge Detectors (`R_TRIG`, `F_TRIG`), and standard math/string functions.
*   **Live Watch Table:** Monitor and force booleans, primitives, arrays, and nested structures in real-time without pausing execution.

### 🧪 3. Automated Unit Testing (TDD)
*   **Custom Test DSL:** Write highly readable testing scripts to validate your block's behavior deterministically.
*   **Virtual Clock:** Timers execute instantly in the test environment (e.g., `RUN 1000 MS` evaluates exactly 1 second of logic without making you wait).
*   **Delta Logging:** Keeps test reports clean by only logging variables that actually changed state during a command.
*   **Save/Load:** Build a permanent library of `.scltest` files for your standard blocks.

### 🤖 4. Offline AI Co-Pilot (Ollama Integration)
*   **Complete Privacy:** Connects to your local Ollama instance so your proprietary code never leaves your laptop.
*   **Real-Time Streaming:** Watch the AI "think" and stream its response live into the UI without freezing the application.
*   **Hostile QA Generation:** Click a button to have the AI write an aggressive Unit Test script designed specifically to catch commissioning bypasses and bugs based on your comments.
*   **One-Click Code Apply:** Ask the AI to refactor your code, and click "Apply to Editor" to safely extract the SCL and update your workspace instantly.

---

## ⚠️ Supported Features & Limitations

The transpiler supports a massive subset of IEC 61131-3 standard logic, but it is a standalone simulation engine, not a 1:1 replacement for an actual Siemens CPU.

**✅ Fully Supported:**
* **Timers:** `TON`, `TOF`, `TP`, `TONR` (Evaluated deterministically per scan)
* **Counters:** `CTU`, `CTD`, `CTUD`
* **Edge Detection:** `R_TRIG`, `F_TRIG`
* **Math/Trig:** `ABS`, `SQRT`, `SIN`, `COS`, `TAN`, `LIMIT`, `SCALE_X`, `NORM_X`
* **Logic:** `IF/ELSIF`, `CASE`, `FOR`, `WHILE`, `REPEAT` loops.
* **Data Types:** Arrays (e.g., `Array[0..10] of Int`), Structs, Primitives.

**❌ Current Limitations (Will cause compile errors):**
* **Hardware-specific Functions:** Instructions interacting with physical IO modules or Siemens diagnostics (e.g., `RD_SYS_T`, `WRREC`, `DPWR_DAT`) cannot be simulated.
* **Nested FB Calls:** Currently, the simulator executes a single, standalone block. You cannot simulate a block that calls another custom `FUNCTION_BLOCK` internally.
* **Absolute Addressing:** Direct memory references (e.g., `%M0.0`, `%DB1.DBW2`) are not supported. Use symbolic tags.

---

## 🚀 Getting Started

### Prerequisites
*   Windows OS (WinForms)
*   .NET 8.0 (or higher)

### 🦙 Setting up Local AI (Ollama)
To use the offline AI Co-Pilot, you need to install Ollama. It acts as a lightweight, invisible server running on your computer that SCL Ninja communicates with.

**Step 1: Install Ollama**
1. Go to **[Ollama.com/download](https://ollama.com/download)** and download the Windows installer.
2. Run the installer. Once finished, Ollama will run quietly in your Windows system tray.

**Step 2: Download a Coding Model**
Ollama needs an AI "brain" (model) to run. For standard laptops without dedicated NVIDIA graphics cards, small coding models are highly recommended so they run fast on your CPU.
1. Open Windows **Command Prompt** (cmd) or **PowerShell**.
2. Type the following command to download and run a highly capable, lightweight model (a 1.5 Billion parameter model, approx ~1GB download):
   ```bash
   ollama run qwen2.5-coder:1.5b
   ```
   *(Alternatively, if you have 16GB+ RAM and want a smarter model, try: `ollama run deepseek-coder:6.7b`)*
3. Wait for the download to finish. Once you see a `>>>` prompt, the model is successfully installed! You can close the command prompt.

**Step 3: Connect SCL Ninja**
1. Open SCL Ninja and click the **⚙️ Settings** button on the top toolbar.
2. Under the "Offline Local AI" section, ensure the API URL is set to `http://localhost:11434`.
3. Click the **🔄 Fetch Models** button. The app will connect to Ollama and populate the dropdown.
4. Select your downloaded model (`qwen2.5-coder:1.5b`) and click **Save & Close**. You are ready to use the AI!

---

## 📖 Automated Testing DSL Guide

The Automated Testing tab uses a custom, easy-to-read Domain Specific Language (DSL). Because the simulator executes in a perfect vacuum (no external hardware), **you must manually drive all physical inputs** (including clock pulses and run feedbacks).

### Syntax Rules
*   `SET [Variable] = [Value]` (Force an input, InOut, or static tag).
*   `RUN [X] SCANS` (Execute the logic loop X times).
*   `RUN [X] MS` (Advance the virtual clock by X milliseconds instantly).
*   `ASSERT [Variable] == [Value]` (Verify an output. Fails the test if it doesn't match).

### ⚠️ The Golden Rule of Timers
Just like a real Siemens PLC, a timer (`TON`, `TOF`, `TP`) must evaluate a change in state *during a scan* before it starts timing. **Always run 1 scan after changing an input before advancing time.**

```text
// ❌ WRONG (Timer won't start, virtual clock jumped before scan evaluated input)
SET StartMotor = TRUE
RUN 2000 MS
ASSERT MotorRunning == TRUE

// ✅ CORRECT
SET StartMotor = TRUE
RUN 1 SCANS         // <--- Timer sees IN = TRUE and registers start time
RUN 2000 MS         // <--- Virtual clock jumps 2 seconds
ASSERT MotorRunning == TRUE
```

## 🤝 Contributing
Feel free to open issues or submit pull requests to add support for missing standard library instructions, improve UI panel docking, or enhance the AI prompting strategies!
```