using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                // 备选方案：用 PreviewKeyDown，它在 TextBox 处理 Enter 之前触发
                _inputBox.PreviewKeyDown += OnPreviewInputKeyDown;
            }

            // 初始化 Agent 引擎并设置命令确认回调
            SetupAgentCommandConfirmation();
        }

        /// <summary>
        /// 设置 Agent 命令确认回调
        /// </summary>
        private void SetupAgentCommandConfirmation()
        {
            RunCommandTool.RequestConfirmation = (command, isDestructive) =>
            {
                // Auto 模式 + 非危险操作 → 静默执行
                if (_plugin.IsAutoMode && !isDestructive)
                    return true;

                // 需要用户确认 — 在 UI 线程上弹窗
                return _plugin.MW.Dispatcher.Invoke(() =>
                {
                    var title = isDestructive
                        ? "⚠️ 危险操作确认"
                        : "🔧 命令执行确认";
                    var message = $"芽衣想要执行以下命令：\n\n{command}\n\n是否允许？";

                    // Agent 模式时才弹窗，非 Agent 模式默认拒绝执行命令
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

                // ===== 全局指令（Agent 模式和聊天模式共享） =====
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

                // ===== 根据当前模式处理消息 =====
                if (_plugin.IsAgentMode)
                {
                    HandleAgentMessage(text);
                }
                else
                {
                    HandleChatMessage(text);
                }
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
                // 初始化 Agent 引擎
                _plugin.InitializeAgentEngine();
                if (_plugin.AgentEngine == null)
                {
                    _plugin.IsAgentMode = false;
                    _plugin.MW.Main.Say("⚠️ 请先在设置中配置 API Key");
                    return;
                }

                var autoStatus = _plugin.IsAutoMode ? "（自动模式已开启）" : "";
                _plugin.MW.Main.Say($"🤖 Agent 模式已开启！{autoStatus}\n我可以帮你读代码、改文件、执行命令。需要我做什么？\n💡 输入 /auto 切换自动模式，/chat 返回聊天模式");
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

            // 确保工作目录是最新的
            engine.WorkingDirectory = _plugin.GetWorkingDirectory();

            _plugin.AddMessage(true, text);

            // 显示思考中提示
            _plugin.MW.Main.Say("🤔 让我看看...");

            // 执行 Agent（同步等待，Task.Run 已在后台线程上）
            var result = engine.ExecuteAsync(text).GetAwaiter().GetResult();

            _plugin.AddMessage(false, result);

            if (!string.IsNullOrWhiteSpace(result))
            {
                _plugin.MW.Main.Say(result);
            }
        }

        // ===== 普通聊天模式 =====

        private void HandleChatMessage(string text)
        {
            if (_plugin.ApiClient == null)
            {
                _plugin.MW.Main.Say("⚠️ 请先在设置中配置 API Key");
                return;
            }

            _plugin.AddMessage(true, text);

            var response = _plugin.ApiClient.SendMessageAsync(
                _plugin.GetMessageHistory(),
                _plugin.Config.SystemPrompt
            ).GetAwaiter().GetResult();

            _plugin.AddMessage(false, response);

            if (!string.IsNullOrWhiteSpace(response))
            {
                _plugin.MW.Main.Say(response);
            }
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
