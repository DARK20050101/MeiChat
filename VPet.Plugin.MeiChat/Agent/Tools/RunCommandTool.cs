using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 执行 Shell 命令
    /// 默认需要用户确认，AutoMode 下跳过确认（删除/危险操作仍需确认）
    /// </summary>
    public class RunCommandTool : ITool
    {
        /// <summary>
        /// 外部设置：是否需要用户确认执行命令
        /// 返回 true = 允许执行，false = 拒绝
        /// 参数: (command, isDestructive)
        /// </summary>
        public static Func<string, bool, bool>? RequestConfirmation { get; set; }

        public string Name => "run_command";
        public string Description => "在系统终端中执行 shell 命令。适用于运行构建、测试、git 操作等。注意：此工具会实际执行命令，请谨慎使用。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                command = new
                {
                    type = "string",
                    description = "要执行的命令（如 \"dotnet build\"、\"git status\"、\"dir\" 等）"
                },
                description = new
                {
                    type = "string",
                    description = "执行此命令的目的说明（帮助用户理解为什么要执行这条命令）"
                }
            },
            required = new[] { "command" }
        };

        /// <summary>
        /// 判断是否为危险/破坏性命令
        /// </summary>
        public static bool IsDestructiveCommand(string command)
        {
            var lower = command.Trim().ToLowerInvariant();

            // 删除类操作
            string[] destructivePatterns =
            {
                "del ", "rm ", "rmdir ", "rd ", "remove-", "remove ",
                "format", "diskpart", "clean",
                "git push --force", "git reset --hard", "git branch -d", "git branch -D",
                "drop ", "truncate ", "delete ",
                ">nul", "> nul",
                "shutdown", "restart-", "stop-",
            };

            return destructivePatterns.Any(p => lower.Contains(p));
        }

        public async Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
                    return ToolResult.Fail("缺少参数: command");

                args.TryGetValue("description", out var description);

                var isDestructive = IsDestructiveCommand(command);

                // 请求用户确认（如果设置了回调）
                if (RequestConfirmation != null)
                {
                    var allowed = RequestConfirmation.Invoke(
                        string.IsNullOrEmpty(description) ? command : $"{description}\n\n命令: {command}",
                        isDestructive);

                    if (!allowed)
                        return ToolResult.Ok("🛑 用户取消了命令执行");
                }

                // 执行命令
                var (output, exitCode) = await RunProcessAsync(command, workingDir, ct);

                // 限制输出长度
                const int maxOutputLength = 10000;
                if (output.Length > maxOutputLength)
                {
                    output = output[..maxOutputLength] +
                        $"\n\n... [输出过长，仅显示前 {maxOutputLength} 字符]";
                }

                if (exitCode != 0)
                {
                    return ToolResult.Fail($"命令执行失败（退出码: {exitCode}）：\n{output}");
                }

                return ToolResult.Ok(output);
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail("命令执行已取消");
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"命令执行失败: {ex.Message}");
            }
        }

        private static async Task<(string output, int exitCode)> RunProcessAsync(string command, string workingDir, CancellationToken ct)
        {
            var sb = new StringBuilder();

            // 检测操作系统，选择合适的 shell
            bool isWindows = OperatingSystem.IsWindows();
            string fileName = isWindows ? "cmd.exe" : "/bin/bash";
            string arguments = isWindows ? $"/c \"{command}\"" : $"-c \"{command.Replace("\"", "\\\"")}\"";

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = psi };

            // 异步读取标准输出
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (sb) { sb.AppendLine(e.Data); }
                }
            };

            // 异步读取标准错误
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (sb) { sb.AppendLine($"[stderr] {e.Data}"); }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 等待进程退出（支持取消）
            await process.WaitForExitAsync(ct);

            var exitCode = process.ExitCode;
            var output = sb.ToString().TrimEnd();

            var result = new StringBuilder();
            result.AppendLine($"$ {command}");
            if (!string.IsNullOrEmpty(output))
                result.AppendLine(output);
            result.AppendLine($"\n[退出代码: {exitCode}]");

            return (result.ToString(), exitCode);
        }
    }
}
