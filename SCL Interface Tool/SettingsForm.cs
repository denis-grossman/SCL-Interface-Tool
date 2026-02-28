using SCL_Interface_Tool.Models;
using System;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCL_Interface_Tool
{
    public partial class SettingsForm : Form
    {
        private AppSettings _settings;
        private ListBox _lstPrompts;
        private TextBox _txtName;
        private RichTextBox _rtbText;

        // Ollama Controls
        private TextBox _txtUrl;
        private ComboBox _cmbModel;

        public SettingsForm(AppSettings settings)
        {
            _settings = settings;
            InitializeUI();
            LoadPrompts();
        }

        private void InitializeUI()
        {
            this.Text = "Settings - LLM & Offline AI";
            this.Size = new Size(600, 580); // Increased height to fit Ollama
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            _lstPrompts = new ListBox { Left = 10, Top = 10, Width = 180, Height = 340 };
            _lstPrompts.SelectedIndexChanged += LstPrompts_SelectedIndexChanged;

            Label lblName = new Label { Text = "Prompt Name:", Left = 200, Top = 10, AutoSize = true };
            _txtName = new TextBox { Left = 200, Top = 30, Width = 370 };
            _txtName.TextChanged += UpdateCurrentPrompt;

            Label lblText = new Label { Text = "Prompt Text (Appended before SCL code):", Left = 200, Top = 60, AutoSize = true };
            _rtbText = new RichTextBox { Left = 200, Top = 80, Width = 370, Height = 270 };
            _rtbText.TextChanged += UpdateCurrentPrompt;

            // --- NEW: OLLAMA GROUP BOX ---
            GroupBox grpOllama = new GroupBox { Text = "Offline Local AI (Ollama) Settings", Left = 10, Top = 360, Width = 560, Height = 100 };

            Label lblUrl = new Label { Text = "API URL:", Left = 15, Top = 30, AutoSize = true };
            _txtUrl = new TextBox { Left = 85, Top = 27, Width = 455, Text = _settings.OllamaApiUrl };

            Label lblModel = new Label { Text = "Model:", Left = 15, Top = 63, AutoSize = true };
            _cmbModel = new ComboBox { Left = 85, Top = 60, Width = 340, Text = _settings.OllamaModelName };

            Button btnFetch = new Button { Text = "🔄 Fetch Models", Left = 435, Top = 59, Width = 105 };
            btnFetch.Click += async (s, e) => await FetchOllamaModels();

            grpOllama.Controls.AddRange(new Control[] { lblUrl, _txtUrl, lblModel, _cmbModel, btnFetch });

            // Bottom Buttons (Moved Down)
            Button btnAdd = new Button { Text = "➕ Add New", Left = 10, Top = 480, Width = 85 };
            btnAdd.Click += BtnAdd_Click;

            Button btnDel = new Button { Text = "❌ Delete", Left = 105, Top = 480, Width = 85 };
            btnDel.Click += BtnDel_Click;

            Button btnReset = new Button { Text = "Reset to Defaults", Left = 200, Top = 480, Width = 150 };
            btnReset.Click += BtnReset_Click;

            Button btnSave = new Button { Text = "Save & Close", Left = 420, Top = 480, Width = 150, BackColor = Color.LightGreen };
            btnSave.Click += BtnSave_Click;

            this.Controls.AddRange(new Control[] { _lstPrompts, lblName, _txtName, lblText, _rtbText, grpOllama, btnAdd, btnDel, btnReset, btnSave });
        }

        private async Task FetchOllamaModels()
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                {
                    string url = _txtUrl.Text.Trim().TrimEnd('/') + "/api/tags";
                    string json = await client.GetStringAsync(url);

                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        _cmbModel.Items.Clear();
                        foreach (JsonElement el in doc.RootElement.GetProperty("models").EnumerateArray())
                        {
                            _cmbModel.Items.Add(el.GetProperty("name").GetString());
                        }
                        if (_cmbModel.Items.Count > 0) _cmbModel.SelectedIndex = 0;
                        MessageBox.Show($"Successfully found {_cmbModel.Items.Count} local models!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not connect to Ollama at {_txtUrl.Text}.\nEnsure Ollama is running.\n\nError: {ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            _settings.OllamaApiUrl = _txtUrl.Text;
            _settings.OllamaModelName = _cmbModel.Text;
            _settings.Save();
            this.DialogResult = DialogResult.OK;
            this.Close();
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

            _txtName.TextChanged -= UpdateCurrentPrompt;
            _rtbText.TextChanged -= UpdateCurrentPrompt;

            _txtName.Text = prompt.Name;
            _rtbText.Text = prompt.Text;

            _txtName.TextChanged += UpdateCurrentPrompt;
            _rtbText.TextChanged += UpdateCurrentPrompt;
        }

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
