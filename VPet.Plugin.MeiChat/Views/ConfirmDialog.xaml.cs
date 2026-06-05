using System.Windows;

namespace VPet.Plugin.MeiChat.Views
{
    /// <summary>
    /// 命令确认弹窗 — 支持长命令滚动、可拖动、一键开启自动模式
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        /// <summary>是否允许执行</summary>
        public bool IsAllowed { get; private set; }

        /// <summary>用户是否要求开启自动模式</summary>
        public bool AutoModeRequested { get; private set; }

        private ConfirmDialog(string title, string icon, string command, bool isDestructive)
        {
            InitializeComponent();

            TitleIcon.Text = icon;
            TitleText.Text = title;
            CommandText.Text = command;

            // 危险操作时调整显示
            if (isDestructive)
            {
                BtnAllow.Background = System.Windows.Media.Brushes.OrangeRed;
                BtnAllow.Content = "⚠ 确认执行";
                BtnAllowAuto.Visibility = Visibility.Collapsed; // 危险操作不允许一键自动
            }

            if (Application.Current?.MainWindow != null)
                Owner = Application.Current.MainWindow;
        }

        /// <summary>
        /// 显示命令确认弹窗
        /// </summary>
        public static (bool Allowed, bool AutoMode) Show(string command, bool isDestructive, string? description = null)
        {
            var title = isDestructive ? "⚠️ 危险操作确认" : "🔧 命令执行确认";
            var icon = isDestructive ? "⚠️" : "🔧";
            var display = string.IsNullOrEmpty(description)
                ? command
                : $"{description}\n\n━━━━━━━━━━━━━━━━━━\n\n{command}";

            var dialog = new ConfirmDialog(title, icon, display, isDestructive);
            dialog.ShowDialog();
            return (dialog.IsAllowed, dialog.AutoModeRequested);
        }

        private void BtnAllow_Click(object sender, RoutedEventArgs e)
        {
            IsAllowed = true;
            Close();
        }

        private void BtnAllowAuto_Click(object sender, RoutedEventArgs e)
        {
            IsAllowed = true;
            AutoModeRequested = true;
            Close();
        }

        private void BtnReject_Click(object sender, RoutedEventArgs e)
        {
            IsAllowed = false;
            Close();
        }
    }
}
