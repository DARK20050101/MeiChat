using System.Diagnostics;
using System.IO;
using System.Text;

namespace VPet.Plugin.MeiChat.TTS
{
    /// <summary>
    /// Edge TTS 提供者 — 调用 edge-tts 命令行工具
    /// 需安装: pip install edge-tts
    /// 如未安装，会自动引导用户安装
    /// </summary>
    public class EdgeTtsProvider : ITtsProvider
    {
        public string Name => "Edge TTS";
        public double Volume { get; set; } = 1.0;
        public double Rate { get; set; } = 0.0;
        public string VoiceName { get; set; } = "zh-CN-XiaoxiaoNeural";

        /// <summary>edge-tts 命令名（Windows 上 pip 安装后可能在 PATH 中找不到，用 python -m）</summary>
        private static readonly string[] EdgeTtsCommands = { "edge-tts", "edge-tts.exe", "python", "python3" };

        /// <summary>edge-tts 是否已安装</summary>
        public static bool IsCliAvailable
        {
            get
            {
                try
                {
                    // 尝试多种方式调用 edge-tts
                    foreach (var cmd in EdgeTtsCommands)
                    {
                        try
                        {
                            var args = cmd == "python" || cmd == "python3"
                                ? "-m edge_tts --help"
                                : "--help";
                            using var proc = Process.Start(new ProcessStartInfo
                            {
                                FileName = cmd,
                                Arguments = args,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            });
                            if (proc == null) continue;
                            proc.WaitForExit(3000);
                            if (proc.ExitCode == 0) return true;
                        }
                        catch { }
                    }
                    return false;
                }
                catch { return false; }
            }
        }

        /// <summary>获取 edge-tts 命令和参数格式</summary>
        private static (string cmd, string prefix) GetCommand()
        {
            // 优先用 edge-tts，不行就用 python -m
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "edge-tts",
                    Arguments = "--help",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (proc != null) { proc.WaitForExit(2000); if (proc.ExitCode == 0) return ("edge-tts", ""); }
            }
            catch { }
            return ("python", "-m edge_tts");
        }

        /// <summary>获取安装指引文本</summary>
        public static string InstallGuide => "需要安装 edge-tts：pip install edge-tts";

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

            // 检查 edge-tts 是否安装
            if (!IsCliAvailable)
            {
                Debug.WriteLine("[EdgeTTS] edge-tts 未安装，跳过朗读");
                return;
            }

            var tmpFile = Path.GetTempFileName() + ".mp3";
            try
            {
                var rateStr = Rate switch
                {
                    > 0 => $"+{Rate * 5:F0}%",
                    < 0 => $"{Rate * 5:F0}%",
                    _ => "+0%"
                };

                var (cmd, prefix) = GetCommand();
                var args = string.IsNullOrEmpty(prefix)
                    ? $"--voice \"{VoiceName}\" --text \"{EscapeArg(text)}\" --write-media \"{tmpFile}\" --rate \"{rateStr}\" --volume \"{(int)(Volume * 100)}\""
                    : $"{prefix} --voice \"{VoiceName}\" --text \"{EscapeArg(text)}\" --write-media \"{tmpFile}\" --rate \"{rateStr}\" --volume \"{(int)(Volume * 100)}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null) return;

                // 异步等待完成（支持取消）
                using var reg = ct.Register(() => { try { proc.Kill(); } catch { } });
                await proc.WaitForExitAsync(ct);

                if (ct.IsCancellationRequested) return;

                if (proc.ExitCode != 0)
                {
                    var err = await proc.StandardError.ReadToEndAsync();
                    Debug.WriteLine($"[EdgeTTS] 错误: {err}");
                    return;
                }

                // 播放生成的音频文件
                if (File.Exists(tmpFile) && new FileInfo(tmpFile).Length > 0)
                {
                    PlayAudioFile(tmpFile);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EdgeTTS] 异常: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
            }
        }

        public void Stop() { /* edge-tts CLI 不支持中断单次朗读 */ }

        private static void PlayAudioFile(string filePath)
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
                Verb = "open"
            });
            // 等待播放器启动
            if (proc != null)
            {
                proc.WaitForExit(3000);
                if (!proc.HasExited) try { proc.Kill(); } catch { }
            }
        }

        private static string EscapeArg(string text)
        {
            // 转义命令行参数中的特殊字符
            return text.Replace("\"", "\\\"")
                       .Replace("\n", " ")
                       .Replace("\r", " ")
                       .Trim();
        }
    }
}
