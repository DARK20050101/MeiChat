using System;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Interface;
using VPet.Plugin.MeiChat.Agent.Tools;

namespace VPet.Plugin.MeiChat
{
    public class DeepSeekTalkBox : TalkBox
    {
        private readonly Main _plugin;
        public override string APIName => "DeepSeek-芽衣";

        private TextBox? _inputBox;
        private Button? _sendBtn;
        private DateTime _lastBubbleUpdate = DateTime.MinValue;

        public DeepSeekTalkBox(Main plugin) : base(plugin)
        {
            _plugin = plugin;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            // 反射获取 tbTalk 输入框
            _inputBox = GetField<TextBox>("tbTalk");
            _sendBtn = GetField<Button>("Send");

            if (_inputBox != null)
            {
                _inputBox.KeyDown += OnInputKeyDown;
                _inputBox.PreviewKeyDown += OnPreviewInputKeyDown;
            }

            // 反转布局：让输入框在上方、输出气泡在下方（向下增长，不挡桌宠）
            Dispatcher.BeginInvoke((Action)FlipLayout,
                System.Windows.Threading.DispatcherPriority.Loaded);

            SetupAgentCommandConfirmation();
        }

        /// <summary>
        /// 反转 TalkBox 内部布局：输入框放上面，输出气泡放下面
        /// 这样气泡变长时会向下增长，不会往上挡住桌宠
        /// </summary>
        private void FlipLayout()
        {
            try
            {
                // 找到根 Grid（TalkBox 模板的主容器）
                var grid = FindVisualChild<Grid>(this);
                if (grid == null) return;

                // 找到输入框所在的容器 Panel
                var inputPanel = FindChildContainer(grid, _inputBox);
                if (inputPanel == null) return;

                // 找到另一个子元素（就是输出气泡容器）
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(grid); i++)
                {
                    var child = VisualTreeHelper.GetChild(grid, i);
                    if (child == inputPanel) continue;
                    if (child is not UIElement bubbleContainer) continue;

                    // 交换 Grid.Row，让输入框在上面
                    int inputRow = Grid.GetRow((UIElement)inputPanel);
                    int bubbleRow = Grid.GetRow(bubbleContainer);

                    if (inputRow < bubbleRow)
                    {
                        // 当前是输入在下面，气泡在上面 → 交换
                        Grid.SetRow((UIElement)inputPanel, bubbleRow);
                        Grid.SetRow(bubbleContainer, inputRow);

                        // 交换 RowDefinition 的高度
                        if (grid.RowDefinitions.Count > Math.Max(inputRow, bubbleRow))
                        {
                            var tmp = grid.RowDefinitions[inputRow].Height;
                            grid.RowDefinitions[inputRow].Height = grid.RowDefinitions[bubbleRow].Height;
                            grid.RowDefinitions[bubbleRow].Height = tmp;
                        }
                    }
                    // else: 已经是我们想要的顺序了
                    break;
                }
            }
            catch { /* 布局反转失败不影响核心功能 */ }
        }

        /// <summary>
        /// 在 Grid 中找到包含指定子元素的直接子容器
        /// </summary>
        private static UIElement? FindChildContainer(Grid grid, DependencyObject? target)
        {
            if (target == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(grid); i++)
            {
                var child = VisualTreeHelper.GetChild(grid, i);
                if (child is UIElement uiChild && ContainsVisual(uiChild, target))
                    return uiChild;
            }
            return null;
        }

        /// <summary>
        /// 判断 parent 的可视树中是否包含 target
        /// </summary>
        private static bool ContainsVisual(DependencyObject parent, DependencyObject target)
        {
            if (parent == target) return true;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                if (ContainsVisual(VisualTreeHelper.GetChild(parent, i), target))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 在可视树中查找指定类型的子元素
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// 设置 Agent 命令确认回调
        /// </summary>
        private void SetupAgentCommandConfirmation()
        {
            RunCommandTool.RequestConfirmation = (command, isDestructive) =>
            {
                if (_plugin.IsAutoMode && !isDestructive)
                    return true;

                return _plugin.MW.Dispatcher.Invoke(() =>
                {
                    var title = isDestructive
                        ? "⚠️ 危险操作确认"
                        : "🔧 命令执行确认";
                    var message = $"芽衣想要执行以下命令：\n\n{command}\n\n是否允许？";

                    if (!_plugin.IsAgentMode)
                        return false;

                    return MessageBox.Show(message, title,
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                });
            };
        }

        /// <summary>Enter 键发送（PreviewKeyDown 优先触发，不被 AcceptsReturn 拦截）</summary>
        private void OnPreviewInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.IsKeyDown(Key.LeftShift))
                return;

            e.Handled = true;
            DoSend();
        }

