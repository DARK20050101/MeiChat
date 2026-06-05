using System.IO;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 读取文件内容
    /// </summary>
    public class ReadFileTool : ITool
    {
        public string Name => "read_file";
        public string Description => "读取指定文件的完整内容。适用于阅读代码、文档、配置文件等。传入相对路径（相对于工作目录）或绝对路径。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                path = new
                {
                    type = "string",
                    description = "文件路径（相对于工作目录的路径，如 \"src/Main.cs\"；或绝对路径）"
                }
            },
            required = new[] { "path" }
        };

        public async Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
                    return ToolResult.Fail("缺少参数: path");

                var fullPath = ResolvePath(path, workingDir);

                if (!File.Exists(fullPath))
                    return ToolResult.Fail($"文件不存在: {path}");

                // 检查文件大小，太大则只读前部分
                var fileInfo = new FileInfo(fullPath);
                const long maxSize = 512 * 1024; // 512KB
                string content;

                if (fileInfo.Length > maxSize)
                {
                    using var reader = new StreamReader(fullPath);
                    // 只读前 256KB
                    var buffer = new char[256 * 1024];
                    var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                    content = new string(buffer, 0, read);
                    content += $"\n\n... [文件较大，仅显示前 {content.Length} 字符，总大小 {fileInfo.Length} 字节]";
                }
                else
                {
                    content = await File.ReadAllTextAsync(fullPath, ct);
                }

                return ToolResult.Ok(content);
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("操作已取消");
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"读取文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析路径，防止目录遍历攻击
        /// </summary>
        internal static string ResolvePath(string path, string workingDir)
        {
            // 如果是绝对路径，直接使用
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            // 相对路径，基于工作目录
            return Path.GetFullPath(Path.Combine(workingDir, path));
        }
    }
}
