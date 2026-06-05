using System.IO;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 在文件中搜索文本/代码模式
    /// 类似 grep，支持按文件扩展名过滤
    /// </summary>
    public class SearchCodeTool : ITool
    {
        public string Name => "search_code";
        public string Description => "在文件中搜索关键词或代码模式。支持按文件扩展名过滤。适用于查找函数定义、引用、特定文本等。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                pattern = new
                {
                    type = "string",
                    description = "搜索关键词（大小写不敏感，支持简单文本匹配，不支持正则）"
                },
                path = new
                {
                    type = "string",
                    description = "搜索范围（相对于工作目录的路径，不传则搜索整个工作目录）"
                },
                include = new
                {
                    type = "string",
                    description = "文件扩展名过滤，如 \".cs\" 或 \".cs,.xaml\"。不传则搜索所有文本文件"
                }
            },
            required = new[] { "pattern" }
        };

        public async Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("pattern", out var pattern) || string.IsNullOrWhiteSpace(pattern))
                    return ToolResult.Fail("缺少参数: pattern");

                var searchPath = args.TryGetValue("path", out var p) && !string.IsNullOrWhiteSpace(p)
                    ? ReadFileTool.ResolvePath(p, workingDir)
                    : workingDir;

                var extensions = args.TryGetValue("include", out var inc) && !string.IsNullOrWhiteSpace(inc)
                    ? inc.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    : null;

                if (!Directory.Exists(searchPath))
                    return ToolResult.Fail($"目录不存在: {p ?? searchPath}");

                var results = new List<string>();
                int fileCount = 0;
                int matchCount = 0;
                const int maxResults = 50;

                foreach (var file in Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();

                    var ext = Path.GetExtension(file).ToLower();
                    if (extensions != null && !extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // 跳过二进制文件和过大文件
                    var binaryExts = new HashSet<string> { ".dll", ".exe", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".zip", ".rar", ".7z", ".raw" };
                    if (binaryExts.Contains(ext)) continue;

                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Length > 1024 * 1024) continue; // 跳过 >1MB 的文件
                        if (fileInfo.Length == 0) continue;

                        using var reader = new StreamReader(file);
                        int lineNum = 0;
                        var relativePath = Path.GetRelativePath(workingDir, file);

                        while (!reader.EndOfStream)
                        {
                            ct.ThrowIfCancellationRequested();
                            var line = await reader.ReadLineAsync(ct);
                            lineNum++;

                            if (line != null && line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                if (matchCount < maxResults)
                                {
                                    var trimmed = line.Trim().Length > 120 ? line.Trim()[..117] + "..." : line.Trim();
                                    results.Add($"{relativePath}:{lineNum}  {trimmed}");
                                }
                                matchCount++;
                            }
                        }
                        if (matchCount > 0) fileCount++;
                    }
                    catch { /* 跳过无法读取的文件 */ }
                }

                var output = new System.Text.StringBuilder();
                output.AppendLine($"🔍 搜索 \"{pattern}\" 结果: {matchCount} 处匹配, {fileCount} 个文件");
                if (results.Count > 0)
                {
                    output.AppendLine();
                    foreach (var r in results)
                        output.AppendLine(r);
                }
                if (matchCount > maxResults)
                    output.AppendLine($"\n... 还有 {matchCount - maxResults} 处匹配未显示");

                return ToolResult.Ok(output.ToString());
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("搜索已取消");
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"搜索失败: {ex.Message}");
            }
        }
    }
}