        /// <summary>Enter 键发送（KeyDown，备选）</summary>
        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.IsKeyDown(Key.LeftShift))
                return;

            e.Handled = true;
            DoSend();
        }

        /// <summary>执行发送</summary>
        private void DoSend()
        {
            if (_inputBox == null) return;

            var text = _inputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            _inputBox.Text = "";
            System.Threading.Tasks.Task.Run(() => Responded(text));
        }

        /// <summary>反射获取私有字段</summary>
        private T? GetField<T>(string name) where T : class
        {
            var f = typeof(TalkBox).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f?.GetValue(this) as T;
        }

        public override void Setting() => _plugin.Setting();

        public override void Responded(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return;

                var cmd = text.Trim().ToLower();

                // ===== 全局指令 =====
                if (cmd == "/ui" || cmd == "/window")
                {
                    _plugin.MW.Dispatcher.Invoke(() => _plugin.OpenChatWindow());
                    return;
                }
                if (cmd == "/clear")
                {
                    _plugin.ClearHistory();
                    _plugin.ResetAgentEngine();
                    _plugin.MW.Main.Say("✅ 已清空对话历史");
                    return;
                }
                if (cmd == "/help")
                {
                    ShowHelp();
                    return;
                }

                // ===== Agent 模式指令 =====
                if (cmd == "/agent")
                {
                    ToggleAgentMode();
                    return;
                }
                if (cmd == "/auto")
                {
                    ToggleAutoMode();
                    return;
                }
                if (cmd == "/chat")
                {
                    if (_plugin.IsAgentMode)
                    {
                        _plugin.IsAgentMode = false;
                        _plugin.MW.Main.Say("💬 已切换回普通聊天模式");
                    }
                    return;
                }

                // ===== 根据模式处理 =====
                if (_plugin.IsAgentMode)
                    HandleAgentMessage(text);
                else
                    HandleChatMessage(text);
            }
            catch (Exception ex)
            {
                _plugin.MW.Main.Say($"⚠️ {ex.Message}");
            }
        }

        // ===== Agent 模式 =====

        private void ToggleAgentMode()
        {
            _plugin.IsAgentMode = !_plugin.IsAgentMode;
            if (_plugin.IsAgentMode)
            {
                _plugin.InitializeAgentEngine();
                if (_plugin.AgentEngine == null)
                {
                    _plugin.IsAgentMode = false;
                    _plugin.MW.Main.Say("⚠️ 请先在设置中配置 API Key");
                    return;
                }

                var autoStatus = _plugin.IsAutoMode ? "（自动模式已开启）" : "";
                _plugin.MW.Main.Say($"🤖 Agent 模式已开启！{autoStatus}\n我可以帮你读代码、改文件、执行命令。\n💡 输入 /auto 切换自动模式，/chat 返回聊天模式");
            }
            else
            {
                _plugin.MW.Main.Say("💬 已退出 Agent 模式");
            }
        }

        private void ToggleAutoMode()
        {
            if (!_plugin.IsAgentMode)
            {
                _plugin.MW.Main.Say("💡 请在 Agent 模式下使用 /auto（先输入 /agent 进入 Agent 模式）");
                return;
            }

            _plugin.IsAutoMode = !_plugin.IsAutoMode;
            var status = _plugin.IsAutoMode ? "开启 ✅" : "关闭 ❌";
            _plugin.MW.Main.Say($"🤖 自动模式已{status}\n" +
                (_plugin.IsAutoMode
                    ? "命令将自动执行，删除等危险操作仍需确认"
                    : "每次执行命令前都会询问你"));
        }

        private void HandleAgentMessage(string text)
        {
            var engine = _plugin.AgentEngine;
            if (engine == null)
            {
                _plugin.MW.Main.Say("⚠️ Agent 引擎未初始化，请检查 API Key 设置");
                return;
            }

            engine.WorkingDirectory = _plugin.GetWorkingDirectory();
            _plugin.AddMessage(true, text);

            _plugin.MW.Main.Say("🤔 让我看看...");

            var result = engine.ExecuteAsync(text).GetAwaiter().GetResult();
            _plugin.AddMessage(false, result);

            if (!string.IsNullOrWhiteSpace(result))
            {
                _plugin.MW.Main.Say(result);
            }
        }

        // ===== 普通聊天模式（流式输出） =====

        private void HandleChatMessage(string text)
        {
            if (_plugin.ApiClient == null)
            {
                _plugin.MW.Main.Say("⚠️ 请先在设置中配置 API Key");
                return;
            }

            _plugin.AddMessage(true, text);

            var fullText = new StringBuilder();
            var hasContent = false;
            _lastBubbleUpdate = DateTime.MinValue;

            try
            {
                _plugin.ApiClient.SendMessageStreamAsync(
                    _plugin.GetMessageHistory(),
                    onContent: chunk =>
                    {
                        fullText.Append(chunk);

                        if (!hasContent && fullText.Length > 0)
                        {
                            hasContent = true;
                            _plugin.MW.Dispatcher.Invoke(() =>
                                _plugin.MW.Main.Say(fullText.ToString()));
                        }
                        else if (hasContent)
                        {
                            var now = DateTime.UtcNow;
                            if ((now - _lastBubbleUpdate).TotalMilliseconds > 120)
                            {
                                _lastBubbleUpdate = now;
                                _plugin.MW.Dispatcher.Invoke(() =>
                                    _plugin.MW.Main.Say(fullText.ToString()));
                            }
                        }
                    },
                    onFinish: _ =>
                    {
                        if (hasContent)
                        {
                            _plugin.MW.Dispatcher.Invoke(() =>
                                _plugin.MW.Main.Say(fullText.ToString()));
                        }
                    },
                    onError: error =>
                    {
                        _plugin.MW.Dispatcher.Invoke(() =>
                            _plugin.MW.Main.Say($"⚠️ {error}"));
                    },
                    systemPrompt: _plugin.Config.SystemPrompt
                ).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _plugin.MW.Main.Say($"⚠️ {ex.Message}");
            }

            _plugin.AddMessage(false, fullText.ToString());
        }

        // ===== 帮助 =====

        private void ShowHelp()
        {
            var help = "📋 可用指令：\n" +
                       "/ui - 打开高级窗口\n" +
                       "/agent - 切换 Agent 模式 🤖\n" +
                       "/auto - 切换自动执行模式\n" +
                       "/chat - 返回普通聊天模式\n" +
                       "/clear - 清空历史\n" +
                       "/help - 本帮助\n\n" +
                       "💡 Agent 模式下我可以读代码、改文件、执行命令！";
            _plugin.MW.Main.Say(help);
        }
    }
}
