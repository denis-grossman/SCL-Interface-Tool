using SCL_Interface_Tool.ImageGeneration;
using SCL_Interface_Tool.Models;
using SCL_Interface_Tool.Simulation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using ExecutionContext = SCL_Interface_Tool.Simulation.ExecutionContext;

namespace SCL_Interface_Tool
{
    // Custom class to ensure DockPanel Suite uniquely remembers each panel
    public class SimToolPane : DockContent
    {
        private readonly string _persistString;
        public SimToolPane(string persistString)
        {
            _persistString = persistString;
        }
        protected override string GetPersistString()
        {
            return _persistString;
        }
    }

    public partial class SimulationForm : Form
    {
        // Session data storage to persist values when form is closed but app stays open
        public class SessionData
        {
            
            public bool IsFirstRun { get; set; } = true;
            public string ScriptText { get; set; } = "// Click '🤖 Generate' to automatically write a script\n";
            public string TestLogText { get; set; } = "";
            public string AiLogText { get; set; } = "";
            public List<string> CheckedVariables { get; set; } = new List<string>();
        }

        private static Dictionary<string, SessionData> _sessions = new Dictionary<string, SessionData>();

        private static SimulationForm _activeInstance = null;
        private AppSettings _appSettings;
        private SclBlock _block;
        private Func<string> _getCode;
        private Bitmap _baseImage;
        private ExecutionContext _context;
        private SimulationEngine _engine;

        // Core UI Elements
        private DockPanel _dockPanel;
        private SimToolPane _paneWatch;
        private SimToolPane _paneScript;
        private SimToolPane _paneFbd;
        private SimToolPane _paneStats;

        // Logic Analyzer Elements
        private SimToolPane _paneVariables;
        private SimToolPane _paneChart;
        private ScottPlot.FormsPlot _plotAnalyzer;
        private TreeView _tvSignals;
        private TimingData _lastTimingData;
        private bool _isUpdatingTree = false;

        // Colors Palette for Analyzer
        private readonly Color[] _plotColors = new Color[] {
            Color.FromArgb(220, 20, 60),    // Crimson
            Color.FromArgb(0, 100, 0),      // DarkGreen
            Color.FromArgb(0, 0, 205),      // MediumBlue
            Color.FromArgb(255, 140, 0),    // DarkOrange
            Color.FromArgb(139, 0, 139),    // DarkMagenta
            Color.FromArgb(0, 139, 139),    // DarkCyan
            Color.FromArgb(139, 69, 19),    // SaddleBrown
            Color.FromArgb(75, 0, 130),     // Indigo
            Color.FromArgb(255, 20, 147),   // DeepPink
            Color.FromArgb(128, 128, 0)     // Olive
        };

        // Output Panels
        private SimToolPane _paneTestLog;
        private SimToolPane _paneAiLog;
        private FastColoredTextBoxNS.FastColoredTextBox _rtbScript;
        private RichTextBox _rtbTestLog;
        private RichTextBox _rtbAiLog;

        private SimDataGridView _dgvWatch;
        private PictureBox _pbLive;
        private Label _lblStats;
        private ToolStripLabel _lblStatus;
        private System.Windows.Forms.Timer _uiTimer;
        private BindingList<WatchRow> _watchRows;
        private Font _boldFont;

        private long _scanCount;
        private long _minCycleUs = long.MaxValue;
        private long _maxCycleUs;
        private long _lastCycleUs;

        private CancellationTokenSource _ollamaCts;
        private ToolStripButton _btnStopAi;

        public class WatchRow
        {
            public string Name { get; set; }
            public string DataType { get; set; }
            public string Direction { get; set; }
            public string LiveValue { get; set; }
            public string Comment { get; set; }
            public string ParentName { get; set; }
            public object SubKey { get; set; }
            public bool IsBool { get; set; }
        }

