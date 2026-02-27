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
        private Button _btnParse;
        private Button _btnGenerateImage;

        // Status bar labels
        private ToolStripStatusLabel _lblLengthLines;
        private ToolStripStatusLabel _lblPosition;

        private ISclParser _parser;
        private IImageGenerator _imageGenerator;
        private List<SclBlock> _parsedBlocks;

        // FCTB Syntax Highlighting Styles
        private TextStyle _commentStyle = new TextStyle(Brushes.Green, null, FontStyle.Italic);
        private TextStyle _keyword1Style = new TextStyle(Brushes.Blue, null, FontStyle.Bold);
        private TextStyle _keyword2Style = new TextStyle(Brushes.DarkMagenta, null, FontStyle.Bold);
        private TextStyle _keyword3Style = new TextStyle(Brushes.DeepPink, null, FontStyle.Regular);
        private TextStyle _numberStyle = new TextStyle(Brushes.DarkOrange, null, FontStyle.Regular);
        private TextStyle _stringStyle = new TextStyle(Brushes.Brown, null, FontStyle.Regular);

        // Syntax Regex Patterns
        private string _kw1 = @"\b(AND|ANY|ARRAY|AT|BEGIN|BLOCK_DB|BLOCK_FB|BLOCK_FC|BLOCK_SDB|BOOL|BY|BYTE|CASE|CHAR|CONST|CONTINUE|COUNTER|DATA_BLOCK|DATE|DATE_AND_TIME|DINT|DIV|DO|DT|DWORD|ELSE|ELSIF|EN|END_CASE|END_CONST|END_DATA_BLOCK|END_FOR|END_FUNCTION|END_FUNCTION_BLOCK|END_IF|END_LABEL|END_ORGANIZATION_BLOCK|END_REPEAT|END_STRUCT|END_TYPE|END_VAR|END_WHILE|ENO|EXIT|FALSE|FOR|FUNCTION|FUNCTION_BLOCK|GOTO|IF|INT|LABEL|MOD|NIL|NOT|OF|OK|OR|ORGANIZATION_BLOCK|POINTER|PROGRAM|END_PROGRAM|REAL|REGION|END_REGION|REPEAT|RET_VAL|RETURN|S5TIME|STRING|STRUCT|THEN|TIME|TIME_OF_DAY|TIMER|TO|TOD|TRUE|TYPE|UNTIL|VAR|VAR_IN_OUT|VAR_INPUT|VAR_OUTPUT|VAR_TEMP|VOID|WHILE|WORD|XOR)\b";
        private string _kw2 = @"\b(ABS|SQR|SQRT|EXP|EXPD|LN|LOG|ACOS|ASIN|ATAN|COS|SIN|TAN|ROL|ROR|SHL|SHR|LEN|CONCAT|LEFT|RIGHT|MID|INSERT|DELETE|REPLACE|FIND|TON|TOF|TP|TONR)\b";
        private string _kw3 = @"\b(TITLE|VERSION|KNOW_HOW_PROTECT|AUTHOR|NAME|FAMILY)\b";
        // Compiled Regexes for ultra-fast Code Folding
        private static readonly Regex _rxFoldOpen = new Regex(@"\b(?:FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE|ORGANIZATION_BLOCK|VAR_INPUT|VAR_OUTPUT|VAR_IN_OUT|VAR_TEMP|VAR|STRUCT|REGION|IF|CASE|FOR|WHILE|REPEAT)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _rxFoldClose = new Regex(@"\b(?:END_FUNCTION_BLOCK|END_FUNCTION|END_DATA_BLOCK|END_TYPE|END_ORGANIZATION_BLOCK|END_VAR|END_STRUCT|END_REGION|END_IF|END_CASE|END_FOR|END_WHILE|END_REPEAT)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public MainForm()
        {
            InitializeComponent();
            InitializeCustomUI();

            _parser = new RegexSclParser();
            _imageGenerator = new GdiFbdImageGenerator();
            _parsedBlocks = new List<SclBlock>();
        }

        private void InitializeCustomUI()
        {
            this.Text = "SCL Interface Extractor & FBD Generator";
            this.Size = new Size(1250, 750);

            SplitContainer mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 550 };

            // =========================================================
            // --- LEFT PANEL (Editor, Toolbar, Status Bar) ---
            // =========================================================
            Panel leftContainer = new Panel { Dock = DockStyle.Fill };

            Panel pnlEditorToolbar = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = Color.WhiteSmoke };
            Button btnImport = new Button { Text = "Import File", Left = 5, Top = 5, Width = 80 };
            btnImport.Click += BtnImport_Click;
            Button btnExport = new Button { Text = "Export File", Left = 90, Top = 5, Width = 80 };
            btnExport.Click += BtnExport_Click;
            Button btnUndo = new Button { Text = "Undo", Left = 180, Top = 5, Width = 60 };
            Button btnRedo = new Button { Text = "Redo", Left = 245, Top = 5, Width = 60 };
            pnlEditorToolbar.Controls.AddRange(new Control[] { btnImport, btnExport, btnUndo, btnRedo });

            // Status Bar at the bottom
            StatusStrip statusStrip = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            _lblLengthLines = new ToolStripStatusLabel { Text = "length: 0  lines: 1" };
            ToolStripStatusLabel springLabel = new ToolStripStatusLabel { Spring = true }; // Pushes the next label to the far right
            _lblPosition = new ToolStripStatusLabel { Text = "Ln: 1  Col: 1" };
            statusStrip.Items.AddRange(new ToolStripItem[] { _lblLengthLines, springLabel, _lblPosition });

            // FastColoredTextBox
            _rtbInput = new FastColoredTextBox
            {
                Dock = DockStyle.Fill,
                Language = Language.Custom,
                Font = new Font("Consolas", 10),
                ShowLineNumbers = true,
                TabLength = 4,
                BorderStyle = BorderStyle.None
            };

            btnUndo.Click += (s, e) => _rtbInput.Undo();
            btnRedo.Click += (s, e) => _rtbInput.Redo();

            // Events for highlighting and Status bar
            _rtbInput.TextChangedDelayed += RtbInput_TextChangedDelayed;
            _rtbInput.SelectionChangedDelayed += (s, e) => UpdateStatusBar();

            // FIX: Strict Docking Order to prevent clipping
            leftContainer.Controls.Add(_rtbInput);        // Fill added FIRST
            leftContainer.Controls.Add(pnlEditorToolbar); // Top added NEXT
            leftContainer.Controls.Add(statusStrip);      // Bottom added LAST

            mainSplit.Panel1.Controls.Add(leftContainer);

            // =========================================================
            // --- RIGHT PANEL (Controls & Grid) ---
            // =========================================================
            SplitContainer rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 500 };
            Panel pnlControls = new Panel { Dock = DockStyle.Top, Height = 40 };

            _btnParse = new Button { Text = "Parse SCL", Left = 10, Top = 8, Width = 100 };
            _btnParse.Click += BtnParse_Click;
            Label lblBlock = new Label { Text = "Select Block:", Left = 120, Top = 12, AutoSize = true };
            _cmbBlocks = new ComboBox { Left = 200, Top = 9, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbBlocks.SelectedIndexChanged += CmbBlocks_SelectedIndexChanged;
            _btnGenerateImage = new Button { Text = "Generate FBD Image", Left = 410, Top = 8, Width = 150, Enabled = false };
            _btnGenerateImage.Click += BtnGenerateImage_Click;

            pnlControls.Controls.Add(_btnParse);
            pnlControls.Controls.Add(lblBlock);
            pnlControls.Controls.Add(_cmbBlocks);
            pnlControls.Controls.Add(_btnGenerateImage);

            _dgvElements = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
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

            rightSplit.Panel1.Controls.Add(_dgvElements);
            rightSplit.Panel1.Controls.Add(pnlControls);

            Label lblErrors = new Label { Text = "Parser Log / Errors:", Dock = DockStyle.Top, Padding = new Padding(5) };
            _lstErrors = new ListBox { Dock = DockStyle.Fill };
            rightSplit.Panel2.Controls.Add(_lstErrors);
            rightSplit.Panel2.Controls.Add(lblErrors);

            mainSplit.Panel2.Controls.Add(rightSplit);
            this.Controls.Add(mainSplit);

            // Initialize status bar on startup
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            if (_rtbInput == null) return;

            int length = _rtbInput.Text.Length;
            int lines = _rtbInput.LinesCount;
            int selLength = _rtbInput.SelectionLength;

            // FCTB lines/chars are 0-indexed, display needs to be 1-indexed
            int ln = _rtbInput.Selection.Start.iLine + 1;
            int col = _rtbInput.Selection.Start.iChar + 1;

            string lengthText = selLength > 0 ? $"length: {length} ({selLength} selected)" : $"length: {length}";

            _lblLengthLines.Text = $"{lengthText}   lines: {lines}";
            _lblPosition.Text = $"Ln: {ln}   Col: {col}";
        }

        private void RtbInput_TextChangedDelayed(object sender, TextChangedEventArgs e)
        {
            // 1. Clear old styles and folding markers
            e.ChangedRange.ClearStyle(_commentStyle, _keyword1Style, _keyword2Style, _keyword3Style, _numberStyle, _stringStyle);
            e.ChangedRange.ClearFoldingMarkers();

            // 2. Apply Syntax Highlighting (Order matters!)
            e.ChangedRange.SetStyle(_stringStyle, @"'[^']*'|""[^""]*""");
            e.ChangedRange.SetStyle(_commentStyle, @"//.*$|\(\*[\s\S]*?\*\)", RegexOptions.Multiline);
            e.ChangedRange.SetStyle(_numberStyle, @"\b\d+(\.\d+)?\b");
            e.ChangedRange.SetStyle(_keyword1Style, _kw1, RegexOptions.IgnoreCase);
            e.ChangedRange.SetStyle(_keyword2Style, _kw2, RegexOptions.IgnoreCase);
            e.ChangedRange.SetStyle(_keyword3Style, _kw3, RegexOptions.IgnoreCase);

            // 3. Smart Code Folding (Ignores Comments and Strings)
            // Get the internal numeric ID that FCTB assigned to our Comment and String styles
            int cIdx = _rtbInput.GetStyleIndex(_commentStyle);
            int sIdx = _rtbInput.GetStyleIndex(_stringStyle);

            // Create a bitmask to easily identify if a character has these styles
            int ignoreMask = 0;
            if (cIdx >= 0) ignoreMask |= (1 << cIdx);
            if (sIdx >= 0) ignoreMask |= (1 << sIdx);

            for (int i = e.ChangedRange.Start.iLine; i <= e.ChangedRange.End.iLine; i++)
            {
                var line = _rtbInput[i];

                // Build a "clean" version of the line where comments/strings are replaced with spaces
                char[] cleanChars = new char[line.Count];
                for (int c = 0; c < line.Count; c++)
                {
                    int styleMask = (int)line[c].style;
                    if ((styleMask & ignoreMask) != 0)
                        cleanChars[c] = ' '; // Hide comment text from the folder!
                    else
                        cleanChars[c] = line[c].c;
                }
                string cleanText = new string(cleanChars);

                // Count opening and closing keywords on this specific line
                int openCount = _rxFoldOpen.Matches(cleanText).Count;
                int closeCount = _rxFoldClose.Matches(cleanText).Count;

                // Apply fold markers based on net balance
                if (openCount > closeCount)
                    line.FoldingStartMarker = "fold";
                else if (closeCount > openCount)
                    line.FoldingEndMarker = "fold";
            }

            // 4. Update the bottom status bar
            UpdateStatusBar();
        }



        private void BtnImport_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "SCL Files (*.scl)|*.scl|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _rtbInput.Text = File.ReadAllText(ofd.FileName);
                    _rtbInput.ClearUndo(); // Reset undo history on fresh load
                    UpdateStatusBar();
                }
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "SCL File (*.scl)|*.scl|Text File (*.txt)|*.txt";
                sfd.DefaultExt = "scl";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(sfd.FileName, _rtbInput.Text);
                    MessageBox.Show("File saved successfully!", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

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

        private void BtnParse_Click(object sender, EventArgs e)
        {
            _lstErrors.Items.Clear();
            _cmbBlocks.Items.Clear();
            _dgvElements.DataSource = null;
            _btnGenerateImage.Enabled = false;

            string sclText = _rtbInput.Text;
            if (string.IsNullOrWhiteSpace(sclText)) return;

            _parsedBlocks = _parser.Parse(sclText, out List<string> errors);
            foreach (var err in errors) _lstErrors.Items.Add(err);

            if (_parsedBlocks.Count == 0) return;

            _lstErrors.Items.Add($"Successfully parsed {_parsedBlocks.Count} block(s).");
            foreach (var block in _parsedBlocks) _cmbBlocks.Items.Add($"{block.BlockType}: {block.Name}");
            _cmbBlocks.SelectedIndex = 0;
        }

        private void CmbBlocks_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmbBlocks.SelectedIndex < 0) return;

            var selectedBlock = _parsedBlocks[_cmbBlocks.SelectedIndex];
            _dgvElements.DataSource = selectedBlock.Elements.ToList();

            if (_dgvElements.Columns.Count > 0)
            {
                if (_dgvElements.Columns.Contains("Index")) { _dgvElements.Columns["Index"].DisplayIndex = 0; _dgvElements.Columns["Index"].FillWeight = 40; }
                if (_dgvElements.Columns.Contains("Name")) { _dgvElements.Columns["Name"].DisplayIndex = 1; _dgvElements.Columns["Name"].FillWeight = 100; }
                if (_dgvElements.Columns.Contains("DataType")) { _dgvElements.Columns["DataType"].DisplayIndex = 2; _dgvElements.Columns["DataType"].FillWeight = 80; }
                if (_dgvElements.Columns.Contains("Direction")) { _dgvElements.Columns["Direction"].DisplayIndex = 3; _dgvElements.Columns["Direction"].FillWeight = 60; }
                if (_dgvElements.Columns.Contains("InitialValue")) { _dgvElements.Columns["InitialValue"].DisplayIndex = 4; _dgvElements.Columns["InitialValue"].FillWeight = 60; }
                if (_dgvElements.Columns.Contains("Attributes")) { _dgvElements.Columns["Attributes"].DisplayIndex = 5; _dgvElements.Columns["Attributes"].FillWeight = 120; }
                if (_dgvElements.Columns.Contains("Comment")) { _dgvElements.Columns["Comment"].DisplayIndex = 6; _dgvElements.Columns["Comment"].FillWeight = 120; }
                if (_dgvElements.Columns.Contains("DisplayBounds")) _dgvElements.Columns["DisplayBounds"].Visible = false;
            }

            bool isLogicBlock = selectedBlock.BlockType == "FUNCTION_BLOCK" || selectedBlock.BlockType == "FUNCTION";
            _btnGenerateImage.Enabled = isLogicBlock;
            _btnGenerateImage.Text = isLogicBlock ? "Generate FBD Image" : "Image N/A for Data Blocks";
        }

        private void BtnGenerateImage_Click(object sender, EventArgs e)
        {
            if (_cmbBlocks.SelectedIndex < 0) return;
            var selectedBlock = _parsedBlocks[_cmbBlocks.SelectedIndex];

            ImagePreviewForm previewForm = new ImagePreviewForm(selectedBlock, _imageGenerator);
            previewForm.ShowDialog(this);
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
            this.Size = new Size(1000, 600);
            this.StartPosition = FormStartPosition.CenterParent;

            Panel pnlTop = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = Color.WhiteSmoke };

            _chkComments = new CheckBox { Text = "Show Comments", Left = 10, Top = 12, AutoSize = true };
            _chkComments.CheckedChanged += (s, e) => RegenerateImage();

            Label lblZoom = new Label { Text = "Zoom:", Left = 140, Top = 14, AutoSize = true };
            _tbZoom = new TrackBar { Left = 180, Top = 5, Width = 150, Minimum = 50, Maximum = 200, Value = 100, TickFrequency = 25 };
            _tbZoom.Scroll += (s, e) => ApplyZoom();

            Button btnResetZoom = new Button { Text = "100%", Left = 340, Top = 10, Width = 50 };
            btnResetZoom.Click += (s, e) => { _tbZoom.Value = 100; ApplyZoom(); };

            Button btnSave = new Button { Text = "Save as PNG", Left = 420, Top = 10, Width = 100 };
            btnSave.Click += BtnSave_Click;

            pnlTop.Controls.AddRange(new Control[] { _chkComments, lblZoom, _tbZoom, btnResetZoom, btnSave });
            this.Controls.Add(pnlTop);

            _pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(245, 246, 248) };

            _pb = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, Location = new Point(0, 0) };
            _pnlScroll.Controls.Add(_pb);
            this.Controls.Add(_pnlScroll);

            pnlTop.BringToFront();

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
            _pb.Width = (int)(_currentImage.Width * factor);
            _pb.Height = (int)(_currentImage.Height * factor);

            int x = Math.Max(0, (_pnlScroll.Width - _pb.Width) / 2);
            int y = Math.Max(0, (_pnlScroll.Height - _pb.Height) / 2);
            _pb.Location = new Point(x, y);
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
    }
}
