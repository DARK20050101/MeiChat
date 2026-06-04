using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VPet.Plugin.MeiChat
{
    /// <summary>
    /// Claude API 客户端 - 调用 Anthropic Messages API
    /// </summary>
    public class ClaudeClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _maxTokens;
        private readonly double _temperature;

        private const string BaseUrl = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";

        /// <summary>
        /// 创建一个 Claude API 客户端
        /// </summary>
        public ClaudeClient(string apiKey, string model = "claude-sonnet-4-20250514",
                            int maxTokens = 4096, double temperature = 0.7)
        {
            _apiKey = apiKey;
            _model = model;
            _maxTokens = maxTokens;
            _temperature = temperature;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
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

            var response = await _httpClient.PostAsync(BaseUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // 提取响应文本
            var text = new StringBuilder();
            if (root.TryGetProperty("content", out var contentArr))
            {
                foreach (var block in contentArr.EnumerateArray())
                {
                    if (block.GetProperty("type").GetString() == "text")
                    {
                        text.Append(block.GetProperty("text").GetString());
                    }
                }
            }

            return text.ToString();
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

                var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
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

                string? finishReason = null;
                string? stopSequence = null;

                while (!reader.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    // SSE 事件解析
                    if (line.StartsWith("event: "))
                    {
                        var eventType = line[7..];
                        continue;
                    }

                    if (line.StartsWith("data: "))
                    {
                        var data = line[6..];

                        if (data == "[DONE]") break;

                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;

                        var type = root.GetProperty("type").GetString();

                        switch (type)
                        {
                            case "content_block_delta":
                                if (root.TryGetProperty("delta", out var delta) &&
                                    delta.TryGetProperty("text", out var text))
                                {
                                    onContent(text.GetString() ?? "");
                                }
                                break;

                            case "message_delta":
                                if (root.TryGetProperty("delta", out var msgDelta) &&
                                    msgDelta.TryGetProperty("stop_reason", out var reason))
                                {
                                    finishReason = reason.GetString();
                                }
                                if (root.TryGetProperty("delta", out var delta2) &&
                                    delta2.TryGetProperty("stop_sequence", out var seq))
                                {
                                    stopSequence = seq.GetString();
                                }
                                break;

                            case "message_stop":
                                // 流结束
                                break;
                        }
                    }
                }

                onFinish(finishReason ?? "end_turn");
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

                var response = await _httpClient.PostAsync(BaseUrl, content);
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

            // 对话历史需要交替 user/assistant 消息
            // Claude API 要求 messages 以 user 开头，user/assistant 交替
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

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                body["system"] = systemPrompt;
            }

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
