using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using VPet_Simulator.Windows.Interface;
using VPet.Plugin.MeiChat.Agent;
using VPet.Plugin.MeiChat.Agent.Tools;
using VPet.Plugin.MeiChat.Memory;
using VPet.Plugin.MeiChat.Models;
using VPet.Plugin.MeiChat.Schedule;
using VPet.Plugin.MeiChat.Views;

namespace VPet.Plugin.MeiChat
{
    public class Main : MainPlugin
    {
        public override string PluginName => "MeiChat";

        public AppConfig Config { get; set; } = new();
        public DeepSeekClient? ApiClient { get; private set; }
        private string _configDir = string.Empty;
        private readonly List<DeepSeekClient.ChatMessage> _messages = new();
        private bool _talkBoxRegistered;

        // ===== Agent 引擎 =====
        /// <summary>Agent 引擎（lazy init，首次进入 Agent 模式时创建）</summary>
        public AgentEngine? AgentEngine { get; private set; }
        /// <summary>工具注册中心</summary>
        public ToolRegistry? ToolRegistry { get; private set; }

        // ===== 持久化模块 =====
        /// <summary>持久记忆</summary>
        public MemoryManager? Memory { get; private set; }
        /// <summary>作息调度</summary>
        public ScheduleManager? Scheduler { get; private set; }
        /// <summary>主动互动</summary>
        public ProactiveInteraction? Proactive { get; private set; }
        /// <summary>API 用量统计</summary>
        public ApiStats Stats { get; private set; } = new();
        /// <summary>是否处于 Agent 模式</summary>
        public bool IsAgentMode
        {
            get => Config.AgentMode;
            set
            {
                Config.AgentMode = value;
                Config.Save();
            }
        }
        /// <summary>是否自动执行命令</summary>
        public bool IsAutoMode
        {
            get => Config.AutoMode;
            set
            {
                Config.AutoMode = value;
                if (AgentEngine != null) AgentEngine.AutoMode = value;
                Config.Save();
            }
        }

        public Main(IMainWindow mainwin) : base(mainwin)
        {
        }

        public override void LoadPlugin()
        {
            base.LoadPlugin();
            try
            {
                _configDir = ExtensionValue.GetMODStorage(PluginName);
                Config = AppConfig.Load(_configDir);

                // 初始化持久模块
                Memory = new MemoryManager(_configDir);
                Scheduler = new ScheduleManager(this, Memory);
                Proactive = new ProactiveInteraction(this, Memory);
                Stats = new ApiStats();

                InitializeApiClient();

                // 注册 TalkBox
                if (!_talkBoxRegistered)
                {
                    var talkBox = new DeepSeekTalkBox(this);
                    MW.TalkAPI.Add(talkBox);
                    _talkBoxRegistered = true;
                }
            }
            catch { }
        }

        public override void GameLoaded()
        {
            base.GameLoaded();

            // 启动作息调度和主动互动（游戏加载完成后）
            try { Scheduler?.Start(); } catch { }
            try { Proactive?.Start(); } catch { }

            // 注册 TalkBox 接入 VPet 原生聊天框
            if (!_talkBoxRegistered)
            {
                try
                {
                    var talkBox = new DeepSeekTalkBox(this);
                    MW.TalkAPI.Add(talkBox);
                    _talkBoxRegistered = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MeiChat] TalkBox注册失败: {ex.Message}");
                }
            }

            // ✅ 原生 TalkBox 已注册，自定义窗口不再自动弹出
            // 需要时可通过聊天输入 "/ui" 唤出
        }

        public override void Setting()
        {
            base.Setting();
            var settingsWindow = new SettingsWindow(Config, OnConfigSaved);
            settingsWindow.Owner = Application.Current?.MainWindow;

            if (settingsWindow.ShowDialog() == true && Config.IsValid())
            {
                OpenChatWindow();
            }
        }