        public class SimDataGridView : DataGridView
        {
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Enter && IsCurrentCellInEditMode)
                {
                    int row = CurrentCell.RowIndex, col = CurrentCell.ColumnIndex;
                    EndEdit();
                    CurrentCell = this[col, row];
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        public SimulationForm(SclBlock block, Func<string> getCode, GdiFbdImageGenerator imageGen)
        {
            // --- Singleton Enforcement ---
            if (_activeInstance != null && !_activeInstance.IsDisposed)
            {
                _activeInstance.BringToFront();
                _activeInstance.Focus();
                if (_activeInstance._block.Name != block.Name)
                {
                    _activeInstance._block = block;
                    _activeInstance._getCode = getCode;
                    _activeInstance._baseImage?.Dispose();
                    _activeInstance._baseImage = imageGen.GenerateImage(block, false);
                    _activeInstance._pbLive.Image = _activeInstance._baseImage;
                    _activeInstance.Text = $"Simulation Commissioning: {block.Name}";
                    _activeInstance.RestartSimulation();
                }
                throw new InvalidOperationException("SINGLETON_REDIRECT");
            }

            _appSettings = AppSettings.Load();
            _block = block;
            _getCode = getCode;
            _baseImage = imageGen.GenerateImage(block, false);
            InitializeUI();
            this.Shown += (s, e) => InitializeSimulation();

            _activeInstance = this;
            this.FormClosed += (s, e) => { _activeInstance = null; };
        }


        private void InitializeUI()
        {
            this.Text = $"Simulation Commissioning: {_block.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(1000, 600);

            // Restore window bounds from settings
            if (_appSettings != null && _appSettings.SimFormWidth > 0 && _appSettings.SimFormHeight > 0)
            {
                this.Size = new Size(_appSettings.SimFormWidth, _appSettings.SimFormHeight);
                this.WindowState = (FormWindowState)_appSettings.SimFormState;
            }
            else
            {
                this.Size = new Size(1400, 850);
            }

            this.IsMdiContainer = true;

            ToolStrip ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(5) };

            // View Panels Dropdown (Dynamic State Mapping)
            var btnView = new ToolStripDropDownButton("👁️ View Panels") { Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            var btnStart = new ToolStripButton("▶ Start", null, (s, e) => {
                SclStandardLib.UseVirtualTime = false;
                SclStandardLib.VirtualTickCount = 0;
                _engine?.Start();
            })
            { ForeColor = Color.Green, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnPause = new ToolStripButton("⏸ Pause", null, (s, e) => _engine?.Pause()) { ForeColor = Color.DarkOrange, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnStep = new ToolStripButton("⏭ Step 1 Scan", null, (s, e) => StepSingleScan()) { ForeColor = Color.DarkBlue, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnStop = new ToolStripButton("⏹ Stop", null, (s, e) => _engine?.Stop()) { ForeColor = Color.Red, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnRestart = new ToolStripButton("🔄 Reset Memory", null, (s, e) => RestartSimulation());
            var btnReload = new ToolStripButton("🔂 Reload code", null, (s, e) => ReloadCode()) { ForeColor = Color.DarkMagenta, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            _lblStatus = new ToolStripLabel("Status: IDLE") { Font = new Font("Consolas", 10, FontStyle.Bold) };

            ts.Items.AddRange(new ToolStripItem[] { btnView, new ToolStripSeparator(), btnStart, btnPause, btnStep, btnStop, new ToolStripSeparator(), btnRestart, btnReload, new ToolStripSeparator(), _lblStatus });
            this.Controls.Add(ts);

            _dockPanel = new DockPanel { Dock = DockStyle.Fill, Theme = new VS2015LightTheme(), DocumentStyle = DocumentStyle.DockingWindow };
            this.Controls.Add(_dockPanel);
            _dockPanel.BringToFront();

            // Set HideOnClose = true for all panes
            _paneWatch = new SimToolPane("Watch") { Text = "🔍 Live Watch Table", CloseButtonVisible = true, HideOnClose = true };
            _dgvWatch = new SimDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
            _boldFont = new Font(_dgvWatch.Font, FontStyle.Bold);
            _dgvWatch.CellValueChanged += DgvWatch_CellValueChanged;
            _dgvWatch.CellFormatting += DgvWatch_CellFormatting;
            _dgvWatch.CellDoubleClick += DgvWatch_CellDoubleClick;
            _paneWatch.Controls.Add(_dgvWatch);

            _paneScript = new SimToolPane("Script") { Text = "⚙️ Test Script", CloseButtonVisible = true, HideOnClose = true };
            _rtbScript = new FastColoredTextBoxNS.FastColoredTextBox { Dock = DockStyle.Fill, Language = FastColoredTextBoxNS.Language.Custom, Font = new Font("Consolas", 10), ShowLineNumbers = true, BackColor = Color.FromArgb(245, 245, 245) };
            var dslKeywordStyle = new FastColoredTextBoxNS.TextStyle(Brushes.Blue, null, FontStyle.Bold);
            var dslCommentStyle = new FastColoredTextBoxNS.TextStyle(Brushes.Green, null, FontStyle.Italic);

            _rtbScript.TextChangedDelayed += (s, ev) => {
                ev.ChangedRange.ClearStyle(dslKeywordStyle, dslCommentStyle);
                ev.ChangedRange.SetStyle(dslKeywordStyle, @"\b(SET|RUN|ASSERT|SCANS|MS)\b", RegexOptions.IgnoreCase);
                ev.ChangedRange.SetStyle(dslCommentStyle, @"//.*");
            };

            // NEW SCRIPT PANEL TOOLSTRIP
            ToolStrip tsScript = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(2), BackColor = Color.WhiteSmoke };
            var btnRunTest = new ToolStripButton("▶ Run Script", null, null) { ForeColor = Color.DarkGreen, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnLoadTest = new ToolStripButton("📂 Load", null, null);
            var btnExportTest = new ToolStripButton("💾 Export", null, null);
            var btnHelp = new ToolStripButton("💡 Help", null, null);
            var btnGenAI = new ToolStripButton("🤖 Generate", null, null) { ForeColor = Color.DarkViolet, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnAnalyzeAI = new ToolStripButton("🧠 Analyze", null, null) { ForeColor = Color.MediumBlue, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            _btnStopAi = new ToolStripButton("⏹ Stop", null, null) { ForeColor = Color.DarkRed, Font = new Font("Segoe UI", 9, FontStyle.Bold), Enabled = false };

            _btnStopAi.Click += (s, e) => { _ollamaCts?.Cancel(); _btnStopAi.Enabled = false; };

            tsScript.Items.AddRange(new ToolStripItem[] {
                btnRunTest, new ToolStripSeparator(),
                btnLoadTest, btnExportTest, new ToolStripSeparator(),
                btnHelp, new ToolStripSeparator(),
                btnGenAI, btnAnalyzeAI, _btnStopAi
            });

            _paneScript.Controls.Add(tsScript);
            tsScript.Dock = DockStyle.Top;
            _paneScript.Controls.Add(_rtbScript);
            _rtbScript.BringToFront();

            _paneVariables = new SimToolPane("Variables") { Text = "☑️ Variables Selection", CloseButtonVisible = true, HideOnClose = true };
            _tvSignals = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, Font = new Font("Segoe UI", 9) };
            _tvSignals.AfterCheck += TvSignals_AfterCheck;
            _paneVariables.Controls.Add(_tvSignals);

            _paneChart = new SimToolPane("Chart") { Text = "📈 Logic Analyzer Chart", CloseButtonVisible = true, HideOnClose = true };
            _plotAnalyzer = new ScottPlot.FormsPlot { Dock = DockStyle.Fill };
            _paneChart.Controls.Add(_plotAnalyzer);

            _paneTestLog = new SimToolPane("TestLog") { Text = "📝 Unit Test Results", CloseButtonVisible = true, HideOnClose = true };
            _rtbTestLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.LightGray, Font = new Font("Consolas", 9), ReadOnly = true };
            _paneTestLog.Controls.Add(_rtbTestLog);

            _paneAiLog = new SimToolPane("AiLog") { Text = "🤖 AI Response", CloseButtonVisible = true, HideOnClose = true };
            _rtbAiLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.LightGray, Font = new Font("Consolas", 9), ReadOnly = true };
            _paneAiLog.Controls.Add(_rtbAiLog);
            _rtbTestLog.ContextMenuStrip = BuildOutputContextMenu(_rtbTestLog, $"{_block.Name}_TestLog");
            _rtbAiLog.ContextMenuStrip = BuildOutputContextMenu(_rtbAiLog, $"{_block.Name}_AiResponse");


            _paneFbd = new SimToolPane("Fbd") { Text = "🖼️ Live FBD", CloseButtonVisible = true, HideOnClose = true };
            Panel pnlImage = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(245, 246, 248) };
            _pbLive = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, Image = _baseImage };
            _pbLive.Paint += PbLive_Paint;
            pnlImage.Controls.Add(_pbLive);
            _paneFbd.Controls.Add(pnlImage);

            _paneStats = new SimToolPane("Stats") { Text = "📊 Statistics", CloseButtonVisible = true, HideOnClose = true };
            Panel pnlStats = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(235, 240, 248), Padding = new Padding(10) };
            _lblStats = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(40, 60, 90), Font = new Font("Consolas", 9), Text = "Waiting...", TextAlign = ContentAlignment.TopLeft };
            pnlStats.Controls.Add(_lblStats);
            _paneStats.Controls.Add(pnlStats);

            // Populate the View Dropdown Dynamically
            SimToolPane[] allPanes = { _paneWatch, _paneScript, _paneVariables, _paneChart, _paneTestLog, _paneAiLog, _paneFbd, _paneStats };
            btnView.DropDownOpening += (s, e) =>
            {
                btnView.DropDownItems.Clear();
                foreach (var pane in allPanes)
                {
                    var item = new ToolStripMenuItem(pane.Text) { Checked = !pane.IsHidden };
                    item.Click += (s2, e2) => {
                        if (pane.IsHidden)
                        {
                            try
                            {
                                // Default restore that uses the previous saved placement state
                                pane.Show(_dockPanel);
                            }
                            catch
                            {
                                // Fallback just in case standard show fails
                                pane.Show(_dockPanel, DockState.Document);
                            }
                        }
                        else
                        {
                            pane.Hide();
                        }
                    };
                    btnView.DropDownItems.Add(item);
                }
            };

            // Restore from Session Cache if available
            if (!_sessions.ContainsKey(_block.Name))
            {
                _sessions[_block.Name] = new SessionData();
            }
            var session = _sessions[_block.Name];
            _rtbScript.Text = session.ScriptText;
            _rtbTestLog.Text = session.TestLogText;
            _rtbAiLog.Text = session.AiLogText;

            // Load Layout
            bool applyDefault = true;
            string layoutFile = AppSettings.GetSimLayoutFilePath();
            if (System.IO.File.Exists(layoutFile))
            {
                try
                {
                    _dockPanel.LoadFromXml(layoutFile, new DeserializeDockContent(GetContentFromPersistString));
                    applyDefault = false;
                }
                catch { }
            }
            if (applyDefault) ApplyDefaultLayout();

            btnLoadTest.Click += (s, e) => { using (OpenFileDialog ofd = new OpenFileDialog { Filter = "SCL Test Files (*.scltest)|*.scltest|Text Files (*.txt)|*.txt" }) { if (ofd.ShowDialog() == DialogResult.OK) _rtbScript.Text = System.IO.File.ReadAllText(ofd.FileName); } };
            btnExportTest.Click += (s, e) => { using (SaveFileDialog sfd = new SaveFileDialog { Filter = "SCL Test Files (*.scltest)|*.scltest", DefaultExt = "scltest", FileName = $"{_block.Name}_Test.scltest" }) { if (sfd.ShowDialog() == DialogResult.OK) { System.IO.File.WriteAllText(sfd.FileName, _rtbScript.Text); MessageBox.Show("Test script exported successfully!", "Exported", MessageBoxButtons.OK, MessageBoxIcon.Information); } } };
            btnHelp.Click += (s, e) => MessageBox.Show("DSL Syntax Guide:\n\n1. SET Variable = Value\n2. RUN [X] SCANS\n3. RUN [X] MS\n4. ASSERT Variable == Value", "Auto-Test Help", MessageBoxButtons.OK, MessageBoxIcon.Information);

            btnGenAI.Click += async (s, e) =>
            {
                if (_paneAiLog.IsHidden) _paneAiLog.Show(_dockPanel);
                _paneAiLog.Activate();
                _ollamaCts?.Cancel();
                _ollamaCts = new CancellationTokenSource();
                var token = _ollamaCts.Token;
                btnGenAI.Enabled = false; _btnStopAi.Enabled = true;

                _rtbAiLog.Clear();
                _rtbAiLog.SelectionColor = Color.MediumPurple;
                _rtbAiLog.AppendText("🤖 AI is thinking... (Streaming real-time)\n\n");

                string promptTemplate = _appSettings?.Prompts?.FirstOrDefault(p => p.Name == "Generate Unit Test Script")?.Text ?? "Generate a test.";
                string fullPrompt = $"{promptTemplate}\n\nCode:\n{_getCode()}";
                StringBuilder fullResponse = new StringBuilder(); int lastUpdateLength = 0;

                System.Windows.Forms.Timer uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 100 };
                uiRefreshTimer.Tick += (senderTick, argsTick) => {
                    if (this.IsDisposed || _rtbAiLog.IsDisposed) return;
                    lock (fullResponse)
                    {
                        if (fullResponse.Length > lastUpdateLength)
                        {
                            string newText = fullResponse.ToString().Substring(lastUpdateLength);
                            lastUpdateLength = fullResponse.Length;
                            int start = _rtbAiLog.TextLength;
                            _rtbAiLog.AppendText(newText);
                            _rtbAiLog.Select(start, newText.Length);
                            _rtbAiLog.SelectionColor = Color.LightGray;
                            _rtbAiLog.ScrollToCaret();
                        }
                    }
                };
                uiRefreshTimer.Start();
                Action<string> streamHandler = (tokenText) => { if (this.IsDisposed) return; lock (fullResponse) { fullResponse.Append(tokenText); } };

                await StreamOllamaAsync(fullPrompt, streamHandler, token);

                uiRefreshTimer.Stop(); uiRefreshTimer.Dispose();
                if (!this.IsDisposed && !_btnStopAi.IsDisposed) _btnStopAi.Enabled = false;
                if (token.IsCancellationRequested || this.IsDisposed) return;

                lock (fullResponse)
                {
                    if (fullResponse.Length > lastUpdateLength && !_rtbAiLog.IsDisposed)
                    {
                        _rtbAiLog.AppendText(fullResponse.ToString().Substring(lastUpdateLength));
                        _rtbAiLog.ScrollToCaret();
                    }
                }

                string finalText = fullResponse.ToString();
                string codeOnly = Regex.Replace(finalText, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var codeMatch = Regex.Match(codeOnly, @"```[^\n]*\n(.*?)\n```", RegexOptions.Singleline);
                if (codeMatch.Success) codeOnly = codeMatch.Groups[1].Value;

                var cleanDslLines = codeOnly.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).Where(line => { string t = line.ToUpper(); return t.StartsWith("SET ") || t.StartsWith("RUN ") || t.StartsWith("ASSERT ") || t.StartsWith("//"); });
                string cleanScript = string.Join(Environment.NewLine, cleanDslLines);

                if (!string.IsNullOrWhiteSpace(cleanScript) && !_rtbScript.IsDisposed)
                {
                    _paneScript.Activate();
                    _rtbScript.Text = cleanScript + Environment.NewLine;
                    _rtbAiLog.SelectionStart = _rtbAiLog.TextLength;
                    _rtbAiLog.SelectionColor = Color.LimeGreen;
                    _rtbAiLog.AppendText("\n\n✅ Generation Complete! Code filtered and moved to Script Editor.\n");
                }
                else if (!_rtbAiLog.IsDisposed)
                {
                    _rtbAiLog.SelectionStart = _rtbAiLog.TextLength;
                    _rtbAiLog.SelectionColor = Color.Red;
                    _rtbAiLog.AppendText("\n\n❌ AI did not return a valid DSL script format.\n");
                }
                if (!btnGenAI.IsDisposed) btnGenAI.Enabled = true;
            };

            btnAnalyzeAI.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_rtbTestLog.Text)) { MessageBox.Show("Please run a test first to generate logs.", "No Logs", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

                if (_paneAiLog.IsHidden) _paneAiLog.Show(_dockPanel);
                _paneAiLog.Activate();
                _ollamaCts?.Cancel();
                _ollamaCts = new CancellationTokenSource();
                var token = _ollamaCts.Token;
                btnAnalyzeAI.Enabled = false;
                _btnStopAi.Enabled = true;

                string analysisPrompt = $"You are a Senior Siemens PLC Engineer. Analyze the following failed Unit Test execution.\n\n--- SCL CODE ---\n{_getCode()}\n\n--- TEST SCRIPT ---\n{_rtbScript.Text}\n\n--- EXECUTION LOG ---\n{_rtbTestLog.Text}\n\nProvide a concise analysis of why the assertions failed. Is it a bug in the code, or a mistake in the test logic?";
                _rtbAiLog.Clear();
                _rtbAiLog.SelectionStart = _rtbAiLog.TextLength;
                _rtbAiLog.SelectionColor = Color.Gold;
                _rtbAiLog.AppendText("\n\n=== 🧠 AI ANALYSIS ===\n");

                StringBuilder fullResponse = new StringBuilder(); int lastUpdateLength = 0;
                System.Windows.Forms.Timer uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 100 };
                uiRefreshTimer.Tick += (senderTick, argsTick) => {
                    if (this.IsDisposed || _rtbAiLog.IsDisposed) return;
                    lock (fullResponse)
                    {
                        if (fullResponse.Length > lastUpdateLength)
                        {
                            string newText = fullResponse.ToString().Substring(lastUpdateLength);
                            lastUpdateLength = fullResponse.Length;
                            int start = _rtbAiLog.TextLength;
                            _rtbAiLog.AppendText(newText);
                            _rtbAiLog.Select(start, newText.Length);
                            _rtbAiLog.SelectionColor = Color.Cyan;
                            _rtbAiLog.ScrollToCaret();
                        }
                    }
                };
                uiRefreshTimer.Start();
                Action<string> streamHandler = (tokenText) => { if (this.IsDisposed) return; lock (fullResponse) { fullResponse.Append(tokenText); } };

                await StreamOllamaAsync(analysisPrompt, streamHandler, token);

                uiRefreshTimer.Stop(); uiRefreshTimer.Dispose();
                if (!this.IsDisposed && !_btnStopAi.IsDisposed) _btnStopAi.Enabled = false;
                if (token.IsCancellationRequested || this.IsDisposed) return;

                lock (fullResponse)
                {
                    if (fullResponse.Length > lastUpdateLength && !_rtbAiLog.IsDisposed)
                    {
                        _rtbAiLog.AppendText(fullResponse.ToString().Substring(lastUpdateLength));
                        _rtbAiLog.ScrollToCaret();
                    }
                }
                if (!btnAnalyzeAI.IsDisposed) btnAnalyzeAI.Enabled = true;
            };

