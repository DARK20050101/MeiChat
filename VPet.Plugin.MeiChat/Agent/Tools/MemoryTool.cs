using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 记忆工具 — AI 可主动记住或遗忘信息
    /// </summary>
    public class MemoryTool : ITool
    {
        public static Memory.MemoryManager? Manager { get; set; }

        public string Name => "remember";
        public string Description => "记住一条重要信息，下次启动后仍会记得。适用于：用户偏好、项目信息、重要决定等。如果要删除已记住的信息，使用 type=forget。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                type = new
                {
                    type = "string",
                    description = "信息类别: preference（偏好）| project（项目）| fact（事实）| user_info（用户信息）| forget（删除记忆）"
                },
                content = new
                {
                    type = "string",
                    description = "要记住的内容（当 type=forget 时，此参数为要删除的关键词）"
                }
            },
            required = new[] { "type", "content" }
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null) return Task.FromResult(ToolResult.Fail("无法解析参数"));

                if (!args.TryGetValue("type", out var type) || string.IsNullOrWhiteSpace(type))
                    return Task.FromResult(ToolResult.Fail("缺少参数: type"));

                if (!args.TryGetValue("content", out var content) || string.IsNullOrWhiteSpace(content))
                    return Task.FromResult(ToolResult.Fail("缺少参数: content"));

                if (Manager == null)
                    return Task.FromResult(ToolResult.Fail("记忆系统未初始化"));

                if (type == "forget")
                {
                    Manager.Forget(content);
                    return Task.FromResult(ToolResult.Ok($"✅ 已忘记与「{content}」相关的记忆"));
                }

                Manager.AddFact(type, content);
                return Task.FromResult(ToolResult.Ok($"✅ 已记住: [{type}] {content}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Fail($"记忆操作失败: {ex.Message}"));
            }
        }
    }
}
