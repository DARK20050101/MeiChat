using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 读取网页内容（下载 HTML → 提取纯文本）
    /// </summary>
    public class ReadWebTool : ITool
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public string Name => "read_webpage";
        public string Description => "读取指定 URL 的网页内容并转为纯文本。适用于阅读文档、新闻、教程等在线内容。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                url = new
                {
                    type = "string",
                    description = "要读取的网页 URL（完整地址，如 https://example.com/doc）"
                }
            },
            required = new[] { "url" }
        };

        public async Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
                    return ToolResult.Fail("缺少参数: url");

                // URL 校验
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    return ToolResult.Fail($"无效的 URL: {url}。请使用 http:// 或 https:// 开头的地址。");
                }

                // 下载
                var response = await _httpClient.GetAsync(uri, ct);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync(ct);

                // 提取纯文本
                var text = HtmlToPlainText(html);

                // 限制长度
                const int maxLength = 15000;
                if (text.Length > maxLength)
                {
                    text = text[..maxLength] +
                        $"\n\n... [内容较长，仅显示前 {maxLength} 字符]";
                }

                if (string.IsNullOrWhiteSpace(text))
                    return ToolResult.Fail("无法从该网页提取到有效内容");

                return ToolResult.Ok($"📄 {uri.Host} - 页面内容:\n\n{text}");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("请求超时或被取消");
            }
            catch (HttpRequestException ex)
            {
                return ToolResult.Fail($"网络请求失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"读取网页失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将 HTML 转为纯文本（移除标签、保留正文）
        /// </summary>
        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            // 移除 script 和 style 块
            html = Regex.Replace(html, @"<script[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<nav[^>]*>.*?</nav>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // 移除 HTML 标签
            html = Regex.Replace(html, @"<[^>]+>", " ");

            // 解码 HTML 实体
            html = System.Net.WebUtility.HtmlDecode(html);

            // 合并空白
            html = Regex.Replace(html, @"\s+", " ");
            html = Regex.Replace(html, @"\n\s*\n", "\n");

            // 移除空行头尾
            var lines = html.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 20)  // 过滤短行（导航、广告等）
                .ToList();

            return string.Join("\n\n", lines).Trim();
        }
    }
}
