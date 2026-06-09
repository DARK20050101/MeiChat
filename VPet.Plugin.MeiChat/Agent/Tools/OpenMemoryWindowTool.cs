using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 打开记忆管理窗口 — AI 在用户要求查看/导出记忆时调用
    /// </summary>
    public class OpenMemoryWindowTool : ITool
    {
        private readonly Main _plugin;

        public OpenMemoryWindowTool(Main plugin) => _plugin = plugin;

        public string Name => "open_memory_window";
        public string Description => "打开记忆管理窗口，用户可在其中查看、搜索、删除和导出记忆。当用户说「查看记忆」「导出备份」「管理记忆」「打开记忆窗口」时调用。";

        public object Parameters => new
        {
            type = "object",
            properties = new { },
            required = Array.Empty<string>()
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            var window = _plugin.MW.Dispatcher.Invoke(() => _plugin.OpenMemoryWindow());
            if (window != null)
                return Task.FromResult(ToolResult.Ok("已打开记忆管理窗口，你可以在里面查看、搜索、删除和导出备份~"));
            else
                return Task.FromResult(ToolResult.Fail("记忆系统未初始化，无法打开记忆窗口"));
        }
    }
}
