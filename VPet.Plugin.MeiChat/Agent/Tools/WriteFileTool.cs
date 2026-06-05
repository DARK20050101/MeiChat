using System.IO;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 创建或覆盖文件
    /// </summary>
    public class WriteFileTool : ITool
    {
        public string Name => "write_file";
        public string Description => "创建新文件或覆盖已有文件。适用于生成代码、创建文档等。注意：此操作会直接覆盖已有文件！";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                path = new
                {
                    type = "string",
                    description = "文件路径（相对于工作目录）"
                },
                content = new
                {
                    type = "string",
                    description = "文件内容（文本格式）"
                }
            },
            required = new[] { "path", "content" }
        };

        public async Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
                    return ToolResult.Fail("缺少参数: path");
                if (!args.TryGetValue("content", out var content))
                    content = "";

                var fullPath = ReadFileTool.ResolvePath(path, workingDir);
                var dir = Path.GetDirectoryName(fullPath);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 检查是否为二进制文件
                var ext = Path.GetExtension(fullPath).ToLower();
                var binaryExtensions = new HashSet<string> { ".dll", ".exe", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".zip", ".rar", ".7z", ".pdf", ".raw" };

                if (binaryExtensions.Contains(ext))
                    return ToolResult.Fail($"不支持写入二进制文件类型: {ext}。请使用文本文件。");

                await File.WriteAllTextAsync(fullPath, content, ct);

                return ToolResult.Ok($"✅ 已写入文件: {path} ({content.Length} 字符)");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("操作已取消");
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"写入文件失败: {ex.Message}");
            }
        }
    }
}
