namespace VPet.Plugin.MeiChat.Agent
{
    /// <summary>
    /// 工具注册中心 — 管理所有可用工具，生成 API 所需的 definitions
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new();

        public void Register(ITool tool)
        {
            _tools[tool.Name] = tool;
        }

        /// <summary>
        /// 获取所有工具的 API 定义（直接发给 DeepSeek/OpenAI）
        /// </summary>
        public List<ToolDefinition> GetDefinitions()
        {
            return _tools.Values.Select(t => new ToolDefinition
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.Parameters
                }
            }).ToList();
        }

        /// <summary>
        /// 执行指定名称的工具
        /// </summary>
        public async Task<ToolResult> ExecuteAsync(string name, string arguments, string workingDir, CancellationToken ct)
        {
            if (_tools.TryGetValue(name, out var tool))
            {
                try
                {
                    return await tool.ExecuteAsync(arguments, workingDir, ct);
                }
                catch (OperationCanceledException)
                {
                    return ToolResult.Fail("操作已取消");
                }
                catch (Exception ex)
                {
                    return ToolResult.Fail($"{tool.Name} 执行异常: {ex.Message}");
                }
            }
            return ToolResult.Fail($"未知工具: {name}");
        }

        /// <summary>
        /// 获取指定类型的工具实例
        /// </summary>
        public T? GetTool<T>() where T : class, ITool
        {
            return _tools.Values.OfType<T>().FirstOrDefault();
        }

        public IReadOnlyCollection<ITool> GetAllTools() => _tools.Values;
    }
}
