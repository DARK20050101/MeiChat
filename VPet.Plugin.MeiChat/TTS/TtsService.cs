using System.IO;
using System.Media;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPet.Plugin.MeiChat.TTS
{
    /// <summary>TTS 提供者</summary>
    public enum TtsProvider
    {
        /// <summary>Windows 内置语音（含 Edge 神经语音）</summary>
        WindowsBuiltIn,
        /// <summary>阿里云通义千问语音合成</summary>
        TongyiQianwen
    }

    /// <summary>
    /// 语音朗读服务 — 支持 Windows 内置 TTS 和通义千问 TTS
    /// </summary>
    public class TtsService : IDisposable
    {
        private SpeechSynthesizer? _synth;
        private readonly HttpClient _http = new();
        private bool _disposed;

        public TtsProvider Provider { get; set; } = TtsProvider.WindowsBuiltIn;
        public string VoiceName { get; set; } = "";
        public bool Enabled { get; set; } = false;
        public double Volume { get; set; } = 1.0;    // 0.0 ~ 1.0
        public double Rate { get; set; } = 0.0;       // -10 ~ 10

        // 通义千问配置
        public string TongyiApiKey { get; set; } = "";
        /// <summary>通义千问语音模型，如 "sambert-zhichu-v1"</summary>
        public string TongyiVoice { get; set; } = "sambert-zhichu-v1";

        public TtsService()
        {
            try { _synth = new SpeechSynthesizer(); }
            catch { }
        }

        /// <summary>异步朗读文本，不等待完成</summary>
        public void Speak(string text)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
            if (Provider == TtsProvider.WindowsBuiltIn)
                SpeakWindows(text);
            else if (Provider == TtsProvider.TongyiQianwen)
                _ = SpeakTongyiAsync(text);
        }

        /// <summary>异步朗读并等待完成</summary>
        public async Task SpeakAndWaitAsync(string text, CancellationToken ct = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
            if (Provider == TtsProvider.WindowsBuiltIn)
            {
                await SpeakWindowsAsync(text, ct);
            }
            else if (Provider == TtsProvider.TongyiQianwen)
            {
                await SpeakTongyiAsync(text);
            }
        }

        /// <summary>将长文本按句分割，逐句朗读（第一句更快）</summary>
        public async Task SpeakSentencesAsync(string text, CancellationToken ct = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;

            var sentences = SplitSentences(text);
            foreach (var sentence in sentences)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(sentence)) continue;

                await SpeakAndWaitAsync(sentence.Trim(), ct);
            }
        }

        /// <summary>停止朗读</summary>
        public void Stop()
        {
            try { _synth?.SpeakAsyncCancelAll(); } catch { }
        }

        // ===== Windows 内置语音 =====

        private void SpeakWindows(string text)
        {
            if (_synth == null) return;
            try
            {
                ApplyVoiceSettings();
                _synth.SpeakAsync(text);
            }
            catch { }
        }

        private async Task SpeakWindowsAsync(string text, CancellationToken ct)
        {
            if (_synth == null) return;
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                using var reg = ct.Register(() =>
                {
                    try { _synth.SpeakAsyncCancelAll(); } catch { }
                    tcs.TrySetCanceled();
                });

                ApplyVoiceSettings();

                EventHandler<SpeakCompletedEventArgs>? handler = null;
                handler = (s, e) =>
                {
                    _synth.SpeakCompleted -= handler;
                    tcs.TrySetResult(true);
                };
                _synth.SpeakCompleted += handler;
                _synth.SpeakAsync(text);

                await tcs.Task;
            }
            catch (OperationCanceledException) { Stop(); }
            catch { }
        }

        private void ApplyVoiceSettings()
        {
            if (_synth == null) return;
            try
            {
                _synth.Volume = (int)(Volume * 100);
                _synth.Rate = (int)Rate;
                if (!string.IsNullOrWhiteSpace(VoiceName))
                    _synth.SelectVoice(VoiceName);
            }
            catch { }
        }

        // ===== 通义千问语音 =====

        private async Task SpeakTongyiAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(TongyiApiKey)) return;
            try
            {
                var requestBody = new
                {
                    model = TongyiVoice,
                    input = new { text = text },
                    parameters = new
                    {
                        format = "wav",
                        sample_rate = 16000,
                        volume = Volume
                    }
                };
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://dashscope.aliyuncs.com/api/v1/services/tts/text-to-speech")
                {
                    Content = content
                };
                request.Headers.Add("Authorization", $"Bearer {TongyiApiKey}");

                var response = await _http.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var audioBytes = await response.Content.ReadAsByteArrayAsync();
                if (audioBytes.Length > 0)
                {
                    // 用 Windows Media Player 或 SoundPlayer 播放
                    PlayWavData(audioBytes);
                }
            }
            catch { /* TTS 失败静默处理 */ }
        }

        private static void PlayWavData(byte[] wavData)
        {
            try
            {
                using var ms = new MemoryStream(wavData);
                using var player = new System.Media.SoundPlayer(ms);
                player.PlaySync();
            }
            catch { }
        }

        // ===== 工具方法 =====

        /// <summary>按句子分割文本</summary>
        public static List<string> SplitSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new();

            var result = new List<string>();
            // 按句号、问号、感叹号、换行符分割（保留分隔符在前一句末尾）
            var parts = Regex.Split(text, @"(?<=[。！？\n.!?])");

            var buffer = new StringBuilder();
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // 如果句子太短，合并到下一句
                if (buffer.Length > 0 && trimmed.Length < 3 && !trimmed.Contains('。'))
                {
                    buffer.Append(trimmed);
                    continue;
                }

                if (buffer.Length > 0)
                {
                    buffer.Append(trimmed);
                    result.Add(buffer.ToString());
                    buffer.Clear();
                }
                else
                {
                    // 单句太长（超过200字）再切
                    if (trimmed.Length > 200)
                    {
                        var subParts = Regex.Split(trimmed, @"(?<=[，,；;])");
                        foreach (var sub in subParts)
                        {
                            if (!string.IsNullOrWhiteSpace(sub))
                                result.Add(sub.Trim());
                        }
                    }
                    else
                    {
                        result.Add(trimmed);
                    }
                }
            }

            if (buffer.Length > 0)
                result.Add(buffer.ToString());

            return result.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        /// <summary>获取已安装的 Windows 语音列表</summary>
        public List<string> GetInstalledVoices()
        {
            var voices = new List<string>();
            try
            {
                if (_synth != null)
                {
                    foreach (var v in _synth.GetInstalledVoices())
                    {
                        if (v.Enabled && v.VoiceInfo != null)
                            voices.Add(v.VoiceInfo.Name);
                    }
                }
            }
            catch { }
            return voices;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _synth?.Dispose();
            _http.Dispose();
        }
    }
}
