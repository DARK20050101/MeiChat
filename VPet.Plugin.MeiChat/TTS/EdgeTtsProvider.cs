using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPet.Plugin.MeiChat.TTS
{
    /// <summary>
    /// Edge TTS 提供者 — 通过 WebSocket 调用 Microsoft Edge 在线语音
    /// 无需任何外部依赖，直接使用 Azure Speech 服务
    /// </summary>
    public class EdgeTtsProvider : ITtsProvider
    {
        public string Name => "Edge TTS";

        public double Volume { get; set; } = 1.0;
        public double Rate { get; set; } = 0.0;
        public string VoiceName { get; set; } = "zh-CN-XiaoxiaoNeural";

        private static readonly string[] EdgeVoices = new[]
        {
            "zh-CN-XiaoxiaoNeural",   // 晓晓（女，温柔）
            "zh-CN-YunxiNeural",      // 云希（男，阳光）
            "zh-CN-YunyangNeural",    // 云扬（男，专业）
            "zh-CN-XiaochenNeural",   // 晓辰（女，活泼）
            "zh-CN-XiaohanNeural",    // 晓涵（女，可爱）
            "zh-CN-XiaomengNeural",   // 晓梦（女，活力）
            "zh-CN-XiaomoNeural",     // 晓墨（女，文学）
            "zh-CN-XiaoqiuNeural",    // 晓秋（女，柔和）
            "zh-CN-XiaoruiNeural",    // 晓睿（女，知性）
            "zh-CN-XiaoshuangNeural", // 晓双（女，亲切）
            "zh-CN-XiaoyanNeural",    // 晓颜（女，自然）
            "zh-CN-XiaoyouNeural",    // 晓悠（女，元气）
            "zh-HK-HiuMaanNeural",    // 晓曼（粤语）
            "zh-TW-HsiaoChenNeural",  // 晓臻（台普）
            "zh-TW-HsiaoYuNeural",    // 晓雨（台普）
        };

        private static readonly Dictionary<string, string> VoiceLabels = new()
        {
            ["zh-CN-XiaoxiaoNeural"] = "晓晓（女·温柔）",
            ["zh-CN-YunxiNeural"] = "云希（男·阳光）",
            ["zh-CN-YunyangNeural"] = "云扬（男·专业）",
            ["zh-CN-XiaochenNeural"] = "晓辰（女·活泼）",
            ["zh-CN-XiaohanNeural"] = "晓涵（女·可爱）",
            ["zh-CN-XiaomengNeural"] = "晓梦（女·活力）",
            ["zh-CN-XiaomoNeural"] = "晓墨（女·文学）",
            ["zh-CN-XiaoqiuNeural"] = "晓秋（女·柔和）",
            ["zh-CN-XiaoruiNeural"] = "晓睿（女·知性）",
            ["zh-CN-XiaoshuangNeural"] = "晓双（女·亲切）",
            ["zh-CN-XiaoyanNeural"] = "晓颜（女·自然）",
            ["zh-CN-XiaoyouNeural"] = "晓悠（女·元气）",
        };

        public static List<string> GetVoiceList()
        {
            return EdgeVoices.Select(v => $"{v} ({GetLabel(v)})").ToList();
        }

        public static string GetLabel(string voice)
        {
            return VoiceLabels.TryGetValue(voice, out var l) ? l : "中文语音";
        }

        public async Task SpeakAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var audioData = await SynthesizeAsync(text, ct);
                if (audioData != null && audioData.Length > 0)
                    PlayAudio(audioData);
            }
            catch { }
        }

        public void Stop() { }

        private async Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Origin", "chrome://edge");

            var url = $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud" +
                      $"?TrustedClient=1&ConnectionId={Guid.NewGuid():N}";
            await ws.ConnectAsync(new Uri(url), ct);

            // 1. 发送 synthesis context
            var context = JsonSerializer.Serialize(new
            {
                context = new
                {
                    synthesis = new
                    {
                        audio = new
                        {
                            metadataoptions = new { },
                            outputformat = "audio-24khz-96kbitrate-mono-mp3"
                        }
                    }
                }
            });
            var contextMsg = $"X-RequestId:{GuidNew()}\r\nContent-Type:application/json; charset=utf-8\r\n\r\n{context}";
            await SendWsMessage(ws, contextMsg, ct);

            // 2. 发送 SSML
            var rateStr = Rate switch
            {
                > 0 => $"+{Rate * 5:F0}%",
                < 0 => $"{Rate * 5:F0}%",
                _ => "0%"
            };
            var volStr = $"{(int)(Volume * 100)}%";

            var ssml = $@"<speak version=""1.0"" xmlns=""http://www.w3.org/2001/10/synthesis"" xmlns:mstts=""https://www.w3.org/2001/mstts"" xml:lang=""zh-CN""><voice name=""{VoiceName}""><prosody rate=""{rateStr}"" volume=""{volStr}"">{EscapeXml(text)}</prosody></voice></speak>";

            // 分块发送长文本（Edge TTS 有限制）
            const int maxLen = 3000;
            var audioData = new List<byte>();

            if (text.Length > maxLen)
            {
                // 长文本分段合成
                var chunks = SplitText(text, maxLen);
                foreach (var chunk in chunks)
                {
                    if (ct.IsCancellationRequested) break;
                    var chunkSsml = $@"<speak version=""1.0"" xmlns=""http://www.w3.org/2001/10/synthesis"" xmlns:mstts=""https://www.w3.org/2001/mstts"" xml:lang=""zh-CN""><voice name=""{VoiceName}""><prosody rate=""{rateStr}"" volume=""{volStr}"">{EscapeXml(chunk)}</prosody></voice></speak>";
                    var chunkData = await SynthesizeOne(ws, chunkSsml, ct);
                    if (chunkData != null) audioData.AddRange(chunkData);
                }
            }
            else
            {
                var data = await SynthesizeOne(ws, ssml, ct);
                if (data != null) audioData.AddRange(data);
            }

            return audioData.ToArray();
        }

        private async Task<byte[]?> SynthesizeOne(ClientWebSocket ws, string ssml, CancellationToken ct)
        {
            var turnMsg = $"X-RequestId:{GuidNew()}\r\nContent-Type:application/ssml+xml\r\n\r\n{ssml}";
            await SendWsMessage(ws, turnMsg, ct);

            // 接收音频
            var audioData = new List<byte>();
            var audioStarted = false;
            var buffer = new byte[8192];

            while (ws.State == WebSocketState.Open)
            {
                ct.ThrowIfCancellationRequested();
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    // 音频数据在 Binary 消息中，Text 消息是元数据
                    if (text.Contains("Path:audio"))
                        audioStarted = true;
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    if (audioStarted)
                    {
                        // 跳过前两个字节（头）
                        var data = new byte[result.Count];
                        Array.Copy(buffer, data, result.Count);
                        audioData.AddRange(data);
                    }
                    if (result.EndOfMessage) break;
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }

            // 清理音频头部的元数据
            return TrimAudioData(audioData.ToArray());
        }

        private static byte[]? TrimAudioData(byte[] data)
        {
            if (data.Length < 10) return null;
            // 查找 MP3 帧头 0xFF 0xFB
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0xFF && (data[i + 1] & 0xE0) == 0xE0)
                    return data[i..];
            }
            return data;
        }

        private static async Task SendWsMessage(ClientWebSocket ws, string message, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private static string GuidNew() => Guid.NewGuid().ToString("N");

        private static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;")
                       .Replace("<", "&lt;")
                       .Replace(">", "&gt;")
                       .Replace("\"", "&quot;")
                       .Replace("'", "&apos;");
        }

        private static List<string> SplitText(string text, int maxLen)
        {
            var result = new List<string>();
            for (int i = 0; i < text.Length; i += maxLen)
                result.Add(text.Substring(i, Math.Min(maxLen, text.Length - i)));
            return result;
        }

        private static void PlayAudio(byte[] audioData)
        {
            var tmp = Path.GetTempFileName() + ".mp3";
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
                    // 等待播放启动（不等待完成，让系统播放器继续播放）
                    proc.WaitForExit(2000);
                }
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}
