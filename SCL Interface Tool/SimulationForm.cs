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
using ExecutionContext = SCL_Interface_Tool.Simulation.ExecutionContext;

namespace SCL_Interface_Tool
{
    public partial class SimulationForm : Form
    {
        private AppSettings _appSettings;
        private SclBlock _block;
        private Func<string> _getCode;
        private Bitmap _baseImage;
        private ExecutionContext _context;
        private SimulationEngine _engine;

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

        // Cancellation switch for background AI tasks
        private CancellationTokenSource _ollamaCts;
        private Button _btnStopAi; // NEW STOP BUTTON

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
            _appSettings = AppSettings.Load();
            _block = block;
            _getCode = getCode;
            _baseImage = imageGen.GenerateImage(block, false);
            InitializeUI();
            this.Shown += (s, e) => InitializeSimulation();
        }

        private void InitializeUI()
        {
            this.Text = $"Simulation Commissioning: {_block.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(800, 500);

            int imgW = _baseImage.Width + 30;
            int imgH = _baseImage.Height + 20;
            this.Size = new Size(Math.Max(1300, imgW + 500), Math.Max(700, imgH + 200));

            ToolStrip ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(5) };
            var btnStart = new ToolStripButton("▶ Start", null, (s, e) => _engine?.Start()) { ForeColor = Color.Green, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnPause = new ToolStripButton("⏸ Pause", null, (s, e) => _engine?.Pause()) { ForeColor = Color.DarkOrange, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnStop = new ToolStripButton("⏹ Stop", null, (s, e) => _engine?.Stop()) { ForeColor = Color.Red, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnRestart = new ToolStripButton("🔄 Reset Memory", null, (s, e) => RestartSimulation());
            var btnReload = new ToolStripButton("🔂 Apply Code Changes", null, (s, e) => ReloadCode()) { ForeColor = Color.DarkMagenta, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            _lblStatus = new ToolStripLabel("Status: IDLE") { Font = new Font("Consolas", 10, FontStyle.Bold) };

            ts.Items.AddRange(new ToolStripItem[] { btnStart, btnPause, btnStop, new ToolStripSeparator(), btnRestart, btnReload, new ToolStripSeparator(), _lblStatus });
            this.Controls.Add(ts);

            TableLayoutPanel mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, imgW));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            TabControl tabLeft = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            TabPage tabWatch = new TabPage("🔍 Live Watch Table");
            _dgvWatch = new SimDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
            _boldFont = new Font(_dgvWatch.Font, FontStyle.Bold);
            _dgvWatch.CellValueChanged += DgvWatch_CellValueChanged;
            _dgvWatch.CellFormatting += DgvWatch_CellFormatting;
            _dgvWatch.CellDoubleClick += DgvWatch_CellDoubleClick;
            tabWatch.Controls.Add(_dgvWatch);

            TabPage tabTest = new TabPage("⚙️ Automated Testing");
            SplitContainer splitTest = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 250 };

            FastColoredTextBoxNS.FastColoredTextBox rtbScript = new FastColoredTextBoxNS.FastColoredTextBox
            {
                Dock = DockStyle.Fill,
                Language = FastColoredTextBoxNS.Language.Custom,
                Font = new Font("Consolas", 10),
                ShowLineNumbers = true,
                BackColor = Color.FromArgb(245, 245, 245)
            };

            var dslKeywordStyle = new FastColoredTextBoxNS.TextStyle(Brushes.Blue, null, FontStyle.Bold);
            var dslCommentStyle = new FastColoredTextBoxNS.TextStyle(Brushes.Green, null, FontStyle.Italic);

            rtbScript.TextChangedDelayed += (s, ev) => {
                ev.ChangedRange.ClearStyle(dslKeywordStyle, dslCommentStyle);
                ev.ChangedRange.SetStyle(dslKeywordStyle, @"\b(SET|RUN|ASSERT|SCANS|MS)\b", RegexOptions.IgnoreCase);
                ev.ChangedRange.SetStyle(dslCommentStyle, @"//.*");
            };
            rtbScript.Text = "// Click '🤖 Generate via AI' to automatically write a script\n";

            // --- REDESIGNED AI TOOLBAR (WITH STOP BUTTON) ---
            Panel pnlTestTop = new Panel { Dock = DockStyle.Top, Height = 35 };
            Button btnRunTest = new Button { Text = "▶ Run Script", Left = 5, Top = 5, Width = 90, BackColor = Color.LightGreen, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            Button btnLoadTest = new Button { Text = "📂 Load", Left = 100, Top = 5, Width = 55, Font = new Font("Segoe UI", 8, FontStyle.Regular) };
            Button btnSaveTest = new Button { Text = "💾 Save", Left = 160, Top = 5, Width = 55, Font = new Font("Segoe UI", 8, FontStyle.Regular) };
            Button btnHelp = new Button { Text = "💡 Help", Left = 220, Top = 5, Width = 55, Font = new Font("Segoe UI", 8, FontStyle.Regular) };

            Button btnGenAI = new Button { Text = "🤖 Generate", Left = 280, Top = 5, Width = 95, Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.DarkViolet };
            Button btnAnalyzeAI = new Button { Text = "🧠 Analyze", Left = 380, Top = 5, Width = 90, Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.DarkBlue };

            // NEW STOP BUTTON
            _btnStopAi = new Button { Text = "⏹ Stop", Left = 475, Top = 5, Width = 65, Enabled = false, Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.DarkRed };
            _btnStopAi.Click += (s, e) => { _ollamaCts?.Cancel(); _btnStopAi.Enabled = false; };

            pnlTestTop.Controls.AddRange(new Control[] { btnRunTest, btnLoadTest, btnSaveTest, btnHelp, btnGenAI, btnAnalyzeAI, _btnStopAi });

            RichTextBox rtbLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.LightGray, Font = new Font("Consolas", 9), ReadOnly = true };

            btnLoadTest.Click += (s, e) => { using (OpenFileDialog ofd = new OpenFileDialog { Filter = "SCL Test Files (*.scltest)|*.scltest|Text Files (*.txt)|*.txt" }) { if (ofd.ShowDialog() == DialogResult.OK) rtbScript.Text = System.IO.File.ReadAllText(ofd.FileName); } };
            btnSaveTest.Click += (s, e) => { using (SaveFileDialog sfd = new SaveFileDialog { Filter = "SCL Test Files (*.scltest)|*.scltest", DefaultExt = "scltest", FileName = $"{_block.Name}_Test.scltest" }) { if (sfd.ShowDialog() == DialogResult.OK) { System.IO.File.WriteAllText(sfd.FileName, rtbScript.Text); MessageBox.Show("Test script saved!", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information); } } };
            btnHelp.Click += (s, e) => MessageBox.Show("DSL Syntax Guide:\n\n1. SET Variable = Value\n2. RUN [X] SCANS\n3. RUN [X] MS\n4. ASSERT Variable == Value", "Auto-Test Help", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // =========================================================
            // OLLAMA REAL-TIME STREAMING: GENERATE SCRIPT
            // =========================================================
            btnGenAI.Click += async (s, e) =>
            {
                _ollamaCts?.Cancel();
                _ollamaCts = new CancellationTokenSource();
                var token = _ollamaCts.Token;

                btnGenAI.Enabled = false;
                _btnStopAi.Enabled = true; // Enable Stop button

                rtbLog.Clear();
                rtbLog.SelectionColor = Color.MediumPurple;
                rtbLog.AppendText("🤖 AI is thinking... (Streaming real-time)\n\n");

                string promptTemplate = _appSettings.Prompts.FirstOrDefault(p => p.Name == "Generate Unit Test Script")?.Text ?? "Generate a test.";
                string fullPrompt = $"{promptTemplate}\n\nCode:\n{_getCode()}";

                StringBuilder fullResponse = new StringBuilder();
                int lastUpdateLength = 0;

                System.Windows.Forms.Timer uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 100 };
                uiRefreshTimer.Tick += (senderTick, argsTick) => {
                    if (this.IsDisposed || rtbLog.IsDisposed) return;
                    lock (fullResponse)
                    {
                        if (fullResponse.Length > lastUpdateLength)
                        {
                            string newText = fullResponse.ToString().Substring(lastUpdateLength);
                            lastUpdateLength = fullResponse.Length;

                            int start = rtbLog.TextLength;
                            rtbLog.AppendText(newText);
                            rtbLog.Select(start, newText.Length);
                            rtbLog.SelectionColor = Color.LightGray;
                            rtbLog.ScrollToCaret();
                        }
                    }
                };
                uiRefreshTimer.Start();

                Action<string> streamHandler = (tokenText) => {
                    if (this.IsDisposed) return;
                    lock (fullResponse) { fullResponse.Append(tokenText); }
                };

                await StreamOllamaAsync(fullPrompt, streamHandler, token);

                uiRefreshTimer.Stop();
                uiRefreshTimer.Dispose();

                if (!this.IsDisposed && !_btnStopAi.IsDisposed) _btnStopAi.Enabled = false; // Disable Stop button

                if (token.IsCancellationRequested || this.IsDisposed) return;

                lock (fullResponse)
                {
                    if (fullResponse.Length > lastUpdateLength && !rtbLog.IsDisposed)
                    {
                        rtbLog.AppendText(fullResponse.ToString().Substring(lastUpdateLength));
                        rtbLog.ScrollToCaret();
                    }
                }

                // Parse out valid DSL
                string finalText = fullResponse.ToString();
                string codeOnly = Regex.Replace(finalText, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                var codeMatch = Regex.Match(codeOnly, @"```[^\n]*\n(.*?)\n```", RegexOptions.Singleline);
                if (codeMatch.Success) codeOnly = codeMatch.Groups[1].Value;

                var cleanDslLines = codeOnly.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => {
                        string t = line.ToUpper();
                        return t.StartsWith("SET ") || t.StartsWith("RUN ") || t.StartsWith("ASSERT ") || t.StartsWith("//");
                    });

                string cleanScript = string.Join(Environment.NewLine, cleanDslLines);

                if (!string.IsNullOrWhiteSpace(cleanScript) && !rtbScript.IsDisposed)
                {
                    rtbScript.Text = cleanScript + Environment.NewLine;
                    rtbLog.SelectionStart = rtbLog.TextLength;
                    rtbLog.SelectionColor = Color.LimeGreen;
                    rtbLog.AppendText("\n\n✅ Generation Complete! Code filtered and moved to Script Editor.\n");
                }
                else if (!rtbLog.IsDisposed)
                {
                    rtbLog.SelectionStart = rtbLog.TextLength;
                    rtbLog.SelectionColor = Color.Red;
                    rtbLog.AppendText("\n\n❌ AI did not return a valid DSL script format.\n");
                }

                if (!btnGenAI.IsDisposed) btnGenAI.Enabled = true;
            };

            // =========================================================
            // OLLAMA REAL-TIME STREAMING: ANALYZE LOGS
            // =========================================================
            btnAnalyzeAI.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(rtbLog.Text)) { MessageBox.Show("Please run a test first to generate logs.", "No Logs", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

                _ollamaCts?.Cancel();
                _ollamaCts = new CancellationTokenSource();
                var token = _ollamaCts.Token;

                btnAnalyzeAI.Enabled = false;
                _btnStopAi.Enabled = true; // Enable Stop button

                string analysisPrompt = $"You are a Senior Siemens PLC Engineer. Analyze the following failed Unit Test execution.\n\n" +
                                        $"--- SCL CODE ---\n{_getCode()}\n\n--- TEST SCRIPT ---\n{rtbScript.Text}\n\n--- EXECUTION LOG ---\n{rtbLog.Text}\n\n" +
                                        $"Provide a concise analysis of why the assertions failed. Is it a bug in the code, or a mistake in the test logic?";

                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.SelectionColor = Color.Gold;
                rtbLog.AppendText("\n\n=== 🧠 AI ANALYSIS ===\n");

                StringBuilder fullResponse = new StringBuilder();
                int lastUpdateLength = 0;

                System.Windows.Forms.Timer uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 100 };
                uiRefreshTimer.Tick += (senderTick, argsTick) => {
                    if (this.IsDisposed || rtbLog.IsDisposed) return;
                    lock (fullResponse)
                    {
                        if (fullResponse.Length > lastUpdateLength)
                        {
                            string newText = fullResponse.ToString().Substring(lastUpdateLength);
                            lastUpdateLength = fullResponse.Length;

                            int start = rtbLog.TextLength;
                            rtbLog.AppendText(newText);
                            rtbLog.Select(start, newText.Length);
                            rtbLog.SelectionColor = Color.Cyan;
                            rtbLog.ScrollToCaret();
                        }
                    }
                };
                uiRefreshTimer.Start();

                Action<string> streamHandler = (tokenText) => {
                    if (this.IsDisposed) return;
                    lock (fullResponse) { fullResponse.Append(tokenText); }
                };

                await StreamOllamaAsync(analysisPrompt, streamHandler, token);

                uiRefreshTimer.Stop();
                uiRefreshTimer.Dispose();

                if (!this.IsDisposed && !_btnStopAi.IsDisposed) _btnStopAi.Enabled = false; // Disable Stop button

                if (token.IsCancellationRequested || this.IsDisposed) return;

                lock (fullResponse)
                {
                    if (fullResponse.Length > lastUpdateLength && !rtbLog.IsDisposed)
                    {
                        rtbLog.AppendText(fullResponse.ToString().Substring(lastUpdateLength));
                        rtbLog.ScrollToCaret();
                    }
                }

                if (!btnAnalyzeAI.IsDisposed) btnAnalyzeAI.Enabled = true;
            };

