using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 语音开关工具 — AI 可主动开启/关闭语音朗读
    /// </summary>
    public class SetTtsTool : ITool
    {
        private readonly Main _plugin;

        public SetTtsTool(Main plugin) => _plugin = plugin;

        public string Name => "set_tts";
        public string Description => "开启或关闭语音朗读功能。当用户说「开口说话」「说两句」时开启语音，说「闭嘴」「别说话了」「安静」时关闭语音。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                enabled = new
                {
                    type = "boolean",
                    description = "true = 开启语音朗读，false = 关闭"
                }
            },
            required = new[] { "enabled" }
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson);
                if (args == null || !args.TryGetValue("enabled", out var val))
                    return Task.FromResult(ToolResult.Fail("缺少参数: enabled"));

                bool enabled = val is bool b ? b : bool.TryParse(val?.ToString(), out var r) && r;

                if (_plugin.Tts != null)
                {
                    _plugin.Tts.Enabled = enabled;
                }
                _plugin.Config.TtsEnabled = enabled;
                _plugin.Config.Save();

                if (enabled)
                    return Task.FromResult(ToolResult.Ok("✅ 语音已开启，现在开始我会把回答读出来~"));
                else
                    return Task.FromResult(ToolResult.Ok("🔇 语音已关闭"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Fail($"操作失败: {ex.Message}"));
            }
        }
    }
}
