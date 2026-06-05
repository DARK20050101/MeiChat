using System.IO;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 精准替换文件中的文本（类似 Claude Code 的编辑能力）
    /// 通过 old_string 定位，替换为 new_string
    /// </summary>
    public class EditFileTool : ITool
    {
        public string Name => "edit_file";
        public string Description => "对已有文件进行精准文本替换。需要提供 old_string（文件中要替换的原文，必须唯一）和 new_string（替换后的新文本）。适用于修改代码、配置文件等。不要用于创建新文件（请用 write_file）。";

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
                old_string = new
                {
                    type = "string",
                    description = "要被替换的原文（必须在文件中出现且仅出现一次，包含完整上下文以确保唯一匹配）"
                },
                new_string = new
                {
                    type = "string",
                    description = "替换后的新文本"
                }
            },
            required = new[] { "path", "old_string", "new_string" }
        };

        public async Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null) return ToolResult.Fail("无法解析参数");

                if (!args.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
                    return ToolResult.Fail("缺少参数: path");
                if (!args.TryGetValue("old_string", out var oldStr) || string.IsNullOrWhiteSpace(oldStr))
                    return ToolResult.Fail("缺少参数: old_string");
                if (!args.TryGetValue("new_string", out var newStr))
                    newStr = "";

                var fullPath = ReadFileTool.ResolvePath(path, workingDir);

                if (!File.Exists(fullPath))
                    return ToolResult.Fail($"文件不存在: {path}");

                var content = await File.ReadAllTextAsync(fullPath, ct);

                // 检查 old_string 出现次数
                int index = content.IndexOf(oldStr, StringComparison.Ordinal);
                if (index < 0)
                    return ToolResult.Fail($"在文件中未找到要替换的文本。请确保 old_string 与文件内容完全匹配（包括缩进和换行）。");

                int lastIndex = content.LastIndexOf(oldStr, StringComparison.Ordinal);
                if (index != lastIndex)
                    return ToolResult.Fail($"old_string 在文件中出现多次 ({CountOccurrences(content, oldStr)} 次)。请增加更多上下文以确保唯一匹配。");

                content = content.Replace(oldStr, newStr);
                await File.WriteAllTextAsync(fullPath, content, ct);

                return ToolResult.Ok($"✅ 已修改文件: {path}（替换了 {oldStr.Length} 字符 → {newStr.Length} 字符）");
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("操作已取消");
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"编辑文件失败: {ex.Message}");
            }
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0, pos = 0;
            while ((pos = text.IndexOf(pattern, pos, StringComparison.Ordinal)) >= 0)
            {
                count++;
                pos += pattern.Length;
            }
            return count;
        }
    }
}
