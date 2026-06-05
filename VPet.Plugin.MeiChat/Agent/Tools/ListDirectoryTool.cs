using System.IO;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 列出目录内容
    /// </summary>
    public class ListDirectoryTool : ITool
    {
        public string Name => "list_directory";
        public string Description => "列出指定目录下的文件和子目录。适用于浏览项目结构、查找文件等。不传 path 参数则列出工作目录根目录。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                path = new
                {
                    type = "string",
                    description = "目录路径（相对于工作目录，如 \"src\"；或绝对路径。不传则列出工作目录）"
                },
                depth = new
                {
                    type = "number",
                    description = "递归深度（0=只列当前目录，1=包含子目录，默认0）"
                }
            }
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson);

                var path = args?.TryGetValue("path", out var p) == true ? p?.ToString() : null;
                var depth = args?.TryGetValue("depth", out var d) == true && d is JsonElement je && je.ValueKind == JsonValueKind.Number ? je.GetInt32() : 0;

                var dirPath = string.IsNullOrWhiteSpace(path)
                    ? workingDir
                    : ReadFileTool.ResolvePath(path, workingDir);

                if (!Directory.Exists(dirPath))
                    return Task.FromResult(ToolResult.Fail($"目录不存在: {path ?? dirPath}"));

                var result = BuildDirectoryTree(dirPath, "", depth);

                return Task.FromResult(ToolResult.Ok(result));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Fail($"列出目录失败: {ex.Message}"));
            }
        }

        private static string BuildDirectoryTree(string dirPath, string prefix, int maxDepth)
        {
            var sb = new System.Text.StringBuilder();
            var dir = new DirectoryInfo(dirPath);
            sb.AppendLine($"📁 {dir.Name}/");

            if (maxDepth < 0) return sb.ToString();

            var items = new List<FileSystemInfo>();
            try { items.AddRange(dir.GetDirectories().OrderBy(d => d.Name)); } catch { }
            try { items.AddRange(dir.GetFiles().OrderBy(f => f.Name)); } catch { }

            for (int i = 0; i < items.Count; i++)
            {
                var isLast = i == items.Count - 1;
                var connector = isLast ? "└── " : "├── ";

                if (items[i] is DirectoryInfo subDir)
                {
                    sb.AppendLine($"{prefix}{connector}📁 {subDir.Name}/");
                    if (maxDepth > 0)
                    {
                        var subPrefix = prefix + (isLast ? "    " : "│   ");
                        sb.Append(BuildDirectoryTree(subDir.FullName, subPrefix, maxDepth - 1));
                    }
                }
                else if (items[i] is FileInfo file)
                {
                    var size = FormatFileSize(file.Length);
                    sb.AppendLine($"{prefix}{connector}📄 {file.Name}  ({size})");
                }
            }

            return sb.ToString();
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            return $"{bytes / (1024.0 * 1024.0):F1}MB";
        }
    }
}
