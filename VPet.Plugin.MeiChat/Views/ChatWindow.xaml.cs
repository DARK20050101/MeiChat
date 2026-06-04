using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet.Plugin.MeiChat.Models;

namespace VPet.Plugin.MeiChat.Views
{
    public partial class ChatWindow : Window
    {
        private readonly Main _plugin;
        private readonly List<DeepSeekClient.ChatMessage> _messages = new();
        private bool _isProcessing;

        public ChatWindow(Main plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            Loaded += (s, e) => InputBox.Focus();
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
            => await SendAsync();

        private async void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
            {
                e.Handled = true;
                await SendAsync();
            }
        }

        private async System.Threading.Tasks.Task SendAsync()
        {
            if (_isProcessing) return;

            var text = InputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            InputBox.Clear();
            AddMessage(text, isUser: true);
            _messages.Add(new DeepSeekClient.ChatMessage { IsUser = true, Content = text });

            _isProcessing = true;
            BtnSend.IsEnabled = false;

            try
            {
                var response = await System.Threading.Tasks.Task.Run(() =>
                    _plugin.ApiClient!.SendMessageAsync(
                        _messages, _plugin.Config.SystemPrompt));

                _messages.Add(new DeepSeekClient.ChatMessage { IsUser = false, Content = response });
                AddMessage(response, isUser: false);
            }
            catch (Exception ex)
            {
                AddMessage($"⚠️ {ex.Message}", isUser: false);
            }
            finally
            {
                _isProcessing = false;
                BtnSend.IsEnabled = true;
                InputBox.Focus();
            }
        }

        private void AddMessage(string content, bool isUser)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            var label = new TextBlock
            {
                Text = isUser ? "🧑 我" : "🌸 芽衣",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(4, 0, 0, 2)
            };
            panel.Children.Add(label);

            var bubble = new Border
            {
                Background = isUser
                    ? new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9))
                    : new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                CornerRadius = new CornerRadius(8, 8, 8, 8),
                Padding = new Thickness(12, 8, 12, 8),
                MaxWidth = 300,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };
            bubble.Child = new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = isUser ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
            };

            panel.Children.Add(bubble);
            MessageList.Children.Add(panel);

            // 滚动到底部
            MessageArea.ScrollToVerticalOffset(double.MaxValue);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_plugin.MW.Windows.Contains(this))
                _plugin.MW.Windows.Remove(this);
            base.OnClosing(e);
        }
    }
}