            btnRunTest.Click += async (s, e) =>
            {
                if (_paneTestLog.IsHidden) _paneTestLog.Show(_dockPanel);
                if (_paneChart.IsHidden) _paneChart.Show(_dockPanel);

                _paneTestLog.Activate();
                _paneChart.Activate();

                if (_engine == null || _context == null) return;
                bool wasRunning = _engine.IsRunning && !_engine.IsPaused;
                _engine.Pause();
                _rtbTestLog.Clear();

                const int MAX_LOG_LENGTH = 500_000;
                const string TRUNCATION_MSG = "\n\n⚠️ [LOG TRUNCATED — Export full log via right-click menu]\n";

                Action<string, Color, bool> appendLog = null;
                appendLog = (text, color, bold) =>
                {
                    if (_rtbTestLog.InvokeRequired)
                    {
                        _rtbTestLog.Invoke(new Action(() => appendLog(text, color, bold)));
                        return;
                    }

                    if (_rtbTestLog.TextLength > MAX_LOG_LENGTH)
                    {
                        if (!_rtbTestLog.Text.EndsWith(TRUNCATION_MSG))
                        {
                            _rtbTestLog.SelectionStart = _rtbTestLog.TextLength;
                            _rtbTestLog.SelectionColor = Color.Orange;
                            _rtbTestLog.SelectionFont = new Font(_rtbTestLog.Font, FontStyle.Bold);
                            _rtbTestLog.AppendText(TRUNCATION_MSG);
                        }
                        return;
                    }

                    int start = _rtbTestLog.TextLength;
                    _rtbTestLog.AppendText(text + "\n");
                    _rtbTestLog.Select(start, text.Length);
                    _rtbTestLog.SelectionColor = color;
                    if (bold) _rtbTestLog.SelectionFont = new Font(_rtbTestLog.Font, FontStyle.Bold);
                    _rtbTestLog.ScrollToCaret();
                };


                var runner = new AutoTestRunner(_context, _engine, appendLog);
                btnRunTest.Enabled = false;
                await runner.RunScriptAsync(_rtbScript.Text);

                _lastTimingData = runner.RecordedData;
                UpdateTimingDiagram(_lastTimingData);

                btnRunTest.Enabled = true;
                if (wasRunning) _engine.Start();
                UpdateUI();
            };

