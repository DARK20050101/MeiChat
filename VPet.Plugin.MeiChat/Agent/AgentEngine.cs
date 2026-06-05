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

        /// <summary>
        /// 创建 Agent 引擎
        /// </summary>
        /// <param name="client">DeepSeek API 客户端</param>
        /// <param name="toolRegistry">工具注册中心</param>
        /// <param name="systemPrompt">系统提示词</param>
        /// <param name="maxIterations">最大工具调用轮数</param>
        public AgentEngine(DeepSeekClient client, ToolRegistry toolRegistry, string systemPrompt, int maxIterations = 25)
        {
            _client = client;
            _toolRegistry = toolRegistry;
            _systemPrompt = systemPrompt;
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
