namespace VPet.Plugin.MeiChat.Agent
{
    /// <summary>
    /// Agent 工具接口 — 所有工具必须实现此接口
    /// </summary>
    public interface ITool
    {
        /// <summary>工具名称（供 AI 调用，如 "read_file"）</summary>
        string Name { get; }

        /// <summary>工具描述（帮助 AI 理解何时使用此工具）</summary>
        string Description { get; }

        /// <summary>
        /// 参数 JSON Schema 定义（OpenAI 兼容格式）
        /// 返回值直接序列化为 tools 数组中的 parameters 字段
        /// </summary>
        object Parameters { get; }

        /// <summary>
        /// 执行工具
        /// </summary>
        /// <param name="argumentsJson">AI 传入的参数 (JSON 字符串)</param>
        /// <param name="workingDir">当前工作目录</param>
        /// <param name="ct">取消令牌</param>
        Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct);
    }
}
