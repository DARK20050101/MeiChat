using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 联网搜索工具 — 通过 DuckDuckGo 搜索互联网
    /// </summary>
    public class SearchWebTool : ITool
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static SearchWebTool()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        }

        public string Name => "search_web";
        public string Description => "搜索互联网获取最新信息。适用于查询新闻、文档、教程、技术问题等需要联网获取的内容。";

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

        public async Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
                    return ToolResult.Fail("缺少参数: query");

                // 搜索词太长会失败，截断
                if (query.Length > 200) query = query[..200];

                // 使用 DuckDuckGo HTML 搜索
                var encodedQuery = WebUtility.UrlEncode(query);
                var url = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

                var response = await _httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync(ct);

                // 解析搜索结果
                var results = ParseResults(html);

                if (results.Count == 0)
                    return ToolResult.Fail($"未找到「{query}」的相关结果，建议换个关键词试试。");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"🔍 搜索结果: {query}");
                sb.AppendLine();

                for (int i = 0; i < Math.Min(results.Count, 8); i++)
                {
                    var r = results[i];
                    sb.AppendLine($"{i + 1}. {r.Title}");
                    if (!string.IsNullOrEmpty(r.Snippet))
                        sb.AppendLine($"   {r.Snippet}");
                    sb.AppendLine($"   {r.Url}");
                    sb.AppendLine();
                }

                return ToolResult.Ok(sb.ToString().TrimEnd());
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("搜索请求超时，请稍后重试。");
            }
            catch (HttpRequestException ex)
            {
                return ToolResult.Fail($"网络连接失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"搜索出错: {ex.Message}");
            }
        }

        private static List<SearchResult> ParseResults(string html)
        {
            var results = new List<SearchResult>();

            try
            {
                // 查找所有 result 条目
                // DuckDuckGo HTML 结果格式: <a rel="nofollow" class="result__a" href="...">TITLE</a>
                // 下面的 <a class="result__snippet">SNIPPET</a>

                var linkPattern = @"<a[^>]+class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>";
                var snippetPattern = @"<a[^>]+class=""result__snippet""[^>]*>(.*?)</a>";

                var linkMatches = Regex.Matches(html, linkPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var snippetMatches = Regex.Matches(html, snippetPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                for (int i = 0; i < linkMatches.Count; i++)
                {
                    var url = WebUtility.HtmlDecode(linkMatches[i].Groups[1].Value);
                    var title = WebUtility.HtmlDecode(Regex.Replace(linkMatches[i].Groups[2].Value, @"<[^>]+>", ""));
                    var snippet = i < snippetMatches.Count
                        ? WebUtility.HtmlDecode(Regex.Replace(snippetMatches[i].Groups[1].Value, @"<[^>]+>", ""))
                        : "";

                    // 过滤广告和无关结果
                    if (!string.IsNullOrWhiteSpace(title) &&
                        !url.Contains("duckduckgo.com") &&
                        !url.Contains("ad_domain"))
                    {
                        results.Add(new SearchResult
                        {
                            Title = title.Trim(),
                            Snippet = snippet.Trim(),
                            Url = url
                        });
                    }
                }
            }
            catch { /* 解析失败返回已有结果 */ }

            return results;
        }

        private class SearchResult
        {
            public string Title { get; set; } = "";
            public string Snippet { get; set; } = "";
            public string Url { get; set; } = "";
        }
    }
}
