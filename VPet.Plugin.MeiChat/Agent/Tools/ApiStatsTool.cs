namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// API 统计查询工具
    /// </summary>
    public class ApiStatsTool : ITool
    {
        public static ApiStats? Stats { get; set; }

        public string Name => "get_stats";
        public string Description => "查看当前的 API 使用统计：token 消耗、缓存命中率、请求次数等。";

        public object Parameters => new
        {
            type = "object",
            properties = new { },
            required = Array.Empty<string>()
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            if (Stats == null)
                return Task.FromResult(ToolResult.Fail("统计系统未初始化"));

            return Task.FromResult(ToolResult.Ok(Stats.GetSummary()));
        }
    }
}
