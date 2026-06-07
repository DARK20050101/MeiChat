using System.Net;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 联网搜索工具 — 生成搜索链接 + 用 read_webpage 读取结果
    /// </summary>
    public class SearchWebTool : ITool
    {
        public string Name => "search_web";
        public string Description => "搜索互联网获取最新信息。输入关键词后，我会生成搜索链接，你可以让我用 read_webpage 打开查看具体内容。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                query = new
                {
                    type = "string",
                    description = "搜索关键词，尽量简洁准确，如「C# async await 用法」"
                }
            },
            required = new[] { "query" }
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
                    return Task.FromResult(ToolResult.Fail("缺少参数: query"));

                if (query.Length > 200) query = query[..200];

                var encoded = WebUtility.UrlEncode(query);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"搜索: {query}");
                sb.AppendLine();
                sb.AppendLine("请在以下选择一个搜索链接，用 read_webpage 打开查看结果：");
                sb.AppendLine();
                sb.AppendLine($"1. 百度搜索: https://www.baidu.com/s?wd={encoded}");
                sb.AppendLine($"2. Bing搜索: https://www.bing.com/search?q={encoded}");
                sb.AppendLine($"3. Google搜索: https://www.google.com/search?q={encoded}");
                sb.AppendLine();
                sb.AppendLine("比如你想看百度结果，就说「打开百度搜索的那个链接看看」。");

                return Task.FromResult(ToolResult.Ok(sb.ToString().TrimEnd()));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Fail($"搜索出错: {ex.Message}"));
            }
        }
    }
}
