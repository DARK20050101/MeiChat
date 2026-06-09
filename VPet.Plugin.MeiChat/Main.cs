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
using VPet.Plugin.MeiChat.TTS;
using VPet.Plugin.MeiChat.Views;

namespace VPet.Plugin.MeiChat
{
    /// <summary>API 连接状态</summary>
    public enum ApiConnectionStatus
    {
        Unknown,   // 尚未验证
        Checking,  // 正在验证
        Connected, // 连接成功
        Failed     // 连接失败
    }

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
        /// <summary>语音朗读服务</summary>
        public TtsService? Tts { get; private set; }
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
        /// <summary>API 连接状态（启动时异步验证）</summary>
        public ApiConnectionStatus ApiStatus { get; private set; } = ApiConnectionStatus.Unknown;

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

                // 初始化 TTS 语音（容错：语音库不可用不影响插件加载）
                try
                {
                    Tts = new TtsService
                    {
                        Enabled = Config.TtsEnabled,
                        Provider = Config.TtsProvider == "Tongyi" ? TtsProvider.TongyiQianwen : TtsProvider.WindowsBuiltIn,
                        VoiceName = Config.TtsVoiceName,
                        TongyiApiKey = Config.TongyiApiKey,
                        TongyiVoice = Config.TongyiVoice,
                        Volume = Config.TtsVolume,
                        Rate = Config.TtsRate
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MeiChat] TTS初始化失败（不影响插件运行）: {ex.Message}");
                }

                // 加载历史聊天记录
                LoadHistory();

                InitializeApiClient();

                // 异步验证 API 连接
                if (ApiClient != null)
                    _ = ValidateApiConnectionAsync();

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