            btnRunTest.Click += async (s, e) =>
            {
                if (_engine == null || _context == null) return;
                bool wasRunning = _engine.IsRunning && !_engine.IsPaused;
                _engine.Pause();
                rtbLog.Clear();

                Action<string, Color, bool> appendLog = null;
                appendLog = (text, color, bold) => {
                    if (rtbLog.InvokeRequired) { rtbLog.Invoke(new Action(() => appendLog(text, color, bold))); return; }
                    int start = rtbLog.TextLength;
                    rtbLog.AppendText(text + "\n");
                    rtbLog.Select(start, text.Length);
                    rtbLog.SelectionColor = color;
                    if (bold) rtbLog.SelectionFont = new Font(rtbLog.Font, FontStyle.Bold);
                    rtbLog.ScrollToCaret();
                };

                var runner = new AutoTestRunner(_context, _engine, appendLog);
                btnRunTest.Enabled = false;
                await runner.RunScriptAsync(rtbScript.Text);
                btnRunTest.Enabled = true;
                if (wasRunning) _engine.Start();
                UpdateUI();
            };

            splitTest.Panel1.Controls.Add(rtbScript);
            splitTest.Panel1.Controls.Add(pnlTestTop);
            splitTest.Panel2.Controls.Add(rtbLog);
            tabTest.Controls.Add(splitTest);

