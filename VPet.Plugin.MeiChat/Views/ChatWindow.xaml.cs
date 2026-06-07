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
        private TextBox? _streamingTextBox;

        // 懒加载
        private const int BatchSize = 20;
        private int _loadCount;          // 已加载的消息数
        private bool _allLoaded;         // 是否已全部加载
        private bool _isLoadingHistory;  // 正在加载中，防止递归
        private bool _initialLoadDone;   // 首次加载完成

        public ChatWindow(Main plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            InputBox.Focus();

            // 定位到 VPet 主窗口附近，并确保在屏幕范围内
            try
            {
                if (Application.Current?.MainWindow is Window mainWin && mainWin.Visibility == Visibility.Visible)
                {
                    double left = mainWin.Left + 20;
                    double top = mainWin.Top + mainWin.Height - this.Height - 80;

                    var wa = SystemParameters.WorkArea;
                    if (left + this.Width > wa.Right) left = wa.Right - this.Width - 10;
                    if (left < wa.Left) left = wa.Left + 10;
                    if (top + this.Height > wa.Bottom) top = wa.Bottom - this.Height - 10;
                    if (top < wa.Top) top = wa.Top + 10;

                    this.Left = left;
                    this.Top = top;
                }
            }
            catch { }

            // 加载历史消息（最后 N 条）
            Dispatcher.BeginInvoke((Action)LoadInitialHistory,
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>首次加载：显示最后 BatchSize 条</summary>
        private void LoadInitialHistory()
        {
            var history = _plugin.GetMessageHistory();
            var total = history.Count;
            if (total == 0) { _initialLoadDone = true; return; }

            var start = Math.Max(0, total - BatchSize);
            _loadCount = total - start;
            _allLoaded = start == 0;

            for (int i = start; i < total; i++)
            {
                var msg = history[i];
                AppendMessage(msg.Content, msg.IsUser, false);
                _messages.Add(new DeepSeekClient.ChatMessage
                {
                    IsUser = msg.IsUser,
                    Content = msg.Content
                });
            }

            _initialLoadDone = true;
            // 滚动到底部
            MessageArea.ScrollToBottom();
        }

        /// <summary>滚动到顶部时加载更多</summary>
        private void MessageArea_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!_initialLoadDone || _allLoaded || _isLoadingHistory) return;
            if (e.VerticalOffset > 0) return; // 还没到顶

            LoadMoreHistory();
        }

        /// <summary>加载更早的消息</summary>
        private void LoadMoreHistory()
        {
            _isLoadingHistory = true;

            try
            {
                var history = _plugin.GetMessageHistory();
                var total = history.Count;
                if (total <= _loadCount) { _allLoaded = true; return; }

                var remaining = total - _loadCount;
                var batch = Math.Min(BatchSize, remaining);
                var start = total - _loadCount - batch;

                // 记录当前滚动位置
                var child = MessageList.Children.Count > 0 ? MessageList.Children[0] : null;

                // 在顶部插入更早的消息
                for (int i = start; i < start + batch; i++)
                {
                    var msg = history[i];
                    InsertMessageAtTop(msg.Content, msg.IsUser);
                    _messages.Insert(0, new DeepSeekClient.ChatMessage
                    {
                        IsUser = msg.IsUser,
                        Content = msg.Content
                    });
                }

                _loadCount += batch;
                _allLoaded = start == 0;

                // 保持滚动位置不变（聚焦在插入前的第一条消息）
                if (child != null)
                {
                    MessageArea.ScrollToVerticalOffset(0);
                }
            }
            finally
            {
                _isLoadingHistory = false;
            }
        }

        /// <summary>在消息列表顶部插入一条消息</summary>
        private void InsertMessageAtTop(string content, bool isUser)
        {
            var panel = BuildMessagePanel(content, isUser);
            MessageList.Children.Insert(0, panel);
        }

        /// <summary>追加一条消息到底部</summary>
        private void AppendMessage(string content, bool isUser, bool scrollToBottom)
        {
            var panel = BuildMessagePanel(content, isUser);
            MessageList.Children.Add(panel);
            if (!isUser) _streamingTextBox = panel.Children[1] is Border b ? b.Child as TextBox : null;
            if (scrollToBottom) MessageArea.ScrollToBottom();
        }

        private StackPanel BuildMessagePanel(string content, bool isUser)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            panel.Children.Add(new TextBlock
            {
                Text = isUser ? "🧑 我" : $"🌸 {_plugin.Config.PetName}",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(4, 0, 0, 2)
            });

            var textBox = new TextBox
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = isUser ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // 允许 Ctrl+C 复制
            textBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                    e.Handled = false; // 让默认复制行为生效
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
            bubble.Child = textBox;
            panel.Children.Add(bubble);

            return panel;
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

            // 拦截指令，转发到 TalkBox 处理
            var cmd = text.Trim().ToLower();
            if (cmd.StartsWith("/"))
            {
                // 通过插件主逻辑处理指令
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // 查找已注册的 TalkBox 并调用 Responded
                        foreach (var api in _plugin.MW.TalkAPI)
                        {
                            if (api is DeepSeekTalkBox talkBox)
                            {
                                talkBox.Responded(text);
                                break;
                            }
                        }
                    }
                    catch { }
                });
                _isProcessing = false;
                BtnSend.IsEnabled = true;
                InputBox.Focus();
                return;
            }

            AppendMessage(text, isUser: true, scrollToBottom: true);
            _messages.Add(new DeepSeekClient.ChatMessage { IsUser = true, Content = text });
            // 同步到主历史
            _plugin.AddMessage(true, text);

            _isProcessing = true;
            BtnSend.IsEnabled = false;

            try
            {
                AppendMessage("", isUser: false, scrollToBottom: true);
                var fullText = new StringBuilder();

                await _plugin.ApiClient!.SendMessageStreamAsync(
                    _messages,
                    onContent: chunk =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            fullText.Append(chunk);
                            if (_streamingTextBox != null)
                                _streamingTextBox.Text = fullText.ToString();
                            MessageArea.ScrollToBottom();
                        });
                    },
                    onFinish: _ =>
                    {
                        Dispatcher.Invoke(() => _streamingTextBox = null);
                    },
                    onError: error =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_streamingTextBox != null)
                                _streamingTextBox.Text += $"\n\n⚠️ {error}";
                            _streamingTextBox = null;
                        });
                    },
                    systemPrompt: _plugin.Config.SystemPrompt
                );

                _messages.Add(new DeepSeekClient.ChatMessage
                {
                    IsUser = false,
                    Content = fullText.ToString()
                });
                // 同步到主历史
                _plugin.AddMessage(false, fullText.ToString());
            }
            catch (Exception ex)
            {
                AppendMessage($"⚠️ {ex.Message}", isUser: false, scrollToBottom: true);
            }
            finally
            {
                _isProcessing = false;
                BtnSend.IsEnabled = true;
                InputBox.Focus();
            }
        }

        public void AddAiMessage(string content)
        {
            Dispatcher.Invoke(() =>
            {
                AppendMessage(content, isUser: false, scrollToBottom: true);
                _messages.Add(new DeepSeekClient.ChatMessage { IsUser = false, Content = content });
            });
        }

        public void AddUserMessage(string content)
        {
            Dispatcher.Invoke(() =>
            {
                AppendMessage(content, isUser: true, scrollToBottom: true);
                _messages.Add(new DeepSeekClient.ChatMessage { IsUser = true, Content = content });
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_plugin.MW.Windows.Contains(this))
                _plugin.MW.Windows.Remove(this);
            base.OnClosing(e);
        }
    }
}
