using System;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Interface;
using VPet.Plugin.MeiChat.Agent.Tools;
using VPet.Plugin.MeiChat.Models;
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

        // Agent 模式 3 小时提醒
        private DateTime _agentModeStartTime = DateTime.MinValue;
        private bool _pendingModeChoice = false;

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

                var result = _plugin.MW.Dispatcher.Invoke(() =>
                    ConfirmDialog.Show(command, isDestructive));

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

        /// <summary>根据用户输入选择合适的思考提示词</summary>
        private string PickThinkingPhrase(string userInput)
        {
            var input = userInput.ToLower();

            if (input.Contains("搜") || input.Contains("查") || input.Contains("找") || input.Contains("搜索") || input.Contains("百度") || input.Contains("谷歌"))
                return "我查一下相关资料...";

            if (input.Contains("代码") || input.Contains("写") || input.Contains("改") || input.Contains("修复") || input.Contains("bug") || input.Contains("报错") || input.Contains("错误"))
                return "让我看一下代码...";

            if (input.Contains("读") || input.Contains("文件") || input.Contains("打开") || input.Contains("项目") || input.Contains("目录"))
                return "让我看看文件...";

            if (input.Contains("工作") || input.Contains("作息") || input.Contains("睡觉") || input.Contains("起床"))
                return "好的~";

            if (input.Contains("编译") || input.Contains("运行") || input.Contains("构建") || input.Contains("build") || input.Contains("执行"))
                return "我来运行看看...";

            if (input.Contains("解释") || input.Contains("什么") || input.Contains("怎么") || input.Contains("为什么") || input.Contains("如何"))
                return "我想想...";

            if (input.Contains("记忆") || input.Contains("记住") || input.Contains("忘了"))
                return "让我回忆一下...";

            if (input.Contains("网页") || input.Contains("网址") || input.Contains("http") || input.Contains("https") || input.Contains("com") || input.Contains("cn"))
                return "我去看看这个网页...";

            if (input.Contains("总结") || input.Contains("概括") || input.Contains("摘要"))
                return "我来看一下重点...";

            if (input.Contains("翻译") || input.Contains("英文") || input.Contains("中文"))
                return "让我看看...";

            // 随机选一个通用提示
            string[] generic = { "让我看看...", "我想想...", "好的我来处理...", "让我先了解一下...", "嗯我来看看...", "好的请稍等..." };
            return generic[Random.Shared.Next(generic.Length)];
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

                // ===== 处理待定的模式选择 =====
                if (_pendingModeChoice)
                {
                    _pendingModeChoice = false;
                    if (cmd == "1")
                    {
                        _agentModeStartTime = DateTime.Now;
                        _plugin.MW.Main.Say("✅ 继续思考模式，已重新计时。");
                    }
                    else if (cmd == "2")
                    {
                        _plugin.IsAgentMode = false;
                        _plugin.MW.Main.Say("💬 已退出思考模式，回到日常聊天。");
                    }
                    else
                    {
                        _pendingModeChoice = true; // 再问一次
                        _plugin.MW.Main.Say("输入 1 继续思考模式，输入 2 切换回日常聊天。");
                    }
                    return;
                }

                // ===== 指令处理 =====
                if (cmd == "/agent")
                {
                    ToggleAgentMode();
                    return;
                }
                if (cmd == "/ui" || cmd == "/window" || cmd == "/long")
                {
                    _plugin.MW.Dispatcher.Invoke(() => _plugin.OpenChatWindow());
                    return;
                }
                if (cmd == "/reset-workdir")
                {
                    var defaultDir = AppConfig.GetDefaultWorkingDirectory();
                    _plugin.Config.WorkingDirectory = defaultDir;
                    _plugin.Config.Save();
                    _plugin.MW.Main.Say($"✅ 工作目录已重置为默认路径:\n{defaultDir}");
                    return;
                }
                if (cmd == "/quiet")
                {
                    _plugin.Proactive?.SetQuiet(3);
                    _plugin.MW.Main.Say("好的，我安静 3 小时，你说话了我再出来~");
                    return;
                }
                if (cmd == "/stats")
                {
                    var stats = _plugin.Stats.GetSummary();
                    var memCount = _plugin.Memory?.Count ?? 0;
                    _plugin.MW.Main.Say($"{stats}\n记忆条数: {memCount}");
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
                        _agentModeStartTime = DateTime.MinValue;
                        _plugin.MW.Main.Say("已退出思考模式，回到日常聊天。");
                    }
                    return;
                }

                // ===== 3 小时提醒检查 =====
                if (_plugin.IsAgentMode && _agentModeStartTime != DateTime.MinValue)
                {
                    if ((DateTime.Now - _agentModeStartTime).TotalHours >= 3)
                    {
                        _pendingModeChoice = true;
                        _plugin.MW.Main.Say("你已经处于思考模式 3 小时了，是否继续？\n输入 1 继续思考模式，输入 2 切换回日常聊天。");
                        return;
                    }
                }

                // ===== 处理消息 =====
                _plugin.Proactive?.NotifyInteraction();

                if (_plugin.IsAgentMode)
                    HandleAgentMessage(text);
                else
                    HandleMessage(text);
            }
            catch (Exception ex)
            {
                _plugin.MW.Main.Say($"⚠️ {ex.Message}");
            }
        }

        // ===== Agent 模式切换 =====

        private void ToggleAgentMode()
        {
            _plugin.IsAgentMode = !_plugin.IsAgentMode;
            if (_plugin.IsAgentMode)
            {
                _agentModeStartTime = DateTime.Now;
                _pendingModeChoice = false;
                _plugin.InitializeAgentEngine();
                if (_plugin.AgentEngine == null)
                {
                    _plugin.IsAgentMode = false;
                    _plugin.MW.Main.Say("⚠️ 请先在设置中配置 API Key");
                    return;
                }
                var autoStatus = _plugin.IsAutoMode ? "（自动模式已开启）" : "";
                _plugin.MW.Main.Say($"思考模式已开启！{autoStatus}\n我可以帮你读代码、改文件、执行命令。\n3 小时后我会提醒你确认是否继续。\n输入 /chat 可随时退出。");
            }
            else
            {
                _agentModeStartTime = DateTime.MinValue;
                _pendingModeChoice = false;
                _plugin.MW.Main.Say("已退出思考模式，回到日常聊天。");
            }
        }

        private void ToggleAutoMode()
        {
            _plugin.IsAutoMode = !_plugin.IsAutoMode;
            var status = _plugin.IsAutoMode ? "开启" : "关闭";
            _plugin.MW.Main.Say($"自动模式已{status}\n" +
                (_plugin.IsAutoMode
                    ? "命令自动执行，危险操作仍需确认"
                    : "每次执行命令前都会询问你"));
        }

        // ===== 思考模式（工具可用） =====

        private void HandleAgentMessage(string text)
        {
            var engine = _plugin.AgentEngine;
            if (engine == null) { _plugin.MW.Main.Say("⚠️ 请先配置 API Key"); return; }

            engine.WorkingDirectory = _plugin.GetWorkingDirectory();
            _plugin.AddMessage(true, text);

            var thinking = PickThinkingPhrase(text);
            _plugin.MW.Main.Say(thinking);

            try
            {
                var result = engine.ExecuteAsync(text).GetAwaiter().GetResult();

                if (!string.IsNullOrWhiteSpace(result))
                    _plugin.MW.Main.Say(result.TrimStart());
                else
                    _plugin.MW.Main.Say("嗯，处理完了，有什么需要补充的吗？");
                _plugin.AddMessage(false, result ?? "");
            }
            catch (Exception ex)
            {
                _plugin.MW.Main.Say($"抱歉出错了: {ex.Message}");
            }
        }

        // ===== 日常聊天 =====

        private void HandleMessage(string text)
        {
            _plugin.InitializeAgentEngine();
            var engine = _plugin.AgentEngine;
            if (engine == null)
            {
                _plugin.MW.Main.Say("⚠️ 请先在设置中配置 API Key");
                return;
            }

            engine.WorkingDirectory = _plugin.GetWorkingDirectory();
            _plugin.AddMessage(true, text);

            var thinking = PickThinkingPhrase(text);
            _plugin.MW.Main.Say(thinking);

            try
            {
                var result = engine.ExecuteAsync(text).GetAwaiter().GetResult();

                if (!string.IsNullOrWhiteSpace(result))
                    _plugin.MW.Main.Say(result.TrimStart());
                else
                    _plugin.MW.Main.Say("嗯，处理完了。");
                _plugin.AddMessage(false, result ?? "");
            }
            catch (Exception ex)
            {
                _plugin.MW.Main.Say($"抱歉出错了: {ex.Message}");
            }
        }

        // ===== 帮助 =====

        private void ShowHelp()
        {
            var help = "可用指令：\n" +
                       "/agent - 切换思考模式\n" +
                       "/chat - 退出思考模式\n" +
                       "/ui 或 /long - 打开长聊天框\n" +
                       "/auto - 切换自动执行模式\n" +
                       "/clear - 清空历史\n" +
                       "/quiet - 安静 3 小时\n" +
                       "/reset-workdir - 重置工作目录为桌面\n" +
                       "/stats - 查看 API 统计\n" +
                       "/help - 本帮助\n\n" +
                       "日常聊天直接说话，复杂任务我会自动处理。\n" +
                       "/agent 开启思考模式后，可读写文件、执行命令。\n" +
                       "我有默认作息，到点会自动工作/休息/睡觉。";
            _plugin.MW.Main.Say(help);
        }
    }
}