            _uiTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _uiTimer.Tick += (s, e) => UpdateUI();

            this.FormClosing += (s, e) => {
                var saveSession = _sessions[_block.Name];
                saveSession.ScriptText = _rtbScript.Text;
                saveSession.TestLogText = _rtbTestLog.Text;
                saveSession.AiLogText = _rtbAiLog.Text;
                saveSession.CheckedVariables = GetCheckedVariables();

                // Save Window Bounds and State to AppSettings
                if (_appSettings != null)
                {
                    _appSettings.SimFormWidth = this.WindowState == FormWindowState.Normal ? this.Width : this.RestoreBounds.Width;
                    _appSettings.SimFormHeight = this.WindowState == FormWindowState.Normal ? this.Height : this.RestoreBounds.Height;
                    _appSettings.SimFormState = this.WindowState; // Save safely as int to avoid enum matching bugs

                    try { _appSettings.Save(); } catch { }
                }

                try { _dockPanel.SaveAsXml(AppSettings.GetSimLayoutFilePath()); } catch { }
                _ollamaCts?.Cancel();
                _engine?.Stop();
                _uiTimer?.Stop();
                _boldFont?.Dispose();
            };
        }

        private IDockContent GetContentFromPersistString(string persistString)
        {
            switch (persistString)
            {
                case "Watch": return _paneWatch;
                case "Script": return _paneScript;
                case "Variables": return _paneVariables;
                case "Chart": return _paneChart;
                case "TestLog": return _paneTestLog;
                case "AiLog": return _paneAiLog;
                case "Fbd": return _paneFbd;
                case "Stats": return _paneStats;
                default: return null;
            }
        }

        private const string DefaultLayoutXml = @"<?xml version=""1.0"" encoding=""utf-16""?>
