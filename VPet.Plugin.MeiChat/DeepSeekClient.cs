using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VPet.Plugin.MeiChat
{
    /// <summary>
    /// OpenAI 兼容 API 客户端（支持 DeepSeek / OpenAI / 国内大模型等）
    /// </summary>
    public class DeepSeekClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _maxTokens;
        private readonly double _temperature;
        private readonly string _baseUrl;

        /// <summary>
        /// 创建 API 客户端
        /// </summary>
        /// <param name="apiKey">API Key</param>
        /// <param name="model">模型名称</param>
        /// <param name="maxTokens">最大 Token 数</param>
        /// <param name="temperature">温度</param>
        /// <param name="baseUrl">API 地址（默认 DeepSeek）</param>
        public DeepSeekClient(string apiKey, string model = "deepseek-chat",
                              int maxTokens = 4096, double temperature = 0.7,
                              string baseUrl = "https://api.deepseek.com/v1")
        {
            _apiKey = apiKey;
            _model = model;
            _maxTokens = maxTokens;
            _temperature = temperature;
            _baseUrl = baseUrl.TrimEnd('/') + "/chat/completions";
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// 发送消息并获取完整响应（非流式）
        /// </summary>
        public async Task<string> SendMessageAsync(
            List<ChatMessage> messages,
            string? systemPrompt = null,
            CancellationToken ct = default)
        {
            var requestBody = BuildRequestBody(messages, systemPrompt);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_baseUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                return message.GetProperty("content").GetString() ?? "";
            }

            return "";
        }

        /// <summary>
        /// 流式发送消息，每收到一块内容就调用 onContent 回调
        /// </summary>
        public async Task SendMessageStreamAsync(
            List<ChatMessage> messages,
            Action<string> onContent,
            Action<string> onFinish,
            Action<string> onError,
            string? systemPrompt = null,
            CancellationToken ct = default)
        {
            try
            {
                var requestBody = BuildRequestBody(messages, systemPrompt, stream: true);
                var json = JsonSerializer.Serialize(requestBody, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    // SSE 格式: data: {...}
                    if (!line.StartsWith("data: ")) continue;

                    var data = line[6..];

                    // 流结束标记
                    if (data == "[DONE]") break;

                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;

                        var choices = root.GetProperty("choices");
                        if (choices.GetArrayLength() == 0) continue;

                        var delta = choices[0].GetProperty("delta");

                        // 检查 finish_reason
                        if (choices[0].TryGetProperty("finish_reason", out var finishReason) &&
                            finishReason.ValueKind != JsonValueKind.Null)
                        {
                            onFinish(finishReason.GetString() ?? "stop");
                        }

                        // 提取内容增量
                        if (delta.TryGetProperty("content", out var contentToken) &&
                            contentToken.ValueKind == JsonValueKind.String)
                        {
                            onContent(contentToken.GetString() ?? "");
                        }
                    }
                    catch (JsonException)
                    {
                        // 跳过无法解析的行
                    }
                }

                onFinish("stop");
            }
            catch (OperationCanceledException)
            {
                onError("请求已取消");
            }
            catch (HttpRequestException ex)
            {
                onError($"网络错误: {ex.Message}");
            }
            catch (JsonException ex)
            {
                onError($"响应解析错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                onError($"未知错误: {ex.Message}");
            }
        }

        // ===== Agent 工具调用支持 =====

        /// <summary>
        /// 带工具定义的消息发送（非流式，支持 tool_calls）
        /// </summary>
        public async Task<Agent.AgentResponse> SendWithToolsAsync(
            List<Agent.AgentMessage> messages,
            List<Agent.ToolDefinition>? tools,
            string? systemPrompt = null,
            CancellationToken ct = default)
        {
            var requestBody = BuildToolRequestBody(messages, tools, systemPrompt);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_baseUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            return ParseAgentResponse(responseJson);
        }

        /// <summary>
        /// 解析包含 tool_calls 的 API 响应
        /// </summary>
        private static Agent.AgentResponse ParseAgentResponse(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            var choices = root.GetProperty("choices");

            if (choices.GetArrayLength() == 0)
                return new Agent.AgentResponse { Content = "" };

            var choice = choices[0];
            var message = choice.GetProperty("message");

            // 提取文本内容（可能为 null）
            string? content = null;
            if (message.TryGetProperty("content", out var contentToken) &&
                contentToken.ValueKind == JsonValueKind.String)
            {
                content = contentToken.GetString();
            }

            // 提取结束原因
            string? finishReason = null;
            if (choice.TryGetProperty("finish_reason", out var reasonToken) &&
                reasonToken.ValueKind == JsonValueKind.String)
            {
                finishReason = reasonToken.GetString();
            }

            // 提取工具调用
            List<Agent.ToolCallData>? toolCalls = null;
            if (message.TryGetProperty("tool_calls", out var tcToken) &&
                tcToken.ValueKind == JsonValueKind.Array)
            {
                toolCalls = new List<Agent.ToolCallData>();
                foreach (var call in tcToken.EnumerateArray())
                {
                    var func = call.GetProperty("function");
                    toolCalls.Add(new Agent.ToolCallData
                    {
                        Id = call.GetProperty("id").GetString() ?? "",
                        Name = func.GetProperty("name").GetString() ?? "",
                        Arguments = func.GetProperty("arguments").GetString() ?? "{}"
                    });
                }
            }

            return new Agent.AgentResponse
            {
                Content = content,
                ToolCalls = toolCalls,
                FinishReason = finishReason
            };
        }

        /// <summary>
        /// 构建带 tools 参数的请求体
        /// </summary>
        private object BuildToolRequestBody(
            List<Agent.AgentMessage> messages,
            List<Agent.ToolDefinition>? tools,
            string? systemPrompt,
            bool stream = false)
        {
            var apiMessages = new List<object>();

            // 系统提示词
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                apiMessages.Add(new { role = "system", content = systemPrompt });
            }

            // 消息列表（支持 user/assistant/tool 角色）
            foreach (var msg in messages)
            {
                switch (msg.Role)
                {
                    case "tool":
                        apiMessages.Add(new
                        {
                            role = "tool",
                            tool_call_id = msg.ToolCallId ?? "",
                            content = msg.Content ?? ""
                        });
                        break;

                    case "assistant" when msg.ToolCalls?.Count > 0:
                        // Assistant 消息 + tool_calls
                        var assistantDict = new Dictionary<string, object?>
                        {
                            ["role"] = "assistant",
                            ["content"] = msg.Content  // 可能为 null
                        };

                        var calls = msg.ToolCalls.Select(tc => new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new
                            {
                                name = tc.Name,
                                arguments = tc.Arguments
                            }
                        }).ToArray();

                        assistantDict["tool_calls"] = calls;
                        apiMessages.Add(assistantDict);
                        break;

                    default:
                        apiMessages.Add(new
                        {
                            role = msg.Role,
                            content = msg.Content ?? ""
                        });
                        break;
                }
            }

            var body = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["max_tokens"] = _maxTokens,
                ["temperature"] = _temperature,
                ["messages"] = apiMessages
            };

            // 添加工具定义
            if (tools != null && tools.Count > 0)
            {
                body["tools"] = tools.Select(t => new
                {
                    type = "function",
                    function = new
                    {
                        name = t.Function.Name,
                        description = t.Function.Description,
                        parameters = t.Function.Parameters
                    }
                }).ToArray();
            }

            if (stream)
            {
                body["stream"] = true;
            }

            return body;
        }

        /// <summary>
        /// 检查 API Key 是否有效
        /// </summary>
        public async Task<bool> ValidateApiKeyAsync()
        {
            try
            {
                var requestBody = new
                {
                    model = _model,
                    max_tokens = 10,
                    messages = new[] { new { role = "user", content = "hi" } }
                };
                var json = JsonSerializer.Serialize(requestBody, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_baseUrl, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private object BuildRequestBody(List<ChatMessage> messages, string? systemPrompt, bool stream = false)
        {
            var apiMessages = new List<object>();

            // 系统提示词
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                apiMessages.Add(new { role = "system", content = systemPrompt });
            }

            // 对话历史
            foreach (var msg in messages)
            {
                apiMessages.Add(new
                {
                    role = msg.IsUser ? "user" : "assistant",
                    content = msg.Content
                });
            }

            var body = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["max_tokens"] = _maxTokens,
                ["temperature"] = _temperature,
                ["messages"] = apiMessages
            };

            if (stream)
            {
                body["stream"] = true;
            }

            return body;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// 用于消息列表传输的简化消息类
        /// </summary>
        public class ChatMessage
        {
            public bool IsUser { get; set; }
            public string Content { get; set; } = "";
        }
    }
}
