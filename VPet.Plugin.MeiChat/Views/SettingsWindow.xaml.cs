using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VPet.Plugin.MeiChat.Models;

namespace VPet.Plugin.MeiChat.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppConfig _config;
        private readonly Action<AppConfig>? _onSaved;

        public SettingsWindow(AppConfig config, Action<AppConfig>? onSaved = null)
        {
            InitializeComponent();
            _config = config;
            _onSaved = onSaved;

            // 限制窗口最大高度不超过屏幕
            this.MaxHeight = SystemParameters.WorkArea.Height - 20;

            // 确保在屏幕范围内
            Loaded += (s, e) =>
            {
                try
                {
                    var wa = SystemParameters.WorkArea;
                    if (this.Left + this.Width > wa.Right) this.Left = wa.Right - this.Width - 10;
                    if (this.Left < wa.Left) this.Left = wa.Left + 10;
                    if (this.Top + this.Height > wa.Bottom) this.Top = wa.Bottom - this.Height - 10;
                    if (this.Top < wa.Top) this.Top = wa.Top + 10;
                }
                catch { }
            };

            LoadConfig();
        }

        private void LoadConfig()
        {
            // API Key
            ApiKeyBox.Password = _config.ApiKey;

            // API 地址
            ApiUrlBox.Text = _config.ApiBaseUrl;

            // 模型
            ModelBox.Text = _config.Model;

            // 在预设列表中查找匹配项
            foreach (ComboBoxItem item in ModelPresetBox.Items)
            {
                if (item.Tag?.ToString() == _config.Model)
                {
                    item.IsSelected = true;
                    break;
                }
            }

            // 温度
            TempSlider.Value = _config.Temperature;
            TempValue.Text = _config.Temperature.ToString("F1");
            TempSlider.ValueChanged += (s, e) =>
                TempValue.Text = e.NewValue.ToString("F1");

            // Max Tokens
            foreach (ComboBoxItem item in MaxTokensBox.Items)
            {
                if (item.Tag?.ToString() == _config.MaxTokens.ToString())
                {
                    item.IsSelected = true;
                    break;
                }
            }

            WorkDirBox.Text = _config.WorkingDirectory;
            SystemPromptBox.Text = _config.SystemPrompt;
        }

        /// <summary>预设选择后自动填充模型名</summary>
        private void ModelPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelBox == null) return; // XAML 加载时 ModelBox 可能还没创建
            if (ModelPresetBox.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                if (!string.IsNullOrEmpty(tag))
                {
                    ModelBox.Text = tag;
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = ApiKeyBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("请输入 API Key！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                ApiKeyBox.Focus();
                return;
            }

            _config.ApiKey = apiKey;
            _config.ApiBaseUrl = ApiUrlBox.Text.Trim();
            _config.Temperature = TempSlider.Value;
            _config.WorkingDirectory = WorkDirBox.Text.Trim();
            _config.SystemPrompt = SystemPromptBox.Text.Trim();

            // 模型：优先使用 ModelBox 中输入的文本
            _config.Model = ModelBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(_config.Model))
            {
                _config.Model = "deepseek-v4-flash";
            }

            // Max Tokens
            if (MaxTokensBox.SelectedItem is ComboBoxItem tokenItem &&
                int.TryParse(tokenItem.Tag?.ToString(), out var maxTokens))
            {
                _config.MaxTokens = maxTokens;
            }

            _onSaved?.Invoke(_config);
            DialogResult = true;
            Close();
        }

        private void BtnBrowseWorkDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择工作目录",
                InitialDirectory = !string.IsNullOrWhiteSpace(WorkDirBox.Text)
                    ? WorkDirBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == true)
                WorkDirBox.Text = dialog.FolderName;
        }

        private void BtnResetPrompt_Click(object sender, RoutedEventArgs e)
        {
            SystemPromptBox.Text = new AppConfig().SystemPrompt;
        }

        private async void BtnTestApi_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = ApiKeyBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                TestStatus.Text = "❌ 请先输入 API Key";
                TestStatus.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            TestStatus.Text = "🔄 测试连接中...";
            TestStatus.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                var model = ModelBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(model)) model = "deepseek-chat";

                var baseUrl = ApiUrlBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://api.deepseek.com/v1";

                using var client = new DeepSeekClient(apiKey, model, baseUrl: baseUrl);
                var isValid = await client.ValidateApiKeyAsync();

                if (isValid)
                {
                    TestStatus.Text = "✅ API 连接成功！";
                    TestStatus.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    TestStatus.Text = "❌ API 连接失败，请检查 API Key 是否有效";
                    TestStatus.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (System.Exception ex)
            {
                TestStatus.Text = $"❌ 连接失败: {ex.Message}";
                TestStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
    }
}
