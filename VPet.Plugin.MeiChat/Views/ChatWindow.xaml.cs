using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
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
        private TextBlock? _streamingTextBlock;

        public ChatWindow(Main plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            InputBox.Focus();

            // 定位到 VPet 主窗口附近（底部偏左，不挡住桌宠）
            try
            {
                if (Application.Current?.MainWindow is Window mainWin && mainWin.Visibility == Visibility.Visible)
                {
                    this.Left = mainWin.Left + 20;
                    this.Top = mainWin.Top + mainWin.Height - this.Height - 80;
                }
            }
            catch { /* 定位失败不影响使用 */ }
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
                // 创建空的 AI 气泡，用于流式填充
                AddMessage("", isUser: false);
                var fullText = new StringBuilder();

                await _plugin.ApiClient!.SendMessageStreamAsync(
                    _messages,
                    onContent: chunk =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            fullText.Append(chunk);
                            if (_streamingTextBlock != null)
                                _streamingTextBlock.Text = fullText.ToString();
                            MessageArea.ScrollToBottom();
                        });
                    },
                    onFinish: _ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _streamingTextBlock = null;
                        });
                    },
                    onError: error =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_streamingTextBlock != null)
                            {
                                _streamingTextBlock.Text += $"\n\n⚠️ {error}";
                                _streamingTextBlock = null;
                            }
                        });
                    },
                    systemPrompt: _plugin.Config.SystemPrompt
                );

                _messages.Add(new DeepSeekClient.ChatMessage
                {
                    IsUser = false,
                    Content = fullText.ToString()
                });
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

        /// <summary>
        /// 外部添加 AI 消息（供 TalkBox Agent 模式调用）
        /// </summary>
        public void AddAiMessage(string content)
        {
            Dispatcher.Invoke(() =>
            {
                AddMessage(content, isUser: false);
            });
        }

        /// <summary>
        /// 外部添加用户消息
        /// </summary>
        public void AddUserMessage(string content)
        {
            Dispatcher.Invoke(() =>
            {
                AddMessage(content, isUser: true);
            });
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

            var textBlock = new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = isUser ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
            };

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
            bubble.Child = textBlock;

            panel.Children.Add(bubble);
            MessageList.Children.Add(panel);

            // AI 消息追踪 TextBlock，用于流式更新
            if (!isUser)
                _streamingTextBlock = textBlock;

            MessageArea.ScrollToBottom();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_plugin.MW.Windows.Contains(this))
                _plugin.MW.Windows.Remove(this);
            base.OnClosing(e);
        }
    }
}
