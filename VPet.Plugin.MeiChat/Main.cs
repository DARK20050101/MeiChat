using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VPet_Simulator.Windows.Interface;
using VPet.Plugin.MeiChat.Models;
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
                InitializeApiClient();

                // 尽早注册 TalkBox，让 VPet 启动时就能识别
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

        public void OpenChatWindow()
        {
            if (ApiClient == null)
            {
                MessageBox.Show("请先在设置中配置 API Key",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 查找是否已有聊天窗口
            foreach (var w in MW.Windows)
            {
                if (w is ChatWindow chatWin)
                {
                    chatWin.Activate();
                    return;
                }
            }

            // 创建常驻聊天窗口（不会每轮关闭）
            var window = new ChatWindow(this);
            window.Closed += (s, e) => MW.Windows.Remove(window);
            MW.Windows.Add(window);
            window.Show();
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
                ApiClient = new DeepSeekClient(Config.ApiKey, Config.Model,
                    Config.MaxTokens, Config.Temperature, Config.ApiBaseUrl);
            }
        }

        private void OnConfigSaved(AppConfig newConfig)
        {
            Config = newConfig;
            Config.ConfigDirectory = _configDir;
            Config.Save();
            ReinitializeApiClient();
        }
    }
}
