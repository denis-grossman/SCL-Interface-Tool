using FastColoredTextBoxNS;
using SCL_Interface_Tool.Core;
using SCL_Interface_Tool.ImageGeneration;
using SCL_Interface_Tool.Interfaces;
using SCL_Interface_Tool.Models;
using SCL_Interface_Tool.Parsers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SCL_Interface_Tool
{
    public partial class MainForm : Form
    {
        // Core UI Elements
        private DockPanel _dockPanel;

        // Changed to ToolPane to support XML Serialization
        private ToolPane _paneEditor;
        private ToolPane _paneElements;
        private ToolPane _paneLog;
        private ToolPane _paneAi;

        private FastColoredTextBox _rtbInput;
        private DataGridView _dgvElements;

        // Changed to ToolStrip UI controls
        private ToolStripComboBox _cmbBlocks;
        private ListBox _lstErrors;
        private ToolStripButton _btnGenerateImage;
        private ToolStripButton _btnSimulate;

        // Settings & Toolbars
        private AppSettings _settings;
        private ToolStripDropDownButton _btnCopyWebAi;
        private ToolStripDropDownButton _btnAskLocalAi;

        // AI Co-Pilot Controls
        private RichTextBox _rtbAiOutput;
        private Button _btnStopAi;
        private Button _btnApplyCode;
        private CancellationTokenSource _mainOllamaCts;

        private ToolStripStatusLabel _lblLengthLines;
        private ToolStripStatusLabel _lblPosition;

        private ISclParser _parser;
        private IImageGenerator _imageGenerator;
        private List<SclBlock> _parsedBlocks;

        private TextStyle _commentStyle = new TextStyle(Brushes.Green, null, FontStyle.Italic);
        private TextStyle _keyword1Style = new TextStyle(Brushes.Blue, null, FontStyle.Bold);
        private TextStyle _keyword2Style = new TextStyle(Brushes.DarkMagenta, null, FontStyle.Bold);
        private TextStyle _keyword3Style = new TextStyle(Brushes.DeepPink, null, FontStyle.Regular);
        private TextStyle _numberStyle = new TextStyle(Brushes.DarkOrange, null, FontStyle.Regular);
        private TextStyle _stringStyle = new TextStyle(Brushes.Brown, null, FontStyle.Regular);

        private string _kw1 = @"\b(AND|ANY|ARRAY|AT|BEGIN|BLOCK_DB|BLOCK_FB|BLOCK_FC|BLOCK_SDB|BOOL|BY|BYTE|CASE|CHAR|CONST|CONTINUE|COUNTER|DATA_BLOCK|DATE|DATE_AND_TIME|DINT|DIV|DO|DT|DWORD|ELSE|ELSIF|EN|END_CASE|END_CONST|END_DATA_BLOCK|END_FOR|END_FUNCTION|END_FUNCTION_BLOCK|END_IF|END_LABEL|END_ORGANIZATION_BLOCK|END_REPEAT|END_STRUCT|END_TYPE|END_VAR|END_WHILE|ENO|EXIT|FALSE|FOR|FUNCTION|FUNCTION_BLOCK|GOTO|IF|INT|LABEL|MOD|NIL|NOT|OF|OK|OR|ORGANIZATION_BLOCK|POINTER|PROGRAM|END_PROGRAM|REAL|REGION|END_REGION|REPEAT|RET_VAL|RETURN|S5TIME|STRING|STRUCT|THEN|TIME|TIME_OF_DAY|TIMER|TO|TOD|TRUE|TYPE|UNTIL|VAR|VAR_IN_OUT|VAR_INPUT|VAR_OUTPUT|VAR_TEMP|VOID|WHILE|WORD|XOR)\b";
        private string _kw2 = @"\b(ABS|SQR|SQRT|EXP|EXPD|LN|LOG|ACOS|ASIN|ATAN|COS|SIN|TAN|ROL|ROR|SHL|SHR|LEN|CONCAT|LEFT|RIGHT|MID|INSERT|DELETE|REPLACE|FIND|TON|TOF|TP|TONR)\b";
        private string _kw3 = @"\b(TITLE|VERSION|KNOW_HOW_PROTECT|AUTHOR|NAME|FAMILY)\b";

        private static readonly Regex _rxFoldOpen = new Regex(@"\b(?:FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE|ORGANIZATION_BLOCK|VAR_INPUT|VAR_OUTPUT|VAR_IN_OUT|VAR_TEMP|VAR|STRUCT|REGION|IF|CASE|FOR|WHILE|REPEAT)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _rxFoldClose = new Regex(@"\b(?:END_FUNCTION_BLOCK|END_FUNCTION|END_DATA_BLOCK|END_TYPE|END_ORGANIZATION_BLOCK|END_VAR|END_STRUCT|END_REGION|END_IF|END_CASE|END_FOR|END_WHILE|END_REPEAT)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // --- Helper Class for DockPanel XML Serialization ---
        public class ToolPane : DockContent
        {
            private readonly string _persistString;
            public ToolPane(string persistString) { _persistString = persistString; }
            protected override string GetPersistString() => _persistString;
        }

        public MainForm()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            InitializeCustomUI();

            _parser = new RegexSclParser();
            _imageGenerator = new GdiFbdImageGenerator();
            _parsedBlocks = new List<SclBlock>();

            this.Load += MainForm_Load;
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeCustomUI()
        {
            this.Text = "SCL Ninja - Interface Extractor, Simulator & AI Co-Pilot";
            this.MinimumSize = new Size(1000, 600);
            this.IsMdiContainer = true;

            // =========================================================
            // GLOBAL TOOLSTRIP
            // =========================================================
            ToolStrip toolStrip = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(5) };

            var btnView = new ToolStripDropDownButton("👁️ View Panels") { Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            var btnParse = new ToolStripButton("▶️ Parse SCL", null, BtnParse_Click) { DisplayStyle = ToolStripItemDisplayStyle.Text, BackColor = Color.FromArgb(144, 238, 144), Margin = new Padding(2) };
            var sep0 = new ToolStripSeparator();

            var btnImport = new ToolStripButton("📂 Import", null, BtnImport_Click) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var btnExport = new ToolStripButton("💾 Export", null, BtnExport_Click) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var sep1 = new ToolStripSeparator();

            var btnClear = new ToolStripButton("❌ Clear", null, (s, e) => { _rtbInput.SelectAll(); _rtbInput.ClearSelected(); }) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var btnCopy = new ToolStripButton("📋 Copy", null, (s, e) => { if (!string.IsNullOrEmpty(_rtbInput.Text)) Clipboard.SetText(_rtbInput.Text); }) { DisplayStyle = ToolStripItemDisplayStyle.Text };

            var btnFormat = new ToolStripButton("✨ Beautify", null, (s, e) => { _rtbInput.Text = SclFormatter.Format(_rtbInput.Text); }) { DisplayStyle = ToolStripItemDisplayStyle.Text, ForeColor = Color.DarkGoldenrod, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var sep2 = new ToolStripSeparator();

            var btnUndo = new ToolStripButton("↩️ Undo", null, (s, e) => _rtbInput.Undo()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var btnRedo = new ToolStripButton("↪️ Redo", null, (s, e) => _rtbInput.Redo()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var sep3 = new ToolStripSeparator();

            var btnColl = new ToolStripButton("➖ Collapse All", null, (s, e) => {
                for (int i = _rtbInput.LinesCount - 1; i >= 0; i--) if (!string.IsNullOrEmpty(_rtbInput[i].FoldingStartMarker)) _rtbInput.CollapseFoldingBlock(i);
            })
            { DisplayStyle = ToolStripItemDisplayStyle.Text };

            var btnExp = new ToolStripButton("➕ Expand All", null, (s, e) => _rtbInput.ExpandAllFoldingBlocks()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var sep4 = new ToolStripSeparator();

            _btnCopyWebAi = new ToolStripDropDownButton("📋 Copy for Web AI") { DisplayStyle = ToolStripItemDisplayStyle.Text, ForeColor = Color.DarkBlue, Font = new Font("Segoe UI", 9, FontStyle.Bold), ToolTipText = "Copy Prompt + Code to paste into ChatGPT/Claude" };
            _btnAskLocalAi = new ToolStripDropDownButton("🤖 Ask Local AI") { DisplayStyle = ToolStripItemDisplayStyle.Text, ForeColor = Color.DarkViolet, Font = new Font("Segoe UI", 9, FontStyle.Bold), ToolTipText = "Run prompt locally via Ollama" };
            BuildLlmDropdowns();

            var btnSettings = new ToolStripButton("⚙️ Settings", null, (s, e) => {
                if (new SettingsForm(_settings).ShowDialog() == DialogResult.OK) BuildLlmDropdowns();
            })
            { DisplayStyle = ToolStripItemDisplayStyle.Text };

            toolStrip.Items.AddRange(new ToolStripItem[] { btnView, new ToolStripSeparator(), btnParse, sep0, btnImport, btnExport, sep1, btnClear, btnCopy, btnFormat, sep2, btnUndo, btnRedo, sep3, btnColl, btnExp, sep4, _btnCopyWebAi, _btnAskLocalAi, new ToolStripSeparator(), btnSettings });

            // =========================================================
            // GLOBAL STATUS BAR
            // =========================================================
            StatusStrip statusStrip = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            _lblLengthLines = new ToolStripStatusLabel { Text = "length: 0  lines: 1" };
            ToolStripStatusLabel springLabel = new ToolStripStatusLabel { Spring = true };
            _lblPosition = new ToolStripStatusLabel { Text = "Ln: 1  Col: 1" };
            statusStrip.Items.AddRange(new ToolStripItem[] { _lblLengthLines, springLabel, _lblPosition });

            // =========================================================
            // DOCK PANEL INITIALIZATION
            // =========================================================
            _dockPanel = new DockPanel
            {
                Dock = DockStyle.Fill,
                Theme = new VS2015LightTheme(),
                DocumentStyle = DocumentStyle.DockingWindow
            };

            this.Controls.Add(_dockPanel);
            this.Controls.Add(toolStrip);
            this.Controls.Add(statusStrip);
            _dockPanel.BringToFront();

            // 1. DOCK PANE: SCL EDITOR (DOCUMENT)
            // Added HideOnClose = true to prevent object disposed exceptions
            _paneEditor = new ToolPane("PaneEditor") { Text = "📝 SCL Editor", CloseButtonVisible = true, HideOnClose = true };
            _rtbInput = new FastColoredTextBox { Dock = DockStyle.Fill, Language = Language.Custom, Font = new Font("Consolas", 10), ShowLineNumbers = true, TabLength = 4, BorderStyle = BorderStyle.None };
            _rtbInput.TextChangedDelayed += RtbInput_TextChangedDelayed;
            _rtbInput.SelectionChangedDelayed += (s, e) => UpdateStatusBar();
            _paneEditor.Controls.Add(_rtbInput);

            // 2. DOCK PANE: INTERFACE ELEMENTS (RIGHT PANEL)
            // Migrated to ToolStrip layout
            _paneElements = new ToolPane("PaneElements") { Text = "🎛️ Interface & Simulation", CloseButtonVisible = true, HideOnClose = true };

            ToolStrip tsElements = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(3), BackColor = Color.WhiteSmoke };
            ToolStripLabel lblBlock = new ToolStripLabel("Select Block: ");
            _cmbBlocks = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            _cmbBlocks.SelectedIndexChanged += CmbBlocks_SelectedIndexChanged;

            _btnGenerateImage = new ToolStripButton("🖼️ Gen FBD Image") { Enabled = false };
            _btnGenerateImage.Click += BtnGenerateImage_Click;

            _btnSimulate = new ToolStripButton("▶️ Simulate") { Enabled = false, BackColor = Color.LightGreen, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(5, 0, 0, 0) };
            _btnSimulate.Click += BtnSimulate_Click;

            tsElements.Items.AddRange(new ToolStripItem[] { lblBlock, _cmbBlocks, new ToolStripSeparator(), _btnGenerateImage, new ToolStripSeparator(), _btnSimulate });

            _dgvElements = new DataGridView { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, AllowUserToAddRows = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White };
            _dgvElements.CellToolTipTextNeeded += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) { var el = (InterfaceElement)_dgvElements.Rows[e.RowIndex].DataBoundItem; if (!string.IsNullOrEmpty(el.Comment)) e.ToolTipText = el.Comment; } };
            SetupGridContextMenu();

            _paneElements.Controls.Add(_dgvElements);
            _paneElements.Controls.Add(tsElements);
            _dgvElements.BringToFront(); // Ensures ToolStrip docks above the datagrid view

            // 3. DOCK PANE: PARSER LOG (BOTTOM TAB)
            _paneLog = new ToolPane("PaneLog") { Text = "⚠️ Parser Log", CloseButtonVisible = true, HideOnClose = true };
            _lstErrors = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Regular) };
            _paneLog.Controls.Add(_lstErrors);

            // 4. DOCK PANE: AI CO-PILOT (BOTTOM TAB)
            _paneAi = new ToolPane("PaneAi") { Text = "🤖 AI Co-Pilot", CloseButtonVisible = true, HideOnClose = true };
            Panel pnlAiTop = new Panel { Dock = DockStyle.Top, Height = 35 };
            _btnStopAi = new Button { Text = "⏹ Stop", Left = 5, Top = 5, Width = 60, Enabled = false, Font = new Font("Segoe UI", 8, FontStyle.Regular) };
            _btnStopAi.Click += (s, e) => { _mainOllamaCts?.Cancel(); _btnStopAi.Enabled = false; };
            _btnApplyCode = new Button { Text = "📝 Apply Code to Editor", Left = 70, Top = 5, Width = 160, Enabled = false, BackColor = Color.LightGreen, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            new ToolTip().SetToolTip(_btnApplyCode, "Replaces your current editor code with the AI's refactored code");
            Button btnClearAi = new Button { Text = "🗑 Clear", Left = 235, Top = 5, Width = 60, Font = new Font("Segoe UI", 8, FontStyle.Regular) };
            btnClearAi.Click += (s, e) => { _rtbAiOutput.Clear(); _btnApplyCode.Enabled = false; };
            pnlAiTop.Controls.AddRange(new Control[] { _btnStopAi, _btnApplyCode, btnClearAi });

            _rtbAiOutput = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.LightGray, Font = new Font("Consolas", 10), ReadOnly = true };
            _paneAi.Controls.Add(_rtbAiOutput);
            _paneAi.Controls.Add(pnlAiTop);

            // =========================================================
            // Dynamic Dropdown For Toggling Panels
            // =========================================================
            ToolPane[] allPanes = { _paneEditor, _paneElements, _paneLog, _paneAi };
            btnView.DropDownOpening += (s, e) =>
            {
                btnView.DropDownItems.Clear();
                foreach (var pane in allPanes)
                {
                    var item = new ToolStripMenuItem(pane.Text) { Checked = !pane.IsHidden };
                    item.Click += (s2, e2) => {
                        if (pane.IsHidden)
                        {
                            try { pane.Show(_dockPanel); }
                            catch { pane.Show(_dockPanel, DockState.Document); } // Fail-safe
                        }
                        else
                        {
                            pane.Hide();
                        }
                    };
                    btnView.DropDownItems.Add(item);
                }
            };
        }

        // --- Default Layout Fallback ---
        private void ApplyDefaultLayout()
        {
            try
            {
                _paneEditor.Show(_dockPanel, DockState.Document);
                _paneElements.Show(_dockPanel, DockState.DockRight);
                _paneLog.Show(_dockPanel, DockState.DockBottom);
                _paneAi.Show(_paneLog.Pane, null);
            }
            catch { } // Failsafe silently to prevent startup crashes
        }

        // --- Deserialization Callback for DockPanelSuite ---
        private IDockContent GetContentFromPersistString(string persistString)
        {
            switch (persistString)
            {
                case "PaneEditor": return _paneEditor;
                case "PaneElements": return _paneElements;
                case "PaneLog": return _paneLog;
                case "PaneAi": return _paneAi;
                default: return null;
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 1. Restore Window Size & State
            if (_settings.MainFormWidth >= MinimumSize.Width) this.Width = _settings.MainFormWidth;
            if (_settings.MainFormHeight >= MinimumSize.Height) this.Height = _settings.MainFormHeight;

            this.WindowState = _settings.MainFormState == FormWindowState.Minimized ? FormWindowState.Normal : _settings.MainFormState;

            // 2. Restore DockPanel Layout
            string layoutFile = AppSettings.GetMainLayoutFilePath();
            if (File.Exists(layoutFile))
            {
                try
                {
                    _dockPanel.LoadFromXml(layoutFile, GetContentFromPersistString);
                }
                catch
                {
                    ApplyDefaultLayout();
                }
            }
            else
            {
                ApplyDefaultLayout();
            }

            // 3. Restore Editor Content
            if (!string.IsNullOrEmpty(_settings.LastCode)) { _rtbInput.Text = _settings.LastCode; _rtbInput.ClearUndo(); }
            UpdateStatusBar();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _mainOllamaCts?.Cancel();
            _settings.LastCode = _rtbInput.Text;

            // 1. Save Hidden Columns
            _settings.HiddenColumns.Clear();
            foreach (DataGridViewColumn col in _dgvElements.Columns) if (!col.Visible && col.Name != "DisplayBounds") _settings.HiddenColumns.Add(col.Name);

            // 2. Save Window Size & State
            _settings.MainFormState = this.WindowState == FormWindowState.Minimized ? FormWindowState.Normal : this.WindowState;

            if (this.WindowState == FormWindowState.Normal)
            {
                _settings.MainFormWidth = this.Width;
                _settings.MainFormHeight = this.Height;
            }
            else
            {
                _settings.MainFormWidth = this.RestoreBounds.Width;
                _settings.MainFormHeight = this.RestoreBounds.Height;
            }

            _settings.Save();

            // 3. Save DockPanel Layout
            try
            {
                _dockPanel.SaveAsXml(AppSettings.GetMainLayoutFilePath());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save layout: {ex.Message}", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ==========================================
        // LLM PROMPT LOGIC
        // ==========================================
        private void BuildLlmDropdowns()
        {
            _btnCopyWebAi.DropDownItems.Clear();
            _btnAskLocalAi.DropDownItems.Clear();

            if (_settings?.Prompts == null) return;

            foreach (var prompt in _settings.Prompts)
            {
                var copyItem = new ToolStripMenuItem(prompt.Name);
                copyItem.Click += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(_rtbInput.Text)) { MessageBox.Show("Please paste some SCL code first.", "No Code", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    Clipboard.SetText($"{prompt.Text}\n\nCode:\n{_rtbInput.Text}");
                    MessageBox.Show($"Copied to clipboard!\n\nYou can now paste this directly into ChatGPT, Claude, or DeepSeek.", "Copied for Web AI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                };
                _btnCopyWebAi.DropDownItems.Add(copyItem);

                var localItem = new ToolStripMenuItem(prompt.Name);
                localItem.Click += async (s, e) => await RunLocalAiPromptAsync(prompt);
                _btnAskLocalAi.DropDownItems.Add(localItem);
            }
        }

        private async Task RunLocalAiPromptAsync(LlmPrompt prompt)
        {
            if (string.IsNullOrWhiteSpace(_rtbInput.Text))
            {
                MessageBox.Show("Please paste some SCL code into the editor first.", "No Code", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_paneAi.IsHidden) _paneAi.Show(_dockPanel);
            _paneAi.Activate();
            _rtbAiOutput.Clear();
            _rtbAiOutput.SelectionColor = Color.MediumPurple;
            _rtbAiOutput.AppendText($"🤖 Asking Local {_settings.OllamaModelName} to '{prompt.Name}'...\n(Note: Local CPU models are slower and less capable than Web AI)\n\n");

            _btnApplyCode.Enabled = false;
            _btnStopAi.Enabled = true;

            _mainOllamaCts?.Cancel();
            _mainOllamaCts = new CancellationTokenSource();
            var token = _mainOllamaCts.Token;

            string fullPrompt = $"{prompt.Text}\n\nCode:\n{_rtbInput.Text}";
            StringBuilder fullResponse = new StringBuilder();
            int lastUpdateLength = 0;

            System.Windows.Forms.Timer uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 100 };
            uiRefreshTimer.Tick += (senderTick, argsTick) => {
                if (this.IsDisposed || _rtbAiOutput.IsDisposed) return;
                lock (fullResponse)
                {
                    if (fullResponse.Length > lastUpdateLength)
                    {
                        string newText = fullResponse.ToString().Substring(lastUpdateLength);
                        lastUpdateLength = fullResponse.Length;
                        int start = _rtbAiOutput.TextLength;
                        _rtbAiOutput.AppendText(newText);
                        _rtbAiOutput.Select(start, newText.Length);
                        _rtbAiOutput.SelectionColor = Color.LightGray;
                        _rtbAiOutput.ScrollToCaret();
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

            if (!this.IsDisposed && !_btnStopAi.IsDisposed) _btnStopAi.Enabled = false;

            if (token.IsCancellationRequested || this.IsDisposed) return;

            lock (fullResponse)
            {
                if (fullResponse.Length > lastUpdateLength && !_rtbAiOutput.IsDisposed)
                {
                    _rtbAiOutput.AppendText(fullResponse.ToString().Substring(lastUpdateLength));
                    _rtbAiOutput.ScrollToCaret();
                }
            }

            string finalText = fullResponse.ToString();
            if (Regex.IsMatch(finalText, @"```[^\n]*\n(.*?)\n```", RegexOptions.Singleline))
            {
                if (!_btnApplyCode.IsDisposed) _btnApplyCode.Enabled = true;
                if (!_rtbAiOutput.IsDisposed)
                {
                    _rtbAiOutput.SelectionStart = _rtbAiOutput.TextLength;
                    _rtbAiOutput.SelectionColor = Color.LimeGreen;
                    _rtbAiOutput.AppendText("\n\n✅ Code Block Detected! Click 'Apply Code to Editor' to update your logic.\n");
                    _rtbAiOutput.ScrollToCaret();
                }
            }
        }

        private void BtnApplyCode_Click(object sender, EventArgs e)
        {
            string aiText = _rtbAiOutput.Text;
            var match = Regex.Match(aiText, @"```[^\n]*\n(.*?)\n```", RegexOptions.Singleline);

            if (match.Success)
            {
                string newCode = match.Groups[1].Value.Trim();

                if (!newCode.Contains("FUNCTION") && !newCode.Contains("DATA_BLOCK") && !newCode.Contains("TYPE"))
                {
                    var result = MessageBox.Show("The extracted code does not appear to contain a valid Siemens block definition (FUNCTION, DATA_BLOCK, etc.).\n\nApply anyway?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (result == DialogResult.No) return;
                }

                _rtbInput.Text = newCode;
                _btnApplyCode.Enabled = false;
                BtnParse_Click(null, null);
                MessageBox.Show("AI code applied to editor successfully!", "Code Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async Task StreamOllamaAsync(string promptText, Action<string> onTokenReceived, CancellationToken cancellationToken)
        {
            try
            {
                using (HttpClient client = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan })
                {
                    var requestBody = new { model = _settings.OllamaModelName, prompt = promptText, stream = true };
                    string jsonPayload = JsonSerializer.Serialize(requestBody);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    string url = $"{_settings.OllamaApiUrl.TrimEnd('/')}/api/generate";

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

        // ==========================================
        // EDITOR LOGIC
        // ==========================================
        private void UpdateStatusBar()
        {
            if (_rtbInput == null || _lblLengthLines == null || _lblPosition == null) return;
            int length = _rtbInput.Text.Length;
            int lines = _rtbInput.LinesCount;
            int selLength = _rtbInput.SelectionLength;

            int ln = _rtbInput.Selection.Start.iLine + 1;
            int col = _rtbInput.Selection.Start.iChar + 1;

            string lengthText = selLength > 0 ? $"length: {length} ({selLength} selected)" : $"length: {length}";
            _lblLengthLines.Text = $"{lengthText}   lines: {lines}";
            _lblPosition.Text = $"Ln: {ln}   Col: {col}";
        }

        private void RtbInput_TextChangedDelayed(object sender, TextChangedEventArgs e)
        {
            e.ChangedRange.ClearStyle(_commentStyle, _keyword1Style, _keyword2Style, _keyword3Style, _numberStyle, _stringStyle);
            e.ChangedRange.ClearFoldingMarkers();

            e.ChangedRange.SetStyle(_stringStyle, @"'[^']*'|""[^""]*""");
            e.ChangedRange.SetStyle(_commentStyle, @"//.*$|\(\*[\s\S]*?\*\)", RegexOptions.Multiline);
            e.ChangedRange.SetStyle(_numberStyle, @"\b\d+(\.\d+)?\b");
            e.ChangedRange.SetStyle(_keyword1Style, _kw1, RegexOptions.IgnoreCase);
            e.ChangedRange.SetStyle(_keyword2Style, _kw2, RegexOptions.IgnoreCase);
            e.ChangedRange.SetStyle(_keyword3Style, _kw3, RegexOptions.IgnoreCase);

            int cIdx = _rtbInput.GetStyleIndex(_commentStyle);
            int sIdx = _rtbInput.GetStyleIndex(_stringStyle);
            int ignoreMask = 0;
            if (cIdx >= 0) ignoreMask |= (1 << cIdx);
            if (sIdx >= 0) ignoreMask |= (1 << sIdx);

            for (int i = e.ChangedRange.Start.iLine; i <= e.ChangedRange.End.iLine; i++)
            {
                var line = _rtbInput[i];
                char[] cleanChars = new char[line.Count];
                for (int c = 0; c < line.Count; c++)
                {
                    int styleMask = (int)line[c].style;
                    if ((styleMask & ignoreMask) != 0) cleanChars[c] = ' ';
                    else cleanChars[c] = line[c].c;
                }
                string cleanText = new string(cleanChars);

                int openCount = _rxFoldOpen.Matches(cleanText).Count;
                int closeCount = _rxFoldClose.Matches(cleanText).Count;

                if (openCount > closeCount) line.FoldingStartMarker = "fold";
                else if (closeCount > openCount) line.FoldingEndMarker = "fold";
            }
            UpdateStatusBar();
        }

        private void BtnImport_Click(object sender, EventArgs e) { using (OpenFileDialog ofd = new OpenFileDialog { Filter = "SCL Files (*.scl)|*.scl|Text Files (*.txt)|*.txt|All Files (*.*)|*.*" }) { if (ofd.ShowDialog() == DialogResult.OK) { _rtbInput.Text = File.ReadAllText(ofd.FileName); _rtbInput.ClearUndo(); UpdateStatusBar(); } } }
        private void BtnExport_Click(object sender, EventArgs e) { using (SaveFileDialog sfd = new SaveFileDialog { Filter = "SCL File (*.scl)|*.scl|Text File (*.txt)|*.txt", DefaultExt = "scl" }) { if (sfd.ShowDialog() == DialogResult.OK) { File.WriteAllText(sfd.FileName, _rtbInput.Text); MessageBox.Show("File saved successfully!", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information); } } }

        // ==========================================
        // GRID & PARSING LOGIC
        // ==========================================
        private void SetupGridContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem copyWithHeaders = new ToolStripMenuItem("Copy Selection (with headers)");
            copyWithHeaders.Click += (s, e) => { _dgvElements.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText; var content = _dgvElements.GetClipboardContent(); if (content != null) Clipboard.SetDataObject(content); };
            ToolStripMenuItem copyWithoutHeaders = new ToolStripMenuItem("Copy Selection (without headers)");
            copyWithoutHeaders.Click += (s, e) => { _dgvElements.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText; var content = _dgvElements.GetClipboardContent(); if (content != null) Clipboard.SetDataObject(content); };
            menu.Items.Add(copyWithHeaders); menu.Items.Add(copyWithoutHeaders); menu.Items.Add(new ToolStripSeparator());
            menu.Opening += (s, e) => { while (menu.Items.Count > 3) menu.Items.RemoveAt(3); foreach (DataGridViewColumn col in _dgvElements.Columns) { if (col.Name == "DisplayBounds") continue; ToolStripMenuItem colItem = new ToolStripMenuItem(col.HeaderText) { CheckOnClick = true, Checked = col.Visible }; colItem.CheckedChanged += (sender, args) => col.Visible = colItem.Checked; menu.Items.Add(colItem); } };
            _dgvElements.ContextMenuStrip = menu;
        }

        private async void BtnParse_Click(object sender, EventArgs e)
        {
            if (_paneLog.IsHidden) _paneLog.Show(_dockPanel);
            _paneLog.Activate();
            _lstErrors.Items.Clear(); _cmbBlocks.Items.Clear(); _dgvElements.DataSource = null; _btnGenerateImage.Enabled = false;
            string sclText = _rtbInput.Text; if (string.IsNullOrWhiteSpace(sclText)) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                var (parsedBlocks, errors) = await Task.Run(() => { var blocks = _parser.Parse(sclText, out List<string> errs); return (blocks, errs); });
                _parsedBlocks = parsedBlocks; foreach (var err in errors) _lstErrors.Items.Add(err);
                if (_parsedBlocks.Count == 0) return;
                _lstErrors.Items.Add($"Successfully parsed {_parsedBlocks.Count} block(s).");

                int maxCmbWidth = _cmbBlocks.Width;
                using (Graphics g = this.CreateGraphics())
                {
                    foreach (var block in _parsedBlocks)
                    {
                        string txt = $"{block.BlockType}: {block.Name}";
                        _cmbBlocks.Items.Add(txt);
                        int txtW = (int)g.MeasureString(txt, _cmbBlocks.Font).Width;
                        if (txtW > maxCmbWidth) maxCmbWidth = txtW;
                    }
                }
                _cmbBlocks.ComboBox.DropDownWidth = maxCmbWidth + 20;
                _cmbBlocks.SelectedIndex = 0;
            }
            finally { Cursor = Cursors.Default; }
        }

        private void CmbBlocks_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmbBlocks.SelectedIndex < 0) return;
            var selectedBlock = _parsedBlocks[_cmbBlocks.SelectedIndex];
            _dgvElements.DataSource = selectedBlock.Elements.ToList();

            if (_dgvElements.Columns.Count > 0)
            {
                if (_dgvElements.Columns.Contains("Index")) _dgvElements.Columns["Index"].DisplayIndex = 0;
                if (_dgvElements.Columns.Contains("Name")) _dgvElements.Columns["Name"].DisplayIndex = 1;
                if (_dgvElements.Columns.Contains("DataType")) _dgvElements.Columns["DataType"].DisplayIndex = 2;
                if (_dgvElements.Columns.Contains("Direction")) _dgvElements.Columns["Direction"].DisplayIndex = 3;
                if (_dgvElements.Columns.Contains("InitialValue")) _dgvElements.Columns["InitialValue"].DisplayIndex = 4;
                if (_dgvElements.Columns.Contains("Attributes")) _dgvElements.Columns["Attributes"].DisplayIndex = 5;
                if (_dgvElements.Columns.Contains("Comment")) _dgvElements.Columns["Comment"].DisplayIndex = 6;
                if (_dgvElements.Columns.Contains("DisplayBounds")) _dgvElements.Columns["DisplayBounds"].Visible = false;
                foreach (var colName in _settings.HiddenColumns) if (_dgvElements.Columns.Contains(colName)) _dgvElements.Columns[colName].Visible = false;
                _dgvElements.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
                if (_dgvElements.Columns.Contains("Comment")) _dgvElements.Columns["Comment"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }

            string bt = selectedBlock.BlockType.ToUpper();
            bool isLogicBlock = bt == "FUNCTION_BLOCK" || bt == "FUNCTION" || bt == "PROGRAM" || bt == "ORGANIZATION_BLOCK";
            bool isDataBlock = bt == "DATA_BLOCK" || bt == "TYPE";

            _btnGenerateImage.Enabled = isLogicBlock;
            _btnSimulate.Enabled = isLogicBlock;

            if (isLogicBlock) _btnGenerateImage.Text = "🖼️ Gen FBD Image";
            else if (isDataBlock) _btnGenerateImage.Text = "📋 Data View (No FBD)";
            else _btnGenerateImage.Text = "🖼️ Image N/A";
        }

        private void BtnSimulate_Click(object sender, EventArgs e)
        {
            if (_cmbBlocks.SelectedIndex < 0) return;
            var selectedBlock = _parsedBlocks[_cmbBlocks.SelectedIndex];
            try
            {
                SimulationForm simForm = new SimulationForm(
                    selectedBlock, () => _rtbInput.Text, (GdiFbdImageGenerator)_imageGenerator);
                simForm.Show();
            }
            catch (InvalidOperationException ex) when (ex.Message == "SINGLETON_REDIRECT")
            {
                // Singleton redirected to existing window — nothing to do
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Simulation Initialization Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }


        private void BtnGenerateImage_Click(object sender, EventArgs e)
        {
            if (_cmbBlocks.SelectedIndex < 0) return;
            var selectedBlock = _parsedBlocks[_cmbBlocks.SelectedIndex];
            using (ImagePreviewForm previewForm = new ImagePreviewForm(selectedBlock, _imageGenerator)) { previewForm.ShowDialog(this); }
        }
    }

    public class ImagePreviewForm : Form
    {
        private SclBlock _block; private IImageGenerator _generator; private Bitmap _currentImage; private PictureBox _pb; private CheckBox _chkComments; private TrackBar _tbZoom; private Panel _pnlScroll;
        public ImagePreviewForm(SclBlock block, IImageGenerator generator) { _block = block; _generator = generator; InitializeComponent(); RegenerateImage(); }
        private void InitializeComponent()
        {
            this.Text = $"FBD View: {_block.Name}"; this.Size = new Size(1000, 700); this.StartPosition = FormStartPosition.CenterParent;
            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = Color.WhiteSmoke };
            _chkComments = new CheckBox { Text = "💬 Show Comments", Left = 10, Top = 12, AutoSize = true }; _chkComments.CheckedChanged += (s, e) => RegenerateImage();
            Label lblZoom = new Label { Text = "🔍 Zoom:", Left = 150, Top = 14, AutoSize = true }; _tbZoom = new TrackBar { Left = 210, Top = 5, Width = 150, Minimum = 25, Maximum = 300, Value = 100, TickFrequency = 25 }; _tbZoom.Scroll += (s, e) => ApplyZoom();
            Button btnResetZoom = new Button { Text = "🔄 100%", Left = 370, Top = 10, Width = 75 }; btnResetZoom.Click += (s, e) => { _tbZoom.Value = 100; ApplyZoom(); };
            Button btnCopy = new Button { Text = "📋 Copy Image", Left = 455, Top = 10, Width = 110 }; btnCopy.Click += BtnCopy_Click;
            Button btnSave = new Button { Text = "💾 Save PNG", Left = 575, Top = 10, Width = 100 }; btnSave.Click += BtnSave_Click;
            pnlTop.Controls.AddRange(new Control[] { _chkComments, lblZoom, _tbZoom, btnResetZoom, btnCopy, btnSave });
            _pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(245, 246, 248) };
            _pb = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, Location = new Point(0, 0) }; _pnlScroll.Controls.Add(_pb);
            this.Controls.Add(_pnlScroll); this.Controls.Add(pnlTop); _pnlScroll.BringToFront(); _pnlScroll.Resize += (s, e) => ApplyZoom(); SetupCustomTooltip();
        }
        private void RegenerateImage() { if (_currentImage != null) _currentImage.Dispose(); _currentImage = _generator.GenerateImage(_block, _chkComments.Checked); _pb.Image = _currentImage; ApplyZoom(); }
        private void ApplyZoom() { if (_currentImage == null) return; float factor = _tbZoom.Value / 100f; int newWidth = (int)(_currentImage.Width * factor); int newHeight = (int)(_currentImage.Height * factor); _pb.Size = new Size(newWidth, newHeight); int x = Math.Max(0, (_pnlScroll.ClientSize.Width - newWidth) / 2); int y = Math.Max(0, (_pnlScroll.ClientSize.Height - newHeight) / 2); _pb.Location = new Point(x, y); }
        private void BtnCopy_Click(object sender, EventArgs e) { if (_currentImage != null) { Clipboard.SetImage(_currentImage); MessageBox.Show("Image copied to clipboard!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); } }
        private void BtnSave_Click(object sender, EventArgs e) { if (_currentImage == null) return; using (SaveFileDialog sfd = new SaveFileDialog()) { sfd.Filter = "PNG Image|*.png"; sfd.FileName = $"{_block.Name}_FBD.png"; if (sfd.ShowDialog() == DialogResult.OK) { _currentImage.Save(sfd.FileName, ImageFormat.Png); MessageBox.Show("Image saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); } } }
        private void SetupCustomTooltip()
        {
            ToolTip imgToolTip = new ToolTip { OwnerDraw = true, AutoPopDelay = 10000, InitialDelay = 200, ReshowDelay = 100 }; Font tipFont = new Font("Segoe UI", 9f);
            imgToolTip.Draw += (s, e) => { e.Graphics.FillRectangle(SystemBrushes.Info, e.Bounds); e.Graphics.DrawRectangle(Pens.Black, new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1)); e.Graphics.DrawString(e.ToolTipText, tipFont, Brushes.Black, new PointF(2, 2)); };
            imgToolTip.Popup += (s, e) => { e.ToolTipSize = TextRenderer.MeasureText(imgToolTip.GetToolTip(e.AssociatedControl), tipFont); e.ToolTipSize = new Size(e.ToolTipSize.Width + 6, e.ToolTipSize.Height + 4); };
            InterfaceElement currentHoverElement = null;
            _pb.MouseMove += (s, args) => {
                if (_currentImage == null) return; float factor = _tbZoom.Value / 100f; int imgX = (int)(args.X / factor); int imgY = (int)(args.Y / factor); Point p = new Point(imgX, imgY);
                var hoverElement = _block.Elements.FirstOrDefault(el => el.DisplayBounds.Contains(p));
                if (hoverElement != currentHoverElement) { currentHoverElement = hoverElement; if (hoverElement != null) { string tip = $"{hoverElement.Direction.ToString().ToUpper()}: {hoverElement.DataType}"; if (!string.IsNullOrWhiteSpace(hoverElement.Comment)) tip += $" / {hoverElement.Comment}"; imgToolTip.SetToolTip(_pb, tip); } else imgToolTip.SetToolTip(_pb, ""); }
            };
        }
    }
}
