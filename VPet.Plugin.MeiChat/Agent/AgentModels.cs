using System.Text.Json.Serialization;

namespace VPet.Plugin.MeiChat.Agent
{
    /// <summary>
    /// Agent 系统中的消息模型，支持 user/assistant/tool/system 四种角色
    /// </summary>
    public class AgentMessage
    {
        /// <summary>角色: "user" | "assistant" | "tool" | "system"</summary>
        public string Role { get; set; } = "user";

        /// <summary>消息内容（tool 消息或纯文本回复）</summary>
        public string? Content { get; set; }

        /// <summary>Assistant 消息中的工具调用列表</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ToolCallData>? ToolCalls { get; set; }

        /// <summary>Tool 消息关联的工具调用 ID</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }
    }

    /// <summary>
    /// DeepSeek/OpenAI 响应中的工具调用数据
    /// </summary>
    public class ToolCallData
    {
        /// <summary>工具调用 ID（用于关联 tool 结果）</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>工具名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>参数字符串 (JSON)</summary>
        public string Arguments { get; set; } = "{}";

        /// <summary>工具执行结果（由 AgentEngine 填入）</summary>
        [JsonIgnore]
        public string? Result { get; set; }
    }

    /// <summary>
    /// DeepSeek/OpenAI 响应（含工具调用）
    /// </summary>
    public class AgentResponse
    {
        /// <summary>文本回复（无工具调用时）</summary>
        public string? Content { get; set; }

        /// <summary>工具调用列表</summary>
        public List<ToolCallData>? ToolCalls { get; set; }

        /// <summary>结束原因: "stop" | "tool_calls" | "length"</summary>
        public string? FinishReason { get; set; }
    }

    /// <summary>
    /// 工具执行结果
    /// </summary>
    public class ToolResult
    {
        /// <summary>是否执行成功</summary>
        public bool Success { get; set; }

        /// <summary>执行结果文本（传给 AI 继续推理）</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>错误信息（Success=false 时）</summary>
        public string? ErrorMessage { get; set; }

        public static ToolResult Ok(string content) => new() { Success = true, Content = content };

        public static ToolResult Fail(string error) => new() { Success = false, ErrorMessage = error, Content = $"错误: {error}" };
    }

    /// <summary>
    /// Tool 的 JSON Schema 定义（OpenAI 兼容格式）
    /// </summary>
    public class ToolDefinition
    {
        public string Type { get; set; } = "function";
        public FunctionDefinition Function { get; set; } = new();
    }

    public class FunctionDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public object Parameters { get; set; } = new { type = "object", properties = new { }, required = Array.Empty<string>() };
    }
}
