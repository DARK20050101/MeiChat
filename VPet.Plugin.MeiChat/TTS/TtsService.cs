using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPet.Plugin.MeiChat.TTS
{
    /// <summary>TTS 提供者</summary>
    public enum TtsProvider
    {
        /// <summary>Windows 内置语音（SAPI / Edge 神经语音）</summary>
        WindowsBuiltIn,
        /// <summary>阿里云通义千问语音合成</summary>
        TongyiQianwen
    }

    /// <summary>
    /// 语音朗读服务 — 通过 Windows SAPI COM 实现，无需额外 DLL
    /// </summary>
    public class TtsService : IDisposable
    {
        private dynamic? _synth; // 延迟绑定的 SAPI.SpVoice
        private readonly HttpClient _http = new();
        private bool _disposed;
        private bool _sapiAvailable;

        public TtsProvider Provider { get; set; } = TtsProvider.WindowsBuiltIn;
        public string VoiceName { get; set; } = "";
        public bool Enabled { get; set; } = false;
        public double Volume { get; set; } = 1.0;    // 0.0 ~ 1.0
        public double Rate { get; set; } = 0.0;       // -10 ~ 10

        // 通义千问配置
        public string TongyiApiKey { get; set; } = "";
        public string TongyiVoice { get; set; } = "sambert-zhichu-v1";

        public TtsService()
        {
            try
            {
                // 通过 COM 创建 SAPI SpVoice（Windows 内置，无需额外 DLL）
                var speechType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (speechType != null)
                {
                    _synth = Activator.CreateInstance(speechType);
                    _sapiAvailable = true;
                }
            }
            catch
            {
                _sapiAvailable = false;
            }
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
                await Task.Run(() => SpeakWindows(text), ct);
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
            try { _synth?.SpeakAsyncCancelAll?.Invoke(); } catch { }
        }

        // ===== Windows SAPI 语音 =====

        private void SpeakWindows(string text)
        {
            if (_synth == null || !_sapiAvailable) return;
            try
            {
                // 设置音量 (0-100)
                _synth.Volume = (int)(Volume * 100);
                // 设置语速 (-10 到 10)
                _synth.Rate = (int)Rate;

                // 选择语音
                if (!string.IsNullOrWhiteSpace(VoiceName))
                {
                    try { _synth.Voice = _synth.GetVoices().Item(VoiceName); }
                    catch { }
                }

                // 异步朗读
                _synth.Speak(text, 1); // 1 = SVSFlagsAsync
            }
            catch { }
        }

        /// <summary>等待朗读完成</summary>
        private void WaitForSpeech()
        {
            if (_synth == null) return;
            try
            {
                while (_synth.Status.RunningState == 1) // 1 = SPRS_IS_SPEAKING
                {
                    Thread.Sleep(100);
                }
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
                    PlayWavData(audioBytes);
                }
            }
            catch { }
        }

        private static void PlayWavData(byte[] wavData)
        {
            try
            {
                using var ms = new MemoryStream(wavData);
                // 不依赖 System.Media，用 Windows API 播放
                var tempFile = Path.GetTempFileName() + ".wav";
                File.WriteAllBytes(tempFile, wavData);
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true,
                    Verb = "open"
                })?.WaitForExit();
                try { File.Delete(tempFile); } catch { }
            }
            catch { }
        }

        // ===== 工具方法 =====

        /// <summary>按句子分割文本</summary>
        public static List<string> SplitSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new();

            var result = new List<string>();
            var parts = Regex.Split(text, @"(?<=[。！？\n.!?])");

            var buffer = new StringBuilder();
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

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

        /// <summary>获取已安装的语音列表</summary>
        public List<string> GetInstalledVoices()
        {
            var voices = new List<string>();
            if (_synth == null || !_sapiAvailable) return voices;
            try
            {
                var allVoices = _synth.GetVoices();
                foreach (var v in allVoices)
                {
                    try
                    {
                        var name = v.GetAttribute("Name") ?? v.Id;
                        if (!string.IsNullOrEmpty(name))
                            voices.Add(name);
                    }
                    catch { }
                }
            }
            catch { }
            return voices;
        }

        /// <summary>SAPI 是否可用</summary>
        public bool IsAvailable => _sapiAvailable;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _http.Dispose();
        }
    }
}
