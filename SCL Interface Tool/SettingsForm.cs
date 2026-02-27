using SCL_Interface_Tool.Models;
using System;
using System.Windows.Forms;

namespace SCL_Interface_Tool
{
    public partial class SettingsForm : Form // Added 'partial' keyword
    {
        private AppSettings _settings;
        private ListBox _lstPrompts;
        private TextBox _txtName;
        private RichTextBox _rtbText;

        public SettingsForm(AppSettings settings)
        {
            _settings = settings;
            InitializeUI();
            LoadPrompts();
        }

        private void InitializeUI()
        {
            this.Text = "Settings - LLM Prompts";
            this.Size = new System.Drawing.Size(600, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            _lstPrompts = new ListBox { Left = 10, Top = 10, Width = 180, Height = 340 };
            _lstPrompts.SelectedIndexChanged += LstPrompts_SelectedIndexChanged;

            Label lblName = new Label { Text = "Prompt Name:", Left = 200, Top = 10, AutoSize = true };
            _txtName = new TextBox { Left = 200, Top = 30, Width = 370 };
            _txtName.TextChanged += UpdateCurrentPrompt; // Fixed mapping

            Label lblText = new Label { Text = "Prompt Text (Appended before SCL code):", Left = 200, Top = 60, AutoSize = true };
            _rtbText = new RichTextBox { Left = 200, Top = 80, Width = 370, Height = 270 };
            _rtbText.TextChanged += UpdateCurrentPrompt; // Fixed mapping

            Button btnAdd = new Button { Text = "➕ Add New", Left = 10, Top = 360, Width = 85 };
            btnAdd.Click += BtnAdd_Click;

            Button btnDel = new Button { Text = "❌ Delete", Left = 105, Top = 360, Width = 85 };
            btnDel.Click += BtnDel_Click;

            Button btnReset = new Button { Text = "Reset to Defaults", Left = 200, Top = 360, Width = 150 };
            btnReset.Click += BtnReset_Click;

            Button btnSave = new Button { Text = "Save & Close", Left = 420, Top = 360, Width = 150 };
            btnSave.Click += (s, e) => { _settings.Save(); this.DialogResult = DialogResult.OK; this.Close(); };

            this.Controls.AddRange(new Control[] { _lstPrompts, lblName, _txtName, lblText, _rtbText, btnAdd, btnDel, btnReset, btnSave });
        }

        private void LoadPrompts()
        {
            _lstPrompts.Items.Clear();
            foreach (var p in _settings.Prompts) _lstPrompts.Items.Add(p.Name);
            if (_lstPrompts.Items.Count > 0) _lstPrompts.SelectedIndex = 0;
        }

        private void LstPrompts_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lstPrompts.SelectedIndex < 0) return;
            var prompt = _settings.Prompts[_lstPrompts.SelectedIndex];

            // Temporarily unhook events so updating textboxes doesn't overwrite data
            _txtName.TextChanged -= UpdateCurrentPrompt;
            _rtbText.TextChanged -= UpdateCurrentPrompt;

            _txtName.Text = prompt.Name;
            _rtbText.Text = prompt.Text;

            _txtName.TextChanged += UpdateCurrentPrompt;
            _rtbText.TextChanged += UpdateCurrentPrompt;
        }

        // Fixed delegate signature by adding (object sender, EventArgs e)
        private void UpdateCurrentPrompt(object sender, EventArgs e)
        {
            if (_lstPrompts.SelectedIndex < 0) return;
            var prompt = _settings.Prompts[_lstPrompts.SelectedIndex];
            prompt.Name = _txtName.Text;
            prompt.Text = _rtbText.Text;
            _lstPrompts.Items[_lstPrompts.SelectedIndex] = prompt.Name;
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            _settings.Prompts.Add(new LlmPrompt { Name = "New Prompt", Text = "" });
            LoadPrompts();
            _lstPrompts.SelectedIndex = _lstPrompts.Items.Count - 1;
        }

        private void BtnDel_Click(object sender, EventArgs e)
        {
            if (_lstPrompts.SelectedIndex < 0 || _settings.Prompts.Count <= 1) return;
            _settings.Prompts.RemoveAt(_lstPrompts.SelectedIndex);
            LoadPrompts();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to restore default prompts?", "Confirm Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _settings.RestoreDefaultPrompts();
                LoadPrompts();
            }
        }
    }
}