        /// <summary>
        /// 打开或激活聊天窗口
        /// </summary>
        /// <returns>聊天窗口实例（新创建或已存在的）</returns>
        public ChatWindow? OpenChatWindow()
        {
            if (ApiClient == null)
            {
                MessageBox.Show("请先在设置中配置 API Key",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            // 查找是否已有聊天窗口
            foreach (var w in MW.Windows)
            {
                if (w is ChatWindow chatWin)
                {
                    chatWin.Activate();
                    return chatWin;
                }
            }

            // 创建常驻聊天窗口
            var window = new ChatWindow(this);
            window.Closed += (s, e) => MW.Windows.Remove(window);
            MW.Windows.Add(window);
            window.Show();
            return window;
        }

        public override void Save()
        {
            base.Save();
            Config.Save();
        }

        public override void EndGame()
        {
            base.EndGame();
            Config.Save();
            Scheduler?.Stop();
            Scheduler?.Dispose();
            Memory?.Save();
            ApiClient?.Dispose();
            ApiClient = null;
        }

        // ===== 对话历史（供 TalkBox 使用） =====
        public List<DeepSeekClient.ChatMessage> GetMessageHistory()
            => new List<DeepSeekClient.ChatMessage>(_messages);

        public void AddMessage(bool isUser, string content)
        {
            _messages.Add(new DeepSeekClient.ChatMessage { IsUser = isUser, Content = content });
            while (_messages.Count > 50) _messages.RemoveAt(0);
        }

        public void ClearHistory() => _messages.Clear();

        public void ReinitializeApiClient()
        {
            ApiClient?.Dispose();
            InitializeApiClient();
        }

        private void InitializeApiClient()
        {
            if (!string.IsNullOrWhiteSpace(Config.ApiKey))
            {
                var client = new DeepSeekClient(Config.ApiKey, Config.Model,
                    Config.MaxTokens, Config.Temperature, Config.ApiBaseUrl);

                // 用量追踪
                client.OnUsage = (prompt, completion, cacheHit, cacheMiss) =>
                    Stats.RecordUsage(prompt, completion, cacheHit, cacheMiss);

                ApiClient = client;
            }
        }

        // ===== Agent 引擎初始化 =====

        /// <summary>
        /// 初始化 Agent 引擎（需要在 ApiClient 可用时调用）
        /// </summary>
        public void InitializeAgentEngine()
        {
            if (ApiClient == null) return;

            // 如果已有引擎但客户端变了，重新创建
            if (AgentEngine != null) return;

            ToolRegistry = new ToolRegistry();
            var workingDir = GetWorkingDirectory();

            // 注册工具
            ToolRegistry.Register(new ReadFileTool());
            ToolRegistry.Register(new WriteFileTool());
            ToolRegistry.Register(new EditFileTool());
            ToolRegistry.Register(new ListDirectoryTool());
            ToolRegistry.Register(new SearchCodeTool());
            ToolRegistry.Register(new RunCommandTool());
            ToolRegistry.Register(new ReadWebTool());
            ToolRegistry.Register(new SearchWebTool());
            MemoryTool.Manager = Memory;
            ToolRegistry.Register(new MemoryTool());
            ApiStatsTool.Stats = Stats;
            ToolRegistry.Register(new ApiStatsTool());
            ToolRegistry.Register(new PetControlTool(this));
            var fullPrompt = Config.SystemPrompt;
            if (Memory != null)
                fullPrompt += Memory.GetMemoryContext();
            if (Scheduler != null)
                fullPrompt += Scheduler.GetScheduleContext();

            AgentEngine = new AgentEngine(ApiClient, ToolRegistry, fullPrompt,
                workingDirectory: workingDir)
            {
                AutoMode = Config.AutoMode,
                WorkingDirectory = workingDir
            };
        }

        /// <summary>
        /// 清理 Agent 引擎状态
        /// </summary>
        public void ResetAgentEngine()
        {
            AgentEngine?.ClearHistory();
        }

        /// <summary>
        /// 销毁并重建 Agent 引擎（API 配置变更时调用）
        /// </summary>
        public void ReinitializeAgentEngine()
        {
            AgentEngine = null;
            ToolRegistry = null;
            InitializeAgentEngine();
        }

        /// <summary>
        /// 获取有效的工作目录
        /// </summary>
        public string GetWorkingDirectory()
        {
            if (!string.IsNullOrWhiteSpace(Config.WorkingDirectory) &&
                Directory.Exists(Config.WorkingDirectory))
                return Config.WorkingDirectory;

            return AppConfig.GetDefaultWorkingDirectory();
        }

        private void OnConfigSaved(AppConfig newConfig)
        {
            Config = newConfig;
            Config.ConfigDirectory = _configDir;
            Config.Save();
            ReinitializeApiClient();
            ReinitializeAgentEngine();
        }
    }
}
