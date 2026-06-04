using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VPet_Simulator.Windows.Interface;

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

                if (cmd == "/ui" || cmd == "/window")
                {
                    _plugin.MW.Dispatcher.Invoke(() => _plugin.OpenChatWindow());
                    return;
                }
                if (cmd == "/clear")
                {
                    _plugin.ClearHistory();
                    _plugin.MW.Main.Say("✅ 已清空对话历史");
                    return;
                }
                if (cmd == "/help")
                {
                    _plugin.MW.Main.Say("📋 可用指令：\n/ui - 打开高级窗口\n/clear - 清空历史\n/help - 本帮助");
                    return;
                }

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
            catch (Exception ex)
            {
                _plugin.MW.Main.Say($"⚠️ {ex.Message}");
            }
        }
    }
}
