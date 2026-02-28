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
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ExecutionContext = SCL_Interface_Tool.Simulation.ExecutionContext;

namespace SCL_Interface_Tool
{
    public partial class SimulationForm : Form
    {
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

        // Custom Flattened Data Model
        public class WatchRow
        {
            public string Name { get; set; }
            public string DataType { get; set; }
            public string Direction { get; set; }
            public string LiveValue { get; set; }
            public string Comment { get; set; }
            public string ParentName { get; set; }
            public object SubKey { get; set; } // int for array index, string for dict key
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
            this.Size = new Size(Math.Max(1200, imgW + 500), Math.Max(700, imgH + 200));

            ToolStrip ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(5) };
            var btnStart = new ToolStripButton("▶ Start", null, (s, e) => _engine?.Start()) { ForeColor = Color.Green, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnPause = new ToolStripButton("⏸ Pause", null, (s, e) => _engine?.Pause()) { ForeColor = Color.DarkOrange, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnStop = new ToolStripButton("⏹ Stop", null, (s, e) => _engine?.Stop()) { ForeColor = Color.Red, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var btnRestart = new ToolStripButton("🔄 Reset Memory", null, (s, e) => RestartSimulation());
            var btnReload = new ToolStripButton("🔁 Apply Code Changes", null, (s, e) => ReloadCode()) { ForeColor = Color.DarkMagenta, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            _lblStatus = new ToolStripLabel("Status: IDLE") { Font = new Font("Consolas", 10, FontStyle.Bold) };

            ts.Items.AddRange(new ToolStripItem[] { btnStart, btnPause, btnStop, new ToolStripSeparator(), btnRestart, btnReload, new ToolStripSeparator(), _lblStatus });
            this.Controls.Add(ts);

            TableLayoutPanel mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, imgW));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _dgvWatch = new SimDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
            _boldFont = new Font(_dgvWatch.Font, FontStyle.Bold);
            _dgvWatch.CellValueChanged += DgvWatch_CellValueChanged;
            _dgvWatch.CellFormatting += DgvWatch_CellFormatting;
            _dgvWatch.CellDoubleClick += DgvWatch_CellDoubleClick;
            mainLayout.Controls.Add(_dgvWatch, 0, 0);

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
            this.FormClosing += (s, e) => { _engine?.Stop(); _uiTimer?.Stop(); _boldFont?.Dispose(); };
        }

        private void InitializeSimulation()
        {
            _context = new ExecutionContext(_block, _getCode());
            _engine = new SimulationEngine();
            _engine.OnError += HandleEngineError;
            BindGrid();
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

                // Flatten Arrays
                if (m.CurrentValue is Array arr)
                {
                    int lo = 0, hi = arr.Length - 1;
                    var match = Regex.Match(dt, @"\[\s*(\d+)\s*\.\.\s*(\d+)\s*\]");
                    if (match.Success) { lo = int.Parse(match.Groups[1].Value); hi = int.Parse(match.Groups[2].Value); }

                    for (int i = lo; i <= hi; i++)
                    {
                        object val = arr.GetValue(i);
                        _watchRows.Add(new WatchRow { Name = $"{m.Name}[{i}]", ParentName = m.Name, SubKey = i, DataType = dt, Direction = m.Direction.ToString(), LiveValue = FormatPrimitive(val), Comment = comment, IsBool = val is bool });
                    }
                }
                // Flatten Structs
                else if (m.CurrentValue is Dictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                    {
                        _watchRows.Add(new WatchRow { Name = $"{m.Name}.{kvp.Key}", ParentName = m.Name, SubKey = kvp.Key, DataType = "Struct Field", Direction = m.Direction.ToString(), LiveValue = FormatPrimitive(kvp.Value), Comment = comment, IsBool = kvp.Value is bool });
                    }
                }
                // Standard Variables
                else
                {
                    _watchRows.Add(new WatchRow { Name = m.Name, ParentName = m.Name, SubKey = null, DataType = dt, Direction = m.Direction.ToString(), LiveValue = FormatPrimitive(m.CurrentValue), Comment = comment, IsBool = m.CurrentValue is bool });
                }
            }

            _dgvWatch.DataSource = _watchRows;
            _dgvWatch.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
            if (_dgvWatch.Columns.Contains("Name")) _dgvWatch.Columns["Name"].ReadOnly = true;
            if (_dgvWatch.Columns.Contains("DataType")) _dgvWatch.Columns["DataType"].ReadOnly = true;
            if (_dgvWatch.Columns.Contains("Direction")) _dgvWatch.Columns["Direction"].ReadOnly = true;
            if (_dgvWatch.Columns.Contains("ParentName")) _dgvWatch.Columns["ParentName"].Visible = false;
            if (_dgvWatch.Columns.Contains("SubKey")) _dgvWatch.Columns["SubKey"].Visible = false;
            if (_dgvWatch.Columns.Contains("IsBool")) _dgvWatch.Columns["IsBool"].Visible = false;
            if (_dgvWatch.Columns.Contains("Comment")) { _dgvWatch.Columns["Comment"].ReadOnly = true; _dgvWatch.Columns["Comment"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; }
            if (_dgvWatch.Columns.Contains("LiveValue")) { _dgvWatch.Columns["LiveValue"].HeaderText = "✏️ Monitor / Force"; _dgvWatch.Columns["LiveValue"].Width = 120; }
        }

        private string FormatPrimitive(object val)
        {
            if (val is bool b) return b ? "True" : "False";
            if (val is float f) return f.ToString("F2", CultureInfo.InvariantCulture);
            return val?.ToString() ?? "0";
        }

        private object ParsePrimitive(string str, object existingVal)
        {
            if (existingVal is bool) return str.Equals("true", StringComparison.OrdinalIgnoreCase) || str == "1";
            if (existingVal is float) return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0.0f;
            if (existingVal is string) return str;
            if (existingVal is long) return long.TryParse(str, out long l) ? l : 0L;
            return int.TryParse(str, out int i) ? i : 0;
        }

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
                    if (row.SubKey is int idx) existingVal = ((Array)tag.CurrentValue).GetValue(idx);
                    else if (row.SubKey is string key) existingVal = ((Dictionary<string, object>)tag.CurrentValue)[key];
                    else existingVal = tag.CurrentValue;

                    object newVal = ParsePrimitive(row.LiveValue, existingVal);

                    if (row.SubKey is int idx2) ((Array)tag.CurrentValue).SetValue(newVal, idx2);
                    else if (row.SubKey is string key2) ((Dictionary<string, object>)tag.CurrentValue)[key2] = newVal;
                    else tag.CurrentValue = newVal;

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
                bool current = row.LiveValue.Equals("True", StringComparison.OrdinalIgnoreCase);
                row.LiveValue = current ? "False" : "True";

                lock (_engine.MemoryLock)
                {
                    if (_context.Memory.TryGetValue(row.ParentName, out var tag))
                    {
                        if (row.SubKey is int idx) ((Array)tag.CurrentValue).SetValue(!current, idx);
                        else if (row.SubKey is string key) ((Dictionary<string, object>)tag.CurrentValue)[key] = !current;
                        else tag.CurrentValue = !current;
                    }
                }
                _dgvWatch.InvalidateRow(e.RowIndex);
                _dgvWatch.EndEdit();
            }
        }