<!--DockPanel configuration file. Author: Weifen Luo, all rights reserved.-->
<!--!!! AUTOMATICALLY GENERATED FILE. DO NOT MODIFY !!!-->
<DockPanel FormatVersion=""1.0"" DockLeftPortion=""0.25"" DockRightPortion=""0.33196958028494417"" DockTopPortion=""0.25"" DockBottomPortion=""0.25"" ActiveDocumentPane=""1"" ActivePane=""1"">
  <Contents Count=""8"">
    <Content ID=""0"" PersistString=""Watch"" AutoHidePortion=""0.25"" IsHidden=""False"" IsFloat=""False"" />
    <Content ID=""1"" PersistString=""Script"" AutoHidePortion=""0.25"" IsHidden=""False"" IsFloat=""False"" />
    <Content ID=""2"" PersistString=""Chart"" AutoHidePortion=""0.25"" IsHidden=""False"" IsFloat=""False"" />
    <Content ID=""3"" PersistString=""Variables"" AutoHidePortion=""0.25"" IsHidden=""False"" IsFloat=""False"" />
    <Content ID=""4"" PersistString=""TestLog"" AutoHidePortion=""0.25"" IsHidden=""False"" IsFloat=""False"" />
    <Content ID=""5"" PersistString=""AiLog"" AutoHidePortion=""0.25"" IsHidden=""False"" IsFloat=""False"" />
    <Content ID=""6"" PersistString=""Fbd"" AutoHidePortion=""0.25"" IsHidden=""False"" IsFloat=""False"" />
    <Content ID=""7"" PersistString=""Stats"" AutoHidePortion=""0.25"" IsHidden=""False"" IsFloat=""False"" />
  </Contents>
  <Panes Count=""8"">
    <Pane ID=""0"" DockState=""Document"" ActiveContent=""1"">
      <Contents Count=""2"">
        <Content ID=""0"" RefID=""0"" />
        <Content ID=""1"" RefID=""1"" />
      </Contents>
    </Pane>
    <Pane ID=""1"" DockState=""Document"" ActiveContent=""3"">
      <Contents Count=""1"">
        <Content ID=""0"" RefID=""3"" />
      </Contents>
    </Pane>
    <Pane ID=""2"" DockState=""DockRight"" ActiveContent=""6"">
      <Contents Count=""1"">
        <Content ID=""0"" RefID=""6"" />
      </Contents>
    </Pane>
    <Pane ID=""3"" DockState=""DockRight"" ActiveContent=""7"">
      <Contents Count=""1"">
        <Content ID=""0"" RefID=""7"" />
      </Contents>
    </Pane>
    <Pane ID=""4"" DockState=""Float"" ActiveContent=""-1"">
      <Contents Count=""1"">
        <Content ID=""0"" RefID=""5"" />
      </Contents>
    </Pane>
    <Pane ID=""5"" DockState=""Float"" ActiveContent=""-1"">
      <Contents Count=""1"">
        <Content ID=""0"" RefID=""2"" />
      </Contents>
    </Pane>
    <Pane ID=""6"" DockState=""Document"" ActiveContent=""2"">
      <Contents Count=""3"">
        <Content ID=""0"" RefID=""2"" />
        <Content ID=""1"" RefID=""4"" />
        <Content ID=""2"" RefID=""5"" />
      </Contents>
    </Pane>
    <Pane ID=""7"" DockState=""Float"" ActiveContent=""-1"">
      <Contents Count=""1"">
        <Content ID=""0"" RefID=""4"" />
      </Contents>
    </Pane>
  </Panes>
  <DockWindows>
    <DockWindow ID=""0"" DockState=""Document"" ZOrderIndex=""1"">
      <NestedPanes Count=""3"">
        <Pane ID=""0"" RefID=""0"" PrevPane=""-1"" Alignment=""Right"" Proportion=""0.5"" />
        <Pane ID=""1"" RefID=""1"" PrevPane=""0"" Alignment=""Bottom"" Proportion=""0.29893294132274323"" />
        <Pane ID=""2"" RefID=""6"" PrevPane=""1"" Alignment=""Right"" Proportion=""0.7866861030126336"" />
      </NestedPanes>
    </DockWindow>
    <DockWindow ID=""1"" DockState=""DockLeft"" ZOrderIndex=""2"">
      <NestedPanes Count=""0"" />
    </DockWindow>
    <DockWindow ID=""2"" DockState=""DockRight"" ZOrderIndex=""3"">
      <NestedPanes Count=""2"">
        <Pane ID=""0"" RefID=""3"" PrevPane=""-1"" Alignment=""Bottom"" Proportion=""0.5"" />
        <Pane ID=""1"" RefID=""2"" PrevPane=""3"" Alignment=""Top"" Proportion=""0.75"" />
      </NestedPanes>
    </DockWindow>
    <DockWindow ID=""3"" DockState=""DockTop"" ZOrderIndex=""4"">
      <NestedPanes Count=""0"" />
    </DockWindow>
    <DockWindow ID=""4"" DockState=""DockBottom"" ZOrderIndex=""5"">
      <NestedPanes Count=""0"" />
    </DockWindow>
  </DockWindows>
  <FloatWindows Count=""3"">
    <FloatWindow ID=""0"" Bounds=""484, 511, 300, 300"" ZOrderIndex=""0"">
      <NestedPanes Count=""1"">
        <Pane ID=""0"" RefID=""5"" PrevPane=""-1"" Alignment=""Right"" Proportion=""0.5"" />
      </NestedPanes>
    </FloatWindow>
    <FloatWindow ID=""1"" Bounds=""130, 130, 300, 300"" ZOrderIndex=""1"">
      <NestedPanes Count=""1"">
        <Pane ID=""0"" RefID=""7"" PrevPane=""-1"" Alignment=""Right"" Proportion=""0.5"" />
      </NestedPanes>
    </FloatWindow>
    <FloatWindow ID=""2"" Bounds=""969, 732, 300, 300"" ZOrderIndex=""2"">
      <NestedPanes Count=""1"">
        <Pane ID=""0"" RefID=""4"" PrevPane=""-1"" Alignment=""Right"" Proportion=""0.5"" />
      </NestedPanes>
    </FloatWindow>
  </FloatWindows>
