using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPet.Plugin.MeiChat.TTS
{
    /// <summary>TTS 提供者类型</summary>
    public enum TtsProviderType
    {
        WindowsSAPI,
        TongyiQianwen
    }

    /// <summary>
    /// TTS 提供者接口 — 第三方语音模型实现此接口即可接入
    /// </summary>
    public interface ITtsProvider
    {
        string Name { get; }
        Task SpeakAsync(string text, CancellationToken ct);
        void Stop();
    }

    /// <summary>
    /// Windows SAPI 语音提供者（通过 COM SpVoice，零依赖）
    /// </summary>
    public class WindowsSapiProvider : ITtsProvider
    {
        private dynamic? _synth;
        private bool _available;
        public string VoiceName { get; set; } = "";
        public double Volume { get; set; } = 1.0;
        public double Rate { get; set; } = 0.0;
        public string Name => "Windows SAPI";

        public WindowsSapiProvider()
        {
            try
            {
                var t = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (t != null) { _synth = Activator.CreateInstance(t); _available = true; }
            }
            catch { }
        }

        public bool Available => _available;

        public async Task SpeakAsync(string text, CancellationToken ct)
        {
            if (_synth == null || !_available || string.IsNullOrWhiteSpace(text)) return;
            await Task.Run(() =>
            {
                try
                {
                    _synth.Volume = (int)(Volume * 100);
                    _synth.Rate = (int)Rate;
                    if (!string.IsNullOrWhiteSpace(VoiceName))
                    {
                        try { _synth.Voice = _synth.GetVoices().Item(VoiceName); } catch { }
                    }
                    _synth.Speak(text, 0); // 0 = 同步朗读（等待完成）
                }
                catch { }
            }, ct);
        }

        public void Stop()
        {
            try { _synth?.SpeakAsyncCancelAll?.Invoke(); } catch { }
        }

        /// <summary>扫描系统已安装的语音列表</summary>
        public List<string> ScanVoices()
        {
            var result = new List<string>();
            if (_synth == null) return result;
            try
            {
                dynamic voices = _synth.GetVoices();
                int count = voices.Count;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic v = voices.Item(i);
                        string? name = v.GetAttribute("Name");
                        if (!string.IsNullOrEmpty(name)) result.Add(name);
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        /// <summary>静态方法：扫描语音（无需实例）</summary>
        public static List<string> ScanAllVoices()
        {
            try
            {
                var t = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (t == null) return new();
                dynamic synth = Activator.CreateInstance(t);
                dynamic voices = synth.GetVoices();
                int count = voices.Count;
                var result = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic v = voices.Item(i);
                        string? name = v.GetAttribute("Name");
                        if (!string.IsNullOrEmpty(name)) result.Add(name);
                    }
                    catch { }
                }
                return result;
            }
            catch { return new(); }
        }
    }

    /// <summary>
    /// 通义千问 TTS 提供者
    /// </summary>
    public class TongyiTtsProvider : ITtsProvider
    {
        private readonly HttpClient _http = new();
        private CancellationTokenSource? _cts;
        public string ApiKey { get; set; } = "";
        public string VoiceModel { get; set; } = "sambert-zhichu-v1";
        public double Volume { get; set; } = 1.0;
        public string Name => "通义千问";

        public async Task SpeakAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(text)) return;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                var body = new
                {
                    model = VoiceModel,
                    input = new { text },
                    parameters = new { format = "wav", sample_rate = 16000, volume = Volume }
                };
                var json = JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var req = new HttpRequestMessage(HttpMethod.Post,
                    "https://dashscope.aliyuncs.com/api/v1/services/tts/text-to-speech")
                { Content = content };
                req.Headers.Add("Authorization", $"Bearer {ApiKey}");

                var resp = await _http.SendAsync(req, _cts.Token);
                resp.EnsureSuccessStatusCode();
                var audio = await resp.Content.ReadAsByteArrayAsync();
                if (audio.Length > 0) PlayWavData(audio);
            }
            catch { }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
        }

        private static void PlayWavData(byte[] data)
        {
            var tmp = Path.GetTempFileName() + ".wav";
            try
            {
                File.WriteAllBytes(tmp, data);
                Process.Start(new ProcessStartInfo { FileName = tmp, UseShellExecute = true, Verb = "open" })?.WaitForExit();
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }

    // ===== 主服务 =====

    /// <summary>
    /// 语音朗读主服务
    /// </summary>
    public class TtsService : IDisposable
    {
        private readonly WindowsSapiProvider _sapi = new();
        private readonly TongyiTtsProvider _tongyi = new();
        private ITtsProvider? _active;
        private bool _disposed;

        public TtsProviderType Provider { get; set; } = TtsProviderType.WindowsSAPI;
        public bool Enabled { get; set; } = false;

        // Windows SAPI 配置
        public string SapiVoice { get; set; } = "";

        // 通义千问配置
        public string TongyiApiKey { get; set; } = "";
        public string TongyiVoiceModel { get; set; } = "sambert-zhichu-v1";

        // 通用配置
        public double Volume { get; set; } = 1.0;
        public double Rate { get; set; } = 0.0;

        public TtsService()
        {
            UpdateActiveProvider();
        }

        /// <summary>获取当前生效的提供者</summary>
        public ITtsProvider? ActiveProvider => _active;

        /// <summary>SAPI 是否可用</summary>
        public bool SapiAvailable => _sapi.Available;

        /// <summary>扫描系统语音</summary>
        public List<string> ScanSapiVoices() => WindowsSapiProvider.ScanAllVoices();

        private void UpdateActiveProvider()
        {
            _sapi.Volume = Volume;
            _sapi.Rate = Rate;
            _sapi.VoiceName = SapiVoice;
            _tongyi.ApiKey = TongyiApiKey;
            _tongyi.VoiceModel = TongyiVoiceModel;
            _tongyi.Volume = Volume;
            _active = Provider switch
            {
                TtsProviderType.TongyiQianwen => _tongyi,
                _ => _sapi
            };
        }

        /// <summary>朗读指定文本（不等待完成）</summary>
        public void Speak(string text)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
            UpdateActiveProvider();
            _ = (_active?.SpeakAsync(text, CancellationToken.None));
        }

        /// <summary>朗读并等待完成</summary>
        public async Task SpeakAndWaitAsync(string text, CancellationToken ct = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
            UpdateActiveProvider();
            if (_active != null) await _active.SpeakAsync(text, ct);
        }

        /// <summary>按句分割，逐句朗读（第一句立即读）</summary>
        public async Task SpeakSentencesAsync(string text, CancellationToken ct = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
            UpdateActiveProvider();
            if (_active == null) return;

            var sentences = SplitSentences(text);
            foreach (var sentence in sentences)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(sentence)) continue;
                await _active.SpeakAsync(sentence.Trim(), ct);
            }
        }

        /// <summary>停止朗读</summary>
        public void Stop()
        {
            _sapi.Stop();
            _tongyi.Stop();
        }

        /// <summary>分割句子</summary>
        public static List<string> SplitSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new();
            var parts = Regex.Split(text, @"(?<=[。！？\n.!?])");
            var result = new List<string>();
            var buf = new StringBuilder();
            foreach (var part in parts)
            {
                var t = part.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (buf.Length > 0 && t.Length < 3 && !t.Contains('。')) { buf.Append(t); continue; }
                if (buf.Length > 0) { buf.Append(t); result.Add(buf.ToString()); buf.Clear(); }
                else if (t.Length > 200)
                {
                    foreach (var sub in Regex.Split(t, @"(?<=[，,；;])"))
                        if (!string.IsNullOrWhiteSpace(sub)) result.Add(sub.Trim());
                }
                else result.Add(t);
            }
            if (buf.Length > 0) result.Add(buf.ToString());
            return result.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
