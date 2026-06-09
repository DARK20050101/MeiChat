using System.Text;
using System.Text.Json;
using VPet.Plugin.MeiChat.Memory;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 读取记忆工具 — AI 可调用来查看自己记住了什么
    /// </summary>
    public class ReadMemoryTool : ITool
    {
        public static MemoryManager? Manager { get; set; }

        public string Name => "read_memories";
        public string Description => "查看芽衣记住的所有信息。当用户问「你记得什么」「你有什么记忆」「我让你记住了什么」「把记住的东西整理一下」时调用，返回完整的记忆列表。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                keyword = new
                {
                    type = "string",
                    description = "可选：筛选关键词，只显示包含该词的记忆"
                }
            },
            required = Array.Empty<string>()
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                if (Manager == null)
                    return Task.FromResult(ToolResult.Fail("记忆系统未初始化"));

                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                var keyword = args?.TryGetValue("keyword", out var k) == true ? k : "";

                List<MemoryFact> facts;
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    facts = Manager.Search(keyword);
                    if (facts.Count == 0)
                        return Task.FromResult(ToolResult.Ok($"🔍 没有找到与「{keyword}」相关的记忆"));
                }
                else
                {
                    facts = Manager.GetAllFacts();
                    if (facts.Count == 0)
                        return Task.FromResult(ToolResult.Ok("📭 我还没有记住任何东西哦~"));
                }

                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(keyword))
                    sb.AppendLine($"🔍 关于「{keyword}」的记忆：\n");
                else
                    sb.AppendLine("🧠 我记住的事情：\n");

                foreach (var f in facts.OrderByDescending(x => x.Timestamp))
                {
                    var typeLabel = f.Type switch
                    {
                        "preference" => "💙偏好",
                        "project" => "💚项目",
                        "fact" => "💜事实",
                        "schedule" => "🧡作息",
                        "user_info" => "❤️用户",
                        "interaction" => "🩶互动",
                        _ => $"🔘{f.Type}"
                    };
                    sb.AppendLine($"{typeLabel} | {f.Content}（{f.Timestamp:MM-dd HH:mm}）");
                }
                sb.AppendLine($"\n共 {facts.Count} 条记忆");
                sb.AppendLine("你可以告诉我「忘记xxx」来删除某条记忆，或说「记住xxx」让我记住新东西。");

                return Task.FromResult(ToolResult.Ok(sb.ToString().TrimEnd()));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Fail($"读取记忆失败: {ex.Message}"));
            }
        }
    }
}