        private void UpdateUI()
        {
            if (_engine == null) return;

            if (_engine.IsRunning && !_engine.IsPaused)
            {
                _lastCycleUs = _engine.CycleTimeTicks * 1_000_000 / Stopwatch.Frequency;
                _lblStatus.Text = $"RUNNING ({_lastCycleUs} µs)";
                _lblStatus.ForeColor = Color.Green;
                _scanCount++;
                if (_lastCycleUs > 0 && _lastCycleUs < _minCycleUs) _minCycleUs = _lastCycleUs;
                if (_lastCycleUs > _maxCycleUs) _maxCycleUs = _lastCycleUs;

                _lblStats.ForeColor = Color.FromArgb(40, 60, 90);
                _lblStats.Text = $"  Statistics\n  ─────────────────────────\n  Cycle Time:  {_lastCycleUs,8} µs\n  Min Cycle:   {_minCycleUs,8} µs\n  Max Cycle:   {_maxCycleUs,8} µs\n  Scan Count:  {_scanCount,8}\n  ─────────────────────────\n  Block: {_block.Name}";
            }
            else if (_engine.IsPaused) { _lblStatus.Text = "PAUSED"; _lblStatus.ForeColor = Color.DarkOrange; }
            else { _lblStatus.Text = "STOPPED"; _lblStatus.ForeColor = Color.Red; _lblStats.Text = $"  Statistics\n  ─────────────────────────\n  Status: STOPPED\n  ─────────────────────────\n  Block: {_block.Name}"; }

            if (_dgvWatch.IsCurrentCellInEditMode) return;

            lock (_engine.MemoryLock)
            {
                for (int i = 0; i < _watchRows.Count; i++)
                {
                    var row = _watchRows[i];
                    if (_context.Memory.TryGetValue(row.ParentName, out var mem))
                    {
                        object val;
                        if (row.SubKey is int idx) val = ((Array)mem.CurrentValue).GetValue(idx);
                        else if (row.SubKey is string key) val = ((Dictionary<string, object>)mem.CurrentValue)[key];
                        else val = mem.CurrentValue;

                        string strVal = FormatPrimitive(val);
                        if (row.LiveValue != strVal) { row.LiveValue = strVal; _dgvWatch.InvalidateRow(i); }
                    }
                }
            }
            _pbLive.Invalidate();
        }

