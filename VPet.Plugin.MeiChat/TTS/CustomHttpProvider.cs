using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.TTS
{
    /// <summary>
    /// 自定义 HTTP TTS 提供者 — 可接入任何第三方语音模型
    /// 适配 GPT-SoVITS / Fish Speech / ChatTTS / VITS 等
    ///
    /// 协议：
    ///   POST {endpoint}
    ///   Content-Type: application/json
    ///   Body: {"text": "要朗读的文本"}
    ///   Response: 音频二进制 (WAV/MP3)
    /// </summary>
    public class CustomHttpProvider : ITtsProvider
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>提供者显示名称</summary>
        public string Name { get; set; } = "自定义";

        /// <summary>HTTP 端点 URL</summary>
        public string Endpoint { get; set; } = "http://127.0.0.1:5000/tts";

        /// <summary>请求模板（{text} 会被替换为实际文本）</summary>
        public string RequestTemplate { get; set; } = @"{""text"": ""{text}""}";

        /// <summary>响应是纯音频流（true）还是JSON中包含音频数据（false）</summary>
        public bool ResponseIsRawAudio { get; set; } = true;

        /// <summary>JSON响应中的音频字段路径（如 "data.audio"），ResponseIsRawAudio=false时使用</summary>
        public string AudioField { get; set; } = "";

        string ITtsProvider.Name => Name;

        /// <summary>支持的语音列表（自定义模式一般只有一个）</summary>
        public List<string> VoiceList { get; set; } = new() { "default" };

        public async Task SpeakAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(Endpoint)) return;
            try
            {
                var body = RequestTemplate.Replace("{text}", EscapeJson(text));
                var content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync(Endpoint, content, ct);
                response.EnsureSuccessStatusCode();

                byte[] audioData;

                if (ResponseIsRawAudio)
                {
                    audioData = await response.Content.ReadAsByteArrayAsync(ct);
                }
                else
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // 按字段路径提取
                    var current = root;
                    foreach (var field in AudioField.Split('.', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (current.TryGetProperty(field, out var next))
                            current = next;
                        else
                            throw new Exception($"找不到字段: {AudioField}");
                    }

                    if (current.ValueKind == JsonValueKind.String)
                    {
                        audioData = Convert.FromBase64String(current.GetString()!);
                    }
                    else if (current.ValueKind == JsonValueKind.Array)
                    {
                        audioData = current.EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
                    }
                    else
                    {
                        audioData = current.GetBytesFromBase64();
                    }
                }

                if (audioData.Length > 0)
                    PlayAudio(audioData);
            }
            catch { }
        }

        public void Stop() { }

        private static string EscapeJson(string text)
        {
            return text.Replace("\\", "\\\\")
                       .Replace("\"", "\\\"")
                       .Replace("\n", "\\n")
                       .Replace("\r", "\\r")
                       .Replace("\t", "\\t");
        }

        private static void PlayAudio(byte[] data)
        {
            var ext = DetectAudioFormat(data);
            var tmp = Path.GetTempFileName() + ext;
            try
            {
                File.WriteAllBytes(tmp, data);
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

        private static string DetectAudioFormat(byte[] data)
        {
            if (data.Length < 4) return ".bin";
            // WAV: RIFF
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return ".wav";
            // MP3: 0xFF 0xFB
            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return ".mp3";
            // OGG: OggS
            if (data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53) return ".ogg";
            return ".bin";
        }
    }
}