            // EndGame 销毁了 ApiClient，这里重新初始化
            if (ApiClient == null && Config != null && !string.IsNullOrWhiteSpace(Config.ApiKey))
            {
                try
                {
                    InitializeApiClient();
                    _ = ValidateApiConnectionAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MeiChat] API客户端重初始化失败: {ex.Message}");
                }
            }

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

            // 注册拖放文件支持
            RegisterFileDrop();
        }

        /// <summary>注册拖放文件到桌宠窗口上的功能</summary>
        private void RegisterFileDrop()
        {
            try
            {
                if (Application.Current?.MainWindow is Window mainWin)
                {
                    mainWin.AllowDrop = true;
                    mainWin.DragEnter += (s, e) =>
                    {
                        if (e.Data.GetDataPresent(DataFormats.FileDrop))
                            e.Effects = DragDropEffects.Copy;
                    };
                    mainWin.Drop += async (s, e) =>
                    {
                        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                            e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                        {
                            var path = files[0];
                            var name = Path.GetFileName(path);

                            // 告知用户收到文件
                            MW.Main.Say($"收到文件 {name}，让我看看~");

                            // 延迟一下让前一条消息显示
                            await System.Threading.Tasks.Task.Delay(500);

                            // 判断是文件还是文件夹
                            if (File.Exists(path))
                            {
                                var ext = Path.GetExtension(path).ToLower();
                                var textExts = new HashSet<string> {
                                    ".cs", ".py", ".js", ".ts", ".cpp", ".c", ".h", ".java",
                                    ".txt", ".md", ".json", ".xml", ".yaml", ".yml", ".toml",
                                    ".html", ".css", ".scss", ".php", ".rb", ".go", ".rs",
                                    ".sh", ".bat", ".ps1", ".sql", ".cfg", ".ini", ".conf",
                                    ".sln", ".csproj", ".xaml"
                                };

                                if (textExts.Contains(ext))
                                {
                                    var content = await File.ReadAllTextAsync(path);
                                    var maxLen = 3000;
                                    if (content.Length > maxLen)
                                        content = content[..maxLen] + $"\n\n...（文件较长，仅显示前 {maxLen} 字符）";

                                    var msg = $"帮我分析这个文件 {name}，内容如下：\n```\n{content}\n```";
                                    System.Threading.Tasks.Task.Run(() =>
                                    {
                                        try
                                        {
                                            foreach (var api in MW.TalkAPI)
                                            {
                                                if (api is DeepSeekTalkBox talkBox)
                                                {
                                                    talkBox.Responded(msg);
                                                    break;
                                                }
                                            }
                                        }
                                        catch { }
                                    });
                                }
                                else
                                {
                                    MW.Main.Say($"这是 {name}，我暂时只能分析文本文件哦~");
                                }
                            }
                            else if (Directory.Exists(path))
                            {
                                // 是文件夹：列出结构交给 AI
                                var dirInfo = new DirectoryInfo(path);
                                var dirTree = new System.Text.StringBuilder();
                                dirTree.AppendLine($"项目文件夹: {name}");
                                dirTree.AppendLine($"完整路径: {path}");
                                dirTree.AppendLine();

                                // 列出顶层文件和子文件夹
                                try
                                {
                                    foreach (var d in dirInfo.GetDirectories().Take(20))
                                        dirTree.AppendLine($"  📁 {d.Name}/");
                                    foreach (var f in dirInfo.GetFiles().Take(30))
                                        dirTree.AppendLine($"  📄 {f.Name} ({(f.Length > 1024 ? $"{f.Length / 1024}KB" : $"{f.Length}B")})");
                                }
                                catch { }

                                var msg = $"帮我看看这个项目 {name}，目录结构如下，总结一下这是什么项目：\n```\n{dirTree}\n```";
                                System.Threading.Tasks.Task.Run(() =>
                                {
                                    try
                                    {
                                        foreach (var api in MW.TalkAPI)
                                        {
                                            if (api is DeepSeekTalkBox talkBox)
                                            {
                                                talkBox.Responded(msg);
                                                break;
                                            }
                                        }
                                    }
                                    catch { }
                                });
                            }
                            else
                            {
                                MW.Main.Say($"没找到这个文件或文件夹...");
                            }
                        }
                    };
                }
            }
            catch { }
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

        /// <summary>
        /// 打开记忆管理窗口
        /// </summary>
        public MemoryWindow? OpenMemoryWindow()
        {
            if (Memory == null) return null;

            foreach (var w in MW.Windows)
            {
                if (w is MemoryWindow memWin)
                {
                    memWin.Activate();
                    return memWin;
                }
            }

            var window = new MemoryWindow(Memory);
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
            Tts?.Stop();
            ApiClient?.Dispose();
            ApiClient = null;
        }

        // ===== 对话历史（持久化，重启不丢失） =====
        private static readonly string HistoryFileName = "MeiChat.history.json";
        private const int MaxHistory = 100;

        public List<DeepSeekClient.ChatMessage> GetMessageHistory()
            => new List<DeepSeekClient.ChatMessage>(_messages);

        public void AddMessage(bool isUser, string content)
        {
            _messages.Add(new DeepSeekClient.ChatMessage { IsUser = isUser, Content = content });
            while (_messages.Count > MaxHistory) _messages.RemoveAt(0);
            SaveHistory();
        }

        public void ClearHistory()
        {
            _messages.Clear();
            SaveHistory();
        }

        private void LoadHistory()
        {
            try
            {
                var path = Path.Combine(_configDir, HistoryFileName);
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = System.Text.Json.JsonSerializer.Deserialize<List<DeepSeekClient.ChatMessage>>(json);
                    if (loaded != null) _messages.AddRange(loaded);
                    while (_messages.Count > MaxHistory) _messages.RemoveAt(0);
                }
            }
            catch { }
        }

        private void SaveHistory()
        {
            try
            {
                var path = Path.Combine(_configDir, HistoryFileName);
                var json = System.Text.Json.JsonSerializer.Serialize(_messages);
                File.WriteAllText(path, json);
            }
            catch { }
        }

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
                ApiStatus = ApiConnectionStatus.Unknown; // 新客户端需要重新验证
            }
        }

        /// <summary>异步验证 API 连接，更新 ApiStatus</summary>
        private async Task ValidateApiConnectionAsync()
        {
            if (ApiClient == null) return;
            ApiStatus = ApiConnectionStatus.Checking;
            try
            {
                var isValid = await ApiClient.ValidateApiKeyAsync();
                ApiStatus = isValid ? ApiConnectionStatus.Connected : ApiConnectionStatus.Failed;
                System.Diagnostics.Debug.WriteLine($"[MeiChat] API连接验证: {(isValid ? "成功" : "失败")}");
            }
            catch
            {
                ApiStatus = ApiConnectionStatus.Failed;
                System.Diagnostics.Debug.WriteLine($"[MeiChat] API连接验证: 异常");
            }
        }

        // ===== Agent 引擎初始化 =====

        /// <summary>
        /// 初始化 Agent 引擎
        /// 如果 ApiClient 为空但配置中有 API Key，自动重试初始化
        /// </summary>
        public void InitializeAgentEngine()
        {
            // 防御性：ApiClient 被 EndGame 销毁了但配置还在，重新创建
            if (ApiClient == null && Config != null && !string.IsNullOrWhiteSpace(Config.ApiKey))
            {
                try { InitializeApiClient(); }
                catch { /* 静默失败，留给上层处理 */ }
            }

            if (ApiClient == null) return;

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
            //ToolRegistry.Register(new SearchWebTool()); // 联网搜索暂时禁用，需要时可取消注释
            MemoryTool.Manager = Memory;
            ToolRegistry.Register(new MemoryTool());
            ApiStatsTool.Stats = Stats;
            ToolRegistry.Register(new ApiStatsTool());
            ToolRegistry.Register(new PetControlTool(this));
            ToolRegistry.Register(new SetPetNameTool(this));
            ToolRegistry.Register(new ShowHistoryTool(this));
            ReadMemoryTool.Manager = Memory;
            ToolRegistry.Register(new ReadMemoryTool());
            ToolRegistry.Register(new OpenMemoryWindowTool(this));
            ToolRegistry.Register(new SetTtsTool(this));
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
