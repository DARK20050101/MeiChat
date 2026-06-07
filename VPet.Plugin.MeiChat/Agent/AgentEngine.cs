using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent
{
    /// <summary>
    /// Agent 核心引擎 — 实现 Think → Act → Observe 循环
    ///
    /// 工作流程:
    /// 1. 接收用户输入，拼接系统提示 + 消息历史 + tools 定义
    /// 2. 调用 DeepSeek API
    /// 3. 解析响应:
    ///    a. 有 tool_calls → 执行工具 → 结果送回 API → 回到步骤 2
    ///    b. 纯文本回复 → 返回给用户
    /// 4. 达到最大迭代次数则中断
    /// </summary>
    public class AgentEngine
    {
        private readonly DeepSeekClient _client;
        private readonly ToolRegistry _toolRegistry;
        private readonly string _systemPrompt;
        private readonly int _maxIterations;

        /// <summary>Agent 的消息历史</summary>
        private readonly List<AgentMessage> _messages = new();

        /// <summary>是否启用自动模式（静默执行，跳过确认弹窗）</summary>
        public bool AutoMode { get; set; } = false;

        /// <summary>
        /// 当工具调用执行时触发，用于外部向用户显示进度
        /// 参数: (toolName, arguments)
        /// </summary>
        public event Action<string, string>? OnToolExecution;

        /// <summary>
        /// 当 Agent 生成文本回复时触发（非工具调用的中间思考）
        /// </summary>
        public event Action<string>? OnThinking;

        /// <summary>当前工作目录</summary>
        public string WorkingDirectory { get; set; } = "";

        // Agent 行为指令（附加到用户自定义的系统提示词之后）
        private static readonly string AgentBehaviorInstructions = @"

===== 工作方式 =====
你可以使用工具处理复杂任务，也可以只用对话来日常聊天。
AI 会自动判断：简单聊天直接回复，复杂任务调用工具完成。

回复要求：
- ❌ 禁止使用 Markdown 格式（不要用 **、##、``` 等符号）
- ❌ 禁止使用列表符号（-、1. 等）
- 使用纯文本、自然的口语表达，语言简洁明了
- 需要分段时用换行分隔即可

何时使用工具：
- 日常聊天 → 直接对话，不要调用工具
- 用户问项目、代码、文件 → 调用 list_directory / read_file 等了解情况
- 用户要求创建或修改文件 → 调用 write_file / edit_file
- 用户要求编译、测试、运行 → 调用 run_command
- 用户给了网址 → 用 read_webpage 读取内容，给用户总结要点
- 工具报错 → 分析原因、修复、重试，多次失败则向用户说明
- 任务完成后，总结做了什么、结果如何
- 回到日常聊天状态";

        /// <summary>
        /// 创建 Agent 引擎
        /// </summary>
        /// <param name="client">DeepSeek API 客户端</param>
        /// <param name="toolRegistry">工具注册中心</param>
        /// <param name="systemPrompt">用户自定义系统提示词</param>
        /// <param name="workingDirectory">当前工作目录（用于告知 AI 文件操作基准路径）</param>
        /// <param name="maxIterations">最大工具调用轮数</param>
        public AgentEngine(DeepSeekClient client, ToolRegistry toolRegistry, string systemPrompt, string workingDirectory = "", int maxIterations = 25)
        {
            _client = client;
            _toolRegistry = toolRegistry;

            // 追加 Agent 行为指令
            var fullPrompt = systemPrompt + AgentBehaviorInstructions;

            // 告知 AI 当前工作目录（非空时）
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                var defaultDir = "桌面"; // 兜底
                try { defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); } catch { }

                fullPrompt += $@"

===== 工作目录 =====
当前工作目录: {workingDirectory}
默认工作目录: {defaultDir}
- 读写文件时不指定路径则默认在此目录下操作
- 可使用 list_directory 查看目录内容
- 可使用相对路径（如 ""src/Main.cs""）
- 如果用户指定了其他路径，优先使用用户指定的路径
- ⚠️ 重要：当用户询问工作目录时，必须如实回复以上路径，不得编造
- 如果用户要求恢复默认工作目录，请告知用户可以输入 /reset-workdir 指令";
            }

            _systemPrompt = fullPrompt;
            _maxIterations = maxIterations;
        }

        /// <summary>
        /// 执行 Agent 任务
        /// </summary>
        /// <param name="userInput">用户输入</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>Agent 最终回复文本</returns>
        public async Task<string> ExecuteAsync(string userInput, CancellationToken ct = default)
        {
            try
            {
                // 添加用户消息
                _messages.Add(new AgentMessage
                {
                    Role = "user",
                    Content = userInput
                });

                for (int iteration = 0; iteration < _maxIterations; iteration++)
                {
                    ct.ThrowIfCancellationRequested();

                    // 调用 DeepSeek API
                    Agent.AgentResponse response;
                    try
                    {
                        response = await _client.SendWithToolsAsync(
                            _messages,
                            _toolRegistry.GetDefinitions(),
                            _systemPrompt,
                            ct);
                    }
                    catch (Exception ex)
                    {
                        // API 调用失败，清理本轮加入的消息，恢复引擎状态
                        CleanupLastTurn();
                        return $"抱歉，调用 AI 时出了点问题: {ex.Message}。你可以重新说一遍，我会重新处理。";
                    }

                    // 处理回复
                    if (response.ToolCalls != null && response.ToolCalls.Count > 0)
                    {
                        // 有工具调用 → 先记录 assistant 消息
                        var assistantMsg = new AgentMessage
                        {
                            Role = "assistant",
                            Content = response.Content,
                            ToolCalls = response.ToolCalls
                        };
                        _messages.Add(assistantMsg);

                        // 如果有中间思考文本，通知外部
                        if (!string.IsNullOrWhiteSpace(response.Content))
                        {
                            OnThinking?.Invoke(response.Content);
                        }

                        // 逐个执行工具
                        foreach (var toolCall in response.ToolCalls)
                        {
                            ct.ThrowIfCancellationRequested();

                            OnToolExecution?.Invoke(toolCall.Name, toolCall.Arguments);

                            // 执行工具
                            var toolResult = await _toolRegistry.ExecuteAsync(
                                toolCall.Name,
                                toolCall.Arguments,
                                WorkingDirectory,
                                ct);

                            toolCall.Result = toolResult.Success ? toolResult.Content : toolResult.ErrorMessage;

                            // 将工具结果加入消息历史
                            _messages.Add(new AgentMessage
                            {
                                Role = "tool",
                                ToolCallId = toolCall.Id,
                                Content = toolResult.Success ? toolResult.Content : toolResult.ErrorMessage
                            });
                        }

                        // 继续循环，让 AI 处理工具结果
                    }
                    else
                    {
                        // 纯文本回复 → 记录并返回
                        var finalText = response.Content ?? "";
                        _messages.Add(new AgentMessage
                        {
                            Role = "assistant",
                            Content = finalText
                        });
                        return finalText;
                    }
                }

                // 达到最大迭代次数
                return "我已经尝试了很多步但任务似乎还未完成。可能需要你提供更多信息或调整方向。请问还需要我继续吗？";
            }
            catch (OperationCanceledException)
            {
                return "好的，任务已取消。随时可以再找我。";
            }
            catch (Exception ex)
            {
                return $"抱歉，执行过程中出现错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 重置 Agent 对话历史
        /// </summary>
        public void ClearHistory()
        {
            _messages.Clear();
        }

        /// <summary>
        /// 获取当前消息历史（用于调试或展示）
        /// </summary>
        public IReadOnlyList<AgentMessage> GetHistory() => _messages.AsReadOnly();

        /// <summary>
        /// 发生错误时完全清空消息历史，避免消息格式错误导致连环失败
        /// </summary>
        private void CleanupLastTurn()
        {
            _messages.Clear();
        }
    }
}