            tabLeft.TabPages.Add(tabWatch);
            tabLeft.TabPages.Add(tabTest);
            mainLayout.Controls.Add(tabLeft, 0, 0);

            TableLayoutPanel rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, imgH));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Panel pnlImage = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(245, 246, 248) };
            _pbLive = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, Image = _baseImage };
            _pbLive.Paint += PbLive_Paint;
            pnlImage.Controls.Add(_pbLive);
            rightPanel.Controls.Add(pnlImage, 0, 0);

            Panel pnlStats = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(235, 240, 248), Padding = new Padding(10), BorderStyle = BorderStyle.FixedSingle };
            _lblStats = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(40, 60, 90), Font = new Font("Consolas", 9), Text = "Waiting...", TextAlign = ContentAlignment.TopLeft };
            pnlStats.Controls.Add(_lblStats);
            rightPanel.Controls.Add(pnlStats, 0, 1);

            mainLayout.Controls.Add(rightPanel, 1, 0);
            this.Controls.Add(mainLayout);
            mainLayout.BringToFront();

            _uiTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _uiTimer.Tick += (s, e) => UpdateUI();

            this.FormClosing += (s, e) => {
                _ollamaCts?.Cancel();
                _engine?.Stop();
                _uiTimer?.Stop();
                _boldFont?.Dispose();
            };
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
                                        if (!string.IsNullOrEmpty(token) && !cancellationToken.IsCancellationRequested)
                                        {
                                            onTokenReceived?.Invoke(token);
                                        }
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
                if (!cancellationToken.IsCancellationRequested)
                    onTokenReceived?.Invoke($"\n\n❌ [ERROR]: {ex.Message}\nCheck Ollama connection.");
            }
        }

        private void InitializeSimulation() { _context = new ExecutionContext(_block, _getCode()); _engine = new SimulationEngine(); _engine.OnError += HandleEngineError; BindGrid(); ReloadCode(); _uiTimer.Start(); }

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
                _lblStats.ForeColor = Color.FromArgb(40, 60, 90); _lblStats.Text = $"  Statistics\n  ──────────────────────────────────────────────────────────\n  Cycle Time:  {_lastCycleUs,8} µs\n  Min Cycle:   {_minCycleUs,8} µs\n  Max Cycle:   {_maxCycleUs,8} µs\n  Scan Count:  {_scanCount,8}\n  ──────────────────────────────────────────────────────────\n  Block: {_block.Name}";
            }
            else if (_engine.IsPaused) { _lblStatus.Text = "PAUSED"; _lblStatus.ForeColor = Color.DarkOrange; } else { _lblStatus.Text = "STOPPED"; _lblStatus.ForeColor = Color.Red; _lblStats.Text = $"  Statistics\n  ──────────────────────────────────────────────────────────\n  Status: STOPPED\n  ──────────────────────────────────────────────────────────\n  Block: {_block.Name}"; }

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

        private void RestartSimulation() { bool wasRunning = _engine.IsRunning && !_engine.IsPaused; _engine.Stop(); _context = new ExecutionContext(_block, _getCode()); _scanCount = 0; _minCycleUs = long.MaxValue; _maxCycleUs = 0; BindGrid(); try { string script = SclTranspiler.Transpile(_getCode(), _context); _engine.Compile(script, _context); if (wasRunning) _engine.Start(); } catch (Exception ex) { HandleEngineError(ex); } }
        private void ReloadCode() { bool wasRunning = _engine != null && _engine.IsRunning && !_engine.IsPaused; _engine?.Stop(); try { string script = SclTranspiler.Transpile(_getCode(), _context); _engine.Compile(script, _context); if (wasRunning) _engine.Start(); } catch (Exception ex) { HandleEngineError(ex); } }

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
