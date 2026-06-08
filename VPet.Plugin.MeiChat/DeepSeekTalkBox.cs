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
        private bool _isProcessing;
        private readonly Queue<string> _messageQueue = new();

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

        /// <summary>人性化思考提示词</summary>
        private string PickThinkingPhrase(string userInput)
        {
            var input = userInput.ToLower();

            // 拖放文件或分析文件优先
            if (input.StartsWith("帮我分析这个文件") || input.Contains("分析") && input.Contains("文件"))
                return RandomOf("让我看看这个文件~", "来咯，我看看里面写了什么~", "嗯我读一下~");

            // 明确的场景匹配
            if (input.Contains("http") || input.Contains("网页") || input.Contains("网址") || input.Contains("链接"))
                return RandomOf("让我看看有什么好看的~", "哎呀，我看看这个网页...", "来咯，帮你看看~");

            if (input.Contains("编译") || input.Contains("运行") || input.Contains("build"))
                return RandomOf("正在请图灵老祖出山...", "我来跑一下看看~", "好嘞，运行一波！");

            if (input.Contains("写") || input.Contains("创建") || input.Contains("新建") || input.Contains("生成"))
                return RandomOf("可给我累坏了...开玩笑的，马上好！", "来咯，写一个~", "好嘞，这就安排上！");

            if (input.Contains("改") || input.Contains("修") || input.Contains("修复") || input.Contains("编辑"))
                return RandomOf("让我看看哪里不对...", "找到问题了，改一下就好~", "小问题，我来修修~");

            if (input.Contains("工作") || input.Contains("干活") || input.Contains("开始"))
                return RandomOf("好嘞~", "来咯！", "收到收到~");

            if (input.Contains("搜") || input.Contains("找") || input.Contains("查") || input.Contains("搜索"))
                return RandomOf("我找找看...", "让我翻一翻~", "嗯我来查查~");

            if (input.Contains("睡觉") || input.Contains("晚安") || input.Contains("睡"))
                return RandomOf("晚安呀~", "好梦！", "明天见~");

            if (input.Contains("起床") || input.Contains("早安"))
                return RandomOf("早安！", "起来啦~", "早上好呀~");

            // 通用短语（随机轮换）
            return RandomOf(
                "让我看看...",
                "好嘞~",
                "嗯我来看看~",
                "来咯~",
                "收到！",
                "哎呀我看看~",
                "好滴~"
            );
        }

        private static string RandomOf(params string[] options)
            => options[Random.Shared.Next(options.Length)];

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

                // 如果正在处理中，加入队列稍后处理
                if (_isProcessing)
                {
                    lock (_messageQueue)
                    {
                        _messageQueue.Enqueue(text);
                    }
                    _plugin.MW.Main.Say("我先处理完当前的，马上回答你~");
                    return;
                }
                _isProcessing = true;
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
            finally
            {
                _isProcessing = false;
                ProcessQueue();
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

        // ===== API 客户端与引擎初始化 =====

        /// <summary>确保引擎已就绪，失败时显示具体错误信息并返回 null</summary>
        private Agent.AgentEngine? EnsureEngineReady(string context)
        {
            // 尝试初始化引擎（内部会修复 ApiClient 为空的问题）
            _plugin.InitializeAgentEngine();

            var engine = _plugin.AgentEngine;
            if (engine != null) return engine;

            // 引擎初始化失败，根据 ApiStatus 给出具体信息
            if (_plugin.Config == null || string.IsNullOrWhiteSpace(_plugin.Config.ApiKey))
            {
                _plugin.MW.Main.Say("⚠️ 请先在设置中配置 API Key（右键桌宠 → 设置 → MeiChat）");
            }
            else switch (_plugin.ApiStatus)
            {
                case ApiConnectionStatus.Failed:
                    _plugin.MW.Main.Say("⚠️ API 连接测试失败，请检查 API Key 是否还有余额，或 API 地址是否正确");
                    break;
                case ApiConnectionStatus.Checking:
                    _plugin.MW.Main.Say("⏳ 正在验证 API 连接，请稍后再试...");
                    break;
                case ApiConnectionStatus.Unknown:
                    // 还没验证过，尝试异步验证并提示
                    _plugin.MW.Main.Say("🔍 正在测试 API 连接，请稍等...");
                    break;
                default:
                    _plugin.MW.Main.Say("⚠️ API 客户端初始化异常，请检查设置中的 API 配置");
                    break;
            }
            return null;
        }

        // ===== 思考模式（工具可用） =====

        private void HandleAgentMessage(string text)
        {
            var engine = EnsureEngineReady("agent");
            if (engine == null) return;

            engine.WorkingDirectory = _plugin.GetWorkingDirectory();
            _plugin.AddMessage(true, text);

            var thinking = PickThinkingPhrase(text);
            _plugin.MW.Main.Say(thinking);

            try
            {
                var result = engine.ExecuteAsync(text, CancellationToken.None).GetAwaiter().GetResult();

                if (!string.IsNullOrWhiteSpace(result))
                    _plugin.MW.Main.Say(result.TrimStart());
                else
                    _plugin.MW.Main.Say("嗯，处理完了，有什么需要补充的吗？");
                _plugin.AddMessage(false, result ?? "");
            }
            catch (Exception ex)
            {
                _plugin.ResetAgentEngine();
                _plugin.MW.Main.Say($"抱歉出错了，已恢复状态，可以继续提问。{ex.Message}");
            }
        }

        // ===== 日常聊天（AI 自动判断是否用工具） =====

        private void HandleMessage(string text)
        {
            var engine = EnsureEngineReady("chat");
            if (engine == null) return;

            engine.WorkingDirectory = _plugin.GetWorkingDirectory();
            _plugin.AddMessage(true, text);

            var thinking = PickThinkingPhrase(text);
            _plugin.MW.Main.Say(thinking);

            try
            {
                var result = engine.ExecuteAsync(text, CancellationToken.None).GetAwaiter().GetResult();

                if (!string.IsNullOrWhiteSpace(result))
                    _plugin.MW.Main.Say(result.TrimStart());
                else
                    _plugin.MW.Main.Say("嗯，处理完了。");
                _plugin.AddMessage(false, result ?? "");
            }
            catch (Exception ex)
            {
                _plugin.ResetAgentEngine();
                _plugin.MW.Main.Say($"抱歉出错了，已恢复状态，可以继续提问。{ex.Message}");
            }
        }

        // ===== 消息队列 =====

        private void ProcessQueue()
        {
            string? next;
            lock (_messageQueue)
            {
                if (_messageQueue.Count == 0) return;
                next = _messageQueue.Dequeue();
            }
            if (next != null)
            {
                _plugin.MW.Main.Say("好的，继续回答你上一个问题~");
                System.Threading.Thread.Sleep(300);
                Responded(next);
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
