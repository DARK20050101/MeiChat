using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.TTS
{
    /// <summary>
    /// Edge TTS 提供者 — 通过 WebSocket 调用微软在线语音服务
    /// 零依赖，直连 speech.platform.bing.com
    /// </summary>
    public class EdgeTtsProvider : ITtsProvider
    {
        public string Name => "Edge TTS";
        public double Volume { get; set; } = 1.0;
        public double Rate { get; set; } = 0.0;
        public string VoiceName { get; set; } = "zh-CN-XiaoxiaoNeural";

        public string LastError { get; private set; } = "";

        // 连接超时
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
        private CancellationTokenSource? _currentCts;

        private static readonly (string Voice, string Label)[] EdgeVoices = new[]
        {
            ("zh-CN-XiaoxiaoNeural",   "晓晓（女·温柔）"),
            ("zh-CN-YunxiNeural",      "云希（男·阳光）"),
            ("zh-CN-YunyangNeural",    "云扬（男·专业）"),
            ("zh-CN-XiaochenNeural",   "晓辰（女·活泼）"),
            ("zh-CN-XiaohanNeural",    "晓涵（女·可爱）"),
            ("zh-CN-XiaomengNeural",   "晓梦（女·活力）"),
            ("zh-CN-XiaomoNeural",     "晓墨（女·文学）"),
            ("zh-CN-XiaoqiuNeural",    "晓秋（女·柔和）"),
            ("zh-CN-XiaoruiNeural",    "晓睿（女·知性）"),
            ("zh-CN-XiaoshuangNeural", "晓双（女·亲切）"),
            ("zh-CN-XiaoyanNeural",    "晓颜（女·自然）"),
            ("zh-CN-XiaoyouNeural",    "晓悠（女·元气）"),
            ("zh-HK-HiuMaanNeural",    "晓曼（粤语·女）"),
            ("zh-TW-HsiaoChenNeural",  "晓臻（台普·女）"),
            ("zh-TW-HsiaoYuNeural",    "晓雨（台普·女）"),
        };

        public static List<string> GetVoiceList()
            => EdgeVoices.Select(v => $"{v.Voice} ({v.Label})").ToList();

        public static string GetLabel(string voice)
            => EdgeVoices.FirstOrDefault(v => v.Voice == voice).Label ?? "中文语音";

        public async Task SpeakAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            LastError = "";
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                var audioData = await SynthesizeWithFallbackAsync(text, _currentCts.Token);
                if (audioData != null && audioData.Length > 0)
                    PlayAudio(audioData);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.WriteLine($"[EdgeTTS] 合成失败: {ex.Message}");
            }
        }

        public void Stop()
        {
            try { _currentCts?.Cancel(); } catch { }
        }

        /// <summary>主合成方法，带自动 fallback</summary>
        private async Task<byte[]?> SynthesizeWithFallbackAsync(string text, CancellationToken ct)
        {
            try
            {
                return await SynthesizeViaWebSocketAsync(text, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EdgeTTS] WebSocket失败: {ex.Message}");
                // WebSocket 失败时尝试 HTTP 方式
                try { return await SynthesizeViaHttpAsync(text, ct); }
                catch (Exception ex2)
                {
                    Debug.WriteLine($"[EdgeTTS] HTTP也失败: {ex2.Message}");
                    return null;
                }
            }
        }

        // ===== WebSocket 方式 =====

        private async Task<byte[]?> SynthesizeViaWebSocketAsync(string text, CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Origin", "https://azure.microsoft.com");
            ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var connId = Guid.NewGuid().ToString("N").ToUpper();
            var url = $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud" +
                      $"?TrustedClient=1&ConnectionId={connId}";

            using var connectCts = new CancellationTokenSource(ConnectTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);
            try { await ws.ConnectAsync(new Uri(url), linkedCts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("连接 Edge TTS 服务器超时");
            }

            // Step 1: 发送 synthesis.context 配置
            var context = JsonSerializer.Serialize(new
            {
                context = new
                {
                    synthesis = new
                    {
                        audio = new
                        {
                            metadataoptions = new
                            {
                                sentenceBoundaryEnabled = "false",
                                wordBoundaryEnabled = "false"
                            },
                            outputFormat = "audio-24khz-96kbitrate-mono-mp3"
                        },
                        request = new
                        {
                            connectionId = connId
                        }
                    }
                }
            });
            await SendWsText(ws, connId, "application/json; charset=utf-8", context, ct);

            // Step 2: 发送 SSML
            var rateStr = Rate switch { > 0 => $"+{Rate * 5:F0}%", < 0 => $"{Rate * 5:F0}%", _ => "+0%" };
            var volStr = $"{(int)(Volume * 100)}%";
            var ssml = $@"<speak version=""1.0"" xmlns=""http://www.w3.org/2001/10/synthesis"" xmlns:mstts=""https://www.w3.org/2001/mstts"" xml:lang=""zh-CN""><voice name=""{VoiceName}""><prosody rate=""{rateStr}"" volume=""{volStr}"">{EscapeXml(text)}</prosody></voice></speak>";

            await SendWsText(ws, connId, "application/ssml+xml", ssml, ct);

            // Step 3: 接收音频数据
            var audioData = new List<byte>();
            var buffer = new byte[65536];
            bool headerSkipped = false;

            while (ws.State == WebSocketState.Open)
            {
                ct.ThrowIfCancellationRequested();
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // 跳过前2字节（网络字节序长度前缀），取实际音频数据
                    int offset = headerSkipped ? 0 : 2;
                    headerSkipped = true;
                    int dataLen = result.Count - offset;
                    if (dataLen > 0)
                    {
                        audioData.AddRange(new ArraySegment<byte>(buffer, offset, dataLen));
                    }
                    if (result.EndOfMessage) break;
                }
            }

            return TrimAudioHeader(audioData.ToArray());
        }

        private static async Task SendWsText(ClientWebSocket ws, string requestId, string contentType, string body, CancellationToken ct)
        {
            var msg = $"X-RequestId:{requestId}\r\nContent-Type:{contentType}\r\n\r\n{body}";
            var bytes = Encoding.UTF8.GetBytes(msg);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        // ===== HTTP Fallback 方式 =====

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private async Task<byte[]?> SynthesizeViaHttpAsync(string text, CancellationToken ct)
        {
            // 尝试通过微软演示页面用的 REST API
            var ssml = $@"<speak version=""1.0"" xmlns=""http://www.w3.org/2001/10/synthesis"" xml:lang=""zh-CN""><voice name=""{VoiceName}"">{EscapeXml(text)}</voice></speak>";

            var body = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://eastus.api.cognitive.microsoft.com/sts/v1.0/issuetoken")
            { Content = body };

            var response = await _http.PostAsync(
                "https://synthesisspeech.microsoft.com/api/v1/speech/synthesize",
                body, ct);

            if (response.IsSuccessStatusCode)
            {
                var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
                return TrimAudioHeader(audioBytes);
            }

            throw new Exception($"HTTP合成失败: HTTP {(int)response.StatusCode}");
        }

        // ===== 音频处理 =====

        private static byte[]? TrimAudioHeader(byte[] data)
        {
            if (data == null || data.Length < 4) return null;

            // 跳过 RIFF WAV 头（如果有）
            if (data[0] == 0x52 && data[1] == 0x49) // "RI"
            {
                // 查找 "data" 标记开始的数据区
                for (int i = 0; i < data.Length - 4; i++)
                {
                    if (data[i] == 0x64 && data[i + 1] == 0x61 && data[i + 2] == 0x74 && data[i + 3] == 0x61) // "data"
                    {
                        int dataSize = (data[i + 4]) | (data[i + 5] << 8) | (data[i + 6] << 16) | (data[i + 7] << 24);
                        var result = new byte[dataSize];
                        Array.Copy(data, i + 8, result, 0, Math.Min(dataSize, data.Length - i - 8));
                        return result;
                    }
                }
                return data;
            }

            // 查找 MP3 帧头
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0xFF && (data[i + 1] & 0xE0) == 0xE0)
                    return data[i..];
            }

            // 没有特殊头，直接返回
            return data;
        }

        private static string EscapeXml(string text)
            => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                   .Replace("\"", "&quot;").Replace("'", "&apos;");

        // ===== 播放 =====

        private static void PlayAudio(byte[] audioData)
        {
            var ext = DetectFormat(audioData);
            var tmp = Path.GetTempFileName() + ext;
            try
            {
                File.WriteAllBytes(tmp, audioData);
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = tmp,
                    UseShellExecute = true,
                    Verb = "open"
                });
                if (proc != null)
                {
                    proc.WaitForExit(2000);
                    if (!proc.HasExited) try { proc.Kill(); } catch { }
                }
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        private static string DetectFormat(byte[] data)
        {
            if (data.Length < 4) return ".bin";
            if (data[0] == 0x52 && data[1] == 0x49) return ".wav";
            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return ".mp3";
            if (data[0] == 0x4F && data[1] == 0x67) return ".ogg";
            return ".mp3";
        }
    }
}
