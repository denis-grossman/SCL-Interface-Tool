using SCL_Interface_Tool.ImageGeneration;
using SCL_Interface_Tool.Interfaces;
using SCL_Interface_Tool.Models;
using SCL_Interface_Tool.Parsers;
using FastColoredTextBoxNS;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SCL_Interface_Tool
{
    public partial class MainForm : Form
    {
        private FastColoredTextBox _rtbInput;
        private DataGridView _dgvElements;
        private ComboBox _cmbBlocks;
        private ListBox _lstErrors;
        private Button _btnGenerateImage;

        // Upgraded Layout Containers
        private SplitContainer _mainSplit;
        private SplitContainer _rightSplit;

        // Settings & Toolbars
        private AppSettings _settings;
        private ToolStripDropDownButton _btnLlmCopy;

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

        public MainForm()
        {
            InitializeComponent();

            _settings = AppSettings.Load(); // Load settings immediately

            InitializeCustomUI();

            _parser = new RegexSclParser();
            _imageGenerator = new GdiFbdImageGenerator();
            _parsedBlocks = new List<SclBlock>();

            // Form lifecycle hooks for loading/saving
            this.Load += MainForm_Load;
            this.Shown += MainForm_Shown; // To properly restore splitters after layout is calculated
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeCustomUI()
        {
            this.Text = "SCL Interface Extractor & FBD Generator";
            this.Size = new Size(1250, 750);

            // =========================================================
            // GLOBAL TOOLSTRIP
            // =========================================================
            ToolStrip toolStrip = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(5) };

            var btnParse = new ToolStripButton("▶️ Parse SCL", null, BtnParse_Click) { DisplayStyle = ToolStripItemDisplayStyle.Text, BackColor = Color.FromArgb(144, 238, 144), Margin = new Padding(2) }; // Light Green
            var sep0 = new ToolStripSeparator();

            var btnImport = new ToolStripButton("📂 Import", null, BtnImport_Click) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var btnExport = new ToolStripButton("💾 Export", null, BtnExport_Click) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var sep1 = new ToolStripSeparator();

            var btnClear = new ToolStripButton("❌ Clear", null, (s, e) => { _rtbInput.SelectAll(); _rtbInput.ClearSelected(); }) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Clear All (Undoable)" };
            var btnCopy = new ToolStripButton("📋 Copy", null, (s, e) => { if (!string.IsNullOrEmpty(_rtbInput.Text)) Clipboard.SetText(_rtbInput.Text); }) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var sep2 = new ToolStripSeparator();

            var btnUndo = new ToolStripButton("↩️ Undo", null, (s, e) => _rtbInput.Undo()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var btnRedo = new ToolStripButton("↪️ Redo", null, (s, e) => _rtbInput.Redo()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var sep3 = new ToolStripSeparator();

            var btnColl = new ToolStripButton("➖ Collapse All", null, (s, e) =>
            {
                // Bottom-Up logic guarantees deeply nested loops are collapsed successfully!
                for (int i = _rtbInput.LinesCount - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrEmpty(_rtbInput[i].FoldingStartMarker)) _rtbInput.CollapseFoldingBlock(i);
                }
            })
            { DisplayStyle = ToolStripItemDisplayStyle.Text };

            var btnExp = new ToolStripButton("➕ Expand All", null, (s, e) => _rtbInput.ExpandAllFoldingBlocks()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            var sep4 = new ToolStripSeparator();

            _btnLlmCopy = new ToolStripDropDownButton("🤖 Copy for LLM") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            BuildLlmDropdown();

            var btnSettings = new ToolStripButton("⚙️ Settings", null, (s, e) => {
                if (new SettingsForm(_settings).ShowDialog() == DialogResult.OK) BuildLlmDropdown();
            })
            { DisplayStyle = ToolStripItemDisplayStyle.Text };

            toolStrip.Items.AddRange(new ToolStripItem[] { btnParse, sep0, btnImport, btnExport, sep1, btnClear, btnCopy, sep2, btnUndo, btnRedo, sep3, btnColl, btnExp, sep4, _btnLlmCopy, btnSettings });

            // =========================================================
            // GLOBAL STATUS BAR
            // =========================================================
            StatusStrip statusStrip = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            _lblLengthLines = new ToolStripStatusLabel { Text = "length: 0  lines: 1" };
            ToolStripStatusLabel springLabel = new ToolStripStatusLabel { Spring = true };
            _lblPosition = new ToolStripStatusLabel { Text = "Ln: 1  Col: 1" };
            statusStrip.Items.AddRange(new ToolStripItem[] { _lblLengthLines, springLabel, _lblPosition });

            // =========================================================
            // LAYOUT & PANELS (FIXED Z-ORDER DOCKING)
            // =========================================================
            _mainSplit = new SplitContainer { Dock = DockStyle.Fill, Panel1MinSize = 60, Panel2MinSize = 60,  SplitterWidth = 6, BorderStyle = BorderStyle.FixedSingle};

            _rtbInput = new FastColoredTextBox
            {
                Dock = DockStyle.Fill,
                Language = Language.Custom,
                Font = new Font("Consolas", 10),
                ShowLineNumbers = true,
                TabLength = 4,
                BorderStyle = BorderStyle.None
            };
            _rtbInput.TextChangedDelayed += RtbInput_TextChangedDelayed;
            _rtbInput.SelectionChangedDelayed += (s, e) => UpdateStatusBar();
            _mainSplit.Panel1.Controls.Add(_rtbInput);

            _rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Panel1MinSize = 60, Panel2MinSize = 60 };

            Panel pnlControls = new Panel { Dock = DockStyle.Top, Height = 40 };
            Label lblBlock = new Label { Text = "Select Block:", Left = 10, Top = 12, AutoSize = true };
            _cmbBlocks = new ComboBox { Left = 90, Top = 9, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbBlocks.SelectedIndexChanged += CmbBlocks_SelectedIndexChanged;
            _btnGenerateImage = new Button { Text = "🖼️ Generate FBD Image", Left = 300, Top = 8, Width = 155, Enabled = false };
            _btnGenerateImage.Click += BtnGenerateImage_Click;

            pnlControls.Controls.AddRange(new Control[] { lblBlock, _cmbBlocks, _btnGenerateImage });

            _dgvElements = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                AllowUserToAddRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            _dgvElements.CellToolTipTextNeeded += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    var el = (InterfaceElement)_dgvElements.Rows[e.RowIndex].DataBoundItem;
                    if (!string.IsNullOrEmpty(el.Comment)) e.ToolTipText = el.Comment;
                }
            };
            SetupGridContextMenu();

            // Right Split TOP Panel (Grid and Controls)
            _rightSplit.Panel1.Controls.Add(pnlControls);
            _rightSplit.Panel1.Controls.Add(_dgvElements);
            _dgvElements.BringToFront(); // Guarantees Grid takes the rest of the space without covering the top panel

            // Right Split BOTTOM Panel (Error Log)
            Label lblErrors = new Label { Text = "Parser Log / Errors:", Dock = DockStyle.Top, Padding = new Padding(5) };
            _lstErrors = new ListBox { Dock = DockStyle.Fill };
            _rightSplit.Panel2.Controls.Add(lblErrors);
            _rightSplit.Panel2.Controls.Add(_lstErrors);
            _lstErrors.BringToFront(); // Guarantees List takes the rest of the space

            _mainSplit.Panel2.Controls.Add(_rightSplit);

            // Assemble Main Form
            this.Controls.Add(toolStrip);
            this.Controls.Add(statusStrip);
            this.Controls.Add(_mainSplit);

            // MAGIC FIX FOR OVERLAPPING BARS
            _mainSplit.BringToFront();
        }

        // ==========================================
        // SETTINGS SYNC (LOAD / SAVE)
        // ==========================================
        private void MainForm_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_settings.LastCode))
            {
                _rtbInput.Text = _settings.LastCode;
                _rtbInput.ClearUndo();
            }
            UpdateStatusBar();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            // Splitters MUST be positioned inside Form_Shown, after layouts render, to prevent errors
            try
            {
                if (_settings.MainSplitterDistance > _mainSplit.Panel1MinSize && _settings.MainSplitterDistance < _mainSplit.Width - _mainSplit.Panel2MinSize)
                    _mainSplit.SplitterDistance = _settings.MainSplitterDistance;

                if (_settings.RightSplitterDistance > _rightSplit.Panel1MinSize && _settings.RightSplitterDistance < _rightSplit.Height - _rightSplit.Panel2MinSize)
                    _rightSplit.SplitterDistance = _settings.RightSplitterDistance;
                else
                    _rightSplit.SplitterDistance = _rightSplit.Height - 75; // Approx 2-3 lines of error log visible
            }
            catch { }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _settings.LastCode = _rtbInput.Text;
            _settings.MainSplitterDistance = _mainSplit.SplitterDistance;
            _settings.RightSplitterDistance = _rightSplit.SplitterDistance;

            _settings.HiddenColumns.Clear();
            foreach (DataGridViewColumn col in _dgvElements.Columns)
            {
                if (!col.Visible && col.Name != "DisplayBounds")
                    _settings.HiddenColumns.Add(col.Name);
            }

            _settings.Save();
        }

        // ==========================================
        // LLM PROMPT LOGIC
        // ==========================================
        private void BuildLlmDropdown()
        {
            _btnLlmCopy.DropDownItems.Clear();
            foreach (var prompt in _settings.Prompts)
            {
                var item = new ToolStripMenuItem(prompt.Name);
                if (prompt.Name == _settings.ActivePromptName) item.Checked = true;

                item.Click += (s, e) =>
                {
                    _settings.ActivePromptName = prompt.Name;
                    BuildLlmDropdown(); // Update checks
                    if (!string.IsNullOrWhiteSpace(_rtbInput.Text))
                    {
                        string fullText = $"{prompt.Text}\n\n{_rtbInput.Text}";
                        Clipboard.SetText(fullText);
                        MessageBox.Show($"Copied to clipboard using '{prompt.Name}' prompt!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                };
                _btnLlmCopy.DropDownItems.Add(item);
            }
        }

        // ==========================================
        // EDITOR LOGIC
        // ==========================================
        private void UpdateStatusBar()
        {
            if (_rtbInput == null) return;
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

        private void BtnImport_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "SCL Files (*.scl)|*.scl|Text Files (*.txt)|*.txt|All Files (*.*)|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _rtbInput.Text = File.ReadAllText(ofd.FileName);
                    _rtbInput.ClearUndo();
                    UpdateStatusBar();
                }
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog { Filter = "SCL File (*.scl)|*.scl|Text File (*.txt)|*.txt", DefaultExt = "scl" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(sfd.FileName, _rtbInput.Text);
                    MessageBox.Show("File saved successfully!", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        // ==========================================
        // GRID & PARSING LOGIC
        // ==========================================
        private void SetupGridContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem copyWithHeaders = new ToolStripMenuItem("Copy Selection (with headers)");
            copyWithHeaders.Click += (s, e) => {
                _dgvElements.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
                var content = _dgvElements.GetClipboardContent();
                if (content != null) Clipboard.SetDataObject(content);
            };

            ToolStripMenuItem copyWithoutHeaders = new ToolStripMenuItem("Copy Selection (without headers)");
            copyWithoutHeaders.Click += (s, e) => {
                _dgvElements.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
                var content = _dgvElements.GetClipboardContent();
                if (content != null) Clipboard.SetDataObject(content);
            };

            menu.Items.Add(copyWithHeaders);
            menu.Items.Add(copyWithoutHeaders);
            menu.Items.Add(new ToolStripSeparator());

            menu.Opening += (s, e) => {
                while (menu.Items.Count > 3) menu.Items.RemoveAt(3);
                foreach (DataGridViewColumn col in _dgvElements.Columns)
                {
                    if (col.Name == "DisplayBounds") continue;
                    ToolStripMenuItem colItem = new ToolStripMenuItem(col.HeaderText) { CheckOnClick = true, Checked = col.Visible };
                    colItem.CheckedChanged += (sender, args) => col.Visible = colItem.Checked;
                    menu.Items.Add(colItem);
                }
            };
            _dgvElements.ContextMenuStrip = menu;
        }

        private async void BtnParse_Click(object sender, EventArgs e)
        {
            _lstErrors.Items.Clear();
            _cmbBlocks.Items.Clear();
            _dgvElements.DataSource = null;
            _btnGenerateImage.Enabled = false;

            string sclText = _rtbInput.Text;
            if (string.IsNullOrWhiteSpace(sclText)) return;

            // Show a loading state here if desired (e.g., change cursor)
            Cursor = Cursors.WaitCursor;

            try
            {
                // Offload heavy Regex parsing to a background thread!
                var (parsedBlocks, errors) = await Task.Run(() =>
                {
                    var blocks = _parser.Parse(sclText, out List<string> errs);
                    return (blocks, errs);
                });

                _parsedBlocks = parsedBlocks;
                foreach (var err in errors) _lstErrors.Items.Add(err);

                if (_parsedBlocks.Count == 0) return;

                _lstErrors.Items.Add($"Successfully parsed {_parsedBlocks.Count} block(s).");

                // Adjust ComboBox width to fit longest block name
                int maxCmbWidth = _cmbBlocks.Width;
                using (Graphics g = _cmbBlocks.CreateGraphics())
                {
                    foreach (var block in _parsedBlocks)
                    {
                        string txt = $"{block.BlockType}: {block.Name}";
                        _cmbBlocks.Items.Add(txt);
                        int txtW = (int)g.MeasureString(txt, _cmbBlocks.Font).Width;
                        if (txtW > maxCmbWidth) maxCmbWidth = txtW;
                    }
                }
                _cmbBlocks.DropDownWidth = maxCmbWidth + 20;

                _cmbBlocks.SelectedIndex = 0;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
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

                // Restore hidden columns from settings
                foreach (var colName in _settings.HiddenColumns)
                {
                    if (_dgvElements.Columns.Contains(colName))
                        _dgvElements.Columns[colName].Visible = false;
                }
            }

            bool isLogicBlock = selectedBlock.BlockType == "FUNCTION_BLOCK" || selectedBlock.BlockType == "FUNCTION";
            _btnGenerateImage.Enabled = isLogicBlock;
            _btnGenerateImage.Text = isLogicBlock ? "🖼️ Generate FBD Image" : "Image N/A for Data Blocks";
        }

        private void BtnGenerateImage_Click(object sender, EventArgs e)
        {
            if (_cmbBlocks.SelectedIndex < 0) return;
            var selectedBlock = _parsedBlocks[_cmbBlocks.SelectedIndex];

            // Added using statement
            using (ImagePreviewForm previewForm = new ImagePreviewForm(selectedBlock, _imageGenerator))
            {
                previewForm.ShowDialog(this);
            }
        }

    }

    public class ImagePreviewForm : Form
    {
        private SclBlock _block;
        private IImageGenerator _generator;
        private Bitmap _currentImage;

        private PictureBox _pb;
        private CheckBox _chkComments;
        private TrackBar _tbZoom;
        private Panel _pnlScroll;

        public ImagePreviewForm(SclBlock block, IImageGenerator generator)
        {
            _block = block;
            _generator = generator;
            InitializeComponent();
            RegenerateImage();
        }

        private void InitializeComponent()
        {
            this.Text = $"FBD View: {_block.Name}";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterParent;

            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = Color.WhiteSmoke };

            _chkComments = new CheckBox { Text = "💬 Show Comments", Left = 10, Top = 12, AutoSize = true };
            _chkComments.CheckedChanged += (s, e) => RegenerateImage();

            Label lblZoom = new Label { Text = "🔍 Zoom:", Left = 150, Top = 14, AutoSize = true };
            _tbZoom = new TrackBar { Left = 210, Top = 5, Width = 150, Minimum = 25, Maximum = 300, Value = 100, TickFrequency = 25 };
            _tbZoom.Scroll += (s, e) => ApplyZoom();

            Button btnResetZoom = new Button { Text = "🔄 100%", Left = 370, Top = 10, Width = 75 };
            btnResetZoom.Click += (s, e) => { _tbZoom.Value = 100; ApplyZoom(); };

            Button btnCopy = new Button { Text = "📋 Copy Image", Left = 455, Top = 10, Width = 110 };
            btnCopy.Click += BtnCopy_Click;

            Button btnSave = new Button { Text = "💾 Save PNG", Left = 575, Top = 10, Width = 100 };
            btnSave.Click += BtnSave_Click;

            pnlTop.Controls.AddRange(new Control[] { _chkComments, lblZoom, _tbZoom, btnResetZoom, btnCopy, btnSave });

            _pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(245, 246, 248) };

            _pb = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, Location = new Point(0, 0) };
            _pnlScroll.Controls.Add(_pb);

            // FIX 1: Strict WinForms Z-Order to prevent Top Panel overlap
            this.Controls.Add(_pnlScroll);
            this.Controls.Add(pnlTop);
            _pnlScroll.BringToFront(); // Forces Fill to respect Top's boundaries

            // FIX 2: Hook window resize to keep image perfectly centered
            _pnlScroll.Resize += (s, e) => ApplyZoom();

            SetupCustomTooltip();
        }

        private void RegenerateImage()
        {
            if (_currentImage != null) _currentImage.Dispose();

            _currentImage = _generator.GenerateImage(_block, _chkComments.Checked);
            _pb.Image = _currentImage;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (_currentImage == null) return;

            float factor = _tbZoom.Value / 100f;
            int newWidth = (int)(_currentImage.Width * factor);
            int newHeight = (int)(_currentImage.Height * factor);

            _pb.Size = new Size(newWidth, newHeight);

            // FIX 3: Centering calculation accounts for scrollbars using ClientSize
            int x = Math.Max(0, (_pnlScroll.ClientSize.Width - newWidth) / 2);
            int y = Math.Max(0, (_pnlScroll.ClientSize.Height - newHeight) / 2);
            _pb.Location = new Point(x, y);
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            if (_currentImage != null)
            {
                Clipboard.SetImage(_currentImage);
                MessageBox.Show("Image copied to clipboard!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (_currentImage == null) return;

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG Image|*.png";
                sfd.FileName = $"{_block.Name}_FBD.png";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    _currentImage.Save(sfd.FileName, ImageFormat.Png);
                    MessageBox.Show("Image saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void SetupCustomTooltip()
        {
            ToolTip imgToolTip = new ToolTip { OwnerDraw = true, AutoPopDelay = 10000, InitialDelay = 200, ReshowDelay = 100 };
            Font tipFont = new Font("Segoe UI", 9f);

            imgToolTip.Draw += (s, e) =>
            {
                e.Graphics.FillRectangle(SystemBrushes.Info, e.Bounds);
                e.Graphics.DrawRectangle(Pens.Black, new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1));
                e.Graphics.DrawString(e.ToolTipText, tipFont, Brushes.Black, new PointF(2, 2));
            };

            imgToolTip.Popup += (s, e) =>
            {
                e.ToolTipSize = TextRenderer.MeasureText(imgToolTip.GetToolTip(e.AssociatedControl), tipFont);
                e.ToolTipSize = new Size(e.ToolTipSize.Width + 6, e.ToolTipSize.Height + 4);
            };

            InterfaceElement currentHoverElement = null;

            _pb.MouseMove += (s, args) =>
            {
                if (_currentImage == null) return;

                float factor = _tbZoom.Value / 100f;
                int imgX = (int)(args.X / factor);
                int imgY = (int)(args.Y / factor);
                Point p = new Point(imgX, imgY);

                var hoverElement = _block.Elements.FirstOrDefault(el => el.DisplayBounds.Contains(p));

                if (hoverElement != currentHoverElement)
                {
                    currentHoverElement = hoverElement;
                    if (hoverElement != null)
                    {
                        string tip = $"{hoverElement.Direction.ToString().ToUpper()}: {hoverElement.DataType}";
                        if (!string.IsNullOrWhiteSpace(hoverElement.Comment)) tip += $" / {hoverElement.Comment}";
                        imgToolTip.SetToolTip(_pb, tip);
                    }
                    else imgToolTip.SetToolTip(_pb, "");
                }
            };
        }
    }
}
