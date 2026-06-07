namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 打开长聊天框展示历史记录
    /// </summary>
    public class ShowHistoryTool : ITool
    {
        private readonly Main _plugin;

        public ShowHistoryTool(Main plugin) => _plugin = plugin;

        public string Name => "show_chat_history";
        public string Description => "打开长聊天框展示历史对话记录。当用户问「之前聊了什么」「聊天记录」「历史消息」时调用。";

        public object Parameters => new
        {
            type = "object",
            properties = new { },
            required = Array.Empty<string>()
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            _plugin.MW.Dispatcher.Invoke(() => _plugin.OpenChatWindow());
            return Task.FromResult(ToolResult.Ok("已打开历史聊天框，你可以往上滚动查看之前的对话。"));
        }
    }
}
