using System;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Interface;
using VPet.Plugin.MeiChat.Agent.Tools;
using VPet.Plugin.MeiChat.Views;

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
            _inputBox = GetField<TextBox>("tbTalk");
            _sendBtn = GetField<Button>("Send");

            if (_inputBox != null)
            {
                _inputBox.KeyDown += OnInputKeyDown;
                _inputBox.PreviewKeyDown += OnPreviewInputKeyDown;
            }

            SetupAgentCommandConfirmation();
        }

        private void SetupAgentCommandConfirmation()
        {
            RunCommandTool.RequestConfirmation = (command, isDestructive) =>
            {
                if (_plugin.IsAutoMode && !isDestructive)
                    return true;

                // 非 Agent 模式默认拒绝
                if (!_plugin.IsAgentMode)
                    return false;

                // 使用自定义弹窗（可滚动、可拖动、可一键开启自动模式）
                var result = _plugin.MW.Dispatcher.Invoke(() =>
                    ConfirmDialog.Show(command, isDestructive));

                // 用户点击了"不再询问" → 开启自动模式
                if (result.AutoMode)
                {
                    _plugin.IsAutoMode = true;
                    _plugin.MW.Main.Say("🚀 自动模式已开启，后续命令将自动执行（危险操作除外）");
                }

                return result.Allowed;
            };
        }

        private void OnPreviewInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.IsKeyDown(Key.LeftShift))
                return;
            e.Handled = true;
            DoSend();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.IsKeyDown(Key.LeftShift))
                return;
            e.Handled = true;
            DoSend();
        }

        private void DoSend()
        {
            if (_inputBox == null) return;
            var text = _inputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            _inputBox.Text = "";
            System.Threading.Tasks.Task.Run(() => Responded(text));
        }

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
                _plugin.MW.Main.Say("💡 请在 Agent 模式下使用 /auto");
                return;
            }
            _plugin.IsAutoMode = !_plugin.IsAutoMode;
            var status = _plugin.IsAutoMode ? "开启 ✅" : "关闭 ❌";
            _plugin.MW.Main.Say($"🤖 自动模式已{status}\n" +
                (_plugin.IsAutoMode
                    ? "命令自动执行，危险操作仍需确认"
                    : "每次执行命令前都会询问你"));
        }

        private void HandleAgentMessage(string text)
        {
            var engine = _plugin.AgentEngine;
            if (engine == null) { _plugin.MW.Main.Say("⚠️ 请先配置 API Key"); return; }

            engine.WorkingDirectory = _plugin.GetWorkingDirectory();
            _plugin.AddMessage(true, text);
            _plugin.MW.Main.Say("🤔 让我看看...");

            var result = engine.ExecuteAsync(text).GetAwaiter().GetResult();
            _plugin.AddMessage(false, result);

            if (!string.IsNullOrWhiteSpace(result))
                _plugin.MW.Main.Say(result.TrimStart());
        }

        // ===== 普通聊天模式（极致流式输出） =====

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

            // 附加工作目录信息，防止 AI 瞎编路径
            var chatSystemPrompt = _plugin.Config.SystemPrompt;
            var workDir = _plugin.GetWorkingDirectory();
            if (!string.IsNullOrWhiteSpace(workDir))
            {
                chatSystemPrompt += $"\n\n当前工作目录（请如实告知用户）:\n{workDir}";
            }

            try
            {
                _plugin.ApiClient.SendMessageStreamAsync(
                    _plugin.GetMessageHistory(),
                    onContent: chunk =>
                    {
                        fullText.Append(chunk);

                        // BeginInvoke 异步派发，不阻塞网络接收线程
                        _plugin.MW.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            var display = fullText.ToString().TrimStart();
                            if (display.Length > 0)
                            {
                                if (!hasContent) hasContent = true;
                                _plugin.MW.Main.Say(display);
                            }
                        }));
                    },
                    onFinish: _ =>
                    {
                        // onContent 已完成最后一块的显示，无需重复调用
                    },
                    onError: error =>
                    {
                        _plugin.MW.Dispatcher.BeginInvoke((Action)(() =>
                            _plugin.MW.Main.Say($"⚠️ {error}")));
                    },
                    systemPrompt: chatSystemPrompt
                ).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _plugin.MW.Main.Say($"⚠️ {ex.Message}");
            }

            _plugin.AddMessage(false, fullText.ToString());
        }

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