        private void PbLive_Paint(object sender, PaintEventArgs e)
        {
            if (_engine == null || _context == null) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using Font valFont = new Font("Consolas", 8, FontStyle.Bold);

            // Draw status badge
            if (_engine.IsRunning && !_engine.IsPaused) { g.FillRectangle(Brushes.LimeGreen, 10, 10, 70, 18); g.DrawString("RUNNING", valFont, Brushes.White, 14, 12); }
            else if (_engine.IsPaused) { g.FillRectangle(Brushes.DarkOrange, 10, 10, 70, 18); g.DrawString("PAUSED", valFont, Brushes.White, 16, 12); }
            else { g.FillRectangle(Brushes.Crimson, 10, 10, 70, 18); g.DrawString("STOPPED", valFont, Brushes.White, 13, 12); }

            lock (_engine.MemoryLock)
            {
                foreach (var el in _block.Elements)
                {
                    if (!_context.Memory.TryGetValue(el.Name, out var mem)) continue;
                    if (el.DisplayBounds.Width == 0 && el.DisplayBounds.Height == 0) continue;

                    string txt;
                    if (mem.CurrentValue is Array) txt = "[ARRAY]";
                    else if (mem.CurrentValue is Dictionary<string, object>) txt = "{STRUCT}";
                    else if (mem.CurrentValue is bool b) txt = b ? "TRUE" : "FALSE";
                    else txt = mem.CurrentValue.ToString();

                    SizeF textSize = g.MeasureString(txt, valFont);
                    int minBoxWidth = (int)g.MeasureString("FALSE", valFont).Width + 10;
                    int tagWidth = Math.Max((int)textSize.Width + 8, minBoxWidth);

                    int yCenter = el.DisplayBounds.Y + (el.DisplayBounds.Height / 2);
                    int yPos = yCenter - 8; // Tag height is 16, so -8 centers it

                    RectangleF tagRect;

                    if (el.Direction == ElementDirection.Input || el.Direction == ElementDirection.InOut)
                    {
                        int rightEdge = el.DisplayBounds.Right - 45;

                        // 1. THE ABSOLUTE ERASER: Scrub a 55x30px white area clean
                        g.FillRectangle(Brushes.White, rightEdge - 50, yCenter - 15, 55, 30);

                        // 2. REDRAW THE WIRE: Put the connection line back
                        g.DrawLine(Pens.Black, rightEdge - 50, yCenter, rightEdge + 5, yCenter);

                        // 3. Define Tag Position
                        tagRect = new RectangleF(rightEdge - tagWidth, yPos, tagWidth, 16);
                    }
                    else // Outputs
                    {
                        int leftEdge = el.DisplayBounds.Left + 45;

                        // 1. THE ABSOLUTE ERASER: Scrub a 55x30px white area clean
                        g.FillRectangle(Brushes.White, leftEdge - 5, yCenter - 15, 55, 30);

                        // 2. REDRAW THE WIRE: Put the connection line back
                        g.DrawLine(Pens.Black, leftEdge - 5, yCenter, leftEdge + 50, yCenter);

                        // 3. Define Tag Position
                        tagRect = new RectangleF(leftEdge, yPos, tagWidth, 16);
                    }

                    // Draw Tag Background
                    Brush bgBrush = Brushes.WhiteSmoke;
                    if (mem.CurrentValue is bool b2) bgBrush = b2 ? Brushes.LightGreen : Brushes.LightGray;
                    else bgBrush = Brushes.LightCyan;

                    g.FillRectangle(bgBrush, tagRect);
                    g.DrawRectangle(Pens.Gray, tagRect.X, tagRect.Y, tagRect.Width, tagRect.Height);

                    // Draw Centered Text
                    float textX = tagRect.X + (tagRect.Width - textSize.Width) / 2;
                    g.DrawString(txt, valFont, Brushes.Black, textX, tagRect.Y + 2);
                }
            }
        }



        private void RestartSimulation()
        {
            bool wasRunning = _engine.IsRunning && !_engine.IsPaused;
            _engine.Stop(); _context = new ExecutionContext(_block, _getCode()); _scanCount = 0; _minCycleUs = long.MaxValue; _maxCycleUs = 0;
            BindGrid();
            try { string script = SclTranspiler.Transpile(_getCode(), _context); _engine.Compile(script, _context); if (wasRunning) _engine.Start(); }
            catch (Exception ex) { HandleEngineError(ex); }
        }

        private void ReloadCode()
        {
            bool wasRunning = _engine != null && _engine.IsRunning && !_engine.IsPaused; _engine?.Stop();
            try { string script = SclTranspiler.Transpile(_getCode(), _context); _engine.Compile(script, _context); if (wasRunning) _engine.Start(); }
            catch (Exception ex) { HandleEngineError(ex); }
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
