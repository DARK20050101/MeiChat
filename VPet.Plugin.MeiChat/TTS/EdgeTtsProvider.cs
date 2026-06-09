using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.TTS
{
    public class EdgeTtsProvider : ITtsProvider
    {
        public string Name => "Edge TTS";
        public double Volume { get; set; } = 1.0;
        public double Rate { get; set; } = 0.0;
        public string VoiceName { get; set; } = "zh-CN-XiaoxiaoNeural";
        public string LastError { get; private set; } = "";

        private CancellationTokenSource? _currentCts;

        private static readonly (string Voice, string Label)[] EdgeVoices =
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

        public async Task SpeakAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            LastError = "";
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // 方法1: 尝试 edge-tts CLI（如果有的话）
                var cliOk = await TryCliAsync(text, _currentCts.Token);
                if (cliOk) return;

                // 方法2: WebSocket 直连
                await TryWebSocketAsync(text, _currentCts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.WriteLine($"[EdgeTTS] 全部失败: {ex.Message}");
            }
        }

        public void Stop() { try { _currentCts?.Cancel(); } catch { } }

        // ===== CLI 方式 =====

        private async Task<bool> TryCliAsync(string text, CancellationToken ct)
        {
            try
            {
                // 检测 edge-tts 是否可用
                var (cmd, prefix) = FindCli();
                if (cmd == null) return false;

                var tmpFile = Path.GetTempFileName() + ".mp3";
                try
                {
                    var rateStr = Rate switch
                    {
                        > 0 => $"+{Rate * 5:F0}%",
                        < 0 => $"{Rate * 5:F0}%",
                        _ => "+0%"
                    };
                    var args = prefix == null
                        ? $"--voice \"{VoiceName}\" --text \"{EscapeArg(text)}\" --write-media \"{tmpFile}\" --rate \"{rateStr}\""
                        : $"{prefix} --voice \"{VoiceName}\" --text \"{EscapeArg(text)}\" --write-media \"{tmpFile}\" --rate \"{rateStr}\"";

                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    });
                    if (proc == null) return false;

                    using var _ = ct.Register(() => { try { proc.Kill(); } catch { } });
                    await proc.WaitForExitAsync(ct);

                    if (proc.ExitCode == 0 && File.Exists(tmpFile) && new FileInfo(tmpFile).Length > 0)
                    {
                        PlayAudioFile(tmpFile);
                        return true;
                    }
                    var err = await proc.StandardError.ReadToEndAsync();
                    Debug.WriteLine($"[EdgeTTS] CLI错误: {err}");
                    return false;
                }
                finally { try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { } }
            }
            catch { return false; }
        }

        private static (string? cmd, string? prefix) FindCli()
        {
            try
            {
                using var p1 = Process.Start(new ProcessStartInfo
                {
                    FileName = "edge-tts", Arguments = "--help",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                });
                if (p1 != null) { p1.WaitForExit(2000); if (p1.ExitCode == 0) return ("edge-tts", null); }
            }
            catch { }
            try
            {
                using var p2 = Process.Start(new ProcessStartInfo
                {
                    FileName = "python", Arguments = "-m edge_tts --help",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                });
                if (p2 != null) { p2.WaitForExit(2000); if (p2.ExitCode == 0) return ("python", "-m edge_tts"); }
            }
            catch { }
            return (null, null);
        }

        // ===== WebSocket 方式 =====

        private static readonly TimeSpan WsTimeout = TimeSpan.FromSeconds(10);

        private async Task TryWebSocketAsync(string text, CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Origin", "https://azure.microsoft.com");
            ws.Options.SetRequestHeader("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var connId = Guid.NewGuid().ToString("N").ToUpper();
            var url = $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud" +
                      $"?TrustedClient=1&ConnectionId={connId}";

            using var tcts = new CancellationTokenSource(WsTimeout);
            using var lcts = CancellationTokenSource.CreateLinkedTokenSource(ct, tcts.Token);
            await ws.ConnectAsync(new Uri(url), lcts.Token);

            // synthesis.context
            var ctx = JsonSerializer.Serialize(new
            {
                context = new
                {
                    synthesis = new
                    {
                        audio = new
                        {
                            metadataoptions = new { sentenceBoundaryEnabled = "false", wordBoundaryEnabled = "false" },
                            outputFormat = "audio-24khz-96kbitrate-mono-mp3"
                        }
                    }
                }
            });
            await SendWsText(ws, connId, "application/json; charset=utf-8", ctx, ct);

            // SSML
            var rateStr = Rate switch { > 0 => $"+{Rate * 5:F0}%", < 0 => $"{Rate * 5:F0}%", _ => "+0%" };
            var ssml = $@"<speak version=""1.0"" xmlns=""http://www.w3.org/2001/10/synthesis"" xmlns:mstts=""https://www.w3.org/2001/mstts"" xml:lang=""zh-CN""><voice name=""{VoiceName}""><prosody rate=""{rateStr}"" volume=""{(int)(Volume * 100)}%"">{EscapeXml(text)}</prosody></voice></speak>";
            await SendWsText(ws, connId, "application/ssml+xml", ssml, ct);

            // 接收音频
            var audioData = new List<byte>();
            var buf = new byte[65536];
            while (ws.State == WebSocketState.Open)
            {
                ct.ThrowIfCancellationRequested();
                var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                if (r.MessageType == WebSocketMessageType.Binary)
                {
                    var raw = new byte[r.Count];
                    Array.Copy(buf, raw, r.Count);
                    if (raw.Length >= 2)
                    {
                        int hLen = (raw[0] << 8) | raw[1];
                        int start = 2 + Math.Min(hLen, raw.Length - 2);
                        int aLen = raw.Length - start;
                        if (aLen > 0) audioData.AddRange(new ArraySegment<byte>(raw, start, aLen));
                    }
                    if (r.EndOfMessage) break;
                }
            }

            var arr = audioData.ToArray();
            if (arr.Length == 0) throw new Exception("未收到音频数据");
            // 找 MP3 帧头
            for (int i = 0; i < arr.Length - 1; i++)
                if (arr[i] == 0xFF && (arr[i + 1] & 0xE0) == 0xE0) { PlayMp3(arr[i..]); return; }
            PlayMp3(arr);
        }

        private static async Task SendWsText(ClientWebSocket ws, string rid, string ct, string body, CancellationToken c)
        {
            var msg = $"X-RequestId:{rid}\r\nContent-Type:{ct}\r\n\r\n{body}";
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, c);
        }

        // ===== 通用 =====

        private static void PlayAudioFile(string path)
        {
            using var p = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "open" });
            if (p != null) { p.WaitForExit(3000); if (!p.HasExited) try { p.Kill(); } catch { } }
        }

        private static void PlayMp3(byte[] data)
        {
            var tmp = Path.GetTempFileName() + ".mp3";
            try { File.WriteAllBytes(tmp, data); PlayAudioFile(tmp); }
            finally { try { File.Delete(tmp); } catch { } }
        }

        private static string EscapeXml(string t) => t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        private static string EscapeArg(string t) => t.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
