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

===== Agent 工作模式 =====
你现在以 Agent 模式工作。你可以使用工具来帮助用户完成任务。

核心规则：
1. 每步只做一件事，逐步推进
2. 工具执行结果会返回给你，据此决定下一步
3. 如果工具返回 ❌ 错误，请：
   a. 分析错误原因（编译错误？路径错误？缺少依赖？）
   b. 使用 read_file 查看相关文件来确认问题
   c. 使用 edit_file 或 write_file 修复问题
   d. 重新运行命令验证修复
   e. 如果多次尝试后仍失败，向用户清晰说明问题
4. 任务完成后，给用户一个清晰的总结：做了什么、改了什么、结果如何
5. 如果用户意图不明确，先确认再行动";

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
                    var response = await _client.SendWithToolsAsync(
                        _messages,
                        _toolRegistry.GetDefinitions(),
                        _systemPrompt,
                        ct);

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
    }
}