</DockPanel>";

        private void ApplyDefaultLayout()
        {
            try
            {
                using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(DefaultLayoutXml)))
                {
                    _dockPanel.LoadFromXml(stream, new DeserializeDockContent(GetContentFromPersistString));
                }
            }
            catch
            {
                // Fallback Layout if parsing completely fails
                _paneWatch.Show(_dockPanel, DockState.Document);
                _paneScript.Show(_paneWatch.Pane, null);
                _paneChart.Show(_paneWatch.Pane, DockAlignment.Bottom, 0.40);
                _paneVariables.Show(_paneChart.Pane, DockAlignment.Left, 0.20);
                _paneTestLog.Show(_dockPanel, DockState.DockBottom);
                _paneAiLog.Show(_paneTestLog.Pane, DockAlignment.Right, 0.50);
                _paneFbd.Show(_dockPanel, DockState.DockRight);
                _paneStats.Show(_paneFbd.Pane, DockAlignment.Bottom, 0.25);
            }
        }

        // ==============================================================
        // TREEVIEW LOGIC (Variable Selection)
        // ==============================================================
        private void PopulateSignalTree()
        {
            _tvSignals.Nodes.Clear();
            Font boldNodeFont = new Font(_tvSignals.Font, FontStyle.Bold);

            var grpIn = _tvSignals.Nodes.Add("Inputs"); grpIn.NodeFont = boldNodeFont;
            var grpOut = _tvSignals.Nodes.Add("Outputs"); grpOut.NodeFont = boldNodeFont;
            var grpInOut = _tvSignals.Nodes.Add("InOuts"); grpInOut.NodeFont = boldNodeFont;
            var grpStat = _tvSignals.Nodes.Add("Statics"); grpStat.NodeFont = boldNodeFont;
            var grpTemp = _tvSignals.Nodes.Add("Temps"); grpTemp.NodeFont = boldNodeFont;

            var session = _sessions.ContainsKey(_block.Name) ? _sessions[_block.Name] : null;

            Action<TreeNode, string, object> addNode = (parent, name, val) => {
                if (val is bool || val is IConvertible)
                {
                    bool isChecked = false;
                    if (session != null && !session.IsFirstRun)
                    {
                        isChecked = session.CheckedVariables.Contains(name);
                    }
                    TreeNode n = new TreeNode(name) { Tag = name, Checked = isChecked };
                    parent.Nodes.Add(n);
                }
            };

            foreach (var m in _context.Memory.Values)
            {
                if (m.Direction == ElementDirection.Member || m.Direction.ToString().Contains("Constant") || m.Direction.ToString() == "Return") continue;

                var elementDef = _block.Elements.FirstOrDefault(el => el.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
                if (elementDef != null)
                {
                    var secProp = elementDef.GetType().GetProperty("Section");
                    if (secProp != null && string.Equals(secProp.GetValue(elementDef) as string, "Constant", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                TreeNode parentNode = m.Direction switch
                {
                    ElementDirection.Input => grpIn,
                    ElementDirection.Output => grpOut,
                    ElementDirection.InOut => grpInOut,
                    ElementDirection.Temp => grpTemp,
                    _ => grpStat
                };

                if (m.CurrentValue is Array arr)
                {
                    for (int i = 0; i < arr.Length; i++) addNode(parentNode, $"{m.Name}[{i}]", arr.GetValue(i));
                }
                else if (m.CurrentValue is Dictionary<string, object> dict)
                {
                    foreach (var kvp in dict) addNode(parentNode, $"{m.Name}.{kvp.Key}", kvp.Value);
                }
                else
                {
                    addNode(parentNode, m.Name, m.CurrentValue);
                }
            }

            if (session != null && !session.IsFirstRun)
            {
                foreach (TreeNode p in _tvSignals.Nodes)
                {
                    if (p.Nodes.Count > 0)
                    {
                        bool allChecked = true;
                        foreach (TreeNode c in p.Nodes) if (!c.Checked) { allChecked = false; break; }
                        _isUpdatingTree = true;
                        p.Checked = allChecked;
                        _isUpdatingTree = false;
                    }
                }
            }

            if (session != null) session.IsFirstRun = false;

            _tvSignals.CollapseAll();
        }

        private List<string> GetCheckedVariables()
        {
            List<string> selected = new List<string>();
            void GetChecked(TreeNodeCollection nodes)
            {
                foreach (TreeNode n in nodes)
                {
                    if (n.Nodes.Count == 0 && n.Checked && n.Tag != null) selected.Add(n.Tag.ToString());
                    GetChecked(n.Nodes);
                }
            }
            GetChecked(_tvSignals.Nodes);
            return selected;
        }

        private void TvSignals_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_isUpdatingTree) return;
            if (e.Action != TreeViewAction.Unknown)
            {
                _isUpdatingTree = true;
                CheckAllChildren(e.Node, e.Node.Checked);
                _isUpdatingTree = false;

                if (_sessions.ContainsKey(_block.Name))
                {
                    _sessions[_block.Name].CheckedVariables = GetCheckedVariables();
                }

                if (_lastTimingData != null) UpdateTimingDiagram(_lastTimingData);
            }
        }

        private void CheckAllChildren(TreeNode node, bool isChecked)
        {
            foreach (TreeNode child in node.Nodes)
            {
                child.Checked = isChecked;
                CheckAllChildren(child, isChecked);
            }
        }

        // ==============================================================
        // MIXED ANALOG & DIGITAL CHARTING
        // ==============================================================
        private void UpdateTimingDiagram(TimingData data)
        {
            if (_plotAnalyzer.InvokeRequired) { _plotAnalyzer.Invoke(new Action(() => UpdateTimingDiagram(data))); return; }
            if (data == null) return;

            _plotAnalyzer.Plot.Clear();
            var validSignals = GetCheckedVariables().Where(s => data.Signals.ContainsKey(s) && data.Signals[s].Count > 0).Reverse().ToList();
            if (validSignals.Count == 0) { _plotAnalyzer.Refresh(); return; }

            double[] xs = data.Scans.ToArray();
            int digitalOffset = 0;
            List<string> y2Labels = new List<string>();
            List<double> y2Positions = new List<double>();

            bool hasAnalog = false;
            bool hasDigital = false;

            int colorIndex = 0;

            foreach (var sig in validSignals)
            {
                var rawYs = data.Signals[sig];
                while (rawYs.Count < xs.Length) rawYs.Add(rawYs.Count > 0 ? rawYs.Last() : 0);
                double[] ys = rawYs.Take(xs.Length).ToArray();

                bool isDigital = data.IsDigital.ContainsKey(sig) && data.IsDigital[sig];
                Color curveColor = _plotColors[colorIndex % _plotColors.Length];

                if (isDigital)
                {
                    hasDigital = true;
                    double[] ysOffset = ys.Select(v => v + digitalOffset).ToArray();
                    var scatter = _plotAnalyzer.Plot.AddScatterStep(xs, ysOffset);
                    scatter.Color = curveColor;
                    scatter.YAxisIndex = 1;
                    scatter.LineWidth = 2;

                    y2Labels.Add(sig);
                    y2Positions.Add(digitalOffset + 0.5);
                    digitalOffset += 2;
                }
                else
                {
                    hasAnalog = true;
                    var scatter = _plotAnalyzer.Plot.AddScatterLines(xs, ys);
                    scatter.Color = curveColor;
                    scatter.YAxisIndex = 0;
                    scatter.LineWidth = 2;
                    scatter.Label = sig;
                }
                colorIndex++;
            }

            if (hasAnalog) { _plotAnalyzer.Plot.YAxis.Label("Analog Values"); _plotAnalyzer.Plot.Legend(true, ScottPlot.Alignment.UpperLeft); }
            else { _plotAnalyzer.Plot.YAxis.Label(""); _plotAnalyzer.Plot.Legend(false); }

            if (hasDigital) { _plotAnalyzer.Plot.YAxis2.Ticks(true); _plotAnalyzer.Plot.YAxis2.ManualTickPositions(y2Positions.ToArray(), y2Labels.ToArray()); _plotAnalyzer.Plot.SetAxisLimits(yMin: -0.5, yMax: digitalOffset, yAxisIndex: 1); }
            else { _plotAnalyzer.Plot.YAxis2.Ticks(false); }

            _plotAnalyzer.Plot.SetAxisLimits(xMin: 0, xMax: xs.Length > 0 ? xs.Max() + 1 : 10, yAxisIndex: 0);
            _plotAnalyzer.Plot.XAxis.MinimumTickSpacing(1);
            _plotAnalyzer.Plot.Title("Logic Analyzer (Analog & Digital)");
            _plotAnalyzer.Plot.XLabel("Scan Cycle");
            _plotAnalyzer.Plot.Style(ScottPlot.Style.Light1);
            _plotAnalyzer.Plot.Grid(color: Color.FromArgb(220, 220, 220));

            _plotAnalyzer.Refresh();
        }

        private void StepSingleScan()
        {
            if (_engine == null) return;
            if (_engine.IsRunning && !_engine.IsPaused) _engine.Pause();
            _engine.StepScans(1);
            UpdateUI();
            _lblStatus.Text = "STEPPED (PAUSED)";
            _lblStatus.ForeColor = Color.DarkBlue;
        }

        private async Task StreamOllamaAsync(string promptText, Action<string> onTokenReceived, CancellationToken cancellationToken)
        {
            try
            {
                using (HttpClient client = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan })
                {
                    var requestBody = new { model = _appSettings.OllamaModelName, prompt = promptText, stream = true };
                    string jsonPayload = JsonSerializer.Serialize(requestBody);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    string url = $"{_appSettings.OllamaApiUrl.TrimEnd('/')}/api/generate";

                    using (var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content })
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            if (cancellationToken.IsCancellationRequested) return;
                            string errJson = await response.Content.ReadAsStringAsync();
                            string errMsg = errJson;
                            try { using (JsonDocument doc = JsonDocument.Parse(errJson)) { errMsg = doc.RootElement.GetProperty("error").GetString(); } } catch { }
                            onTokenReceived?.Invoke($"\n\n❌ [API ERROR {response.StatusCode}]: {errMsg}\n");
                            return;
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var reader = new System.IO.StreamReader(stream))
                        {
                            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                            {
                                var line = await reader.ReadLineAsync();
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                using (JsonDocument doc = JsonDocument.Parse(line))
                                {
                                    if (doc.RootElement.TryGetProperty("response", out var respElement))
                                    {
                                        string token = respElement.GetString();
                                        if (!string.IsNullOrEmpty(token) && !cancellationToken.IsCancellationRequested) onTokenReceived?.Invoke(token);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested) onTokenReceived?.Invoke($"\n\n❌ [ERROR]: {ex.Message}\nCheck Ollama connection.");
            }
        }

        private void InitializeSimulation()
        {
            _context = new ExecutionContext(_block, _getCode());
            _engine = new SimulationEngine();
            _engine.OnError += HandleEngineError;
            BindGrid();
            PopulateSignalTree();
            ReloadCode();
            _uiTimer.Start();
        }

        private void BindGrid()
        {
            _watchRows = new BindingList<WatchRow>();
            foreach (var m in _context.Memory.Values)
            {
                string dt = m.DataType.ToUpper();
                if (dt == "TON" || dt == "TOF" || dt == "TP" || dt == "TONR" || dt == "R_TRIG" || dt == "F_TRIG" || dt == "CTU" || dt == "CTD" || dt == "CTUD" || dt == "SR" || dt == "RS") continue;
                if (m.Direction == ElementDirection.Member) continue;
                string comment = _block.Elements.FirstOrDefault(el => el.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))?.Comment ?? "";
                if (m.CurrentValue is Array arr)
                {
                    int lo = 0, hi = arr.Length - 1;
                    var match = Regex.Match(dt, @"\[\s*(\d+)\s*\.\.\s*(\d+)\s*\]");
                    if (match.Success) { lo = int.Parse(match.Groups[1].Value); hi = int.Parse(match.Groups[2].Value); }
                    for (int i = lo; i <= hi; i++) { object val = arr.GetValue(i); _watchRows.Add(new WatchRow { Name = $"{m.Name}[{i}]", ParentName = m.Name, SubKey = i, DataType = dt, Direction = m.Direction.ToString(), LiveValue = FormatPrimitive(val), Comment = comment, IsBool = val is bool }); }
                }
                else if (m.CurrentValue is Dictionary<string, object> dict)
                {
                    foreach (var kvp in dict) _watchRows.Add(new WatchRow { Name = $"{m.Name}.{kvp.Key}", ParentName = m.Name, SubKey = kvp.Key, DataType = "Struct Field", Direction = m.Direction.ToString(), LiveValue = FormatPrimitive(kvp.Value), Comment = comment, IsBool = kvp.Value is bool });
                }
                else
                {
                    _watchRows.Add(new WatchRow { Name = m.Name, ParentName = m.Name, SubKey = null, DataType = dt, Direction = m.Direction.ToString(), LiveValue = FormatPrimitive(m.CurrentValue), Comment = comment, IsBool = m.CurrentValue is bool });
                }
            }
            _dgvWatch.DataSource = _watchRows; _dgvWatch.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
            if (_dgvWatch.Columns.Contains("Name")) _dgvWatch.Columns["Name"].ReadOnly = true;
            if (_dgvWatch.Columns.Contains("DataType")) _dgvWatch.Columns["DataType"].ReadOnly = true;
            if (_dgvWatch.Columns.Contains("Direction")) _dgvWatch.Columns["Direction"].ReadOnly = true;
            if (_dgvWatch.Columns.Contains("ParentName")) _dgvWatch.Columns["ParentName"].Visible = false;
            if (_dgvWatch.Columns.Contains("SubKey")) _dgvWatch.Columns["SubKey"].Visible = false;
            if (_dgvWatch.Columns.Contains("IsBool")) _dgvWatch.Columns["IsBool"].Visible = false;
            if (_dgvWatch.Columns.Contains("Comment")) { _dgvWatch.Columns["Comment"].ReadOnly = true; _dgvWatch.Columns["Comment"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; }
            if (_dgvWatch.Columns.Contains("LiveValue")) { _dgvWatch.Columns["LiveValue"].HeaderText = "✏️ Monitor / Force"; _dgvWatch.Columns["LiveValue"].Width = 120; }
        }

        private string FormatPrimitive(object val) { if (val is bool b) return b ? "True" : "False"; if (val is float f) return f.ToString("F2", CultureInfo.InvariantCulture); return val?.ToString() ?? "0"; }

        private object ParsePrimitive(string str, object existingVal) { if (existingVal is bool) return str.Equals("true", StringComparison.OrdinalIgnoreCase) || str == "1"; if (existingVal is float) return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0.0f; if (existingVal is string) return str; if (existingVal is long) return long.TryParse(str, out long l) ? l : 0L; return int.TryParse(str, out int i) ? i : 0; }

        private void DgvWatch_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _engine == null || e.RowIndex >= _watchRows.Count) return;
            if (_dgvWatch.Columns[e.ColumnIndex].DataPropertyName != "LiveValue") return;
            var row = _watchRows[e.RowIndex];
            lock (_engine.MemoryLock)
            {
                if (_context.Memory.TryGetValue(row.ParentName, out var tag))
                {
                    object existingVal;
                    if (row.SubKey is int idx) existingVal = ((Array)tag.CurrentValue).GetValue(idx); else if (row.SubKey is string key) existingVal = ((Dictionary<string, object>)tag.CurrentValue)[key]; else existingVal = tag.CurrentValue;
                    object newVal = ParsePrimitive(row.LiveValue, existingVal);
                    if (row.SubKey is int idx2) ((Array)tag.CurrentValue).SetValue(newVal, idx2); else if (row.SubKey is string key2) ((Dictionary<string, object>)tag.CurrentValue)[key2] = newVal; else tag.CurrentValue = newVal;
                    row.LiveValue = FormatPrimitive(newVal);
                }
            }
        }

        private void DgvWatch_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _watchRows.Count) return;
            if (_dgvWatch.Columns[e.ColumnIndex].DataPropertyName != "LiveValue") return;
            var row = _watchRows[e.RowIndex];
            if (row.IsBool && (row.Direction == "Input" || row.Direction == "InOut"))
            {
                bool current = row.LiveValue.Equals("True", StringComparison.OrdinalIgnoreCase); row.LiveValue = current ? "False" : "True";
                lock (_engine.MemoryLock)
                {
                    if (_context.Memory.TryGetValue(row.ParentName, out var tag))
                    {
                        if (row.SubKey is int idx) ((Array)tag.CurrentValue).SetValue(!current, idx); else if (row.SubKey is string key) ((Dictionary<string, object>)tag.CurrentValue)[key] = !current; else tag.CurrentValue = !current;
                    }
                }
                _dgvWatch.InvalidateRow(e.RowIndex); _dgvWatch.EndEdit();
            }
        }

        private void UpdateUI()
        {
            if (_engine == null) return;
            if (_engine.IsRunning && !_engine.IsPaused)
            {
                _lastCycleUs = _engine.CycleTimeTicks * 1_000_000 / Stopwatch.Frequency;
                _lblStatus.Text = $"RUNNING ({_lastCycleUs} µs)"; _lblStatus.ForeColor = Color.Green; _scanCount++;
                if (_lastCycleUs > 0 && _lastCycleUs < _minCycleUs) _minCycleUs = _lastCycleUs; if (_lastCycleUs > _maxCycleUs) _maxCycleUs = _lastCycleUs;
                _lblStats.ForeColor = Color.FromArgb(40, 60, 90); _lblStats.Text = $"  Statistics\n  ────────────────────────────────────────────────────────\n  Cycle Time:  {_lastCycleUs,8} µs\n  Min Cycle:   {_minCycleUs,8} µs\n  Max Cycle:   {_maxCycleUs,8} µs\n  Scan Count:  {_scanCount,8}\n  ────────────────────────────────────────────────────────\n  Block: {_block.Name}";
            }
            else if (_engine.IsPaused) { _lblStatus.Text = "PAUSED"; _lblStatus.ForeColor = Color.DarkOrange; } else { _lblStatus.Text = "STOPPED"; _lblStatus.ForeColor = Color.Red; _lblStats.Text = $"  Statistics\n  ────────────────────────────────────────────────────────\n  Status: STOPPED\n  ────────────────────────────────────────────────────────\n  Block: {_block.Name}"; }

            if (_dgvWatch.IsCurrentCellInEditMode) return;
            lock (_engine.MemoryLock)
            {
                for (int i = 0; i < _watchRows.Count; i++)
                {
                    var row = _watchRows[i];
                    if (_context.Memory.TryGetValue(row.ParentName, out var mem))
                    {
                        object val;
                        if (row.SubKey is int idx) val = ((Array)mem.CurrentValue).GetValue(idx); else if (row.SubKey is string key) val = ((Dictionary<string, object>)mem.CurrentValue)[key]; else val = mem.CurrentValue;
                        string strVal = FormatPrimitive(val); if (row.LiveValue != strVal) { row.LiveValue = strVal; _dgvWatch.InvalidateRow(i); }
                    }
                }
            }
            _pbLive.Invalidate();
        }

        private void PbLive_Paint(object sender, PaintEventArgs e)
        {
            if (_engine == null || _context == null) return;
            Graphics g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using Font valFont = new Font("Consolas", 8, FontStyle.Bold);

            if (_engine.IsRunning && !_engine.IsPaused) { g.FillRectangle(Brushes.LimeGreen, 10, 10, 70, 18); g.DrawString("RUNNING", valFont, Brushes.White, 14, 12); }
            else if (_engine.IsPaused) { g.FillRectangle(Brushes.DarkOrange, 10, 10, 70, 18); g.DrawString("PAUSED", valFont, Brushes.White, 16, 12); }
            else { g.FillRectangle(Brushes.Crimson, 10, 10, 70, 18); g.DrawString("STOPPED", valFont, Brushes.White, 13, 12); }

            lock (_engine.MemoryLock)
            {
                foreach (var el in _block.Elements)
                {
                    if (!_context.Memory.TryGetValue(el.Name, out var mem)) continue;
                    if (el.DisplayBounds.Width == 0 && el.DisplayBounds.Height == 0) continue;
                    string txt; if (mem.CurrentValue is Array) txt = "[ARRAY]"; else if (mem.CurrentValue is Dictionary<string, object>) txt = "{STRUCT}"; else if (mem.CurrentValue is bool b) txt = b ? "TRUE" : "FALSE"; else txt = mem.CurrentValue.ToString();
                    SizeF textSize = g.MeasureString(txt, valFont);
                    int minBoxWidth = (int)g.MeasureString("FALSE", valFont).Width + 10;
                    int tagWidth = Math.Max((int)textSize.Width + 8, minBoxWidth);
                    int yCenter = el.DisplayBounds.Y + (el.DisplayBounds.Height / 2); int yPos = yCenter - 8;
                    RectangleF tagRect;

                    if (el.Direction == ElementDirection.Input || el.Direction == ElementDirection.InOut)
                    {
                        int rightEdge = el.DisplayBounds.Right - 45;
                        g.FillRectangle(Brushes.White, rightEdge - 50, yCenter - 15, 55, 30);
                        g.DrawLine(Pens.Black, rightEdge - 50, yCenter, rightEdge + 5, yCenter);
                        tagRect = new RectangleF(rightEdge - tagWidth, yPos, tagWidth, 16);
                    }
                    else
                    {
                        int leftEdge = el.DisplayBounds.Left + 45;
                        g.FillRectangle(Brushes.White, leftEdge - 5, yCenter - 15, 55, 30);
                        g.DrawLine(Pens.Black, leftEdge - 5, yCenter, leftEdge + 50, yCenter);
                        tagRect = new RectangleF(leftEdge, yPos, tagWidth, 16);
                    }

                    Brush bgBrush = Brushes.WhiteSmoke;
                    if (mem.CurrentValue is bool b2) bgBrush = b2 ? Brushes.LightGreen : Brushes.LightGray; else bgBrush = Brushes.LightCyan;
                    g.FillRectangle(bgBrush, tagRect); g.DrawRectangle(Pens.Gray, tagRect.X, tagRect.Y, tagRect.Width, tagRect.Height);
                    float textX = tagRect.X + (tagRect.Width - textSize.Width) / 2;
                    g.DrawString(txt, valFont, Brushes.Black, textX, tagRect.Y + 2);
                }
            }
        }

        private void RestartSimulation()
        {
            bool wasRunning = _engine.IsRunning && !_engine.IsPaused;
            _engine.Stop();

            SclStandardLib.UseVirtualTime = false;
            SclStandardLib.VirtualTickCount = 0;

            _context = new ExecutionContext(_block, _getCode());
            _scanCount = 0;
            _minCycleUs = long.MaxValue;
            _maxCycleUs = 0;
            BindGrid();
            PopulateSignalTree();
            ReloadCode();
            _uiTimer.Start();
        }


        private void ReloadCode() { bool wasRunning = _engine != null && _engine.IsRunning && !_engine.IsPaused; _engine?.Stop(); try { string script = SclTranspiler.Transpile(_getCode(), _context); _engine.Compile(script, _context); if (wasRunning) _engine.Start(); } catch (Exception ex) { HandleEngineError(ex); } }

        private ContextMenuStrip BuildOutputContextMenu(RichTextBox rtb, string defaultFileName)
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add(new ToolStripMenuItem("🗑 Clear", null, (s, e) => {
                rtb.Clear();
            }));

            menu.Items.Add(new ToolStripMenuItem("📋 Copy All", null, (s, e) => {
                if (!string.IsNullOrEmpty(rtb.Text))
                    Clipboard.SetText(rtb.Text);
            }));

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("💾 Export to File...", null, (s, e) => {
                using (SaveFileDialog sfd = new SaveFileDialog
                {
                    Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|All Files (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = defaultFileName
                })
                {
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        System.IO.File.WriteAllText(sfd.FileName, rtb.Text);
                        MessageBox.Show("Exported successfully!", "Export",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }));

            menu.Items.Add(new ToolStripMenuItem("📝 Open in Text Editor", null, (s, e) => {
                try
                {
                    string tempFile = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        $"{defaultFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    System.IO.File.WriteAllText(tempFile, rtb.Text);
                    Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open editor: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }));

            return menu;
        }


        private void HandleEngineError(Exception ex)
        {
            if (this.InvokeRequired) { this.Invoke(new Action(() => HandleEngineError(ex))); return; }
            _uiTimer.Stop(); _lblStatus.Text = "Status: FAULT"; _lblStatus.ForeColor = Color.Red; _uiTimer.Start();
            using (Form errForm = new Form { Text = "Engine Error / Crash", Size = new Size(800, 600), StartPosition = FormStartPosition.CenterParent })
            {
                TextBox txt = new TextBox { Dock = DockStyle.Fill, Text = ex.Message, Font = new Font("Consolas", 10), ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, BackColor = Color.WhiteSmoke, ForeColor = Color.DarkRed };
                Button btnCopy = new Button { Text = "📋 Copy Error Text to Clipboard", Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI", 10, FontStyle.Bold), BackColor = Color.LightYellow };
                btnCopy.Click += (s2, e2) => { Clipboard.SetText(txt.Text); MessageBox.Show("Copied!", "Success"); };
                errForm.Controls.Add(txt); errForm.Controls.Add(btnCopy); errForm.ShowDialog(this);
            }
        }

        private void DgvWatch_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _watchRows.Count) return;
            string dir = _watchRows[e.RowIndex].Direction;
            if (dir == "Input" || dir == "InOut") { _dgvWatch.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.White; if (_dgvWatch.Columns[e.ColumnIndex].DataPropertyName == "LiveValue") e.CellStyle.Font = _boldFont; }
            else { _dgvWatch.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.WhiteSmoke; _dgvWatch.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Color.DimGray; }
        }
    }
}
